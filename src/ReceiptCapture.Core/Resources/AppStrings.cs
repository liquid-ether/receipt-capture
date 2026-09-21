using System.Globalization;
using System.Resources;

namespace ReceiptCapture.Core.Resources;

/// <summary>
/// Access to the English and French strings.
/// </summary>
/// <remarks>
/// Lookups are by key rather than by generated property because most of them are
/// dynamic: the parser reports warnings as keys, categories are stored as codes,
/// and receipt statuses are an enum. One lookup path keeps all of those honest, and
/// <see cref="Missing"/> makes a forgotten translation obvious instead of silent.
/// </remarks>
public static class AppStrings
{
    private static readonly ResourceManager Manager = new(
        "ReceiptCapture.Core.Resources.Strings.AppResources",
        typeof(AppStrings).Assembly);

    /// <summary>
    /// Culture used for lookups. Null follows the device language, which is what the
    /// app does by default; Settings can override it so a French user on an English
    /// phone still gets French.
    /// </summary>
    public static CultureInfo? OverrideCulture { get; set; }

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or a bracketed key
    /// when it is missing so the gap shows up in testing rather than in production.
    /// </summary>
    public static string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return Manager.GetString(key, OverrideCulture ?? CultureInfo.CurrentUICulture) ?? Missing(key);
    }

    /// <summary>Localized string with <see cref="string.Format(IFormatProvider,string,object[])"/> arguments.</summary>
    public static string Format(string key, params object?[] args)
    {
        var template = Get(key);
        return args.Length == 0
            ? template
            : string.Format(OverrideCulture ?? CultureInfo.CurrentCulture, template, args);
    }

    /// <summary>Display name for a receipt status, e.g. "Waiting to send" / "En attente d'envoi".</summary>
    public static string ForStatus(Models.ReceiptStatus status) => Get("Status_" + status);

    /// <summary>Display name for an expense category code.</summary>
    public static string ForCategory(string? categoryCode) =>
        string.IsNullOrEmpty(categoryCode)
            ? string.Empty
            : Get(Models.ExpenseCategories.ResourceKey(categoryCode));

    private static string Missing(string key) => "[" + key + "]";
}
