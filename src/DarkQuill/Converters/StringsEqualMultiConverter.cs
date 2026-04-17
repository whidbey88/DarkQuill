using System.Globalization;
using Avalonia.Data.Converters;

namespace DarkQuill.Converters;

/// <summary>
/// Multi-value converter that returns true when two bound string values are equal (case-insensitive).
/// Used to compare a recording's FileName to the currently playing file name.
/// </summary>
public class StringsEqualMultiConverter : IMultiValueConverter
{
    /// <summary>
    /// Returns true when both bound values are equal strings (case-insensitive ordinal comparison).
    /// When <paramref name="parameter"/> is "negate", returns the inverse (true when NOT equal or either is null).
    /// </summary>
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return parameter is string p && string.Equals(p, "negate", StringComparison.OrdinalIgnoreCase);
        }

        string? a = values[0] as string;
        string? b = values[1] as string;

        bool areEqual = a is not null && b is not null
            && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        bool negate = parameter is string param && string.Equals(param, "negate", StringComparison.OrdinalIgnoreCase);

        return negate ? !areEqual : areEqual;
    }
}
