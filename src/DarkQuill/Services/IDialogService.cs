using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Abstracts modal dialog presentation for MVVM ViewModels.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the project selection dialog and returns the selected project, or null if cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selected project info, or null if the dialog was cancelled.</returns>
    Task<ProjectInfo?> ShowProjectDialogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows an error dialog with a title and message.
    /// </summary>
    /// <param name="title">Error dialog title.</param>
    /// <param name="message">Error message body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a confirmation dialog and returns the user's choice.
    /// </summary>
    /// <param name="title">Confirmation dialog title.</param>
    /// <param name="message">Confirmation message body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if confirmed, false if cancelled.</returns>
    Task<bool> ShowConfirmAsync(string title, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the audio settings dialog for configuring input device and level.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ShowAudioSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the Whisper model selection dialog for choosing a GGML model.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ShowModelSelectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a save file dialog and returns the selected file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="defaultFileName">Suggested file name.</param>
    /// <param name="filter">File type filter (e.g., "Markdown files|*.md|All files|*.*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The selected file path, or null/empty if cancelled.</returns>
    Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter, CancellationToken cancellationToken = default);
}
