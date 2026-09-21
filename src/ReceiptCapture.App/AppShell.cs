using ReceiptCapture.App.Views;
using ReceiptCapture.Core.Resources;

namespace ReceiptCapture.App;

/// <summary>
/// The tab bar. Built in code rather than XAML so every page comes out of the DI
/// container, and so the tab titles can be localized at construction time.
/// </summary>
public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        var tabs = new TabBar
        {
            Items =
            {
                Tab<CapturePage>(services, AppStrings.Get("Tab_Capture"), "capture"),
                Tab<HistoryPage>(services, AppStrings.Get("Tab_History"), "history"),
                Tab<SettingsPage>(services, AppStrings.Get("Tab_Settings"), "settings"),
            },
        };

        Items.Add(tabs);
    }

    private static ShellContent Tab<TPage>(IServiceProvider services, string title, string route)
        where TPage : Page
    {
        var content = new ShellContent
        {
            Title = title,
            Route = route,
        };

        // ContentTemplate defers creation until the tab is first shown, while still
        // resolving the page — and its view model — through the container.
        content.ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>());

        return content;
    }
}
