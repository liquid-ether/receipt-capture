using CommunityToolkit.Mvvm.ComponentModel;

namespace ReceiptCapture.App.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    public bool IsNotBusy => !IsBusy;

    protected void ClearMessages()
    {
        StatusMessage = null;
        ErrorMessage = null;
    }
}
