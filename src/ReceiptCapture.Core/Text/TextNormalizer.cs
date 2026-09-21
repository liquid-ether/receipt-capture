using System.Globalization;
using System.Text;

namespace ReceiptCapture.Core.Text;

/// <summary>
/// Normalization helpers shared by the parser. Receipt OCR output is messy and
/// bilingual, so every keyword comparison happens on a folded form: lower case,
/// accents removed, punctuation reduced to spaces, whitespace collapsed.
/// That lets a single keyword list match "MONTANT DÛ", "Montant du" and "montant  dû".
/// </summary>
public static class TextNormalizer
{
    /// <summary>Space characters OCR emits that are not plain U+0020.</summary>
    public static readonly char[] SpaceCharacters =
    [
        ' ', '\u00A0', '\u2007', '\u2009', '\u202F', '\t',
    ];

    /// <summary>
    /// Folds a line for keyword matching: lower case, accents stripped, hyphens and
    /// punctuation turned into spaces, runs of whitespace collapsed to one space.
    /// </summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true; // suppresses a leading space

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue; // the accent of a decomposed character
            }

            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == ',' || ch == '%' || ch == '$')
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// A second matching surface for lines that print tax codes as dotted acronyms
    /// ("T.P.S.", "T.V.Q."). Drops a period only when it follows a letter and is not
    /// part of a number, so "t.p.s. 1.50" folds to "tps 1.50".
    /// </summary>
    public static string FoldAcronyms(string? text)
    {
        var folded = Fold(text);
        if (folded.IndexOf('.') < 0)
        {
            return folded;
        }

        var builder = new StringBuilder(folded.Length);
        for (var i = 0; i < folded.Length; i++)
        {
            var ch = folded[i];
            if (ch == '.' && i > 0 && char.IsLetter(folded[i - 1]))
            {
                var nextIsDigit = i + 1 < folded.Length && char.IsDigit(folded[i + 1]);
                if (!nextIsDigit)
                {
                    continue;
                }
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>Collapses whitespace without changing case or accents. Used for display values.</summary>
    public static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var lastWasSpace = true;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || Array.IndexOf(SpaceCharacters, ch) >= 0)
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// True when <paramref name="foldedLine"/> contains <paramref name="foldedKeyword"/>
    /// as whole words, so "tax" does not match "taxi" and "tps" does not match "tpsx".
    /// </summary>
    public static bool ContainsWord(string foldedLine, string foldedKeyword)
    {
        if (foldedLine.Length == 0 || foldedKeyword.Length == 0)
        {
            return false;
        }

        var index = 0;
        while (true)
        {
            index = foldedLine.IndexOf(foldedKeyword, index, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var startsCleanly = index == 0 || !IsWordCharacter(foldedLine[index - 1]);
            var endIndex = index + foldedKeyword.Length;
            var endsCleanly = endIndex >= foldedLine.Length || !IsWordCharacter(foldedLine[endIndex]);

            if (startsCleanly && endsCleanly)
            {
                return true;
            }

            index += 1;
        }
    }

    /// <summary>True when any of <paramref name="foldedKeywords"/> appears as a whole word.</summary>
    public static bool ContainsAnyWord(string foldedLine, IReadOnlyList<string> foldedKeywords)
    {
        for (var i = 0; i < foldedKeywords.Count; i++)
        {
            if (ContainsWord(foldedLine, foldedKeywords[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordCharacter(char ch) => char.IsLetterOrDigit(ch);
}
