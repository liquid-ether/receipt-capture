namespace ReceiptCapture.Core.Connectors;

/// <summary>
/// Settings for <see cref="GenericRestConnector"/>. The instance is mutable and
/// shared with the Settings screen, so edits take effect on the next submission
/// without rebuilding the DI graph.
/// </summary>
public sealed class GenericRestConnectorOptions
{
    /// <summary>Where receipts are POSTed, e.g. <c>https://books.example.com/api/receipts</c>.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Optional GET endpoint used by "Test connection". Falls back to <see cref="Endpoint"/>.</summary>
    public Uri? HealthEndpoint { get; set; }

    /// <summary>
    /// Secret for the accounting system. Supplied by the host from its own
    /// configuration; it is never written to the receipt database.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Header carrying <see cref="ApiKey"/>. Some systems want "Authorization" instead.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-API-Key";

    /// <summary>Send the receipt photo alongside the structured data.</summary>
    public bool IncludeImage { get; set; } = true;

    /// <summary>True when the connector has enough configuration to attempt a call.</summary>
    public bool IsConfigured => Endpoint is not null;
}
