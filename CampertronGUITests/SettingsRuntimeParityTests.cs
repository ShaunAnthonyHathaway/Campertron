using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // D71: Settings > Campertron never saves a configuration that the console's unattended monitoring would refuse at its
    // start. After every save these tests make, the saved General.yaml, Email.yaml and Ntfy.yaml pass the monitoring's own
    // start checks (ConfigValidation.ThrowIfEmailOutputRefused and ThrowIfNtfyOutputRefused on the files as the loader reads
    // them) for the saved outputTo, or the save was not offered. The recipient rows: an empty row is never written and never
    // an error, an address is written without the spaces around it, an address that is not valid blocks Save in every
    // output, email mode needs one recipient, and a repeated address is a warning. Each test has its own settings folder;
    // nothing is sent, searched or published.
    public class SettingsRuntimeParityTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string Topic = "parityTopic0123456789abcdef";
        private const string NoAddress = "sendToAddressList has no address: add at least one recipient.";

        private string Folder = string.Empty;
        private string General => Path.Join(Folder, "General.yaml");
        private string Email => Path.Join(Folder, "Email.yaml");
        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");
        private const string EmailText = "# Campertron email settings\nsmtpServer: mail.example.org   # my provider\nsmtpPort: 587\nsmtpUsername: camper\n" +
            "smtpPassword: pass-not-real\nsendToAddressList:\n  # family\n  - first@example.org # one\nsendFromAddress: camper@example.org\n# end\n";

        private static string GeneralText(string Output) =>
            "# general\noutputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "settings-parity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(General, GeneralText("Console"));
            File.WriteAllText(Email, EmailText);
            // an enabled search with a campground, so the monitoring's start has a search to check (D73) and these tests
            // judge Email.yaml and Ntfy.yaml alone
            File.WriteAllText(Path.Join(Folder, "Watch.yaml"), WatchSearch);
        }

        internal const string WatchSearch = "displayName: Watch\nautoRun: true\ncampgroundID: '232445'\n";

        [TearDown]
        public void DeleteFolder()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        private (MainWindow Window, SettingsViewModel Settings) ShowSettings()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            return (Window, ViewModel.Settings!);
        }

        private static Control Editor(MainWindow Window, string Name) => In<Control>(Named<SettingsView>(Window, "SettingsContent"), Name);

        private static void Show(MainWindow Window, SettingsViewModel Settings, string File)
        {
            Settings.IsGeneralSelected = File == "General";
            Settings.IsEmailSelected = File == "Email";
            Settings.IsNtfySelected = File == "Ntfy";
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Choose(SettingsViewModel Settings, OutputType Output)
        {
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(Output));
            Dispatcher.UIThread.RunJobs();
        }

        // the recipient rows as the user leaves them: the old rows removed, then one row for each value, typed in
        private static void SetRecipients(EmailSettingsViewModel E, params string[] Values)
        {
            while (E.Recipients.Rows.Count > 0)
            {
                E.Recipients.Rows[0].RemoveCommand!.Execute(null);
            }
            foreach (string Value in Values)
            {
                E.Recipients.AddRow();
                E.Recipients.Rows[^1].Text = Value;
            }
            Dispatcher.UIThread.RunJobs();
        }

        private List<string> SavedRecipients() => Yaml.LoadSettingsText<EmailConfig>(File.ReadAllText(Email)).SendToAddressList ?? new List<string>();

        // what the console's unattended monitoring says at its start about the saved files, for the saved outputTo, with its
        // own checks: null when it starts (Console and HtmlFile never look at Email.yaml or Ntfy.yaml)
        private string? MonitoringStart()
        {
            OutputType Output = Yaml.LoadSettingsText<GeneralConfig>(File.ReadAllText(General)).OutputTo;
            try
            {
                if (Output == OutputType.Email)
                {
                    ConfigValidation.ThrowIfEmailOutputRefused(Yaml.LoadSettingsText<EmailConfig>(File.ReadAllText(Email)));
                }
                if (Output == OutputType.Email || Output == OutputType.Ntfy)
                {
                    bool Exists = File.Exists(Ntfy);
                    ConfigValidation.ThrowIfNtfyOutputRefused(Exists ? Yaml.LoadSettingsText<NtfyConfig>(File.ReadAllText(Ntfy)) : null, Output == OutputType.Ntfy, Exists);
                }
                return null;
            }
            catch (ConfigValidationException Refused)
            {
                return Refused.Message;
            }
        }

        // every recipient state the editor can save passes the monitoring's start: with outputTo: Email directly, with
        // outputTo: Console once General.yaml is switched to Email, which the General form offers only when it would start.
        // The rows: an empty row and one of spaces are left out, addresses are trimmed, a repeated address is a warning,
        // an address that is not valid never saves, and no recipient at all saves only outside the Email output
        [AvaloniaTest]
        public void EveryRecipientStateTheEditorSaves_PassesTheMonitoringsStart([Values("Console", "Email")] string Output)
        {
            File.WriteAllText(General, GeneralText(Output));
            bool EmailOutput = Output == "Email";
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "Email");
            EmailSettingsViewModel E = Settings.Email;
            Button Save = In<Button>(Editor(Window, "EmailEditor"), "SaveButton");
            var States = new (string[] Rows, bool Saves, string[] Saved, string? ListMessage, (int Row, string Message)? RowMessage)[]
            {
                (new[] { "second@example.org", "" }, true, new[] { "second@example.org" }, null, null),
                (new[] { "", "second@example.org", "   " }, true, new[] { "second@example.org" }, null, null),
                (new[] { "  second@example.org  ", "third@example.org\t" }, true, new[] { "second@example.org", "third@example.org" }, null, null),
                (new[] { "second@example.org", "Second@Example.org" }, true, new[] { "second@example.org", "Second@Example.org" }, null,
                    (1, "Warning: sendToAddressList item 2 repeats an address of item 1.")),
                (new[] { "second@example.org", "nobody" }, false, Array.Empty<string>(), null, (1, "Error: sendToAddressList item 2 is not a valid email address.")),
                (new[] { "", "nobody", "" }, false, Array.Empty<string>(), null, (1, "Error: sendToAddressList item 1 is not a valid email address.")),
                (new[] { "", "   " }, !EmailOutput, Array.Empty<string>(), (EmailOutput ? "Error: " : "Warning: ") + NoAddress, null),
                (Array.Empty<string>(), !EmailOutput, Array.Empty<string>(), (EmailOutput ? "Error: " : "Warning: ") + NoAddress, null),
            };
            foreach (var State in States)
            {
                string Name = "[" + string.Join("|", State.Rows) + "]";
                SetRecipients(E, State.Rows);
                Assert.That(Save.IsEffectivelyEnabled, Is.EqualTo(State.Saves), Name);
                // the list's own message (beside the editor's note about comments removed with a row)
                if (State.ListMessage is string ListMessage)
                {
                    Assert.That((E.Recipients.Severity, E.Recipients.Message), Is.EqualTo((ListMessage.Split(':')[0], E.Recipients.Message)), Name);
                    Assert.That(E.Recipients.Message, Does.Contain(NoAddress), Name);
                }
                else
                {
                    Assert.That(E.Recipients.IsError || (E.Recipients.Message ?? string.Empty).Contains("has no address"), Is.False, Name);
                }
                for (int i = 0; i < E.Recipients.Rows.Count; i++)
                {
                    string Expected = State.RowMessage is (int Row, string Message) && Row == i ? Message : string.Empty;
                    Assert.That(E.Recipients.Rows[i].MessageText, Is.EqualTo(Expected), Name + " row " + (i + 1));
                }
                Assert.That(E.Recipients.Rows.Where(R => string.IsNullOrWhiteSpace(R.Text)).Select(R => R.HasMessage), Is.All.False, Name + ": an empty row is never an error");
                if (State.Saves)
                {
                    Click(Save);
                    Assert.That(E.StatusIsError, Is.False, Name);
                    Assert.That(SavedRecipients(), Is.EqualTo(State.Saved), Name);
                    Assert.That(File.ReadAllText(Email), Does.Not.Contain("''").And.Not.Match(@"\n\s*-\s*\n"), Name + ": no empty item is written");
                    Assert.That(MonitoringStart(), Is.Null, Name + ": the saved configuration starts");
                    if (!EmailOutput)
                    {
                        // switching to outputTo: Email is offered exactly when the monitoring would start with this Email.yaml
                        Show(Window, Settings, "General");
                        Choose(Settings, OutputType.Email);
                        bool Starts = ConfigValidation.MonitoringStartErrors(Folder, OutputType.Email, DateTime.Now).Count == 0;
                        Assert.That(In<Button>(Editor(Window, "GeneralEditor"), "SaveButton").IsEffectivelyEnabled, Is.EqualTo(Starts), Name);
                        Assert.That(Settings.General.OutputTo.IsError, Is.EqualTo(!Starts), Name);
                        Choose(Settings, OutputType.Console);
                        Show(Window, Settings, "Email");
                    }
                }
                else
                {
                    // Save refuses whoever calls it, and nothing is written
                    Settings.Email.Save();
                    Assert.That(E.StatusIsError, Is.True, Name);
                    Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText), Name);
                    Assert.That(MonitoringStart(), Is.Null, Name + ": nothing was saved");
                }
                if (E.IsDirty)
                {
                    Click(In<Button>(Editor(Window, "EmailEditor"), "RevertButton"));
                }
                File.WriteAllText(Email, EmailText);
                Settings.CheckForExternalChanges();
                Dispatcher.UIThread.RunJobs();
                Assert.That(E.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "first@example.org" }), Name + ": back to the file");
            }
            Window.Close();
        }

        // the reported defect: with outputTo: Console the user added a recipient row left empty and saved, then chose outputTo:
        // Email, and the console refused to start ("sendToAddressList item 2 is not a valid email address"). Now the empty
        // row is never written, so outputTo: Email can be saved and starts. (Fails on eb9d9c7, which wrote the empty item.)
        [AvaloniaTest]
        public void TheReportedDefect_AnEmptyRecipientRow_NeverReachesEmailYaml()
        {
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "Email");
            Control EmailEditor = Editor(Window, "EmailEditor");
            Click(In<Button>(EmailEditor, "AddRecipientButton"));
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Email.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "first@example.org", "" }));
            Assert.That(Settings.Email.IsDirty, Is.False, "an empty row is no change");
            Click(In<Button>(EmailEditor, "AddRecipientButton"));
            Settings.Email.Recipients.Rows[2].Text = "second@example.org";
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(EmailEditor, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace("  - first@example.org # one\n", "  - first@example.org # one\n  - second@example.org\n")));
            Assert.That(File.ReadAllText(Email + ".bak"), Is.EqualTo(EmailText));

            Show(Window, Settings, "General");
            Choose(Settings, OutputType.Email);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False);
            Click(In<Button>(Editor(Window, "GeneralEditor"), "SaveButton"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText("Email")));
            Assert.That(MonitoringStart(), Is.Null, "the console's email mode starts with the saved files");
            Window.Close();
        }

        // outputTo: Email or Ntfy is saved only when the monitoring would start with Email.yaml and Ntfy.yaml as saved; the
        // message at outputTo names the file and the setting, never a value. Fixed and saved there, outputTo can be saved.
        // An outputTo that General.yaml already has gets the same message as a warning when another program broke a file
        [AvaloniaTest]
        public void OutputTo_IsSavedOnlyWhenTheMonitoringWouldStart()
        {
            const string Secret = "S3cretValue";
            File.WriteAllText(Email, EmailText.Replace("  - first@example.org # one\n", "  - first@example.org # one\n  - '" + Secret + "'\n").Replace("pass-not-real", Secret));
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "General");
            Control G = Editor(Window, "GeneralEditor");
            Choose(Settings, OutputType.Email);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Error: With outputTo: Email, Campertron would refuse to start because of Email.yaml. "
                + "Email.yaml: sendToAddressList item 2 is not a valid email address. Fix and save Email.yaml first, then save General.yaml."));
            Assert.That(AutomationProperties.GetHelpText(In<ComboBox>(G, "OutputToComboBox")), Is.EqualTo(Settings.General.OutputTo.MessageText));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False);
            Settings.General.Save();
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText("Console")), "Save refuses whoever calls it");
            Assert.That(Settings.General.StatusText, Is.EqualTo("Nothing was saved: fix the errors shown first."));

            // the Email editor follows the outputTo shown: the item is an error; removed and saved, General can be saved
            Show(Window, Settings, "Email");
            Assert.That(Settings.Email.Recipients.Rows[1].MessageText, Is.EqualTo("Error: sendToAddressList item 2 is not a valid email address."));
            Settings.Email.Recipients.Rows[1].RemoveCommand!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(Editor(Window, "EmailEditor"), "SaveButton"));
            Show(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False);
            Click(In<Button>(G, "SaveButton"));
            Assert.That(MonitoringStart(), Is.Null);

            // another program breaks Email.yaml: the saved outputTo gets a warning; another setting can still be saved
            File.WriteAllText(Email, EmailText.Replace("smtpPort: 587", "smtpPort: 70000"));
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: Email, Campertron refuses to start because of Email.yaml as saved. "
                + "Email.yaml: smtpPort is not between 1 and 65535. Fix and save Email.yaml."));
            Settings.General.AutoRefresh.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Does.Contain("autoRefresh: true"));

            // outputTo: Ntfy needs Ntfy.yaml with ntfy on: created and saved in its editor, then General can be saved
            File.WriteAllText(Email, EmailText);
            Choose(Settings, OutputType.Ntfy);
            Assert.That(Settings.General.OutputTo.MessageText, Does.StartWith("Error: With outputTo: Ntfy, Campertron would refuse to start because of Ntfy.yaml. Ntfy.yaml: The file does not exist"));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False);
            Show(Window, Settings, "Ntfy");
            NtfySettingsViewModel N = Settings.Ntfy;
            N.Enabled.IsChecked = true;
            N.Server.Text = "https://ntfy.example.invalid";
            N.Topic.Text = Topic;
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(Editor(Window, "NtfyEditor"), "SaveButton"));
            Show(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False);
            Click(In<Button>(G, "SaveButton"));
            Assert.That(MonitoringStart(), Is.Null);

            List<string> Texts = SettingsEditorTests.Exposed(Window);
            Assert.That(Texts.Count, Is.GreaterThan(100), "the scan saw the window");
            Assert.That(Texts.Where(T => T.Contains(Secret, StringComparison.Ordinal)), Is.Empty, "no value in any message");
            Window.Close();
        }

        // the saved outputTo counts while the form shows another one: with outputTo: Email saved, removing every recipient
        // is an error even after choosing Console (unsaved); with outputTo: Ntfy saved, turning ntfy off is
        [AvaloniaTest]
        public void TheSavedOutputTo_StillCounts_WhileTheFormShowsAnother()
        {
            File.WriteAllText(General, GeneralText("Email"));
            File.WriteAllText(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "General");
            Choose(Settings, OutputType.Console);
            Show(Window, Settings, "Email");
            Control EmailEditor = Editor(Window, "EmailEditor");
            Assert.That(In<TextBlock>(EmailEditor, "OutputExplanation").Text, Does.StartWith("outputTo is Email in the saved General.yaml"));
            SetRecipients(Settings.Email);
            Assert.That(Settings.Email.Recipients.MessageText, Does.StartWith("Error: " + NoAddress));
            Assert.That(In<Button>(EmailEditor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Click(In<Button>(EmailEditor, "RevertButton"));

            Show(Window, Settings, "General");
            Click(In<Button>(Editor(Window, "GeneralEditor"), "RevertButton"));
            Choose(Settings, OutputType.Ntfy);
            Click(In<Button>(Editor(Window, "GeneralEditor"), "SaveButton"));
            Choose(Settings, OutputType.Console);
            Show(Window, Settings, "Ntfy");
            Control NtfyEditor = Editor(Window, "NtfyEditor");
            Settings.Ntfy.Enabled.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Ntfy.Enabled.MessageText, Does.StartWith("Error: enabled is false, and outputTo: Ntfy needs ntfy"));
            Assert.That(In<TextBlock>(NtfyEditor, "NtfyModeText").Text, Does.StartWith("outputTo is Ntfy in the saved General.yaml"));
            Assert.That(In<Button>(NtfyEditor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Assert.That(MonitoringStart(), Is.Null, "the saved files still start");
            Window.Close();
        }

        // an existing Email.yaml that email mode refuses is shown as such when it is opened (the item's own message, and the
        // warning at the saved outputTo); with any change its empty item is left out of the save (the preview shows it), the
        // comments and the rest stay byte for byte and the backup is the old file
        [AvaloniaTest]
        public void AnEmailYamlTheStartRefuses_IsShownWhenOpened_AndAnyChangeLeavesItsEmptyItemOut()
        {
            File.WriteAllText(General, GeneralText("Email"));
            string Broken = EmailText.Replace("  - first@example.org # one\n", "  - first@example.org # one\n  - ''\n  - third@example.org\n");
            File.WriteAllText(Email, Broken);
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Warning: With outputTo: Email, Campertron refuses to start because of Email.yaml as saved. "
                + "Email.yaml: sendToAddressList item 2 is empty. Fix and save Email.yaml."));
            Show(Window, Settings, "Email");
            EmailSettingsViewModel E = Settings.Email;
            Assert.That(E.Recipients.Rows.Select(R => R.MessageText), Is.EqualTo(new[] { "", "Error: sendToAddressList item 2 is empty.", "" }));

            E.SmtpServer.Text = "smtp.example.org";
            Dispatcher.UIThread.RunJobs();
            Assert.That(E.Recipients.Rows.Select(R => R.HasMessage), Is.All.False, "the empty item is left out");
            Assert.That(E.PreviewText, Does.Contain("-   - ''"));
            Click(In<Button>(Editor(Window, "EmailEditor"), "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(Broken.Replace("  - ''\n", "").Replace("mail.example.org   # my provider", "smtp.example.org   # my provider")));
            Assert.That(File.ReadAllText(Email + ".bak"), Is.EqualTo(Broken));
            Assert.That(MonitoringStart(), Is.Null);
            Show(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "the General form checked the saved Email.yaml again");
            Window.Close();
        }

        // the recipient and tag lists say how their rows work, label Add, and move the keyboard focus to a row just added;
        // an empty tag row is never written or an error, a tag is trimmed, a tag that is not valid blocks Save also while
        // ntfy is off
        [AvaloniaTest]
        public void TheLists_ExplainTheirRows_AndAddMovesTheFocusToTheNewRow()
        {
            File.WriteAllText(Ntfy, "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntags:\n  - tent\n");
            var (Window, Settings) = ShowSettings();
            Show(Window, Settings, "Email");
            Control EmailEditor = Editor(Window, "EmailEditor");
            Assert.That(In<TextBlock>(EmailEditor, "RecipientsHelp").Text, Is.EqualTo("One email address per row. Empty rows are ignored."));
            Assert.That(Settings.Email.Recipients.Label, Is.EqualTo("_Recipients (sendToAddressList)"));
            Button AddRecipient = In<Button>(EmailEditor, "AddRecipientButton");
            Assert.That(AutomationProperties.GetName(AddRecipient), Is.EqualTo("Add recipient"));
            Assert.That(AutomationProperties.GetHelpText(AddRecipient), Is.EqualTo("Adds an empty row at the end and moves the focus to it. One email address per row. Empty rows are ignored."));
            Click(AddRecipient);
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            List<TextBox> Boxes = In<ItemsControl>(EmailEditor, "RecipientsList").GetVisualDescendants().OfType<TextBox>().Where(b => b.Name == "RecipientBox").ToList();
            Assert.That(Boxes, Has.Count.EqualTo(2));
            Assert.That(Focused(Window), Is.SameAs(Boxes[1]), "the keyboard goes on in the new row");
            Assert.That(AutomationProperties.GetName(Boxes[1]), Is.EqualTo("Recipient 2"));
            Assert.That(In<ItemsControl>(EmailEditor, "RecipientsList").GetVisualDescendants().OfType<Button>().Where(b => b.Name == "RecipientRemoveButton").Select(AutomationProperties.GetName),
                Is.EqualTo(new[] { "Remove recipient 1", "Remove recipient 2" }));

            Show(Window, Settings, "Ntfy");
            Control NtfyEditor = Editor(Window, "NtfyEditor");
            NtfySettingsViewModel N = Settings.Ntfy;
            Assert.That(In<TextBlock>(NtfyEditor, "TagsHelp").Text, Is.EqualTo("One tag per row. Empty rows are ignored."));
            Click(In<Button>(NtfyEditor, "AddTagButton"));
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            List<TextBox> Tags = In<ItemsControl>(NtfyEditor, "NtfyTagsList").GetVisualDescendants().OfType<TextBox>().Where(b => b.Name == "TagBox").ToList();
            Assert.That(Focused(Window), Is.SameAs(Tags[1]));
            Assert.That((N.IsDirty, N.Tags.Rows[1].HasMessage), Is.EqualTo((false, false)), "an empty tag row is no change and no error");
            Tags[1].Text = "two words";
            Dispatcher.UIThread.RunJobs();
            Assert.That(N.Tags.Rows[1].MessageText, Does.StartWith("Error: tags item 2 must be"), "an error also while ntfy is off");
            Assert.That(In<Button>(NtfyEditor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Tags[1].Text = "  camping  ";
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(NtfyEditor, "SaveButton"));
            Assert.That(File.ReadAllText(Ntfy), Does.EndWith("tags:\n  - tent\n  - camping\n"));
            Window.Close();
        }
    }
}
