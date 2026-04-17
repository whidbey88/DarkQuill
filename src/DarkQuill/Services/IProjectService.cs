using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Manages project lifecycle, naming conventions, and folder scanning.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Scans the recordings and transcriptions folders for projects matching the given date.
    /// </summary>
    /// <param name="date">The date to scan for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of projects found for the given date.</returns>
    Task<IReadOnlyList<ProjectInfo>> ScanProjectsForDateAsync(DateTime date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new project with the given name, setting up required folder structure.
    /// </summary>
    /// <param name="projectName">Human-readable project name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CreateProjectAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an existing project by its normalized name.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project metadata.</returns>
    Task<ProjectInfo> LoadProjectAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Normalizes a project name by lowercasing and replacing spaces with hyphens.
    /// </summary>
    /// <param name="name">Human-readable project name (e.g., "Airport Dialogue Study").</param>
    /// <returns>Normalized name (e.g., "airport-dialogue-study").</returns>
    string NormalizeProjectName(string name);

    /// <summary>
    /// Gets the folder name for a project on a specific date.
    /// </summary>
    /// <param name="projectName">Normalized project name.</param>
    /// <param name="date">The date for the project folder.</param>
    /// <returns>Folder name (e.g., "airport-dialogue-study-04-15-2026").</returns>
    string GetProjectFolderName(string projectName, DateTime date);
}
