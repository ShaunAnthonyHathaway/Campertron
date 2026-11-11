using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using CampertronLibrary.function.Base;
using System;

namespace Campertron
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // "--version" prints the version and .NET runtime and exits before Avalonia starts (no window,
            // no configuration or database access). On Windows the GUI is a WinExe, so redirect its output
            // to see it, for example: CampertronGUI.exe --version | more
            if (RuntimeInfo.IsVersionRequest(args))
            {
                Console.WriteLine(RuntimeInfo.Describe(typeof(Program).Assembly));
                return;
            }
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}
