using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// Root ViewModel for the main application window. Coordinates recording, transcription,
/// and project management workflows.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly IStorageService _storageService;
    private readonly ISettingsService _settingsService;
    private readonly IExportService _exportService;
    private readonly IDialogService _dialogService;

    /// <summary>
    /// Name of the currently active project, or empty if none selected.
    /// </summary>
    [ObservableProperty]
    private string _currentProject = string.Empty;

    /// <summary>
    /// Subtitle text shown below the project name.
    /// </summary>
    [ObservableProperty]
    private string _projectSubtitle = "Select or create a project to get started";

    /// <summary>
    /// Whether a transcription operation is currently in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isTranscribing;

    /// <summary>
    /// Status text shown during transcription operations.
    /// </summary>
    [ObservableProperty]
    private string _transcriptionStatusText = string.Empty;

    /// <summary>
    /// Whether a project is currently loaded and the workspace is active.
    /// </summary>
    public bool HasProject => !string.IsNullOrEmpty(CurrentProject);

    /// <summary>
    /// The recording control panel ViewModel, exposed for view binding.
    /// </summary>
    public RecordingControlViewModel RecordingControlViewModel { get; }

    /// <summary>
    /// The recording list panel ViewModel, exposed for view binding.
    /// </summary>
    public RecordingListViewModel RecordingListViewModel { get; }

    /// <summary>
    /// The transcription list panel ViewModel, exposed for view binding.
    /// </summary>
    public TranscriptionListViewModel TranscriptionListViewModel { get; }

    /// <summary>
    /// Initializes the main ViewModel with all required services.
    /// </summary>
    public MainViewModel(
        IAudioRecorder audioRecorder,
        ITranscriptionService transcriptionService,
        IProjectService projectService,
        IStorageService storageService,
        ISettingsService settingsService,
        IExportService exportService,
        IHotkeyService hotkeyService,
        IDialogService dialogService,
        RecordingControlViewModel recordingControlViewModel,
        RecordingListViewModel recordingListViewModel,
        TranscriptionListViewModel transcriptionListViewModel)
    {
        _transcriptionService = transcriptionService;
        _storageService = storageService;
        _settingsService = settingsService;
        _exportService = exportService;
        _dialogService = dialogService;
        RecordingControlViewModel = recordingControlViewModel;
        RecordingListViewModel = recordingListViewModel;
        TranscriptionListViewModel = transcriptionListViewModel;

        WeakReferenceMessenger.Default.Register<ProjectSelectedMessage>(this, async (recipient, msg) =>
        {
            var vm = (MainViewModel)recipient;
            vm.CurrentProject = msg.ProjectName;
            vm.ProjectSubtitle = $"Project: {msg.ProjectName}";
            vm.OnPropertyChanged(nameof(HasProject));
            vm.RecordingListViewModel.SetProject(msg.ProjectName);
            vm.TranscriptionListViewModel.SetProject(msg.ProjectName);
            await vm.RecordingListViewModel.LoadRecordingsCommand.ExecuteAsync(null);
            await vm.TranscriptionListViewModel.LoadTranscriptionsCommand.ExecuteAsync(null);
        });

        WeakReferenceMessenger.Default.Register<HotkeyPressedMessage>(this, (recipient, msg) =>
        {
            var vm = (MainViewModel)recipient;
            if (msg.Hotkey.Id == HotkeyIds.TranscribeLatest)
            {
                vm.TranscribeMostRecentCommand.Execute(null);
            }
        });

        WeakReferenceMessenger.Default.Register<TranscribeSingleMessage>(this, async (recipient, msg) =>
        {
            var vm = (MainViewModel)recipient;
            await vm.TranscribeRecordingAsync(msg.Recording);
        });

        WeakReferenceMessenger.Default.Register<TranscribeBatchMessage>(this, async (recipient, msg) =>
        {
            var vm = (MainViewModel)recipient;
            await vm.TranscribeBatchAsync(msg.Recordings);
        });
    }

    /// <summary>
    /// Shows the project dialog for creating or selecting a project.
    /// </summary>
    [RelayCommand]
    private async Task NewSessionAsync()
    {
        await _dialogService.ShowProjectDialogAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Finds the most recent untranscribed recording and sends a transcription request via messenger.
    /// Triggered by the Ctrl+Shift+T hotkey.
    /// </summary>
    [RelayCommand]
    private void TranscribeMostRecent()
    {
        var mostRecent = RecordingListViewModel.Recordings
            .SelectMany(g => g.Items)
            .OrderByDescending(r => r.Timestamp)
            .FirstOrDefault(r => r.TranscriptionStatus == TranscriptionStatus.Pending);

        if (mostRecent is not null)
        {
            WeakReferenceMessenger.Default.Send(new TranscribeSingleMessage(mostRecent));
        }
    }

    /// <summary>
    /// Exports all transcriptions for the current project to a Markdown file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        try
        {
            var allEntries = TranscriptionListViewModel.Transcriptions
                .SelectMany(g => g.Items)
                .OrderBy(e => e.Timestamp)
                .ToList();

            if (allEntries.Count == 0)
            {
                await _dialogService.ShowErrorAsync("Nothing to Export",
                    "There are no transcriptions to export. Record and transcribe some audio first.").ConfigureAwait(true);
                return;
            }

            var outputPath = await _dialogService.ShowSaveFileDialogAsync(
                "Export Transcriptions",
                $"{CurrentProject}-export.md",
                "Markdown files|*.md|All files|*.*").ConfigureAwait(true);

            if (string.IsNullOrEmpty(outputPath))
                return;

            await _exportService.ExportAndSaveAsync(CurrentProject, outputPath, allEntries).ConfigureAwait(true);
            TranscriptionListViewModel.StatusMessage = "Exported successfully";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Export error: {ex}");
            await _dialogService.ShowErrorAsync("Export Error", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether the export command can execute.
    /// </summary>
    private bool CanExport() => HasProject && !IsTranscribing;

    /// <summary>
    /// Transcribes a single recording: initializes the model if needed, runs inference,
    /// saves the result, and notifies other ViewModels via messenger.
    /// </summary>
    private async Task TranscribeRecordingAsync(Recording recording)
    {
        if (IsTranscribing) return;

        IsTranscribing = true;
        TranscriptionStatusText = $"Transcribing {recording.FileName}...";
        ExportCommand.NotifyCanExecuteChanged();

        try
        {
            TranscriptionStatusText = "Loading Whisper model...";
            var singleSettings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            var singleModelFileName = string.IsNullOrEmpty(singleSettings.SelectedWhisperModel) ? null : singleSettings.SelectedWhisperModel;
            await _transcriptionService.InitializeAsync(singleModelFileName).ConfigureAwait(true);

            TranscriptionStatusText = $"Transcribing {recording.FileName}...";
            var result = await _transcriptionService.TranscribeAsync(recording.Path).ConfigureAwait(true);

            var entry = new TranscriptionEntry(
                RecordingFileName: recording.FileName,
                Timestamp: recording.Timestamp,
                Duration: result.Duration.TotalSeconds,
                Text: result.Text,
                Segments: result.Segments);

            await _storageService.SaveTranscriptionAsync(entry, CurrentProject, recording.Timestamp.Date).ConfigureAwait(true);

            WeakReferenceMessenger.Default.Send(new TranscribeCompleteMessage(entry, recording.FileName));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Transcription error for {recording.FileName}: {ex}");
            await _dialogService.ShowErrorAsync("Transcription Error",
                $"Failed to transcribe {recording.FileName}: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            IsTranscribing = false;
            TranscriptionStatusText = string.Empty;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Transcribes a batch of recordings sequentially, sending completion messages for each.
    /// </summary>
    private async Task TranscribeBatchAsync(IReadOnlyList<Recording> recordings)
    {
        if (IsTranscribing) return;

        IsTranscribing = true;
        ExportCommand.NotifyCanExecuteChanged();
        var completedEntries = new List<TranscriptionEntry>();

        try
        {
            TranscriptionStatusText = "Loading Whisper model...";
            var batchSettings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            var batchModelFileName = string.IsNullOrEmpty(batchSettings.SelectedWhisperModel) ? null : batchSettings.SelectedWhisperModel;
            await _transcriptionService.InitializeAsync(batchModelFileName).ConfigureAwait(true);

            for (var i = 0; i < recordings.Count; i++)
            {
                var recording = recordings[i];
                TranscriptionStatusText = $"Transcribing {i + 1} of {recordings.Count}: {recording.FileName}...";

                try
                {
                    var result = await _transcriptionService.TranscribeAsync(recording.Path).ConfigureAwait(true);

                    var entry = new TranscriptionEntry(
                        RecordingFileName: recording.FileName,
                        Timestamp: recording.Timestamp,
                        Duration: result.Duration.TotalSeconds,
                        Text: result.Text,
                        Segments: result.Segments);

                    await _storageService.SaveTranscriptionAsync(entry, CurrentProject, recording.Timestamp.Date).ConfigureAwait(true);

                    completedEntries.Add(entry);
                    WeakReferenceMessenger.Default.Send(new TranscribeCompleteMessage(entry, recording.FileName));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Transcription error for {recording.FileName}: {ex}");
                }
            }

            if (completedEntries.Count > 0)
            {
                WeakReferenceMessenger.Default.Send(new TranscribeBatchCompleteMessage(completedEntries));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Batch transcription error: {ex}");
            await _dialogService.ShowErrorAsync("Transcription Error",
                $"Batch transcription failed: {ex.Message}").ConfigureAwait(true);
        }
        finally
        {
            IsTranscribing = false;
            TranscriptionStatusText = string.Empty;
            ExportCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Called when <see cref="CurrentProject"/> changes.
    /// </summary>
    partial void OnCurrentProjectChanged(string value)
    {
        OnPropertyChanged(nameof(HasProject));
        ExportCommand.NotifyCanExecuteChanged();
    }
}
