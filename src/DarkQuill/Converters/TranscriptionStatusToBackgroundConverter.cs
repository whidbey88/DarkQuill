using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DarkQuill.Models;

namespace DarkQuill.Converters;

/// <summary>
/// Converts a <see cref="TranscriptionStatus"/> to a background brush for recording items.
/// Returns a darker brush for completed transcriptions to visually distinguish them
/// from pending recordings.
/// </summary>
public class TranscriptionStatusToBackgroundConverter : IValueConverter
{
    /// <summary>
    /// Converts a <see cref="TranscriptionStatus"/> to a background brush.
    /// Completed recordings get a darker background; all others get the standard background.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TranscriptionStatus status && status == TranscriptionStatus.Complete)
        {
            if (Application.Current!.TryFindResource("SurfaceContainerHighDarkBrush", out var darkBrush))
            {
                return darkBrush;
            }
        }

        if (Application.Current!.TryFindResource("SurfaceContainerHighBrush", out var normalBrush))
        {
            return normalBrush;
        }

        return null;
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
