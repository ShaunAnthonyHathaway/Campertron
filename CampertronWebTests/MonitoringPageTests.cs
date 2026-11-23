using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronWeb.Pages;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The web UI's monitoring page (v0.3.0's PR 8, D83 and D98; business-rules-settings §35) on the real host: the web UI's
    // own MonitoringService (the one email and ntfy mode run) started by web mode and by the page, stopped and restarted
    // from the page; a GET only looks; every action a POST with the antiforgery token and the moment the page showed, then
    // a redirect; the monitoring the web UI's, never a session's; the start's checks, the monitoring lock, Camp.History and
    // the send-failure rule the service's; the last check's openings one immutable snapshot in the shared result rows. The
    // monitoring's searches ask a recreation.gov the test answers in memory (AvailabilityApi.ConsoleDownload), its email and
    // ntfy notifications go to the test (CampsiteConfig.SendResultsEmail, Notifications.PublishNtfy), and the campground's
    // campsite details are in a cache folder of the test's own, so nothing reaches the network and no database is opened.
    public class MonitoringPageTests
    {
        internal const string Sentinel = "S3ntinel-password-PR8";
        internal const string ServerText = "s3cret-server-answer for someone@example.invalid at /etc/campertron";
        internal static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

        private Func<string, string> OriginalDownload = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Action<TimeSpan> OriginalBlockedWait = null!;
        private Action<int> OriginalExit = null!;

        // the monitoring's recreation.gov, email and ntfy, as the test answers them
        internal static Func<string, string> Answer = Available;
        internal static readonly ConcurrentQueue<string> Requested = new();
        internal static readonly ConcurrentQueue<string> Emails = new();
        internal static readonly ConcurrentQueue<string> Published = new();
        internal static Action<string> Send = Name => { };
        internal static Action<string> Publish = Name => { };

        [SetUp]
        public void SetUp()
        {
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            OriginalBlockedWait = AvailabilityApi.BlockedWait;
            OriginalExit = AvailabilityApi.ExitProcess;
            Requested.Clear();
            Emails.Clear();
            Published.Clear();
            Answer = Available;
            Send = Name => { };
            Publish = Name => { };
            SmtpPassword.GetEnvironment = Name => null;
            NtfyToken.GetEnvironment = Name => null;
            AvailabilityApi.BlockedWait = Wait => throw new AssertionException("the console's five-minute wait is the console's only");
            AvailabilityApi.ExitProcess = Code => throw new AssertionException("the web UI's monitoring never ends the process");
            AvailabilityApi.ConsoleDownload = Url =>
            {
                Requested.Enqueue(Url);
                return Answer(Url);
            };
            CampsiteConfig.SendResultsEmail = (Settings, Message) =>
            {
                string Name = Message.Subject;
                Message.Dispose();
                Emails.Enqueue(Name);
                Send(Name);
            };
            Notifications.PublishNtfy = (Settings, Message) =>
            {
                Published.Enqueue(Message.Title);
                Publish(Message.Title);
            };
        }

        [TearDown]
        public void TearDown()
        {
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            AvailabilityApi.BlockedWait = OriginalBlockedWait;
            AvailabilityApi.ExitProcess = OriginalExit;
        }

        // ---- recreation.gov as the test answers it ----

        // every night of every campsite available, for the month the URL asks for
        internal static string Available(string Url) => MonthJson(MonthOf(Url), Sites);
        internal static string[] Sites = Campsites;
        internal static string Nothing(string Url) => "{\"campsites\":{},\"count\":0}";
        internal static string Blocked(string Url) => "<html>Request blocked</html>";

        internal static DateTime MonthOf(string Url)
        {
            Match Asked = Regex.Match(Url, @"/month\?start_date=(\d{4})-(\d{2})-");
            Assert.That(Asked.Success, Is.True, "a month's URL: " + Url);
            return new DateTime(int.Parse(Asked.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(Asked.Groups[2].Value, CultureInfo.InvariantCulture), 1);
        }

        // ---- the wait between two checks ----

        // the waits of the monitoring: each holds until the monitoring is stopped or the test lets the next check begin (Next)
        internal sealed class Waits
        {
            private int _Count;
            internal int Count => Volatile.Read(ref _Count);
            private readonly AutoResetEvent _Next = new AutoResetEvent(false);
            internal readonly SemaphoreSlim Began = new SemaphoreSlim(0);
            internal void Wait(TimeSpan Delay, CancellationToken Token)
            {
                Assert.That(Delay, Is.EqualTo(TimeSpan.FromMinutes(1)), "checks are one minute apart");
                Interlocked.Increment(ref _Count);
                Began.Release();
                WaitHandle.WaitAny(new[] { Token.WaitHandle, _Next });
            }
            // the wait that holds now ends: the next check begins
            internal void Next() => _Next.Set();
            internal void Await(int Count = 1)
            {
                for (int i = 0; i < Count; i++)
                {
                    Assert.That(Began.Wait(Long), Is.True, "a wait between two checks began");
                }
            }
        }

        // ---- the web UI ----

        // a web UI with a cache folder of its own (the fixture campground's campsite details), a placeholder for the campground
        // data (never opened: the cache holds every detail), General.yaml (outputTo), Email.yaml with a plain-text password
        // and the monitoring's waits the test's. Run: the results page's stub run (null: one that returns nothing)
        internal sealed class MonitoringHost : IAsyncDisposable
        {
            internal WebTestHost Host { get; private set; } = null!;
            internal string Cache { get; private set; } = string.Empty;
            internal Waits Waits { get; } = new Waits();
            internal ManualTime Time { get; } = new ManualTime();
            internal string Folder => Host.Folder;
            internal string In(string Name) => Path.Join(Host.Folder, Name);
            internal WebMonitoring Monitoring => Host.Service<WebMonitoring>();
            internal string HistoryPath => In("Camp.History");

            internal static async Task<MonitoringHost> Start(string? OutputTo = "Email", bool WithPassword = true, bool WithEmail = true,
                Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>? Run = null, string? WebYaml = null,
                Action? Rendering = null)
            {
                var M = new MonitoringHost();
                M.Host = await WebTestHost.Start(WebYaml: WebYaml, WithPassword: WithPassword, Configure: Settings =>
                {
                    M.Cache = Settings.SettingsFolder + "-cache";
                    Directory.CreateDirectory(M.Cache);
                    WriteCampground(M.Cache);
                    Settings.CachePath = M.Cache;
                    Settings.PrepareCampgroundData = () => { };
                    Settings.Monitoring = new WebMonitoringSeams { Wait = M.Waits.Wait, Rendering = Rendering };
                    Settings.Results = new WebResultsSeams { Time = M.Time, Run = Run ?? Returns() };
                });
                File.WriteAllText(M.In("RecreationDotOrg.db"), string.Empty);
                if (OutputTo != null)
                {
                    File.WriteAllText(M.In("General.yaml"), "outputTo: " + OutputTo + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
                }
                if (WithEmail)
                {
                    File.WriteAllText(M.In("Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                        + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
                }
                return M;
            }

            // true while a monitoring holds the settings folder's lock
            internal bool LockIsHeld()
            {
                using FileStream? Taken = MonitoringLock.TryAcquire(Folder, out MonitoringLockProblem? Problem);
                return Taken == null && Problem == MonitoringLockProblem.Held;
            }

            // every marker of Camp.History
            internal List<string> Markers() => !Directory.Exists(HistoryPath) ? new List<string>()
                : Directory.GetFileSystemEntries(HistoryPath, "*", SearchOption.AllDirectories).Select(P => Path.GetRelativePath(Folder, P).Replace('\\', '/'))
                    .Where(P => P.Count(C => C == '/') == 4).OrderBy(P => P, StringComparer.Ordinal).ToList();

            public async ValueTask DisposeAsync()
            {
                await Host.DisposeAsync();
                if (Cache.Length > 0 && Directory.Exists(Cache))
                {
                    Directory.Delete(Cache, true);
                }
            }
        }

        // ---- the page ----

        internal static XElement Form(WebPage Page) => Page.Xml().Descendants("form").Single(F => ((string?)F.Attribute("action") ?? "").StartsWith("/monitoring", StringComparison.Ordinal));
        internal static string StateOf(WebPage Page) => (string)Form(Page).Descendants("input").Single(I => (string?)I.Attribute("name") == "state").Attribute("value")!;
        internal static XElement Button(WebPage Page, string Handler) => Form(Page).Descendants("button").Single(B => (string?)B.Attribute("formaction") == "/monitoring?handler=" + Handler);
        internal static bool Enabled(WebPage Page, string Handler) => Button(Page, Handler).Attribute("disabled") == null;
        internal static string State(WebPage Page) => Said(Page, "status")!;
        // an element's text as a reader hears it: its words, without the markup's line breaks
        internal static string? Said(WebPage Page, string Id) => Element(Page, Id) is XElement Found
            ? string.Join(" ", Found.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0)) : null;

        // an action with the moment the page showed (State: another one), then the page the redirect leads to
        internal static async Task<WebPage> Act(Browser Owner, string Handler, WebPage From, string? State = null)
        {
            WebPage Posted = await Owner.Post("/monitoring?handler=" + Handler, ("state", State ?? StateOf(From)));
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect), Posted.Body);
            Assert.That(Posted.Location, Is.EqualTo("/monitoring"));
            return await Owner.Get("/monitoring");
        }

        // the page once it says what Wanted wants
        internal static async Task<WebPage> Until(Browser Owner, Func<WebPage, bool> Wanted, string What)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                WebPage Page = await Owner.Get("/monitoring");
                if (Wanted(Page))
                {
                    return Page;
                }
                Assert.That(Clock.Elapsed, Is.LessThan(Long), What + ": " + State(Page));
                await Task.Delay(20);
            }
        }

        internal static Task<WebPage> Waiting(Browser Owner) => Until(Owner, P => State(P).StartsWith("Running: the next check starts at ", StringComparison.Ordinal), "the monitoring waits for its next check");
        internal static Task<WebPage> NotRunning(Browser Owner) => Until(Owner, P => State(P).StartsWith("Not running", StringComparison.Ordinal), "the monitoring is not running");

        internal static void WriteSearch(MonitoringHost M, string File, string Name, bool AutoRun = true) => System.IO.File.WriteAllText(M.In(File), Search(Name, AutoRun));

        // ---- who may use it, and what a GET does ----

        [Test]
        public async Task TheMonitoringPage_NeedsTheOwner_AndAGetOnlyLooks()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");

            using Browser Visitor = M.Host.Browser();
            WebPage Refused = await Visitor.Get("/monitoring");
            Assert.That((Refused.Status, Refused.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin?returnUrl=%2Fmonitoring")));
            foreach (string Handler in new[] { "start", "stop", "restart" })
            {
                WebPage Post = await Visitor.Post("/monitoring?handler=" + Handler, ("state", "0-idle"));
                Assert.That((Post.Status, Post.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin")), "to the sign-in, without starting anything");
            }
            Assert.That(M.Monitoring.View().Generation, Is.Zero, "no start without the owner");

            using Browser Owner = await M.Host.SignedIn();
            string Before = Snapshot(M.Folder);
            WebPage Page = await Owner.Get("/monitoring");
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Page.Header("Cache-Control"), Does.Contain("no-store"), "no browser or proxy keeps the monitoring's state");
            Assert.That(Page.Header("Content-Security-Policy"), Does.Contain("script-src 'self'").And.Contain("frame-ancestors 'none'"));
            Assert.That(Page.Xml().Descendants("h1").Single().Value, Is.EqualTo("Monitoring"));
            Assert.That(Page.Xml().Descendants("nav").First().Descendants("a").Select(A => A.Value), Is.EqualTo(new[] { "Start", "Searches", "Results", "Monitoring", "Settings", "Change password" }));
            Assert.That(Page.Xml().Descendants("a").Single(A => A.Value == "Monitoring" && (string?)A.Attribute("aria-current") == "page"), Is.Not.Null);
            Assert.That(Page.Text(), Does.Contain(WebMonitoringWording.NotUpdating).And.Contain(WebMonitoringWording.ShowLatest));
            Assert.That(State(Page), Is.EqualTo("Not running."));
            Assert.That((Enabled(Page, "start"), Enabled(Page, "stop"), Enabled(Page, "restart")), Is.EqualTo((true, false, false)));
            Assert.That(Said(Page, "actions-unavailable"), Is.EqualTo(WebMonitoringWording.StopUnavailableIdle), "why Stop and Restart can't be used, in words");
            Assert.That(Said(Page, "no-check"), Is.EqualTo(WebMonitoringWording.NoCheckYet));
            for (int i = 0; i < 3; i++)
            {
                await Owner.Get("/monitoring");
                await Owner.Get("/monitoring?run=1&check=1&search=0&part=2");
            }
            Assert.That(Snapshot(M.Folder), Is.EqualTo(Before), "a GET writes nothing: no lock file, no Camp.History, no settings file");
            Assert.That(M.Monitoring.View().Generation, Is.Zero, "a GET starts nothing");
            Assert.That((Requested.Count, Emails.Count, Published.Count), Is.EqualTo((0, 0, 0)), "a GET asks recreation.gov nothing and sends nothing");
            Assert.That(M.Host.Lines, Is.Empty);
            Assert.That(Page.Body, Does.Not.Contain(Sentinel).And.Not.Contain(M.Folder).And.Not.Contain(M.Cache));
        }

        // ---- web mode starts it by itself (D83 point 2) ----

        [Test]
        public async Task WebMode_StartsTheMonitoringByItself_WhenOutputToIsAMonitoring_AndTheWebUIGoesOnWithoutOne()
        {
            //outputTo: Email and a search to check: web mode's own start starts it (WebMode.Run, as the console starts web mode)
            string Folder = WebTestHost.NewFolder();
            string Cache = Folder + "-cache";
            Directory.CreateDirectory(Cache);
            WriteCampground(Cache);
            try
            {
                File.WriteAllText(Path.Join(Folder, "RecreationDotOrg.db"), string.Empty);
                File.WriteAllText(Path.Join(Folder, "General.yaml"), "outputTo: Email\nautoRefresh: false\n");
                File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                    + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
                File.WriteAllText(Path.Join(Folder, "Alpha.yaml"), Search("First trip"));
                var Waits = new Waits();
                var Lines = new ConcurrentQueue<string>();
                var Settings = new WebHostSettings
                {
                    SettingsFolder = Folder,
                    CachePath = Cache,
                    Port = 0,
                    Line = Text => Lines.Enqueue(Text),
                    Warning = Text => Lines.Enqueue("warning: " + Text),
                    Error = (Text, Details) => Lines.Enqueue("error: " + Text),
                    PrepareCampgroundData = () => { },
                    Monitoring = new WebMonitoringSeams { Wait = Waits.Wait }
                };
                using var Stop = new CancellationTokenSource();
                var Up = new TaskCompletionSource<Microsoft.AspNetCore.Builder.WebApplication>(TaskCreationOptions.RunContinuationsAsynchronously);
                Task<int> Run = Task.Run(() => WebMode.Run(Settings, WebArguments.Parse(new[] { "--web", "--listen", "127.0.0.1" }), Stop.Token, App => Up.TrySetResult(App)));
                Microsoft.AspNetCore.Builder.WebApplication App = await Up.Task.WaitAsync(Long);
                Waits.Await();
                WebMonitoring Monitoring = (WebMonitoring)App.Services.GetService(typeof(WebMonitoring))!;
                WebMonitoringView Running = Monitoring.View();
                Assert.That((Running.Phase, Running.Status.State, Running.Status.CheckNumber), Is.EqualTo((WebMonitoringPhase.Running, MonitoringState.Waiting, 1)));
                Assert.That(Running.ThisStart!.WithWebMode, Is.True);
                Assert.That(Emails, Has.Count.EqualTo(1), "its first check notified the openings");
                var Said = System.Diagnostics.Stopwatch.StartNew();
                while (!Lines.Contains(WebMonitoring.LinePrefix + "started (Email; 1 search checked every minute).") && Said.Elapsed < Long)
                {
                    await Task.Delay(20);
                }
                Assert.That(Lines, Does.Contain(WebMonitoring.LinePrefix + "started (Email; 1 search checked every minute)."));
                Stop.Cancel();
                Assert.That(await Run.WaitAsync(Long), Is.Zero);
                Assert.That(Monitoring.View().Status.State, Is.EqualTo(MonitoringState.Stopped), "web mode's stop stops the monitoring");
                using (FileStream? Taken = MonitoringLock.TryAcquire(Folder, out _))
                {
                    Assert.That(Taken, Is.Not.Null, "and its lock is released");
                }
                List<string> All = Lines.ToList();
                Assert.That(All.IndexOf(WebMonitoring.LinePrefix + "stopped."), Is.LessThan(All.IndexOf("Web UI: stopped.")).And.Not.Negative, "the monitoring's end is said before web mode's");
                Assert.That(string.Join("\n", All), Does.Not.Contain(Sentinel).And.Not.Contain("First trip").And.Not.Contain("someone@example.invalid"), "no secret, search or recipient on the console");
            }
            finally
            {
                Directory.Delete(Folder, true);
                Directory.Delete(Cache, true);
            }

            //outputTo: Console: nothing starts, nothing is said on the console, and the page says why in the start's words
            Requested.Clear();
            await using MonitoringHost M = await MonitoringHost.Start(OutputTo: "Console");
            string Before = Snapshot(M.Folder);
            M.Monitoring.StartWithWebMode();
            using Browser Owner = await M.Host.SignedIn();
            WebPage Page = await Until(Owner, P => Element(P, "not-started") != null, "the start's words");
            Assert.That(State(Page), Is.EqualTo("Not running: it did not start when web mode started."));
            Assert.That(Said(Page, "not-started"), Is.EqualTo("Info: the monitoring is not turned on: outputTo in General.yaml is not Email or Ntfy."));
            Assert.That(M.Host.Lines, Is.Empty, "a web UI without a monitoring says nothing about it on the console");
            Assert.That(Snapshot(M.Folder), Is.EqualTo(Before), "nothing created");
            Assert.That(Requested, Is.Empty);
        }

        // ---- Start, Stop and Restart, and the moment the page showed ----

        [Test]
        public async Task StartStopAndRestart_ComeFromThePage_AndAnOutOfDateOrRepeatedPageDoesNothing()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            WebPage Idle = await Owner.Get("/monitoring");
            Assert.That(StateOf(Idle), Is.EqualTo("0-idle"));

            WebPage Starting = await Act(Owner, "start", Idle);
            Assert.That(Element(Starting, "status")!.Attribute("autofocus"), Is.Not.Null, "the status has the focus after Start");
            WebPage Running = await Waiting(Owner);
            Assert.That(StateOf(Running), Is.EqualTo("1-running"));
            Assert.That((Enabled(Running, "start"), Enabled(Running, "stop"), Enabled(Running, "restart")), Is.EqualTo((false, true, true)));
            Assert.That(Said(Running, "actions-unavailable"), Is.EqualTo(WebMonitoringWording.StartUnavailableRunning));
            Assert.That(M.LockIsHeld(), Is.True, "the monitoring holds the settings folder's lock");
            Assert.That(Said(Running, "facts"), Does.Contain("Email").And.Contain("Check 1 at "));

            //the same Start again (the back button, a page sent twice): out of date, nothing started
            WebPage Again = await Act(Owner, "start", Idle);
            Assert.That(Said(Again, "page-notice"), Is.EqualTo("Warning: " + WebMonitoringWording.StaleText));
            Assert.That(Element(Again, "page-notice")!.Attribute("autofocus"), Is.Not.Null);
            Assert.That(M.Monitoring.View().Generation, Is.EqualTo(1), "no second start");
            //a Stop of a page that showed another moment (here: before the start) stops nothing
            await Act(Owner, "stop", Idle);
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.True, "an out-of-date Stop stops nothing");
            //a made-up moment, a missing one
            await Act(Owner, "stop", Running, State: "2-running");
            await Act(Owner, "stop", Running, State: "");
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.True);

            //a search file changes: the page says so, and Restart reads the files again (a new start)
            WriteSearch(M, "Bravo.yaml", "Second trip");
            WebPage Changed = await Owner.Get("/monitoring");
            Assert.That(Said(Changed, "files-changed"), Is.EqualTo("Warning: " + WebMonitoringWording.FilesChanged));
            await Act(Owner, "restart", Changed);
            M.Waits.Await();
            WebPage Restarted = await Waiting(Owner);
            Assert.That(StateOf(Restarted), Is.EqualTo("2-running"), "a restart is a new start");
            Assert.That(Element(Restarted, "files-changed"), Is.Null);
            Assert.That(Said(Restarted, "facts"), Does.Contain("Check 1 at "), "every start counts its checks from 1");
            Assert.That(M.Monitoring.View().Status.Searches, Is.EqualTo(2));
            //a Stop of the page before the restart can't stop the new monitoring
            await Act(Owner, "stop", Running);
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.True, "a Stop of an earlier start stops nothing");

            WebPage Stopped = await Act(Owner, "stop", Restarted);
            Stopped = await NotRunning(Owner);
            Assert.That(State(Stopped), Does.StartWith("Not running: it was stopped at ").And.EndWith("It does not start again by itself: select Start to start it again."));
            Assert.That((Enabled(Stopped, "start"), Enabled(Stopped, "stop"), Enabled(Stopped, "restart")), Is.EqualTo((true, false, false)));
            Assert.That(M.LockIsHeld(), Is.False, "the lock is released");
            //a Stop sent again: out of date
            WebPage StopAgain = await Act(Owner, "stop", Restarted);
            Assert.That(Said(StopAgain, "page-notice"), Is.EqualTo("Warning: " + WebMonitoringWording.StaleText));
            await Task.Delay(200);
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.False, "nothing starts it again by itself");

            Assert.That(M.Host.Lines, Is.EqualTo(new[]
            {
                WebMonitoring.LinePrefix + "started (Email; 1 search checked every minute).",
                WebMonitoring.LinePrefix + "stopped.",
                WebMonitoring.LinePrefix + "started (Email; 2 searches checked every minute).",
                WebMonitoring.LinePrefix + "stopped."
            }));
        }

        [Test]
        public async Task TheMonitoring_IsTheWebUIs_ASessionThatEndsStopsNothing_AndEverySessionSeesIt()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser First = await M.Host.SignedIn();
            using Browser Second = await M.Host.SignedIn();
            await Act(First, "start", await First.Get("/monitoring"));
            M.Waits.Await();
            WebPage Seen = await Waiting(Second);
            Assert.That(StateOf(Seen), Is.EqualTo("1-running"), "the other session sees the same monitoring");

            //the starting session signs out: the monitoring goes on
            WebPage SignOut = await First.Post("/signout");
            Assert.That(SignOut.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That((await First.Get("/monitoring")).Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.True, "a sign-out stops nothing");
            //every session ends (the idle time): the monitoring goes on
            M.Host.Now = M.Host.Now.AddHours(9);
            Assert.That((await Second.Get("/monitoring")).Status, Is.EqualTo(HttpStatusCode.Redirect), "the session ended");
            Assert.That(M.Monitoring.View().Status.IsRunning, Is.True, "a session's end stops nothing");
            Assert.That(M.LockIsHeld(), Is.True);
            //a new session sees it running, and stops it
            using Browser Third = await M.Host.SignedIn();
            WebPage Page = await Owner(Third);
            Assert.That(StateOf(Page), Is.EqualTo("1-running"));
            await Act(Third, "stop", Page);
            await NotRunning(Third);

            static Task<WebPage> Owner(Browser B) => B.Get("/monitoring");
        }

        // a page that is being written (a slow browser) holds nothing the monitoring needs: another session stops the
        // monitoring, and its end is recorded, while the page waits
        [Test]
        public async Task APageBeingWritten_HoldsNothingTheMonitoringNeeds()
        {
            var Hold = new ManualResetEventSlim(false);
            var InPage = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            await using MonitoringHost M = await MonitoringHost.Start(Rendering: () =>
            {
                if (Hold.IsSet)
                {
                    InPage.Set();
                    Release.Wait(Long);
                }
            });
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Slow = await M.Host.SignedIn();
            using Browser Other = await M.Host.SignedIn();
            await Act(Other, "start", await Other.Get("/monitoring"));
            M.Waits.Await();
            WebPage Running = await Waiting(Other);
            Hold.Set();
            Task<WebPage> Writing = Slow.Get("/monitoring");
            try
            {
                Assert.That(InPage.Wait(Long), Is.True, "the slow page is being written");
                Hold.Reset();
                Task<WebPage> Stop = Other.Post("/monitoring?handler=stop", ("state", StateOf(Running)));
                Assert.That(await Task.WhenAny(Stop, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(Stop), "another session's Stop is not held by the page");
                var Clock = System.Diagnostics.Stopwatch.StartNew();
                WebMonitoringView? Ended = null;
                while (Clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Task<WebMonitoringView> Look = Task.Run(() => M.Monitoring.View());
                    Assert.That(await Task.WhenAny(Look, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(Look), "the monitoring's view is not held by the page");
                    if (Look.Result.ThisEnd != null && Look.Result.Busy == WebMonitoringBusy.None)
                    {
                        Ended = Look.Result;
                        break;
                    }
                    await Task.Delay(20);
                }
                Assert.That(Ended, Is.Not.Null, "the monitoring stopped and its end was recorded while the page was being written");
                Assert.That(M.LockIsHeld(), Is.False);
            }
            finally
            {
                Release.Set();
            }
            Assert.That((await Writing).Status, Is.EqualTo(HttpStatusCode.OK));
        }

        // the page takes no folder, file, path or search from the browser: a start reads the web UI's own settings folder
        [Test]
        public async Task ThePage_TakesNoFolderFileOrSearchFromTheBrowser()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            string Other = WebTestHost.NewFolder();
            try
            {
                File.WriteAllText(Path.Join(Other, "General.yaml"), "outputTo: Email\nautoRefresh: false\n");
                File.Copy(M.In("Email.yaml"), Path.Join(Other, "Email.yaml"));
                File.WriteAllText(Path.Join(Other, "Evil.yaml"), Search("Evil trip"));
                File.WriteAllText(Path.Join(Other, "Second.yaml"), Search("Second evil trip"));
                string Before = Snapshot(Other);
                using Browser Owner = await M.Host.SignedIn();
                WebPage Page = await Owner.Get("/monitoring");
                WebPage Posted = await Owner.Post("/monitoring?handler=start", ("state", StateOf(Page)), ("folder", Other), ("settingsFolder", Other), ("configPath", Other),
                    ("file", "../" + Path.GetFileName(Other) + "/Evil.yaml"), ("searches", "Evil.yaml"), ("search", Search("Injected trip")), ("outputTo", "Ntfy"));
                Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect));
                M.Waits.Await();
                await Waiting(Owner);
                WebMonitoringView View = M.Monitoring.View();
                Assert.That((View.Status.Searches, View.ThisStart!.Output), Is.EqualTo((1, "Email")), "the web UI's own one search, its own outputTo");
                Assert.That(View.LastCheck!.Searches.Select(S => S.Title), Is.EqualTo(new[] { "First trip" }));
                Assert.That(M.LockIsHeld(), Is.True, "the lock of the web UI's own folder");
                Assert.That(Snapshot(Other), Is.EqualTo(Before), "the other folder untouched: no lock file, no Camp.History");
            }
            finally
            {
                Directory.Delete(Other, true);
            }
        }

        // ---- the start's checks and the monitoring lock ----

        [Test]
        public async Task TheStartsChecks_AreEmailAndNtfyModes_InTheirWords_AndCreateNothing()
        {
            await using MonitoringHost M = await MonitoringHost.Start(OutputTo: null, WithEmail: false);
            using Browser Owner = await M.Host.SignedIn();
            async Task<string> Refused()
            {
                WebPage Page = await Act(Owner, "start", await Owner.Get("/monitoring"));
                Page = await Until(Owner, P => Element(P, "not-started") != null, "the start's words");
                Assert.That(State(Page), Is.EqualTo("Not running: the last start did not start it."));
                Assert.That(Enabled(Page, "start"), Is.True, "Start again, once it is fixed");
                return Said(Page, "not-started")!;
            }
            string Before = Snapshot(M.Folder);

            //no General.yaml: no monitoring is turned on (and General.yaml is not created)
            Assert.That(await Refused(), Is.EqualTo("Info: the monitoring is not turned on: outputTo in General.yaml is not Email or Ntfy."));
            //a General.yaml that is no valid YAML: its name and position, before the mode is known
            File.WriteAllText(M.In("General.yaml"), "outputTo: [Email\n");
            Assert.That(await Refused(), Does.StartWith("Error: the monitoring can't start: General.yaml can't be read: it is not valid YAML (line "));
            //outputTo: Email without Email.yaml (which is not created either)
            File.WriteAllText(M.In("General.yaml"), "outputTo: Email\nautoRefresh: false\n");
            Before = Snapshot(M.Folder);
            Assert.That(await Refused(), Does.StartWith("Error: email mode can't start: Email.yaml does not exist yet."));
            //an Email.yaml the start refuses: the validation's value-free words
            File.WriteAllText(M.In("Email.yaml"), "smtpServer: ''\nsmtpPort: 587\nsmtpPassword: " + Sentinel + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
            Before = Snapshot(M.Folder);
            string Email = await Refused();
            Assert.That(Email, Does.StartWith("Error: email mode can't start: Email.yaml can't be used: ").And.Contain("smtpServer"));
            Assert.That(Email, Does.Not.Contain(Sentinel).And.Not.Contain("someone@example.invalid").And.Not.Contain(M.Folder));
            //a password file that can't be used: the start's own words, no fallback to smtpPassword
            File.WriteAllText(M.In("Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                + "\nsmtpPasswordFile: " + M.In("missing-password-file") + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
            Before = Snapshot(M.Folder);
            string Password = await Refused();
            Assert.That(Password, Does.StartWith("Error: email mode can't start: the SMTP password can't be used: "));
            Assert.That(Password, Does.Not.Contain(Sentinel).And.Not.Contain(M.Folder));
            //no search file: D64's gate, in its words; no example search is created (D65)
            File.WriteAllText(M.In("Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
            Before = Snapshot(M.Folder);
            Assert.That(await Refused(), Does.StartWith("Error: email mode has no search to check, so it stopped: the settings folder has no search file."));
            //a search file that is not Enabled (autoRun)
            WriteSearch(M, "Alpha.yaml", "First trip", AutoRun: false);
            Before = Snapshot(M.Folder);
            Assert.That(await Refused(), Does.StartWith("Error: email mode has no search to check, so it stopped: the search file is not Enabled (autoRun)."));
            Assert.That(Snapshot(M.Folder), Is.EqualTo(Before), "a refused start creates nothing: no lock file, no settings file, no example search");
            //outputTo: Ntfy without Ntfy.yaml
            File.WriteAllText(M.In("General.yaml"), "outputTo: Ntfy\nautoRefresh: false\n");
            WriteSearch(M, "Alpha.yaml", "First trip");
            Assert.That(await Refused(), Does.StartWith("Error: ntfy mode can't start: Ntfy.yaml can't be used: "));
            Assert.That((Requested.Count, Emails.Count, Published.Count), Is.EqualTo((0, 0, 0)), "nothing searched or sent");
            Assert.That(File.Exists(M.In(MonitoringLock.FileName)), Is.False, "the lock file is created only by a start that passed every other check");
            Assert.That(M.Host.Lines.Where(L => L.StartsWith(WebMonitoring.LinePrefix, StringComparison.Ordinal)), Has.All.StartWith(WebMonitoring.LinePrefix + "not started: "));
            Assert.That(string.Join("\n", M.Host.Lines), Does.Not.Contain(Sentinel).And.Not.Contain(M.Folder));
        }

        [Test]
        public async Task OneMonitoring_ForASettingsFolder_AnotherOnesLockRefusesTheStart()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            using (FileStream? Other = MonitoringLock.TryAcquire(M.Folder, out _))
            {
                Assert.That(Other, Is.Not.Null);
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                WebPage Page = await Until(Owner, P => Element(P, "not-started") != null, "the lock's words");
                Assert.That(Said(Page, "not-started"), Does.StartWith("Error: email mode can't start: another Campertron is already monitoring this settings folder."));
                Assert.That(Requested, Is.Empty, "nothing searched");
            }
            //the other one has ended: the start goes through
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            Assert.That(M.LockIsHeld(), Is.True);
            //a second monitoring of the same folder, the console's or another web UI's, is refused while this one runs
            var Second = new MonitoringService();
            MonitoringStartResult Refused = Second.Start(new CampertronInternalConfig
            {
                ConfigPath = M.Folder,
                CachePath = M.Cache,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.Email },
                EmailConfig = Yaml.EmailConfigGetExisting(M.Folder)
            });
            Assert.That(Refused.Problem, Is.EqualTo(MonitoringStartProblem.LockHeld));
        }

        // ---- a check, its notifications, Camp.History and the send-failure rule ----

        [Test]
        public async Task ACheck_NotifiesItsNewOpeningsOnce_RecordsThemInCampHistory_AndThePageShowsThem()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage Page = await Waiting(Owner);

            Assert.That(Emails, Has.Count.EqualTo(1), "the first check notified its new openings");
            int Markers = M.Markers().Count;
            Assert.That(Markers, Is.GreaterThan(0), "the notified openings are in Camp.History");
            WebMonitoringCheck First = M.Monitoring.View().LastCheck!;
            Assert.That(Said(Page, "check-summary"), Does.StartWith("Check 1 at ").And.EndWith(": 1 search, " + First.NewOpenings + " new openings; 1 of 1 email sent."));
            Assert.That(First.Searches.Single().Openings, Is.EqualTo(Markers), "the page shows what was notified");
            Assert.That(Page.Text(), Does.Contain(WebMonitoringWording.LastCheckNote));
            Assert.That(Said(Page, "shown-heading"), Is.EqualTo("First trip"));
            Assert.That(Page.Text(), Does.Contain("Booking page of Site 001").And.Contain("PROBE CAMPGROUND"));
            foreach (XElement Link in Page.Xml().Descendants("a").Where(A => A.Value.StartsWith("Booking page of ", StringComparison.Ordinal)))
            {
                Assert.That((string?)Link.Attribute("href"), Does.Match("^https://www\\.recreation\\.gov/camping/campsites/99000010[12]$"));
            }

            //the next check: the same openings are in Camp.History, so nothing is new, sent or shown again
            M.Waits.Next();
            M.Waits.Await();
            Page = await Until(Owner, P => (Said(P, "check-summary") ?? "").StartsWith("Check 2 at ", StringComparison.Ordinal), "the second check");
            Assert.That(Said(Page, "check-summary"), Does.EndWith(": 1 search, nothing new to notify."));
            Assert.That(Said(Page, "empty"), Is.EqualTo(WebMonitoringWording.NoNewOpening));
            Assert.That(Page.Text(), Does.Not.Contain("Booking page of "));
            Assert.That((Emails.Count, M.Markers().Count), Is.EqualTo((1, Markers)), "no duplicate notification, no new marker");
            Assert.That(Said(Page, "facts"), Does.Contain("Check 2 at "));
            await Act(Owner, "stop", Page);
            await NotRunning(Owner);
            Assert.That(M.Markers().Count, Is.EqualTo(Markers), "a stop writes nothing");
        }

        [Test]
        public async Task NtfyMode_PublishesThroughTheMonitoringsOwnTransport_AndSaysSo()
        {
            await using MonitoringHost M = await MonitoringHost.Start(OutputTo: "Ntfy", WithEmail: false);
            File.WriteAllText(M.In("Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: monitoring-page-topic-7f3a\n");
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage Page = await Waiting(Owner);
            Assert.That(Published, Has.Count.EqualTo(1), "one ntfy notification for the search with openings");
            Assert.That(Emails, Is.Empty, "no email in ntfy mode");
            Assert.That(M.Markers(), Is.Not.Empty);
            Assert.That(Said(Page, "facts"), Does.Contain("Ntfy"));
            Assert.That(Said(Page, "check-summary"), Does.EndWith("; 1 of 1 ntfy notification sent."));
            Assert.That(Page.Body, Does.Not.Contain("monitoring-page-topic-7f3a"), "the topic is shown only in Ntfy.yaml's editor");
            Assert.That(string.Join("\n", M.Host.Lines), Does.Contain(WebMonitoring.LinePrefix + "started (Ntfy; 1 search checked every minute).").And.Not.Contain("monitoring-page-topic-7f3a"));
        }

        [Test]
        public async Task AFailedSend_StopsTheMonitoring_InFixedWords_RecordsNothingUndelivered_AndNothingStartsItAgain()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            Send = Name => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, ServerText);
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            WebPage Page = await Until(Owner, P => Element(P, "failure") != null, "the failure");

            Assert.That(State(Page), Does.StartWith("Not running: it stopped at ").And.EndWith(" because a check failed. It does not start again by itself."));
            string Failure = Said(Page, "failure")!;
            Assert.That(Failure, Does.StartWith("Error: the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(Failure, Does.Contain("Check sendFromAddress and sendToAddressList in Email.yaml.")
                .And.Contain("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.")
                .And.Contain("The monitoring does not start again by itself"));
            Assert.That(Page.Body, Does.Not.Contain("s3cret-server-answer").And.Not.Contain("someone@example.invalid").And.Not.Contain(Sentinel).And.Not.Contain(M.Folder)
                .And.Not.Contain("SmtpException").And.Not.Contain("   at "));
            Assert.That(M.Markers(), Is.Empty, "what was not delivered is not recorded");
            Assert.That(M.LockIsHeld(), Is.False, "the lock is released");
            Assert.That(Enabled(Page, "start"), Is.True);
            //the web UI goes on, and nothing starts the monitoring again
            int Asked = Requested.Count;
            await Task.Delay(300);
            Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "the web UI goes on");
            Assert.That((M.Monitoring.View().Generation, Requested.Count, Emails.Count), Is.EqualTo((1L, Asked, 1)), "no automatic restart");
            Assert.That(M.Host.Lines.Last(), Is.EqualTo(WebMonitoring.LinePrefix + "stopped because a check failed: the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(string.Join("\n", M.Host.Lines.Concat(M.Host.Warnings)), Does.Not.Contain("s3cret-server-answer"));

            //fixed, then started again: the openings that were not delivered come again, and are recorded once delivered
            Send = Name => { };
            await Act(Owner, "start", Page);
            M.Waits.Await();
            Assert.That(Emails, Has.Count.EqualTo(2));
            Assert.That(M.Markers(), Is.Not.Empty);
        }

        [Test]
        public async Task ABlockedCheck_StopsTheMonitoring_AndHoldsStartHereAndOnTheResultsPageForFiveMinutes()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            Answer = Blocked;
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            WebPage Page = await Until(Owner, P => Element(P, "failure") != null, "the block");
            Assert.That(Said(Page, "failure"), Does.StartWith("Error: recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it."));
            Assert.That(Said(Page, "cooldown"), Is.EqualTo("recreation.gov limited Campertron's requests. Start is available again in 5:00."));
            Assert.That(Enabled(Page, "start"), Is.False, "Start waits five minutes");
            WebPage Refused = await Act(Owner, "start", Page);
            Assert.That(Said(Refused, "page-notice"), Does.StartWith("Warning: Nothing was started: recreation.gov limited"));
            Assert.That(M.Monitoring.View().Generation, Is.EqualTo(1));
            Assert.That(Emails, Is.Empty);
            Assert.That(M.Markers(), Is.Empty);
            //the results page waits too: recreation.gov limits the computer
            WebPage Results = await Owner.Get("/results");
            Assert.That(Said(Results, "cooldown"), Is.EqualTo("recreation.gov limited Campertron's requests. Start is available again in 5:00."));
            M.Time.Advance(TimeSpan.FromMinutes(5));
            Assert.That(Enabled(await Owner.Get("/monitoring"), "start"), Is.True, "after five minutes");
        }

        [Test]
        public async Task AStop_WhileACheckSends_LetsItFinishItsNotificationsAndRecords()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            WriteSearch(M, "Bravo.yaml", "Second trip");
            var InSend = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                InSend.Set();
                Assert.That(Release.Wait(Long), Is.True);
            };
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            Assert.That(InSend.Wait(Long), Is.True);
            WebPage Sending = await Owner.Get("/monitoring");
            Assert.That(State(Sending), Is.EqualTo("Running: check 1 is sending its notifications."));
            WebPage Stopping = await Act(Owner, "stop", Sending);
            Assert.That(State(Stopping), Does.StartWith("Stopping: "));
            Assert.That((Enabled(Stopping, "start"), Enabled(Stopping, "stop"), Enabled(Stopping, "restart")), Is.EqualTo((false, false, false)));
            Assert.That(Said(Stopping, "actions-unavailable"), Is.EqualTo(WebMonitoringWording.UnavailableBusy));
            Release.Set();
            WebPage Stopped = await NotRunning(Owner);
            Assert.That(Emails, Has.Count.EqualTo(2), "every notification of the check was sent");
            Assert.That(M.Markers(), Is.Not.Empty, "and recorded");
            Assert.That(Said(Stopped, "check-summary"), Does.EndWith("; 2 of 2 emails sent."));
        }

        // ---- the last check's results, and the results page's own ----

        [Test]
        public async Task TheLastCheck_IsOneImmutableSnapshot_APageAtATime_SeparateFromTheResultsPagesRun()
        {
            string[] Was = Sites;
            Sites = Enumerable.Range(1, 30).Select(N => "99000" + N.ToString("0000", CultureInfo.InvariantCulture)).ToArray();
            try
            {
                await using MonitoringHost M = await MonitoringHost.Start(Run: Returns(Result("Alpha.yaml", new[] { Opening("777", new DateTime(2026, 12, 1)) }, Name: "First trip")));
                //the fixture's campsites as the cache knows them
                File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-Campsites.json"), System.Text.Json.JsonSerializer.Serialize(Sites.Select(Site => new CampsitesRecdata
                {
                    CampsiteID = Site, FacilityID = CampgroundId, CampsiteName = "Site " + Site.Substring(5), CampsiteType = "STANDARD NONELECTRIC", Loop = "A", TypeOfUse = "Overnight"
                }).ToList()));
                File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-CampsitesByPark.json"), System.Text.Json.JsonSerializer.Serialize(Sites.ToList()));
                foreach (string Site in Sites)
                {
                    File.WriteAllText(Path.Join(M.Cache, Site + "-Equipment.json"), "[\"Tent\"]");
                    File.WriteAllText(Path.Join(M.Cache, Site + "-Attributes.json"), "[]");
                }
                WriteSearch(M, "Alpha.yaml", "First trip");
                using Browser Owner = await M.Host.SignedIn();

                //the results page's manual run first: its results are the session's
                WebPage Manual = await ResultsPageTests.Act(Owner, "/results?handler=start", ResultsPageTests.Fields(await Owner.Get("/results")));
                Manual = await ResultsPageTests.Ended(Owner);
                Assert.That(Manual.Text(), Does.Contain("Booking page of Site 777"));

                await Act(Owner, "start", await Owner.Get("/monitoring"));
                M.Waits.Await();
                WebPage Page = await Waiting(Owner);
                WebMonitoringCheck Check = M.Monitoring.View().LastCheck!;
                Assert.That(Check.Number, Is.EqualTo(1));
                int Openings = Check.Searches.Single().Openings;
                Assert.That(Openings, Is.GreaterThan(ResultsListing.PageSize), "more than a page: " + Requested.Count + " requests, " + string.Join(", ", Check.Searches.Select(S => S.CountsText)));
                Assert.That(Said(Page, "rows-range"), Is.EqualTo("Showing openings 1 to 200 of " + Openings.ToString("N0", CultureInfo.InvariantCulture) + "."));
                Assert.That(Page.Xml().Descendants("a").Count(A => A.Value.StartsWith("Booking page of ", StringComparison.Ordinal)), Is.EqualTo(200), "a page of 200 rows");
                WebPage Second = await Owner.Get("/monitoring?run=1&check=1&search=0&part=2");
                Assert.That(Said(Second, "rows-range"), Does.StartWith("Showing openings 201 to "));
                Assert.That(Second.Xml().Descendants("a").Count(A => A.Value.StartsWith("Booking page of ", StringComparison.Ordinal)), Is.EqualTo(Openings - 200));
                //the snapshot does not change while it is shown
                Assert.That(M.Monitoring.View().LastCheck, Is.SameAs(Check));

                //the results page still shows its own run, and the monitoring page only the monitoring's check
                WebPage ManualAgain = await Owner.Get("/results");
                Assert.That(ManualAgain.Text(), Does.Contain("Booking page of Site 777").And.Not.Contain("Booking page of Site 0001"));
                Assert.That(Page.Text(), Does.Not.Contain("Booking page of Site 777"));

                //the next check replaces it: no history of checks is kept, and an earlier check's address shows the last one
                M.Waits.Next();
                M.Waits.Await();
                WebPage Later = await Until(Owner, P => (Said(P, "check-summary") ?? "").StartsWith("Check 2 at ", StringComparison.Ordinal), "the second check");
                Assert.That(M.Monitoring.View().LastCheck!.Number, Is.EqualTo(2));
                WebPage Earlier = await Owner.Get("/monitoring?run=1&check=1&search=0&part=2");
                Assert.That((Said(Earlier, "check-summary") ?? "").StartsWith("Check 2 at ", StringComparison.Ordinal), Is.True, "only the last check is kept");
                Assert.That(Element(Earlier, "rows-range"), Is.Null);
                Assert.That(Said(Earlier, "empty"), Is.EqualTo(WebMonitoringWording.NoNewOpening));
                Assert.That(Earlier.Text(), Does.Not.Contain("Booking page of "));

                await Act(Owner, "stop", Later);
                await NotRunning(Owner);
                //a new start is a new monitoring: the previous one's check is not its
                Answer = Nothing;
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                M.Waits.Await();
                WebPage Fresh = await Waiting(Owner);
                Assert.That(StateOf(Fresh), Is.EqualTo("2-running"));
                Assert.That(Said(Fresh, "check-summary"), Does.StartWith("Check 1 at ").And.EndWith("nothing new to notify."));
                Assert.That(Said(Fresh, "empty"), Is.EqualTo(WebMonitoringWording.NoNewOpening));
                Assert.That((await Owner.Get("/results")).Text(), Does.Contain("Booking page of Site 777"), "a monitoring's start leaves the manual run's results as they are");
            }
            finally
            {
                Sites = Was;
            }
        }

        [Test]
        public async Task UntrustedText_IsShownAsText()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-Campsites.json"), System.Text.Json.JsonSerializer.Serialize(Campsites.Select((Site, Index) => new CampsitesRecdata
            {
                CampsiteID = Site, FacilityID = CampgroundId, CampsiteName = "<script>alert(1)</script> " + Index, CampsiteType = "\"><b>x</b>", Loop = "A", TypeOfUse = "Overnight"
            }).ToList()));
            File.WriteAllText(M.In("Alpha.yaml"), Search("<img src=x onerror=alert(1)> & <b>trip</b>"));
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage Page = await Waiting(Owner);
            Assert.That(Page.Text(), Does.Contain("<script>alert(1)</script>").And.Contain("<img src=x onerror=alert(1)> & <b>trip</b>"), "shown as text");
            Assert.That(Page.Xml().Descendants().Where(E => E.Name == "script" || E.Name == "img" || E.Name == "b"), Is.Empty);
        }

        // ---- the page's POSTs ----

        [Test]
        public async Task TheMonitoringPagesPosts_NeedTheirTokenOriginAndAKnownHandler_AndAreRateLimited()
        {
            await using MonitoringHost M = await MonitoringHost.Start(OutputTo: "Console");
            using Browser Owner = await M.Host.SignedIn();
            WebPage Page = await Owner.Get("/monitoring");
            string State = StateOf(Page);
            WebPage NoToken = await Owner.Post("/monitoring?handler=start", ("state", State), ("__RequestVerificationToken", "made-up"));
            Assert.That(NoToken.Status, Is.EqualTo(HttpStatusCode.BadRequest), "the antiforgery token");
            Owner.Origin = "http://attacker.example";
            Assert.That((await Owner.Post("/monitoring?handler=start", ("state", State))).Status, Is.EqualTo(HttpStatusCode.Forbidden), "the Origin check");
            Owner.Origin = M.Host.Root.GetLeftPart(UriPartial.Authority);
            Assert.That((await Owner.Post("/monitoring", ("state", State))).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), "no action without its handler");
            Assert.That((await Owner.Post("/monitoring?handler=delete", ("state", State))).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), "only Start, Stop and Restart");
            Assert.That(M.Monitoring.View().Generation, Is.Zero, "nothing started");
            //Start, Stop and Restart together: ten a minute from a client (the refused ones above were counted too)
            int Accepted = 0;
            HttpStatusCode Last = HttpStatusCode.OK;
            for (int i = 0; i < CampertronWebHost.MonitoringActionsPerClient + 2; i++)
            {
                await Owner.Get("/monitoring");
                WebPage Post = await Owner.Post("/monitoring?handler=" + (i % 2 == 0 ? "stop" : "restart"), ("state", "0-idle"));
                Last = Post.Status;
                Accepted += Post.Status == HttpStatusCode.Redirect ? 1 : 0;
            }
            Assert.That(Last, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Accepted, Is.LessThan(CampertronWebHost.MonitoringActionsPerClient));
        }

        // ---- one monitoring path ----

        [Test]
        public void TheMonitoringPagesCode_RunsOnlyTheMonitoringService_AndNoOtherSearchNotificationSecretOrHistoryPath()
        {
            List<string> Members = IlReferences.ReferencedMembers(typeof(WebMonitoring), typeof(MonitoringModel), typeof(WebMonitoringWording)).Select(M => M.Member).Distinct().ToList();
            Assert.That(Members, Does.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::Start")
                .And.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::Stop")
                .And.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::GetStatus"));
            string[] Forbidden =
            {
                "::Deliver", "::RecordNotificationHistory", "::HasUnreportedEntries", "SmtpPassword::ResolveOnce", "NtfyToken::ResolveOnce", "NtfyPublisher::Publish",
                "CampsiteConfig::SendSearchEmail", "CampsiteConfig::get_SendResultsEmail", "Notifications::get_PublishNtfy", "AvailabilityApi::", "MonitoringLock::TryAcquire",
                "CampsiteSearch::Run", "CampsiteSearch::Plan", "MonitoringService::ReportCheck", "Load::LoadMonitoringSearches", "Load::PrepareRun",
                "TestNotification::Send", "Yaml::GeneralConfigGetConfig", "Yaml::EmailConfigGetConfig", "ConfigFileReplace::", "File::Write", "File::Delete"
            };
            foreach (string Each in Forbidden)
            {
                Assert.That(Members.Where(M => M.Contains(Each, StringComparison.Ordinal)), Is.Empty, Each);
            }
            //one monitoring service in a web UI: made once, by its WebMonitoring
            List<string> Made = IlReferences.ReferencedMembers(typeof(WebMonitoring)).Select(M => M.Member).Where(M => M == "CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::.ctor").ToList();
            Assert.That(Made, Has.Count.EqualTo(1));
            //a session's end, the workspaces and the other pages never reach the monitoring
            List<string> Others = IlReferences.ReferencedMembers(typeof(WebSettingsWorkspaces), typeof(WebResults), typeof(WebResultsRuns), typeof(ResultsModel),
                typeof(SearchesModel), typeof(SettingsModel), typeof(SignOutModel), typeof(WebSessions)).Select(M => M.Member).ToList();
            Assert.That(Others.Where(M => M.Contains("WebMonitoring::", StringComparison.Ordinal) || M.Contains("MonitoringService::", StringComparison.Ordinal)), Is.Empty);
        }
    }
}
