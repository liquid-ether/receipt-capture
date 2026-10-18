using ReceiptCapture.Core.Models;
using ReceiptCapture.Core.Text;

namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// Turns the lines an OCR engine read off a receipt into structured fields.
/// </summary>
/// <remarks>
/// The parser is deliberately independent of the device's language. A receipt is a
/// physical object that does not know what the phone is set to, so classification
/// works off a combined English/French vocabulary and number formats are resolved
/// from the shape of each number rather than from a culture. Input is expected in
/// reading order, top of the slip first, which is what Vision returns.
/// </remarks>
public static class ReceiptTextParser
{
    public static ParsedReceipt Parse(IEnumerable<string> lines, ReceiptParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        options ??= ReceiptParseOptions.Default;

        var cleaned = lines
            .Select(TextNormalizer.CollapseWhitespace)
            .Where(line => line.Length > 0)
            .ToList();

        var warnings = new List<string>();
        var summary = ReadSummaryLines(cleaned);

        var merchant = FindMerchant(cleaned, options);
        if (merchant is null)
        {
            warnings.Add(ParseWarnings.MerchantNotFound);
        }

        DateOnly? date = null;
        if (ReceiptDateParser.TryFindInLines(cleaned, options, out var dateMatch))
        {
            date = dateMatch.Date;
            if (dateMatch.IsAmbiguous)
            {
                warnings.Add(ParseWarnings.DateAmbiguous);
            }
        }
        else
        {
            warnings.Add(ParseWarnings.DateNotFound);
        }

        // A "Total des taxes" line is a roll-up of the individual tax lines. Keep it
        // only when the individual lines are missing, otherwise taxes are double counted.
        var taxes = summary.Taxes;
        if (taxes.Count == 0 && summary.TaxSummary is { } rolledUp)
        {
            taxes = [new ReceiptTax("TAX", rolledUp)];
        }

        var taxTotal = taxes.Sum(t => t.Amount);
        var total = summary.Total;

        if (total is null)
        {
            if (summary.Subtotal is { } subtotalOnly)
            {
                total = subtotalOnly + taxTotal + (summary.Tip ?? 0m);
                warnings.Add(ParseWarnings.TotalComputed);
            }
            else
            {
                warnings.Add(ParseWarnings.TotalNotFound);
            }
        }
        else if (summary.Subtotal is { } subtotal)
        {
            var expected = subtotal + taxTotal + (summary.Tip ?? 0m);
            if (Math.Abs(expected - total.Value) > 0.02m)
            {
                warnings.Add(ParseWarnings.TotalsDoNotReconcile);
            }
        }

        return new ParsedReceipt
        {
            MerchantName = merchant,
            TransactionDate = date,
            Currency = DetectCurrency(cleaned, taxes, options),
            Subtotal = summary.Subtotal,
            Tip = summary.Tip,
            Total = total,
            Taxes = taxes,
            Warnings = warnings,
            Confidence = total is null
                ? ParseConfidence.Low
                : warnings.Count == 0
                    ? ParseConfidence.High
                    : ParseConfidence.Medium,
        };
    }

