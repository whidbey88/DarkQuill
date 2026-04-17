using System.Diagnostics;
using System.Text;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Compiles transcriptions into Markdown output for export.
/// Stateless service that generates formatted Markdown from transcription data
/// and optionally writes it to disk.
/// </summary>
public class ExportService : IExportService
{
    /// <summary>
    /// Generates a Markdown string from a collection of transcription entries.
    /// Entries are grouped by date with timestamps and speaker labels.
    /// </summary>
    /// <param name="projectName">Project name used as the document title.</param>
    /// <param name="entries">The transcription entries to export.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A Markdown-formatted string containing all transcriptions.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
    public Task<string> ExportToMarkdownAsync(
        string projectName, IReadOnlyList<TranscriptionEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.AppendLine($"# {projectName}");
        sb.AppendLine();

        if (entries.Count == 0)
        {
            sb.AppendLine("No transcriptions available.");
            return Task.FromResult(sb.ToString());
        }

        var groupedByDate = entries
            .OrderBy(e => e.Timestamp)
            .GroupBy(e => e.Timestamp.Date);

        var isFirstGroup = true;
        foreach (var dateGroup in groupedByDate)
        {
            if (!isFirstGroup)
            {
                sb.AppendLine();
            }

            isFirstGroup = false;
            sb.AppendLine($"## {dateGroup.Key:MMMM d, yyyy}");
            sb.AppendLine();

            var isFirstEntry = true;
            foreach (var entry in dateGroup)
            {
                if (!isFirstEntry)
                {
                    sb.AppendLine();
                }

                isFirstEntry = false;
                AppendEntry(sb, entry);
            }
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Generates Markdown and writes it to a file on disk.
    /// Creates the output directory if it does not exist.
    /// </summary>
    /// <param name="projectName">Project name used as the document title.</param>
    /// <param name="outputPath">Absolute path to write the Markdown file.</param>
    /// <param name="entries">The transcription entries to export.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="projectName"/> or <paramref name="outputPath"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the output directory cannot be created or the file cannot be written.</exception>
    public async Task ExportAndSaveAsync(
        string projectName, string outputPath, IReadOnlyList<TranscriptionEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var markdown = await ExportToMarkdownAsync(projectName, entries, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(outputPath)!;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Failed to create export directory: {directory}", ex);
        }

        try
        {
            await File.WriteAllTextAsync(outputPath, markdown, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Permission denied writing export file: {outputPath}", ex);
        }
    }

    /// <summary>
    /// Appends a single transcription entry to the <see cref="StringBuilder"/>.
    /// </summary>
    private static void AppendEntry(StringBuilder sb, TranscriptionEntry entry)
    {
        var timestamp = entry.Timestamp.ToString("HH:mm:ss");

        var speakers = GetDistinctSpeakers(entry.Segments);
        if (speakers.Length > 0)
        {
            sb.AppendLine($"**{timestamp}** — {speakers}");
        }
        else
        {
            sb.AppendLine($"**{timestamp}**");
        }

        if (!string.IsNullOrWhiteSpace(entry.Text))
        {
            sb.AppendLine(entry.Text);
        }
    }

    /// <summary>
    /// Extracts distinct speaker labels from segments as a comma-separated string.
    /// Returns an empty string if no segments or no speaker information.
    /// </summary>
    private static string GetDistinctSpeakers(IReadOnlyList<TranscriptionSegment>? segments)
    {
        if (segments is null or { Count: 0 })
        {
            return string.Empty;
        }

        try
        {
            var speakers = segments
                .Select(s => s.Speaker)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return speakers.Count > 0 ? string.Join(", ", speakers) : string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error extracting speakers: {ex.Message}");
            return string.Empty;
        }
    }
}
