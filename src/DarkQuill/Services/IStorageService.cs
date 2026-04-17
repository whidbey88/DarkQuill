using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Reads and writes transcription JSON files, manages recording folders, and tracks soft-deleted items.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Loads all transcription entries for a project on a given date.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date to load transcriptions for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of transcription entries.</returns>
    Task<IReadOnlyList<TranscriptionEntry>> LoadTranscriptionsAsync(string projectName, DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a transcription entry to the project's date-scoped JSON file.
    /// </summary>
    /// <param name="entry">The transcription entry to save.</param>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date scope for the transcription file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveTranscriptionAsync(TranscriptionEntry entry, string projectName, DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the identifiers of all soft-deleted items.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of soft-deleted item identifiers.</returns>
    Task<IReadOnlyList<string>> GetSoftDeletedIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an item as soft-deleted by adding its identifier to the state file.
    /// </summary>
    /// <param name="itemId">The identifier of the item to soft-delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkSoftDeletedAsync(string itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures the recording folder exists for a project on a given date, creating it if necessary.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date for the recording folder.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnsureRecordingFolderExistsAsync(string projectName, DateTime date, CancellationToken cancellationToken = default);
}
