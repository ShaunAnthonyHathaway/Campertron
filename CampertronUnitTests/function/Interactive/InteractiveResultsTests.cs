using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Results;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronLibrary.function.RecDotOrg.sqlite;
using CampertronUnitTests.function.Base;
using CampertronUnitTests.function.RecDotOrg.search;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Results.InteractiveResultsWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Results (v0.4.0's phase 10, D126; business-rules §43): the saved searches run now through
    // the shared search service (CampsiteSearch, the desktop GUI's Results tab's and the web UI's results page's) and the
    // shared form layer's Results (ResultsSearchList, ResultsStart, ResultsOutcome, ResultsListing, ResultsWording,
    // BlockHold), driven from the main menu by the terminal that answers a choice by its label or number and types keys
    // while a search runs (MenuTerminal), and judged by the words shown and the files left. recreation.gov is answered in
    // memory through the shared run's transport seam (no request leaves the process, NoLiveNetwork); the campground's
    // campsite details are written into the cache by hand (no database is opened); a scripted run reports progress and
    // waits for the console to have looked at it. Nothing may be sent, Camp.History never read, created, changed or
    // re-armed, and no exception's text or other settings file's secret shown (canaries).
    [NonParallelizable]
    public class InteractiveResultsTests : ConfigEditTestBase
    {
        private const string CampgroundId = "990801";
        private const string Canary = "cnry-results-3e9b";
        private static readonly DateTime UtcNow = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly List<string> Sites = new() { "9908011", "9908012", "9908013" };

        private string Root = string.Empty;
        private string Cache => Path.Join(Root, "cache");
        private DateTime MonthA;
        private DateTime MonthB;
        private InteractiveResultsSeams OriginalSeams = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Action<string> OriginalRemove = null!;
        private Counting Handler = null!;
        private ManualTime Clock = null!;
        private int EmailSends;
        private readonly ConcurrentQueue<string> Removed = new();
        private bool Prepared;
        private readonly ConcurrentQueue<SearchRequest> Requests = new();
        // what recreation.gov answers a month's request: by default every night of every campsite is available
        private Func<SearchRequest, CancellationToken, SearchResponse> Answer = null!;
        private Action? StopAction;
        private int StopRegistrations;
        private int StopDisposals;

        [SetUp]
        public void Seams()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronInteractiveResults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Cache);
            DateTime Ahead = DateTime.Now.AddMonths(2);
            MonthA = new DateTime(Ahead.Year, Ahead.Month, 1);
            MonthB = MonthA.AddMonths(1);
            OriginalSeams = InteractiveResults.Seams;
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalRemove = NotificationHistory.RemoveMarker;
            EmailSends = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => EmailSends++;
            Handler = new Counting();
            NtfyPublisher.CreateHandler = () => Handler;
            Removed.Clear();
            NotificationHistory.RemoveMarker = Marker =>
            {
                Removed.Enqueue(Marker);
                OriginalRemove(Marker);
            };
            Prepared = true;
            Requests.Clear();
            Answer = (Request, Token) => new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Sites, (Site, Day) => true));
            StopAction = null;
            StopRegistrations = 0;
            StopDisposals = 0;
            Clock = new ManualTime();
            InteractiveResults.Seams = With();
            WriteCampground();
        }

        [TearDown]
        public void RestoreSeams()
        {
            InteractiveResults.Seams = OriginalSeams;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            NotificationHistory.RemoveMarker = OriginalRemove;
            Handler.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private sealed class Counting : HttpMessageHandler
        {
            internal int Seen;
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Seen++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        private sealed class Undo : IDisposable
        {
            private readonly Action Do;
            internal Undo(Action Do) => this.Do = Do;
            public void Dispose() => Do();
        }

        // a clock the tests move: a run's elapsed time is 0:00 and a block's hold lasts unless a test moves it
        private sealed class ManualTime : TimeProvider
        {
            private long Ticks;
            public override long GetTimestamp() => Interlocked.Read(ref Ticks);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            internal void Advance(TimeSpan By) => Interlocked.Add(ref Ticks, By.Ticks);
        }

        // the seams of a test: the campground data's state and names, the clock, recreation.gov in memory (no waits between
        // attempts), the console's looks every millisecond, every change of the counts shown, the stop signals recorded
        private InteractiveResultsSeams With(Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>? Run = null,
            TimeSpan? CountInterval = null) => new InteractiveResultsSeams
        {
            Prepared = Folder_ => Prepared,
            Campgrounds = Folder_ => new List<CampgroundPickerEntry> { new CampgroundPickerEntry(new CampgroundChoice(CampgroundId, "FIXTURE CAMP", "SOMEWHERE", "CA"), "Fixture Park") },
            Time = Clock,
            Options = new SearchHostOptions { Transport = Send, RetryDelay = TimeSpan.Zero, HeavyTrafficDelay = TimeSpan.Zero },
            Run = Run ?? new Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>(CampsiteSearch.Run),
            PollInterval = TimeSpan.FromMilliseconds(1),
            CountInterval = CountInterval ?? TimeSpan.Zero,
            WhenAskedToStop = Act =>
            {
                StopAction = Act;
                StopRegistrations++;
                return new Undo(() => StopDisposals++);
            }
        };

        private SearchResponse Send(SearchRequest Request, CancellationToken Token)
        {
            Requests.Enqueue(Request);
            return Answer(Request, Token);
        }

        // the campground's campsite details, in the cache's own files (the completion marker last), as a search builds them
        private void WriteCampground(string Name = "FIXTURE CAMP", IReadOnlyList<string>? Ids = null, Func<string, string>? SiteName = null)
        {
            void Put(string File_, object Value) => File.WriteAllText(Path.Join(Cache, File_), JsonSerializer.Serialize(Value), new UTF8Encoding(false));
            IReadOnlyList<string> All = Ids ?? Sites;
            Put(CampgroundId + "-CampInfo.json", new ReturnParkCampground { CampsiteName = Name, ParkName = "Fixture Park" });
            Put(CampgroundId + "-Campsites.json", All.Select(Site => new CampsitesRecdata
            {
                CampsiteID = Site, FacilityID = CampgroundId, CampsiteName = SiteName?.Invoke(Site) ?? Site.Substring(Site.Length - 3), CampsiteType = "STANDARD NONELECTRIC", Loop = "A", TypeOfUse = "Overnight"
            }).ToList());
            Put(CampgroundId + "-CampsitesByPark.json", All.ToList());
            foreach (string Site in All)
            {
                Put(Site + "-Equipment.json", new List<string> { "Tent" });
                Put(Site + "-Attributes.json", new List<AttributeValuePair>());
            }
            Put(CampgroundId + "-Cached.json", "2026-11-27");
        }

        // a search file as a user writes it: StartEndDate over days of MonthA (and MonthB with TwoMonths)
        private string WriteSearch(string FileName, string Name, bool AutoRun = true, int Consecutive = 1, int FirstDay = 1, int LastDay = 10, bool TwoMonths = false, string Extra = "")
        {
            DateTime First = MonthA.AddDays(FirstDay - 1);
            DateTime Last = TwoMonths ? MonthB.AddDays(LastDay - 1) : MonthA.AddDays(LastDay - 1);
            string Text = "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '" + CampgroundId + "'\ntotalHumans: 2\nsearchBy: StartEndDate\nsearchValue: 0\n"
                + "searchValueDates:\n- " + First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n- " + Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "\n"
                + "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n"
                + "consecutiveDays: " + Consecutive.ToString(CultureInfo.InvariantCulture) + "\n" + Extra;
            File.WriteAllBytes(Path.Join(Folder, FileName), new UTF8Encoding(false).GetBytes(Text));
            return FileName;
        }

        private void WriteSettings()
        {
            Write(General, "outputTo: Email\nautoRefresh: false\nrefreshRidbDataDayInterval: 30\n");
            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: PwSmtp-" + Canary + "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
            Write(Path.Join(Folder, "Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: topic" + Canary.Replace("-", "") + "\n");
        }

        // ---- the script ----

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private const string BackToMain = "Back to the main menu";
        private static readonly object[] Into = { P(W.Results) };
        private static readonly object[] Start = { P(W.Results), P(RW.StartName) };
        // from a search's result after a run (one search): back to Results, then the main menu, then Exit
        private static readonly object[] FromResultToExit = { P(W.BackToResults), P(BackToMain), P("Exit") };
        private static readonly object[] BackToExit = { P(BackToMain), P("Exit") };

        private InteractiveSession Session(InteractiveTerminal Terminal, bool NoFolder = false, bool Container = false, string? CachePath = null) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Folder, CachePath = CachePath ?? Cache, ContainerMode = Container });

        private int Run(MenuTerminal Terminal, InteractiveSession? With_ = null) => InteractiveConsole.Run(With_ ?? Session(Terminal));

        private static Dictionary<string, string> Fingerprint(string Of) =>
            !Directory.Exists(Of) ? new Dictionary<string, string>() : Directory.GetFileSystemEntries(Of, "*", SearchOption.AllDirectories).ToDictionary(P_ => P_,
                P_ => File.Exists(P_) ? System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(P_))) + File.GetLastWriteTimeUtc(P_).Ticks : "dir");

        private void AssertNothingSentOrLeaked(MenuTerminal Terminal)
        {
            string All = Terminal.AllText + "\n" + Out + "\n" + Err;
            Assert.That(All, Does.Not.Contain(Canary), "no secret of another settings file and no problem's own text");
            Assert.That(All, Does.Not.Contain("Exception"), "no exception's name or text");
            Assert.That(All.Any(C => char.IsControl(C) && C != '\n'), Is.False, "no control character reaches the terminal");
            Assert.That((EmailSends, Handler.Seen), Is.EqualTo((0, 0)), "nothing was sent");
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "nothing written past the terminal");
        }

        // the console looked at the keys this many more times (its loop: the progress first, then the keys)
        private static void AwaitLooks(MenuTerminal Terminal, int More)
        {
            int Begin = Volatile.Read(ref Terminal.Polls);
            Assert.That(SpinWait.SpinUntil(() => Volatile.Read(ref Terminal.Polls) >= Begin + More, 20000), Is.True, "the console kept looking");
        }

        private static SearchProgress Snapshot(long Sequence, SearchRunStage Stage, (string File, SearchState State)[] Searches, int CampPlanned = 0, int CampPrepared = 0,
            int Planned = 0, int Completed = 0, int Failed = 0, int Retrying = 0) =>
            new SearchProgress(Sequence, Stage, CampPlanned, CampPrepared, Planned, Completed, Failed, Retrying, Searches.Select(S => new SearchProgressItem(S.File, S.State)).ToList());

        private static SearchResult Found(string File, string Name, IEnumerable<SearchOpening> Openings, SearchState State = SearchState.Complete, int Consecutive = 1)
        {
            List<SearchOpening> All = Openings.ToList();
            return new SearchResult(File, Name, true, CampgroundId, "FIXTURE CAMP", "Fixture Park", Consecutive, State, 1, 1,
                new[] { new SearchMonthResult(new DateTime(2027, 1, 1), true, null) }, All, CampsiteSearch.SitesOf(All), Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>());
        }

        private static SearchOpening Opening(string Site, DateTime Night, string? Url = null) =>
            new SearchOpening(Site, Site, "A", "STANDARD NONELECTRIC", new[] { "Tent" }, Night, null, null, 1, 6, Url ?? CampsiteSearch.BookingUrl(Site));

        private static SearchRunResult RunResult(SearchRunStatus Status, params SearchResult[] Searches) =>
            new SearchRunResult(Status, DateTime.UtcNow, TimeSpan.FromSeconds(2), 1, 1, 0, Searches, Array.Empty<SearchIssue>());

        // ---- the main menu and entering Results ----

        [Test]
        public void TheMainMenu_HasResultsBeforeMonitoringAndHelp_AndHelpSaysWhatItDoes()
        {
            Assert.That(InteractiveConsole.MainMenu().Select(I => I.Label), Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Guided setup", "Help", "Exit" }));
            string Help = string.Join("\n", InteractiveWording.HelpLines);
            Assert.That(Help, Does.Contain("Results: run saved search files now at recreation.gov"));
            Assert.That(Help, Does.Contain("never reads or changes Camp.History"));
            Assert.That(Help, Does.Contain("Esc or Ctrl+C cancels a running search"));
        }

        [Test]
        public void EnteringResults_ListsTheSearchFilesAndTheirChoice_AndStartsReadsAndWritesNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            WriteSearch("Later.yaml", "Later", AutoRun: false);
            Write(Path.Join(Folder, "Broken.yaml"), "displayName: Broken\nautoRun: true\ncampgroundID: '" + CampgroundId + "'\nsearchBy: Nowhere\n");
            Dictionary<string, string> Before = Fingerprint(Folder);
            Dictionary<string, string> CacheBefore = Fingerprint(Cache);
            var Terminal = new MenuTerminal(Script(Into, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Written, Does.Contain(("heading", W.ResultsTitle)));
            Assert.That(Terminal.Texts, Does.Contain(W.ResultsNote));
            string Listed = Terminal.AllText;
            Assert.That(Listed, Does.Contain("Broken.yaml [not chosen] \u00b7 Broken.yaml \u00b7 No campground \u00b7 Not enabled Can't be searched: The search file can't be searched until the errors the validation shows are fixed. Error: searchBy must be one of"),
                "a file that can't run: the shared row's words and why");
            Assert.That(Listed, Does.Contain("Trip [chosen] \u00b7 Trip.yaml \u00b7 FIXTURE CAMP \u2014 SOMEWHERE, CA \u2014 ID " + CampgroundId + " \u00b7 Enabled (autoRun): selected by default"),
                "the shared row's details, the campground by its name from the campground data");
            Assert.That(Listed, Does.Contain("Later [not chosen] \u00b7 Later.yaml"));
            Assert.That(Listed, Does.Contain("Not enabled: runs when selected"));
            Assert.That(Listed, Does.Contain("Trip [chosen] \u00b7 Trip.yaml"));
            Assert.That(Listed, Does.Contain("Enabled (autoRun): selected by default"));
            Assert.That(Terminal.Texts, Does.Contain(W.ChosenCount(1, 3)));
            Assert.That(Terminal.MenusOf(RW.RunHeading).Single().Skip(3), Is.EqualTo(new[] { RW.StartName, RW.SelectEnabledLabel, RW.SelectAllLabel, RW.ClearSelectionLabel, BackToMain }), "no results to show yet");
            Assert.That(Requests, Is.Empty, "nothing was searched");
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before), "no settings or search file and no Camp.History written");
            Assert.That(Fingerprint(Cache), Is.EqualTo(CacheBefore), "the cache untouched");
            Assert.That(StopRegistrations, Is.Zero);
            AssertNothingSentOrLeaked(Terminal);
        }

        [Test]
        public void WithoutSearchFiles_ResultsSaysSo_AndStartStartsNothing()
        {
            var Terminal = new MenuTerminal(Script(Start, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain(W.NoSearchFiles));
            Assert.That(Terminal.Texts, Does.Contain("Not started: no search is chosen."));
            Assert.That(Requests, Is.Empty);
            AssertNothingSentOrLeaked(Terminal);
        }

        [TestCase(false, false, W.FolderMissing)]
        [TestCase(true, false, W.ContainerFolderMissing)]
        [TestCase(false, true, W.NoFolder)]
        public void WithoutTheSettingsFolder_NothingIsListedOrCreated(bool Container, bool NoFolder, string Expected)
        {
            Directory.Delete(Folder, true);
            var Terminal = new MenuTerminal(Script(Into, new object[] { P("Exit") }));

            Assert.That(Run(Terminal, Session(Terminal, NoFolder, Container)), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain(Expected));
            Assert.That(Directory.Exists(Folder), Is.False, "the settings folder is never created");
            Assert.That(Requests, Is.Empty);
            Directory.CreateDirectory(Folder);
        }

        [TestCase(false, W.CacheFolderMissing)]
        [TestCase(true, W.ContainerCacheFolderMissing)]
        public void WithoutTheCacheFolder_StartStartsNothing_AndCreatesNothing(bool Container, string Expected)
        {
            WriteSearch("Trip.yaml", "Trip");
            string Missing = Path.Join(Root, "no-cache");
            var Terminal = new MenuTerminal(Script(Start, BackToExit));

            Assert.That(Run(Terminal, Session(Terminal, Container: Container, CachePath: Missing)), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain(Expected));
            Assert.That(Directory.Exists(Missing), Is.False, "the cache folder is never created here");
            Assert.That(Requests, Is.Empty);
        }

        [Test]
        public void EscCtrlCAndTheEndOfTheInput_LeaveResults()
        {
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(Into, new object[] { Esc }, Into, new object[] { CtrlC }, Into, new object[] { End }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Prompts.Count(T => T == "Choose a number from 1 to 10: "), Is.EqualTo(3), "Esc and Ctrl+C go back to the main menu");
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(Requests, Is.Empty);
        }

        // ---- the choice ----

        [Test]
        public void AFileIsChosenOrLeftOut_ByTheSharedRules_AndOneThatCantRunCantBeChosen()
        {
            WriteSearch("Broken.yaml", "Broken", Extra: "searchBy: Nowhere\n");
            WriteSearch("Later.yaml", "Later", AutoRun: false);
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(Into, new object[]
            {
                L("1"), L("2"), L("3"), P(RW.ClearSelectionLabel), P(RW.SelectAllLabel), P(RW.SelectEnabledLabel), BackToMain, P("Exit")
            }).Select(O => O is string S ? (object)P(S) : O).ToArray());

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Broken.yaml (Broken.yaml) can't be chosen. Can't be searched:", StringComparison.Ordinal)), Is.True, Terminal.AllText);
            Assert.That(Terminal.Texts, Does.Contain("Later (Later.yaml) is chosen."));
            Assert.That(Terminal.Texts, Does.Contain("Trip (Trip.yaml) is no longer chosen."));
            Assert.That(Terminal.Texts, Does.Contain(W.ChosenCount(1, 3)), "Later after Trip left out");
            Assert.That(Terminal.Texts, Does.Contain(W.NoneChosen), "Clear selection");
            Assert.That(Terminal.Texts, Does.Contain(W.ChosenCount(2, 3)), "Select all that can run: Later and Trip, never Broken");
            Assert.That(File.ReadAllText(Path.Join(Folder, "Later.yaml")), Does.Contain("autoRun: false"), "choosing changes no file");
            Assert.That(Requests, Is.Empty, "choosing searches nothing");
        }

        [Test]
        public void StartWithNothingChosen_OrNothingThatCanRun_StartsNothing()
        {
            WriteSearch("Later.yaml", "Later", AutoRun: false);
            var Terminal = new MenuTerminal(Script(Start, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Not started: no search is chosen."));
            Assert.That(Requests, Is.Empty);
        }

        [Test]
        public void AStartThatStartsNothing_KeepsTheLastResults()
        {
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            var Terminal = new MenuTerminal(Script(Start, new object[]
            {
                P(W.BackToResults), P(RW.ClearSelectionLabel), P(RW.StartName), P(W.ShowResults), P(W.BackToResults), P(BackToMain), P("Exit")
            }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Not started: no search is chosen."));
            Assert.That(Terminal.MenusOf(RW.RunHeading).Last(), Does.Contain(W.ShowResults), "the last results are still there");
            Assert.That(Terminal.MenusOf(RW.OpeningsName), Has.Count.EqualTo(2), "and shown again: the same run's openings");
            Assert.That(Terminal.MenusOf(RW.OpeningsName)[1], Is.EqualTo(Terminal.MenusOf(RW.OpeningsName)[0]));
            Assert.That(Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void AChosenFileTheDiskChangedSinceItWasListed_StartsNothing_AndTheListSaysWhich()
        {
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(Into, new object[]
            {
                (Action)(() => File.WriteAllText(Path.Join(Folder, "Trip.yaml"), "displayName: Trip\nautoRun: true\ncampgroundID: '" + CampgroundId + "'\nsearchBy: Nowhere\n")),
                P(RW.StartName)
            }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Search files changed on disk, so these are no longer chosen: Trip.yaml. Check the choice, then select Start again."));
            Assert.That(Terminal.Texts, Does.Contain("Not started: the search files changed on disk."));
            Assert.That(Requests, Is.Empty, "a file that can't run is never searched");
        }

        // ---- a run ----

        [Test]
        public void ASearch_RunsThroughTheSharedService_AndShowsItsResults_ChangingNoSearchFileAndNoHistory()
        {
            WriteSettings();
            string File_ = WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            byte[] Search = File.ReadAllBytes(Path.Join(Folder, File_));
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Requests.Select(R => R.Url), Is.EqualTo(new[] { AvailabilityApi_MonthUrl(MonthA) }), "one request: the shared run's month, once");
            Assert.That(Terminal.Texts, Does.Contain("Searching 1 search\u2026"));
            Assert.That(Terminal.Texts, Does.Contain(W.Searching("Trip (Trip.yaml)")));
            Assert.That(Terminal.Texts, Does.Contain(W.RunningHow));
            Assert.That(Terminal.Texts, Does.Contain("Finished: 6 openings found in 1 search. Elapsed 0:00."));
            Assert.That(Terminal.Written, Does.Contain(("heading", RW.LastRunHeading)));
            Assert.That(Terminal.Written, Does.Contain(("heading", "Trip")));
            Assert.That(Terminal.Texts, Does.Contain("  Complete: 6 openings found"));
            Assert.That(Terminal.Texts, Does.Contain("  FIXTURE CAMP \u00b7 Fixture Park \u00b7 ID " + CampgroundId));
            Assert.That(Terminal.Texts, Does.Contain("  6 openings at 3 campsites on 2 nights"));
            List<string> Openings = Terminal.MenusOf(RW.OpeningsName).Single();
            Assert.That(Openings, Has.Count.EqualTo(7), "six openings and Back");
            Assert.That(Openings[0], Does.StartWith(MonthA.ToString("D", CultureInfo.CurrentCulture) + ": Site 011, ID 9908011"));
            Assert.That(File.ReadAllBytes(Path.Join(Folder, File_)), Is.EqualTo(Search), "the search file byte for byte, autoRun included");
            Assert.That(Directory.Exists(Path.Join(Folder, "Camp.History")), Is.False, "Camp.History never created");
            Assert.That(File.Exists(Path.Join(Folder, MonitoringLock.FileName)), Is.False, "the monitoring's lock never taken");
            Assert.That((StopRegistrations, StopDisposals), Is.EqualTo((1, 1)), "the stop signals only while it ran");
            Assert.That(Terminal.OperationKeys, Is.EqualTo(1));
            AssertNothingSentOrLeaked(Terminal);
        }

        private string AvailabilityApi_MonthUrl(DateTime Month) => CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.MonthUrl(CampgroundId, Month);

        [Test]
        public void ASearchThatFindsNothing_SaysSoInTheSharedWords()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = (Request, Token) => new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Sites, (Site, Day) => false));
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Finished: 0 openings found in 1 search. Elapsed 0:00."));
            Assert.That(Terminal.Texts, Does.Contain("  Complete: no matching availability in the months checked"));
            Assert.That(Terminal.Texts, Does.Contain("  No campsite matched this search in the months checked."));
            Assert.That(Terminal.MenusOf(RW.OpeningsName).Single(), Is.EqualTo(new[] { W.BackToResults }));
        }

        [Test]
        public void SeveralSearches_AreChosenFromBySharedLabels_AndAFileThatCantRunIsNeverSearched()
        {
            WriteSearch("Alpha.yaml", "Alpha", LastDay: 2);
            WriteSearch("Beta.yaml", "Beta", AutoRun: false, FirstDay: 3, LastDay: 4);
            WriteSearch("Gamma.yaml", "Gamma", Extra: "searchBy: Nowhere\n");
            var Terminal = new MenuTerminal(Script(Into, new object[]
            {
                P(RW.SelectAllLabel), P(RW.StartName), L("2"), P(W.BackToSearches), P(W.BackToResults), P(BackToMain), P("Exit")
            }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Requests.Select(R => R.Url).Distinct(), Is.EqualTo(new[] { AvailabilityApi_MonthUrl(MonthA) }), "one month for both: the shared run's sharing");
            Assert.That(Requests, Has.Count.EqualTo(1));
            Assert.That(Terminal.Texts, Does.Contain("Finished: 12 openings found in 2 searches. Elapsed 0:00."));
            Assert.That(Terminal.MenusOf(RW.ResultsOfName).First(), Is.EqualTo(new[] { "Alpha (Alpha.yaml) \u00b7 Complete: 6 openings found", "Beta (Beta.yaml) \u00b7 Complete: 6 openings found", W.BackToResults }),
                "the run's searches in the plan's order, by the shared labels");
            Assert.That(Terminal.Written, Does.Contain(("heading", "Beta")));
            Assert.That(Terminal.AllText, Does.Not.Contain("Gamma (Gamma.yaml) \u00b7"), "a file that can't run is not part of the run");
        }

        // ---- progress ----

        [Test]
        public void Progress_ShowsEachStageAndStateOnce_TheCountsWhenTheyChange_AndNeverAnEarlierSnapshot()
        {
            WriteSearch("Trip.yaml", "Trip");
            MenuTerminal Terminal = null!;
            (string, SearchState)[] Waiting = { ("Trip.yaml", SearchState.Waiting) };
            (string, SearchState)[] Searching = { ("Trip.yaml", SearchState.Searching) };
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                foreach (SearchProgress Each in new[]
                {
                    Snapshot(1, SearchRunStage.Validating, Waiting),
                    Snapshot(2, SearchRunStage.PreparingCampsiteDetails, Waiting, 1, 0),
                    Snapshot(3, SearchRunStage.PreparingCampsiteDetails, Waiting, 1, 1),
                    Snapshot(4, SearchRunStage.CheckingAvailability, Searching, 1, 1, 2, 0),
                    Snapshot(5, SearchRunStage.CheckingAvailability, Searching, 1, 1, 2, 1),
                    //an earlier snapshot that arrives late: never shown
                    Snapshot(3, SearchRunStage.Validating, Waiting, 9, 9),
                    Snapshot(6, SearchRunStage.CheckingAvailability, Searching, 1, 1, 2, 1),
                    Snapshot(7, SearchRunStage.Retrying, Searching, 1, 1, 2, 1, 0, 1),
                    Snapshot(8, SearchRunStage.BuildingResults, new[] { ("Trip.yaml", SearchState.Complete) }, 1, 1, 2, 2)
                })
                {
                    Progress!.Report(Each);
                    AwaitLooks(Terminal, 2);
                }
                return RunResult(SearchRunStatus.Completed, Found("Trip.yaml", "Trip", Array.Empty<SearchOpening>()));
            });
            Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            List<string> Lines = Terminal.Texts;
            int From = Lines.IndexOf(W.RunningHow);
            int To = Lines.IndexOf("Finished: 0 openings found in 1 search. Elapsed 0:00.");
            Assert.That(Lines.Skip(From + 1).Take(To - From - 1), Is.EqualTo(new[]
            {
                "Reading the searches",
                "Preparing campsite details",
                "  Campsite details: 0 of 1 campground ready \u00b7 Elapsed 0:00",
                "  Campsite details: 1 of 1 campground ready \u00b7 Elapsed 0:00",
                "Checking availability at recreation.gov",
                W.SearchState("Trip (Trip.yaml)", "Searching"),
                "  Campsite details: 1 of 1 campground ready \u00b7 Availability: 0 of 2 month requests finished \u00b7 Elapsed 0:00",
                "  Campsite details: 1 of 1 campground ready \u00b7 Availability: 1 of 2 month requests finished \u00b7 Elapsed 0:00",
                "Checking availability; 1 request is waiting to be tried again (each month is asked for at most three times)",
                "  Campsite details: 1 of 1 campground ready \u00b7 Availability: 1 of 2 month requests finished, 1 waiting to be tried again \u00b7 Elapsed 0:00",
                "Building the results",
                W.SearchState("Trip (Trip.yaml)", "Complete"),
                "  Campsite details: 1 of 1 campground ready \u00b7 Availability: 2 of 2 month requests finished \u00b7 Elapsed 0:00"
            }), "each change once, in order; the late earlier snapshot and the repeated counts never");
        }

        [Test]
        public void Counts_AreShownAtMostOnceEveryInterval_WithTheElapsedTime()
        {
            WriteSearch("Trip.yaml", "Trip");
            MenuTerminal Terminal = null!;
            (string, SearchState)[] Searching = { ("Trip.yaml", SearchState.Searching) };
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                for (int i = 0; i < 4; i++)
                {
                    Progress!.Report(Snapshot(i + 1, SearchRunStage.CheckingAvailability, Searching, 0, 0, 10, i));
                    AwaitLooks(Terminal, 2);
                    Clock.Advance(TimeSpan.FromSeconds(2));
                }
                return RunResult(SearchRunStatus.Completed, Found("Trip.yaml", "Trip", Array.Empty<SearchOpening>()));
            }, CountInterval: TimeSpan.FromSeconds(3));
            Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            List<string> Counts = Terminal.Texts.Where(T => T.StartsWith("  Availability:", StringComparison.Ordinal)).ToList();
            Assert.That(Counts, Is.EqualTo(new[] { "  Availability: 0 of 10 month requests finished \u00b7 Elapsed 0:00", "  Availability: 2 of 10 month requests finished \u00b7 Elapsed 0:04" }),
                "the first counts at once, then at most once every three seconds");
            Assert.That(Terminal.Texts, Does.Contain("Finished: 0 openings found in 1 search. Elapsed 0:08."));
        }

        // ---- recreation.gov's answers ----

        [Test]
        public void ABlock_StopsTheRun_AndHoldsStartForFiveMinutes_WithNoRequestOrRetryByTheConsole()
        {
            WriteSearch("Trip.yaml", "Trip", TwoMonths: true);
            Answer = (Request, Token) => new SearchResponse(429, "slow down");
            var Terminal = new MenuTerminal(Script(Start, new object[]
            {
                P(W.BackToResults), P(RW.StartName), (Action)(() => Clock.Advance(TimeSpan.FromMinutes(5))), P(RW.StartName), P(W.BackToResults), P(BackToMain), P("Exit")
            }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.AllText, Does.Contain("Stopped: recreation.gov limited the requests, so no further request was made."));
            Assert.That(Terminal.AllText, Does.Contain("Error: recreation.gov asked for fewer requests (HTTP 429), so no further request was made. Try again later."));
            Assert.That(Terminal.Texts, Does.Contain("recreation.gov limited Campertron's requests. Start is available again in 5:00."), "Start within five minutes starts nothing");
            Assert.That(Terminal.Texts.Count(T => T == "Searching 1 search\u2026"), Is.EqualTo(2), "a second run only after the five minutes");
            Assert.That(Requests, Has.Count.EqualTo(2), "one request a run: none after the block, none retried by the console");
        }

        [Test]
        public void AFailedMonth_IsNotChecked_InTheSharedWords_AfterTheSharedRunsOwnAttempts_AndNothingIsRerun()
        {
            WriteSearch("Trip.yaml", "Trip", TwoMonths: true);
            Answer = (Request, Token) => Request.Month == MonthB ? new SearchResponse(503, "busy " + Canary)
                : new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Sites, (Site, Day) => Day == 1));
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Requests.Count(R => R.Month == MonthB), Is.EqualTo(SearchHostOptions.MaxAttempts), "the shared run's three attempts, then not checked");
            Assert.That(Requests.Count(R => R.Month == MonthA), Is.EqualTo(1));
            Assert.That(Terminal.AllText, Does.Contain("Finished with problems: some searches or months were not checked."));
            Assert.That(Terminal.Texts, Does.Contain("  Incomplete: some months were not checked; 3 openings found in the others"));
            Assert.That(Terminal.AllText, Does.Contain(MonthB.ToString("Y", CultureInfo.CurrentCulture) + " (recreation.gov answered with an error)"));
            Assert.That(Terminal.Texts.Count(T => T == "Searching 1 search\u2026"), Is.EqualTo(1), "never run again by the console");
            AssertNothingSentOrLeaked(Terminal);
        }

        [Test]
        public void ARunThatThrows_IsTheSharedFailedRun_NamedByItsTypeOnly()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) => throw new InvalidDataException("secret " + Canary));
            var Terminal = new MenuTerminal(Script(Start, new object[] { P(W.BackToResults), P(BackToMain), P("Exit") }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Nothing could be searched. Elapsed 0:00."));
            Assert.That(Terminal.AllText, Does.Contain("Error: The search stopped because of an unexpected problem in Campertron."));
            Assert.That(Terminal.AllText, Does.Not.Contain(Canary));
            Assert.That(Terminal.AllText, Does.Not.Contain("InvalidData"));
        }

        // ---- cancellation ----

        [Test]
        public void EscBeforeTheFirstRequest_CancelsTheSharedRun_AndNoRequestIsMade()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Assert.That(Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)), Is.True, "the console's Esc reached the run's token");
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            });
            var Terminal = new MenuTerminal(Script(Start, new object[] { new Press(TerminalInputKind.Escape) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Requests, Is.Empty, "canceled before any request");
            Assert.That(Terminal.Texts, Does.Contain(RW.Canceling));
            Assert.That(Terminal.AllText, Does.Contain("Canceled. Searches that had finished keep their results;"));
            Assert.That(Terminal.Texts, Does.Contain("  Canceled before it finished"));
            Assert.That(Terminal.Texts, Does.Contain("  The search was canceled before it found anything. Months not checked are not shown as without availability."));
            Assert.That(Terminal.Written.Where(W_ => W_.Kind == "stopped"), Is.Empty, "a key never ends the process while a search runs");
        }

        [Test]
        public void CtrlCDuringARequest_CancelsItsToken_AndTheRunIsWaitedFor_AndRepeatedKeysSaySoOnce()
        {
            WriteSearch("Trip.yaml", "Trip");
            var Asked = new ManualResetEventSlim();
            bool RequestCanceled = false;
            Answer = (Request, Token) =>
            {
                Asked.Set();
                RequestCanceled = Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                Token.ThrowIfCancellationRequested();
                return new SearchResponse(200, "{}");
            };
            var Terminal = new MenuTerminal(Script(Start, new object[]
            {
                new Press(TerminalInputKind.Interrupt, () => Asked.IsSet), new Press(TerminalInputKind.Escape), new Press(TerminalInputKind.Interrupt)
            }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(RequestCanceled, Is.True, "the request in flight was canceled through the shared run's token");
            Assert.That(Requests, Has.Count.EqualTo(1), "no request after the cancel and none retried");
            Assert.That(Terminal.Texts.Count(T => T == RW.Canceling), Is.EqualTo(1));
            Assert.That(Terminal.Texts.Count(T => T == W.StillCanceling), Is.EqualTo(1), "a key again says it once");
            Assert.That(Terminal.AllText, Does.Contain("Canceled. Searches that had finished keep their results;"));
            Assert.That(Terminal.Written.Where(W_ => W_.Kind == "stopped"), Is.Empty);
            Assert.That(Terminal.KeysAsTyped, Is.False, "Ctrl+C is a key only while the search ran");
        }

        [Test]
        public void ACancelWhileTheResultsAreBuilt_IsTheSharedCanceledSearch()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) => CampsiteSearch.Run(Plan, new Pausing(Progress!, Token), Token, Options));
            MenuTerminal Terminal = null!;
            Terminal = new MenuTerminal(Script(Start, new object[] { new Press(TerminalInputKind.Escape, () => Terminal.Texts.Contains("Building the results")) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Pausing.AtResults, Is.True, "the run was building its results when it was canceled");
            Assert.That(Terminal.AllText, Does.Contain("Canceled. Searches that had finished keep their results;"));
            Assert.That(Terminal.Texts, Does.Contain("  Canceled before it finished"), "not shown as a completed search");
            Assert.That(Terminal.MenusOf(RW.OpeningsName).Single(), Is.EqualTo(new[] { W.BackToResults }), "no opening of a search whose results were not built");
        }

        // the shared run's progress, passed on, and held at the results' stage until the console cancels (or a while)
        private sealed class Pausing : IProgress<SearchProgress>
        {
            internal static volatile bool AtResults;
            private readonly IProgress<SearchProgress> Inner;
            private readonly CancellationToken Token;
            internal Pausing(IProgress<SearchProgress> Inner, CancellationToken Token)
            {
                this.Inner = Inner;
                this.Token = Token;
                AtResults = false;
            }
            public void Report(SearchProgress Value)
            {
                Inner.Report(Value);
                if (Value.Stage == SearchRunStage.BuildingResults)
                {
                    AtResults = true;
                    Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                }
            }
        }

        [Test]
        public void ARunThatFinishedBeforeItsCancelWasSeen_IsShownAsItEnded()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                //the run had already finished its work: the cancel came too late, and its complete result stands
                return RunResult(SearchRunStatus.Completed, Found("Trip.yaml", "Trip", new[] { Opening("9908011", MonthA) }));
            });
            var Terminal = new MenuTerminal(Script(Start, new object[] { new Press(TerminalInputKind.Escape) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain(RW.Canceling));
            Assert.That(Terminal.Texts, Does.Contain("Finished: 1 opening found in 1 search. Elapsed 0:00."), "its end as it ended, never called canceled");
            Assert.That(Terminal.Texts, Does.Contain("  Complete: 1 opening found"));
        }

        [Test]
        public void TheEndOfTheInput_WhileItRuns_CancelsIt_AndTheConsoleEndsWhenItHasStopped()
        {
            WriteSearch("Trip.yaml", "Trip");
            bool Waited = false;
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Assert.That(Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)), Is.True);
                Thread.Sleep(50);
                Waited = true;
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            });
            var Terminal = new MenuTerminal(Script(Start, new object[] { new Press(TerminalInputKind.EndOfInput) }));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Waited, Is.True, "the run was waited for, never abandoned");
            Assert.That(Terminal.Texts, Does.Contain(W.InputEndedDuringSearch));
            Assert.That(Terminal.AllText, Does.Contain("Canceled. Searches that had finished keep their results;"));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(Requests, Is.Empty);
        }

        [Test]
        public void ATerminalThatIsGone_WhileItRuns_CancelsIt_AndTheConsoleEnds()
        {
            WriteSearch("Trip.yaml", "Trip");
            bool Waited = false;
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Assert.That(Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)), Is.True, "a terminal that is gone cancels the search");
                Waited = true;
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            });
            var Terminal = new GoneTerminal(Script(Start));

            Assert.That(InteractiveConsole.Run(Session(Terminal)), Is.EqualTo(0));

            Assert.That(Waited, Is.True);
            Assert.That(Requests, Is.Empty);
        }

        // a terminal whose writes fail once a search runs (a closed window whose hang-up was ignored)
        private sealed class GoneTerminal : InteractiveTerminal
        {
            private readonly MenuTerminal Inner;
            private bool Gone;
            internal GoneTerminal(object[] Script) => Inner = new MenuTerminal(Script);
            //the screen (since D130): as the terminal it wraps
            public override bool ClearScreen() => Inner.ClearScreen();
            public override void WriteLogo(string Text) => Inner.WriteLogo(Text);
            public override TerminalInput ReadContinue() => Inner.ReadContinue();
            public override int Width => int.MaxValue;
            public override bool CanHideInput => false;
            private void Write(Action Do)
            {
                if (Gone)
                {
                    throw new IOException("gone");
                }
                Do();
            }
            public override void WriteLine(string Text) => Write(() => Inner.WriteLine(Text));
            public override void WriteHeading(string Text) => Write(() => Inner.WriteHeading(Text));
            public override void WriteWarning(string Text) => Write(() => Inner.WriteWarning(Text));
            public override void WriteError(string Text) => Write(() => Inner.WriteError(Text));
            public override void WriteStopped() => Write(() => Inner.WriteStopped());
            public override void WritePrompt(string Text) => Write(() => Inner.WritePrompt(Text));
            public override TerminalInput ReadLine() => Inner.ReadLine();
            public override TerminalInput ReadSecret() => TerminalInput.Unavailable;
            public override IDisposable ReadKeysAsTheyAreTyped()
            {
                Gone = true;
                return Inner.ReadKeysAsTheyAreTyped();
            }
            public override TerminalInputKind? NextKeyTyped() => Inner.NextKeyTyped();
        }

        [Test]
        public void AnUnexpectedProblemOfTheTerminal_WhileItRuns_NeverLeavesTheRunRunning()
        {
            WriteSearch("Trip.yaml", "Trip");
            bool Waited = false;
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Assert.That(Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)), Is.True, "the run is canceled when the console's watch ends");
                Thread.Sleep(50);
                Waited = true;
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            });
            //the choice's problem is said, and the main menu comes again
            var Terminal = new FailingKeysTerminal(Script(Start, new object[] { P("Exit") }));

            Assert.That(InteractiveConsole.Run(Session(Terminal)), Is.EqualTo(0));

            Assert.That(Waited, Is.True, "waited for before the console went on");
            Assert.That(Terminal.Inner.Written, Does.Contain(("error", "That could not be done because of an unexpected problem (InvalidOperationException).")));
            Assert.That(Requests, Is.Empty);
        }

        // a terminal whose keys can't be read while a search runs (an unexpected problem of the terminal)
        private sealed class FailingKeysTerminal : InteractiveTerminal
        {
            internal readonly MenuTerminal Inner;
            internal FailingKeysTerminal(object[] Script) => Inner = new MenuTerminal(Script);
            //the screen (since D130): as the terminal it wraps
            public override bool ClearScreen() => Inner.ClearScreen();
            public override void WriteLogo(string Text) => Inner.WriteLogo(Text);
            public override TerminalInput ReadContinue() => Inner.ReadContinue();
            public override int Width => int.MaxValue;
            public override bool CanHideInput => false;
            public override void WriteLine(string Text) => Inner.WriteLine(Text);
            public override void WriteHeading(string Text) => Inner.WriteHeading(Text);
            public override void WriteWarning(string Text) => Inner.WriteWarning(Text);
            public override void WriteError(string Text) => Inner.WriteError(Text);
            public override void WriteStopped() => Inner.WriteStopped();
            public override void WritePrompt(string Text) => Inner.WritePrompt(Text);
            public override TerminalInput ReadLine() => Inner.ReadLine();
            public override TerminalInput ReadSecret() => TerminalInput.Unavailable;
            public override IDisposable ReadKeysAsTheyAreTyped() => Inner.ReadKeysAsTheyAreTyped();
            public override TerminalInputKind? NextKeyTyped() => throw new InvalidOperationException("the keys can't be read " + Canary);
        }

        [Test]
        public void ARequestToStop_WhileItRuns_CancelsIt_AndTheConsoleEndsWithZero()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
            {
                Assert.That(SpinWait.SpinUntil(() => StopAction != null, 20000), Is.True);
                StopAction!();
                Assert.That(Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)), Is.True, "SIGTERM or SIGHUP cancels the search");
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            });
            var Terminal = new MenuTerminal(Script(Start));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain(W.AskedToStop));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.AskedToStop));
            Assert.That((StopRegistrations, StopDisposals), Is.EqualTo((1, 1)));
            Assert.That(Requests, Is.Empty);
        }

        // ---- the results ----

        [Test]
        public void ManyOpenings_AreShownAPartAtATime_WithTheRangeAndTheSharedNextAndPreviousRows()
        {
            WriteCampground(Ids: Enumerable.Range(1, 6).Select(N => "99080" + N.ToString("00", CultureInfo.InvariantCulture)).ToList());
            List<string> Six = Enumerable.Range(1, 6).Select(N => "99080" + N.ToString("00", CultureInfo.InvariantCulture)).ToList();
            Answer = (Request, Token) => new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Six, (Site, Day) => true));
            WriteSearch("Trip.yaml", "Trip", LastDay: 10);
            var Terminal = new MenuTerminal(Script(Start, new object[] { P(RW.NextRows), P(RW.NextRows), P(RW.PreviousRows) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Finished: 60 openings found in 1 search. Elapsed 0:00."));
            Assert.That(Terminal.Texts.Where(T => T.StartsWith("Showing openings", StringComparison.Ordinal)), Is.EqualTo(new[]
            {
                "Showing openings 1 to 25 of 60.", "Showing openings 26 to 50 of 60.", "Showing openings 51 to 60 of 60.", "Showing openings 26 to 50 of 60."
            }));
            Assert.That(Terminal.MenusOf(RW.OpeningsName).Select(M => M.Count), Is.EqualTo(new[] { 25 + 2, 25 + 3, 10 + 2, 25 + 3 }), "never more than a part: its rows, Next, Previous and Back");
            Assert.That(Terminal.MenusOf(RW.OpeningsName)[1].Skip(25), Is.EqualTo(new[] { RW.NextRows, RW.PreviousRows, W.BackToResults }));
        }

        [Test]
        public void AnOpening_ShowsItsCampsite_ItsBookingLinkAloneOnItsLine_AndItsIdToCopy()
        {
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            var Terminal = new MenuTerminal(Script(Start, new object[] { L("2"), P(W.ShowLink), P(W.ShowId), P(W.BackToRows) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            string Link = "https://www.recreation.gov/camping/campsites/9908012";
            Assert.That(Terminal.Written, Does.Contain(("heading", "Site 012")));
            Assert.That(Terminal.Texts, Does.Contain("  " + W.CampsiteIdLabel + ": 9908012"));
            Assert.That(Terminal.Texts, Does.Contain("  " + MonthA.ToString("D", CultureInfo.CurrentCulture) + ": Site 012, ID 9908012, A \u00b7 STANDARD NONELECTRIC \u00b7 Tent \u00b7 1 to 8 people"));
            Assert.That(Terminal.Texts, Does.Contain("Booking page of Site 012:"));
            Assert.That(Terminal.Texts.Count(T => T == Link), Is.EqualTo(4), "the link alone on its line: in the details each of the three times they came, and once on request");
            Assert.That(Terminal.Texts.Count(T => T == "9908012"), Is.EqualTo(1), "the ID alone on request");
            Assert.That(Terminal.Texts.Count(T => T == W.CopyHow), Is.EqualTo(2));
            AssertNothingSentOrLeaked(Terminal);
        }

        [Test]
        public void ALinkTheSharedCheckRefuses_IsNeverShown_AndSaysWhy()
        {
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResults.Seams = With((Plan, Progress, Token, Options) =>
                RunResult(SearchRunStatus.Completed, Found("Trip.yaml", "Trip", new[] { Opening("9908011", MonthA, "https://evil.example.invalid/camping/campsites/9908011") })));
            var Terminal = new MenuTerminal(Script(Start, new object[] { L("1"), P(W.BackToRows) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain(RW.NoBookingLink));
            Assert.That(Terminal.AllText, Does.Not.Contain("evil.example"));
            Assert.That(Terminal.MenusOf(W.Choices).Single(), Is.EqualTo(new[] { W.ShowId, W.BackToRows }), "no link to copy");
        }

        [Test]
        public void ConsecutiveNights_AreStaysAtOneCampsiteEach_AsTheSharedResultBuildsThemToday()
        {
            //TD-65 unchanged: a stay is at one campsite; a run of nights across campsites is no stay
            WriteSearch("Trip.yaml", "Trip", Consecutive: 2, LastDay: 6);
            Answer = (Request, Token) => new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Sites,
                (Site, Day) => Site == "9908011" ? Day <= 2 : Site == "9908012" ? Day == 3 || Day == 4 : Day == 2 || Day == 4));
            var Terminal = new MenuTerminal(Script(Start, new object[] { L("1"), P(W.BackToRows) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("  2 campsites with at least 2 nights in a row (4 nights in all)"));
            string First = MonthA.ToString("D", CultureInfo.CurrentCulture);
            Assert.That(Terminal.AllText, Does.Contain("Site 011, ID 9908011: Arrive " + First + ", leave " + MonthA.AddDays(2).ToString("D", CultureInfo.CurrentCulture) + " \u00b7 2 nights"));
            Assert.That(Terminal.AllText, Does.Contain("Site 012, ID 9908012: Arrive " + MonthA.AddDays(2).ToString("D", CultureInfo.CurrentCulture)));
            Assert.That(Terminal.AllText, Does.Not.Contain("ID 9908013:"), "a campsite free on two nights that do not follow each other is no stay");
            Assert.That(Terminal.Texts, Does.Contain("  " + W.StaysLabel + ": 1 stay \u00b7 2 nights"));
            Assert.That(Terminal.AllText, Does.Not.Contain("same campsite"), "no same-campsite option (TD-65 is phase 23a's)");
        }

        [Test]
        public void ControlCharactersOfAFileOrOfRecreationGov_NeverReachTheTerminal()
        {
            WriteCampground(Name: "EVIL\u001b[31mCAMP\u0007", SiteName: Site => "S\u001b]0;t\u0007" + Site.Substring(Site.Length - 1));
            WriteSearch("Evil.yaml", "\"Evil\\e[2J name\"", LastDay: 2);
            var Terminal = new MenuTerminal(Script(Start, new object[] { L("1"), P(W.BackToRows) }, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.AllText, Does.Contain("EVIL\uFFFD[31mCAMP\uFFFD"), "the campground's name with its control characters replaced");
            Assert.That(Terminal.AllText, Does.Contain("Evil\uFFFD[2J name [chosen]"), "the search's own name (an escape in YAML) too, in the list");
            Assert.That(Terminal.AllText, Does.Contain("Evil\uFFFD[2J name (Evil.yaml)"), "and while it runs");
            AssertNothingSentOrLeaked(Terminal);
        }

        [Test]
        public void TheResults_StayForTheSession_UntilTheNextRun_AndANewConsoleHasNone()
        {
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            var Terminal = new MenuTerminal(Script(Start, new object[]
            {
                P(W.BackToResults), P(BackToMain), P("Status"), P(W.Results), (Action)(() => Clock.Advance(TimeSpan.FromMinutes(3))), P(W.ShowResults), P(W.BackToResults), P(BackToMain), P("Exit")
            }));
            InteractiveSession Kept = Session(Terminal);

            Assert.That(Run(Terminal, Kept), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("This run ended 3 minutes ago. Availability may have changed since then: the booking page shows what is available now."));
            Assert.That(Terminal.Texts.Count(T => T == "Finished: 6 openings found in 1 search. Elapsed 0:00."), Is.EqualTo(5),
                "said at its end, at each of the three times Results' choices came, and with the results looked at later");
            Assert.That(Terminal.AllText, Does.Contain("Last run: Complete: 6 openings found"), "the row says what the last run did");
            Assert.That(Requests, Has.Count.EqualTo(1), "looking again asks recreation.gov nothing");

            var Fresh = new MenuTerminal(Script(Into, BackToExit));
            Assert.That(Run(Fresh), Is.EqualTo(0));
            Assert.That(Fresh.MenusOf(RW.RunHeading).Single(), Does.Not.Contain(W.ShowResults), "another console's session has no results");
        }

        [Test]
        public void OnlyTheLatestRunsEnd_IsTakenOver_AndAnEarlierRunsResultNeverReplacesIt()
        {
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            var Terminal = new MenuTerminal(Script(Start, new object[] { P(W.BackToResults), P(RW.StartName), P(W.BackToResults), P(BackToMain), P("Exit") }));
            InteractiveSession Kept = Session(Terminal);
            Assert.That(Run(Terminal, Kept), Is.EqualTo(0), Terminal.AllText);
            InteractiveResultsState State = Kept.ResultsState!;
            ResultsOutcome Latest = State.Outcome!;
            Assert.That(State.LastRun, Is.EqualTo(2));

            var Earlier = new SearchOperation(1, new[] { "Trip.yaml" }, 0);
            bool Taken = State.Ended(Earlier, RunResult(SearchRunStatus.Completed, Found("Trip.yaml", "Trip", Array.Empty<SearchOpening>())), 0, "0:00");

            Assert.That(Taken, Is.False);
            Assert.That(State.Outcome, Is.SameAs(Latest), "the latest run's results stay");
            Assert.That(State.OutcomeOf, Is.EqualTo(2));
            Assert.That(State.Status, Is.EqualTo("Finished: 6 openings found in 1 search. Elapsed 0:00."));
        }

        [Test]
        public void AnEarlierSnapshot_NeverReplacesALaterOne()
        {
            var Op = new SearchOperation(1, new[] { "Trip.yaml" }, 0);
            Op.Report(Snapshot(5, SearchRunStage.CheckingAvailability, Array.Empty<(string, SearchState)>()));
            Op.Report(Snapshot(4, SearchRunStage.Validating, Array.Empty<(string, SearchState)>()));
            Assert.That(Op.Progress!.Sequence, Is.EqualTo(5));
            Op.Ended();
        }

        // ---- Camp.History, the monitoring and the other operations ----

        [Test]
        public void CampHistory_IsNeitherReadNorWritten_ByTheConsolesSearch_AndNoMarkerHidesAResult()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            Answer = (Request, Token) => new SearchResponse(200, SearchTestBase.MonthJson(Request.Month, Sites, (Site, Day) => Site != "9908013"));
            //every opening already notified, and the reserved campsite's nights a re-arm would remove (D104)
            foreach (string Site in Sites)
            {
                for (int Day = 1; Day <= 2; Day++)
                {
                    Directory.CreateDirectory(Path.Join(Folder, "Camp.History", MonthA.Year.ToString(), MonthA.Month.ToString(), Day.ToString(), Site));
                }
            }
            Dictionary<string, string> Before = Fingerprint(Folder);
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Finished: 4 openings found in 1 search. Elapsed 0:00."), "every opening, markers or not");
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before), "Camp.History and every file byte for byte as before");
            Assert.That(Removed, Is.Empty, "no marker removed or re-armed");
            AssertNothingSentOrLeaked(Terminal);
        }

        [Test]
        public void AnotherProgramsMonitoringOrRefreshLock_IsNeitherTakenNorWaitedFor()
        {
            //TD-54 unchanged: a manual search is no monitoring and no refresh; it takes and waits for neither lock
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            using FileStream? Monitoring = MonitoringLock.TryAcquire(Folder, out MonitoringLockProblem? Problem);
            using FileStream? Refresh = RidbStagedRefresh.TryAcquireRefreshLock(Folder, out string RefreshProblem);
            Assert.That((Monitoring, Refresh), Is.Not.EqualTo(((FileStream?)null, (FileStream?)null)));
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain("Finished: 6 openings found in 1 search. Elapsed 0:00."));
            using FileStream? Again = MonitoringLock.TryAcquire(Folder, out MonitoringLockProblem? Held);
            Assert.That((Again, Held), Is.EqualTo(((FileStream?)null, (MonitoringLockProblem?)MonitoringLockProblem.Held)), "the other program still holds it");
        }

        [Test]
        public void WithoutTheCampgroundData_ResultsSaysSo_AndAnUnpreparedCampgroundIsTheSharedProblem()
        {
            Prepared = false;
            WriteSearch("Trip.yaml", "Trip", LastDay: 2);
            File.Delete(Path.Join(Cache, CampgroundId + "-Cached.json"));
            var Terminal = new MenuTerminal(Script(Start, FromResultToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), Terminal.AllText);

            Assert.That(Terminal.Texts, Does.Contain(W.DataMissing));
            Assert.That(Terminal.AllText, Does.Contain("Campground ID " + CampgroundId), "the campground by its ID without the data");
            Assert.That(Terminal.AllText, Does.Contain("Error: The campground data is missing, so the campsite details could not be prepared."));
            Assert.That(Requests, Is.Empty, "no request without the campsite details");
            Assert.That(File.Exists(Path.Join(Folder, "RecreationDotOrg.db")), Is.False, "the campground data is never created or prepared here");
        }

        // ---- structure: what the code reaches, what it has of its own ----

        private static List<string> Reached()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveResults).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveResults).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheResultsCode_ReachesTheSharedSearchAndResults_AndNoEngineRequestHistoryNotificationOrLockOfItsOwn()
        {
            List<string> Called = Reached();
            string[] Forbidden =
            {
                //no search engine, request, parsing or result shaping of its own: the legacy engine and the shared run's internals
                "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole", "CampertronLibrary.function.RecDotOrg.data.Load::PostProcess", "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults",
                "CampertronLibrary.function.RecDotOrg.data.Load::OutputResults", "CampertronLibrary.function.RecDotOrg.data.Load::Preload", "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig",
                "CampertronLibrary.function.Base.Menu", "CampertronLibrary.function.Base.ConsoleResults", "CampertronLibrary.function.Base.ResultsHtml", "CampertronLibrary.function.Base.ResultsEmail",
                "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.search.SearchHost", "CampertronLibrary.function.RecDotOrg.search.HttpSearchTransport",
                "CampertronLibrary.function.RecDotOrg.search.SearchFilters", "CampertronLibrary.function.RecDotOrg.data.Data::", "CampertronLibrary.function.RecDotOrg.data.Cache",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::OpeningsOf", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::SitesOf",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::BookingUrl", "SearchOpening::.ctor", "SearchSite::.ctor", "SearchStay::.ctor", "SearchResult::.ctor",
                "System.Net.", "Newtonsoft.", "System.Text.Json.", "System.Linq.Enumerable::GroupBy", "System.Linq.Enumerable::OrderBy",
                //no history, notification, monitoring or lock
                "CampertronLibrary.function.RecDotOrg.data.NotificationHistory", "CampertronLibrary.function.RecDotOrg.data.Load::HasNotificationMarker",
                "CampertronLibrary.function.RecDotOrg.data.Load::RecordNotificationHistory", "CampertronLibrary.function.RecDotOrg.data.Load::UsesNotificationHistory",
                "CampertronLibrary.function.RecDotOrg.data.Load::HasNewEntries", "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.Forms.Monitoring",
                "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.Base.Notifications", "CampertronLibrary.function.Base.NtfyPublisher",
                "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.SmtpPassword", "CampertronLibrary.function.Base.NtfyToken",
                "CampertronLibrary.function.RecDotOrg.sqlite.RidbStagedRefresh", "System.Threading.Mutex", "System.Threading.Semaphore", "System.Threading.Monitor::", "System.Threading.Thread::",
                //no writer, YAML, database, refresh, guided setup, transfer, web or process (no browser)
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "System.IO.File::", "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.IO.Directory::Move",
                "Microsoft.Data.Sqlite.", "Microsoft.EntityFrameworkCore.", "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.Load::ExtractEmbeddedDbTo",
                "CampertronLibrary.function.RecDotOrg.api.RidbRefreshService", "CampertronLibrary.function.Forms.GuidedSetup", "CampertronLibrary.function.Forms.Settings",
                "CampertronLibrary.function.Interactive.Searches.", "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.",
                "System.Diagnostics.Process", "System.Environment::", "System.Console::"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Plan", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Run",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::LoadEntry", "CampertronLibrary.function.Forms.ResultsStart::Prepare",
                "CampertronLibrary.function.Forms.ResultsOutcome::.ctor", "CampertronLibrary.function.Forms.ResultsOutcome::Unexpected", "CampertronLibrary.function.Forms.ResultsListing::Of",
                "CampertronLibrary.function.Forms.ResultsSearchList::.ctor", "CampertronLibrary.function.Forms.ResultsSearchList::Rescan", "CampertronLibrary.function.Forms.ResultsSearchList::SelectEnabled",
                "CampertronLibrary.function.Forms.ResultsSearchList::SelectAllRunnable", "CampertronLibrary.function.Forms.ResultsSearchList::ClearSelection",
                "CampertronLibrary.function.Forms.BlockHold::Begin", "CampertronLibrary.function.Forms.BlockHold::get_IsActive", "CampertronLibrary.function.Forms.BlockHold::Text",
                "CampertronLibrary.function.Forms.ResultsWording::Stage", "CampertronLibrary.function.Forms.ResultsWording::State", "CampertronLibrary.function.Forms.ResultsWording::Requests",
                "CampertronLibrary.function.Forms.ResultsWording::Campgrounds", "CampertronLibrary.function.Forms.ResultsWording::ProgressDetails", "CampertronLibrary.function.Forms.ResultsWording::Finished",
                "CampertronLibrary.function.Forms.ResultsWording::Searching", "CampertronLibrary.function.Forms.ResultsWording::EndedAgo", "CampertronLibrary.function.Forms.ResultSearchViewModel::get_StatusText",
                "CampertronLibrary.function.Forms.ResultsLink::get_Href", "CampertronLibrary.function.RecDotOrg.data.Load::DbExists",
                "CampertronLibrary.function.Interactive.InteractiveTerminal::ReadKeysAsTheyAreTyped", "CampertronLibrary.function.Interactive.InteractiveTerminal::NextKeyTyped",
                "CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundRefresh::RegisterStopSignals"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheStartWithoutArguments_KeepsItsOwnSearchEngine_UntilPhase14()
        {
            //D115: the no-argument menu's Search moves to CampsiteSearch only in phase 14, after PT1; until then its own loop
            //Load's methods with its closures (its search's loop runs the console's search in a lambda)
            var Called = new List<string>();
            const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var Types = new Stack<Type>(new[] { typeof(Load) });
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                foreach (MethodBase Method in T.GetMethods(All).Cast<MethodBase>().Concat(T.GetConstructors(All)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
                foreach (Type Nested in T.GetNestedTypes(All))
                {
                    Types.Push(Nested);
                }
            }
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetAvailabilitiesByCampground"), "its own loop over the console's search");
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.RecDotOrg.search.CampsiteSearch", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Forms.Results", StringComparison.Ordinal) || C.Contains(".Interactive.", StringComparison.Ordinal)), Is.Empty,
                "never the shared search service, the shared Results or the interactive console");
        }

        [Test]
        public void TheResultsCode_HasNoResultModelOrLaterPhaseOfItsOwn_AndNoControlCharacter()
        {
            Type[] Own = typeof(InteractiveResults).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveResults).Namespace).ToArray();
            Assert.That(Own.Where(T => T.IsEnum), Is.Empty, "no stage, state or outcome of its own: the shared run's");
            Assert.That(Own.SelectMany(T => T.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(P_ => new[] { "Openings", "Sites", "Stays", "Nights", "Rows", "Stage", "Total" }.Contains(P_.Name)), Is.Empty, "no result or progress model of its own");
            var Literals = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in Own)
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Literals.AddRange(ConsoleStartContractTests.StringLiterals(Method));
                }
                Literals.AddRange(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!));
            }
            string AllLiterals = string.Join("\n", Literals)
                //since D130: a missing settings folder names the guided setup, which offers to create it
                .Replace(CampertronLibrary.function.Interactive.Results.InteractiveResultsWording.FolderMissing, string.Empty);
            foreach (string Later in new[] { "Start the monitoring", "Stop the monitoring", "Restart the monitoring", "Guided setup", "Export", "Import", "Beam", "same campsite", "free space",
                "disk space", "Retry", "Try again", "https://", "http://", "open it in", "Opened the booking page" })
            {
                Assert.That(AllLiterals, Does.Not.Contain(Later), "no later phase, link of its own, retry or browser: " + Later);
            }
            Assert.That(Literals.Where(S => S.Any(char.IsControl)), Is.Empty, "no escape sequence or control character of its own");
        }
    }
}
