using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;

namespace CampertronLibrary.function.Interactive.Monitoring
{
    // The interactive console's own words for its monitoring (v0.4.0's phase 11, D127; business-rules §43): the menu and its
    // notes, how an action is asked for and waited for at a terminal, the console's end while the monitoring is on, a
    // signal that ends the process, and the console's sentences in the shared words (Host). Only the console says them.
    // Every word the other UIs show for the same thing is the shared form layer's and is shown as it gives it (ER-37): the
    // state, the facts, why an action is unavailable, what an action did, a refused start, a failure, the last check and
    // its searches (MonitoringWording), the five minutes after a block (BlockHold), a search's rows and their pages
    // (ResultSearchViewModel, ResultsListing) and a campsite's booking link (ResultsLink). Fixed words: no settings value, no
    // path, no secret and no exception's text.
    public static class InteractiveMonitoringWording
    {
        // ---- the menu ----

        public const string Monitoring = MonitoringWording.Heading;
        public const string MonitoringTitle = "Monitoring (nothing starts or stops until you choose it)";
        public const string Intro = "The monitoring checks the Enabled (autoRun) search files every minute and notifies new openings by email or ntfy, as outputTo "
            + "in General.yaml says (Email or Ntfy), exactly as CampertronConsole's email and ntfy mode do: openings it notified are recorded in Camp.History and "
            + "notified again only after a check has seen them unavailable. It starts only when you choose Start, and it belongs to this interactive console, "
            + "not to this menu: it goes on while you use the other menus, until it is stopped, a check fails or the console ends. It never starts by itself, "
            + "and a later start of the console does not start it again.";
        public const string ShowLatest = "Show the latest status";
        public const string ShowLastCheck = "The last check's new openings";
        public const string BackToMonitoring = "Back to Monitoring";
        public const string BackToLastCheck = "Back to the last check";
        public const string ChooseSearch = "Choose a search of the last check to see its new openings.";
        public const string StopHelp = "Stop ends the monitoring until you start it again: it saves nothing. A check that is searching ends at its next step and "
            + "sends nothing; a check that is sending finishes its notifications and records them in Camp.History first.";
        public const string LastCheckNote = "These are the new openings of the monitoring's last check: the openings it found that are not in Camp.History, which it "
            + "notifies. An opening it notified is not shown or notified again while it stays available; once a check has seen it unavailable, it is notified "
            + "again when it becomes available again. A stay of several nights is shown whole when any of its nights is new. They are the monitoring's own: "
            + "Results shows only the searches you run there, and neither replaces the other. Availability may have changed since then: the booking page shows "
            + "what is available now.";

        // the settings folder holds the settings and search files the monitoring reads (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there is nothing to monitor.";
        public const string FolderMissing = "The settings folder does not exist yet, so there is nothing to monitor. A start without arguments, or the desktop app, creates it.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there is nothing to monitor. In a container, mount the settings folder at /config.";
        // the cache folder holds the campsite details every check reads (the console never creates it)
        public const string CacheFolderMissing = "The cache folder does not exist yet, so the monitoring can't start here. A start without arguments, or the desktop app, creates it.";
        public const string ContainerCacheFolderMissing = "The cache folder was not found, so the monitoring can't start here. In a container, mount the cache folder at /cache.";

        // ---- an action ----

        public const string StartQuestion = "Start the monitoring now?";
        public const string RestartQuestion = "Restart the monitoring now?";
        public const string NotStarted = "Nothing was started.";
        public const string NotRestarted = "Nothing was restarted.";
        public const string WaitHow = "Esc or Ctrl+C stops waiting here; the monitoring's start or stop goes on and Monitoring shows how it went.";
        public const string StoppedWaiting = "No longer waiting: the monitoring's start or stop goes on, and Monitoring shows how it goes.";
        public static string StillBusy(TimeSpan Waited) =>
            "The monitoring is still starting or stopping after " + Seconds(Waited) + ": it goes on, and Monitoring shows how it goes (" + ShowLatest + ").";
        public const string InputEndedWhileWaiting = "The input ended, so the console ends; the monitoring is stopped first.";

        // ---- the console's end while the monitoring is on (D115's rule for an application's end, at a terminal) ----

