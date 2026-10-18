using System.Globalization;
using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Parsing;
using ReceiptCapture.Core.Resources;

namespace ReceiptCapture.Core.Tests;

/// <summary>
/// These run in one collection because they move the ambient culture and the
/// <see cref="AppStrings.OverrideCulture"/> switch, which are process-wide.
/// </summary>
[Collection("Localization")]
public class LocalizationTests : IDisposable
{
    public void Dispose() => AppStrings.OverrideCulture = null;

    [Theory]
    [InlineData("en", "History")]
    [InlineData("fr", "Historique")]
    [InlineData("fr-CA", "Historique")]
    public void Strings_resolve_for_both_languages(string culture, string expected)
    {
        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(expected, AppStrings.Get("Tab_History"));
    }

    [Fact]
    public void An_unknown_language_falls_back_to_english()
    {
        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo("de");

        Assert.Equal("History", AppStrings.Get("Tab_History"));
    }

    [Fact]
    public void The_device_language_is_followed_when_nothing_is_overridden()
    {
        AppStrings.OverrideCulture = null;
        using var _ = new CultureScope("fr-CA");

        Assert.Equal("Historique", AppStrings.Get("Tab_History"));
    }

    [Fact]
    public void Formatted_strings_substitute_their_arguments()
    {
        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo("fr");

        Assert.Equal("2 envoyé(s), 1 à réessayer, 0 en échec", AppStrings.Format("History_SyncResult", 2, 1, 0));
    }

    [Fact]
    public void A_missing_key_is_obvious_rather_than_silent()
    {
        Assert.Equal("[No_Such_Key]", AppStrings.Get("No_Such_Key"));
    }

    [Theory]
    [InlineData("en", "Waiting to send")]
    [InlineData("fr", "En attente d'envoi")]
    public void Receipt_statuses_have_a_display_name(string culture, string expected)
    {
        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal(expected, AppStrings.ForStatus(ReceiptStatus.Pending));
    }

    [Fact]
    public void Every_receipt_status_is_translated_in_both_languages()
    {
        foreach (var culture in new[] { "en", "fr" })
        {
            AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(culture);

            foreach (var status in Enum.GetValues<ReceiptStatus>())
            {
                Assert.DoesNotContain("[", AppStrings.ForStatus(status), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Every_expense_category_is_translated_in_both_languages()
    {
        foreach (var culture in new[] { "en", "fr" })
        {
            AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(culture);

            foreach (var code in ExpenseCategories.All)
            {
                var name = AppStrings.ForCategory(code);
                Assert.False(
                    name.StartsWith('['),
                    $"category '{code}' has no {culture} translation");
            }
        }
    }

    [Fact]
    public void Every_parser_warning_is_translated_in_both_languages()
    {
        // The parser reports warnings as resource keys; a key with no translation
        // would surface to the user as raw text on the review screen.
        string[] keys =
        [
            ParseWarnings.MerchantNotFound,
            ParseWarnings.DateNotFound,
            ParseWarnings.DateAmbiguous,
            ParseWarnings.TotalNotFound,
            ParseWarnings.TotalComputed,
            ParseWarnings.TotalsDoNotReconcile,
        ];

        foreach (var culture in new[] { "en", "fr" })
        {
            AppStrings.OverrideCulture = CultureInfo.GetCultureInfo(culture);

            foreach (var key in keys)
            {
                Assert.False(
                    AppStrings.Get(key).StartsWith('['),
                    $"warning '{key}' has no {culture} translation");
            }
        }
    }

    [Fact]
    public void The_french_resources_are_genuinely_different_from_the_english_ones()
    {
        // Guards against a satellite assembly silently failing to load, which would
        // make every French lookup quietly return English.
        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo("en");
        var english = AppStrings.Get("Review_Send");

        AppStrings.OverrideCulture = CultureInfo.GetCultureInfo("fr");
        var french = AppStrings.Get("Review_Send");

        Assert.Equal("Send to accounting", english);
        Assert.Equal("Envoyer à la comptabilité", french);
    }
}
