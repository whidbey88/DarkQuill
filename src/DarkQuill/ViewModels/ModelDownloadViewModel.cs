using System.Diagnostics;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the model download dialog. Downloads the Whisper GGML base and
/// large-v3-turbo models from Hugging Face to the configured models folder.
/// </summary>
public partial class ModelDownloadViewModel : ObservableObject
{
    /// <summary>
    /// Hugging Face download URL for ggml-base.bin.
    /// </summary>
    private const string BaseModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    /// <summary>
    /// Hugging Face download URL for ggml-large-v3-turbo.bin.
    /// </summary>
    private const string LargeModelUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo.bin";

    /// <summary>
    /// Filename of the base model.
    /// </summary>
    private const string BaseModelFileName = "ggml-base.bin";

    /// <summary>
    /// Filename of the large-v3-turbo model.
    /// </summary>
    private const string LargeModelFileName = "ggml-large-v3-turbo.bin";

    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Whether the download is currently in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>
    /// Whether the download has completed successfully.
    /// </summary>
    [ObservableProperty]
    private bool _isComplete;

    /// <summary>
    /// Whether an error occurred during download.
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// Status message displayed to the user.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Two Whisper models are required for transcription. Click Download to get started.";

    /// <summary>
    /// Display name of the model currently being downloaded.
    /// </summary>
    [ObservableProperty]
    private string _currentModelName = string.Empty;

    /// <summary>
    /// Download progress for the base model (0–100).
    /// </summary>
    [ObservableProperty]
    private double _baseModelProgress;

    /// <summary>
    /// Download progress for the large model (0–100).
    /// </summary>
    [ObservableProperty]
    private double _largeModelProgress;

    /// <summary>
    /// Human-readable status for the base model download.
    /// </summary>
    [ObservableProperty]
    private string _baseModelStatus = "Pending";

    /// <summary>
    /// Human-readable status for the large model download.
    /// </summary>
    [ObservableProperty]
    private string _largeModelStatus = "Pending";

    /// <summary>
    /// The destination folder for downloaded models.
    /// </summary>
    [ObservableProperty]
    private string _modelsFolder = string.Empty;

    /// <summary>
    /// Whether the user cancelled without downloading (signals app shutdown).
    /// </summary>
    [ObservableProperty]
    private bool _wasCancelled;

    /// <summary>
    /// Callback to close the dialog. The boolean parameter indicates whether
    /// models were successfully downloaded (true) or the user cancelled (false).
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    /// <summary>
    /// Initializes the model download ViewModel with the settings service.
    /// </summary>
    /// <param name="settingsService">Settings service for reading model folder configuration.</param>
    public ModelDownloadViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// Loads the models folder path from settings for display.
    /// </summary>
    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
        ModelsFolder = settings.WhisperModelsFolder;
    }

    /// <summary>
    /// Downloads both Whisper models to the configured models folder.
    /// The base model is downloaded first, followed by the large-v3-turbo model.
    /// On completion, the base model is set as the selected model in settings.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadModelsAsync()
    {
        IsDownloading = true;
        HasError = false;
        _cts = new CancellationTokenSource();

        try
        {
            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            var folder = settings.WhisperModelsFolder;

            // Ensure the models folder exists.
            Directory.CreateDirectory(folder);

            // Download base model.
            CurrentModelName = "Base Model";
            BaseModelStatus = "Downloading...";
            StatusMessage = "Downloading ggml-base.bin...";

            await DownloadFileAsync(
                BaseModelUrl,
                Path.Combine(folder, BaseModelFileName),
                progress => BaseModelProgress = progress,
                _cts.Token).ConfigureAwait(true);

            BaseModelStatus = "Complete";
            BaseModelProgress = 100;

            // Download large-v3-turbo model.
            CurrentModelName = "Large v3 Turbo";
            LargeModelStatus = "Downloading...";
            StatusMessage = "Downloading ggml-large-v3-turbo.bin...";

            await DownloadFileAsync(
                LargeModelUrl,
                Path.Combine(folder, LargeModelFileName),
                progress => LargeModelProgress = progress,
                _cts.Token).ConfigureAwait(true);

            LargeModelStatus = "Complete";
            LargeModelProgress = 100;

            // Set the base model as the default selected model.
            settings.SelectedWhisperModel = BaseModelFileName;
            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(true);

            StatusMessage = "Both models downloaded successfully. Base model set as default.";
            IsComplete = true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Download cancelled.";
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Download failed: {ex.Message}";
            Debug.WriteLine($"Model download error: {ex}");
        }
        finally
        {
            IsDownloading = false;
            _cts?.Dispose();
            _cts = null;
            DownloadModelsCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Cancels the current download operation and closes the dialog.
    /// Signals that the user chose not to download, which will shut down the application.
    /// </summary>
    [RelayCommand]
    private void CancelAndExit()
    {
        _cts?.Cancel();
        WasCancelled = true;
        RequestClose?.Invoke(false);
    }

    /// <summary>
    /// Closes the dialog after a successful download.
    /// </summary>
    [RelayCommand]
    private void Done()
    {
        RequestClose?.Invoke(true);
    }

    /// <summary>
    /// Whether the download command can execute. Disabled while a download is in progress.
    /// </summary>
    private bool CanDownload() => !IsDownloading;

    /// <summary>
    /// Downloads a file from a URL to a local path with progress reporting.
    /// Uses streaming to handle large files without excessive memory usage.
    /// </summary>
    /// <param name="url">Source URL.</param>
    /// <param name="destinationPath">Local file path to save to.</param>
    /// <param name="progressCallback">Callback receiving download progress (0–100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        Action<double> progressCallback,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        long downloadedBytes = 0;

        // Write to a temporary file first, then rename on success.
        var tempPath = destinationPath + ".tmp";

        try
        {
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);

                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (double)downloadedBytes / totalBytes * 100;
                    progressCallback(percent);
                }
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Clean up partial temp file on failure.
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            throw;
        }

        // Rename temp file to final destination (overwrite if re-downloading).
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        File.Move(tempPath, destinationPath);
    }
}
