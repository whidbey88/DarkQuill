using System.Diagnostics;
using System.Text.Json;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Loads and saves application settings to a JSON file in the user's application data directory.
/// Settings are stored at <c>{AppData}/DarkQuill/settings.json</c>.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly Dictionary<string, string> DefaultHotkeys = new()
    {
        ["startRecording"] = "F9",
        ["stopRecording"] = "Space",
        ["transcribeLatest"] = "Ctrl+Shift+T",
    };

    private readonly string _settingsFilePath;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class.
    /// The settings file path is computed from the user's application data directory.
    /// </summary>
    public SettingsService()
    {
        var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsFilePath = Path.Combine(appDataDir, "DarkQuill", "settings.json");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsService"/> class with a custom settings file path.
    /// Used for testing to redirect file I/O to a temporary directory.
    /// </summary>
    /// <param name="settingsFilePath">Full path to the settings JSON file.</param>
    internal SettingsService(string settingsFilePath)
    {
        _settingsFilePath = settingsFilePath;
    }

    /// <summary>
    /// Loads application settings from the JSON file on disk.
    /// Returns default settings if the file does not exist or contains malformed JSON.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The loaded <see cref="ApplicationSettings"/>, or defaults if unavailable.</returns>
    public async Task<ApplicationSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsFilePath))
        {
            return CreateDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<ApplicationSettings>(json, SerializerOptions);

            if (settings is null)
            {
                Debug.WriteLine($"Settings file deserialized to null: {_settingsFilePath}");
                return CreateDefaultSettings();
            }

            // Ensure hotkeys dictionary has all default keys (merge missing ones)
            foreach (var kvp in DefaultHotkeys)
            {
                settings.Hotkeys.TryAdd(kvp.Key, kvp.Value);
            }

            return settings;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Malformed settings JSON at {_settingsFilePath}: {ex.Message}");
            return CreateDefaultSettings();
        }
    }

    /// <summary>
    /// Saves application settings to the JSON file on disk using atomic write
    /// (write to temp file, then move to final location).
    /// </summary>
    /// <param name="settings">The settings to persist.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is <c>null</c>.</exception>
    /// <exception cref="IOException">Thrown when the settings directory cannot be created or the file cannot be written.</exception>
    public async Task SaveSettingsAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_settingsFilePath)!;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new IOException($"Failed to create settings directory: {directory}", ex);
        }

        var tempFilePath = _settingsFilePath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempFilePath, _settingsFilePath, overwrite: true);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException($"Permission denied writing settings file: {_settingsFilePath}", ex);
        }
    }

    /// <summary>
    /// Creates an <see cref="ApplicationSettings"/> instance populated with default values
    /// including default hotkey bindings.
    /// </summary>
    /// <returns>A new settings instance with all defaults applied.</returns>
    private static ApplicationSettings CreateDefaultSettings()
    {
        return new ApplicationSettings
        {
            Hotkeys = new Dictionary<string, string>(DefaultHotkeys),
        };
    }
}
