using System.Text.Json.Serialization;
using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Connectors;

/// <summary>Wire format posted to the accounting system.</summary>
public sealed class ReceiptSubmissionDto
{
    public required string Id { get; init; }

    public string? Merchant { get; init; }

    /// <summary>ISO 8601 calendar date, e.g. "2026-09-21". Null when the receipt had none.</summary>
    public string? TransactionDate { get; init; }

    public required string Currency { get; init; }

    public decimal? Subtotal { get; init; }

    public decimal? Tip { get; init; }

    public required decimal Total { get; init; }

    public required decimal TaxTotal { get; init; }

    public IReadOnlyList<TaxDto> Taxes { get; init; } = [];

    public string? Category { get; init; }

    public string? Notes { get; init; }

    /// <summary>When the photo was taken, ISO 8601 with offset.</summary>
    public required string CapturedAt { get; init; }

    /// <summary>Lets the accounting system tell this app's submissions apart from others.</summary>
    public string Source { get; init; } = "receipt-capture-ios";

    public static ReceiptSubmissionDto FromReceipt(Receipt receipt) => new()
    {
        Id = receipt.Id.ToString("D"),
        Merchant = receipt.MerchantName,
        TransactionDate = receipt.TransactionDate?.ToString("yyyy-MM-dd"),
        Currency = receipt.Currency,
        Subtotal = receipt.Subtotal,
        Tip = receipt.Tip,
        Total = receipt.Total,
        TaxTotal = receipt.TaxTotal,
        Taxes = [.. receipt.Taxes.Select(t => new TaxDto { Code = t.Code, Amount = t.Amount, Rate = t.Rate })],
        Category = receipt.CategoryCode,
        Notes = receipt.Notes,
        CapturedAt = receipt.CapturedAtUtc.ToString("O"),
    };
}

public sealed class TaxDto
{
    public required string Code { get; init; }

    public required decimal Amount { get; init; }

    public decimal? Rate { get; init; }
}

/// <summary>
/// Source-generated serialization. iOS builds run the ILLink trimmer and, for
/// release, Native AOT; reflection-based System.Text.Json breaks under both, so the
/// contract is generated at compile time instead.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReceiptSubmissionDto))]
[JsonSerializable(typeof(TaxDto))]
public sealed partial class ReceiptJsonContext : JsonSerializerContext;
