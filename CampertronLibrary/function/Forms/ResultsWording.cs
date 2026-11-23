using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronLibrary.function.Forms
{
    // The words of a run in the Results tab (GS2), for every UI: why a run did not start, its stages and counts while it
    // goes on, where its bar stands, how it ended, the five minutes after a block and the campsite actions. Fixed words
    // around numbers, file names and a campsite's name and ID: never a response body, an exception's message, a folder or a
    // value of a settings file. The words of one search's result and of its rows are ResultSearchViewModel's and ResultText's.
    internal static class ResultsWording
    {
        // ---- before a run ----
        internal const string Idle = "Choose the searches to run, then select Start.";
        internal const string NoCacheFolder = "Campertron couldn't find its cache folder, so it can't search. Restart Campertron.";
        internal const string RefreshRunning = "Start is unavailable while the campground data is refreshed (Settings > System). It is available again when the refresh has finished.";
        internal const string NotStartedChangedOnDisk = "Not started: the search files changed on disk.";
        internal const string NotStartedNoChoice = "Not started: no search is chosen.";
        internal const string NotStartedUnreadable = "Not started: Campertron couldn't read the chosen search files.";
        internal const string NotStartedNoneReady = "Not started: none of the chosen searches can be searched now.";
        internal const string NotStartedRefreshing = "Not started: the campground data is being refreshed.";

        // the list's notice when chosen files changed on disk between their listing and Start
        internal static string ChangedOnDisk(IEnumerable<string> Unchosen) =>
            "Search files changed on disk, so these are no longer chosen: " + string.Join(", ", Unchosen) + ". Check the choice, then select Start again.";

        // a chosen file the plan can't search, at its row
        internal static string CantBeSearched(SearchPlanEntry Entry) =>
            "Can't be searched: " + (Entry.Issues.Count > 0 ? Entry.Issues[0].Message : CampsiteSearch.Message(SearchIssueCode.FileInvalid));

        // ---- while a run goes on ----
        internal const string Waiting = "Waiting";
        internal const string NotSearched = "Not searched";
        internal const string ReadingTheSearches = "Reading the searches";
        internal const string Canceling = "Canceling… Searches that already finished keep their results.";

        internal static string Searching(int Searches) => "Searching " + ResultSearchViewModel.Count(Searches, "search", "searches") + "…";

        internal static string CancelLabel(bool IsCanceling) => IsCanceling ? "Canceling…" : "Cancel";

        internal static string Stage(SearchRunStage Stage, int Retrying) => Stage switch
        {
            SearchRunStage.Validating => ReadingTheSearches,
            SearchRunStage.PreparingCampsiteDetails => "Preparing campsite details",
            SearchRunStage.CheckingAvailability => "Checking availability at recreation.gov",
            SearchRunStage.Retrying => "Checking availability; " + ResultSearchViewModel.Count(Retrying, "request is", "requests are")
                + " waiting to be tried again (each month is asked for at most three times)",
            SearchRunStage.BuildingResults => "Building the results",
            _ => "Finishing",
        };

        // one search's state at its row
        internal static string State(SearchState State) => State switch
        {
            SearchState.Waiting => Waiting,
            SearchState.Searching => "Searching",
            SearchState.Complete => "Complete",
            SearchState.Incomplete => "Incomplete",
            SearchState.Failed => "Failed",
            SearchState.Canceled => "Canceled",
            SearchState.Skipped => NotSearched,
            _ => "Can't be searched",
        };

        // the campgrounds whose campsite details are ready, while their number is known
        internal static string? Campgrounds(SearchProgress Snapshot) => Snapshot.CampgroundsPlanned > 0
            ? "Campsite details: " + Snapshot.CampgroundsPrepared + " of " + ResultSearchViewModel.Count(Snapshot.CampgroundsPlanned, "campground", "campgrounds") + " ready"
            : null;

        // the month requests finished, not checked and waiting, while their number is known
        internal static string? Requests(SearchProgress Snapshot) => Snapshot.RequestsPlanned > 0
            ? "Availability: " + Snapshot.RequestsCompleted + " of " + ResultSearchViewModel.Count(Snapshot.RequestsPlanned, "month request", "month requests") + " finished"
              + (Snapshot.RequestsFailed > 0 ? ", " + Snapshot.RequestsFailed + " not checked" : string.Empty)
              + (Snapshot.RequestsRetrying > 0 ? ", " + Snapshot.RequestsRetrying + " waiting to be tried again" : string.Empty)
            : null;

        // a bar only where the total is known: the campgrounds while their details are prepared, the month requests while
        // availability is checked; otherwise indeterminate
        internal static (bool Indeterminate, double Value, double Maximum) Bar(SearchProgress Snapshot)
        {
            if ((Snapshot.Stage == SearchRunStage.CheckingAvailability || Snapshot.Stage == SearchRunStage.Retrying) && Snapshot.RequestsPlanned > 0)
            {
                return (false, Snapshot.RequestsCompleted, Snapshot.RequestsPlanned);
            }
            if (Snapshot.Stage == SearchRunStage.PreparingCampsiteDetails && Snapshot.CampgroundsPlanned > 0)
            {
                return (false, Snapshot.CampgroundsPrepared, Snapshot.CampgroundsPlanned);
            }
            return (true, 0, 1);
        }

        // the stage, the counts whose totals are known and the elapsed time, in one line
        internal static string ProgressDetails(string? Stage, string? Campgrounds, string? Requests, string? Elapsed) =>
            string.Join(" · ", new[] { Stage, Campgrounds, Requests, Elapsed == null ? null : "Elapsed " + Elapsed }.Where(T => !string.IsNullOrEmpty(T)));

        // ---- how a run ended ----
        internal static string Summary(SearchRunStatus Status, int Openings, int Searches)
        {
            string Found = ResultSearchViewModel.Count(Openings, "opening", "openings") + " found in " + ResultSearchViewModel.Count(Searches, "search", "searches");
            return Status switch
            {
                SearchRunStatus.Completed => "Finished: " + Found + ".",
                SearchRunStatus.CompletedWithProblems => "Finished with problems: some searches or months were not checked. " + Found + ".",
                SearchRunStatus.Blocked => "Stopped: recreation.gov limited the requests, so no further request was made. What arrived before that is shown; the months not checked are listed. " + Found + ".",
                SearchRunStatus.Canceled => "Canceled. Searches that had finished keep their results; months not checked are listed, never as without availability. " + Found + ".",
                _ => "Nothing could be searched.",
            };
        }

        // the status line after a run: its summary and how long it took
        internal static string Finished(string Summary, string? Elapsed) => Summary + " Elapsed " + Elapsed + ".";

        // ---- the five minutes after a block ----
        internal static string Cooldown(TimeSpan Left) =>
            "recreation.gov limited Campertron's requests. Start is available again in " + ResultText.Elapsed(Left + TimeSpan.FromSeconds(0.999)) + ".";

        // ---- the selected campsite and its actions ----
        internal const string ChooseACampsite = "Choose a campsite in the list.";
        internal const string LinkNotOpened = "Campertron did not open this link: it is not a recreation.gov campsite page.";
        internal const string BookingNotOpened = "Campertron couldn't open the booking page. Use Copy booking link and paste it into a browser.";
        internal const string IdNotCopied = "Campertron couldn't copy the campsite ID.";
        internal const string LinkNotCopiedForeign = "Campertron did not copy this link: it is not a recreation.gov campsite page.";
        internal const string LinkNotCopied = "Campertron couldn't copy the booking link.";

        internal static string SelectedCampsite(ResultRow? Row) => Row?.CampsiteTitle is string Title ? Title + " (ID " + Row.CampsiteId + ")" : ChooseACampsite;

        internal static string BookingOpened(ResultRow Row) => "Opened the booking page of " + Row.CampsiteTitle + " in your browser.";

        internal static string IdCopied(ResultRow Row) => "Copied the campsite ID " + Row.CampsiteId + ".";

        internal static string LinkCopied(ResultRow Row) => "Copied the booking link of " + Row.CampsiteTitle + ".";
    }
}
