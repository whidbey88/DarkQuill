using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Messaging;
using DarkQuill.Models;
using DarkQuill.Services;

namespace DarkQuill.Views;

/// <summary>
/// Main application window. Subscribes to <see cref="IHotkeyService.HotkeyPressed"/>
/// and forwards hotkey events to ViewModels via <see cref="WeakReferenceMessenger"/>.
/// Space bar is handled locally (not as a global hotkey) to avoid capturing it system-wide.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IHotkeyService? _hotkeyService;

    /// <summary>
    /// Design-time parameterless constructor required by the Avalonia XAML loader.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the main window with the hotkey service for event forwarding.
    /// </summary>
    /// <param name="hotkeyService">The hotkey service to subscribe to.</param>
    public MainWindow(IHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        InitializeComponent();

        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Forwards hotkey events to ViewModels via <see cref="WeakReferenceMessenger"/>.
    /// </summary>
    private void OnHotkeyPressed(object? sender, HotkeyEventArgs e)
    {
        WeakReferenceMessenger.Default.Send(new HotkeyPressedMessage(e.Hotkey));
    }

    /// <summary>
    /// Handles local key events. Space bar stops recording when the app is focused,
    /// without capturing it system-wide via RegisterHotKey.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            // Only handle Space when not focused on a text input control.
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            {
                return;
            }

            var hotkeyDef = new HotkeyDefinition(HotkeyIds.StopRecording, "Stop Recording", Key.Space, KeyModifiers.None);
            WeakReferenceMessenger.Default.Send(new HotkeyPressedMessage(hotkeyDef));
            e.Handled = true;
        }
    }
}
