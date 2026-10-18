using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ReceiptCapture.Core.Parsing;
using ReceiptCapture.Web.Ocr;

namespace ReceiptCapture.Web.Tests;

/// <summary>
/// The whole reading half of the app against real images: Tesseract actually runs,
/// and its output actually goes through the parser. A unit test over hand-written
/// OCR lines cannot tell you the engine and the parser agree on what a receipt
/// looks like; this can.
/// </summary>
[Collection("Tesseract")]
public class OcrPipelineTests
{
    private static readonly ReceiptParseOptions ParseOptions =
        new() { Today = new DateOnly(2026, 9, 21) };

    private static TesseractOcrService CreateService() =>
        new(Options.Create(new TesseractOcrOptions()), NullLogger<TesseractOcrService>.Instance);

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [SkippableFact]
    public async Task An_english_receipt_photo_is_read_and_parsed()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        var text = await CreateService().RecognizeAsync(Fixture("receipt-en.png"));
        var parsed = ReceiptTextParser.Parse(text.Lines, ParseOptions);

        Assert.Equal("THE COFFEE HOUSE", parsed.MerchantName);
        Assert.Equal(new DateOnly(2026, 3, 14), parsed.TransactionDate);
        Assert.Equal(11.20m, parsed.Subtotal);
        Assert.Equal(12.66m, parsed.Total);
        Assert.Equal("CAD", parsed.Currency);

        var tax = Assert.Single(parsed.Taxes);
        Assert.Equal("HST", tax.Code);
        Assert.Equal(1.46m, tax.Amount);
    }

    [SkippableFact]
    public async Task A_french_receipt_photo_is_read_and_parsed()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        var text = await CreateService().RecognizeAsync(Fixture("receipt-fr.png"));
        var parsed = ReceiptTextParser.Parse(text.Lines, ParseOptions);

        Assert.Equal("BOULANGERIE DU COIN", parsed.MerchantName);
        Assert.Equal(new DateOnly(2026, 3, 14), parsed.TransactionDate);
        Assert.Equal(10.50m, parsed.Subtotal);
        Assert.Equal(12.08m, parsed.Total);

        Assert.Collection(
            parsed.Taxes,
            tps => Assert.Equal("TPS", tps.Code),
            tvq => Assert.Equal("TVQ", tvq.Code));

        // 10,50 + 0,53 + 1,05 = 12,08 — the comma decimals survived the round trip.
        Assert.Equal(1.58m, parsed.TaxTotal);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [SkippableFact]
    public async Task Recognized_lines_come_back_in_reading_order()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        var text = await CreateService().RecognizeAsync(Fixture("receipt-en.png"));

        var lines = text.Lines.ToList();
        var merchant = lines.FindIndex(l => l.Contains("COFFEE", StringComparison.OrdinalIgnoreCase));
        var total = lines.FindIndex(l => l.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase));

        Assert.True(merchant >= 0, "the merchant line should be recognized");
        Assert.True(total > merchant, "the total must come after the merchant, as it does on the slip");
        Assert.InRange(text.AverageConfidence, 0.5d, 1.0d);
    }

    [Fact]
    public async Task A_missing_image_is_reported_rather_than_silently_empty()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => CreateService().RecognizeAsync(Fixture("does-not-exist.png")));
    }

    [Fact]
    public async Task A_missing_tesseract_binary_gives_an_actionable_error()
    {
        var service = new TesseractOcrService(
            Options.Create(new TesseractOcrOptions { ExecutablePath = "tesseract-not-installed" }),
            NullLogger<TesseractOcrService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.RecognizeAsync(Fixture("receipt-en.png")));

        Assert.Contains("tesseract-ocr", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tsv_rows_are_grouped_into_lines_by_layout_not_whitespace()
    {
        // Two words on one line, then a word on the next, deliberately out of order
        // in the rows to prove grouping uses the block/paragraph/line columns.
        const string Tsv =
            "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n" +
            "5\t1\t1\t1\t1\t2\t10\t10\t10\t10\t96.0\tHOUSE\n" +
            "5\t1\t1\t1\t1\t1\t10\t10\t10\t10\t95.0\tCOFFEE\n" +
            "5\t1\t1\t1\t2\t1\t10\t50\t10\t10\t90.0\tTOTAL\n" +
            "4\t1\t1\t1\t2\t0\t10\t50\t10\t10\t-1\t\n";

        var result = TesseractOcrService.ParseTsv(Tsv);

        Assert.Equal(["COFFEE HOUSE", "TOTAL"], result.Lines);
        Assert.InRange(result.AverageConfidence, 0.9d, 1.0d);
    }

    [Fact]
    public void Empty_tsv_yields_no_lines()
    {
        Assert.Empty(TesseractOcrService.ParseTsv(string.Empty).Lines);
    }
}
