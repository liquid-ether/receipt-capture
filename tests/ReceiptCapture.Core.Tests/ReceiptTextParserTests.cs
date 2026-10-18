using ReceiptCapture.Core.Parsing;

namespace ReceiptCapture.Core.Tests;

public class ReceiptTextParserTests
{
    private static readonly ReceiptParseOptions Options =
        new() { Today = new DateOnly(2026, 9, 21) };

    /// <summary>A bilingual-province slip: English labels, HST, dot decimals.</summary>
    private static string[] EnglishReceipt =>
    [
        "THE COFFEE HOUSE",
        "123 Main Street",
        "Toronto, ON  M5V 2T6",
        "Tel: 416-555-0134",
        "",
        "2026-03-14   10:42",
        "",
        "Latte                     4.75",
        "Croissant                 3.50",
        "Muffin                    2.95",
        "--------------------------------",
        "SUBTOTAL                 11.20",
        "HST 13%                   1.46",
        "TOTAL                    12.66",
        "",
        "VISA                     12.66",
        "CHANGE                    0.00",
        "Thank you!",
    ];

    /// <summary>A Québec slip: French labels, TPS/TVQ, comma decimals.</summary>
    private static string[] FrenchReceipt =>
    [
        "BOULANGERIE DU COIN",
        "456, rue Saint-Denis",
        "Montréal (Québec)  H2J 2L1",
        "",
        "Facture no 00123",
        "Date: 14/03/2026",
        "",
        "Pain au chocolat          3,50",
        "Baguette                  4,25",
        "Café                      2,75",
        "--------------------------------",
        "Sous-total               10,50",
        "TPS 5%                    0,53",
        "TVQ 9,975%                1,05",
        "TOTAL                    12,08",
        "",
        "Comptant                 20,00",
        "Monnaie                   7,92",
        "Merci de votre visite!",
    ];

