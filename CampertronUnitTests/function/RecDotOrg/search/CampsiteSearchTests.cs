using System.Globalization;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.search;
using static ConsoleConfig;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // The host-neutral search service (GS1, decision D45): CampsiteSearch.Plan reads and checks the chosen search files,
    // CampsiteSearch.Run searches them with the console's own search and returns typed results. Every month comes from
    // FakeGov through the transport seam (no network); the campsite details come from the shared fixture cache.
    public class CampsiteSearchTests : SearchTestBase
    {
        private List<string> Sites = new();

        [SetUp]
        public void SetUp()
        {
            Sites = SearchFixture.SiteIds(SearchFixture.Watchman, 5);
        }

        private Dictionary<string, string> AnswerMonths(string Campground, IEnumerable<string> SiteIds, Func<string, int, bool> Available, params DateTime[] Months)
        {
            var Bodies = new Dictionary<string, string>();
            foreach (DateTime Month in Months)
            {
                string Body = MonthJson(Month, SiteIds, Available);
                Gov.Json(Url(Campground, Month), Body);
                Bodies[Url(Campground, Month)] = Body;
            }
            return Bodies;
        }

        private static string NameOf(string Campground, string Id) => SearchFixture.Sites[Campground].First(S => S.CampsiteID == Id).CampsiteName!;

        // ---- Plan: one file at a time, read once, never written ----

        [Test]
        public void Plan_AValidFile_IsReady_WithItsDatesAndNoNetwork()
        {
            string File1 = WriteSearch("Trip.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));

            SearchRunPlan Plan = PlanOf(File1);

            SearchPlanEntry Entry = Plan.Entries.Single();
            Assert.That(Entry.State, Is.EqualTo(SearchPlanState.Ready));
            Assert.That(Entry.FileName, Is.EqualTo("Trip.yaml"));
            Assert.That(Entry.DisplayName, Is.EqualTo("Trip"));
            Assert.That(Entry.CampgroundId, Is.EqualTo(SearchFixture.Watchman));
            Assert.That(Entry.Search!.GetSearchDates(), Is.EqualTo(Enumerable.Range(0, 10).Select(D => MonthA.AddDays(D)).ToList()));
            Assert.That(Entry.Issues, Is.Empty);
            Assert.That(Gov.Requests, Is.Empty, "planning makes no request");
        }

        [Test]
        public void Plan_ADisabledFile_IsSearchedExplicitly_AndStaysAsItIs()
        {
            string File1 = WriteSearch("Off.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(6), AutoRun: false);
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day <= 3, MonthA);
            List<string> Before = Fingerprint(ConfigPath);

            SearchRunPlan Plan = PlanOf(File1);
            SearchRunResult Result = RunOf(Plan);

            Assert.That(Plan.Entries.Single().State, Is.EqualTo(SearchPlanState.Ready));
            Assert.That(Plan.Entries.Single().Diagnostics.Select(D => D.Code), Does.Not.Contain(ConfigCode.SearchDisabled), "the start's \"does not run\" is not about an explicit search");
            SearchResult Search = Result.Searches.Single();
            Assert.That(Search.AutoRun, Is.False);
            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Openings, Has.Count.EqualTo(Sites.Count * 3));
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(Before), "the file and the folder are byte for byte as they were, autoRun still false");
            Assert.That(System.IO.File.ReadAllText(Path.Join(ConfigPath, File1)), Does.Contain("autoRun: false"));
        }

        [Test]
        public void Plan_RefusesWhatAStartRefuses_AlsoInADisabledFile()
        {
            string Equal = WriteSearch("Equal.yaml", SearchFixture.Watchman, MonthA, MonthA);
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Negative.yaml"),
                "displayName: Negative\nautoRun: false\ncampgroundID: '232445'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: -3\nshowSaturday: true\nconsecutiveDays: 1\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Unknown.yaml"), "displayName: X\nautoRun: true\ncampgroundId: '232445'\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Malformed.yaml"), "displayName: [unclosed\n  autoRun: true\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Empty.yaml"), "# only a comment\n");

            SearchRunPlan Plan = PlanOf(Equal, "Negative.yaml", "Unknown.yaml", "Malformed.yaml", "Empty.yaml");

            Assert.That(Plan.Entries.Select(E => E.State), Is.All.EqualTo(SearchPlanState.Invalid));
            Assert.That(Plan.Entries.Select(E => E.Issues.Single().Code), Is.All.EqualTo(SearchIssueCode.FileInvalid));
            Assert.That(Plan.Entries[0].Diagnostics.Single(D => D.Severity == ConfigSeverity.Error).Code, Is.EqualTo(ConfigCode.DatesEqual));
            //in a disabled file the start would only warn; choosing it to run makes it what an enabled search is
            Assert.That(Plan.Entries[1].AutoRun, Is.False);
            Assert.That(Plan.Entries[1].Diagnostics.Single(D => D.Code == ConfigCode.SearchValueNegative).Severity, Is.EqualTo(ConfigSeverity.Error));
            Assert.That(Plan.Entries[2].Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.UnknownKey));
            Assert.That(Plan.Entries[3].Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.Malformed));
            Assert.That(Plan.Entries[4].Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.Empty));

            SearchRunResult Result = RunOf(Plan);
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Failed), "nothing could be searched");
            Assert.That(Result.Searches.Select(S => S.State), Is.All.EqualTo(SearchState.Invalid));
            Assert.That(Gov.Requests, Is.Empty);
        }

        [Test]
        public void Plan_WarningsDoNotBlock()
        {
            //no displayName: a warning; the search still runs and finds its openings
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "NoName.yaml"),
                $"autoRun: true\ncampgroundID: '232445'\ntotalHumans: 2\nsearchBy: StartEndDate\nsearchValueDates:\n- {MonthA:yyyy-MM-dd}\n- {MonthA.AddDays(4):yyyy-MM-dd}\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n");
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day == 2, MonthA);

            SearchRunPlan Plan = PlanOf("NoName.yaml");
            SearchRunResult Result = RunOf(Plan);

            SearchPlanEntry Entry = Plan.Entries.Single();
            Assert.That(Entry.State, Is.EqualTo(SearchPlanState.Ready));
            Assert.That(Entry.Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.DisplayNameMissing));
            Assert.That(Entry.Diagnostics.All(D => D.Severity != ConfigSeverity.Error));
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Result.Searches.Single().Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.DisplayNameMissing), "the warnings reach the result");
            Assert.That(Result.Searches.Single().Openings, Has.Count.EqualTo(Sites.Count));
        }

        [Test]
        public void Plan_FilesThatAreNotThereOrNotSearches_AreTypedOutcomes()
        {
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpPassword: SENTINEL-PASSWORD\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: Email\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Trip.yml"), "displayName: X\n");
            string NoCampground = WriteSearch("NoCampground.yaml", "", MonthA, MonthA.AddDays(2));

            SearchRunPlan Plan = PlanOf("Missing.yaml", "Email.yaml", "general.YAML", "../Escape.yaml", "Trip.yml", "", NoCampground, "Missing.yaml");

            Assert.That(Plan.Entries.Select(E => E.FileName), Is.EqualTo(new[] { "Missing.yaml", "Email.yaml", "general.YAML", "../Escape.yaml", "Trip.yml", "", "NoCampground.yaml" }),
                "one entry per name, in the order asked for");
            Assert.That(Plan.Entries.Select(E => E.Issues.Single().Code), Is.EqualTo(new[]
            {
                SearchIssueCode.FileMissing, SearchIssueCode.NotASearchFile, SearchIssueCode.NotASearchFile, SearchIssueCode.NotASearchFile,
                SearchIssueCode.NotASearchFile, SearchIssueCode.NotASearchFile, SearchIssueCode.CampgroundIdMissing
            }));
            Assert.That(Plan.Entries.Last().State, Is.EqualTo(SearchPlanState.Skipped));
            Assert.That(Plan.Entries.Take(6).Select(E => E.State), Is.All.EqualTo(SearchPlanState.Invalid));

            SearchRunResult Result = RunOf(Plan);
            Assert.That(Result.Searches.Last().State, Is.EqualTo(SearchState.Skipped));
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Failed));
            Assert.That(Gov.Requests, Is.Empty);
        }

        [Test]
        public void Plan_AnUnreadableFile_IsTyped()
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            {
                Assert.Ignore("Unix permissions as an ordinary user");
                return;
            }
            string File1 = WriteSearch("Locked.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(2));
            System.IO.File.SetUnixFileMode(Path.Join(ConfigPath, File1), UnixFileMode.None);

            SearchPlanEntry Entry = PlanOf(File1).Entries.Single();

            Assert.That(Entry.State, Is.EqualTo(SearchPlanState.Invalid));
            Assert.That(Entry.Issues.Single().Code, Is.EqualTo(SearchIssueCode.FileUnreadable));
        }

        [Test]
        public void Plan_UnusableFolders_AreAnInvalidRequest()
        {
            string File1 = WriteSearch("Trip.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(2));
            foreach ((string Config, string CacheFolder) in new[]
            {
                (ConfigPath, ConfigPath),
                (ConfigPath, ""),
                (ConfigPath, "relative/cache"),
                (Path.Join(ConfigPath, "missing"), SearchFixture.CachePath),
                (ConfigPath, Path.GetPathRoot(ConfigPath)!)
            })
            {
                SearchRunPlan Plan = CampsiteSearch.Plan(new SearchRunRequest(Config, CacheFolder, new[] { File1 }), DateTime.Now);
                Assert.That(Plan.Issues.Single().Code, Is.EqualTo(SearchIssueCode.InvalidRequest), CacheFolder);
                Assert.That(Plan.Entries.Single().State, Is.EqualTo(SearchPlanState.Invalid));
                Assert.That(RunOf(Plan).Status, Is.EqualTo(SearchRunStatus.Failed));
            }
            Assert.That(Gov.Requests, Is.Empty);
        }

        // ---- results ----

        [Test]
        public void ASearch_FindsEveryOpening_OfEveryDateItAsksFor()
        {
            //date completeness: five campsites, each available on its own set of days over two months
            DateTime Last = MonthB.AddMonths(1).AddDays(-1);
            string File1 = WriteSearch("Two months.yaml", SearchFixture.Watchman, MonthA, Last);
            bool Open(string Id, int Day) => (Day + Sites.IndexOf(Id)) % 3 == 0;
            AnswerMonths(SearchFixture.Watchman, Sites, Open, MonthA, MonthB);

            SearchRunResult Result = RunOf(PlanOf(File1));

            var Expected = new List<(string, DateTime)>();
            foreach (DateTime Month in new[] { MonthA, MonthB })
            {
                for (int Day = 1; Day <= DateTime.DaysInMonth(Month.Year, Month.Month); Day++)
                {
                    foreach (string Id in Sites.Where(Id => Open(Id, Day)))
                    {
                        Expected.Add((Id, Month.AddDays(Day - 1)));
                    }
                }
            }
            SearchResult Search = Result.Searches.Single();
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Months, Is.EqualTo(new[] { new SearchMonthResult(MonthA, true, null), new SearchMonthResult(MonthB, true, null) }));
            Assert.That(Search.Openings.Select(O => (O.CampsiteId, O.Night)), Is.EquivalentTo(Expected));
            Assert.That(Search.Openings, Has.Count.EqualTo(Expected.Count));
            Assert.That(Search.RequestsPlanned, Is.EqualTo(2));
            Assert.That(Search.RequestsCompleted, Is.EqualTo(2));
            Assert.That(Search.CampgroundName, Is.EqualTo("WATCHMAN CAMPGROUND"));
            Assert.That(Search.ParkName, Is.Not.Empty);
            SearchOpening First = Search.Openings[0];
            Assert.That(First.CampsiteName, Is.EqualTo(NameOf(SearchFixture.Watchman, First.CampsiteId)));
            Assert.That(First.MinOccupancy, Is.EqualTo(1));
            Assert.That(First.MaxOccupancy, Is.EqualTo(8));
            Assert.That(First.BookingUrl, Is.EqualTo("https://www.recreation.gov/camping/campsites/" + First.CampsiteId));
        }

        [Test]
        public void NoAvailability_IsACompleteSearchWithoutOpenings_NotAFailure()
        {
            string File1 = WriteSearch("Nothing.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => false, MonthA);

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Openings, Is.Empty);
            Assert.That(Search.Sites, Is.Empty);
            Assert.That(Search.Months.Single().Checked, Is.True);
            Assert.That(Search.Issues, Is.Empty);
        }

        [Test]
        public void OneOpening()
        {
            string File1 = WriteSearch("One.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Id == Sites[2] && Day == 5, MonthA);

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.Openings.Single().CampsiteId, Is.EqualTo(Sites[2]));
            Assert.That(Search.Openings.Single().Night, Is.EqualTo(MonthA.AddDays(4)));
            Assert.That(Search.Sites.Single().Stays.Single(), Is.EqualTo(new SearchStay(MonthA.AddDays(4), 1)));
            Assert.That(Search.Sites.Single().Stays.Single().CheckoutDate, Is.EqualTo(MonthA.AddDays(5)));
        }

        [Test]
        public void ManyOpenings_AreAllThere_WithoutTruncation()
        {
            //every campsite of the campground available every day of two months: more than 10,000 openings
            List<string> All = SearchFixture.SiteIds(SearchFixture.Watchman);
            DateTime Last = MonthB.AddMonths(1).AddDays(-1);
            string File1 = WriteSearch("Everything.yaml", SearchFixture.Watchman, MonthA, Last);
            Dictionary<string, string> Months = AnswerMonths(SearchFixture.Watchman, All, (Id, Day) => true, MonthA, MonthB);
            int Expected = All.Count * ((Last - MonthA).Days + 1);
            Assert.That(Expected, Is.GreaterThan(10000));

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.Openings, Has.Count.EqualTo(Expected));
            Assert.That(Search.Sites, Has.Count.EqualTo(All.Count));
            Assert.That(Search.Sites.All(S => S.Stays.Single().Nights == (Last - MonthA).Days + 1), "each campsite one stay over both months");
            //value for value what the console's own search shows
            List<ConsoleConfigValue> Stream = ConsoleStream(File1, OutputType.HtmlFile, Months);
            Assert.That(Stream.Count(V => V.Kind == OutputKind.Availability), Is.EqualTo(Expected));
            Assert.That(Keys(Search.Openings), Is.EquivalentTo(Keys(CampsiteSearch.OpeningsOf(Stream, CancellationToken.None, out _))));
        }

        [Test]
        public void TheSameCampsiteTwiceInAMonth_IsOneOpeningPerNight()
        {
            string File1 = WriteSearch("Twice.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            string Body = MonthJson(MonthA, Sites, (Id, Day) => Day == 3, Repeat: new[] { Sites[0] });
            Gov.Json(Url(SearchFixture.Watchman, MonthA), Body);

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();
            List<ConsoleConfigValue> Stream = ConsoleStream(File1, OutputType.HtmlFile, new Dictionary<string, string> { [Url(SearchFixture.Watchman, MonthA)] = Body });

            Assert.That(Stream.Count(V => V.Kind == OutputKind.Availability && V.Metadata!.CampsiteID == Sites[0]), Is.EqualTo(2), "the console's output lists it twice");
            Assert.That(Search.Openings.Count(O => O.CampsiteId == Sites[0]), Is.EqualTo(1), "the typed results once");
            Assert.That(Search.Openings, Has.Count.EqualTo(Sites.Count));
        }

        [Test]
        public void TheOrder_IsByNightThenCampsiteId_AndTheSameEveryRun()
        {
            //IDs of different lengths sort as numbers
            List<string> Ids = SearchFixture.SiteIds(SearchFixture.Watchman).OrderBy(Id => Id.Length).ThenBy(Id => Id, StringComparer.Ordinal).ToList();
            List<string> Chosen = new[] { Ids.First(), Ids[Ids.Count / 2], Ids.Last() }.Reverse().ToList();
            string File1 = WriteSearch("Order.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            AnswerMonths(SearchFixture.Watchman, Chosen, (Id, Day) => Day >= 3 && Day <= 6, MonthA);
            SearchRunPlan Plan = PlanOf(File1);

            List<string> First = Keys(RunOf(Plan).Searches.Single().Openings);
            List<string> Again = Keys(RunOf(PlanOf(File1)).Searches.Single().Openings);

            Assert.That(Again, Is.EqualTo(First));
            List<SearchOpening> Openings = RunOf(PlanOf(File1)).Searches.Single().Openings.ToList();
            Assert.That(Openings.Select(O => O.Night), Is.Ordered);
            foreach (var Night in Openings.GroupBy(O => O.Night))
            {
                Assert.That(Night.Select(O => O.CampsiteId), Is.EqualTo(Night.Select(O => O.CampsiteId).OrderBy(Id => Id.Length).ThenBy(Id => Id, StringComparer.Ordinal)));
            }
        }

        [Test]
        public void Filters_AreTheConsolesFilters()
        {
            //filter completeness: Saturdays only, one campsite by name; and an equipment filter nothing matches
            string Only = NameOf(SearchFixture.Watchman, Sites[1]);
            string Saturdays = WriteSearch("Saturdays.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1), Weekdays: "-----S-",
                Extra: "includeSites:\n- '" + Only + "'\n");
            string NoTrailer = WriteSearch("NoTrailer.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1), Extra: "includeEquipment:\n- NoSuchEquipment\n");
            Dictionary<string, string> Months = AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => true, MonthA);

            SearchRunResult Result = RunOf(PlanOf(Saturdays, NoTrailer));

            List<DateTime> SaturdayNights = Enumerable.Range(0, DateTime.DaysInMonth(MonthA.Year, MonthA.Month)).Select(D => MonthA.AddDays(D))
                .Where(D => D.DayOfWeek == DayOfWeek.Saturday).ToList();
            Assert.That(Result.Searches[0].Openings.Select(O => (O.CampsiteId, O.Night)), Is.EqualTo(SaturdayNights.Select(N => (Sites[1], N))));
            Assert.That(Result.Searches[1].Openings, Is.Empty);
            Assert.That(Result.Searches[1].State, Is.EqualTo(SearchState.Complete));
            foreach (string File1 in new[] { Saturdays, NoTrailer })
            {
                Assert.That(Keys(Result.Searches.Single(S => S.FileName == File1).Openings),
                    Is.EquivalentTo(Keys(CampsiteSearch.OpeningsOf(ConsoleStream(File1, OutputType.HtmlFile, Months), CancellationToken.None, out _))));
            }
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(1), "both searches share the month: one request");
        }

        [Test]
        public void ConsecutiveNights_AreTheConsolesRuns()
        {
            //consecutive completeness: only nights that belong to runs of two or more
            string File1 = WriteSearch("Two nights.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1), Consecutive: 2);
            var Open = new Dictionary<string, int[]> { [Sites[0]] = new[] { 3, 4, 5, 10 }, [Sites[1]] = new[] { 7 }, [Sites[2]] = new[] { 20, 21, 23, 24 } };
            Dictionary<string, string> Months = AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Open.TryGetValue(Id, out int[]? Days) && Days.Contains(Day), MonthA);

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.ConsecutiveNights, Is.EqualTo(2));
            Assert.That(Search.Openings.Select(O => (O.CampsiteId, O.Night.Day)), Is.EquivalentTo(new[]
            {
                (Sites[0], 3), (Sites[0], 4), (Sites[0], 5), (Sites[2], 20), (Sites[2], 21), (Sites[2], 23), (Sites[2], 24)
            }));
            SearchSite Site0 = Search.Sites.Single(S => S.CampsiteId == Sites[0]);
            Assert.That(Site0.Stays, Is.EqualTo(new[] { new SearchStay(MonthA.AddDays(2), 3) }));
            Assert.That(Search.Sites.Single(S => S.CampsiteId == Sites[2]).Stays, Is.EqualTo(new[] { new SearchStay(MonthA.AddDays(19), 2), new SearchStay(MonthA.AddDays(22), 2) }));
            Assert.That(Keys(Search.Openings), Is.EquivalentTo(Keys(CampsiteSearch.OpeningsOf(ConsoleStream(File1, OutputType.HtmlFile, Months), CancellationToken.None, out _))));
            //the console's own output (with its calendar values) has the same openings
            Assert.That(Keys(Search.Openings), Is.EquivalentTo(Keys(CampsiteSearch.OpeningsOf(ConsoleStream(File1, OutputType.Console, Months), CancellationToken.None, out _))));
        }

        [Test]
        public void ConsecutiveDaysBelowOne_ShowsNothing_AsTheConsole()
        {
            string File1 = WriteSearch("Zero.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9), Consecutive: 0);
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => true, MonthA);

            SearchRunPlan Plan = PlanOf(File1);
            SearchResult Search = RunOf(Plan).Searches.Single();

            Assert.That(Plan.Entries.Single().Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.ConsecutiveDaysBelowOne));
            Assert.That(Search.Openings, Is.Empty);
            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
        }

        [Test]
        public void ASharedRun_GivesEachSearchWhatItGetsAlone()
        {
            //searches of two campgrounds, one month shared by two of them, in one run and each alone
            List<string> MatherSites = SearchFixture.SiteIds(SearchFixture.Mather, 5);
            string Single = WriteSearch("Single.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1));
            string Pairs = WriteSearch("Pairs.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1), Consecutive: 2, Weekdays: "----FS-");
            string Mather = WriteSearch("Mather.yaml", SearchFixture.Mather, MonthA, MonthB.AddDays(14));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => (Day * 7 + Sites.IndexOf(Id)) % 4 != 0, MonthA);
            AnswerMonths(SearchFixture.Mather, MatherSites, (Id, Day) => Day % 2 == 0, MonthA, MonthB);

            SearchRunResult Together = RunOf(PlanOf(Single, Pairs, Mather));
            int SharedRequests = Gov.Count(Url(SearchFixture.Watchman, MonthA));

            Assert.That(Together.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(SharedRequests, Is.EqualTo(1), "the shared month is requested once");
            Assert.That(Together.RequestsPlanned, Is.EqualTo(3));
            foreach (string File1 in new[] { Single, Pairs, Mather })
            {
                SearchResult Alone = RunOf(PlanOf(File1)).Searches.Single();
                SearchResult InRun = Together.Searches.Single(S => S.FileName == File1);
                Assert.That(Keys(InRun.Openings), Is.EqualTo(Keys(Alone.Openings)), File1);
                Assert.That(InRun.Openings, Is.Not.Empty, File1);
            }
            Assert.That(Together.Searches.Select(S => S.FileName), Is.EqualTo(new[] { Single, Pairs, Mather }), "the plan's order");
        }

        // ---- the manual-search policy: no history, no Email settings ----

        [Test]
        public void CampHistory_IsNeitherReadNorWritten_AndEmailSettingsCantHideResults()
        {
            string File1 = WriteSearch("Marked.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day <= 4, MonthA);
            //every opening already reported by the Email loop, and the user's settings say Email (with a secret)
            foreach (string Id in Sites)
            {
                for (int Day = 1; Day <= 4; Day++)
                {
                    Directory.CreateDirectory(Path.Join(ConfigPath, "Camp.History", MonthA.Year.ToString(), MonthA.Month.ToString(), Day.ToString(), Id));
                }
            }
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n");
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: SENTINEL-PASSWORD\n");
            List<string> Before = Fingerprint(ConfigPath);

            SearchRunResult Result = RunOf(PlanOf(File1));

            Assert.That(Result.Searches.Single().Openings, Has.Count.EqualTo(Sites.Count * 4), "every current match, markers or not");
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(Before), "Camp.History, General.yaml, Email.yaml and the search file unchanged");
            Assert.That(Describe(Result), Does.Not.Contain("SENTINEL"));
        }

        [Test]
        public void CampHistory_IsNotCreated_AndEmailYamlIsNeverRead()
        {
            string File1 = WriteSearch("Fresh.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day == 1, MonthA);
            //an Email.yaml nobody could read: if the search read it, it would fail
            System.IO.File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpPassword: [SENTINEL\n");
            if (!OperatingSystem.IsWindows())
            {
                System.IO.File.SetUnixFileMode(Path.Join(ConfigPath, "Email.yaml"), UnixFileMode.None);
            }

            SearchRunResult Result = RunOf(PlanOf(File1));

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Directory.Exists(Path.Join(ConfigPath, "Camp.History")), Is.False);
        }

        // ---- structure: what the service can and can't reach ----

        [Test]
        public void TheService_NeverReachesTheConsoleTheExitTheHistoryTheEmailOrTheRenderedText()
        {
            var Methods = new List<MethodBase>();
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var Types = new Stack<Type>(typeof(CampsiteSearch).Assembly.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.RecDotOrg.search" ||
                new[] { typeof(SearchRunPlan), typeof(SearchRunResult), typeof(SearchResult), typeof(SearchOpening), typeof(SearchSite), typeof(SearchIssue), typeof(SearchProgress), typeof(SearchLists) }.Contains(T)));
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                Methods.AddRange(T.GetMethods(All));
                Methods.AddRange(T.GetConstructors(All));
                foreach (Type Nested in T.GetNestedTypes(All))
                {
                    Types.Push(Nested);
                }
            }
            var Called = new HashSet<string>();
            foreach (MethodBase M in Methods)
            {
                foreach (MemberInfo Member in Base.YamlDocumentPreservationTests.ReferencedMembers(M))
                {
                    Called.Add((Member.DeclaringType?.FullName ?? "?") + "::" + Member.Name);
                }
            }
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetAvailabilitiesByCampground"));
            Assert.That(Called, Does.Contain("ConsoleConfig+OutputMetadata::get_CampsiteID"));
            string[] Forbidden =
            {
                "System.Console", "CampertronLibrary.function.Base.ConsoleUi", "CampertronLibrary.function.Base.ConsoleResults", "CampertronLibrary.function.Base.CampsiteConfig",
                "System.Environment::Exit", "System.Environment::GetFolderPath", "System.Environment::FailFast",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_ExitProcess", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_BlockedWait",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetData", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetWebApiData",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_ConsoleDownload",
                "CampertronLibrary.function.RecDotOrg.data.Load::RecordNotificationHistory", "CampertronLibrary.function.RecDotOrg.data.Load::HasNewEntries",
                "CampertronLibrary.function.RecDotOrg.data.Load::HasUnreportedEntries", "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig",
                "CampertronLibrary.function.RecDotOrg.data.Load::PostProcess", "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults",
                "CampertronLibrary.function.RecDotOrg.data.Load::OutputResults", "CampertronLibrary.function.RecDotOrg.data.Load::Preload",
                "CampertronLibrary.function.Base.Yaml::EmailConfig", "CampertronLibrary.function.Base.Yaml::GeneralConfig", "CampertronLibrary.function.Base.Yaml::CampertronConfigGetConfigs",
                "CampertronLibrary.function.Base.Yaml::CampertronConfigConvertFromYaml", "CampertronLibrary.function.Base.ConfigFileReplace",
                "CampertronLibrary.function.Base.ResultsHtml::Render", "CampertronLibrary.function.Base.Menu",
                "EmailConfig::get_", "ConsoleConfig+ConsoleConfigValue::get_ConfigValue", "System.IO.File::Write", "System.IO.File::Delete", "System.IO.File::Move",
                "System.IO.Directory::Delete", "System.IO.StreamWriter"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
        }

        [Test]
        public void TheTypedResults_NeverDependOnTheOutputText()
        {
            //the same stream with every text scrambled gives the same openings: they come from Kind and Metadata only
            string File1 = WriteSearch("Text.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9), Consecutive: 2);
            Dictionary<string, string> Months = AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day >= 2 && Day <= 6, MonthA);
            List<ConsoleConfigValue> Stream = ConsoleStream(File1, OutputType.HtmlFile, Months);
            List<string> Before = Keys(CampsiteSearch.OpeningsOf(Stream, CancellationToken.None, out _));
            foreach (ConsoleConfigValue Value in Stream)
            {
                Value.ConfigValue = "scrambled";
            }

            Assert.That(Keys(CampsiteSearch.OpeningsOf(Stream, CancellationToken.None, out _)), Is.EqualTo(Before));
            Assert.That(Before, Is.Not.Empty);
        }

        [Test]
        public void TheOccupancyMetadata_ChangesNoRenderedOutput()
        {
            //the renderers ignore the occupancy GS1 adds to the metadata: the page and the console are the same without it
            string File1 = WriteSearch("Render.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9));
            string Pairs = WriteSearch("RenderPairs.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(9), Consecutive: 2);
            Dictionary<string, string> Months = AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day >= 2 && Day <= 6, MonthA);
            foreach (OutputType Output in new[] { OutputType.HtmlFile, OutputType.Console })
            {
                var With = new List<ConsoleConfigItem>
                {
                    new ConsoleConfigItem { Name = "Render", Values = ConsoleStream(File1, Output, Months) },
                    new ConsoleConfigItem { Name = "RenderPairs", Values = ConsoleStream(Pairs, Output, Months) }
                };
                Assert.That(With.SelectMany(I => I.Values!).Where(V => V.Metadata?.MinOccupancy != null), Is.Not.Empty);
                string Page = ResultsHtml.Render(With, CultureInfo.CurrentCulture);
                string Lines = string.Join("\n", ConsoleResults.Render(With, CultureInfo.CurrentCulture, 100, true).Select(L => L.Text));
                foreach (OutputMetadata Meta in With.SelectMany(I => I.Values!).Select(V => V.Metadata).OfType<OutputMetadata>())
                {
                    Meta.MinOccupancy = null;
                    Meta.MaxOccupancy = null;
                }
                Assert.That(ResultsHtml.Render(With, CultureInfo.CurrentCulture), Is.EqualTo(Page), Output.ToString());
                Assert.That(string.Join("\n", ConsoleResults.Render(With, CultureInfo.CurrentCulture, 100, true).Select(L => L.Text)), Is.EqualTo(Lines), Output.ToString());
            }
        }

        [Test]
        public void BookingUrls_AreTheFixedPrefixAndTheEscapedId()
        {
            Assert.That(CampsiteSearch.BookingUrl("123"), Is.EqualTo("https://www.recreation.gov/camping/campsites/123"));
            Assert.That(CampsiteSearch.BookingUrl("1 2/../?x=<y>"), Is.EqualTo("https://www.recreation.gov/camping/campsites/1%202%2F..%2F%3Fx%3D%3Cy%3E"));
        }

        [Test]
        public void TheResults_AreImmutable()
        {
            string File1 = WriteSearch("Fixed.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(4));
            AnswerMonths(SearchFixture.Watchman, Sites, (Id, Day) => Day == 2, MonthA);

            SearchRunResult Result = RunOf(PlanOf(File1));

            Assert.That(Result.Searches, Is.Not.InstanceOf<List<SearchResult>>().And.Not.InstanceOf<SearchResult[]>());
            Assert.That(Result.Searches.Single().Openings, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<SearchOpening>>());
            Assert.That(Result.Searches.Single().Openings[0].Equipment, Is.InstanceOf<System.Collections.ObjectModel.ReadOnlyCollection<string>>());
            Assert.That(() => ((IList<SearchOpening>)Result.Searches.Single().Openings).Add(Result.Searches.Single().Openings[0]), Throws.InstanceOf<NotSupportedException>());
            foreach (Type T in new[] { typeof(SearchRunResult), typeof(SearchResult), typeof(SearchOpening), typeof(SearchSite), typeof(SearchStay), typeof(SearchIssue), typeof(SearchMonthResult), typeof(SearchProgress), typeof(SearchPlanEntry), typeof(SearchRunPlan) })
            {
                foreach (PropertyInfo Property in T.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    MethodInfo? Setter = Property.SetMethod;
                    bool InitOnly = Setter != null && Setter.ReturnParameter.GetRequiredCustomModifiers().Any(M => M.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                    Assert.That(Setter == null || InitOnly, Is.True, T.Name + "." + Property.Name + " can't be changed");
                }
            }
        }

        internal static string Describe(SearchRunResult Result) => string.Join("\n",
            Result.Issues.Select(I => I.Message).Concat(Result.Searches.SelectMany(S => S.Issues.Select(I => I.Message).Concat(S.Diagnostics.Select(D => D.Message)))));
    }
}
