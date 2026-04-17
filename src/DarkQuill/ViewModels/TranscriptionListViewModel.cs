using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the transcription list panel. Displays transcriptions grouped by date
/// with copy, delete, and export actions.
/// </summary>
public partial class TranscriptionListViewModel : ObservableObject
{
    private readonly IStorageService _storageService;
    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;
    private readonly IDialogService _dialogService;

    private string _currentProject = string.Empty;

    /// <summary>
    /// Transcriptions grouped by date, sorted date-descending (today first).
    /// </summary>
    public ObservableCollection<DayGroup<TranscriptionEntry>> Transcriptions { get; } = [];

    /// <summary>
    /// Total number of non-deleted transcriptions across all groups.
    /// </summary>
    [ObservableProperty]
    private int _totalTranscriptionCount;

    /// <summary>
    /// Whether transcriptions are currently being loaded from disk.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Status message for error or success feedback.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Initializes the transcription list ViewModel with required services and message subscriptions.
    /// </summary>
    /// <param name="storageService">Storage service for transcription persistence and soft-delete.</param>
    /// <param name="settingsService">Settings service for folder path resolution.</param>
    /// <param name="clipboardService">Clipboard service for copy-to-clipboard.</param>
    /// <param name="dialogService">Dialog service for presenting modal dialogs.</param>
    public TranscriptionListViewModel(
        IStorageService storageService,
        ISettingsService settingsService,
        IClipboardService clipboardService,
        IDialogService dialogService)
    {
        _storageService = storageService;
        _settingsService = settingsService;
        _clipboardService = clipboardService;
        _dialogService = dialogService;

        WeakReferenceMessenger.Default.Register<TranscribeCompleteMessage>(this, (recipient, msg) =>
            ((TranscriptionListViewModel)recipient).OnTranscribeComplete(msg));

        WeakReferenceMessenger.Default.Register<TranscribeBatchCompleteMessage>(this, (recipient, msg) =>
            ((TranscriptionListViewModel)recipient).OnTranscribeBatchComplete(msg));
    }

    /// <summary>
    /// Sets the current project name for loading transcriptions.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    public void SetProject(string projectName)
    {
        _currentProject = projectName;
    }

    /// <summary>
    /// Loads all transcriptions for the current project from disk, grouping by date.
    /// Scans transcription JSON files matching the project name pattern.
    /// </summary>
    [RelayCommand]
    private async Task LoadTranscriptionsAsync()
    {
        if (string.IsNullOrEmpty(_currentProject))
            return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var allEntries = new List<TranscriptionEntry>();
            var transcriptionsFolder = settings.TranscriptionsFolder;

            if (Directory.Exists(transcriptionsFolder))
            {
                var prefix = $"{_currentProject}-";
                foreach (var file in Directory.EnumerateFiles(transcriptionsFolder, $"{prefix}*.json"))
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var datePart = fileName[prefix.Length..];
                    if (!DateTime.TryParseExact(datePart, "MM-dd-yyyy", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var fileDate))
                        continue;

                    var entries = await _storageService.LoadTranscriptionsAsync(_currentProject, fileDate);
                    allEntries.AddRange(entries);
                }
            }

            var groups = allEntries
                .GroupBy(e => e.Timestamp.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new DayGroup<TranscriptionEntry>(
                    g.Key,
                    new ObservableCollection<TranscriptionEntry>(g.OrderByDescending(e => e.Timestamp)),
                    g.Key.Date == DateTime.Today))
                .ToList();

            Transcriptions.Clear();
            foreach (var group in groups)
                Transcriptions.Add(group);

