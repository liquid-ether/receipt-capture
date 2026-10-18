using System.Globalization;
using System.Text.RegularExpressions;
using ReceiptCapture.Core.Text;

namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// Finds the transaction date on a receipt line. Handles ISO dates, numeric dates
/// in either day-first or month-first order, and month names in English or French.
/// </summary>
/// <remarks>
/// "03/04/2026" is genuinely ambiguous — 3 April in Montréal, 4 March in Boston —
/// and no amount of cleverness resolves it from the paper alone. Rather than guess
/// silently, the parser applies a configured preference and reports the ambiguity
/// so the review screen can highlight the field for the user to confirm.
/// </remarks>
public static partial class ReceiptDateParser
{
    [GeneratedRegex(@"(?<![\d])(\d{1,4})[-/.](\d{1,2})[-/.](\d{2,4})(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDateRegex();

    /// <summary>"15 janvier 2026", "15 janv 2026", "1 mars 2026".</summary>
    [GeneratedRegex(@"(?<![\d])(\d{1,2})\s+([a-z]{3,10})\.?\s+(\d{4})(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex DayMonthYearRegex();

    /// <summary>"January 15, 2026", "Jan 15 2026".</summary>
    [GeneratedRegex(@"\b([a-z]{3,10})\.?\s+(\d{1,2})\s*,?\s+(\d{4})(?![\d])", RegexOptions.CultureInvariant)]
    private static partial Regex MonthDayYearRegex();

    private static readonly Dictionary<string, int> MonthNames = BuildMonthNames();

    /// <summary>
    /// Attempts to read a date from a single line.
    /// </summary>
    /// <param name="line">Raw OCR line.</param>
    /// <param name="options">Supplies the day-first preference and the reference "today".</param>
    /// <param name="result">The date found, and whether its day/month order was ambiguous.</param>
    public static bool TryFind(string? line, ReceiptParseOptions options, out ReceiptDateMatch result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var today = options.Today ?? DateOnly.FromDateTime(DateTime.Today);

        // Numeric dates are matched against the raw line: folding turns "/" into a
        // space, which would erase the very separators "14/03/2026" is recognised by.
        // Month names need the folded form so "Février" matches "fevrier".
        return TryNumeric(TextNormalizer.CollapseWhitespace(line), options, today, out result)
            || TryMonthName(TextNormalizer.Fold(line), today, out result);
    }

    /// <summary>Scans lines in order and returns the first plausible date.</summary>
    public static bool TryFindInLines(
        IReadOnlyList<string> lines,
        ReceiptParseOptions options,
        out ReceiptDateMatch result)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (TryFind(lines[i], options, out result))
            {
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool TryNumeric(
        string line,
        ReceiptParseOptions options,
        DateOnly today,
        out ReceiptDateMatch result)
    {
        result = default;

        foreach (Match match in NumericDateRegex().Matches(line))
        {
            var first = match.Groups[1].Value;
            var second = match.Groups[2].Value;
            var third = match.Groups[3].Value;

            int year, month, day;
            var ambiguous = false;

            if (first.Length == 4)
            {
                // ISO-style: 2026-09-21
                year = int.Parse(first, CultureInfo.InvariantCulture);
                month = int.Parse(second, CultureInfo.InvariantCulture);
                day = int.Parse(third, CultureInfo.InvariantCulture);
            }
            else
            {
                year = NormalizeYear(int.Parse(third, CultureInfo.InvariantCulture), today);
                var a = int.Parse(first, CultureInfo.InvariantCulture);
                var b = int.Parse(second, CultureInfo.InvariantCulture);

                if (a > 12 && b <= 12)
                {
                    day = a;
                    month = b;
                }
                else if (b > 12 && a <= 12)
                {
                    month = a;
                    day = b;
                }
                else if (a <= 12 && b <= 12)
                {
                    ambiguous = a != b;
                    (day, month) = options.PreferDayFirst ? (a, b) : (b, a);
                }
                else
                {
                    continue; // both above 12: not a date
                }
            }

            if (TryBuild(year, month, day, today, out var date))
            {
                result = new ReceiptDateMatch(date, ambiguous);
                return true;
            }
        }

        return false;
    }

    private static bool TryMonthName(string folded, DateOnly today, out ReceiptDateMatch result)
    {
        result = default;

        foreach (Match match in DayMonthYearRegex().Matches(folded))
        {
            if (MonthNames.TryGetValue(match.Groups[2].Value, out var month)
                && TryBuild(
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    month,
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    today,
                    out var date))
            {
                result = new ReceiptDateMatch(date, IsAmbiguous: false);
                return true;
            }
        }

        foreach (Match match in MonthDayYearRegex().Matches(folded))
        {
            if (MonthNames.TryGetValue(match.Groups[1].Value, out var month)
                && TryBuild(
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    month,
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    today,
                    out var date))
            {
                result = new ReceiptDateMatch(date, IsAmbiguous: false);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the date and rejects anything that cannot be a receipt: a future date
    /// beyond a small timezone allowance, or one implausibly far in the past.
    /// </summary>
    private static bool TryBuild(int year, int month, int day, DateOnly today, out DateOnly date)
    {
        date = default;

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, Math.Clamp(month, 1, 12)))
        {
            return false;
        }

        var candidate = new DateOnly(year, month, day);
        if (candidate > today.AddDays(2) || candidate < today.AddYears(-10))
        {
            return false;
        }

        date = candidate;
        return true;
    }

    /// <summary>Expands a two-digit year, preferring the current century unless that lands in the future.</summary>
    private static int NormalizeYear(int year, DateOnly today)
    {
        if (year >= 100)
        {
            return year;
        }

        var century = today.Year / 100 * 100;
        var candidate = century + year;
        return candidate > today.Year + 1 ? candidate - 100 : candidate;
    }

    /// <summary>
    /// Month names in both languages, folded to match parser output. Built from the
    /// CLR's culture data so regional spellings stay correct, then topped up with the
    /// abbreviations receipt printers actually use.
    /// </summary>
    private static Dictionary<string, int> BuildMonthNames()
    {
        var names = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var cultureName in new[] { "en-CA", "en-US", "fr-CA", "fr-FR" })
        {
            DateTimeFormatInfo format;
            try
            {
                format = CultureInfo.GetCultureInfo(cultureName).DateTimeFormat;
            }
            catch (CultureNotFoundException)
            {
                continue;
            }

            AddRange(names, format.MonthNames);
            AddRange(names, format.AbbreviatedMonthNames);
            AddRange(names, format.MonthGenitiveNames);
        }

        // Printer abbreviations that culture data does not always carry.
        foreach (var (alias, month) in new (string, int)[]
                 {
                     ("jan", 1), ("janv", 1),
                     ("feb", 2), ("fev", 2), ("fevr", 2),
                     ("mar", 3), ("mars", 3),
                     ("apr", 4), ("avr", 4), ("avril", 4),
                     ("may", 5), ("mai", 5),
                     ("jun", 6), ("juin", 6),
                     ("jul", 7), ("juil", 7), ("juill", 7),
                     ("aug", 8), ("aou", 8), ("aout", 8),
                     ("sep", 9), ("sept", 9),
                     ("oct", 10),
                     ("nov", 11),
                     ("dec", 12), ("decembre", 12),
                 })
        {
            names[alias] = month;
        }

        return names;
    }

    private static void AddRange(Dictionary<string, int> names, string[] monthNames)
    {
        for (var i = 0; i < monthNames.Length; i++)
        {
            var folded = TextNormalizer.Fold(monthNames[i]).TrimEnd('.');
            if (folded.Length >= 3)
            {
                names[folded] = i + 1;
            }
        }
    }
}

/// <summary>A date found on a receipt, with a flag for day/month order that had to be guessed.</summary>
public readonly record struct ReceiptDateMatch(DateOnly Date, bool IsAmbiguous);
