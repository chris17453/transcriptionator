using System.Runtime.InteropServices;
using Avalonia;

namespace Transcriptonator;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowFatalError($"An unexpected error occurred.\n\n{ex.GetType().Name}: {ex.Message}");
        };

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            ShowFatalError(
                "Transcriptonator failed to start.\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n" +
                "This may indicate a missing runtime or corrupted installation.\n" +
                "Try reinstalling the application.");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    internal static void ShowFatalError(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                MessageBox(IntPtr.Zero, message, "Transcriptonator - Error", 0x10 /* MB_ICONERROR */);
            }
            catch { /* P/Invoke unavailable, fall through to stderr */ }
        }

        Console.Error.WriteLine(message);
        Environment.Exit(1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
