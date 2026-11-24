namespace CampertronWeb
{
    // The words of the results page's automatic progress (D108; its answer is WebResults.Observe, its shape WebLiveAnswer,
    // since D109 the web UI's one shape for every live page). They are the web UI's own: the desktop GUI's Results tab
    // shows a run's progress as it goes by itself. The page puts them into its markup (the script has no words of its own),
    // and the script says the time of the last update where a text has {time}, as the browser writes a time.
    internal static class WebResultsLiveWording
    {
        internal const string TimePlaceholder = WebLive.TimePlaceholder;

        // the intro's sentence about the progress
        internal const string Intro = "While a search runs, this page shows its progress as it goes, in a browser that runs the web UI's own script; Show the latest progress shows the page as it is now.";
        // what the page says until its script runs (and in a browser that runs no script): the server's page as it was sent
        internal const string Manual = "Select Show the latest progress to see how the search is going.";
        // the page gets the run's state
        internal const string Updating = "This page updates by itself while the search runs. Last update: {time}.";
        // the page gets no answer, or one it can't use (the network, the web UI, a proxy): the search itself is not affected
        internal const string Failing = "This page can't get the search's progress right now (last update: {time}). That does not stop the search, and the page keeps trying. Show the latest progress shows the page as it is now.";
        // said once when answers come again after a failure
        internal const string Resumed = "This page gets the search's progress again.";
        // the run has ended, but the page can't get its results part
        internal const string EndedNotShown = "The search has ended, but this page can't show its results right now, and it keeps trying. Show the latest progress shows them.";
        // the web UI sent the page to the sign-in: the session ended, and its run with it
        internal const string SignedOut = "This page stopped updating: this browser is no longer signed in, and a search ends with its browser session. Sign in again to see the results page.";
        // the page names no run of the session as it is now
        internal const string OutOfDate = "This page stopped updating: it no longer shows this browser's current search (another search started in this browser, or the web UI started again). Show the latest progress shows the page as it is now.";

        internal static readonly WebLiveWords Words = new WebLiveWords(Manual, Updating, Failing, Resumed, EndedNotShown, SignedOut, OutOfDate);
    }
}
