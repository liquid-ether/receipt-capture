namespace ReceiptCapture.Core.Services;

/// <summary>
/// Reads the text off a receipt photo.
/// </summary>
/// <remarks>
/// An interface rather than a concrete class because the engine is a deployment
/// decision, not a domain one: the web app shells out to Tesseract on the server,
/// and a native client would use whatever its platform provides instead.
/// </remarks>
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
