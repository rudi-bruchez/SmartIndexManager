using System;
using Avalonia;

namespace SmartIndexManager.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseWaylandIfAvailable()
            .WithInterFont()
            .LogToTrace();
}

internal static class PlatformExtensions
{
    /// <summary>
    /// Uses the native Wayland backend on Linux when WAYLAND_DISPLAY is present,
    /// otherwise keeps the backend configured by UsePlatformDetect (X11 on Linux).
    /// Avalonia.Wayland 12.1.0 does not yet expose UseWaylandWithFallback.
    /// </summary>
    public static AppBuilder UseWaylandIfAvailable(this AppBuilder builder)
    {
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            builder.UseWayland();
        }

        return builder;
    }
}
