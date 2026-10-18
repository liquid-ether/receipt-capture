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
/// a bracketed key makes a forgotten translation obvious instead of silent.
/// </remarks>
public static class AppStrings
{
    private const string BaseName = "ReceiptCapture.Core.Resources.Strings.AppResources";

    private static readonly ResourceManager Manager = new(BaseName, typeof(AppStrings).Assembly);

    /// <summary>
    /// Culture used when a caller does not name one.
    /// </summary>
    /// <remarks>
    /// Intended for single-user clients that let someone override the device
    /// language. A server must leave this null and set
    /// <see cref="CultureInfo.CurrentUICulture"/> per request instead — it is
    /// async-local, so it keeps concurrent requests in different languages apart,
    /// which a process-wide override could not.
    /// </remarks>
    public static CultureInfo? OverrideCulture { get; set; }

    /// <summary>Localized string for <paramref name="key"/> in the ambient culture.</summary>
    public static string Get(string key) => Get(key, culture: null);

    /// <summary>
    /// Localized string for <paramref name="key"/>, or a bracketed key when it is
    /// missing so the gap shows up in testing rather than in production.
    /// </summary>
    public static string Get(string key, CultureInfo? culture)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return Manager.GetString(key, Resolve(culture)) ?? Missing(key);
    }

    /// <summary>Localized string with <see cref="string.Format(IFormatProvider,string,object[])"/> arguments.</summary>
    public static string Format(string key, params object?[] args) => Format(key, null, args);

    /// <inheritdoc cref="Format(string, object?[])"/>
    public static string Format(string key, CultureInfo? culture, params object?[] args)
    {
        var template = Get(key, culture);
        return args.Length == 0 ? template : string.Format(Resolve(culture), template, args);
    }

    /// <summary>
    /// Every key and its translation for one culture, for a client that wants the
    /// whole table at once rather than a request per label.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetAll(CultureInfo? culture)
    {
        var resolved = Resolve(culture);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);

        // Each value is looked up in the requested culture, so a partially translated
        // satellite assembly falls back per key rather than dropping the key entirely.
        foreach (var key in Keys.Value)
        {
            strings[key] = Manager.GetString(key, resolved) ?? Missing(key);
        }

        return strings;
    }

    /// <summary>
    /// Every key in the neutral resources, read once.
    /// </summary>
    /// <remarks>
    /// The ResourceSet handed back here belongs to the ResourceManager and is cached
    /// by it, so it must not be disposed — doing so breaks every later lookup for
    /// that culture. Reading the names once and keeping only the strings avoids
    /// holding the set at all.
    /// </remarks>
    private static readonly Lazy<string[]> Keys = new(
        () =>
        {
            var set = Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true);
            if (set is null)
            {
                return [];
            }

            var names = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in set)
            {
                if (entry.Key is string key)
                {
                    names.Add(key);
                }
            }

            names.Sort(StringComparer.Ordinal);
            return [.. names];
        },
        isThreadSafe: true);

    /// <summary>Display name for a receipt status, e.g. "Waiting to send" / "En attente d'envoi".</summary>
    public static string ForStatus(Models.ReceiptStatus status) => ForStatus(status, culture: null);

    /// <inheritdoc cref="ForStatus(Models.ReceiptStatus)"/>
    public static string ForStatus(Models.ReceiptStatus status, CultureInfo? culture) =>
        Get("Status_" + status, culture);

    /// <summary>Display name for an expense category code.</summary>
    public static string ForCategory(string? categoryCode) => ForCategory(categoryCode, culture: null);

    /// <inheritdoc cref="ForCategory(string?)"/>
    public static string ForCategory(string? categoryCode, CultureInfo? culture) =>
        string.IsNullOrEmpty(categoryCode)
            ? string.Empty
            : Get(Models.ExpenseCategories.ResourceKey(categoryCode), culture);

    private static CultureInfo Resolve(CultureInfo? culture) =>
        culture ?? OverrideCulture ?? CultureInfo.CurrentUICulture;

    private static string Missing(string key) => "[" + key + "]";
}
