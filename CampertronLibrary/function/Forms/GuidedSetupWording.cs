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
    // Since v0.4.0's phase 3 (D117) these are the words every host of the guided setup says (the orchestration's,
    // GuidedSetupFlow, among them); what only one host says, its pages, menu and links, is that host's (the web UI's
    // WebGuidedSetupWording), and a sentence of the orchestration that only a host knows is its GuidedSetupHostWords.
    public static class GuidedSetupWording
    {
        public const string Heading = "Guided setup";
        public const string Intro = "Set up Campertron to check your searches every minute and tell you when a campsite opens up, by email or with ntfy. "
            + "Each step saves its own settings file, so you can stop at any time and come back later: guided setup starts again where the saved files say.";
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

        // what the start page says of the guided setup, from the saved files, and its link's words (v0.4.0's phase 3 moved
        // the choice from the web UI's start page, unchanged; GuidedSetupFlow.ResumeMethod says where the link leads)
        public static string StartState(GuidedSetupStatus Status) => Status.Overall switch
        {
            GuidedSetupState.Unconfigured => StartUnconfigured,
            GuidedSetupState.Ready => StartReady,
            GuidedSetupState.Complete when Status.SuggestedMethod is GuidedSetupMethod Method => StartComplete(Method),
            _ => StartPartial,
        };
        public static string StartAction(GuidedSetupStatus Status) => Status.Overall switch
        {
            GuidedSetupState.Unconfigured => StartLink,
            GuidedSetupState.Complete => ReopenLink,
            _ => ContinueLink,
        };

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

        // a step's heading: "Step N of 4: " and its name; the done page's says whether the saved files are complete
        public static string StepHeading(GuidedSetupStep Step, GuidedSetupMethod? Method, GuidedSetupStatus Status) => Step == GuidedSetupStep.Done
            ? (Method is GuidedSetupMethod Known && Status.State(Known) == GuidedSetupState.Complete ? DoneHeading(Known) : NotDoneHeading)
            : StepOf(Step) + ": " + StepName(Step, Method);

        // a step in the list of steps, in words (never by color alone)
        public const string StepDone = "done";
        public const string StepCurrent = "current step";
        public const string StepToDo = "to do";
        public static string StepState(GuidedSetupStepState State) => State switch
        {
            GuidedSetupStepState.Done => StepDone,
            GuidedSetupStepState.Current => StepCurrent,
            _ => StepToDo,
        };

        // ---- step 1: how you are notified ----
        public const string MethodLegend = "How do you want to be told about new openings?";
        public const string EmailChoice = "By email: Campertron sends an email through your email provider's SMTP server.";
        public const string NtfyChoice = "With ntfy: Campertron sends a push notification to an ntfy topic, which the ntfy app on your phone or computer shows.";
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

        public static string NotReady(GuidedSetupMethod Method) => "Warning: to continue, " + GuidedSetupStatus.FileOf(Method) + " must be saved with everything the monitoring needs"
            + (Method == GuidedSetupMethod.Ntfy ? ", with ntfy turned on (Enabled)" : string.Empty) + ". As it is saved now, the monitoring would refuse it:";

        // ---- step 3: a search ----
        public const string SearchReadyText = "Info: at least one enabled search is ready to be checked.";
        public const string SearchesHeading = "Your search files";
        public const string NoSearchFiles = "There is no search file yet.";
        public const string SetUpThisSearch = "Set up this search here";
        public const string NewSearchHeading = "Create a search";
        public const string BackToSearches = "All search files of this step";
        public const string SearchNotReady = "Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors.";

        // ---- step 4: review and turn on ----
        public const string ReviewHeading = "What the monitoring needs";
        public const string FilesRefused = "Error: the monitoring would refuse the saved files:";
        public static string TurnOnLabel(GuidedSetupMethod Method) => "Turn the monitoring on (save outputTo: " + GuidedSetupStatus.OutputOf(Method) + " in General.yaml)";
        public const string NoGeneralYet = "General.yaml does not exist yet: turning the monitoring on creates it with its default settings and outputTo set.";
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
        public const string NotDoneHeading = "Guided setup is not finished";
        public const string NotDoneText = "The saved files don't have everything the monitoring needs yet:";
        public const string MonitoringHeading = "The monitoring";
        public const string StartMonitoringLabel = "Start the monitoring";

        // the step's own result after an action that leads to it, said once
        public static string Saved(string FileName) => "Info: " + FileName + " was saved.";
        public static IReadOnlyList<string> Steps(GuidedSetupMethod? Method) => new[]
        {
            StepName(GuidedSetupStep.Method, Method), StepName(GuidedSetupStep.Notifications, Method), StepName(GuidedSetupStep.Search, Method), StepName(GuidedSetupStep.TurnOn, Method),
        };
    }
}
