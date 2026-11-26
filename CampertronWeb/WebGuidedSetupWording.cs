using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The guided setup's words that only the web UI says (v0.4.0's phase 3, D117; business-rules-settings §37): its pages,
    // its menu, its start page, the settings, searches, system and monitoring pages it sends the user to, web mode's own
    // start, and its one sentence in the shared orchestration (Host). Until phase 3 they were in the shared
    // GuidedSetupWording; they moved here unchanged, beside the words every host says (GuidedSetupWording) and the steps'
    // orchestration (GuidedSetupFlow). Constants only.
    internal static class WebGuidedSetupWording
    {
        // ---- every step: the full web UI stays one link away ----
        internal const string FullWebUi = "Every page of the web UI stays in the menu above, and guided setup changes only the files its steps name.";
        internal const string SkipLabel = "Skip guided setup and use the full web UI";
        internal const string LeaveLabel = "Leave guided setup and use the full web UI";
        internal const string SkipHint = "Skipping or leaving saves and marks nothing. The start page offers guided setup again whenever you want it.";

        // ---- step 1 ----
        internal const string MethodHint = "You can change this later: the last step saves your choice as outputTo in General.yaml, and the settings pages change it at any time. Nothing is saved on this step.";

        // ---- step 2 ----
        internal const string SaveAndContinueHint = "Save and continue saves your changes (nothing when there are none) and goes to the next step once the saved file is ready. "
            + "Check changes shows the problems and the lines Save would write, without saving.";

        // ---- step 3 ----
        internal const string SearchHelp = "Campertron checks the searches that are Enabled (autoRun). Create a search, choose its campground, the dates and the number of people, check Enabled (autoRun) and save it. "
            + "Its other settings (the days of the week and the filters) keep the example's values: change them on the searches page.";
        internal const string OtherSettingsLink = "All settings of this search, on the searches page";
        internal const string OtherSettingsHint = "The days of the week, the matching and the filters are on the searches page; they keep their values here.";
        internal const string HiddenChangesWarning = "Warning: this search also has unsaved changes on its searches page. Save writes them too: check them there first.";
        internal const string CampgroundDataProblem = "The campground data can't be read, so campgrounds can't be listed or checked here. The system page refreshes it (Settings > System); a search can still be set up by its campground's ID.";
        internal const string CampgroundDataLink = "Settings > System";
        // the validation's words name CampertronGUI's tab; in the web UI they mean its page
        internal const string TabMeansPage = "Where these words name the Searches tab, in this web UI they mean its Searches page.";

        // ---- step 4 ----
        internal static string TurnOnHelp(GuidedSetupMethod Method) => "This saves outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml and changes nothing else. "
            + "From then on web mode starts the monitoring by itself whenever it starts: it checks your enabled searches every minute and "
            + (Method == GuidedSetupMethod.Email ? "emails new openings (and sends them with ntfy too when Ntfy.yaml turns ntfy on)." : "sends new openings with ntfy.")
            + " The console's email and ntfy mode and the container's headless monitoring use the same setting.";
        internal const string GeneralUnsaved = "Error: General.yaml has unsaved changes on its settings page, so nothing was saved here. Save or revert them there first.";

        // ---- done ----
        internal static string DoneText(GuidedSetupMethod Method) => "Your " + (Method == GuidedSetupMethod.Email ? "email" : "ntfy") + " settings, at least one enabled search and outputTo: "
            + GuidedSetupStatus.OutputOf(Method) + " are saved, and the monitoring's checks of the files pass. "
            + "Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on the monitoring page.";
        internal const string MonitoringNotRunning = "It starts by itself whenever web mode starts. To start it now, select Start the monitoring; the monitoring page shows how it goes.";
        internal const string MonitoringRunningChanged = "It runs with the settings and search files it read when it started: select Restart on the monitoring page so that it reads your changes.";
        internal const string MonitoringPage = "Go to the monitoring page";
        internal const string StartPage = "Go to the start page";

        // the web UI's sentence in the shared orchestration (GuidedSetupFlow.TurnOn)
        internal static readonly GuidedSetupHostWords Host = new GuidedSetupHostWords
        {
            GeneralUnsaved = GeneralUnsaved
        };
    }
}
