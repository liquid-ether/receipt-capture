using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Parsing;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Web.Services;

/// <summary>
/// The capture pipeline: store the photo, read it, parse it, hand back a draft.
/// </summary>
/// <remarks>
/// The photo is written to the image store before anything else happens. OCR can
/// fail and parsing can find nothing; neither should cost the user the picture of a
/// receipt they have very likely already thrown away.
/// </remarks>
public sealed class ReceiptCaptureWorkflow
{
    private readonly IOcrService _ocr;
    private readonly IReceiptImageStore _images;
    private readonly IReceiptRepository _repository;
    private readonly ReceiptSyncService _sync;
    private readonly ILogger<ReceiptCaptureWorkflow> _logger;

    public ReceiptCaptureWorkflow(
        IOcrService ocr,
        IReceiptImageStore images,
        IReceiptRepository repository,
        ReceiptSyncService sync,
        ILogger<ReceiptCaptureWorkflow> logger)
    {
        _ocr = ocr;
        _images = images;
        _repository = repository;
        _sync = sync;
        _logger = logger;
    }

    /// <summary>Saves the uploaded photo, reads it, and returns a draft to review.</summary>
    public async Task<CaptureOutcome> CaptureAsync(
        Stream photo,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        var receiptId = Guid.NewGuid();
        var imagePath = await _images
            .SaveAsync(receiptId, photo, fileExtension, cancellationToken)
            .ConfigureAwait(false);

        OcrResult text;
        try
        {
            text = await _ocr.RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Text recognition failed for receipt {ReceiptId}.", receiptId);

            // The photo is safe; let the user fill the form in by hand.
            return CaptureOutcome.WithoutText(BlankDraft(receiptId, imagePath), ex.Message);
        }

        if (text.Lines.Count == 0)
        {
            return CaptureOutcome.WithoutText(BlankDraft(receiptId, imagePath), errorMessage: null);
        }

        var parsed = ReceiptTextParser.Parse(text.Lines);
        var draft = parsed.ToDraft(imagePath);
        draft.Id = receiptId;

        // Persisted as a draft straight away: a phone that loses signal, locks, or
        // has its tab evicted mid-review must not lose the capture.
        await _repository.SaveAsync(draft, cancellationToken).ConfigureAwait(false);

        return new CaptureOutcome(draft, parsed.Warnings, parsed.Confidence, ErrorMessage: null);
    }

    /// <summary>
    /// Accepts the user's reviewed values, queues the receipt and tries to send it
    /// immediately. A send failure is not surfaced: the receipt is safely queued and
    /// the sync service keeps trying.
    /// </summary>
    public async Task ConfirmAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        receipt.Status = ReceiptStatus.Pending;
        receipt.AttemptCount = 0;
        receipt.NextAttemptUtc = null;
        receipt.LastError = null;

        await _repository.SaveAsync(receipt, cancellationToken).ConfigureAwait(false);
        await _sync.SyncPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Throws a receipt and its photo away.</summary>
    public async Task DiscardAsync(Guid receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _repository.GetAsync(receiptId, cancellationToken).ConfigureAwait(false);

        if (receipt?.ImagePath is { Length: > 0 } path)
        {
            await _images.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
        }

        await _repository.DeleteAsync(receiptId, cancellationToken).ConfigureAwait(false);
    }

    private static Receipt BlankDraft(Guid id, string imagePath) => new()
    {
        Id = id,
        ImagePath = imagePath,
        Status = ReceiptStatus.Draft,
    };
}

/// <summary>Result of capturing one receipt.</summary>
/// <param name="Draft">Pre-filled receipt for the review form.</param>
/// <param name="Warnings">Resource keys describing what needs the user's attention.</param>
/// <param name="Confidence">How much of the slip was read cleanly.</param>
/// <param name="ErrorMessage">Set when text recognition itself failed.</param>
public sealed record CaptureOutcome(
    Receipt Draft,
    IReadOnlyList<string> Warnings,
    ParseConfidence Confidence,
    string? ErrorMessage)
{
    public static CaptureOutcome WithoutText(Receipt draft, string? errorMessage) =>
        new(draft, [ParseWarnings.TotalNotFound], ParseConfidence.Low, errorMessage);
}
