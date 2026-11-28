using CampertronLibrary.function.Forms;

namespace CampertronLibrary.function.Interactive.GuidedSetup
{
    // The guided setup's words that only the interactive console says (v0.4.0's phase 12, D128; business-rules §43): its
    // menu, how it is used at a terminal, leaving it, the main menu's choices it names (Notifications, Searches, Campgrounds,
    // Monitoring), how the console's monitoring starts and its one sentence in the shared orchestration (Host). Every word
    // every host says is the shared GuidedSetupWording's (the steps, their headings and states, what each step is for, the
    // review and the done words); the steps' order and actions are GuidedSetupFlow's; a form's words are the form's. These
    // stand where the desktop GUI's DesktopGuidedSetupWording and the web UI's WebGuidedSetupWording name their tabs and pages.
    // Constants only: never a settings value, a secret, a server's answer, an exception's text or a path.
    public static class InteractiveGuidedSetupWording
    {
        // ---- the menu ----

        public const string GuidedSetup = GuidedSetupWording.Heading;
        public const string Title = "Guided setup (nothing is saved, sent or started until you choose to)";
        public const string HowItWorks = "Each step shows the same form, checks and words as Notifications and Searches, the desktop app and the web UI. "
            + "A file is saved only when you choose Save and continue (or the review's Turn the monitoring on) and answer y, and nothing is sent or started unless you choose it. "
            + "Where you are comes from the saved files only: leaving keeps what was saved and nothing else, and choosing Guided setup again starts where the saved files say.";
        public const string WhereYouAre = "Where you are";
        public const string LeaveLabel = "Leave guided setup";
        public const string Left = "You left guided setup: the files it saved stay saved, nothing unsaved was kept, and the monitoring is as it was. "
            + "Choose Guided setup in the main menu to go on where the saved files say.";
        // the settings folder is needed before anything can be set up (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there is nothing to set up here.";
        public const string FolderMissing = "The settings folder does not exist yet, so there is nothing to set up here. A start without arguments, or the desktop app, creates it.";
        // ---- a first setup (since D130, PT1): the settings folder offered, created only after a y ----
        public const string SetUpExplanation = "The settings folder does not exist yet. Guided setup can create it now, as Campertron's first start does: the settings folder and the cache folder, "
            + "General.yaml with its starting settings (outputTo: Console, no automatic refresh), Email.yaml with sample settings to fill in later, and the campground data that comes with Campertron. "
            + "No search is created, nothing is downloaded or sent, and the monitoring does not start.";
        public const string SetUpQuestion = "Create the settings folder now?";
        public const string NothingCreated = "Nothing was created. Choose Guided setup again when you want to set Campertron up.";
        public const string SettingUp = "Creating the settings folder and preparing the campground data...";
        public const string SetUpDone = "The settings folder was created. Guided setup goes on with the saved files.";
        public const string FolderAppeared = "The settings folder exists now (another program created it), so nothing was created here. Guided setup goes on with the saved files.";
        public static string SetUpFailed(string Kind) => "The settings folder could not be created because of a problem (" + Kind + "). "
            + "What was created stays; choose Guided setup again to go on, or start Campertron without arguments.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there is nothing to set up here. In a container, mount the settings folder at /config.";

        // a step in the list of steps: "Step N of 4", its name and its state in words (GuidedSetupWording.StepOf, StepState), never
        // numbered as a choice is
        public static string StepLine(GuidedSetupStep Step, string Name, string State) => GuidedSetupWording.StepOf(Step) + ": " + Name + " (" + State + ")";

        // ---- step 1 ----

        public const string MethodHint = "You can change this later: the last step saves your choice as outputTo in General.yaml, and Settings > General settings changes it at any time. "
            + "Nothing is saved on this step.";
        public static string ChosenNow(GuidedSetupMethod Method) => "Chosen now: " + (Method == GuidedSetupMethod.Email ? "email." : "ntfy.");

        // ---- step 2 ----

