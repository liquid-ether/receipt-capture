using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Connectors;

/// <summary>
/// A destination for finished receipts. One implementation per accounting system.
/// </summary>
/// <remarks>
/// Everything above this interface — the capture flow, the queue, the retry policy —
/// is written once. Adding QuickBooks or Xero later means adding a class here, not
/// touching the app.
/// </remarks>
public interface IAccountingConnector
{
    /// <summary>Stable identifier for the connector, shown in Settings.</summary>
    string Name { get; }

    /// <summary>Verifies credentials and reachability so Settings can show a green check.</summary>
    Task<ConnectorResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one receipt. <paramref name="image"/> is the receipt photo, left null when
    /// the destination does not want attachments. The caller owns the stream.
    /// </summary>
    Task<ConnectorResult> SubmitAsync(
        Receipt receipt,
        Stream? image,
        string? imageFileName,
        CancellationToken cancellationToken = default);
}
