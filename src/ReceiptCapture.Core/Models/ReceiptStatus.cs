namespace ReceiptCapture.Core.Models;

/// <summary>Lifecycle of a captured receipt as it moves towards the accounting system.</summary>
public enum ReceiptStatus
{
    /// <summary>Captured and parsed, but the user has not confirmed the values yet.</summary>
    Draft = 0,

    /// <summary>Confirmed by the user and queued for submission.</summary>
    Pending = 1,

    /// <summary>Accepted by the accounting system.</summary>
    Sent = 2,

    /// <summary>Permanently rejected, or retried too many times. Needs the user to intervene.</summary>
    Failed = 3,
}
