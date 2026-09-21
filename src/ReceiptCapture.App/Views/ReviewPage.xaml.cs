using ReceiptCapture.App.ViewModels;

namespace ReceiptCapture.App.Views;

public partial class ReviewPage : ContentPage
{
    public ReviewPage(ReviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
