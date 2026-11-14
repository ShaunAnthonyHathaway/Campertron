using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // Camp.History is the notification history of the Email loop only (business-rules §5). A manual search (Console,
    // HtmlFile) and UnitTest neither read nor write it, so they show every current match on every run, whatever ran
    // before; Email reports a campsite and date once and then suppresses it, across restarts.
    //
    // Each run goes through the real search (GetAvailabilitiesByCampground → ProcessMonth → FilterData) and then the
    // first part of Load.PostProcess: HasNewEntries, then Load.OutputResults for Console and HtmlFile (PostProcess
    // itself waits for a key afterwards), the real PostProcess for UnitTest, and for Email only PostProcess's gate
    // (HasNewEntries && FilteredAvailableData.Count() > 0), so no email is sent. The month is built in memory and put
    // into RunData.SiteData with its URL already claimed, so recreation.gov is never called (as SharedMonthSearchTests).
    // The browser launch is replaced by a recorder. Every RunningData and config is new for each run, so a run is a
    // process restart as far as Campertron's state goes: Camp.History on disk is the only thing carried over.
    // Paths: a fixture cache built from the bundled DB of the isolated test run, and a new Camp.History per test.
    public class NotificationHistoryTests
    {
        private const string CampgroundId = "232445";
        private static readonly string[] Modes = { "Console", "HtmlFile", "UnitTest", "Email" };
        private string FixtureRoot = string.Empty;
        private string CachePath => Path.Join(FixtureRoot, "cache");
        private string ConfigPath = string.Empty;
        private string HistoryPath => Path.Join(ConfigPath, "Camp.History");
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;
        private Action<string> OriginalOpen = null!;
        private readonly List<string> OpenedPages = new();
        private DateTime MonthStart;
        private string Url = string.Empty;
        private List<string> SiteIds = new();

        // available days per site (site index → days of the month)
        private static readonly int[][] Base = { new[] { 3, 4, 5, 10 }, new[] { 3, 20, 21 }, Array.Empty<int>() };
        // Base without site 1 on day 20 and with site 2 on day 15
        private static readonly int[][] Changed = { new[] { 3, 4, 5, 10 }, new[] { 3, 21 }, new[] { 15 } };

        private record Result(List<string> Hits, int Filtered, bool NewEntries, bool WouldSendEmail, string ConsoleText, string? Page);

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            CampertronInternalConfig Installed = Load.GetConfig();
            FixtureRoot = Path.Join(Path.GetTempPath(), "CampertronNotificationHistoryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CachePath);
            // a cache of its own (HtmlFile output replaces the *.html files in CachePath), built from the bundled DB
            Cache.PreCheckCache(CampgroundId, CachePath, Installed.ConfigPath!);

            DateTime Ahead = DateTime.Now.AddMonths(2);
            MonthStart = new DateTime(Ahead.Year, Ahead.Month, 1);
            Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundId}/month?start_date={MonthStart.Year}-{MonthStart:MM}-01T00%3A00%3A00.000Z";
            var Header = new List<ConsoleConfig.ConsoleConfigValue>();
            var Probe = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest }, CachePath = CachePath, ConfigPath = FixtureRoot };
            SiteIds = Cache.CheckCache(Search("ids"), ref Header, Probe).Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
            Assert.That(SiteIds, Has.Count.EqualTo(3), "the bundled campground has at least three campsites");
            CultureInfo.CurrentCulture = OriginalCulture!;
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            if (Directory.Exists(FixtureRoot))
            {
                Directory.Delete(FixtureRoot, true);
            }
        }

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            OriginalOpen = CampsiteConfig.OpenHtmlFile;
            OpenedPages.Clear();
            CampsiteConfig.OpenHtmlFile = FilePath => OpenedPages.Add(FilePath);
            ConfigPath = Path.Join(FixtureRoot, "config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut!);
            CampsiteConfig.OpenHtmlFile = OriginalOpen;
            CultureInfo.CurrentCulture = OriginalCulture!;
            if (Directory.Exists(ConfigPath))
            {
                Directory.Delete(ConfigPath, true);
            }
        }

        private CampertronConfig Search(string Name)
        {
            var Search = new CampertronConfig
            {
                DisplayName = Name,
                CampgroundID = CampgroundId,
                TotalHumans = 2,
                SearchBy = SearchTypes.StartEndDate,
                SearchValueDates = new List<string> { MonthStart.ToString("yyyy-MM-dd"), MonthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd") },
                ShowMonday = true, ShowTuesday = true, ShowWednesday = true, ShowThursday = true, ShowFriday = true, ShowSaturday = true, ShowSunday = true,
                IncludeEquipment = new(), ExcludeEquipment = new(), IncludeSites = new(), ExcludeSites = new(),
                IncludeAttributes = new(), ExcludeAttributes = new(), IncludeCampsiteType = new(), ExcludeCampsiteType = new(),
                ConsecutiveDays = 1
            };
            Search.GenerateSearchData();
            return Search;
        }

        private AvailabilityEntries Month(int[][] Days)
        {
            int DaysInMonth = DateTime.DaysInMonth(MonthStart.Year, MonthStart.Month);
            return new AvailabilityEntries
            {
                campsites = SiteIds.Select((Id, i) => new CampsitesDataEntry
                {
                    campsite_id = Id,
                    min_num_people = 1,
                    max_num_people = 6,
                    availabilities = Enumerable.Range(1, DaysInMonth).Select(Day => new availabilities { availability = Days[i].Contains(Day) ? "Available" : "Reserved" }).ToList()
                }).ToList()
            };
        }

        private static OutputType Mode(string Name) => Enum.Parse<OutputType>(Name);

        // one search of one month, then what PostProcess does with it for this output type (see the class comment)
        private Result Run(OutputType Output, int[][] Days)
        {
            var Config = new CampertronInternalConfig
            {
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = new EmailConfig(),
                CachePath = CachePath,
                ConfigPath = ConfigPath
            };
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            RunData.SiteData.TryAdd(Url, Month(Days));
            RunData.Urls.TryAdd(Url, false);
            var Captured = new StringWriter();
            Console.SetOut(Captured);
            OpenedPages.Clear();

            var Item = new ConsoleConfig.ConsoleConfigItem { Name = "Trip" };
            Item.Values = AvailabilityApi.GetAvailabilitiesByCampground(Search("Trip"), ref RunData, Config);
            RunData.AllConsoleConfigItems.Add(Item);
            Assert.That(RunData.Urls.Keys, Is.EqualTo(new[] { Url }), "only the in-memory month; no download");

            List<string> Hits = Item.Values.Where(p => p.Kind == ConsoleConfig.OutputKind.Availability)
                .Select(p => $"{SiteIds.IndexOf(p.Metadata!.CampsiteID!)}@{p.Metadata.Date!.Value.Day}").OrderBy(p => p, StringComparer.Ordinal).ToList();
            int Filtered = RunData.FilteredAvailableData.Count;
            bool NewEntries = false;
            bool WouldSend = false;
            if (Output == OutputType.UnitTest)
            {
                Assert.That(Load.PostProcess(Config, ref RunData), Is.True);
            }
            else
            {
                NewEntries = Load.HasNewEntries(new List<AvailableData>(RunData.FilteredAvailableData), Config);
                if (Output == OutputType.Email)
                {
                    WouldSend = NewEntries && RunData.FilteredAvailableData.Count() > 0;
                }
                else
                {
                    ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
                    Load.OutputResults(Config, RunData.AllConsoleConfigItems, ref Last);
                }
            }
            Console.SetOut(OriginalOut!);

            string? Page = null;
            if (Output == OutputType.HtmlFile)
            {
                Assert.That(OpenedPages, Has.Count.EqualTo(1), "one page per run");
                Page = File.ReadAllText(OpenedPages[0]);
            }
            else
            {
                Assert.That(OpenedPages, Is.Empty);
            }
            return new Result(Hits, Filtered, NewEntries, WouldSend, Captured.ToString(), Page);
        }

        private static List<string> Expected(params (int Site, int Day)[] Hits) =>
            Hits.Select(h => $"{h.Site}@{h.Day}").OrderBy(p => p, StringComparer.Ordinal).ToList();

        private static readonly List<string> AllBase = Expected((0, 3), (0, 4), (0, 5), (0, 10), (1, 3), (1, 20), (1, 21));
        private static readonly List<string> AllChanged = Expected((0, 3), (0, 4), (0, 5), (0, 10), (1, 3), (1, 21), (2, 15));

        // every directory and file under Camp.History, relative, with '/' (empty if it doesn't exist)
        private List<string> HistorySnapshot() => !Directory.Exists(HistoryPath) ? new List<string>()
            : Directory.GetFileSystemEntries(HistoryPath, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(ConfigPath, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList();

        // the marker layout since 2024: directories Camp.History/{yyyy}/{M}/{d}/{CampsiteID}, no zero padding
        private string Marker(int Site, int Day)
        {
            DateTime Date = MonthStart.AddDays(Day - 1);
            return $"Camp.History/{Date.Year}/{Date.Month}/{Date.Day}/{SiteIds[Site]}";
        }

        private List<string> Markers(params (int Site, int Day)[] Hits) => Hits.Select(h => Marker(h.Site, h.Day)).ToList();

        private List<string> MarkerDirectories() => HistorySnapshot().Where(p => p.Count(c => c == '/') == 4).ToList();

        private static readonly (int, int)[] BaseHits = { (0, 3), (0, 4), (0, 5), (0, 10), (1, 3), (1, 20), (1, 21) };

        private void AssertShowsAll(Result R, OutputType Output, List<string> ExpectedHits)
        {
            Assert.That(R.Hits, Is.EqualTo(ExpectedHits));
            if (Output == OutputType.Console || Output == OutputType.UnitTest)
            {
                Assert.That(R.ConsoleText.Split('\n').Count(l => l.Contains("\tDate:")), Is.EqualTo(ExpectedHits.Count), "one Date line per hit");
            }
            if (Output == OutputType.HtmlFile)
            {
                foreach (string Hit in ExpectedHits)
                {
                    string Id = SiteIds[int.Parse(Hit.Split('@')[0])];
                    Assert.That(R.Page, Does.Contain($"https://www.recreation.gov/camping/campsites/{Id}"));
                }
                Assert.That(R.Page!.Split("href=\"https://www.recreation.gov/camping/campsites/").Length - 1, Is.EqualTo(ExpectedHits.Count), "one booking link per hit");
                Assert.That(R.Page, Does.Not.Contain("No new matching availability"));
            }
        }

        // 1, 2, 7: the same manual search twice over unchanged availability
        [TestCase("Console")]
        [TestCase("HtmlFile")]
        [TestCase("UnitTest")]
        public void ManualSearchTwice_ShowsEveryMatchBothTimes_AndWritesNoHistory(string Name)
        {
            OutputType Output = Mode(Name);
            Result First = Run(Output, Base);
            Result Second = Run(Output, Base);

            AssertShowsAll(First, Output, AllBase);
            AssertShowsAll(Second, Output, AllBase);
            Assert.That(Second.ConsoleText, Is.EqualTo(First.ConsoleText), "same output both times");
            Assert.That(Second.Page, Is.EqualTo(First.Page), "same page both times");
            Assert.That(HistorySnapshot(), Is.Empty, "no Camp.History marker written");
        }

        // 3, 4: switching the output type between manual searches
        [Test]
        public void ManualConsoleHtmlFileConsole_AndHtmlFileConsole_ShowEveryMatchEachTime()
        {
            foreach (OutputType Output in new[] { OutputType.Console, OutputType.HtmlFile, OutputType.Console, OutputType.HtmlFile, OutputType.Console })
            {
                AssertShowsAll(Run(Output, Base), Output, AllBase);
            }
            Assert.That(HistorySnapshot(), Is.Empty);
        }

        // 5, 6, 7, 16, 17, 18, 20: history already present (written by Email) is neither read nor changed
        [TestCase("Console")]
        [TestCase("HtmlFile")]
        [TestCase("UnitTest")]
        public void ManualSearch_WithExistingHistory_ShowsEveryMatch_AndLeavesHistoryUnchanged(string Name)
        {
            OutputType Output = Mode(Name);
            Run(OutputType.Email, Base);
            // and a marker of another month, with a file in it, written by someone else
            Directory.CreateDirectory(Path.Join(HistoryPath, "2024", "7", "4", "999999"));
            File.WriteAllText(Path.Join(HistoryPath, "2024", "7", "4", "999999", "note.txt"), "x");
            List<string> Before = HistorySnapshot();
            Assert.That(MarkerDirectories(), Is.SupersetOf(Markers(BaseHits)));

            AssertShowsAll(Run(Output, Base), Output, AllBase);
            AssertShowsAll(Run(Output, Changed), Output, AllChanged);

            Assert.That(HistorySnapshot(), Is.EqualTo(Before), "manual searches do not add, remove or change markers");
            Assert.That(File.ReadAllText(Path.Join(HistoryPath, "2024", "7", "4", "999999", "note.txt")), Is.EqualTo("x"));
        }

        // 8–13: Email cycles over base, base, changed, base, changed; each cycle is a fresh RunningData and config
        [Test]
        public void EmailCycles_ReportOnlyUnreportedMatches_AndKeepSuppressingReappearingOnes()
        {
            Result First = Run(OutputType.Email, Base);
            Assert.That(First.Hits, Is.EqualTo(AllBase), "first cycle: every current match");
            Assert.That(First.NewEntries && First.WouldSendEmail, Is.True);
            Assert.That(MarkerDirectories(), Is.EquivalentTo(Markers(BaseHits)));

            Result Unchanged = Run(OutputType.Email, Base);
            Assert.That(Unchanged.Hits, Is.Empty, "already reported");
            Assert.That(Unchanged.Filtered, Is.Zero);
            Assert.That(Unchanged.NewEntries || Unchanged.WouldSendEmail, Is.False, "no email");

            // site 1 day 20 is gone, site 2 day 15 is new
            Result Changes = Run(OutputType.Email, Changed);
            Assert.That(Changes.Hits, Is.EqualTo(Expected((2, 15))), "only the new campsite and date; nothing for the removed one");
            Assert.That(Changes.WouldSendEmail, Is.True);
            Assert.That(MarkerDirectories(), Is.EquivalentTo(Markers(BaseHits).Append(Marker(2, 15))), "the removed one keeps its marker");

            // site 1 day 20 reappears: it was reported once, so it stays suppressed (current rule, unchanged)
            Result Reappeared = Run(OutputType.Email, Base);
            Assert.That(Reappeared.Hits, Is.Empty);
            Assert.That(Reappeared.WouldSendEmail, Is.False);

            Result Again = Run(OutputType.Email, Changed);
            Assert.That(Again.Hits, Is.Empty);
            Assert.That(Again.WouldSendEmail, Is.False);
            Assert.That(MarkerDirectories(), Has.Count.EqualTo(8));
        }

        // 14: a fresh Camp.History reports everything again
        [Test]
        public void EmailWithFreshHistory_ReportsEveryCurrentMatch()
        {
            Run(OutputType.Email, Base);
            Directory.Delete(HistoryPath, true);

            Result Fresh = Run(OutputType.Email, Base);
            Assert.That(Fresh.Hits, Is.EqualTo(AllBase));
            Assert.That(Fresh.WouldSendEmail, Is.True);
        }

        // 15: markers in the existing layout (as earlier versions, including manual HtmlFile runs, left them) are
        // still read by Email, and Email writes exactly that layout: empty directories, no zero padding
        [Test]
        public void EmailReadsAndWritesTheExistingMarkerLayout()
        {
            Directory.CreateDirectory(Path.Join(ConfigPath, Marker(0, 3)));
            Directory.CreateDirectory(Path.Join(ConfigPath, Marker(1, 21)));

            Result R = Run(OutputType.Email, Base);
            Assert.That(R.Hits, Is.EqualTo(Expected((0, 4), (0, 5), (0, 10), (1, 3), (1, 20))), "the two existing markers suppress their hits");
            Assert.That(MarkerDirectories(), Is.EquivalentTo(Markers(BaseHits)));
            DateTime Day3 = MonthStart.AddDays(2);
            Assert.That(Directory.Exists(Path.Join(HistoryPath, Day3.Year.ToString(), Day3.Month.ToString(), Day3.Day.ToString(), SiteIds[1])), Is.True);
            Assert.That(Directory.GetFiles(HistoryPath, "*", SearchOption.AllDirectories), Is.Empty, "markers are directories only");
        }

        // 16–19: Email, then manual searches, then Email again
        [Test]
        public void EmailHistory_DoesNotHideManualResults_AndSurvivesManualRuns()
        {
            Run(OutputType.Email, Base);
            List<string> EmailHistory = HistorySnapshot();

            AssertShowsAll(Run(OutputType.Console, Base), OutputType.Console, AllBase);
            AssertShowsAll(Run(OutputType.HtmlFile, Base), OutputType.HtmlFile, AllBase);
            AssertShowsAll(Run(OutputType.HtmlFile, Changed), OutputType.HtmlFile, AllChanged);
            Assert.That(HistorySnapshot(), Is.EqualTo(EmailHistory), "manual runs leave Email's history alone");

            Result Email = Run(OutputType.Email, Base);
            Assert.That(Email.Hits, Is.Empty, "Email still honors its own markers");
            Assert.That(Email.WouldSendEmail, Is.False);
            Result EmailChanged = Run(OutputType.Email, Changed);
            Assert.That(EmailChanged.Hits, Is.EqualTo(Expected((2, 15))), "seen only by a manual run, so still new for Email");
        }

        // 20: the policy for every output type, through the search: only Email consults the markers
        [TestCase("Console", false)]
        [TestCase("HtmlFile", false)]
        [TestCase("UnitTest", false)]
        [TestCase("Email", true)]
        public void OnlyEmailConsultsHistory(string Name, bool Consults)
        {
            foreach (var Hit in BaseHits)
            {
                Directory.CreateDirectory(Path.Join(ConfigPath, Marker(Hit.Item1, Hit.Item2)));
            }
            Result R = Run(Mode(Name), Base);
            Assert.That(R.Hits, Is.EqualTo(Consults ? new List<string>() : AllBase));
        }

        [Test]
        public void EveryOutputTypeIsCovered()
        {
            Assert.That(Enum.GetNames<OutputType>(), Is.EquivalentTo(Modes), "a new output type needs a notification-history decision");
        }
    }
}
