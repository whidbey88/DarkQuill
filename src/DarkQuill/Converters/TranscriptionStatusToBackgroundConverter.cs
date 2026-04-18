using System.Globalization;
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
    /// <summary>
    /// Semi-transparent brush for completed recordings.
    /// </summary>
    private static readonly SolidColorBrush CompleteBrush = new(Color.Parse("#A019191d"));

    /// <summary>
    /// Semi-transparent brush for pending recordings.
    /// </summary>
    private static readonly SolidColorBrush PendingBrush = new(Color.Parse("#A025252b"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TranscriptionStatus status && status == TranscriptionStatus.Complete)
        {
            return CompleteBrush;
        }

        return PendingBrush;
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
