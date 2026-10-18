namespace ReceiptCapture.Web.Ocr;

/// <summary>Settings for <see cref="TesseractOcrService"/>.</summary>
public sealed class TesseractOcrOptions
{
    public const string SectionName = "ReceiptCapture:Ocr";

    /// <summary>The tesseract binary. Left as a bare name it is resolved from PATH.</summary>
    public string ExecutablePath { get; set; } = "tesseract";

    /// <summary>
    /// Recognition languages, in Tesseract's "+" syntax. Both are requested together
    /// because a Canadian receipt routinely prints English and French on one slip,
    /// and the server cannot know which it is about to be handed.
    /// </summary>
    public string Languages { get; set; } = "eng+fra";

    /// <summary>
    /// Page segmentation mode. 6 ("assume a single uniform block of text") is the
    /// best fit for a receipt, which is one narrow column top to bottom.
    /// </summary>
    public int PageSegmentationMode { get; set; } = 6;

    /// <summary>Ceiling on one recognition, so a pathological image cannot tie up a worker.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}
