using CampertronLibrary.function.Forms;

namespace Campertron
{
    // The guided setup's words that only the desktop GUI says (v0.4.0's phase 5; business-rules-gui §42): its place in the
    // Monitoring tab, leaving it, the tabs it sends the user to (Settings > Campertron, Settings > System, Searches, the
    // monitoring's Status), how the desktop GUI's monitoring starts (D115: only on the user's Start, only while Campertron is
    // open) and its one sentence in the shared orchestration (Host). Every word every host says is the shared
    // GuidedSetupWording's; the steps' orchestration is GuidedSetupFlow's; a form's words are the form's. These stand where the
    // web UI's WebGuidedSetupWording names its pages, its menu and web mode. Constants only: never an exception's message, a
    // server's answer, a recipient, a topic, a password, a token, an address or a path.
    public static class DesktopGuidedSetupWording
    {
        // ---- every step ----
        public const string LeaveHint = "Leaving guided setup saves, discards and marks nothing: the other tabs stay above, an unsaved change stays in its editor "
            + "(Settings > Campertron, Searches) until you save or revert it there or here, and guided setup changes only the files its steps name. "
            + "It stays here, under Monitoring, whenever you want it.";
        public const string ReadinessHeading = "Where you are";
        public const string ProblemsHeading = "There is a problem";

        // ---- step 1 ----
        public const string MethodHint = "You can change this later: the last step saves your choice as outputTo in General.yaml, and Settings > Campertron changes it at any time. "
            + "Nothing is saved on this step.";

        // ---- step 2 ----
        public const string SaveAndContinueHint = "Save and continue saves your changes (nothing when there are none) and goes to the next step once the saved file is ready. "
            + "The preview shows the lines it writes.";

        // ---- step 3 ----
        public const string SearchHelp = "Campertron checks the searches that are Enabled (autoRun). Create a search, choose its campground, the dates and the number of people, check Enabled (autoRun) and save it. "
            + "Its other settings (the days of the week and the filters) keep the example's values: change them in the Searches tab.";
        public const string OtherSettingsLabel = "Show all settings of this search in the Searches tab";
        public const string OtherSettingsHint = "The days of the week, the matching and the filters are in the Searches tab; they keep their values here.";
        public const string HiddenChangesWarning = "Warning: this search also has unsaved changes in the Searches tab. Save and continue writes them too: check them there first.";
        public const string CampgroundDataProblem = "Warning: the campground data can't be read, so campgrounds can't be listed or checked here. Settings > System refreshes it; "
            + "a search can still be set up by its campground's ID.";
        public const string CampgroundDataLabel = "Show Settings > System";

        // ---- step 4 ----
        public static string TurnOnHelp(GuidedSetupMethod Method) => "This saves outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml and changes nothing else: "
            + "it does not start the monitoring. The desktop app monitors only after you select Start the monitoring (the next step offers it) and only while it is open; "
            + "it then checks your enabled searches every minute and "
            + (Method == GuidedSetupMethod.Email ? "emails new openings (and sends them with ntfy too when Ntfy.yaml turns ntfy on)." : "sends new openings with ntfy.")
            + " CampertronConsole's email and ntfy mode, its web mode and the container's headless monitoring start it by themselves with the same setting.";
        public const string GeneralUnsaved = "Error: General.yaml has unsaved changes in Settings > Campertron, so nothing was saved here. Save or revert them there first.";

        // ---- done ----
        public static string DoneText(GuidedSetupMethod Method) => "Your " + (Method == GuidedSetupMethod.Email ? "email" : "ntfy") + " settings, at least one enabled search and outputTo: "
            + GuidedSetupStatus.OutputOf(Method) + " are saved, and the monitoring's checks of the files pass. "
            + "Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on the monitoring's Status.";
        public const string MonitoringNotRunning = "The desktop app monitors only after you start it and only while it is open: closing Campertron stops it. "
            + "To start it now, select Start the monitoring; the monitoring's Status shows how it goes.";
        public const string MonitoringRunningChanged = "It runs with the settings and search files it read when it started: select Restart in the monitoring's Status so that it reads your changes.";
        public const string ShowStatusLabel = "Show the monitoring's Status";

        // the desktop GUI's sentence in the shared orchestration (GuidedSetupFlow.TurnOn)
        internal static readonly GuidedSetupHostWords Host = new GuidedSetupHostWords
        {
            GeneralUnsaved = GeneralUnsaved
        };
    }
}
