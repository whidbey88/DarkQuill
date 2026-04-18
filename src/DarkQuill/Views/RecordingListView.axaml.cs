using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DarkQuill.Models;
using DarkQuill.Services;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// View for the recording list panel. Handles multi-select click routing to the ViewModel
/// and drag-and-drop import of external audio files.
/// </summary>
public partial class RecordingListView : UserControl
{
    /// <summary>
    /// Initializes the recording list view and registers pointer and drag-drop event handlers.
    /// </summary>
    public RecordingListView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnRecordingItemPressed, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
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

    /// <summary>
    /// Validates dragged data during DragOver and shows the drag highlight if files are valid audio.
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not RecordingListViewModel viewModel)
            return;

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            var hasAudio = files?.Any(f =>
                f is Avalonia.Platform.Storage.IStorageFile sf &&
                TranscriptionService.SupportedExtensions.Contains(
                    System.IO.Path.GetExtension(sf.Name))) ?? false;

            if (hasAudio)
            {
                e.DragEffects = DragDropEffects.Copy;
                viewModel.IsDragOver = true;
                e.Handled = true;
                return;
            }
        }

        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Hides the drag highlight when the drag leaves the recording list area.
    /// </summary>
    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is RecordingListViewModel viewModel)
        {
            viewModel.IsDragOver = false;
        }
    }

    /// <summary>
    /// Handles the Drop event by importing each valid audio file via the ViewModel.
    /// </summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not RecordingListViewModel viewModel)
            return;

        viewModel.IsDragOver = false;

        if (!e.Data.Contains(DataFormats.Files))
            return;

        var files = e.Data.GetFiles();
        if (files is null)
            return;

        foreach (var item in files)
        {
            if (item is Avalonia.Platform.Storage.IStorageFile storageFile)
            {
                var extension = System.IO.Path.GetExtension(storageFile.Name);
                if (TranscriptionService.SupportedExtensions.Contains(extension))
                {
                    var localPath = storageFile.Path.LocalPath;
                    await viewModel.ImportExternalFileAsync(localPath).ConfigureAwait(true);
                }
            }
        }

        e.Handled = true;
    }
}
