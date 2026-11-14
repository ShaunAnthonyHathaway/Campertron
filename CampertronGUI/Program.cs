using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CampertronLibrary.function.Base;
using ReactiveUI.Avalonia.Reactive;
using System;

namespace Campertron
{
    internal class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // "--version" prints the version and .NET runtime and exits before Avalonia starts (no window,
            // no configuration or database access). On Windows the GUI is a WinExe, so redirect its output
            // to see it, for example: CampertronGUI.exe --version | more
            if (RuntimeInfo.IsVersionRequest(args))
            {
                Console.WriteLine(RuntimeInfo.Describe(typeof(Program).Assembly));
                return 0;
            }
            int ExitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            // If the window was closed while the startup was still preparing the data, let that finish first.
            (Application.Current as App)?.WaitForPendingPreparation();
            return ExitCode;
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI(_ => { });
    }
}
