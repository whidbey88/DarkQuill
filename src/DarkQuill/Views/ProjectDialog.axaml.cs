using Avalonia.Controls;
using Avalonia.Input;
using DarkQuill.Models;
using DarkQuill.ViewModels;

namespace DarkQuill.Views;

/// <summary>
/// Modal dialog for project selection, creation, and loading.
/// </summary>
public partial class ProjectDialog : Window
{
    /// <summary>
    /// Initializes the project dialog.
    /// </summary>
    public ProjectDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is ProjectDialogViewModel viewModel)
        {
            viewModel.RequestClose = CloseWithResult;
            await viewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Handles double-click on a today's project tile to select it immediately.
    /// </summary>
    private void OnExistingProjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectDialogViewModel vm && vm.SelectedExistingProject is not null)
        {
            vm.SelectExistingProjectCommand.Execute(vm.SelectedExistingProject);
        }
    }

    /// <summary>
    /// Handles double-click on a previous project tile to load it immediately.
    /// </summary>
    private void OnPreviousProjectDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ProjectDialogViewModel vm && vm.SelectedAllProject is not null)
        {
            vm.LoadPreviousProjectCommand.Execute(vm.SelectedAllProject);
        }
    }

    /// <summary>
    /// Closes the dialog with the specified project result.
    /// </summary>
    private void CloseWithResult(ProjectInfo? result)
    {
        Close(result);
    }
}
