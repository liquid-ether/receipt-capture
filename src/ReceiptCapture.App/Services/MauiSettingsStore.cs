using ReceiptCapture.Core.Connectors;

namespace ReceiptCapture.App.Services;

/// <inheritdoc cref="ISettingsStore"/>
public sealed class MauiSettingsStore : ISettingsStore
{
    private const string EndpointKey = "accounting.endpoint";
    private const string HealthEndpointKey = "accounting.healthEndpoint";
    private const string ApiKeyHeaderKey = "accounting.apiKeyHeader";
    private const string IncludeImageKey = "accounting.includeImage";
    private const string LanguageKey = "app.language";

    /// <summary>Keychain entry, deliberately not a preference.</summary>
    private const string ApiKeySecureKey = "accounting.apiKey";

    public string? PreferredLanguage
    {
        get => Preferences.Default.Get<string?>(LanguageKey, null);
        set
        {
            if (value is null)
            {
                Preferences.Default.Remove(LanguageKey);
            }
            else
            {
                Preferences.Default.Set(LanguageKey, value);
            }
        }
    }

    public async Task LoadIntoAsync(GenericRestConnectorOptions options)
    {
        options.Endpoint = ReadUri(EndpointKey);
        options.HealthEndpoint = ReadUri(HealthEndpointKey);
        options.ApiKeyHeaderName = Preferences.Default.Get(ApiKeyHeaderKey, "X-API-Key");
        options.IncludeImage = Preferences.Default.Get(IncludeImageKey, true);

        try
        {
            options.ApiKey = await SecureStorage.Default.GetAsync(ApiKeySecureKey);
        }
        catch (Exception)
        {
            // The Keychain is unavailable before first unlock. Leaving the key null
            // makes the next submission fail and retry rather than crash on launch.
            options.ApiKey = null;
        }
    }

    public async Task SaveAsync(GenericRestConnectorOptions options)
    {
        WriteUri(EndpointKey, options.Endpoint);
        WriteUri(HealthEndpointKey, options.HealthEndpoint);
        Preferences.Default.Set(ApiKeyHeaderKey, options.ApiKeyHeaderName);
        Preferences.Default.Set(IncludeImageKey, options.IncludeImage);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            SecureStorage.Default.Remove(ApiKeySecureKey);
        }
        else
        {
            await SecureStorage.Default.SetAsync(ApiKeySecureKey, options.ApiKey);
        }
    }

    private static Uri? ReadUri(string key)
    {
        var raw = Preferences.Default.Get<string?>(key, null);
        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
    }

    private static void WriteUri(string key, Uri? value)
    {
        if (value is null)
        {
            Preferences.Default.Remove(key);
        }
        else
        {
            Preferences.Default.Set(key, value.ToString());
        }
    }
}
