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
        internal const string Intro = "This browser's theme, Campertron's current configuration and its campground data, with the rules and words of CampertronGUI's Settings > System. "
            + "Where the words below name the Results tab, in this web UI they mean its Results page.";
        internal const string PathsNote = "The web UI never shows a folder's path: CampertronGUI's Settings > System and the console's View Config show them.";
        internal const string SelectToCopy = "Select the values to copy them.";
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
        internal const string Pausing = RefreshWording.PausingMonitoring;
        internal const string UnavailableShuttingDown = "Unavailable: web mode is stopping.";
        internal const string LastResultHeading = "The last refresh";

        // The theme (D110): the shared words (ThemeWording) and what only the web UI says: System follows the browser, the
        // choice is this browser's (a cookie), and a page needs Apply theme (the web UI's forms are sent, D80)
        internal const string ThemeIntro = "How Campertron's pages look in this browser. Your choice applies to every page and stays with this browser; "
            + "it changes none of your settings files and not CampertronGUI's theme.";
        internal const string ThemeSystemHelp = "Follow this browser's light or dark setting, which usually follows your operating system's. " + ThemeWording.DefaultNote;
        internal const string ThemeApply = "Apply theme";
        internal const string ThemeStorage = "Kept by this browser for this Campertron, in a cookie that holds only the theme. "
            + "Another browser, or another browser profile, keeps its own choice.";
        // the line that says which theme the page is shown in: for System, both names, of which the stylesheet shows the one
        // the browser's setting gives (WebTheme, site.css)
        internal const string ShownBefore = "Campertron is shown in its ";
        internal const string ShownAfter = " theme.";
        internal const string ShownAfterSystem = " theme, as this browser asks.";

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

        // the status line while a refresh runs (its progress, its pause and Cancel as one moment took them,
        // WebRefreshView): the shared words, or the pause before it
        internal static string Running(RidbRefreshProgress? Progress, bool Pausing_, bool CancelRequested)
        {
            if (Pausing_ && Progress == null)
            {
                return CancelRequested ? RefreshWording.CancelingText : Pausing;
            }
            string Stage = RefreshWording.StageName(Progress?.Stage ?? RidbRefreshStage.AcquiringLock);
            if (!(Progress?.CanCancel ?? true))
            {
                return RefreshWording.Finishing(Stage, CancelRequested);
            }
            return CancelRequested ? RefreshWording.CancelingText : RefreshWording.Refreshing(Stage);
        }

        // The page's automatic progress while a refresh runs (D109; WebLive): the web UI's own words, as the results page's
        // (D108) are for a search. The refresh is the web UI's, not the browser's: a page that stops updating says it goes on
        internal const string LiveManual = "Select Show the latest progress to see how the refresh is going.";
        internal const string LiveUpdating = "This page updates by itself while the refresh runs. Last update: {time}.";
        internal const string LiveFailing = "This page can't get the refresh's progress right now (last update: {time}). That does not stop the refresh, and the page keeps trying. Show the latest progress shows the page as it is now.";
        internal const string LiveResumed = "This page gets the refresh's progress again.";
        internal const string LiveEndedNotShown = "The refresh has ended, but this page can't show how it ended right now, and it keeps trying. Show the latest progress shows it.";
        internal const string LiveSignedOut = "This page stopped updating: this browser is no longer signed in. The refresh belongs to this web UI and goes on; sign in again to see how it goes.";
        internal const string LiveOutOfDate = "This page stopped updating: it no longer shows the web UI's refresh (the web UI started again). Show the latest progress shows the page as it is now.";
        internal static readonly WebLiveWords Live = new WebLiveWords(LiveManual, LiveUpdating, LiveFailing, LiveResumed, LiveEndedNotShown, LiveSignedOut, LiveOutOfDate);

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
