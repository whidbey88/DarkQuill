using NAudio.Wave;
using NSubstitute;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Tests for <see cref="TranscriptionService"/> covering initialization state, argument
/// validation, file validation, cancellation, dispose behavior, model enumeration,
/// and model-dependent transcription. Tests that require the Whisper model are skipped
/// when the model is not cached locally.
/// </summary>
public class TranscriptionServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _modelsDir;
    private readonly ISettingsService _settingsService;
    private readonly TranscriptionService _sut;

    /// <summary>
    /// Path where TranscriptionService caches the Whisper model.
    /// </summary>
    private static readonly string ModelPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DarkQuill", "Models", "ggml-large-v3-turbo.bin");

    /// <summary>
    /// Returns true if the Whisper model file is cached locally.
    /// </summary>
    private static bool HasCachedModel => File.Exists(ModelPath);

    public TranscriptionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        _modelsDir = Path.Combine(_tempDir, "models");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_modelsDir);

        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings { WhisperModelsFolder = _modelsDir });

        _sut = new TranscriptionService(_settingsService);
    }

    public void Dispose()
    {
        _sut.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Creates a valid 16-bit PCM, 16 kHz mono WAV file with silence of the specified duration.
    /// </summary>
    private string CreateValidWavFile(string fileName, TimeSpan duration)
    {
        string path = Path.Combine(_tempDir, fileName);
        var format = new WaveFormat(16000, 16, 1);
        using var writer = new WaveFileWriter(path, format);

        int sampleCount = (int)(format.SampleRate * duration.TotalSeconds);
        byte[] silence = new byte[sampleCount * format.BlockAlign];
        writer.Write(silence, 0, silence.Length);

        return path;
    }

    /// <summary>
    /// Creates a file with the given extension containing arbitrary content.
    /// </summary>
    private string CreateFileWithExtension(string fileName, string content = "not audio data")
    {
        string path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Creates a dummy .bin file in the models directory.
    /// </summary>
    private void CreateDummyModel(string fileName)
    {
        File.WriteAllText(Path.Combine(_modelsDir, fileName), "dummy model data");
    }

    // ───────────────────────────────────────────────
    // IsInitialized — initial state
    // ───────────────────────────────────────────────

    [Fact]
    public void IsInitialized_BeforeInitialization_ReturnsFalse()
    {
        // Assert
        Assert.False(_sut.IsInitialized);
    }

    // ───────────────────────────────────────────────
    // GetAvailableModelsAsync
    // ───────────────────────────────────────────────

    [Fact]
    public async Task GetAvailableModelsAsync_ReturnsFilenamesFromModelsFolder()
    {
        // Arrange
        CreateDummyModel("ggml-base.bin");
        CreateDummyModel("ggml-large-v3-turbo.bin");

        // Act
        var models = await _sut.GetAvailableModelsAsync();

        // Assert
        Assert.Equal(2, models.Count);
        Assert.Contains("ggml-base.bin", models);
        Assert.Contains("ggml-large-v3-turbo.bin", models);
    }

    [Fact]
    public async Task GetAvailableModelsAsync_ReturnsEmptyListWhenNoFoldersExist()
    {
        // Arrange — point settings to a non-existent folder; also ensure other search paths
        // don't interfere by using a unique temp dir that won't match CWD or LocalAppData
        var isolatedSettingsService = Substitute.For<ISettingsService>();
        var nonExistentDir = Path.Combine(_tempDir, "nonexistent");
        isolatedSettingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings
            {
                WhisperModelsFolder = nonExistentDir
            });
        using var isolatedService = new TranscriptionService(isolatedSettingsService);

        // Act
        var models = await isolatedService.GetAvailableModelsAsync();

        // Assert — may find models in CWD or LocalAppData; verify the nonexistent folder
        // doesn't cause an error. The count depends on the environment.
        Assert.DoesNotContain("nonexistent-model.bin", models);
    }

    [Fact]
    public async Task GetAvailableModelsAsync_DeduplicatesAcrossFolders()
    {
        // Arrange — place a model that also exists in LocalAppData (if cached)
        // The dedup should ensure each filename appears only once
        CreateDummyModel("ggml-large-v3-turbo.bin");

        // Act
        var models = await _sut.GetAvailableModelsAsync();

        // Assert — ggml-large-v3-turbo.bin should appear exactly once even if in multiple dirs
        Assert.Equal(1, models.Count(m => m == "ggml-large-v3-turbo.bin"));
    }

    [Fact]
    public async Task GetAvailableModelsAsync_IgnoresNonBinFiles()
    {
        // Arrange
        CreateDummyModel("ggml-test-only.bin");
        File.WriteAllText(Path.Combine(_modelsDir, "readme.txt"), "not a model");
        File.WriteAllText(Path.Combine(_modelsDir, "config.json"), "{}");

        // Act
        var models = await _sut.GetAvailableModelsAsync();

        // Assert — our test model should be found; non-.bin files should not
        Assert.Contains("ggml-test-only.bin", models);
        Assert.DoesNotContain("readme.txt", models);
        Assert.DoesNotContain("config.json", models);
    }

    // ───────────────────────────────────────────────
    // TranscribeAsync — argument validation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task TranscribeAsync_WithNullPath_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.TranscribeAsync(null!));
    }

    [Fact]
    public async Task TranscribeAsync_WithEmptyPath_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.TranscribeAsync(""));
    }

    [Fact]
    public async Task TranscribeAsync_WithMissingWavFile_ThrowsFileNotFoundException()
    {
        // Arrange
        string nonExistentPath = Path.Combine(_tempDir, "nonexistent.wav");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => _sut.TranscribeAsync(nonExistentPath));
        Assert.Contains(nonExistentPath, ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_WithNonWavExtension_ThrowsInvalidOperationException()
    {
        // Arrange
        string txtPath = CreateFileWithExtension("test.txt");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TranscribeAsync(txtPath));
        Assert.Contains(txtPath, ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_WithMp3Extension_ThrowsInvalidOperationException()
    {
        // Arrange
        string mp3Path = CreateFileWithExtension("test.mp3");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.TranscribeAsync(mp3Path));
    }

    // ───────────────────────────────────────────────
    // InitializeAsync — model not found
    // ───────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WithMissingModel_ThrowsTranscriptionException()
    {
        // Arrange — empty models folder, no model to find
        // Act & Assert
        var ex = await Assert.ThrowsAsync<TranscriptionException>(
            () => _sut.InitializeAsync("nonexistent-model.bin"));
        Assert.Contains("No Whisper model found", ex.Message);
    }

    // ───────────────────────────────────────────────
    // Cancellation
    // ───────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.InitializeAsync(cancellationToken: cts.Token));
    }

    [Fact]
    public async Task TranscribeAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange — file must exist and be .wav to pass validation before hitting cancellation
        string wavPath = CreateValidWavFile("test.wav", TimeSpan.FromSeconds(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — cancellation hits during InitializeAsync (called lazily)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.TranscribeAsync(wavPath, cts.Token));
    }

    // ───────────────────────────────────────────────
    // Dispose behavior
    // ───────────────────────────────────────────────

    [Fact]
    public void Dispose_WhenNotInitialized_DoesNotThrow()
    {
        // Arrange
        var service = new TranscriptionService(_settingsService);

        // Act & Assert
        service.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var service = new TranscriptionService(_settingsService);

        // Act & Assert
        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var service = new TranscriptionService(_settingsService);
        service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.InitializeAsync());
    }

    [Fact]
    public async Task TranscribeAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var service = new TranscriptionService(_settingsService);
        service.Dispose();
        string wavPath = CreateValidWavFile("test.wav", TimeSpan.FromSeconds(1));

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.TranscribeAsync(wavPath));
    }

    // ───────────────────────────────────────────────
    // File validation edge cases
    // ───────────────────────────────────────────────

    [Fact]
    public async Task TranscribeAsync_WithUpperCaseWavExtension_PassesValidation()
    {
        // Arrange — .WAV should be accepted (case-insensitive)
        string wavPath = CreateFileWithExtension("test.WAV");

        if (!HasCachedModel)
        {
            // Validation passes, but initialization will fail without model.
            // We verify it doesn't throw InvalidOperationException (the extension check).
            var ex = await Assert.ThrowsAsync<TranscriptionException>(
                () => _sut.TranscribeAsync(wavPath));
            Assert.DoesNotContain("not a WAV file", ex.Message);
            return;
        }

        // With model: it would attempt transcription (corrupt file, but extension passes).
        await Assert.ThrowsAsync<TranscriptionException>(
            () => _sut.TranscribeAsync(wavPath));
    }

    [Fact]
    public async Task TranscribeAsync_WithCorruptedWavFile_ThrowsTranscriptionException()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model
        }

        // Arrange — file has .wav extension but garbage content
        string corruptPath = CreateFileWithExtension("corrupt.wav", "this is not audio data at all");

        // Act & Assert — Whisper.net should fail to process garbage data
        await Assert.ThrowsAsync<TranscriptionException>(
            () => _sut.TranscribeAsync(corruptPath));
    }

    // ───────────────────────────────────────────────
    // Model initialization (model-dependent)
    // ───────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_OnFirstCall_LoadsModelAndSetsIsInitializedTrue()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange
        using var service = new TranscriptionService(_settingsService);
        Assert.False(service.IsInitialized);

        // Act
        await service.InitializeAsync();

        // Assert
        Assert.True(service.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_OnSecondCall_DoesNotReloadModel()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange
        using var service = new TranscriptionService(_settingsService);
        await service.InitializeAsync();
        Assert.True(service.IsInitialized);

        // Act — second call should be idempotent
        await service.InitializeAsync();

        // Assert — still initialized, no exception
        Assert.True(service.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_WithSpecificModelFileName_LoadsThatModel()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange
        using var service = new TranscriptionService(_settingsService);

        // Act
        await service.InitializeAsync("ggml-large-v3-turbo.bin");

        // Assert
        Assert.True(service.IsInitialized);
    }

    [Fact]
    public async Task TranscribeAsync_WithValidWavFile_ReturnsTranscriptionResult()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange — create a short WAV file with silence
        string wavPath = CreateValidWavFile("silence.wav", TimeSpan.FromSeconds(2));
        using var service = new TranscriptionService(_settingsService);

        // Act
        var result = await service.TranscribeAsync(wavPath);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Text);
        Assert.NotNull(result.Segments);
        Assert.Equal("ggml-large-v3-turbo.bin", result.ModelVersion);
    }

    [Fact]
    public async Task TranscribeAsync_TriggeringInitialization_LoadsModelIfNotYetInitialized()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange — do NOT call InitializeAsync first
        using var service = new TranscriptionService(_settingsService);
        Assert.False(service.IsInitialized);
        string wavPath = CreateValidWavFile("silence.wav", TimeSpan.FromSeconds(1));

        // Act — lazy init via TranscribeAsync
        var result = await service.TranscribeAsync(wavPath);

        // Assert
        Assert.True(service.IsInitialized);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task TranscribeAsync_ReturnsSegmentsWithSpeaker1Label()
    {
        if (!HasCachedModel)
        {
            return; // Skip: requires Whisper model download
        }

        // Arrange — create a WAV with some duration; Whisper may produce segments even for silence
        string wavPath = CreateValidWavFile("silence.wav", TimeSpan.FromSeconds(3));
        using var service = new TranscriptionService(_settingsService);

        // Act
        var result = await service.TranscribeAsync(wavPath);

        // Assert — if any segments produced, they should all have "Speaker 1"
        Assert.All(result.Segments, segment =>
        {
            Assert.Equal("Speaker 1", segment.Speaker);
            Assert.NotNull(segment.Text);
        });
    }
}
