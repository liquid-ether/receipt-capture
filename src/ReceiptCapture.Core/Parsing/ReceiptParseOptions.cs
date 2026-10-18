namespace ReceiptCapture.Core.Parsing;

/// <summary>Tuning knobs for <see cref="ReceiptTextParser"/>.</summary>
public sealed class ReceiptParseOptions
{
    public static ReceiptParseOptions Default { get; } = new();

    /// <summary>
    /// How to read "03/04/2026" when both numbers could be a month. Day-first is the
    /// default because it is correct in Canada and France; a US-only deployment would
    /// set this to false. Either way the ambiguity is reported, never hidden.
    /// </summary>
    public bool PreferDayFirst { get; init; } = true;

    /// <summary>ISO 4217 code used when the receipt gives no clue about its currency.</summary>
    public string DefaultCurrency { get; init; } = "CAD";

    /// <summary>
    /// Reference date used to reject implausible dates and expand two-digit years.
    /// Left null it means today; tests set it so they do not drift over time.
    /// </summary>
    public DateOnly? Today { get; init; }

    /// <summary>How many lines from the top may hold the merchant name.</summary>
    public int MerchantSearchLineCount { get; init; } = 8;
}
