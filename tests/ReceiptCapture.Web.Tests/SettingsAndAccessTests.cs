using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ReceiptCapture.Core.Connectors;

namespace ReceiptCapture.Web.Tests;

public class SettingsTests
{
    [Fact]
    public async Task The_api_key_is_never_sent_to_the_browser()
    {
        const string Secret = "super-secret-key-9f2c";

        using var factory = new WebAppFactory { ApiKey = Secret };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/settings");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The page may say a key exists; it must never say what it is.
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("hasApiKey").GetBoolean());
    }

    [Fact]
    public async Task A_missing_api_key_is_reported_without_inventing_one()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");

        Assert.False(settings.GetProperty("hasApiKey").GetBoolean());
    }

    [Fact]
    public async Task The_endpoint_can_be_changed_and_survives_the_round_trip()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings", new
        {
            endpoint = "https://other.example.test/receipts",
            apiKeyHeaderName = "Authorization",
            includeImage = false,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var settings = await client.GetFromJsonAsync<JsonElement>("/api/settings");
        Assert.Equal("https://other.example.test/receipts", settings.GetProperty("endpoint").GetString());
        Assert.Equal("Authorization", settings.GetProperty("apiKeyHeaderName").GetString());
        Assert.False(settings.GetProperty("includeImage").GetBoolean());
    }

    [Fact]
    public async Task A_plaintext_endpoint_is_refused_at_the_form_rather_than_at_send_time()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings?lang=fr", new
        {
            endpoint = "http://books.example.test/receipts",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insecure_endpoint", body.GetProperty("error").GetString());
        Assert.Contains("https", body.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_endpoint_is_refused()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/settings", new { endpoint = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_connection_test_reports_what_the_connector_said()
    {
        using var factory = new WebAppFactory();
        factory.Connector.NextTestResult = ConnectorResult.Permanent("403 Forbidden");

        using var client = factory.CreateClient();

        var result = await (await client.PostAsync("/api/settings/test", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains("403", result.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }
}

public class AccessGateTests
{
    private const string Code = "let-me-in-1234";

    [Fact]
    public async Task With_no_code_configured_the_app_is_open()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient();

        var status = await client.GetFromJsonAsync<JsonElement>("/api/session/status");

        Assert.False(status.GetProperty("accessCodeRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/receipts")).StatusCode);
    }

    [Fact]
    public async Task With_a_code_configured_the_api_is_closed_until_it_is_given()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/receipts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/settings")).StatusCode);

        // The front end itself stays reachable so it can render the prompt.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task The_string_table_is_readable_before_admission_and_carries_nothing_else()
    {
        using var factory = new WebAppFactory { AccessCode = Code, ApiKey = "never-leak-this" };
        using var client = factory.CreateClient();

        // The gate needs the strings to speak French and to explain a refused code.
        var response = await client.GetAsync("/api/bootstrap?lang=fr");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            ["categories", "language", "strings"],
            document.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("Ce code n'a pas été accepté.",
            document.RootElement.GetProperty("strings").GetProperty("Session_Invalid").GetString());
        Assert.DoesNotContain("never-leak-this", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_right_code_opens_the_api_for_that_browser()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        var signIn = await client.PostAsJsonAsync("/api/session", new { code = Code });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        // HttpClient carries the cookie forward, exactly as a browser would.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/receipts")).StatusCode);
    }

    [Fact]
    public async Task The_wrong_code_is_refused()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/session", new { code = "not-the-code" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/receipts")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_closes_the_api_again()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/session", new { code = Code });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/receipts")).StatusCode);

        await client.DeleteAsync("/api/session");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/receipts")).StatusCode);
    }

    [Fact]
    public async Task A_forged_cookie_does_not_get_in()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Add("Cookie", "rc_access=obviously-not-a-protected-payload");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/receipts")).StatusCode);
    }

    [Fact]
    public async Task Guessing_the_code_is_rate_limited()
    {
        using var factory = new WebAppFactory { AccessCode = Code };
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 15; attempt++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/session", new { code = $"guess-{attempt}" })).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
