using Avalonia;
using Avalonia.Headless;
using ReactiveUI.Avalonia.Reactive;

[assembly: AvaloniaTestApplication(typeof(CampertronGUITests.TestAppBuilder))]

namespace CampertronGUITests
{
    // Headless counterpart of Campertron.Program.BuildAvaloniaApp (Program is internal): the same App class and the
    // same UseReactiveUI configuration, with the headless platform instead of UsePlatformDetect. Each [AvaloniaTest]
    // runs on Avalonia's UI thread in its own application instance (default AvaloniaTestIsolationLevel.PerTest).
    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<Campertron.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .UseReactiveUI(_ => { });
    }
}
