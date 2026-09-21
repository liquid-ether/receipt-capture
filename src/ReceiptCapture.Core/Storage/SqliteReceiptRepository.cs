using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Services;
using SQLite;

namespace ReceiptCapture.Core.Storage;

/// <summary>
/// SQLite-backed <see cref="IReceiptRepository"/>.
/// </summary>
/// <remarks>
/// The host application must initialise a SQLitePCLRaw provider — normally
/// <c>SQLitePCL.Batteries_V2.Init()</c> — once at startup before any call here.
/// Core deliberately does not pick one: the iOS head binds to the system SQLite
/// that Apple patches, while tests bind to a bundled build.
/// </remarks>
public sealed class SqliteReceiptRepository : IReceiptRepository, IAsyncDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    /// <param name="databasePath">Absolute path to the database file.</param>
    public SqliteReceiptRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connection = new SQLiteAsyncConnection(
            databasePath,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await _connection.CreateTableAsync<ReceiptRow>().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task SaveAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _connection.InsertOrReplaceAsync(ReceiptRow.FromReceipt(receipt)).ConfigureAwait(false);
    }

    public async Task<Receipt?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var key = id.ToString("D");
        var row = await _connection.Table<ReceiptRow>()
            .Where(r => r.Id == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return row?.ToReceipt();
    }

    public async Task<IReadOnlyList<Receipt>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var rows = await _connection.Table<ReceiptRow>()
            .OrderByDescending(r => r.CapturedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        return [.. rows.Select(r => r.ToReceipt())];
    }

    public async Task<IReadOnlyList<Receipt>> GetDueForSyncAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var pending = (int)ReceiptStatus.Pending;
        var rows = await _connection.Table<ReceiptRow>()
            .Where(r => r.Status == pending)
            .OrderBy(r => r.CapturedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        // The backoff filter is applied here rather than in SQL: the queue holds tens
        // of rows, and comparing instants in C# avoids relying on text comparison
        // semantics for the timestamp column.
        return
        [
            .. rows
                .Select(r => r.ToReceipt())
                .Where(r => r.NextAttemptUtc is null || r.NextAttemptUtc <= asOf)
        ];
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var key = id.ToString("D");
        await _connection.Table<ReceiptRow>().DeleteAsync(r => r.Id == key).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.CloseAsync().ConfigureAwait(false);
        _initGate.Dispose();
    }
}