        public const string ExitHeading = "The monitoring is running";
        public const string ExitQuestion = "End the interactive console?";
        public const string ExitText = "Ending the interactive console stops the monitoring: no new openings are checked or notified until you start it again "
            + "(a later start of the console does not start it by itself). A check that is searching ends at its next step and sends nothing; a check that is "
            + "sending finishes its notifications and records them in Camp.History first.";
        public const string KeepOpen = "Keep Campertron open";
        public const string StopAndExit = "Stop the monitoring and exit";
        public const string KeptOpen = "The interactive console stays open, and the monitoring goes on.";
        public const string StoppingAtEnd = "Stopping the monitoring before the interactive console ends: a check that is searching ends at its next step and "
            + "sends nothing; a check that is sending finishes its notifications and records them first…";
        public const string StoppedAtEnd = "The monitoring stopped.";
        public static string NotStoppedInTime(TimeSpan Waited) =>
            "The monitoring had not stopped after " + Seconds(Waited) + ", so it ends with the console: what a check that was still sending had not recorded "
            + "in Camp.History yet is notified again by the next monitoring.";
        // a signal that ends the process (SIGTERM, a hang-up, a Ctrl+C outside a question) while the monitoring is on
        public const string StoppingForSignal = "Campertron is ending, so the monitoring is stopped first: a check that is searching ends at its next step and "
            + "sends nothing; a check that is sending finishes its notifications and records them first…";

        // ---- after a refresh of the campground data that paused the monitoring (D83 point 6) ----

        public const string ResumedAfterRefresh = "The monitoring that the refresh paused starts again by itself now, with the settings, the search files and the "
            + "campground data as they are now; Monitoring shows how it goes.";

        // ---- the console's sentences in the shared words ----

        // The monitoring controller writes them into a start's refusal and a failure; the menu's state and notices use them.
        // The console never starts the monitoring with itself (only the user's Start), so NotStartedWithHost says what a
        // start of the user's says
        internal static readonly MonitoringHostWords Host = new MonitoringHostWords
        {
            EmailMissingFix = "Enter the SMTP server, its port, the sender and at least one recipient in Notifications > " + ConfigFiles.EmailFileName
                + " and save it, then select Start.",
            NoCacheFolderFix = "Start CampertronConsole without arguments once, or the desktop app, which creates it; in a container, mount the cache folder at /cache. Then select Start.",
            NoCampgroundDataFix = "Install it with Campgrounds > Refresh or install campground data, or start CampertronConsole without arguments once; then select Start.",
            RefusedFileFix = "Fix it in Settings, Notifications or Searches, then select Start.",
            InvalidYamlFix = "Fix it in Settings, Notifications or Searches, or in a text editor, then select Start.",
            UnreadableFileFix = "Check it in Settings, Notifications or Searches, then select Start.",
            BlockedFix = "Start is available again after five minutes; until then Results waits too.",
            NotificationFix = "The monitoring does not start again by itself: fix the problem (Notifications sends a test email and a test notification), then select Start.",
            NotStartedWithHost = "the last start did not start it.",
            UnavailableShuttingDown = "Start, Stop and Restart are unavailable: the interactive console is ending.",
            ShuttingDownText = "Nothing was done: the interactive console is ending.",
            StaleText = "The monitoring changed since this menu was shown (it started, stopped or ended meanwhile), so nothing was started or stopped. The menu shows it as it is now."
        };

        // the shared words of the menu's state, its unavailable actions and an action's notice, with the console's sentences
        internal static string StateText(MonitoringControlView View) => MonitoringWording.StateText(View, Host);
        internal static string Unavailable(MonitoringControlView View) => MonitoringWording.Unavailable(View, Host);
        internal static string? Outcome(MonitoringControlOutcome Outcome, string? Cooldown) => MonitoringWording.Outcome(Outcome, Cooldown, Host);
        // the console never starts it with itself: no note of its own after the start's time
        internal static string StartedText(MonitoringControlView View) => MonitoringWording.StartedText(View, string.Empty);

        private static string Seconds(TimeSpan Value)
        {
            int Whole = (int)Math.Round(Value.TotalSeconds);
            return Whole.ToString(CultureInfo.InvariantCulture) + (Whole == 1 ? " second" : " seconds");
        }
    }
}
