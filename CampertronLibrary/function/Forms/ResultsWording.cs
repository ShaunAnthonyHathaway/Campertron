using System.Globalization;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronLibrary.function.Forms
{
    // The words of a run in the Results tab (GS2), for every UI: why a run did not start, its stages and counts while it
    // goes on, where its bar stands, how it ended, the five minutes after a block and the campsite actions. Fixed words
    // around numbers, file names and a campsite's name and ID: never a response body, an exception's message, a folder or a
    // value of a settings file. The words of one search's result and of its rows are ResultSearchViewModel's and ResultText's.
    // Since v0.3.0's PR 7 (D97) the tab's own words (its headings, buttons and their help) are written here once for both
    // UIs, as SearchesWording's are (ER-37): the desktop GUI's view binds them (an underscore marks a label's access key
    // there; SearchesWording.Plain drops it), the web UI's Results page shows them, with the few that only a page needs
    // (a page of rows, a link that is not shown, a run that ended a while ago, another browser session's run).
    public static class ResultsWording
    {
        // ---- the tab's own words (its view) ----
        public const string RunHeading = "Searches to run";
        public const string StartLabel = "Start";
        public const string StartName = "Start search";
        public const string StartHelp = "Search the chosen search files now, at recreation.gov";
        public const string CancelName = "Cancel search";
        public const string CancelHelp = "Stop the running search; searches that already finished keep their results";
        public const string SearchFilesName = "Search files to run";
        public const string NoSearchFiles = "There are no search files in the settings folder. Create one in the Searches tab.";
        public const string SelectEnabledLabel = "Select enabled";
        public const string SelectEnabledName = "Select the enabled searches";
        public const string SelectEnabledHelp = "Selects the enabled searches (autoRun: true) that can be searched now and clears the others: the default choice. Nothing runs until Start.";
        public const string SelectEnabledTip = "Select the enabled searches (autoRun: true) that can be searched now and clear the others: the default choice";
        public const string SelectAllLabel = "Select all that can run";
        public const string SelectAllHelp = "Selects every search that can be searched now, enabled or not. Nothing runs until Start.";
        public const string SelectAllTip = "Select every search that can be searched now, enabled or not";
        public const string ClearSelectionLabel = "Clear selection";
        public const string ProgressName = "Search progress";
        public const string ResultsOfLabel = "_Results of";
        public const string ResultsOfName = "Results of the search";
        public const string SummaryName = "Summary of the search";
        public const string OpeningsName = "Openings found";
        public const string OpenBookingLabel = "Open booking page";
        public const string OpenBookingName = "Open the booking page of the chosen campsite";
        public const string OpenBookingTip = "Opens the campsite's page at recreation.gov in your browser";
        public const string CopyIdLabel = "Copy campsite ID";
        public const string CopyIdName = "Copy the chosen campsite's ID";
        public const string CopyLinkLabel = "Copy booking link";
        public const string CopyLinkName = "Copy the chosen campsite's booking link";

        // ---- what only a page that lists the rows itself needs (the web UI, D97) ----
        public const string LastRunHeading = "Results of the last run";
        public const string PreviousRows = "Previous rows";
        public const string NextRows = "Next rows";
        // a result's link that BookingLink refuses is not shown: the row says so instead
        public const string NoBookingLink = "No booking link: this link is not a recreation.gov campsite page.";
        // another browser session's run (the web UI runs one search at a time)
        public const string OtherSessionRunning = "Start is unavailable while a search started in another browser session runs. It is available again when that search has ended.";
        internal const string NotStartedOtherSession = "Not started: a search started in another browser session is running.";

        // a campsite's booking page, named by the campsite (a link's text says where it goes)
        public static string BookingLinkText(string CampsiteTitle) => "Booking page of " + CampsiteTitle;

        // the pages of one search's rows, named by the search
        public static string RowPagesName(string SearchTitle) => "Rows of " + SearchTitle;

        // which rows a page shows when they don't all fit on one (ResultsListing): never cut without saying so
        public static string RowsShown(int First, int Last, int Total, bool Stays) =>
            "Showing " + (Stays ? "stays " : "openings ") + First.ToString("N0", CultureInfo.CurrentCulture) + " to " + Last.ToString("N0", CultureInfo.CurrentCulture)
            + " of " + Total.ToString("N0", CultureInfo.CurrentCulture) + ".";

        // a run's results shown a while after it ended: when it ended, and that recreation.gov may have changed since
        public static string EndedAgo(TimeSpan Ago)
        {
            int Minutes = (int)Math.Floor(Math.Max(0, Ago.TotalMinutes));
            string When = Minutes < 1 ? "less than a minute ago"
                : Minutes < 60 ? ResultSearchViewModel.Count(Minutes, "minute", "minutes") + " ago"
                : ResultSearchViewModel.Count(Minutes / 60, "hour", "hours") + " ago";
            return "This run ended " + When + ". Availability may have changed since then: the booking page shows what is available now.";
        }

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
