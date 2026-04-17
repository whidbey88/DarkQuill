using Avalonia.Controls;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// Dialog for configuring audio input device and levels.
/// </summary>
public partial class AudioSettingsDialog : Window
{
    /// <summary>
    /// Initializes the audio settings dialog.
    /// </summary>
    public AudioSettingsDialog()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is AudioSettingsViewModel vm)
            {
                vm.RequestClose = applied => Close(applied);
                await vm.LoadDevicesCommand.ExecuteAsync(null);
            }
        };
    }
}
