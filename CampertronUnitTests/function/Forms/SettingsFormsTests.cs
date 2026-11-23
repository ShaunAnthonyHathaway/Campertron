using System.ComponentModel;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The shared form layer (v0.3.0, D79's PR 3), at its own level: the forms of General.yaml, Email.yaml and Ntfy.yaml as
    // any UI drives them, without a window, a dispatcher or a UI toolkit. What a user types is parsed here, a text that is
    // no value is the field's own error and never reaches the file, the words the forms compose (outputTo's labels, D71's,
    // D73's and D77's messages at outputTo, the ntfy line, Email.yaml's explanation and severity) are written out, and Save
    // is refused while a field has its own error. The desktop GUI's tests check the same through its window
    // (CampertronGUITests); the web UI relies on these. Temporary folders only; nothing connects, sends or reads a secret.
    public class SettingsFormsTests : ConfigEditTestBase
    {
        private const string GeneralText = "# Campertron settings\noutputTo: Console   # where results go\n\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";
        private const string EmailText = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: form-user\nsmtpPassword: F0rm-S3cret\n" +
            "sendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private const string NtfyText = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: formLayerTopic0123456789ab\n";
        private const string SearchText = "displayName: Form search\nautoRun: true\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");

        private SettingsViewModel Settings() => new SettingsViewModel(Folder, () => Now);

        private static SettingChoice Choice(ChoiceSettingField Field, object Value) => Field.Choices.Single(C => C.Value.Equals(Value));

        // ---- field parsing and input errors ----

        [Test]
        public void ANumberOrDateThatIsNoValue_IsTheFieldsOwnError_NeverReachesTheFile_AndBlocksSave()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            Assert.That(Form.CanSave, Is.False, "nothing changed yet");

            Form.RefreshInterval.Text = "a month";
            Assert.That(Form.RefreshInterval.MessageText, Is.EqualTo("Error: Enter a number of days, such as 30 or 7.5."));
            Assert.That(Form.RefreshInterval.IsError, Is.True);
            Assert.That(Form.RefreshInterval.HelpText, Is.EqualTo("Error: Enter a number of days, such as 30 or 7.5."), "what a screen reader reads with the box");
            Assert.That(Form.IsDirty, Is.False, "the text never reached the session");
            Assert.That(Form.CanSave, Is.False);

            // another, valid change does not make the form savable while the error is shown
            Form.AutoRefresh.IsChecked = true;
            Assert.That(Form.IsDirty, Is.True);
            Assert.That(Form.CanSave, Is.False, "a field's own error blocks Save");
            Form.Save();
            Assert.That(Form.StatusText, Is.EqualTo("Nothing was saved: fix the errors shown first."));
            Assert.That(Form.StatusIsError, Is.True);
            Assert.That(Text(General), Is.EqualTo(GeneralText), "the file is untouched");

            Form.RefreshInterval.Text = " 7.5 ";
            Assert.That(Form.RefreshInterval.HasMessage, Is.False);
            Assert.That(Form.CanSave, Is.True);
            Assert.That(Form.PreviewSummary, Is.EqualTo("Changes: refreshRidbDataDayInterval, autoRefresh").Or.EqualTo("Changes: autoRefresh, refreshRidbDataDayInterval"));
            Form.Save();
            Assert.That(Form.StatusIsError, Is.False, Form.StatusText);
            Assert.That(Text(General), Does.Contain("refreshRidbDataDayInterval: 7.5").And.Contain("autoRefresh: true").And.Contain("# where results go"));
            Assert.That(Form.IsDirty, Is.False);
        }

        [Test]
        public void TheLastRefresh_IsAUtcDateAndTime_InTheFormsTheFormAccepts()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            Assert.That(Form.LastRefresh.Text, Is.EqualTo("2020-01-02T03:04:05.0000000Z"));

            foreach (string Refused in new[] { "yesterday", "02/01/2020", "2020-13-01", "" })
            {
                Form.LastRefresh.Text = Refused;
                Assert.That(Form.LastRefresh.MessageText, Is.EqualTo("Error: Enter a UTC date and time such as 2026-01-31T00:00:00Z."), Refused);
                Assert.That(Form.CanSave, Is.False, Refused);
            }
            foreach (string Accepted in new[] { "2026-01-31", "2026-01-31T12:30", "2026-01-31 12:30:15", "2026-01-31T12:30:15Z", "2026-01-31T12:30:15+02:00" })
            {
                Form.LastRefresh.Text = Accepted;
                Assert.That(Form.LastRefresh.HasMessage, Is.False, Accepted);
                Assert.That(Form.Session.Value("lastRidbDataRefresh"), Is.InstanceOf<DateTime>(), Accepted);
                Assert.That(((DateTime)Form.Session.Value("lastRidbDataRefresh")!).Kind, Is.EqualTo(DateTimeKind.Utc), Accepted);
            }
            Assert.That((DateTime)Form.Session.Value("lastRidbDataRefresh")!, Is.EqualTo(new DateTime(2026, 1, 31, 10, 30, 15, DateTimeKind.Utc)), "an offset is converted to UTC");
        }

        [Test]
        public void EmailsPortAndPasswordFile_AreParsedByTheForm()
        {
            Write(General, GeneralText);
            Write(Email, EmailText);
            EmailSettingsViewModel Form = Settings().Email;

            Form.SmtpPort.Text = "five eight seven";
            Assert.That(Form.SmtpPort.MessageText, Is.EqualTo("Error: Enter a port number, such as 587."));
            Assert.That(Form.CanSave, Is.False);
            Form.SmtpPort.Text = " 465 ";
            Assert.That(Form.SmtpPort.HasMessage, Is.False);
            Assert.That(Form.Session.Value("smtpPort"), Is.EqualTo(465));

            // D70: a path the validation would refuse is the field's own error in every output; the file is never opened
            Form.SmtpPasswordFile.Text = "relative/password.txt";
            Assert.That(Form.SmtpPasswordFile.IsError, Is.True);
            Assert.That(Form.SmtpPasswordFile.Message, Is.EqualTo(ConfigValidation.PasswordFilePathProblem("relative/password.txt")!.Value.Message));
            Assert.That(Form.Session.IsChanged("smtpPasswordFile"), Is.False);
            Assert.That(Form.CanSave, Is.False);
            Form.SmtpPasswordFile.Text = string.Empty;
            Assert.That(Form.SmtpPasswordFile.IsError, Is.False);
            Assert.That(Form.CanSave, Is.True, "the port's change can be saved");

            // the password never appears in a message, the preview or the summary
            Form.SmtpPassword.Text = "An0ther-S3cret";
            Assert.That(Form.PreviewText, Does.Not.Contain("An0ther-S3cret").And.Not.Contain("F0rm-S3cret"));
            Assert.That(Form.PreviewSummary, Does.Contain("smtpPassword (hidden)"));
            Assert.That(Form.SmtpPassword.IsSecret, Is.True);
        }

        [Test]
        public void AListsEmptyRows_AreNeverWritten_AndTheOthersAreWrittenWithoutTheSpacesAroundThem()
        {
            Write(General, GeneralText);
            Write(Email, EmailText);
            EmailSettingsViewModel Form = Settings().Email;
            Assert.That(Form.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "to@example.invalid" }));
            Assert.That(Form.Recipients.RowsHelp, Is.EqualTo("One email address per row. Empty rows are ignored."));
            Assert.That(Form.Recipients.AddLabel, Is.EqualTo("Add recipient"));

            Form.AddRecipient();
            Assert.That(Form.Recipients.Rows, Has.Count.EqualTo(2));
            Assert.That(Form.Recipients.Rows[1].AccessibleName, Is.EqualTo("Recipient 2"));
            Assert.That(Form.Recipients.Rows[1].RemoveName, Is.EqualTo("Remove recipient 2"));
            Assert.That(Form.IsDirty, Is.False, "an empty row is no change (D71)");

            Form.Recipients.Rows[1].Text = "  second@example.invalid  ";
            Form.AddRecipient();
            Assert.That(Form.IsDirty, Is.True);
            Form.Save();
            Assert.That(Form.StatusIsError, Is.False, Form.StatusText);
            Assert.That(Text(Email), Does.Contain("- to@example.invalid\n- second@example.invalid\n"));
            Assert.That(Form.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "to@example.invalid", "second@example.invalid" }), "the rows are the file's again");

            // an address that is not valid is its row's message and blocks Save in every output
            Form.Recipients.Rows[1].Text = "not an address";
            Assert.That(Form.Recipients.Rows[1].IsError, Is.True);
            Assert.That(Form.Recipients.Rows[1].MessageText, Does.StartWith("Error: "));
            Assert.That(Form.Recipients.Rows[0].HasMessage, Is.False);
            Assert.That(Form.CanSave, Is.False);
        }

        [Test]
        public void NtfysTimeout_IsAWholeNumberInItsRange_AndAnEmptiedTokenFileRemovesItsLine()
        {
            Write(General, GeneralText);
            Write(Ntfy, NtfyText + "tokenFile: /run/secrets/ntfy_token\ntimeoutSeconds: 20\n");
            NtfySettingsViewModel Form = Settings().Ntfy;
            Assert.That(Form.IsNew, Is.False);

            foreach (string Refused in new[] { "0", "121", "ten", "1.5", "" })
            {
                Form.TimeoutSeconds.Text = Refused;
                Assert.That(Form.TimeoutSeconds.MessageText, Is.EqualTo("Error: Enter a whole number of seconds from 1 to 120, such as 30."), Refused);
                Assert.That(Form.CanSave, Is.False, Refused);
            }
            Form.TimeoutSeconds.Text = "45";
            Assert.That(Form.TimeoutSeconds.HasMessage, Is.False);

            Assert.That(Form.CanRemoveTokenFile, Is.True);
            Form.TokenFile.Text = string.Empty;
            Form.Save();
            Assert.That(Form.StatusIsError, Is.False, Form.StatusText);
            Assert.That(Text(Ntfy), Does.Contain("timeoutSeconds: 45").And.Not.Contain("tokenFile"));
        }

        // ---- composed words ----

        [Test]
        public void OutputTosChoices_AreTheFourOutputs_InTheirWords()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            Assert.That(Form.OutputTo.Choices.Select(C => C.Label), Is.EqualTo(new[]
            {
                "Console: show results in the console",
                "Email: check every minute and send new results by email (and with ntfy when Ntfy.yaml enables it)",
                "HtmlFile: write and open a results page",
                "Ntfy: check every minute and send new results with ntfy only (Ntfy.yaml)",
            }));
            Assert.That(Form.OutputTo.Selected!.Value, Is.EqualTo(OutputType.Console));
            Assert.That(Form.OutputTo.Label, Is.EqualTo("_Output"), "the label carries its access key; AccessibleName is the label without it");
            Assert.That(Form.OutputTo.AccessibleName, Is.EqualTo("Output"));
            Assert.That(Form.Title, Is.EqualTo("General settings"));

            // outputTo: UnitTest is kept as the file has it and never offered
            Write(General, GeneralText.Replace("outputTo: Console", "outputTo: UnitTest"));
            GeneralSettingsViewModel Kept = Settings().General;
            Assert.That(Kept.OutputTo.Choices, Has.Count.EqualTo(5));
            Assert.That(Kept.OutputTo.Selected!.Label, Is.EqualTo("UnitTest: for Campertron's tests only (kept as the file has it)"));
        }

        [Test]
        public void D71_AChangedOutputTo_IsNotSavedWhileTheMonitoringsStartWouldRefuseEmailYaml()
        {
            Write(General, GeneralText);
            Write(Path.Join(Folder, "Search.yaml"), SearchText);
            // Email.yaml without a server: the start's own check refuses it
            Write(Email, EmailText.Replace("smtpServer: smtp.example.invalid\n", string.Empty));
            SettingsViewModel All = Settings();
            GeneralSettingsViewModel Form = All.General;
            Assert.That(Form.OutputTo.HasMessage, Is.False, "outputTo: Console needs nothing of Email.yaml");

            Form.OutputTo.Selected = Choice(Form.OutputTo, OutputType.Email);
            List<ConfigDiagnostic> Start = ConfigValidation.MonitoringStartErrors(Folder, OutputType.Email, Now);
            Assert.That(Start, Is.Not.Empty);
            string Problems = string.Join(" ", Start.Select(D => D.FileName + ": " + D.Message).Distinct());
            Assert.That(Form.OutputTo.MessageText, Is.EqualTo("Error: With outputTo: Email, Campertron would refuse to start because of Email.yaml. " + Problems
                + " Fix and save Email.yaml first, then save General.yaml."));
            Assert.That(Form.IsDirty, Is.True);
            Assert.That(Form.CanSave, Is.False, "an error beyond the file blocks Save (D71)");
            Form.Save();
            Assert.That(Form.StatusText, Is.EqualTo("Nothing was saved: fix the errors shown first."));
            Assert.That(Text(General), Is.EqualTo(GeneralText));

            // an outputTo the file already has gets the same message as a warning: saving another setting is not blocked
            Write(General, GeneralText.Replace("outputTo: Console", "outputTo: Email"));
            GeneralSettingsViewModel Saved = Settings().General;
            Assert.That(Saved.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: Email, Campertron refuses to start because of Email.yaml as saved. " + Problems
                + " Fix and save Email.yaml."));
            Saved.AutoRefresh.IsChecked = true;
            Assert.That(Saved.CanSave, Is.True);
        }

        [Test]
        public void D73_AChangedOutputTo_IsNotSavedWhileTheMonitoringHasNoSearchToCheck()
        {
            Write(General, GeneralText);
            Write(Email, EmailText);
            GeneralSettingsViewModel Form = Settings().General;

            Form.OutputTo.Selected = Choice(Form.OutputTo, OutputType.Email);
            string Problems = string.Join(" ", ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Email).Select(D => D.Message).Distinct());
            Assert.That(Problems, Does.StartWith(ConfigValidation.NeedsEnabledSearch));
            Assert.That(Form.OutputTo.MessageText, Is.EqualTo("Error: With outputTo: Email, Campertron would refuse to start: " + Problems + " Then save General.yaml."));
            Assert.That(Form.HasMonitoringSearchProblem, Is.True);
            Assert.That(Form.CanSave, Is.False);

            // with a search to check, the same change can be saved, and the message is gone
            Write(Path.Join(Folder, "Search.yaml"), SearchText);
            Form.RefreshMonitoringCheck();
            Assert.That(Form.OutputTo.HasMessage, Is.False);
            Assert.That(Form.CanSave, Is.True);

            // a saved outputTo: Ntfy without a search: a warning when the form opens, in ntfy's start's words
            File.Delete(Path.Join(Folder, "Search.yaml"));
            Write(Ntfy, NtfyText);
            Write(General, GeneralText.Replace("outputTo: Console", "outputTo: Ntfy"));
            GeneralSettingsViewModel Saved = Settings().General;
            Assert.That(Saved.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: Ntfy, Campertron refuses to start: "
                + string.Join(" ", ConfigValidation.MonitoringSearchErrors(Folder, OutputType.Ntfy).Select(D => D.Message).Distinct())));
        }

        [Test]
        public void D77_ASecretFileTheMonitoringWouldRead_IsJudgedAtOutputTo_AndNeverBlocksSave()
        {
            Write(General, GeneralText);
            Write(Path.Join(Folder, "Search.yaml"), SearchText);
            string Missing = Path.Join(Folder, "no-such-password-file");
            Write(Email, EmailText.Replace("smtpPassword: F0rm-S3cret\n", "smtpPasswordFile: " + Missing + "\n"));
            SettingsViewModel All = Settings();
            GeneralSettingsViewModel Form = All.General;

            Form.OutputTo.Selected = Choice(Form.OutputTo, OutputType.Email);
            (string Words, bool IsProblem) = SecretFileCheck.Describe("smtpPasswordFile", Missing, SecretFileCheck.Inspect(Missing), "password", true)!.Value;
            Assert.That(IsProblem, Is.True);
            Assert.That(Form.OutputTo.MessageText, Is.EqualTo("Error: Email.yaml: " + Words));
            Assert.That(Form.CanSave, Is.True, "a secret file's problem never blocks Save (D75): the file may exist only where Campertron runs");

            // the same file at its own field in Email.yaml's form, after the field's own notice: an error while outputTo is
            // Email, shown or saved, else a warning
            Assert.That(All.Email.SmtpPasswordFile.MessageText, Does.StartWith("Error: ").And.EndWith(" " + Words));
            Form.OutputTo.Selected = Choice(Form.OutputTo, OutputType.Console);
            Assert.That(Form.OutputTo.HasMessage, Is.False);
            Assert.That(All.Email.SmtpPasswordFile.MessageText,
                Does.StartWith("Warning: ").And.EndWith(" " + SecretFileCheck.Describe("smtpPasswordFile", Missing, SecretFileCheck.Inspect(Missing), "password", false)!.Value.Message));
        }

        [Test]
        public void TheNtfyLine_SaysNtfysState_InItsFixedWords_WithItsSeverity()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            Assert.That(Form.NtfyStatus, Is.EqualTo("Ntfy.yaml does not exist, so ntfy is off. To add ntfy notifications, choose Ntfy.yaml above."));
            Assert.That(Form.NtfyStatusSeverity, Is.EqualTo("Info"));

            var Raised = new List<string?>();
            Form.PropertyChanged += (_, e) => Raised.Add(e.PropertyName);
            Form.OutputTo.Selected = Choice(Form.OutputTo, OutputType.Ntfy);
            Assert.That(Raised, Does.Contain(nameof(GeneralSettingsViewModel.NtfyStatus)).And.Contain(nameof(GeneralSettingsViewModel.NtfyStatusSeverity)));
            Assert.That(Form.NtfyStatus, Is.EqualTo("Ntfy.yaml does not exist, and outputTo: Ntfy needs it: choose Ntfy.yaml above, turn Enabled on, enter the server and the topic, and save."));
            Assert.That(Form.NtfyStatusSeverity, Is.EqualTo("Error"));
            Form.Revert();

            Write(Ntfy, NtfyText.Replace("enabled: true", "enabled: false"));
            Form.RefreshMonitoringCheck();
            Assert.That(Form.NtfyStatus, Is.EqualTo("Ntfy.yaml: ntfy is off (enabled: false)."));
            Assert.That(Form.NtfyStatusSeverity, Is.EqualTo("Info"));

            Write(Ntfy, NtfyText);
            Form.RefreshMonitoringCheck();
            Assert.That(Form.NtfyStatus, Is.EqualTo("Ntfy.yaml: ntfy is on. With outputTo Email or Ntfy, Campertron also publishes new results to its topic (not shown here)."));
            Assert.That(Form.NtfyStatus, Does.Not.Contain("formLayerTopic").And.Not.Contain("ntfy.example.invalid"), "never the topic or the server");

            // a problem of Ntfy.yaml: the file's name, the severity and the validation's message
            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\n");
            Form.RefreshMonitoringCheck();
            Assert.That(Form.NtfyStatus, Does.StartWith("Ntfy.yaml: Error: "));
            Assert.That(Form.NtfyStatusSeverity, Is.EqualTo("Error"));
            Assert.That(MessageSeverity.LabelOf(Form.NtfyStatus), Is.EqualTo("Error"), "a UI reads the line's severity from its label too");
        }

        [Test]
        public void EmailYamlsSeverity_FollowsTheOutputTheGeneralFormShows_AndTheOneGeneralYamlHas()
        {
            Write(General, GeneralText);
            Write(Email, EmailText.Replace("smtpServer: smtp.example.invalid\n", string.Empty));
            SettingsViewModel All = Settings();
            Assert.That(All.Email.OutputExplanation, Is.EqualTo("These settings are used only when outputTo is Email; until then a missing setting is a warning, but a value that is not valid still blocks saving."));
            Assert.That(All.Email.SmtpServer.IsWarning, Is.True, All.Email.SmtpServer.MessageText);

            All.General.OutputTo.Selected = Choice(All.General.OutputTo, OutputType.Email);
            Assert.That(All.Email.OutputExplanation, Is.EqualTo("outputTo is Email, so these settings are needed: problems block saving."));
            Assert.That(All.Email.SmtpServer.IsError, Is.True, "what sending needs is an error while outputTo: Email is shown (D71)");

            All.General.Revert();
            Assert.That(All.Email.SmtpServer.IsWarning, Is.True);

            Write(General, GeneralText.Replace("outputTo: Console", "outputTo: Email"));
            SettingsViewModel Saved = Settings();
            Saved.General.OutputTo.Selected = Choice(Saved.General.OutputTo, OutputType.Console);
            Assert.That(Saved.Email.OutputExplanation, Is.EqualTo("outputTo is Email in the saved General.yaml, so these settings are needed until you save another outputTo there: problems block saving."));
            Assert.That(Saved.Email.SmtpServer.IsError, Is.True, "the saved outputTo: Email still needs them");
        }

        [Test]
        public void NtfysModeLine_SaysWhatNtfyDoesForTheOutput()
        {
            Write(General, GeneralText);
            SettingsViewModel All = Settings();
            NtfySettingsViewModel Form = All.Ntfy;
            Assert.That(Form.IsNew, Is.True, "no Ntfy.yaml: a new file, written only by Save");
            Assert.That(Form.ModeText, Is.EqualTo("outputTo is Console: nothing is sent. ntfy is used only by the unattended monitoring, with outputTo: Ntfy (ntfy only) or outputTo: Email (email, and ntfy while Enabled is on)."));

            All.General.OutputTo.Selected = Choice(All.General.OutputTo, OutputType.Email);
            Assert.That(Form.ModeText, Is.EqualTo("outputTo is Email: the unattended monitoring sends emails only, because Enabled is off."));
            Form.Enabled.IsChecked = true;
            Assert.That(Form.ModeText, Is.EqualTo("outputTo is Email: the unattended monitoring sends emails and, while Enabled is on, ntfy notifications too."));

            All.General.OutputTo.Selected = Choice(All.General.OutputTo, OutputType.Ntfy);
            Assert.That(Form.ModeText, Is.EqualTo("outputTo is Ntfy: the unattended monitoring sends new openings with ntfy only. ntfy is required, so its problems block saving."));
            Assert.That(File.Exists(Ntfy), Is.False, "nothing is written until Save");
            Assert.That(Form.Priority.Choices.Select(C => C.Label), Is.EqualTo(new[]
            {
                "Min: no sound or vibration", "Low: no sound, shown in the list", "Default: the phone's usual notification", "High: more noticeable", "Max: as noticeable as possible",
            }));
        }

        // ---- the form's own life: revert, reload, a change on disk, notifications ----

        [Test]
        public void RevertReloadAndAChangeOnDisk_SayWhatHappened_InFixedWords()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            Form.AutoRefresh.IsChecked = true;
            Assert.That(Form.RefreshExplanation, Is.EqualTo("At each start, Campertron refreshes the campground data when the interval has passed since the last refresh."));
            Form.Revert();
            Assert.That(Form.StatusText, Is.EqualTo("Your changes to General.yaml were reverted."));
            Assert.That(Form.AutoRefresh.IsChecked, Is.False);
            Assert.That(Form.RefreshExplanation, Is.EqualTo("The campground data is not refreshed automatically; the interval is not used. Refresh it from the console's menu."));

            Form.Reload();
            Assert.That(Form.StatusText, Is.EqualTo("Reloaded General.yaml."));

            // a clean form follows a change on disk; an edited one shows a conflict and blocks Save until it is resolved
            Write(General, GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 14"));
            Form.CheckForExternalChanges();
            Assert.That(Form.StatusText, Is.EqualTo("General.yaml was changed on disk and has been reloaded."));
            Assert.That(Form.RefreshInterval.Text, Is.EqualTo("14"));

            Form.AutoRefresh.IsChecked = true;
            Write(General, GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 21"));
            Form.CheckForExternalChanges();
            Assert.That(Form.HasConflict, Is.True);
            Assert.That(Form.IsEditable, Is.False);
            Assert.That(Form.CanSave, Is.False);
            Assert.That(Form.ApplyMineLabel, Is.EqualTo("Apply my changes to the new version"));
            Form.UseTheirs();
            Assert.That(Form.StatusText, Is.EqualTo("Your changes were discarded; General.yaml shows the version on disk."));
            Assert.That(Form.RefreshInterval.Text, Is.EqualTo("21"));
        }

        [Test]
        public void AForm_SaysWhatChanged_ToWhoeverListens_AndWorksWithNoListener()
        {
            Write(General, GeneralText);
            GeneralSettingsViewModel Form = Settings().General;
            // a host that renders a form once listens to nothing
            Form.RefreshInterval.Text = "x";
            Assert.That(Form.RefreshInterval.IsError, Is.True);
            Form.RefreshInterval.Text = "30";

            var Field = new List<string?>();
            var Whole = new List<string?>();
            ((INotifyPropertyChanged)Form.RefreshInterval).PropertyChanged += (_, e) => Field.Add(e.PropertyName);
            Form.PropertyChanged += (_, e) => Whole.Add(e.PropertyName);
            Form.RefreshInterval.Text = "31";
            Assert.That(Field, Does.Contain("Text").And.Contain("IsChanged"));
            Assert.That(Whole, Does.Contain("IsDirty").And.Contain("CanSave").And.Contain("PreviewLines"));
            Field.Clear();
            Form.RefreshInterval.Text = "31";
            Assert.That(Field, Is.Empty, "the same value is no change");
        }

        [Test]
        public void TwoFormsOfTwoFolders_ShareNothing()
        {
            string Other = Path.Join(Folder, "other");
            Directory.CreateDirectory(Other);
            Write(General, GeneralText);
            Write(Path.Join(Other, "General.yaml"), GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 5"));
            GeneralSettingsViewModel First = Settings().General;
            GeneralSettingsViewModel Second = new SettingsViewModel(Other, () => Now).General;

            First.RefreshInterval.Text = "bad";
            First.OutputTo.Selected = Choice(First.OutputTo, OutputType.HtmlFile);
            Assert.That(Second.RefreshInterval.Text, Is.EqualTo("5"));
            Assert.That(Second.RefreshInterval.HasMessage, Is.False);
            Assert.That(Second.IsDirty, Is.False);
            Assert.That(Second.OutputTo.Selected!.Value, Is.EqualTo(OutputType.Console));
            Assert.That(ReferenceEquals(First.OutputTo.Choices[0], Second.OutputTo.Choices[0]), Is.True, "the offered choices are fixed values, the same for every form");
            Assert.That(ReferenceEquals(First.OutputTo.Choices, Second.OutputTo.Choices), Is.False, "each form has its own list of them");
        }
    }
}
