using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Core.Storage;

/// <summary>
/// Stores receipt photos as files under a root directory that only this app reads.
/// </summary>
public sealed class FileSystemImageStore : IReceiptImageStore
{
    private readonly string _rootDirectory;

    public FileSystemImageStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    public async Task<string> SaveAsync(
        Guid receiptId,
        Stream source,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        Directory.CreateDirectory(_rootDirectory);

        var extension = NormalizeExtension(fileExtension);
        var destination = Path.Combine(_rootDirectory, $"{receiptId:D}{extension}");

        await using (var file = File.Create(destination))
        {
            await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return destination;
    }

    public Task<Stream?> OpenAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!IsInsideStore(imagePath) || !File.Exists(imagePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(imagePath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (IsInsideStore(imagePath) && File.Exists(imagePath))
        {
            File.Delete(imagePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Confines reads and deletes to the store's own directory. Paths come back out
    /// of the database, so a corrupted or tampered row must not be able to point the
    /// app at an arbitrary file elsewhere on the device.
    /// </summary>
    private bool IsInsideStore(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return false;
        }

        var full = Path.GetFullPath(imagePath);
        var root = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDirectory
            : _rootDirectory + Path.DirectorySeparatorChar;

        return full.StartsWith(root, StringComparison.Ordinal);
    }

    private static string NormalizeExtension(string fileExtension)
    {
        if (string.IsNullOrWhiteSpace(fileExtension))
        {
            return ".jpg";
        }

        var extension = fileExtension.StartsWith('.') ? fileExtension : "." + fileExtension;

        // The extension reaches here from an upload; keep it to a known-safe set so it
        // cannot introduce path separators or traversal segments into the file name.
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ".jpg",
            ".png" => ".png",
            ".heic" => ".heic",
            ".webp" => ".webp",
            ".pdf" => ".pdf",
            _ => ".jpg",
        };
    }
}
