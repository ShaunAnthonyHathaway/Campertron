using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The web UI's results page (v0.3.0's PR 7, D97; business-rules-settings §34): the desktop GUI's Results tab as a page,
    // on the real host. A GET only looks; Start, Cancel and the choice are POSTs with the antiforgery token from this web UI,
    // each followed by a redirect; a run is GS1's manual search (CampsiteSearch.Plan and Run) against a recreation.gov the
    // test answers in memory and a fixture campground in a cache folder of the test's own, so nothing reaches the network
    // and no database is opened. One run at a time in the web UI, its progress and results only its session's, five minutes
    // after a block in every session; an out-of-date page applies nothing; no run writes Camp.History, a settings file or
    // the monitoring lock, sends anything or writes to the console. Large and hostile results come from a stub run.
    public class ResultsPageTests
    {
        internal const string CampgroundId = "9900001";
        internal static readonly string[] Campsites = { "990000101", "990000102" };
        internal const string Sentinel = "S3ntinel-password-PR7";

        // A recreation.gov the test controls: answers in memory (never the network), counts the requests, and can stall a
        // request until it is canceled or answer HTTP 429
        internal sealed class Gov
        {
            internal const int Available = 0, Stall = 1, RateLimited = 2;
            private int _Mode;
            private int _Requests;
            internal readonly ManualResetEventSlim Stalled = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim Released = new ManualResetEventSlim(false);
            internal int Mode { get => Volatile.Read(ref _Mode); set => Volatile.Write(ref _Mode, value); }
            internal int Requests => Volatile.Read(ref _Requests);

            internal SearchResponse Send(SearchRequest Request, CancellationToken Token)
            {
                Interlocked.Increment(ref _Requests);
                switch (Mode)
                {
                    case Stall:
                        Stalled.Set();
                        Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                        Released.Set();
                        Token.ThrowIfCancellationRequested();
                        return new SearchResponse(503, "");
                    case RateLimited:
                        return new SearchResponse(429, "Too Many Requests");
                    default:
                        return new SearchResponse(200, MonthJson(Request.Month, Campsites));
                }
            }
        }

        // A clock the test moves (a monotonic timestamp in ticks): the five minutes after a block, how long ago a run ended
        internal sealed class ManualTime : TimeProvider
        {
            private long _Now = 1_000_000_000;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _Now);
            internal void Advance(TimeSpan By) => Interlocked.Add(ref _Now, By.Ticks);
        }

        // a web UI with a cache folder of its own and the campgrounds in memory; Run: a stub of GS1's run (null: the real
        // one, through Gov)
        internal sealed class ResultsHost : IAsyncDisposable
        {
            internal WebTestHost Host { get; private set; } = null!;
            internal string Cache { get; private set; } = string.Empty;
            internal Gov Gov { get; } = new Gov();
            internal ManualTime Time { get; } = new ManualTime();
            internal string Folder => Host.Folder;
            internal string In(string Name) => Path.Join(Host.Folder, Name);

            // LiveInterval, LiveTimeout: the page's automatic progress (D108), how often it asks and how long it waits
            internal static async Task<ResultsHost> Start(Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>? Run = null,
                bool WithCache = true, bool WithPassword = true, TimeSpan? LiveInterval = null, TimeSpan? LiveTimeout = null)
            {
                var Results = new ResultsHost();
                Results.Host = await SearchesPageTests.Start(WithPassword, Configure: Settings =>
                {
                    if (WithCache)
                    {
                        Results.Cache = Settings.SettingsFolder + "-cache";
                        Directory.CreateDirectory(Results.Cache);
                        WriteCampground(Results.Cache);
                        Settings.CachePath = Results.Cache;
                    }
                    var Options = new SearchHostOptions { Transport = Results.Gov.Send, RetryDelay = TimeSpan.FromMilliseconds(20), HeavyTrafficDelay = TimeSpan.FromMilliseconds(5) };
                    Settings.Results = Run == null
                        ? new WebResultsSeams { Time = Results.Time, Options = Options }
                        : new WebResultsSeams { Time = Results.Time, Options = Options, Run = Run };
                    Settings.LiveInterval = LiveInterval ?? Settings.LiveInterval;
                    Settings.LiveTimeout = LiveTimeout ?? Settings.LiveTimeout;
                });
                File.WriteAllText(Results.In("General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
                File.WriteAllText(Results.In("Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                    + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
                return Results;
            }

            public async ValueTask DisposeAsync()
            {
                await Host.DisposeAsync();
                if (Cache.Length > 0 && Directory.Exists(Cache))
                {
                    Directory.Delete(Cache, true);
                }
            }
        }

        internal static string Search(string Name, bool AutoRun = true, string Campground = CampgroundId, int Consecutive = 1) =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '" + Campground + "'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 10\n" +
            "searchValueDates: []\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
            "includeEquipment: []\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\nincludeAttributes: []\nexcludeAttributes: []\nincludeCampsiteType: []\nexcludeCampsiteType: []\n" +
            "consecutiveDays: " + Consecutive.ToString(CultureInfo.InvariantCulture) + "\n";

        // the campground's campsite details, in the cache's own files and format (the completion marker last), as the
        // desktop GUI's smoke test writes them (RunProbe): no database is needed
        internal static void WriteCampground(string Cache)
        {
            void Write(string Name, object Value) => File.WriteAllText(Path.Join(Cache, Name), JsonSerializer.Serialize(Value), new UTF8Encoding(false));
            Write(CampgroundId + "-CampInfo.json", new ReturnParkCampground { CampsiteName = "PROBE CAMPGROUND", ParkName = "Campertron Probe Park" });
            Write(CampgroundId + "-Campsites.json", Campsites.Select((Site, Index) => new CampsitesRecdata
            {
                CampsiteID = Site,
                FacilityID = CampgroundId,
                CampsiteName = (Index + 1).ToString("000", CultureInfo.InvariantCulture),
                CampsiteType = "STANDARD NONELECTRIC",
                Loop = "A",
                TypeOfUse = "Overnight",
            }).ToList());
            Write(CampgroundId + "-CampsitesByPark.json", Campsites.ToList());
            foreach (string Site in Campsites)
            {
                Write(Site + "-Equipment.json", new List<string> { "Tent" });
                Write(Site + "-Attributes.json", new List<AttributeValuePair>
                {
                    new AttributeValuePair { AttributeName = "Checkin Time", AttributeValue = "2:00 PM" },
                    new AttributeValuePair { AttributeName = "Checkout Time", AttributeValue = "12:00 PM" },
                });
            }
            Write(CampgroundId + "-Cached.json", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        }

        // a month as recreation.gov answers it: every night of every campsite available
        internal static string MonthJson(DateTime Month, IEnumerable<string> Sites)
        {
            int Days = DateTime.DaysInMonth(Month.Year, Month.Month);
            var Json = new StringBuilder("{\"campsites\":{");
            int Count = 0;
            foreach (string Site in Sites)
            {
                Json.Append(Count++ > 0 ? "," : "").Append('"').Append(Site).Append("\":{\"availabilities\":{");
                for (int Day = 1; Day <= Days; Day++)
                {
                    Json.Append(Day > 1 ? "," : "").Append('"').Append(new DateTime(Month.Year, Month.Month, Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("T00:00:00Z\":\"Available\"");
                }
                Json.Append("},\"campsite_id\":\"").Append(Site).Append("\",\"campsite_type\":\"STANDARD NONELECTRIC\",\"loop\":\"A\",\"max_num_people\":6,\"min_num_people\":1,\"site\":\"")
                    .Append(Site).Append("\"}");
            }
            return Json.Append("},\"count\":").Append(Count).Append('}').ToString();
        }

        // ---- GS1's model made by the test (a stub run's result) ----

        internal static SearchOpening Opening(string Site, DateTime Night, string? Name = null, string? Url = null, string? Loop = "A") =>
            new SearchOpening(Site, Name ?? Site, Loop, "STANDARD NONELECTRIC", new[] { "Tent" }, Night, "2:00 PM", "12:00 PM", 1, 6, Url ?? CampsiteSearch.BookingUrl(Site));

        internal static SearchResult Result(string File, IEnumerable<SearchOpening> Openings, string? Name = null, string? Campground = "UPPER PINES")
        {
            List<SearchOpening> All = Openings.ToList();
            List<SearchSite> Sites = All.GroupBy(O => O.CampsiteId).Select(G => new SearchSite(G.Key, G.First().CampsiteName, G.First().Loop, G.First().CampsiteType, G.First().Equipment,
                null, null, 1, 6, G.First().BookingUrl, G.Select(O => O.Night).ToList(), new[] { new SearchStay(G.First().Night, G.Count()) })).ToList();
            return new SearchResult(File, Name ?? File.Replace(".yaml", string.Empty), true, CampgroundId, Campground, "Yosemite", 1, SearchState.Complete, 1, 1,
                new[] { new SearchMonthResult(new DateTime(2026, 12, 1), true, null) }, All, Sites, Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>());
        }

        internal static Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult> Returns(params SearchResult[] Searches) =>
            (Plan, Progress, Token, Options) => new SearchRunResult(SearchRunStatus.Completed, DateTime.UtcNow, TimeSpan.FromSeconds(2), 1, 1, 0, Searches, Array.Empty<SearchIssue>());

        // ---- the page ----

        internal static XElement Form(WebPage Page) => Page.Xml().Descendants("form").Single(F => ((string?)F.Attribute("action") ?? "").StartsWith("/results", StringComparison.Ordinal));

        // the page's fields as a browser sends them: the revision, the boxes checked (Check: names to check or uncheck)
        internal static (string Name, string Value)[] Fields(WebPage Page, string? Action = null, params (string File, bool On)[] Check)
        {
            var Sent = new List<(string, string)>();
            foreach (XElement Input in Form(Page).Descendants("input"))
            {
                string? Name = (string?)Input.Attribute("name");
                if (Name == null || Input.Attribute("disabled") != null || Name == "__RequestVerificationToken")
                {
                    continue;
                }
                string Value = (string?)Input.Attribute("value") ?? string.Empty;
                if ((string?)Input.Attribute("type") == "checkbox")
                {
                    bool On = Check.Any(C => C.File == Value) ? Check.Single(C => C.File == Value).On : Input.Attribute("checked") != null;
                    if (!On)
                    {
                        continue;
                    }
                }
                Sent.Add((Name, Value));
            }
            if (Action != null)
            {
                Sent.Add(("action", Action));
            }
            return Sent.ToArray();
        }

        internal static XElement? Element(WebPage Page, string Id) => Page.Xml().Descendants().FirstOrDefault(E => (string?)E.Attribute("id") == Id);
        internal static string Status(WebPage Page) => Element(Page, "status")!.Value;
        internal static XElement Box(WebPage Page, string File) => Form(Page).Descendants("input").Single(I => (string?)I.Attribute("type") == "checkbox" && (string?)I.Attribute("value") == File);
        internal static bool Checked(WebPage Page, string File) => Box(Page, File).Attribute("checked") != null;
        internal static bool Disabled(XElement Element) => Element.Attribute("disabled") != null;
        internal static XElement Button(WebPage Page, Func<XElement, bool> Which) => Form(Page).Descendants("button").Single(Which);
        internal static XElement StartButton(WebPage Page) => Button(Page, B => (string?)B.Attribute("formaction") == "/results?handler=start");

        internal static async Task<WebPage> Act(Browser Owner, string Path, (string, string)[] Fields)
        {
            WebPage Posted = await Owner.Post(Path, Fields);
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect), Posted.Body);
            Assert.That(Posted.Location, Is.EqualTo("/results"));
            return await Owner.Get("/results");
        }

        // the page once the session's run has ended
        internal static async Task<WebPage> Ended(Browser Owner)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                WebPage Page = await Owner.Get("/results");
                if (Element(Page, "progress") == null || Clock.Elapsed > TimeSpan.FromSeconds(20))
                {
                    Assert.That(Element(Page, "progress"), Is.Null, "the run did not end: " + Status(Page));
                    return Page;
                }
                await Task.Delay(20);
            }
        }

        // the page's main part without the antiforgery token (a new one with every page)
        internal static string WithoutToken(WebPage Page)
        {
            XElement Main = Page.Xml().Descendants("main").Single();
            Main.Descendants("input").Where(I => (string?)I.Attribute("name") == "__RequestVerificationToken").Remove();
            return Main.ToString();
        }

        // the settings folder's files with their bytes and times, and the cache folder's names
        internal static string Snapshot(string Folder) => string.Join("\n", Directory.GetFileSystemEntries(Folder).OrderBy(N => N, StringComparer.Ordinal)
            .Select(N => Path.GetFileName(N) + "\t" + (File.Exists(N) ? System.Convert.ToHexString(File.ReadAllBytes(N)) + "\t" + File.GetLastWriteTimeUtc(N).Ticks : "dir")));

        // ---- the page and who may use it ----

        [Test]
        public async Task TheResultsPage_NeedsTheOwner_ListsTheSearchFilesWithTheTabsChoice_AndAGetOnlyLooks()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip", AutoRun: false));
            File.WriteAllText(R.In("Broken.yaml"), "displayName: [not, a, name\n");

            using Browser Visitor = R.Host.Browser();
            WebPage Refused = await Visitor.Get("/results");
            Assert.That((Refused.Status, Refused.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin?returnUrl=%2Fresults")));
            WebPage VisitorStart = await Visitor.Post("/results?handler=start", ("revision", "1"), ("chosen", "Alpha.yaml"));
            Assert.That(VisitorStart.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(VisitorStart.Location, Is.EqualTo("/signin"), "to the sign-in, without running anything");
            Assert.That(R.Gov.Requests, Is.Zero, "no run without the owner");

            using Browser Owner = await R.Host.SignedIn();
            string Before = Snapshot(R.Folder);
            WebPage Page = await Owner.Get("/results");
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Page.Header("Cache-Control"), Does.Contain("no-store"), "no browser or proxy keeps a results page");
            Assert.That(Page.Xml().Descendants("h1").Single().Value, Is.EqualTo("Results"));
            Assert.That(Page.Xml().Descendants("nav").First().Descendants("a").Select(A => A.Value), Is.EqualTo(new[] { "Start", "Searches", "Results", "Monitoring", "Settings", "Campgrounds", "Change password" }));
            Assert.That(Page.Xml().Descendants("a").Single(A => A.Value == "Results" && (string?)A.Attribute("aria-current") == "page"), Is.Not.Null);
            Assert.That(Page.Text(), Does.Contain(WebResultsLiveWording.Intro), "the intro says that a run's progress shows by itself (D108)");
            Assert.That(Page.Xml().Descendants("script"), Is.Empty, "no run: no script");
            Assert.That(Element(Page, "run-heading")!.Value, Is.EqualTo(ResultsWording.RunHeading));
            Assert.That(Form(Page).Descendants("legend").Single().Value, Is.EqualTo(ResultsWording.SearchFilesName));
            Assert.That((Checked(Page, "Alpha.yaml"), Checked(Page, "Bravo.yaml"), Checked(Page, "Broken.yaml")), Is.EqualTo((true, false, false)), "the enabled files that can run, chosen by default");
            Assert.That(Disabled(Box(Page, "Broken.yaml")), Is.True);
            Assert.That(Page.Text(), Does.Contain("Error: Can't be searched:"));
            Assert.That(Page.Text(), Does.Contain(AutoRunWording.Enabled + ": selected by default").And.Contain(AutoRunWording.NotEnabled + ": runs when selected"));
            Assert.That(Status(Page), Is.EqualTo(ResultsWording.Idle));
            Assert.That(Disabled(StartButton(Page)), Is.False);
            Assert.That(Element(Page, "results-heading"), Is.Null, "no run yet: no results");
            Assert.That(Element(Page, "progress"), Is.Null);
            for (int i = 0; i < 3; i++)
            {
                await Owner.Get("/results");
                await Owner.Get("/results?run=1&search=0&part=2");
            }
            Assert.That(Snapshot(R.Folder), Is.EqualTo(Before), "a GET writes nothing");
            Assert.That(R.Gov.Requests, Is.Zero, "a GET asks recreation.gov nothing");
            Assert.That(R.Host.Lines, Is.Empty);
            Assert.That(Page.Body, Does.Not.Contain(Sentinel).And.Not.Contain(R.Folder).And.Not.Contain(R.Cache));
        }

        [Test]
        public async Task Start_RunsTheChosenFilesThroughGS1_AsAManualSearch_AndShowsItsResults()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip", AutoRun: false));
            using Browser Owner = await R.Host.SignedIn();
            string Before = Snapshot(R.Folder);
            WebPage Page = await Owner.Get("/results");
            WebPage Started = await Act(Owner, "/results?handler=start", Fields(Page));
            Assert.That(Element(Started, "status")!.Attribute("autofocus"), Is.Not.Null, "the status has the focus after Start");
            WebPage Done = await Ended(Owner);

            Assert.That(Status(Done), Does.StartWith("Finished: ").And.Contain(" found in 1 search.").And.Contain(" Elapsed 0:00."));
            Assert.That(R.Gov.Requests, Is.InRange(1, 3 * 2), "at most three requests a month");
            Assert.That(Element(Done, "results-heading")!.Value, Is.EqualTo(ResultsWording.LastRunHeading));
            Assert.That(Element(Done, "ended")!.Value, Does.StartWith("This run ended less than a minute ago."));
            Assert.That(Element(Done, "shown-heading")!.Value, Is.EqualTo("First trip"));
            Assert.That(Done.Xml().Descendants("nav").Single(N => (string?)N.Attribute("aria-label") == ResultsWording.ResultsOfName).Descendants("a").Single().Value,
                Does.StartWith("First trip (Alpha.yaml) · Complete: "));
            List<XElement> Links = Done.Xml().Descendants("a").Where(A => ((string?)A.Attribute("href") ?? "").StartsWith("https://", StringComparison.Ordinal)).ToList();
            Assert.That(Links.Select(A => (string)A.Attribute("href")!).Distinct(), Is.EquivalentTo(Campsites.Select(CampsiteSearch.BookingUrl)));
            Assert.That(Links.Select(A => A.Value).Distinct(), Is.EquivalentTo(new[] { "Booking page of Site 001", "Booking page of Site 002" }));
            Assert.That(Box(Done, "Alpha.yaml").Parent!.Descendants("p").Single(P => (string?)P.Attribute("class") == "run-state").Value, Does.StartWith("Complete: "));
            Assert.That(Box(Done, "Bravo.yaml").Parent!.Descendants("p").Any(P => (string?)P.Attribute("class") == "run-state"), Is.False, "not chosen: nothing to say");
            Assert.That(Disabled(Box(Done, "Alpha.yaml")), Is.False, "the choice is open again");

            //a manual search: nothing written in the settings folder (no Camp.History, no lock), nothing sent, nothing said
            Assert.That(Snapshot(R.Folder), Is.EqualTo(Before));
            Assert.That(File.Exists(R.In("Camp.History")) || File.Exists(R.In(MonitoringLock.FileName)), Is.False);
            Assert.That(R.Host.Lines.Concat(R.Host.Warnings).Concat(R.Host.Errors), Is.Empty);
            Assert.That(Done.Body, Does.Not.Contain(Sentinel).And.Not.Contain(R.Folder).And.Not.Contain(R.Cache));

            //the same page again: the same results, in the same order (an immutable snapshot), but for the form's new token
            string Once = WithoutToken(await Owner.Get("/results"));
            string Twice = WithoutToken(await Owner.Get("/results"));
            Assert.That(Twice, Is.EqualTo(Once));
        }

        [Test]
        public async Task WhileARunGoesOn_ThePageShowsItsProgress_FixesTheChoice_AndCancelStopsIt()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip", AutoRun: false));
            R.Gov.Mode = Gov.Stall;
            using Browser Owner = await R.Host.SignedIn();
            await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
            Assert.That(R.Gov.Stalled.Wait(TimeSpan.FromSeconds(15)), Is.True);
            WebPage Running = await Owner.Get("/results");
            Assert.That(Status(Running), Is.EqualTo(ResultsWording.Searching(1)));
            XElement Bar = Element(Running, "progress")!;
            Assert.That(Running.Xml().Descendants("label").Single(L => (string?)L.Attribute("for") == "progress").Value, Is.EqualTo(ResultsWording.ProgressName));
            Assert.That(Element(Running, "progress-details")!.Value, Does.Contain("Elapsed 0:00"));
            Assert.That(Running.Xml().Descendants("a").Single(A => A.Value == "Show the latest progress").Attribute("href")!.Value, Is.EqualTo("/results"));
            Assert.That(Form(Running).Descendants("input").Where(I => (string?)I.Attribute("type") == "checkbox").All(Disabled), Is.True, "the choice is fixed while it runs");
            Assert.That(Form(Running).Descendants("input").Any(I => (string?)I.Attribute("name") == "shown"), Is.False);
            Assert.That(Disabled(StartButton(Running)), Is.True);
            Assert.That(Form(Running).Descendants("button").Where(B => (string?)B.Attribute("name") == "action" && (string?)B.Attribute("value") != "keep").All(Disabled), Is.True);
            XElement Cancel = Button(Running, B => (string?)B.Attribute("formaction") == "/results?handler=cancel");
            Assert.That(Cancel.Value, Is.EqualTo("Cancel"));
            Assert.That(Box(Running, "Alpha.yaml").Parent!.Descendants("p").Single(P => (string?)P.Attribute("class") == "run-state").Value, Is.EqualTo("Searching"));
            Assert.That(Element(Running, "results-heading"), Is.Null, "the previous results give way to the run");

            //a choice posted meanwhile changes nothing; a Cancel of another run cancels nothing
            await Act(Owner, "/results", Fields(Running, "all").Append(("chosen", "Bravo.yaml")).Append(("shown", "0Bravo.yaml")).ToArray());
            await Act(Owner, "/results?handler=cancel", new[] { ("run", "999") });
            Assert.That(R.Gov.Released.IsSet, Is.False, "the run goes on");
            WebPage Still = await Owner.Get("/results");
            Assert.That(Checked(Still, "Bravo.yaml"), Is.False);

            WebPage Canceling = await Act(Owner, "/results?handler=cancel", new[] { ("run", (string)Cancel.Attribute("value")!) });
            Assert.That(R.Gov.Released.Wait(TimeSpan.FromSeconds(15)), Is.True, "the stalled request was canceled");
            WebPage Done = await Ended(Owner);
            Assert.That(Status(Done), Does.StartWith("Canceled. Searches that had finished keep their results"));
            Assert.That(Canceling.Status, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task OneSearchRunsAtATime_InTheWebUI_AndASessionSeesOnlyItsOwnRun()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            R.Gov.Mode = Gov.Stall;
            using Browser First = await R.Host.SignedIn();
            using Browser Second = await R.Host.SignedIn();
            await Act(First, "/results?handler=start", Fields(await First.Get("/results")));
            Assert.That(R.Gov.Stalled.Wait(TimeSpan.FromSeconds(15)), Is.True);
            int Asked = R.Gov.Requests;
            string FirstRun = (string)Button(await First.Get("/results"), B => (string?)B.Attribute("formaction") == "/results?handler=cancel").Attribute("value")!;

            WebPage Other = await Second.Get("/results");
            Assert.That(Element(Other, "other-session")!.Value, Is.EqualTo("Warning: " + ResultsWording.OtherSessionRunning));
            Assert.That(Disabled(StartButton(Other)), Is.True);
            Assert.That(Element(Other, "progress"), Is.Null, "another session's progress is not shown");
            Assert.That(Other.Xml().Descendants("p").Any(P => (string?)P.Attribute("class") == "run-state"), Is.False);
            Assert.That(Disabled(Box(Other, "Alpha.yaml")), Is.False, "this session's choice is its own");
            WebPage Refused = await Act(Second, "/results?handler=start", Fields(Other));
            Assert.That(Status(Refused), Is.EqualTo("Not started: a search started in another browser session is running."));
            await Act(Second, "/results?handler=cancel", new[] { ("run", FirstRun) });
            Assert.That(R.Gov.Requests, Is.EqualTo(Asked), "no second run");
            Assert.That(R.Gov.Released.IsSet, Is.False, "another session can't cancel the run");

            await Act(First, "/results?handler=cancel", new[] { ("run", FirstRun) });
            WebPage FirstDone = await Ended(First);
            R.Gov.Mode = Gov.Available;
            WebPage Again = await Second.Get("/results");
            Assert.That(Element(Again, "other-session"), Is.Null);
            await Act(Second, "/results?handler=start", Fields(Again));
            WebPage SecondDone = await Ended(Second);
            Assert.That(Status(SecondDone), Does.StartWith("Finished: "));
            Assert.That(Status(await First.Get("/results")), Is.EqualTo(Status(FirstDone)), "each session keeps its own last run");
            Assert.That(Status(FirstDone), Does.StartWith("Canceled."));
        }

        [Test]
        public async Task ABlockedRun_HoldsStartForFiveMinutes_InEverySession()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            R.Gov.Mode = Gov.RateLimited;
            using Browser First = await R.Host.SignedIn();
            using Browser Second = await R.Host.SignedIn();
            await Act(First, "/results?handler=start", Fields(await First.Get("/results")));
            WebPage Blocked = await Ended(First);
            Assert.That(Status(Blocked), Does.StartWith("Stopped: recreation.gov limited the requests, so no further request was made."));
            int Asked = R.Gov.Requests;
            foreach (Browser Each in new[] { First, Second })
            {
                WebPage Page = await Each.Get("/results");
                Assert.That(Element(Page, "cooldown")!.Value, Is.EqualTo("recreation.gov limited Campertron's requests. Start is available again in 5:00."));
                Assert.That(Disabled(StartButton(Page)), Is.True);
                await Act(Each, "/results?handler=start", Fields(Page));
            }
            Assert.That(R.Gov.Requests, Is.EqualTo(Asked), "nothing is asked during the five minutes");
            R.Time.Advance(TimeSpan.FromMinutes(4));
            Assert.That(Element(await Second.Get("/results"), "cooldown")!.Value, Does.EndWith("in 1:00."));
            R.Time.Advance(TimeSpan.FromMinutes(1));
            WebPage After = await Second.Get("/results");
            Assert.That(Element(After, "cooldown"), Is.Null);
            Assert.That(Disabled(StartButton(After)), Is.False);
        }

        [Test]
        public async Task AnOutOfDatePage_AppliesNothing_AndStartsNothing()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip"));
            using Browser Owner = await R.Host.SignedIn();
            WebPage Old = await Owner.Get("/results");
            await Act(Owner, "/results", Fields(Old, "clear"));
            WebPage Stale = await Act(Owner, "/results?handler=start", Fields(Old));
            Assert.That(Element(Stale, "page-notice")!.Value, Is.EqualTo("Warning: " + ResultsModel.StaleText));
            Assert.That(Element(Stale, "page-notice")!.Attribute("autofocus"), Is.Not.Null);
            Assert.That((Checked(Stale, "Alpha.yaml"), Checked(Stale, "Bravo.yaml")), Is.EqualTo((false, false)), "the old page's boxes were not applied");
            Assert.That(R.Gov.Requests, Is.Zero);
            Assert.That(Element(await Owner.Get("/results"), "page-notice"), Is.Null, "the notice is shown once");

            //a Start sent twice starts once
            WebPage Fresh = await Act(Owner, "/results", Fields(await Owner.Get("/results"), "enabled"));
            await Act(Owner, "/results?handler=start", Fields(Fresh));
            await Ended(Owner);
            int Asked = R.Gov.Requests;
            WebPage Again = await Act(Owner, "/results?handler=start", Fields(Fresh));
            Assert.That(Element(Again, "page-notice"), Is.Not.Null);
            Assert.That(R.Gov.Requests, Is.EqualTo(Asked));
        }

        [Test]
        public async Task TheChoice_FollowsTheTabsRules_AndOnlyTheBoxesThePageShowed()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip", AutoRun: false));
            File.WriteAllText(R.In("Broken.yaml"), "displayName: [not, a, name\n");
            using Browser Owner = await R.Host.SignedIn();
            WebPage All = await Act(Owner, "/results", Fields(await Owner.Get("/results"), "all"));
            Assert.That((Checked(All, "Alpha.yaml"), Checked(All, "Bravo.yaml"), Checked(All, "Broken.yaml")), Is.EqualTo((true, true, false)));
            Assert.That(Element(All, "run-files")!.Attribute("autofocus"), Is.Not.Null);
            WebPage Cleared = await Act(Owner, "/results", Fields(All, "clear"));
            Assert.That((Checked(Cleared, "Alpha.yaml"), Checked(Cleared, "Bravo.yaml")), Is.EqualTo((false, false)));
            WebPage Enabled = await Act(Owner, "/results", Fields(Cleared, "enabled"));
            Assert.That((Checked(Enabled, "Alpha.yaml"), Checked(Enabled, "Bravo.yaml")), Is.EqualTo((true, false)));

            //Enter keeps the boxes as they are: a change the user made is applied, nothing runs
            WebPage Kept = await Act(Owner, "/results", Fields(Enabled, "keep", ("Alpha.yaml", false), ("Bravo.yaml", true)));
            Assert.That((Checked(Kept, "Alpha.yaml"), Checked(Kept, "Bravo.yaml")), Is.EqualTo((false, true)));
            //a request that sends no box changes no box (only a box the page showed, and the user changed, is applied)
            string Revision = (string)Form(Kept).Descendants("input").Single(I => (string?)I.Attribute("name") == "revision").Attribute("value")!;
            WebPage Unsent = await Act(Owner, "/results", new[] { ("revision", Revision), ("action", "keep") });
            Assert.That((Checked(Unsent, "Alpha.yaml"), Checked(Unsent, "Bravo.yaml")), Is.EqualTo((false, true)));
            Kept = Unsent;

            //a box the page did not show, or a file that can't run, is never chosen by a request
            File.WriteAllText(R.In("Charlie.yaml"), Search("Third trip"));
            WebPage Forged = await Act(Owner, "/results", Fields(Kept, "keep").Concat(new[] { ("chosen", "Broken.yaml"), ("shown", "0Broken.yaml"), ("chosen", "Charlie.yaml"), ("shown", "0Charlie.yaml"), ("chosen", "../General.yaml"), ("shown", "0../General.yaml") }).ToArray());
            Assert.That(Checked(Forged, "Broken.yaml"), Is.False);
            Assert.That(Form(Forged).Descendants("input").Any(I => (string?)I.Attribute("value") == "Charlie.yaml"), Is.False, "the page after an action shows the list the action used");
            Assert.That(Checked(await Owner.Get("/results"), "Charlie.yaml"), Is.True, "listed at the next look, chosen by default as an enabled file: not by the request");
            WebPage NoChoice = await Act(Owner, "/results", Fields(await Owner.Get("/results"), "clear"));
            WebPage Refused = await Act(Owner, "/results?handler=start", Fields(NoChoice));
            Assert.That(Status(Refused), Is.EqualTo(ResultsWording.NotStartedNoChoice));
            Assert.That(R.Gov.Requests, Is.Zero);
        }

        [Test]
        public async Task ManyOpenings_AreShownAPageAtATime_AndThePageSaysWhichRowsItShows()
        {
            var Night = new DateTime(2026, 12, 1);
            var Many = Enumerable.Range(0, 9).SelectMany(n => Enumerable.Range(0, 50).Select(s => Opening((1000 + s).ToString(CultureInfo.InvariantCulture), Night.AddDays(n)))).ToList();
            await using ResultsHost R = await ResultsHost.Start(Run: Returns(Result("Alpha.yaml", Many, Name: "First trip"), Result("Bravo.yaml", Array.Empty<SearchOpening>(), Name: "Second trip")));
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip"));
            using Browser Owner = await R.Host.SignedIn();
            await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
            WebPage First = await Ended(Owner);
            Assert.That(First.Xml().Descendants("ul").Where(U => (string?)U.Attribute("class") == "opening-list").SelectMany(U => U.Elements("li")).Count(), Is.EqualTo(200));
            Assert.That(Element(First, "rows-range")!.Value, Is.EqualTo("Showing openings 1 to 200 of 450."));
            Assert.That(First.Body.Length, Is.LessThan(256 * 1024), "a page of rows stays small");
            XElement Pages = First.Xml().Descendants("nav").Single(N => (string?)N.Attribute("aria-label") == "Rows of First trip");
            Assert.That(Pages.Descendants("a").Select(A => (A.Value, (string?)A.Attribute("href"))), Is.EqualTo(new[] { ("Next rows", (string?)"/results?run=1&search=0&part=2") }));

            WebPage Second = await Owner.Get("/results?run=1&search=0&part=2");
            Assert.That(Element(Second, "rows-range")!.Value, Is.EqualTo("Showing openings 201 to 400 of 450."));
            Assert.That(Second.Xml().Descendants("nav").Single(N => (string?)N.Attribute("aria-label") == "Rows of First trip").Descendants("a").Select(A => A.Value), Is.EqualTo(new[] { "Previous rows", "Next rows" }));
            Assert.That(Element(await Owner.Get("/results?run=1&search=0&part=99"), "rows-range")!.Value, Is.EqualTo("Showing openings 401 to 450 of 450."));
            Assert.That(Element(await Owner.Get("/results?run=7&search=0&part=2"), "rows-range")!.Value, Is.EqualTo("Showing openings 1 to 200 of 450."), "another run's address shows this run's first page");
            Assert.That(Element(await Owner.Get("/results?run=1&search=9"), "shown-heading")!.Value, Is.EqualTo("First trip"), "no such search: the first with openings");
            Assert.That(Element(await Owner.Get("/results?run=1&search=x&part=-1"), "shown-heading")!.Value, Is.EqualTo("First trip"));

            WebPage Empty = await Owner.Get("/results?run=1&search=1");
            Assert.That(Element(Empty, "shown-heading")!.Value, Is.EqualTo("Second trip"));
            Assert.That(Element(Empty, "empty")!.Value, Is.EqualTo("No campsite matched this search in the months checked."));
            Assert.That(Element(Empty, "rows-range"), Is.Null);
            Assert.That(Empty.Xml().Descendants("a").Single(A => (string?)A.Attribute("aria-current") == "page" && ((string?)A.Attribute("href") ?? "").StartsWith("/results?", StringComparison.Ordinal)).Value,
                Does.StartWith("Second trip (Bravo.yaml) · Complete: no matching availability"));
        }

        [Test]
        public async Task UntrustedResultText_IsShownAsText_AndOnlyRecreationGovCampsitePagesAreLinked()
        {
            var Night = new DateTime(2026, 12, 1);
            string[] Hostile = { "<script>alert(1)</script>", "\"><img src=x onerror=alert(1)>", "a&amp;b ‮ evil", "javascript:alert(1)" };
            //each a link of its own row's campsite (ID 2 to 8), so only the scheme, the host, the user, the port or the text refuses it
            string[] BadLinks =
            {
                "javascript:alert(1)", "https://evil.example/camping/campsites/3", "https://user:pw@www.recreation.gov/camping/campsites/4",
                "https://www.recreation.gov/camping/campsites/5\r\nSet-Cookie: x=1", "data:text/html,<script>alert(1)</script>", "/results?handler=start",
                "https://www.recreation.gov:8443/camping/campsites/8",
            };
            var Openings = Hostile.Select((Name, i) => Opening((10 + i).ToString(CultureInfo.InvariantCulture), Night, Name: Name, Loop: Name))
                .Concat(BadLinks.Select((Url, i) => Opening((2 + i).ToString(CultureInfo.InvariantCulture), Night.AddDays(1), Url: Url)))
                .OrderBy(O => O.Night).ToList();
            await using ResultsHost R = await ResultsHost.Start(Run: Returns(Result("Alpha.yaml", Openings, Name: "<b>Trip</b>", Campground: "<i>CAMP</i> & \"co\"")));
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
            WebPage Page = await Ended(Owner);

            XDocument Xml = Page.Xml();
            Assert.That(Xml.Descendants().Where(E => E.Name.LocalName is "script" or "img" or "b" or "i" or "iframe"), Is.Empty, "a name is never markup");
            Assert.That(Xml.Descendants().SelectMany(E => E.Attributes()).Where(A => A.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)), Is.Empty);
            Assert.That(Page.Text(), Does.Contain("Site <script>alert(1)</script>").And.Contain("<b>Trip</b>").And.Contain("<i>CAMP</i> & \"co\""));
            List<string> Hrefs = Xml.Descendants("a").Select(A => (string)A.Attribute("href")!).ToList();
            Assert.That(Hrefs.Where(H => !H.StartsWith("/", StringComparison.Ordinal) && !H.StartsWith("#", StringComparison.Ordinal)),
                Is.EquivalentTo(Enumerable.Range(10, Hostile.Length).Select(i => CampsiteSearch.BookingUrl(i.ToString(CultureInfo.InvariantCulture)))), "only the exact recreation.gov campsite pages");
            Assert.That(Hrefs.Where(H => H.StartsWith("/", StringComparison.Ordinal)), Has.None.Contain("handler"));
            Assert.That(Xml.Descendants("p").Count(P => (string?)P.Attribute("class") == "booking" && P.Value == ResultsWording.NoBookingLink), Is.EqualTo(BadLinks.Length));
            Assert.That(Page.Body, Does.Not.Contain("evil.example").And.Not.Contain("user:pw").And.Not.Contain("Set-Cookie: x"));
            Assert.That(Page.SetCookies.Where(C => C.StartsWith("x=", StringComparison.Ordinal)), Is.Empty);
        }

        [Test]
        public async Task ARunThatFails_SaysSoInFixedWords_AndNeverShowsAnExceptionOrAPath()
        {
            string? Folder = null;
            await using ResultsHost R = await ResultsHost.Start(Run: (Plan, Progress, Token, Options) => throw new IOException("secret detail " + Folder + " " + Plan.CachePath));
            Folder = R.Folder;
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
            WebPage Failed = await Ended(Owner);
            Assert.That(Status(Failed), Is.EqualTo("Nothing could be searched. Elapsed 0:00."));
            Assert.That(Element(Failed, "run-issues")!.Elements("li").Select(L => L.Value), Is.EqualTo(new[] { "Error: " + CampsiteSearch.Message(SearchIssueCode.InternalError) }));
            Assert.That(Failed.Body, Does.Not.Contain("secret").And.Not.Contain(R.Folder).And.Not.Contain(R.Cache).And.Not.Contain("IOException"));
            Assert.That(R.Host.Lines.Concat(R.Host.Warnings).Concat(R.Host.Errors), Is.Empty);
            Assert.That(Box(Failed, "Alpha.yaml").Parent!.Descendants("p").Single(P => (string?)P.Attribute("class") == "run-state").Value, Is.EqualTo(ResultsWording.NotSearched));
        }

        [Test]
        public async Task ASessionThatEnds_CancelsItsRun()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            R.Gov.Mode = Gov.Stall;
            using Browser First = await R.Host.SignedIn();
            await Act(First, "/results?handler=start", Fields(await First.Get("/results")));
            Assert.That(R.Gov.Stalled.Wait(TimeSpan.FromSeconds(15)), Is.True);
            WebPage Out = await First.Post("/signout");
            Assert.That(Out.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(R.Gov.Released.Wait(TimeSpan.FromSeconds(15)), Is.True, "the run of a session that ended is canceled");
            using Browser Second = await R.Host.SignedIn();
            R.Gov.Mode = Gov.Available;
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            WebPage Page = await Second.Get("/results");
            while (Element(Page, "other-session") != null && Clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(20);
                Page = await Second.Get("/results");
            }
            Assert.That(Element(Page, "other-session"), Is.Null, "the web UI's one run is free again");
            await Act(Second, "/results?handler=start", Fields(Page));
            Assert.That(Status(await Ended(Second)), Does.StartWith("Finished: "));
        }

        [Test]
        public async Task WithoutACacheFolder_NothingCanStart()
        {
            await using ResultsHost R = await ResultsHost.Start(WithCache: false);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            WebPage Page = await Owner.Get("/results");
            Assert.That(Status(Page), Is.EqualTo(ResultsWording.NoCacheFolder));
            Assert.That(Disabled(StartButton(Page)), Is.True);
            WebPage Refused = await Act(Owner, "/results?handler=start", Fields(Page));
            Assert.That(Status(Refused), Is.EqualTo(ResultsWording.NoCacheFolder));
            Assert.That(R.Gov.Requests, Is.Zero);
            Assert.That(Page.Body + Refused.Body, Does.Not.Contain(R.Folder).And.Not.Contain(Path.GetTempPath()), "no folder in the words");
        }

        [Test]
        public async Task TheResultsPagesPosts_NeedTheirTokenAndOrigin_AndStartIsRateLimited()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            WebPage Page = await Owner.Get("/results");
            Assert.That((await Owner.Post("/results?handler=start", Fields(Page).Append(("__RequestVerificationToken", "forged")).ToArray())).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/results?handler=start", Fields(Page))).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = R.Host.Root.GetLeftPart(UriPartial.Authority);
            Assert.That(R.Gov.Requests, Is.Zero);

            //out-of-date pages start nothing, and still count: a client may ask for RunsPerClient starts in a window (the
            //one with the forged token above counted; the one from another origin was refused before the limits)
            for (int i = 1; i < CampertronWebHost.RunsPerClient; i++)
            {
                Assert.That((await Owner.Post("/results?handler=start", ("revision", "0"))).Status, Is.EqualTo(HttpStatusCode.Redirect), "start " + i);
            }
            WebPage Limited = await Owner.Post("/results?handler=start", ("revision", "0"));
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That((await Owner.Post("/results", ("revision", "0"), ("action", "clear"))).Status, Is.EqualTo(HttpStatusCode.Redirect), "the choice has no limit of its own");
            Assert.That(R.Gov.Requests, Is.Zero);
        }

        [Test]
        public async Task EveryAction_GoesBackToThisPageOnly_WhateverTheRequestNames()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            foreach (string Handler in new[] { "", "?handler=cancel", "?handler=start&returnUrl=https%3A%2F%2Fevil.example%2F" })
            {
                WebPage Sent = await Owner.Post("/results" + Handler, Fields(await Owner.Get("/results"), "keep")
                    .Concat(new[] { ("returnUrl", "https://evil.example/"), ("next", "//evil.example/"), ("ReturnUrl", "/signout"), ("run", "1") }).ToArray());
                Assert.That((Sent.Status, Sent.Location), Is.EqualTo((HttpStatusCode.Redirect, "/results")), Handler);
            }
            Assert.That(Status(await Ended(Owner)), Does.StartWith("Finished: "), "the Start among them ran, and came back here");
        }

        [Test]
        public void TheResultsPagesCode_RunsOnlyGS1sManualSearch_AndReachesNoMonitoringNotificationOrHistory()
        {
            //the page model, the session's results, the web UI's runs and the page Razor compiled
            Type[] Types = typeof(ResultsModel).Assembly.GetTypes()
                .Where(T => T == typeof(ResultsModel) || (T.FullName ?? "").Contains("Results", StringComparison.Ordinal))
                .ToArray();
            Assert.That(Types.Select(T => T.Name), Does.Contain("Pages_Results").And.Contain("WebResults").And.Contain("WebResultsRuns"), "the compiled page is scanned too");
            List<string> Called = IlReferences.ReferencedMembers(Types).Select(C => C.Member).Distinct().ToList();
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                "CampertronLibrary.function.RecDotOrg.data.Load::", "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.Notifications",
                "CampertronLibrary.function.Base.ResultsEmail", "CampertronLibrary.function.Base.ResultsHtml", "CampsiteConfig::", "System.Net.Http.", "System.Net.Mail.",
                "System.Diagnostics.Process", "System.IO.File::", "System.IO.Directory::Delete", "System.IO.FileInfo::", "System.Console::", "ConfigFileReplace",
                "CampertronLibrary.function.Forms.MonitoringController", "CampertronWeb.WebMonitoring",
            };
            Assert.That(Called.Where(M => Forbidden.Any(F => M.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called.Where(M => M.StartsWith("CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::", StringComparison.Ordinal)).OrderBy(M => M, StringComparer.Ordinal),
                Is.EqualTo(new[] { "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::LoadEntry", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Plan", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Run" }),
                "GS1's listing, plan and run, and nothing else of it");

            //the page shows the layer's words and rows: it reads no result of GS1 itself, checks no link and builds no text
            Type[] Page = typeof(ResultsModel).Assembly.GetTypes().Where(T => (T.FullName ?? "").Contains("Pages_Results", StringComparison.Ordinal)).ToArray();
            List<string> Shown = IlReferences.ReferencedMembers(Page).Select(C => C.Member).Distinct().ToList();
            string[] NotInThePage = { "CampertronLibrary.function.RecDotOrg.search.", "BookingLink::", "ResultText::", "System.Uri::", "ResultsHtml::", "::get_BookingUrl", "::get_CampsiteId" };
            Assert.That(Shown.Where(M => NotInThePage.Any(F => M.Contains(F, StringComparison.Ordinal))), Is.Empty);
        }
    }
}
