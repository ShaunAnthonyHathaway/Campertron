using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // `CampertronGUI --smoke-test` (R8). These tests attach the smoke test to a real MainWindow on the headless platform,
    // as SmokeTest.Run does with the classic desktop lifetime. The headless window has no native handle, so the tests
    // pass one; the rendered-frame signal is Avalonia's own (render timer ticks forced) except where a test removes it.
    public class SmokeTestTests
    {
        private static readonly string[] AttachedKeys =
        {
            "window-opened", "native-handle", "ready", "config-path", "rendered-frame", "parks", "states",
            "window-size", "render-scaling", "theme", "settings-probe", "settings-tab", "search-probe", "searches-tab", "close-requested", "closing", "shutdown", "result",
        };

        private sealed class Harness
        {
            public readonly StringWriter Out = new StringWriter();
            public readonly StringWriter Err = new StringWriter();
            private int _Aborted = -1;
            public int Aborted => Volatile.Read(ref _Aborted);
            public SmokeTest Smoke = null!;
            public MainWindow Window = null!;
            public MainWindowViewModel ViewModel = null!;
            public bool Closed;

            public Harness(Func<CampertronInternalConfig>? Loader = null, Func<TopLevel, IPlatformHandle?>? Handle = null,
                Action<TopLevel, Action>? Frame = null, double TimeoutSeconds = 30)
            {
                Smoke = new SmokeTest(Out, Err, TimeSpan.FromSeconds(TimeoutSeconds), Code => Interlocked.Exchange(ref _Aborted, Code),
                    Handle ?? (_ => new PlatformHandle(new IntPtr(0x2a), "TEST")), Frame ?? SmokeTest.WaitForRenderedFrame);
                ViewModel = Loader == null ? new MainWindowViewModel() : new MainWindowViewModel(Loader);
                Window = new MainWindow { DataContext = ViewModel };
                Window.Closed += (_, _) => Closed = true;
                Smoke.Attach(Window);
                Window.Show();
            }

            // Pumps the UI thread (and the render timer, which drives animation frames) until Done.
            public void PumpUntil(Func<bool> Done, int Milliseconds = 15000)
            {
                var Clock = System.Diagnostics.Stopwatch.StartNew();
                while (!Done() && Clock.ElapsedMilliseconds < Milliseconds)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Thread.Sleep(1);
                }
                Dispatcher.UIThread.RunJobs();
            }

            public List<string> Lines => Out.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();

            public string Value(string Key) => Lines.Single(l => l.StartsWith("SMOKE " + Key + "=")).Substring(("SMOKE " + Key + "=").Length);
        }

        [Test]
        public void Argument_IsRecognisedOnlyAlone_AndDoesNotAffectVersion()
        {
            Assert.That(SmokeTest.IsRequest(new[] { "--smoke-test" }), Is.True);
            Assert.That(SmokeTest.IsRequest(Array.Empty<string>()), Is.False);
            Assert.That(SmokeTest.IsRequest(null), Is.False);
            Assert.That(SmokeTest.IsRequest(new[] { "--smoke-test", "--version" }), Is.False);
            Assert.That(SmokeTest.IsRequest(new[] { "--SMOKE-TEST" }), Is.False);
            Assert.That(SmokeTest.IsRequest(new[] { "--version" }), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "--smoke-test" }), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "--version" }), Is.True);
        }

        [AvaloniaTest]
        public void Success_ReportsEveryStepInOrder_ClosesTheWindow_AndReturnsZero()
        {
            var Run = new Harness();
            Run.PumpUntil(() => Run.Closed);
            int ExitCode = Run.Smoke.Finish(0);

            Assert.That(ExitCode, Is.EqualTo(SmokeTest.PassExitCode), Run.Err.ToString());
            Assert.That(Run.Err.ToString(), Is.Empty);
            Assert.That(Run.Lines.All(l => l.StartsWith("SMOKE ")), Is.True, string.Join("\n", Run.Lines));
            Assert.That(Run.Lines.Select(l => l.Substring(6, l.IndexOf('=') - 6)), Is.EqualTo(AttachedKeys), string.Join("\n", Run.Lines));
            Assert.That(Run.Value("window-opened"), Is.EqualTo("yes"));
            Assert.That(Run.Value("native-handle"), Is.EqualTo("TEST:0x2a"));
            Assert.That(Run.Value("ready"), Is.EqualTo("yes"));
            Assert.That(Run.Value("config-path"), Is.EqualTo(GuiTestEnvironment.ConfigPath));
            Assert.That(Run.Value("parks"), Is.EqualTo("4"));
            Assert.That(Run.Value("states"), Is.EqualTo("2"));
            Assert.That(Run.Value("rendered-frame"), Is.EqualTo("yes"));
            Assert.That(Run.Value("window-size"), Is.EqualTo("1024x768"));
            Assert.That(Run.Value("render-scaling"), Is.EqualTo("1"));
            Assert.That(Run.Value("theme"), Is.EqualTo("Light").Or.EqualTo("Dark"));
            // CM5: the settings editor's probe ran in a temporary folder, and the Settings tab opened the fixture's files
            Assert.That(Run.Value("settings-probe"), Is.EqualTo("pass general=saved email=created,saved backups=exact line-endings=crlf bom=kept comments=kept permissions="
                + (OperatingSystem.IsWindows() ? "access-rules-kept" : "640,600")));
            Assert.That(Run.Value("settings-tab"), Is.EqualTo("yes general=editable email=editable"));
            Assert.That(Directory.GetDirectories(Path.GetTempPath(), "campertron-settings-probe-*"), Is.Empty, "the probe removes its folder");
            // CM6: the search editor's probe ran in a temporary folder with the fixture's campground data, and the Searches
            // tab listed the fixture's (no) search files and its 12 reservable, enabled campgrounds
            Assert.That(Run.Value("search-probe"), Is.EqualTo("pass created=disabled auto-run=toggled-alone lists=kept comments=kept line-endings=crlf bom=kept "
                + "save-as=created,no-overwrite campground-id=exact permissions=" + (OperatingSystem.IsWindows() ? "access-rules-kept" : "640")));
            Assert.That(Run.Value("searches-tab"), Is.EqualTo("yes files=0 campgrounds=12"));
            Assert.That(Directory.GetDirectories(Path.GetTempPath(), "campertron-search-probe-*"), Is.Empty, "the probe removes its folder");
            Assert.That(Directory.GetFiles(GuiTestEnvironment.ConfigPath, "*.yaml").Select(Path.GetFileName), Is.SubsetOf(new[] { "General.yaml", "Email.yaml" }), "no search file written into the settings folder");
            Assert.That(Run.Value("close-requested"), Is.EqualTo("yes"));
            Assert.That(Run.Value("closing"), Is.EqualTo("programmatic=yes reason=WindowClosing"));
            Assert.That(Run.Value("shutdown"), Is.EqualTo("clean exit-code=0"));
            Assert.That(Run.Value("result"), Is.EqualTo("pass"));
            Assert.That(Run.Aborted, Is.EqualTo(-1));
        }

        [AvaloniaTest]
        public void StartupFailure_FailsWithTheReasonOnStandardError()
        {
            var Run = new Harness(Loader: () => throw new InvalidDataException("Property 'theme' not found on type 'GeneralConfig'."));
            Run.PumpUntil(() => Run.Closed);
            int ExitCode = Run.Smoke.Finish(0);

            Assert.That(ExitCode, Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("startup failed: Property 'theme' not found on type 'GeneralConfig'."));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE ready=")), Is.False);
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE shutdown=")), Is.False);
        }

        [AvaloniaTest]
        public void MissingNativeHandle_Fails()
        {
            var Run = new Harness(Handle: _ => null);
            Run.PumpUntil(() => Run.Closed);

            Assert.That(Run.Smoke.Finish(0), Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("no native platform handle"));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));

            var Zero = new Harness(Handle: _ => new PlatformHandle(IntPtr.Zero, "TEST"));
            Zero.PumpUntil(() => Zero.Closed);
            Assert.That(Zero.Smoke.Finish(0), Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Zero.Err.ToString(), Does.Contain("no native platform handle"));
        }

        // Without a rendered frame the smoke test times out: it reports the step it waited for and exits with code 2
        // (the process exits even if the UI thread is blocked).
        [AvaloniaTest]
        public void MissingRenderedFrame_TimesOut()
        {
            var Run = new Harness(Frame: (_, _) => { }, TimeoutSeconds: 1);
            Run.PumpUntil(() => Run.Aborted != -1, 10000);

            Assert.That(Run.Aborted, Is.EqualTo(SmokeTest.TimeoutExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("timed out after 1 s waiting for a rendered frame"));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE rendered-frame=")), Is.False);
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE parks=")), Is.False);
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE close-requested=")), Is.False);
            Run.Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Run.Smoke.Finish(0), Is.Not.EqualTo(SmokeTest.PassExitCode));
            Assert.That(Run.Lines.Count(l => l.StartsWith("SMOKE result=")), Is.EqualTo(1), "no second result after the timeout");
        }

        // A window closed by anything but the smoke test (here, while the data is still being prepared) is a failure.
        [AvaloniaTest]
        public void WindowClosedBeforeTheSmokeTestFinished_Fails()
        {
            using var Gate = new ManualResetEventSlim();
            var Run = new Harness(Loader: () => { Gate.Wait(TimeSpan.FromSeconds(20)); return CampertronLibrary.function.RecDotOrg.data.Load.GetConfig(); });
            Run.PumpUntil(() => Run.Lines.Any(l => l.StartsWith("SMOKE native-handle=")));
            Run.Window.Close();
            Dispatcher.UIThread.RunJobs();
            Gate.Set();
            Run.ViewModel.PendingPreparation.Wait(TimeSpan.FromSeconds(20));

            Assert.That(Run.Smoke.Finish(0), Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("the window was closed before the smoke test finished (programmatic=yes reason=WindowClosing)"));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));
        }

        [AvaloniaTest]
        public void UncleanLifetimeExit_Fails()
        {
            var Run = new Harness();
            Run.PumpUntil(() => Run.Closed);

            Assert.That(Run.Smoke.Finish(3), Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("the application lifetime returned exit code 3"));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));
            Assert.That(Run.Lines.Any(l => l.StartsWith("SMOKE shutdown=")), Is.False);
        }
    }
}
