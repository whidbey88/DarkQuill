namespace DarkQuill.Services;

/// <summary>
/// Plays back WAV audio files. Supports single-file playback with automatic stop
/// when the file ends or when explicitly stopped.
/// </summary>
public interface IAudioPlaybackService
{
    /// <summary>
    /// Starts playback of the specified WAV file. If another file is already playing,
    /// stops it first and starts the new one.
    /// </summary>
    /// <param name="wavFilePath">Absolute path to the WAV file to play.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="wavFilePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    Task PlayAsync(string wavFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops current playback. Idempotent — returns normally if nothing is playing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether audio is currently playing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if audio is currently playing; otherwise false.</returns>
    Task<bool> IsPlayingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The file path of the currently playing audio, or null if stopped.
    /// </summary>
    string? CurrentFilePath { get; }

    /// <summary>
    /// Fired when playback ends, either naturally at end of file or via <see cref="StopAsync"/>.
    /// </summary>
    event EventHandler<EventArgs>? PlaybackStopped;
}
