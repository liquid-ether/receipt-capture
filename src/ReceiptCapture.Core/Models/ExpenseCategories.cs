namespace ReceiptCapture.Core.Models;

/// <summary>
/// The default expense categories offered on the review screen. Codes are stable and
/// culture-independent; the display names live in the localization resources
/// (<c>Category_&lt;Code&gt;</c>) so they render in English or French.
/// </summary>
public static class ExpenseCategories
{
    public const string Meals = "MEALS";
    public const string Travel = "TRAVEL";
    public const string Lodging = "LODGING";
    public const string Fuel = "FUEL";
    public const string OfficeSupplies = "OFFICE_SUPPLIES";
    public const string Software = "SOFTWARE";
    public const string Telecom = "TELECOM";
    public const string Entertainment = "ENTERTAINMENT";
    public const string ProfessionalServices = "PROFESSIONAL_SERVICES";
    public const string Other = "OTHER";

    public static IReadOnlyList<string> All { get; } =
    [
        Meals,
        Travel,
        Lodging,
        Fuel,
        OfficeSupplies,
        Software,
        Telecom,
        Entertainment,
        ProfessionalServices,
        Other,
    ];

    /// <summary>Resource key holding the localized display name for <paramref name="code"/>.</summary>
    public static string ResourceKey(string code) => "Category_" + code;
}
