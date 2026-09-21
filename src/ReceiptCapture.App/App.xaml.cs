namespace ReceiptCapture.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell(_services));

    /// <summary>
    /// Rebuilds the shell so every page picks up a newly chosen language. The
    /// localized text is resolved when a page is created, so the pages have to be
    /// created again.
    /// </summary>
    public void ReloadForLanguageChange()
    {
        if (Windows.Count > 0)
        {
            Windows[0].Page = new AppShell(_services);
        }
    }
}
