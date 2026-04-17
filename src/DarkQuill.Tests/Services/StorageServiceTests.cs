using System.Text.Json;
using NSubstitute;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Unit tests for <see cref="StorageService"/> covering JSON serialization,
/// file I/O, soft-delete state management, folder operations, and error handling.
/// </summary>
public class StorageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _recordingsDir;
    private readonly string _transcriptionsDir;
    private readonly string _appStateFilePath;
    private readonly ISettingsService _settingsService;
    private readonly StorageService _sut;

    private static readonly DateTime TestDate = new(2026, 4, 15);

    public StorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        _recordingsDir = Path.Combine(_tempDir, "recordings");
        _transcriptionsDir = Path.Combine(_tempDir, "transcriptions");
        var appDataDir = Path.Combine(_tempDir, "appdata");
        Directory.CreateDirectory(_recordingsDir);
        Directory.CreateDirectory(_transcriptionsDir);
        Directory.CreateDirectory(appDataDir);

        _appStateFilePath = Path.Combine(appDataDir, "app-state.json");

        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings
            {
                RecordingsFolder = _recordingsDir,
                TranscriptionsFolder = _transcriptionsDir,
            });

        _sut = new StorageService(_settingsService, _appStateFilePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── SaveTranscriptionAsync ──────────────────────────────────────────

    [Fact]
    public async Task SaveTranscriptionAsync_WithNewEntry_CreatesJsonFile()
    {
        // Arrange
        var entry = CreateTestEntry("recording-001.wav");

        // Act
        await _sut.SaveTranscriptionAsync(entry, "test-project", TestDate);

        // Assert
        var expectedPath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public async Task SaveTranscriptionAsync_WithMultipleEntries_AppendsToExistingFile()
    {
        // Arrange
        var entry1 = CreateTestEntry("recording-001.wav");
        var entry2 = CreateTestEntry("recording-002.wav", text: "Second transcription.");

        // Act
        await _sut.SaveTranscriptionAsync(entry1, "test-project", TestDate);
        await _sut.SaveTranscriptionAsync(entry2, "test-project", TestDate);

        // Assert
        var filePath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        var json = await File.ReadAllTextAsync(filePath);
        var entries = JsonSerializer.Deserialize<List<TranscriptionEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Equal("recording-001.wav", entries[0].RecordingFileName);
        Assert.Equal("recording-002.wav", entries[1].RecordingFileName);
    }

    [Fact]
    public async Task SaveTranscriptionAsync_WithNullEntry_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.SaveTranscriptionAsync(null!, "test-project", TestDate));
    }

    [Fact]
    public async Task SaveTranscriptionAsync_WithEmptyProjectName_ThrowsArgumentException()
    {
        var entry = CreateTestEntry("recording-001.wav");
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.SaveTranscriptionAsync(entry, "", TestDate));
    }

    // ── LoadTranscriptionsAsync ─────────────────────────────────────────

    [Fact]
    public async Task LoadTranscriptionsAsync_WithExistingFile_ReturnsDeserializedEntries()
    {
        // Arrange
        var entries = new List<TranscriptionEntry>
        {
            CreateTestEntry("recording-001.wav", text: "First entry."),
            CreateTestEntry("recording-002.wav", text: "Second entry."),
        };
        var filePath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(filePath, json);

        // Act
        var loaded = await _sut.LoadTranscriptionsAsync("test-project", TestDate);

        // Assert
        Assert.Equal(2, loaded.Count);
        Assert.Equal("First entry.", loaded[0].Text);
        Assert.Equal("Second entry.", loaded[1].Text);
    }

    [Fact]
    public async Task LoadTranscriptionsAsync_WithMissingFile_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.LoadTranscriptionsAsync("nonexistent-project", TestDate);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadTranscriptionsAsync_WithCorruptedJson_ReturnsEmptyListAndLogsError()
    {
        // Arrange
        var filePath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        await File.WriteAllTextAsync(filePath, "{ this is not valid json !!! }");

        // Act
        var result = await _sut.LoadTranscriptionsAsync("test-project", TestDate);

        // Assert — returns empty list without throwing
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task LoadTranscriptionsAsync_WithEmptyProjectName_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.LoadTranscriptionsAsync("  ", TestDate));
    }

    // ── Round-trip ──────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndLoadRoundTrip_TranscriptionsPreserveCorrectly()
    {
        // Arrange
        var timestamp = new DateTime(2026, 4, 15, 14, 30, 22, DateTimeKind.Utc);
        var entry = new TranscriptionEntry(
            RecordingFileName: "2026-04-15_14-30-22.wav",
            Timestamp: timestamp,
            Duration: 42.5,
            Text: "The quick brown fox jumps over the lazy dog.",
            Segments: new List<TranscriptionSegment>
            {
                new("Speaker 1", "The quick brown fox"),
                new("Speaker 1", "jumps over the lazy dog."),
            });

        // Act
        await _sut.SaveTranscriptionAsync(entry, "roundtrip-project", TestDate);
        var loaded = await _sut.LoadTranscriptionsAsync("roundtrip-project", TestDate);

        // Assert
        Assert.Single(loaded);
        var result = loaded[0];
        Assert.Equal("2026-04-15_14-30-22.wav", result.RecordingFileName);
        Assert.Equal(timestamp, result.Timestamp);
        Assert.Equal(42.5, result.Duration);
        Assert.Equal("The quick brown fox jumps over the lazy dog.", result.Text);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal("Speaker 1", result.Segments[0].Speaker);
        Assert.Equal("The quick brown fox", result.Segments[0].Text);
        Assert.Equal("jumps over the lazy dog.", result.Segments[1].Text);
    }

    // ── EnsureRecordingFolderExistsAsync ─────────────────────────────────

    [Fact]
    public async Task EnsureRecordingFolderExistsAsync_WithMissingFolder_CreatesFolder()
    {
        // Act
        await _sut.EnsureRecordingFolderExistsAsync("test-project", TestDate);

        // Assert
        var expectedPath = Path.Combine(_recordingsDir, "test-project-04-15-2026");
        Assert.True(Directory.Exists(expectedPath));
    }

    [Fact]
    public async Task EnsureRecordingFolderExistsAsync_WithExistingFolder_DoesNotThrow()
    {
        // Arrange
        var folderPath = Path.Combine(_recordingsDir, "test-project-04-15-2026");
        Directory.CreateDirectory(folderPath);

        // Act — should not throw
        await _sut.EnsureRecordingFolderExistsAsync("test-project", TestDate);

        // Assert
        Assert.True(Directory.Exists(folderPath));
    }

    [Fact]
    public async Task EnsureRecordingFolderExistsAsync_WithEmptyProjectName_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.EnsureRecordingFolderExistsAsync("", TestDate));
    }

    // ── MarkSoftDeletedAsync ────────────────────────────────────────────

    [Fact]
    public async Task MarkSoftDeletedAsync_WithNewWavId_AddsToRecordingsList()
    {
        // Act
        await _sut.MarkSoftDeletedAsync("2026-04-15_14-30-22.wav");

        // Assert
        Assert.True(File.Exists(_appStateFilePath));
        var json = await File.ReadAllTextAsync(_appStateFilePath);
        var state = JsonSerializer.Deserialize<AppState>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(state);
        Assert.Contains("2026-04-15_14-30-22.wav", state.SoftDeletedRecordings);
        Assert.Empty(state.SoftDeletedTranscriptions);
    }

    [Fact]
    public async Task MarkSoftDeletedAsync_WithTranscriptionId_AddsToTranscriptionsList()
    {
        // Act
        await _sut.MarkSoftDeletedAsync("2026-04-15T14:30:22Z_test-project");

        // Assert
        var json = await File.ReadAllTextAsync(_appStateFilePath);
        var state = JsonSerializer.Deserialize<AppState>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(state);
        Assert.Empty(state.SoftDeletedRecordings);
        Assert.Contains("2026-04-15T14:30:22Z_test-project", state.SoftDeletedTranscriptions);
    }

    [Fact]
    public async Task MarkSoftDeletedAsync_WithDuplicateId_DoesNotDuplicate()
    {
        // Act
        await _sut.MarkSoftDeletedAsync("2026-04-15_14-30-22.wav");
        await _sut.MarkSoftDeletedAsync("2026-04-15_14-30-22.wav");

        // Assert
        var json = await File.ReadAllTextAsync(_appStateFilePath);
        var state = JsonSerializer.Deserialize<AppState>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(state);
        Assert.Single(state.SoftDeletedRecordings);
    }

    [Fact]
    public async Task MarkSoftDeletedAsync_WithEmptyItemId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.MarkSoftDeletedAsync(""));
    }

    // ── GetSoftDeletedIdsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetSoftDeletedIdsAsync_WithExistingState_ReturnsIds()
    {
        // Arrange
        var state = new AppState
        {
            SoftDeletedRecordings = ["recording-a.wav", "recording-b.wav"],
            SoftDeletedTranscriptions = ["trans-id-1"],
        };
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        });
        await File.WriteAllTextAsync(_appStateFilePath, json);

        // Act
        var result = await _sut.GetSoftDeletedIdsAsync();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("recording-a.wav", result);
        Assert.Contains("recording-b.wav", result);
        Assert.Contains("trans-id-1", result);
    }

    [Fact]
    public async Task GetSoftDeletedIdsAsync_WithMissingAppState_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.GetSoftDeletedIdsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSoftDeletedIdsAsync_WithCorruptedAppState_ReturnsEmptyList()
    {
        // Arrange
        await File.WriteAllTextAsync(_appStateFilePath, "not valid json {{{");

        // Act
        var result = await _sut.GetSoftDeletedIdsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── Soft-delete filtering ───────────────────────────────────────────

    [Fact]
    public async Task LoadTranscriptionsAsync_FiltersOutSoftDeletedEntries()
    {
        // Arrange — save two entries
        var entry1 = CreateTestEntry("keep-this.wav", text: "Keep me.");
        var entry2 = CreateTestEntry("delete-this.wav", text: "Delete me.");
        await _sut.SaveTranscriptionAsync(entry1, "test-project", TestDate);
        await _sut.SaveTranscriptionAsync(entry2, "test-project", TestDate);

        // Mark one as soft-deleted
        await _sut.MarkSoftDeletedAsync("delete-this.wav");

        // Act
        var loaded = await _sut.LoadTranscriptionsAsync("test-project", TestDate);

        // Assert
        Assert.Single(loaded);
        Assert.Equal("keep-this.wav", loaded[0].RecordingFileName);
        Assert.Equal("Keep me.", loaded[0].Text);
    }

    // ── JSON format verification ────────────────────────────────────────

    [Fact]
    public async Task JsonSerializationFormat_UsesCamelCasePropertyNames()
    {
        // Arrange
        var entry = CreateTestEntry("recording-001.wav");

        // Act
        await _sut.SaveTranscriptionAsync(entry, "test-project", TestDate);

        // Assert — read raw JSON and verify camelCase
        var filePath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        var json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"recordingFileName\"", json);
        Assert.Contains("\"timestamp\"", json);
        Assert.Contains("\"duration\"", json);
        Assert.Contains("\"text\"", json);
        Assert.Contains("\"segments\"", json);
        Assert.DoesNotContain("\"RecordingFileName\"", json);
        Assert.DoesNotContain("\"Timestamp\"", json);
    }

    [Fact]
    public async Task SaveTranscriptionAsync_PreservesSoftDeletedEntriesInFile()
    {
        // Arrange — save two entries, soft-delete one
        var entry1 = CreateTestEntry("first.wav", text: "First.");
        var entry2 = CreateTestEntry("second.wav", text: "Second.");
        await _sut.SaveTranscriptionAsync(entry1, "test-project", TestDate);
        await _sut.SaveTranscriptionAsync(entry2, "test-project", TestDate);
        await _sut.MarkSoftDeletedAsync("first.wav");

        // Act — save a third entry (should not lose the soft-deleted entry from the file)
        var entry3 = CreateTestEntry("third.wav", text: "Third.");
        await _sut.SaveTranscriptionAsync(entry3, "test-project", TestDate);

        // Assert — raw file should have all 3 entries
        var filePath = Path.Combine(_transcriptionsDir, "test-project-04-15-2026.json");
        var json = await File.ReadAllTextAsync(filePath);
        var rawEntries = JsonSerializer.Deserialize<List<TranscriptionEntry>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(rawEntries);
        Assert.Equal(3, rawEntries.Count);

        // But LoadTranscriptionsAsync should filter the soft-deleted one
        var loaded = await _sut.LoadTranscriptionsAsync("test-project", TestDate);
        Assert.Equal(2, loaded.Count);
        Assert.DoesNotContain(loaded, e => e.RecordingFileName == "first.wav");
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private static TranscriptionEntry CreateTestEntry(
        string recordingFileName,
        string text = "Sample transcription text.",
        double duration = 12.5)
    {
        return new TranscriptionEntry(
            RecordingFileName: recordingFileName,
            Timestamp: new DateTime(2026, 4, 15, 14, 30, 0, DateTimeKind.Utc),
            Duration: duration,
            Text: text,
            Segments: new List<TranscriptionSegment>
            {
                new("Speaker 1", text),
            });
    }
}
