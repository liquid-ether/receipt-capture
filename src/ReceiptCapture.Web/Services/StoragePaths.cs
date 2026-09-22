using Microsoft.Extensions.Options;
using ReceiptCapture.Web.Configuration;

namespace ReceiptCapture.Web.Services;

/// <summary>
/// Resolves where the database and receipt photos live, and creates the directories
/// once at startup so nothing later has to guess whether they exist.
/// </summary>
public sealed class StoragePaths
{
    public StoragePaths(IOptions<ReceiptCaptureOptions> options, IHostEnvironment environment)
    {
        DataDirectory = Resolve(options.Value.DataDirectory, environment.ContentRootPath);

        ImageDirectory = Path.Combine(DataDirectory, "receipt-images");
        DatabasePath = Path.Combine(DataDirectory, "receipts.db3");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ImageDirectory);
    }

    public string DataDirectory { get; }

    public string ImageDirectory { get; }

    public string DatabasePath { get; }

    /// <summary>
    /// The same resolution, available before the service container is built —
    /// Data Protection needs the directory while services are still being registered.
    /// </summary>
    public static string Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration[$"{ReceiptCaptureOptions.SectionName}:DataDirectory"];

        return Resolve(
            string.IsNullOrWhiteSpace(configured) ? new ReceiptCaptureOptions().DataDirectory : configured,
            environment.ContentRootPath);
    }

    private static string Resolve(string configured, string contentRoot)
    {
        var directory = Path.IsPathRooted(configured)
            ? Path.GetFullPath(configured)
            : Path.GetFullPath(Path.Combine(contentRoot, configured));

        Directory.CreateDirectory(directory);
        return directory;
    }
}
