using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Parsing;
using ReceiptCapture.Core.Resources;
using ReceiptCapture.Core.Services;
using ReceiptCapture.Web.Configuration;
using ReceiptCapture.Web.Security;
using ReceiptCapture.Web.Services;

namespace ReceiptCapture.Web.Api;

/// <summary>The JSON API behind the single-page front end.</summary>
public static class ApiEndpoints
{
    /// <summary>Image types a phone camera or photo library actually produces.</summary>
    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/heic", "image/heif", "image/webp",
    };

    public static void MapReceiptCaptureApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        MapSession(api);
        MapBootstrap(api);
        MapReceipts(api);
        MapSettings(api);

        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    }

    private static void MapSession(IEndpointRouteBuilder api)
    {
        api.MapGet("/session/status", (HttpContext context, AccessGate gate) =>
            Results.Ok(new SessionStatusResponse(gate.IsRequired, gate.IsAdmitted(context.Request))));

        api.MapPost("/session", (HttpContext context, AccessCodeRequest request, AccessGate gate) =>
        {
            if (!gate.IsCodeValid(request.Code))
            {
                return Results.Json(
                    new ErrorResponse("invalid_access_code"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            gate.Grant(context.Response);
            return Results.Ok(new SessionStatusResponse(gate.IsRequired, Admitted: true));
        })
        // Throttled: a shared code is short enough to be worth guessing at speed.
        .RequireRateLimiting("access-code");

        api.MapDelete("/session", (HttpContext context, AccessGate gate) =>
        {
            gate.Revoke(context.Response);
            return Results.NoContent();
        });
    }

    private static void MapBootstrap(IEndpointRouteBuilder api)
    {
        api.MapGet("/bootstrap", () =>
        {
            var culture = CultureInfo.CurrentUICulture;

            return Results.Ok(new BootstrapResponse(
                culture.TwoLetterISOLanguageName,
                AppStrings.GetAll(culture),
                [.. ExpenseCategories.All.Select(code =>
                    new CategoryDto(code, AppStrings.ForCategory(code, culture)))]));
        });
    }

    private static void MapReceipts(IEndpointRouteBuilder api)
    {
        var receipts = api.MapGroup("/receipts");

        receipts.MapPost("/capture", CaptureAsync)
            .DisableAntiforgery();

        receipts.MapPost("/{id:guid}/confirm", ConfirmAsync);

        receipts.MapGet("/", async (IReceiptRepository repository, CancellationToken cancellationToken) =>
        {
            var culture = CultureInfo.CurrentUICulture;
            var all = await repository.GetAllAsync(cancellationToken);

            return Results.Ok(all.Select(r => ReceiptMapping.ToListItem(r, culture)));
        });

        receipts.MapGet("/{id:guid}/image", GetImageAsync);

        receipts.MapPost("/{id:guid}/retry", async (
            Guid id,
            IReceiptRepository repository,
            ReceiptSyncService sync,
            CancellationToken cancellationToken) =>
        {
            var receipt = await repository.GetAsync(id, cancellationToken);
            if (receipt is null)
            {
                return Results.NotFound(new ErrorResponse("not_found"));
            }

            receipt.Status = ReceiptStatus.Pending;
            receipt.AttemptCount = 0;
            receipt.NextAttemptUtc = null;
            receipt.LastError = null;

            await repository.SaveAsync(receipt, cancellationToken);
            await sync.SyncPendingAsync(cancellationToken);

            return Results.NoContent();
        });

        receipts.MapDelete("/{id:guid}", async (
            Guid id,
            ReceiptCaptureWorkflow workflow,
            CancellationToken cancellationToken) =>
        {
            await workflow.DiscardAsync(id, cancellationToken);
            return Results.NoContent();
        });

        api.MapPost("/sync", async (ReceiptSyncService sync, CancellationToken cancellationToken) =>
        {
            var summary = await sync.SyncPendingAsync(cancellationToken);
            var culture = CultureInfo.CurrentUICulture;

            return Results.Ok(new SyncSummaryResponse(
                summary.Sent,
                summary.RetryScheduled,
                summary.Failed,
                AppStrings.Format(
                    "History_SyncResult",
                    culture,
                    summary.Sent,
                    summary.RetryScheduled,
                    summary.Failed)));
        });
    }

    private static async Task<IResult> CaptureAsync(
        [FromForm] IFormFile? photo,
        ReceiptCaptureWorkflow workflow,
        IOptions<ReceiptCaptureOptions> options,
        CancellationToken cancellationToken)
    {
        if (photo is null || photo.Length == 0)
        {
            return Results.BadRequest(new ErrorResponse("no_photo"));
        }

        if (photo.Length > options.Value.MaxUploadBytes)
        {
            return Results.Json(
                new ErrorResponse("photo_too_large"),
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!AllowedImageTypes.Contains(photo.ContentType ?? string.Empty))
        {
            return Results.BadRequest(new ErrorResponse("unsupported_image_type", photo.ContentType));
        }

        await using var stream = photo.OpenReadStream();

        // The extension is derived from the declared content type rather than the
        // uploaded file name, which is attacker-controlled and never touches disk.
        var outcome = await workflow
            .CaptureAsync(stream, ExtensionFor(photo.ContentType), cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(ReceiptMapping.ToDraftResponse(outcome, CultureInfo.CurrentUICulture));
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id,
        ConfirmReceiptRequest request,
        IReceiptRepository repository,
        ReceiptCaptureWorkflow workflow,
        CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(id, cancellationToken);
        if (receipt is null)
        {
            return Results.NotFound(new ErrorResponse("not_found"));
        }

        var culture = CultureInfo.CurrentUICulture;

        if (request.Total is not { } total || total <= 0m)
        {
            return Results.BadRequest(new ErrorResponse(
                "invalid_total",
                AppStrings.Get("Validation_TotalPositive", culture)));
        }

        receipt.MerchantName = Trimmed(request.Merchant);
        receipt.TransactionDate = ParseDate(request.TransactionDate);
        receipt.Currency = NormalizeCurrency(request.Currency);
        receipt.Subtotal = request.Subtotal;
        receipt.Tip = request.Tip;
        receipt.Total = total;
        receipt.Notes = Trimmed(request.Notes);
        receipt.CategoryCode = ExpenseCategories.All.Contains(request.Category) ? request.Category : null;

        receipt.Taxes =
        [
            .. (request.Taxes ?? [])
                .Where(t => !string.IsNullOrWhiteSpace(t.Code))
                .Select(t => new ReceiptTax(t.Code.Trim().ToUpperInvariant(), t.Amount, t.Rate))
        ];

        await workflow.ConfirmAsync(receipt, cancellationToken);

        return Results.Ok(new { queued = true, message = AppStrings.Get("Review_Queued", culture) });
    }

    private static async Task<IResult> GetImageAsync(
        Guid id,
        IReceiptRepository repository,
        IReceiptImageStore images,
        CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(id, cancellationToken);
        if (receipt?.ImagePath is not { Length: > 0 } path)
        {
            return Results.NotFound();
        }

        // The path comes from the database and is re-checked by the store, which
        // confines reads to its own directory.
        var stream = await images.OpenAsync(path, cancellationToken);
        if (stream is null)
        {
            return Results.NotFound();
        }

        return Results.Stream(stream, ContentTypeFor(path));
    }

    private static void MapSettings(IEndpointRouteBuilder api)
    {
        var settings = api.MapGroup("/settings");

        settings.MapGet("/", (GenericRestConnectorOptions options, ConnectorSettingsStore store) =>
            Results.Ok(new SettingsResponse(
                options.Endpoint?.ToString(),
                options.HealthEndpoint?.ToString(),
                options.ApiKeyHeaderName,
                options.IncludeImage,
                store.HasApiKey)));

        settings.MapPut("/", async (
            UpdateSettingsRequest request,
            GenericRestConnectorOptions options,
            ConnectorSettingsStore store,
            CancellationToken cancellationToken) =>
        {
            var culture = CultureInfo.CurrentUICulture;

            if (string.IsNullOrWhiteSpace(request.Endpoint)
                || !Uri.TryCreate(request.Endpoint.Trim(), UriKind.Absolute, out var endpoint))
            {
                return Results.BadRequest(new ErrorResponse(
                    "invalid_endpoint",
                    AppStrings.Get("Validation_EndpointRequired", culture)));
            }

            // Mirrors the connector's own rule so the user is told while they are
            // looking at the field, not when a receipt later fails to send.
            if (endpoint.Scheme != Uri.UriSchemeHttps && !endpoint.IsLoopback)
            {
                return Results.BadRequest(new ErrorResponse(
                    "insecure_endpoint",
                    AppStrings.Get("Validation_EndpointHttps", culture)));
            }

            await store.SaveAsync(
                new PersistedConnectorSettings
                {
                    Endpoint = request.Endpoint,
                    HealthEndpoint = request.HealthEndpoint,
                    ApiKeyHeaderName = request.ApiKeyHeaderName,
                    IncludeImage = request.IncludeImage,
                },
                cancellationToken);

            return Results.Ok(new SettingsResponse(
                options.Endpoint?.ToString(),
                options.HealthEndpoint?.ToString(),
                options.ApiKeyHeaderName,
                options.IncludeImage,
                store.HasApiKey));
        });

        settings.MapPost("/test", async (IAccountingConnector connector, CancellationToken cancellationToken) =>
        {
            var culture = CultureInfo.CurrentUICulture;
            var result = await connector.TestConnectionAsync(cancellationToken);

            return Results.Ok(new ConnectionTestResponse(
                result.Success,
                result.Success
                    ? AppStrings.Get("Settings_ConnectionOk", culture)
                    : AppStrings.Format("Settings_ConnectionFailed", culture, result.ErrorMessage ?? string.Empty)));
        });
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeCurrency(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "CAD" : value.Trim().ToUpperInvariant();

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string ExtensionFor(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/heic" or "image/heif" => ".heic",
        "image/webp" => ".webp",
        _ => ".jpg",
    };

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".heic" => "image/heic",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "image/jpeg",
    };
}
