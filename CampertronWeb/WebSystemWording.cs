using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronWeb
{
    // The system page's own words (v0.3.0's PR 9, D99; business-rules-settings §36): what only a page of the web UI says
    // around the shared words of Settings > System (SystemWording, CurrentConfiguration, RefreshWording). What follows a
    // successful refresh is each UI's own (RefreshWording): the desktop GUI restarts itself (D62), the web UI can't (the
    // automatic restart is desktop-only, D84), so these words say to restart web mode, which is D61's rule before D62
    // (no live reload: a new process loads the new data). Fixed words: never a path, an exception's message, a server's
    // text or an address.
    internal static class WebSystemWording
    {
        internal const string Intro = "Campertron's current configuration and its campground data, with the rules and words of CampertronGUI's Settings > System. "
            + "Where the words below name the Results tab, in this web UI they mean its Results page. The theme is CampertronGUI's own: this web UI follows the browser's light or dark setting.";
        internal const string PathsNote = "The web UI never shows a folder's path: CampertronGUI's Settings > System and the console's View Config show them.";
        internal const string SelectToCopy = "Select the values to copy them.";
        internal const string NotUpdating = "This page does not update by itself: select Show the latest progress to see how the refresh is going.";
        internal const string ShowLatest = "Show the latest progress";
        internal const string ConfirmationBackLink = "Back, don't refresh";
        // the confirmation, after the shared words: what the refresh does to the rest of the web UI and what follows it here
        internal const string ConfirmationAfter = "While it runs, no search can be started on the results page, and the monitoring, if it runs, pauses: it starts again by itself when the refresh has finished. "
            + "The refresh belongs to this web UI, not to this browser: it goes on if you sign out or close the page. "
            + "This web UI can't restart itself: after a successful refresh, restart web mode (in a container, restart the container) so that every page uses the new data.";
        // after an installed refresh: the web UI reloads nothing (D61) and can't restart (D84)
        internal const string RestartNote = "Restart web mode (in a container, restart the container) so that every page uses the new data: until then the searches and results pages "
            + "may still list the campgrounds they read before. This web UI can't restart itself.";
        internal const string MonitoringResumed = "The monitoring was paused for the refresh and has been started again; the monitoring page shows how it goes.";
        internal const string Pausing = "Pausing the monitoring before the refresh starts: a check that is sending finishes its notifications first.";
        internal const string UnavailableShuttingDown = "Unavailable: web mode is stopping.";
        internal const string LastResultHeading = "The last refresh";

        // what a page's action did, when it did not do what was asked
        internal static string? Outcome(WebRefreshOutcome Outcome) => Outcome switch
        {
            WebRefreshOutcome.Stale => "This page was out of date (a refresh started or ended, or the page was sent again), so nothing was started or canceled. The page shows the refresh as it is now.",
            WebRefreshOutcome.AlreadyRunning => "Nothing was started: a refresh of the campground data is already running.",
            WebRefreshOutcome.NotRunning => "Nothing was canceled: no refresh is running.",
            WebRefreshOutcome.SearchRunning => "Nothing was started: " + RefreshWording.SearchRunningReason,
            WebRefreshOutcome.NoFolders => "Nothing was started: " + RefreshWording.NoFoldersReason,
            WebRefreshOutcome.TooLate => "Nothing was canceled: the refresh had already reached the installation, so it finishes. " + RefreshWording.FinishingText,
            WebRefreshOutcome.ShuttingDown => "Nothing was done: web mode is stopping.",
            _ => null
        };

        // the status line while a refresh runs: the shared words, or the pause before it
        internal static string Running(WebRefreshOperation Op)
        {
            if (Op.Pausing && Op.Progress == null)
            {
                return Op.CancelRequested ? RefreshWording.CancelingText : Pausing;
            }
            string Stage = RefreshWording.StageName(Op.Progress?.Stage ?? RidbRefreshStage.AcquiringLock);
            if (!Op.Cancellable)
            {
                return RefreshWording.Finishing(Stage, Op.CancelRequested);
            }
            return Op.CancelRequested ? RefreshWording.CancelingText : RefreshWording.Refreshing(Stage);
        }

        // the console's line at a refresh's end: its result in the shared words (fixed, an unexpected problem by its type)
        internal static string ConsoleResult(RidbRefreshResult Result) => RefreshWording.Describe(Result).Text;
    }
}

namespace CampertronWeb
{
    // The campgrounds pages' own words (v0.3.0's PR 9, D99): what only the web UI's page says around the lookup's shared
    // words (CampgroundsWording)
    internal static class WebCampgroundsWording
    {
        // a page only looks: it never prepares the campground data (the searches and results pages and the monitoring do)
        internal const string NoDataYet = "There is no campground data in the settings folder yet. It is prepared the first time the searches or results page is opened, or when the monitoring starts.";

        internal static string PartsName(string? Context) => "Parts of the campground list" + (string.IsNullOrEmpty(Context) ? string.Empty : " of " + Context);
    }
}
