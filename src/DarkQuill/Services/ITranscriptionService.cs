using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Runs Whisper inference on WAV audio files and returns structured transcription results.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Gets whether the Whisper model has been loaded and is ready for inference.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Loads the Whisper model into memory. Must be called before <see cref="TranscribeAsync"/>.
    /// </summary>
    /// <param name="modelFileName">
    /// Optional filename (not full path) of the GGML model to load, e.g. <c>"ggml-large-v3-turbo.bin"</c>.
    /// If null or empty, searches for <c>ggml-large-v3-turbo.bin</c> as the default.
    /// If the requested model differs from the currently loaded model, the old model is disposed and the new one loaded.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TranscriptionException">Thrown when the model cannot be found or loaded.</exception>
    Task InitializeAsync(string? modelFileName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans configured model folders for available Whisper GGML model files (.bin).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A deduplicated list of model filenames found across all search locations.</returns>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transcribes a WAV audio file using the Whisper model.
    /// </summary>
    /// <param name="wavFilePath">Absolute path to the WAV file (16-bit PCM, 16 kHz mono).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TranscriptionResult"/> containing the transcribed text and segments.</returns>
    Task<TranscriptionResult> TranscribeAsync(string wavFilePath, CancellationToken cancellationToken = default);
}
