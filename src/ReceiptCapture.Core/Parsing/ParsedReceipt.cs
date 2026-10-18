using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// What the parser managed to read off a receipt photo. Every value is nullable:
/// this is a best effort over OCR output, and the review screen is where it becomes
/// a <see cref="Receipt"/> the user has vouched for.
/// </summary>
public sealed class ParsedReceipt
{
    public string? MerchantName { get; init; }

    public DateOnly? TransactionDate { get; init; }

    public string Currency { get; init; } = "CAD";

    public decimal? Subtotal { get; init; }

    public decimal? Total { get; init; }

    public decimal? Tip { get; init; }

    public IReadOnlyList<ReceiptTax> Taxes { get; init; } = [];

    /// <summary>Resource keys from <see cref="ParseWarnings"/>, in the order they were raised.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public ParseConfidence Confidence { get; init; }

    public decimal TaxTotal => Taxes.Sum(t => t.Amount);

    /// <summary>
    /// Projects the parse into a draft receipt for the review screen. The result is a
    /// <see cref="ReceiptStatus.Draft"/> until the user confirms it.
    /// </summary>
    public Receipt ToDraft(string? imagePath = null, DateTimeOffset? capturedAtUtc = null) => new()
    {
        MerchantName = MerchantName,
        TransactionDate = TransactionDate,
        Currency = Currency,
        Subtotal = Subtotal,
        Tip = Tip,
        Total = Total ?? 0m,
        Taxes = [.. Taxes.Select(t => new ReceiptTax(t.Code, t.Amount, t.Rate))],
        ImagePath = imagePath,
        Status = ReceiptStatus.Draft,
        CapturedAtUtc = capturedAtUtc ?? DateTimeOffset.UtcNow,
    };
}
