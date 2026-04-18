using Avalonia.Controls;
using Avalonia.Platform;

namespace DarkQuill.Views;

/// <summary>
/// Resizable window displaying the DarkQuill user guide in an embedded WebView.
/// Loads the HTML user guide from embedded application assets.
/// </summary>
public partial class HelpWindow : Window
{
    /// <summary>
    /// Initializes the help window and navigates to the embedded user guide HTML.
    /// </summary>
    public HelpWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        LoadUserGuideAsync();
    }

    /// <summary>
    /// Extracts the embedded HTML user guide to a temporary file and loads it in the WebView.
    /// WebView cannot navigate directly to avares:// URIs, so we write to a temp file first.
    /// </summary>
    private async void LoadUserGuideAsync()
    {
        try
        {
            var assetUri = new Uri("avares://DarkQuill/Assets/Help/user-guide.html");
            using var stream = AssetLoader.Open(assetUri);
            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync().ConfigureAwait(true);

            var tempPath = Path.Combine(Path.GetTempPath(), "darkquill-user-guide.html");
            await File.WriteAllTextAsync(tempPath, html).ConfigureAwait(true);

            HelpWebView.Url = new Uri($"file:///{tempPath.Replace('\\', '/')}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load user guide: {ex}");
        }
    }
}
