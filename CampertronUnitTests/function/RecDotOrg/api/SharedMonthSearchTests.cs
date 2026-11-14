using System.Globalization;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // TD-40, through the real search (GetAvailabilitiesByCampground → ProcessMonth → FilterData): searches of the same
    // campground and month read one cached month object and must neither change it nor see each other's dates.
    // The month is built in memory and put into RunData.SiteData with its URL already claimed in RunData.Urls, so
    // ProcessMonth takes it from the cache and never calls recreation.gov. The campsites are the first three of the
    // bundled campground's cached campsite list (whatever they are), the month is two months ahead (all dates in
    // the future), and Camp.History is a new empty directory. Each search gets its own RunningData around the same
    // month object, so the shared FilteredAvailableData of TD-04 (not fixed) plays no part.
    // Needs the isolated XDG_DATA_HOME of the test run (bundled DB and campground cache, like LoadTests).
    public class SharedMonthSearchTests
    {
        private const string CampgroundId = "232445";
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;
        private string HistoryRoot = string.Empty;
        private CampertronInternalConfig Config = null!;
        private DateTime MonthStart;
        private string Url = string.Empty;
        private List<string> SiteIds = new();

        [SetUp]
        public void Setup()
        {
            // CampertronConfig and ProcessMonth build dates from M/d/yyyy strings (current culture)
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Console.SetOut(TextWriter.Null);

            CampertronInternalConfig Installed = Load.GetConfig();
            Cache.PreCheckCache(CampgroundId, Installed.CachePath!, Installed.ConfigPath!);
            HistoryRoot = Path.Join(Path.GetTempPath(), "CampertronSharedMonthTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(HistoryRoot);
            // the campground cache exists, so the search reads only CachePath; ConfigPath only holds Camp.History
            Config = new CampertronInternalConfig
            {
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest },
                CachePath = Installed.CachePath,
                ConfigPath = HistoryRoot
            };

            DateTime Ahead = DateTime.Now.AddMonths(2);
            MonthStart = new DateTime(Ahead.Year, Ahead.Month, 1);
            // the URL ProcessMonth builds for that month
            Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundId}/month?start_date={MonthStart.Year}-{MonthStart:MM}-01T00%3A00%3A00.000Z";

            var Header = new List<ConsoleConfig.ConsoleConfigValue>();
            SiteIds = Cache.CheckCache(Search("ids", 1), ref Header, Config).Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
            Assert.That(SiteIds, Has.Count.EqualTo(3), "the bundled campground has at least three campsites");
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut!);
            CultureInfo.CurrentCulture = OriginalCulture!;
            if (Directory.Exists(HistoryRoot))
            {
                Directory.Delete(HistoryRoot, true);
            }
        }

        private CampertronConfig Search(string Name, int ConsecutiveDays)
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
                ConsecutiveDays = ConsecutiveDays
            };
            Search.GenerateSearchData();
            return Search;
        }

        // site 0: days 3, 4, 5 and 10; site 1: days 3, 20 and 21; site 2: none
        private static readonly int[][] AvailableDays = { new[] { 3, 4, 5, 10 }, new[] { 3, 20, 21 }, Array.Empty<int>() };

        private AvailabilityEntries Month()
        {
            int Days = DateTime.DaysInMonth(MonthStart.Year, MonthStart.Month);
            return new AvailabilityEntries
            {
                campsites = SiteIds.Select((Id, i) => new CampsitesDataEntry
                {
                    campsite_id = Id,
                    min_num_people = 1,
                    max_num_people = 6,
                    availabilities = Enumerable.Range(1, Days).Select(Day => new availabilities { availability = AvailableDays[i].Contains(Day) ? "Available" : "Reserved" }).ToList()
                }).ToList()
            };
        }

        private List<ConsoleConfig.ConsoleConfigValue> Run(CampertronConfig ThisSearch, AvailabilityEntries Shared)
        {
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            RunData.SiteData.TryAdd(Url, Shared);
            RunData.Urls.TryAdd(Url, false);
            return AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, Config);
        }

        private static List<string> Hits(List<ConsoleConfig.ConsoleConfigValue> Values, ConsoleConfig.OutputKind Kind) =>
            Values.Where(p => p.Kind == Kind).Select(p => $"{p.Metadata!.CampsiteID} {p.Metadata.Date:yyyy-MM-dd}").OrderBy(p => p, StringComparer.Ordinal).ToList();

        private List<string> Expected(params (int Site, int Day)[] Hits) =>
            Hits.Select(h => $"{SiteIds[h.Site]} {MonthStart.AddDays(h.Day - 1):yyyy-MM-dd}").OrderBy(p => p, StringComparer.Ordinal).ToList();

        private static List<string> Text(List<ConsoleConfig.ConsoleConfigValue> Values) =>
            Values.Select(p => $"{p.ConfigType}|{p.Color}|{p.ConfigValue}").ToList();

        [Test]
        public void SearchesOfOneCachedMonth_GetTheirOwnCorrectResults_AndLeaveTheMonthUnchanged()
        {
            AvailabilityEntries Shared = Month();
            var Entries = Shared.campsites!.ToList();
            var Lists = Shared.campsites!.Select(p => p.availabilities!).ToList();
            var Statuses = Shared.campsites!.Select(p => p.availabilities!.Select(a => a.availability).ToList()).ToList();

            List<ConsoleConfig.ConsoleConfigValue> SingleFirst = Run(Search("single", 1), Shared);
            List<ConsoleConfig.ConsoleConfigValue> Consecutive = Run(Search("consecutive", 2), Shared);
            List<ConsoleConfig.ConsoleConfigValue> SingleAgain = Run(Search("single", 1), Shared);

            // single nights: every available day; two consecutive nights: only the days in runs of two or more
            List<string> SingleExpected = Expected((0, 3), (0, 4), (0, 5), (0, 10), (1, 3), (1, 20), (1, 21));
            Assert.That(Hits(SingleFirst, ConsoleConfig.OutputKind.Availability), Is.EqualTo(SingleExpected));
            Assert.That(Hits(Consecutive, ConsoleConfig.OutputKind.ConsecutiveDate), Is.EqualTo(Expected((0, 3), (0, 4), (0, 5), (1, 20), (1, 21))));
            Assert.That(Hits(SingleAgain, ConsoleConfig.OutputKind.Availability), Is.EqualTo(SingleExpected));
            Assert.That(Text(SingleAgain), Is.EqualTo(Text(SingleFirst)), "the same search over the same month writes the same output");

            // the cached month: same entries, same status lists and statuses, no derived dates written into it
            Assert.That(Shared.campsites, Is.EqualTo(Entries).AsCollection);
            for (int i = 0; i < Entries.Count; i++)
            {
                Assert.That(Shared.campsites![i], Is.SameAs(Entries[i]));
                Assert.That(Entries[i].availabilities, Is.SameAs(Lists[i]));
                Assert.That(Entries[i].availabilities!.Select(a => a.availability), Is.EqualTo(Statuses[i]));
                Assert.That(Entries[i].AvailabilityDates, Is.Null, "the search must not write AvailabilityDates into the cached month");
            }
        }
    }
}
