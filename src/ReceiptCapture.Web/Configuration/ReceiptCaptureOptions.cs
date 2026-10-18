namespace ReceiptCapture.Web.Configuration;

/// <summary>Top-level configuration, bound from the <c>ReceiptCapture</c> section.</summary>
public sealed class ReceiptCaptureOptions
{
    public const string SectionName = "ReceiptCapture";

    /// <summary>
    /// Where the receipt database and photos live. Relative paths resolve against the
    /// content root; point this at a mounted volume when running in a container.
    /// </summary>
    public string DataDirectory { get; set; } = "App_Data";

    /// <summary>
    /// Shared code required before the app can be used. Leaving it unset leaves the
    /// app open to anyone who can reach it, which is only ever appropriate on a
    /// private network — the server logs a warning at startup when that is the case.
    /// </summary>
    public string? AccessCode { get; set; }

    /// <summary>
    /// Largest receipt photo accepted. The browser downscales before uploading, so
    /// anything approaching this is either a very large sensor or not a photo at all.
    /// </summary>
    public long MaxUploadBytes { get; set; } = 12L * 1024 * 1024;

    public AccountingOptions Accounting { get; set; } = new();
}

/// <summary>Where finished receipts are sent.</summary>
public sealed class AccountingOptions
{
    /// <summary>HTTPS endpoint receipts are POSTed to.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Optional GET endpoint for the connection test; falls back to <see cref="Endpoint"/>.</summary>
    public string? HealthEndpoint { get; set; }

    /// <summary>
    /// Credential for the accounting system.
    /// </summary>
    /// <remarks>
    /// Deliberately configuration-only: set it through an environment variable
    /// (<c>ReceiptCapture__Accounting__ApiKey</c>), user-secrets or your host's secret
    /// store. It is never sent to the browser and cannot be set through the web UI,
    /// so reaching the Settings page does not hand anyone the credential.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>Header carrying the key. Set to "Authorization" to send a bearer token.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>Whether the receipt photo is attached to the submission.</summary>
    public bool IncludeImage { get; set; } = true;
}
