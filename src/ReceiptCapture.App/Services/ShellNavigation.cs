using ReceiptCapture.App.ViewModels;
using ReceiptCapture.App.Views;

namespace ReceiptCapture.App.Services;

/// <inheritdoc cref="IAppNavigation"/>
public sealed class ShellNavigation : IAppNavigation
{
    private readonly IServiceProvider _services;

    public ShellNavigation(IServiceProvider services) => _services = services;

    public async Task ShowReviewAsync(CaptureOutcome outcome)
    {
        var page = _services.GetRequiredService<ReviewPage>();
        ((ReviewViewModel)page.BindingContext).Load(outcome);

        await Shell.Current.Navigation.PushAsync(page);
    }

    public Task GoBackAsync() => Shell.Current.Navigation.PopAsync();

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.CurrentPage is { } page
            ? page.DisplayAlert(title, message, accept, cancel)
            : Task.FromResult(false);

    public void ReloadForLanguageChange()
    {
        if (Application.Current is App app)
        {
            app.ReloadForLanguageChange();
        }
    }
}
