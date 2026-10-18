using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Services;
using ReceiptCapture.Core.Storage;
using ReceiptCapture.Web.Api;
using ReceiptCapture.Web.Configuration;
using ReceiptCapture.Web.Ocr;
using ReceiptCapture.Web.Security;
using ReceiptCapture.Web.Services;

// sqlite-net needs a native provider bound before the first query. Core leaves the
// choice to its host; here it is the SQLite the operating system ships and patches.
SQLitePCL.Batteries_V2.Init();

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ReceiptCaptureOptions>()
    .Bind(builder.Configuration.GetSection(ReceiptCaptureOptions.SectionName));

builder.Services
    .AddOptions<TesseractOcrOptions>()
    .Bind(builder.Configuration.GetSection(TesseractOcrOptions.SectionName));

var maxUpload = builder.Configuration
    .GetSection(ReceiptCaptureOptions.SectionName)
    .Get<ReceiptCaptureOptions>()?.MaxUploadBytes ?? new ReceiptCaptureOptions().MaxUploadBytes;

// Enforced by the server as well as checked in the endpoint, so an oversized body is
// rejected before it is buffered rather than after.
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUpload);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUpload);

builder.Services.AddSingleton<StoragePaths>();
builder.Services.AddSingleton<AccessGate>();

// The access cookie is signed with a Data Protection key. Persisting the key ring
// beside the receipts means a restart does not sign everyone out; without this it is
// regenerated in memory on every boot.
builder.Services
    .AddDataProtection()
    .SetApplicationName("ReceiptCapture")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(StoragePaths.Resolve(builder.Configuration, builder.Environment), "keys")));

builder.Services.AddSingleton<IReceiptRepository>(provider =>
    new SqliteReceiptRepository(provider.GetRequiredService<StoragePaths>().DatabasePath));

builder.Services.AddSingleton<IReceiptImageStore>(provider =>
    new FileSystemImageStore(provider.GetRequiredService<StoragePaths>().ImageDirectory));

builder.Services.AddSingleton<IOcrService, TesseractOcrService>();

// One shared options instance: the Settings page edits it in place, so a changed
// endpoint takes effect on the next submission without restarting the server.
builder.Services.AddSingleton<GenericRestConnectorOptions>();
builder.Services.AddSingleton<ConnectorSettingsStore>();

builder.Services.AddHttpClient<IAccountingConnector, GenericRestConnector>(client =>
        client.Timeout = TimeSpan.FromSeconds(60));

builder.Services.AddSingleton<ReceiptSyncService>();
builder.Services.AddSingleton<ReceiptCaptureWorkflow>();
builder.Services.AddHostedService<SyncBackgroundService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("access-code", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
        }));
});

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("fr") };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // ?lang=fr wins over the browser's Accept-Language, so someone on an English
    // phone can still read the app in French.
    options.RequestCultureProviders =
    [
        new QueryStringRequestCultureProvider { QueryStringKey = "lang", UIQueryStringKey = "lang" },
        new CookieRequestCultureProvider(),
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});

var app = builder.Build();

app.UseRequestLocalization();
app.UseRateLimiter();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<AccessGateMiddleware>();

app.MapReceiptCaptureApi();

await StartupAsync(app);

app.Run();

static async Task StartupAsync(WebApplication app)
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    await app.Services.GetRequiredService<IReceiptRepository>().InitializeAsync();
    await app.Services.GetRequiredService<ConnectorSettingsStore>().LoadAsync();

    var options = app.Services.GetRequiredService<IOptions<ReceiptCaptureOptions>>().Value;

    if (string.IsNullOrWhiteSpace(options.AccessCode))
    {
        logger.LogWarning(
            "No ReceiptCapture:AccessCode is set, so anyone who can reach this server can " +
            "capture and submit receipts. Set one before exposing it beyond a private network.");
    }

    if (string.IsNullOrWhiteSpace(options.Accounting.ApiKey))
    {
        logger.LogInformation(
            "No accounting API key is configured. Set ReceiptCapture__Accounting__ApiKey if your " +
            "accounting system requires one; receipts will queue locally until it is.");
    }
}

/// <summary>Named so the test project can host the app with WebApplicationFactory.</summary>
public partial class Program;
