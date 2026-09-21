using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Services;

/// <summary>
/// Local store of captured receipts. The app is offline-first: a receipt is saved
/// here the moment it is photographed and only then queued for the accounting
/// system, so losing signal — or the app being killed — never loses a receipt.
/// </summary>
public interface IReceiptRepository
{
    /// <summary>Creates the schema if needed. Safe to call repeatedly.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a receipt.</summary>
    Task SaveAsync(Receipt receipt, CancellationToken cancellationToken = default);

    Task<Receipt?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All receipts, newest capture first.</summary>
    Task<IReadOnlyList<Receipt>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pending receipts whose backoff window has elapsed at <paramref name="asOf"/>,
    /// oldest capture first so the queue drains in order.
    /// </summary>
    Task<IReadOnlyList<Receipt>> GetDueForSyncAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
