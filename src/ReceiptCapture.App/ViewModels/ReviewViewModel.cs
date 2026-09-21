using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReceiptCapture.App.Services;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Parsing;
using ReceiptCapture.Core.Resources;

namespace ReceiptCapture.App.ViewModels;

/// <summary>
/// The review form. Everything the parser produced is editable here, because OCR on
/// a crumpled thermal receipt is a suggestion, not a source of truth — and the
/// numbers are going into someone's books.
/// </summary>
public sealed partial class ReviewViewModel : BaseViewModel
{
    private readonly ReceiptCaptureWorkflow _workflow;
    private readonly IAppNavigation _navigation;
    private Receipt _receipt = new();

    [ObservableProperty]
    private string? _merchantName;

    [ObservableProperty]
    private DateTime _transactionDate = DateTime.Today;

    [ObservableProperty]
    private bool _hasTransactionDate = true;

    [ObservableProperty]
    private string _currency = "CAD";

    [ObservableProperty]
    private string? _subtotalText;

    [ObservableProperty]
    private string? _tipText;

    [ObservableProperty]
    private string? _totalText;

    [ObservableProperty]
    private string? _notes;

    [ObservableProperty]
    private string? _imagePath;

    [ObservableProperty]
    private int _selectedCategoryIndex = -1;

    [ObservableProperty]
    private ParseConfidence _confidence;

    public ReviewViewModel(ReceiptCaptureWorkflow workflow, IAppNavigation navigation)
    {
        _workflow = workflow;
        _navigation = navigation;
    }

    public ObservableCollection<TaxLineViewModel> Taxes { get; } = [];

    /// <summary>Localized warnings from the parser, shown above the form.</summary>
    public ObservableCollection<string> Warnings { get; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>Category names in the user's language; the index maps to <see cref="ExpenseCategories.All"/>.</summary>
    public IReadOnlyList<string> CategoryNames { get; } =
        [.. ExpenseCategories.All.Select(AppStrings.ForCategory)];

    /// <summary>Fills the form from a freshly captured receipt.</summary>
    public void Load(CaptureOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        _receipt = outcome.Draft;
        Confidence = outcome.Confidence;

        MerchantName = _receipt.MerchantName;
        HasTransactionDate = _receipt.TransactionDate is not null;
        TransactionDate = _receipt.TransactionDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        Currency = _receipt.Currency;
        SubtotalText = Format(_receipt.Subtotal);
        TipText = Format(_receipt.Tip);
        TotalText = _receipt.Total > 0m ? Format(_receipt.Total) : null;
        Notes = _receipt.Notes;
        ImagePath = _receipt.ImagePath;

        SelectedCategoryIndex = _receipt.CategoryCode is { } code
            ? ExpenseCategories.All.ToList().IndexOf(code)
            : -1;

        Taxes.Clear();
        foreach (var tax in _receipt.Taxes)
        {
            Taxes.Add(new TaxLineViewModel(tax.Code, Format(tax.Amount), tax.Rate));
        }

        Warnings.Clear();
        foreach (var key in outcome.Warnings)
        {
            Warnings.Add(AppStrings.Get(key));
        }

        OnPropertyChanged(nameof(HasWarnings));
    }

    [RelayCommand]
    private void AddTax() => Taxes.Add(new TaxLineViewModel("TAX", null, null));

    [RelayCommand]
    private void RemoveTax(TaxLineViewModel tax) => Taxes.Remove(tax);

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task SendAsync()
    {
        ClearMessages();

        if (!TryBuildReceipt(out var error))
        {
            ErrorMessage = error;
            return;
        }

        IsBusy = true;
        try
        {
            await _workflow.ConfirmAsync(_receipt);
            StatusMessage = AppStrings.Get("Review_Queued");

            await _navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task DiscardAsync()
    {
        var confirmed = await _navigation.ConfirmAsync(
            AppStrings.Get("Review_Discard"),
            AppStrings.Get("Review_DiscardConfirm"),
            AppStrings.Get("Common_Delete"),
            AppStrings.Get("Common_Cancel"));

        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _workflow.DiscardAsync(_receipt);
            await _navigation.GoBackAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Folds the edited fields back into the receipt, rejecting anything that would
    /// put a meaningless figure into the accounting system.
    /// </summary>
    private bool TryBuildReceipt(out string? error)
    {
        if (string.IsNullOrWhiteSpace(TotalText))
        {
            error = AppStrings.Get("Validation_TotalRequired");
            return false;
        }

        // The same parser the OCR text goes through, so someone typing "12,34" on a
        // French keyboard is understood exactly as "12.34" on an English one.
        if (!AmountParser.TryParse(TotalText, out var total))
        {
            error = AppStrings.Get("Validation_TotalRequired");
            return false;
        }

        if (total <= 0m)
        {
            error = AppStrings.Get("Validation_TotalPositive");
            return false;
        }

        _receipt.MerchantName = string.IsNullOrWhiteSpace(MerchantName) ? null : MerchantName.Trim();
        _receipt.TransactionDate = HasTransactionDate ? DateOnly.FromDateTime(TransactionDate) : null;
        _receipt.Currency = string.IsNullOrWhiteSpace(Currency) ? "CAD" : Currency.Trim().ToUpperInvariant();
        _receipt.Subtotal = ParseOptional(SubtotalText);
        _receipt.Tip = ParseOptional(TipText);
        _receipt.Total = total;
        _receipt.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        _receipt.CategoryCode = SelectedCategoryIndex >= 0 && SelectedCategoryIndex < ExpenseCategories.All.Count
            ? ExpenseCategories.All[SelectedCategoryIndex]
            : null;

        _receipt.Taxes =
        [
            .. Taxes
                .Select(t => new
                {
                    t.Code,
                    Parsed = AmountParser.TryParse(t.AmountText, out var amount) ? amount : (decimal?)null,
                    t.Rate,
                })
                .Where(t => t.Parsed is not null)
                .Select(t => new ReceiptTax(
                    string.IsNullOrWhiteSpace(t.Code) ? "TAX" : t.Code.Trim().ToUpperInvariant(),
                    t.Parsed!.Value,
                    t.Rate))
        ];

        error = null;
        return true;
    }

    private static decimal? ParseOptional(string? text) =>
        AmountParser.TryParse(text, out var value) ? value : null;

    private static string Format(decimal? value) =>
        value?.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty;
}

/// <summary>One editable tax row on the review form.</summary>
public sealed partial class TaxLineViewModel : ObservableObject
{
    [ObservableProperty]
    private string _code;

    [ObservableProperty]
    private string? _amountText;

    public TaxLineViewModel(string code, string? amountText, decimal? rate)
    {
        _code = code;
        _amountText = amountText;
        Rate = rate;
    }

    /// <summary>Kept from the parse so it can be sent on, but not edited in the form.</summary>
    public decimal? Rate { get; }
}
