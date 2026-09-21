using System.Globalization;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using ReceiptCapture.App.Services;
using ReceiptCapture.App.ViewModels;
using ReceiptCapture.App.Views;
using ReceiptCapture.Core.Connectors;
using ReceiptCapture.Core.Resources;
using ReceiptCapture.Core.Services;
using ReceiptCapture.Core.Storage;

namespace ReceiptCapture.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // sqlite-net needs a native provider bound before the first query. Core leaves
        // the choice to its host; here it is the SQLite iOS itself ships.
        SQLitePCL.Batteries_V2.Init();

        var builder = MauiApp.CreateBuilder();
        // No ConfigureFonts call: the app uses the iOS system font (San Francisco),
        // which is the native typography and keeps the bundle smaller.
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterServices(builder.Services);
        RegisterViews(builder.Services);

        var app = builder.Build();
        ApplyStoredSettings(app.Services);

        return app;
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, MauiSettingsStore>();
        services.AddSingleton<IAppNavigation, ShellNavigation>();
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        services.AddSingleton<IReceiptRepository>(_ =>
            new SqliteReceiptRepository(Path.Combine(FileSystem.AppDataDirectory, "receipts.db3")));

        services.AddSingleton<IReceiptImageStore>(_ =>
            new FileSystemImageStore(Path.Combine(FileSystem.AppDataDirectory, "receipt-images")));

#if IOS
        services.AddSingleton<IOcrService, Platforms.iOS.VisionOcrService>();
#else
        services.AddSingleton<IOcrService, UnavailableOcrService>();
#endif

        // One shared options instance: the Settings screen edits it in place, so a
        // changed endpoint takes effect on the next submission without a restart.
        services.AddSingleton<GenericRestConnectorOptions>();

        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });

        services.AddSingleton<IAccountingConnector>(provider => new GenericRestConnector(
            provider.GetRequiredService<HttpClient>(),
            provider.GetRequiredService<GenericRestConnectorOptions>()));

        services.AddSingleton<ReceiptSyncService>();
        services.AddSingleton<ReceiptCaptureWorkflow>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddSingleton<CaptureViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<ReviewViewModel>();

        services.AddSingleton<CapturePage>();
        services.AddSingleton<HistoryPage>();
        services.AddSingleton<SettingsPage>();
        services.AddTransient<ReviewPage>();
    }

    /// <summary>
    /// Applies the saved language and connection settings before the first screen is
    /// shown, so the app does not flash English at a French user on launch.
    /// </summary>
    private static void ApplyStoredSettings(IServiceProvider services)
    {
        var settings = services.GetRequiredService<ISettingsStore>();

        if (settings.PreferredLanguage is { Length: > 0 } language)
        {
            try
            {
                AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                // A stale preference must not stop the app launching; fall back to the device.
                settings.PreferredLanguage = null;
            }
        }

        var options = services.GetRequiredService<GenericRestConnectorOptions>();

        // Fire and forget: reading the Keychain is async, and the first submission
        // cannot happen before the user has captured something.
        _ = Task.Run(async () =>
        {
            try
            {
                await settings.LoadIntoAsync(options);
            }
            catch (Exception)
            {
                // Settings stay at their defaults; the Settings screen will show them empty.
            }
        });
    }
}
