using CommunityToolkit.Mvvm.ComponentModel;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the application settings dialog. Manages folder paths, GPU toggle,
/// and hotkey configuration.
/// </summary>
public partial class SettingsViewModel(
    ISettingsService settingsService) : ObservableObject
{
    private readonly ISettingsService _settingsService = settingsService;
}
