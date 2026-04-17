using System.Diagnostics;
using NAudio.Wave;

namespace DarkQuill.Services;

/// <summary>
/// Plays back WAV audio files using NAudio's <see cref="WaveOutEvent"/>.
/// Supports single-file playback with automatic stop when the file ends.
/// Implements <see cref="IDisposable"/> for resource cleanup.
/// </summary>
public class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioFileReader;
    private volatile bool _isPlaying;
    private bool _disposed;

    /// <inheritdoc />
    public string? CurrentFilePath { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? PlaybackStopped;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="wavFilePath"/> is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    public async Task PlayAsync(string wavFilePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(wavFilePath);

        if (!File.Exists(wavFilePath))
        {
            throw new FileNotFoundException($"Audio file not found: '{wavFilePath}'", wavFilePath);
        }

        // Stop any existing playback before starting a new file.
        await StopAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _audioFileReader = new AudioFileReader(wavFilePath);
            _waveOut = new WaveOutEvent();

            _waveOut.PlaybackStopped += OnWaveOutPlaybackStopped;
            _waveOut.Init(_audioFileReader);

            CurrentFilePath = wavFilePath;
            _isPlaying = true;
            _waveOut.Play();

            Debug.WriteLine($"Playback started: {wavFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting playback: {ex.Message}");
            CleanupPlaybackResources();
            throw;
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_waveOut is not null && _isPlaying)
        {
            _waveOut.Stop();
            // The PlaybackStopped event handler will clean up resources.
        }
        else
        {
            // Nothing playing — ensure state is clean.
            CleanupPlaybackResources();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsPlayingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_isPlaying);
    }

    /// <summary>
    /// Handles the <see cref="WaveOutEvent.PlaybackStopped"/> event. Fires when playback
    /// ends naturally (end of file) or when <see cref="StopAsync"/> is called.
    /// </summary>
    private void OnWaveOutPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        _isPlaying = false;
        CurrentFilePath = null;
        CleanupPlaybackResources();

        if (e.Exception is not null)
        {
            Debug.WriteLine($"Playback stopped with error: {e.Exception.Message}");
        }
        else
        {
            Debug.WriteLine("Playback stopped.");
        }

        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Disposes playback resources (WaveOutEvent, AudioFileReader) without firing events.
    /// </summary>
    private void CleanupPlaybackResources()
    {
        _isPlaying = false;
        CurrentFilePath = null;

        if (_waveOut is not null)
        {
            _waveOut.PlaybackStopped -= OnWaveOutPlaybackStopped;
            _waveOut.Dispose();
            _waveOut = null;
        }

        _audioFileReader?.Dispose();
        _audioFileReader = null;
    }

    /// <summary>
    /// Releases all resources held by this instance.
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
            CleanupPlaybackResources();
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
