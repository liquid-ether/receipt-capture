using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReceiptCapture.App.Services;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Resources;

namespace ReceiptCapture.App.ViewModels;

/// <summary>Connection settings and the language override.</summary>
public sealed partial class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsStore _settings;
    private readonly GenericRestConnectorOptions _options;
    private readonly IAccountingConnector _connector;
    private readonly IAppNavigation _navigation;

    /// <summary>Index 0 follows the device; the rest map to <see cref="LanguageCodes"/>.</summary>
    private static readonly string?[] LanguageCodes = [null, "en", "fr"];

    [ObservableProperty]
    private string? _endpoint;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string _apiKeyHeaderName = "X-API-Key";

    [ObservableProperty]
    private bool _includeImage = true;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    public SettingsViewModel(
        ISettingsStore settings,
        GenericRestConnectorOptions options,
        IAccountingConnector connector,
        IAppNavigation navigation)
    {
        _settings = settings;
        _options = options;
        _connector = connector;
        _navigation = navigation;
    }

    /// <summary>Always offered in both languages so a lost user can find their way back.</summary>
    public IReadOnlyList<string> LanguageNames { get; } = ["System / Système", "English", "Français"];

    public string KeychainNote => AppStrings.Get("Settings_KeychainNote");

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _settings.LoadIntoAsync(_options);

        Endpoint = _options.Endpoint?.ToString();
        ApiKey = _options.ApiKey;
        ApiKeyHeaderName = _options.ApiKeyHeaderName;
        IncludeImage = _options.IncludeImage;
        SelectedLanguageIndex = Math.Max(0, Array.IndexOf(LanguageCodes, _settings.PreferredLanguage));
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SaveAsync()
    {
        ClearMessages();

        if (!TryApplyEndpoint(out var error))
        {
            ErrorMessage = error;
            return;
        }

        _options.ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
        _options.ApiKeyHeaderName = string.IsNullOrWhiteSpace(ApiKeyHeaderName)
            ? "X-API-Key"
            : ApiKeyHeaderName.Trim();
        _options.IncludeImage = IncludeImage;

        IsBusy = true;
        try
        {
            await _settings.SaveAsync(_options);
            StatusMessage = AppStrings.Get("Settings_Saved");

            ApplyLanguageSelection();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task TestConnectionAsync()
    {
        ClearMessages();

        if (!TryApplyEndpoint(out var error))
        {
            ErrorMessage = error;
            return;
        }

        _options.ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();

        IsBusy = true;
        try
        {
            var result = await _connector.TestConnectionAsync();
            if (result.Success)
            {
                StatusMessage = AppStrings.Get("Settings_ConnectionOk");
            }
            else
            {
                ErrorMessage = AppStrings.Format("Settings_ConnectionFailed", result.ErrorMessage ?? string.Empty);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryApplyEndpoint(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            error = AppStrings.Get("Validation_EndpointRequired");
            return false;
        }

        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri))
        {
            error = AppStrings.Get("Validation_EndpointRequired");
            return false;
        }

        // Mirrors the connector's own rule, so the user finds out while they are
        // looking at the field rather than when a receipt fails to send.
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
        {
            error = AppStrings.Get("Validation_EndpointHttps");
            return false;
        }

        _options.Endpoint = uri;
        error = null;
        return true;
    }

    /// <summary>
    /// Applies a language change and rebuilds the shell, since the localized text on
    /// each page is resolved when that page is created.
    /// </summary>
    private void ApplyLanguageSelection()
    {
        var code = SelectedLanguageIndex >= 0 && SelectedLanguageIndex < LanguageCodes.Length
            ? LanguageCodes[SelectedLanguageIndex]
            : null;

        if (code == _settings.PreferredLanguage)
        {
            return;
        }

        _settings.PreferredLanguage = code;
        AppStrings.OverrideCulture = code is null ? null : CultureInfo.GetCultureInfo(code);

        _navigation.ReloadForLanguageChange();
    }
}
