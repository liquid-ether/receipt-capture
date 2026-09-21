namespace ReceiptCapture.App.Services;

/// <summary>
/// The one seam between the view models and the Shell.
/// </summary>
/// <remarks>
/// View models ask for "show the review screen", not for a specific page type, so
/// they carry no reference to the views and can be exercised without a running
/// MAUI application.
/// </remarks>
public interface IAppNavigation
{
    /// <summary>Opens the review form for a freshly captured receipt.</summary>
    Task ShowReviewAsync(CaptureOutcome outcome);

    /// <summary>Returns to the previous screen.</summary>
    Task GoBackAsync();

    /// <summary>Asks the user a yes/no question. Returns false if no page is on screen.</summary>
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <summary>
    /// Rebuilds the shell after a language change, since each page resolves its
    /// localized text when it is created.
    /// </summary>
    void ReloadForLanguageChange();
}