    /// <summary>
    /// Single pass over the slip classifying each line by the keyword it carries.
    /// Order matters: "sous-total" contains "total", and "total des taxes" contains
    /// both "total" and "taxes", so the most specific interpretation is tested first.
    /// </summary>
    private static SummaryLines ReadSummaryLines(List<string> cleaned)
    {
        var taxes = new List<ReceiptTax>();
        var totalCandidates = new List<decimal>();
        decimal? subtotal = null;
        decimal? tip = null;
        decimal? taxSummary = null;

        for (var i = 0; i < cleaned.Count; i++)
        {
            var folded = TextNormalizer.FoldAcronyms(cleaned[i]);
            if (folded.Length == 0)
            {
                continue;
            }

            // Card and change lines repeat the total or hand money back; neither is ours.
            if (TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Tender))
            {
                continue;
            }

            var isSubtotal = TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Subtotal);
            var taxCode = FindTaxCode(folded);
            var isTax = taxCode is not null || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.GenericTax);
            var isTip = TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Tip);
            var isTotal = TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Total);

            if (!isSubtotal && !isTax && !isTip && !isTotal)
            {
                continue;
            }

            if (!TryReadAmountFor(cleaned, i, out var amount))
            {
                continue;
            }

            if (isSubtotal)
            {
                subtotal ??= amount;
            }
            else if (isTax && isTotal)
            {
                taxSummary ??= amount;
            }
            else if (isTax)
            {
                AmountParser.TryExtractRate(cleaned[i], out var rate);
                taxes.Add(new ReceiptTax(taxCode ?? "TAX", amount, rate > 0m ? rate : null));
            }
            else if (isTip)
            {
                tip ??= amount;
            }
            else
            {
                totalCandidates.Add(amount);
            }
        }

        return new SummaryLines(
            subtotal,
            tip,
            taxes,
            taxSummary,
            // Slips repeat the total ("TOTAL", then "TOTAL DUE"); the grand total is
            // the largest of them, and never smaller than a line-item count.
            totalCandidates.Count > 0 ? totalCandidates.Max() : null);
    }

    /// <summary>
    /// Reads the amount belonging to the keyword on line <paramref name="index"/>.
    /// Receipts normally print "LABEL .... 12.34" on one line, but a narrow slip can
    /// wrap the amount onto the next line, so that fallback is checked too.
    /// </summary>
    private static bool TryReadAmountFor(List<string> lines, int index, out decimal amount)
    {
        if (AmountParser.TryExtractLast(lines[index], out amount))
        {
            return true;
        }

        if (index + 1 < lines.Count
            && IsAmountOnly(lines[index + 1])
            && AmountParser.TryExtractLast(lines[index + 1], out amount))
        {
            return true;
        }

        amount = 0m;
        return false;
    }

    /// <summary>True when the line carries a number and nothing else of substance.</summary>
    private static bool IsAmountOnly(string line)
    {
        var sawDigit = false;

        foreach (var ch in line)
        {
            if (char.IsLetter(ch))
            {
                return false;
            }

            if (char.IsDigit(ch))
            {
                sawDigit = true;
            }
        }

        return sawDigit;
    }

    /// <summary>True when the line carries any of the vocabulary of a receipt's summary block.</summary>
    private static bool IsSummaryLine(string folded) =>
        TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Total)
        || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Subtotal)
        || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.GenericTax)
        || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.TaxCodes)
        || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Tip)
        || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.Tender);

    /// <summary>Returns the printed tax code in upper case, or null when the line names no specific tax.</summary>
    private static string? FindTaxCode(string folded)
    {
        foreach (var code in ReceiptLexicon.TaxCodes)
        {
            if (TextNormalizer.ContainsWord(folded, code))
            {
                return code.ToUpperInvariant();
            }
        }

        return null;
    }

    /// <summary>
    /// Picks the merchant name from the top of the slip: the first line that reads
    /// like a business name rather than an address, a phone number, a date or boilerplate.
    /// </summary>
    private static string? FindMerchant(List<string> cleaned, ReceiptParseOptions options)
    {
        var limit = Math.Min(cleaned.Count, options.MerchantSearchLineCount);

        for (var i = 0; i < limit; i++)
        {
            var candidate = cleaned[i].Trim(TrimmableDecoration);
            if (candidate.Length == 0)
            {
                continue;
            }

            var folded = TextNormalizer.FoldAcronyms(candidate);
            if (folded.Length < 3 || TextNormalizer.ContainsAnyWord(folded, ReceiptLexicon.MerchantNoise))
            {
                continue;
            }

            // A short slip can put "TOTAL 9,99" within the first few lines; a summary
            // or tender line is never the business name.
            if (IsSummaryLine(folded))
            {
                continue;
            }

            var letters = candidate.Count(char.IsLetter);
            var digits = candidate.Count(char.IsDigit);

            // A business name is mostly letters; addresses and phone numbers are not.
            if (letters < 3 || digits > letters)
            {
                continue;
            }

            if (ReceiptDateParser.TryFind(candidate, options, out _))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string DetectCurrency(
        List<string> cleaned,
        IReadOnlyList<ReceiptTax> taxes,
        ReceiptParseOptions options)
    {
        var joined = string.Join(' ', cleaned);
        var folded = TextNormalizer.Fold(joined);

        if (joined.Contains('€') || TextNormalizer.ContainsWord(folded, "eur"))
        {
            return "EUR";
        }

        if (TextNormalizer.ContainsWord(folded, "usd"))
        {
            return "USD";
        }

        if (TextNormalizer.ContainsWord(folded, "cad"))
        {
            return "CAD";
        }

        // Tax codes identify the jurisdiction more reliably than a bare "$" does.
        foreach (var tax in taxes)
        {
            if (CanadianTaxCodes.Contains(tax.Code))
            {
                return "CAD";
            }

            if (tax.Code is "TVA")
            {
                return "EUR";
            }
        }

        return options.DefaultCurrency;
    }

    private static readonly HashSet<string> CanadianTaxCodes =
        new(StringComparer.Ordinal) { "GST", "HST", "QST", "PST", "RST", "TPS", "TVQ", "TVH", "TVP" };

    private static readonly char[] TrimmableDecoration = ['*', '-', '=', '~', '_', '.', ' ', '#'];

    private readonly record struct SummaryLines(
        decimal? Subtotal,
        decimal? Tip,
        List<ReceiptTax> Taxes,
        decimal? TaxSummary,
        decimal? Total);
}
