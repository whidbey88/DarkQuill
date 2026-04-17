using System.Globalization;
using Avalonia.Data.Converters;

namespace DarkQuill.Converters;

/// <summary>
/// Converts an audio level (0.0–1.0) to a percentage string for width binding.
/// Used by the AudioLevelMeter to scale the filled bar proportionally.
/// </summary>
public class LevelToWidthConverter : IValueConverter
{
    /// <summary>
    /// Converts a double level value (0.0–1.0) to a percentage value (0–100) for ProgressBar.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double level)
        {
            return Math.Clamp(level * 100.0, 0.0, 100.0);
        }

        return 0.0;
    }

    /// <summary>
    /// Not supported — this is a one-way converter.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
