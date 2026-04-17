using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the recording list panel. Displays recordings grouped by date,
/// supports multi-select, and provides transcribe and soft-delete actions.
/// </summary>
public partial class RecordingListViewModel : ObservableObject
{
    private readonly IStorageService _storageService;
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;
    private readonly IAudioPlaybackService _audioPlaybackService;

    private string _currentProject = string.Empty;

    /// <summary>
    /// Recordings grouped by date, sorted date-descending (today first).
    /// </summary>
    public ObservableCollection<DayGroup<Recording>> Recordings { get; } = [];

    /// <summary>
    /// Currently selected recordings for batch operations.
    /// </summary>
    public ObservableCollection<Recording> SelectedRecordings { get; } = [];

    /// <summary>
    /// Total number of non-deleted recordings across all groups.
    /// </summary>
    [ObservableProperty]
    private int _totalRecordingCount;

    /// <summary>
    /// Whether recordings are currently being loaded from disk.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Status message for error display.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// The file name of the recording currently being played back, or null if nothing is playing.
    /// Used by the view to toggle play/stop button visibility per recording item.
    /// </summary>
    [ObservableProperty]
    private string? _currentlyPlayingFileName;

    /// <summary>
    /// Initializes the recording list ViewModel with required services and message subscriptions.
    /// </summary>
    /// <param name="storageService">Storage service for soft-delete and transcription state.</param>
    /// <param name="projectService">Project service for folder name resolution.</param>
    /// <param name="settingsService">Settings service for folder path resolution.</param>
    /// <param name="audioPlaybackService">Audio playback service for playing recordings.</param>
    public RecordingListViewModel(
        IStorageService storageService,
        IProjectService projectService,
        ISettingsService settingsService,
        IAudioPlaybackService audioPlaybackService)
    {
        _storageService = storageService;
        _projectService = projectService;
        _settingsService = settingsService;
        _audioPlaybackService = audioPlaybackService;

        _audioPlaybackService.PlaybackStopped += OnPlaybackStopped;

        SelectedRecordings.CollectionChanged += (_, _) =>
            TranscribeSelectedCommand.NotifyCanExecuteChanged();

        WeakReferenceMessenger.Default.Register<RecordingCompletedMessage>(this, (recipient, msg) =>
            ((RecordingListViewModel)recipient).OnRecordingCompleted(msg));

        WeakReferenceMessenger.Default.Register<TranscribeCompleteMessage>(this, (recipient, msg) =>
            ((RecordingListViewModel)recipient).OnTranscribeComplete(msg));
    }

    /// <summary>
    /// Sets the current project name for loading recordings.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    public void SetProject(string projectName)
    {
        _currentProject = projectName;
    }

