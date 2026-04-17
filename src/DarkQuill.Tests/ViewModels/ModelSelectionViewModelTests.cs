using NSubstitute;
using DarkQuill.Models;
using DarkQuill.Services;
using DarkQuill.ViewModels;
using Xunit;

namespace DarkQuill.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="ModelSelectionViewModel"/> covering model enumeration,
/// selection persistence, and cancel behavior.
/// </summary>
public class ModelSelectionViewModelTests
{
    private readonly ITranscriptionService _transcriptionService;
    private readonly ISettingsService _settingsService;
    private readonly ModelSelectionViewModel _sut;

    public ModelSelectionViewModelTests()
    {
        _transcriptionService = Substitute.For<ITranscriptionService>();
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new ApplicationSettings());
        _sut = new ModelSelectionViewModel(_transcriptionService, _settingsService);
    }

    // ───────────────────────────────────────────────
    // LoadModelsCommand
    // ───────────────────────────────────────────────

    [Fact]
    public async Task LoadModelsCommand_PopulatesAvailableModels()
    {
        // Arrange
        var models = new List<string> { "ggml-base.bin", "ggml-large-v3-turbo.bin" };
        _transcriptionService.GetAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns(models.AsReadOnly());

        // Act
        await _sut.LoadModelsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(2, _sut.AvailableModels.Count);
        Assert.Contains("ggml-base.bin", _sut.AvailableModels);
        Assert.Contains("ggml-large-v3-turbo.bin", _sut.AvailableModels);
    }

    [Fact]
    public async Task LoadModelsCommand_SelectsPreviouslySavedModel()
    {
        // Arrange
        var settings = new ApplicationSettings { SelectedWhisperModel = "ggml-large-v3-turbo.bin" };
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(settings);
        var models = new List<string> { "ggml-base.bin", "ggml-large-v3-turbo.bin" };
        _transcriptionService.GetAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns(models.AsReadOnly());

        // Act
        await _sut.LoadModelsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("ggml-large-v3-turbo.bin", _sut.SelectedModel);
    }

    [Fact]
    public async Task LoadModelsCommand_SelectsFirstModelWhenNoSavedSelection()
    {
        // Arrange
        var models = new List<string> { "ggml-base.bin", "ggml-large-v3-turbo.bin" };
        _transcriptionService.GetAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns(models.AsReadOnly());

        // Act
        await _sut.LoadModelsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal("ggml-base.bin", _sut.SelectedModel);
    }

    [Fact]
    public async Task LoadModelsCommand_ShowsMessageWhenNoModelsFound()
    {
        // Arrange
        _transcriptionService.GetAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string>().AsReadOnly());

        // Act
        await _sut.LoadModelsCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(_sut.AvailableModels);
        Assert.Contains("No models found", _sut.StatusMessage);
    }

    [Fact]
    public async Task LoadModelsCommand_SetsModelsFolderFromSettings()
    {
        // Arrange
        var settings = new ApplicationSettings { WhisperModelsFolder = @"C:\custom\models" };
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(settings);
        _transcriptionService.GetAvailableModelsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<string>().AsReadOnly());

        // Act
        await _sut.LoadModelsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(@"C:\custom\models", _sut.ModelsFolder);
    }

    // ───────────────────────────────────────────────
    // ApplyCommand
    // ───────────────────────────────────────────────

    [Fact]
    public async Task ApplyCommand_SavesSelectedModelToSettings()
    {
        // Arrange
        var settings = new ApplicationSettings();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(settings);
        _sut.SelectedModel = "ggml-base.bin";

        // Act
        await _sut.ApplyCommand.ExecuteAsync(null);

        // Assert
        await _settingsService.Received(1).SaveSettingsAsync(
            Arg.Is<ApplicationSettings>(s => s.SelectedWhisperModel == "ggml-base.bin"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyCommand_InvokesRequestCloseWithTrue()
    {
        // Arrange
        var settings = new ApplicationSettings();
        _settingsService.LoadSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(settings);
        bool? closedWith = null;
        _sut.RequestClose = applied => closedWith = applied;
        _sut.SelectedModel = "ggml-base.bin";

        // Act
        await _sut.ApplyCommand.ExecuteAsync(null);

        // Assert
        Assert.True(closedWith);
    }

    // ───────────────────────────────────────────────
    // CancelCommand
    // ───────────────────────────────────────────────

    [Fact]
    public void CancelCommand_DoesNotSaveSettings()
    {
        // Arrange
        _sut.SelectedModel = "ggml-base.bin";

        // Act
        _sut.CancelCommand.Execute(null);

        // Assert
        _settingsService.DidNotReceive().SaveSettingsAsync(
            Arg.Any<ApplicationSettings>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CancelCommand_InvokesRequestCloseWithFalse()
    {
        // Arrange
        bool? closedWith = null;
        _sut.RequestClose = applied => closedWith = applied;

        // Act
        _sut.CancelCommand.Execute(null);

        // Assert
        Assert.False(closedWith);
    }
}
