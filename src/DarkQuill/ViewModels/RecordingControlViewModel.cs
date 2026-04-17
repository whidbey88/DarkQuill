using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the recording control panel. Manages the recording state machine
/// (Idle -> Recording -> Saving -> Idle), audio level monitoring, and elapsed time tracking.
/// </summary>
public partial class RecordingControlViewModel : ObservableObject
{
    private readonly IAudioRecorder _audioRecorder;
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;
    private readonly IStorageService _storageService;
    private readonly IDialogService _dialogService;
    private readonly IAudioPlaybackService _audioPlaybackService;

    private DispatcherTimer? _elapsedTimeTimer;
    private CancellationTokenSource? _recordingCts;
    private string _currentOutputPath = string.Empty;
    private string _currentProject = string.Empty;
    private bool _recordingFinalized;

    /// <summary>
    /// True while recording is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>
    /// Current recording duration, updated every 100ms during recording.
    /// </summary>
    [ObservableProperty]
    private TimeSpan _elapsedTime;

    /// <summary>
    /// Current audio level (0.0 to 1.0), updated from IProgress during recording.
    /// </summary>
    [ObservableProperty]
    private double _audioLevel;

    /// <summary>
    /// Current state of the recording control panel.
    /// </summary>
    [ObservableProperty]
    private RecordingState _recordingState = RecordingState.Idle;

    /// <summary>
    /// Label displayed on the Record/Stop button.
    /// </summary>
    public string ButtonLabel => IsRecording ? "Stop" : "Record";

    /// <summary>
    /// Formatted elapsed time string in MM:SS format.
    /// </summary>
    public string ElapsedTimeFormatted => ElapsedTime.ToString(@"mm\:ss");

    /// <summary>
    /// Text displayed in the recording state indicator chip.
    /// </summary>
    public string StateText => IsRecording ? "Recording..." : "Ready to record";

    /// <summary>
    /// Initializes the recording control ViewModel with required services.
    /// </summary>
    public RecordingControlViewModel(
        IAudioRecorder audioRecorder,
        IProjectService projectService,
        ISettingsService settingsService,
        IStorageService storageService,
        IDialogService dialogService,
        IAudioPlaybackService audioPlaybackService)
    {
        _audioRecorder = audioRecorder;
        _projectService = projectService;
        _settingsService = settingsService;
        _storageService = storageService;
        _dialogService = dialogService;
        _audioPlaybackService = audioPlaybackService;

        WeakReferenceMessenger.Default.Register<ProjectSelectedMessage>(this, (_, msg) =>
        {
            _currentProject = msg.ProjectName;
            StartRecordingCommand.NotifyCanExecuteChanged();
        });

        WeakReferenceMessenger.Default.Register<ProjectCreatedMessage>(this, (_, msg) =>
        {
            _currentProject = msg.ProjectName;
            StartRecordingCommand.NotifyCanExecuteChanged();
        });

        WeakReferenceMessenger.Default.Register<HotkeyPressedMessage>(this, (recipient, msg) =>
        {
            var vm = (RecordingControlViewModel)recipient;
            switch (msg.Hotkey.Id)
            {
                case HotkeyIds.StartRecording:
                    if (vm.StartRecordingCommand.CanExecute(null))
                        vm.StartRecordingCommand.Execute(null);
                    break;
                case HotkeyIds.StopRecording:
                    if (vm.IsRecording && vm.StopRecordingCommand.CanExecute(null))
                        vm.StopRecordingCommand.Execute(null);
                    break;
            }
        });
    }

