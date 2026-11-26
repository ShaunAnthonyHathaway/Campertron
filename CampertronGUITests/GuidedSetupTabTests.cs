using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's guided setup (v0.4.0's phase 5; business-rules-gui §42) in the real window on the headless platform:
    // Monitoring > Guided setup hosts the shared GuidedSetupFlow over the window's own Settings and Searches forms and the
    // application's monitoring. Where the user is comes from the saved files only; every save is the form's own; nothing
    // starts the monitoring but the done step's Start the monitoring, the user's choice; leaving saves, discards and stops
    // nothing. Recreation.gov, email and ntfy are the monitoring tests' in-memory stand-ins (DesktopMonitoringTestBase): nothing
    // reaches the network.
    public class GuidedSetupTabTests : DesktopMonitoringTestBase
    {
        private const string Console = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n";

        // nothing of the guided setup saved: outputTo Console, no Email.yaml or Ntfy.yaml the monitoring would use, no search
        private void Unconfigured()
        {
            Folders.Write("General.yaml", Console);
            File.Delete(Folders.In("Email.yaml"));
        }

        // a complete Email.yaml (the monitoring tests' own, with its plain-text password) and outputTo Console, no search
        private void EmailReadyOnly() => Folders.Write("General.yaml", Console);

        private static GuidedSetupView View(MainWindow Window) => Named<GuidedSetupView>(Window, "GuidedSetupContent");

        private static T InGuided<T>(MainWindow Window, string Name) where T : Control =>
            View(Window).FindControl<T>(Name) ?? throw new AssertionException(Name + " not found in the guided setup");

        // a control of an editor the guided setup shows (its own name scope), while it is shown
        private static T InEditor<T>(Control Editor, string Name) where T : Control =>
            Editor.GetVisualDescendants().OfType<T>().FirstOrDefault(C => C.Name == Name) ?? throw new AssertionException(Name + " not found in " + Editor.Name);

        private static TabControl MonitoringRow(MainWindow Window) => Named<TabControl>(Window, "MonitoringTabControl");

        private static GuidedSetupViewModel ShowGuided(MainWindow Window, MainWindowViewModel ViewModel)
        {
            MonitoringRow(Window).SelectedIndex = MainWindow.GuidedSetupTabIndex;
            Tabs(Window).SelectedIndex = MainWindow.MonitoringTabIndex;
            Settle(Window);
            return ViewModel.GuidedSetup!;
        }

        private static void Run(System.Windows.Input.ICommand Command, MainWindow Window)
        {
            Assert.That(Command.CanExecute(null), Is.True, "the action is available");
            Command.Execute(null);
            Settle(Window);
        }

        private static string? FocusedName(MainWindow Window) => (Window.FocusManager?.GetFocusedElement() as Control)?.Name;

        // the email form filled as a user would: the server, the port, the user, the password, the sender and one recipient
        private static void FillEmail(EmailSettingsViewModel Email)
        {
            Email.SmtpServer.Text = "smtp.example.invalid";
            Email.SmtpPort.Text = "587";
            Email.SmtpUsername.Text = "camper";
            Email.SmtpPassword.Text = ResultsFolders.Sentinel;
            Email.SendFromAddress.Text = "camper@example.invalid";
            if (Email.Recipients.Rows.Count == 0)
            {
                Email.AddRecipientCommand.Execute(null);
            }
            Email.Recipients.Rows[0].Text = "first@example.invalid";
        }

        // ---- the lead and the entry (ER-40 for the desktop GUI) ----

        // A start while nothing of the guided setup is saved opens at Monitoring > Guided setup, at step 1, with its heading in
        // focus, once: nothing is read but the saved files and nothing is written; the Searches tab was shown to its view model
        // when the startup was ready, as always
        [AvaloniaTest]
        public void AnUnconfiguredFolder_IsLedOnceToTheGuidedSetup_AtItsFirstStep()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow();
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            // the lead and the first step wrote nothing: the folder has the test's General.yaml only (and the database)
            Assert.That(Directory.GetFiles(Folders.Config, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml" }));
            Assert.That(File.ReadAllText(Folders.In("General.yaml")), Is.EqualTo(Console));
            Assert.That(ViewModel.GuidedSetupLed, Is.True);
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.MonitoringTabIndex));
            Assert.That(MonitoringRow(Window).SelectedIndex, Is.EqualTo(MainWindow.GuidedSetupTabIndex));
            GuidedSetupViewModel Guided = ViewModel.GuidedSetup!;
            Assert.That(Guided.IsShown, Is.True);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Method));
            Assert.That(Guided.Heading, Is.EqualTo("Step 1 of 4: How you are notified"));
            Assert.That(Guided.ReadinessText, Is.EqualTo(GuidedSetupWording.StartUnconfigured));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedStepHeading"), "the step's heading has the focus");
            Assert.That(ViewModel.Searches!.IsLoaded, Is.True, "the first tab was shown to its view model at the start");
            Assert.That(ViewModel.TakeGuidedSetupLead(), Is.False, "once per start");
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SearchesTabIndex), "no second lead");
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "the lead and the step write nothing");
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.Zero, "nothing starts the monitoring");
            Window.Close();
        }

        // anything saved (a search file, a ready notification file, outputTo Email) and the window opens at Searches, as before
        [AvaloniaTest]
        public void AFolderWithAnythingSaved_IsNotLed([Values("search", "email", "output")] string Saved)
        {
            Unconfigured();
            switch (Saved)
            {
                case "search":
                    WriteSearch(AutoRun: false);
                    break;
                case "email":
                    Folders.Write("Email.yaml", "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + ResultsFolders.Sentinel
                        + "\nsendToAddressList:\n  - someone@example.invalid\nsendFromAddress: someone@example.invalid\n");
                    break;
                default:
                    Folders.Write("General.yaml", Console.Replace("outputTo: Console", "outputTo: Email"));
                    break;
            }
            var (Window, ViewModel) = ShowWindow();
            Assert.That(ViewModel.GuidedSetupLed, Is.False);
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SearchesTabIndex), "Searches is shown at the start");
            Assert.That(ViewModel.GuidedSetup!.IsShown, Is.False);
            Window.Close();
        }

        // a process the automatic restart after a refresh started (D62) opens at Settings > System, never at the guided setup
        [AvaloniaTest]
        public void ARestartedProcess_IsNotLed()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow(Arrival: new RestartHandoff(DateTime.UtcNow, RestartOutcome.Refreshed, null, RestartTopTab.Monitoring, RestartSettingsTab.Campertron));
            Assert.That(ViewModel.GuidedSetupLed, Is.False);
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex));
            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SystemSettingsTabIndex));
            Window.Close();
        }

        // Opened by hand, the guided setup resumes where the saved files say (the shared ResumeMethod and the method's first step
        // not done): a ready Email.yaml goes on at the search; a complete setup opens again at step 1 with its method checked
        [AvaloniaTest]
        public void ManualOpening_ResumesWhereTheSavedFilesSay([Values("email-ready", "complete")] string Saved)
        {
            if (Saved == "email-ready")
            {
                EmailReadyOnly();
            }
            else
            {
                WriteSearch();
            }
            var (Window, ViewModel) = ShowWindow();
            Assert.That(ViewModel.GuidedSetupLed, Is.False);
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            if (Saved == "email-ready")
            {
                Assert.That(Guided.Method, Is.EqualTo(GuidedSetupMethod.Email));
                Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search), "the notification step is done; the search is not");
                Assert.That(Guided.Steps.Select(S => S.StateText), Is.EqualTo(new[] { "done", "done", "current step", "to do" }));
                Assert.That(Guided.ReadinessText, Is.EqualTo(GuidedSetupWording.StartPartial));
            }
            else
            {
                Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Method), "a complete setup opens again at its first step");
                Assert.That(Guided.IsEmailChosen, Is.True, "with the saved method checked");
                Assert.That(Guided.ReadinessText, Is.EqualTo(GuidedSetupWording.StartComplete(GuidedSetupMethod.Email)));
            }
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "opening it writes nothing");
            Window.Close();
        }

        // ---- step 1 ----

        [AvaloniaTest]
        public void Step1_NothingChosen_IsSaid_AndAChoiceLeadsToItsFirstStepNotDone_SavingNothing()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ViewModel.GuidedSetup!;
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Method));
            Assert.That(Guided.ChoiceError, Is.EqualTo(GuidedSetupWording.MethodMissing));
            SeverityBlock Choice = InGuided<SeverityBlock>(Window, "GuidedChoiceBlock");
            Assert.That(Choice.IsVisible, Is.True);
            Assert.That(Choice.EffectiveSeverity, Is.EqualTo(SeverityBlock.Error), "an error, in words and its icon, not color alone");
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedChoiceBlock"), "the problem has the focus");
            InGuided<RadioButton>(Window, "GuidedNtfyChoice").IsChecked = true;
            Settle(Window);
            Assert.That(Guided.ChoiceError, Is.Null, "a choice clears it");
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Method, Is.EqualTo(GuidedSetupMethod.Ntfy));
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(Guided.Heading, Is.EqualTo("Step 2 of 4: ntfy settings"));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedStepHeading"));
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "step 1 saves nothing");
            Window.Close();
        }

        // ---- step 2 ----

        // The notification step shows the Settings tab's own editor of the window's own form (never a second session of the
        // file), with Save and continue where Save is; Save and continue saves through the form's own Save, once, and goes on to
        // the search once the saved file is what the monitoring needs
        [AvaloniaTest]
        public void Step2_IsTheSettingsTabsEditor_AndSaveAndContinueSavesThroughTheForm_Once()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ViewModel.GuidedSetup!;
            Guided.IsEmailChosen = true;
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications));
            EmailSettingsView Editor = InGuided<EmailSettingsView>(Window, "GuidedEmailEditor");
            Assert.That(Editor.DataContext, Is.SameAs(ViewModel.Settings!.Email), "the window's own form");
            Assert.That(Editor.IsEffectivelyVisible, Is.True);
            Assert.That(InGuided<NtfySettingsView>(Window, "GuidedNtfyEditor").IsEffectivelyVisible, Is.False);
            Button Continue = InEditor<Button>(Editor, "ContinueButton");
            Assert.That(Continue.IsEffectivelyVisible, Is.True);
            Assert.That(Continue.Command, Is.SameAs(Guided.SaveNotificationsCommand), "Save and continue is the guided setup's");
            Assert.That(Continue.Content, Is.EqualTo(GuidedSetupWording.SaveAndContinueLabel));
            Assert.That(InEditor<Button>(Editor, "SaveButton").IsVisible, Is.False, "Save and continue stands where Save is");
            Assert.That(Guided.HasNeeds, Is.True, "what the monitoring would refuse in the saved file is said");
            Assert.That(Guided.NeedsText, Does.StartWith(GuidedSetupWording.NotReady(GuidedSetupMethod.Email)));
            Assert.That(File.Exists(Folders.In("Email.yaml")), Is.False);

            // the window's form has no file yet: Email.yaml is created by the form's own Create, then filled
            Assert.That(ViewModel.Settings.Email.IsMissing, Is.True);
            ViewModel.Settings.Email.CreateCommand.Execute(null);
            FillEmail(ViewModel.Settings.Email);
            Settle(Window);
            Assert.That(ViewModel.Settings.Email.IsDirty, Is.True);
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search), "the saved file is ready: on to the search");
            Assert.That(ViewModel.Settings.Email.IsDirty, Is.False, "saved by the form");
            Assert.That(File.ReadAllText(Folders.In("Email.yaml")), Does.Contain("smtp.example.invalid").And.Contain("first@example.invalid"));
            Assert.That(Guided.Notice, Does.StartWith("Info: "), "the form's result, said once on the next step");
            Assert.That(Guided.SaveNotificationsCommand.CanExecute(null), Is.False, "a second activation saves nothing");
            Dictionary<string, string> After = Fingerprint(Folders.Config);
            Guided.SaveNotificationsCommand.Execute(null);
            Settle(Window);
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(After), "the double activation wrote nothing");
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.Zero, "nothing started the monitoring");
            // the Settings tab's editor of the same form is unchanged: Save, no Save and continue
            ShowSettingsTab(Window);
            ViewModel.Settings.IsEmailSelected = true;
            Settle(Window);
            EmailSettingsView SettingsEditor = Named<SettingsView>(Window, "SettingsContent").FindControl<EmailSettingsView>("EmailEditor")!;
            Assert.That(SettingsEditor.DataContext, Is.SameAs(ViewModel.Settings.Email));
            Assert.That(InEditor<Button>(SettingsEditor, "SaveButton").IsVisible, Is.True);
            Assert.That(InEditor<Button>(SettingsEditor, "ContinueButton").IsVisible, Is.False);
            Assert.That(InEditor<Button>(SettingsEditor, "ContinueButton").Command, Is.Null);
            Window.Close();
        }

        // A value with its own error is never saved: the step stays, its problems in the form's words (FormProblems) have the
        // focus, nothing is written; corrected, Save and continue goes on (a retry)
        [AvaloniaTest]
        public void Step2_AProblem_KeepsTheStep_AndItsCorrectionGoesOn()
        {
            EmailReadyOnly();
            File.Delete(Folders.In("Email.yaml"));
            Folders.Write("Email.yaml", "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: " + ResultsFolders.Sentinel
                + "\nsendToAddressList: []\nsendFromAddress: camper@example.invalid\n");
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Guided.IsEmailChosen = true;
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications), "no recipient: the step is not done");
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            ViewModel.Settings!.Email.SmtpPort.Text = "not a port";
            Settle(Window);
            Assert.That(Guided.HasProblems, Is.True);
            Assert.That(Guided.ProblemsText, Is.EqualTo(string.Join(Environment.NewLine, FormProblems.Of(ViewModel.Settings.Email).Select(P => P.Text))), "the form's own problems");
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications), "a failed save stays");
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "nothing was written");
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedProblemsBlock"));
            Assert.That(InGuided<SeverityBlock>(Window, "GuidedProblemsBlock").EffectiveSeverity, Is.EqualTo(SeverityBlock.Error));
            // corrected, but the saved file still lacks a recipient: saved, and the step says what the monitoring needs
            ViewModel.Settings.Email.SmtpPort.Text = "587";
            ViewModel.Settings.Email.SmtpUsername.Text = "camper2";
            Settle(Window);
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(File.ReadAllText(Folders.In("Email.yaml")), Does.Contain("camper2"), "the change was saved by the form");
            Assert.That(Guided.HasNeeds, Is.True);
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNeedsBlock"), "what the monitoring needs has the focus");
            // the retry with a recipient goes on
            FillEmail(ViewModel.Settings.Email);
            Settle(Window);
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Window.Close();
        }

        // a change on disk under unsaved edits is the form's conflict: the step stays with the conflict in focus, nothing saved
        [AvaloniaTest]
        public void Step2_AConflictWithTheDisk_IsTheForms_AndNothingIsSaved()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Guided.GoToNotificationsCommand.Execute(null);
            Settle(Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications));
            ViewModel.Settings!.Email.SmtpUsername.Text = "mine";
            Folders.Write("Email.yaml", File.ReadAllText(Folders.In("Email.yaml")).Replace("smtpUsername: someone", "smtpUsername: theirs"));
            File.SetLastWriteTimeUtc(Folders.In("Email.yaml"), DateTime.UtcNow.AddMinutes(1));
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Notifications));
            Assert.That(ViewModel.Settings.Email.HasConflict, Is.True);
            Assert.That(ViewModel.Settings.Email.StatusIsError, Is.True, "the form's save did not go through");
            Assert.That(File.ReadAllText(Folders.In("Email.yaml")), Does.Contain("theirs").And.Not.Contain("mine"), "the other program's file is kept");
            // the flow's order: the form's failed save first (its result says why), then its conflict
            Assert.That(FocusedName(Window), Is.EqualTo("StatusBlock"), "the form's result has the focus");
            Assert.That(InEditor<SeverityBlock>(InGuided<EmailSettingsView>(Window, "GuidedEmailEditor"), "ConflictBlock").IsEffectivelyVisible, Is.True, "the conflict's choices are shown");
            Window.Close();
        }

        // ntfy: the Settings tab's editor with its random topic and its test, the form's own; its way to General.yaml is the
        // last step's, so the guided editor leaves that button out (the Settings tab's keeps it)
        [AvaloniaTest]
        public void Step2_Ntfy_TheRandomTopicAndTheTestAreTheFormsOwn()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ViewModel.GuidedSetup!;
            Guided.IsNtfyChosen = true;
            Run(Guided.ContinueCommand, Window);
            NtfySettingsView Editor = InGuided<NtfySettingsView>(Window, "GuidedNtfyEditor");
            NtfySettingsViewModel Ntfy = ViewModel.Settings!.Ntfy;
            Assert.That(Editor.DataContext, Is.SameAs(Ntfy));
            Assert.That(InEditor<Button>(Editor, "GenerateTopicButton").Command, Is.SameAs(Ntfy.GenerateTopicCommand));
            Assert.That(InEditor<Button>(Editor, "NtfyTestButton").Command, Is.SameAs(Ntfy.Test.SendCommand));
            Assert.That(Editor.FindControl<Button>("ShowGeneralButton")!.IsVisible, Is.False);
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            Run(Ntfy.GenerateTopicCommand, Window);
            Assert.That(Ntfy.Topic.IsChanged, Is.True, "a random topic is an unsaved change");
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "nothing written");
            Ntfy.Enabled.IsChecked = true;
            Ntfy.Server.Text = "https://ntfy.example.invalid";
            Settle(Window);
            Run(Guided.SaveNotificationsCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(File.Exists(Folders.In("Ntfy.yaml")), Is.True, "the form's save created Ntfy.yaml");
            Assert.That(File.Exists(Folders.In("Ntfy.yaml.bak")), Is.False, "a new file has no backup");
            ShowSettingsTab(Window);
            ViewModel.Settings.IsNtfySelected = true;
            Settle(Window);
            NtfySettingsView SettingsEditor = Named<SettingsView>(Window, "SettingsContent").FindControl<NtfySettingsView>("NtfyEditor")!;
            Assert.That(SettingsEditor.FindControl<Button>("ShowGeneralButton")!.IsVisible, Is.True, "the Settings tab's editor keeps it");
            Window.Close();
        }

        // the test email is the form's own (the saved file, on the user's action only; the send through the monitoring tests'
        // recording transport), and the guided setup goes on without it
        [AvaloniaTest]
        public void Step2_TheTestEmail_IsTheFormsOwnTest()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Guided.GoToNotificationsCommand.Execute(null);
            Settle(Window);
            EmailSettingsView Editor = InGuided<EmailSettingsView>(Window, "GuidedEmailEditor");
            Button Test = InEditor<Button>(Editor, "EmailTestButton");
            Assert.That(Test.Command, Is.SameAs(ViewModel.Settings!.Email.Test.SendCommand));
            Assert.That(Emails, Is.Empty, "nothing is sent until the user asks");
            Run(Test.Command!, Window);
            WaitUntil(() => ViewModel.Settings.Email.Test.HasResult, () => "the test's result");
            Assert.That(Emails.Count, Is.EqualTo(1), "one test email");
            Assert.That(ViewModel.Settings.Email.Test.ResultText, Does.Not.Contain(ResultsFolders.Sentinel));
            Assert.That(Directory.Exists(Folders.In("Camp.History")), Is.False, "a test never touches Camp.History");
            Window.Close();
        }

        // ---- step 3 ----

        // New search is the Searches tab's own command, through the flow: one file, its own page with the form's message; the
        // page is the Searches tab's editor of the same file with its main settings only; a retry with the name refuses
        [AvaloniaTest]
        public void Step3_NewSearch_CreatesOneFile_AndARetryDuplicatesNothing()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(Guided.IsSearchListStep, Is.True);
            Assert.That(Guided.HasNoSearchFiles, Is.True);
            Assert.That(Guided.SearchStateText, Does.StartWith(GuidedSetupWording.SearchNotReady));
            InGuided<TextBox>(Window, "GuidedNewSearchNameBox").Text = "Lake trip";
            Settle(Window);
            Assert.That(ViewModel.Searches!.NewSearchName, Is.EqualTo("Lake trip"), "the Searches tab's own form");
            Run(InGuided<Button>(Window, "GuidedNewSearchButton").Command!, Window);
            Assert.That(ConfigFiles.SortedSearchFileNames(Folders.Config), Is.EqualTo(new[] { "Lake trip.yaml" }));
            Assert.That(Guided.IsSearchStep, Is.True, "the new search's own page");
            Assert.That(Guided.Search, Is.SameAs(ViewModel.Searches.Files.Single()), "the window's own form of it");
            Assert.That(ViewModel.Searches.SelectedFile, Is.SameAs(Guided.Search));
            Assert.That(Guided.CreatedText, Is.EqualTo(ViewModel.Searches.NewSearchMessage));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedCreatedBlock"));
            Assert.That(Guided.NewSearchCommand.CanExecute(null), Is.False, "a second activation creates nothing");
            SearchFileView Editor = InGuided<SearchFileView>(Window, "GuidedSearchEditor");
            Assert.That(Editor.DataContext, Is.SameAs(Guided.Search));
            Assert.That(InEditor<Border>(Editor, "WeekdaysCard").IsVisible, Is.False, "the days of the week stay in the Searches tab");
            Assert.That(InEditor<Border>(Editor, "FiltersCard").IsVisible, Is.False, "the filters stay in the Searches tab");
            Assert.That(InEditor<Border>(Editor, "CampgroundCard").IsEffectivelyVisible, Is.True);
            Assert.That(InEditor<Border>(Editor, "DatesCard").IsEffectivelyVisible, Is.True);
            Assert.That(InEditor<Button>(Editor, "ContinueButton").Command, Is.SameAs(Guided.SaveSearchCommand));
            // a retry with the same name: the form refuses it, one file only
            Run(Guided.BackToSearchesCommand, Window);
            Assert.That(Guided.IsSearchListStep, Is.True);
            Assert.That(Guided.SearchItems.Select(I => I.File.FileName), Is.EqualTo(new[] { "Lake trip.yaml" }));
            Assert.That(Guided.SearchItems[0].ActionName, Is.EqualTo(GuidedSetupWording.SetUpThisSearch + ": Lake trip.yaml"));
            ViewModel.Searches.NewSearchName = "Lake trip";
            Run(Guided.NewSearchCommand, Window);
            Assert.That(ViewModel.Searches.NewSearchIsError, Is.True);
            Assert.That(Guided.IsSearchListStep, Is.True, "a refused name stays");
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNewSearchBlock"));
            Assert.That(ConfigFiles.SortedSearchFileNames(Folders.Config), Is.EqualTo(new[] { "Lake trip.yaml" }));
            // the Searches tab's editor of the same file shows every setting
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchFileView TabEditor = Named<SearchesView>(Window, "SearchesContent").FindControl<SearchFileView>("SearchEditor")!;
            Assert.That(TabEditor.DataContext, Is.SameAs(ViewModel.Searches.Files.Single()));
            Assert.That(TabEditor.FindControl<Border>("FiltersCard")!.IsVisible, Is.True, "the Searches tab's editor shows every setting");
            Assert.That(TabEditor.FindControl<Border>("WeekdaysCard")!.IsVisible, Is.True);
            Window.Close();
        }

        // A search's Save and continue: the form's own Save; not enabled is said and stays; enabled with a campground it goes on;
        // unsaved changes of settings the step doesn't show are said before Save writes them too
        [AvaloniaTest]
        public void Step3_ASearchsSaveAndContinue_SavesThroughItsForm_AndGoesOnOnceASearchIsReady()
        {
            EmailReadyOnly();
            WriteSearch(AutoRun: false);
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(Guided.SearchItems.Count, Is.EqualTo(1));
            Run(Guided.SearchItems[0].SetUpCommand, Window);
            SearchFileViewModel Search = Guided.Search!;
            Assert.That(Search, Is.SameAs(ViewModel.Searches!.Files.Single()));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedStepHeading"));
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            Run(Guided.SaveSearchCommand, Window);
            Assert.That(Guided.IsSearchStep, Is.True, "not enabled: it stays");
            Assert.That(Guided.Notice, Is.EqualTo(GuidedSetupWording.SearchNotReady));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNoticeBlock"));
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "nothing changed, nothing written");
            // a setting the step doesn't show, changed in the Searches tab: said here
            Search.Weekdays.First().IsChecked = false;
            Settle(Window);
            Assert.That(Guided.HasOtherChanges, Is.True);
            Assert.That(InGuided<TextBlock>(Window, "GuidedOtherChangesText").IsEffectivelyVisible, Is.True);
            Search.Weekdays.First().IsChecked = true;
            Settle(Window);
            Assert.That(Guided.HasOtherChanges, Is.False);
            InEditor<CheckBox>(InGuided<SearchFileView>(Window, "GuidedSearchEditor"), "AutoRunCheckBox").IsChecked = true;
            Settle(Window);
            Run(Guided.SaveSearchCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.TurnOn));
            Assert.That(File.ReadAllText(Folders.In("Alpha.yaml")), Does.Contain("autoRun: true"));
            Assert.That(File.Exists(Folders.In("Alpha.yaml.bak")), Is.True, "the form's save keeps its backup");
            Assert.That(ConfigFiles.SortedSearchFileNames(Folders.Config), Is.EqualTo(new[] { "Alpha.yaml" }), "no file created");
            Window.Close();
        }

        // a search whose own values have errors: its problems in the form's words, and nothing saved
        [AvaloniaTest]
        public void Step3_ASearchWithAProblem_IsNotSaved()
        {
            EmailReadyOnly();
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.TurnOn), "ready: it resumes at the last step");
            Run(Guided.GoToSearchCommand, Window);
            Run(Guided.SearchItems[0].SetUpCommand, Window);
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            Guided.Search!.ConsecutiveDays.Text = "many";
            Settle(Window);
            Assert.That(Guided.HasProblems, Is.True);
            Assert.That(Guided.ProblemsText, Is.EqualTo(string.Join(Environment.NewLine, FormProblems.Of(Guided.Search).Select(P => P.Text))));
            Run(Guided.SaveSearchCommand, Window);
            Assert.That(Guided.IsSearchStep, Is.True);
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedProblemsBlock"));
            Window.Close();
        }

        // the campground data that can't be read is said, with the way to Settings > System's own card (the guided setup
        // refreshes nothing itself: no second refresh engine)
        [AvaloniaTest]
        public void Step3_CampgroundDataThatCantBeRead_LeadsToSettingsSystem()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindowWithoutCampgroundData();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(Guided.HasCampgroundDataProblem, Is.True);
            Assert.That(InGuided<TextBlock>(Window, "GuidedCampgroundDataText").IsEffectivelyVisible, Is.True);
            Run(InGuided<Button>(Window, "GuidedShowSystemButton").Command!, Window);
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex));
            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SystemSettingsTabIndex));
            Assert.That(ViewModel.SystemSettings.CampgroundData!.IsRunning, Is.False, "nothing was refreshed");
            Window.Close();
        }

        private (MainWindow Window, MainWindowViewModel ViewModel) ShowWindowWithoutCampgroundData()
        {
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folders.Config, CachePath = Folders.Cache },
                new ResultsSeams { Delay = Timers.Once, Ticker = Timers.Every, Time = Time }, null, null, MonitoringSeams());
            File.WriteAllText(Folders.In("RecreationDotOrg.db"), "not a database");
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Settle(Window);
            return (Window, ViewModel);
        }

        // ---- step 4 ----

        // Turn the monitoring on saves outputTo through the General form, only when the files are ready, and starts nothing
        [AvaloniaTest]
        public void Step4_TurnOn_SavesOutputToThroughTheGeneralForm_AndStartsNothing()
        {
            EmailReadyOnly();
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.TurnOn), "ready: the last step");
            Assert.That(Guided.TurnOnLabel, Is.EqualTo("Turn the monitoring on (save outputTo: Email in General.yaml)"));
            Assert.That(Guided.TurnOnHelp, Is.EqualTo(DesktopGuidedSetupWording.TurnOnHelp(GuidedSetupMethod.Email)));
            Assert.That(InGuided<Button>(Window, "GuidedTurnOnButton").IsEffectivelyEnabled, Is.True);
            string Email = File.ReadAllText(Folders.In("Email.yaml"));
            Run(Guided.TurnOnCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Done));
            Assert.That(Guided.IsComplete, Is.True);
            Assert.That(File.ReadAllText(Folders.In("General.yaml")), Does.Contain("outputTo: Email"));
            Assert.That(File.ReadAllText(Folders.In("Email.yaml")), Is.EqualTo(Email), "nothing else saved");
            Assert.That(Guided.Notice, Is.EqualTo(GuidedSetupWording.Saved("General.yaml")));
            Assert.That(ViewModel.Settings!.General.IsDirty, Is.False);
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.Zero, "turning it on starts nothing");
            Assert.That(LockIsHeld(), Is.False);
            Assert.That(Guided.CanStartMonitoring, Is.True, "the user's Start is offered");
            Window.Close();
        }

        [AvaloniaTest]
        public void Step4_TurnOn_IsRefused_WhileGeneralYamlHasUnsavedChanges_OrTheFilesAreNotReady()
        {
            EmailReadyOnly();
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            string General = File.ReadAllText(Folders.In("General.yaml"));
            ViewModel.Settings!.General.RefreshInterval.Text = "9";
            Run(Guided.TurnOnCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.TurnOn));
            Assert.That(Guided.Notice, Is.EqualTo(DesktopGuidedSetupWording.GeneralUnsaved));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNoticeBlock"));
            Assert.That(File.ReadAllText(Folders.In("General.yaml")), Is.EqualTo(General), "nothing saved");
            Assert.That(ViewModel.Settings.General.RefreshInterval.Text, Is.EqualTo("9"), "the user's unsaved change is kept, never saved or discarded");
            ViewModel.Settings.General.Revert();
            // the files stop being ready (another program removed the search): Turn on is unavailable with its reason
            File.Delete(Folders.In("Alpha.yaml"));
            Guided.Activated();
            Settle(Window);
            Assert.That(Guided.TurnOnCommand.CanExecute(null), Is.False);
            Assert.That(Guided.HasTurnOnUnavailable, Is.True);
            Assert.That(InGuided<Button>(Window, "GuidedTurnOnButton").IsEffectivelyEnabled, Is.False);
            Assert.That(InGuided<TextBlock>(Window, "GuidedTurnOnUnavailable").IsEffectivelyVisible, Is.True, "a disabled button says why");
            Assert.That(Guided.HasRefused, Is.True);
            Window.Close();
        }

        // ---- done and the monitoring ----

        // Complete, the done step offers the application's monitoring's own Start, which only the user's choice presses: it
        // starts the one monitoring of the application (no second controller), the monitoring's Status then shows it, and
        // leaving the guided setup stops nothing
        [AvaloniaTest]
        public void Done_StartsTheApplicationsMonitoringOnlyOnTheUsersChoice_AndLeavingStopsNothing()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Method), "complete: it opens at step 1");
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Done));
            Assert.That(Guided.IsComplete, Is.True);
            Assert.That(Guided.DoneText, Is.EqualTo(DesktopGuidedSetupWording.DoneText(GuidedSetupMethod.Email)));
            Assert.That(Guided.MonitoringStateText, Is.EqualTo(DesktopMonitoringWording.StateText(ViewModel.Monitoring!.Controller.View())));
            Assert.That(ViewModel.Monitoring.Controller.View().Generation, Is.Zero, "completing it starts nothing");
            Button Start = InGuided<Button>(Window, "GuidedStartMonitoringButton");
            Assert.That(Start.Command, Is.SameAs(Guided.StartMonitoringCommand));
            Assert.That(Start.IsEffectivelyEnabled, Is.True);
            Run(Guided.StartMonitoringCommand, Window);
            Waited.Await();
            Waiting(ViewModel);
            Assert.That(ViewModel.Monitoring.Controller.View().Generation, Is.EqualTo(1), "the application's one monitoring started, once");
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.MonitoringTabIndex));
            Assert.That(MonitoringRow(Window).SelectedIndex, Is.EqualTo(MainWindow.MonitoringStatusTabIndex), "its Status shows how it goes");
            Assert.That(ViewModel.MonitoringTab!.IsShown, Is.True);
            Assert.That(Guided.IsShown, Is.False);
            // the guided setup shown again while it runs, then left for another tab: the monitoring goes on, no stop asked for
            ShowGuided(Window, ViewModel);
            Assert.That(Guided.IsShown, Is.True);
            Assert.That(Guided.CanStartMonitoring, Is.False, "running: no Start");
            Assert.That(Guided.MonitoringStateText, Does.StartWith("Running"));
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Assert.That(Guided.IsShown, Is.False);
            MonitoringControlView After = ViewModel.Monitoring.Controller.View();
            Assert.That(After.Phase, Is.EqualTo(MonitoringControlPhase.Running), "leaving the guided setup stops nothing");
            Assert.That(After.Status.StopRequested, Is.False);
            Assert.That(After.Busy, Is.EqualTo(MonitoringControlBusy.None));
            Assert.That(After.Generation, Is.EqualTo(1));
            Window.Close();
        }

        // a Start for a moment that is gone (the monitoring started meanwhile in its Status) starts nothing and says so
        [AvaloniaTest]
        public void Done_AStartForAMomentThatIsGone_StartsNothing_AndSaysSo()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.CanStartMonitoring, Is.True);
            ViewModel.Monitoring!.Controller.Act(MonitoringControlAction.Start, ViewModel.Monitoring.Controller.View().Token);
            Waited.Await();
            Waiting(ViewModel);
            Run(Guided.StartMonitoringCommand, Window);
            Assert.That(Guided.MonitoringNotice, Is.EqualTo(DesktopMonitoringWording.Host.StaleText));
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedMonitoringNoticeBlock"));
            Assert.That(ViewModel.Monitoring.Controller.View().Generation, Is.EqualTo(1), "one start only");
            Assert.That(MonitoringRow(Window).SelectedIndex, Is.EqualTo(MainWindow.GuidedSetupTabIndex), "it stays");
            Window.Close();
        }

        // not complete: what is not done, each with its way to the step
        [AvaloniaTest]
        public void Done_NotComplete_ListsTheStepsNotDone()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.IsComplete, Is.True);
            File.Delete(Folders.In("Alpha.yaml"));
            Guided.Activated();
            Settle(Window);
            Assert.That(Guided.IsNotComplete, Is.True);
            Assert.That(Guided.Heading, Is.EqualTo(GuidedSetupWording.NotDoneHeading));
            Assert.That(Guided.ToDoItems.Select(I => I.Step), Is.EqualTo(GuidedSetupFlow.ToDo(GuidedSetupMethod.Email, Guided.Status)));
            Assert.That(Guided.CanStartMonitoring, Is.False);
            Run(Guided.ToDoItems[0].OpenCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            Window.Close();
        }

        // ---- moving between the steps, leaving ----

        // Back is the flow's step before; nothing is saved or discarded: an unsaved edit stays in the window's form, shows in the
        // Settings tab too and is there again when the step is opened again; leaving the guided setup masks a revealed password
        [AvaloniaTest]
        public void BackAndLeaving_SaveNothing_DiscardNothing_AndKeepTheEditsInTheWindowsForm()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Run(Guided.GoToNotificationsCommand, Window);
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            ViewModel.Settings!.Email.SmtpUsername.Text = "edited";
            ViewModel.Settings.Email.IsPasswordRevealed = true;
            Assert.That(GuidedSetupFlow.Previous(Guided.Step), Is.EqualTo(GuidedSetupStep.Method));
            Run(Guided.BackCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Method));
            Assert.That(Guided.IsEmailChosen, Is.True, "the method chosen stays checked");
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedStepHeading"));
            Assert.That(ViewModel.Settings.Email.IsPasswordRevealed, Is.False, "leaving the editor masks the password");
            Assert.That(ViewModel.Settings.Email.IsDirty, Is.True, "the edit is kept");
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "Back saves nothing");
            Run(Guided.ContinueCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search), "the saved file is ready: the flow goes on at the search");
            Run(Guided.GoToNotificationsCommand, Window);
            Assert.That(ViewModel.Settings.Email.SmtpUsername.Text, Is.EqualTo("edited"));
            // leave for the Settings tab: the same unsaved edit is there
            ShowSettingsTab(Window);
            Settle(Window);
            Assert.That(Guided.IsShown, Is.False);
            ViewModel.Settings.IsEmailSelected = true;
            Settle(Window);
            Assert.That(Named<SettingsView>(Window, "SettingsContent").FindControl<EmailSettingsView>("EmailEditor")!.DataContext, Is.SameAs(ViewModel.Settings.Email));
            Assert.That(ViewModel.Settings.Email.SmtpUsername.Text, Is.EqualTo("edited"));
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "leaving saves nothing");
            Window.Close();
        }

        // the list of steps: each one's state in words (never color alone), each a button that opens it once a method is known
        [AvaloniaTest]
        public void TheListOfSteps_SaysEachStepsStateInWords_AndOpensAStep()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Steps.Select(S => S.Step), Is.EqualTo(GuidedSetupFlow.Steps), "the flow's order");
            Assert.That(Guided.Steps.Select(S => S.AccessibleName), Is.EqualTo(new[]
            {
                "Step 1 of 4: How you are notified, done", "Step 2 of 4: Email settings, done", "Step 3 of 4: A search to check, current step", "Step 4 of 4: Review and turn on, to do"
            }));
            Assert.That(Guided.Steps.Select(S => S.OpenCommand.CanExecute(null)), Is.EqualTo(new[] { true, true, false, true }), "the current step is not opened again");
            ItemsControl List = InGuided<ItemsControl>(Window, "GuidedStepsList");
            List<Button> Buttons = List.GetVisualDescendants().OfType<Button>().Where(B => B.Name == "GuidedStepButton").ToList();
            Assert.That(Buttons.Select(B => ControlAutomationPeer.CreatePeerForElement(B).GetName()), Is.EqualTo(Guided.Steps.Select(S => S.AccessibleName)));
            Run(Guided.Steps[3].OpenCommand, Window);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.TurnOn));
            Window.Close();
        }

        // ---- accessibility, focus and layout ----

        // every action and input of each step has a name for screen readers; every message is a SeverityBlock; no secret, no
        // YAML key of a value is in what the guided setup says itself
        [AvaloniaTest]
        public void EveryStep_NamesItsControls_AndShowsNoSecret()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            foreach (GuidedSetupStep Step in new[] { GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn })
            {
                Guided.Steps.Single(S => S.Step == Step).OpenCommand.Execute(null);
                Settle(Window);
                foreach (Control Input in View(Window).GetVisualDescendants().OfType<Control>().Where(C => C.IsEffectivelyVisible && C is Button or TextBox or RadioButton or CheckBox or ComboBox))
                {
                    string? Name = ControlAutomationPeer.CreatePeerForElement(Input).GetName();
                    Assert.That(Name, Is.Not.Null.And.Not.Empty, Input.GetType().Name + " " + Input.Name + " on " + Step + " has no accessible name");
                }
                List<string> Said = View(Window).GetVisualDescendants().OfType<TextBlock>().Where(T => T.IsEffectivelyVisible).Select(T => T.Text ?? string.Empty)
                    .Concat(View(Window).GetVisualDescendants().OfType<SeverityBlock>().Select(B => B.AccessibleText)).ToList();
                Assert.That(Said.Where(T => T.Contains(ResultsFolders.Sentinel, StringComparison.Ordinal)), Is.Empty, "no password on " + Step);
                Assert.That(InGuided<TextBlock>(Window, "GuidedStepHeading").Focusable, Is.True);
                Assert.That(InGuided<TextBlock>(Window, "GuidedStepHeading").IsTabStop, Is.False, "the heading takes the focus it is given, never a Tab stop");
            }
            Window.Close();
        }

        // Tab walks the guided setup in reading order: the nested row's header, the steps, Back, then the step's own controls;
        // the last stop wraps to the top-level header (the TD-39 workaround)
        [AvaloniaTest]
        public void TheKeyboard_ReachesTheStepsAndTheStepsActions_InReadingOrder()
        {
            Unconfigured();
            var (Window, ViewModel) = ShowWindow();
            // step 1, nothing chosen: no step can be opened yet (each says so in its name), so Tab goes from the row's header to
            // the two methods and Continue, then wraps to the top-level header
            Assert.That(PressTab(Window, 6), Is.EqualTo(new[] { "Tab:Guided setup", "GuidedEmailChoice", "GuidedNtfyChoice", "GuidedContinueButton", "Tab:Monitoring", "Tab:Guided setup" }));
            Window.Close();
        }

        // once a method is known: the steps that can be opened (never the current one), Back, then the step's own controls
        [AvaloniaTest]
        public void TheKeyboard_ReachesTheStepsBackAndTheSearchStep_InReadingOrder()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            Assert.That(Guided.Step, Is.EqualTo(GuidedSetupStep.Search));
            List<string?> Stops = PressTab(Window, 8);
            Assert.That(Stops, Is.EqualTo(new[] { "Tab:Guided setup", "GuidedStepButton", "GuidedStepButton", "GuidedStepButton", "GuidedBackButton", "GuidedNewSearchNameBox",
                "GuidedNewSearchButton", "Tab:Monitoring" }), string.Join(", ", Stops));
            Window.Close();
        }

        // Tab from the selected top-level header, Count times: what has the focus after each (a header as "Tab:" and its label)
        private static List<string?> PressTab(MainWindow Window, int Count)
        {
            ((TabItem)Tabs(Window).SelectedItem!).Focus(NavigationMethod.Tab);
            Settle(Window);
            var Stops = new List<string?>();
            for (int i = 0; i < Count; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Settle(Window);
                Control? Focused = Window.FocusManager?.GetFocusedElement() as Control;
                Stops.Add(Focused is TabItem Item ? "Tab:" + Item.Header : Focused?.Name);
            }
            return Stops;
        }

        // a reading of the saved files (the window activated, the tab shown again) never moves the focus or takes what the user typed
        [AvaloniaTest]
        public void AReadingOfTheFiles_NeverMovesTheFocus()
        {
            EmailReadyOnly();
            var (Window, ViewModel) = ShowWindow();
            GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
            TextBox Name = InGuided<TextBox>(Window, "GuidedNewSearchNameBox");
            Name.Focus(NavigationMethod.Tab);
            Name.Text = "typed";
            Settle(Window);
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNewSearchNameBox"));
            WriteSearch(AutoRun: false);
            Guided.Activated();
            Settle(Window);
            Assert.That(FocusedName(Window), Is.EqualTo("GuidedNewSearchNameBox"), "a reading never moves the focus");
            Assert.That(Guided.SearchItems.Select(I => I.File.FileName), Is.EqualTo(new[] { "Alpha.yaml" }), "the reading saw the new file");
            Assert.That(ViewModel.Searches!.NewSearchName, Is.EqualTo("typed"));
            Window.Close();
        }

        // the guided setup's colors are the theme's (ER-07, ER-35): its view names no color of its own
        [Test]
        public void TheGuidedSetupView_NamesNoColorOfItsOwn()
        {
            string Root = TestContext.CurrentContext.TestDirectory;
            while (!File.Exists(Path.Join(Root, "Campertron.sln")))
            {
                Root = Path.GetDirectoryName(Root)!;
            }
            string Xaml = File.ReadAllText(Path.Join(Root, "CampertronGUI", "Views", "GuidedSetupView.axaml"));
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(Xaml, "(Background|Foreground|BorderBrush|Fill|Color)=\"(#|[A-Z][a-z]+\")"), Is.False,
                "GuidedSetupView.axaml names a color of its own instead of the theme's resources");
            Assert.That(Xaml, Does.Contain("Classes=\"readable\""), "a form page (D107)");
        }

        // the steps and their actions are inside the window from the least window up, side then main, the same instances in
        // both layouts (ER-42), in Light and Dark with the theme's and the severities' resources (ER-35)
        [AvaloniaTest]
        public void EveryStepsActions_AreInsideTheWindow_InBothLayoutsAndThemes([Values(720, 1191, 1192, 1920)] int Width, [Values("Light", "Dark")] string ThemeName)
        {
            Application.Current!.RequestedThemeVariant = ThemeName == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
            try
            {
                EmailReadyOnly();
                WriteSearch();
                var (Window, ViewModel) = ShowWindow();
                Resize(Window, Width, 540);
                GuidedSetupViewModel Guided = ShowGuided(Window, ViewModel);
                ResponsiveColumns Layout = InGuided<ResponsiveColumns>(Window, "GuidedSetupLayout");
                Assert.That(Layout.Classes, Does.Contain("readable"), "a form page (D107)");
                Assert.That(Layout.Children.Select(C => C.Name), Is.EqualTo(new[] { "GuidedSetupSide", "GuidedSetupMain" }));
                Button Back = InGuided<Button>(Window, "GuidedBackButton");
                foreach ((GuidedSetupStep Step, string Action) in new[] { (GuidedSetupStep.Method, "GuidedContinueButton"), (GuidedSetupStep.Search, "GuidedSearchContinueButton"),
                    (GuidedSetupStep.TurnOn, "GuidedTurnOnButton") })
                {
                    Guided.Steps.Single(S => S.Step == Step).OpenCommand.Execute(null);
                    Settle(Window);
                    Assert.That(Layout.IsWide, Is.EqualTo(Width >= 1192), "the boundary every tab shares");
                    foreach (string Name in new[] { "GuidedStepButton", Action })
                    {
                        Control Part = Name == "GuidedStepButton" ? InGuided<ItemsControl>(Window, "GuidedStepsList").GetVisualDescendants().OfType<Button>().First() : InGuided<Control>(Window, Name);
                        Part.BringIntoView();
                        Settle(Window);
                        Rect At = new Rect(Part.TranslatePoint(new Point(0, 0), Window)!.Value, Part.Bounds.Size);
                        Assert.That(Part.IsEffectivelyVisible, Is.True, Name + " on " + Step + " at " + Width);
                        Assert.That(At.X, Is.GreaterThanOrEqualTo(0));
                        Assert.That(At.Right, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), Name + " is cut off at " + Width);
                        Assert.That(At.Width, Is.GreaterThan(20), Name + " is squeezed at " + Width);
                    }
                    Assert.That(InGuided<Button>(Window, "GuidedBackButton"), Is.SameAs(Back), "one instance");
                }
                Rect Side = new Rect(InGuided<Control>(Window, "GuidedSetupSide").TranslatePoint(new Point(0, 0), Window)!.Value, InGuided<Control>(Window, "GuidedSetupSide").Bounds.Size);
                Rect Main = new Rect(InGuided<Control>(Window, "GuidedSetupMain").TranslatePoint(new Point(0, 0), Window)!.Value, InGuided<Control>(Window, "GuidedSetupMain").Bounds.Size);
                Assert.That(Layout.IsWide ? Main.X > Side.Right : Main.Y > Side.Bottom - 0.5, Is.True, "side then main at " + Width);
                Assert.That(InGuided<Border>(Window, "GuidedStepsCard").Background, Is.Not.Null, "the card's background is a theme resource");
                foreach (string Key in new[] { "SeverityErrorTextBrush", "SeverityWarningTextBrush", "SeverityInfoTextBrush" })
                {
                    Assert.That(Window.TryFindResource(Key, Window.ActualThemeVariant, out object? Found) && Found is Avalonia.Media.IBrush, Is.True, Key + " in " + ThemeName);
                }
                Window.Close();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
            }
        }

        // ---- the shared flow, not a copy ----

        // The desktop GUI hosts the shared flow and decides no step itself: its view model calls the flow's actions and state, and
        // never a form's Save, Create, New search or outputTo directly, a settings writer, a refresh, a search, the monitoring's
        // own start with the GUI or a web page; the desktop GUI's own sentence of the orchestration is its own words
        [Test]
        public void TheDesktopGui_HostsTheSharedFlow_AndCopiesNoDecision()
        {
            List<string> Members = ReferencedMembers(typeof(GuidedSetupViewModel)).Select(R => R.Member).ToList();
            foreach (string Flow in new[] { "Choose", "ContinueNotifications", "NewSearch", "ContinueSearch", "TurnOn", "Previous", "StateOf", "get_Steps", "ToDo", "ResumeMethod" })
            {
                Assert.That(Members, Does.Contain("CampertronLibrary.function.Forms.GuidedSetupFlow::" + Flow), Flow);
            }
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Forms.FormProblems::Of"));
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Forms.GuidedSetupStatus::Read"));
            string[] Forbidden =
            {
                "CampertronLibrary.function.Forms.SettingsFileViewModel::Save", "CampertronLibrary.function.Forms.SettingsFileViewModel::Revert",
                "CampertronLibrary.function.Forms.SearchesViewModel::NewSearch", "CampertronLibrary.function.Forms.SearchesViewModel::get_NewSearchCommand",
                "CampertronLibrary.function.Forms.SettingsFileViewModel::get_CreateCommand", "CampertronLibrary.function.Forms.GeneralSettingsViewModel::get_OutputTo",
                "CampertronLibrary.function.Forms.ChoiceSettingField::set_Selected", "CampertronLibrary.function.Forms.MonitoringController::StartWithHost",
                "CampertronLibrary.function.Forms.MonitoringController::Shutdown", "CampertronLibrary.function.Forms.MonitoringController::PauseForRefresh",
                "CampertronLibrary.function.Forms.MonitoringController::ResumeAfterRefresh"
            };
            Assert.That(Members.Intersect(Forbidden), Is.Empty, "no step decided, no form saved and no monitoring started or stopped but through the flow and Start");
            Assert.That(Members.Where(M => M.StartsWith("CampertronLibrary.function.Base.ConfigFileReplace::", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.Base.ConfigEditSession", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.RecDotOrg.api.", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.RecDotOrg.search.", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.RecDotOrg.data.", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.Base.TestNotification", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.RecDotOrg.monitoring.", StringComparison.Ordinal)
                || M.StartsWith("CampertronWeb", StringComparison.Ordinal)
                || M.StartsWith("System.IO.File", StringComparison.Ordinal) || M.StartsWith("System.IO.Directory", StringComparison.Ordinal)
                || M.StartsWith("System.IO.FileStream", StringComparison.Ordinal)), Is.Empty, "no writer, file access, refresh, search, send, monitoring engine or web page of its own");
            Assert.That(ReferencedMembers(typeof(GuidedSetupViewModel)).Select(R => R.Assembly?.GetName().Name).Distinct(), Has.None.EqualTo("CampertronWeb"));
            Assert.That(typeof(GuidedSetupViewModel).Assembly.GetReferencedAssemblies().Select(A => A.Name), Has.None.EqualTo("CampertronWeb").And.None.StartWith("Microsoft.AspNetCore"),
                "no web host or page in the desktop GUI");
            // the one Start is the controller's Act, for the moment the done step showed
            List<string> Start = ReferencedMembers(typeof(GuidedSetupViewModel).GetMethod("StartMonitoring", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
            Assert.That(Start, Does.Contain("CampertronLibrary.function.Forms.MonitoringController::Act"));
            Assert.That(Members.Count(M => M == "CampertronLibrary.function.Forms.MonitoringController::Act"), Is.EqualTo(1), "Start the monitoring is the only action on the monitoring");
            // the desktop GUI's sentence of the orchestration, and its own words: fixed, no path, not the web UI's
            Assert.That(DesktopGuidedSetupWording.Host.GeneralUnsaved, Does.Contain("Settings > Campertron"));
            string[] Words =
            {
                DesktopGuidedSetupWording.LeaveHint, DesktopGuidedSetupWording.MethodHint, DesktopGuidedSetupWording.SaveAndContinueHint, DesktopGuidedSetupWording.SearchHelp,
                DesktopGuidedSetupWording.OtherSettingsHint, DesktopGuidedSetupWording.HiddenChangesWarning, DesktopGuidedSetupWording.CampgroundDataProblem,
                DesktopGuidedSetupWording.TurnOnHelp(GuidedSetupMethod.Email), DesktopGuidedSetupWording.TurnOnHelp(GuidedSetupMethod.Ntfy), DesktopGuidedSetupWording.GeneralUnsaved,
                DesktopGuidedSetupWording.DoneText(GuidedSetupMethod.Email), DesktopGuidedSetupWording.MonitoringNotRunning, DesktopGuidedSetupWording.MonitoringRunningChanged,
            };
            Assert.That(Words, Has.None.Contain("page").And.None.Contain("/").And.None.Contain("\\").And.None.Contain("menu above"), "the desktop GUI's own words, not the web UI's");
            Assert.That(DesktopGuidedSetupWording.TurnOnHelp(GuidedSetupMethod.Email), Does.Contain("does not start the monitoring"), "it starts nothing (D115)");
        }
    }
}
