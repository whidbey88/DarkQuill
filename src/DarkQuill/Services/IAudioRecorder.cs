using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Captures microphone input, enumerates audio devices, and monitors audio levels.
/// </summary>
public interface IAudioRecorder
{
    /// <summary>
    /// Returns a list of available audio input devices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of available audio devices.</returns>
    Task<IReadOnlyList<AudioDevice>> GetAvailableDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts recording audio from the configured device to a WAV file.
    /// </summary>
    /// <param name="outputPath">Absolute path for the output WAV file.</param>
    /// <param name="settings">Audio device and level settings.</param>
    /// <param name="levelProgress">Reports audio level (0–100) during recording.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartRecordingAsync(string outputPath, AudioSettings settings, IProgress<int> levelProgress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the current recording and finalizes the WAV file.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether a recording is currently in progress.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if currently recording; otherwise false.</returns>
    Task<bool> IsRecordingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests an audio device by briefly capturing input and reporting levels.
    /// </summary>
    /// <param name="settings">Audio device and level settings to test.</param>
    /// <param name="levelProgress">Reports audio level (0–100) during the test.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task TestAudioDeviceAsync(AudioSettings settings, IProgress<int> levelProgress, CancellationToken cancellationToken = default);
}
