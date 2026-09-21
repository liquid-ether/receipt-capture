using ReceiptCapture.Core.Parsing;

namespace ReceiptCapture.Core.Tests;

public class AmountParserTests
{
    [Theory]
    // English / North American grouping
    [InlineData("12.34", 12.34)]
    [InlineData("$12.34", 12.34)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1,234", 1234)]
    [InlineData(".50", 0.50)]
    [InlineData("0.00", 0)]
    // French grouping: comma decimal, space or dot for thousands
    [InlineData("12,34", 12.34)]
    [InlineData("12,34 $", 12.34)]
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1.234", 1234)]
    // Narrow no-break space, which is what French typography actually uses
    [InlineData("1 234,56", 1234.56)]
    [InlineData("1 234,56", 1234.56)]
    // Refunds
    [InlineData("-4.00", -4.00)]
    [InlineData("(4.00)", -4.00)]
    [InlineData("4.00-", -4.00)]
    public void TryParse_reads_both_conventions(string token, double expected)
    {
        Assert.True(AmountParser.TryParse(token, out var value), $"'{token}' should parse");
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("TOTAL")]
    [InlineData("12 34")]      // a space that does not separate groups of three
    [InlineData("12.3456")]    // too many decimals to be money
    public void TryParse_rejects_non_amounts(string? token)
    {
        Assert.False(AmountParser.TryParse(token, out _));
    }

    [Fact]
    public void ExtractAll_ignores_percentages()
    {
        // "5" is glued to a percent sign and is a rate, not money.
        Assert.Equal([0.53m], AmountParser.ExtractAll("TPS 5%                  0,53"));
    }

    [Fact]
    public void TryExtractLast_takes_the_rightmost_amount()
    {
        // Receipts print "label .... amount", so the amount is the last number.
        Assert.True(AmountParser.TryExtractLast("TVQ 9,975%        1,05", out var value));
        Assert.Equal(1.05m, value);
    }

    [Fact]
    public void TryExtractLast_is_not_fooled_by_a_quantity_prefix()
    {
        Assert.True(AmountParser.TryExtractLast("2 Croissants          7.00", out var value));
        Assert.Equal(7.00m, value);
    }

    [Theory]
    [InlineData("TPS 5%", 5)]
    [InlineData("TVQ 9,975%", 9.975)]
    [InlineData("HST 13 %", 13)]
    public void TryExtractRate_reads_printed_rates(string line, double expected)
    {
        Assert.True(AmountParser.TryExtractRate(line, out var rate));
        Assert.Equal((decimal)expected, rate);
    }

    [Fact]
    public void TryExtractRate_returns_false_without_a_percent_sign()
    {
        Assert.False(AmountParser.TryExtractRate("TOTAL 12.66", out _));
    }
}
