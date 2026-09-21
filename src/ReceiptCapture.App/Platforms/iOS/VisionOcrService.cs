using Foundation;
using ReceiptCapture.Core.Services;
using UIKit;
using Vision;

namespace ReceiptCapture.App.Platforms.iOS;

/// <summary>
/// On-device OCR using Apple's Vision framework.
/// </summary>
/// <remarks>
/// Vision runs entirely on the phone, so a receipt photo — which shows where
/// someone was, when, and what they bought — never leaves the device before the
/// user has reviewed it and chosen to send it. It is also free, fast, and works in
/// airplane mode, which a cloud OCR service is not.
/// </remarks>
public sealed class VisionOcrService : IOcrService
{
    /// <summary>
    /// Both languages are requested together so one slip can mix them, which is
    /// normal on Canadian receipts ("Subtotal" above "Sous-total").
    /// </summary>
    private static readonly string[] RecognitionLanguages = ["en-CA", "en-US", "fr-CA", "fr-FR"];

    public Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        var completion = new TaskCompletionSource<OcrResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Vision is CPU-heavy; keeping it off the UI thread keeps the spinner moving.
        Task.Run(
            () =>
            {
                try
                {
                    completion.TrySetResult(Recognize(imagePath));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            cancellationToken);

        return completion.Task.WaitAsync(cancellationToken);
    }

    private static OcrResult Recognize(string imagePath)
    {
        using var image = UIImage.FromFile(imagePath);
        if (image?.CGImage is not { } cgImage)
        {
            return OcrResult.Empty;
        }

        var lines = new List<RecognizedLine>();
        var confidences = new List<double>();

        using var request = new VNRecognizeTextRequest((vnRequest, error) =>
        {
            if (error is not null || vnRequest.GetResults<VNRecognizedTextObservation>() is not { } observations)
            {
                return;
            }

            foreach (var observation in observations)
            {
                var candidate = observation.TopCandidates(1).FirstOrDefault();
                if (candidate?.String is not { Length: > 0 } text)
                {
                    continue;
                }

                // BoundingBox is in Vision's normalized coordinate space, whose origin
                // is bottom-left, so a larger Y is higher up the slip.
                lines.Add(new RecognizedLine(
                    text,
                    Top: observation.BoundingBox.GetMaxY(),
                    Left: observation.BoundingBox.GetMinX()));

                confidences.Add(candidate.Confidence);
            }
        })
        {
            // Accurate is materially better than Fast on small receipt print, and the
            // extra time is invisible next to the user tapping through the review form.
            RecognitionLevel = VNRequestTextRecognitionLevel.Accurate,
            UsesLanguageCorrection = true,
            RecognitionLanguages = RecognitionLanguages,
        };

        using var handler = new VNImageRequestHandler(cgImage, Orientation(image), new NSDictionary());
        handler.Perform([request], out var performError);

        if (performError is not null)
        {
            throw new InvalidOperationException($"Vision could not read the image: {performError.LocalizedDescription}");
        }

        // Vision returns observations in no particular order. The parser depends on
        // reading order, so sort down the slip and then across each line.
        var ordered = lines
            .OrderByDescending(line => line.Top)
            .ThenBy(line => line.Left)
            .Select(line => line.Text)
            .ToList();

        return new OcrResult(ordered, confidences.Count == 0 ? 0d : confidences.Average());
    }

    /// <summary>Maps the photo's EXIF orientation so Vision reads a sideways photo correctly.</summary>
    private static CoreGraphics.CGImagePropertyOrientation Orientation(UIImage image) =>
        image.Orientation switch
        {
            UIImageOrientation.Up => CoreGraphics.CGImagePropertyOrientation.Up,
            UIImageOrientation.Down => CoreGraphics.CGImagePropertyOrientation.Down,
            UIImageOrientation.Left => CoreGraphics.CGImagePropertyOrientation.Left,
            UIImageOrientation.Right => CoreGraphics.CGImagePropertyOrientation.Right,
            UIImageOrientation.UpMirrored => CoreGraphics.CGImagePropertyOrientation.UpMirrored,
            UIImageOrientation.DownMirrored => CoreGraphics.CGImagePropertyOrientation.DownMirrored,
            UIImageOrientation.LeftMirrored => CoreGraphics.CGImagePropertyOrientation.LeftMirrored,
            UIImageOrientation.RightMirrored => CoreGraphics.CGImagePropertyOrientation.RightMirrored,
            _ => CoreGraphics.CGImagePropertyOrientation.Up,
        };

    private readonly record struct RecognizedLine(string Text, double Top, double Left);
}
