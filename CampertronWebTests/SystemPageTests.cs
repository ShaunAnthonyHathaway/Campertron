using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronWeb.Pages;
using static CampertronWebTests.MonitoringPageTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The web UI's system page and its refresh of the campground data (v0.3.0's PR 9, D99; business-rules-settings §36) on
    // the real host: CampertronGUI's Settings > System as a page (the current configuration with the folders in words, the
    // campground data's card), the library's RidbRefreshService behind a seam the test drives step by step (it reports the
    // service's stages, reads the token only at the service's checkpoints and never after the cache step, and returns the
    // service's typed results), so nothing reaches the network and no database is opened. A GET only looks; Start refresh
    // and Cancel are POSTs with the antiforgery token and the moment the page showed, then a redirect; the refresh is the
    // web UI's, never a session's; one data operation at a time with the results page; the monitoring paused for its time.
    public class SystemPageTests
    {
        internal static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

        // the monitoring's recreation.gov, email and ntfy as the monitoring page's tests answer them (process-wide seams, set
        // and restored around each test), so a monitoring the refresh pauses reaches nothing
        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        internal const string ServerText = "s3cret-server-answer from ridb at /var/lib/campertron/secret-path";

        // The refresh service as the test drives it: the stages it reports, where it waits, what it returns
        internal sealed class FakeRefresh
        {
            internal readonly SemaphoreSlim Downloading = new(0);
            internal readonly SemaphoreSlim Installing = new(0);
            internal readonly ManualResetEventSlim LetDownload = new(false);
            internal readonly ManualResetEventSlim LetInstall = new(false);
            private int _Calls;
            internal int Calls => Volatile.Read(ref _Calls);
            internal RidbRefreshRequest? Request;
            internal CancellationToken Token;
            // what the refresh ends with once it has installed, or before the installation (Before)
            internal Func<RidbRefreshResult>? Before;
            internal RidbRefreshOutcome After = RidbRefreshOutcome.Refreshed;
            // called when the service starts, on its thread (the test looks at the web UI then)
            internal Action? OnStart;

            internal RidbRefreshResult Run(RidbRefreshRequest Request, IProgress<RidbRefreshProgress>? Progress, CancellationToken Token)
            {
                Interlocked.Increment(ref _Calls);
                this.Request = Request;
                this.Token = Token;
                OnStart?.Invoke();
                Progress!.Report(new RidbRefreshProgress(1, RidbRefreshStage.AcquiringLock, true, null, null));
                Progress.Report(new RidbRefreshProgress(2, RidbRefreshStage.Downloading, true, 120_400_000, 570_000_000));
                Downloading.Release();
                WaitHandle.WaitAny(new[] { Token.WaitHandle, LetDownload.WaitHandle }, Long);
                //a checkpoint: the token is read until the cache step
                if (Token.IsCancellationRequested)
                {
                    return Result(RidbRefreshOutcome.Canceled, RidbRefreshStage.Downloading, false, null, true);
                }
                if (Before != null)
                {
                    return Before();
                }
                Progress.Report(new RidbRefreshProgress(3, RidbRefreshStage.ClearingCache, false, null, null));
                Progress.Report(new RidbRefreshProgress(4, RidbRefreshStage.Installing, false, null, null));
                Installing.Release();
                //from the cache step on the token is never read: the refresh finishes
                Assert.That(LetInstall.Wait(Long), Is.True, "the test lets the installation end");
                return Result(After, After == RidbRefreshOutcome.Refreshed ? RidbRefreshStage.Completed : RidbRefreshStage.CheckingDatabase, true, null, Token.IsCancellationRequested);
            }

            internal static RidbRefreshResult Result(RidbRefreshOutcome Outcome, RidbRefreshStage Stage, bool Installed, Exception? Error, bool CancelRequested = false) =>
                new RidbRefreshResult(Outcome, Stage, Installed, Installed ? new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc) : null, TimeSpan.FromSeconds(42),
                    RidbRefreshResult.NoProblems, null, Error, CancelRequested);

            internal void Await(SemaphoreSlim Stage, string What) => Assert.That(Stage.Wait(Long), Is.True, What);
        }

        // a web UI with a cache folder of its own and the refresh as the test drives it
        internal sealed class SystemHost : IAsyncDisposable
        {
            internal WebTestHost Host { get; private set; } = null!;
            internal FakeRefresh Fake { get; } = new FakeRefresh();
            internal ManualTime Time { get; } = new ManualTime();
            internal string Cache { get; private set; } = string.Empty;
            internal string Folder => Host.Folder;
            internal string In(string Name) => Path.Join(Host.Folder, Name);

            internal static async Task<SystemHost> Start(bool WithPassword = true, string? WebYaml = null, bool WithCache = true, bool Container = false,
                Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>? Run = null)
            {
                var S = new SystemHost();
                S.Host = await WebTestHost.Start(WebYaml: WebYaml, WithPassword: WithPassword, Configure: Settings =>
                {
                    if (WithCache)
                    {
                        S.Cache = Settings.SettingsFolder + "-cache";
                        Directory.CreateDirectory(S.Cache);
                        Settings.CachePath = S.Cache;
                    }
                    Settings.ContainerMode = Container;
                    Settings.PrepareCampgroundData = () => { };
                    Settings.Campgrounds = () => new List<CampgroundPickerEntry>();
                    Settings.Results = new WebResultsSeams { Time = S.Time, Run = Run ?? Returns() };
                    Settings.Refresh = new WebRefreshSeams { Run = S.Fake.Run, Time = S.Time, LastRefreshed = Folder => CurrentConfiguration.LastRefreshed(Folder, new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc), System.Globalization.CultureInfo.InvariantCulture) };
                });
                File.WriteAllText(S.In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00.0000000Z\nautoRefresh: false\n");
                return S;
            }

            public async ValueTask DisposeAsync()
            {
                Fake.LetDownload.Set();
                Fake.LetInstall.Set();
                await Host.DisposeAsync();
                if (Cache.Length > 0 && Directory.Exists(Cache))
                {
                    Directory.Delete(Cache, true);
                }
            }
        }

        // ---- the page ----

        internal const string Address = "/settings/system";
        internal static XElement? StartForm(WebPage Page) => Page.Xml().Descendants("form").SingleOrDefault(F => (string?)F.Attribute("action") == "/settings/system?handler=start");
        internal static XElement? CancelForm(WebPage Page) => Page.Xml().Descendants("form").SingleOrDefault(F => (string?)F.Attribute("action") == "/settings/system?handler=cancel");
        internal static XElement RequestForm(WebPage Page) => Page.Xml().Descendants("form").Single(F => (string?)F.Attribute("method") == "get" && (string?)F.Attribute("action") == Address);
        internal static string StateOf(XElement Form) => (string)Form.Descendants("input").Single(I => (string?)I.Attribute("name") == "state").Attribute("value")!;
        internal static bool Enabled(XElement Form) => Form.Descendants("button").Single().Attribute("disabled") == null;
        internal static string Says(WebPage Page, string Id) => Said(Page, Id) ?? string.Empty;

        // Start refresh from the confirmation the page showed (or with a given state), then the page the redirect leads to
        internal static async Task<WebPage> StartRefresh(Browser Owner, WebPage? Confirmation = null, string? State = null)
        {
            Confirmation ??= await Owner.Get(Address + "?confirm=refresh");
            WebPage Posted = await Owner.Post(Address + "?handler=start", ("state", State ?? StateOf(StartForm(Confirmation)!)));
            Assert.That((Posted.Status, Posted.Location), Is.EqualTo((HttpStatusCode.Redirect, Address)), Posted.Body);
            return await Owner.Get(Address);
        }

        internal static async Task<WebPage> CancelRefresh(Browser Owner, WebPage From, string? State = null)
        {
            WebPage Posted = await Owner.Post(Address + "?handler=cancel", ("state", State ?? StateOf(CancelForm(From)!)));
            Assert.That((Posted.Status, Posted.Location), Is.EqualTo((HttpStatusCode.Redirect, Address)), Posted.Body);
            return await Owner.Get(Address);
        }

        // the page once the refresh has ended
        internal static async Task<WebPage> RefreshEnded(Browser Owner)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                WebPage Page = await Owner.Get(Address);
                if (CancelForm(Page) == null)
                {
                    return Page;
                }
                Assert.That(Clock.Elapsed, Is.LessThan(Long), "the refresh did not end: " + Says(Page, "status"));
                await Task.Delay(20);
            }
        }

        // ---- who may use it, and what a GET does ----

        [Test]
        public async Task TheSystemPage_NeedsTheOwner_ShowsTheCurrentConfigurationWithoutAPath_AndAGetOnlyLooks()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Visitor = S.Host.Browser();
            WebPage Refused = await Visitor.Get(Address);
            Assert.That((Refused.Status, Refused.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin?returnUrl=%2Fsettings%2Fsystem")));
            foreach (string Handler in new[] { "start", "cancel" })
            {
                WebPage Post = await Visitor.Post(Address + "?handler=" + Handler, ("state", "0-idle"));
                Assert.That((Post.Status, Post.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin")), "to the sign-in, without starting anything");
            }

            using Browser Owner = await S.Host.SignedIn();
            string Before = Snapshot(S.Folder);
            string CacheBefore = Snapshot(S.Cache);
            WebPage Page = await Owner.Get(Address);
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            WebPage Other = await Owner.Get(Address + "?confirm=yes&handler=start");
            Assert.That(new[] { Page.Status, Confirm.Status, Other.Status }, Has.All.EqualTo(HttpStatusCode.OK));
            Assert.That(S.Fake.Calls, Is.Zero, "a GET starts no refresh");
            Assert.That(Snapshot(S.Folder), Is.EqualTo(Before), "a GET creates and changes nothing in the settings folder");
            Assert.That(Snapshot(S.Cache), Is.EqualTo(CacheBefore));
            Assert.That(StartForm(Page), Is.Null, "the confirmation only when asked for");
            Assert.That(StartForm(Other), Is.Null);

            //the eight items of View Config, in its order; the folders in words
            XElement Facts = Page.Xml().Descendants("dl").Single(D => (string?)D.Attribute("id") == "configuration");
            Assert.That(Facts.Elements("dt").Select(D => D.Value), Is.EqualTo(CurrentConfiguration.Labels));
            List<string> Values = Facts.Elements("dd").Select(D => D.Value).ToList();
            Assert.That(Values[1], Is.EqualTo(CurrentConfiguration.FolderNotShown));
            Assert.That(Values[2], Is.EqualTo(CurrentConfiguration.FolderNotShown));
            Assert.That(Values[3], Is.EqualTo("Console"));
            Assert.That(Values[5], Does.Contain("(2026-11-20)").Or.Contain("ago"));
            Assert.That(Page.Body, Does.Not.Contain(S.Folder).And.Not.Contain(S.Cache).And.Not.Contain(Path.GetFileName(S.Folder)), "no path");
            Assert.That(Page.Text(), Does.Contain(WebSystemWording.PathsNote).And.Contain(RefreshWording.Explanation).And.Contain(SystemWording.ConfigurationIntro));
            Assert.That(Says(Page, "last-refreshed"), Does.Contain("2026"));
            //the settings pages' row leads here, and the page names no desktop-only action
            Assert.That(Page.Xml().Descendants("nav").Single(N => (string?)N.Attribute("aria-label") == "Settings").Descendants("a").Select(A => A.Value),
                Is.EqualTo(new[] { "General.yaml", "Email.yaml", "Ntfy.yaml", "Web.yaml", "System" }));
            Assert.That(Page.Xml().Descendants("button").Select(B => B.Value.Trim()), Is.EqualTo(new[] { "Sign out", RefreshWording.RefreshLabel }));
            Assert.That(Page.Text(), Does.Not.Contain("Open settings folder").And.Not.Contain("Theme:").And.Not.Contain("Restarting Campertron"));

            //Refresh campground data… asks with a GET, which only shows the confirmation
            XElement Ask = RequestForm(Page);
            Assert.That(Ask.Descendants("input").Single(I => (string?)I.Attribute("name") == "confirm").Attribute("value")!.Value, Is.EqualTo("refresh"));
            Assert.That(Ask.Descendants("input").Where(I => (string?)I.Attribute("name") == "__RequestVerificationToken"), Is.Empty);
            Assert.That(Confirm.Text(), Does.Contain(RefreshWording.ConfirmationHeading).And.Contain(RefreshWording.ConfirmationBasics).And.Contain(WebSystemWording.ConfirmationAfter));
            Assert.That(Enabled(StartForm(Confirm)!), Is.True);
            Assert.That(StateOf(StartForm(Confirm)!), Is.EqualTo("0-idle"));
            Assert.That(Confirm.Xml().Descendants("a").Single(A => A.Value == WebSystemWording.ConfirmationBackLink).Attribute("href")!.Value, Is.EqualTo(Address));
        }

        [Test]
        public async Task EveryAction_IsAPostWithTheFormsToken_FromThisWebUI_WithItsOwnHandler()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            string State = StateOf(StartForm(Confirm)!);

            WebPage NoToken = await Owner.Post(Address + "?handler=start", ("state", State), ("__RequestVerificationToken", "forged"));
            Assert.That(NoToken.Status, Is.EqualTo(HttpStatusCode.BadRequest), "antiforgery");
            Owner.Origin = "http://evil.example";
            WebPage Foreign = await Owner.Post(Address + "?handler=start", ("state", State));
            Assert.That(Foreign.Status, Is.EqualTo(HttpStatusCode.Forbidden), "the Origin check");
            Owner.Origin = Owner.Root.GetLeftPart(UriPartial.Authority);
            WebPage NoHandler = await Owner.Post(Address, ("state", State));
            Assert.That(NoHandler.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), "a POST without a handler of the page");
            WebPage Put = await Owner.Send(new HttpRequestMessage(HttpMethod.Put, Address + "?handler=start"));
            Assert.That(Put.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(S.Fake.Calls, Is.Zero, "none of them started a refresh");

            WebPage Running = await StartRefresh(Owner, Confirm);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
            Assert.That(Running.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That(S.Host.Lines, Does.Contain(WebRefresh.LinePrefix + "refresh started."));
        }

        // ---- the refresh: start, progress, cancel, the cache step, the end ----

        [Test]
        public async Task StartRefresh_RunsTheLibrarysRefresh_WithTheWebUIsFolders_AndThePageShowsItsProgress()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "downloading");
            Assert.That(S.Fake.Request, Is.EqualTo(new RidbRefreshRequest(S.Folder, S.Cache, false)), "the web UI's folders; not in a container");
            Running = await Owner.Get(Address);

            Assert.That(Says(Running, "status"), Is.EqualTo("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            XElement Bar = Running.Xml().Descendants("progress").Single();
            Assert.That(((string?)Bar.Attribute("value"), (string?)Bar.Attribute("max")), Is.EqualTo(("120400000", "570000000")));
            Assert.That(Running.Xml().Descendants("label").Single(L => (string?)L.Attribute("for") == "progress").Value, Is.EqualTo(RefreshWording.ProgressName));
            Assert.That(Says(Running, "progress-details"), Does.StartWith("120.4 MB of 570.0 MB downloaded · Elapsed "));
            Assert.That(Enabled(CancelForm(Running)!), Is.True);
            Assert.That(CancelForm(Running)!.Descendants("button").Single().Value, Is.EqualTo(RefreshWording.CancelLabel(false)));
            Assert.That(() => RequestForm(Running), Throws.Exception, "no new refresh is offered while one runs");
            Assert.That(Running.Text(), Does.Contain(WebSystemWording.NotUpdating));
            Assert.That(Running.Xml().Descendants("a").Count(A => A.Value == WebSystemWording.ShowLatest && (string?)A.Attribute("href") == Address), Is.EqualTo(1));

            //a confirmation asked for now shows the running refresh instead
            WebPage Again = await Owner.Get(Address + "?confirm=refresh");
            Assert.That(StartForm(Again), Is.Null);
            Assert.That(CancelForm(Again), Is.Not.Null);

            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "installing");
            WebPage Finishing = await Owner.Get(Address);
            Assert.That(Says(Finishing, "status"), Is.EqualTo("Installing the new campground data. " + RefreshWording.FinishingText));
            Assert.That(Enabled(CancelForm(Finishing)!), Is.False, "from the cache step on Cancel is unavailable");
            Assert.That(Says(Finishing, "cancel-unavailable"), Is.EqualTo(RefreshWording.FinishingText));
            S.Fake.LetInstall.Set();
            WebPage Ended = await RefreshEnded(Owner);

            Assert.That(Says(Ended, "status"), Does.StartWith("Info: The campground data was refreshed. Elapsed 0:"));
            Assert.That(Says(Ended, "status"), Does.Contain(WebSystemWording.RestartNote));
            Assert.That(Says(Ended, "status"), Does.Not.Contain(WebSystemWording.MonitoringResumed), "no monitoring ran");
            Assert.That(Enabled(RequestForm(Ended)), Is.True, "another refresh can be asked for");
            Assert.That(S.Host.Lines, Does.Contain(WebRefresh.LinePrefix + "The campground data was refreshed."));
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
            Assert.That(S.Host.App.Lifetime.ApplicationStopping.IsCancellationRequested, Is.False, "the web UI never restarts or stops itself");
        }

        [Test]
        public async Task Cancel_StopsTheRefreshAtItsNextCheckpoint_AndThePageSaysTheDataWasNotChanged()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "downloading");
            WebPage Running = await Owner.Get(Address);
            WebPage Canceling = await CancelRefresh(Owner, Running);
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.True);
            WebPage Ended = await RefreshEnded(Owner);
            Assert.That(Says(Ended, "status"), Does.StartWith("Info: The refresh was canceled. The campground data was not changed. Elapsed "));
            Assert.That(Says(Ended, "status"), Does.Not.Contain(WebSystemWording.RestartNote), "nothing to restart for");
            Assert.That(S.Host.Lines, Does.Contain(WebRefresh.LinePrefix + "The refresh was canceled. The campground data was not changed."));
            _ = Canceling;
        }

        [Test]
        public async Task AnOutOfDateOrRepeatedPage_StartsAndCancelsNothing()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage OldConfirmation = await Owner.Get(Address + "?confirm=refresh");
            WebPage First = await StartRefresh(Owner, OldConfirmation);
            S.Fake.Await(S.Fake.Downloading, "the first refresh");
            WebPage FirstRunning = await Owner.Get(Address);

            //the same confirmation sent again, while the refresh runs: nothing
            WebPage Again = await StartRefresh(Owner, OldConfirmation);
            Assert.That(Says(Again, "page-notice"), Does.Contain("out of date"));
            Assert.That(S.Fake.Calls, Is.EqualTo(1));

            //the first refresh ends; the confirmation of before and the first refresh's Cancel are out of date now
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "installing");
            S.Fake.LetInstall.Set();
            await RefreshEnded(Owner);
            WebPage Repeated = await StartRefresh(Owner, OldConfirmation);
            Assert.That(Says(Repeated, "page-notice"), Does.Contain("out of date"), "a repeated Start starts no second refresh");
            Assert.That(S.Fake.Calls, Is.EqualTo(1));

            //a second refresh, and the first one's Cancel: it cancels nothing of the second
            S.Fake.LetDownload.Reset();
            S.Fake.LetInstall.Reset();
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the second refresh");
            Assert.That(S.Fake.Calls, Is.EqualTo(2));
            WebPage Stale = await CancelRefresh(Owner, FirstRunning);
            Assert.That(Says(Stale, "page-notice"), Does.Contain("out of date"));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False, "the newer refresh goes on");
            Assert.That(StateOf(CancelForm(Stale)!), Is.EqualTo("2-running"));
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "the second installs");
            //Cancel past the cache step: too late, said so
            WebPage TooLate = await CancelRefresh(Owner, await Owner.Get(Address));
            Assert.That(Says(TooLate, "page-notice"), Does.Contain("already reached the installation"));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False);
            S.Fake.LetInstall.Set();
            await RefreshEnded(Owner);
        }

        [Test]
        public async Task TheRefresh_IsTheWebUIs_ASessionThatEndsCancelsNothing_AndAnotherSessionSeesAndCancelsIt()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser First = await S.Host.SignedIn();
            using Browser Second = await S.Host.SignedIn();
            await StartRefresh(First);
            S.Fake.Await(S.Fake.Downloading, "started by the first session");
            WebPage SignedOut = await First.Post("/signout");
            Assert.That(SignedOut.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False, "a sign-out cancels nothing");

            WebPage Seen = await Second.Get(Address);
            Assert.That(Says(Seen, "status"), Does.StartWith("Refreshing the campground data:"));
            await CancelRefresh(Second, Seen);
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.True, "any session of the owner may cancel it");
            WebPage Ended = await RefreshEnded(Second);
            Assert.That(Says(Ended, "status"), Does.Contain("The refresh was canceled."));
        }

        [Test]
        public async Task Failures_AreSaidInFixedWords_NeverAServersTextAnExceptionsMessageOrAPath()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            var Cases = new (Func<RidbRefreshResult> Ends, string Words)[]
            {
                (() => FakeRefresh.Result(RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading, false, new HttpRequestException(ServerText)),
                    "Error: The download from Recreation.gov failed. Check the internet connection and try again. The campground data was not changed."),
                (() => FakeRefresh.Result(RidbRefreshOutcome.DownloadTimedOut, RidbRefreshStage.Downloading, false, new DownloadTimedOutException(TimeSpan.FromSeconds(60))),
                    "Error: The download from Recreation.gov stopped responding, so Campertron ended it. Try again later. The campground data was not changed."),
                (() => FakeRefresh.Result(RidbRefreshOutcome.ValidationFailed, RidbRefreshStage.Validating, false, null),
                    "Error: The downloaded campground data failed Campertron's checks, so it was not installed. The campground data was not changed."),
                (() => FakeRefresh.Result(RidbRefreshOutcome.StorageFailed, RidbRefreshStage.Preparing, false, new UnauthorizedAccessException("Access to the path '" + S.Cache + "' is denied.")),
                    "Error: Campertron couldn't read or write the files it needs"),
                (() => throw new InvalidOperationException(ServerText),
                    "Error: Campertron couldn't read or write the files it needs"),
                (() => throw new FormatException(ServerText),
                    "Error: The refresh stopped because of an unexpected problem (FormatException). The campground data was not changed."),
            };
            foreach ((Func<RidbRefreshResult> Ends, string Words) in Cases)
            {
                S.Fake.Before = Ends;
                S.Fake.LetDownload.Set();
                await StartRefresh(Owner);
                WebPage Ended = await RefreshEnded(Owner);
                Assert.That(Says(Ended, "status"), Does.StartWith(Words));
                Assert.That(Says(Ended, "status"), Does.Not.Contain(WebSystemWording.RestartNote));
                Assert.That(Ended.Body, Does.Not.Contain(ServerText).And.Not.Contain(S.Cache).And.Not.Contain("secret-path"), Words);
                Assert.That(Enabled(RequestForm(Ended)), Is.True, "a failed refresh can be asked for again");
            }
            Assert.That(S.Host.Lines.Where(L => L.StartsWith(WebRefresh.LinePrefix, StringComparison.Ordinal)), Has.None.Contain(ServerText).And.None.Contain(S.Cache));
            Assert.That(S.Host.Warnings, Is.Empty);
        }

        [Test]
        public async Task AnInstallationWithoutItsDate_OrWhoseCheckFailed_SaysWhatToDo()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            S.Fake.LetDownload.Set();
            S.Fake.LetInstall.Set();
            S.Fake.After = RidbRefreshOutcome.RefreshedWithoutTimestamp;
            await StartRefresh(Owner);
            WebPage Warned = await RefreshEnded(Owner);
            Assert.That(Says(Warned, "status"), Does.StartWith("Warning: The campground data was refreshed, but Campertron couldn't record the date in General.yaml"));
            Assert.That(Says(Warned, "status"), Does.Contain(WebSystemWording.RestartNote));

            S.Fake.After = RidbRefreshOutcome.InstalledButCheckFailed;
            await StartRefresh(Owner);
            WebPage Checked = await RefreshEnded(Owner);
            Assert.That(Says(Checked, "status"), Does.StartWith("Error: The new campground data was installed, but Campertron's check of it found a problem."));
        }

        // ---- one data operation at a time: the results page's search and the refresh ----

        [Test]
        public async Task ASearchOfTheResultsPage_AndTheRefresh_NeverRunTogether()
        {
            var InRun = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            SearchRunResult Blocking(SearchRunPlan Plan, IProgress<SearchProgress>? Progress, CancellationToken Token, SearchHostOptions? Options)
            {
                InRun.Set();
                Release.Wait(Long);
                return Returns()(Plan, Progress, Token, Options);
            }
            await using SystemHost S = await SystemHost.Start(Run: Blocking);
            File.WriteAllText(S.In("Alpha.yaml"), ResultsPageTests.Search("First trip"));
            using Browser Owner = await S.Host.SignedIn();

            //a search runs: the refresh is unavailable, with the shared reason
            await Owner.Get("/results");
            WebPage Started = await ResultsPageTests.Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results"), null, ("Alpha.yaml", true)));
            Assert.That(InRun.Wait(Long), Is.True);
            WebPage Busy = await Owner.Get(Address);
            Assert.That(Enabled(RequestForm(Busy)), Is.False);
            Assert.That(Says(Busy, "refresh-unavailable"), Is.EqualTo(RefreshWording.SearchRunningReason));
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            Assert.That(Enabled(StartForm(Confirm)!), Is.False);
            WebPage Refused = await StartRefresh(Owner, Confirm);
            Assert.That(Says(Refused, "page-notice"), Is.EqualTo("Warning: " + WebSystemWording.Outcome(WebRefreshOutcome.SearchRunning)));
            Assert.That(S.Fake.Calls, Is.Zero);
            Release.Set();
            await Ended(Owner);
            _ = Started;

            //the refresh runs: the results page's Start is unavailable in every session, with the shared words
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh runs");
            using Browser Other = await S.Host.SignedIn();
            foreach (Browser Each in new[] { Owner, Other })
            {
                WebPage Results = await Each.Get("/results");
                Assert.That(StartButton(Results).Attribute("disabled"), Is.Not.Null);
                Assert.That(Said(Results, "refresh-running"), Is.EqualTo("Warning: " + ResultsWording.RefreshRunning));
            }
            InRun.Reset();
            WebPage NotStarted = await ResultsPageTests.Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results"), null, ("Alpha.yaml", true)));
            Assert.That(Status(NotStarted), Is.EqualTo(ResultsWording.RefreshRunning));
            Assert.That(InRun.IsSet, Is.False, "no search started while the refresh runs");
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "installing");
            S.Fake.LetInstall.Set();
            await RefreshEnded(Owner);
            Assert.That(StartButton(await Owner.Get("/results")).Attribute("disabled"), Is.Null, "the gate is free again");
        }

        // ---- the monitoring: paused for the refresh, started again after it (D83 point 6) ----

        [Test]
        public async Task TheRefresh_PausesARunningMonitoring_AndStartsItAgainAfterwards_WithoutTouchingCampHistory()
        {
            var Fake = new FakeRefresh();
            await using MonitoringHost M = await MonitoringHost.Start(Refresh: new WebRefreshSeams { Run = Fake.Run, Time = new ManualTime() });
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            await MonitoringPageTests.Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage Running = await Waiting(Owner);
            List<string> Markers = M.Markers();
            Assert.That(Markers, Is.Not.Empty, "the first check notified and recorded");
            Assert.That(M.LockIsHeld(), Is.True);
            bool? RanDuringRefresh = null;
            Fake.OnStart = () => RanDuringRefresh = M.Monitoring.View().Status.IsRunning || M.LockIsHeld();

            await StartRefresh(Owner);
            Fake.Await(Fake.Downloading, "the refresh started after the pause");
            Assert.That(RanDuringRefresh, Is.False, "the monitoring stopped before the service started");
            WebPage Paused = await Owner.Get("/monitoring");
            Assert.That(State(Paused), Does.StartWith("Paused while the campground data is refreshed (Settings > System)"));
            Assert.That(MonitoringPageTests.Enabled(Paused, "start") || MonitoringPageTests.Enabled(Paused, "stop") || MonitoringPageTests.Enabled(Paused, "restart"), Is.False);
            Assert.That(Said(Paused, "actions-unavailable"), Does.Contain(WebMonitoringWording.UnavailablePaused));
            WebPage Refused = await MonitoringPageTests.Act(Owner, "start", Paused);
            Assert.That(Said(Refused, "page-notice"), Is.EqualTo("Warning: " + WebMonitoringWording.PausedText));
            Assert.That(M.Host.Lines, Does.Contain(WebMonitoring.LinePrefix + "paused while the campground data is refreshed."));

            Fake.LetDownload.Set();
            Fake.Await(Fake.Installing, "installing");
            Fake.LetInstall.Set();
            WebPage Ended = await RefreshEnded(Owner);
            Assert.That(Says(Ended, "status"), Does.Contain(WebSystemWording.MonitoringResumed));
            M.Waits.Await();
            WebPage Again = await Waiting(Owner);
            Assert.That(MonitoringPageTests.StateOf(Again), Is.Not.EqualTo(MonitoringPageTests.StateOf(Running)), "a new start");
            Assert.That(Said(Again, "facts"), Does.Contain("(again after the campground data's refresh)"));
            Assert.That(M.LockIsHeld(), Is.True);
            Assert.That(M.Markers(), Is.EqualTo(Markers), "the refresh adds and removes nothing of Camp.History, and nothing new was notified");
        }

        [Test]
        public async Task AMonitoringThatDidNotRun_IsNotStartedByTheRefresh()
        {
            var Fake = new FakeRefresh();
            await using MonitoringHost M = await MonitoringHost.Start(Refresh: new WebRefreshSeams { Run = Fake.Run, Time = new ManualTime() });
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            Fake.LetDownload.Set();
            Fake.LetInstall.Set();
            await StartRefresh(Owner);
            WebPage Ended = await RefreshEnded(Owner);
            Assert.That(Says(Ended, "status"), Does.Not.Contain(WebSystemWording.MonitoringResumed));
            WebPage Monitoring = await Owner.Get("/monitoring");
            Assert.That(State(Monitoring), Is.EqualTo("Not running."));
            Assert.That(M.LockIsHeld(), Is.False);
            Assert.That(M.Monitoring.View().Generation, Is.Zero, "never started");
        }

        // ---- the folders, the container, web mode's stop, the health answer, the rate limit ----

        [Test]
        public async Task WithoutTheCacheFolder_TheRefreshIsUnavailable_AndSaysWhy()
        {
            await using SystemHost S = await SystemHost.Start(WithCache: false);
            using Browser Owner = await S.Host.SignedIn();
            WebPage Page = await Owner.Get(Address);
            Assert.That(Enabled(RequestForm(Page)), Is.False);
            Assert.That(Says(Page, "refresh-unavailable"), Is.EqualTo(RefreshWording.NoFoldersReason));
            Assert.That(Page.Xml().Descendants("dd").ElementAt(2).Value, Is.EqualTo(CurrentConfiguration.Unknown));
            WebPage Refused = await StartRefresh(Owner, State: "0-idle");
            Assert.That(Says(Refused, "page-notice"), Is.EqualTo("Warning: " + WebSystemWording.Outcome(WebRefreshOutcome.NoFolders)));
            Assert.That(S.Fake.Calls, Is.Zero);
        }

        [Test]
        public async Task InTheContainer_TheRefreshIsAskedForWithTheContainersFolders()
        {
            await using SystemHost S = await SystemHost.Start(Container: true);
            using Browser Owner = await S.Host.SignedIn();
            S.Fake.LetDownload.Set();
            S.Fake.LetInstall.Set();
            await StartRefresh(Owner);
            await RefreshEnded(Owner);
            Assert.That(S.Fake.Request, Is.EqualTo(new RidbRefreshRequest(S.Folder, S.Cache, true)), "in the container: the download and extraction in the cache folder (D85)");
        }

        [Test]
        public async Task WebModesStop_CancelsARefreshThatCanStop()
        {
            SystemHost S = await SystemHost.Start();
            try
            {
                using Browser Owner = await S.Host.SignedIn();
                await StartRefresh(Owner);
                S.Fake.Await(S.Fake.Downloading, "downloading");
                await Task.Run(() => S.Host.App.StopAsync()).WaitAsync(Long);
                Assert.That(S.Fake.Token.IsCancellationRequested, Is.True);
                Assert.That(S.Host.Lines, Does.Contain(WebRefresh.LinePrefix + "The refresh was canceled. The campground data was not changed."));
            }
            finally
            {
                await S.DisposeAsync();
            }
        }

        [Test]
        public async Task WebModesStop_WaitsForARefreshThatInstalls_ToFinish()
        {
            SystemHost S = await SystemHost.Start();
            try
            {
                using Browser Owner = await S.Host.SignedIn();
                S.Fake.LetDownload.Set();
                await StartRefresh(Owner);
                S.Fake.Await(S.Fake.Installing, "installing");
                //web mode's stop runs the stopping callbacks on the thread that asks: another thread here, as a signal's
                Task Stopping = Task.Run(() => S.Host.App.StopAsync());
                await Task.Delay(300);
                Assert.That(Stopping.IsCompleted, Is.False, "web mode waits for the installation");
                S.Fake.LetInstall.Set();
                await Stopping.WaitAsync(Long);
                //the stop asked it to cancel when it could no longer stop: it finished, and says so
                Assert.That(S.Fake.Token.IsCancellationRequested, Is.True);
                Assert.That(S.Host.Lines, Does.Contain(WebRefresh.LinePrefix + RefreshWording.FinishedAfterCancel + "The campground data was refreshed."));
            }
            finally
            {
                await S.DisposeAsync();
            }
        }

        [Test]
        public async Task TheHealthAnswer_SaysNothingOfTheRefresh_AndTheSystemPagesActionsHaveTheirOwnRateLimit()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "downloading");
            using Browser Anyone = S.Host.Browser();
            WebPage Health = await Anyone.Get("/healthz");
            Assert.That((Health.Status, Health.Body), Is.EqualTo((HttpStatusCode.OK, "ok")));
            WebPage Page = await Anyone.Get(Address);
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.Redirect), "the refresh's state needs the sign-in");

            //ten actions a minute from a client; the eleventh is refused before anything is looked at
            for (int i = 0; i < CampertronWebHost.RefreshActionsPerClient - 1; i++)
            {
                WebPage Posted = await Owner.Post(Address + "?handler=cancel", ("state", "0-idle"));
                Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect));
            }
            WebPage Limited = await Owner.Post(Address + "?handler=start", ("state", "1-running"));
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False);
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task WithoutPasswordProtection_ThePageAndTheRefreshAreTheOwners()
        {
            await using SystemHost S = await SystemHost.Start(WithPassword: false, WebYaml: "passwordProtection: false\nnetworkListening: false\n");
            using Browser Anyone = S.Host.Browser();
            WebPage Page = await Anyone.Get(Address);
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
            S.Fake.LetDownload.Set();
            S.Fake.LetInstall.Set();
            await StartRefresh(Anyone);
            WebPage Ended = await RefreshEnded(Anyone);
            Assert.That(Says(Ended, "status"), Does.Contain("The campground data was refreshed."));
        }

        // ---- no path from the browser, no file to the browser ----

        [Test]
        public async Task TheRefresh_TakesNoFolderPathOrContainerFlagFromTheBrowser()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            string Other = WebTestHost.NewFolder();
            try
            {
                WebPage Confirm = await Owner.Get(Address + "?confirm=refresh&folder=" + Uri.EscapeDataString(Other) + "&cache=" + Uri.EscapeDataString(Other));
                Assert.That(Confirm.Body, Does.Not.Contain(Other));
                S.Fake.LetDownload.Set();
                S.Fake.LetInstall.Set();
                WebPage Posted = await Owner.Post(Address + "?handler=start", ("state", StateOf(StartForm(Confirm)!)), ("ConfigPath", Other), ("CachePath", Other),
                    ("SettingsFolder", Other), ("folder", Other), ("path", "../../" + Path.GetFileName(Other)), ("WorkRoot", Other), ("InContainer", "true"), ("ContainerMode", "true"));
                Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect));
                await RefreshEnded(Owner);
                Assert.That(S.Fake.Request, Is.EqualTo(new RidbRefreshRequest(S.Folder, S.Cache, false)), "the web UI's own folders, never the browser's");
                Assert.That(Directory.GetFileSystemEntries(Other), Is.Empty);
            }
            finally
            {
                Directory.Delete(Other, true);
            }
        }

        [Test]
        public async Task TheCampgroundDataAndTheCacheFolder_AreNeverServed_ThroughTheNewPagesOrTheContainersPaths()
        {
            await using SystemHost S = await SystemHost.Start();
            File.WriteAllText(S.In("RecreationDotOrg.db"), "MARKER-DATABASE");
            File.WriteAllText(Path.Join(S.Cache, "232445-Campsites.json"), "MARKER-CACHE");
            string Download = RidbRefreshService.DownloadFolderPrefix + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Join(S.Cache, Download));
            File.WriteAllText(Path.Join(S.Cache, Download, RidbRefreshService.ArchiveName), "MARKER-ARCHIVE");
            Directory.CreateDirectory(Path.Join(S.Cache, "CampertronExtract"));
            File.WriteAllText(Path.Join(S.Cache, "CampertronExtract", "Facilities_API_v1.json"), "MARKER-EXTRACT");
            string Cache = S.Cache.TrimStart('/');
            string[] Targets =
            {
                "/config/RecreationDotOrg.db", "/cache/232445-Campsites.json", "/cache/" + Download + "/" + RidbRefreshService.ArchiveName, "/cache/CampertronExtract/Facilities_API_v1.json",
                "/" + Download + "/" + RidbRefreshService.ArchiveName, "/CampertronExtract/Facilities_API_v1.json", "/" + RidbRefreshService.ArchiveName, "/232445-Campsites.json",
                "/settings/system/RecreationDotOrg.db", "/settings/system/..%2F..%2FRecreationDotOrg.db", "/settings/RecreationDotOrg.db", "/campgrounds/RecreationDotOrg.db",
                "/campgrounds/park/..%2F..%2FRecreationDotOrg.db", "/campgrounds/state/232445-Campsites.json", "/" + Cache + "/232445-Campsites.json", "/assets/..%2F..%2F" + Cache.Replace("/", "%2F") + "%2F232445-Campsites.json",
                "/settings/system?file=RecreationDotOrg.db", "/campgrounds/park?file=" + Uri.EscapeDataString(S.In("RecreationDotOrg.db")),
            };
            foreach (Browser Each in new[] { S.Host.Browser(), await S.Host.SignedIn() })
            {
                using (Each)
                {
                    foreach (string Target in Targets)
                    {
                        WebPage Answer = await Each.Get(Target);
                        Assert.That(Answer.Body, Does.Not.Contain("MARKER"), Target);
                        Assert.That(Answer.Header("Content-Type"), Does.Not.Contain("octet-stream").And.Not.Contain("application/json").And.Not.Contain("zip"), Target);
                    }
                }
            }
        }

        // ---- what the web UI's refresh refers to ----

        [Test]
        public void TheWebUIsRefresh_RunsOnlyTheLibrarysService_AndNeverRestartsOrEndsTheProcess()
        {
            string[] Forbidden =
            {
                "System.Diagnostics.Process::", "System.Environment::Exit", "System.Environment::FailFast", "Microsoft.Extensions.Hosting.IHostApplicationLifetime::StopApplication",
                "CampertronLibrary.function.RecDotOrg.data.Load::", "CampertronLibrary.function.Base.Web::", "System.Net.Http.", "CampertronLibrary.function.RecDotOrg.sqlite.",
                "CampertronLibrary.function.Base.ConfigFileReplace::", "System.IO.File::Delete", "System.IO.Directory::Delete",
            };
            List<string> Called = IlReferences.ReferencedMembers(typeof(WebRefresh), typeof(WebRefreshOperation), typeof(WebRefreshView), typeof(WebSystemWording), typeof(SystemModel))
                .Select(C => C.Member).Distinct().ToList();
            Assert.That(Called.Where(M => Forbidden.Any(F => M.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(IlReferences.ReferencedMembers(typeof(WebRefreshSeams)).Select(C => C.Member), Does.Contain("CampertronLibrary.function.RecDotOrg.api.RidbRefreshService::Run"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Forms.RefreshWording::Describe"));
        }
    }
}
