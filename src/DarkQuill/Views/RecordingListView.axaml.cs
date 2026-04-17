using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DarkQuill.Models;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// View for the recording list panel. Handles multi-select click routing to the ViewModel.
/// </summary>
public partial class RecordingListView : UserControl
{
    /// <summary>
    /// Initializes the recording list view and registers pointer event handlers.
    /// </summary>
    public RecordingListView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnRecordingItemPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Routes pointer-pressed events on recording items to the ViewModel's SelectRecording method,
    /// passing keyboard modifier state (Ctrl/Shift) for multi-select support.
    /// </summary>
    private void OnRecordingItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RecordingListViewModel viewModel)
            return;

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        var source = e.Source as Visual;
        var border = source?.FindAncestorOfType<Border>();

        while (border is not null)
        {
            if (border.Classes.Contains("RecordingItem") && border.Tag is Recording recording)
            {
                var modifiers = e.KeyModifiers;
                var isCtrl = modifiers.HasFlag(KeyModifiers.Control);
                var isShift = modifiers.HasFlag(KeyModifiers.Shift);
                viewModel.SelectRecording(recording, isCtrl, isShift);
                return;
            }

            border = border.FindAncestorOfType<Border>();
        }
    }
}
