using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ReceiptCapture.Core.Connectors;

namespace ReceiptCapture.Web.Tests;

[Collection("Tesseract")]
public class ApiTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private static MultipartFormDataContent PhotoForm(string fixture, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(File.ReadAllBytes(Fixture(fixture)));
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return new MultipartFormDataContent { { content, "photo", "receipt.jpg" } };
    }

    [Fact]
    public async Task The_app_starts_and_reports_healthy()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_front_end_is_served_from_the_root()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Receipt Capture", html, StringComparison.Ordinal);
        Assert.Contains("camera-enable", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "History")]
    [InlineData("fr", "Historique")]
    public async Task Bootstrap_returns_strings_in_the_requested_language(string lang, string expected)
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>($"/api/bootstrap?lang={lang}");

        Assert.Equal(lang, document.GetProperty("language").GetString());
        Assert.Equal(expected, document.GetProperty("strings").GetProperty("Tab_History").GetString());

        // Categories arrive translated too, so the picker needs no second call.
        var categories = document.GetProperty("categories").EnumerateArray().ToList();
        Assert.NotEmpty(categories);
        Assert.All(categories, c => Assert.False(
            string.IsNullOrWhiteSpace(c.GetProperty("name").GetString())));
    }

    [Fact]
    public async Task Two_requests_in_different_languages_do_not_interfere()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        // Guards the server against the process-wide culture override that a
        // single-user client would be free to use.
        var both = await Task.WhenAll(
            client.GetFromJsonAsync<JsonElement>("/api/bootstrap?lang=fr"),
            client.GetFromJsonAsync<JsonElement>("/api/bootstrap?lang=en"));

        Assert.Equal("Historique", both[0].GetProperty("strings").GetProperty("Tab_History").GetString());
        Assert.Equal("History", both[1].GetProperty("strings").GetProperty("Tab_History").GetString());
    }

    [SkippableFact]
    public async Task A_photographed_receipt_is_read_parsed_and_returned_for_review()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var form = PhotoForm("receipt-fr.png", "image/png");
        var response = await client.PostAsync("/api/receipts/capture", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var draft = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BOULANGERIE DU COIN", draft.GetProperty("merchant").GetString());
        Assert.Equal("2026-03-14", draft.GetProperty("transactionDate").GetString());
        Assert.Equal(12.08m, draft.GetProperty("total").GetDecimal());
        Assert.Equal(2, draft.GetProperty("taxes").GetArrayLength());

        // The photo is retrievable straight away, before anything is confirmed.
        var imageUrl = draft.GetProperty("imageUrl").GetString();
        Assert.NotNull(imageUrl);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(imageUrl)).StatusCode);
    }

    [SkippableFact]
    public async Task Warnings_come_back_translated_rather_than_as_resource_keys()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        // A blank image reads as nothing, which always raises at least one warning.
        var blank = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(blank);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "photo", "blank.png");

        var draft = await (await client.PostAsync("/api/receipts/capture?lang=fr", form))
            .Content.ReadFromJsonAsync<JsonElement>();

        var warnings = draft.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()!).ToList();

        Assert.NotEmpty(warnings);
        Assert.All(warnings, w => Assert.DoesNotContain("Warning_", w, StringComparison.Ordinal));
        Assert.All(warnings, w => Assert.False(w.StartsWith('[')));
    }

    [SkippableFact]
    public async Task A_confirmed_receipt_is_submitted_and_listed_as_sent()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var form = PhotoForm("receipt-en.png", "image/png");
        var draft = await (await client.PostAsync("/api/receipts/capture", form))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetGuid();

        var confirm = await client.PostAsJsonAsync($"/api/receipts/{id}/confirm", new
        {
            id,
            merchant = "THE COFFEE HOUSE",
            transactionDate = "2026-03-14",
            currency = "CAD",
            subtotal = 11.20m,
            total = 12.66m,
            taxes = new[] { new { code = "HST", amount = 1.46m, rate = 13m } },
            category = "MEALS",
        });

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var submitted = Assert.Single(factory.Connector.Submitted);
        Assert.Equal(12.66m, submitted.Total);
        Assert.Equal("MEALS", submitted.CategoryCode);
        Assert.Equal(1.46m, submitted.TaxTotal);
        Assert.True(factory.Connector.SawImage, "the receipt photo should be attached");

        var history = await client.GetFromJsonAsync<JsonElement>("/api/receipts");
        var row = history.EnumerateArray().Single();
        Assert.Equal("Sent", row.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task A_rejected_submission_leaves_the_receipt_retryable()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        using var factory = new WebAppFactory();
        factory.Connector.NextResult = ConnectorResult.Permanent("401 Unauthorized");

        using var client = factory.CreateClient();
        using var form = PhotoForm("receipt-en.png", "image/png");

        var draft = await (await client.PostAsync("/api/receipts/capture", form))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetGuid();

        await client.PostAsJsonAsync($"/api/receipts/{id}/confirm", new { id, total = 12.66m });

        var row = (await client.GetFromJsonAsync<JsonElement>("/api/receipts")).EnumerateArray().Single();
        Assert.Equal("Failed", row.GetProperty("status").GetString());
        Assert.True(row.GetProperty("canRetry").GetBoolean());
        Assert.Equal("401 Unauthorized", row.GetProperty("lastError").GetString());

        // Retrying goes back to the connector, which now accepts it.
        factory.Connector.NextResult = ConnectorResult.Ok("TXN-2");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/receipts/{id}/retry", null)).StatusCode);

        row = (await client.GetFromJsonAsync<JsonElement>("/api/receipts")).EnumerateArray().Single();
        Assert.Equal("Sent", row.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Confirming_a_receipt_that_does_not_exist_is_a_404()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync($"/api/receipts/{id}/confirm", new { id, total = 5m });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SkippableFact]
    public async Task A_total_of_zero_is_refused_with_a_translated_message()
    {
        Skip.IfNot(TesseractProbe.IsAvailable, TesseractProbe.SkipReason);

        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var form = PhotoForm("receipt-en.png", "image/png");
        var draft = await (await client.PostAsync("/api/receipts/capture", form))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = draft.GetProperty("id").GetGuid();

        var response = await client.PostAsJsonAsync($"/api/receipts/{id}/confirm?lang=fr", new { id, total = 0m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Le total doit être supérieur à zéro.", body.GetProperty("message").GetString());
        Assert.Empty(factory.Connector.Submitted);
    }

    [Fact]
    public async Task A_capture_with_no_photo_is_refused()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("not a photo"), "notes");

        var response = await client.PostAsync("/api/receipts/capture", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_refused()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var content = new ByteArrayContent("#!/bin/sh\necho hello"u8.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-sh");

        using var form = new MultipartFormDataContent { { content, "photo", "payload.sh" } };
        var response = await client.PostAsync("/api/receipts/capture", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_a_receipt_removes_it_from_history()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        // Nothing captured, so deleting an unknown id is simply a no-op.
        var response = await client.DeleteAsync($"/api/receipts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/receipts")).EnumerateArray());
    }
}