            TotalTranscriptionCount = allEntries.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Failed to load transcriptions: {ex.Message}";
            Debug.WriteLine($"Error loading transcriptions: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggles the expanded state of a day group.
    /// </summary>
    /// <param name="group">The day group to toggle.</param>
    [RelayCommand]
    private void ToggleDayGroup(DayGroup<TranscriptionEntry> group)
    {
        group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// Copies the full transcription text to the system clipboard.
    /// </summary>
    /// <param name="entry">The transcription entry to copy.</param>
    [RelayCommand]
    private async Task CopyTranscriptionAsync(TranscriptionEntry entry)
    {
        try
        {
            await _clipboardService.CopyToClipboardAsync(entry.Text);
            StatusMessage = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy: {ex.Message}";
            Debug.WriteLine($"Clipboard error: {ex}");
        }
    }

    /// <summary>
    /// Soft-deletes a single transcription and removes it from the UI.
    /// </summary>
    /// <param name="entry">The transcription entry to delete.</param>
    [RelayCommand]
    private async Task DeleteTranscriptionAsync(TranscriptionEntry entry)
    {
        await _storageService.MarkSoftDeletedAsync(entry.RecordingFileName);
        RemoveEntryFromGroups(entry);
        TotalTranscriptionCount--;
    }

    /// <summary>
    /// Soft-deletes all transcriptions in a day group and removes the group from the UI.
    /// </summary>
    /// <param name="group">The day group to delete.</param>
    [RelayCommand]
    private async Task DeleteDayGroupAsync(DayGroup<TranscriptionEntry> group)
    {
        foreach (var entry in group.Items)
            await _storageService.MarkSoftDeletedAsync(entry.RecordingFileName);

        TotalTranscriptionCount -= group.Items.Count;
        Transcriptions.Remove(group);
    }

    /// <summary>
    /// Handles a single transcription completion by adding it to the appropriate day group.
    /// </summary>
    private void OnTranscribeComplete(TranscribeCompleteMessage msg)
    {
        AddTranscriptionEntry(msg.Entry);
    }

    /// <summary>
    /// Handles a batch transcription completion by adding all entries.
    /// </summary>
    private void OnTranscribeBatchComplete(TranscribeBatchCompleteMessage msg)
    {
        foreach (var entry in msg.Entries)
            AddTranscriptionEntry(entry);
    }

    /// <summary>
    /// Adds a transcription entry to the appropriate day group, creating the group if needed.
    /// Deduplicates by recording filename.
    /// </summary>
    /// <param name="entry">The transcription entry to add.</param>
    private void AddTranscriptionEntry(TranscriptionEntry entry)
    {
        // Deduplicate: skip if an entry for this recording already exists
        var existing = Transcriptions
            .SelectMany(g => g.Items)
            .Any(e => string.Equals(e.RecordingFileName, entry.RecordingFileName, StringComparison.OrdinalIgnoreCase));
        if (existing)
            return;

        var dateKey = entry.Timestamp.Date;
        var todayGroup = Transcriptions.FirstOrDefault(g => g.Date.Date == dateKey);
        if (todayGroup is not null)
        {
            todayGroup.Items.Insert(0, entry);
        }
        else
        {
            var newGroup = new DayGroup<TranscriptionEntry>(
                dateKey,
                new ObservableCollection<TranscriptionEntry> { entry },
                isExpanded: true);

            // Insert in date-descending order
            var insertIndex = 0;
            for (var i = 0; i < Transcriptions.Count; i++)
            {
                if (Transcriptions[i].Date < dateKey)
                {
                    insertIndex = i;
                    break;
                }

                insertIndex = i + 1;
            }

            Transcriptions.Insert(insertIndex, newGroup);
        }

        TotalTranscriptionCount++;
    }

    /// <summary>
    /// Removes a transcription entry from its parent day group. Removes the group if it becomes empty.
    /// </summary>
    private void RemoveEntryFromGroups(TranscriptionEntry entry)
    {
        foreach (var group in Transcriptions)
        {
            if (!group.Items.Remove(entry))
                continue;

            if (group.Items.Count == 0)
                Transcriptions.Remove(group);

            return;
        }
    }

    /// <summary>
    /// Opens the transcriptions folder in the system file explorer.
    /// </summary>
    [RelayCommand]
    private async Task OpenTranscriptionsFolderAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var folder = settings.TranscriptionsFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open transcriptions folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the Whisper model selection dialog.
    /// </summary>
    [RelayCommand]
    private async Task OpenModelSelectionAsync()
    {
        await _dialogService.ShowModelSelectionAsync().ConfigureAwait(true);
    }
}
