using System.Globalization;
using Avalonia.Data.Converters;

namespace DarkQuill.Converters;

/// <summary>
/// Returns true when the bound string value equals the converter parameter.
/// Used for tab visibility bindings (e.g., IsVisible when ActiveTab == "Select").
/// </summary>
public class StringEqualConverter : IValueConverter
{
    /// <summary>
    /// Converts a string value by comparing it to the parameter.
    /// </summary>
    /// <returns>True if the value equals the parameter (case-ordinal), otherwise false.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && parameter is string p)
        {
            return string.Equals(s, p, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Not supported — this is a one-way converter.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
