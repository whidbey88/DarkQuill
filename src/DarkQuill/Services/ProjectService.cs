using System.Diagnostics;
using System.Text.RegularExpressions;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Manages project lifecycle, naming conventions, folder scanning, and startup dialog logic.
/// Projects are identified by date-based subfolders following the pattern
/// <c>{normalized-name}-MM-dd-yyyy</c>.
/// </summary>
public partial class ProjectService(ISettingsService settingsService, IStorageService storageService) : IProjectService
{
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IStorageService _storageService = storageService;

    /// <summary>
    /// Regex matching the date suffix <c>-MM-dd-yyyy</c> at the end of a project folder or file name.
    /// </summary>
    [GeneratedRegex(@"-(\d{2}-\d{2}-\d{4})$", RegexOptions.Compiled)]
    private static partial Regex DateSuffixRegex();

    /// <summary>
    /// Regex matching characters that are not alphanumeric, hyphens, or underscores.
    /// </summary>
    [GeneratedRegex(@"[^a-z0-9\-_]")]
    private static partial Regex InvalidCharsRegex();

    /// <summary>
    /// Regex matching consecutive hyphens.
    /// </summary>
    [GeneratedRegex(@"-{2,}")]
    private static partial Regex ConsecutiveHyphensRegex();

    /// <inheritdoc />
    /// <summary>
    /// Normalizes a project name by lowercasing, replacing spaces with hyphens,
    /// and removing invalid filesystem characters.
    /// </summary>
    /// <param name="name">Human-readable project name.</param>
    /// <returns>Normalized name suitable for use in folder and file names.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty, or produces an empty normalized result.</exception>
    public string NormalizeProjectName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = name.ToLowerInvariant().Replace(' ', '-');
        normalized = InvalidCharsRegex().Replace(normalized, "");
        normalized = ConsecutiveHyphensRegex().Replace(normalized, "-");
        normalized = normalized.Trim('-');

        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Project name contains no valid characters.", nameof(name));
        }

        return normalized;
    }

    /// <inheritdoc />
    /// <summary>
    /// Returns the folder/file name for a project on a specific date.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date for the project folder.</param>
    /// <returns>Formatted string <c>{projectName}-MM-dd-yyyy</c>.</returns>
    public string GetProjectFolderName(string projectName, DateTime date)
    {
        return $"{projectName}-{date:MM-dd-yyyy}";
    }

    /// <inheritdoc />
    /// <summary>
    /// Scans the recordings and transcriptions folders for projects matching the given date.
    /// Returns a deduplicated list of <see cref="ProjectInfo"/> objects.
    /// </summary>
    /// <param name="date">The date to scan for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of projects found for the given date, or empty if none found.</returns>
    public async Task<IReadOnlyList<ProjectInfo>> ScanProjectsForDateAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var dateSuffix = date.ToString("MM-dd-yyyy");
        var projects = new Dictionary<string, ProjectInfo>(StringComparer.OrdinalIgnoreCase);

        ScanRecordingFolders(settings.RecordingsFolder, dateSuffix, projects);
        ScanTranscriptionFiles(settings.TranscriptionsFolder, dateSuffix, projects);

        return projects.Values.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    /// <summary>
    /// Creates a new project by normalizing the name and setting up the recordings subfolder
    /// and transcriptions directory for today's date.
    /// </summary>
    /// <param name="projectName">Human-readable project name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> is invalid.</exception>
    /// <exception cref="IOException">Thrown when folder creation fails.</exception>
    public async Task CreateProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProjectName(projectName);
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Now;
        var folderName = GetProjectFolderName(normalized, today);

        var recordingPath = Path.Combine(settings.RecordingsFolder, folderName);
        var transcriptionsDir = settings.TranscriptionsFolder;

        try
        {
            Directory.CreateDirectory(recordingPath);
            Debug.WriteLine($"Created recording folder: {recordingPath}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException($"Failed to create recording folder: {recordingPath}", ex);
        }

        try
        {
            Directory.CreateDirectory(transcriptionsDir);
            Debug.WriteLine($"Ensured transcriptions directory exists: {transcriptionsDir}");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException($"Failed to create transcriptions directory: {transcriptionsDir}", ex);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Loads an existing project by verifying its recording folder or transcription file
    /// exists for today's date.
    /// </summary>
    /// <param name="projectName">Normalized or human-readable project name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project metadata populated from the file system.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the project does not exist on disk.</exception>
    public async Task<ProjectInfo> LoadProjectAsync(string projectName, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeProjectName(projectName);
        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Now;
        var folderName = GetProjectFolderName(normalized, today);

        var recordingPath = Path.Combine(settings.RecordingsFolder, folderName);
        var transcriptionPath = Path.Combine(settings.TranscriptionsFolder, folderName + ".json");

        DateTime createdDate;
        DateTime lastModifiedDate;

        if (Directory.Exists(recordingPath))
        {
            var dirInfo = new DirectoryInfo(recordingPath);
            createdDate = dirInfo.CreationTime;
            lastModifiedDate = dirInfo.LastWriteTime;
        }
        else if (File.Exists(transcriptionPath))
        {
            var fileInfo = new FileInfo(transcriptionPath);
            createdDate = fileInfo.CreationTime;
            lastModifiedDate = fileInfo.LastWriteTime;
        }
        else
        {
            throw new InvalidOperationException(
                $"Project '{normalized}' does not exist for date {today:MM-dd-yyyy}. " +
                $"Expected recording folder at '{recordingPath}' or transcription file at '{transcriptionPath}'.");
        }

        return new ProjectInfo(normalized, createdDate, lastModifiedDate);
    }

    /// <summary>
    /// Scans the recordings folder for subdirectories matching the date suffix
    /// and adds discovered projects to the dictionary.
    /// </summary>
    private static void ScanRecordingFolders(string recordingsFolder, string dateSuffix, Dictionary<string, ProjectInfo> projects)
    {
        if (!Directory.Exists(recordingsFolder))
        {
            Debug.WriteLine($"Recordings folder does not exist: {recordingsFolder}");
            return;
        }

        Debug.WriteLine($"Scanning for projects in {recordingsFolder}");

        foreach (var dir in Directory.EnumerateDirectories(recordingsFolder, $"*-{dateSuffix}"))
        {
            var folderName = Path.GetFileName(dir);
            var projectName = ExtractProjectName(folderName, dateSuffix);
            if (projectName is null) continue;

            var dirInfo = new DirectoryInfo(dir);
            projects.TryAdd(projectName, new ProjectInfo(projectName, dirInfo.CreationTime, dirInfo.LastWriteTime));
        }
    }

    /// <summary>
    /// Scans the transcriptions folder for JSON files matching the date suffix
    /// and adds discovered projects to the dictionary.
    /// </summary>
    private static void ScanTranscriptionFiles(string transcriptionsFolder, string dateSuffix, Dictionary<string, ProjectInfo> projects)
    {
        if (!Directory.Exists(transcriptionsFolder))
        {
            Debug.WriteLine($"Transcriptions folder does not exist: {transcriptionsFolder}");
            return;
        }

        Debug.WriteLine($"Scanning for transcriptions in {transcriptionsFolder}");

        foreach (var file in Directory.EnumerateFiles(transcriptionsFolder, $"*-{dateSuffix}.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var projectName = ExtractProjectName(fileName, dateSuffix);
            if (projectName is null) continue;

            if (!projects.ContainsKey(projectName))
            {
                var fileInfo = new FileInfo(file);
                projects.TryAdd(projectName, new ProjectInfo(projectName, fileInfo.CreationTime, fileInfo.LastWriteTime));
            }
        }
    }

    /// <summary>
    /// Extracts the project name from a folder or file name by stripping the date suffix.
    /// </summary>
    /// <param name="name">Folder or file name (without extension).</param>
    /// <param name="dateSuffix">Expected date suffix in <c>MM-dd-yyyy</c> format.</param>
    /// <returns>The project name, or <c>null</c> if the name does not end with the expected suffix.</returns>
    private static string? ExtractProjectName(string name, string dateSuffix)
    {
        var suffix = $"-{dateSuffix}";
        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var projectName = name[..^suffix.Length];
        return string.IsNullOrEmpty(projectName) ? null : projectName;
    }
}
