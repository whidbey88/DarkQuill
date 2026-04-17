using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DarkQuill.Models;

namespace DarkQuill.Converters;

/// <summary>
/// Converts a <see cref="TranscriptionStatus"/> to the corresponding status chip background brush.
/// Colors match the theme's Info, Warning, and Success palette values.
/// </summary>
public class TranscriptionStatusToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush InfoBrush = new SolidColorBrush(Color.Parse("#42a5f5"));
    private static readonly ISolidColorBrush WarningBrush = new SolidColorBrush(Color.Parse("#ffb84d"));
    private static readonly ISolidColorBrush SuccessBrush = new SolidColorBrush(Color.Parse("#66bb6a"));

    /// <summary>
    /// Converts a <see cref="TranscriptionStatus"/> value to a status-colored brush.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TranscriptionStatus status)
            return null;

        return status switch
        {
            TranscriptionStatus.Pending => InfoBrush,
            TranscriptionStatus.Transcribing => WarningBrush,
            TranscriptionStatus.Complete => SuccessBrush,
            _ => InfoBrush
        };
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
