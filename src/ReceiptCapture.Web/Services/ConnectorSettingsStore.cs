using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Web.Configuration;

namespace ReceiptCapture.Web.Services;

/// <summary>
/// Holds the live connector settings: configuration supplies the defaults, and the
/// non-secret fields can be edited from the Settings page and persisted.
/// </summary>
/// <remarks>
/// The API key is pointedly absent from everything writable here. It comes from
/// configuration alone, so it is never written to a file this app controls, never
/// returned to a browser, and never editable by whoever reaches the UI.
/// </remarks>
public sealed class ConnectorSettingsStore
{
    private readonly GenericRestConnectorOptions _live;
    private readonly ReceiptCaptureOptions _options;
    private readonly string _settingsPath;
    private readonly ILogger<ConnectorSettingsStore> _logger;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ConnectorSettingsStore(
        GenericRestConnectorOptions live,
        IOptions<ReceiptCaptureOptions> options,
        StoragePaths paths,
        ILogger<ConnectorSettingsStore> logger)
    {
        _live = live;
        _options = options.Value;
        _settingsPath = Path.Combine(paths.DataDirectory, "connector-settings.json");
        _logger = logger;
    }

    /// <summary>Applies configuration, then any saved overrides, to the live options.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var accounting = _options.Accounting;

        _live.Endpoint = ParseUri(accounting.Endpoint);
        _live.HealthEndpoint = ParseUri(accounting.HealthEndpoint);
        _live.ApiKeyHeaderName = accounting.ApiKeyHeaderName;
        _live.IncludeImage = accounting.IncludeImage;

        // The key only ever comes from configuration.
        _live.ApiKey = accounting.ApiKey;

        var saved = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
        if (saved is null)
        {
            return;
        }

        if (ParseUri(saved.Endpoint) is { } endpoint)
        {
            _live.Endpoint = endpoint;
        }

        if (ParseUri(saved.HealthEndpoint) is { } health)
        {
            _live.HealthEndpoint = health;
        }

        if (!string.IsNullOrWhiteSpace(saved.ApiKeyHeaderName))
        {
            _live.ApiKeyHeaderName = saved.ApiKeyHeaderName;
        }

        if (saved.IncludeImage is { } includeImage)
        {
            _live.IncludeImage = includeImage;
        }
    }

    /// <summary>Applies and persists an edit from the Settings page.</summary>
    public async Task SaveAsync(PersistedConnectorSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _live.Endpoint = ParseUri(settings.Endpoint);
            _live.HealthEndpoint = ParseUri(settings.HealthEndpoint);
            _live.ApiKeyHeaderName = string.IsNullOrWhiteSpace(settings.ApiKeyHeaderName)
                ? "X-API-Key"
                : settings.ApiKeyHeaderName.Trim();
            _live.IncludeImage = settings.IncludeImage ?? true;

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

            await using var file = File.Create(_settingsPath);
            await JsonSerializer
                .SerializeAsync(file, settings, WebJsonContext.Default.PersistedConnectorSettings, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>True when a credential is configured, without revealing it.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(_live.ApiKey);

    private async Task<PersistedConnectorSettings?> ReadFileAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return null;
        }

        try
        {
            await using var file = File.OpenRead(_settingsPath);
            return await JsonSerializer
                .DeserializeAsync(file, WebJsonContext.Default.PersistedConnectorSettings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A corrupt overrides file must not stop the app starting; configuration wins.
            _logger.LogWarning(ex, "Ignoring unreadable connector settings at {Path}.", _settingsPath);
            return null;
        }
    }

    private static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ? uri : null;
}

/// <summary>The editable, non-secret part of the connector configuration.</summary>
public sealed class PersistedConnectorSettings
{
    public string? Endpoint { get; set; }

    public string? HealthEndpoint { get; set; }

    public string? ApiKeyHeaderName { get; set; }

    public bool? IncludeImage { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(PersistedConnectorSettings))]
internal sealed partial class WebJsonContext : JsonSerializerContext;
