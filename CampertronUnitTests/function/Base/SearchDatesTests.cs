using System.Globalization;
using System.Net;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // TD-11: the search dates (CampertronConfig.ConvertToZeroHour) and the requested month (AvailabilityApi) were built by
    // writing a date as "M/d/yyyy 0:00:00 AM" text and parsing it back with the current culture. In en-GB, en-AU, de-DE,
    // fr-FR and es-MX "11/14/2026" is no date (FormatException) and "11/1/2026" is 11 January; the month number in the
    // request URL followed the culture's calendar (fa-IR wrote 08 for November). They are now built from numbers.
    // These tests generate every search type at fixed times in each culture and compare with en-US, with the rules of
    // business-rules §2 and with the former parse in en-US, and run real searches over months held in memory.
    // User-written searchValueDates are still parsed with the current culture, including its calendar (a documented
    // contract, unchanged), so searches with written dates are compared only in cultures with the Gregorian calendar.
    public class SearchDatesTests
    {
        // the cultures of TD-11, the invariant culture (""), and two whose default calendar is not Gregorian
        public static readonly string[] Cultures = { "en-US", "en-GB", "en-AU", "de-DE", "fr-FR", "es-MX", "ja-JP", "", "th-TH", "fa-IR" };

        // a month end, a year end and a month without a 29th, at different times of the day
        private static readonly DateTime[] Times =
        {
            new DateTime(2026, 11, 28, 15, 30, 0),
            new DateTime(2026, 12, 31, 23, 59, 30),
            new DateTime(2027, 2, 27, 0, 0, 1)
        };

        private static CultureInfo Culture(string Name) => Name == "" ? CultureInfo.InvariantCulture : new CultureInfo(Name);

        private static void RequireCultureData()
        {
            if (new CultureInfo("de-DE").DateTimeFormat.GetDayName(DayOfWeek.Friday) != "Freitag")
            {
                Assert.Ignore("culture data not available (invariant globalization)");
            }
        }

        private static T InCulture<T>(string Name, Func<T> Action)
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            TextWriter OriginalOut = Console.Out;
            TextWriter OriginalError = Console.Error;
            try
            {
                CultureInfo.CurrentCulture = Culture(Name);
                // invalid dates and the 12-month cap write warnings; they are not what these tests look at
                Console.SetOut(TextWriter.Null);
                Console.SetError(TextWriter.Null);
                return Action();
            }
            finally
            {
                Console.SetOut(OriginalOut);
                Console.SetError(OriginalError);
                CultureInfo.CurrentCulture = Original;
            }
        }

        private static CampertronConfig Search(SearchTypes By, int Value = 0, params string[] Dates) => new CampertronConfig
        {
            DisplayName = "dates",
            CampgroundID = "232445",
            TotalHumans = 2,
            SearchBy = By,
            SearchValue = Value,
            SearchValueDates = Dates.ToList(),
            ShowMonday = true, ShowTuesday = true, ShowWednesday = true, ShowThursday = true, ShowFriday = true, ShowSaturday = true, ShowSunday = true,
            ConsecutiveDays = 1
        };

        // every search type; the ones with written dates only where the culture reads them as Gregorian dates
        private static List<(string Name, Func<CampertronConfig> Make)> Searches(bool WithWrittenDates)
        {
            var All = new List<(string, Func<CampertronConfig>)>
            {
                ("DaysOut 7", () => Search(SearchTypes.DaysOut, 7)),
                ("DaysOut 0", () => Search(SearchTypes.DaysOut, 0)),
                ("MonthsOut 2", () => Search(SearchTypes.MonthsOut, 2)),
                ("MonthsOut 14 (capped)", () => Search(SearchTypes.MonthsOut, 14)),
                ("SpecificDates, none", () => Search(SearchTypes.SpecificDates))
            };
            if (WithWrittenDates)
            {
                All.Add(("Until", () => Search(SearchTypes.Until, 0, "2027-03-03")));
                All.Add(("Until, past", () => Search(SearchTypes.Until, 0, "2026-11-01")));
                All.Add(("StartEndDate", () => Search(SearchTypes.StartEndDate, 0, "2026-12-30", "2027-01-02")));
                All.Add(("SpecificDates", () => Search(SearchTypes.SpecificDates, 0, "2027-01-01", "not a date", "2026-12-24")));
            }
            return All;
        }

        private static (List<DateTime> Dates, double? Months) Generate(Func<CampertronConfig> Make, string CultureName, DateTime Now) =>
            InCulture(CultureName, () =>
            {
                CampertronConfig Config = Make();
                Config.GenerateSearchData(Now);
                return (Config.GetSearchDates().ToList(), Config.GetMonthsToCheck());
            });

        private static bool Gregorian(string Name) => Culture(Name).Calendar is GregorianCalendar;

        [TestCaseSource(nameof(Cultures))]
        public void SearchDates_AreTheSameAsInEnUs(string CultureName)
        {
            RequireCultureData();
            foreach (DateTime Now in Times)
            {
                foreach (var (Name, Make) in Searches(Gregorian(CultureName)))
                {
                    var Expected = Generate(Make, "en-US", Now);
                    var Actual = Generate(Make, CultureName, Now);
                    Assert.That(Actual.Dates, Is.EqualTo(Expected.Dates), $"{Name} at {Now:yyyy-MM-dd HH:mm:ss}");
                    Assert.That(Actual.Months, Is.EqualTo(Expected.Months), $"{Name} at {Now:yyyy-MM-dd HH:mm:ss}: months to check");
                    Assert.That(Actual.Dates, Is.Not.Empty, $"{Name}: at least one date");
                }
            }
        }

        // business-rules §2 at 2026-11-28 15:30, and every computed date equal to what the former parse gave in en-US
        [Test]
        public void SearchDates_FollowTheRules_AsBefore()
        {
            DateTime Now = Times[0];
            DateTime Today = new DateTime(2026, 11, 28);
            List<DateTime> Days(DateTime From, DateTime To) => Enumerable.Range(0, (To - From).Days + 1).Select(i => From.AddDays(i)).ToList();
            var Expected = new Dictionary<string, (List<DateTime> Dates, double Months)>
            {
                ["DaysOut 7"] = (Days(Today, new DateTime(2026, 12, 5)), 1),
                ["DaysOut 0"] = (new List<DateTime> { Today }, 0),
                ["MonthsOut 2"] = (Days(Today, new DateTime(2027, 1, 28)), 2),
                ["MonthsOut 14 (capped)"] = (Days(Today, new DateTime(2028, 1, 28)), 12),
                ["SpecificDates, none"] = (new List<DateTime> { Today }, 0),
                ["Until"] = (Days(Today, new DateTime(2027, 3, 3)), 4),
                ["Until, past"] = (new List<DateTime> { Today }, 0),
                ["StartEndDate"] = (Days(new DateTime(2026, 12, 30), new DateTime(2027, 1, 2)), 2),
                ["SpecificDates"] = (new List<DateTime> { new DateTime(2026, 12, 24), new DateTime(2027, 1, 1) }, 2)
            };
            CultureInfo English = new CultureInfo("en-US");
            foreach (var (Name, Make) in Searches(true))
            {
                var Actual = Generate(Make, "en-US", Now);
                Assert.That(Actual.Dates, Is.EqualTo(Expected[Name].Dates), Name);
                Assert.That(Actual.Months, Is.EqualTo(Expected[Name].Months), Name + ": months to check");
                foreach (DateTime Date in Actual.Dates)
                {
                    // the former ConvertToZeroHour, which gave these values in en-US
                    DateTime Former = System.Convert.ToDateTime($"{Date.Month}/{Date.Day}/{Date.Year} 0:00:00 AM", English);
                    Assert.That(Date, Is.EqualTo(Former), Name);
                    Assert.That(Date.Kind, Is.EqualTo(Former.Kind), Name + ": DateTimeKind");
                    Assert.That(Date.TimeOfDay, Is.EqualTo(TimeSpan.Zero), Name + ": midnight");
                }
            }
        }

        [TestCaseSource(nameof(Cultures))]
        public void RequestedMonth_IsTheFirstOfTheMonth_WithTheSameUrl(string CultureName)
        {
            RequireCultureData();
            InCulture(CultureName, () =>
            {
                for (DateTime Day = new DateTime(2026, 1, 1); Day < new DateTime(2028, 1, 1); Day = Day.AddDays(1))
                {
                    DateTime First = AvailabilityApi.MonthStart(Day.AddHours(13));
                    Assert.That(First, Is.EqualTo(new DateTime(Day.Year, Day.Month, 1)), $"{Day:yyyy-MM-dd}");
                    // the former expression in en-US
                    Assert.That(First, Is.EqualTo(System.Convert.ToDateTime($"{Day.Month}/1/{Day.Year} 0:00:00 AM", new CultureInfo("en-US"))));
                }
                Assert.That(AvailabilityApi.MonthUrl("232445", new DateTime(2026, 11, 1)),
                    Is.EqualTo("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2026-11-01T00%3A00%3A00.000Z"));
                Assert.That(AvailabilityApi.MonthUrl("232490", new DateTime(2027, 2, 1)),
                    Is.EqualTo("https://www.recreation.gov/api/camps/availability/campground/232490/month?start_date=2027-02-01T00%3A00%3A00.000Z"));
                return true;
            });
        }

        // ---- real searches over months held in memory (as SharedMonthSearchTests; no request leaves the process) ----

        private const string CampgroundId = "232445";
        private string FixtureRoot = string.Empty;
        private string CachePath => Path.Join(FixtureRoot, "cache");
        private List<string> SiteIds = new();
        private DateTime Target;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            TextWriter OriginalOut = Console.Out;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Console.SetOut(TextWriter.Null);
                CampertronInternalConfig Installed = Load.GetConfig();
                FixtureRoot = Path.Join(Path.GetTempPath(), "CampertronSearchDatesTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(CachePath);
                Cache.PreCheckCache(CampgroundId, CachePath, Installed.ConfigPath!);
                DateTime Ahead = DateTime.Now.AddMonths(2);
                Target = new DateTime(Ahead.Year, Ahead.Month, 1);
                var Header = new List<ConsoleConfig.ConsoleConfigValue>();
                SiteIds = Cache.CheckCache(Search(SearchTypes.DaysOut, 1), ref Header, Config(OutputType.UnitTest)).Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
                Assert.That(SiteIds, Has.Count.EqualTo(3), "the bundled campground has at least three campsites");
            }
            finally
            {
                Console.SetOut(OriginalOut);
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

        private CampertronInternalConfig Config(OutputType Output) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            CachePath = CachePath,
            ConfigPath = FixtureRoot
        };

        // site 0: days 3, 4, 5 and 10 of the target month; site 1: days 3, 20 and 21; site 2: none
        private static readonly int[][] TargetDays = { new[] { 3, 4, 5, 10 }, new[] { 3, 20, 21 }, Array.Empty<int>() };

        private AvailabilityEntries Month(DateTime First, bool WithAvailability)
        {
            int DaysInMonth = DateTime.DaysInMonth(First.Year, First.Month);
            return new AvailabilityEntries
            {
                campsites = SiteIds.Select((Id, i) => new CampsitesDataEntry
                {
                    campsite_id = Id,
                    min_num_people = 1,
                    max_num_people = 6,
                    availabilities = Enumerable.Range(1, DaysInMonth).Select(Day => new availabilities { availability = WithAvailability && TargetDays[i].Contains(Day) ? "Available" : "Reserved" }).ToList()
                }).ToList()
            };
        }

        // every month of this year and the next is held in memory (only the target month has availability) and its URL,
        // written here with the invariant culture, already claimed: a wrong month reads an empty month instead of
        // calling recreation.gov
        private RunningData Run()
        {
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            for (DateTime First = new DateTime(Target.Year - 1, 1, 1); First.Year <= Target.Year + 1; First = First.AddMonths(1))
            {
                string Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundId}/month?start_date={First.ToString("yyyy-MM", CultureInfo.InvariantCulture)}-01T00%3A00%3A00.000Z";
                RunData.SiteData.TryAdd(Url, Month(First, First == Target));
                RunData.Urls.TryAdd(Url, false);
            }
            return RunData;
        }

        private List<string> Hits(CampertronConfig ThisSearch, string CultureName)
        {
            IWebProxy OriginalProxy = HttpClient.DefaultProxy;
            try
            {
                // belt and braces: if a month were ever requested that the test does not hold, it must not reach the network
                HttpClient.DefaultProxy = new WebProxy("http://127.0.0.1:9");
                return InCulture(CultureName, () =>
                {
                    ThisSearch.GenerateSearchData();
                    RunningData RunData = Run();
                    Task<List<ConsoleConfig.ConsoleConfigValue>> Searching = Task.Run(() =>
                    {
                        CultureInfo.CurrentCulture = Culture(CultureName);
                        return AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, Config(OutputType.UnitTest));
                    });
                    Assert.That(Searching.Wait(TimeSpan.FromSeconds(60)), Is.True, "the search asked for a month the test does not hold");
                    Assert.That(RunData.Urls.Keys, Is.SubsetOf(RunData.SiteData.Keys), "only months the test holds were requested");
                    return Searching.Result.Where(p => p.Kind == ConsoleConfig.OutputKind.Availability)
                        .Select(p => p.Metadata!.CampsiteID + " " + p.Metadata.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).OrderBy(p => p, StringComparer.Ordinal).ToList();
                });
            }
            finally
            {
                HttpClient.DefaultProxy = OriginalProxy;
            }
        }

        [TestCaseSource(nameof(Cultures))]
        public void Search_FindsTheSameAvailabilityAsInEnUs(string CultureName)
        {
            RequireCultureData();
            DateTime Last = Target.AddMonths(1).AddDays(-1);
            var Expected = TargetDays.SelectMany((Days, i) => Days.Select(Day => $"{SiteIds[i]} {Target.AddDays(Day - 1):yyyy-MM-dd}")).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var Searches = new List<(string, Func<CampertronConfig>)> { ("DaysOut 120", () => Search(SearchTypes.DaysOut, 120)) };
            if (Gregorian(CultureName))
            {
                Searches.Add(("StartEndDate", () => Search(SearchTypes.StartEndDate, 0, Target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
            }
            foreach (var (Name, Make) in Searches)
            {
                Assert.That(Hits(Make(), "en-US"), Is.EqualTo(Expected), Name + " in en-US");
                Assert.That(Hits(Make(), CultureName), Is.EqualTo(Expected), Name);
            }
        }
    }
}
