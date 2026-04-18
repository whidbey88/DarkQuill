using Avalonia;
using Avalonia.WebView.Desktop;

namespace DarkQuill;

/// <summary>
/// Application entry point. Configures the Avalonia platform and launches the application.
/// </summary>
internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // WebView2 needs a writable cache folder. When installed to Program Files,
        // the default location (next to the exe) is read-only. Redirect to AppData.
        var webViewCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DarkQuill", "WebView2Cache");
        Directory.CreateDirectory(webViewCache);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewCache);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures the Avalonia application builder with platform defaults and fonts.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseDesktopWebView()
            .WithInterFont()
            .LogToTrace();
}
