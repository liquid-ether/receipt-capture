namespace ReceiptCapture.Core.Services;

/// <summary>
/// Holds receipt photos outside the database, in the app sandbox. Images are far
/// too large for SQLite rows, and keeping them as files lets iOS exclude them from
/// backups or purge them independently of the metadata.
/// </summary>
public interface IReceiptImageStore
{
    /// <summary>Copies <paramref name="source"/> into the store and returns its path.</summary>
    Task<string> SaveAsync(Guid receiptId, Stream source, string fileExtension, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored image for reading, or returns null when it is gone.</summary>
    Task<Stream?> OpenAsync(string imagePath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string imagePath, CancellationToken cancellationToken = default);
}
