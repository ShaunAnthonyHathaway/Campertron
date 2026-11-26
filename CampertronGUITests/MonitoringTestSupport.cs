using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.monitoring;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's monitoring under test (v0.4.0's phase 4): the real window, its MainWindowViewModel and the library's
    // MonitoringController and MonitoringService, with a recreation.gov the test answers in memory (AvailabilityApi
    // .ConsoleDownload), email and ntfy that only record (CampsiteConfig.SendResultsEmail, Notifications.PublishNtfy), the
    // fixture campground's campsite details in the cache folder (ResultsFolders), the waits between two checks the test's and
    // a fixed clock. Nothing reaches the network, no real server, recipient or topic is used, and no test leaves a monitoring
    // behind (the window's close stops it, then the controller is shut down and waited for).
    public abstract class DesktopMonitoringTestBase
    {
        internal static readonly DateTime Fixed = new DateTime(2026, 11, 26, 9, 30, 0);
        internal static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

        private Func<string, string> OriginalDownload = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Action<TimeSpan> OriginalBlockedWait = null!;
        private Action<int> OriginalExit = null!;

        internal ResultsFolders Folders = null!;
        internal Waits Waited = null!;
        internal ManualTimers Timers = null!;
        internal ManualTime Time = null!;
        // what recreation.gov answers for a month's URL (by default every campsite of the fixture campground is available)
        internal Func<string, string> Answer = Available;
        internal readonly ConcurrentQueue<string> Requested = new();
        internal readonly ConcurrentQueue<string> Emails = new();
        internal readonly ConcurrentQueue<string> Published = new();
        // what a send does after recording it; it throws to refuse it
        internal Action<string> Send = _ => { };
        internal int Prepared;
        // the monitoring's clock (fixed unless a test moves it)
        internal Func<DateTime> Clock = () => Fixed;
        private readonly List<(MainWindow Window, MainWindowViewModel ViewModel)> Shown = new();

        [SetUp]
        public void MonitoringSetUp()
        {
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            OriginalBlockedWait = AvailabilityApi.BlockedWait;
            OriginalExit = AvailabilityApi.ExitProcess;
            Folders = new ResultsFolders();
            Waited = new Waits();
            Timers = new ManualTimers();
            Time = new ManualTime();
            Answer = Available;
            Send = _ => { };
            Prepared = 0;
            Clock = () => Fixed;
            Requested.Clear();
            Emails.Clear();
            Published.Clear();
            Shown.Clear();
            SmtpPassword.GetEnvironment = _ => null;
            NtfyToken.GetEnvironment = _ => null;
            AvailabilityApi.BlockedWait = _ => throw new AssertionException("the console's five-minute wait is the console's only");
            AvailabilityApi.ExitProcess = _ => throw new AssertionException("the desktop GUI's monitoring never ends the process");
            AvailabilityApi.ConsoleDownload = Url =>
            {
                Requested.Enqueue(Url);
                return Answer(Url);
            };
            CampsiteConfig.SendResultsEmail = (Settings, Message) =>
            {
                string Subject = Message.Subject;
                Message.Dispose();
                Emails.Enqueue(Subject);
                Send(Subject);
            };
            Notifications.PublishNtfy = (Settings, Message) => Published.Enqueue(Message.Title);
        }

        [TearDown]
        public void MonitoringTearDown()
        {
            // no test leaves a monitoring behind: its thread would use the next test's seams
            foreach ((MainWindow Window, MainWindowViewModel ViewModel) in Shown)
            {
                if (Window.IsVisible)
                {
                    ViewModel.MonitoringTab?.Detach();
                }
                ViewModel.Monitoring?.BeginShutdown().Wait(Long);
            }
            Shown.Clear();
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            AvailabilityApi.BlockedWait = OriginalBlockedWait;
            AvailabilityApi.ExitProcess = OriginalExit;
            Folders.Dispose();
        }

        // ---- recreation.gov as the test answers it ----

        internal static string Available(string Url) => RunProbe.MonthJson(MonthOf(Url), RunProbe.Campsites);
        internal static string Nothing(string Url) => RunProbe.MonthJson(MonthOf(Url), RunProbe.Campsites, (Site, Day) => false);
        internal static string Blocked(string Url) => "<html>Request blocked</html>";

        internal static DateTime MonthOf(string Url)
        {
            Match Asked = Regex.Match(Url, @"/month\?start_date=(\d{4})-(\d{2})-");
            Assert.That(Asked.Success, Is.True, "a month's URL: " + Url);
            return new DateTime(int.Parse(Asked.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(Asked.Groups[2].Value, CultureInfo.InvariantCulture), 1);
        }

        // ---- the waits between two checks ----

        // each wait holds until the monitoring is stopped or the test lets the next check begin
        internal sealed class Waits
        {
            private readonly AutoResetEvent _Next = new AutoResetEvent(false);
            private int _Count;
            internal readonly SemaphoreSlim Began = new SemaphoreSlim(0);
            internal int Count => Volatile.Read(ref _Count);
            internal void Wait(TimeSpan Delay, CancellationToken Token)
            {
                Assert.That(Delay, Is.EqualTo(TimeSpan.FromMinutes(1)), "checks are one minute apart");
                Interlocked.Increment(ref _Count);
                Began.Release();
                WaitHandle.WaitAny(new[] { Token.WaitHandle, _Next });
            }
            internal void Next() => _Next.Set();
            internal void Await()
            {
                Assert.That(Began.Wait(Long), Is.True, "a wait between two checks began");
            }
        }

        // ---- the window ----

        // a search file the monitoring checks (Enabled (autoRun)) for the fixture campground
        internal void WriteSearch(string File = "Alpha.yaml", string Name = "First trip", bool AutoRun = true) =>
            Folders.Write(File, ResultsFolders.Search(Name, AutoRun));

        internal DesktopMonitoringSeams MonitoringSeams() => new DesktopMonitoringSeams
        {
            Now = () => Clock(),
            Wait = Waited.Wait,
            PrepareCampgroundData = (_, _) => Interlocked.Increment(ref Prepared),
            Ticker = Timers.Every
        };

        // the real window with these folders; Results and refresh seams of the test's (the Results tab's ticker is the test's)
        internal (MainWindow Window, MainWindowViewModel ViewModel) ShowWindow(ResultsSeams? Results = null, RefreshSeams? Refresh = null, RestartHandoff? Arrival = null,
            SystemSettingsViewModel? System = null)
        {
            File.Copy(GuiTestEnvironment.DbFile, Folders.In("RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folders.Config, CachePath = Folders.Cache },
                Results ?? new ResultsSeams { Delay = Timers.Once, Ticker = Timers.Every, Time = Time }, System, Refresh, MonitoringSeams()) { Arrival = Arrival };
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Shown.Add((Window, ViewModel));
            Settle(Window);
            return (Window, ViewModel);
        }

        internal static void Settle(MainWindow Window)
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            }
        }

        internal static void ShowMonitoringTab(MainWindow Window)
        {
            Tabs(Window).SelectedIndex = MainWindow.MonitoringTabIndex;
            Settle(Window);
        }

        internal static MonitoringView TabView(MainWindow Window) => Named<MonitoringView>(Window, "MonitoringContent");

        internal static T InTab<T>(MainWindow Window, string Name) where T : Avalonia.Controls.Control =>
            TabView(Window).FindControl<T>(Name) ?? throw new AssertionException(Name + " not found in the Monitoring tab");

        // the controller's view once Wanted says so (the UI thread runs meanwhile)
        internal static MonitoringControlView Until(MainWindowViewModel ViewModel, Func<MonitoringControlView, bool> Wanted, string What)
        {
            MonitoringControlView? Seen = null;
            WaitUntil(() => Wanted(Seen = ViewModel.Monitoring!.Controller.View()), () => What + ": " + DesktopMonitoringWording.StateText(Seen ?? ViewModel.Monitoring!.Controller.View()));
            return ViewModel.Monitoring!.Controller.View();
        }

        internal static MonitoringControlView Waiting(MainWindowViewModel ViewModel) =>
            Until(ViewModel, V => V.Phase == MonitoringControlPhase.Running && V.Status.State == MonitoringState.Waiting && V.LastCheck != null, "the monitoring waits for its next check");

        internal static MonitoringControlView Ended(MainWindowViewModel ViewModel) =>
            Until(ViewModel, V => V.Busy == MonitoringControlBusy.None && !V.Status.IsRunning && (V.ThisEnd != null || (V.ThisStart != null && !V.ThisStart.Started)),
                "the run ended or the start was refused");

        // the tab's Start (its command, as the button runs it), then the tab reads the monitoring again
        internal static void Press(System.Windows.Input.ICommand Command)
        {
            Assert.That(Command.CanExecute(null), Is.True, "the action is unavailable");
            Command.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        // the settings folder's monitoring lock is held (by this or another process)
        internal bool LockIsHeld()
        {
            using FileStream? Taken = MonitoringLock.TryAcquire(Folders.Config, out MonitoringLockProblem? Problem);
            return Taken == null && Problem == MonitoringLockProblem.Held;
        }

        internal List<string> Markers()
        {
            string History = Folders.In("Camp.History");
            return !Directory.Exists(History) ? new List<string>()
                : Directory.GetFileSystemEntries(History, "*", SearchOption.AllDirectories).Select(P => Path.GetRelativePath(Folders.Config, P).Replace('\\', '/'))
                    .Where(P => P.Count(C => C == '/') == 4).OrderBy(P => P, StringComparer.Ordinal).ToList();
        }
    }
}
