using ReceiptCapture.Core.Connectors;

namespace ReceiptCapture.App.Services;

/// <summary>
/// Persists the connection settings. The API key goes to the iOS Keychain via
/// <see cref="Microsoft.Maui.Storage.SecureStorage"/>; the rest is ordinary
/// preferences. Keeping the secret out of the receipt database means a backup or a
/// database copy never carries the credential with it.
/// </summary>
public interface ISettingsStore
{
    Task LoadIntoAsync(GenericRestConnectorOptions options);

    Task SaveAsync(GenericRestConnectorOptions options);

    /// <summary>Two-letter language override ("en", "fr"), or null to follow the device.</summary>
    string? PreferredLanguage { get; set; }
}
