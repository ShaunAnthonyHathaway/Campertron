using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.Forms
{
    // The shared form layer (D79's PR 3), at its own level: what the Results tab of any UI shows and allows. The list of
    // search files and the rules of its choice (ResultsSearchList), the progress throttle (ProgressGate), the five minutes
    // after a block (BlockHold), the words of a run (ResultsWording), a search's result and its rows (ResultSearchViewModel,
    // ResultRow, ResultText), the booking link's check and the window's one-data-operation gate. No search runs here: the
    // listing is a function, the clock and the timers are the test's.
    public class ResultsFormsTests
    {
        private string Folder = string.Empty;
        private CultureInfo OriginalCulture = null!;
        private readonly Dictionary<string, SearchPlanEntry?> Listed = new(StringComparer.Ordinal);
        private readonly HashSet<string> Unsaved = new(StringComparer.Ordinal);
        private int SelectionChanges;

        [SetUp]
        public void SetUp()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronResultsFormsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Listed.Clear();
            Unsaved.Clear();
            SelectionChanges = 0;
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture;
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        // a manual monotonic clock
        private sealed class Clock : TimeProvider
        {
            private long _Ticks = 1000;
            public override long GetTimestamp() => _Ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            internal void Advance(TimeSpan By) => _Ticks += By.Ticks;
        }

        private static SearchPlanEntry Entry(string File_, bool AutoRun, SearchPlanState State = SearchPlanState.Ready, string? Name = null, string? Campground = "232447",
            params ConfigDiagnostic[] Diagnostics) =>
            new SearchPlanEntry(File_, State, Name, AutoRun, Campground, 1, Diagnostics,
                State == SearchPlanState.Ready ? Array.Empty<SearchIssue>()
                    : new[] { new SearchIssue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, CampsiteSearch.Message(SearchIssueCode.FileInvalid), File_) },
                null);

        // a search file on disk (the list takes the names from the folder) and what its listing reads
        private void Put(string Name, SearchPlanEntry? Listing)
        {
            File.WriteAllText(Path.Join(Folder, Name), "displayName: x\n");
            Listed[Name] = Listing;
        }

        private ResultsSearchList List(Func<List<CampgroundPickerEntry>>? Campgrounds = null)
        {
            var Catalog = new CampgroundCatalog(Campgrounds ?? (() => new List<CampgroundPickerEntry>()));
            Catalog.Ensure();
            return new ResultsSearchList(Folder, Catalog, Name => Unsaved.Contains(Name),
                (Config, Name, When) => Listed[Name] ?? throw new InvalidDataException("unreadable"), () => new DateTime(2026, 11, 22, 12, 0, 0), () => SelectionChanges++);
        }

        private static List<string> Rescan(ResultsSearchList Rows, bool AtStart, out string? Notice)
        {
            string? Said = "not called";
            List<string> Unchosen = Rows.Rescan(AtStart, N => Said = N);
            Notice = Said;
            return Unchosen;
        }

        // ---- the selection rules ----

        [Test]
        public void TheDefaultChoice_IsTheEnabledFilesThatCanRun_AndOnlyAFileThatCanRunCanBeChosen()
        {
            Put("Alpha.yaml", Entry("Alpha.yaml", AutoRun: true, Name: " First trip "));
            Put("Bravo.yaml", Entry("Bravo.yaml", AutoRun: false));
            Put("Charlie.yaml", Entry("Charlie.yaml", AutoRun: true, State: SearchPlanState.Invalid));
            Put("Delta.yaml", null);
            ResultsSearchList Rows = List(() => new List<CampgroundPickerEntry> { new CampgroundPickerEntry(new CampgroundChoice("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA"), null) });

            Assert.That(Rescan(Rows, false, out string? Notice), Is.Empty);
            Assert.That(Notice, Is.Null);
            Assert.That(Rows.Rows.Select(R => R.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Bravo.yaml", "Charlie.yaml", "Delta.yaml" }), "the console's order");
            ResultsSearchRow Alpha = Rows.Rows[0], Bravo = Rows.Rows[1], Charlie = Rows.Rows[2], Delta = Rows.Rows[3];

            Assert.That(Rows.Rows.Select(R => R.IsSelected), Is.EqualTo(new[] { true, false, false, false }));
            Assert.That(Alpha.Title, Is.EqualTo("First trip"));
            Assert.That(Alpha.ChoiceText, Is.EqualTo("Enabled (autoRun): selected by default"));
            Assert.That(Alpha.Details, Does.StartWith("Alpha.yaml · UPPER PINES").And.EndWith(" · Enabled (autoRun): selected by default"));
            Assert.That(Alpha.CheckBoxName, Is.EqualTo("First trip (Alpha.yaml), Enabled (autoRun): selected by default"));
            Assert.That(Bravo.Title, Is.EqualTo("Bravo.yaml"), "a file without a name is shown by its file name");
            Assert.That(Bravo.ChoiceText, Is.EqualTo("Not enabled: runs when selected"));
            Assert.That(Charlie.IsRunnable, Is.False);
            Assert.That(Charlie.Reason, Is.EqualTo("Can't be searched: " + CampsiteSearch.Message(SearchIssueCode.FileInvalid)));
            Assert.That(Charlie.ChoiceText, Is.EqualTo("Enabled (autoRun)"), "the setting alone: it can't be searched now");
            Assert.That(Charlie.CheckBoxName, Is.EqualTo("Charlie.yaml (Charlie.yaml), Enabled (autoRun), can't be searched now"));
            Assert.That(Delta.Reason, Is.EqualTo("Can't be searched: " + CampsiteSearch.Message(SearchIssueCode.FileUnreadable)));

            // a file that is not enabled runs when it is chosen; one that can't run can't be chosen
            Bravo.IsSelected = true;
            Charlie.IsSelected = true;
            Assert.That(Bravo.IsSelected, Is.True);
            Assert.That(Charlie.IsSelected, Is.False);
            Assert.That(Charlie.CanSelect, Is.False);
            Assert.That(SelectionChanges, Is.GreaterThanOrEqualTo(2));

            // while a run goes on the host locks the rows: nothing can be chosen or unchosen
            Alpha.SetLocked(true);
            Alpha.IsSelected = false;
            Assert.That(Alpha.IsSelected, Is.True);
            Assert.That(Alpha.CanSelect, Is.False);
        }

        [Test]
        public void ALaterLook_KeepsTheUsersChoice_AndUnchoosesWhatCantRunOrIsNoLongerEnabled()
        {
            Put("Alpha.yaml", Entry("Alpha.yaml", AutoRun: true));
            Put("Bravo.yaml", Entry("Bravo.yaml", AutoRun: true));
            Put("Charlie.yaml", Entry("Charlie.yaml", AutoRun: false));
            ResultsSearchList Rows = List();
            Rescan(Rows, false, out _);
            Rows.Rows[2].IsSelected = true;   // the user chose Charlie, which is not enabled
            Rows.Rows[1].IsSelected = false;  // and unchose Bravo

            // Alpha is no longer enabled (chosen by default, not by the user): unchosen. Bravo stays unchosen, Charlie chosen
            Listed["Alpha.yaml"] = Entry("Alpha.yaml", AutoRun: false);
            Assert.That(Rescan(Rows, false, out _), Is.EqualTo(new[] { "Alpha.yaml" }));
            Assert.That(Rows.Rows.Select(R => R.IsSelected), Is.EqualTo(new[] { false, false, true }));

            // a file with unsaved changes in the Searches form can't be searched until they are saved or reverted
            Unsaved.Add("Charlie.yaml");
            Assert.That(Rescan(Rows, false, out _), Is.EqualTo(new[] { "Charlie.yaml" }));
            Assert.That(Rows.Rows[2].Reason, Is.EqualTo("Save or revert the changes in the Searches tab first."));
            Assert.That(Rows.Rows[2].IsRunnable, Is.False);

            // the three commands: enabled and runnable, everything runnable, nothing
            Unsaved.Clear();
            Listed["Alpha.yaml"] = Entry("Alpha.yaml", AutoRun: true);
            Rescan(Rows, false, out _);
            Rows.SelectAllRunnable();
            Assert.That(Rows.Rows.Select(R => R.IsSelected), Is.EqualTo(new[] { true, true, true }));
            Rows.SelectEnabled();
            Assert.That(Rows.Rows.Select(R => R.IsSelected), Is.EqualTo(new[] { true, true, false }));
            Rows.ClearSelection();
            Assert.That(Rows.Rows.Select(R => R.IsSelected), Is.All.False);
        }

        [Test]
        public void AtStart_NothingIsChosenByItself_AndAChosenFileThatIsGoneIsStillAskedFor()
        {
            Put("Alpha.yaml", Entry("Alpha.yaml", AutoRun: true));
            Put("Bravo.yaml", Entry("Bravo.yaml", AutoRun: true));
            ResultsSearchList Rows = List();
            Rescan(Rows, false, out _);

            // between the listing and Start: a new enabled file appears, and a chosen one is deleted
            Put("Aaa-new.yaml", Entry("Aaa-new.yaml", AutoRun: true));
            File.Delete(Path.Join(Folder, "Bravo.yaml"));
            Assert.That(Rescan(Rows, true, out _), Is.Empty, "nothing the user chose was unchosen");
            Assert.That(Rows.Rows.Select(R => R.FileName), Is.EqualTo(new[] { "Aaa-new.yaml", "Bravo.yaml", "Alpha.yaml" }).Or.EqualTo(new[] { "Aaa-new.yaml", "Alpha.yaml", "Bravo.yaml" }));
            ResultsSearchRow New = Rows.Rows.Single(R => R.FileName == "Aaa-new.yaml"), Gone = Rows.Rows.Single(R => R.FileName == "Bravo.yaml");
            Assert.That(New.IsSelected, Is.False, "the user has not seen it");
            Assert.That(Gone.IsSelected, Is.True, "still asked for, so the run reports it as missing");
            Assert.That(Gone.IsMissing, Is.True);
            Assert.That(Gone.Reason, Is.EqualTo("Not found in the settings folder any more; the search reports it as missing."));

            // a chosen file that can't run any more is unchosen, and Start is told so
            Listed["Alpha.yaml"] = Entry("Alpha.yaml", AutoRun: true, State: SearchPlanState.Invalid);
            Assert.That(Rescan(Rows, true, out _), Is.EqualTo(new[] { "Alpha.yaml" }));
            Assert.That(ResultsWording.ChangedOnDisk(new[] { "Alpha.yaml", "Bravo.yaml" }),
                Is.EqualTo("Search files changed on disk, so these are no longer chosen: Alpha.yaml, Bravo.yaml. Check the choice, then select Start again."));
        }

        [Test]
        public void AFolderThatIsMissing_IsSaid_AndTheListIsEmpty()
        {
            Put("Alpha.yaml", Entry("Alpha.yaml", AutoRun: true));
            ResultsSearchList Rows = List();
            Rescan(Rows, false, out _);
            Directory.Delete(Folder, true);
            Rescan(Rows, false, out string? Notice);
            Assert.That(Notice, Is.EqualTo("The settings folder does not exist."));
            Assert.That(Rows.Rows, Is.Empty);
        }

        // ---- the progress throttle ----

        [Test]
        public void Progress_ReachesTheForm_AtMostOncePerInterval_NewestOnly_WithOneJobQueuedAtATime()
        {
            var Time = new Clock();
            var Posted = new Queue<Action>();
            var Timers = new List<(TimeSpan Due, Action Job)>();
            var Shown = new List<long>();
            var Gate = new ProgressGate(P => Shown.Add(P.Sequence), Posted.Enqueue,
                (Due, Job) => { Timers.Add((Due, Job)); return new Stop(() => Timers.RemoveAll(T => ReferenceEquals(T.Job, Job))); }, Time, TimeSpan.FromMilliseconds(100));
            SearchProgress Snapshot(long Sequence) => new SearchProgress(Sequence, SearchRunStage.CheckingAvailability, 0, 0, 10, (int)Sequence, 0, 0, Array.Empty<SearchProgressItem>());

            // a flood from the worker: one job for the form's thread, and only the newest snapshot is shown
            for (long i = 1; i <= 50; i++)
            {
                Gate.Report(Snapshot(i));
            }
            Assert.That(Posted, Has.Count.EqualTo(1));
            Posted.Dequeue()();
            Assert.That(Shown, Is.EqualTo(new long[] { 50 }));

            // 30 ms later: the next snapshot waits for the rest of the interval on a one-shot timer
            Time.Advance(TimeSpan.FromMilliseconds(30));
            Gate.Report(Snapshot(51));
            Gate.Report(Snapshot(52));
            Posted.Dequeue()();
            Assert.That(Shown, Has.Count.EqualTo(1), "not yet");
            Assert.That(Timers, Has.Count.EqualTo(1));
            Assert.That(Timers[0].Due, Is.EqualTo(TimeSpan.FromMilliseconds(70)));
            Gate.Report(Snapshot(53));
            Assert.That(Posted, Is.Empty, "nothing more is queued while one waits");
            Time.Advance(TimeSpan.FromMilliseconds(70));
            Action Due = Timers[0].Job;
            Timers.Clear();
            Due();
            Assert.That(Shown, Is.EqualTo(new long[] { 50, 53 }));

            // an older snapshot never replaces a newer one; after Close nothing is shown
            Time.Advance(TimeSpan.FromSeconds(1));
            Gate.Report(Snapshot(60));
            Gate.Report(Snapshot(55));
            Posted.Dequeue()();
            Assert.That(Shown, Is.EqualTo(new long[] { 50, 53, 60 }));
            Time.Advance(TimeSpan.FromSeconds(1));
            Gate.Report(Snapshot(61));
            Gate.Close();
            Posted.Dequeue()();
            Gate.Report(Snapshot(62));
            Assert.That(Posted, Is.Empty);
            Assert.That(Shown, Has.Count.EqualTo(3));
            Assert.That(Gate.Published, Is.EqualTo(3));
        }

        private sealed class Stop : IDisposable
        {
            private readonly Action _Dispose;
            internal Stop(Action Dispose) => _Dispose = Dispose;
            public void Dispose() => _Dispose();
        }

        // ---- the five minutes after a block ----

        [Test]
        public void ABlock_HoldsStartForFiveMinutes_ByTheHostsClock_AndIsThenForgotten()
        {
            var Time = new Clock();
            var Hold = new BlockHold(Time);
            Assert.That(BlockHold.Duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(Hold.IsActive, Is.False);
            Assert.That(Hold.IsSet, Is.False);
            Assert.That(Hold.Text(), Is.Null);

            Hold.Begin();
            Assert.That(Hold.IsActive, Is.True);
            Assert.That(Hold.Text(), Is.EqualTo("recreation.gov limited Campertron's requests. Start is available again in 5:00."));
            Time.Advance(TimeSpan.FromSeconds(1.2));
            Assert.That(Hold.Text(), Is.EqualTo("recreation.gov limited Campertron's requests. Start is available again in 4:59."));
            Time.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(58.3));
            Assert.That(Hold.IsActive, Is.True);
            Assert.That(Hold.Text(), Does.EndWith("again in 0:01."), "never 0:00 while it still holds");

            Time.Advance(TimeSpan.FromSeconds(0.5));
            Assert.That(Hold.IsActive, Is.False);
            Assert.That(Hold.IsSet, Is.True, "until the host asks for the line again");
            Assert.That(Hold.Text(), Is.Null);
            Assert.That(Hold.IsSet, Is.False);

            // two holds of two forms share nothing
            var Other = new BlockHold(Time);
            Hold.Begin();
            Assert.That(Other.IsActive, Is.False);
        }

        // ---- the words of a run ----

        [Test]
        public void TheRunsWords_AreFixed_AroundItsNumbers()
        {
            Assert.That(ResultsWording.Idle, Is.EqualTo("Choose the searches to run, then select Start."));
            Assert.That(ResultsWording.NotStartedNoChoice, Is.EqualTo("Not started: no search is chosen."));
            Assert.That(ResultsWording.NotStartedNoneReady, Is.EqualTo("Not started: none of the chosen searches can be searched now."));
            Assert.That(ResultsWording.NotStartedRefreshing, Is.EqualTo("Not started: the campground data is being refreshed."));
            Assert.That(ResultsWording.RefreshRunning, Is.EqualTo("Start is unavailable while the campground data is refreshed (Settings > System). It is available again when the refresh has finished."));
            Assert.That(ResultsWording.Searching(1), Is.EqualTo("Searching 1 search…"));
            Assert.That(ResultsWording.Searching(3), Is.EqualTo("Searching 3 searches…"));
            Assert.That(ResultsWording.CancelLabel(false), Is.EqualTo("Cancel"));
            Assert.That(ResultsWording.CancelLabel(true), Is.EqualTo("Canceling…"));
            Assert.That(ResultsWording.Stage(SearchRunStage.Validating, 0), Is.EqualTo("Reading the searches"));
            Assert.That(ResultsWording.Stage(SearchRunStage.Retrying, 1), Is.EqualTo("Checking availability; 1 request is waiting to be tried again (each month is asked for at most three times)"));
            Assert.That(ResultsWording.Stage(SearchRunStage.Retrying, 2), Does.StartWith("Checking availability; 2 requests are waiting"));
            Assert.That(Enum.GetValues<SearchState>().Select(ResultsWording.State), Is.All.Not.Empty);
            Assert.That(ResultsWording.State(SearchState.Skipped), Is.EqualTo("Not searched"));

            var Snapshot = new SearchProgress(7, SearchRunStage.CheckingAvailability, 2, 2, 12, 5, 1, 2, Array.Empty<SearchProgressItem>());
            Assert.That(ResultsWording.Campgrounds(Snapshot), Is.EqualTo("Campsite details: 2 of 2 campgrounds ready"));
            Assert.That(ResultsWording.Requests(Snapshot), Is.EqualTo("Availability: 5 of 12 month requests finished, 1 not checked, 2 waiting to be tried again"));
            Assert.That(ResultsWording.Bar(Snapshot), Is.EqualTo((false, 5d, 12d)));
            Assert.That(ResultsWording.Bar(Snapshot with { Stage = SearchRunStage.PreparingCampsiteDetails, CampgroundsPrepared = 1 }), Is.EqualTo((false, 1d, 2d)));
            Assert.That(ResultsWording.Bar(Snapshot with { Stage = SearchRunStage.BuildingResults }), Is.EqualTo((true, 0d, 1d)), "no total: indeterminate");
            Assert.That(ResultsWording.Requests(Snapshot with { RequestsPlanned = 0 }), Is.Null);
            Assert.That(ResultsWording.ProgressDetails("Stage", null, "Requests", "0:07"), Is.EqualTo("Stage · Requests · Elapsed 0:07"));

            Assert.That(ResultsWording.Summary(SearchRunStatus.Completed, 12, 2), Is.EqualTo("Finished: 12 openings found in 2 searches."));
            Assert.That(ResultsWording.Summary(SearchRunStatus.CompletedWithProblems, 1, 1), Is.EqualTo("Finished with problems: some searches or months were not checked. 1 opening found in 1 search."));
            Assert.That(ResultsWording.Summary(SearchRunStatus.Blocked, 0, 1), Does.StartWith("Stopped: recreation.gov limited the requests, so no further request was made."));
            Assert.That(ResultsWording.Summary(SearchRunStatus.Canceled, 0, 1), Does.StartWith("Canceled. Searches that had finished keep their results; months not checked are listed, never as without availability."));
            Assert.That(ResultsWording.Summary(SearchRunStatus.Failed, 0, 0), Is.EqualTo("Nothing could be searched."));
            Assert.That(ResultsWording.Finished("Finished: 0 openings found in 1 search.", "1:02"), Is.EqualTo("Finished: 0 openings found in 1 search. Elapsed 1:02."));
            Assert.That(ResultText.Elapsed(TimeSpan.FromSeconds(3725.9)), Is.EqualTo("1:02:05"));
            Assert.That(ResultText.Elapsed(TimeSpan.FromSeconds(-3)), Is.EqualTo("0:00"));
        }

        // ---- a search's result and its rows ----

        private static SearchOpening Opening(string Id, DateTime Night, string? Name = null) =>
            new SearchOpening(Id, Name, "Loop A", "STANDARD NONELECTRIC", new[] { "Tent", " ", "RV" }, Night, "2:00 PM", "12:00 PM", 1, 6, CampsiteSearch.BookingUrl(Id));

        private static SearchResult Result(SearchState State, IReadOnlyList<SearchOpening> Openings, IReadOnlyList<SearchSite>? Sites = null, int Consecutive = 1,
            IReadOnlyList<SearchMonthResult>? Months = null) =>
            new SearchResult("Alpha.yaml", " First trip ", true, "232447", "UPPER PINES", "Yosemite National Park", Consecutive, State, 2, 2,
                Months ?? new[] { new SearchMonthResult(new DateTime(2027, 7, 1), true, null) }, Openings, Sites ?? Array.Empty<SearchSite>(),
                Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>());

        [Test]
        public void SingleNights_AreRowsByDate_EachDatesCampsitesUnderIt()
        {
            DateTime First = new DateTime(2027, 7, 2), Second = new DateTime(2027, 7, 3);
            var Sites = new[] { new SearchSite("101", "086", null, null, Array.Empty<string>(), null, null, null, null, CampsiteSearch.BookingUrl("101"), new[] { First, Second }, Array.Empty<SearchStay>()),
                new SearchSite("102", null, null, null, Array.Empty<string>(), null, null, null, null, CampsiteSearch.BookingUrl("102"), new[] { First }, Array.Empty<SearchStay>()) };
            var Shown = new ResultSearchViewModel(Result(SearchState.Complete, new[] { Opening("101", First, "086"), Opening("102", First, "Site B2"), Opening("101", Second, "086") }, Sites), RunBlocked: false);

            Assert.That(Shown.Title, Is.EqualTo("First trip"));
            Assert.That(Shown.Kind, Is.EqualTo(ResultKind.Openings));
            Assert.That(Shown.StatusText, Is.EqualTo("Complete: 3 openings found"));
            Assert.That(Shown.CountsText, Is.EqualTo("3 openings at 2 campsites on 2 nights"));
            Assert.That(Shown.CampgroundText, Is.EqualTo("UPPER PINES · Yosemite National Park · ID 232447"));
            Assert.That(Shown.MonthsText, Is.EqualTo("Months checked: July 2027."));
            Assert.That(Shown.ChoiceLabel, Is.EqualTo("First trip (Alpha.yaml) · Complete: 3 openings found"));
            Assert.That(Shown.HasEmptyText, Is.False);

            Assert.That(Shown.Rows.Select(R => R.GetType()), Is.EqualTo(new[] { typeof(ResultDateRow), typeof(ResultOpeningRow), typeof(ResultOpeningRow), typeof(ResultDateRow), typeof(ResultOpeningRow) }));
            Assert.That(((ResultDateRow)Shown.Rows[0]).Text, Is.EqualTo("Friday, July 2, 2027 · 2 campsites"));
            Assert.That(((ResultDateRow)Shown.Rows[3]).Text, Is.EqualTo("Saturday, July 3, 2027 · 1 campsite"));
            var Row = (ResultOpeningRow)Shown.Rows[1];
            Assert.That(Row.Title, Is.EqualTo("Site 086"), "a bare number is named as the results page names it");
            Assert.That(((ResultOpeningRow)Shown.Rows[2]).Title, Is.EqualTo("Site B2"), "a name that starts with Site stays as it is");
            Assert.That(Row.IdText, Is.EqualTo("ID 101"));
            Assert.That(Row.Details, Is.EqualTo("Loop A · STANDARD NONELECTRIC · Tent, RV · check-in 2:00 PM · checkout 12:00 PM · 1 to 6 people"));
            Assert.That(Row.AccessibleName, Is.EqualTo("Friday, July 2, 2027: Site 086, ID 101, " + Row.Details));
            Assert.That(Row.HasCampsite, Is.True);
            Assert.That(Shown.Rows[0].HasCampsite, Is.False, "a date's heading is no campsite");
            Assert.That(ResultsWording.SelectedCampsite(Row), Is.EqualTo("Site 086 (ID 101)"));
            Assert.That(ResultsWording.SelectedCampsite(Shown.Rows[0]), Is.EqualTo("Choose a campsite in the list."));
            Assert.That(ReferenceEquals(Shown.Rows, Shown.Rows), Is.True, "built once");
        }

        [Test]
        public void ConsecutiveNights_AreRowsByCampsite_WithEachStaysDates()
        {
            DateTime First = new DateTime(2027, 7, 2);
            var Site = new SearchSite("101", "086", "Loop A", null, new[] { "Tent" }, null, null, null, 4, CampsiteSearch.BookingUrl("101"),
                new[] { First, First.AddDays(1), First.AddDays(5), First.AddDays(6), First.AddDays(7) }, new[] { new SearchStay(First.AddDays(5), 3), new SearchStay(First, 2) });
            var Shown = new ResultSearchViewModel(Result(SearchState.Complete, Enumerable.Range(0, 5).Select(i => Opening("101", First.AddDays(i))).ToList(), new[] { Site }, Consecutive: 2), false);

            Assert.That(Shown.CountsText, Is.EqualTo("1 campsite with at least 2 nights in a row (5 nights in all)"));
            Assert.That(Shown.Rows.Select(R => R.GetType()), Is.EqualTo(new[] { typeof(ResultSiteRow), typeof(ResultStayRow), typeof(ResultStayRow) }));
            var SiteRow = (ResultSiteRow)Shown.Rows[0];
            Assert.That(SiteRow.StaysText, Is.EqualTo("2 stays · 5 nights"));
            Assert.That(SiteRow.Details, Is.EqualTo("Loop A · Tent · up to 4 people"));
            Assert.That(((ResultStayRow)Shown.Rows[1]).Text, Is.EqualTo("Arrive Friday, July 2, 2027, leave Sunday, July 4, 2027 · 2 nights"), "the stays in date order");
            Assert.That(((ResultStayRow)Shown.Rows[2]).Text, Is.EqualTo("Arrive Wednesday, July 7, 2027, leave Saturday, July 10, 2027 · 3 nights"));
            Assert.That(Shown.Rows[2].CampsiteId, Is.EqualTo("101"), "a stay's actions are its campsite's");
            Assert.That(Shown.Rows[2].AccessibleName, Is.EqualTo("Site 086, ID 101: " + ((ResultStayRow)Shown.Rows[2]).Text));
        }

        [Test]
        public void HowASearchEnded_IsSaidInWords_AndAMonthNotCheckedIsNeverWithoutAvailability()
        {
            var None = new ResultSearchViewModel(Result(SearchState.Complete, Array.Empty<SearchOpening>()), false);
            Assert.That(None.Kind, Is.EqualTo(ResultKind.NoAvailability));
            Assert.That(None.StatusText, Is.EqualTo("Complete: no matching availability in the months checked"));
            Assert.That(None.EmptyText, Is.EqualTo("No campsite matched this search in the months checked."));
            Assert.That(None.IsComplete, Is.True);

            var Months = new[] { new SearchMonthResult(new DateTime(2027, 7, 1), true, null), new SearchMonthResult(new DateTime(2027, 8, 1), false, SearchIssueCode.RateLimited) };
            var Incomplete = new ResultSearchViewModel(Result(SearchState.Incomplete, new[] { Opening("101", new DateTime(2027, 7, 2)) }, Months: Months), RunBlocked: false);
            Assert.That(Incomplete.Kind, Is.EqualTo(ResultKind.Incomplete));
            Assert.That(Incomplete.StatusText, Is.EqualTo("Incomplete: some months were not checked; 1 opening found in the others"));
            Assert.That(Incomplete.MonthsText, Is.EqualTo("Months checked: July 2027. Not checked: August 2027 (recreation.gov asked for fewer requests)."));

            // the same search in a run that recreation.gov blocked
            var Blocked = new ResultSearchViewModel(Result(SearchState.Incomplete, Array.Empty<SearchOpening>(), Months: Months), RunBlocked: true);
            Assert.That(Blocked.Kind, Is.EqualTo(ResultKind.Blocked));
            Assert.That(Blocked.StatusText, Is.EqualTo("Stopped: recreation.gov limited the requests before every month was checked"));
            Assert.That(Blocked.EmptyText, Is.EqualTo("Nothing was found in the months checked. The months that were not checked are listed above; they are not without availability."));

            var Canceled = new ResultSearchViewModel(Result(SearchState.Canceled, Array.Empty<SearchOpening>(), Months: Array.Empty<SearchMonthResult>()), false);
            Assert.That(Canceled.StatusText, Is.EqualTo("Canceled before it finished"));
            Assert.That(Canceled.MonthsText, Is.EqualTo("No month was searched."));
            Assert.That(Canceled.EmptyText, Is.EqualTo("The search was canceled before it found anything. Months not checked are not shown as without availability."));

            Assert.That(new ResultSearchViewModel(Result(SearchState.Skipped, Array.Empty<SearchOpening>()), false).StatusText, Is.EqualTo("Not searched: no campground is set (campgroundID)"));
            Assert.That(new ResultSearchViewModel(Result(SearchState.Invalid, Array.Empty<SearchOpening>()), false).StatusText, Is.EqualTo("Not searched: the file can't be searched"));
            Assert.That(new ResultSearchViewModel(Result(SearchState.Failed, Array.Empty<SearchOpening>()), false).StatusText, Is.EqualTo("Failed: no month could be checked"));

            // a problem: its severity written out, fixed text, the month, the status and the requests; never a body
            var Issue = new SearchIssue(SearchIssueCode.HttpStatus, SearchIssueSeverity.Warning, "recreation.gov answered with an error.", "Alpha.yaml", new DateTime(2027, 8, 1), 503, 3);
            Assert.That(ResultText.Issue(Issue), Is.EqualTo("Warning: recreation.gov answered with an error. (August 2027, HTTP 503, 3 requests)"));
            Assert.That(Enum.GetValues<SearchIssueCode>().Select(C => ResultText.Reason(C)), Is.All.Not.Empty);
        }

        [Test]
        public void ABookingLink_IsOnlyTheLinkTheSearchBuiltForThatCampsite()
        {
            Assert.That(BookingLink.TryCreate(CampsiteSearch.BookingUrl("101"), "101", out Uri? Link), Is.True);
            Assert.That(Link!.AbsoluteUri, Is.EqualTo("https://www.recreation.gov/camping/campsites/101"));
            foreach ((string? Url, string? Id) in new (string?, string?)[]
            {
                (null, "101"), (CampsiteSearch.BookingUrl("101"), null), (CampsiteSearch.BookingUrl("102"), "101"), ("http://www.recreation.gov/camping/campsites/101", "101"),
                ("https://www.recreation.gov.example.invalid/camping/campsites/101", "101"), ("https://www.recreation.gov/camping/campsites/101?next=x", "101"),
                ("https://user@www.recreation.gov/camping/campsites/101", "101"), ("https://www.recreation.gov:8443/camping/campsites/101", "101"), ("file:///etc/passwd", "101"),
            })
            {
                Assert.That(BookingLink.TryCreate(Url, Id, out Uri? Refused), Is.False, Url);
                Assert.That(Refused, Is.Null);
            }
        }

        // ---- one data operation at a time ----

        [Test]
        public void ASearchAndARefresh_NeverOverlap_EachGateItsOwn()
        {
            var Gate = new DataOperationGate();
            int Changes = 0;
            Gate.Changed += () => Changes++;
            Assert.That(Gate.TryEnter(DataOperation.None), Is.False);
            Assert.That(Gate.TryEnter(DataOperation.Search), Is.True);
            Assert.That(Gate.TryEnter(DataOperation.Refresh), Is.False, "the refresh clears the cache folder a run writes");
            Assert.That(Gate.TryEnter(DataOperation.Search), Is.False);
            Gate.Leave(DataOperation.Refresh);
            Assert.That(Gate.Current, Is.EqualTo(DataOperation.Search), "leaving a gate another operation holds changes nothing");
            Gate.Leave(DataOperation.Search);
            Assert.That(Gate.TryEnter(DataOperation.Refresh), Is.True);
            Assert.That(Changes, Is.EqualTo(3));
            Assert.That(new DataOperationGate().TryEnter(DataOperation.Search), Is.True, "another window's or session's gate is its own");
        }
    }
}
