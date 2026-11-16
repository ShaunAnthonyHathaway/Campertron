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
            // "--smoke-test" (release smoke tests, R8): a normal start that reports each step on standard output,
            // closes its window when the GUI is ready and returns 0 only if every step passed. See SmokeTest.
            if (SmokeTest.IsRequest(args))
            {
                return SmokeTest.Run(BuildAvaloniaApp(), args);
            }
            int ExitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            // If the window was closed while the startup was still preparing the data, let that finish first, and give a
            // search the close canceled a bounded time to stop (GS2).
            (Application.Current as App)?.WaitForPendingPreparation();
            (Application.Current as App)?.WaitForPendingSearch(App.SearchShutdownWait);
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
