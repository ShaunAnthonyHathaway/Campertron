using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.RecDotOrg.data
{
    // NotificationHistory.Rearm on its own (decision D104; business-rules §5): which markers a completed check's months
    // re-arm. A month is given as RunningData.SiteData holds it, by its URL (AvailabilityApi.MonthUrl); the statuses are
    // read as the search reads them (only exactly "Available" is available, the statuses are the month's last days).
    // NotificationReopeningTests runs it through the monitoring service; every folder here is the test's own.
    public class NotificationHistoryRearmTests
    {
        private string ConfigPath = string.Empty;
        private Action<string> OriginalRemoveMarker = null!;
        private static readonly DateTime First = new DateTime(2031, 3, 1);

        [SetUp]
        public void SetUp()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronRearmTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            OriginalRemoveMarker = NotificationHistory.RemoveMarker;
        }

        [TearDown]
        public void TearDown()
        {
            NotificationHistory.RemoveMarker = OriginalRemoveMarker;
            if (Directory.Exists(ConfigPath))
            {
                Directory.Delete(ConfigPath, true);
            }
        }

        private CampertronInternalConfig Config(OutputType Output = OutputType.Email) =>
            new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = ConfigPath, GeneralConfig = new GeneralConfig { OutputTo = Output } };

        private string Marker(string Site, int Day, DateTime? Month = null) =>
            Path.Join(ConfigPath, "Camp.History", (Month ?? First).Year.ToString(CultureInfo.InvariantCulture), (Month ?? First).Month.ToString(CultureInfo.InvariantCulture),
                Day.ToString(CultureInfo.InvariantCulture), Site);

        private string Mark(string Site, int Day, DateTime? Month = null)
        {
            string Folder = Marker(Site, Day, Month);
            Directory.CreateDirectory(Folder);
            return Folder;
        }

        // a campsite entry whose statuses are the last Statuses.Length days of the month
        private static CampsitesDataEntry Entry(string Site, params string?[] Statuses) =>
            new CampsitesDataEntry { campsite_id = Site, availabilities = Statuses.Select(S => new availabilities { availability = S }).ToList() };

        // a whole month of one status, with some days set
        private static CampsitesDataEntry Month(string Site, string Status, DateTime Of, params (int Day, string? Status)[] Days)
        {
            var Statuses = Enumerable.Repeat<string?>(Status, DateTime.DaysInMonth(Of.Year, Of.Month)).ToArray();
            foreach (var (Day, Set) in Days)
            {
                Statuses[Day - 1] = Set;
            }
            return Entry(Site, Statuses);
        }

        private static KeyValuePair<string, AvailabilityEntries> Fetched(string Campground, DateTime Of, params CampsitesDataEntry[] Entries) =>
            new(AvailabilityApi.MonthUrl(Campground, Of), new AvailabilityEntries { campsites = Entries.ToList() });

        // ---- the month of a URL ----

        [Test]
        public void TryMonthOfUrl_ReadsEveryMonthMonthUrlWrites_InAnyCulture()
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            try
            {
                foreach (string Culture in new[] { "en-US", "fa-IR", "th-TH", "ar-SA" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(Culture);
                    for (int Month = 1; Month <= 12; Month++)
                    {
                        var Of = new DateTime(2030, Month, 1);
                        Assert.That(AvailabilityApi.TryMonthOfUrl(AvailabilityApi.MonthUrl("232445", Of), out DateTime Read), Is.True, Culture);
                        Assert.That(Read, Is.EqualTo(Of), Culture);
                    }
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = Original;
            }
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2030-13-01T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2030-00-01T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=0000-05-01T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2030-05-02T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/232445/month?start_date=2030-05-01T00%3A00%3A00.000Z&x=1")]
        [TestCase("https://example.invalid/api/camps/availability/campground/232445/month?start_date=2030-05-01T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground//month?start_date=2030-05-01T00%3A00%3A00.000Z")]
        [TestCase("https://www.recreation.gov/api/camps/availability/campground/a/b/month?start_date=2030-05-01T00%3A00%3A00.000Z")]
        public void TryMonthOfUrl_RefusesAnyOtherText(string? Url)
        {
            Assert.That(AvailabilityApi.TryMonthOfUrl(Url, out DateTime Read), Is.False);
            Assert.That(Read, Is.EqualTo(default(DateTime)));
        }

        // ---- which nights re-arm ----

        [TestCase("Reserved")]
        [TestCase("Not Reservable")]
        [TestCase("Not Available")]
        [TestCase("Not Reservable Management")]
        [TestCase("Open")]
        [TestCase("Walk Up")]
        [TestCase("Lottery")]
        [TestCase("available")]
        [TestCase("Available ")]
        public void AnyStatusButExactlyAvailable_ReArmsTheNight_AsTheSearchNeverCountsItAvailable(string Status)
        {
            string Folder = Mark("S1", 12);
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Available", First, (12, Status))) });

            Assert.That(Result, Is.EqualTo(new NotificationHistory.RearmResult(1, 0)));
            Assert.That(Directory.Exists(Folder), Is.False);
            Assert.That(CampsitesDataEntry.CalculateAvailableDates(Month("S1", "Available", First, (12, Status)).availabilities, First, 31), Does.Not.Contain(First.AddDays(11)),
                "consistent with the search: the night is not available to it");
        }

        [Test]
        public void AnAvailableNight_KeepsItsMarker_AndOnlyTheNightsThatAreUnavailableReArm()
        {
            string Kept = Mark("S1", 12);
            string Gone = Mark("S1", 13);
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Available", First, (13, "Reserved"))) });

            Assert.That(Result, Is.EqualTo(new NotificationHistory.RearmResult(1, 0)));
            Assert.That((Directory.Exists(Kept), Directory.Exists(Gone)), Is.EqualTo((true, false)));
        }

        [Test]
        public void TheStatuses_AreTheMonthsLastDays_AsTheSearchReadsThem()
        {
            //March has 31 days; ten statuses are days 22 to 31
            string Before = Mark("S1", 21);
            string FirstCovered = Mark("S1", 22);
            string LastCovered = Mark("S1", 31);
            var Ten = Entry("S1", "Reserved", "Available", "Available", "Available", "Available", "Available", "Available", "Available", "Available", "Not Available");
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Ten) });

            Assert.That(Result.Removed, Is.EqualTo(2));
            Assert.That((Directory.Exists(Before), Directory.Exists(FirstCovered), Directory.Exists(LastCovered)), Is.EqualTo((true, false, false)),
                "day 21 has no status: no observation");
            Assert.That(CampsitesDataEntry.CalculateAvailableDates(Ten.availabilities, First, 31), Is.EqualTo(Enumerable.Range(23, 8).Select(Day => First.AddDays(Day - 1))),
                "the search's own reading of the same entry");
        }

        [Test]
        public void NoStatus_AnEmptyStatus_AndMoreStatusesThanDays_AreNoObservationOfAMissingDay()
        {
            string Null = Mark("S1", 5);
            string Empty = Mark("S1", 6);
            string Unlisted = Mark("S9", 7);
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Reserved", First, (5, null), (6, string.Empty))) });

            Assert.That(Result, Is.EqualTo(new NotificationHistory.RearmResult(0, 0)));
            Assert.That(new[] { Null, Empty, Unlisted }.All(Directory.Exists), Is.True);

            //33 statuses for 31 days: the first two belong to the month before (the search's offsets); day 1 is the third
            string Day1 = Mark("S2", 1);
            var Long = Entry("S2", Enumerable.Repeat<string?>("Available", 2).Concat(Enumerable.Repeat<string?>("Reserved", 31)).ToArray());
            Assert.That(NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Long) }).Removed, Is.EqualTo(1));
            Assert.That(Directory.Exists(Day1), Is.False);
        }

        [Test]
        public void ACampsiteListedTwice_IsAvailableWhenEitherEntrySaysSo()
        {
            string Either = Mark("S1", 12);
            string Neither = Mark("S1", 13);
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[]
            {
                Fetched("1", First, Month("S1", "Reserved", First, (12, "Available")), Month("S1", "Reserved", First))
            });

            Assert.That(Result.Removed, Is.EqualTo(1));
            Assert.That((Directory.Exists(Either), Directory.Exists(Neither)), Is.EqualTo((true, false)));
        }

        [Test]
        public void OnlyTheMonthsGiven_AndOnlyTheirOwnCampsites_ReArm()
        {
            DateTime April = First.AddMonths(1);
            string March = Mark("S1", 12);
            string NotFetched = Mark("S1", 12, April);
            string OtherCampground = Mark("T1", 12);
            NotificationHistory.RearmResult Result = NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Reserved", First)) });

            Assert.That(Result.Removed, Is.EqualTo(1));
            Assert.That((Directory.Exists(March), Directory.Exists(NotFetched), Directory.Exists(OtherCampground)), Is.EqualTo((false, true, true)));

            Assert.That(NotificationHistory.Rearm(Config(), new[] { Fetched("2", First, Month("T1", "Reserved", First)), Fetched("1", April, Month("S1", "Available", April)) }).Removed,
                Is.EqualTo(1));
            Assert.That((Directory.Exists(NotFetched), Directory.Exists(OtherCampground)), Is.EqualTo((true, false)));
        }

        [Test]
        public void AMonthWithoutCampsites_OrWithAnotherKey_ReArmsNothing()
        {
            string Folder = Mark("S1", 12);
            var Months = new[]
            {
                new KeyValuePair<string, AvailabilityEntries>(AvailabilityApi.MonthUrl("1", First), new AvailabilityEntries()),
                new KeyValuePair<string, AvailabilityEntries>(AvailabilityApi.MonthUrl("1", First), new AvailabilityEntries { campsites = new List<CampsitesDataEntry>() }),
                new KeyValuePair<string, AvailabilityEntries>("not a month's URL", new AvailabilityEntries { campsites = new List<CampsitesDataEntry> { Month("S1", "Reserved", First) } }),
                new KeyValuePair<string, AvailabilityEntries>(AvailabilityApi.MonthUrl("1", First), new AvailabilityEntries { campsites = new List<CampsitesDataEntry> { new CampsitesDataEntry { campsite_id = "S1" } } })
            };
            Assert.That(NotificationHistory.Rearm(Config(), Months), Is.EqualTo(new NotificationHistory.RearmResult(0, 0)));
            Assert.That(Directory.Exists(Folder), Is.True);
        }

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.UnitTest)]
        public void AManualSearchsSettings_HaveNoHistory_AndReArmNothing(OutputType Output)
        {
            string Folder = Mark("S1", 12);
            Assert.That(NotificationHistory.Rearm(Config(Output), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }), Is.EqualTo(new NotificationHistory.RearmResult(0, 0)));
            Assert.That(Directory.Exists(Folder), Is.True);
            Assert.That(NotificationHistory.Rearm(new CampertronInternalConfig { ConfigPath = ConfigPath }, new[] { Fetched("1", First, Month("S1", "Reserved", First)) }).Removed,
                Is.Zero, "no settings: not the monitoring");
        }

        [Test]
        public void ItCreatesNothing_AndRemovesOnlyTheMarkerFolder()
        {
            Assert.That(NotificationHistory.Rearm(Config(OutputType.Ntfy), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }).Removed, Is.Zero);
            Assert.That(Directory.Exists(Path.Join(ConfigPath, "Camp.History")), Is.False, "no Camp.History is created");

            string Folder = Mark("S1", 12);
            Assert.That(NotificationHistory.Rearm(Config(OutputType.Ntfy), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }).Removed, Is.EqualTo(1));
            Assert.That((Directory.Exists(Folder), Directory.Exists(Path.GetDirectoryName(Folder))), Is.EqualTo((false, true)), "its day folder stays");
        }

        [Test]
        public void FoldersNotNamedAsTheMarkersAreWritten_AreNotRead()
        {
            string Padded = Path.Join(ConfigPath, "Camp.History", "2031", "03", "12", "S1");
            string PaddedDay = Path.Join(ConfigPath, "Camp.History", "2031", "3", "012", "S1");
            Directory.CreateDirectory(Padded);
            Directory.CreateDirectory(PaddedDay);
            Assert.That(NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }), Is.EqualTo(new NotificationHistory.RearmResult(0, 0)));
            Assert.That(Directory.Exists(Padded) && Directory.Exists(PaddedDay), Is.True);
        }

        [Test]
        public void AMarkerThatCantBeRemoved_IsCounted_AndTheOthersAreStillRemoved()
        {
            string WithFile = Mark("S1", 12);
            File.WriteAllText(Path.Join(WithFile, "note.txt"), "x");
            string Refused = Mark("S1", 13);
            string Removed = Mark("S1", 14);
            NotificationHistory.RemoveMarker = Folder =>
            {
                if (Folder == Refused)
                {
                    throw new UnauthorizedAccessException("refused");
                }
                OriginalRemoveMarker(Folder);
            };

            Assert.That(NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }), Is.EqualTo(new NotificationHistory.RearmResult(1, 2)));
            Assert.That((Directory.Exists(WithFile), Directory.Exists(Refused), Directory.Exists(Removed)), Is.EqualTo((true, true, false)));
            Assert.That(File.ReadAllText(Path.Join(WithFile, "note.txt")), Is.EqualTo("x"));
        }

        [Test]
        public void AFailureThatIsNotAFileSystemFailure_IsNotHidden()
        {
            Mark("S1", 12);
            NotificationHistory.RemoveMarker = Folder => throw new InvalidOperationException("a defect");
            Assert.Throws<InvalidOperationException>(() => NotificationHistory.Rearm(Config(), new[] { Fetched("1", First, Month("S1", "Reserved", First)) }));
        }
    }
}
