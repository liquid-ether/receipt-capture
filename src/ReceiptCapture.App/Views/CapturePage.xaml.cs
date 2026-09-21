using ReceiptCapture.App.ViewModels;

namespace ReceiptCapture.App.Views;

public partial class CapturePage : ContentPage
{
    private readonly CaptureViewModel _viewModel;

    public CapturePage(CaptureViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