    /// <summary>
    /// Called when <see cref="IsRecording"/> changes. Updates computed properties.
    /// </summary>
    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(ButtonLabel));
        OnPropertyChanged(nameof(StateText));
        StartRecordingCommand.NotifyCanExecuteChanged();
        StopRecordingCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Called when <see cref="ElapsedTime"/> changes. Updates the formatted time string.
    /// </summary>
    partial void OnElapsedTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(ElapsedTimeFormatted));
    }

    /// <summary>
    /// Starts recording audio from the microphone.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private async Task StartRecordingAsync()
    {
        try
        {
            // Stop any active playback to prevent playback audio from being captured by the microphone.
            await _audioPlaybackService.StopAsync().ConfigureAwait(true);

            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            var projectFolder = _projectService.GetProjectFolderName(_currentProject, DateTime.Today);

            await _storageService.EnsureRecordingFolderExistsAsync(_currentProject, DateTime.Today).ConfigureAwait(true);

            var fileName = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav";
            _currentOutputPath = Path.Combine(settings.RecordingsFolder, projectFolder, fileName);

            var audioSettings = new AudioSettings
            {
                DeviceId = settings.AudioDeviceId,
                InputLevel = settings.InputLevel
            };

            _recordingCts = new CancellationTokenSource();
            var levelProgress = new Progress<int>(level =>
            {
                Dispatcher.UIThread.Post(() => AudioLevel = level / 100.0);
            });

            _recordingFinalized = false;
            RecordingState = RecordingState.Recording;
            IsRecording = true;
            ElapsedTime = TimeSpan.Zero;

            StartElapsedTimeTimer();

            await _audioRecorder.StartRecordingAsync(
                _currentOutputPath,
                audioSettings,
                levelProgress,
                _recordingCts.Token).ConfigureAwait(true);

            // StartRecordingAsync returns when recording stops (user stop or auto-stop).
            // If StopRecordingCommand already handled cleanup, _recordingFinalized will be true.
            // Only finalize here if the user did NOT already stop manually.
            if (!_recordingFinalized)
            {
                // This path is reached when the AudioRecorder's internal 5-minute timeout fires.
                await FinalizeRecordingAsync(wasAutoStopped: true).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_recordingFinalized)
            {
                await FinalizeRecordingAsync(wasAutoStopped: true).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            StopElapsedTimeTimer();
            RecordingState = RecordingState.Idle;
            IsRecording = false;
            AudioLevel = 0;
            ElapsedTime = TimeSpan.Zero;
            await _dialogService.ShowErrorAsync("Recording Error", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Stops the current recording and saves the WAV file.
    /// Sets <see cref="_recordingFinalized"/> synchronously (before any await) to prevent
    /// <see cref="StartRecordingAsync"/>'s continuation from racing and showing the
    /// auto-stop dialog. Cleanup is performed directly after the recorder stops.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        // CRITICAL: Set this synchronously before any await. When StopRecordingAsync
        // causes StartRecordingAsync's await to complete, both continuations are posted
        // to the UI dispatcher. StartRecordingAsync's continuation can run first — so
        // this flag must already be true by then.
        _recordingFinalized = true;

        try
        {
            await _audioRecorder.StopRecordingAsync().ConfigureAwait(true);

            // Perform finalization directly (FinalizeRecordingAsync would return early
            // because _recordingFinalized is already true).
            StopElapsedTimeTimer();

            RecordingState = RecordingState.Saving;
            var duration = ElapsedTime;

            RecordingState = RecordingState.Idle;
            IsRecording = false;
            AudioLevel = 0;
            ElapsedTime = TimeSpan.Zero;

            _recordingCts?.Dispose();
            _recordingCts = null;

            WeakReferenceMessenger.Default.Send(new RecordingCompletedMessage(_currentOutputPath, duration));
        }
        catch (Exception ex)
        {
            StopElapsedTimeTimer();
            RecordingState = RecordingState.Idle;
            IsRecording = false;
            AudioLevel = 0;
            ElapsedTime = TimeSpan.Zero;
            await _dialogService.ShowErrorAsync("Stop Recording Error", ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens the audio settings dialog.
    /// </summary>
    [RelayCommand]
    private async Task OpenAudioSettingsAsync()
    {
        await _dialogService.ShowAudioSettingsAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Toggles between starting and stopping a recording.
    /// </summary>
    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync().ConfigureAwait(true);
        }
        else
        {
            await StartRecordingAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Whether the start recording command can execute.
    /// </summary>
    private bool CanStartRecording() => !IsRecording && !string.IsNullOrEmpty(_currentProject);

    /// <summary>
    /// Whether the stop recording command can execute.
    /// </summary>
    private bool CanStopRecording() => IsRecording;

    /// <summary>
    /// Finalizes a recording: stops timer, transitions state, and posts completion message.
    /// Idempotent — returns immediately if already finalized (prevents duplicate clips
    /// when both StopRecordingAsync and StartRecordingAsync's continuation fire).
    /// </summary>
    private async Task FinalizeRecordingAsync(bool wasAutoStopped)
    {
        if (_recordingFinalized)
        {
            return;
        }

        _recordingFinalized = true;

        StopElapsedTimeTimer();

        RecordingState = RecordingState.Saving;
        var duration = ElapsedTime;

        RecordingState = RecordingState.Idle;
        IsRecording = false;
        AudioLevel = 0;
        ElapsedTime = TimeSpan.Zero;

        _recordingCts?.Dispose();
        _recordingCts = null;

        WeakReferenceMessenger.Default.Send(new RecordingCompletedMessage(_currentOutputPath, duration));

        if (wasAutoStopped)
        {
            await _dialogService.ShowErrorAsync(
                "Recording Limit Reached",
                "Recording was stopped automatically because it reached the 5-minute limit.").ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Starts the DispatcherTimer that updates <see cref="ElapsedTime"/> every 100ms.
    /// </summary>
    private void StartElapsedTimeTimer()
    {
        _elapsedTimeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _elapsedTimeTimer.Tick += (_, _) =>
        {
            ElapsedTime = ElapsedTime.Add(TimeSpan.FromMilliseconds(100));
        };
        _elapsedTimeTimer.Start();
    }

    /// <summary>
    /// Stops and disposes the elapsed time timer.
    /// </summary>
    private void StopElapsedTimeTimer()
    {
        _elapsedTimeTimer?.Stop();
        _elapsedTimeTimer = null;
    }
}