    [Fact]
    public void An_english_receipt_is_read_end_to_end()
    {
        var parsed = ReceiptTextParser.Parse(EnglishReceipt, Options);

        Assert.Equal("THE COFFEE HOUSE", parsed.MerchantName);
        Assert.Equal(new DateOnly(2026, 3, 14), parsed.TransactionDate);
        Assert.Equal(11.20m, parsed.Subtotal);
        Assert.Equal(12.66m, parsed.Total);
        Assert.Equal("CAD", parsed.Currency);

        var tax = Assert.Single(parsed.Taxes);
        Assert.Equal("HST", tax.Code);
        Assert.Equal(1.46m, tax.Amount);
        Assert.Equal(13m, tax.Rate);

        Assert.Empty(parsed.Warnings);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public void A_french_receipt_is_read_end_to_end()
    {
        var parsed = ReceiptTextParser.Parse(FrenchReceipt, Options);

        Assert.Equal("BOULANGERIE DU COIN", parsed.MerchantName);
        Assert.Equal(new DateOnly(2026, 3, 14), parsed.TransactionDate);
        Assert.Equal(10.50m, parsed.Subtotal);
        Assert.Equal(12.08m, parsed.Total);
        Assert.Equal("CAD", parsed.Currency);

        Assert.Collection(
            parsed.Taxes,
            tps =>
            {
                Assert.Equal("TPS", tps.Code);
                Assert.Equal(0.53m, tps.Amount);
                Assert.Equal(5m, tps.Rate);
            },
            tvq =>
            {
                Assert.Equal("TVQ", tvq.Code);
                Assert.Equal(1.05m, tvq.Amount);
                Assert.Equal(9.975m, tvq.Rate);
            });

        Assert.Empty(parsed.Warnings);
        Assert.Equal(ParseConfidence.High, parsed.Confidence);
    }

    [Fact]
    public void The_phone_language_does_not_change_the_result()
    {
        // The receipt is a physical object; it does not know what the phone is set to.
        using var _ = new CultureScope("fr-CA");
        var inFrenchCulture = ReceiptTextParser.Parse(EnglishReceipt, Options);

        using var __ = new CultureScope("en-US");
        var inEnglishCulture = ReceiptTextParser.Parse(EnglishReceipt, Options);

        Assert.Equal(inEnglishCulture.Total, inFrenchCulture.Total);
        Assert.Equal(12.66m, inFrenchCulture.Total);
    }

    [Fact]
    public void Card_and_change_lines_never_become_the_total()
    {
        var parsed = ReceiptTextParser.Parse(
            ["TOTAL      12,08", "Comptant   20,00", "Monnaie     7,92"],
            Options);

        Assert.Equal(12.08m, parsed.Total);
    }

    [Fact]
    public void A_tax_roll_up_line_is_not_counted_on_top_of_its_own_parts()
    {
        // "Total des taxes" repeats TPS + TVQ; adding it would inflate the tax total.
        var parsed = ReceiptTextParser.Parse(
            [
                "Sous-total          10,50",
                "TPS                  0,53",
                "TVQ                  1,05",
                "Total des taxes      1,58",
                "TOTAL               12,08",
            ],
            Options);

        Assert.Equal(1.58m, parsed.TaxTotal);
        Assert.Equal(2, parsed.Taxes.Count);

        // 10.50 + 1.58 == 12.08, so the amounts reconcile; double counting the
        // roll-up would have produced 13.66 and raised this warning.
        Assert.DoesNotContain(ParseWarnings.TotalsDoNotReconcile, parsed.Warnings);
    }

    [Fact]
    public void A_tax_roll_up_is_used_when_no_individual_lines_were_printed()
    {
        var parsed = ReceiptTextParser.Parse(
            ["Subtotal   10.50", "Total tax   1.58", "TOTAL      12.08"],
            Options);

        var tax = Assert.Single(parsed.Taxes);
        Assert.Equal(1.58m, tax.Amount);
    }

    [Fact]
    public void Subtotal_wins_over_total_when_a_line_says_both()
    {
        var parsed = ReceiptTextParser.Parse(
            ["Total before tax    10.50", "HST                  1.37", "TOTAL               11.87"],
            Options);

        Assert.Equal(10.50m, parsed.Subtotal);
        Assert.Equal(11.87m, parsed.Total);
    }

    [Fact]
    public void A_total_the_amounts_contradict_is_flagged_for_review()
    {
        var parsed = ReceiptTextParser.Parse(
            ["Subtotal   10.00", "GST         0.50", "TOTAL      99.99"],
            Options);

        Assert.Contains(ParseWarnings.TotalsDoNotReconcile, parsed.Warnings);
        Assert.Equal(ParseConfidence.Medium, parsed.Confidence);
    }

    [Fact]
    public void A_missing_total_is_computed_from_the_parts_and_flagged()
    {
        var parsed = ReceiptTextParser.Parse(
            ["Sous-total   10,50", "TPS           0,53", "TVQ           1,05"],
            Options);

        Assert.Equal(12.08m, parsed.Total);
        Assert.Contains(ParseWarnings.TotalComputed, parsed.Warnings);
        Assert.Equal(ParseConfidence.Medium, parsed.Confidence);
    }

    [Fact]
    public void A_slip_with_no_readable_amounts_reports_low_confidence()
    {
        var parsed = ReceiptTextParser.Parse(["CORNER STORE", "blurred beyond reading"], Options);

        Assert.Null(parsed.Total);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
        Assert.Contains(ParseWarnings.TotalNotFound, parsed.Warnings);
    }

    [Fact]
    public void An_amount_wrapped_onto_the_next_line_is_still_found()
    {
        var parsed = ReceiptTextParser.Parse(["TOTAL", "12.66"], Options);

        Assert.Equal(12.66m, parsed.Total);
    }

    [Fact]
    public void Dotted_tax_acronyms_are_recognized()
    {
        var parsed = ReceiptTextParser.Parse(["T.P.S.    0,53", "T.V.Q.    1,05"], Options);

        Assert.Collection(
            parsed.Taxes,
            tps => Assert.Equal("TPS", tps.Code),
            tvq => Assert.Equal("TVQ", tvq.Code));
    }

    [Fact]
    public void A_euro_receipt_is_detected_from_its_vat_line()
    {
        var parsed = ReceiptTextParser.Parse(
            ["LE PETIT CAFE", "Sous-total   10,00", "TVA 20%       2,00", "TOTAL        12,00"],
            Options);

        Assert.Equal("EUR", parsed.Currency);
        Assert.Equal(12.00m, parsed.Total);
    }

    [Fact]
    public void Addresses_and_boilerplate_are_not_mistaken_for_the_merchant()
    {
        var parsed = ReceiptTextParser.Parse(
            ["*** CUSTOMER COPY ***", "123 456 789", "QUINCAILLERIE LAMBERT", "TOTAL   9,99"],
            Options);

        Assert.Equal("QUINCAILLERIE LAMBERT", parsed.MerchantName);
    }

    [Fact]
    public void A_missing_merchant_is_reported()
    {
        var parsed = ReceiptTextParser.Parse(["123 456 789", "TOTAL   9,99"], Options);

        Assert.Null(parsed.MerchantName);
        Assert.Contains(ParseWarnings.MerchantNotFound, parsed.Warnings);
    }

    [Fact]
    public void Parsing_an_empty_slip_does_not_throw()
    {
        var parsed = ReceiptTextParser.Parse([], Options);

        Assert.Null(parsed.Total);
        Assert.Equal(ParseConfidence.Low, parsed.Confidence);
    }

    [Fact]
    public void ToDraft_carries_the_parsed_values_onto_the_review_model()
    {
        var draft = ReceiptTextParser.Parse(EnglishReceipt, Options).ToDraft("/tmp/receipt.jpg");

        Assert.Equal(12.66m, draft.Total);
        Assert.Equal(1.46m, draft.TaxTotal);
        Assert.Equal("/tmp/receipt.jpg", draft.ImagePath);
        Assert.Equal(Core.Models.ReceiptStatus.Draft, draft.Status);
        Assert.True(draft.IsArithmeticConsistent);
    }
}
