using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Web.Services;

/// <summary>
/// Drains the pending queue on a timer, so a receipt that could not be delivered
/// when it was captured still gets through without anyone opening the app again.
/// </summary>
public sealed class SyncBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ReceiptSyncService _sync;
    private readonly ILogger<SyncBackgroundService> _logger;

    public SyncBackgroundService(ReceiptSyncService sync, ILogger<SyncBackgroundService> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var summary = await _sync.SyncPendingAsync(stoppingToken).ConfigureAwait(false);

                if (summary.Processed > 0)
                {
                    _logger.LogInformation(
                        "Sync pass: {Sent} sent, {Retry} to retry, {Failed} failed.",
                        summary.Sent,
                        summary.RetryScheduled,
                        summary.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failing pass must never end the loop; the next tick tries again.
                _logger.LogError(ex, "Sync pass failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
