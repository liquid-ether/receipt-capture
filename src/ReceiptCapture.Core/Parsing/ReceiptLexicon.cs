namespace ReceiptCapture.Core.Parsing;

/// <summary>
/// The English and French vocabulary a receipt uses for its summary lines.
/// Every entry is already in folded form (lower case, no accents, hyphens as
/// spaces) so it can be compared directly against <see cref="Text.TextNormalizer.Fold"/>
/// output. Both languages live in one list on purpose: a receipt from a bilingual
/// province prints "Sous-total" and "Subtotal" on the same slip, and the phone's
/// own language tells us nothing about the paper in front of the camera.
/// </summary>
public static class ReceiptLexicon
{
    /// <summary>Amount before taxes. Checked before <see cref="Total"/> because "sous-total" contains "total".</summary>
    public static IReadOnlyList<string> Subtotal { get; } =
    [
        "subtotal", "sub total",
        "total before tax", "total before taxes", "amount before tax", "net amount",
        "sous total", "soustotal",
        "total avant taxes", "total avant taxe", "montant avant taxes",
    ];

    /// <summary>
    /// Specific tax codes, most specific first so "TVQ" is not swallowed by the
    /// generic "taxe". Canadian, French and generic VAT codes.
    /// </summary>
    public static IReadOnlyList<string> TaxCodes { get; } =
    [
        "hst", "gst", "qst", "pst", "rst",
        "tvh", "tvq", "tps", "tvp", "tva",
        "vat",
    ];

    /// <summary>Generic tax wording used when no specific code is printed.</summary>
    public static IReadOnlyList<string> GenericTax { get; } =
    [
        "sales tax", "tax", "taxes", "taxe",
    ];

    public static IReadOnlyList<string> Tip { get; } =
    [
        "tip", "tips", "gratuity", "gratuities",
        "pourboire", "pourboires",
        "service charge", "frais de service",
    ];

    /// <summary>Grand-total wording. Checked last so the more specific lists win.</summary>
    public static IReadOnlyList<string> Total { get; } =
    [
        "grand total", "total due", "amount due", "balance due", "total amount",
        "total paid", "total sale", "total",
        "montant du", "montant total", "total general", "total a payer",
        "net a payer", "somme due", "a payer",
    ];

    /// <summary>
    /// Tender and card lines. Their amounts duplicate the total (or are change given
    /// back), so they are excluded from classification entirely. "balance" is
    /// deliberately absent: "balance due" is a total, not a tender line.
    /// </summary>
    public static IReadOnlyList<string> Tender { get; } =
    [
        "change", "change due", "cash", "cash tendered", "amount tendered", "tendered",
        "visa", "mastercard", "master card", "amex", "american express", "discover",
        "interac", "debit", "credit card", "card payment", "payment", "paid by",
        "comptant", "monnaie", "rendu", "especes", "paiement", "carte de credit",
        "carte de debit", "approved", "approuve", "auth code", "no autor",
    ];

    /// <summary>
    /// Boilerplate that is never a merchant name. Used only when guessing which of
    /// the first few lines carries the business name.
    /// </summary>
    public static IReadOnlyList<string> MerchantNoise { get; } =
    [
        "receipt", "invoice", "tax invoice", "bill", "order", "table", "server",
        "cashier", "customer copy", "merchant copy", "duplicate", "welcome",
        "thank you", "thanks", "tel", "telephone", "fax", "www", "http",
        "facture", "recu", "addition", "commande", "serveur", "serveuse",
        "caissier", "caissiere", "copie client", "copie marchand", "bienvenue",
        "merci", "magasin", "succursale", "terminal", "transaction",
    ];
}
