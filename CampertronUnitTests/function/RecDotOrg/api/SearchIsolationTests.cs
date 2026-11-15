using System.Globalization;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // TD-04: FilterConsecutiveDays used to group, filter and replace RunData.FilteredAvailableData, the set every search
    // of a run adds its hits to. So a consecutive-night search took other searches' hits into its runs (a false two-night
    // opening, another campground's campsites) and replaced the set with its own result (other searches' hits gone: an
    // empty Email gate, hits that were emailed but never marked). Now each search collects its own hits, filters only those
    // and adds only its final hits to the run's set.
    //
    // The searches run through the real GetAvailabilitiesByCampground with one RunningData per run, as
    // Load.RunConsoleSearch does: one after the other in a fixed order (a valid schedule of its Parallel.ForEach, so the
    // former failures are deterministic here), and in parallel. The months are built in memory and put into
    // RunData.SiteData with their URLs already claimed, so recreation.gov is never called (as SharedMonthSearchTests).
    // A search "alone" is the same search with a RunningData of its own: what every search must get in a shared run.
    // Paths: a fixture cache built from the bundled DB of the isolated test run; a new empty Camp.History per test.
    public class SearchIsolationTests
    {
        private const string Watchman = "232445";
        private const string Mather = "232490";
        private string FixtureRoot = string.Empty;
        private string CachePath => Path.Join(FixtureRoot, "cache");
        private string ConfigPath = string.Empty;
        private readonly Dictionary<string, List<string>> SiteIds = new();
        private DateTime MonthStart;
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            TextWriter OriginalWriter = Console.Out;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Console.SetOut(TextWriter.Null);
                CampertronInternalConfig Installed = Load.GetConfig();
                FixtureRoot = Path.Join(Path.GetTempPath(), "CampertronSearchIsolationTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(CachePath);
                DateTime Ahead = DateTime.Now.AddMonths(2);
                MonthStart = new DateTime(Ahead.Year, Ahead.Month, 1);
                foreach (string Campground in new[] { Watchman, Mather })
                {
                    Cache.PreCheckCache(Campground, CachePath, Installed.ConfigPath!);
                    var Header = new List<ConsoleConfig.ConsoleConfigValue>();
                    SiteIds[Campground] = Cache.CheckCache(Search("ids", Campground, 1), ref Header, Config(OutputType.UnitTest, FixtureRoot))
                        .Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
                    Assert.That(SiteIds[Campground], Has.Count.EqualTo(3), $"campground {Campground} has at least three campsites");
                }
            }
            finally
            {
                Console.SetOut(OriginalWriter);
                CultureInfo.CurrentCulture = Original;
            }
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
            Console.SetOut(TextWriter.Null);
            ConfigPath = Path.Join(FixtureRoot, "config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut!);
            CultureInfo.CurrentCulture = OriginalCulture!;
            if (Directory.Exists(ConfigPath))
            {
                Directory.Delete(ConfigPath, true);
            }
        }

        private CampertronInternalConfig Config(OutputType Output, string? ConfigFolder = null) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            CachePath = CachePath,
            ConfigPath = ConfigFolder ?? ConfigPath
        };

        // the whole month, every weekday unless Weekdays says otherwise
        private CampertronConfig Search(string Name, string Campground, int ConsecutiveDays, params DayOfWeek[] Weekdays)
        {
            bool Show(DayOfWeek Day) => Weekdays.Length == 0 || Weekdays.Contains(Day);
            var Search = new CampertronConfig
            {
                DisplayName = Name,
                CampgroundID = Campground,
                TotalHumans = 2,
                SearchBy = SearchTypes.StartEndDate,
                SearchValueDates = new List<string> { MonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MonthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                ShowMonday = Show(DayOfWeek.Monday), ShowTuesday = Show(DayOfWeek.Tuesday), ShowWednesday = Show(DayOfWeek.Wednesday),
                ShowThursday = Show(DayOfWeek.Thursday), ShowFriday = Show(DayOfWeek.Friday), ShowSaturday = Show(DayOfWeek.Saturday), ShowSunday = Show(DayOfWeek.Sunday),
                IncludeEquipment = new(), ExcludeEquipment = new(), IncludeSites = new(), ExcludeSites = new(),
                IncludeAttributes = new(), ExcludeAttributes = new(), IncludeCampsiteType = new(), ExcludeCampsiteType = new(),
                ConsecutiveDays = ConsecutiveDays
            };
            Search.GenerateSearchData();
            return Search;
        }

        private DayOfWeek WeekdayOf(int Day) => MonthStart.AddDays(Day - 1).DayOfWeek;

        private string Url(string Campground) => $"https://www.recreation.gov/api/camps/availability/campground/{Campground}/month?start_date={MonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)}-01T00%3A00%3A00.000Z";

        // available days per site (site index → days of the month) for each campground
        private Dictionary<string, AvailabilityEntries> Months(int[][] WatchmanDays, int[][] MatherDays)
        {
            AvailabilityEntries Month(string Campground, int[][] Days)
            {
                int DaysInMonth = DateTime.DaysInMonth(MonthStart.Year, MonthStart.Month);
                return new AvailabilityEntries
                {
                    campsites = SiteIds[Campground].Select((Id, i) => new CampsitesDataEntry
                    {
                        campsite_id = Id,
                        min_num_people = 1,
                        max_num_people = 6,
                        availabilities = Enumerable.Range(1, DaysInMonth).Select(Day => new availabilities { availability = i < Days.Length && Days[i].Contains(Day) ? "Available" : "Reserved" }).ToList()
                    }).ToList()
                };
            }
            return new Dictionary<string, AvailabilityEntries> { [Watchman] = Month(Watchman, WatchmanDays), [Mather] = Month(Mather, MatherDays) };
        }

        private RunningData NewRun(Dictionary<string, AvailabilityEntries> Months)
        {
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            foreach (var Month in Months)
            {
                RunData.SiteData.TryAdd(Url(Month.Key), Month.Value);
                RunData.Urls.TryAdd(Url(Month.Key), false);
            }
            return RunData;
        }

        private record Outcome(Dictionary<string, List<ConsoleConfig.ConsoleConfigValue>> Streams, RunningData RunData);

        // the searches one after the other with one RunningData, in the given order
        private Outcome InOrder(Dictionary<string, AvailabilityEntries> Months, CampertronInternalConfig Config, params CampertronConfig[] Searches)
        {
            RunningData RunData = NewRun(Months);
            var Streams = new Dictionary<string, List<ConsoleConfig.ConsoleConfigValue>>();
            foreach (CampertronConfig ThisSearch in Searches)
            {
                Streams[ThisSearch.DisplayName!] = AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, Config);
            }
            return new Outcome(Streams, RunData);
        }

        // the searches in parallel with one RunningData, as Load.RunConsoleSearch runs them
        private Outcome InParallel(Dictionary<string, AvailabilityEntries> Months, CampertronInternalConfig Config, params CampertronConfig[] Searches)
        {
            RunningData RunData = NewRun(Months);
            var Streams = new System.Collections.Concurrent.ConcurrentDictionary<string, List<ConsoleConfig.ConsoleConfigValue>>();
            Parallel.ForEach(Searches, ThisSearch =>
            {
                Streams[ThisSearch.DisplayName!] = AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, Config);
            });
            return new Outcome(Streams.ToDictionary(p => p.Key, p => p.Value), RunData);
        }

        private Outcome Alone(Dictionary<string, AvailabilityEntries> Months, CampertronInternalConfig Config, CampertronConfig ThisSearch) => InOrder(Months, Config, ThisSearch);

        // the date entries a stream reports (the HTML page and the console render these): campsite and date
        private static List<string> Hits(List<ConsoleConfig.ConsoleConfigValue> Stream) =>
            Stream.Where(p => p.Kind == ConsoleConfig.OutputKind.Availability || p.Kind == ConsoleConfig.OutputKind.ConsecutiveDate)
                .Select(p => p.Metadata!.CampsiteID + " " + p.Metadata.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        // every value of a stream, in order, with its kind and metadata
        private static List<string> Text(List<ConsoleConfig.ConsoleConfigValue> Stream) =>
            Stream.Select(p => $"{p.Kind}|{p.ConfigType}|{p.Color}|{p.ConfigValue}|{p.Metadata?.CampsiteID}|{p.Metadata?.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}").ToList();

        // the run's FilteredAvailableData: campsite and date of every hit, as a sorted multiset
        private static List<string> Set(RunningData RunData) =>
            RunData.FilteredAvailableData.Select(p => p.CampsiteID + " " + p.HitDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(p => p, StringComparer.Ordinal).ToList();

        private List<string> Day(string Campground, int Site, params int[] Days) =>
            Days.Select(d => SiteIds[Campground][Site] + " " + MonthStart.AddDays(d - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();

        // ---- the failures the audit reproduced (technical-debt.md TD-04) ----

        // a single-night opening at Watchman and nothing for a two-night search at Mather: with the single-night search
        // first, the two-night search used to replace the run's set with its own empty result, so the Email check said
        // "nothing new" and sent nothing
        [TestCase("single first")]
        [TestCase("two-night first")]
        public void SingleNightHits_SurviveAConsecutiveSearch_AndOpenTheEmailGate(string Order)
        {
            var Months = this.Months(new[] { Array.Empty<int>(), new[] { 15 } }, new[] { new[] { 10 } });
            CampertronConfig Single = Search("A single nights", Watchman, 1);
            CampertronConfig TwoNights = Search("B two nights", Mather, 2);
            CampertronInternalConfig Email = Config(OutputType.Email);
            Outcome Run = Order == "single first" ? InOrder(Months, Email, Single, TwoNights) : InOrder(Months, Email, TwoNights, Single);

            Assert.That(Hits(Run.Streams["A single nights"]), Is.EqualTo(Day(Watchman, 1, 15)));
            Assert.That(Hits(Run.Streams["B two nights"]), Is.Empty);
            Assert.That(Set(Run.RunData), Is.EqualTo(Day(Watchman, 1, 15)), "the opening is in the run's set, for the gate and the markers");
            // Load.PostProcess's Email gate (the markers it writes go to this test's own Camp.History)
            bool NewEntries = Load.HasNewEntries(new List<AvailableData>(Run.RunData.FilteredAvailableData), Email);
            Assert.That(NewEntries && Run.RunData.FilteredAvailableData.Count() > 0, Is.True, "the Email gate opens");
        }

        // a two-night search for one weekday finds nothing alone; a single-night search of the same campground in the
        // same run used to supply the following night and make it report a two-night opening
        [TestCase("single first")]
        [TestCase("two-night first")]
        public void ConsecutiveSearch_IsNotCompletedByAnotherSearchesDates(string Order)
        {
            var Months = this.Months(new[] { new[] { 3, 4 } }, Array.Empty<int[]>());
            CampertronConfig Single = Search("A single nights", Watchman, 1);
            CampertronConfig TwoNights = Search("B two nights, one weekday", Watchman, 2, WeekdayOf(3));
            CampertronInternalConfig ConsoleRun = Config(OutputType.Console);
            Outcome AloneB = Alone(Months, ConsoleRun, TwoNights);
            Outcome Run = Order == "single first" ? InOrder(Months, ConsoleRun, Single, TwoNights) : InOrder(Months, ConsoleRun, TwoNights, Single);

            Assert.That(Hits(AloneB.Streams["B two nights, one weekday"]), Is.Empty, "no two-night run matches the search's own weekday");
            Assert.That(Hits(Run.Streams["B two nights, one weekday"]), Is.Empty, "another search's Monday does not complete it");
            Assert.That(Text(Run.Streams["B two nights, one weekday"]), Is.EqualTo(Text(AloneB.Streams["B two nights, one weekday"])));
            Assert.That(Hits(Run.Streams["A single nights"]), Is.EqualTo(Day(Watchman, 0, 3, 4)));
            Assert.That(Set(Run.RunData), Is.EqualTo(Day(Watchman, 0, 3, 4)), "only the single-night search's hits");
        }

        // a two-night run at Watchman and nothing for a two-night search at Mather: the Mather search used to take the
        // Watchman campsite into its own results
        [TestCase("Watchman first")]
        [TestCase("Mather first")]
        public void ConsecutiveSearch_ShowsNoCampsiteOfAnotherCampground(string Order)
        {
            var Months = this.Months(new[] { new[] { 3, 4 } }, new[] { new[] { 10 } });
            CampertronConfig AtWatchman = Search("Watchman two nights", Watchman, 2);
            CampertronConfig AtMather = Search("Mather two nights", Mather, 2);
            CampertronInternalConfig ConsoleRun = Config(OutputType.Console);
            Outcome Run = Order == "Watchman first" ? InOrder(Months, ConsoleRun, AtWatchman, AtMather) : InOrder(Months, ConsoleRun, AtMather, AtWatchman);

            Assert.That(Run.Streams["Mather two nights"].Where(p => p.Metadata?.CampsiteID != null).Select(p => p.Metadata!.CampsiteID), Is.Empty,
                "no campsite in the Mather search's output");
            Assert.That(Text(Run.Streams["Mather two nights"]), Is.EqualTo(Text(Alone(Months, ConsoleRun, AtMather).Streams["Mather two nights"])));
            Assert.That(Text(Run.Streams["Watchman two nights"]), Is.EqualTo(Text(Alone(Months, ConsoleRun, AtWatchman).Streams["Watchman two nights"])));
            Assert.That(Set(Run.RunData), Is.EqualTo(Day(Watchman, 0, 3, 4)));
        }

        // business-rules §4, unchanged: with consecutiveDays 0 or below the hits are in the run's set but not in the output
        [TestCase(0)]
        [TestCase(-1)]
        public void ConsecutiveDaysBelowOne_KeepsItsHitsInTheRunSet_ButNotInTheOutput(int ConsecutiveDays)
        {
            var Months = this.Months(new[] { new[] { 3, 4 } }, Array.Empty<int[]>());
            Outcome Run = InOrder(Months, Config(OutputType.Console), Search("no output", Watchman, ConsecutiveDays));
            Assert.That(Hits(Run.Streams["no output"]), Is.Empty);
            Assert.That(Set(Run.RunData), Is.EqualTo(Day(Watchman, 0, 3, 4)));
        }

        // ---- every search gets what it gets alone, and the run's set is the union of the searches' own final hits ----

        // overlapping searches of two campgrounds: single nights, one weekday, two and three nights, and a two-night
        // search of the other campground (the kind of set behind the audit's statistical results, build-and-test.md §T)
        private (Dictionary<string, AvailabilityEntries> Months, CampertronConfig[] Searches) Mixed()
        {
            var Months = this.Months(
                new[] { new[] { 3, 4, 5, 10, 11 }, new[] { 3, 20, 21, 22 }, new[] { 15 } },
                new[] { new[] { 10, 11 }, new[] { 12 } });
            return (Months, new[]
            {
                Search("Watchman single nights", Watchman, 1),
                Search("Watchman one weekday", Watchman, 1, WeekdayOf(3)),
                Search("Watchman two nights", Watchman, 2),
                Search("Watchman three nights", Watchman, 3),
                Search("Mather two nights", Mather, 2)
            });
        }

        private void AssertEverySearchAsAlone(Outcome Run, Dictionary<string, AvailabilityEntries> Months, CampertronInternalConfig Config, CampertronConfig[] Searches, string Context)
        {
            List<string> Union = new List<string>();
            foreach (CampertronConfig ThisSearch in Searches)
            {
                Outcome AloneRun = Alone(Months, Config, ThisSearch);
                Assert.That(Text(Run.Streams[ThisSearch.DisplayName!]), Is.EqualTo(Text(AloneRun.Streams[ThisSearch.DisplayName!])), $"{Context}: {ThisSearch.DisplayName}");
                Union.AddRange(Set(AloneRun.RunData));
            }
            Assert.That(Set(Run.RunData), Is.EqualTo(Union.OrderBy(p => p, StringComparer.Ordinal).ToList()), $"{Context}: the run's set");
        }

        [Test]
        public void EverySearch_GetsWhatItGetsAlone_InEitherOrder()
        {
            var (Months, Searches) = Mixed();
            CampertronInternalConfig ConsoleRun = Config(OutputType.Console);
            // the searches find something (otherwise this would prove nothing)
            Assert.That(Hits(Alone(Months, ConsoleRun, Searches[2]).Streams["Watchman two nights"]), Is.EqualTo(Day(Watchman, 0, 3, 4, 5, 10, 11).Concat(Day(Watchman, 1, 20, 21, 22)).OrderBy(p => p, StringComparer.Ordinal)));
            Assert.That(Hits(Alone(Months, ConsoleRun, Searches[3]).Streams["Watchman three nights"]), Is.EqualTo(Day(Watchman, 0, 3, 4, 5).Concat(Day(Watchman, 1, 20, 21, 22)).OrderBy(p => p, StringComparer.Ordinal)));
            Assert.That(Hits(Alone(Months, ConsoleRun, Searches[4]).Streams["Mather two nights"]), Is.EqualTo(Day(Mather, 0, 10, 11)));

            AssertEverySearchAsAlone(InOrder(Months, ConsoleRun, Searches), Months, ConsoleRun, Searches, "in file order");
            AssertEverySearchAsAlone(InOrder(Months, ConsoleRun, Enumerable.Reverse(Searches).ToArray()), Months, ConsoleRun, Searches, "in reverse order");
        }

        [Test]
        public void EverySearch_GetsWhatItGetsAlone_InParallel()
        {
            var (Months, Searches) = Mixed();
            CampertronInternalConfig ConsoleRun = Config(OutputType.Console);
            for (int Iteration = 1; Iteration <= 50; Iteration++)
            {
                AssertEverySearchAsAlone(InParallel(Months, ConsoleRun, Searches), Months, ConsoleRun, Searches, $"parallel run {Iteration}");
            }
        }
    }
}
