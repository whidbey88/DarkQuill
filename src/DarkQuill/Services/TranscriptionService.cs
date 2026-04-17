using System.Diagnostics;
using Whisper.net;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Runs Whisper inference on WAV files using Whisper.net.
/// The model is loaded from a configured models folder (no automatic downloading).
/// The <see cref="WhisperFactory"/> and <see cref="WhisperProcessor"/> are loaded once
/// and held in memory as a singleton. GPU (CUDA) acceleration is attempted first
/// with automatic CPU fallback.
/// </summary>
public class TranscriptionService : ITranscriptionService, IDisposable
{
    /// <summary>
    /// Default model filename used when no specific model is requested.
    /// </summary>
    private const string DefaultModelFileName = "ggml-base.bin";

    /// <summary>
    /// Duration threshold after which a performance warning is logged.
    /// </summary>
    private static readonly TimeSpan SlowTranscriptionThreshold = TimeSpan.FromMinutes(2);

    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private string? _loadedModelFileName;
    private bool _disposed;
    private bool _usingGpu;

    /// <summary>
    /// Initializes a new instance of the <see cref="TranscriptionService"/> class.
    /// </summary>
    /// <param name="settingsService">Settings service for reading model folder configuration.</param>
    public TranscriptionService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public bool IsInitialized => _processor is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Resolves the model file from configured folders. Does not download models automatically.
    /// Attempts GPU (CUDA) acceleration first; falls back to CPU if CUDA is unavailable.
    /// If a different model is requested than what is currently loaded, the old model is
    /// disposed and the new one loaded.
    /// </remarks>
    /// <exception cref="TranscriptionException">Thrown when the model cannot be found or loaded.</exception>
    public async Task InitializeAsync(string? modelFileName = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var requestedModel = string.IsNullOrEmpty(modelFileName) ? DefaultModelFileName : modelFileName;

        // If already loaded with the same model, return immediately.
        if (IsInitialized && string.Equals(_loadedModelFileName, requestedModel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring the lock.
            if (IsInitialized && string.Equals(_loadedModelFileName, requestedModel, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // If a different model is loaded, dispose it first.
            if (IsInitialized)
            {
                Debug.WriteLine($"Switching model from '{_loadedModelFileName}' to '{requestedModel}'.");
                _processor?.Dispose();
                _processor = null;
                _factory?.Dispose();
                _factory = null;
                _loadedModelFileName = null;
            }

            string modelPath = await ResolveModelPathAsync(requestedModel, cancellationToken).ConfigureAwait(false);
            LoadModel(modelPath);
            _loadedModelFileName = requestedModel;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TranscriptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TranscriptionException($"Failed to initialize Whisper model: {ex.Message}", ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var searchDirs = GetSearchDirectories(settings);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var models = new List<string>();

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.bin"))
            {
                var fileName = Path.GetFileName(file);
                if (seen.Add(fileName))
                {
                    models.Add(fileName);
                }
            }
        }

        return models.AsReadOnly();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Lazily initializes the model if not already loaded. Runs Whisper inference on a
    /// background thread via <see cref="Task.Run"/> to keep the UI thread responsive.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="wavFilePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the WAV file does not exist.</exception>
    /// <exception cref="TranscriptionException">Thrown when inference fails.</exception>
    public async Task<TranscriptionResult> TranscribeAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(wavFilePath);

        if (!File.Exists(wavFilePath))
        {
            throw new FileNotFoundException($"WAV file not found: '{wavFilePath}'", wavFilePath);
        }

        if (!wavFilePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"File is not a WAV file: '{wavFilePath}'");
        }

        // Lazy initialization — ensure model is loaded.
        // Only initialize if no model is loaded yet. When a specific model was already
        // loaded via an explicit InitializeAsync call (e.g., from MainViewModel reading
        // the user's selected model), we must not override it with the default model.
        if (!IsInitialized)
        {
            var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
            var fallbackModel = string.IsNullOrEmpty(settings.SelectedWhisperModel) ? null : settings.SelectedWhisperModel;
            await InitializeAsync(fallbackModel, cancellationToken).ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await Task.Run(() => RunInferenceAsync(wavFilePath, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            Debug.WriteLine($"Transcription completed in {stopwatch.Elapsed.TotalSeconds:F1}s: {wavFilePath}");

            if (stopwatch.Elapsed > SlowTranscriptionThreshold)
            {
                Debug.WriteLine($"WARNING: Transcription took {stopwatch.Elapsed.TotalSeconds:F1}s (>{SlowTranscriptionThreshold.TotalMinutes}m threshold). " +
                    $"GPU={_usingGpu}, File={wavFilePath}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TranscriptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TranscriptionException($"Transcription failed for '{wavFilePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolves the Whisper model path by searching configured folders in order:
    /// 1. <see cref="ApplicationSettings.WhisperModelsFolder"/> from settings
    /// 2. <c>models/</c> relative to the current directory (project root)
    /// 3. <c>{LocalAppData}/DarkQuill/Models/</c>
    /// If the model is not found in any folder, throws a <see cref="TranscriptionException"/>.
    /// </summary>
    private async Task<string> ResolveModelPathAsync(string modelFileName, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var searchDirs = GetSearchDirectories(settings);

        foreach (var dir in searchDirs)
        {
            var candidate = Path.GetFullPath(Path.Combine(dir, modelFileName));
            if (File.Exists(candidate))
            {
                Debug.WriteLine($"Whisper model found at: {candidate}");
                return candidate;
            }

            Debug.WriteLine($"Whisper model not found at: {candidate}");
        }

        throw new TranscriptionException(
            $"No Whisper model found. Place a GGML model file (.bin) in the models folder and select it via the Whisper Model dialog. " +
            $"Searched for '{modelFileName}' in: {string.Join(", ", searchDirs)}");
    }

    /// <summary>
    /// Returns the ordered list of directories to search for model files.
    /// </summary>
    private static string[] GetSearchDirectories(ApplicationSettings settings)
    {
        // Search multiple locations to find models regardless of how the app is launched.
        // The BaseDirectory walk-up finds the project root's models/ folder when running
        // from bin/Debug/net8.0/ (5 levels up: net8.0 -> Debug -> bin -> DarkQuill -> src -> root).
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return
        [
            settings.WhisperModelsFolder,
            Path.Combine(Directory.GetCurrentDirectory(), "models"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "models")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "models")),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DarkQuill", "Models"),
        ];
    }

    /// <summary>
    /// Loads the Whisper model from disk, attempting GPU acceleration first with CPU fallback.
    /// Creates both the <see cref="WhisperFactory"/> and <see cref="WhisperProcessor"/>.
    /// </summary>
    private void LoadModel(string modelPath)
    {
        // Try GPU first.
        try
        {
            _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions
            {
                UseGpu = true
            });
            _processor = BuildProcessor(_factory);
            _usingGpu = true;
            Debug.WriteLine("Whisper model loaded with GPU (CUDA) acceleration.");
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GPU loading failed, falling back to CPU: {ex.Message}");
            _factory?.Dispose();
            _factory = null;
            _processor = null;
        }

        // CPU fallback.
        try
        {
            _factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions
            {
                UseGpu = false
            });
            _processor = BuildProcessor(_factory);
            _usingGpu = false;
            Debug.WriteLine("Whisper model loaded with CPU inference.");
        }
        catch (Exception ex)
        {
            _factory?.Dispose();
            _factory = null;
            throw new TranscriptionException(
                $"Failed to load Whisper model from '{modelPath}' with both GPU and CPU: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Builds a <see cref="WhisperProcessor"/> from the factory with language auto-detection
    /// and beam search sampling for improved accuracy.
    /// </summary>
    private static WhisperProcessor BuildProcessor(WhisperFactory factory)
    {
        return factory.CreateBuilder()
            .WithLanguageDetection()
            .WithBeamSearchSamplingStrategy()
            .ParentBuilder
            .Build();
    }

    /// <summary>
    /// Runs Whisper inference on the specified WAV file and returns a structured result.
    /// This method is intended to be called within <see cref="Task.Run"/> to avoid blocking the UI thread.
    /// </summary>
    private async Task<TranscriptionResult> RunInferenceAsync(string wavFilePath, CancellationToken cancellationToken)
    {
        var segments = new List<TranscriptionSegment>();
        TimeSpan audioDuration = TimeSpan.Zero;

        using var fileStream = File.OpenRead(wavFilePath);

        await foreach (var segment in _processor!.ProcessAsync(fileStream, cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new TranscriptionSegment(
                Speaker: "Speaker 1",
                Text: segment.Text.Trim()));

            // Track the end time of the last segment as the audio duration.
            if (segment.End > audioDuration)
            {
                audioDuration = segment.End;
            }
        }

        string fullText = string.Join(" ", segments.Select(s => s.Text));

        // Derive a human-readable model version from the loaded filename.
        string modelVersion = _loadedModelFileName ?? "Unknown";

        return new TranscriptionResult(
            Text: fullText,
            Segments: segments.AsReadOnly(),
            ModelVersion: modelVersion,
            Duration: audioDuration);
    }

    /// <summary>
    /// Releases all resources held by this instance, including the Whisper processor,
    /// factory, and initialization lock.
    /// </summary>
    /// <param name="disposing">True if called from <see cref="Dispose"/>; false if called from a finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _processor?.Dispose();
            _processor = null;

            _factory?.Dispose();
            _factory = null;

            _initLock.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
