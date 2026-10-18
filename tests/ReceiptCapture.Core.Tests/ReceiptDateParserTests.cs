using ReceiptCapture.Core.Parsing;

namespace ReceiptCapture.Core.Tests;

public class ReceiptDateParserTests
{
    /// <summary>Fixed so the "not in the future" rule does not drift as time passes.</summary>
    private static readonly ReceiptParseOptions Options =
        new() { Today = new DateOnly(2026, 9, 21) };

    [Theory]
    [InlineData("2026-03-14", 2026, 3, 14)]
    [InlineData("Date: 2026/03/14  10:42", 2026, 3, 14)]
    [InlineData("14/03/2026", 2026, 3, 14)]      // day > 12 resolves the order
    [InlineData("03/25/2026", 2026, 3, 25)]      // second value > 12, so month-first
    [InlineData("14-03-26", 2026, 3, 14)]        // two-digit year
    [InlineData("15 janvier 2026", 2026, 1, 15)]
    [InlineData("15 janv. 2026", 2026, 1, 15)]
    [InlineData("3 février 2026", 2026, 2, 3)]
    [InlineData("1 août 2026", 2026, 8, 1)]
    [InlineData("January 15, 2026", 2026, 1, 15)]
    [InlineData("Mar 14 2026", 2026, 3, 14)]
    public void TryFind_reads_dates_in_both_languages(string line, int year, int month, int day)
    {
        Assert.True(ReceiptDateParser.TryFind(line, Options, out var match), $"'{line}' should parse");
        Assert.Equal(new DateOnly(year, month, day), match.Date);
    }

    [Fact]
    public void An_unresolvable_order_is_reported_rather_than_guessed_silently()
    {
        Assert.True(ReceiptDateParser.TryFind("03/04/2026", Options, out var match));

        Assert.True(match.IsAmbiguous);
        Assert.Equal(new DateOnly(2026, 4, 3), match.Date); // day-first default
    }

    [Fact]
    public void The_day_first_preference_can_be_reversed()
    {
        var monthFirst = new ReceiptParseOptions { Today = Options.Today, PreferDayFirst = false };

        Assert.True(ReceiptDateParser.TryFind("03/04/2026", monthFirst, out var match));

        Assert.True(match.IsAmbiguous);
        Assert.Equal(new DateOnly(2026, 3, 4), match.Date);
    }

    [Fact]
    public void Identical_day_and_month_are_not_ambiguous()
    {
        Assert.True(ReceiptDateParser.TryFind("04/04/2026", Options, out var match));
        Assert.False(match.IsAmbiguous);
    }

    [Theory]
    [InlineData("Tel: 416-555-0134")]
    [InlineData("Card ****1234")]
    [InlineData("TOTAL 12.66")]
    [InlineData("2099-01-01")]          // implausibly far in the future
    [InlineData("1990-01-01")]          // implausibly far in the past
    [InlineData("2026-02-30")]          // not a real day
    public void Non_dates_are_rejected(string line)
    {
        Assert.False(ReceiptDateParser.TryFind(line, Options, out _));
    }

    [Fact]
    public void TryFindInLines_returns_the_first_date_on_the_slip()
    {
        string[] lines =
        [
            "BOULANGERIE DU COIN",
            "456, rue Saint-Denis",
            "Facture no 00123",
            "Date: 14/03/2026",
        ];

        Assert.True(ReceiptDateParser.TryFindInLines(lines, Options, out var match));
        Assert.Equal(new DateOnly(2026, 3, 14), match.Date);
    }
}
