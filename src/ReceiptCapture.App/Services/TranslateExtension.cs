using ReceiptCapture.Core.Resources;

namespace ReceiptCapture.App.Services;

/// <summary>
/// XAML markup extension for localized text: <c>Text="{loc:Translate Review_Title}"</c>.
/// </summary>
/// <remarks>
/// Resolved once when the page is created, which is why changing the language in
/// Settings rebuilds the shell rather than trying to update every label in place.
/// </remarks>
[ContentProperty(nameof(Key))]
[AcceptEmptyServiceProvider]
public sealed class TranslateExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => AppStrings.Get(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
