using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Reads and writes transcription JSON files, manages recording folder structure,
/// and tracks soft-deleted items via <c>app-state.json</c>.
/// All file operations use atomic writes (temp file then move) to prevent corruption.
/// </summary>
public class StorageService : IStorageService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISettingsService _settingsService;
    private readonly string _appStateFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageService"/> class.
    /// The app-state file path is computed from the user's application data directory.
    /// </summary>
    /// <param name="settingsService">Settings service for resolving folder paths.</param>
    public StorageService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DarkQuill");
        _appStateFilePath = Path.Combine(appDataDir, "app-state.json");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageService"/> class with a custom app-state file path.
    /// Used for testing to redirect file I/O to a temporary directory.
    /// </summary>
    /// <param name="settingsService">Settings service for resolving folder paths.</param>
    /// <param name="appStateFilePath">Full path to the app-state JSON file.</param>
    internal StorageService(ISettingsService settingsService, string appStateFilePath)
    {
        _settingsService = settingsService;
        _appStateFilePath = appStateFilePath;
    }

    /// <summary>
    /// Loads all transcription entries for a project on a given date.
    /// Returns an empty list if the file does not exist or contains malformed JSON.
    /// Entries marked as soft-deleted are filtered out.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date to load transcriptions for.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of transcription entries, excluding soft-deleted items.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> is null or empty.</exception>
    public async Task<IReadOnlyList<TranscriptionEntry>> LoadTranscriptionsAsync(
        string projectName, DateTime date, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var filePath = BuildTranscriptionFilePath(settings.TranscriptionsFolder, projectName, date);

        if (!File.Exists(filePath))
        {
            return Array.Empty<TranscriptionEntry>();
        }

        List<TranscriptionEntry>? entries;
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            entries = JsonSerializer.Deserialize<List<TranscriptionEntry>>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Malformed transcription JSON at {filePath}: {ex.Message}");
            return Array.Empty<TranscriptionEntry>();
        }

        if (entries is null)
        {
            return Array.Empty<TranscriptionEntry>();
        }

        var softDeletedIds = await GetSoftDeletedIdsAsync(cancellationToken).ConfigureAwait(false);
        if (softDeletedIds.Count == 0)
        {
            return entries.AsReadOnly();
        }

        var softDeletedSet = new HashSet<string>(softDeletedIds, StringComparer.Ordinal);
        return entries
            .Where(e => !softDeletedSet.Contains(e.RecordingFileName))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Saves a transcription entry to the project's date-scoped JSON file.
    /// Creates the transcriptions folder if it does not exist.
    /// Uses atomic write (temp file then move) to prevent corruption.
    /// </summary>
    /// <param name="entry">The transcription entry to save.</param>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date scope for the transcription file.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transcriptions folder cannot be created.</exception>
    /// <exception cref="IOException">Thrown when the file cannot be written due to permissions.</exception>
    public async Task SaveTranscriptionAsync(
        TranscriptionEntry entry, string projectName, DateTime date, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var filePath = BuildTranscriptionFilePath(settings.TranscriptionsFolder, projectName, date);
        var directory = Path.GetDirectoryName(filePath)!;

        EnsureDirectoryExists(directory, "transcriptions");

        // Load existing entries directly from file (not via LoadTranscriptionsAsync,
        // which filters soft-deleted — we need all entries for persistence)
        List<TranscriptionEntry> entries;
        if (File.Exists(filePath))
        {
            try
            {
                var existingJson = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                entries = JsonSerializer.Deserialize<List<TranscriptionEntry>>(existingJson, SerializerOptions)
                          ?? [];
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Malformed transcription JSON at {filePath}, starting fresh: {ex.Message}");
                entries = [];
            }
        }
        else
        {
            entries = [];
        }

        entries.Add(entry);

        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        await WriteAtomicAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the identifiers of all soft-deleted items (both recordings and transcriptions).
    /// Returns an empty list if the state file does not exist or is corrupted.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list combining all soft-deleted recording filenames and transcription identifiers.</returns>
    public async Task<IReadOnlyList<string>> GetSoftDeletedIdsAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadAppStateAsync(cancellationToken).ConfigureAwait(false);
        var combined = new List<string>(state.SoftDeletedRecordings.Count + state.SoftDeletedTranscriptions.Count);
        combined.AddRange(state.SoftDeletedRecordings);
        combined.AddRange(state.SoftDeletedTranscriptions);
        return combined.AsReadOnly();
    }

    /// <summary>
    /// Marks an item as soft-deleted by adding its identifier to <c>app-state.json</c>.
    /// Recording identifiers (ending in <c>.wav</c>) go to <c>softDeletedRecordings</c>;
    /// all others go to <c>softDeletedTranscriptions</c>.
    /// Uses atomic write to prevent corruption.
    /// </summary>
    /// <param name="itemId">The identifier of the item to soft-delete.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="itemId"/> is null or empty.</exception>
    public async Task MarkSoftDeletedAsync(string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        var state = await LoadAppStateAsync(cancellationToken).ConfigureAwait(false);

        if (itemId.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            if (!state.SoftDeletedRecordings.Contains(itemId))
            {
                state.SoftDeletedRecordings.Add(itemId);
            }
        }
        else
        {
            if (!state.SoftDeletedTranscriptions.Contains(itemId))
            {
                state.SoftDeletedTranscriptions.Add(itemId);
            }
        }

        await SaveAppStateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the recording folder exists for a project on a given date, creating it if necessary.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date for the recording folder.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the recordings folder cannot be created.</exception>
    public async Task EnsureRecordingFolderExistsAsync(
        string projectName, DateTime date, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var settings = await _settingsService.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        var folderPath = Path.Combine(settings.RecordingsFolder, $"{projectName}-{date:MM-dd-yyyy}");

        await Task.Run(() => EnsureDirectoryExists(folderPath, "recordings"), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the file path for a date-scoped transcription JSON file.
    /// </summary>
    private static string BuildTranscriptionFilePath(string transcriptionsFolder, string projectName, DateTime date)
    {
        return Path.Combine(transcriptionsFolder, $"{projectName}-{date:MM-dd-yyyy}.json");
    }

    /// <summary>
    /// Ensures a directory exists, creating it if necessary.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the directory cannot be created.</exception>
    private static void EnsureDirectoryExists(string directoryPath, string purpose)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Failed to create {purpose} directory: {directoryPath}", ex);
        }
    }

    /// <summary>
    /// Loads the application state from <c>app-state.json</c>.
    /// Returns an empty state if the file does not exist or is corrupted.
    /// </summary>
    private async Task<AppState> LoadAppStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_appStateFilePath))
        {
            return new AppState();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_appStateFilePath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<AppState>(json, SerializerOptions) ?? new AppState();
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Corrupted app-state JSON at {_appStateFilePath}: {ex.Message}");
            return new AppState();
        }
    }

    /// <summary>
    /// Saves the application state to <c>app-state.json</c> using atomic write.
    /// </summary>
    private async Task SaveAppStateAsync(AppState state, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_appStateFilePath)!;
        EnsureDirectoryExists(directory, "app data");

        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await WriteAtomicAsync(_appStateFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes content to a file atomically by writing to a temporary file first,
    /// then moving it to the final location.
    /// </summary>
    /// <exception cref="IOException">Thrown when the file cannot be written due to permissions.</exception>
    private static async Task WriteAtomicAsync(string finalPath, string content, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetDirectoryName(finalPath)!, Path.GetRandomFileName());

        try
        {
            await File.WriteAllTextAsync(tempPath, content, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Permission denied writing file: {finalPath}", ex);
        }
        finally
        {
            // Clean up temp file if move failed
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}
