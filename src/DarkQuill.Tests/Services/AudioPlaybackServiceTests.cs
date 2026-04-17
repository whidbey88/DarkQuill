using NAudio.Wave;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Tests for <see cref="AudioPlaybackService"/> covering initial state, argument validation,
/// idempotent stop, and disposal. Integration tests that require audio hardware are marked
/// with <c>[Trait("Category", "Integration")]</c>.
/// </summary>
public class AudioPlaybackServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AudioPlaybackService _sut;

    /// <summary>
    /// Returns true if the system has at least one audio output device available.
    /// </summary>
    private static bool HasAudioOutputDevice
    {
        get
        {
            try
            {
                using var test = new WaveOutEvent();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public AudioPlaybackServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new AudioPlaybackService();
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ───────────────────────────────────────────────
    // Initial state
    // ───────────────────────────────────────────────

    [Fact]
    public async Task IsPlayingAsync_ReturnsFalse_Initially()
    {
        // Act
        bool isPlaying = await _sut.IsPlayingAsync();

        // Assert
        Assert.False(isPlaying);
    }

    [Fact]
    public void CurrentFilePath_IsNull_Initially()
    {
        // Assert
        Assert.Null(_sut.CurrentFilePath);
    }

    // ───────────────────────────────────────────────
    // StopAsync idempotency
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_DoesNotThrow_WhenNothingIsPlaying()
    {
        // Act & Assert — should not throw
        await _sut.StopAsync();
    }

    [Fact]
    public async Task StopAsync_IsIdempotent_CalledMultipleTimes()
    {
        // Act & Assert — calling multiple times should not throw
        await _sut.StopAsync();
        await _sut.StopAsync();
        await _sut.StopAsync();
    }

    // ───────────────────────────────────────────────
    // PlayAsync argument validation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task PlayAsync_ThrowsFileNotFoundException_ForNonExistentFile()
    {
        // Arrange
        var fakePath = Path.Combine(_tempDir, "nonexistent.wav");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.PlayAsync(fakePath));
    }

    [Fact]
    public async Task PlayAsync_ThrowsArgumentException_ForNullPath()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.PlayAsync(null!));
    }

    [Fact]
    public async Task PlayAsync_ThrowsArgumentException_ForEmptyPath()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.PlayAsync(string.Empty));
    }

    // ───────────────────────────────────────────────
    // Dispose idempotency
    // ───────────────────────────────────────────────

    [Fact]
    public void Dispose_IsIdempotent()
    {
        // Act & Assert — calling multiple times should not throw
        _sut.Dispose();
        _sut.Dispose();
        _sut.Dispose();
    }

    // ───────────────────────────────────────────────
    // Integration tests (require audio output device)
    // ───────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Integration")]
    public async Task PlayAsync_PlaysFileAndFiresPlaybackStopped_WhenAudioDeviceAvailable()
    {
        if (!HasAudioOutputDevice)
            return;

        // Arrange — create a short WAV file with silence (0.5 seconds)
        var wavPath = CreateSilentWavFile(TimeSpan.FromMilliseconds(500));
        var stoppedTcs = new TaskCompletionSource();

        _sut.PlaybackStopped += (_, _) => stoppedTcs.TrySetResult();

        // Act
        await _sut.PlayAsync(wavPath);

        // Assert — playback should be in progress
        Assert.True(await _sut.IsPlayingAsync());
        Assert.Equal(wavPath, _sut.CurrentFilePath);

        // Wait for playback to end naturally (with timeout)
        var completed = await Task.WhenAny(stoppedTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(stoppedTcs.Task, completed);

        Assert.False(await _sut.IsPlayingAsync());
        Assert.Null(_sut.CurrentFilePath);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StopAsync_StopsPlaybackAndFiresEvent_WhenAudioDeviceAvailable()
    {
        if (!HasAudioOutputDevice)
            return;

        // Arrange — create a longer WAV file so it doesn't end before we stop it
        var wavPath = CreateSilentWavFile(TimeSpan.FromSeconds(5));
        var stoppedTcs = new TaskCompletionSource();

        _sut.PlaybackStopped += (_, _) => stoppedTcs.TrySetResult();

        await _sut.PlayAsync(wavPath);
        Assert.True(await _sut.IsPlayingAsync());

        // Act
        await _sut.StopAsync();

        // Assert
        var completed = await Task.WhenAny(stoppedTcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Equal(stoppedTcs.Task, completed);

        Assert.False(await _sut.IsPlayingAsync());
        Assert.Null(_sut.CurrentFilePath);
    }

    // ───────────────────────────────────────────────
    // Helpers
    // ───────────────────────────────────────────────

    /// <summary>
    /// Creates a WAV file containing silence at 16 kHz, 16-bit mono (matching the app's capture format).
    /// </summary>
    private string CreateSilentWavFile(TimeSpan duration)
    {
        var wavPath = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(16000, 16, 1);
        int totalSamples = (int)(format.SampleRate * duration.TotalSeconds);
        var silence = new byte[totalSamples * format.BlockAlign];

        using var writer = new WaveFileWriter(wavPath, format);
        writer.Write(silence, 0, silence.Length);

        return wavPath;
    }
}
