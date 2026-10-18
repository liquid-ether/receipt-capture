namespace ReceiptCapture.Web.Api;

/// <summary>A receipt as the review form needs it.</summary>
public sealed record ReceiptDraftResponse(
    Guid Id,
    string? Merchant,
    string? TransactionDate,
    string Currency,
    decimal? Subtotal,
    decimal? Tip,
    decimal? Total,
    IReadOnlyList<TaxLineDto> Taxes,
    string? Category,
    string? Notes,
    string? ImageUrl,
    IReadOnlyList<string> Warnings,
    string Confidence,
    string? OcrError);

public sealed record TaxLineDto(string Code, decimal Amount, decimal? Rate);

/// <summary>The reviewed values the user is happy to send.</summary>
public sealed record ConfirmReceiptRequest(
    Guid Id,
    string? Merchant,
    string? TransactionDate,
    string? Currency,
    decimal? Subtotal,
    decimal? Tip,
    decimal? Total,
    IReadOnlyList<TaxLineDto>? Taxes,
    string? Category,
    string? Notes);

/// <summary>A row in the history list, pre-formatted in the caller's language.</summary>
public sealed record ReceiptListItemResponse(
    Guid Id,
    string Merchant,
    string DateText,
    string AmountText,
    string Status,
    string StatusText,
    string? LastError,
    bool CanRetry,
    string? ImageUrl);

public sealed record SyncSummaryResponse(int Sent, int RetryScheduled, int Failed, string Message);

/// <summary>
/// Connector settings as the Settings page sees them. There is no API key field:
/// the credential comes from server configuration and is never sent to a browser.
/// </summary>
public sealed record SettingsResponse(
    string? Endpoint,
    string? HealthEndpoint,
    string ApiKeyHeaderName,
    bool IncludeImage,
    bool HasApiKey);

public sealed record UpdateSettingsRequest(
    string? Endpoint,
    string? HealthEndpoint,
    string? ApiKeyHeaderName,
    bool? IncludeImage);

public sealed record ConnectionTestResponse(bool Success, string Message);

public sealed record SessionStatusResponse(bool AccessCodeRequired, bool Admitted);

public sealed record AccessCodeRequest(string? Code);

public sealed record CategoryDto(string Code, string Name);

/// <summary>Everything the front end needs to render itself in one language.</summary>
public sealed record BootstrapResponse(
    string Language,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyList<CategoryDto> Categories);

public sealed record ErrorResponse(string Error, string? Message = null);
