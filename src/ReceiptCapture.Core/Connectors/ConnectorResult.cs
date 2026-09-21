namespace ReceiptCapture.Core.Connectors;

/// <summary>
/// Outcome of talking to an accounting system.
/// </summary>
/// <remarks>
/// The <see cref="IsTransient"/> flag is what the sync queue acts on: a dropped
/// connection on a train is worth retrying, a rejected API key is not, and retrying
/// the latter forever would just burn battery and lock the account out.
/// </remarks>
public sealed record ConnectorResult
{
    public required bool Success { get; init; }

    /// <summary>Identifier assigned by the accounting system, when it returns one.</summary>
    public string? RemoteId { get; init; }

    /// <summary>Diagnostic message for the History screen. Never contains credentials.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when retrying later could plausibly succeed.</summary>
    public bool IsTransient { get; init; }

    public static ConnectorResult Ok(string? remoteId = null) =>
        new() { Success = true, RemoteId = remoteId };

    /// <summary>A failure worth retrying: network loss, timeout, throttling, server fault.</summary>
    public static ConnectorResult Transient(string message) =>
        new() { Success = false, ErrorMessage = message, IsTransient = true };

    /// <summary>A failure that will keep failing: bad credentials, rejected payload, misconfiguration.</summary>
    public static ConnectorResult Permanent(string message) =>
        new() { Success = false, ErrorMessage = message, IsTransient = false };
}
