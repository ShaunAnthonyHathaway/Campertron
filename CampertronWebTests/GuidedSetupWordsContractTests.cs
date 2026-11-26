using CampertronLibrary.function.Forms;

namespace CampertronWebTests
{
    // Every word the web UI's guided setup and the start page's guided setup section show, exactly (v0.4.0's phase 3: the
    // guided setup's orchestration moves into the library and its words are sorted into the words every host shares and the
    // web UI's own, and the web UI must say the same words before and after): the fixed words, each step's name for each
    // method, the step's number, what each method needs, the review's items, the done page's words and what a step says
    // after an action.
    // The binding below names where each word comes from; everything after it holds only the expected words, so a move of
    // the words changes the binding and nothing else (phase 3 moved the web UI's own words to WebGuidedSetupWording and its
    // sentence in the shared orchestration to WebGuidedSetupWording.Host).
    public class GuidedSetupWordsContractTests
    {
        // ---- the binding: where the web UI's words come from ----

        private static readonly GuidedSetupMethod?[] Methods = { null, GuidedSetupMethod.Email, GuidedSetupMethod.Ntfy };
        private static readonly GuidedSetupStep[] AllSteps = { GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn, GuidedSetupStep.Done };

        private static Dictionary<string, string> Words()
        {
            var Found = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Heading"] = GuidedSetupWording.Heading,
                ["Intro"] = GuidedSetupWording.Intro,
                ["FullWebUi"] = WebGuidedSetupWording.FullWebUi,
                ["SkipLabel"] = WebGuidedSetupWording.SkipLabel,
                ["LeaveLabel"] = WebGuidedSetupWording.LeaveLabel,
                ["SkipHint"] = WebGuidedSetupWording.SkipHint,
                ["StepsName"] = GuidedSetupWording.StepsName,
                ["StepCount"] = GuidedSetupWording.StepCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["StartUnconfigured"] = GuidedSetupWording.StartUnconfigured,
                ["StartPartial"] = GuidedSetupWording.StartPartial,
                ["StartReady"] = GuidedSetupWording.StartReady,
                ["StartLink"] = GuidedSetupWording.StartLink,
                ["ContinueLink"] = GuidedSetupWording.ContinueLink,
                ["ReopenLink"] = GuidedSetupWording.ReopenLink,
                ["StepDone"] = GuidedSetupWording.StepDone,
                ["StepCurrent"] = GuidedSetupWording.StepCurrent,
                ["StepToDo"] = GuidedSetupWording.StepToDo,
                ["MethodLegend"] = GuidedSetupWording.MethodLegend,
                ["EmailChoice"] = GuidedSetupWording.EmailChoice,
                ["NtfyChoice"] = GuidedSetupWording.NtfyChoice,
                ["MethodHint"] = WebGuidedSetupWording.MethodHint,
                ["MethodMissing"] = GuidedSetupWording.MethodMissing,
                ["ContinueLabel"] = GuidedSetupWording.ContinueLabel,
                ["BackLabel"] = GuidedSetupWording.BackLabel,
                ["EmailHelp"] = GuidedSetupWording.EmailHelp,
                ["NtfyHelp"] = GuidedSetupWording.NtfyHelp,
                ["TestRecommended"] = GuidedSetupWording.TestRecommended,
                ["SaveAndContinueLabel"] = GuidedSetupWording.SaveAndContinueLabel,
                ["SaveAndContinueHint"] = WebGuidedSetupWording.SaveAndContinueHint,
                ["SearchHelp"] = WebGuidedSetupWording.SearchHelp,
                ["SearchReadyText"] = GuidedSetupWording.SearchReadyText,
                ["SearchesHeading"] = GuidedSetupWording.SearchesHeading,
                ["NoSearchFiles"] = GuidedSetupWording.NoSearchFiles,
                ["SetUpThisSearch"] = GuidedSetupWording.SetUpThisSearch,
                ["NewSearchHeading"] = GuidedSetupWording.NewSearchHeading,
                ["OtherSettingsLink"] = WebGuidedSetupWording.OtherSettingsLink,
                ["OtherSettingsHint"] = WebGuidedSetupWording.OtherSettingsHint,
                ["HiddenChangesWarning"] = WebGuidedSetupWording.HiddenChangesWarning,
                ["BackToSearches"] = GuidedSetupWording.BackToSearches,
                ["CampgroundDataProblem"] = WebGuidedSetupWording.CampgroundDataProblem,
                ["CampgroundDataLink"] = WebGuidedSetupWording.CampgroundDataLink,
                ["SearchNotReady"] = GuidedSetupWording.SearchNotReady,
                ["TabMeansPage"] = WebGuidedSetupWording.TabMeansPage,
                ["ReviewHeading"] = GuidedSetupWording.ReviewHeading,
                ["NoGeneralYet"] = GuidedSetupWording.NoGeneralYet,
                ["GeneralUnsaved"] = WebGuidedSetupWording.Host.GeneralUnsaved,
                ["TurnOnNotReady"] = GuidedSetupWording.TurnOnNotReady,
                ["AlreadyOn"] = GuidedSetupWording.AlreadyOn,
                ["NotDoneHeading"] = GuidedSetupWording.NotDoneHeading,
                ["NotDoneText"] = GuidedSetupWording.NotDoneText,
                ["MonitoringHeading"] = GuidedSetupWording.MonitoringHeading,
                ["MonitoringNotRunning"] = WebGuidedSetupWording.MonitoringNotRunning,
                ["MonitoringRunningChanged"] = WebGuidedSetupWording.MonitoringRunningChanged,
                ["MonitoringPage"] = WebGuidedSetupWording.MonitoringPage,
                ["StartPage"] = WebGuidedSetupWording.StartPage,
                ["StartMonitoringLabel"] = GuidedSetupWording.StartMonitoringLabel,
                ["Saved(Email.yaml)"] = GuidedSetupWording.Saved("Email.yaml"),
                ["Saved(General.yaml)"] = GuidedSetupWording.Saved("General.yaml"),
                ["SearchItem(true)"] = GuidedSetupWording.SearchItem(true),
                ["SearchItem(false)"] = GuidedSetupWording.SearchItem(false),
            };
            foreach (GuidedSetupStep Step in AllSteps)
            {
                Found["StepNumber(" + Step + ")"] = GuidedSetupWording.StepNumber(Step).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Found["StepOf(" + Step + ")"] = GuidedSetupWording.StepOf(Step);
                foreach (GuidedSetupMethod? Method in Methods)
                {
                    Found["StepName(" + Step + "," + (Method?.ToString() ?? "none") + ")"] = GuidedSetupWording.StepName(Step, Method);
                }
            }
            foreach (GuidedSetupMethod? Method in Methods)
            {
                Found["Steps(" + (Method?.ToString() ?? "none") + ")"] = string.Join(" | ", GuidedSetupWording.Steps(Method));
            }
            foreach (GuidedSetupMethod Method in new[] { GuidedSetupMethod.Email, GuidedSetupMethod.Ntfy })
            {
                string M = Method.ToString();
                Found["StartComplete(" + M + ")"] = GuidedSetupWording.StartComplete(Method);
                Found["NotReady(" + M + ")"] = GuidedSetupWording.NotReady(Method);
                Found["TurnOnLabel(" + M + ")"] = GuidedSetupWording.TurnOnLabel(Method);
                Found["TurnOnHelp(" + M + ")"] = WebGuidedSetupWording.TurnOnHelp(Method);
                Found["NotificationsItem(" + M + ",true)"] = GuidedSetupWording.NotificationsItem(Method, true);
                Found["NotificationsItem(" + M + ",false)"] = GuidedSetupWording.NotificationsItem(Method, false);
                Found["DoneHeading(" + M + ")"] = GuidedSetupWording.DoneHeading(Method);
                Found["DoneText(" + M + ")"] = WebGuidedSetupWording.DoneText(Method);
                foreach (OutputType? Saved in new OutputType?[] { null, OutputType.Console, OutputType.Email, OutputType.Ntfy })
                {
                    Found["OutputItem(" + M + "," + (Saved?.ToString() ?? "none") + ")"] = GuidedSetupWording.OutputItem(Method, Saved);
                }
            }
            return Found;
        }

