using System.IO.Compression;
using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using CampertronUnitTests.function.Base;
using CampertronUnitTests.function.RecDotOrg.api;
using CampertronUnitTests.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;
using CW = CampertronLibrary.function.Forms.CampgroundsWording;
using W = CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundsWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Campgrounds (v0.4.0's phase 9, D125; business-rules §43): the campground lookup and details
    // (the shared queries and words of the desktop GUI's Campgrounds tab, the web UI's campgrounds pages and the search
    // editors' picker), the campground data's status and its refresh (the shared RidbRefreshService with RefreshRules and
    // RefreshWording), driven from the main menu by the terminal that answers a choice by its label and types keys while an
    // operation runs (MenuTerminal), and judged by the words shown and the files left. The lookup reads an in-memory
    // campground data through InteractiveCampgrounds' source, or a real fixture database through the library's own queries;
    // the refresh is a scripted service that reports progress and waits for the console to have looked at it, or the real
    // service against an in-memory export (FakeExport). No request leaves the process (NoLiveNetwork), nothing is sent or
    // searched, and no exception's text or other settings file's secret may be shown (canaries, boolean checks).
    [NonParallelizable]
    public class InteractiveCampgroundsTests : ConfigEditTestBase
    {
        private static readonly DateTime UtcNow = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime PreviousRefresh = new DateTime(2026, 11, 20, 3, 4, 5, DateTimeKind.Utc);
        private static readonly DateTime RefreshTime = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        private const string OldMarker = "OLD FACILITY";
        private const string NewMarker = "NEW FACILITY";
        private const string Canary = "cnry-a-problem-or-path-7f3a";

        private static string SharedDirectory = string.Empty;
        private static string PreviousDb = string.Empty;
        private static byte[] ValidExport = Array.Empty<byte>();

        private string Root = string.Empty;
        private string Cache => Path.Join(Root, "cache");
        private string WorkRoot => Path.Join(Root, "work");
        private string InstalledDb => Path.Join(Folder, "RecreationDotOrg.db");

        private CampgroundLookupSource OriginalSource = null!;
        private Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>, CancellationToken, RidbRefreshResult> OriginalService = null!;
        private TimeProvider OriginalTime = null!;
        private TimeSpan OriginalPoll;
        private TimeSpan OriginalCount;
        private Func<Action, IDisposable> OriginalStop = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Counting Handler = null!;
        private ManualTime Clock = null!;
        private int EmailSends;
        private bool Prepared;
        private bool ReadsFail;
        private readonly Dictionary<string, int> Reads = new Dictionary<string, int>(StringComparer.Ordinal);
        private int ServiceCalls;
        private Action? StopAction;
        private int StopRegistrations;
        private int StopDisposals;
        private FakeExport? Export;

        [OneTimeSetUp]
        public void CreateDatabases()
        {
            SharedDirectory = Path.Join(Path.GetTempPath(), "CampertronInteractiveCampgroundsShared-" + Guid.NewGuid().ToString("N"));
            string Template = Path.Join(SharedDirectory, "template");
            Directory.CreateDirectory(Template);
            string Emptied = Path.Join(Template, "RecreationDotOrg.db");
            Load.ExtractEmbeddedDbTo(Emptied);
            Clear.ClearDatabase(Template);
            SqliteConnection.ClearAllPools();
            string Previous = Path.Join(SharedDirectory, "previous");
            Directory.CreateDirectory(Previous);
            PreviousDb = Path.Join(Previous, "RecreationDotOrg.db");
            File.Copy(Emptied, PreviousDb);
            string OldRidb = Path.Join(SharedDirectory, "old-ridb");
            Directory.CreateDirectory(OldRidb);
            PopulateDbFromRIDBFiles.Populate(Fixture(OldMarker).Write(OldRidb), Previous);
            SqliteConnection.ClearAllPools();
            RidbIndexes.Create(PreviousDb);
            Checkpoint(PreviousDb);
            Assert.That(RidbDatabase.Validate(Previous), Is.Empty);
            string NewRidb = Path.Join(SharedDirectory, "new-ridb");
            Directory.CreateDirectory(NewRidb);
            Fixture(NewMarker).Write(NewRidb);
            string Archive = Path.Join(SharedDirectory, "export.zip");
            ZipFile.CreateFromDirectory(NewRidb, Archive);
            ValidExport = File.ReadAllBytes(Archive);
        }

        [OneTimeTearDown]
        public void DeleteShared()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(SharedDirectory))
            {
                Directory.Delete(SharedDirectory, true);
            }
        }

        // the fixture of the refresh's own tests, with Watchman (so it passes RidbDatabase.Validate) and a marker on a
        // reservable, enabled campground (the lookup lists only those)
        private static RidbFixture Fixture(string Marker)
        {
            RidbFixture Fixture_ = RidbFixture.Create();
            Fixture_.Facilities[3].FacilityName = RidbDatabase.WatchmanName;
            Fixture_.Facilities[1].FacilityName = Marker;
            return Fixture_;
        }

        [SetUp]
        public void Seams()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronInteractiveCampgrounds-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(WorkRoot);
            OriginalSource = InteractiveCampgrounds.Source;
            OriginalService = InteractiveCampgroundRefresh.Service;
            OriginalTime = InteractiveCampgroundRefresh.Time;
            OriginalPoll = InteractiveCampgroundRefresh.PollInterval;
            OriginalCount = InteractiveCampgroundRefresh.CountInterval;
            OriginalStop = InteractiveCampgroundRefresh.WhenAskedToStop;
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            EmailSends = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => EmailSends++;
            Handler = new Counting();
            NtfyPublisher.CreateHandler = () => Handler;
            Prepared = true;
            ReadsFail = false;
            Reads.Clear();
            ServiceCalls = 0;
            StopAction = null;
            StopRegistrations = 0;
            StopDisposals = 0;
            Export = null;
            Clock = new ManualTime();
            InteractiveCampgrounds.Source = Memory();
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                ServiceCalls++;
                throw new AssertionException("no refresh was scripted");
            };
            InteractiveCampgroundRefresh.Time = Clock;
            InteractiveCampgroundRefresh.PollInterval = TimeSpan.FromMilliseconds(1);
            InteractiveCampgroundRefresh.CountInterval = TimeSpan.Zero;
            InteractiveCampgroundRefresh.WhenAskedToStop = Act =>
            {
                StopAction = Act;
                StopRegistrations++;
                return new Undo(() => StopDisposals++);
            };
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
        }

        [TearDown]
        public void RestoreSeams()
        {
            InteractiveCampgrounds.Source = OriginalSource;
            InteractiveCampgroundRefresh.Service = OriginalService;
            InteractiveCampgroundRefresh.Time = OriginalTime;
            InteractiveCampgroundRefresh.PollInterval = OriginalPoll;
            InteractiveCampgroundRefresh.CountInterval = OriginalCount;
            InteractiveCampgroundRefresh.WhenAskedToStop = OriginalStop;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
            Handler.Dispose();
            Export?.Dispose();
            SqliteConnection.ClearAllPools();
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

        // a clock the tests move: the refresh's elapsed time is 0:00 unless a test moves it
        private sealed class ManualTime : TimeProvider
        {
            private long Ticks;
            public override long GetTimestamp() => Interlocked.Read(ref Ticks);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            internal void Advance(TimeSpan By) => Interlocked.Add(ref Ticks, By.Ticks);
        }

        // ---- the campground data (in memory) ----

        private static CampgroundPickerEntry Entry(string Id, string Name, string? City, string? State, string? Park) =>
            new CampgroundPickerEntry(new CampgroundChoice(Id, Name, City, State), Park);

        private static readonly CampgroundPickerEntry NorthPines = Entry("232449", "NORTH PINES", "YOSEMITE VALLEY", "CA", "Yosemite National Park");
        private static readonly CampgroundPickerEntry UpperPines = Entry("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA", "Yosemite National Park");
        private static readonly CampgroundPickerEntry HighCamp = Entry("300001", "HIGH CAMP", null, "CA", null);
        private static readonly CampgroundPickerEntry Watchman = Entry("232445", "WATCHMAN CAMPGROUND", "SPRINGDALE", "UT", "Zion National Park");
        private static readonly CampgroundPickerEntry Evil = Entry("500001", "EVIL\u001b[31mRED\u0007", "BAD\u0000TOWN", "CA", "Park\u001b]0;title\u0007");
        // thirty campgrounds of one park, city and state: more than a part of a list holds
        private static readonly List<CampgroundPickerEntry> Lakes = Enumerable.Range(1, 30)
            .Select(N => Entry((400000 + N).ToString(System.Globalization.CultureInfo.InvariantCulture), "LAKE CAMP " + N.ToString("00", System.Globalization.CultureInfo.InvariantCulture), "LAKEVILLE", "CA", "Lake Park")).ToList();

        private static List<CampgroundPickerEntry> All() => new[] { HighCamp, NorthPines, UpperPines, Watchman, Evil }.Concat(Lakes).ToList();

        private static List<CampgroundChoice> Choices(IEnumerable<CampgroundPickerEntry> Entries) => Entries.Select(E => E.Choice).ToList();

        private T Read<T>(string Kind, Func<T> Value)
        {
            Reads[Kind] = Reads.GetValueOrDefault(Kind) + 1;
            if (ReadsFail)
            {
                throw new SqliteException(Canary, 11);
            }
            return Value();
        }

        private int ReadCount => Reads.Values.Sum();

        private CampgroundLookupSource Memory() => new CampgroundLookupSource
        {
            Prepared = Folder_ => Prepared,
            Campgrounds = Folder_ => Read("campgrounds", All),
            Parks = Folder_ => Read("parks", () => new List<string> { "Lake Park", "Park\u001b]0;title\u0007", "Yosemite National Park", "Zion National Park" }),
            CampgroundsByPark = (Folder_, Park) => Read("by park", () => Choices(All().Where(E => E.Park == Park))),
            States = Folder_ => Read("states", () => new List<string> { "CA", "UT" }),
            Cities = (Folder_, State) => Read("cities", () => State == "CA" ? new List<string> { "", "BAD\u0000TOWN", "LAKEVILLE", "YOSEMITE VALLEY" } : new List<string> { "SPRINGDALE" }),
            CampgroundsByStateAndCity = (Folder_, State, City) => Read("by city", () => Choices(All().Where(E => E.Choice.State == State && (E.Choice.City ?? string.Empty) == City))),
            CampgroundsByState = (Folder_, State) => Read("by state", () => Choices(All().Where(E => E.Choice.State == State))),
            Details = (Folder_, Id) => Read("details", () => Facility(Id)),
            Campsites = (Folder_, Id) => Read("campsites", () => Campsites(Id))
        };

        private static FacilitiesData? Facility(string Id)
        {
            CampgroundPickerEntry? Found = All().FirstOrDefault(E => E.FacilityID == Id);
            if (Found == null)
            {
                return null;
            }
            return Id switch
            {
                "232445" => new FacilitiesData
                {
                    FacilityID = Id, FacilityName = "WATCHMAN CAMPGROUND", FacilityTypeDescription = "Campground", FacilityPhone = "435-772-3837", FacilityEmail = "watchman@example.invalid",
                    FacilityDescription = "Overview\nNear the south entrance.\n\nSecond\tparagraph.\r", FacilityDirections = "Take the shuttle.", FacilityUseFeeDescription = "$20 a night"
                },
                "500001" => new FacilitiesData
                {
                    FacilityID = Id, FacilityName = "EVIL\u001b[31mRED\u0007", FacilityTypeDescription = "Campground", FacilityPhone = "555\u001b[2J0100",
                    FacilityDescription = "Clear\u001b[2Jthe screen\u0008\u0000", FacilityDirections = "\u009b31m", FacilityUseFeeDescription = "\u0007"
                },
                _ => new FacilitiesData { FacilityID = Id, FacilityName = Found.Choice.Name, FacilityTypeDescription = "Campground" }
            };
        }

        // Watchman: two equipment values, a campsite type, two names, an attribute; Upper Pines: thirty names "Site 1" to
        // "Site 30" (more than a part holds; in their natural order, 2 before 10); the evil one a value with control characters
        private static CampgroundFilterChoices Campsites(string Id) => Id switch
        {
            "232445" => new CampgroundFilterChoices(Id, 2, new[] { new FilterChoice("Tent", 2), new FilterChoice("SMALL TENT", 1) },
                new[] { new FilterChoice("STANDARD NONELECTRIC", 2) }, new[] { new FilterChoice("A001", 1), new FilterChoice("A002", 2) },
                new[] { new FilterChoice("Shade:Yes", 1, "Shade", "Yes") }),
            "232447" => new CampgroundFilterChoices(Id, 30, Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(),
                Enumerable.Range(1, 30).Select(N => new FilterChoice("Site " + N.ToString(System.Globalization.CultureInfo.InvariantCulture), 1)), Array.Empty<FilterChoice>()),
            "500001" => new CampgroundFilterChoices(Id, 1, new[] { new FilterChoice("Bad\u001b[31mValue", 1) }, Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>()),
            _ => CampgroundFilterChoices.None(Id)
        };

        // ---- the script ----

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static readonly TerminalInput No = TerminalInput.Line("n");
        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private static readonly object[] Into = { P(W.Campgrounds) };
        private static readonly object[] IntoLookUp = { P(W.Campgrounds), P(W.LookUp) };
        private static readonly object[] Find = { P(W.Campgrounds), P(W.LookUp), P(W.FindChoice) };
        private static readonly object[] IntoRefresh = { P(W.Campgrounds), P(W.RefreshOrInstall) };
        private static readonly object[] BackToExit = { P(InteractiveSettingsWordingBack), P("Exit") };
        private const string InteractiveSettingsWordingBack = "Back to the main menu";

        private InteractiveSession Session(InteractiveTerminal Terminal, bool Container = false, bool NoFolder = false, string? Settings = null) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Settings ?? Folder, CachePath = Cache, ContainerMode = Container });

        private int Run(MenuTerminal Terminal, bool Container = false, string? Settings = null) => InteractiveConsole.Run(Session(Terminal, Container, Settings: Settings));

        private Dictionary<string, string> Fingerprint(string Of) =>
            Directory.GetFileSystemEntries(Of, "*", SearchOption.AllDirectories).ToDictionary(P => P,
                P => File.Exists(P) ? System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(P))) + File.GetLastWriteTimeUtc(P).Ticks : "dir");

        private void WriteSettings()
        {
            Write(General, "outputTo: Console\nautoRefresh: false\nlastRidbDataRefresh: " + PreviousRefresh.ToString("o") + "\n");
            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: PwSmtp-" + Canary + "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
            Write(Path.Join(Folder, "Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: topic" + Canary.Replace("-", "") + "\n");
        }

        private void AssertNoSecretOrProblemText(MenuTerminal Terminal)
        {
            string AllText = Terminal.AllText + "\n" + Out + "\n" + Err;
            Assert.That(AllText.Contains(Canary, StringComparison.Ordinal) || AllText.Contains(Canary.Replace("-", ""), StringComparison.Ordinal), Is.False,
                "a secret, a path or an exception's text was written (its canary is not repeated here)");
        }

        private static void AssertNoControlCharacter(MenuTerminal Terminal)
        {
            Assert.That(Terminal.Texts.Where(T => T.Any(char.IsControl)).Count(), Is.Zero, "no control character reaches the terminal");
        }

        private void AssertNothingSentOrStarted()
        {
            Assert.That(EmailSends, Is.Zero, "no email");
            Assert.That(Handler.Seen, Is.Zero, "no ntfy request");
            Assert.That(File.Exists(Path.Join(Folder, "Camp.History")), Is.False, "no Camp.History");
            Assert.That(File.Exists(Path.Join(Folder, "Campertron.monitoring.lock")), Is.False, "no monitoring lock");
        }

        // the numbered choices of every menu shown with this heading
        private static List<List<string>> MenusUnder(MenuTerminal Terminal, string Heading)
        {
            var Menus = new List<List<string>>();
            for (int i = 0; i < Terminal.Written.Count; i++)
            {
                if (Terminal.Written[i].Kind == "heading" && Terminal.Written[i].Text == Heading)
                {
                    var Choices = new List<string>();
                    for (int j = i + 1; j < Terminal.Written.Count && Terminal.Written[j].Kind == "out" && Terminal.Written[j].Text.StartsWith("  ", StringComparison.Ordinal); j++)
                    {
                        System.Text.RegularExpressions.Match M = System.Text.RegularExpressions.Regex.Match(Terminal.Written[j].Text, @"^  (\d+)\. (.*)$");
                        if (M.Success)
                        {
                            Choices.Add(M.Groups[2].Value);
                        }
                    }
                    Menus.Add(Choices);
                }
            }
            return Menus;
        }

        // the menus shown with this heading that have choices (a campground's details and the menu of its values share the
        // campground's name as their heading)
        private static List<List<string>> ChoicesUnder(MenuTerminal Terminal, string Heading) => MenusUnder(Terminal, Heading).Where(M => M.Count > 0).ToList();

        // ---- the main menu and Campgrounds ----

        [Test]
        public void TheMainMenu_HasCampgroundsAfterSearches_AndBeforeResultsMonitoringAndHelp()
        {
            var Terminal = new MenuTerminal(P("Exit"));
            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(MenusUnder(Terminal, InteractiveWording.MainMenu).Single(),
                Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Guided setup", "Help", "Exit" }));
            Assert.That(Terminal.Prompts.Single(), Is.EqualTo("Choose a number from 1 to 10: "));
        }

        [Test]
        public void EnteringCampgrounds_ShowsItsChoices_AndReadsChangesStartsOrSendsNothing()
        {
            WriteSettings();
            File.Copy(PreviousDb, InstalledDb);
            Dictionary<string, string> Before = Fingerprint(Folder);
            Dictionary<string, string> CacheBefore = Fingerprint(Root);
            var Terminal = new MenuTerminal(Script(Into, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Written.Count(W_ => W_.Kind == "heading" && W_.Text == W.CampgroundsTitle), Is.EqualTo(1));
            Assert.That(Terminal.Texts, Does.Contain(W.CampgroundsNote));
            Assert.That(MenusUnder(Terminal, W.CampgroundsChoices).Single(), Is.EqualTo(new[] { W.LookUp, W.DataStatus, W.RefreshOrInstall, "Back to the main menu" }));
            Assert.That(ReadCount, Is.Zero, "entering reads no campground data");
            Assert.That(ServiceCalls, Is.Zero, "no refresh");
            Assert.That(StopRegistrations, Is.Zero);
            Assert.That(Terminal.OperationKeys, Is.Zero);
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before), "nothing was created, changed or deleted");
            Assert.That(Fingerprint(Root), Is.EqualTo(CacheBefore), "the cache and work folders are as they were");
            AssertNothingSentOrStarted();
            AssertNoSecretOrProblemText(Terminal);
            foreach (string Later in new[] { "Search now", "Run", "Results", "Monitoring", "monitoring now", "Guided setup", "Export", "Import", "Beam", "same campsite" })
            {
                Assert.That(MenusUnder(Terminal, W.CampgroundsChoices).Single().Any(C => C.Contains(Later, StringComparison.Ordinal)), Is.False, "no later phase's choice: " + Later);
            }
        }

        [Test]
        public void EscCtrlCAndTheEndOfTheInput_AtCampgrounds()
        {
            var Escaped = new MenuTerminal(Script(Into, new object[] { Esc, P(W.Campgrounds), CtrlC, P("Exit") }));
            Assert.That(Run(Escaped), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(MenusUnder(Escaped, InteractiveWording.MainMenu).Count, Is.EqualTo(3), "Esc and Ctrl+C go back to the main menu");
            Assert.That(Escaped.Texts, Has.None.EqualTo(ConsoleUi.StoppedText));

            var Ended = new MenuTerminal(Script(Into, new object[] { End }));
            Assert.That(Run(Ended), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Ended.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(ReadCount, Is.Zero);
        }

        [TestCase("none")]
        [TestCase("missing")]
        [TestCase("container")]
        public void WithoutTheSettingsFolder_NothingIsReadOrCreated(string Case)
        {
            string Missing = Path.Join(Root, "not-there");
            var Terminal = new MenuTerminal(Script(Into, new object[] { P("Exit") }));
            InteractiveConsole.Run(Session(Terminal, Container: Case == "container", NoFolder: Case == "none", Settings: Missing));

            Assert.That(Terminal.Texts, Does.Contain(Case == "none" ? W.NoFolder : Case == "container" ? W.ContainerFolderMissing : W.FolderMissing));
            Assert.That(MenusUnder(Terminal, W.CampgroundsChoices), Is.Empty, "no choice without the folder");
            Assert.That(Directory.Exists(Missing), Is.False, "the folder is never created");
            Assert.That(ReadCount, Is.Zero);
        }

        [Test]
        public void Help_NamesCampgrounds_AndStillNoLaterPhase()
        {
            var Terminal = new MenuTerminal(P("Help"), P("Exit"));
            Run(Terminal);
            string Help = string.Join("\n", Terminal.Texts.Where(T => T.StartsWith("  ", StringComparison.Ordinal)));
            Assert.That(Help, Does.Contain("Campgrounds: look up a campground"));
            Assert.That(Help, Does.Contain("Esc or Ctrl+C cancels it until the new data is being installed"));
            //since phase 11 (D127) and phase 12 (D128) the Help names the monitoring's Start and the guided setup
            foreach (string Later in new[] { "Search now", "Export", "Beam" })
            {
                Assert.That(Help, Does.Not.Contain(Later));
            }
        }

        // ---- the lookup ----

        [Test]
        public void WithoutTheCampgroundData_TheLookupListsNothing_AndOpensNothing()
        {
            Prepared = false;
            WriteSettings();
            Dictionary<string, string> Before = Fingerprint(Folder);
            var Terminal = new MenuTerminal(Script(IntoLookUp, new object[] { P(W.DataStatus) }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain(W.DataMissing));
            Assert.That(ChoicesUnder(Terminal, W.LookUp), Is.Empty, "no way to look without the data");
            Assert.That(Terminal.Texts, Does.Contain("  " + W.PreparedLabel + ": " + W.NotPrepared));
            Assert.That(Terminal.Texts, Does.Contain("  " + W.ReadableLabel + ": " + W.NotChecked));
            Assert.That(Terminal.Texts, Does.Contain("  " + W.NeededLabel + ": " + W.NeedsInstall));
            Assert.That(ReadCount, Is.Zero, "no database is opened");
            Assert.That(File.Exists(InstalledDb), Is.False, "nothing is prepared");
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before));
            Assert.That(ServiceCalls, Is.Zero, "no refresh by itself");
        }

        [Test]
        public void Find_ByWords_ListsTheSharedMatches_AndShowsTheChosenCampgroundsDetails_ByItsFacilityID()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[] { L("pines"), P(UpperPines.AccessibleName), P(W.BackToList), P(W.Back), Esc, Esc }, BackToExit));

            Run(Terminal);

            //the shared find's matches, in its order, each with its name, location, ID and park
            Assert.That(MenusUnder(Terminal, CW.Heading).First(), Is.EqualTo(new[] { NorthPines.AccessibleName, UpperPines.AccessibleName, W.Back }));
            Assert.That(Reads["details"], Is.EqualTo(1));
            Assert.That(Reads["campgrounds"], Is.EqualTo(1), "the campgrounds are read once for the visit");
            int At = Terminal.Written.FindIndex(T => T.Kind == "heading" && T.Text == "UPPER PINES");
            Assert.That(At, Is.GreaterThan(0));
            Assert.That(Terminal.Texts.Skip(At + 1).Take(13), Is.EqualTo(new[]
            {
                "  Yosemite National Park",
                "  " + CW.CampgroundIdLabel + ": 232447",
                "  " + CW.CampgroundIdNote,
                "  " + CW.ContactHeading,
                "    " + CW.PhoneLabel + ": " + CW.NotListed,
                "    " + CW.EmailLabel + ": " + CW.NotListed,
                "  " + CW.DescriptionHeading,
                "    " + CW.NotListed,
                "  " + CW.DirectionsHeading,
                "    " + CW.NotListed,
                "  " + CW.FeesHeading,
                "    " + CW.NotListed,
                "  " + SearchesWording.CampsitesHeading
            }));
            Assert.That(Terminal.Texts, Does.Contain("    " + W.CampsiteCount(30)));
            AssertNoControlCharacter(Terminal);
            Assert.That(ServiceCalls, Is.Zero);
        }

        [Test]
        public void Find_AnExactId_OpensItsDetailsAtOnce_WithTheSharedSnapshot()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[] { L("232445"), P(W.BackToList), Esc, Esc }, BackToExit));

            Run(Terminal);

            Assert.That(MenusUnder(Terminal, CW.Heading), Is.Empty, "no list for an exact ID");
            int At = Terminal.Written.FindIndex(T => T.Kind == "heading" && T.Text == "WATCHMAN CAMPGROUND");
            Assert.That(Terminal.Texts.Skip(At + 1).Take(17), Is.EqualTo(new[]
            {
                "  Zion National Park",
                "  " + CW.CampgroundIdLabel + ": 232445",
                "  " + CW.CampgroundIdNote,
                "  " + CW.ContactHeading,
                "    " + CW.PhoneLabel + ": 435-772-3837",
                "    " + CW.EmailLabel + ": watchman@example.invalid",
                "  " + CW.DescriptionHeading,
                "    Overview",
                "    Near the south entrance.",
                "",
                "    Second paragraph.",
                "  " + CW.DirectionsHeading,
                "    Take the shuttle.",
                "  " + CW.FeesHeading,
                "    $20 a night",
                "  " + SearchesWording.CampsitesHeading,
                "    " + W.CampsiteCount(2)
            }));
            Assert.That(ChoicesUnder(Terminal, "WATCHMAN CAMPGROUND").Single(), Is.EqualTo(new[]
            {
                W.Values(SearchesWording.EquipmentHeading, 2), W.Values(SearchesWording.CampsiteTypesHeading, 1), W.Values(SearchesWording.CampsitesHeading, 2),
                W.Values(SearchesWording.AttributesHeading, 1), W.BackToList
            }));
        }

        [Test]
        public void Find_WithoutAMatch_SaysSoAndAsksAgain_AndEscGoesBack()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[] { L("no such place"), L("23244"), Esc, P(W.BackToCampgrounds) }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts.Count(T => T == W.NoMatches), Is.EqualTo(2), "a part of an ID matches nothing (the shared rule)");
            Assert.That(Reads.GetValueOrDefault("details"), Is.Zero);
        }

        [Test]
        public void Find_EnterAlone_ListsEveryCampground_ATerminalsPartAtATime_WithFindAndTheWholeList()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[]
            {
                L(""), P(W.NextPart(10)), P(W.PreviousPart(25)), P(W.FindInList), L("lake camp 2"), P(W.WholeList), P(W.FindInList), L("zzz"), P(W.Back), Esc, P(W.BackToCampgrounds)
            }, BackToExit));

            Run(Terminal);

            List<List<string>> Lists = MenusUnder(Terminal, CW.Heading);
            Assert.That(Lists[0].Take(25), Is.EqualTo(All().Take(25).Select(E => InteractiveCampgroundList.Safe(E.AccessibleName))));
            Assert.That(Lists[0].Skip(25), Is.EqualTo(new[] { W.NextPart(10), W.FindInList, W.Back }));
            Assert.That(Lists[1].Take(10), Is.EqualTo(All().Skip(25).Select(E => InteractiveCampgroundList.Safe(E.AccessibleName))));
            Assert.That(Lists[1].Skip(10), Is.EqualTo(new[] { W.PreviousPart(25), W.FindInList, W.Back }));
            //"lake camp 2": LAKE CAMP 02, 12 and 20 to 29 (a find of the list's own text, each word, ignoring case)
            Assert.That(Lists[3].Where(C => C.StartsWith("LAKE", StringComparison.Ordinal)).Count(), Is.EqualTo(12));
            Assert.That(Lists[3].Count, Is.EqualTo(12 + 3));
            Assert.That(Lists[3].Last(), Is.EqualTo(W.Back));
            Assert.That(Lists[3], Does.Contain(W.WholeList));
            Assert.That(Terminal.Texts, Does.Contain(W.Found(12)));
            Assert.That(Terminal.Texts, Does.Contain(W.NothingInList));
            Assert.That(Terminal.Texts, Does.Contain(W.Shown(1, 25, 35)));
            Assert.That(Terminal.Texts, Does.Contain(W.Shown(26, 35, 35)));
            Assert.That(Lists.Max(L => L.Count), Is.LessThanOrEqualTo(W.PageSize + 5), "a bounded list: a part and its choices");
        }

        [Test]
        public void ByPark_ByCity_AndByState_AreTheCampgroundsTabsLists_WithTheirContexts()
        {
            var Terminal = new MenuTerminal(Script(IntoLookUp, new object[]
            {
                P(CW.ByPark), P("Zion National Park"), P(Watchman.Choice.Label), P(W.BackToList), P(W.Back), P(W.Back),
                P(CW.ByCity), P("CA"), P(CW.NoCityListed), P(HighCamp.Choice.Label), P(W.BackToList), P(W.Back), P(W.Back), P(W.Back),
                P(CW.ByState), P("UT"), P(Watchman.Choice.Label), P(W.BackToList), P(W.Back), P(W.Back),
                P(W.BackToCampgrounds)
            }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain(CW.ByParkHint));
            Assert.That(Terminal.Texts, Does.Contain(CW.ByCityHint));
            Assert.That(Terminal.Texts, Does.Contain(CW.ByStateHint));
            Assert.That(MenusUnder(Terminal, CW.ByPark).First(), Is.EqualTo(new[] { "Lake Park", "Park\uFFFD]0;title\uFFFD", "Yosemite National Park", "Zion National Park", W.Back }));
            Assert.That(MenusUnder(Terminal, CW.ByCity + ": CA").First(), Is.EqualTo(new[] { CW.NoCityListed, "BAD\uFFFDTOWN", "LAKEVILLE", "YOSEMITE VALLEY", W.Back }));
            Assert.That(MenusUnder(Terminal, CW.Heading + ": Zion National Park").First(), Is.EqualTo(new[] { Watchman.Choice.Label, W.Back }));
            Assert.That(MenusUnder(Terminal, CW.Heading + ": CA").First(), Is.EqualTo(new[] { HighCamp.Choice.Label, W.Back }), "the empty city's campgrounds (its context: the state)");
            Assert.That(MenusUnder(Terminal, CW.Heading + ": UT").First(), Is.EqualTo(new[] { Watchman.Choice.Label, W.Back }));
            //the details' context is the list's: the park, the state for a campground without a city, the state
            List<string> Contexts = Terminal.Written.Select((T, i) => (T, i)).Where(X => X.T.Kind == "heading" && (X.T.Text == "WATCHMAN CAMPGROUND" || X.T.Text == "HIGH CAMP"))
                .Select(X => Terminal.Texts[X.i + 1]).Where(T => !System.Text.RegularExpressions.Regex.IsMatch(T, @"^  \d+\. ")).ToList();
            Assert.That(Contexts, Is.EqualTo(new[] { "  Zion National Park", "  CA", "  UT" }));
            Assert.That(Reads["details"], Is.EqualTo(3));
        }

        [Test]
        public void AListThatCantBeRead_IsTheSharedFixedWords_NeverTheProblemsText()
        {
            ReadsFail = true;
            var Terminal = new MenuTerminal(Script(IntoLookUp, new object[] { P(W.FindChoice), P(CW.ByPark), P(CW.ByCity), P(CW.ByState), P(W.BackToCampgrounds), P(W.DataStatus) }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Texts.Count(T => T == CW.NoData), Is.EqualTo(4));
            Assert.That(Terminal.Texts, Does.Contain("  " + W.ReadableLabel + ": " + W.NotReadable));
            Assert.That(Terminal.Texts, Does.Contain("  " + W.NeededLabel + ": " + W.NeedsAttention));
            AssertNoSecretOrProblemText(Terminal);
            Assert.That(Terminal.Written.Where(T => T.Kind == "error"), Is.Empty, "no unexpected problem");
        }

        [Test]
        public void DetailsOrCampsitesThatCantBeRead_AndACampgroundThatIsGone_AreFixedWords()
        {
            //the list is read, then the details fail; a campground the details no longer have; campsites that fail
            int DetailsCalls = 0;
            CampgroundLookupSource Memory_ = Memory();
            InteractiveCampgrounds.Source = new CampgroundLookupSource
            {
                Prepared = Memory_.Prepared, Campgrounds = Memory_.Campgrounds,
                Details = (F, Id) => ++DetailsCalls == 1 ? throw new SqliteException(Canary, 11) : DetailsCalls == 2 ? null : Facility(Id),
                Campsites = (F, Id) => throw new IOException(Canary)
            };
            var Terminal = new MenuTerminal(Script(Find, new object[] { L("232445"), L("232445"), L("232445"), P(W.BackToList), Esc, P(W.BackToCampgrounds) }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain(W.DetailsUnreadable));
            Assert.That(Terminal.Texts, Does.Contain(W.CampgroundGone));
            Assert.That(Terminal.Texts, Does.Contain("    " + W.CampsitesUnreadable));
            Assert.That(ChoicesUnder(Terminal, "WATCHMAN CAMPGROUND").Single(), Is.EqualTo(new[] { W.BackToList }), "no values to show");
            AssertNoSecretOrProblemText(Terminal);
        }

        [Test]
        public void ControlCharactersOfTheData_NeverReachTheTerminal()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[]
            {
                L("evil"), P(InteractiveCampgroundList.Safe(Evil.AccessibleName)), P(W.Values(SearchesWording.EquipmentHeading, 1)), P(W.Back), P(W.BackToList), P(W.Back), Esc,
                P(CW.ByCity), P("CA"), P(W.Back), P(W.Back), P(W.BackToCampgrounds)
            }, BackToExit));

            Run(Terminal);

            AssertNoControlCharacter(Terminal);
            Assert.That(Terminal.Written.Any(T => T.Kind == "heading" && T.Text == "EVIL\uFFFD[31mRED\uFFFD"), Is.True);
            Assert.That(Terminal.Texts, Does.Contain("    " + CW.PhoneLabel + ": 555\uFFFD[2J0100"));
            Assert.That(Terminal.Texts, Does.Contain("  Bad\uFFFD[31mValue \u2014 1 campsite"));
        }

        [Test]
        public void ACampgroundsCampsiteValues_AreListedByKind_APartAtATime_InTheirOrder_WithTheirCounts()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[]
            {
                L("232447"), P(W.Values(SearchesWording.CampsitesHeading, 30)), P(W.NextPart(5)), P(W.FindInList), L("site 2"), P(W.Back), P(W.BackToList), Esc,
                P(W.FindChoice), L("232445"), P(W.Values(SearchesWording.CampsitesHeading, 2)), P(W.Back), P(W.Values(SearchesWording.AttributesHeading, 1)), P(W.Back), P(W.BackToList), Esc,
                P(W.BackToCampgrounds)
            }, BackToExit));

            Run(Terminal);

            int First = Terminal.Written.FindIndex(T => T.Kind == "heading" && T.Text == "UPPER PINES: " + SearchesWording.CampsitesHeading);
            Assert.That(Terminal.Texts[First - 2], Is.EqualTo(W.Shown(1, 25, 30)), "said before the list's heading");
            Assert.That(Terminal.Texts.Skip(First + 1).Take(25), Is.EqualTo(Enumerable.Range(1, 25).Select(N => "  Site " + N + " \u2014 1 campsite")), "the names in their natural order");
            Assert.That(MenusUnder(Terminal, W.ListChoices).First(), Is.EqualTo(new[] { W.NextPart(5), W.FindInList, W.Back }), "a list to read: only its choices are numbered");
            Assert.That(Terminal.Texts, Does.Contain(W.Found(12)), "site 2, 12 and 20 to 29");
            Assert.That(Terminal.Texts, Does.Contain("  A002 \u2014 2 campsites share this name"));
            Assert.That(Terminal.Texts, Does.Contain("  Shade:Yes \u2014 1 campsite"));
            Assert.That(Terminal.Texts, Does.Contain("    " + W.ValuesNote));
        }

        [Test]
        public void AChoiceThatIsNoChoice_IsAskedAgain_AndTheEndOfTheInputAnywhereEndsTheConsole()
        {
            var Terminal = new MenuTerminal(Script(Find, new object[] { L("pines"), L("9"), L("x"), End }));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Terminal.Texts, Does.Contain(InteractiveWording.NotAChoice(3)));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(Reads.GetValueOrDefault("details"), Is.Zero);

            foreach (object[] Path_ in new[]
            {
                new object[] { P(W.Campgrounds), P(W.LookUp), End },
                new object[] { P(W.Campgrounds), P(W.LookUp), P(W.FindChoice), End },
                new object[] { P(W.Campgrounds), P(W.LookUp), P(CW.ByCity), P("CA"), End },
                new object[] { P(W.Campgrounds), P(W.LookUp), P(W.FindChoice), L("232445"), P(W.Values(SearchesWording.EquipmentHeading, 2)), End },
                new object[] { P(W.Campgrounds), P(W.RefreshOrInstall), End }
            })
            {
                var Ended = new MenuTerminal(Path_);
                Assert.That(Run(Ended), Is.EqualTo(InteractiveConsole.ExitCodeDone));
                Assert.That(Ended.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            }
            Assert.That(ServiceCalls, Is.Zero);
        }

        [Test]
        public void TheStatus_IsReadOnly_PreparedReadableAndLastRefreshed_InTheSharedWords()
        {
            WriteSettings();
            File.Copy(PreviousDb, InstalledDb);
            Dictionary<string, string> Before = Fingerprint(Folder);
            var Terminal = new MenuTerminal(Script(Into, new object[] { P(W.DataStatus) }, BackToExit));

            Run(Terminal);

            int At = Terminal.Written.FindIndex(T => T.Kind == "heading" && T.Text == W.StatusTitle);
            Assert.That(Terminal.Texts.Skip(At + 1).Take(5), Is.EqualTo(new[]
            {
                "  " + W.PreparedLabel + ": " + W.Prepared,
                "  " + W.ReadableLabel + ": " + W.Readable(All().Count),
                "  " + RefreshWording.LastRefreshedLabel + ": " + CurrentConfiguration.LastRefreshed(Folder, UtcNow, System.Globalization.CultureInfo.CurrentCulture),
                "  " + W.NeededLabel + ": " + W.NeedsNothing,
                "  " + W.RestartLabel + ": " + W.NoRestartHere
            }));
            Assert.That(Terminal.Texts[At + 3], Does.Contain("7 days ago"));
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before), "the status writes nothing");
            Assert.That(ServiceCalls, Is.Zero);
            AssertNoSecretOrProblemText(Terminal);
        }

        // ---- the real campground data (a fixture database, the library's own queries) ----

        [Test]
        public void TheRealQueries_FindListAndShowAFixtureDatabase_AndChangeNothingInIt()
        {
            InteractiveCampgrounds.Source = new CampgroundLookupSource();
            WriteSettings();
            File.Copy(PreviousDb, InstalledDb);
            string Hash = Sha256(InstalledDb);
            var Terminal = new MenuTerminal(Script(Find, new object[] { L(RidbFixture.Keys[1]), P(W.BackToList), Esc, P(CW.ByState), P("UT"), P(W.Back), P(W.Back), P(W.BackToCampgrounds), P(W.DataStatus) }, BackToExit));

            Run(Terminal);
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Written.Any(T => T.Kind == "heading" && T.Text == OldMarker), Is.True, "found by its exact ID, with its details");
            Assert.That(MenusUnder(Terminal, CW.ByState).First(), Does.Contain("UT"));
            Assert.That(Terminal.Texts, Does.Contain("  " + CW.CampgroundIdLabel + ": " + RidbFixture.Keys[1]));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("  " + W.ReadableLabel + ": yes: ", StringComparison.Ordinal)), Is.True);
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash), "the database is only read");
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName).Where(N => !N!.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal)),
                Is.EquivalentTo(new[] { "General.yaml", "Email.yaml", "Ntfy.yaml" }), "no other file");
            AssertNoControlCharacter(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void ADatabaseThatIsNotOne_IsTheFixedWords_AndIsLeftAsItIs()
        {
            InteractiveCampgrounds.Source = new CampgroundLookupSource();
            File.WriteAllText(InstalledDb, "not a database " + Canary);
            string Hash = Sha256(InstalledDb);
            var Terminal = new MenuTerminal(Script(Find, new object[] { P(CW.ByState), P(W.BackToCampgrounds), P(W.DataStatus) }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts.Count(T => T == CW.NoData), Is.EqualTo(2), "the find and By State");
            Assert.That(Terminal.Texts, Does.Contain("  " + W.ReadableLabel + ": " + W.NotReadable));
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash), "never repaired, replaced or written");
            AssertNoSecretOrProblemText(Terminal);
            Assert.That(ServiceCalls, Is.Zero, "no refresh by itself");
        }

        // ---- the refresh: its confirmation ----

        [Test]
        public void TheRefresh_ExplainsItself_AndItsConfirmationIsNoByDefault()
        {
            WriteSettings();
            File.Copy(PreviousDb, InstalledDb);
            Dictionary<string, string> Before = Fingerprint(Folder);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { L(""), P(W.RefreshOrInstall), No, P(W.RefreshOrInstall), Esc, P(W.RefreshOrInstall), CtrlC }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Texts, Does.Contain(RefreshWording.Explanation));
            Assert.That(Terminal.Written.Count(T => T.Kind == "heading" && T.Text == RefreshWording.ConfirmationHeading), Is.EqualTo(4));
            Assert.That(Terminal.Texts, Does.Contain(RefreshWording.ConfirmationBasics));
            Assert.That(Terminal.Texts, Does.Contain(W.ConfirmationAfter));
            Assert.That(Terminal.Texts, Has.None.EqualTo(W.InstallNote), "the data is prepared");
            Assert.That(Terminal.Prompts.Count(T => T == W.StartQuestion + " [y/N]: "), Is.EqualTo(4), "No by default");
            Assert.That(Terminal.Texts.Count(T => T == W.NotStarted), Is.EqualTo(4));
            Assert.That(ServiceCalls, Is.Zero, "Enter, n, Esc and Ctrl+C start nothing");
            Assert.That(StopRegistrations, Is.Zero);
            Assert.That(Terminal.OperationKeys, Is.Zero);
            Assert.That(Fingerprint(Folder), Is.EqualTo(Before));
        }

        [Test]
        public void WithoutTheCampgroundData_TheRefreshSaysItInstallsWhatItDownloads()
        {
            Prepared = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { No }, BackToExit));

            Run(Terminal);

            int Explained = Terminal.Texts.IndexOf(RefreshWording.Explanation);
            Assert.That(Terminal.Texts[Explained + 1], Is.EqualTo(W.InstallNote));
            Assert.That(ServiceCalls, Is.Zero);
            Assert.That(File.Exists(InstalledDb), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WithoutTheCacheFolder_TheRefreshIsNotOffered_AndNothingIsCreated(bool Container)
        {
            Directory.Delete(Cache);
            var Terminal = new MenuTerminal(Script(IntoRefresh, BackToExit));

            Run(Terminal, Container);

            Assert.That(Terminal.Texts, Does.Contain(Container ? W.ContainerCacheFolderMissing : W.CacheFolderMissing));
            Assert.That(Terminal.Prompts.Any(T => T.StartsWith(W.StartQuestion, StringComparison.Ordinal)), Is.False);
            Assert.That(Directory.Exists(Cache), Is.False, "never created");
            Assert.That(ServiceCalls, Is.Zero);
        }

        // ---- the refresh: a scripted service ----

        // a snapshot the scripted service reports, then waits until the console has looked at it (two looks at the keys
        // after it: one whole look at the progress)
        private sealed class Scripted
        {
            private readonly MenuTerminal Terminal;
            private long Sequence;
            internal readonly List<RidbRefreshProgress> Reported = new List<RidbRefreshProgress>();
            internal RidbRefreshRequest? Request;
            internal CancellationToken Token;
            internal bool Ended;

            internal Scripted(MenuTerminal Terminal)
            {
                this.Terminal = Terminal;
            }

            internal void Step(IProgress<RidbRefreshProgress> Progress, RidbRefreshStage Stage, long? Done = null, long? Total = null)
            {
                var Snapshot = new RidbRefreshProgress(++Sequence, Stage, Stage < RidbRefreshStage.ClearingCache, Done, Total);
                Reported.Add(Snapshot);
                Progress.Report(Snapshot);
                Seen();
            }

            internal void Seen()
            {
                int Start = Volatile.Read(ref Terminal.Polls);
                if (!SpinWait.SpinUntil(() => Volatile.Read(ref Terminal.Polls) >= Start + 2, 10000))
                {
                    throw new TimeoutException("the console never looked");
                }
            }

            // until the console cancels (true) or a while has passed (false)
            internal bool Canceled(int Milliseconds = 10000) => Token.WaitHandle.WaitOne(Milliseconds);
        }

        private static RidbRefreshResult Result(RidbRefreshOutcome Outcome, RidbRefreshStage Stage, bool CancelRequested = false, bool Installed = false,
            Exception? Error = null, IReadOnlyList<string>? Problems = null, string? Detail = null) =>
            new RidbRefreshResult(Outcome, Stage, Installed, Installed ? RefreshTime : null, TimeSpan.FromMinutes(3), Problems ?? RidbRefreshResult.NoProblems, Detail, Error, CancelRequested);

        private Scripted Use(MenuTerminal Terminal, Func<Scripted, IProgress<RidbRefreshProgress>, RidbRefreshResult> Body)
        {
            var Service = new Scripted(Terminal);
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                ServiceCalls++;
                Service.Request = Request;
                Service.Token = Token;
                try
                {
                    return Body(Service, Progress);
                }
                finally
                {
                    Service.Ended = true;
                }
            };
            return Service;
        }

        // a line of counts in the shared words (the culture's own number format)
        private static string CountLine(RidbRefreshStage Stage, long Done, long Total, string Elapsed) =>
            "  " + RefreshWording.ProgressDetails(RefreshWording.Count(new RidbRefreshProgress(1, Stage, true, Done, Total)), Elapsed);

        private static readonly RidbRefreshStage[] EveryStage =
        {
            RidbRefreshStage.AcquiringLock, RidbRefreshStage.CleaningUp, RidbRefreshStage.Preparing, RidbRefreshStage.Downloading, RidbRefreshStage.Extracting,
            RidbRefreshStage.PreparingDatabase, RidbRefreshStage.Importing, RidbRefreshStage.Saving, RidbRefreshStage.Indexing, RidbRefreshStage.Validating,
            RidbRefreshStage.Finalizing, RidbRefreshStage.ClearingCache, RidbRefreshStage.Installing, RidbRefreshStage.CheckingDatabase, RidbRefreshStage.RecordingRefresh,
            RidbRefreshStage.Completed
        };

        [Test]
        public void ASuccessfulRefresh_ShowsEachStageOnce_ItsCountsNeverTwice_ThenTheSharedResult_AndWhatUsesTheNewData()
        {
            WriteSettings();
            File.Copy(PreviousDb, InstalledDb);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            bool KeysDuring = false;
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                foreach (RidbRefreshStage Stage in EveryStage)
                {
                    S.Step(Progress, Stage);
                    if (Stage == RidbRefreshStage.Downloading)
                    {
                        KeysDuring = Terminal.KeysAsTyped && StopRegistrations == 1 && StopDisposals == 0;
                        S.Step(Progress, Stage, 0, 570_000_000);
                        S.Step(Progress, Stage, 0, 570_000_000);
                        S.Step(Progress, Stage, 285_000_000, 570_000_000);
                        S.Step(Progress, Stage, 570_000_000, 570_000_000);
                    }
                    if (Stage == RidbRefreshStage.Importing)
                    {
                        S.Step(Progress, Stage, 3, 6);
                    }
                }
                return Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Service.Request, Is.EqualTo(new RidbRefreshRequest(Folder, Cache, false)));
            Assert.That(ServiceCalls, Is.EqualTo(1));
            Assert.That(KeysDuring, Is.True, "Ctrl+C is read as a key and a stop is asked for while the refresh runs");
            Assert.That(Terminal.KeysAsTyped, Is.False, "and no longer after it");
            Assert.That(StopDisposals, Is.EqualTo(1));
            int From = Terminal.Texts.IndexOf(W.RunningHow);
            List<string> During = Terminal.Texts.Skip(From).TakeWhile(T => !T.StartsWith("Info: ", StringComparison.Ordinal)).ToList();
            var Expected = new List<string> { W.RunningHow };
            foreach (RidbRefreshStage Stage in EveryStage.Where(S_ => S_ != RidbRefreshStage.Completed))
            {
                string Name = RefreshWording.StageName(Stage);
                Expected.Add(Stage < RidbRefreshStage.ClearingCache ? RefreshWording.Refreshing(Name) : RefreshWording.Finishing(Name, false));
                if (Stage == RidbRefreshStage.Downloading)
                {
                    Expected.Add(CountLine(Stage, 0, 570_000_000, "0:00"));
                    Expected.Add(CountLine(Stage, 285_000_000, 570_000_000, "0:00"));
                    Expected.Add(CountLine(Stage, 570_000_000, 570_000_000, "0:00"));
                }
                if (Stage == RidbRefreshStage.Importing)
                {
                    Expected.Add(CountLine(Stage, 3, 6, "0:00"));
                }
            }
            Assert.That(During, Is.EqualTo(Expected), "each stage once in the shared words, from the cache step on finishing; each count once");
            int Ended = Terminal.Texts.FindIndex(From, T => T.StartsWith("Info: ", StringComparison.Ordinal));
            Assert.That(Terminal.Texts[Ended], Is.EqualTo("Info: The campground data was refreshed. Elapsed 0:00."));
            Assert.That(Terminal.Texts[Ended + 1], Is.EqualTo(W.AfterSuccess));
            Assert.That(MenusUnder(Terminal, W.CampgroundsChoices).Count, Is.EqualTo(2), "back at Campgrounds after it");
            AssertNothingSentOrStarted();
            Assert.That(Terminal.Texts, Has.None.EqualTo(ConsoleUi.StoppedText));
        }

        [Test]
        public void Counts_AreShownAtMostOnceEveryInterval_AndTheFirstOfAStageAtOnce()
        {
            InteractiveCampgroundRefresh.CountInterval = TimeSpan.FromSeconds(5);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Downloading);
                for (int Second = 0; Second <= 12; Second++)
                {
                    S.Step(Progress, RidbRefreshStage.Downloading, Second * 1_000_000L, 12_000_000);
                    Clock.Advance(TimeSpan.FromSeconds(1));
                }
                S.Step(Progress, RidbRefreshStage.Extracting, 1, 6);
                return Result(RidbRefreshOutcome.Canceled, RidbRefreshStage.Extracting);
            });

            Run(Terminal);

            List<string> Counts = Terminal.Texts.Where(T => T.StartsWith("  ", StringComparison.Ordinal) && T.Contains(" \u00b7 Elapsed ", StringComparison.Ordinal)).ToList();
            Assert.That(Counts, Is.EqualTo(new[]
            {
                CountLine(RidbRefreshStage.Downloading, 0, 12_000_000, "0:00"),
                CountLine(RidbRefreshStage.Downloading, 5_000_000, 12_000_000, "0:05"),
                CountLine(RidbRefreshStage.Downloading, 10_000_000, 12_000_000, "0:10"),
                CountLine(RidbRefreshStage.Extracting, 1, 6, "0:13")
            }), "the first count at once, then one every five seconds at most, a new stage's first at once");
        }

        [TestCase("CleaningUp", "Esc")]
        [TestCase("Downloading", "CtrlC")]
        [TestCase("Extracting", "Esc")]
        [TestCase("Importing", "CtrlC")]
        [TestCase("Finalizing", "Esc")]
        public void EscOrCtrlC_BeforeTheCacheStep_CancelsTheRefresh_ThroughItsToken_AndSaysItInTheSharedWords(string StageName, string Key)
        {
            RidbRefreshStage At = Enum.Parse<RidbRefreshStage>(StageName);
            bool Reached = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, new Press(Key == "Esc" ? TerminalInputKind.Escape : TerminalInputKind.Interrupt, () => Reached) }, BackToExit));
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                foreach (RidbRefreshStage Stage in EveryStage.TakeWhile(S_ => S_ <= At))
                {
                    S.Step(Progress, Stage);
                }
                Reached = true;
                return S.Canceled() ? Result(RidbRefreshOutcome.Canceled, At, CancelRequested: true) : Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Service.Token.IsCancellationRequested, Is.True, "the service's own token");
            Assert.That(Service.Ended, Is.True, "the refresh had ended before the console went on");
            Assert.That(Terminal.Texts.Count(T => T == RefreshWording.CancelingText), Is.EqualTo(1));
            Assert.That(Terminal.Texts, Does.Contain("Info: The refresh was canceled. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Terminal.Texts, Has.None.EqualTo(W.AfterSuccess));
            Assert.That(Terminal.Texts, Has.None.EqualTo(ConsoleUi.StoppedText), "Ctrl+C never ends the process while the refresh runs");
            Assert.That(MenusUnder(Terminal, W.CampgroundsChoices).Count, Is.EqualTo(2), "the console goes on at Campgrounds");
            Assert.That(ServiceCalls, Is.EqualTo(1), "no retry");
        }

        [Test]
        public void FromTheCacheStepOn_AKeySaysItFinishes_AndNothingIsCanceled()
        {
            bool Reached = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, new Press(TerminalInputKind.Escape, () => Reached), new Press(TerminalInputKind.Interrupt) }, BackToExit));
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                foreach (RidbRefreshStage Stage in EveryStage.TakeWhile(S_ => S_ <= RidbRefreshStage.Installing))
                {
                    S.Step(Progress, Stage);
                }
                Reached = true;
                S.Canceled(300);
                S.Seen();
                S.Step(Progress, RidbRefreshStage.CheckingDatabase);
                return Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true, CancelRequested: S.Token.IsCancellationRequested);
            });

            Run(Terminal);

            Assert.That(Service.Token.IsCancellationRequested, Is.False, "nothing can stop it there: it is not asked to");
            string TooLate = RefreshWording.Finishing(RefreshWording.StageName(RidbRefreshStage.Installing), true);
            Assert.That(Terminal.Texts.Count(T => T == TooLate), Is.EqualTo(2), "each key says it finishes");
            Assert.That(Terminal.Texts, Has.None.EqualTo(RefreshWording.CancelingText));
            Assert.That(Terminal.Texts, Does.Contain("Info: The campground data was refreshed. Elapsed 0:00."));
            Assert.That(Terminal.Texts, Does.Contain(W.AfterSuccess));
        }

        [Test]
        public void ACancelThatCameTooLate_FinishesTheRefresh_AndSaysSoInTheSharedWords()
        {
            bool Reached = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, new Press(TerminalInputKind.Escape, () => Reached) }, BackToExit));
            Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Finalizing);
                Reached = true;
                S.Canceled();
                //the service had passed its last checkpoint: it installs anyway
                return Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true, CancelRequested: true);
            });

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Info: " + RefreshWording.FinishedAfterCancel + "The campground data was refreshed. Elapsed 0:00."));
            Assert.That(Terminal.Texts, Does.Contain(W.AfterSuccess));
        }

        [Test]
        public void RepeatedKeys_WhileItCancels_SayItOnce_AndNeverEndTheProcess()
        {
            bool Reached = false;
            bool Again = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[]
            {
                Yes, new Press(TerminalInputKind.Interrupt, () => Reached), new Press(TerminalInputKind.Interrupt), new Press(TerminalInputKind.Escape), new Press(TerminalInputKind.Interrupt),
                (Action)(() => Again = true)
            }, BackToExit));
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Importing);
                Reached = true;
                S.Canceled();
                //it takes a while to reach its next checkpoint: three more keys meanwhile
                SpinWait.SpinUntil(() => Again, 10000);
                S.Seen();
                return Result(RidbRefreshOutcome.Canceled, RidbRefreshStage.Importing, CancelRequested: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Texts.Count(T => T == RefreshWording.CancelingText), Is.EqualTo(1));
            Assert.That(Terminal.Texts.Count(T => T == W.StillCanceling), Is.EqualTo(1), "said once");
            Assert.That(Terminal.Texts, Has.None.EqualTo(ConsoleUi.StoppedText));
            Assert.That(Service.Ended, Is.True);
            Assert.That(MenusUnder(Terminal, W.CampgroundsChoices).Count, Is.EqualTo(2));
        }

        [Test]
        public void TheEndOfTheInput_WhileItRuns_CancelsIt_WaitsForIt_AndEndsTheConsole()
        {
            bool Reached = false;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, new Press(TerminalInputKind.EndOfInput, () => Reached) }));
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Downloading, 1_000_000, 570_000_000);
                Reached = true;
                return S.Canceled() ? Result(RidbRefreshOutcome.Canceled, RidbRefreshStage.Downloading, CancelRequested: true) : Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Service.Token.IsCancellationRequested && Service.Ended, Is.True);
            Assert.That(Terminal.Texts, Does.Contain(W.InputEndedDuringRefresh));
            Assert.That(Terminal.Texts, Does.Contain(RefreshWording.CancelingText));
            Assert.That(Terminal.Texts, Does.Contain("Info: The refresh was canceled. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
        }

        [Test]
        public void ARequestToStop_WhileItRuns_CancelsIt_WaitsForIt_AndEndsTheConsole()
        {
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }));
            Scripted Service = Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Extracting, 2, 6);
                StopAction!();
                return S.Canceled() ? Result(RidbRefreshOutcome.Canceled, RidbRefreshStage.Extracting, CancelRequested: true) : Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Service.Token.IsCancellationRequested && Service.Ended, Is.True);
            Assert.That(Terminal.Texts, Does.Contain(W.AskedToStop));
            Assert.That(Terminal.Texts, Does.Contain("Info: The refresh was canceled. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.AskedToStop));
            Assert.That(StopDisposals, Is.EqualTo(1), "the stop's own handling is undone after the refresh");
        }

        [TestCase("DownloadFailed", "The download from Recreation.gov failed. Check the internet connection and try again. The campground data was not changed.")]
        [TestCase("DownloadTimedOut", "The download from Recreation.gov stopped responding, so Campertron ended it. Try again later. The campground data was not changed.")]
        [TestCase("ExtractionFailed", "The downloaded file is not a readable Recreation.gov export. Try again later. The campground data was not changed.")]
        [TestCase("ImportFailed", "The downloaded campground data could not be imported. Try again later. The campground data was not changed.")]
        [TestCase("ValidationFailed", "The downloaded campground data failed Campertron's checks, so it was not installed. The campground data was not changed.")]
        [TestCase("StorageFailed", "Campertron couldn't read or write the files it needs (for example, the disk may be full or a folder may not be writable). The campground data was not changed.")]
        [TestCase("InstallationFailed", "Campertron couldn't install the new campground data (another program may have been using the database). Try again. The campground data was not changed.")]
        [TestCase("AnotherRefreshRunning", "Another refresh of the campground data is running (in Campertron's console or another Campertron window). Try again when it has finished. The campground data was not changed.")]
        [TestCase("InstalledButCheckFailed", "The new campground data was installed, but Campertron's check of it found a problem. Restart Campertron; if the campground list or searches fail, refresh the campground data again.")]
        [TestCase("Unexpected", "The refresh stopped because of an unexpected problem (IOException). The campground data was not changed.")]
        public void AFailure_IsTheSharedWords_AnError_NeverAPathOrMessage_AndIsNotRetried(string OutcomeName, string Words)
        {
            RidbRefreshOutcome Outcome = Enum.Parse<RidbRefreshOutcome>(OutcomeName);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, P(W.DataStatus) }, BackToExit));
            Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Downloading);
                return Result(Outcome, RidbRefreshStage.Downloading, Installed: Outcome == RidbRefreshOutcome.InstalledButCheckFailed, Error: new IOException(Canary),
                    Problems: new[] { "/home/" + Canary + "/RecreationDotOrg.db: problem" }, Detail: Canary);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Texts, Does.Contain("Error: " + Words + " Elapsed 0:00."));
            Assert.That(Terminal.Texts, Has.None.EqualTo(W.AfterSuccess));
            AssertNoSecretOrProblemText(Terminal);
            Assert.That(ServiceCalls, Is.EqualTo(1), "no automatic retry");
            Assert.That(Terminal.Written.Any(T => T.Kind == "heading" && T.Text == W.StatusTitle), Is.True, "the console goes on");
        }

        [Test]
        public void ATimestampThatCouldNotBeWritten_IsTheSharedWarning()
        {
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            Use(Terminal, (S, Progress) => Result(RidbRefreshOutcome.RefreshedWithoutTimestamp, RidbRefreshStage.RecordingRefresh, Installed: true, Detail: Canary));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Warning: The campground data was refreshed, but Campertron couldn't record the date in General.yaml, so it may refresh again when it starts. Elapsed 0:00."));
            Assert.That(Terminal.Texts, Does.Contain(W.AfterSuccess));
            AssertNoSecretOrProblemText(Terminal);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AServiceThatRefusesTheRequest_IsTheWebUIsResult_NamedByItsTypeOnly(bool UnusableCache)
        {
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            Use(Terminal, (S, Progress) => throw (UnusableCache ? new InvalidOperationException(Canary) : new ArgumentException(Canary)));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Texts, Does.Contain(UnusableCache
                ? "Error: Campertron couldn't read or write the files it needs (for example, the disk may be full or a folder may not be writable). The campground data was not changed. Elapsed 0:00."
                : "Error: The refresh stopped because of an unexpected problem (ArgumentException). The campground data was not changed. Elapsed 0:00."));
            AssertNoSecretOrProblemText(Terminal);
        }

        [Test]
        public void NoQuestionIsAsked_AndNothingElseRuns_WhileTheRefreshRuns()
        {
            //the terminal fails the test if a question is asked while the operation's keys are read (MenuTerminal.ReadLine)
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            Use(Terminal, (S, Progress) =>
            {
                S.Step(Progress, RidbRefreshStage.Downloading);
                S.Step(Progress, RidbRefreshStage.Importing);
                return Result(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, Installed: true);
            });

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));

            Assert.That(Terminal.Prompts, Is.EqualTo(new[]
            {
                InteractiveWording.ChoosePrompt(10), InteractiveWording.ChoosePrompt(4), W.StartQuestion + " [y/N]: ", InteractiveWording.ChoosePrompt(4), InteractiveWording.ChoosePrompt(10)
            }), "the console only shows and reads keys while the refresh runs");
            AssertNothingSentOrStarted();
            Assert.That(ReadCount, Is.Zero, "the refresh reads none of the lookup's campground data");
        }

        // ---- the refresh: the real service, an export in memory ----

        private void RealService()
        {
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                ServiceCalls++;
                return RidbRefreshService.Run(Request, Progress, Token, new RidbRefreshOptions
                {
                    Handler = Export, WorkRoot = WorkRoot, StallTimeout = TimeSpan.FromSeconds(30), HeaderTimeout = TimeSpan.FromSeconds(30), UtcNow = () => RefreshTime
                });
            };
        }

        private void RealData()
        {
            Write(General, "outputTo: Console\nautoRefresh: false\nlastRidbDataRefresh: " + PreviousRefresh.ToString("o") + "\n");
            File.Copy(PreviousDb, InstalledDb);
            File.WriteAllText(Path.Join(Cache, "232445-Campsites.json"), "[]");
        }

        private void AssertNoLeftovers()
        {
            Assert.That(Directory.GetDirectories(Folder).Select(Path.GetFileName).Where(N => RidbStagedRefresh.IsStagingDirectoryName(N!)), Is.Empty, "no staging folder");
            Assert.That(Directory.GetFileSystemEntries(WorkRoot), Is.Empty, "no download folder or extraction");
            Assert.That(Directory.GetFiles(Folder, "*.partial-*"), Is.Empty, "no partial database");
            using FileStream? Lock = RidbStagedRefresh.TryAcquireRefreshLock(Folder, out _);
            Assert.That(Lock, Is.Not.Null, "the refresh lock was released");
        }

        [Test]
        public void TheRealService_InstallsTheNewData_LeavesNothingBehind_AndTheConsoleReadsItAtOnce()
        {
            RealData();
            Export = new FakeExport(ValidExport);
            RealService();
            InteractiveCampgrounds.Source = new CampgroundLookupSource();
            var Terminal = new MenuTerminal(Script(Find, new object[] { L(RidbFixture.Keys[1]), P(W.BackToList), Esc, Esc, P(W.RefreshOrInstall), Yes,
                P(W.LookUp), P(W.FindChoice), L(RidbFixture.Keys[1]), P(W.BackToList), Esc, Esc }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain("Info: The campground data was refreshed. Elapsed 0:00."));
            Assert.That(Terminal.Written.Any(T => T.Kind == "heading" && T.Text == OldMarker), Is.True, "before");
            Assert.That(Terminal.Written.Any(T => T.Kind == "heading" && T.Text == NewMarker), Is.True, "after, in the same console: no restart needed here");
            List<string> Stages = Terminal.Texts.Where(T => T.StartsWith("Refreshing the campground data: ", StringComparison.Ordinal)).ToList();
            Assert.That(Stages, Is.Unique, "a stage is said once (the console shows the stages it sees, not every one)");
            Assert.That(Yaml.GeneralConfigGetConfig(Folder).LastRidbDataRefresh, Is.EqualTo(RefreshTime));
            Assert.That(Directory.GetFiles(Cache), Is.Empty, "the derived cache was cleared, as the service does");
            Assert.That(Export.Requests, Is.EqualTo(1));
            AssertNoLeftovers();
            AssertNothingSentOrStarted();
            AssertNoControlCharacter(Terminal);
        }

        [Test]
        public void TheRealService_CanceledWhileItDownloads_LeavesTheCampgroundDataAsItWas()
        {
            RealData();
            Export = new FakeExport(ValidExport) { StallAfter = 64 };
            RealService();
            string Hash = Sha256(InstalledDb);
            string General_ = File.ReadAllText(General);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes, new Press(TerminalInputKind.Escape, () => Export.MaxPosition >= 64) }, BackToExit));

            Run(Terminal);
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain(RefreshWording.CancelingText));
            Assert.That(Terminal.Texts, Does.Contain("Info: The refresh was canceled. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash), "the installed database byte for byte");
            Assert.That(File.ReadAllText(General), Is.EqualTo(General_));
            Assert.That(Directory.GetFiles(Cache).Select(Path.GetFileName), Is.EqualTo(new[] { "232445-Campsites.json" }), "the cache as it was");
            AssertNoLeftovers();
        }

        [Test]
        public void TheRealService_AFailedDownload_LeavesTheCampgroundDataAsItWas_AndIsNotRetried()
        {
            RealData();
            Export = new FakeExport(ValidExport) { Status = HttpStatusCode.InternalServerError };
            RealService();
            string Hash = Sha256(InstalledDb);

            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));
            Run(Terminal);
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain("Error: The download from Recreation.gov failed. Check the internet connection and try again. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            Assert.That(Export.Requests, Is.EqualTo(1), "no retry");
            AssertNoLeftovers();
        }

        [Test]
        public void TheRealService_AnotherRefreshHoldingTheLock_IsRefused_AndNothingIsDownloadedOrChanged()
        {
            RealData();
            Export = new FakeExport(ValidExport);
            RealService();
            string Hash = Sha256(InstalledDb);
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));

            using (FileStream? Held = RidbStagedRefresh.TryAcquireRefreshLock(Folder, out _))
            {
                Assert.That(Held, Is.Not.Null);
                Run(Terminal);
            }
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain("Error: Another refresh of the campground data is running (in Campertron's console or another Campertron window). Try again when it has finished. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Export.Requests, Is.Zero);
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            AssertNoLeftovers();
        }

        [Test]
        public void TheRealService_AFailedInstallation_LeavesTheInstalledDatabaseAsItWas()
        {
            RealData();
            Export = new FakeExport(ValidExport);
            RealService();
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 300;
            string Marker_ = Marker(InstalledDb)!;
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));

            using (var Writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = InstalledDb, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
            {
                Writer.Open();
                using SqliteTransaction Transaction = Writer.BeginTransaction();
                using (SqliteCommand Command = Writer.CreateCommand())
                {
                    Command.Transaction = Transaction;
                    Command.CommandText = "UPDATE RecAreaEntries SET RecAreaName = RecAreaName WHERE rowid = 1";
                    Command.ExecuteNonQuery();
                }
                Run(Terminal);
                Transaction.Rollback();
            }
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain("Error: Campertron couldn't install the new campground data (another program may have been using the database). Try again. The campground data was not changed. Elapsed 0:00."));
            Assert.That(Marker(InstalledDb), Is.EqualTo(Marker_), "the installed data is the old one");
            Assert.That(Marker_, Is.EqualTo(OldMarker));
            Assert.That(Yaml.GeneralConfigGetConfig(Folder).LastRidbDataRefresh, Is.EqualTo(PreviousRefresh), "not recorded");
            AssertNoLeftovers();
        }

        // TD-54, unchanged: a monitoring of another Campertron program (its lock held here) is neither waited for, paused,
        // stopped nor touched by the console's refresh; the console's own monitoring (since v0.4.0's phase 11, D127), never
        // opened here, has nothing to pause
        [Test]
        public void TheRealService_AMonitoringOfAnotherProgram_IsNotPausedOrTouched()
        {
            RealData();
            Export = new FakeExport(ValidExport);
            RealService();
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));

            using (FileStream? Monitoring = CampertronLibrary.function.RecDotOrg.monitoring.MonitoringLock.TryAcquire(Folder, out _))
            {
                Assert.That(Monitoring, Is.Not.Null);
                Run(Terminal);
                Assert.That(CampertronLibrary.function.RecDotOrg.monitoring.MonitoringLock.TryAcquire(Folder, out _), Is.Null, "the other program's monitoring still holds its lock");
            }
            SqliteConnection.ClearAllPools();

            Assert.That(Terminal.Texts, Does.Contain("Info: The campground data was refreshed. Elapsed 0:00."));
            Assert.That(Terminal.Texts.Any(T => T.Contains("monitoring", StringComparison.OrdinalIgnoreCase) && !T.StartsWith(RefreshWording.ConfirmationBasics, StringComparison.Ordinal)
                && T != W.ConfirmationAfter && T != W.CampgroundsNote && T != "  7. Monitoring"), Is.False, "no pause or resume is said: there is none");
            Assert.That(Terminal.Texts, Does.Not.Contain(RefreshWording.PausingMonitoring).And.Not.Contain(CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringWording.ResumedAfterRefresh));
            AssertNoLeftovers();
        }

        [Test]
        public void TheRealService_WithoutTheCampgroundData_AFailedDownload_LeavesNoDatabase()
        {
            Write(General, "outputTo: Console\nautoRefresh: false\n");
            Export = new FakeExport(ValidExport) { Status = HttpStatusCode.NotFound };
            RealService();
            InteractiveCampgrounds.Source = new CampgroundLookupSource();
            var Terminal = new MenuTerminal(Script(IntoRefresh, new object[] { Yes }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain(W.InstallNote));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Error: The download from Recreation.gov failed.", StringComparison.Ordinal)), Is.True);
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName).Where(N => N!.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal)), Is.Empty, "no database, partial or whole");
            AssertNoLeftovers();
        }

        private static string Sha256(string File_) => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(File_)));

        private static void Checkpoint(string DbFile)
        {
            using var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
            Connection.Open();
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            Command.ExecuteNonQuery();
        }

        private static string? Marker(string DbFile)
        {
            using var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            Connection.Open();
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = "SELECT FacilityName FROM FacilitiesEntries WHERE FacilityID = $id";
            Command.Parameters.AddWithValue("$id", RidbFixture.Keys[1]);
            return Command.ExecuteScalar() as string;
        }

        // ---- the console's keys while an operation runs ----

        [Test]
        public void TheTerminal_ReadsCtrlCAsAKey_OnlyWhileAnOperationRuns_AndItsKeysAsAQuestionReadsThem()
        {
            bool ControlCAsInput = false;
            var Typed = new Queue<ConsoleKeyInfo?>(new ConsoleKeyInfo?[]
            {
                new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false), new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false),
                new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true), new ConsoleKeyInfo('\u0004', ConsoleKey.D, false, false, true),
                new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false), null
            });
            bool Available = true;
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, () => Typed.Dequeue(), () => ControlCAsInput, V => ControlCAsInput = V, () => Available);

            using (Terminal.ReadKeysAsTheyAreTyped())
            {
                Assert.That(ControlCAsInput, Is.True);
                Assert.That(Enumerable.Range(0, 6).Select(_ => Terminal.NextKeyTyped()), Is.EqualTo(new TerminalInputKind?[]
                {
                    TerminalInputKind.Escape, TerminalInputKind.Escape, TerminalInputKind.Interrupt, TerminalInputKind.EndOfInput, TerminalInputKind.Line, TerminalInputKind.EndOfInput
                }));
                Available = false;
                Assert.That(Terminal.NextKeyTyped(), Is.Null, "no key typed: nothing waits");
            }
            Assert.That(ControlCAsInput, Is.False, "as it was");
            Assert.That(ConsoleTerminalKey(new ConsoleKeyInfo('\u001a', ConsoleKey.Z, false, false, true), true), Is.EqualTo(TerminalInputKind.EndOfInput), "Ctrl+Z on Windows");
            Assert.That(ConsoleTerminalKey(new ConsoleKeyInfo('\u001a', ConsoleKey.Z, false, false, true), false), Is.EqualTo(TerminalInputKind.Line));

            var Gone = new ConsoleTerminal(ConsoleUi.Current, () => null, () => false, V => { }, () => throw new InvalidOperationException("no console"));
            Assert.That(Gone.NextKeyTyped(), Is.EqualTo(TerminalInputKind.EndOfInput), "a terminal that is gone");
        }

        private static TerminalInputKind ConsoleTerminalKey(ConsoleKeyInfo Key, bool CtrlZEndsInput) =>
            (TerminalInputKind)typeof(ConsoleTerminal).GetMethod("OperationKey", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { Key, CtrlZEndsInput })!;

        // ---- the architecture ----

        private static List<string> Reached()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveCampgrounds).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveCampgrounds).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheCampgroundsCode_ReachesTheSharedLookupAndRefresh_AndNothingThatReadsDownloadsInstallsOrLocksByItself()
        {
            List<string> Called = Reached();
            //since v0.4.0's phase 11 (D127): the console's own monitoring is paused for the refresh's time through the shared
            //controller, and only that (no start, stop, restart, shutdown or service of its own)
            string[] Paused =
            {
                "CampertronLibrary.function.Forms.MonitoringController::View", "CampertronLibrary.function.Forms.MonitoringControlView::get_IsActive",
                "CampertronLibrary.function.Forms.MonitoringController::PauseForRefresh", "CampertronLibrary.function.Forms.MonitoringController::ResumeAfterRefresh"
            };
            string[] Forbidden =
            {
                //no database, download, archive, import, installation, cleanup or lock of its own: only the shared service and queries
                "Microsoft.Data.Sqlite.", "Microsoft.EntityFrameworkCore.", "System.Net.", "System.IO.Compression.", "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter",
                "System.IO.File::", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.IO.Directory::Move", "System.IO.Directory::GetFiles", "System.IO.Directory::GetDirectories",
                "System.Threading.Mutex", "System.Threading.Semaphore", "System.Threading.Monitor::", "System.Threading.Thread::",
                "CampertronLibrary.function.RecDotOrg.sqlite.RidbStagedRefresh", "CampertronLibrary.function.RecDotOrg.sqlite.RidbIndexes", "CampertronLibrary.function.RecDotOrg.sqlite.Clear",
                "CampertronLibrary.function.RecDotOrg.sqlite.PopulateDbFromRIDBFiles", "CampertronLibrary.function.RecDotOrg.sqlite.LegacyAddressLayout", "CampertronLibrary.function.RecDotOrg.sqlite.RecreationDotOrgContext",
                "CampertronLibrary.function.RecDotOrg.sqlite.RidbDatabase", "CampertronLibrary.function.RecDotOrg.api.Web", "CampertronLibrary.function.RecDotOrg.api.Compression",
                "CampertronLibrary.function.RecDotOrg.api.RefreshRidbRecreationData", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.Load::DbExistsCheck", "CampertronLibrary.function.RecDotOrg.data.Load::ExtractEmbeddedDbTo",
                "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole", "CampertronLibrary.function.RecDotOrg.data.Cache",
                //no settings writer, YAML, search, results, monitoring, guided setup, notification or transfer
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.Base.Menu",
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.Forms.Monitoring", "CampertronLibrary.function.Forms.Results",
                "CampertronLibrary.function.Forms.GuidedSetup", "CampertronLibrary.function.Forms.Searches", "CampertronLibrary.function.Forms.Settings", "CampertronLibrary.function.Forms.DataOperationGate",
                "CampertronLibrary.function.Interactive.Searches.", "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.",
                "System.Diagnostics.Process", "System.Environment::", "System.Console::"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal)) && !Paused.Contains(C)), Is.Empty);
            foreach (string Needed in Paused.Concat(new[]
            {
                "CampertronLibrary.function.RecDotOrg.api.RidbRefreshService::Run", "CampertronLibrary.function.Forms.RefreshRules::CanBegin", "CampertronLibrary.function.Forms.RefreshRules::CanCancel",
                "CampertronLibrary.function.Forms.RefreshRules::UnavailableReason", "CampertronLibrary.function.Forms.RefreshWording::Describe", "CampertronLibrary.function.Forms.RefreshWording::StageName",
                "CampertronLibrary.function.Forms.RefreshWording::Count", "CampertronLibrary.function.Forms.RefreshWording::ProgressDetails", "CampertronLibrary.function.Forms.RefreshWording::Refreshing",
                "CampertronLibrary.function.Forms.RefreshWording::Finishing", "CampertronLibrary.function.Forms.RefreshWording::Finished", "CampgroundPickerEntry::Filter",
                "CampertronLibrary.function.Forms.CampgroundDetails::.ctor", "CampertronLibrary.function.Forms.CampgroundDetails::CityContext",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundPickerEntries", "CampertronLibrary.function.RecDotOrg.sqlite.Read::ParksWithCampgrounds",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByPark", "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueStates",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueCities", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByStateAndCity",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByState", "CampertronLibrary.function.RecDotOrg.sqlite.Read::GetCampgroundDetailsById",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::FilterChoicesByCampground", "CampertronLibrary.function.RecDotOrg.data.Load::DbExists",
                "CampertronLibrary.function.Base.CurrentConfiguration::LastRefreshed", "CampertronLibrary.function.Interactive.InteractiveTerminal::ReadKeysAsTheyAreTyped",
                "CampertronLibrary.function.Interactive.InteractiveTerminal::NextKeyTyped"
            }))
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheCampgroundsCode_HasNoProgressModelOrLaterPhaseOfItsOwn_AndNoControlCharacter()
        {
            Type[] Own = typeof(InteractiveCampgrounds).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveCampgrounds).Namespace).ToArray();
            Assert.That(Own.Where(T => T.IsEnum).Select(T => T.Name), Is.EqualTo(new[] { "ListEnd" }), "no stage or outcome of its own: the service's");
            Assert.That(Own.SelectMany(T => T.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(P_ => P_.Name == "Stage" || P_.Name == "Done" || P_.Name == "Total" || P_.Name == "Sequence" || P_.Name == "Outcome"), Is.Empty, "no progress or result model of its own");
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
            string AllLiterals = string.Join("\n", Literals);
            foreach (string Later in new[] { "Search now", "Run the search", "Results", "Start the monitoring", "Stop the monitoring", "Guided setup", "Export", "Import", "Beam", "same campsite", "free space", "disk space", "retry", "Retry", "https://" })
            {
                Assert.That(AllLiterals, Does.Not.Contain(Later), "no later phase, no link, no retry, no free-space check: " + Later);
            }
            Assert.That(Literals.Where(S => S.Any(char.IsControl)), Is.Empty, "no escape sequence or control character of its own");
        }
    }
}
