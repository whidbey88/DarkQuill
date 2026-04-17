using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace DarkQuill.Services;

/// <summary>
/// Provides clipboard operations using Avalonia's platform clipboard API.
/// Accesses the clipboard via the main window's top-level control.
/// </summary>
public class ClipboardService : IClipboardService
{
    /// <summary>
    /// Copies text to the system clipboard via Avalonia's clipboard API.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task CopyToClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var clipboard = GetClipboard();
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// Resolves the platform clipboard from the application's main window.
    /// </summary>
    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }
}