        // ---- the expected words (nothing below names where a word comes from) ----

        [Test]
        public void EveryWordOfTheGuidedSetup_IsTheSameWord()
        {
            Dictionary<string, string> Found = Words();
            Dictionary<string, string> Wanted = Expected();
            Assert.That(Found.Keys.Order(StringComparer.Ordinal), Is.EqualTo(Wanted.Keys.Order(StringComparer.Ordinal)), "the same words are bound");
            foreach (KeyValuePair<string, string> Word in Wanted)
            {
                Assert.That(Found[Word.Key], Is.EqualTo(Word.Value), Word.Key);
            }
        }

        private static Dictionary<string, string> Expected() => new(StringComparer.Ordinal)
        {
            ["AlreadyOn"] = "Info: outputTo in General.yaml is already this method.",
            ["BackLabel"] = "Back",
            ["BackToSearches"] = "All search files of this step",
            ["CampgroundDataLink"] = "Settings > System",
            ["CampgroundDataProblem"] = "The campground data can't be read, so campgrounds can't be listed or checked here. The system page refreshes it (Settings > System); a search can still be set up by its campground's ID.",
            ["ContinueLabel"] = "Continue",
            ["ContinueLink"] = "Continue guided setup",
            ["DoneHeading(Email)"] = "Campertron is set up to email you",
            ["DoneHeading(Ntfy)"] = "Campertron is set up to notify you with ntfy",
            ["DoneText(Email)"] = "Your email settings, at least one enabled search and outputTo: Email are saved, and the monitoring's checks of the files pass. Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on the monitoring page.",
            ["DoneText(Ntfy)"] = "Your ntfy settings, at least one enabled search and outputTo: Ntfy are saved, and the monitoring's checks of the files pass. Campertron notifies each new opening once; it can't promise that a message arrives, so keep an eye on the monitoring page.",
            ["EmailChoice"] = "By email: Campertron sends an email through your email provider's SMTP server.",
            ["EmailHelp"] = "Enter your email provider's SMTP server and port, the user name and password it signs in with, the sender's address and at least one recipient, then save. For the password, a password file (smtpPasswordFile) or the CAMPERTRON_EMAIL_PASSWORD variable keeps it out of Email.yaml.",
            ["FullWebUi"] = "Every page of the web UI stays in the menu above, and guided setup changes only the files its steps name.",
            ["GeneralUnsaved"] = "Error: General.yaml has unsaved changes on its settings page, so nothing was saved here. Save or revert them there first.",
            ["Heading"] = "Guided setup",
            ["HiddenChangesWarning"] = "Warning: this search also has unsaved changes on its searches page. Save writes them too: check them there first.",
            ["Intro"] = "Set up Campertron to check your searches every minute and tell you when a campsite opens up, by email or with ntfy. Each step saves its own settings file, so you can stop at any time and come back later: guided setup starts again where the saved files say.",
            ["LeaveLabel"] = "Leave guided setup and use the full web UI",
            ["MethodHint"] = "You can change this later: the last step saves your choice as outputTo in General.yaml, and the settings pages change it at any time. Nothing is saved on this step.",
            ["MethodLegend"] = "How do you want to be told about new openings?",
            ["MethodMissing"] = "Choose email or ntfy to continue.",
            ["MonitoringHeading"] = "The monitoring",
            ["MonitoringNotRunning"] = "It starts by itself whenever web mode starts. To start it now, select Start the monitoring; the monitoring page shows how it goes.",
            ["MonitoringPage"] = "Go to the monitoring page",
            ["MonitoringRunningChanged"] = "It runs with the settings and search files it read when it started: select Restart on the monitoring page so that it reads your changes.",
            ["NewSearchHeading"] = "Create a search",
            ["NoGeneralYet"] = "General.yaml does not exist yet: turning the monitoring on creates it with its default settings and outputTo set.",
            ["NoSearchFiles"] = "There is no search file yet.",
            ["NotDoneHeading"] = "Guided setup is not finished",
            ["NotDoneText"] = "The saved files don't have everything the monitoring needs yet:",
            ["NotReady(Email)"] = "Warning: to continue, Email.yaml must be saved with everything the monitoring needs. As it is saved now, the monitoring would refuse it:",
            ["NotReady(Ntfy)"] = "Warning: to continue, Ntfy.yaml must be saved with everything the monitoring needs, with ntfy turned on (Enabled). As it is saved now, the monitoring would refuse it:",
            ["NotificationsItem(Email,false)"] = "Email settings (Email.yaml): not ready.",
            ["NotificationsItem(Email,true)"] = "Email settings (Email.yaml): ready.",
            ["NotificationsItem(Ntfy,false)"] = "ntfy settings (Ntfy.yaml): not ready.",
            ["NotificationsItem(Ntfy,true)"] = "ntfy settings (Ntfy.yaml): ready.",
            ["NtfyChoice"] = "With ntfy: Campertron sends a push notification to an ntfy topic, which the ntfy app on your phone or computer shows.",
            ["NtfyHelp"] = "Turn ntfy on (Enabled), enter the server (such as https://ntfy.sh) and a topic, then save. Anyone who knows the topic of a public server can read its notifications, so generate a random topic, and subscribe to it in the ntfy app after saving.",
            ["OtherSettingsHint"] = "The days of the week, the matching and the filters are on the searches page; they keep their values here.",
            ["OtherSettingsLink"] = "All settings of this search, on the searches page",
            ["OutputItem(Email,Console)"] = "outputTo in General.yaml: Console now; the last step sets it to Email.",
            ["OutputItem(Email,Email)"] = "outputTo in General.yaml: Email, so the monitoring is turned on.",
            ["OutputItem(Email,Ntfy)"] = "outputTo in General.yaml: Ntfy now; the last step sets it to Email.",
            ["OutputItem(Email,none)"] = "outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).",
            ["OutputItem(Ntfy,Console)"] = "outputTo in General.yaml: Console now; the last step sets it to Ntfy.",
            ["OutputItem(Ntfy,Email)"] = "outputTo in General.yaml: Email now; the last step sets it to Ntfy.",
            ["OutputItem(Ntfy,Ntfy)"] = "outputTo in General.yaml: Ntfy, so the monitoring is turned on.",
            ["OutputItem(Ntfy,none)"] = "outputTo in General.yaml: not set yet (no General.yaml the monitoring could use).",
            ["ReopenLink"] = "Open guided setup",
            ["ReviewHeading"] = "What the monitoring needs",
            ["SaveAndContinueHint"] = "Save and continue saves your changes (nothing when there are none) and goes to the next step once the saved file is ready. Check changes shows the problems and the lines Save would write, without saving.",
            ["SaveAndContinueLabel"] = "Save and continue",
            ["Saved(Email.yaml)"] = "Info: Email.yaml was saved.",
            ["Saved(General.yaml)"] = "Info: General.yaml was saved.",
            ["SearchHelp"] = "Campertron checks the searches that are Enabled (autoRun). Create a search, choose its campground, the dates and the number of people, check Enabled (autoRun) and save it. Its other settings (the days of the week and the filters) keep the example's values: change them on the searches page.",
            ["SearchItem(false)"] = "A search to check: not ready.",
            ["SearchItem(true)"] = "A search to check: ready.",
            ["SearchNotReady"] = "Warning: to continue, at least one search must be Enabled (autoRun), have a campground and be saved without errors.",
            ["SearchReadyText"] = "Info: at least one enabled search is ready to be checked.",
            ["SearchesHeading"] = "Your search files",
            ["SetUpThisSearch"] = "Set up this search here",
            ["SkipHint"] = "Skipping or leaving saves and marks nothing. The start page offers guided setup again whenever you want it.",
            ["SkipLabel"] = "Skip guided setup and use the full web UI",
            ["StartComplete(Email)"] = "Notifications are set up: the monitoring checks your enabled searches every minute and notifies you by email.",
            ["StartComplete(Ntfy)"] = "Notifications are set up: the monitoring checks your enabled searches every minute and notifies you with ntfy.",
            ["StartLink"] = "Start guided setup",
            ["StartMonitoringLabel"] = "Start the monitoring",
            ["StartPage"] = "Go to the start page",
            ["StartPartial"] = "Notifications are not set up yet: Campertron does not notify you of new openings.",
            ["StartReady"] = "Your notification settings and a search are ready: the last step turns the monitoring on.",
            ["StartUnconfigured"] = "Campertron is not set up yet: it does not check any search or notify you.",
            ["StepCount"] = "4",
            ["StepCurrent"] = "current step",
            ["StepDone"] = "done",
            ["StepName(Done,Email)"] = "Done",
            ["StepName(Done,Ntfy)"] = "Done",
            ["StepName(Done,none)"] = "Done",
            ["StepName(Method,Email)"] = "How you are notified",
            ["StepName(Method,Ntfy)"] = "How you are notified",
            ["StepName(Method,none)"] = "How you are notified",
            ["StepName(Notifications,Email)"] = "Email settings",
            ["StepName(Notifications,Ntfy)"] = "ntfy settings",
            ["StepName(Notifications,none)"] = "Email or ntfy settings",
            ["StepName(Search,Email)"] = "A search to check",
            ["StepName(Search,Ntfy)"] = "A search to check",
            ["StepName(Search,none)"] = "A search to check",
            ["StepName(TurnOn,Email)"] = "Review and turn on",
            ["StepName(TurnOn,Ntfy)"] = "Review and turn on",
            ["StepName(TurnOn,none)"] = "Review and turn on",
            ["StepNumber(Done)"] = "4",
            ["StepNumber(Method)"] = "1",
            ["StepNumber(Notifications)"] = "2",
            ["StepNumber(Search)"] = "3",
            ["StepNumber(TurnOn)"] = "4",
            ["StepOf(Done)"] = "Step 4 of 4",
            ["StepOf(Method)"] = "Step 1 of 4",
            ["StepOf(Notifications)"] = "Step 2 of 4",
            ["StepOf(Search)"] = "Step 3 of 4",
            ["StepOf(TurnOn)"] = "Step 4 of 4",
            ["StepToDo"] = "to do",
            ["Steps(Email)"] = "How you are notified | Email settings | A search to check | Review and turn on",
            ["Steps(Ntfy)"] = "How you are notified | ntfy settings | A search to check | Review and turn on",
            ["Steps(none)"] = "How you are notified | Email or ntfy settings | A search to check | Review and turn on",
            ["StepsName"] = "Guided setup steps",
            ["TabMeansPage"] = "Where these words name the Searches tab, in this web UI they mean its Searches page.",
            ["TestRecommended"] = "Recommended: send a test after saving, to see that it arrives. It is optional, and guided setup goes on without it.",
            ["TurnOnHelp(Email)"] = "This saves outputTo: Email in General.yaml and changes nothing else. From then on web mode starts the monitoring by itself whenever it starts: it checks your enabled searches every minute and emails new openings (and sends them with ntfy too when Ntfy.yaml turns ntfy on). The console's email and ntfy mode and the container's headless monitoring use the same setting.",
            ["TurnOnHelp(Ntfy)"] = "This saves outputTo: Ntfy in General.yaml and changes nothing else. From then on web mode starts the monitoring by itself whenever it starts: it checks your enabled searches every minute and sends new openings with ntfy. The console's email and ntfy mode and the container's headless monitoring use the same setting.",
            ["TurnOnLabel(Email)"] = "Turn the monitoring on (save outputTo: Email in General.yaml)",
            ["TurnOnLabel(Ntfy)"] = "Turn the monitoring on (save outputTo: Ntfy in General.yaml)",
            ["TurnOnNotReady"] = "Error: nothing was saved: the monitoring would refuse the saved files. Fix the problems listed first.",
        };
    }
}
