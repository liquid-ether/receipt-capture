using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.Core.Tests;

public class ReceiptSyncServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    private static Receipt PendingReceipt(string? imagePath = null) => new()
    {
        MerchantName = "THE COFFEE HOUSE",
        Total = 12.66m,
        Currency = "CAD",
        Status = ReceiptStatus.Pending,
        CapturedAtUtc = Now.AddMinutes(-5),
        ImagePath = imagePath,
    };

    private static (ReceiptSyncService Service, InMemoryReceiptRepository Repository, TestTimeProvider Clock)
        Build(FakeConnector connector, InMemoryImageStore? images = null, ReceiptSyncOptions? options = null)
    {
        var repository = new InMemoryReceiptRepository();
        var clock = new TestTimeProvider(Now);
        var service = new ReceiptSyncService(
            repository,
            connector,
            images ?? new InMemoryImageStore(),
            clock,
            options);

        return (service, repository, clock);
    }

    [Fact]
    public async Task An_accepted_receipt_is_marked_sent_with_its_remote_id()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Ok("TXN-1"));
        var (service, repository, _) = Build(connector);

        var receipt = PendingReceipt();
        await repository.SaveAsync(receipt);

        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.Sent);
        var stored = await repository.GetAsync(receipt.Id);
        Assert.Equal(ReceiptStatus.Sent, stored!.Status);
        Assert.Equal("TXN-1", stored.RemoteId);
        Assert.Equal(Now, stored.SubmittedAtUtc);
        Assert.Null(stored.LastError);
        Assert.Null(stored.NextAttemptUtc);
    }

    [Fact]
    public async Task A_transient_failure_schedules_a_retry_instead_of_failing_the_receipt()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Transient("network down"));
        var (service, repository, _) = Build(connector);

        var receipt = PendingReceipt();
        await repository.SaveAsync(receipt);

        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.RetryScheduled);
        var stored = await repository.GetAsync(receipt.Id);
        Assert.Equal(ReceiptStatus.Pending, stored!.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(Now.AddSeconds(30), stored.NextAttemptUtc);
        Assert.Equal("network down", stored.LastError);
    }

    [Fact]
    public async Task A_receipt_waiting_out_its_backoff_is_left_alone()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Transient("network down"));
        var (service, repository, clock) = Build(connector);

        await repository.SaveAsync(PendingReceipt());
        await service.SyncPendingAsync();

        // Still inside the 30-second window: nothing should be attempted again.
        clock.Advance(TimeSpan.FromSeconds(10));
        var summary = await service.SyncPendingAsync();

        Assert.Equal(0, summary.Processed);
        Assert.Equal(1, connector.SubmitCount);

        // Past the window: it is retried.
        clock.Advance(TimeSpan.FromSeconds(25));
        await service.SyncPendingAsync();

        Assert.Equal(2, connector.SubmitCount);
    }

    [Fact]
    public async Task A_permanent_failure_stops_immediately_rather_than_retrying_forever()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Permanent("401 Unauthorized"));
        var (service, repository, _) = Build(connector);

        var receipt = PendingReceipt();
        await repository.SaveAsync(receipt);

        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.Failed);
        var stored = await repository.GetAsync(receipt.Id);
        Assert.Equal(ReceiptStatus.Failed, stored!.Status);
        Assert.Equal("401 Unauthorized", stored.LastError);
        Assert.Null(stored.NextAttemptUtc);
    }

    [Fact]
    public async Task Retrying_gives_up_once_the_attempt_limit_is_reached()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Transient("still down"));
        var options = new ReceiptSyncOptions
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.FromSeconds(1),
            MaxBackoff = TimeSpan.FromSeconds(10),
        };

        var (service, repository, clock) = Build(connector, options: options);
        var receipt = PendingReceipt();
        await repository.SaveAsync(receipt);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await service.SyncPendingAsync();
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var stored = await repository.GetAsync(receipt.Id);
        Assert.Equal(ReceiptStatus.Failed, stored!.Status);
        Assert.Equal(3, stored.AttemptCount);
    }

    [Fact]
    public void Backoff_doubles_and_then_holds_at_the_ceiling()
    {
        var connector = new FakeConnector();
        var options = new ReceiptSyncOptions
        {
            InitialBackoff = TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromHours(1),
        };
        var (service, _, _) = Build(connector, options: options);

        Assert.Equal(TimeSpan.FromSeconds(30), service.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(60), service.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(120), service.BackoffFor(3));
        Assert.Equal(TimeSpan.FromHours(1), service.BackoffFor(10));

        // A receipt that has been failing for a very long time must not overflow.
        Assert.Equal(TimeSpan.FromHours(1), service.BackoffFor(int.MaxValue));
    }

    [Fact]
    public async Task The_receipt_photo_is_handed_to_the_connector()
    {
        var images = new InMemoryImageStore();
        images.Add("/images/receipt.jpg", "jpeg"u8.ToArray());

        var connector = new FakeConnector().Returns(ConnectorResult.Ok());
        var (service, repository, _) = Build(connector, images);

        await repository.SaveAsync(PendingReceipt("/images/receipt.jpg"));
        await service.SyncPendingAsync();

        Assert.Equal([true], connector.ReceivedImage);
    }

    [Fact]
    public async Task A_missing_photo_does_not_stop_the_receipt_being_sent()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Ok());
        var (service, repository, _) = Build(connector);

        await repository.SaveAsync(PendingReceipt("/images/vanished.jpg"));
        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.Sent);
        Assert.Equal([false], connector.ReceivedImage);
    }

    [Fact]
    public async Task A_connector_that_throws_is_treated_as_a_transient_failure()
    {
        var connector = new FakeConnector { ThrowOnSubmit = new InvalidOperationException("boom") };
        var (service, repository, _) = Build(connector);

        var receipt = PendingReceipt();
        await repository.SaveAsync(receipt);

        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.RetryScheduled);
        Assert.Equal("boom", (await repository.GetAsync(receipt.Id))!.LastError);
    }

    [Fact]
    public async Task One_bad_receipt_does_not_block_the_rest_of_the_queue()
    {
        var connector = new FakeConnector()
            .Returns(ConnectorResult.Permanent("rejected"), ConnectorResult.Ok("TXN-2"));
        var (service, repository, _) = Build(connector);

        var first = PendingReceipt();
        first.CapturedAtUtc = Now.AddMinutes(-10);
        var second = PendingReceipt();
        second.CapturedAtUtc = Now.AddMinutes(-5);

        await repository.SaveAsync(first);
        await repository.SaveAsync(second);

        var summary = await service.SyncPendingAsync();

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Sent);
        Assert.Equal(ReceiptStatus.Sent, (await repository.GetAsync(second.Id))!.Status);
    }

    [Fact]
    public async Task Draft_and_sent_receipts_are_never_submitted()
    {
        var connector = new FakeConnector().Returns(ConnectorResult.Ok());
        var (service, repository, _) = Build(connector);

        var draft = PendingReceipt();
        draft.Status = ReceiptStatus.Draft;
        var sent = PendingReceipt();
        sent.Status = ReceiptStatus.Sent;

        await repository.SaveAsync(draft);
        await repository.SaveAsync(sent);

        Assert.Equal(0, (await service.SyncPendingAsync()).Processed);
        Assert.Equal(0, connector.SubmitCount);
    }

    [Fact]
    public async Task Overlapping_sync_passes_do_not_submit_the_same_receipt_twice()
    {
        var gate = new TaskCompletionSource();
        var slowConnector = new BlockingConnector(gate.Task);
        var repository = new InMemoryReceiptRepository();
        var service = new ReceiptSyncService(
            repository,
            slowConnector,
            new InMemoryImageStore(),
            new TestTimeProvider(Now));

        await repository.SaveAsync(PendingReceipt());

        var first = service.SyncPendingAsync();
        await slowConnector.Entered.Task;

        // The timer tick and a pull-to-refresh can land at the same moment.
        var second = await service.SyncPendingAsync();
        Assert.True(second.WasSkipped);

        gate.SetResult();
        Assert.Equal(1, (await first).Sent);
        Assert.Equal(1, slowConnector.SubmitCount);
    }

    private sealed class BlockingConnector : IAccountingConnector
    {
        private readonly Task _release;

        public BlockingConnector(Task release) => _release = release;

        public TaskCompletionSource Entered { get; } = new();

        public int SubmitCount { get; private set; }

        public string Name => "blocking";

        public Task<ConnectorResult> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectorResult.Ok());

        public async Task<ConnectorResult> SubmitAsync(
            Receipt receipt,
            Stream? image,
            string? imageFileName,
            CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            Entered.TrySetResult();
            await _release;
            return ConnectorResult.Ok("TXN-slow");
        }
    }
}
