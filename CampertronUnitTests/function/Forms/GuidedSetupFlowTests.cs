using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The guided setup's orchestration with no web host (v0.4.0's phase 3, D117; business-rules-settings §37): the shared
    // form layer's GuidedSetupFlow over the forms the settings and searches pages use, in a temporary settings folder. The
    // steps' order, a step's state and Back; Choose, Save and continue of the notification settings and of a search, New
    // search and Turn the monitoring on, each saving only through the form's own Save (its checks, its backup, its secret
    // rules) and going on only once the saved files say so; a failed write, a conflict and an unsaved General.yaml keep the
    // user on the step with the form's own words; nothing is started, sent or written to the console. No network, no
    // database: the campground list is in memory.
    public class GuidedSetupFlowTests : ConfigEditTestBase
    {
        private const string Password = "Flow-Unit-S3cret-Canary";
        private const string Campground = "232447";
        private static readonly GuidedSetupHostWords Host = new GuidedSetupHostWords { GeneralUnsaved = "Error: the host's own words for an unsaved General.yaml." };

        private static string Search(string Name, bool AutoRun) =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '" + Campground + "'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private const string ValidEmail = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: guided-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        private string FileOf(string Name) => Path.Join(Folder, Name);
        private string? OnDisk(string Name) => File.Exists(FileOf(Name)) ? File.ReadAllText(FileOf(Name)) : null;
        private GuidedSetupStatus Status() => GuidedSetupStatus.Read(Folder, DateTime.Now);
        private SettingsViewModel Settings() => new SettingsViewModel(Folder, () => Now);

        private SearchesViewModel Searches()
        {
            var Entries = new List<CampgroundPickerEntry> { new CampgroundPickerEntry(new CampgroundChoice(Campground, "UPPER PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park") };
            return new SearchesViewModel(Folder, () => Now, () => Entries,
                () => new FilterChoiceSource(Id => CampgroundFilterChoices.None(Id), () => "db", Job => Job(), Job => { Job(); return Task.CompletedTask; }));
        }

        // nothing of the guided setup reaches the console, a secret least of all; nothing starts the monitoring
        private void NothingSaidOrStarted()
        {
            Assert.That(Out.ToString(), Is.Empty, "nothing written to the console");
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Has.None.EqualTo("Camp.History").And.None.EqualTo("Campertron.monitoring.lock"));
        }

        // ---- the steps ----

        [Test]
        public void TheSteps_AreInTheirOrder_BackLeadsToTheOneBefore_AndAStepsStateComesFromTheSavedFilesOnly()
        {
            Assert.That(GuidedSetupFlow.Steps, Is.EqualTo(new[] { GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn }));
            Assert.That(GuidedSetupFlow.Steps, Has.Count.EqualTo(GuidedSetupWording.StepCount));
            Assert.That(new[] { GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn, GuidedSetupStep.Done }.Select(GuidedSetupFlow.Previous),
                Is.EqualTo(new GuidedSetupStep?[] { null, GuidedSetupStep.Method, GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn }));

            GuidedSetupStatus Empty = Status();
            Assert.That(GuidedSetupFlow.Steps.Select(One => GuidedSetupFlow.StateOf(One, GuidedSetupStep.Method, null, Empty)),
                Is.EqualTo(new[] { GuidedSetupStepState.Current, GuidedSetupStepState.ToDo, GuidedSetupStepState.ToDo, GuidedSetupStepState.ToDo }), "no method yet: nothing is done");
            Assert.That(GuidedSetupFlow.Steps.Select(One => GuidedSetupFlow.StateOf(One, GuidedSetupStep.Notifications, GuidedSetupMethod.Email, Empty)),
                Is.EqualTo(new[] { GuidedSetupStepState.Done, GuidedSetupStepState.Current, GuidedSetupStepState.ToDo, GuidedSetupStepState.ToDo }), "a method chosen is step 1 done");
            Assert.That(GuidedSetupFlow.ToDo(GuidedSetupMethod.Email, Empty), Is.EqualTo(new[] { GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupStep.TurnOn }));

            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            GuidedSetupStatus Mail = Status();
            Assert.That(GuidedSetupFlow.Steps.Select(One => GuidedSetupFlow.StateOf(One, GuidedSetupStep.Search, GuidedSetupMethod.Email, Mail)),
                Is.EqualTo(new[] { GuidedSetupStepState.Done, GuidedSetupStepState.Done, GuidedSetupStepState.Current, GuidedSetupStepState.ToDo }), "the saved Email.yaml is ready");
            Assert.That(GuidedSetupFlow.StateOf(GuidedSetupStep.Notifications, GuidedSetupStep.Search, GuidedSetupMethod.Ntfy, Mail), Is.EqualTo(GuidedSetupStepState.ToDo), "per method");
            Assert.That(GuidedSetupFlow.ToDo(GuidedSetupMethod.Email, Mail), Is.EqualTo(new[] { GuidedSetupStep.Search, GuidedSetupStep.TurnOn }));

            File.WriteAllText(FileOf("Trip.yaml"), Search("Trip", AutoRun: true));
            File.WriteAllText(FileOf("General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            GuidedSetupStatus Complete = Status();
            Assert.That(GuidedSetupFlow.ToDo(GuidedSetupMethod.Email, Complete), Is.Empty);
            Assert.That(GuidedSetupFlow.Steps.Select(One => GuidedSetupFlow.StateOf(One, GuidedSetupStep.Done, GuidedSetupMethod.Email, Complete)), Has.All.EqualTo(GuidedSetupStepState.Done));

            //the words of a step and of its state
            Assert.That(new[] { GuidedSetupStepState.Done, GuidedSetupStepState.Current, GuidedSetupStepState.ToDo }.Select(GuidedSetupWording.StepState), Is.EqualTo(new[] { "done", "current step", "to do" }));
            Assert.That(GuidedSetupWording.StepHeading(GuidedSetupStep.Notifications, GuidedSetupMethod.Ntfy, Empty), Is.EqualTo("Step 2 of 4: ntfy settings"));
            Assert.That(GuidedSetupWording.StepHeading(GuidedSetupStep.Method, null, Empty), Is.EqualTo("Step 1 of 4: How you are notified"));
            Assert.That(GuidedSetupWording.StepHeading(GuidedSetupStep.Done, GuidedSetupMethod.Email, Mail), Is.EqualTo("Guided setup is not finished"), "never done unless the files say so");
            Assert.That(GuidedSetupWording.StepHeading(GuidedSetupStep.Done, GuidedSetupMethod.Email, Complete), Is.EqualTo("Campertron is set up to email you"));
            Assert.That(GuidedSetupWording.StepHeading(GuidedSetupStep.Done, GuidedSetupMethod.Ntfy, Complete), Is.EqualTo("Guided setup is not finished"));
            Assert.That(Mail.NotificationProblems(GuidedSetupMethod.Email), Is.Empty);
            Assert.That(Empty.NotificationProblems(GuidedSetupMethod.Email), Is.EqualTo(new[] { "Email.yaml: The file does not exist." }), "only the method's own file");
            Assert.That(Empty.Problems(GuidedSetupMethod.Email), Is.EqualTo(Empty.StartProblems(GuidedSetupMethod.Email).Concat(Empty.SecretFileProblems(GuidedSetupMethod.Email)).Concat(Empty.SearchProblems)));
            Assert.That(Complete.Problems(GuidedSetupMethod.Email), Is.Empty);
            NothingSaidOrStarted();
        }

        [Test]
        public void TheStartPagesWords_AndWhereTheGuidedSetupResumes_ComeFromTheSavedFiles()
        {
            (string, string, GuidedSetupMethod?) Start()
            {
                GuidedSetupStatus S = Status();
                return (GuidedSetupWording.StartState(S), GuidedSetupWording.StartAction(S), GuidedSetupFlow.ResumeMethod(S));
            }
            Assert.That(Start(), Is.EqualTo((GuidedSetupWording.StartUnconfigured, GuidedSetupWording.StartLink, (GuidedSetupMethod?)null)));
            File.WriteAllText(FileOf("Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: flowUnitTopic0123456789abc\n");
            Assert.That(Start(), Is.EqualTo((GuidedSetupWording.StartPartial, GuidedSetupWording.ContinueLink, (GuidedSetupMethod?)GuidedSetupMethod.Ntfy)));
            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            Assert.That(Start(), Is.EqualTo((GuidedSetupWording.StartPartial, GuidedSetupWording.ContinueLink, (GuidedSetupMethod?)null)), "both ready: the user chooses");
            File.Delete(FileOf("Ntfy.yaml"));
            File.WriteAllText(FileOf("Trip.yaml"), Search("Trip", AutoRun: true));
            Assert.That(Start(), Is.EqualTo((GuidedSetupWording.StartReady, GuidedSetupWording.ContinueLink, (GuidedSetupMethod?)GuidedSetupMethod.Email)));
            File.WriteAllText(FileOf("General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            Assert.That(Start(), Is.EqualTo((GuidedSetupWording.StartComplete(GuidedSetupMethod.Email), GuidedSetupWording.ReopenLink, (GuidedSetupMethod?)null)), "complete: it opens again at step 1");
            NothingSaidOrStarted();
        }

        [Test]
        public void ChoosingAMethod_LeadsToItsFirstStepThatIsNotDone_SavesNothing_AndNothingChosenIsSaid()
        {
            GuidedSetupOutcome None = GuidedSetupFlow.Choose(null, Status());
            Assert.That((None.Next, None.Notice, None.Attention, None.FormActed), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupWording.MethodMissing, GuidedSetupAttention.Problems, false)));
            GuidedSetupOutcome Ntfy = GuidedSetupFlow.Choose(GuidedSetupMethod.Ntfy, Status());
            Assert.That((Ntfy.Next, Ntfy.Notice, Ntfy.Attention), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Notifications, (string?)null, GuidedSetupAttention.Step)));
            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            Assert.That(GuidedSetupFlow.Choose(GuidedSetupMethod.Email, Status()).Next, Is.EqualTo(GuidedSetupStep.Search), "it resumes where the saved files say");
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Email.yaml" }), "choosing writes nothing");
            NothingSaidOrStarted();
        }

        // ---- step 2 ----

        [Test]
        public void TheEmailStepsSaveAndContinue_SavesThroughTheFormsOwnSave_NeverAValueInError_AndGoesOnOnlyOnceTheSavedFileIsReady()
        {
            SettingsViewModel All = Settings();
            EmailSettingsViewModel Form = All.Email;
            Form.CreateCommand.Execute(null);
            string Sample = OnDisk("Email.yaml")!;

            //a value the form refuses: nothing saved, the form's problems have the attention
            Form.SmtpPort.Text = "not a port";
            GuidedSetupOutcome Refused = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((Refused.Next, Refused.Attention, Refused.Notice), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Problems, (string?)null)));
            Assert.That(FormProblems.Of(Form).Select(P => P.Text), Has.Some.StartWith("SMTP port: "));
            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(Sample), "a value with its own error is never saved");
            Assert.That(Refused.Status!.NotificationsReady(GuidedSetupMethod.Email), Is.False);

            //saved without a recipient: the form saves it, the step stays on what the monitoring's start would refuse
            Form.SmtpServer.Text = "smtp.example.invalid";
            Form.SmtpPort.Text = "587";
            Form.SmtpUsername.Text = "guided-user";
            Form.SmtpPassword.Text = Password;
            Form.SendFromAddress.Text = "from@example.invalid";
            GuidedSetupOutcome NoRecipient = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((NoRecipient.Next, NoRecipient.Attention, NoRecipient.Notice), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Needs, (string?)null)));
            Assert.That(OnDisk("Email.yaml"), Does.Contain("smtp.example.invalid").And.Contain(Password), "the form saved what it accepted");
            Assert.That(NoRecipient.Status!.NotificationProblems(GuidedSetupMethod.Email), Is.EqualTo(new[] { "Email.yaml: sendToAddressList has no address: add at least one recipient." }));
            Assert.That(File.Exists(FileOf("Email.yaml.bak")), Is.True, "the form's own backup");
            Assert.That(File.ReadAllText(FileOf("Email.yaml.bak")), Is.EqualTo(Sample), "of the file before the save");

            //the recipient: saved, on to the search, the save's result said once there
            Form.Recipients.AddCommand.Execute(null);
            Form.Recipients.Rows[0].Text = "to@example.invalid";
            GuidedSetupOutcome Ready = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((Ready.Next, Ready.Attention), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Search, GuidedSetupAttention.Step)));
            Assert.That(Ready.Notice, Is.EqualTo("Info: Saved Email.yaml. The previous version is in Email.yaml.bak."));
            Assert.That(Ready.Status!.NotificationsReady(GuidedSetupMethod.Email), Is.True);
            Assert.That(Ready.FormActed, Is.False, "only Turn the monitoring on reports its form's action; a host follows every Save and continue");

            //ready and unchanged: goes on without writing
            string Saved = OnDisk("Email.yaml")!;
            DateTime Written = File.GetLastWriteTimeUtc(FileOf("Email.yaml"));
            Assert.That(GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder).Next, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That((OnDisk("Email.yaml"), File.GetLastWriteTimeUtc(FileOf("Email.yaml"))), Is.EqualTo((Saved, Written)));
            Assert.That(Ready.Notice + NoRecipient.Notice + Refused.Notice, Does.Not.Contain(Password), "no outcome says the password");
            Assert.That(OnDisk("General.yaml"), Is.Null, "the step writes its own file only");
            NothingSaidOrStarted();
        }

        [Test]
        public void TheNtfyStepsSaveAndContinue_GoesOnOnlyWithNtfyTurnedOn()
        {
            NtfySettingsViewModel Form = Settings().Ntfy;
            Form.CreateCommand.Execute(null);
            Form.Server.Text = "https://ntfy.example.invalid";
            Form.Topic.Text = "flowUnitTopic0123456789abc";
            GuidedSetupOutcome Off = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Ntfy, Folder);
            Assert.That((Off.Next, Off.Attention), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Needs)));
            Assert.That(OnDisk("Ntfy.yaml"), Does.Contain("enabled: false"), "saved as it was entered");
            Assert.That(Off.Status!.NotificationProblems(GuidedSetupMethod.Ntfy), Has.Count.EqualTo(1).And.Some.Contain("enabled is false"));

            Form.Enabled.IsChecked = true;
            GuidedSetupOutcome On = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Ntfy, Folder);
            Assert.That((On.Next, On.Notice), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Search, "Info: Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak.")));
            Assert.That(File.ReadAllText(FileOf("Ntfy.yaml.bak")), Does.Contain("enabled: false"), "the form's own backup of the version before");

            //a new topic: the backup never keeps the topic the save replaced (D112), whoever saves
            Form.Topic.Text = "flowUnitTopicReplacement0123";
            GuidedSetupOutcome Replaced = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Ntfy, Folder);
            Assert.That(Replaced.Next, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(Replaced.Notice, Does.StartWith("Info: Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without "));
            Assert.That(File.ReadAllText(FileOf("Ntfy.yaml.bak")), Does.Not.Contain("flowUnitTopic0123456789abc"));
            Assert.That(OnDisk("Email.yaml"), Is.Null, "the other method's file is not touched");
            NothingSaidOrStarted();
        }

        [Test]
        public void AFailedWrite_OrAConflict_KeepsTheUserOnTheStep_WithTheFormsOwnWords_AndARetryGoesOn()
        {
            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            EmailSettingsViewModel Form = Settings().Email;

            //the write fails (the writer's own words): nothing changes on disk, the step stays with the form's result; the
            //orchestration says nothing of its own
            Form.SmtpUsername.Text = "second-user";
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("Email.yaml could not be replaced.");
                }
            };
            GuidedSetupOutcome Failed = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((Failed.Next, Failed.Attention, Failed.Notice), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Status, (string?)null)));
            Assert.That((Form.StatusIsError, Form.StatusText), Is.EqualTo((true, "Email.yaml could not be replaced.")), "the form's result, as the settings page shows it");
            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(ValidEmail), "nothing written");
            Assert.That(Form.IsDirty, Is.True, "the user's change is kept for a retry");

            //the retry goes on
            ConfigFileReplace.Step = (_, _) => { };
            GuidedSetupOutcome Retried = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That(Retried.Next, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(OnDisk("Email.yaml"), Does.Contain("smtpUsername: second-user"));

            //changed on disk under an unsaved change: the form's conflict, never an overwrite
            Form.SmtpServer.Text = "mine.example.invalid";
            string Theirs = OnDisk("Email.yaml")!.Replace("smtp.example.invalid", "theirs.example.invalid", StringComparison.Ordinal);
            File.WriteAllText(FileOf("Email.yaml"), Theirs);
            Form.CheckForExternalChanges();
            GuidedSetupOutcome Conflict = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((Conflict.Next, Conflict.Attention), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Status)), "the save's refusal, then its conflict, have the attention");
            Assert.That(Form.HasConflict, Is.True);
            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(Theirs));
            Form.UseTheirsCommand.Execute(null);
            Assert.That(GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder).Next, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(Theirs));
            NothingSaidOrStarted();
        }

        // ---- baseline of v0.4.0's phase 12 (the console's guided setup relies on these, unchanged) ----

        // Save and continue of a form that has nothing to save, whose saved file is not ready: it stays on the step with what
        // the monitoring's start would refuse, and writes nothing (no file created, changed or backed up), for a missing
        // Email.yaml (its form offers Create, which only the user's own action runs) and for one without a recipient
        [Test]
        public void SaveAndContinue_WithNothingToSave_AndASavedFileThatIsNotReady_StaysAndWritesNothing()
        {
            EmailSettingsViewModel Missing = Settings().Email;
            Assert.That(Missing.IsMissing, Is.True);
            GuidedSetupOutcome None = GuidedSetupFlow.ContinueNotifications(Missing, GuidedSetupMethod.Email, Folder);
            Assert.That((None.Next, None.Attention, None.FormActed), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Needs, false)));
            Assert.That(None.Status!.NotificationProblems(GuidedSetupMethod.Email), Is.EqualTo(new[] { "Email.yaml: The file does not exist." }));
            Assert.That(Directory.GetFiles(Folder), Is.Empty, "Save and continue never creates the file: only Create does");

            string NoRecipient = ValidEmail.Replace("sendToAddressList:\n- to@example.invalid\n", string.Empty, StringComparison.Ordinal);
            File.WriteAllText(FileOf("Email.yaml"), NoRecipient);
            DateTime Written = File.GetLastWriteTimeUtc(FileOf("Email.yaml"));
            EmailSettingsViewModel Form = Settings().Email;
            GuidedSetupOutcome Stays = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That((Stays.Next, Stays.Attention, Stays.Notice), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Needs, (string?)null)));
            Assert.That((OnDisk("Email.yaml"), File.GetLastWriteTimeUtc(FileOf("Email.yaml"))), Is.EqualTo((NoRecipient, Written)), "nothing written");
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Email.yaml" }), "no backup: nothing was saved");
            NothingSaidOrStarted();
        }

        // Save and continue that replaces Email.yaml's plain-text password: the form's own save, whose backup never keeps the
        // password it replaced (D111), whichever host's step saves it; no outcome says it
        [Test]
        public void SaveAndContinue_ThatReplacesThePlainTextPassword_KeepsTheOldPasswordOutOfTheBackup()
        {
            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            EmailSettingsViewModel Form = Settings().Email;
            Form.SmtpPassword.Text = "Flow-Unit-Replacement-Canary";
            GuidedSetupOutcome Saved = GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Folder);
            Assert.That(Saved.Next, Is.EqualTo(GuidedSetupStep.Search));
            Assert.That(OnDisk("Email.yaml"), Does.Contain("Flow-Unit-Replacement-Canary").And.Not.Contain(Password));
            Assert.That(File.ReadAllText(FileOf("Email.yaml.bak")), Does.Not.Contain(Password), "the replaced password is not kept in the backup");
            Assert.That(Saved.Notice, Does.Not.Contain(Password).And.Not.Contain("Flow-Unit-Replacement-Canary"));
            NothingSaidOrStarted();
        }

        // Save and continue of a search that is unchanged and ready goes on without writing; one whose problem is in a setting
        // the step does not show (its filters) stays with the form's problems and writes nothing
        [Test]
        public void ASearchsSaveAndContinue_WithNothingToSave_GoesOnWithoutWriting_AndAProblemOfAnotherSettingStays()
        {
            File.WriteAllText(FileOf("Trip.yaml"), Search("Trip", AutoRun: true));
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel Trip = Form.Files.Single();
            Form.SelectedFile = Trip;
            DateTime Written = File.GetLastWriteTimeUtc(FileOf("Trip.yaml"));
            GuidedSetupOutcome Ready = GuidedSetupFlow.ContinueSearch(Trip, Folder);
            Assert.That((Ready.Next, Ready.Notice), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.TurnOn, (string?)null)), "nothing saved, nothing said");
            Assert.That(File.GetLastWriteTimeUtc(FileOf("Trip.yaml")), Is.EqualTo(Written));
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Trip.yaml" }), "no backup: nothing was saved");

            //a file whose filter holds an empty item (the form's own error): a problem of a setting the step does not show
            string WithEmpty = Search("Trip", AutoRun: true) + "includeEquipment:\n- ~\n- Tent\n";
            File.WriteAllText(FileOf("Trip.yaml"), WithEmpty);
            SearchesViewModel Again = Searches();
            Again.Show();
            SearchFileViewModel Held = Again.Files.Single();
            Again.SelectedFile = Held;
            Assert.That(FormProblems.Of(Held).Select(P => P.Field), Has.Some.SameAs(Held.IncludeEquipment), "the form's own problem, at the filter");
            DateTime Before = File.GetLastWriteTimeUtc(FileOf("Trip.yaml"));
            GuidedSetupOutcome Refused = GuidedSetupFlow.ContinueSearch(Held, Folder);
            Assert.That((Refused.Next, Refused.Attention), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Problems)));
            Assert.That((OnDisk("Trip.yaml"), File.GetLastWriteTimeUtc(FileOf("Trip.yaml"))), Is.EqualTo((WithEmpty, Before)), "nothing written");
            NothingSaidOrStarted();
        }

        // ---- step 3 ----

        [Test]
        public void NewSearch_CreatesTheExampleSearchNotEnabled_AndANameTheFormRefusesStays()
        {
            SearchesViewModel Form = Searches();
            Form.Show();
            Form.NewSearchName = "../General";
            GuidedSetupOutcome Refused = GuidedSetupFlow.NewSearch(Form);
            Assert.That((Refused.Next, Refused.CreatedFile, Refused.Attention), Is.EqualTo(((GuidedSetupStep?)null, (string?)null, GuidedSetupAttention.NewSearch)));
            Assert.That(Form.NewSearchIsError, Is.True);
            Assert.That(Directory.GetFiles(Folder), Is.Empty);

            Form.NewSearchName = "Lake trip";
            GuidedSetupOutcome Created = GuidedSetupFlow.NewSearch(Form);
            Assert.That((Created.Next, Created.CreatedFile, Created.Attention), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Search, "Lake trip.yaml", GuidedSetupAttention.Notice)));
            Assert.That(Created.Notice, Is.EqualTo(Form.NewSearchMessage).And.StartWith("Created Lake trip.yaml"));
            Assert.That(OnDisk("Lake trip.yaml"), Does.Contain("autoRun: false"), "the searches page's own New search: the example, not enabled");

            Form.NewSearchName = "Lake trip";
            Assert.That(GuidedSetupFlow.NewSearch(Form).CreatedFile, Is.Null, "never over a file that exists");
            NothingSaidOrStarted();
        }

        [Test]
        public void ASearchsSaveAndContinue_GoesOnOnceAnEnabledSearchCanBeChecked()
        {
            File.WriteAllText(FileOf("Trip.yaml"), Search("Trip", AutoRun: false));
            SearchesViewModel Form = Searches();
            Form.Show();
            SearchFileViewModel Trip = Form.Files.Single();
            Form.SelectedFile = Trip;

            Trip.TotalHumans.Text = "4";
            GuidedSetupOutcome NotEnabled = GuidedSetupFlow.ContinueSearch(Trip, Folder);
            Assert.That((NotEnabled.Next, NotEnabled.Notice, NotEnabled.Attention), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupWording.SearchNotReady, GuidedSetupAttention.Notice)));
            Assert.That(OnDisk("Trip.yaml"), Does.Contain("totalHumans: 4"), "saved, but not yet enabled");

            Trip.TotalHumans.Text = "many";
            GuidedSetupOutcome Refused = GuidedSetupFlow.ContinueSearch(Trip, Folder);
            Assert.That((Refused.Next, Refused.Notice, Refused.Attention), Is.EqualTo(((GuidedSetupStep?)null, (string?)null, GuidedSetupAttention.Problems)), "the form's failed save says why");
            Assert.That(Trip.StatusText, Is.EqualTo("Nothing was saved: fix the errors shown first."));
            Assert.That(OnDisk("Trip.yaml"), Does.Contain("totalHumans: 4"));

            Trip.TotalHumans.Text = "4";
            Trip.AutoRun.IsChecked = true;
            GuidedSetupOutcome Ready = GuidedSetupFlow.ContinueSearch(Trip, Folder);
            Assert.That((Ready.Next, Ready.Notice), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.TurnOn, "Info: Saved Trip.yaml. The previous version is in Trip.yaml.bak.")));
            Assert.That(Ready.Status!.SearchReady, Is.True);
            Assert.That(OnDisk("Trip.yaml"), Does.Contain("autoRun: true").And.Contain("showMonday: true"), "the settings it does not show keep their values");
            NothingSaidOrStarted();
        }

        // ---- step 4 ----

        [Test]
        public void TurningOn_SavesOutputToOnly_OnlyOnceTheFilesPass_NeverOverAnUnsavedGeneralYaml_AndStartsNothing()
        {
            GeneralSettingsViewModel General = Settings().General;
            GuidedSetupOutcome NotReady = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Email, Status(), Folder, Host);
            Assert.That((NotReady.Next, NotReady.Notice, NotReady.Attention, NotReady.FormActed), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupWording.TurnOnNotReady, GuidedSetupAttention.Notice, false)));
            Assert.That(OnDisk("General.yaml"), Is.Null, "nothing saved, not even a sample");

            File.WriteAllText(FileOf("Email.yaml"), ValidEmail);
            File.WriteAllText(FileOf("Trip.yaml"), Search("Trip", AutoRun: true));
            GuidedSetupOutcome On = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Email, Status(), Folder, Host);
            Assert.That((On.Next, On.Notice, On.Attention, On.FormActed), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Done, "Info: General.yaml was saved.", GuidedSetupAttention.Step, true)));
            Assert.That(On.Status!.State(GuidedSetupMethod.Email), Is.EqualTo(GuidedSetupState.Complete));
            string Saved = OnDisk("General.yaml")!;
            Assert.That(Saved, Does.Contain("outputTo: Email"), "created with its defaults and outputTo");
            Assert.That(General.IsDirty, Is.False);

            //already on: goes on and writes nothing
            DateTime Written = File.GetLastWriteTimeUtc(FileOf("General.yaml"));
            GuidedSetupOutcome Again = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Email, Status(), Folder, Host);
            Assert.That((Again.Next, Again.Notice, Again.FormActed), Is.EqualTo(((GuidedSetupStep?)GuidedSetupStep.Done, GuidedSetupWording.AlreadyOn, false)));
            Assert.That((OnDisk("General.yaml"), File.GetLastWriteTimeUtc(FileOf("General.yaml"))), Is.EqualTo((Saved, Written)));

            //General.yaml with the user's unsaved change: never saved along; the host's own words
            General.RefreshInterval.Text = "12";
            GuidedSetupOutcome Held = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Ntfy, Status(), Folder, Host);
            Assert.That((Held.Next, Held.Notice, Held.FormActed), Is.EqualTo(((GuidedSetupStep?)null, Host.GeneralUnsaved, false)));
            Assert.That(OnDisk("General.yaml"), Is.EqualTo(Saved));
            General.Revert();

            //another method whose files are ready, and a write that fails: the form's words, no unsaved outputTo left behind
            File.WriteAllText(FileOf("Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: flowUnitTopic0123456789abc\n");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("General.yaml could not be replaced.");
                }
            };
            GuidedSetupOutcome Failed = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Ntfy, Status(), Folder, Host);
            Assert.That((Failed.Next, Failed.Attention, Failed.FormActed), Is.EqualTo(((GuidedSetupStep?)null, GuidedSetupAttention.Notice, true)));
            Assert.That(Failed.Notice, Is.EqualTo("Error: General.yaml could not be replaced."), "the form's own words with their severity");
            Assert.That(General.IsDirty, Is.False, "the outputTo that was not saved is reverted");
            Assert.That(OnDisk("General.yaml"), Is.EqualTo(Saved));
            ConfigFileReplace.Step = (_, _) => { };
            GuidedSetupOutcome Ntfy = GuidedSetupFlow.TurnOn(General, GuidedSetupMethod.Ntfy, Status(), Folder, Host);
            Assert.That(Ntfy.Next, Is.EqualTo(GuidedSetupStep.Done), "the retry saves it");
            Assert.That(OnDisk("General.yaml"), Is.EqualTo(Saved.Replace("outputTo: Email", "outputTo: Ntfy", StringComparison.Ordinal)), "outputTo and nothing else");
            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(ValidEmail), "nothing of the other method is touched");
            NothingSaidOrStarted();
        }
    }
}
