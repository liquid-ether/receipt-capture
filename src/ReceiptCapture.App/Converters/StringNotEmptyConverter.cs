using System.Globalization;

namespace ReceiptCapture.App.Converters;

/// <summary>
/// Shows a message row only when there is a message. Keeps the pages free of
/// duplicated "HasError"/"HasStatus" properties on every view model.
/// </summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
