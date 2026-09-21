using ReceiptCapture.Core.Services;

namespace ReceiptCapture.App.Services;

/// <summary>
/// Stands in on any platform with no OCR implementation wired up. Registering this
/// rather than leaving the dependency unregistered turns a startup crash into a
/// clear message on the capture screen.
/// </summary>
public sealed class UnavailableOcrService : IOcrService
{
    public Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromException<OcrResult>(
            new PlatformNotSupportedException("Text recognition is not available on this platform."));
}
