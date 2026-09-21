using System.Globalization;
using System.Runtime.CompilerServices;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Core.Tests;

/// <summary>
/// Binds a native SQLite before any test touches the repository. Core leaves the
/// choice of provider to its host precisely so the shipping app can use Apple's.
/// </summary>
internal static class SqliteTestInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => SQLitePCL.Batteries_V2.Init();
}

/// <summary>Temporarily switches the ambient culture, restoring it on dispose.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture;
    private readonly CultureInfo _uiCulture;

    public CultureScope(string name)
    {
        _culture = CultureInfo.CurrentCulture;
        _uiCulture = CultureInfo.CurrentUICulture;

        var target = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = target;
        CultureInfo.CurrentUICulture = target;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}

/// <summary>A clock the tests move by hand, so backoff can be asserted without waiting.</summary>
internal sealed class TestTimeProvider : TimeProvider
{
    public TestTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class InMemoryReceiptRepository : IReceiptRepository
{
    private readonly Dictionary<Guid, Receipt> _receipts = [];

    public int SaveCount { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        _receipts[receipt.Id] = receipt;
        return Task.CompletedTask;
    }

    public Task<Receipt?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_receipts.GetValueOrDefault(id));

    public Task<IReadOnlyList<Receipt>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Receipt>>([.. _receipts.Values]);

    public Task<IReadOnlyList<Receipt>> GetDueForSyncAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Receipt>>(
        [
            .. _receipts.Values
                .Where(r => r.Status == ReceiptStatus.Pending)
                .Where(r => r.NextAttemptUtc is null || r.NextAttemptUtc <= asOf)
                .OrderBy(r => r.CapturedAtUtc)
        ]);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _receipts.Remove(id);
        return Task.CompletedTask;
    }
}

/// <summary>A connector that returns whatever the test queued, in order.</summary>
internal sealed class FakeConnector : IAccountingConnector
{
    private readonly Queue<ConnectorResult> _results = new();
    private ConnectorResult _last = ConnectorResult.Ok();

    public string Name => "fake";

    public int SubmitCount { get; private set; }

    public List<bool> ReceivedImage { get; } = [];

    public Exception? ThrowOnSubmit { get; set; }

    public FakeConnector Returns(params ConnectorResult[] results)
    {
        foreach (var result in results)
        {
            _results.Enqueue(result);
        }

        return this;
    }

    public Task<ConnectorResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ConnectorResult.Ok());

    public Task<ConnectorResult> SubmitAsync(
        Receipt receipt,
        Stream? image,
        string? imageFileName,
        CancellationToken cancellationToken = default)
    {
        SubmitCount++;
        ReceivedImage.Add(image is not null);

        if (ThrowOnSubmit is { } ex)
        {
            throw ex;
        }

        // Once the queued results run out the fake keeps answering the way it
        // answered last, so a test of repeated retries does not have to queue one
        // result per attempt.
        if (_results.Count > 0)
        {
            _last = _results.Dequeue();
        }

        return Task.FromResult(_last);
    }
}

/// <summary>An image store that hands back bytes the test supplied, with no filesystem.</summary>
internal sealed class InMemoryImageStore : IReceiptImageStore
{
    private readonly Dictionary<string, byte[]> _files = [];

    public Task<string> SaveAsync(
        Guid receiptId,
        Stream source,
        string fileExtension,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);

        var path = $"/images/{receiptId:D}{fileExtension}";
        _files[path] = buffer.ToArray();
        return Task.FromResult(path);
    }

    public void Add(string path, byte[] content) => _files[path] = content;

    public Task<Stream?> OpenAsync(string imagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(
            _files.TryGetValue(imagePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        _files.Remove(imagePath);
        return Task.CompletedTask;
    }
}

/// <summary>Captures outgoing requests and replies with a canned response.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public StubHttpMessageHandler(Exception throwThis) =>
        _responder = _ => throw throwThis;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);

        // Read the body now: the connector disposes the content once it returns.
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        return _responder(request);
    }
}
