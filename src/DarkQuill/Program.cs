using Avalonia;

namespace DarkQuill;

/// <summary>
/// Application entry point. Configures the Avalonia platform and launches the application.
/// </summary>
internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures the Avalonia application builder with platform defaults and fonts.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
