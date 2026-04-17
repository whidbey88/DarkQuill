using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the Whisper model selection dialog. Enumerates available GGML models
/// from configured folders and allows the user to select one for transcription.
/// </summary>
public partial class ModelSelectionViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// Available model filenames discovered in the models folders.
    /// </summary>
    public ObservableCollection<string> AvailableModels { get; } = [];

    /// <summary>
    /// The currently selected model filename.
    /// </summary>
    [ObservableProperty]
    private string? _selectedModel;

    /// <summary>
    /// Display-only path of the models folder from settings.
    /// </summary>
    [ObservableProperty]
    private string _modelsFolder = string.Empty;

    /// <summary>
    /// Whether the dialog is currently scanning for models.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Status or error message displayed to the user.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Loading models...";

    /// <summary>
    /// Callback to close the dialog. Set by the dialog code-behind.
    /// True indicates the user applied changes; false indicates cancellation.
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    /// <summary>
    /// Initializes the model selection ViewModel with required services.
    /// </summary>
    /// <param name="transcriptionService">Transcription service for enumerating available models.</param>
    /// <param name="settingsService">Settings service for reading/writing model selection.</param>
    public ModelSelectionViewModel(ITranscriptionService transcriptionService, ISettingsService settingsService)
    {
        _transcriptionService = transcriptionService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Scans configured model folders for available GGML models and populates the list.
    /// Restores the previously saved model selection.
    /// </summary>
    [RelayCommand]
    private async Task LoadModelsAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Scanning for models...";

            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            ModelsFolder = settings.WhisperModelsFolder;

            var models = await _transcriptionService.GetAvailableModelsAsync().ConfigureAwait(true);

            AvailableModels.Clear();
            foreach (var model in models)
            {
                AvailableModels.Add(model);
            }

            if (AvailableModels.Count == 0)
            {
                StatusMessage = "No models found. Download a GGML model file and place it in the models folder above.";
                return;
            }

            // Try to select the previously saved model.
            if (!string.IsNullOrEmpty(settings.SelectedWhisperModel) &&
                AvailableModels.Contains(settings.SelectedWhisperModel))
            {
                SelectedModel = settings.SelectedWhisperModel;
            }
            else
            {
                SelectedModel = AvailableModels[0];
            }

            StatusMessage = $"{AvailableModels.Count} model(s) found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning for models: {ex.Message}";
            Debug.WriteLine($"Error loading models: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Saves the selected model to settings and closes the dialog.
    /// </summary>
    [RelayCommand]
    private async Task ApplyAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            settings.SelectedWhisperModel = SelectedModel ?? string.Empty;
            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(true);

            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
            Debug.WriteLine($"Error saving model selection: {ex}");
        }
    }

    /// <summary>
    /// Closes the dialog without saving changes.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}
