using System.Diagnostics;
using NAudio.Wave;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Captures microphone input via NAudio, enumerates devices, and monitors audio levels.
/// Produces 16-bit PCM, 16 kHz mono WAV files suitable for Whisper transcription.
/// Enforces a 5-minute maximum recording duration.
/// </summary>
public class AudioRecorder : IAudioRecorder, IDisposable
{
    /// <summary>
    /// Target WAV format: 16-bit PCM, 16 kHz, mono.
    /// </summary>
    private static readonly WaveFormat CaptureFormat = new(16000, 16, 1);

    /// <summary>
    /// Maximum allowed recording duration before auto-stop.
    /// </summary>
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);

    private WaveInEvent? _waveIn;
    private WaveFileWriter? _fileWriter;
    private CancellationTokenSource? _recordingCts;
    private TaskCompletionSource? _recordingStoppedTcs;
    private volatile bool _isRecording;
    private bool _disposed;

    /// <inheritdoc />
    /// <remarks>
    /// Enumerates microphone devices via <see cref="WaveInEvent.DeviceCount"/> and
    /// <see cref="WaveInEvent.GetCapabilities"/>. Returns an empty list when no devices are found.
    /// </remarks>
    public Task<IReadOnlyList<AudioDevice>> GetAvailableDevicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var devices = new List<AudioDevice>();

        try
        {
            int deviceCount = WaveInEvent.DeviceCount;

            for (int i = 0; i < deviceCount; i++)
            {
                var capabilities = WaveInEvent.GetCapabilities(i);
                devices.Add(new AudioDevice(
                    Id: i.ToString(),
                    Name: capabilities.ProductName,
                    IsDefault: i == 0));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error enumerating audio devices: {ex.Message}");
        }

        return Task.FromResult<IReadOnlyList<AudioDevice>>(devices.AsReadOnly());
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="outputPath"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a recording is already in progress or the output directory cannot be created.</exception>
    /// <exception cref="AudioDeviceNotFoundException">Thrown when the specified audio device is not available.</exception>
    public Task StartRecordingAsync(string outputPath, AudioSettings settings, IProgress<int> levelProgress, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (_isRecording)
        {
            throw new InvalidOperationException("A recording is already in progress. Call StopRecordingAsync before starting a new recording.");
        }

        int deviceIndex = ParseDeviceIndex(settings.DeviceId);
        ValidateDeviceIndex(deviceIndex);
        EnsureOutputDirectory(outputPath);

        // Create linked cancellation: user token + 5-minute timeout.
        _recordingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _recordingCts.CancelAfter(MaxRecordingDuration);

        _recordingStoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = CaptureFormat
        };

        try
        {
            _fileWriter = new WaveFileWriter(outputPath, CaptureFormat);
        }
        catch (Exception ex)
        {
            CleanupRecordingResources();
            throw new IOException($"Failed to create WAV file at '{outputPath}': {ex.Message}", ex);
        }

        var linkedToken = _recordingCts.Token;

        _waveIn.DataAvailable += (sender, e) =>
        {
            try
            {
                if (linkedToken.IsCancellationRequested)
                {
                    return;
                }

                _fileWriter?.Write(e.Buffer, 0, e.BytesRecorded);

                int rmsPercent = CalculateRmsPercent(e.Buffer, e.BytesRecorded);
                levelProgress?.Report(rmsPercent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DataAvailable handler: {ex.Message}");
            }
        };

        _waveIn.RecordingStopped += (sender, e) =>
        {
            _isRecording = false;
            FinalizeFileWriter();
            _recordingStoppedTcs?.TrySetResult();

            if (e.Exception is not null)
            {
                Debug.WriteLine($"Recording stopped with error: {e.Exception.Message}");
            }
        };

        // Register cancellation callback to auto-stop when timeout or user cancels.
        linkedToken.Register(() =>
        {
            try
            {
                if (_isRecording)
                {
                    _waveIn?.StopRecording();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during auto-stop: {ex.Message}");
            }
        });

        try
        {
            _waveIn.StartRecording();
            _isRecording = true;
        }
        catch (Exception ex)
        {
            CleanupRecordingResources();
            throw new AudioDeviceNotFoundException(
                $"Failed to start recording on device '{settings.DeviceId}': {ex.Message}", ex);
        }

        Debug.WriteLine($"Recording started: device={deviceIndex}, format={CaptureFormat}, output={outputPath}");
        return _recordingStoppedTcs.Task;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Idempotent: returns normally if no recording is in progress.
    /// Waits for the NAudio <c>RecordingStopped</c> event before returning to ensure
    /// the WAV file is fully flushed and closed.
    /// </remarks>
    public async Task StopRecordingAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_waveIn is null)
        {
            return;
        }

        var stoppedTcs = _recordingStoppedTcs;

        if (_isRecording)
        {
            _waveIn.StopRecording();
        }

        // Wait for the RecordingStopped event to fire and finalize the file.
        if (stoppedTcs is not null)
        {
            await stoppedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        CleanupRecordingResources();
        Debug.WriteLine("Recording stopped and resources cleaned up.");
    }

    /// <inheritdoc />
    public Task<bool> IsRecordingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_isRecording);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Captures audio from the specified device without writing to disk.
    /// Reports RMS levels via <paramref name="levelProgress"/> until
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </remarks>
    /// <exception cref="AudioDeviceNotFoundException">Thrown when the specified device is not available.</exception>
    public async Task TestAudioDeviceAsync(AudioSettings settings, IProgress<int> levelProgress, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int deviceIndex = ParseDeviceIndex(settings.DeviceId);
        ValidateDeviceIndex(deviceIndex);

        using var testWaveIn = new WaveInEvent
        {
            DeviceNumber = deviceIndex,
            WaveFormat = CaptureFormat
        };

        var stoppedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var testRunning = true;

        testWaveIn.DataAvailable += (sender, e) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            int rmsPercent = CalculateRmsPercent(e.Buffer, e.BytesRecorded);
            levelProgress?.Report(rmsPercent);
        };

        testWaveIn.RecordingStopped += (sender, e) =>
        {
            stoppedTcs.TrySetResult();
        };

        cancellationToken.Register(() =>
        {
            try
            {
                if (testRunning)
                {
                    testWaveIn.StopRecording();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping device test: {ex.Message}");
            }
        });

        try
        {
            testWaveIn.StartRecording();
        }
        catch (Exception ex)
        {
            throw new AudioDeviceNotFoundException(
                $"Failed to test audio device '{settings.DeviceId}': {ex.Message}", ex);
        }

        Debug.WriteLine($"Device test started: device={deviceIndex}");

        await stoppedTcs.Task.ConfigureAwait(false);
        testRunning = false;

        Debug.WriteLine("Device test completed.");
    }

    /// <summary>
    /// Calculates the RMS (root mean square) level of 16-bit PCM audio data
    /// and returns it as a percentage (0–100).
    /// </summary>
    /// <param name="buffer">Raw byte buffer containing 16-bit PCM samples (little-endian).</param>
    /// <param name="bytesRecorded">Number of valid bytes in the buffer.</param>
    /// <returns>RMS level as an integer percentage from 0 to 100, using a logarithmic (dB) scale
    /// for visually meaningful meter response. Typical speech shows 40–70% on this scale.</returns>
    internal static int CalculateRmsPercent(byte[] buffer, int bytesRecorded)
    {
        double rms = CalculateRmsLevel(buffer, bytesRecorded);

        if (rms <= 0.0)
        {
            return 0;
        }

        // Convert to dB (0 dB = full scale, -60 dB = silence floor).
        double db = 20.0 * Math.Log10(rms);
        const double minDb = -60.0;
        const double maxDb = 0.0;

        // Map dB range to 0–100 percent.
        double percent = (db - minDb) / (maxDb - minDb) * 100.0;
        return Math.Clamp((int)percent, 0, 100);
    }

    /// <summary>
    /// Calculates the RMS (root mean square) level of 16-bit PCM audio data,
    /// normalized to a 0.0–1.0 range.
    /// </summary>
    /// <param name="buffer">Raw byte buffer containing 16-bit PCM samples (little-endian).</param>
    /// <param name="bytesRecorded">Number of valid bytes in the buffer.</param>
    /// <returns>RMS level normalized to 0.0–1.0 range based on 16-bit max value (32767).</returns>
    internal static double CalculateRmsLevel(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2;
        if (sampleCount == 0)
        {
            return 0.0;
        }

        double sumOfSquares = 0.0;

        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumOfSquares += (double)sample * sample;
        }

        double meanSquare = sumOfSquares / sampleCount;
        double rms = Math.Sqrt(meanSquare);

        return rms / 32767.0;
    }

    /// <summary>
    /// Parses a device ID string to an integer index. Returns -1 for null, empty, or unparseable values
    /// (NAudio uses -1 for the default device).
    /// </summary>
    private static int ParseDeviceIndex(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return -1;
        }

        return int.TryParse(deviceId, out int index) ? index : -1;
    }

    /// <summary>
    /// Validates that the specified device index corresponds to an available audio device.
    /// Device index -1 is always valid (system default).
    /// </summary>
    /// <exception cref="AudioDeviceNotFoundException">Thrown when the device index is out of range.</exception>
    private static void ValidateDeviceIndex(int deviceIndex)
    {
        if (deviceIndex == -1)
        {
            return;
        }

        int deviceCount = WaveInEvent.DeviceCount;

        if (deviceIndex < 0 || deviceIndex >= deviceCount)
        {
            throw new AudioDeviceNotFoundException(
                $"Audio device with index {deviceIndex} was not found. Available devices: {deviceCount}.");
        }
    }

    /// <summary>
    /// Ensures the parent directory for the output file exists. Creates it if necessary.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the directory cannot be created.</exception>
    private static void EnsureOutputDirectory(string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);

        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException(
                $"Cannot determine parent directory for output path: '{outputPath}'.");
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create output directory '{directory}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Flushes and disposes the WAV file writer to ensure the file is complete.
    /// </summary>
    private void FinalizeFileWriter()
    {
        try
        {
            _fileWriter?.Flush();
            _fileWriter?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error finalizing WAV file: {ex.Message}");
        }

        _fileWriter = null;
    }

    /// <summary>
    /// Disposes all recording-session resources (WaveInEvent, CancellationTokenSource, TaskCompletionSource).
    /// Does not dispose the file writer — call <see cref="FinalizeFileWriter"/> first.
    /// </summary>
    private void CleanupRecordingResources()
    {
        _isRecording = false;

        _waveIn?.Dispose();
        _waveIn = null;

        _recordingCts?.Dispose();
        _recordingCts = null;

        _recordingStoppedTcs = null;
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
            FinalizeFileWriter();
            CleanupRecordingResources();
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
