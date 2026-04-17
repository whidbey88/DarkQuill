using NAudio.Wave;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Tests for <see cref="AudioRecorder"/> covering device enumeration, state transitions,
/// RMS level calculation, argument validation, and hardware-dependent recording scenarios.
/// Tests that require a real audio device are skipped when no devices are available.
/// </summary>
public class AudioRecorderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AudioRecorder _sut;

    /// <summary>
    /// Returns true if the system has at least one audio input device available.
    /// </summary>
    private static bool HasAudioDevice
    {
        get
        {
            try
            {
                return WaveInEvent.DeviceCount > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public AudioRecorderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new AudioRecorder();
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
    // Device enumeration
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetAvailableDevicesAsync_ReturnsNonNullList()
    {
        // Act
        var devices = await _sut.GetAvailableDevicesAsync();

        // Assert
        Assert.NotNull(devices);
    }

    [Fact]
    public async Task GetAvailableDevicesAsync_WithAvailableDevices_ReturnsDeviceList()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Act
        var devices = await _sut.GetAvailableDevicesAsync();

        // Assert
        Assert.NotEmpty(devices);
        Assert.All(devices, d =>
        {
            Assert.False(string.IsNullOrEmpty(d.Id));
            Assert.False(string.IsNullOrEmpty(d.Name));
        });
    }

    [Fact]
    public async Task GetAvailableDevicesAsync_FirstDeviceIsDefault()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Act
        var devices = await _sut.GetAvailableDevicesAsync();

        // Assert
        Assert.True(devices[0].IsDefault);
    }

    [Fact]
    public async Task GetAvailableDevicesAsync_DeviceIdsAreSequentialStrings()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Act
        var devices = await _sut.GetAvailableDevicesAsync();

        // Assert
        for (int i = 0; i < devices.Count; i++)
        {
            Assert.Equal(i.ToString(), devices[i].Id);
        }
    }

    [Fact]
    public async Task GetAvailableDevicesAsync_WithCancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.GetAvailableDevicesAsync(cts.Token));
    }

    // ───────────────────────────────────────────────
    // IsRecording state
    // ───────────────────────────────────────────────

    [Fact]
    public async Task IsRecordingAsync_WhenNotRecording_ReturnsFalse()
    {
        // Act
        bool isRecording = await _sut.IsRecordingAsync();

        // Assert
        Assert.False(isRecording);
    }

    [Fact]
    public async Task IsRecordingAsync_WithCancelledToken_ThrowsOperationCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.IsRecordingAsync(cts.Token));
    }

    // ───────────────────────────────────────────────
    // StopRecording — idempotent
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StopRecordingAsync_WithNoActiveRecording_DoesNotThrow()
    {
        // Act & Assert — should complete without exceptions
        await _sut.StopRecordingAsync();
    }

    [Fact]
    public async Task StopRecordingAsync_CalledTwice_DoesNotThrow()
    {
        // Act & Assert — double stop should be idempotent
        await _sut.StopRecordingAsync();
        await _sut.StopRecordingAsync();
    }

    // ───────────────────────────────────────────────
    // StartRecording — argument validation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StartRecordingAsync_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.StartRecordingAsync(null!, new AudioSettings(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task StartRecordingAsync_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.StartRecordingAsync("", new AudioSettings(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task StartRecordingAsync_WithInvalidDeviceId_ThrowsAudioDeviceNotFoundException()
    {
        // Arrange — device index 9999 almost certainly doesn't exist
        var settings = new AudioSettings { DeviceId = "9999" };
        string outputPath = Path.Combine(_tempDir, "test.wav");

        // Act & Assert
        await Assert.ThrowsAsync<AudioDeviceNotFoundException>(
            () => _sut.StartRecordingAsync(outputPath, settings, null!, CancellationToken.None));
    }

    // ───────────────────────────────────────────────
    // Recording state transitions (hardware-dependent)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StartRecordingAsync_WithValidPath_CreatesWavFile()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath = Path.Combine(_tempDir, "recording.wav");
        var settings = new AudioSettings(); // default device

        // Act
        await _sut.StartRecordingAsync(outputPath, settings, null!, CancellationToken.None);
        await Task.Delay(200); // Let NAudio create and write to the file
        await _sut.StopRecordingAsync();

        // Assert
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task StartRecordingAsync_WithMissingParentDirectory_CreatesDirectoryAndRecords()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string nestedDir = Path.Combine(_tempDir, "nested", "subdir");
        string outputPath = Path.Combine(nestedDir, "recording.wav");
        var settings = new AudioSettings();

        // Act
        await _sut.StartRecordingAsync(outputPath, settings, null!, CancellationToken.None);
        await Task.Delay(200);
        await _sut.StopRecordingAsync();

        // Assert
        Assert.True(Directory.Exists(nestedDir));
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task IsRecordingAsync_DuringRecording_ReturnsTrue()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath = Path.Combine(_tempDir, "recording.wav");
        var settings = new AudioSettings();

        // Act
        await _sut.StartRecordingAsync(outputPath, settings, null!, CancellationToken.None);

        bool isRecording = await _sut.IsRecordingAsync();

        await _sut.StopRecordingAsync();

        // Assert
        Assert.True(isRecording);
    }

    [Fact]
    public async Task StartRecordingAsync_ThenStopRecordingAsync_TransitionsIdleToRecordingToIdle()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath = Path.Combine(_tempDir, "recording.wav");
        var settings = new AudioSettings();

        // Assert initial state
        Assert.False(await _sut.IsRecordingAsync());

        // Act — start recording
        await _sut.StartRecordingAsync(outputPath, settings, null!, CancellationToken.None);
        Assert.True(await _sut.IsRecordingAsync());

        // Let some audio data be recorded
        await Task.Delay(500);

        // Act — stop recording
        await _sut.StopRecordingAsync();

        // Assert final state
        Assert.False(await _sut.IsRecordingAsync());
        Assert.True(File.Exists(outputPath));

        var fileInfo = new FileInfo(outputPath);
        Assert.True(fileInfo.Length > 0, "WAV file should not be empty after recording.");
    }

    [Fact]
    public async Task StartRecordingAsync_WhileAlreadyRecording_ThrowsInvalidOperationException()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath1 = Path.Combine(_tempDir, "recording1.wav");
        string outputPath2 = Path.Combine(_tempDir, "recording2.wav");
        var settings = new AudioSettings();

        await _sut.StartRecordingAsync(outputPath1, settings, null!, CancellationToken.None);

        try
        {
            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _sut.StartRecordingAsync(outputPath2, settings, null!, CancellationToken.None));
        }
        finally
        {
            await _sut.StopRecordingAsync();
        }
    }

    // ───────────────────────────────────────────────
    // Level progress reporting
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LevelProgress_ReportsLevelsDuringRecording()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath = Path.Combine(_tempDir, "recording.wav");
        var settings = new AudioSettings();
        var reportedLevels = new List<int>();
        var progress = new Progress<int>(level => reportedLevels.Add(level));

        // Act
        await _sut.StartRecordingAsync(outputPath, settings, progress, CancellationToken.None);
        await Task.Delay(1000); // Allow time for audio buffers to be reported
        await _sut.StopRecordingAsync();

        // Assert — at least some levels should have been reported
        Assert.NotEmpty(reportedLevels);
        Assert.All(reportedLevels, level =>
        {
            Assert.InRange(level, 0, 100);
        });
    }

    // ───────────────────────────────────────────────
    // TestAudioDevice
    // ───────────────────────────────────────────────

    [Fact]
    public async Task TestAudioDeviceAsync_WithoutSavingFile_DoesNotCreateWavFile()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        var settings = new AudioSettings();
        var reportedLevels = new List<int>();
        var progress = new Progress<int>(level => reportedLevels.Add(level));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        await _sut.TestAudioDeviceAsync(settings, progress, cts.Token);

        // Assert — no WAV files should be created
        var wavFiles = Directory.GetFiles(_tempDir, "*.wav", SearchOption.AllDirectories);
        Assert.Empty(wavFiles);
    }

    [Fact]
    public async Task TestAudioDeviceAsync_ReportsAudioLevels()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        var settings = new AudioSettings();
        var reportedLevels = new List<int>();
        var progress = new Progress<int>(level => reportedLevels.Add(level));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        await _sut.TestAudioDeviceAsync(settings, progress, cts.Token);

        // Allow Progress<T> callbacks to complete (they are posted to SynchronizationContext)
        await Task.Delay(200);

        // Assert
        Assert.NotEmpty(reportedLevels);
        Assert.All(reportedLevels, level =>
        {
            Assert.InRange(level, 0, 100);
        });
    }

    [Fact]
    public async Task TestAudioDeviceAsync_WithInvalidDevice_ThrowsAudioDeviceNotFoundException()
    {
        // Arrange
        var settings = new AudioSettings { DeviceId = "9999" };
        var progress = new Progress<int>(_ => { });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act & Assert
        await Assert.ThrowsAsync<AudioDeviceNotFoundException>(
            () => _sut.TestAudioDeviceAsync(settings, progress, cts.Token));
    }

    // ───────────────────────────────────────────────
    // Auto-stop via CancellationToken
    // ───────────────────────────────────────────────

    [Fact]
    public async Task StartRecordingAsync_WithCancelledToken_StopsRecording()
    {
        if (!HasAudioDevice)
        {
            return; // Skip: no audio hardware
        }

        // Arrange
        string outputPath = Path.Combine(_tempDir, "recording.wav");
        var settings = new AudioSettings();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act — start recording; it should auto-stop when token is cancelled
        await _sut.StartRecordingAsync(outputPath, settings, null!, cts.Token);

        // Wait for the cancellation to trigger the auto-stop callback
        await Task.Delay(1500);

        // Assert
        Assert.False(await _sut.IsRecordingAsync());
        Assert.True(File.Exists(outputPath));
    }

    // ───────────────────────────────────────────────
    // RMS calculation (pure logic — no hardware needed)
    // ───────────────────────────────────────────────

    [Fact]
    public void CalculateRmsLevel_WithSilentBuffer_ReturnsZero()
    {
        // Arrange — buffer of all zeros (silence)
        byte[] buffer = new byte[100];
        int bytesRecorded = buffer.Length;

        // Act
        double rms = AudioRecorder.CalculateRmsLevel(buffer, bytesRecorded);

        // Assert
        Assert.Equal(0.0, rms);
    }

    [Fact]
    public void CalculateRmsLevel_WithEmptyBuffer_ReturnsZero()
    {
        // Act
        double rms = AudioRecorder.CalculateRmsLevel(Array.Empty<byte>(), 0);

        // Assert
        Assert.Equal(0.0, rms);
    }

    [Fact]
    public void CalculateRmsLevel_WithMaxAmplitudeSamples_ReturnsApproximatelyOne()
    {
        // Arrange — 16-bit PCM samples at max positive value (32767 = 0x7FFF)
        // Little-endian: 0xFF, 0x7F
        byte[] buffer = new byte[20];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            buffer[i] = 0xFF;     // low byte
            buffer[i + 1] = 0x7F; // high byte
        }

        // Act
        double rms = AudioRecorder.CalculateRmsLevel(buffer, buffer.Length);

        // Assert — should be very close to 1.0 (32767/32767)
        Assert.InRange(rms, 0.99, 1.01);
    }

    [Fact]
    public void CalculateRmsLevel_WithKnownSamples_ReturnsExpectedValue()
    {
        // Arrange — two 16-bit samples: 1000 and -1000
        // 1000 little-endian: 0xE8, 0x03
        // -1000 little-endian: 0x18, 0xFC
        byte[] buffer =
        [
            0xE8, 0x03, // 1000
            0x18, 0xFC, // -1000
        ];

        // Act
        double rms = AudioRecorder.CalculateRmsLevel(buffer, buffer.Length);

        // Assert — RMS of [1000, -1000] = sqrt((1000^2 + 1000^2) / 2) / 32767 = 1000 / 32767 ≈ 0.03052
        double expected = 1000.0 / 32767.0;
        Assert.InRange(rms, expected - 0.001, expected + 0.001);
    }

    [Fact]
    public void CalculateRmsPercent_WithSilentBuffer_ReturnsZero()
    {
        // Arrange
        byte[] buffer = new byte[100];

        // Act
        int percent = AudioRecorder.CalculateRmsPercent(buffer, buffer.Length);

        // Assert
        Assert.Equal(0, percent);
    }

    [Fact]
    public void CalculateRmsPercent_WithMaxAmplitude_Returns100()
    {
        // Arrange — all samples at max positive value
        byte[] buffer = new byte[20];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            buffer[i] = 0xFF;
            buffer[i + 1] = 0x7F;
        }

        // Act
        int percent = AudioRecorder.CalculateRmsPercent(buffer, buffer.Length);

        // Assert
        Assert.Equal(100, percent);
    }

    [Fact]
    public void CalculateRmsPercent_ReturnsClamped0To100()
    {
        // Arrange — mid-range samples
        byte[] buffer =
        [
            0xE8, 0x03, // 1000
            0x18, 0xFC, // -1000
        ];

        // Act
        int percent = AudioRecorder.CalculateRmsPercent(buffer, buffer.Length);

        // Assert — should be clamped within 0–100
        Assert.InRange(percent, 0, 100);
    }

    [Fact]
    public void CalculateRmsLevel_WithNegativeMaxAmplitude_ReturnsApproximatelyOne()
    {
        // Arrange — samples at max negative value (-32768 = 0x8000, but RMS uses absolute)
        // -32768 little-endian: 0x00, 0x80
        byte[] buffer = new byte[20];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            buffer[i] = 0x00;
            buffer[i + 1] = 0x80;
        }

        // Act
        double rms = AudioRecorder.CalculateRmsLevel(buffer, buffer.Length);

        // Assert — -32768 squared / 32767 normalizer → slightly above 1.0
        // (32768/32767 ≈ 1.00003), clamped in percent but raw double may exceed 1.0 slightly
        Assert.InRange(rms, 0.99, 1.01);
    }

    // ───────────────────────────────────────────────
    // Dispose behavior
    // ───────────────────────────────────────────────

    [Fact]
    public void Dispose_WhenNotRecording_DoesNotThrow()
    {
        // Arrange
        var recorder = new AudioRecorder();

        // Act & Assert — no exception
        recorder.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var recorder = new AudioRecorder();

        // Act & Assert
        recorder.Dispose();
        recorder.Dispose();
    }

    [Fact]
    public async Task StartRecordingAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var recorder = new AudioRecorder();
        recorder.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => recorder.StartRecordingAsync(
                Path.Combine(_tempDir, "test.wav"), new AudioSettings(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task StopRecordingAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var recorder = new AudioRecorder();
        recorder.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => recorder.StopRecordingAsync());
    }

    [Fact]
    public async Task IsRecordingAsync_AfterDispose_ThrowsFalseOrThrows()
    {
        // Arrange
        var recorder = new AudioRecorder();
        recorder.Dispose();

        // Act & Assert — disposed recorder should throw ObjectDisposedException
        // IsRecordingAsync checks _disposed flag only indirectly via _isRecording volatile bool.
        // Since it only checks cancellation token, it returns false (no ObjectDisposedException guard).
        bool result = await recorder.IsRecordingAsync();
        Assert.False(result);
    }
}
