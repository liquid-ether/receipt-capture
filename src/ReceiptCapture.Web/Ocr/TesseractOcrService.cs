using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Web.Ocr;

/// <summary>
/// Reads receipt text by invoking the Tesseract binary.
/// </summary>
/// <remarks>
/// Shelling out rather than binding the native library: the command line is a
/// stable, well-documented contract, a crash in the OCR engine cannot take the web
/// server down with it, and the dependency is a single apt package on the host.
/// <para>
/// TSV output is requested rather than plain text because it carries the block,
/// paragraph and line numbers Tesseract derived from the page layout. Grouping
/// words by those gives reliable reading order — which the parser depends on —
/// instead of guessing from whitespace in a flat text dump.
/// </para>
/// </remarks>
public sealed class TesseractOcrService : IOcrService
{
    private readonly TesseractOcrOptions _options;
    private readonly ILogger<TesseractOcrService> _logger;

    public TesseractOcrService(IOptions<TesseractOcrOptions> options, ILogger<TesseractOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("The image to recognize does not exist.", imagePath);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Passed as separate argument values, never concatenated into a command
        // line, so a crafted file name cannot become extra arguments.
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add(_options.Languages);
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add(_options.PageSegmentationMode.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("tsv");

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not run Tesseract ('{_options.ExecutablePath}'). Is tesseract-ocr installed on the server?",
                ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Tesseract exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            // Tesseract chats about DPI and orientation on stderr even when it succeeds.
            _logger.LogDebug("Tesseract diagnostics: {Diagnostics}", stderr.Trim());
        }

        return ParseTsv(stdout);
    }

    /// <summary>
    /// Turns Tesseract's TSV into lines in reading order.
    /// </summary>
    /// <remarks>
    /// Columns are: level, page, block, paragraph, line, word, left, top, width,
    /// height, confidence, text. Only level 5 rows are words; the rest describe the
    /// enclosing regions and carry no text.
    /// </remarks>
    internal static OcrResult ParseTsv(string tsv)
    {
        if (string.IsNullOrWhiteSpace(tsv))
        {
            return OcrResult.Empty;
        }

        var lines = new List<(int Block, int Paragraph, int Line, List<(int Word, string Text)> Words)>();
        var index = new Dictionary<(int, int, int), int>();
        var confidences = new List<double>();
        var isFirstRow = true;

        foreach (var row in tsv.Split('\n'))
        {
            if (isFirstRow)
            {
                isFirstRow = false; // header
                continue;
            }

            var fields = row.TrimEnd('\r').Split('\t');
            if (fields.Length < 12 || fields[0] != "5")
            {
                continue;
            }

            var text = fields[11];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!TryInt(fields[2], out var block)
                || !TryInt(fields[3], out var paragraph)
                || !TryInt(fields[4], out var line)
                || !TryInt(fields[5], out var word))
            {
                continue;
            }

            if (double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence)
                && confidence >= 0)
            {
                confidences.Add(confidence / 100d);
            }

            var key = (block, paragraph, line);
            if (!index.TryGetValue(key, out var position))
            {
                position = lines.Count;
                index[key] = position;
                lines.Add((block, paragraph, line, []));
            }

            lines[position].Words.Add((word, text));
        }

        var ordered = lines
            .Select(l => string.Join(
                ' ',
                l.Words.OrderBy(w => w.Word).Select(w => w.Text)))
            .Where(l => l.Length > 0)
            .ToList();

        return new OcrResult(ordered, confidences.Count == 0 ? 0d : confidences.Average());
    }

    private static bool TryInt(string value, out int parsed) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already went away; nothing left to clean up.
        }
    }
}
