using System.Text.Json;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Unit tests for <see cref="SettingsService"/> covering load, save, round-trip,
/// missing file defaults, corrupted JSON resilience, and hotkey deserialization.
/// </summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsFilePath;
    private readonly SettingsService _sut;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsFilePath = Path.Combine(_tempDir, "settings.json");
        _sut = new SettingsService(_settingsFilePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadSettingsAsync_WithExistingSettingsFile_ReturnsDeserializedSettings()
    {
        // Arrange
        var settings = new ApplicationSettings
        {
            RecordingsFolder = "/custom/recordings",
            TranscriptionsFolder = "/custom/transcriptions",
            AudioDeviceId = "device-42",
            InputLevel = 0.5,
            GpuAccelerationEnabled = false,
            Hotkeys = new Dictionary<string, string>
            {
                ["startRecording"] = "F5",
                ["stopRecording"] = "Escape",
                ["transcribeLatest"] = "Ctrl+T",
            },
        };
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(_settingsFilePath, json);

        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert
        Assert.Equal("/custom/recordings", loaded.RecordingsFolder);
        Assert.Equal("/custom/transcriptions", loaded.TranscriptionsFolder);
        Assert.Equal("device-42", loaded.AudioDeviceId);
        Assert.Equal(0.5, loaded.InputLevel);
        Assert.False(loaded.GpuAccelerationEnabled);
        Assert.Equal("F5", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Escape", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Ctrl+T", loaded.Hotkeys["transcribeLatest"]);
    }

    [Fact]
    public async Task LoadSettingsAsync_WithMissingFile_ReturnsDefaultSettings()
    {
        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert
        Assert.Equal("./recordings", loaded.RecordingsFolder);
        Assert.Equal("./transcriptions", loaded.TranscriptionsFolder);
        Assert.Equal("", loaded.AudioDeviceId);
        Assert.Equal(0.8, loaded.InputLevel);
        Assert.True(loaded.GpuAccelerationEnabled);
        Assert.Equal("F9", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Space", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Ctrl+Shift+T", loaded.Hotkeys["transcribeLatest"]);
    }

    [Fact]
    public async Task LoadSettingsAsync_WithCorruptedJson_ReturnsDefaultSettings()
    {
        // Arrange
        await File.WriteAllTextAsync(_settingsFilePath, "{ this is not valid json !!! }");

        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert — returns defaults without throwing
        Assert.Equal("./recordings", loaded.RecordingsFolder);
        Assert.Equal(0.8, loaded.InputLevel);
        Assert.True(loaded.GpuAccelerationEnabled);
        Assert.Equal("F9", loaded.Hotkeys["startRecording"]);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithValidSettings_WritesJsonFile()
    {
        // Arrange
        var settings = new ApplicationSettings
        {
            RecordingsFolder = "/saved/recordings",
            TranscriptionsFolder = "/saved/transcriptions",
            AudioDeviceId = "mic-1",
            InputLevel = 0.6,
            GpuAccelerationEnabled = false,
            Hotkeys = new Dictionary<string, string>
            {
                ["startRecording"] = "F10",
            },
        };

        // Act
        await _sut.SaveSettingsAsync(settings);

        // Assert
        Assert.True(File.Exists(_settingsFilePath));
        var json = await File.ReadAllTextAsync(_settingsFilePath);
        var deserialized = JsonSerializer.Deserialize<ApplicationSettings>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(deserialized);
        Assert.Equal("/saved/recordings", deserialized.RecordingsFolder);
        Assert.Equal("mic-1", deserialized.AudioDeviceId);
        Assert.Equal(0.6, deserialized.InputLevel);
        Assert.False(deserialized.GpuAccelerationEnabled);
        Assert.Equal("F10", deserialized.Hotkeys["startRecording"]);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithMissingParentDirectory_CreatesDirectoryAndWritesFile()
    {
        // Arrange — point to a nested directory that doesn't exist yet
        var nestedPath = Path.Combine(_tempDir, "deep", "nested", "dir", "settings.json");
        var sut = new SettingsService(nestedPath);
        var settings = new ApplicationSettings { RecordingsFolder = "/nested/test" };

        // Act
        await sut.SaveSettingsAsync(settings);

        // Assert
        Assert.True(File.Exists(nestedPath));
        var json = await File.ReadAllTextAsync(nestedPath);
        Assert.Contains("nested/test", json);
    }

    [Fact]
    public async Task SaveAndLoadRoundTrip_SettingsPersistCorrectly()
    {
        // Arrange — all non-default values
        var original = new ApplicationSettings
        {
            RecordingsFolder = "/roundtrip/recordings",
            TranscriptionsFolder = "/roundtrip/transcriptions",
            AudioDeviceId = "usb-microphone-3",
            InputLevel = 0.35,
            GpuAccelerationEnabled = false,
            Hotkeys = new Dictionary<string, string>
            {
                ["startRecording"] = "F12",
                ["stopRecording"] = "Enter",
                ["transcribeLatest"] = "Alt+T",
                ["customAction"] = "Ctrl+Shift+X",
            },
        };

        // Act
        await _sut.SaveSettingsAsync(original);
        var loaded = await _sut.LoadSettingsAsync();

        // Assert
        Assert.Equal(original.RecordingsFolder, loaded.RecordingsFolder);
        Assert.Equal(original.TranscriptionsFolder, loaded.TranscriptionsFolder);
        Assert.Equal(original.AudioDeviceId, loaded.AudioDeviceId);
        Assert.Equal(original.InputLevel, loaded.InputLevel);
        Assert.Equal(original.GpuAccelerationEnabled, loaded.GpuAccelerationEnabled);
        Assert.Equal("F12", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Enter", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Alt+T", loaded.Hotkeys["transcribeLatest"]);
        Assert.Equal("Ctrl+Shift+X", loaded.Hotkeys["customAction"]);
    }

    [Fact]
    public async Task LoadSettingsAsync_WithCustomHotkeys_DeserializesHotkeysCorrectly()
    {
        // Arrange — write JSON with camelCase hotkey keys directly
        var json = """
            {
              "recordingsFolder": "./recordings",
              "transcriptionsFolder": "./transcriptions",
              "audioDeviceId": "",
              "inputLevel": 0.8,
              "gpuAccelerationEnabled": true,
              "hotkeys": {
                "startRecording": "Ctrl+R",
                "stopRecording": "Ctrl+S",
                "transcribeLatest": "Ctrl+Shift+T",
                "exportAll": "Ctrl+E"
              }
            }
            """;
        await File.WriteAllTextAsync(_settingsFilePath, json);

        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert
        Assert.Equal(4, loaded.Hotkeys.Count);
        Assert.Equal("Ctrl+R", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Ctrl+S", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Ctrl+Shift+T", loaded.Hotkeys["transcribeLatest"]);
        Assert.Equal("Ctrl+E", loaded.Hotkeys["exportAll"]);
    }

    [Fact]
    public async Task LoadSettingsAsync_WithPartialHotkeys_MergesDefaultHotkeys()
    {
        // Arrange — only one hotkey defined; the other two defaults should be merged in
        var json = """
            {
              "hotkeys": {
                "startRecording": "F5"
              }
            }
            """;
        await File.WriteAllTextAsync(_settingsFilePath, json);

        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert — user-defined key preserved, missing defaults merged
        Assert.Equal("F5", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Space", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Ctrl+Shift+T", loaded.Hotkeys["transcribeLatest"]);
    }

    [Fact]
    public async Task SaveSettingsAsync_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _sut.SaveSettingsAsync(null!));
    }

    [Fact]
    public async Task LoadSettingsAsync_WithEmptyJsonObject_ReturnsModelDefaultsWithMergedHotkeys()
    {
        // Arrange
        await File.WriteAllTextAsync(_settingsFilePath, "{}");

        // Act
        var loaded = await _sut.LoadSettingsAsync();

        // Assert — model property defaults apply, plus hotkey defaults are merged
        Assert.Equal("./recordings", loaded.RecordingsFolder);
        Assert.Equal(0.8, loaded.InputLevel);
        Assert.Equal("F9", loaded.Hotkeys["startRecording"]);
        Assert.Equal("Space", loaded.Hotkeys["stopRecording"]);
        Assert.Equal("Ctrl+Shift+T", loaded.Hotkeys["transcribeLatest"]);
    }
}
