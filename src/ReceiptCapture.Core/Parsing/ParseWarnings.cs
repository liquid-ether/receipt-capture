namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// Resource keys for the things the parser could not determine confidently.
/// They are keys rather than sentences so the review screen can render them in
/// the user's language; see <c>Resources/Strings/AppResources.resx</c>.
/// </summary>
public static class ParseWarnings
{
    public const string MerchantNotFound = "Warning_MerchantNotFound";
    public const string DateNotFound = "Warning_DateNotFound";
    public const string DateAmbiguous = "Warning_DateAmbiguous";
    public const string TotalNotFound = "Warning_TotalNotFound";
    public const string TotalComputed = "Warning_TotalComputed";
    public const string TotalsDoNotReconcile = "Warning_TotalsDoNotReconcile";
}

/// <summary>How much the app should trust the parsed values without the user checking them.</summary>
public enum ParseConfidence
{
    /// <summary>No total could be read. The user has to fill the form in themselves.</summary>
    Low = 0,

    /// <summary>A total was read, but something else needs confirming.</summary>
    Medium = 1,

    /// <summary>Everything read cleanly and the amounts reconcile.</summary>
    High = 2,
}
