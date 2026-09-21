using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Resources;
using ReceiptCapture.Core.Services;

namespace ReceiptCapture.App.ViewModels;

/// <summary>Lists captured receipts and lets the user push the queue along by hand.</summary>
public sealed partial class HistoryViewModel : BaseViewModel
{
    private readonly IReceiptRepository _repository;
    private readonly IReceiptImageStore _images;
    private readonly ReceiptSyncService _sync;

    public HistoryViewModel(
        IReceiptRepository repository,
        IReceiptImageStore images,
        ReceiptSyncService sync)
    {
        _repository = repository;
        _images = images;
        _sync = sync;
    }

    public ObservableCollection<ReceiptListItem> Receipts { get; } = [];

    [ObservableProperty]
    private bool _isEmpty = true;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var all = await _repository.GetAllAsync();

            Receipts.Clear();
            foreach (var receipt in all)
            {
                Receipts.Add(new ReceiptListItem(receipt));
            }

            IsEmpty = Receipts.Count == 0;
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
    private async Task SyncNowAsync()
    {
        ClearMessages();
        IsBusy = true;

        try
        {
            var summary = await _sync.SyncPendingAsync();
            StatusMessage = summary.WasSkipped
                ? null
                : AppStrings.Format("History_SyncResult", summary.Sent, summary.RetryScheduled, summary.Failed);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    /// <summary>Puts a failed receipt back in the queue for another try.</summary>
    [RelayCommand]
    private async Task RetryAsync(ReceiptListItem item)
    {
        if (item is null)
        {
            return;
        }

        var receipt = await _repository.GetAsync(item.Id);
        if (receipt is null)
        {
            return;
        }

        receipt.Status = ReceiptStatus.Pending;
        receipt.AttemptCount = 0;
        receipt.NextAttemptUtc = null;
        receipt.LastError = null;

        await _repository.SaveAsync(receipt);
        await SyncNowAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(ReceiptListItem item)
    {
        if (item is null)
        {
            return;
        }

        var receipt = await _repository.GetAsync(item.Id);
        if (receipt?.ImagePath is { Length: > 0 } path)
        {
            await _images.DeleteAsync(path);
        }

        await _repository.DeleteAsync(item.Id);
        await RefreshAsync();
    }
}

/// <summary>A row in the history list, formatted for display.</summary>
public sealed class ReceiptListItem
{
    public ReceiptListItem(Receipt receipt)
    {
        Id = receipt.Id;
        Merchant = receipt.MerchantName ?? AppStrings.Get("Warning_MerchantNotFound");
        StatusText = AppStrings.ForStatus(receipt.Status);
        LastError = receipt.LastError;
        CanRetry = receipt.Status == ReceiptStatus.Failed;

        DateText = receipt.TransactionDate is { } date
            ? date.ToString("d", CultureInfo.CurrentCulture)
            : receipt.CapturedAtUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);

        // Formatted in the receipt's own currency rather than the phone's, so a euro
        // receipt captured in Canada still reads as euros.
        AmountText = FormatMoney(receipt.Total, receipt.Currency);
    }

    public Guid Id { get; }

    public string Merchant { get; }

    public string DateText { get; }

    public string AmountText { get; }

    public string StatusText { get; }

    public string? LastError { get; }

    public bool CanRetry { get; }

    public bool HasError => !string.IsNullOrEmpty(LastError);

    private static string FormatMoney(decimal amount, string currencyCode)
    {
        var format = (NumberFormatInfo)CultureInfo.CurrentCulture.NumberFormat.Clone();
        format.CurrencySymbol = CurrencySymbol(currencyCode);

        return amount.ToString("C", format);
    }

    private static string CurrencySymbol(string currencyCode) => currencyCode switch
    {
        "EUR" => "€",
        "GBP" => "£",
        "USD" => "US$",
        "CAD" => "$",
        _ => currencyCode + " ",
    };
}
