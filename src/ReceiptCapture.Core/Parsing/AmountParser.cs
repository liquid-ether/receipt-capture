using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// Parses monetary amounts without knowing the receipt's culture in advance.
/// A receipt photographed in Montréal may print "1 234,56 $" while the next one
/// prints "$1,234.56", and the phone's own culture tells us nothing about either,
/// so the separators are resolved from the shape of each number.
/// </summary>
public static partial class AmountParser
{
    /// <summary>
    /// Matches a number that could be an amount. Three alternatives, longest first:
    /// grouped ("1 234,56", "1,234.56", "1.234"), plain decimal ("12.34", "12,34"),
    /// and bare integer ("12"). A trailing percent sign disqualifies the match so
    /// rates like "9,975 %" are never mistaken for money.
    /// </summary>
    [GeneratedRegex(
        @"(?<![\d.,])(?:\d{1,3}(?:[ \u00A0\u2007\u2009\u202F.,]\d{3})+(?:[.,]\d{1,2})?|\d+[.,]\d{1,2}|\d+)(?![\d])(?!\s*%)",
        RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(\d{1,2}(?:[.,]\d{1,3})?)\s*%", RegexOptions.CultureInvariant)]
    private static partial Regex RateRegex();

    /// <summary>
    /// Parses a single amount token such as "1 234,56", "$12.34", "(4.00)" or "12.34-".
    /// Returns false for anything that is not unambiguously a number.
    /// </summary>
    public static bool TryParse(string? token, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var isNegative = false;
        var builder = new StringBuilder(token.Length);

        foreach (var ch in token)
        {
            if (char.IsDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch is '.' or ',')
            {
                builder.Append(ch);
            }
            else if (ch is '-' or '(' or '−')
            {
                // Refunds print either a leading/trailing minus or parentheses.
                isNegative = true;
            }
            else if (char.IsWhiteSpace(ch) || Array.IndexOf(SpaceLikeCharacters, ch) >= 0)
            {
                // Kept: a space may be a group separator, which TryParseCleaned validates.
                builder.Append(' ');
            }

            // Everything else — currency symbols, ')', stray OCR letters — carries no digits.
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0 || !cleaned.Any(char.IsDigit))
        {
            return false;
        }

        if (!TryParseCleaned(cleaned, out value))
        {
            return false;
        }

        if (isNegative)
        {
            value = -value;
        }

        return true;
    }

    /// <summary>
    /// Returns every amount-shaped number on the line, in order of appearance.
    /// </summary>
    public static IReadOnlyList<decimal> ExtractAll(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        List<decimal>? results = null;
        foreach (var match in AmountRegex().EnumerateMatches(line))
        {
            var token = line.Substring(match.Index, match.Length);
            if (TryParse(token, out var value))
            {
                (results ??= []).Add(value);
            }
        }

        return (IReadOnlyList<decimal>?)results ?? [];
    }

    /// <summary>
    /// Returns the rightmost amount on the line. Receipts are laid out as
    /// "label .... amount", so the last number is nearly always the one that matters
    /// ("TPS 5% 1,50" yields 1.50, not 5).
    /// </summary>
    public static bool TryExtractLast(string? line, out decimal value)
    {
        var all = ExtractAll(line);
        if (all.Count == 0)
        {
            value = 0m;
            return false;
        }

        value = all[^1];
        return true;
    }

    /// <summary>Extracts a printed percentage rate such as "5 %" or "9,975%".</summary>
    public static bool TryExtractRate(string? line, out decimal rate)
    {
        rate = 0m;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var match = RateRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var token = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out rate);
    }

    private static readonly char[] SpaceLikeCharacters = ['\u00A0', '\u2007', '\u2009', '\u202F'];

    /// <summary>
    /// Resolves which separator is the decimal point for a string of digits,
    /// dots, commas and spaces, then parses it with the invariant culture.
    /// </summary>
    private static bool TryParseCleaned(string cleaned, out decimal value)
    {
        value = 0m;

        // A space is only ever a group separator, and only when it splits groups of
        // three digits. "12 34" is OCR noise rather than a number, so reject it.
        if (cleaned.Contains(' '))
        {
            var segments = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 1; i < segments.Length; i++)
            {
                var digitsAtStart = segments[i].TakeWhile(char.IsDigit).Count();
                if (digitsAtStart != 3)
                {
                    return false;
                }
            }

            cleaned = string.Concat(segments);
        }

        var lastDot = cleaned.LastIndexOf('.');
        var lastComma = cleaned.LastIndexOf(',');

        char? decimalSeparator;
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: whichever comes last is the decimal point ("1.234,56" / "1,234.56").
            decimalSeparator = lastDot > lastComma ? '.' : ',';
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var separator = lastDot >= 0 ? '.' : ',';
            var position = lastDot >= 0 ? lastDot : lastComma;
            var occurrences = cleaned.Count(c => c == separator);
            var digitsAfter = cleaned.Length - position - 1;

            // Repeated ("1.234.567") or followed by exactly three digits ("1,234")
            // means grouping; one or two trailing digits means a decimal fraction.
            decimalSeparator = occurrences > 1 || digitsAfter == 3 ? null : separator;

            if (digitsAfter is not (1 or 2 or 3))
            {
                return false;
            }
        }
        else
        {
            decimalSeparator = null;
        }

        var normalized = new StringBuilder(cleaned.Length);
        foreach (var ch in cleaned)
        {
            if (char.IsDigit(ch))
            {
                normalized.Append(ch);
            }
            else if (decimalSeparator is { } sep && ch == sep)
            {
                normalized.Append('.');
            }

            // every other separator is grouping and gets dropped
        }

        return decimal.TryParse(
            normalized.ToString(),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }
}
