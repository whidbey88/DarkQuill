using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Compiles transcriptions into Markdown output for export.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Generates a Markdown string from a collection of transcription entries.
    /// </summary>
    /// <param name="projectName">Project name used as the document title.</param>
    /// <param name="entries">The transcription entries to export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A Markdown-formatted string containing all transcriptions.</returns>
    Task<string> ExportToMarkdownAsync(string projectName, IReadOnlyList<TranscriptionEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates Markdown and writes it to a file on disk.
    /// </summary>
    /// <param name="projectName">Project name used as the document title.</param>
    /// <param name="outputPath">Absolute path to write the Markdown file.</param>
    /// <param name="entries">The transcription entries to export.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExportAndSaveAsync(string projectName, string outputPath, IReadOnlyList<TranscriptionEntry> entries, CancellationToken cancellationToken = default);
}
