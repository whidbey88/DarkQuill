using Avalonia.Controls;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// Dialog for selecting the Whisper GGML model used for transcription.
/// </summary>
public partial class ModelSelectionDialog : Window
{
    /// <summary>
    /// Initializes the model selection dialog.
    /// </summary>
    public ModelSelectionDialog()
    {
        InitializeComponent();

        Opened += async (_, _) =>
        {
            if (DataContext is ModelSelectionViewModel vm)
            {
                vm.RequestClose = applied => Close(applied);
                await vm.LoadModelsCommand.ExecuteAsync(null);
            }
        };
    }
}
