using System.Net;
using System.Text;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Tests;

public class GenericRestConnectorTests
{
    private static Receipt SampleReceipt() => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        MerchantName = "BOULANGERIE DU COIN",
        TransactionDate = new DateOnly(2026, 3, 14),
        Currency = "CAD",
        Subtotal = 10.50m,
        Total = 12.08m,
        Taxes = [new ReceiptTax("TPS", 0.53m, 5m), new ReceiptTax("TVQ", 1.05m, 9.975m)],
        CategoryCode = ExpenseCategories.Meals,
        CapturedAtUtc = new DateTimeOffset(2026, 3, 14, 15, 42, 0, TimeSpan.Zero),
    };

    private static (GenericRestConnector Connector, StubHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder,
        Action<GenericRestConnectorOptions>? configure = null)
    {
        var handler = new StubHttpMessageHandler(responder);
        var options = new GenericRestConnectorOptions
        {
            Endpoint = new Uri("https://books.example.com/api/receipts"),
            ApiKey = "secret-key",
        };

        configure?.Invoke(options);

        return (new GenericRestConnector(new HttpClient(handler), options), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task A_successful_submission_returns_the_remote_id()
    {
        var (connector, _) = Build(_ => Json(HttpStatusCode.Created, """{"id":"TXN-9001"}"""));

        var result = await connector.SubmitAsync(SampleReceipt(), image: null, imageFileName: null);

        Assert.True(result.Success);
        Assert.Equal("TXN-9001", result.RemoteId);
    }

    [Fact]
    public async Task The_receipt_is_posted_as_multipart_json_with_the_image()
    {
        var (connector, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var image = new MemoryStream("fake-jpeg-bytes"u8.ToArray());

        var result = await connector.SubmitAsync(SampleReceipt(), image, "receipt.jpg");

        Assert.True(result.Success);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);

        var body = handler.Bodies[0];
        Assert.Contains("\"merchant\":\"BOULANGERIE DU COIN\"", body, StringComparison.Ordinal);
        Assert.Contains("\"transactionDate\":\"2026-03-14\"", body, StringComparison.Ordinal);
        Assert.Contains("\"total\":12.08", body, StringComparison.Ordinal);
        Assert.Contains("\"taxTotal\":1.58", body, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"TVQ\"", body, StringComparison.Ordinal);
        Assert.Contains("fake-jpeg-bytes", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Amounts_are_serialized_invariantly_whatever_the_phone_language()
    {
        using var _ = new CultureScope("fr-CA");
        var (connector, handler) = Build(__ => new HttpResponseMessage(HttpStatusCode.OK));

        await connector.SubmitAsync(SampleReceipt(), image: null, imageFileName: null);

        // A French culture formats 12.08 as "12,08"; JSON must not follow suit.
        Assert.Contains("\"total\":12.08", handler.Bodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("\"total\":\"12,08\"", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_key_travels_in_the_configured_header()
    {
        var (connector, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            options => options.ApiKeyHeaderName = "X-Books-Key");

        await connector.SubmitAsync(SampleReceipt(), image: null, imageFileName: null);

        Assert.Equal("secret-key", handler.Requests[0].Headers.GetValues("X-Books-Key").Single());
    }

    [Fact]
    public async Task An_authorization_header_is_sent_as_a_bearer_token()
    {
        var (connector, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            options => options.ApiKeyHeaderName = "Authorization");

        await connector.SubmitAsync(SampleReceipt(), image: null, imageFileName: null);

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("secret-key", auth.Parameter);
    }

    [Fact]
    public async Task The_image_is_left_out_when_the_user_turns_it_off()
    {
        var (connector, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            options => options.IncludeImage = false);

        using var image = new MemoryStream("fake-jpeg-bytes"u8.ToArray());
        await connector.SubmitAsync(SampleReceipt(), image, "receipt.jpg");

        Assert.DoesNotContain("fake-jpeg-bytes", handler.Bodies[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_side_and_throttling_failures_are_worth_retrying(HttpStatusCode status)
    {
        var (connector, _) = Build(_ => new HttpResponseMessage(status));

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.False(result.Success);
        Assert.True(result.IsTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Request_level_failures_are_not_retried(HttpStatusCode status)
    {
        var (connector, _) = Build(_ => new HttpResponseMessage(status));

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
    }

    [Fact]
    public async Task A_dropped_connection_is_transient()
    {
        var handler = new StubHttpMessageHandler(new HttpRequestException("connection reset"));
        var connector = new GenericRestConnector(
            new HttpClient(handler),
            new GenericRestConnectorOptions { Endpoint = new Uri("https://books.example.com/api/receipts") });

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.True(result.IsTransient);
    }

    [Fact]
    public async Task A_plaintext_endpoint_is_refused_before_anything_leaves_the_device()
    {
        var (connector, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            options => options.Endpoint = new Uri("http://books.example.com/api/receipts"));

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
        Assert.Contains("HTTPS", result.ErrorMessage!, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_loopback_endpoint_is_allowed_for_local_development()
    {
        var (connector, handler) = Build(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            options => options.Endpoint = new Uri("http://localhost:5001/api/receipts"));

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.True(result.Success);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task An_unconfigured_connector_fails_without_calling_out()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var connector = new GenericRestConnector(new HttpClient(handler), new GenericRestConnectorOptions());

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_cancelled_submission_propagates_rather_than_looking_like_a_retry()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (connector, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => connector.SubmitAsync(SampleReceipt(), null, null, cts.Token));
    }

    [Fact]
    public async Task A_post_only_endpoint_still_passes_the_connection_test()
    {
        var (connector, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));

        Assert.True((await connector.TestConnectionAsync()).Success);
    }

    [Fact]
    public async Task A_rejected_key_fails_the_connection_test_permanently()
    {
        var (connector, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await connector.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.False(result.IsTransient);
    }

    [Fact]
    public async Task A_non_json_success_body_still_counts_as_accepted()
    {
        var (connector, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("OK", Encoding.UTF8, "text/plain"),
        });

        var result = await connector.SubmitAsync(SampleReceipt(), null, null);

        Assert.True(result.Success);
        Assert.Equal("11111111-2222-3333-4444-555555555555", result.RemoteId);
    }
}
