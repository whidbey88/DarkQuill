using NSubstitute;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ProjectService"/> covering naming normalization,
/// folder/file scanning, project creation, and project loading.
/// </summary>
public class ProjectServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _recordingsDir;
    private readonly string _transcriptionsDir;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;
    private readonly ProjectService _sut;

    public ProjectServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        _recordingsDir = Path.Combine(_tempDir, "recordings");
        _transcriptionsDir = Path.Combine(_tempDir, "transcriptions");
        Directory.CreateDirectory(_recordingsDir);
        Directory.CreateDirectory(_transcriptionsDir);

        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings
            {
                RecordingsFolder = _recordingsDir,
                TranscriptionsFolder = _transcriptionsDir,
            });

        _storageService = Substitute.For<IStorageService>();
        _sut = new ProjectService(_settingsService, _storageService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── NormalizeProjectName ─────────────────────────────────────────────

    [Fact]
    public void NormalizeProjectName_WithSpacesAndUppercase_ReturnsLowercaseWithHyphens()
    {
        var result = _sut.NormalizeProjectName("Airport Dialogue Study");
        Assert.Equal("airport-dialogue-study", result);
    }

    [Fact]
    public void NormalizeProjectName_WithSpecialCharacters_RemovesSafelyKeepsHyphensUnderscores()
    {
        var result = _sut.NormalizeProjectName("My/Project (2026)");
        Assert.Equal("myproject-2026", result);
    }

    [Fact]
    public void NormalizeProjectName_WithLeadingTrailingSpaces_Trims()
    {
        var result = _sut.NormalizeProjectName("  test project  ");
        Assert.Equal("test-project", result);
    }

    [Fact]
    public void NormalizeProjectName_WithConsecutiveHyphens_CollapsesToSingle()
    {
        var result = _sut.NormalizeProjectName("my---project");
        Assert.Equal("my-project", result);
    }

    [Fact]
    public void NormalizeProjectName_WithUnderscores_PreservesUnderscores()
    {
        var result = _sut.NormalizeProjectName("my_project_name");
        Assert.Equal("my_project_name", result);
    }

    [Fact]
    public void NormalizeProjectName_WithNullOrWhitespace_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sut.NormalizeProjectName(""));
        Assert.Throws<ArgumentException>(() => _sut.NormalizeProjectName("   "));
        Assert.Throws<ArgumentNullException>(() => _sut.NormalizeProjectName(null!));
    }

    [Fact]
    public void NormalizeProjectName_WithOnlyInvalidChars_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _sut.NormalizeProjectName("///"));
    }

    // ── GetProjectFolderName ─────────────────────────────────────────────

    [Fact]
    public void GetProjectFolderName_WithProjectNameAndDate_FormatsCorrectly()
    {
        var result = _sut.GetProjectFolderName("airport-dialogue-study", new DateTime(2026, 4, 15));
        Assert.Equal("airport-dialogue-study-04-15-2026", result);
    }

    [Fact]
    public void GetProjectFolderName_WithSingleDigitMonthAndDay_PadsWithZero()
    {
        var result = _sut.GetProjectFolderName("test", new DateTime(2026, 1, 5));
        Assert.Equal("test-01-05-2026", result);
    }

    // ── ScanProjectsForDateAsync ─────────────────────────────────────────

    [Fact]
    public async Task ScanProjectsForDateAsync_WithExistingRecordingFolder_ReturnsProjectInfo()
    {
        var date = new DateTime(2026, 4, 15);
        Directory.CreateDirectory(Path.Combine(_recordingsDir, "airport-dialogue-04-15-2026"));

        var results = await _sut.ScanProjectsForDateAsync(date);

        Assert.Single(results);
        Assert.Equal("airport-dialogue", results[0].Name);
    }

    [Fact]
    public async Task ScanProjectsForDateAsync_WithExistingTranscriptionFile_ReturnsProjectInfo()
    {
        var date = new DateTime(2026, 4, 15);
        await File.WriteAllTextAsync(
            Path.Combine(_transcriptionsDir, "airport-dialogue-04-15-2026.json"), "[]");

        var results = await _sut.ScanProjectsForDateAsync(date);

        Assert.Single(results);
        Assert.Equal("airport-dialogue", results[0].Name);
    }

    [Fact]
    public async Task ScanProjectsForDateAsync_WithNoProjectsOnDate_ReturnsEmptyList()
    {
        var date = new DateTime(2099, 12, 31);

        var results = await _sut.ScanProjectsForDateAsync(date);

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanProjectsForDateAsync_WithMultipleProjects_ReturnsAllUnique()
    {
        var date = new DateTime(2026, 4, 15);
        Directory.CreateDirectory(Path.Combine(_recordingsDir, "project-one-04-15-2026"));
        Directory.CreateDirectory(Path.Combine(_recordingsDir, "project-two-04-15-2026"));

        var results = await _sut.ScanProjectsForDateAsync(date);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, p => p.Name == "project-one");
        Assert.Contains(results, p => p.Name == "project-two");
    }

    [Fact]
    public async Task ScanProjectsForDateAsync_WithRecordingAndTranscriptionForSameProject_DeduplicatesCorrectly()
    {
        var date = new DateTime(2026, 4, 15);
        Directory.CreateDirectory(Path.Combine(_recordingsDir, "my-project-04-15-2026"));
        await File.WriteAllTextAsync(
            Path.Combine(_transcriptionsDir, "my-project-04-15-2026.json"), "[]");

        var results = await _sut.ScanProjectsForDateAsync(date);

        Assert.Single(results);
        Assert.Equal("my-project", results[0].Name);
    }

    [Fact]
    public async Task ScanProjectsForDateAsync_WithNonExistentFolders_ReturnsEmptyList()
    {
        // Point settings to non-existent directories
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings
            {
                RecordingsFolder = Path.Combine(_tempDir, "nonexistent-recordings"),
                TranscriptionsFolder = Path.Combine(_tempDir, "nonexistent-transcriptions"),
            });

        var results = await _sut.ScanProjectsForDateAsync(new DateTime(2026, 4, 15));

        Assert.NotNull(results);
        Assert.Empty(results);
    }

    // ── CreateProjectAsync ───────────────────────────────────────────────

    [Fact]
    public async Task CreateProjectAsync_WithValidName_CreatesRecordingFolder()
    {
        await _sut.CreateProjectAsync("New Project");

        var today = DateTime.Now;
        var expectedFolder = $"new-project-{today:MM-dd-yyyy}";
        var recordingPath = Path.Combine(_recordingsDir, expectedFolder);

        Assert.True(Directory.Exists(recordingPath));
        Assert.True(Directory.Exists(_transcriptionsDir));
    }

    [Fact]
    public async Task CreateProjectAsync_WithExistingProject_DoesNotThrow()
    {
        await _sut.CreateProjectAsync("Existing Project");

        // Call again with the same name — should not throw
        var exception = await Record.ExceptionAsync(() => _sut.CreateProjectAsync("Existing Project"));
        Assert.Null(exception);
    }

    // ── LoadProjectAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadProjectAsync_WithExistingRecordingFolder_ReturnsProjectInfo()
    {
        var today = DateTime.Now;
        var folderName = $"new-project-{today:MM-dd-yyyy}";
        Directory.CreateDirectory(Path.Combine(_recordingsDir, folderName));

        var result = await _sut.LoadProjectAsync("new-project");

        Assert.Equal("new-project", result.Name);
        Assert.True(result.CreatedDate <= DateTime.Now);
        Assert.True(result.LastModifiedDate <= DateTime.Now);
    }

    [Fact]
    public async Task LoadProjectAsync_WithExistingTranscriptionFile_ReturnsProjectInfo()
    {
        var today = DateTime.Now;
        var fileName = $"test-project-{today:MM-dd-yyyy}.json";
        await File.WriteAllTextAsync(Path.Combine(_transcriptionsDir, fileName), "[]");

        var result = await _sut.LoadProjectAsync("test-project");

        Assert.Equal("test-project", result.Name);
    }

    [Fact]
    public async Task LoadProjectAsync_WithNonExistentProject_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.LoadProjectAsync("nonexistent"));
    }

    [Fact]
    public async Task LoadProjectAsync_NormalizesInputName()
    {
        var today = DateTime.Now;
        var folderName = $"my-project-{today:MM-dd-yyyy}";
        Directory.CreateDirectory(Path.Combine(_recordingsDir, folderName));

        // Pass unnormalized name — should still find the project
        var result = await _sut.LoadProjectAsync("My Project");

        Assert.Equal("my-project", result.Name);
    }
}
