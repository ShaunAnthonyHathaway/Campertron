using CampertronLibrary.function.Base;
using System.Collections.Generic;
using System.Globalization;

namespace CampertronLibrary.function.Forms
{
    // The guided setup's own words (v0.3.0's PR 10, D100; business-rules-settings §37). Every rule a step shows is its
    // form's (the Email.yaml, Ntfy.yaml, General.yaml and search forms of the settings and searches pages, D91) or the
    // validation's; these are only the words around them: the steps, what each is for, and the states. Constants only (no
    // static state that changes, ER-37). The guided setup is web-only in v0.3.0 (D79 point 6); its words are here so that
    // CampertronGUI's later guided setup says the same.
    public static class GuidedSetupWording
    {
        public const string Heading = "Guided setup";
        public const string Intro = "Set up Campertron to check your searches every minute and tell you when a campsite opens up, by email or with ntfy. "
            + "Each step saves its own settings file, so you can stop at any time and come back later: guided setup starts again where the saved files say.";
        public const string FullWebUi = "Every page of the web UI stays in the menu above, and guided setup changes only the files its steps name.";
        public const string SkipLabel = "Skip guided setup and use the full web UI";
        public const string LeaveLabel = "Leave guided setup and use the full web UI";
        public const string SkipHint = "Skipping saves and marks nothing. The start page offers guided setup again whenever you want it.";
        public const string StepsName = "Guided setup steps";
        public const int StepCount = 4;

        // ---- the start page ----
        public const string StartUnconfigured = "Campertron is not set up yet: it does not check any search or notify you.";
        public const string StartPartial = "Notifications are not set up yet: Campertron does not notify you of new openings.";
        public const string StartReady = "Your notification settings and a search are ready: the last step turns the monitoring on.";
        public const string StartLink = "Start guided setup";
        public const string ContinueLink = "Continue guided setup";
        public const string ReopenLink = "Open guided setup";

        public static string StartComplete(GuidedSetupMethod Method) => "Notifications are set up: the monitoring checks your enabled searches every minute and notifies you "
            + (Method == GuidedSetupMethod.Email ? "by email" : "with ntfy") + ".";

        // ---- the steps ----
        public static string StepName(GuidedSetupStep Step, GuidedSetupMethod? Method) => Step switch
        {
            GuidedSetupStep.Method => "How you are notified",
            GuidedSetupStep.Notifications => Method == GuidedSetupMethod.Ntfy ? "ntfy settings" : Method == GuidedSetupMethod.Email ? "Email settings" : "Email or ntfy settings",
            GuidedSetupStep.Search => "A search to check",
            GuidedSetupStep.TurnOn => "Review and turn on",
            _ => "Done",
        };

        public static int StepNumber(GuidedSetupStep Step) => Step switch
        {
            GuidedSetupStep.Method => 1,
            GuidedSetupStep.Notifications => 2,
            GuidedSetupStep.Search => 3,
            _ => 4,
        };

        public static string StepOf(GuidedSetupStep Step) => "Step " + StepNumber(Step).ToString(CultureInfo.InvariantCulture) + " of "
            + StepCount.ToString(CultureInfo.InvariantCulture);

        // a step in the list of steps, in words (never by color alone)
        public const string StepDone = "done";
        public const string StepCurrent = "current step";
        public const string StepToDo = "to do";

        // ---- step 1: how you are notified ----
        public const string MethodLegend = "How do you want to be told about new openings?";
        public const string EmailChoice = "By email: Campertron sends an email through your email provider's SMTP server.";
        public const string NtfyChoice = "With ntfy: Campertron sends a push notification to an ntfy topic, which the ntfy app on your phone or computer shows.";
        public const string MethodHint = "You can change this later: the last step saves your choice as outputTo in General.yaml, and the settings pages change it at any time. Nothing is saved on this step.";
        public const string MethodMissing = "Choose email or ntfy to continue.";
        public const string ContinueLabel = "Continue";
        public const string BackLabel = "Back";

        // ---- step 2: the notification settings ----
        public const string EmailHelp = "Enter your email provider's SMTP server and port, the user name and password it signs in with, the sender's address and at least one recipient, then save. "
            + "For the password, a password file (smtpPasswordFile) or the CAMPERTRON_EMAIL_PASSWORD variable keeps it out of Email.yaml.";
        public const string NtfyHelp = "Turn ntfy on (Enabled), enter the server (such as https://ntfy.sh) and a topic, then save. "
            + "Anyone who knows the topic of a public server can read its notifications, so generate a random topic, and subscribe to it in the ntfy app after saving.";
        public const string TestRecommended = "Recommended: send a test after saving, to see that it arrives. It is optional, and guided setup goes on without it.";
        public const string SaveAndContinueLabel = "Save and continue";
        public const string SaveAndContinueHint = "Save and continue saves your changes (nothing when there are none) and goes to the next step once the saved file is ready. "
            + "Check changes shows the problems and the lines Save would write, without saving.";

        public static string NotReady(GuidedSetupMethod Method) => "Warning: to continue, " + GuidedSetupStatus.FileOf(Method) + " must be saved without errors"
            + (Method == GuidedSetupMethod.Ntfy ? " and with ntfy turned on (Enabled)" : string.Empty) + ". The problems are listed with its settings.";

