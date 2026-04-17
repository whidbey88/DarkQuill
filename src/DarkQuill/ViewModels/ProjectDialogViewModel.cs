using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.ViewModels;

/// <summary>
/// ViewModel for the project selection dialog. Supports selecting existing projects,
/// creating new projects, and loading previous projects from any date.
/// </summary>
public partial class ProjectDialogViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private readonly ISettingsService _settingsService;

    /// <summary>
    /// Callback to close the dialog with a result. Set by the dialog code-behind.
    /// </summary>
    public Action<ProjectInfo?>? RequestClose { get; set; }

    /// <summary>
    /// Projects found for today's date.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ProjectInfo> _existingProjects = [];

    /// <summary>
    /// Currently selected project from the existing projects list.
    /// </summary>
    [ObservableProperty]
    private ProjectInfo? _selectedExistingProject;

    /// <summary>
    /// Text input for new project name.
    /// </summary>
    [ObservableProperty]
    private string _newProjectName = string.Empty;

    /// <summary>
    /// True while scanning or loading projects.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Current active tab: "Select", "New", or "Load".
    /// </summary>
    [ObservableProperty]
    private string _activeTab = "Select";

    /// <summary>
    /// All unique project names from the transcriptions folder (for "Load Previous" tab).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ProjectInfo> _allProjects = [];

    /// <summary>
    /// Selected project from the all-projects list.
    /// </summary>
    [ObservableProperty]
    private ProjectInfo? _selectedAllProject;

    /// <summary>
    /// Error message to display, or empty if no error.
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// Indicates whether the "Create" button should be enabled.
    /// </summary>
    public bool CanCreateProject => !string.IsNullOrWhiteSpace(NewProjectName);

    /// <summary>
    /// Initializes the project dialog ViewModel with required services.
    /// </summary>
    /// <param name="projectService">Project management service.</param>
    /// <param name="settingsService">Settings service for folder paths.</param>
    public ProjectDialogViewModel(IProjectService projectService, ISettingsService settingsService)
    {
        _projectService = projectService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Called when <see cref="NewProjectName"/> changes. Notifies that <see cref="CanCreateProject"/> may have changed.
    /// </summary>
    partial void OnNewProjectNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanCreateProject));
        CreateNewProjectCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Scans for today's projects and all previous projects. Should be called after the dialog is shown.
    /// </summary>
    [RelayCommand]
    private async Task InitializeAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var todayProjects = await _projectService.ScanProjectsForDateAsync(DateTime.Today).ConfigureAwait(true);
            ExistingProjects = new ObservableCollection<ProjectInfo>(todayProjects);

            if (ExistingProjects.Count == 0)
            {
                ActiveTab = "New";
            }

            await ScanAllProjectsAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "An error occurred while loading projects. Please try again.";
            Debug.WriteLine($"Error scanning projects: {ex}");
            ActiveTab = "New";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Selects an existing project from today's list and closes the dialog.
    /// </summary>
    /// <param name="project">The project to select.</param>
    [RelayCommand]
    private async Task SelectExistingProjectAsync(ProjectInfo project)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var loaded = await _projectService.LoadProjectAsync(project.Name).ConfigureAwait(true);
            WeakReferenceMessenger.Default.Send(new ProjectSelectedMessage(loaded.Name));
            RequestClose?.Invoke(loaded);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = "Project folder not found. Please check your file paths.";
            Debug.WriteLine($"Error loading project: {ex}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "An error occurred while loading the project. Please try again.";
            Debug.WriteLine($"Error loading project: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Creates a new project with the entered name and closes the dialog.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateProject))]
    private async Task CreateNewProjectAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var normalizedName = _projectService.NormalizeProjectName(NewProjectName);
            await _projectService.CreateProjectAsync(NewProjectName).ConfigureAwait(true);

            var result = new ProjectInfo(normalizedName, DateTime.Now, DateTime.Now);
            WeakReferenceMessenger.Default.Send(new ProjectCreatedMessage(normalizedName));
            WeakReferenceMessenger.Default.Send(new ProjectSelectedMessage(normalizedName));
            RequestClose?.Invoke(result);
        }
        catch (ArgumentException)
        {
            ErrorMessage = "Project name contains no valid characters. Please enter a different name.";
        }
        catch (IOException ex)
        {
            ErrorMessage = "An error occurred while creating the project. Please try again.";
            Debug.WriteLine($"Error creating project: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads a previously created project from any date and closes the dialog.
    /// </summary>
    /// <param name="project">The project to load.</param>
    [RelayCommand]
    private async Task LoadPreviousProjectAsync(ProjectInfo project)
    {
        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            // Create the project folder for today if it doesn't exist
            await _projectService.CreateProjectAsync(project.Name).ConfigureAwait(true);
            var loaded = await _projectService.LoadProjectAsync(project.Name).ConfigureAwait(true);

            WeakReferenceMessenger.Default.Send(new ProjectSelectedMessage(loaded.Name));
            RequestClose?.Invoke(loaded);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = "An error occurred while loading the project. Please try again.";
            Debug.WriteLine($"Error loading previous project: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Closes the dialog without selecting a project.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(null);
    }

    /// <summary>
    /// Switches the active tab.
    /// </summary>
    /// <param name="tabName">The tab name to switch to ("Select", "New", or "Load").</param>
    [RelayCommand]
    private void SwitchTab(string tabName)
    {
        ActiveTab = tabName;
        ErrorMessage = string.Empty;
    }

    /// <summary>
    /// Scans all project folders in the recordings and transcriptions directories
    /// to populate the "Load Previous Project" list.
    /// </summary>
    private async Task ScanAllProjectsAsync()
    {
        try
        {
            var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(true);
            var projectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projects = new List<ProjectInfo>();

            ScanFolderForProjectNames(settings.RecordingsFolder, projectNames, projects);
            ScanFilesForProjectNames(settings.TranscriptionsFolder, projectNames, projects);

            AllProjects = new ObservableCollection<ProjectInfo>(projects.OrderBy(p => p.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Error scanning all projects: {ex}");
        }
    }

    /// <summary>
    /// Scans subdirectories in the given folder for project names by stripping date suffixes.
    /// </summary>
    private static void ScanFolderForProjectNames(string folder, HashSet<string> seenNames, List<ProjectInfo> projects)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var dir in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(dir);
            var projectName = StripDateSuffix(name);
            if (projectName is not null && seenNames.Add(projectName))
            {
                var info = new DirectoryInfo(dir);
                projects.Add(new ProjectInfo(projectName, info.CreationTime, info.LastWriteTime));
            }
        }
    }

    /// <summary>
    /// Scans JSON files in the given folder for project names by stripping date suffixes.
    /// </summary>
    private static void ScanFilesForProjectNames(string folder, HashSet<string> seenNames, List<ProjectInfo> projects)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var projectName = StripDateSuffix(name);
            if (projectName is not null && seenNames.Add(projectName))
            {
                var info = new FileInfo(file);
                projects.Add(new ProjectInfo(projectName, info.CreationTime, info.LastWriteTime));
            }
        }
    }

    /// <summary>
    /// Strips the trailing date suffix (-MM-dd-yyyy) from a folder or file name.
    /// </summary>
    /// <returns>The project name without the date suffix, or null if no valid suffix found.</returns>
    private static string? StripDateSuffix(string name)
    {
        // Pattern: name-MM-dd-yyyy (minimum: "x-01-01-2000" = 13 chars)
        if (name.Length < 12) return null;

        var lastHyphen = name.LastIndexOf('-');
        if (lastHyphen < 6) return null;

        // Expect -dd-yyyy at end → find the -MM- before it
        var dateStart = name.Length - 10; // "MM-dd-yyyy" = 10 chars
        if (dateStart <= 0 || name[dateStart - 1] != '-') return null;

        var datePart = name[dateStart..];
        if (DateTime.TryParseExact(datePart, "MM-dd-yyyy", null, System.Globalization.DateTimeStyles.None, out _))
        {
            var projectName = name[..(dateStart - 1)];
            return string.IsNullOrEmpty(projectName) ? null : projectName;
        }

        return null;
    }
}
