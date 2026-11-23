using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // What v0.3.0's PR 7 (D97) added to the shared form layer for the web UI's results page, at the layer's own level: a
    // Start's checks and words (ResultsStart) and an ended run as the Results tab shows it (ResultsOutcome), which the
    // desktop GUI's tab now uses too; a page of a search's rows for a UI without a virtualized list (ResultsListing), with
    // each booking link only as BookingLink accepts it; and the tab's own words written once (ResultsWording). Temporary
    // folders only; the runs' results are made by the test (GS1's immutable model), nothing is searched.
    public class ResultsPagesFormsTests : ConfigEditTestBase
    {
        internal static string Search(string Name, bool AutoRun = true, string Campground = "232447") =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '" + Campground + "'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private ResultsSearchList List() =>
            new ResultsSearchList(Folder, new CampgroundCatalog(() => new List<CampgroundPickerEntry>()), _ => false, CampsiteSearch.LoadEntry, () => Now, () => { });

        // ---- GS1's model, made by the test ----

        internal static SearchOpening Opening(string Site, DateTime Night, string? Name = null, string? Url = null) =>
            new SearchOpening(Site, Name ?? Site, "A", "STANDARD NONELECTRIC", new[] { "Tent" }, Night, "2:00 PM", "12:00 PM", 1, 6, Url ?? CampsiteSearch.BookingUrl(Site));

        internal static SearchResult Result(string File, IEnumerable<SearchOpening> Openings, SearchState State = SearchState.Complete, int Consecutive = 1)
        {
            List<SearchOpening> All = Openings.OrderBy(O => O.Night).ThenBy(O => O.CampsiteId.Length).ThenBy(O => O.CampsiteId, StringComparer.Ordinal).ToList();
            List<SearchSite> Sites = All.GroupBy(O => O.CampsiteId).OrderBy(G => G.Key.Length).ThenBy(G => G.Key, StringComparer.Ordinal).Select(G =>
            {
                SearchOpening First = G.First();
                List<DateTime> Nights = G.Select(O => O.Night).OrderBy(N => N).ToList();
                return new SearchSite(First.CampsiteId, First.CampsiteName, First.Loop, First.CampsiteType, First.Equipment, First.CheckinTime, First.CheckoutTime,
                    First.MinOccupancy, First.MaxOccupancy, First.BookingUrl, Nights, new[] { new SearchStay(Nights[0], Nights.Count) });
            }).ToList();
            return new SearchResult(File, File.Replace(".yaml", string.Empty), true, "232447", "UPPER PINES", "Yosemite", Consecutive, State, 1, 1,
                new[] { new SearchMonthResult(new DateTime(2026, 12, 1), true, null) }, All, Sites, Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>());
        }

        internal static SearchRunResult Run(SearchRunStatus Status, params SearchResult[] Searches) =>
            new SearchRunResult(Status, Now, TimeSpan.FromSeconds(3), 1, 1, 0, Searches, Array.Empty<SearchIssue>());

        private static IEnumerable<SearchOpening> Nights(int Count, int SitesPerNight)
        {
            for (int n = 0; n < Count; n++)
            {
                for (int s = 0; s < SitesPerNight; s++)
                {
                    yield return Opening((100 + s).ToString(CultureInfo.InvariantCulture), new DateTime(2026, 12, 1).AddDays(n));
                }
            }
        }

        // ---- a Start ----

        [Test]
        public void AStart_RefusesAsTheResultsTabAlwaysDid_AndPlansOnlyTheChosenFiles_InTheListsOrder()
        {
            ResultsSearchList Files = List();
            Files.Rescan(false, _ => { });
            ResultsStart None = ResultsStart.Prepare(Files.Rows, () => Files.Rescan(true, _ => { }), _ => throw new AssertionException("nothing to plan"));
            Assert.That((None.Plan, None.Status, None.ListNotice), Is.EqualTo(((SearchRunPlan?)null, ResultsWording.NotStartedNoChoice, (string?)null)));

            Write(Path.Join(Folder, "Bravo.yaml"), Search("Second"));
            Write(Path.Join(Folder, "Alpha.yaml"), Search("First"));
            Write(Path.Join(Folder, "Off.yaml"), Search("Not enabled", AutoRun: false));
            Files.Rescan(false, _ => { });
            Assert.That(Files.Rows.Where(R => R.IsSelected).Select(R => R.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Bravo.yaml" }), "the enabled files, chosen by default");

            //a chosen file the disk unchose: nothing starts, and the list says which
            Write(Path.Join(Folder, "Bravo.yaml"), "displayName: [broken\n");
            ResultsStart Changed = ResultsStart.Prepare(Files.Rows, () => Files.Rescan(true, _ => { }), _ => throw new AssertionException("nothing to plan"));
            Assert.That((Changed.Plan, Changed.Status), Is.EqualTo(((SearchRunPlan?)null, ResultsWording.NotStartedChangedOnDisk)));
            Assert.That(Changed.ListNotice, Is.EqualTo(ResultsWording.ChangedOnDisk(new[] { "Bravo.yaml" })));

            //a plan that can't be made
            ResultsStart Unreadable = ResultsStart.Prepare(Files.Rows, () => Files.Rescan(true, _ => { }), _ => throw new IOException("/a/path/never/shown"));
            Assert.That((Unreadable.Plan, Unreadable.Status), Is.EqualTo(((SearchRunPlan?)null, ResultsWording.NotStartedUnreadable)));

            //no chosen file can be searched: each chosen row says why
            var Invalid = new SearchIssue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, CampsiteSearch.Message(SearchIssueCode.FileInvalid), "Alpha.yaml");
            ResultsStart NoneReady = ResultsStart.Prepare(Files.Rows, () => Files.Rescan(true, _ => { }), Names => new SearchRunPlan(Folder, Folder, Now,
                Names.Select(N => new SearchPlanEntry(N, SearchPlanState.Invalid, null, true, null, 1, Array.Empty<ConfigDiagnostic>(), new[] { Invalid }, null)).ToList(), Array.Empty<SearchIssue>()));
            Assert.That((NoneReady.Plan, NoneReady.Status), Is.EqualTo(((SearchRunPlan?)null, ResultsWording.NotStartedNoneReady)));
            Assert.That(Files.Rows.Single(R => R.FileName == "Alpha.yaml").RunState, Is.EqualTo("Can't be searched: " + Invalid.Message));

            //a plan with a file that can run starts, with the chosen files in the list's order
            Files.Rows.Single(R => R.FileName == "Off.yaml").IsSelected = true;
            IReadOnlyList<string>? Asked = null;
            ResultsStart Started = ResultsStart.Prepare(Files.Rows, () => Files.Rescan(true, _ => { }), Names =>
            {
                Asked = Names;
                return CampsiteSearch.Plan(new SearchRunRequest(Folder, Path.Join(Folder, "cache"), Names), Now);
            });
            Assert.That(Started.Status, Is.Null);
            Assert.That(Started.Plan, Is.Not.Null);
            Assert.That(Started.Files, Is.EqualTo(new[] { "Alpha.yaml", "Off.yaml" }));
            Assert.That(Asked, Is.EqualTo(Started.Files));
        }

        // ---- an ended run ----

        [Test]
        public void AnEndedRun_IsShownAsTheResultsTabAlwaysShowedIt()
        {
            SearchResult Empty = Result("Alpha.yaml", Array.Empty<SearchOpening>());
            SearchResult Found = Result("Bravo.yaml", Nights(2, 3));
            var Problem = new SearchIssue(SearchIssueCode.HttpStatus, SearchIssueSeverity.Warning, CampsiteSearch.Message(SearchIssueCode.HttpStatus), StatusCode: 500);
            SearchRunResult Ended = Run(SearchRunStatus.CompletedWithProblems, Empty, Found) with { Issues = new[] { Problem, Problem } };
            var Outcome = new ResultsOutcome(Ended, new[] { "Alpha.yaml", "Bravo.yaml", "Gone.yaml" });
            Assert.That(Outcome.Searches.Select(S => S.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Bravo.yaml" }), "GS1's order");
            Assert.That(Outcome.Openings, Is.EqualTo(6));
            Assert.That(Outcome.Summary, Is.EqualTo(ResultsWording.Summary(SearchRunStatus.CompletedWithProblems, 6, 2)));
            Assert.That(Outcome.IssueLines, Is.EqualTo(new[] { ResultText.Issue(Problem) }), "each problem once");
            Assert.That(Outcome.Shown?.FileName, Is.EqualTo("Bravo.yaml"), "the first search with openings is shown first");
            Assert.That(Outcome.RunStateOf("Bravo.yaml"), Is.EqualTo("Complete: 6 openings found"));
            Assert.That(Outcome.RunStateOf("Gone.yaml"), Is.EqualTo(ResultsWording.NotSearched), "chosen, but not in the run's results");
            Assert.That(Outcome.RunStateOf("Other.yaml"), Is.Null, "not chosen");
            Assert.That((Outcome.HadProblems, Outcome.WasBlocked), Is.EqualTo((true, false)));
            Assert.That(new ResultsOutcome(Run(SearchRunStatus.Completed, Empty), new[] { "Alpha.yaml" }).Shown?.FileName, Is.EqualTo("Alpha.yaml"), "else the first");

            //a run that threw: a failed run whose problem names the exception's type only, never its message
            SearchRunResult Thrown = ResultsOutcome.Unexpected(new InvalidOperationException("secret /home/someone/CampertronConfig"));
            var Failed = new ResultsOutcome(Thrown, new[] { "Alpha.yaml" });
            Assert.That(Failed.Summary, Is.EqualTo("Nothing could be searched."));
            Assert.That(Failed.IssueLines, Is.EqualTo(new[] { "Error: " + CampsiteSearch.Message(SearchIssueCode.InternalError) }));
            Assert.That(string.Join(" ", Failed.IssueLines), Does.Not.Contain("secret").And.Not.Contain("/home"));
            Assert.That(Thrown.Issues.Single().DefectType, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(Failed.RunStateOf("Alpha.yaml"), Is.EqualTo(ResultsWording.NotSearched));
            Assert.That(new ResultsOutcome(Run(SearchRunStatus.Blocked, Empty), new[] { "Alpha.yaml" }).WasBlocked, Is.True);
        }

        // ---- a page of rows ----

        [Test]
        public void APageOfRows_HoldsAtMostItsSize_RepeatsItsHeading_AndSaysWhichRowsItShows()
        {
            //nine nights of 60 campsites: the first page ends inside the fourth night
            var Search = new ResultSearchViewModel(Result("Bravo.yaml", Nights(9, 60)), false);
            Assert.That(Search.Openings, Is.EqualTo(540));
            ResultsListing First = ResultsListing.Of(Search, 1);
            Assert.That((First.Page, First.Pages, First.First, First.Last, First.Total), Is.EqualTo((1, 3, 1, 200, 540)));
            Assert.That(First.Groups.Sum(G => G.Items.Count), Is.EqualTo(ResultsListing.PageSize));
            Assert.That(First.Groups.Select(G => G.Items.Count), Is.EqualTo(new[] { 60, 60, 60, 20 }));
            Assert.That(First.RangeText, Is.EqualTo("Showing openings 1 to 200 of 540."));
            Assert.That((First.HasPrevious, First.HasNext), Is.EqualTo((false, true)));

            ResultsListing Second = ResultsListing.Of(Search, 2);
            Assert.That((Second.First, Second.Last), Is.EqualTo((201, 400)));
            Assert.That(Second.Groups[0].HeadingText, Is.EqualTo(First.Groups.Last().HeadingText), "a page that starts inside a night shows its heading again");
            Assert.That(Second.Groups[0].Items.Count, Is.EqualTo(40));
            Assert.That(Second.Groups[0].HeadingText, Does.EndWith("60 campsites"), "the night's own count, as the desktop GUI's heading has it");

            //the pages together are the rows, in the result's order, once each
            List<ResultRow> Listed = Enumerable.Range(1, 3).SelectMany(P => ResultsListing.Of(Search, P).Groups.SelectMany(G => G.Items.Select(I => (ResultRow)I.Opening!))).ToList();
            Assert.That(Listed, Is.EqualTo(Search.Rows.Where(R => R is ResultOpeningRow).ToList()));
            Assert.That(ResultsListing.Of(Search, 3).RangeText, Is.EqualTo("Showing openings 401 to 540 of 540."));
            Assert.That(ResultsListing.Of(Search, 99).Page, Is.EqualTo(3), "a page past the last is the last");
            Assert.That(ResultsListing.Of(Search, -4).Page, Is.EqualTo(1));

            //rows that fit on one page say nothing about pages; a search without rows has none
            Assert.That(ResultsListing.Of(new ResultSearchViewModel(Result("A.yaml", Nights(2, 3)), false), 1).RangeText, Is.Null);
            ResultsListing None = ResultsListing.Of(new ResultSearchViewModel(Result("A.yaml", Array.Empty<SearchOpening>()), false), 1);
            Assert.That((None.Groups.Count, None.Pages, None.Total, None.First), Is.EqualTo((0, 1, 0, 0)));

            //consecutive nights: by campsite, the stays under it, the booking link the campsite's
            ResultsListing Stays = ResultsListing.Of(new ResultSearchViewModel(Result("C.yaml", Nights(3, 2), Consecutive: 2), false), 1);
            Assert.That(Stays.Groups.Select(G => G.HeadingText), Is.EqualTo(new[] { "Site 100", "Site 101" }));
            Assert.That(Stays.Groups.All(G => G.Site != null && G.Link!.IsShown && G.Items.All(I => I.Stay != null)), Is.True);
            Assert.That(ResultsListing.Of(new ResultSearchViewModel(Result("C.yaml", Nights(3, 300), Consecutive: 2), false), 2).RangeText, Is.EqualTo("Showing stays 201 to 300 of 300."));
        }

        [Test]
        public void ABookingLink_IsShownOnlyAsBookingLinkAcceptsIt_AndOtherwiseSaysWhy()
        {
            DateTime Night = new DateTime(2026, 12, 4);
            string[] Refused =
            {
                "javascript:alert(1)", "data:text/html,<b>x</b>", "file:///etc/passwd", "http://www.recreation.gov/camping/campsites/100",
                "https://evil.example/camping/campsites/100", "https://www.recreation.gov.evil.example/camping/campsites/100",
                "https://user:pass@www.recreation.gov/camping/campsites/100", "https://www.recreation.gov:8443/camping/campsites/100",
                "https://www.recreation.gov/camping/campsites/100?next=https://evil.example", "https://www.recreation.gov/camping/campsites/100#x",
                "https://www.recreation.gov/camping/campsites/101", "//www.recreation.gov/camping/campsites/100", "/camping/campsites/100",
                "https://www.recreation.gov/camping/campsites/100\r\nSet-Cookie: x=1", " https://www.recreation.gov/camping/campsites/100", "",
            };
            var Openings = Refused.Select((Url, i) => Opening("100", Night.AddDays(i), Url: Url)).Prepend(Opening("100", Night.AddDays(-1))).ToList();
            ResultsListing Page = ResultsListing.Of(new ResultSearchViewModel(Result("A.yaml", Openings), false), 1);
            List<ResultsLink> Links = Page.Groups.SelectMany(G => G.Items).Select(I => I.Link!).ToList();
            Assert.That(Links[0].Href, Is.EqualTo("https://www.recreation.gov/camping/campsites/100"));
            Assert.That(Links[0].Text, Is.EqualTo("Booking page of Site 100"));
            Assert.That(Links.Skip(1).Select(L => (L.Href, L.Text, L.IsShown)), Is.All.EqualTo(((string?)null, ResultsWording.NoBookingLink, false)));

            //an ID with characters of its own: the escaped page GS1 builds, and nothing else
            string Odd = "1 0/0?";
            ResultsLink Escaped = ResultsListing.Of(new ResultSearchViewModel(Result("A.yaml", new[] { Opening(Odd, Night, Name: "<b>Site & \"one\"</b>") }), false), 1).Groups[0].Items[0].Link!;
            Assert.That(Escaped.Href, Is.EqualTo("https://www.recreation.gov/camping/campsites/" + Uri.EscapeDataString(Odd)));
            Assert.That(Escaped.Text, Is.EqualTo("Booking page of Site <b>Site & \"one\"</b>"), "a name is text; the page encodes it");
        }

        // ---- the words ----

        [Test]
        public void TheResultsTabsOwnWords_AreWrittenOnce_AsTheDesktopGUIHadThem_WithThePagesOwn()
        {
            Assert.That(ResultsWording.RunHeading, Is.EqualTo("Searches to run"));
            Assert.That(ResultsWording.ResultsOfLabel, Is.EqualTo("_Results of"));
            Assert.That(SearchesWording.Plain(ResultsWording.ResultsOfLabel), Is.EqualTo("Results of"));
            Assert.That(ResultsWording.SelectEnabledHelp, Does.EndWith("Nothing runs until Start."));
            Assert.That(ResultsWording.BookingLinkText("Site 086"), Is.EqualTo("Booking page of Site 086"));
            Assert.That(ResultsWording.RowsShown(1, 200, 12030, Stays: false), Is.EqualTo("Showing openings 1 to " + 200.ToString("N0", CultureInfo.CurrentCulture) + " of " + 12030.ToString("N0", CultureInfo.CurrentCulture) + "."));
            Assert.That(ResultsWording.EndedAgo(TimeSpan.FromSeconds(20)), Does.StartWith("This run ended less than a minute ago. Availability may have changed since then"));
            Assert.That(ResultsWording.EndedAgo(TimeSpan.FromMinutes(1.5)), Does.StartWith("This run ended 1 minute ago."));
            Assert.That(ResultsWording.EndedAgo(TimeSpan.FromMinutes(59)), Does.StartWith("This run ended 59 minutes ago."));
            Assert.That(ResultsWording.EndedAgo(TimeSpan.FromMinutes(150)), Does.StartWith("This run ended 2 hours ago."));
            Assert.That(ResultsWording.EndedAgo(TimeSpan.FromMinutes(-5)), Does.StartWith("This run ended less than a minute ago."));
            foreach (string Words in new[] { ResultsWording.StartHelp, ResultsWording.SelectEnabledHelp, ResultsWording.SelectAllHelp, ResultsWording.OtherSessionRunning, ResultsWording.NoBookingLink })
            {
                Assert.That(Words, Does.Not.Contain("at startup").And.Not.Contain("when Campertron starts"), Words);
            }
        }
    }
}
