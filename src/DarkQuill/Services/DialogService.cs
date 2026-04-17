using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DarkQuill.Models;
using DarkQuill.ViewModels;
using DarkQuill.Views;

namespace DarkQuill.Services;

/// <summary>
/// Presents modal dialogs using Avalonia's windowing system.
/// Resolves the owner window from the application lifetime.
/// </summary>
public class DialogService(IProjectService projectService, ISettingsService settingsService, IAudioRecorder audioRecorder, ITranscriptionService transcriptionService) : IDialogService
{
    private readonly IProjectService _projectService = projectService;
    private readonly ISettingsService _settingsService = settingsService;
    private readonly IAudioRecorder _audioRecorder = audioRecorder;
    private readonly ITranscriptionService _transcriptionService = transcriptionService;

    /// <inheritdoc />
    public async Task<ProjectInfo?> ShowProjectDialogAsync(CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return null;

        var viewModel = new ProjectDialogViewModel(_projectService, _settingsService);
        var dialog = new ProjectDialog { DataContext = viewModel };

        var result = await dialog.ShowDialog<ProjectInfo?>(owner).ConfigureAwait(true);
        return result;
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };

        var button = ((StackPanel)dialog.Content).Children[1] as Button;
        button!.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return false;

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 12,
                        Children =
                        {
                            new Button { Content = "Cancel" },
                            new Button { Content = "Confirm" }
                        }
                    }
                }
            }
        };

        var buttonPanel = ((StackPanel)dialog.Content).Children[1] as StackPanel;
        var cancelButton = buttonPanel!.Children[0] as Button;
        var confirmButton = buttonPanel.Children[1] as Button;

        cancelButton!.Click += (_, _) => dialog.Close(false);
        confirmButton!.Click += (_, _) => dialog.Close(true);

        var result = await dialog.ShowDialog<object?>(owner).ConfigureAwait(true);
        return result is true;
    }

    /// <inheritdoc />
    public async Task ShowAudioSettingsAsync(CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return;

        var viewModel = new AudioSettingsViewModel(_audioRecorder, _settingsService);
        var dialog = new AudioSettingsDialog { DataContext = viewModel };

        await dialog.ShowDialog(owner).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task ShowModelSelectionAsync(CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return;

        var viewModel = new ModelSelectionViewModel(_transcriptionService, _settingsService);
        var dialog = new ModelSelectionDialog { DataContext = viewModel };

        await dialog.ShowDialog(owner).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter, CancellationToken cancellationToken = default)
    {
        var owner = GetMainWindow();
        if (owner is null) return null;

        var topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel is null) return null;

        var fileTypes = ParseFileFilter(filter);
        var result = await topLevel.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName,
                FileTypeChoices = fileTypes
            }).ConfigureAwait(true);

        return result?.Path.LocalPath;
    }

    /// <summary>
    /// Gets the main window from the current application lifetime.
    /// </summary>
    private static Window? GetMainWindow()
    {
        return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    }

    /// <summary>
    /// Parses a pipe-delimited file filter string into Avalonia file type choices.
    /// Format: "Description|*.ext|Description2|*.ext2"
    /// </summary>
    private static List<Avalonia.Platform.Storage.FilePickerFileType> ParseFileFilter(string filter)
    {
        var types = new List<Avalonia.Platform.Storage.FilePickerFileType>();
        var parts = filter.Split('|');
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            types.Add(new Avalonia.Platform.Storage.FilePickerFileType(parts[i])
            {
                Patterns = parts[i + 1].Split(';').ToList()
            });
        }
        return types;
    }
}