        public const string SaveAndContinueHint = "Save and continue saves your changes (nothing when there are none), after a y, and goes to the next step once the saved file is ready. "
            + "Show the changes Save would write shows the lines first. Back and Leave guided setup ask before they leave unsaved changes behind.";
        public const string ProblemsHeading = "There is a problem";

        // ---- step 3 ----

        public const string SearchHelp = "Campertron checks the searches that are Enabled (autoRun). Create a search, choose its campground, the dates and the number of people, turn Enabled (autoRun) on and save it. "
            + "Its other settings (the days of the week and the filters) keep the example's values: change them in Searches, in the main menu.";
        public const string SearchPageHelp = "This step shows the search's main settings. The days of the week, the matching and the filters keep their values here; they are in Searches, in the main menu.";
        public const string OtherSettingsProblem = "A problem above is in a setting this step does not show: fix it in Searches, in the main menu.";
        public static string SetUp(string File) => GuidedSetupWording.SetUpThisSearch + ": " + File;
        public const string DataMissing = "The campground data is not prepared yet, so campgrounds can't be listed, found or checked here. "
            + "Refresh or install campground data downloads it from Recreation.gov (about 570 MB) and installs it, after a y; a start without arguments, or the desktop app, prepares the copy that comes with Campertron.";
        public const string DataUnreadableHow = "Refresh or install campground data replaces it, after a y.";
        public const string RefreshLabel = "Refresh or install campground data";

        // ---- step 4 ----

        public static string TurnOnHelp(GuidedSetupMethod Method) => "This saves outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml and changes nothing else: "
            + "it does not start the monitoring. This console monitors only after you choose Start the monitoring (the next step offers it, and so does Monitoring in the main menu) "
            + "and only while it is open; it then checks your enabled searches every minute and "
            + (Method == GuidedSetupMethod.Email ? "emails new openings (and sends them with ntfy too when Ntfy.yaml turns ntfy on)." : "sends new openings with ntfy.")
            + " From then on CampertronConsole started without arguments monitors by itself instead of opening its menu, and so do web mode and the container's headless monitoring.";
        public static string TurnOnQuestion(GuidedSetupMethod Method) => "Save outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml now?";
        public const string NothingSaved = "Nothing was saved.";
        public const string TurnOnUnavailable = "Turn the monitoring on is not offered yet: the monitoring would refuse the saved files. Fix the problems listed first.";
        // the console's sentence in the shared orchestration (GuidedSetupFlow.TurnOn): the console's forms live only while their
        // screen is shown, so General.yaml never has an unsaved change here; the words are kept for the flow's rule
        public const string GeneralUnsaved = "Error: General.yaml has unsaved changes, so nothing was saved here. Save or revert them in Settings > General settings first.";

        // ---- done ----

        public static string DoneText(GuidedSetupMethod Method) => "Your " + (Method == GuidedSetupMethod.Email ? "email" : "ntfy") + " settings, at least one enabled search and outputTo: "
            + GuidedSetupStatus.OutputOf(Method) + " are saved, and the monitoring's checks of the files pass. "
            + "Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on Monitoring in the main menu.";
        public const string MonitoringNotRunning = "This console monitors only after you start it and only while it is open: ending the console stops it. "
            + "To start it now, choose Start the monitoring; Monitoring in the main menu then shows how it goes.";
        public const string MonitoringRunningChanged = "It runs with the settings and search files it read when it started: choose Restart in Monitoring, in the main menu, so that it reads your changes.";
        public const string DoneChoices = "What next";
        public const string ElsewhereNote = "A monitoring that runs elsewhere (CampertronConsole started without arguments, web mode, the desktop app or a container) read the files when it started: restart it so that it uses them.";

        // the console's sentence in the shared orchestration
        internal static readonly GuidedSetupHostWords Host = new GuidedSetupHostWords
        {
            GeneralUnsaved = GeneralUnsaved
        };
    }
}
