using System.Globalization;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Resources;
using ReceiptCapture.Web.Services;

namespace ReceiptCapture.Web.Api;

/// <summary>Turns domain receipts into the shapes the browser renders.</summary>
internal static class ReceiptMapping
{
    public static ReceiptDraftResponse ToDraftResponse(CaptureOutcome outcome, CultureInfo culture)
    {
        var receipt = outcome.Draft;

        return new ReceiptDraftResponse(
            receipt.Id,
            receipt.MerchantName,
            receipt.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            receipt.Currency,
            receipt.Subtotal,
            receipt.Tip,
            receipt.Total > 0m ? receipt.Total : null,
            [.. receipt.Taxes.Select(t => new TaxLineDto(t.Code, t.Amount, t.Rate))],
            receipt.CategoryCode,
            receipt.Notes,
            ImageUrl(receipt),
            // Warnings arrive as resource keys; the browser should never see a key.
            [.. outcome.Warnings.Select(key => AppStrings.Get(key, culture))],
            outcome.Confidence.ToString(),
            outcome.ErrorMessage);
    }

    public static ReceiptListItemResponse ToListItem(Receipt receipt, CultureInfo culture)
    {
        var dateText = receipt.TransactionDate is { } date
            ? date.ToString("d", culture)
            : receipt.CapturedAtUtc.ToLocalTime().ToString("d", culture);

        return new ReceiptListItemResponse(
            receipt.Id,
            receipt.MerchantName ?? AppStrings.Get("Warning_MerchantNotFound", culture),
            dateText,
            FormatMoney(receipt.Total, receipt.Currency, culture),
            receipt.Status.ToString(),
            AppStrings.ForStatus(receipt.Status, culture),
            receipt.LastError,
            receipt.Status is ReceiptStatus.Failed,
            ImageUrl(receipt));
    }

    /// <summary>
    /// Formats in the receipt's own currency rather than the viewer's, so a euro
    /// receipt captured in Canada still reads as euros.
    /// </summary>
    public static string FormatMoney(decimal amount, string currencyCode, CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = CurrencySymbol(currencyCode);

        return amount.ToString("C", format);
    }

    private static string? ImageUrl(Receipt receipt) =>
        string.IsNullOrEmpty(receipt.ImagePath) ? null : $"/api/receipts/{receipt.Id:D}/image";

    private static string CurrencySymbol(string currencyCode) => currencyCode switch
    {
        "EUR" => "€",
        "GBP" => "£",
        "USD" => "US$",
        "CAD" => "$",
        _ => currencyCode + " ",
    };
}
