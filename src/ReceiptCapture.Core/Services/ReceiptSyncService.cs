using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Services;

/// <summary>
/// Drains the queue of confirmed receipts into the accounting system.
/// </summary>
/// <remarks>
/// A phone loses connectivity constantly — a parking garage, a plane, a bad cell.
/// So submission is never tied to the capture flow: the user confirms a receipt, it
/// becomes <see cref="ReceiptStatus.Pending"/>, and this service moves it along
/// whenever it can, backing off between attempts and giving up only on errors that
/// retrying cannot fix.
/// </remarks>
public sealed class ReceiptSyncService
{
    private readonly IReceiptRepository _repository;
    private readonly IAccountingConnector _connector;
    private readonly IReceiptImageStore _imageStore;
    private readonly TimeProvider _timeProvider;
    private readonly ReceiptSyncOptions _options;

    /// <summary>Guards against the timer tick and a manual pull-to-refresh overlapping.</summary>
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public ReceiptSyncService(
        IReceiptRepository repository,
        IAccountingConnector connector,
        IReceiptImageStore imageStore,
        TimeProvider? timeProvider = null,
        ReceiptSyncOptions? options = null)
    {
        _repository = repository;
        _connector = connector;
        _imageStore = imageStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new ReceiptSyncOptions();
    }

    /// <summary>
    /// Submits every receipt whose backoff has elapsed. Safe to call from a timer, on
    /// app foreground, or from pull-to-refresh; overlapping calls return immediately.
    /// </summary>
    public async Task<SyncSummary> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return SyncSummary.AlreadyRunning;
        }

        try
        {
            var now = _timeProvider.GetUtcNow();
            var due = await _repository.GetDueForSyncAsync(now, cancellationToken).ConfigureAwait(false);

            var sent = 0;
            var retryScheduled = 0;
            var failed = 0;

            foreach (var receipt in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (await SubmitAsync(receipt, cancellationToken).ConfigureAwait(false))
                {
                    case SubmissionOutcome.Sent:
                        sent++;
                        break;
                    case SubmissionOutcome.RetryScheduled:
                        retryScheduled++;
                        break;
                    default:
                        failed++;
                        break;
                }
            }

            return new SyncSummary(sent, retryScheduled, failed, WasSkipped: false);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<SubmissionOutcome> SubmitAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        Stream? image = null;
        try
        {
            if (receipt.ImagePath is { Length: > 0 } path)
            {
                image = await _imageStore.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            }

            var result = await _connector
                .SubmitAsync(receipt, image, Path.GetFileName(receipt.ImagePath), cancellationToken)
                .ConfigureAwait(false);

            return await ApplyResultAsync(receipt, result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A connector that throws rather than returning a result is still a
            // failure of this one receipt; it must not stop the rest of the queue.
            return await ApplyResultAsync(
                receipt,
                ConnectorResult.Transient(ex.Message),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (image is not null)
            {
                await image.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<SubmissionOutcome> ApplyResultAsync(
        Receipt receipt,
        ConnectorResult result,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        receipt.AttemptCount++;

        if (result.Success)
        {
            receipt.Status = ReceiptStatus.Sent;
            receipt.RemoteId = result.RemoteId;
            receipt.SubmittedAtUtc = now;
            receipt.NextAttemptUtc = null;
            receipt.LastError = null;

            await _repository.SaveAsync(receipt, cancellationToken).ConfigureAwait(false);
            return SubmissionOutcome.Sent;
        }

        receipt.LastError = result.ErrorMessage;

        if (result.IsTransient && receipt.AttemptCount < _options.MaxAttempts)
        {
            receipt.Status = ReceiptStatus.Pending;
            receipt.NextAttemptUtc = now + BackoffFor(receipt.AttemptCount);

            await _repository.SaveAsync(receipt, cancellationToken).ConfigureAwait(false);
            return SubmissionOutcome.RetryScheduled;
        }

        // Either the error will never resolve itself, or we have tried long enough
        // that the user deserves to know rather than have it retry silently forever.
        receipt.Status = ReceiptStatus.Failed;
        receipt.NextAttemptUtc = null;

        await _repository.SaveAsync(receipt, cancellationToken).ConfigureAwait(false);
        return SubmissionOutcome.Failed;
    }

    /// <summary>Exponential backoff from <see cref="ReceiptSyncOptions.InitialBackoff"/>, capped.</summary>
    internal TimeSpan BackoffFor(int attemptCount)
    {
        var exponent = Math.Max(0, attemptCount - 1);

        // Cap the exponent before shifting so a long-failing receipt cannot overflow.
        var multiplier = exponent >= 20 ? 1 << 20 : 1 << exponent;
        var delay = _options.InitialBackoff * multiplier;

        return delay > _options.MaxBackoff ? _options.MaxBackoff : delay;
    }

    private enum SubmissionOutcome
    {
        Sent,
        RetryScheduled,
        Failed,
    }
}

/// <summary>Retry policy for <see cref="ReceiptSyncService"/>.</summary>
public sealed class ReceiptSyncOptions
{
    /// <summary>Delay before the first retry; doubles on each subsequent attempt.</summary>
    public TimeSpan InitialBackoff { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Ceiling for the backoff, so a long outage still retries hourly.</summary>
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Attempts before a transient failure is treated as permanent.</summary>
    public int MaxAttempts { get; init; } = 8;
}

/// <summary>What one sync pass accomplished.</summary>
/// <param name="Sent">Receipts accepted by the accounting system.</param>
/// <param name="RetryScheduled">Receipts that failed transiently and will be retried.</param>
/// <param name="Failed">Receipts moved to <see cref="ReceiptStatus.Failed"/>.</param>
/// <param name="WasSkipped">True when another sync pass was already running.</param>
public readonly record struct SyncSummary(int Sent, int RetryScheduled, int Failed, bool WasSkipped)
{
    public static SyncSummary AlreadyRunning { get; } = new(0, 0, 0, WasSkipped: true);

    public int Processed => Sent + RetryScheduled + Failed;
}
