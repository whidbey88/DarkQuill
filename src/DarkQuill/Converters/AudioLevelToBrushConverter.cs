using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DarkQuill.Converters;

/// <summary>
/// Converts an audio level (0.0–1.0) to a color-coded brush for the VU meter.
/// Green below 60%, orange 60–80%, red above 80%.
/// </summary>
public class AudioLevelToBrushConverter : IValueConverter
{
    /// <summary>
    /// Converts a double level value to the appropriate VU meter brush.
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double level)
        {
            var resourceKey = level switch
            {
                >= 0.8 => "VULevelHighBrush",
                >= 0.6 => "VULevelMediumBrush",
                _ => "VULevelLowBrush"
            };

            if (Application.Current is { } app
                && app.Resources.TryGetResource(resourceKey, app.ActualThemeVariant, out var resource)
                && resource is IBrush brush)
            {
                return brush;
            }
        }

        return new SolidColorBrush(Color.Parse("#66bb6a"));
    }

    /// <summary>
    /// Not supported — this is a one-way converter.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
