using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using DarkQuill.Models;
using DarkQuill.Services;
using DarkQuill.ViewModels;
using DarkQuill.Views;

namespace DarkQuill;

/// <summary>
/// Application root. Configures the dependency injection container and wires up the main window.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Loads the AXAML markup for the application.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Configures the DI container and resolves the main window on framework initialization.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<RecordingListViewModel>();
        services.AddSingleton<RecordingControlViewModel>();
        services.AddSingleton<TranscriptionListViewModel>();
        services.AddSingleton<AudioSettingsViewModel>();
        services.AddSingleton<ModelSelectionViewModel>();
        services.AddSingleton<SettingsViewModel>();

        ServiceProvider provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var hotkeyService = provider.GetRequiredService<IHotkeyService>();
            var mainWindow = new MainWindow(hotkeyService)
            {
                DataContext = provider.GetRequiredService<MainViewModel>()
            };
            desktop.MainWindow = mainWindow;

            mainWindow.Opened += async (_, _) =>
            {
                // Set the native window handle for hotkey registration.
                var handle = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                hotkeyService.SetWindowHandle(handle);

                // Register default hotkeys (failures are logged but do not prevent startup).
                await hotkeyService.RegisterHotkeyAsync(
                    new HotkeyDefinition(HotkeyIds.StartRecording, "Start Recording", Key.F9, KeyModifiers.None));

                // Space bar is NOT registered as a global hotkey — it would capture
                // Space system-wide, breaking typing in other apps. Instead, Space is
                // handled as a local KeyDown event in MainWindow.

                await hotkeyService.RegisterHotkeyAsync(
                    new HotkeyDefinition(HotkeyIds.TranscribeLatest, "Transcribe Latest", Key.T, KeyModifiers.Control | KeyModifiers.Shift));

                // Show the project selection dialog.
                var dialogService = provider.GetRequiredService<IDialogService>();
                await dialogService.ShowProjectDialogAsync();

                // Check whether any Whisper models are available. If not, prompt the user to download them.
                var transcriptionService = provider.GetRequiredService<ITranscriptionService>();
                var availableModels = await transcriptionService.GetAvailableModelsAsync();
                if (availableModels.Count == 0)
                {
                    var downloaded = await dialogService.ShowModelDownloadAsync();
                    if (!downloaded)
                    {
                        // User cancelled — models are required, so shut down the application.
                        desktop.Shutdown();
                        return;
                    }
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
