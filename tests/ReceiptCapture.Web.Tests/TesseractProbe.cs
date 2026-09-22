using System.Diagnostics;

namespace ReceiptCapture.Web.Tests;

/// <summary>
/// Whether the OCR binary is on this machine.
/// </summary>
/// <remarks>
/// The tests that actually run Tesseract are the most valuable ones here, so they
/// are not deleted for portability — they skip with a message telling a developer
/// exactly what to install. CI installs it, so CI runs them for real.
/// </remarks>
internal static class TesseractProbe
{
    public const string SkipReason =
        "tesseract is not on PATH. Install it to run the OCR tests: " +
        "apt-get install tesseract-ocr tesseract-ocr-eng tesseract-ocr-fra";

    private static readonly Lazy<bool> Available = new(Probe, isThreadSafe: true);

    public static bool IsAvailable => Available.Value;

    private static bool Probe()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "tesseract",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return false;
            }

            return process.WaitForExit(TimeSpan.FromSeconds(20)) && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Runs the Tesseract-backed tests one at a time. Each spawns a process that will
/// happily use every core, and several at once just make each other slower.
/// </summary>
[CollectionDefinition("Tesseract", DisableParallelization = true)]
public sealed class TesseractCollection;
