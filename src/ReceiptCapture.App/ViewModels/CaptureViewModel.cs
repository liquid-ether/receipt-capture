using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReceiptCapture.App.Services;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Resources;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.App.ViewModels;

/// <summary>Drives the capture screen: take or pick a photo, then open the review form.</summary>
public sealed partial class CaptureViewModel : BaseViewModel
{
    private readonly ReceiptCaptureWorkflow _workflow;
    private readonly IReceiptRepository _repository;
    private readonly IAppNavigation _navigation;

    [ObservableProperty]
    private int _pendingCount;

    public CaptureViewModel(
        ReceiptCaptureWorkflow workflow,
        IReceiptRepository repository,
        IAppNavigation navigation)
    {
        _workflow = workflow;
        _repository = repository;
        _navigation = navigation;
    }

    public string PendingSummary => AppStrings.Format("Capture_PendingCount", PendingCount);

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(PendingSummary));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var all = await _repository.GetAllAsync();
        PendingCount = all.Count(r => r.Status is ReceiptStatus.Pending or ReceiptStatus.Failed);
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task TakePhotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            ErrorMessage = AppStrings.Get("Capture_CameraUnavailable");
            return;
        }

        await CaptureFromAsync(() => MediaPicker.Default.CapturePhotoAsync());
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private Task PickPhotoAsync() =>
        // PickPhotosAsync is the supported API; one receipt at a time, so take the first.
        CaptureFromAsync(async () => (await MediaPicker.Default.PickPhotosAsync()).FirstOrDefault());

    private async Task CaptureFromAsync(Func<Task<FileResult?>> pick)
    {
        ClearMessages();

        FileResult? photo;
        try
        {
            photo = await pick();
        }
        catch (PermissionException)
        {
            ErrorMessage = AppStrings.Get("Capture_CameraUnavailable");
            return;
        }

        if (photo is null)
        {
            return; // the user backed out
        }

        IsBusy = true;
        StatusMessage = AppStrings.Get("Capture_Reading");

        try
        {
            await using var stream = await photo.OpenReadAsync();
            var outcome = await _workflow.CaptureAsync(stream, Path.GetExtension(photo.FileName));

            if (outcome.ErrorMessage is { Length: > 0 })
            {
                // The photo is saved either way; the user can still fill the form in.
                ErrorMessage = outcome.ErrorMessage;
            }

            await _navigation.ShowReviewAsync(outcome);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
            await RefreshAsync();
        }
    }
}
