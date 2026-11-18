using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Platform;
using Avalonia.Styling;
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
            "window-opened", "native-handle", "ready", "config-path", "rendered-frame", "navigation", "parks", "states",
            "window-size", "render-scaling", "theme", "theme-probe", "settings-probe", "settings-tab", "system-tab", "search-probe", "searches-tab",
            "filter-probe", "results-tab", "run-probe", "close-requested", "closing", "shutdown", "result",
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
            // D50: four top-level tabs with Searches shown at the start and the two nested rows; the parks were counted in
            // Campgrounds > By Park (the row's key keeps the name campsites, D55)
            Assert.That(Run.Value("navigation"), Is.EqualTo("start=Searches tabs=Searches,Results,Settings,Campgrounds settings=Campertron,System campsites=By Park,By City,By State"));
            Assert.That(Run.Value("navigation"), Is.EqualTo(SmokeTest.ExpectedNavigation));
            // D50: the theme probe ran in a temporary folder and restored the application's theme; Settings > System shows
            // the theme of the start: System, without a preferences file (the fixture has none)
            Assert.That(Run.Value("theme-probe"), Is.EqualTo("pass default=System saved=Dark reloaded=Dark kept-keys=yes malformed=System unknown=System applied=Light,Dark,System"));
            Assert.That(Directory.GetDirectories(Path.GetTempPath(), "campertron-theme-probe-*"), Is.Empty, "the probe removes its folder");
            Assert.That(Application.Current!.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Default), "the probe restored the application's theme");
            // V011-2 and V011-1: the current configuration and the campground data, nothing pressed and no refresh started;
            // D62: the automatic restart's launcher of this process (the test host runs under the dotnet host; a release
            // archive reports apphost), resolved without starting anything or writing a handoff
            Assert.That(RestartTargets.Current(), Is.Not.Null);
            Assert.That(Run.Value("system-tab"), Is.EqualTo("yes choices=System,Light,Dark selected=System preference=missing configuration=8 paths=startup copy=bound open-folder=bound actions=idle"
                + " campground-data=shown last-refreshed=shown refresh=bound confirmation=ready refresh-state=idle restart=" + RestartTargets.Current()!.Kind));
            Assert.That(Directory.Exists(Path.Join(GuiTestEnvironment.Root, ThemePreferenceStore.FolderName, RestartHandoffStore.FolderName)), Is.False,
                "the smoke test writes no restart handoff");
            Assert.That(File.Exists(Path.Join(GuiTestEnvironment.Root, ThemePreferenceStore.FolderName, ThemePreferenceStore.FileName)), Is.False,
                "the smoke test never writes the user's theme preference");
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
            // D51: the first listed campground with campsites (the fixture's Alpha) gave its filter values on a worker thread,
            // the fixture database has Campertron's index, and a selector checked a value
            Assert.That(Run.Value("filter-probe"), Is.EqualTo("pass campground=" + GuiTestEnvironment.CampgroundAlphaID
                + " campsites=4 equipment=5 types=3 names=3 attributes=4 thread=worker index=yes selector=toggled"));
            // GS2: the Results tab with the startup's folders and its bound buttons, then the run probe in a temporary folder
            // with a fixture campground and transport: a completed run bound to the list, a prompt cancel, a block without exit
            Assert.That(Run.Value("results-tab"), Is.EqualTo("yes files=0 paths=startup start=bound cancel=bound"));
            Assert.That(Run.Value("run-probe"), Does.Match("^pass planned=1 completed=openings:[1-9][0-9]* results=bound canceled=prompt blocked=no-exit cooldown=on network=none$"));
            Assert.That(Directory.GetDirectories(Path.GetTempPath(), "campertron-run-probe-*"), Is.Empty, "the probe removes its folder");
            Assert.That(Run.ViewModel.Results!.IsDetached, Is.True, "the close detached the Results tab");
            Assert.That(Named<ResultsView>(Run.Window, "ResultsContent").DataContext, Is.SameAs(Run.ViewModel.Results), "the real tab got its view model back");
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

        // TD-56: a failure reported from another thread (an unhandled exception on a worker) closes the window through the
        // dispatcher Attach captured on the UI thread
        [AvaloniaTest]
        public void AFailureFromAWorkerThread_ClosesTheWindow()
        {
            using var Gate = new ManualResetEventSlim();
            var Run = new Harness(Loader: () => { Gate.Wait(TimeSpan.FromSeconds(20)); return CampertronLibrary.function.RecDotOrg.data.Load.GetConfig(); });
            Run.PumpUntil(() => Run.Lines.Any(l => l.StartsWith("SMOKE native-handle=")));
            Assert.That(Task.Run(() => Run.Smoke.Fail("a failure on a worker thread")).Wait(TimeSpan.FromSeconds(10)), Is.True);
            Run.PumpUntil(() => Run.Closed);
            Gate.Set();
            Run.ViewModel.PendingPreparation.Wait(TimeSpan.FromSeconds(20));

            Assert.That(Run.Closed, Is.True, "the close reached the window's dispatcher");
            Assert.That(Run.Smoke.Finish(0), Is.EqualTo(SmokeTest.FailExitCode));
            Assert.That(Run.Err.ToString(), Does.Contain("a failure on a worker thread"));
            Assert.That(Run.Value("result"), Is.EqualTo("fail"));
        }

        // TD-56: a failure reported from a worker thread before anything was attached (no window to close) asks for no
        // dispatcher, so the worker never becomes the UI thread. A plain test, so that this thread plays the UI thread; the
        // reset stands for the moment between two headless tests
        [Test]
        public void AFailureFromAWorkerThread_BeforeAttach_MakesNoWorkerTheUiThread()
        {
            MethodInfo? Reset = typeof(Dispatcher).GetMethod("ResetBeforeUnitTests", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(Reset, Is.Not.Null, "Avalonia's reset between two headless tests (Dispatcher.ResetBeforeUnitTests) was not found: reassess this test after the Avalonia change");
            Reset!.Invoke(null, null);
            try
            {
                var Smoke = new SmokeTest(new StringWriter(), new StringWriter(), TimeSpan.FromSeconds(30), _ => { }, _ => null, (_, _) => { });
                Assert.That(Task.Run(() => Smoke.Fail("an early failure")).Wait(TimeSpan.FromSeconds(10)), Is.True);
                Assert.That(Smoke.Failure, Is.EqualTo("an early failure"));
                Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True, "the worker asked for Dispatcher.UIThread and became the UI thread");
            }
            finally
            {
                Reset.Invoke(null, null);
            }
        }

        // TD-56: only Attach, which runs on the UI thread, asks for Dispatcher.UIThread; Fail and every lambda use the
        // dispatcher it captured
        [Test]
        public void TheSmokeTest_AsksForTheUiDispatcherOnlyInAttach()
        {
            const string UiThread = "Avalonia.Threading.Dispatcher::get_UIThread";
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.That(typeof(SmokeTest).GetMethods(Declared).Where(M => ReferencedMembers(M).Contains(UiThread)).Select(M => M.Name), Is.EqualTo(new[] { "Attach" }));
            List<string> FromLambdas = typeof(SmokeTest).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(T => T.GetMethods(Declared)).SelectMany(ReferencedMembers).ToList();
            Assert.That(FromLambdas, Does.Not.Contain(UiThread));
            Assert.That(ReferencedMembers(typeof(SmokeTest).GetMethod(nameof(SmokeTest.Fail))!), Does.Contain("Avalonia.Threading.Dispatcher::Post"), "Fail posts to the captured dispatcher");
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
