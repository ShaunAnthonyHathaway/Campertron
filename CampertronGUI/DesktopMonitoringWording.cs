using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;

namespace Campertron
{
    // The desktop GUI's own words of the monitoring (v0.4.0's phase 4, D115 and D118): what only the desktop GUI says around
    // the shared words of the monitoring's states and decisions (MonitoringWording, which the web UI's monitoring page shows
    // too). Here: the Monitoring tab's introduction and notes, the close confirmation, and the desktop GUI's sentences in the
    // shared words (Host: where to fix a file, restarting Campertron, the Results tab's wait after a block, the settings
    // editors' tests, Campertron closing, an out-of-date tab). Fixed words: never an exception's message, a server's answer,
    // a recipient, a topic, a password, a token, an address or a path.
    public static class DesktopMonitoringWording
    {
        public const string Intro = "The monitoring checks the Enabled (autoRun) search files every minute and notifies new openings by email or ntfy, as outputTo "
            + "in General.yaml says (Email or Ntfy), exactly as CampertronConsole's email and ntfy mode do: openings it notified are recorded in Camp.History and "
            + "notified again only after a check has seen them unavailable. It starts only when you select Start, and it belongs to Campertron, not to this tab: "
            + "it goes on while you use the other tabs, until it is stopped, a check fails or you close Campertron. It never starts by itself when Campertron "
            + "starts, except after the restart that follows a refresh of the campground data that paused it.";
        public const string StopHelp = "Stop ends the monitoring until you start it again: it saves nothing. A check that is searching ends at its next step "
            + "and sends nothing; a check that is sending finishes its notifications and records them in Camp.History first.";
        public const string LastCheckNote = "These are the new openings of the monitoring's last check: the openings it found that are not in Camp.History, "
            + "which it notifies. An opening it notified is not shown or notified again while it stays available; once a check has seen it unavailable, it is "
            + "notified again when it becomes available again. A stay of several nights is shown whole when any of its nights is new. They are the monitoring's "
            + "own: the Results tab shows only the searches you run there, and neither replaces the other. Availability may have changed since then: the "
            + "booking page shows what is available now.";
        // the window's close while the monitoring is on (D115): the user cancels the close, or stops the monitoring and closes
        public const string CloseHeading = "Campertron is monitoring";
        public const string CloseText = "Closing Campertron stops the monitoring: no new openings are checked or notified until you open Campertron and select "
            + "Start again. A check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records "
            + "them in Camp.History first.";
        public const string KeepOpenLabel = "Keep Campertron open";
        public const string KeepOpenHelp = "Close this and leave the monitoring running (Escape)";
        public const string StopAndCloseLabel = "Stop the monitoring and close";
        public const string StopAndCloseHelp = "Stop the monitoring, then close Campertron";
        public const string StoppingText = "Stopping the monitoring… Campertron closes when it has stopped.";

        // the desktop GUI's sentences in the shared words (the monitoring controller writes them into a start's refusal and a
        // failure; the tab's state and notices use them below). The desktop GUI never starts the monitoring with itself (D115:
        // only the user's Start), so NotStartedWithHost is never shown; it says what a start of the user's says
        internal static readonly MonitoringHostWords Host = new MonitoringHostWords
        {
            EmailMissingFix = "Enter the SMTP server, its port, the sender and at least one recipient in Settings > Campertron > " + ConfigFiles.EmailFileName
                + " and save it, then select Start.",
            NoCacheFolderFix = "Restart Campertron.",
            NoCampgroundDataFix = "Restart Campertron; if that does not help, check that the settings folder can be written.",
            RefusedFileFix = "Fix it in the Settings or Searches tab, then select Start.",
            InvalidYamlFix = "Fix it in the Settings or Searches tab, or in a text editor, then select Start.",
            UnreadableFileFix = "Check it in the Settings or Searches tab, then select Start.",
            BlockedFix = "Start is available again after five minutes; until then the Results tab waits too.",
            NotificationFix = "The monitoring does not start again by itself: fix the problem (Settings > Campertron sends a test email and a test notification), then select Start.",
            NotStartedWithHost = "the last start did not start it.",
            UnavailableShuttingDown = "Start, Stop and Restart are unavailable: Campertron is closing.",
            ShuttingDownText = "Nothing was done: Campertron is closing.",
            StaleText = "This tab was out of date (the monitoring started, stopped or ended meanwhile), so nothing was started or stopped. The tab shows the monitoring as it is now."
        };

        // the shared words of the tab's state, its unavailable actions and an action's notice, with the desktop GUI's sentences
        internal static string StateText(MonitoringControlView View) => MonitoringWording.StateText(View, Host);
        internal static string Unavailable(MonitoringControlView View) => MonitoringWording.Unavailable(View, Host);
        internal static string? Outcome(MonitoringControlOutcome Outcome, string? Cooldown) => MonitoringWording.Outcome(Outcome, Cooldown, Host);
        // the desktop GUI never starts it with itself: no note of its own after the start's time
        internal static string StartedText(MonitoringControlView View) => MonitoringWording.StartedText(View, string.Empty);
    }
}
