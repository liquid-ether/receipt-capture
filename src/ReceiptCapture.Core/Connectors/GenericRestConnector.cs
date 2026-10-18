using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ReceiptCapture.Core.Models;

namespace ReceiptCapture.Core.Connectors;

/// <summary>
/// Posts receipts to any HTTPS endpoint as <c>multipart/form-data</c>: a
/// <c>receipt</c> part holding the JSON fields and, optionally, an <c>image</c>
/// part holding the photo. This covers an in-house accounting system without
/// waiting for a vendor-specific connector.
/// </summary>
public sealed class GenericRestConnector : IAccountingConnector
{
    private readonly HttpClient _httpClient;
    private readonly GenericRestConnectorOptions _options;

    public GenericRestConnector(HttpClient httpClient, GenericRestConnectorOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public string Name => "generic-rest";

    public async Task<ConnectorResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!Validate(out var validationError))
        {
            return ConnectorResult.Permanent(validationError!);
        }

        var target = _options.HealthEndpoint ?? _options.Endpoint!;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            ApplyApiKey(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // A configured endpoint that answers at all proves reachability and
            // credentials; a POST-only endpoint answering 405 still counts.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed)
            {
                return ConnectorResult.Ok();
            }

            return ClassifyFailure(response.StatusCode, null);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return ConnectorResult.Transient(Describe(ex));
        }
    }

    public async Task<ConnectorResult> SubmitAsync(
        Receipt receipt,
        Stream? image,
        string? imageFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        if (!Validate(out var validationError))
        {
            return ConnectorResult.Permanent(validationError!);
        }

        try
        {
            using var content = BuildContent(receipt, image, imageFileName);
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint) { Content = content };
            ApplyApiKey(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ConnectorResult.Ok(ExtractRemoteId(body) ?? receipt.Id.ToString("D"));
            }

            return ClassifyFailure(response.StatusCode, body);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            return ConnectorResult.Transient(Describe(ex));
        }
    }

    private MultipartFormDataContent BuildContent(Receipt receipt, Stream? image, string? imageFileName)
    {
        var dto = ReceiptSubmissionDto.FromReceipt(receipt);
        var json = JsonSerializer.Serialize(dto, ReceiptJsonContext.Default.ReceiptSubmissionDto);

        var content = new MultipartFormDataContent();
        var jsonContent = new StringContent(json, Encoding.UTF8, "application/json");
        content.Add(jsonContent, "receipt");

        if (_options.IncludeImage && image is not null)
        {
            var imageContent = new StreamContent(image);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue(GuessImageMediaType(imageFileName));
            content.Add(imageContent, "image", imageFileName ?? $"{receipt.Id:D}.jpg");
        }

        return content;
    }

    private void ApplyApiKey(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return;
        }

        // "Authorization" is a typed header and has to go through the typed API.
        if (string.Equals(_options.ApiKeyHeaderName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = _options.ApiKey.Contains(' ', StringComparison.Ordinal)
                ? AuthenticationHeaderValue.Parse(_options.ApiKey)
                : new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            return;
        }

        request.Headers.TryAddWithoutValidation(_options.ApiKeyHeaderName, _options.ApiKey);
    }

    /// <summary>
    /// Rejects a misconfigured endpoint before anything leaves the device. Receipts
    /// carry financial data and the request carries an API key, so plaintext HTTP is
    /// refused outright — loopback excepted, which never leaves the machine and is
    /// what a developer points the app at while testing.
    /// </summary>
    private bool Validate(out string? error)
    {
        if (_options.Endpoint is not { } endpoint)
        {
            error = "No accounting endpoint is configured.";
            return false;
        }

        if (!endpoint.IsAbsoluteUri)
        {
            error = "The accounting endpoint must be an absolute URL.";
            return false;
        }

        if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
        {
            error = "The accounting endpoint must use HTTPS.";
            return false;
        }

        error = null;
        return true;
    }

    private static ConnectorResult ClassifyFailure(HttpStatusCode status, string? body)
    {
        var detail = Summarize(body);
        var message = detail is null
            ? $"The accounting system returned {(int)status} {status}."
            : $"The accounting system returned {(int)status} {status}: {detail}";

        // 408/429 and 5xx are worth another attempt; 4xx means the request itself is
        // wrong (bad key, rejected payload) and will fail identically every time.
        var isTransient = status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

        return isTransient ? ConnectorResult.Transient(message) : ConnectorResult.Permanent(message);
    }

    /// <summary>True for failures that are about the connection rather than the request.</summary>
    private static bool IsTransportFailure(Exception ex, CancellationToken cancellationToken) =>
        ex switch
        {
            // A cancellation the caller asked for must propagate, not be swallowed as transient.
            OperationCanceledException when cancellationToken.IsCancellationRequested => false,
            OperationCanceledException => true, // HttpClient timeout surfaces this way
            HttpRequestException => true,
            IOException => true,
            _ => false,
        };

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => "The request to the accounting system timed out.",
        _ => $"Could not reach the accounting system: {ex.Message}",
    };

    /// <summary>
    /// Pulls the created record's identifier out of the response. Systems disagree on
    /// the field name, so the common spellings are all accepted.
    /// </summary>
    private static string? ExtractRemoteId(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in RemoteIdPropertyNames)
            {
                if (!document.RootElement.TryGetProperty(name, out var property))
                {
                    continue;
                }

                var value = property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (JsonException)
        {
            // A non-JSON body is fine; the submission still succeeded.
        }

        return null;
    }

    /// <summary>Trims a server error body to something a phone screen can show.</summary>
    private static string? Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var collapsed = Text.TextNormalizer.CollapseWhitespace(body);
        return collapsed.Length <= 200 ? collapsed : collapsed[..200] + "…";
    }

    private static string GuessImageMediaType(string? fileName) =>
        Path.GetExtension(fileName)?.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".heic" => "image/heic",
            ".pdf" => "application/pdf",
            _ => "image/jpeg",
        };

    private static readonly string[] RemoteIdPropertyNames =
        ["id", "receiptId", "receipt_id", "documentId", "transactionId"];
}