    /// <summary>
    /// Loads all recordings for the current project from disk, grouping by date.
    /// Excludes soft-deleted recordings and determines transcription status.
    /// </summary>
    [RelayCommand]
    private async Task LoadRecordingsAsync()
    {
        if (string.IsNullOrEmpty(_currentProject))
            return;

        IsLoading = true;
        StatusMessage = string.Empty;

        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var softDeletedIds = await _storageService.GetSoftDeletedIdsAsync();
            var softDeletedSet = new HashSet<string>(softDeletedIds, StringComparer.OrdinalIgnoreCase);
            var allRecordings = new List<Recording>();

            if (Directory.Exists(settings.RecordingsFolder))
            {
                var prefix = $"{_currentProject}-";
                foreach (var dir in Directory.EnumerateDirectories(settings.RecordingsFolder, $"{prefix}*"))
                {
                    var folderName = Path.GetFileName(dir);
                    var datePart = folderName[prefix.Length..];
                    if (!DateTime.TryParseExact(datePart, "MM-dd-yyyy", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var folderDate))
                        continue;

                    var transcriptions = await _storageService.LoadTranscriptionsAsync(_currentProject, folderDate);
                    var transcribedFileNames = new HashSet<string>(
                        transcriptions.Select(t => t.RecordingFileName), StringComparer.OrdinalIgnoreCase);

                    foreach (var wavPath in Directory.EnumerateFiles(dir, "*.wav"))
                    {
                        var fileName = Path.GetFileName(wavPath);
                        if (softDeletedSet.Contains(fileName))
                            continue;

                        var timestamp = ParseTimestampFromFileName(fileName)
                                        ?? File.GetCreationTime(wavPath);
                        var status = transcribedFileNames.Contains(fileName)
                            ? TranscriptionStatus.Complete
                            : TranscriptionStatus.Pending;

                        var duration = GetWavDuration(wavPath);
                        allRecordings.Add(new Recording(fileName, wavPath, duration, timestamp, status));
                    }
                }
            }

            var groups = allRecordings
                .GroupBy(r => r.Timestamp.Date)
                .OrderByDescending(g => g.Key)
                .Select(g => new DayGroup<Recording>(
                    g.Key,
                    new ObservableCollection<Recording>(g.OrderByDescending(r => r.Timestamp)),
                    g.Key.Date == DateTime.Today))
                .ToList();

            Recordings.Clear();
            SelectedRecordings.Clear();
            foreach (var group in groups)
                Recordings.Add(group);

            TotalRecordingCount = allRecordings.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Failed to load recordings: {ex.Message}";
            Debug.WriteLine($"Error loading recordings: {ex}");
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
    private void ToggleDayGroup(DayGroup<Recording> group)
    {
        group.IsExpanded = !group.IsExpanded;
    }

    /// <summary>
    /// Starts playback of the specified recording. If the recording is already playing, stops it.
    /// </summary>
    /// <param name="recording">The recording to play.</param>
    [RelayCommand]
    private async Task PlayRecordingAsync(Recording recording)
    {
        try
        {
            CurrentlyPlayingFileName = recording.FileName;
            await _audioPlaybackService.PlayAsync(recording.Path);
        }
        catch (Exception ex)
        {
            CurrentlyPlayingFileName = null;
            Debug.WriteLine($"Error playing recording: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the currently playing recording.
    /// </summary>
    [RelayCommand]
    private async Task StopPlaybackAsync()
    {
        try
        {
            await _audioPlaybackService.StopAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error stopping playback: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the <see cref="IAudioPlaybackService.PlaybackStopped"/> event.
    /// Marshals the UI update to the dispatcher thread since the event may fire from a background thread.
    /// </summary>
    private void OnPlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => CurrentlyPlayingFileName = null);
    }

    /// <summary>
    /// Posts a message to transcribe a single recording.
    /// </summary>
    /// <param name="recording">The recording to transcribe.</param>
    [RelayCommand]
    private void TranscribeSingle(Recording recording)
    {
        if (recording.TranscriptionStatus == TranscriptionStatus.Complete)
            return;

        WeakReferenceMessenger.Default.Send(new TranscribeSingleMessage(recording));
    }

    /// <summary>
    /// Posts a message to transcribe all selected recordings that are not yet complete.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTranscribeSelected))]
    private void TranscribeSelected()
    {
        var pending = SelectedRecordings
            .Where(r => r.TranscriptionStatus != TranscriptionStatus.Complete)
            .ToList();

        if (pending.Count == 0)
            return;

        WeakReferenceMessenger.Default.Send(new TranscribeBatchMessage(pending));
    }

    /// <summary>
    /// Whether the transcribe-selected command can execute.
    /// </summary>
    private bool CanTranscribeSelected() =>
        SelectedRecordings.Count > 0 &&
        SelectedRecordings.Any(r => r.TranscriptionStatus != TranscriptionStatus.Complete);

    /// <summary>
    /// Soft-deletes a single recording and removes it from the UI.
    /// </summary>
    /// <param name="recording">The recording to delete.</param>
    [RelayCommand]
    private async Task DeleteSingleAsync(Recording recording)
    {
        if (string.Equals(CurrentlyPlayingFileName, recording.FileName, StringComparison.OrdinalIgnoreCase))
        {
            await _audioPlaybackService.StopAsync();
        }

        await _storageService.MarkSoftDeletedAsync(recording.FileName);
        RemoveRecordingFromGroups(recording);
        SelectedRecordings.Remove(recording);
        TotalRecordingCount--;
        WeakReferenceMessenger.Default.Send(new RecordingDeletedMessage(recording.FileName));
    }

    /// <summary>
    /// Soft-deletes all recordings in a day group and removes the group from the UI.
    /// </summary>
    /// <param name="group">The day group to delete.</param>
    [RelayCommand]
    private async Task DeleteDayGroupAsync(DayGroup<Recording> group)
    {
        foreach (var recording in group.Items)
        {
            await _storageService.MarkSoftDeletedAsync(recording.FileName);
            SelectedRecordings.Remove(recording);
            WeakReferenceMessenger.Default.Send(new RecordingDeletedMessage(recording.FileName));
        }

        TotalRecordingCount -= group.Items.Count;
        Recordings.Remove(group);
    }

    /// <summary>
    /// Handles recording selection with support for Ctrl+Click (toggle) and Shift+Click (range).
    /// Called from the view's code-behind when a recording item is clicked.
    /// </summary>
    /// <param name="recording">The clicked recording.</param>
    /// <param name="isCtrlHeld">Whether the Ctrl key was held during click.</param>
    /// <param name="isShiftHeld">Whether the Shift key was held during click.</param>
    public void SelectRecording(Recording recording, bool isCtrlHeld, bool isShiftHeld)
    {
        if (isCtrlHeld)
        {
            if (SelectedRecordings.Contains(recording))
                SelectedRecordings.Remove(recording);
            else
                SelectedRecordings.Add(recording);
        }
        else if (isShiftHeld && SelectedRecordings.Count > 0)
        {
            var allRecordings = Recordings.SelectMany(g => g.Items).ToList();
            var lastSelected = SelectedRecordings[^1];
            var startIndex = allRecordings.IndexOf(lastSelected);
            var endIndex = allRecordings.IndexOf(recording);

            if (startIndex >= 0 && endIndex >= 0)
            {
                var from = Math.Min(startIndex, endIndex);
                var to = Math.Max(startIndex, endIndex);
                for (var i = from; i <= to; i++)
                {
                    if (!SelectedRecordings.Contains(allRecordings[i]))
                        SelectedRecordings.Add(allRecordings[i]);
                }
            }
        }
        else
        {
            SelectedRecordings.Clear();
            SelectedRecordings.Add(recording);
        }
    }

    /// <summary>
    /// Updates a recording's transcription status to Complete when its transcription finishes.
    /// Since <see cref="Recording"/> is an immutable record, the item is replaced in its parent group.
    /// </summary>
    private void OnTranscribeComplete(TranscribeCompleteMessage msg)
    {
        foreach (var group in Recordings)
        {
            for (var i = 0; i < group.Items.Count; i++)
            {
                if (!string.Equals(group.Items[i].FileName, msg.RecordingId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var original = group.Items[i];
                group.Items[i] = original with { TranscriptionStatus = TranscriptionStatus.Complete };
                return;
            }
        }
    }

    /// <summary>
    /// Handles a new recording completion by adding it to today's group.
    /// </summary>
    private void OnRecordingCompleted(RecordingCompletedMessage msg)
    {
        var fileName = Path.GetFileName(msg.FilePath);
        var recording = new Recording(
            FileName: fileName,
            Path: msg.FilePath,
            Duration: msg.Duration,
            Timestamp: DateTime.Now,
            TranscriptionStatus: TranscriptionStatus.Pending);

        var todayGroup = Recordings.FirstOrDefault(g => g.Date.Date == DateTime.Today);
        if (todayGroup is not null)
        {
            todayGroup.Items.Insert(0, recording);
        }
        else
        {
            var newGroup = new DayGroup<Recording>(
                DateTime.Today,
                new ObservableCollection<Recording> { recording },
                isExpanded: true);
            Recordings.Insert(0, newGroup);
        }

        TotalRecordingCount++;
    }

    /// <summary>
    /// Removes a recording from its parent day group. Removes the group if it becomes empty.
    /// </summary>
    private void RemoveRecordingFromGroups(Recording recording)
    {
        foreach (var group in Recordings)
        {
            if (!group.Items.Remove(recording))
                continue;

            if (group.Items.Count == 0)
                Recordings.Remove(group);

            return;
        }
    }

    /// <summary>
    /// Parses a timestamp from a recording filename with pattern "yyyy-MM-dd_HH-mm-ss.wav".
    /// </summary>
    /// <param name="fileName">The recording filename.</param>
    /// <returns>Parsed timestamp, or <c>null</c> if the filename does not match the expected pattern.</returns>
    private static DateTime? ParseTimestampFromFileName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (DateTime.TryParseExact(nameWithoutExtension, "yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    /// <summary>
    /// Reads the duration of a WAV file from its header. Returns <see cref="TimeSpan.Zero"/> on failure.
    /// </summary>
    private static TimeSpan GetWavDuration(string wavPath)
    {
        try
        {
            using var reader = new NAudio.Wave.WaveFileReader(wavPath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Opens the recordings folder in the system file explorer.
    /// </summary>
    [RelayCommand]
    private async Task OpenRecordingsFolderAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync();
            var folder = settings.RecordingsFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open recordings folder: {ex.Message}");
        }
    }
}
