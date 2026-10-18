using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Storage;

namespace ReceiptCapture.Core.Tests;

public sealed class SqliteReceiptRepositoryTests : IAsyncLifetime
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"receipts-{Guid.NewGuid():N}.db3");

    private SqliteReceiptRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _repository = new SqliteReceiptRepository(_databasePath);
        await _repository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _repository.DisposeAsync();
        File.Delete(_databasePath);
    }

    private static Receipt SampleReceipt() => new()
    {
        MerchantName = "BOULANGERIE DU COIN",
        TransactionDate = new DateOnly(2026, 3, 14),
        Currency = "CAD",
        Subtotal = 10.50m,
        Tip = 2.00m,
        Total = 14.08m,
        Taxes = [new ReceiptTax("TPS", 0.53m, 5m), new ReceiptTax("TVQ", 1.05m, 9.975m)],
        CategoryCode = ExpenseCategories.Meals,
        Notes = "Déjeuner d'équipe",
        ImagePath = "/images/receipt.jpg",
        Status = ReceiptStatus.Pending,
        CapturedAtUtc = new DateTimeOffset(2026, 3, 14, 15, 42, 7, TimeSpan.Zero),
        AttemptCount = 2,
    };

    [Fact]
    public async Task A_receipt_survives_a_round_trip_unchanged()
    {
        var receipt = SampleReceipt();
        await _repository.SaveAsync(receipt);

        var stored = await _repository.GetAsync(receipt.Id);

        Assert.NotNull(stored);
        Assert.Equal(receipt.MerchantName, stored.MerchantName);
        Assert.Equal(receipt.TransactionDate, stored.TransactionDate);
        Assert.Equal(receipt.Currency, stored.Currency);
        Assert.Equal(receipt.Subtotal, stored.Subtotal);
        Assert.Equal(receipt.Tip, stored.Tip);
        Assert.Equal(receipt.Total, stored.Total);
        Assert.Equal(receipt.CategoryCode, stored.CategoryCode);
        Assert.Equal(receipt.Notes, stored.Notes);
        Assert.Equal(receipt.ImagePath, stored.ImagePath);
        Assert.Equal(receipt.Status, stored.Status);
        Assert.Equal(receipt.CapturedAtUtc, stored.CapturedAtUtc);
        Assert.Equal(receipt.AttemptCount, stored.AttemptCount);

        Assert.Collection(
            stored.Taxes,
            tps =>
            {
                Assert.Equal("TPS", tps.Code);
                Assert.Equal(0.53m, tps.Amount);
                Assert.Equal(5m, tps.Rate);
            },
            tvq =>
            {
                Assert.Equal("TVQ", tvq.Code);
                Assert.Equal(1.05m, tvq.Amount);
                Assert.Equal(9.975m, tvq.Rate);
            });
    }

    [Fact]
    public async Task Money_keeps_its_exact_value_rather_than_going_through_a_float()
    {
        // 9.975% QST on a large bill is precisely the case a double would round wrong.
        var receipt = SampleReceipt();
        receipt.Total = 1234567.89m;
        receipt.Taxes = [new ReceiptTax("TVQ", 0.07m, 9.975m)];

        await _repository.SaveAsync(receipt);
        var stored = await _repository.GetAsync(receipt.Id);

        Assert.Equal(1234567.89m, stored!.Total);
        Assert.Equal(0.07m, stored.Taxes[0].Amount);
    }

    [Fact]
    public async Task Saving_twice_updates_rather_than_duplicates()
    {
        var receipt = SampleReceipt();
        await _repository.SaveAsync(receipt);

        receipt.Status = ReceiptStatus.Sent;
        receipt.RemoteId = "TXN-7";
        await _repository.SaveAsync(receipt);

        var all = await _repository.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(ReceiptStatus.Sent, all[0].Status);
        Assert.Equal("TXN-7", all[0].RemoteId);
    }

    [Fact]
    public async Task Only_pending_receipts_whose_backoff_elapsed_are_due()
    {
        var now = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

        var ready = SampleReceipt();
        ready.NextAttemptUtc = null;

        var waiting = SampleReceipt();
        waiting.NextAttemptUtc = now.AddMinutes(5);

        var elapsed = SampleReceipt();
        elapsed.NextAttemptUtc = now.AddMinutes(-1);

        var alreadySent = SampleReceipt();
        alreadySent.Status = ReceiptStatus.Sent;

        foreach (var receipt in new[] { ready, waiting, elapsed, alreadySent })
        {
            await _repository.SaveAsync(receipt);
        }

        var due = await _repository.GetDueForSyncAsync(now);

        Assert.Equal(2, due.Count);
        Assert.Contains(due, r => r.Id == ready.Id);
        Assert.Contains(due, r => r.Id == elapsed.Id);
    }

    [Fact]
    public async Task GetAll_returns_the_newest_capture_first()
    {
        var older = SampleReceipt();
        older.CapturedAtUtc = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var newer = SampleReceipt();
        newer.CapturedAtUtc = new DateTimeOffset(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

        await _repository.SaveAsync(older);
        await _repository.SaveAsync(newer);

        var all = await _repository.GetAllAsync();

        Assert.Equal(newer.Id, all[0].Id);
        Assert.Equal(older.Id, all[1].Id);
    }

    [Fact]
    public async Task A_receipt_with_no_optional_values_round_trips()
    {
        var sparse = new Receipt { Total = 5m, Currency = "EUR" };

        await _repository.SaveAsync(sparse);
        var stored = await _repository.GetAsync(sparse.Id);

        Assert.NotNull(stored);
        Assert.Null(stored.MerchantName);
        Assert.Null(stored.TransactionDate);
        Assert.Null(stored.Subtotal);
        Assert.Empty(stored.Taxes);
    }

    [Fact]
    public async Task Deleting_removes_the_receipt()
    {
        var receipt = SampleReceipt();
        await _repository.SaveAsync(receipt);

        await _repository.DeleteAsync(receipt.Id);

        Assert.Null(await _repository.GetAsync(receipt.Id));
    }

    [Fact]
    public async Task An_unknown_id_returns_null_rather_than_throwing()
    {
        Assert.Null(await _repository.GetAsync(Guid.NewGuid()));
    }
}

