using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ReceiptCapture.Core.Models;
using SQLite;

namespace ReceiptCapture.Core.Storage;

/// <summary>
/// The SQLite shape of a receipt, kept separate from the <see cref="Receipt"/>
/// domain model so persistence attributes and storage compromises stay out of it.
/// </summary>
/// <remarks>
/// Money is stored as invariant-culture text rather than a floating-point column.
/// SQLite has no decimal type, and rounding a tax line through a double is exactly
/// the sort of error an accounting app must not make.
/// </remarks>
[Table("receipts")]
internal sealed class ReceiptRow
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("merchant_name")]
    public string? MerchantName { get; set; }

    /// <summary>"yyyy-MM-dd", or null.</summary>
    [Column("transaction_date")]
    public string? TransactionDate { get; set; }

    [Column("currency")]
    public string Currency { get; set; } = "CAD";

    [Column("subtotal")]
    public string? Subtotal { get; set; }

    [Column("tip")]
    public string? Tip { get; set; }

    [Column("total")]
    public string Total { get; set; } = "0";

    /// <summary>JSON array of <see cref="ReceiptTax"/>.</summary>
    [Column("taxes_json")]
    public string? TaxesJson { get; set; }

    [Column("category_code")]
    public string? CategoryCode { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("image_path")]
    public string? ImagePath { get; set; }

    [Indexed]
    [Column("status")]
    public int Status { get; set; }

    [Column("captured_at")]
    public string CapturedAt { get; set; } = string.Empty;

    [Column("submitted_at")]
    public string? SubmittedAt { get; set; }

    [Column("remote_id")]
    public string? RemoteId { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("next_attempt_at")]
    public string? NextAttemptAt { get; set; }

    public static ReceiptRow FromReceipt(Receipt receipt) => new()
    {
        Id = receipt.Id.ToString("D"),
        MerchantName = receipt.MerchantName,
        TransactionDate = receipt.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Currency = receipt.Currency,
        Subtotal = FormatMoney(receipt.Subtotal),
        Tip = FormatMoney(receipt.Tip),
        Total = FormatMoney(receipt.Total)!,
        TaxesJson = receipt.Taxes.Count == 0
            ? null
            : JsonSerializer.Serialize(receipt.Taxes, StorageJsonContext.Default.ListReceiptTax),
        CategoryCode = receipt.CategoryCode,
        Notes = receipt.Notes,
        ImagePath = receipt.ImagePath,
        Status = (int)receipt.Status,
        CapturedAt = FormatInstant(receipt.CapturedAtUtc)!,
        SubmittedAt = FormatInstant(receipt.SubmittedAtUtc),
        RemoteId = receipt.RemoteId,
        LastError = receipt.LastError,
        AttemptCount = receipt.AttemptCount,
        NextAttemptAt = FormatInstant(receipt.NextAttemptUtc),
    };

    public Receipt ToReceipt() => new()
    {
        Id = Guid.Parse(Id),
        MerchantName = MerchantName,
        TransactionDate = ParseDate(TransactionDate),
        Currency = Currency,
        Subtotal = ParseMoney(Subtotal),
        Tip = ParseMoney(Tip),
        Total = ParseMoney(Total) ?? 0m,
        Taxes = string.IsNullOrEmpty(TaxesJson)
            ? []
            : JsonSerializer.Deserialize(TaxesJson, StorageJsonContext.Default.ListReceiptTax) ?? [],
        CategoryCode = CategoryCode,
        Notes = Notes,
        ImagePath = ImagePath,
        Status = Enum.IsDefined((ReceiptStatus)Status) ? (ReceiptStatus)Status : ReceiptStatus.Draft,
        CapturedAtUtc = ParseInstant(CapturedAt) ?? DateTimeOffset.UnixEpoch,
        SubmittedAtUtc = ParseInstant(SubmittedAt),
        RemoteId = RemoteId,
        LastError = LastError,
        AttemptCount = AttemptCount,
        NextAttemptUtc = ParseInstant(NextAttemptAt),
    };

    private static string? FormatMoney(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Formats as UTC with a fixed-width pattern, so sorting the column as text
    /// sorts it chronologically.
    /// </summary>
    private static string? FormatInstant(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseInstant(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}

/// <summary>Source-generated contract for the tax blob, so the trimmer keeps what it needs.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<ReceiptTax>))]
internal sealed partial class StorageJsonContext : JsonSerializerContext;
