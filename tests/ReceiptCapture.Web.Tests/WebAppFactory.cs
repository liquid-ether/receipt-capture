using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Web.Tests;

/// <summary>
/// Hosts the real application over a throwaway data directory, so tests exercise
/// the actual startup path, middleware order and endpoint wiring rather than a
/// hand-assembled approximation of them.
/// </summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDirectory =
        Path.Combine(Path.GetTempPath(), $"rc-web-{Guid.NewGuid():N}");

    /// <summary>Set before the first request to protect the instance with a code.</summary>
    public string? AccessCode { get; init; }

    /// <summary>Set before the first request to give the connector a credential.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Stands in for the accounting system.</summary>
    public RecordingConnector Connector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ReceiptCapture:DataDirectory", _dataDirectory);
        builder.UseSetting("ReceiptCapture:Accounting:Endpoint", "https://books.example.test/api/receipts");

        if (AccessCode is not null)
        {
            builder.UseSetting("ReceiptCapture:AccessCode", AccessCode);
        }

        if (ApiKey is not null)
        {
            builder.UseSetting("ReceiptCapture:Accounting:ApiKey", ApiKey);
        }

        builder.ConfigureTestServices(services =>
        {
            // No test should reach a real accounting system.
            services.RemoveAll<IAccountingConnector>();
            services.AddSingleton<IAccountingConnector>(Connector);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The SQLite file may still be held briefly; a temp directory left
                // behind is not worth failing a test run over.
            }
        }
    }
}

/// <summary>Records what the app tried to submit, and answers however a test wants.</summary>
public sealed class RecordingConnector : IAccountingConnector
{
    public string Name => "recording";

    public List<Receipt> Submitted { get; } = [];

    public bool SawImage { get; private set; }

    public ConnectorResult NextResult { get; set; } = ConnectorResult.Ok("TXN-TEST");

    public ConnectorResult NextTestResult { get; set; } = ConnectorResult.Ok();

    public Task<ConnectorResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NextTestResult);

    public Task<ConnectorResult> SubmitAsync(
        Receipt receipt,
        Stream? image,
        string? imageFileName,
        CancellationToken cancellationToken = default)
    {
        Submitted.Add(receipt);
        SawImage |= image is not null;

        return Task.FromResult(NextResult);
    }
}
