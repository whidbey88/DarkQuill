using System.Text;
using DarkQuill.Models;
using DarkQuill.Services;
using Xunit;

namespace DarkQuill.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ExportService"/> covering Markdown generation,
/// file export, date grouping, speaker labels, and edge cases.
/// </summary>
public class ExportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExportService _sut;

    public ExportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DarkQuillTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new ExportService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // ── ExportToMarkdownAsync ───────────────────────────────────────────

    [Fact]
    public async Task ExportToMarkdownAsync_WithSingleEntry_GeneratesFormattedMarkdown()
    {
        // Arrange
        var entry = CreateEntry("recording.wav", new DateTime(2026, 4, 15, 14, 23, 45),
            "The quick brown fox jumps over the lazy dog.",
            new TranscriptionSegment("Speaker 1", "The quick brown fox jumps over the lazy dog."));

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("test-project", [entry]);

        // Assert
        Assert.Contains("# test-project", markdown);
        Assert.Contains("## April 15, 2026", markdown);
        Assert.Contains("**14:23:45**", markdown);
        Assert.Contains("Speaker 1", markdown);
        Assert.Contains("The quick brown fox jumps over the lazy dog.", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithMultipleDays_GroupsByDate()
    {
        // Arrange
        var entry1 = CreateEntry("rec-1.wav", new DateTime(2026, 4, 15, 10, 0, 0), "Day one text.",
            new TranscriptionSegment("Speaker 1", "Day one text."));
        var entry2 = CreateEntry("rec-2.wav", new DateTime(2026, 4, 16, 11, 0, 0), "Day two text.",
            new TranscriptionSegment("Speaker 1", "Day two text."));

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("multi-day", [entry1, entry2]);

        // Assert
        Assert.Contains("## April 15, 2026", markdown);
        Assert.Contains("## April 16, 2026", markdown);
        Assert.Contains("Day one text.", markdown);
        Assert.Contains("Day two text.", markdown);

        // Verify ordering: April 15 appears before April 16
        var idx15 = markdown.IndexOf("## April 15, 2026", StringComparison.Ordinal);
        var idx16 = markdown.IndexOf("## April 16, 2026", StringComparison.Ordinal);
        Assert.True(idx15 < idx16);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithMultipleEntriesSameDay_ListsAllInGroup()
    {
        // Arrange
        var entry1 = CreateEntry("rec-1.wav", new DateTime(2026, 4, 15, 9, 0, 0), "First.",
            new TranscriptionSegment("Speaker 1", "First."));
        var entry2 = CreateEntry("rec-2.wav", new DateTime(2026, 4, 15, 10, 30, 0), "Second.",
            new TranscriptionSegment("Speaker 1", "Second."));
        var entry3 = CreateEntry("rec-3.wav", new DateTime(2026, 4, 15, 14, 45, 0), "Third.",
            new TranscriptionSegment("Speaker 1", "Third."));

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("same-day", [entry1, entry2, entry3]);

        // Assert — single date heading, all three entries present
        var headingCount = CountOccurrences(markdown, "## April 15, 2026");
        Assert.Equal(1, headingCount);
        Assert.Contains("**09:00:00**", markdown);
        Assert.Contains("**10:30:00**", markdown);
        Assert.Contains("**14:45:00**", markdown);
        Assert.Contains("First.", markdown);
        Assert.Contains("Second.", markdown);
        Assert.Contains("Third.", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithNoSpeakers_OmitsSpeakerLine()
    {
        // Arrange — entry with empty segments list
        var entry = new TranscriptionEntry(
            RecordingFileName: "recording.wav",
            Timestamp: new DateTime(2026, 4, 15, 14, 0, 0),
            Duration: 10.0,
            Text: "No speakers here.",
            Segments: []);

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("no-speakers", [entry]);

        // Assert — timestamp line should not contain the em-dash separator
        Assert.Contains("**14:00:00**", markdown);
        Assert.DoesNotContain("—", markdown);
        Assert.Contains("No speakers here.", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithMultipleSpeakers_ListsSeparatedByComma()
    {
        // Arrange
        var entry = new TranscriptionEntry(
            RecordingFileName: "recording.wav",
            Timestamp: new DateTime(2026, 4, 15, 14, 0, 0),
            Duration: 30.0,
            Text: "Multi-speaker dialogue.",
            Segments:
            [
                new TranscriptionSegment("Speaker 1", "Hello."),
                new TranscriptionSegment("Speaker 2", "Hi there."),
                new TranscriptionSegment("Speaker 3", "Greetings."),
                new TranscriptionSegment("Speaker 1", "How are you?"),
            ]);

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("multi-speaker", [entry]);

        // Assert — distinct speakers, comma-separated
        Assert.Contains("Speaker 1, Speaker 2, Speaker 3", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithEmptyEntries_ReturnsProjectNameAndNote()
    {
        // Act
        var markdown = await _sut.ExportToMarkdownAsync("empty-project",
            Array.Empty<TranscriptionEntry>());

        // Assert
        Assert.Contains("# empty-project", markdown);
        Assert.Contains("No transcriptions available.", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithLongText_FormatsCorrectly()
    {
        // Arrange — multi-paragraph long text
        var longText = string.Join(" ", Enumerable.Repeat(
            "This is a sentence that is part of a much longer transcription.", 50));
        var entry = CreateEntry("recording.wav", new DateTime(2026, 4, 15, 14, 0, 0), longText,
            new TranscriptionSegment("Speaker 1", longText));

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("long-text", [entry]);

        // Assert — full text preserved, no truncation
        Assert.Contains(longText, markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_DateFormatting_IsConsistentAndReadable()
    {
        // Arrange — entries across multiple months
        var entries = new List<TranscriptionEntry>
        {
            CreateEntry("rec-1.wav", new DateTime(2026, 1, 5, 10, 0, 0), "January entry.",
                new TranscriptionSegment("Speaker 1", "January entry.")),
            CreateEntry("rec-2.wav", new DateTime(2026, 12, 25, 15, 0, 0), "December entry.",
                new TranscriptionSegment("Speaker 1", "December entry.")),
        };

        // Act
        var markdown = await _sut.ExportToMarkdownAsync("date-format", entries);

        // Assert — readable date format (MMMM d, yyyy)
        Assert.Contains("## January 5, 2026", markdown);
        Assert.Contains("## December 25, 2026", markdown);
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithNullProjectName_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.ExportToMarkdownAsync(null!, []));
    }

    [Fact]
    public async Task ExportToMarkdownAsync_WithNullEntries_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.ExportToMarkdownAsync("project", null!));
    }

    // ── ExportAndSaveAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ExportAndSaveAsync_WithValidPath_WritesMarkdownFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "export.md");
        var entry = CreateEntry("recording.wav", new DateTime(2026, 4, 15, 14, 23, 45),
            "Exported text.",
            new TranscriptionSegment("Speaker 1", "Exported text."));

        // Act
        await _sut.ExportAndSaveAsync("test-project", outputPath, [entry]);

        // Assert
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# test-project", content);
        Assert.Contains("**14:23:45**", content);
        Assert.Contains("Exported text.", content);
    }

    [Fact]
    public async Task ExportAndSaveAsync_WithMissingDirectory_CreatesDirectory()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "nested", "deep", "export.md");

        // Act
        await _sut.ExportAndSaveAsync("test-project", outputPath,
            Array.Empty<TranscriptionEntry>());

        // Assert
        Assert.True(File.Exists(outputPath));
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("# test-project", content);
    }

    [Fact]
    public async Task ExportAndSaveAsync_WithExistingFile_OverwritesFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDir, "export.md");
        await File.WriteAllTextAsync(outputPath, "Old content that should be overwritten.");

        var entry = CreateEntry("recording.wav", new DateTime(2026, 4, 15, 14, 0, 0),
            "New content.",
            new TranscriptionSegment("Speaker 1", "New content."));

        // Act
        await _sut.ExportAndSaveAsync("new-project", outputPath, [entry]);

        // Assert
        var content = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("Old content", content);
        Assert.Contains("# new-project", content);
        Assert.Contains("New content.", content);
    }

    [Fact]
    public async Task ExportAndSaveAsync_WithEmptyOutputPath_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.ExportAndSaveAsync("project", "", []));
    }

    [Fact]
    public async Task ExportAndSaveAsync_UTF8Encoding_WritesCorrectly()
    {
        // Arrange — non-ASCII characters
        var outputPath = Path.Combine(_tempDir, "utf8-export.md");
        var text = "Café résumé naïve — en español: ¿Cómo estás? 日本語テスト";
        var entry = CreateEntry("recording.wav", new DateTime(2026, 4, 15, 14, 0, 0), text,
            new TranscriptionSegment("Speaker 1", text));

        // Act
        await _sut.ExportAndSaveAsync("utf8-project", outputPath, [entry]);

        // Assert — read back with UTF-8 and verify characters preserved
        var bytes = await File.ReadAllBytesAsync(outputPath);
        var content = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Café résumé naïve", content);
        Assert.Contains("¿Cómo estás?", content);
        Assert.Contains("日本語テスト", content);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static TranscriptionEntry CreateEntry(
        string recordingFileName,
        DateTime timestamp,
        string text,
        params TranscriptionSegment[] segments)
    {
        return new TranscriptionEntry(
            RecordingFileName: recordingFileName,
            Timestamp: timestamp,
            Duration: 12.5,
            Text: text,
            Segments: segments.ToList());
    }

    private static int CountOccurrences(string source, string substring)
    {
        var count = 0;
        var idx = 0;
        while ((idx = source.IndexOf(substring, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += substring.Length;
        }

        return count;
    }
}
