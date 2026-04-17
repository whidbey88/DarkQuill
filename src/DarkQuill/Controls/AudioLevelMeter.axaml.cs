using Avalonia;
using Avalonia.Controls;

namespace DarkQuill.Controls;

/// <summary>
/// A color-coded audio level meter that displays green, orange, or red
/// based on the current audio level threshold.
/// </summary>
public partial class AudioLevelMeter : UserControl
{
    /// <summary>
    /// Defines the <see cref="Level"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<AudioLevelMeter, double>(nameof(Level));

    /// <summary>
    /// Gets or sets the current audio level (0.0–1.0).
    /// </summary>
    public double Level
    {
        get => GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>
    /// Initializes the AudioLevelMeter control.
    /// </summary>
    public AudioLevelMeter()
    {
        InitializeComponent();
    }
}
