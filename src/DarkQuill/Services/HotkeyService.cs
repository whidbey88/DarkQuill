using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input;
using DarkQuill.Models;

namespace DarkQuill.Services;

/// <summary>
/// Win32 P/Invoke declarations for global hotkey registration and window subclassing.
/// File-scoped to prevent external access.
/// </summary>
file static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public const int GWLP_WNDPROC = -4;
    public const uint WM_HOTKEY = 0x0312;

    // Win32 modifier key flags for RegisterHotKey.
    public const uint MOD_NONE = 0x0000;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
}

/// <summary>
/// Registers and manages global hotkeys on Windows via P/Invoke (RegisterHotKey Win32 API).
/// On non-Windows platforms, all operations are no-ops with logged warnings.
/// </summary>
public class HotkeyService : IHotkeyService, IDisposable
{
    private readonly Dictionary<int, HotkeyDefinition> _registeredHotkeys = [];
    private IntPtr _windowHandle = IntPtr.Zero;
    private IntPtr _originalWndProc = IntPtr.Zero;
    private NativeWndProcDelegate? _wndProcDelegate;
    private bool _disposed;

    private delegate IntPtr NativeWndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <inheritdoc />
    public event EventHandler<HotkeyEventArgs>? HotkeyPressed;

    /// <inheritdoc />
    public IReadOnlyList<HotkeyDefinition> RegisteredHotkeys => _registeredHotkeys.Values.ToList();

    /// <inheritdoc />
    public void SetWindowHandle(IntPtr handle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Debug.WriteLine("HotkeyService: Window handle setup skipped (non-Windows platform).");
            return;
        }

        if (handle == IntPtr.Zero)
        {
            Debug.WriteLine("HotkeyService: Received zero window handle; cannot install WndProc hook.");
            return;
        }

        _windowHandle = handle;

        // Subclass the window to intercept WM_HOTKEY messages.
        // Store the delegate as a field to prevent garbage collection.
        _wndProcDelegate = WndProc;
        var newWndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        _originalWndProc = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWLP_WNDPROC, newWndProcPtr);

        Debug.WriteLine("HotkeyService: WndProc hook installed for hotkey message interception.");
    }

    /// <inheritdoc />
    public Task<bool> RegisterHotkeyAsync(HotkeyDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Debug.WriteLine($"HotkeyService: Skipping hotkey registration for '{definition.Name}' (non-Windows platform).");
            return Task.FromResult(false);
        }

        if (_windowHandle == IntPtr.Zero)
        {
            Debug.WriteLine($"HotkeyService: Cannot register hotkey '{definition.Name}' — window handle not set.");
            return Task.FromResult(false);
        }

        var fsModifiers = ConvertModifiers(definition.Modifiers);
        var vk = ConvertKey(definition.Key);

        if (vk == 0)
        {
            Debug.WriteLine($"HotkeyService: Unsupported key '{definition.Key}' for hotkey '{definition.Name}'.");
            return Task.FromResult(false);
        }

        var success = NativeMethods.RegisterHotKey(_windowHandle, definition.Id, fsModifiers, vk);

        if (success)
        {
            _registeredHotkeys[definition.Id] = definition;
            Debug.WriteLine($"HotkeyService: Registered hotkey '{definition.Name}' (ID={definition.Id}, Key={definition.Key}, Modifiers={definition.Modifiers}).");
        }
        else
        {
            Debug.WriteLine($"HotkeyService: Failed to register hotkey '{definition.Name}'. The key may be in use by another application.");
        }

        return Task.FromResult(success);
    }

    /// <inheritdoc />
    public Task<bool> UnregisterHotkeyAsync(int hotkeyId, CancellationToken cancellationToken = default)
    {
        if (!_registeredHotkeys.ContainsKey(hotkeyId))
        {
            return Task.FromResult(false);
        }

        if (_windowHandle == IntPtr.Zero)
        {
            return Task.FromResult(false);
        }

        var success = NativeMethods.UnregisterHotKey(_windowHandle, hotkeyId);

        if (success)
        {
            _registeredHotkeys.Remove(hotkeyId);
            Debug.WriteLine($"HotkeyService: Unregistered hotkey ID={hotkeyId}.");
        }

        return Task.FromResult(success);
    }

    /// <summary>
    /// Custom WndProc that intercepts WM_HOTKEY messages and fires the <see cref="HotkeyPressed"/> event.
    /// All other messages are forwarded to the original WndProc.
    /// </summary>
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(hotkeyId, out var definition))
            {
                HotkeyPressed?.Invoke(this, new HotkeyEventArgs(definition));
            }
        }

        return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Converts Avalonia <see cref="KeyModifiers"/> to Win32 modifier flags for RegisterHotKey.
    /// </summary>
    private static uint ConvertModifiers(KeyModifiers modifiers)
    {
        uint result = NativeMethods.MOD_NONE;

        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            result |= NativeMethods.MOD_WIN;

        return result;
    }

    /// <summary>
    /// Converts an Avalonia <see cref="Key"/> to a Win32 virtual key code.
    /// Returns 0 for unsupported keys.
    /// </summary>
    private static uint ConvertKey(Key key)
    {
        return key switch
        {
            Key.F9 => 0x78,     // VK_F9
            Key.Space => 0x20,  // VK_SPACE
            Key.T => 0x54,      // VK_T
            _ => 0
        };
    }

    /// <summary>
    /// Unregisters all hotkeys and restores the original WndProc.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _windowHandle == IntPtr.Zero)
            return;

        foreach (var id in _registeredHotkeys.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
        }
        _registeredHotkeys.Clear();

        if (_originalWndProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWLP_WNDPROC, _originalWndProc);
            _originalWndProc = IntPtr.Zero;
        }

        _wndProcDelegate = null;

        GC.SuppressFinalize(this);
    }
}
