using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Loads and saves application settings to a JSON configuration file.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads application settings from disk. Returns defaults if the file does not exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The loaded application settings.</returns>
    Task<ApplicationSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves application settings to disk.
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
