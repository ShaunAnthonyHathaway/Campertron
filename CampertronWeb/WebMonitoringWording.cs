using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The monitoring page's own words (v0.3.0's PR 8, D98; business-rules-settings §35): what only a page of the web UI says
    // around the shared words of the monitoring's states and decisions (MonitoringWording, v0.4.0's phase 2, which the
    // monitoring controller and the page both use). Here: the page's introduction, its live words (D109), what a page of a
    // browser shows, and the web UI's own sentences in the shared words (Host: where to fix a file, restarting web mode,
    // the results page's wait after a block, the settings pages' tests, web mode's own start and stop, an out-of-date
    // page). Fixed words: never an exception's message, a server's answer, a recipient, a topic, a password, a token, an
    // address or a path.
    internal static class WebMonitoringWording
    {
        internal const string Intro = "The monitoring checks the Enabled (autoRun) search files every minute and notifies new openings by email or ntfy, as outputTo in General.yaml says (Email or Ntfy), exactly as CampertronConsole's email and ntfy mode do: openings it notified are recorded in Camp.History and notified again only after a check has seen them unavailable. It belongs to this web UI, not to a browser: it goes on when you sign out or close the page, until it is stopped, a check fails or web mode stops, and it starts by itself when web mode starts.";
        // the page's automatic progress (D109; WebLive): the web UI's own words, as the results page's (D108) are for a search
        internal const string LiveIntro = "While the monitoring runs, this page shows its status as it goes, in a browser that runs the web UI's own script; Show the latest status shows the page as it is now.";
        internal const string LiveManual = "Select Show the latest status to see how the monitoring is going.";
        internal const string LiveUpdating = "This page updates by itself while the monitoring runs. Last update: {time}.";
        internal const string LiveFailing = "This page can't get the monitoring's status right now (last update: {time}). That does not stop the monitoring, and the page keeps trying. Show the latest status shows the page as it is now.";
        internal const string LiveResumed = "This page gets the monitoring's status again.";
        internal const string LiveEndedNotShown = "The monitoring is no longer running, but this page can't show how it ended right now, and it keeps trying. Show the latest status shows it.";
        internal const string LiveSignedOut = "This page stopped updating: this browser is no longer signed in. The monitoring belongs to this web UI and goes on; sign in again to see how it goes.";
        internal const string LiveOutOfDate = "This page stopped updating: it no longer shows the web UI's monitoring (the web UI started again). Show the latest status shows the page as it is now.";
        internal static readonly WebLiveWords Live = new WebLiveWords(LiveManual, LiveUpdating, LiveFailing, LiveResumed, LiveEndedNotShown, LiveSignedOut, LiveOutOfDate);
        internal const string Times = "Times are those of the computer that runs Campertron.";
        internal const string ShowLatest = "Show the latest status";
        internal const string StopHelp = "Stop ends the monitoring until you start it again or web mode starts again: it saves nothing. A check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them in Camp.History first.";
        internal const string LastCheckNote = "These are the new openings of the monitoring's last check: the openings it found that are not in Camp.History, which it notifies. An opening it notified is not shown or notified again while it stays available; once a check has seen it unavailable, it is notified again when it becomes available again. A stay of several nights is shown whole when any of its nights is new. They are the monitoring's own: the results page shows only the searches you run there, and neither replaces the other. Availability may have changed since then: the booking page shows what is available now.";
        // web mode stops (the host's stop), and a page that was out of date
        internal const string UnavailableShuttingDown = "Start, Stop and Restart are unavailable: web mode is stopping.";
        internal const string ShuttingDownText = "Nothing was done: web mode is stopping.";
        internal const string StaleText = "This page was out of date (the monitoring started, stopped or ended, or the page was sent again), so nothing was started or stopped. The page shows the monitoring as it is now.";

        // the web UI's sentences in the shared words (the monitoring controller writes them into a start's refusal and a
        // failure; the page's state and notices use them below)
        internal static readonly MonitoringHostWords Host = new MonitoringHostWords
        {
            EmailMissingFix = "Enter the SMTP server, its port, the sender and at least one recipient in Settings > " + ConfigFiles.EmailFileName + " and save it, then select Start.",
            NoCacheFolderFix = "Restart web mode.",
            NoCampgroundDataFix = "Restart web mode; if that does not help, check that the settings folder can be written.",
            RefusedFileFix = "Fix it on its settings or searches page, then select Start.",
            InvalidYamlFix = "Fix it on its settings or searches page, or in a text editor, then select Start.",
            UnreadableFileFix = "Check it on its settings or searches page, then select Start.",
            BlockedFix = "Start is available again after five minutes; until then the results page waits too.",
            NotificationFix = "The monitoring does not start again by itself: fix the problem (the settings pages send a test email and a test notification), then select Start.",
            NotStartedWithHost = "it did not start when web mode started.",
            UnavailableShuttingDown = UnavailableShuttingDown,
            ShuttingDownText = ShuttingDownText,
            StaleText = StaleText
        };

        // the shared words of the page's state, its unavailable actions and an action's notice, with the web UI's sentences
        internal static string StateText(MonitoringControlView View) => MonitoringWording.StateText(View, Host);
        internal static string Unavailable(MonitoringControlView View) => MonitoringWording.Unavailable(View, Host);
        internal static string? Outcome(MonitoringControlOutcome Outcome, string? Cooldown) => MonitoringWording.Outcome(Outcome, Cooldown, Host);
    }
}
