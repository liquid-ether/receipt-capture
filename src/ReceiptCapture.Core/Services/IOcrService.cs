namespace ReceiptCapture.Core.Services;

/// <summary>
/// Reads the text off a receipt photo. Implemented per platform — on iPhone by
/// Apple's Vision framework, which runs entirely on device so the photo of a
/// receipt never leaves the phone before the user has reviewed it.
/// </summary>
public interface IOcrService
{
    /// <summary>
    /// Recognizes text in the image at <paramref name="imagePath"/> and returns the
    /// lines in reading order, top of the slip first.
    /// </summary>
    Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}

/// <summary>Text recognized from one image.</summary>
/// <param name="Lines">Recognized lines, in reading order.</param>
/// <param name="AverageConfidence">Mean engine confidence across lines, 0 to 1.</param>
public sealed record OcrResult(IReadOnlyList<string> Lines, double AverageConfidence = 0d)
{
    public static OcrResult Empty { get; } = new([]);
}
