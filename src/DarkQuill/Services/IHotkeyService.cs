using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Manages global hotkey registration and event handling.
/// Windows-only implementation via P/Invoke (RegisterHotKey Win32 API).
/// </summary>
public interface IHotkeyService
{
    /// <summary>
    /// Fired when a registered hotkey is pressed.
    /// </summary>
    event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    /// <summary>
    /// Registers a global hotkey with the operating system.
    /// </summary>
    /// <param name="definition">The hotkey definition to register.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if registered successfully; false if failed (e.g., key already in use).</returns>
    Task<bool> RegisterHotkeyAsync(HotkeyDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a previously registered global hotkey.
    /// </summary>
    /// <param name="hotkeyId">The identifier of the hotkey to unregister.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if unregistered successfully; false if hotkey not found.</returns>
    Task<bool> UnregisterHotkeyAsync(int hotkeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently registered hotkeys.
    /// </summary>
    IReadOnlyList<HotkeyDefinition> RegisteredHotkeys { get; }

    /// <summary>
    /// Sets the native window handle used for hotkey registration and message processing.
    /// Must be called after the main window is realized (e.g., on Window.Opened).
    /// </summary>
    /// <param name="handle">The native window handle (HWND on Windows).</param>
    void SetWindowHandle(IntPtr handle);
}
