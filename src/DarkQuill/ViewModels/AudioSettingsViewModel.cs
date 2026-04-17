using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the audio settings dialog. Manages device selection, input level,
/// and device testing.
/// </summary>
public partial class AudioSettingsViewModel : ObservableObject
{
    private readonly IAudioRecorder _audioRecorder;
    private readonly ISettingsService _settingsService;

    private CancellationTokenSource? _testCts;

    /// <summary>
    /// List of available microphone devices.
    /// </summary>
    public ObservableCollection<AudioDevice> AvailableDevices { get; } = [];

    /// <summary>
    /// Currently selected audio input device.
    /// </summary>
    [ObservableProperty]
    private AudioDevice? _selectedDevice;

    /// <summary>
    /// Input level sensitivity (0–100).
    /// </summary>
    [ObservableProperty]
    private double _inputLevel;

    /// <summary>
    /// Real-time audio level during device test (0.0–1.0).
    /// </summary>
    [ObservableProperty]
    private double _testAudioLevel;

    /// <summary>
    /// True while a device test is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isTesting;

    /// <summary>
    /// True while loading available devices.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Status message displayed to the user.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "Loading devices...";

    /// <summary>
    /// Callback to close the dialog. Set by the dialog code-behind.
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    /// <summary>
    /// Initializes the audio settings ViewModel with required services.
    /// </summary>
    public AudioSettingsViewModel(IAudioRecorder audioRecorder, ISettingsService settingsService)
    {
        _audioRecorder = audioRecorder;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Called when <see cref="SelectedDevice"/> changes. Updates command states.
    /// </summary>
    partial void OnSelectedDeviceChanged(AudioDevice? value)
    {
        TestAudioCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Loads available audio devices and restores saved settings.
    /// </summary>
    [RelayCommand]
    private async Task LoadDevicesAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading devices...";

            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            InputLevel = settings.InputLevel * 100.0;

            var devices = await _audioRecorder.GetAvailableDevicesAsync().ConfigureAwait(true);

            AvailableDevices.Clear();
            foreach (var device in devices)
            {
                AvailableDevices.Add(device);
            }

            if (AvailableDevices.Count == 0)
            {
                StatusMessage = "No microphone devices found.";
                return;
            }

            // Try to select the saved device
            var savedDevice = AvailableDevices.FirstOrDefault(d => d.Id == settings.AudioDeviceId);
            if (savedDevice is not null)
            {
                SelectedDevice = savedDevice;
                StatusMessage = "Ready.";
            }
            else
            {
                // Saved device no longer available; select first device
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IsDefault)
                                 ?? AvailableDevices[0];
                StatusMessage = string.IsNullOrEmpty(settings.AudioDeviceId)
                    ? "Ready."
                    : "Previously selected device is no longer available. Default device selected.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading devices: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Tests the selected audio device by briefly capturing input and showing real-time levels.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestAudio))]
    private async Task TestAudioAsync()
    {
        if (SelectedDevice is null) return;

        try
        {
            IsTesting = true;
            TestAudioLevel = 0;
            StatusMessage = "Testing microphone...";

            var audioSettings = new AudioSettings
            {
                DeviceId = SelectedDevice.Id,
                InputLevel = InputLevel / 100.0
            };

            _testCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var levelProgress = new Progress<int>(level =>
            {
                Dispatcher.UIThread.Post(() => TestAudioLevel = level / 100.0);
            });

            await _audioRecorder.TestAudioDeviceAsync(
                audioSettings,
                levelProgress,
                _testCts.Token).ConfigureAwait(true);

            StatusMessage = "Test complete. Check levels above.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Test complete.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    /// <summary>
    /// Saves the current settings and closes the dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedDevice is null) return;

        try
        {
            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            settings.AudioDeviceId = SelectedDevice.Id;
            settings.InputLevel = InputLevel / 100.0;

            await _settingsService.SaveSettingsAsync(settings).ConfigureAwait(true);

            WeakReferenceMessenger.Default.Send(new SettingsChangedMessage(settings));

            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
    }

    /// <summary>
    /// Cancels the dialog without saving changes.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _testCts?.Cancel();
        RequestClose?.Invoke(false);
    }

    /// <summary>
    /// Whether the test audio command can execute.
    /// </summary>
    private bool CanTestAudio() => SelectedDevice is not null && !IsTesting;

    /// <summary>
    /// Whether the apply command can execute.
    /// </summary>
    private bool CanApply() => SelectedDevice is not null && !IsLoading;
}
