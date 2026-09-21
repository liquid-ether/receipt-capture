namespace ReceiptCapture.Core.Models;

/// <summary>
/// A captured receipt. This is the domain model: it carries no persistence or UI
/// concerns so it can be created, validated and submitted from anywhere.
/// </summary>
public sealed class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string? MerchantName { get; set; }

    /// <summary>Date printed on the receipt (not the capture date).</summary>
    public DateOnly? TransactionDate { get; set; }

    /// <summary>ISO 4217 code, e.g. "CAD", "EUR", "USD".</summary>
    public string Currency { get; set; } = "CAD";

    /// <summary>Amount before taxes and tip, when the receipt prints one.</summary>
    public decimal? Subtotal { get; set; }

    public decimal? Tip { get; set; }

    /// <summary>Grand total actually charged.</summary>
    public decimal Total { get; set; }

    public List<ReceiptTax> Taxes { get; set; } = [];

    /// <summary>One of <see cref="ExpenseCategories"/>.</summary>
    public string? CategoryCode { get; set; }

    public string? Notes { get; set; }

    /// <summary>Absolute path to the receipt photo inside the app sandbox.</summary>
    public string? ImagePath { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    /// <summary>Identifier returned by the accounting system once accepted.</summary>
    public string? RemoteId { get; set; }

    public string? LastError { get; set; }

    /// <summary>Number of submission attempts made so far.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Earliest time the sync service may retry. Null means "eligible now".</summary>
    public DateTimeOffset? NextAttemptUtc { get; set; }

    public decimal TaxTotal => Taxes.Sum(t => t.Amount);

    /// <summary>
    /// True when subtotal + taxes + tip reconciles with the total, within a small
    /// tolerance for rounding. Null when the receipt does not carry a subtotal,
    /// in which case there is nothing to reconcile.
    /// </summary>
    public bool? IsArithmeticConsistent
    {
        get
        {
            if (Subtotal is not { } subtotal)
            {
                return null;
            }

            var expected = subtotal + TaxTotal + (Tip ?? 0m);
            return Math.Abs(expected - Total) <= 0.02m;
        }
    }
}
