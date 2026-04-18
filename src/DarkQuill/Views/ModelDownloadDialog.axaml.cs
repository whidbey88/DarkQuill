using Avalonia.Controls;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// Dialog for downloading Whisper GGML models when none are available locally.
/// </summary>
public partial class ModelDownloadDialog : Window
{
    /// <summary>
    /// Initializes the model download dialog.
    /// </summary>
    public ModelDownloadDialog()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is ModelDownloadViewModel vm)
            {
                vm.RequestClose = downloaded => Close(downloaded);
                await vm.LoadSettingsCommand.ExecuteAsync(null);
            }
        };
    }
}