        // ---- step 3: a search ----
        public const string SearchHelp = "Campertron checks the searches that are Enabled (autoRun). Create a search, choose its campground, the dates and the number of people, check Enabled (autoRun) and save it. "
            + "Its other settings (the days of the week and the filters) keep the example's values: change them on the searches page.";
        public const string SearchReadyText = "Info: at least one enabled search is ready to be checked.";
        public const string SearchesHeading = "Your search files";
        public const string NoSearchFiles = "There is no search file yet.";
        public const string SetUpThisSearch = "Set up this search here";
        public const string NewSearchHeading = "Create a search";
        public const string OtherSettingsLink = "All settings of this search, on the searches page";
        public const string OtherSettingsHint = "The days of the week, the matching and the filters are on the searches page; they keep their values here.";
        public const string HiddenChangesWarning = "Warning: this search also has unsaved changes on its searches page. Save writes them too: check them there first.";
        public const string BackToSearches = "All search files of this step";
        public const string CampgroundDataProblem = "The campground data can't be read, so campgrounds can't be listed or checked here. The system page refreshes it (Settings > System); a search can still be set up by its campground's ID.";
        public const string CampgroundDataLink = "Settings > System";
        public const string SearchNotReady = "Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors.";
        // the validation's words name CampertronGUI's tab; in the web UI they mean its page
        public const string TabMeansPage = "Where these words name the Searches tab, in this web UI they mean its Searches page.";

        // ---- step 4: review and turn on ----
        public const string ReviewHeading = "What the monitoring needs";
        public static string TurnOnLabel(GuidedSetupMethod Method) => "Turn the monitoring on (save outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml)";
        public static string TurnOnHelp(GuidedSetupMethod Method) => "This saves outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml and changes nothing else. "
            + "From then on web mode starts the monitoring by itself whenever it starts: it checks your enabled searches every minute and "
            + (Method == GuidedSetupMethod.Email ? "emails new openings (and sends them with ntfy too when Ntfy.yaml turns ntfy on)." : "sends new openings with ntfy.")
            + " The console's email and ntfy mode and the container's headless monitoring use the same setting.";
        public const string NoGeneralYet = "General.yaml does not exist yet: turning the monitoring on creates it with its default settings and outputTo set.";
        public const string GeneralUnsaved = "Error: General.yaml has unsaved changes on its settings page, so nothing was saved here. Save or revert them there first.";
        public const string TurnOnNotReady = "Error: nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first.";
        public const string AlreadyOn = "Info: outputTo in General.yaml is already this method.";

        public static string NotificationsItem(GuidedSetupMethod Method, bool Ready) => (Method == GuidedSetupMethod.Email ? "Email settings (Email.yaml): " : "ntfy settings (Ntfy.yaml): ")
            + (Ready ? "ready." : "not ready.");
        public static string SearchItem(bool Ready) => "A search to check: " + (Ready ? "ready." : "not ready.");
        public static string OutputItem(GuidedSetupMethod Method, OutputType? Saved) => "outputTo in General.yaml: "
            + (Saved == GuidedSetupStatus.OutputOf(Method) ? Saved + ", so the monitoring is turned on."
                : Saved == null ? "not set yet (no General.yaml the monitoring could use)." : Saved + " now; the last step sets it to " + GuidedSetupStatus.OutputOf(Method) + ".");

        // ---- done ----
        public static string DoneHeading(GuidedSetupMethod Method) => Method == GuidedSetupMethod.Email ? "Campertron is set up to email you" : "Campertron is set up to notify you with ntfy";
        public static string DoneText(GuidedSetupMethod Method) => "Your " + (Method == GuidedSetupMethod.Email ? "email" : "ntfy") + " settings, at least one enabled search and outputTo: "
            + GuidedSetupStatus.OutputOf(Method) + " are saved, and the monitoring's checks of the files pass. "
            + "Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on the monitoring page.";
        public const string NotDoneHeading = "Guided setup is not finished";
        public const string NotDoneText = "The saved files don't have everything the monitoring needs yet:";
        public const string MonitoringHeading = "The monitoring";
        public const string MonitoringNotRunning = "It starts by itself whenever web mode starts. To start it now, select Start the monitoring; the monitoring page shows how it goes.";
        public const string MonitoringRunningChanged = "It runs with the settings and search files it read when it started: select Restart on the monitoring page so that it reads your changes.";
        public const string MonitoringPage = "Go to the monitoring page";
        public const string StartPage = "Go to the start page";
        public const string StartMonitoringLabel = "Start the monitoring";

        // the step's own result after an action that leads to it, said once
        public static string Saved(string FileName) => "Info: " + FileName + " was saved.";
        public static IReadOnlyList<string> Steps(GuidedSetupMethod? Method) => new[]
        {
            StepName(GuidedSetupStep.Method, Method), StepName(GuidedSetupStep.Notifications, Method), StepName(GuidedSetupStep.Search, Method), StepName(GuidedSetupStep.TurnOn, Method),
        };
    }
}
