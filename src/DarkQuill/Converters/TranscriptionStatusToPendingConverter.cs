using System.Globalization;
using Avalonia.Data.Converters;
using DarkQuill.Models;

namespace DarkQuill.Converters;

/// <summary>
/// Returns <c>true</c> when the <see cref="TranscriptionStatus"/> is not
/// <see cref="TranscriptionStatus.Complete"/>, indicating the recording still
/// needs transcription. Used to show/hide the transcribe button.
/// </summary>
public class TranscriptionStatusToPendingConverter : IValueConverter
{
    /// <summary>
    /// Converts a <see cref="TranscriptionStatus"/> to a boolean indicating whether
    /// transcription is still pending (i.e., not yet complete).
    /// </summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TranscriptionStatus status)
            return true;

        return status != TranscriptionStatus.Complete;
    }

    /// <summary>
    /// Not supported.
    /// </summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