public sealed class FileSystemImageStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"receipt-images-{Guid.NewGuid():N}");

    private readonly FileSystemImageStore _store;

    public FileSystemImageStoreTests() => _store = new FileSystemImageStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task An_image_saved_can_be_read_back()
    {
        var id = Guid.NewGuid();
        using var source = new MemoryStream("jpeg-bytes"u8.ToArray());

        var path = await _store.SaveAsync(id, source, ".jpg");

        await using var stored = await _store.OpenAsync(path);
        Assert.NotNull(stored);
        using var reader = new StreamReader(stored);
        Assert.Equal("jpeg-bytes", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData(".jpeg", ".jpg")]
    [InlineData("png", ".png")]
    [InlineData(".HEIC", ".heic")]
    [InlineData("../../etc/passwd", ".jpg")]
    [InlineData("", ".jpg")]
    public async Task The_stored_extension_is_confined_to_a_known_safe_set(string given, string expected)
    {
        var id = Guid.NewGuid();
        using var source = new MemoryStream([1, 2, 3]);

        var path = await _store.SaveAsync(id, source, given);

        Assert.Equal(expected, Path.GetExtension(path));
        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(path));
    }

    [Fact]
    public async Task A_path_outside_the_store_is_refused()
    {
        // Paths come back out of the database; a tampered row must not reach
        // arbitrary files elsewhere on the device.
        var outsider = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outsider, "secret");

        try
        {
            Assert.Null(await _store.OpenAsync(outsider));

            await _store.DeleteAsync(outsider);
            Assert.True(File.Exists(outsider), "a file outside the store must not be deleted");
        }
        finally
        {
            File.Delete(outsider);
        }
    }

    [Fact]
    public async Task Opening_a_missing_image_returns_null()
    {
        Assert.Null(await _store.OpenAsync(Path.Combine(_root, "nope.jpg")));
    }

    [Fact]
    public async Task Deleting_removes_the_file()
    {
        var id = Guid.NewGuid();
        using var source = new MemoryStream([1]);
        var path = await _store.SaveAsync(id, source, ".jpg");

        await _store.DeleteAsync(path);

        Assert.False(File.Exists(path));
    }
}
