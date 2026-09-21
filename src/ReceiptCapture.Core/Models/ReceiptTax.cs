namespace ReceiptCapture.Core.Models;

/// <summary>A single tax line on a receipt (GST/TPS, QST/TVQ, HST/TVH, VAT/TVA, ...).</summary>
public sealed class ReceiptTax
{
    /// <summary>Normalized, upper-case tax code exactly as printed on the receipt, e.g. "GST" or "TVQ".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Tax amount in the receipt's currency.</summary>
    public decimal Amount { get; set; }

    /// <summary>Percentage rate when the receipt prints one, e.g. 9.975m for QST. Null when unknown.</summary>
    public decimal? Rate { get; set; }

    public ReceiptTax() { }

    public ReceiptTax(string code, decimal amount, decimal? rate = null)
    {
        Code = code;
        Amount = amount;
        Rate = rate;
    }
}
