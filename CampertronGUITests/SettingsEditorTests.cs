using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Base;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The Settings tab (CM5, decision D43): editing General.yaml and Email.yaml in the real window on the headless platform.
    // Each test has its own settings folder (with a copy of the fixture database for the lookup tabs) and a window whose
    // startup loader returns it, so the shared fixture's files are never edited. The password is a sentinel that must not
    // appear in any text, tooltip, accessible name, help text or accessibility value of the window.
    public class SettingsEditorTests
    {
        private const string Secret = "S3ntinel-Gui-Secret-5e1d";
        private const string NewSecret = "N3w-Gui-Sentinel-c4a7";
        private const int SettingsTab = MainWindow.SettingsTabIndex;

        private const string GeneralText = "# Campertron settings\noutputTo: Console   # where results go\n\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC\nautoRefresh: false\n# end\n";

        private const string EmailText = "# SMTP settings\nsmtpServer: 'mail.example.org' # host\nsmtpPort: 587\nsmtpUsername: camper\n" +
            "smtpPassword: " + Secret + " # keep it safe\nsendToAddressList:\n  # family\n  - first@example.org # one\n  - second@example.org\n" +
            "sendFromAddress: camper@example.org\n";

        private string Folder = string.Empty;
        private string General => Path.Join(Folder, "General.yaml");
        private string Email => Path.Join(Folder, "Email.yaml");

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(General, GeneralText);
            File.WriteAllText(Email, EmailText);
        }

        [TearDown]
        public void DeleteFolder()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        // the real window with this test's settings folder, on the Settings tab
        private (MainWindow Window, MainWindowViewModel ViewModel, SettingsViewModel Settings) ShowSettings()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            return (Window, ViewModel, ViewModel.Settings!);
        }

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        private static Control Editor(MainWindow Window, string Name) => In<Control>(Named<SettingsView>(Window, "SettingsContent"), Name);

        private static void Type(TextBox Box, string Text)
        {
            Box.Text = Text;
            Dispatcher.UIThread.RunJobs();
        }

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        // every text the window shows or gives to accessibility clients: text blocks, text boxes (not the password box,
        // whose text is the password it edits), tooltips, accessible names and help texts, and the automation peers'
        // names, help texts and values (the password box's peer included)
        internal static List<string> Exposed(Window Window, params Control[] Except)
        {
            var Texts = new List<string>();
            foreach (Visual V in Window.GetVisualDescendants())
            {
                // D69: the Ntfy.yaml editor shows ntfy's topic in its own boxes and preview; the rest of the window must not
                if (V is TextPresenter || Except.Any(E => ReferenceEquals(V, E) || V.GetVisualAncestors().Contains(E)))
                {
                    continue;
                }
                if (V is TextBlock Block && Block.Text != null)
                {
                    Texts.Add(Block.Text);
                }
                if (V is TextBox Box && V is not SecretTextBox && Box.Text != null)
                {
                    Texts.Add(Box.Text);
                }
                if (V is ContentControl Content && Content.Content is string S)
                {
                    Texts.Add(S);
                }
                if (V is Control C)
                {
                    Texts.Add(ToolTip.GetTip(C) as string ?? string.Empty);
                    Texts.Add(AutomationProperties.GetName(C) ?? string.Empty);
                    Texts.Add(AutomationProperties.GetHelpText(C) ?? string.Empty);
                    Texts.Add(C.Name ?? string.Empty);
                    AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(C);
                    Texts.Add(Peer.GetName() ?? string.Empty);
                    Texts.Add(Peer.GetHelpText() ?? string.Empty);
                    if (Peer is IValueProvider Value)
                    {
                        Texts.Add(Value.Value ?? string.Empty);
                    }
                }
            }
            return Texts;
        }

        private static void AssertNoSecret(Window Window, string When)
        {
            List<string> Texts = Exposed(Window);
            Assert.That(Texts.Count, Is.GreaterThan(100), "the scan saw the window");
            Assert.That(Texts.Where(T => T.Contains(Secret, StringComparison.Ordinal) || T.Contains(NewSecret, StringComparison.Ordinal)), Is.Empty, When);
        }

        // ---- the tab ----

        // 59, 60, 74
        [AvaloniaTest]
        public void SettingsCampertron_IsTheFirstSettingsTab_AndOpensBothFiles()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _, Settings) = ShowSettings();

            Assert.That(Tabs(Window).Items.Cast<TabItem>().Select(t => t.Header), Is.EqualTo(AllTabs));
            Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(MainWindow.SettingsTabIndex));
            Assert.That(Headers(SettingsTabs(Window)), Is.EqualTo(SettingsTabHeaders));
            Assert.That(SettingsTabs(Window).SelectedIndex, Is.EqualTo(MainWindow.CampertronSettingsTabIndex), "Campertron is the Settings tab shown first");
            Assert.That(Named<SettingsView>(Window, "SettingsContent").IsEffectivelyVisible, Is.True);
            Assert.That(Settings.ConfigPath, Is.EqualTo(Folder));
            Assert.That(Settings.General.IsEditable && Settings.Email.IsEditable, Is.True);
            Assert.That(In<RadioButton>(Named<SettingsView>(Window, "SettingsContent"), "GeneralFileButton").IsChecked, Is.True);
            Assert.That(Editor(Window, "GeneralEditor").IsEffectivelyVisible, Is.True);
            Assert.That(Editor(Window, "EmailEditor").IsEffectivelyVisible, Is.False);
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Editor(Window, "EmailEditor").IsEffectivelyVisible, Is.True);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText));
        }

        // 26, 27, 31, 33: every General.yaml setting binds; edits become targeted changes; a save keeps the rest
        [AvaloniaTest]
        public void TheGeneralForm_BindsEverySetting_AndSavesOnlyTheEditedValues()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            var Output = In<ComboBox>(G, "OutputToComboBox");
            var Auto = In<CheckBox>(G, "AutoRefreshCheckBox");
            var Interval = In<TextBox>(G, "RefreshIntervalBox");
            var Last = In<TextBox>(G, "LastRefreshBox");

            Assert.That(((SettingChoice)Output.SelectedItem!).Value, Is.EqualTo(OutputType.Console));
            Assert.That(Output.Items.Cast<SettingChoice>().Select(c => c.Value), Is.EqualTo(new object[] { OutputType.Console, OutputType.Email, OutputType.HtmlFile, OutputType.Ntfy }));
            Assert.That(Auto.IsChecked, Is.False);
            Assert.That(Interval.Text, Is.EqualTo("30"));
            Assert.That(Last.Text, Is.EqualTo("2020-01-02T03:04:05.0000000Z"));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False, "nothing to save");

            Output.SelectedItem = Output.Items.Cast<SettingChoice>().Single(c => c.Value.Equals(OutputType.HtmlFile));
            Auto.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Type(Interval, "7.5");
            Assert.That(Settings.General.OutputTo.IsChanged && Settings.General.AutoRefresh.IsChanged && Settings.General.RefreshInterval.IsChanged, Is.True);
            Assert.That(Settings.General.LastRefresh.IsChanged, Is.False);
            Assert.That(In<SelectableTextBlock>(G, "PreviewText").Text, Is.EqualTo(string.Join("\n", new[]
            {
                "  # Campertron settings", "- outputTo: Console   # where results go", "+ outputTo: HtmlFile   # where results go", "  ",
                "- refreshRidbDataDayInterval: 30", "+ refreshRidbDataDayInterval: 7.5", "  lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC",
                "- autoRefresh: false", "+ autoRefresh: true", "  # end",
            })));
            Assert.That(In<TextBlock>(G, "PreviewSummary").Text, Is.EqualTo("Changes: outputTo, refreshRidbDataDayInterval, autoRefresh"));

            Click(In<Button>(G, "SaveButton"));
            Assert.That(In<TextBlock>(G, "StatusText").Text, Is.EqualTo("Saved General.yaml. The previous version is in General.yaml.bak."));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText.Replace("outputTo: Console", "outputTo: HtmlFile").Replace("Interval: 30", "Interval: 7.5").Replace("autoRefresh: false", "autoRefresh: true")));
            Assert.That(File.ReadAllText(General + ".bak"), Is.EqualTo(GeneralText));
            Assert.That(Settings.General.IsDirty, Is.False);
            Assert.That(In<Border>(G, "PreviewCard").IsVisible, Is.False);

            // a new timestamp, in UTC
            Type(Last, "2026-01-31 08:30");
            Assert.That(Settings.General.CanSave, Is.True);
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Does.Contain("lastRidbDataRefresh: '2026-01-31T08:30:00.0000000Z' # UTC"));
            Window.Close();
        }

        // 28
        [AvaloniaTest]
        public void OutputToUnitTest_IsKeptAndShown_ButNotOffered()
        {
            File.WriteAllText(General, "outputTo: UnitTest\nautoRefresh: false\n");
            var (Window, _, Settings) = ShowSettings();
            ComboBox Output = In<ComboBox>(Editor(Window, "GeneralEditor"), "OutputToComboBox");

            List<SettingChoice> Choices = Output.Items.Cast<SettingChoice>().ToList();
            Assert.That(Choices.Select(c => c.Value), Is.EqualTo(new object[] { OutputType.Console, OutputType.Email, OutputType.HtmlFile, OutputType.Ntfy, OutputType.UnitTest }));
            Assert.That(Output.SelectedItem, Is.SameAs(Choices[4]));
            Assert.That(Choices[4].Label, Is.EqualTo("UnitTest: for Campertron's tests only (kept as the file has it)"));
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Info: outputTo: UnitTest is for Campertron's tests: a start with it shows and searches nothing."));
            Assert.That(Settings.General.IsDirty, Is.False);
            Window.Close();
        }

        // 29, 30, 64, 65: problems are shown at their field (severity in words, also as accessible help text); errors block
        // the save; the refresh explanation follows the check box
        [AvaloniaTest]
        public void Problems_AreShownAtTheirField_AndErrorsBlockTheSave()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            TextBox Interval = In<TextBox>(G, "RefreshIntervalBox");
            Button Save = In<Button>(G, "SaveButton");
            TextBlock Explanation = In<TextBlock>(G, "RefreshExplanation");
            Assert.That(Explanation.Text, Does.StartWith("The campground data is not refreshed automatically"));

            Type(Interval, "about a week");
            Assert.That(Settings.General.RefreshInterval.MessageText, Is.EqualTo("Error: Enter a number of days, such as 30 or 7.5."));
            Assert.That(AutomationProperties.GetHelpText(Interval), Is.EqualTo("Error: Enter a number of days, such as 30 or 7.5."));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);

            Type(Interval, "0");
            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Explanation.Text, Does.StartWith("At each start, Campertron refreshes the campground data"));
            Assert.That(Settings.General.RefreshInterval.MessageText, Does.StartWith("Warning: refreshRidbDataDayInterval is 0 or less"));
            Assert.That(Save.IsEffectivelyEnabled, Is.True, "a warning does not block");
            Type(Interval, "1e308");
            Type(Interval, "-1e308");
            Assert.That(Settings.General.RefreshInterval.IsError, Is.True);
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Window.Close();
        }

        // 34, 35, 36, 37, 39, 40, 42, 70: the Email form binds every setting; the password is masked, revealed only on
        // screen and only for a moment, and absent from every text and accessibility value of the window
        [AvaloniaTest]
        public void ThePassword_IsMasked_RevealedOnlyOnScreenForAMoment_AndNeverExposed()
        {
            var (Window, _, Settings) = ShowSettings();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            var Password = In<SecretTextBox>(E, "SmtpPasswordBox");
            var Reveal = In<ToggleButton>(E, "RevealPasswordButton");

            Assert.That(In<TextBox>(E, "SmtpServerBox").Text, Is.EqualTo("mail.example.org"));
            Assert.That(In<TextBox>(E, "SmtpPortBox").Text, Is.EqualTo("587"));
            Assert.That(In<TextBox>(E, "SmtpUsernameBox").Text, Is.EqualTo("camper"));
            Assert.That(In<TextBox>(E, "SendFromAddressBox").Text, Is.EqualTo("camper@example.org"));
            Assert.That(Settings.Email.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "first@example.org", "second@example.org" }));
            Assert.That(Password.Text, Is.EqualTo(Secret));
            Assert.That(Password.PasswordChar, Is.EqualTo('\u2022'));
            Assert.That(Password.RevealPassword, Is.False);
            Assert.That(Password.CanCopy, Is.False);
            var Peer = (IValueProvider)ControlAutomationPeer.CreatePeerForElement(Password);
            Assert.That(Peer.Value, Is.EqualTo(new string('\u2022', Secret.Length)));
            AssertNoSecret(Window, "masked");

            Reveal.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Password.RevealPassword, Is.True);
            Assert.That(Password.CanCopy, Is.False, "revealing does not allow copying");
            Assert.That(Peer.Value, Is.EqualTo(new string('\u2022', Secret.Length)), "revealed on screen only");
            AssertNoSecret(Window, "revealed");

            Type(Password, NewSecret);
            Assert.That(Settings.Email.SmtpPassword.IsChanged, Is.True);
            Assert.That(In<TextBlock>(E, "PreviewSummary").Text, Is.EqualTo("Changes: smtpPassword (hidden)"));
            Assert.That(In<SelectableTextBlock>(E, "PreviewText").Text, Is.EqualTo(
                "  smtpUsername: camper\n- smtpPassword: \u2022\u2022\u2022\u2022\u2022\u2022 # keep it safe\n+ smtpPassword: \u2022\u2022\u2022\u2022\u2022\u2022 # keep it safe\n  sendToAddressList:"));
            AssertNoSecret(Window, "changed and revealed");

            // the reveal ends with a save, when the user leaves the tab or the window, or switches files
            Click(In<Button>(E, "SaveButton"));
            Assert.That(Password.RevealPassword, Is.False);
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace(Secret, NewSecret)));
            Reveal.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Tabs(Window).SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Email.IsPasswordRevealed, Is.False, "leaving the tab");
            Tabs(Window).SelectedIndex = SettingsTab;
            Reveal.IsChecked = true;
            Settings.IsGeneralSelected = true;
            Assert.That(Settings.Email.IsPasswordRevealed, Is.False, "switching files");
            Settings.IsEmailSelected = true;
            Reveal.IsChecked = true;
            Settings.HideSecrets();
            Assert.That(Settings.Email.IsPasswordRevealed, Is.False, "leaving the window");
            AssertNoSecret(Window, "after saving");
            Window.Close();
        }

        // 38: an unchanged password stays byte for byte while other settings are saved
        [AvaloniaTest]
        public void AnUnchangedPassword_StaysByteForByte()
        {
            File.WriteAllText(Email, EmailText.Replace(Secret, "\"" + Secret + "\""));
            var (Window, _, Settings) = ShowSettings();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            Type(In<TextBox>(E, "SmtpServerBox"), "smtp.example.org");
            Type(In<SecretTextBox>(E, "SmtpPasswordBox"), Secret);

            Assert.That(Settings.Email.SmtpPassword.IsChanged, Is.False);
            Click(In<Button>(E, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace(Secret, "\"" + Secret + "\"").Replace("'mail.example.org'", "'smtp.example.org'")));
            Window.Close();
        }

        // D66: a password from smtpPasswordFile or CAMPERTRON_EMAIL_PASSWORD is never read, shown or written by the tab. The
        // password field edits smtpPassword only (empty here); since D70 the password file's path is shown in its own field
        // with the validation's notice there, and nowhere else; nothing shown holds either canary, and an unrelated save
        // changes only its own line: the smtpPasswordFile line, the empty smtpPassword and the password file stay as they are
        [AvaloniaTest]
        public void AnExternalPasswordSource_IsNeverReadShownOrWritten_AndAnUnrelatedSaveKeepsIt()
        {
            string FileCanary = "Gui-File-Canary-" + Guid.NewGuid().ToString("N");
            string EnvCanary = "Gui-Env-Canary-" + Guid.NewGuid().ToString("N");
            string Secrets = Path.Join(Folder, "secrets");
            Directory.CreateDirectory(Secrets);
            string SecretFile = Path.Join(Secrets, "smtp_secret");
            File.WriteAllText(SecretFile, FileCanary + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(SecretFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            File.SetLastWriteTimeUtc(SecretFile, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            string Text = EmailText.Replace("smtpPassword: " + Secret + " # keep it safe", "smtpPassword: '' # not used") + "smtpPasswordFile: '" + SecretFile + "'\n";
            File.WriteAllText(Email, Text);
            Func<string, string?> OriginalEnvironment = SmtpPassword.GetEnvironment;
            int Asked = 0;
            SmtpPassword.GetEnvironment = Name => { Asked++; return EnvCanary; };
            try
            {
                var (Window, _, Settings) = ShowSettings();
                Settings.IsEmailSelected = true;
                Dispatcher.UIThread.RunJobs();
                Control E = Editor(Window, "EmailEditor");

                Assert.That(In<SecretTextBox>(E, "SmtpPasswordBox").Text ?? string.Empty, Is.Empty, "the field edits smtpPassword only");
                Assert.That(In<TextBox>(E, "SmtpPasswordFileBox").Text, Is.EqualTo(SecretFile), "D70: the path in its own field");
                Assert.That(Settings.Email.SmtpPasswordFile.MessageText, Is.EqualTo(
                    "Info: Email mode reads the SMTP password from the file named by smtpPasswordFile when it starts; smtpPassword is not used."));
                Assert.That(Settings.Email.SmtpPassword.Hint, Does.Contain("smtpPasswordFile").And.Contain("CAMPERTRON_EMAIL_PASSWORD"));
                List<string> Texts = Exposed(Window);
                Assert.That(Texts.Count, Is.GreaterThan(100), "the scan saw the window");
                Assert.That(Texts.Where(T => T.Contains(FileCanary, StringComparison.Ordinal) || T.Contains(EnvCanary, StringComparison.Ordinal)), Is.Empty, "no password");
                Assert.That(Exposed(Window, In<TextBox>(E, "SmtpPasswordFileBox"), In<SelectableTextBlock>(E, "PreviewText")).Where(T => T.Contains(Secrets, StringComparison.Ordinal)),
                    Is.Empty, "the password file's path only in its own field (and the preview)");

                Type(In<TextBox>(E, "SmtpServerBox"), "smtp.example.org");
                Click(In<Button>(E, "SaveButton"));

                Assert.That(File.ReadAllText(Email), Is.EqualTo(Text.Replace("'mail.example.org'", "'smtp.example.org'")));
                Assert.That(File.ReadAllText(SecretFile), Is.EqualTo(FileCanary + "\n"));
                Assert.That(File.GetLastWriteTimeUtc(SecretFile), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
                Assert.That(Directory.GetFileSystemEntries(Secrets), Is.EqualTo(new[] { SecretFile }));
                Assert.That(Exposed(Window).Where(T => T.Contains(FileCanary, StringComparison.Ordinal) || T.Contains(EnvCanary, StringComparison.Ordinal)), Is.Empty, "after saving");
                Assert.That(Asked, Is.EqualTo(0), "the GUI never reads CAMPERTRON_EMAIL_PASSWORD");
                Window.Close();
            }
            finally
            {
                SmtpPassword.GetEnvironment = OriginalEnvironment;
            }
        }

        // D67: the General form offers outputTo: Ntfy and shows ntfy's state read-only in the validation's words: never the
        // topic, the server or the token file's path. Since D69 Ntfy.yaml has its own editor (NtfySettingsEditorTests), the
        // only place that shows those values; a save of General.yaml still never writes Ntfy.yaml, and saving outputTo: Ntfy
        // changes only that line of General.yaml
        [AvaloniaTest]
        public void NtfyIsOffered_ItsStatusIsShownWithoutItsValues_AndAGeneralSaveNeverWritesNtfyYaml()
        {
            string Topic = "guiCnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 16);
            string NtfyFile = Path.Join(Folder, "Ntfy.yaml");
            // an enabled search with a campground: the monitoring's start has a search to check (D73), so only Ntfy.yaml counts here
            File.WriteAllText(Path.Join(Folder, "Watch.yaml"), SettingsRuntimeParityTests.WatchSearch);
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            TextBlock Status = In<TextBlock>(G, "NtfyStatus");
            Assert.That(Status.Text, Is.EqualTo("Ntfy.yaml does not exist, so ntfy is off. To add ntfy notifications, choose Ntfy.yaml above."));

            ComboBox Output = In<ComboBox>(G, "OutputToComboBox");
            Output.SelectedItem = Output.Items.Cast<SettingChoice>().Single(c => c.Value.Equals(OutputType.Ntfy));
            Dispatcher.UIThread.RunJobs();
            Assert.That(Status.Text, Does.StartWith("Ntfy.yaml does not exist, and outputTo: Ntfy needs it: choose Ntfy.yaml above"));
            // D71: the monitoring's start would refuse outputTo: Ntfy without Ntfy.yaml, so General.yaml is not saved yet
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Error: With outputTo: Ntfy, Campertron would refuse to start because of Ntfy.yaml. "
                + "Ntfy.yaml: The file does not exist, and outputTo: Ntfy needs it with enabled: true, the server and the topic. Fix and save Ntfy.yaml first, then save General.yaml."));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False);

            string Text = "enabled: true\nserver: https://ntfy-gui.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + Path.Join(Folder, "secret-token-file") + "'\n";
            File.WriteAllText(NtfyFile, Text);
            File.SetLastWriteTimeUtc(NtfyFile, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "a usable Ntfy.yaml: nothing to fix");
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText.Replace("outputTo: Console", "outputTo: Ntfy")));
            Assert.That(Status.Text, Is.EqualTo("Ntfy.yaml: ntfy is on. With outputTo Email or Ntfy, Campertron also publishes new results to its topic (not shown here)."));

            // the Ntfy.yaml editor looks at the file (as showing the tab does) and shows its values; nothing else does
            Settings.CheckForExternalChanges();
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Control NtfyEditor = Editor(Window, "NtfyEditor");
            Assert.That(In<TextBox>(NtfyEditor, "NtfyTopicBox").Text, Is.EqualTo(Topic), "the editor shows the topic it edits");
            // only the editor's value boxes and its preview may show them (the preview is hidden: nothing is edited)
            // D75: the token file's name may also appear in its own field's message, at the field in the Ntfy.yaml editor
            Control TokenMessage = (Control)NtfyEditor.GetVisualDescendants().OfType<Control>().First(C => C.Name == "TokenFileMessage").Parent!;
            List<string> Texts = Exposed(Window, In<TextBox>(NtfyEditor, "NtfyServerBox"), In<TextBox>(NtfyEditor, "NtfyTopicBox"), In<TextBox>(NtfyEditor, "NtfyTokenFileBox"), TokenMessage,
                In<SelectableTextBlock>(NtfyEditor, "PreviewText"));
            Assert.That(Texts.Count, Is.GreaterThan(100), "the scan saw the window");
            Assert.That(Texts.Where(T => T.Contains(Topic, StringComparison.Ordinal) || T.Contains("ntfy-gui.example.invalid", StringComparison.Ordinal)
                || T.Contains("secret-token-file", StringComparison.Ordinal)), Is.Empty);

            File.WriteAllText(NtfyFile, "enabled: true\nserver: https://ntfy-gui.example.invalid/" + Topic + "\ntopic: " + Topic + "\n");
            File.SetLastWriteTimeUtc(NtfyFile, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(OutputType.Email));
            Dispatcher.UIThread.RunJobs();
            Assert.That(Status.Text, Does.StartWith("Ntfy.yaml: Error: server must be the ntfy server's address only"));
            Assert.That(Status.Text, Does.Not.Contain(Topic));
            // D71: ntfy is on and its server can't be used, so outputTo: Email would not start either; another outputTo
            // without the monitoring can be saved, and no General.yaml save writes Ntfy.yaml
            Assert.That(Settings.General.OutputTo.MessageText, Does.StartWith("Error: With outputTo: Email, Campertron would refuse to start because of Ntfy.yaml. Ntfy.yaml: server must be"));
            Assert.That(Settings.General.OutputTo.MessageText, Does.Not.Contain(Topic).And.Not.Contain("ntfy-gui.example.invalid"));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False);
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(OutputType.Console));
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
            Assert.That(File.GetLastWriteTimeUtc(NtfyFile), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)), "Ntfy.yaml is never written");
            Assert.That(Directory.GetFiles(Folder, "Ntfy.yaml*"), Is.EqualTo(new[] { NtfyFile }), "no backup or temporary file of it");
            Window.Close();
        }

        // 43, 44, 45, 46: recipients are added, removed and moved with buttons; a moved item keeps its comments
        [AvaloniaTest]
        public void Recipients_AreAddedRemovedAndMoved_WithTheirComments()
        {
            var (Window, _, Settings) = ShowSettings();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            List<Control> Rows() => In<ItemsControl>(E, "RecipientsList").GetRealizedContainers().ToList();
            Button RowButton(int Row, string Name) => In<Button>(Rows()[Row], Name);

            Assert.That(Rows().Select(R => AutomationProperties.GetName(In<TextBox>(R, "RecipientBox"))), Is.EqualTo(new[] { "Recipient 1", "Recipient 2" }));
            Assert.That(AutomationProperties.GetName(RowButton(0, "RecipientRemoveButton")), Is.EqualTo("Remove recipient 1"));
            Assert.That(RowButton(0, "RecipientUpButton").IsEffectivelyEnabled, Is.False);
            Click(RowButton(1, "RecipientUpButton"));
            Click(In<Button>(E, "AddRecipientButton"));
            Type(In<TextBox>(Rows()[2], "RecipientBox"), "third@example.org");
            Assert.That(Settings.Email.Recipients.Rows.Select(R => R.Text), Is.EqualTo(new[] { "second@example.org", "first@example.org", "third@example.org" }));
            Assert.That(Settings.Session().Candidate, Does.Contain("  - second@example.org\n  # family\n  - first@example.org # one\n  - third@example.org\n"));
            Click(In<Button>(E, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Does.Contain("sendToAddressList:\n  - second@example.org\n  # family\n  - first@example.org # one\n  - third@example.org\nsendFromAddress"));

            Click(RowButton(1, "RecipientRemoveButton"));
            Assert.That(Settings.Email.Recipients.MessageText, Is.EqualTo("Warning: Removing item 2 of sendToAddressList also removes 2 comments that belong to it."));
            Click(In<Button>(E, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Does.Contain("sendToAddressList:\n  - second@example.org\n  - third@example.org\nsendFromAddress"));
            Window.Close();
        }

        // 48, 49, 50, 51, 52: outside the Email output a missing setting is a warning; with outputTo: Email in the General form
        // it is an error that blocks the save. D71: a value written but invalid blocks the save in every output
        [AvaloniaTest]
        public void TheEmailChecks_FollowTheOutputChosenInTheGeneralForm()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            Type(In<TextBox>(E, "SmtpPortBox"), "70000");
            Type(In<TextBox>(E, "SendFromAddressBox"), "nobody");
            Type(In<TextBox>(In<ItemsControl>(E, "RecipientsList").GetRealizedContainers().Last(), "RecipientBox"), "not an address");

            Assert.That(Settings.Email.SmtpPort.MessageText, Is.EqualTo("Error: smtpPort is not between 1 and 65535."));
            Assert.That(Settings.Email.SendFromAddress.MessageText, Is.EqualTo("Error: sendFromAddress is not a valid email address."));
            Assert.That(Settings.Email.Recipients.Rows[1].MessageText, Is.EqualTo("Error: sendToAddressList item 2 is not a valid email address."));
            Assert.That(In<TextBlock>(E, "OutputExplanation").Text, Does.StartWith("These settings are used only when outputTo is Email"));
            Assert.That(In<Button>(E, "SaveButton").IsEffectivelyEnabled, Is.False, "values that are not valid block the save in every output");

            Type(In<TextBox>(E, "SmtpPortBox"), "587");
            Type(In<TextBox>(E, "SendFromAddressBox"), "");
            Type(In<TextBox>(In<ItemsControl>(E, "RecipientsList").GetRealizedContainers().Last(), "RecipientBox"), "third@example.org");
            Assert.That(Settings.Email.SendFromAddress.MessageText, Is.EqualTo("Warning: sendFromAddress is empty."));
            Assert.That(In<Button>(E, "SaveButton").IsEffectivelyEnabled, Is.True, "a missing setting is a warning outside the Email output");
            Type(In<TextBox>(E, "SmtpPortBox"), "70000");

            ComboBox Output = In<ComboBox>(G, "OutputToComboBox");
            Output.SelectedItem = Output.Items.Cast<SettingChoice>().Single(c => c.Value.Equals(OutputType.Email));
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Email.SmtpPort.MessageText, Is.EqualTo("Error: smtpPort is not between 1 and 65535."));
            Assert.That(Settings.Email.SendFromAddress.MessageText, Is.EqualTo("Error: sendFromAddress is empty."));
            Assert.That(In<TextBlock>(E, "OutputExplanation").Text, Does.StartWith("outputTo is Email, so these settings are needed"));
            Assert.That(In<Button>(E, "SaveButton").IsEffectivelyEnabled, Is.False);

            Type(In<TextBox>(E, "SmtpPortBox"), "port");
            Assert.That(Settings.Email.SmtpPort.MessageText, Is.EqualTo("Error: Enter a port number, such as 587."));
            Window.Close();
        }

        // 66, 67: Save only with changes; Revert only with changes; Reload only without
        [AvaloniaTest]
        public void SaveRevertAndReload_AreEnabledOnlyWhenTheyApply()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            Button Save = In<Button>(G, "SaveButton");
            Button Revert = In<Button>(G, "RevertButton");
            Button Reload = In<Button>(G, "ReloadButton");
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((false, false, true)));

            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((true, true, false)));

            Click(Revert);
            Assert.That(In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked, Is.False);
            Assert.That(In<TextBlock>(G, "StatusText").Text, Is.EqualTo("Your changes to General.yaml were reverted."));
            File.WriteAllText(General, GeneralText.Replace("autoRefresh: false", "autoRefresh: true"));
            Click(Reload);
            Assert.That(In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked, Is.True);
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((false, false, true)));
            Window.Close();
        }

        // 68: a change by another program while the form is edited opens the conflict card; nothing is written until the
        // user chooses
        [AvaloniaTest]
        public void AnExternalChange_WhileEditing_ShowsTheConflictCard()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            string Theirs = GeneralText.Replace("outputTo: Console ", "outputTo: HtmlFile");
            File.WriteAllText(General, Theirs);

            // switching to the tab looks at the files (as activating the window does)
            Tabs(Window).SelectedIndex = 0;
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            Border Card = In<Border>(G, "ConflictCard");
            Assert.That(Card.IsVisible, Is.True);
            Assert.That(In<SelectableTextBlock>(G, "ConflictText").Text, Does.StartWith("General.yaml was changed by another program while you were editing it, in other settings than yours."));
            Assert.That(In<Button>(G, "ApplyMineButton").Content, Is.EqualTo("Apply my changes to the new version"));
            Assert.That(In<Button>(G, "SaveButton").IsEffectivelyEnabled, Is.False);
            Assert.That(In<CheckBox>(G, "AutoRefreshCheckBox").IsEffectivelyEnabled, Is.False, "resolve first");
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs));

            Click(In<Button>(G, "ApplyMineButton"));
            Assert.That(Card.IsVisible, Is.False);
            Assert.That(((SettingChoice)In<ComboBox>(G, "OutputToComboBox").SelectedItem!).Value, Is.EqualTo(OutputType.HtmlFile));
            Click(In<Button>(G, "SaveButton"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs.Replace("autoRefresh: false", "autoRefresh: true")));

            // the same setting changed on both sides: the choice names it
            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            File.WriteAllText(General, Theirs.Replace("autoRefresh: false", "autoRefresh: false # theirs"));
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Card.IsVisible, Is.True);
            Assert.That(In<Button>(G, "ApplyMineButton").Content, Is.EqualTo("Keep my values for autoRefresh"));
            Click(In<Button>(G, "UseTheirsButton"));
            Assert.That(Card.IsVisible, Is.False);
            Assert.That(In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked, Is.False);
            Assert.That(Settings.General.IsDirty, Is.False);
            Window.Close();
        }

        // missing, read-only and unreadable files: the state card with Create or Reload; no editing
        [AvaloniaTest]
        public void MissingAndUnsupportedFiles_ShowWhyAndOfferWhatIsSafe()
        {
            File.Delete(Email);
            File.WriteAllText(General, "outputTo: Console\ntheme: dark\n");
            // the window starts with this folder (its loader is the test's), although Load.GetConfig would refuse the unknown key
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            Assert.That(In<Border>(G, "StateCard").IsVisible, Is.True);
            Assert.That(In<SelectableTextBlock>(G, "StateText").Text, Does.StartWith("General.yaml can't be edited here because Campertron can't read its settings"));
            Assert.That(In<ComboBox>(G, "OutputToComboBox").IsEffectivelyEnabled, Is.False);
            Assert.That(Settings.General.FileNotices.Select(N => N.Text), Has.Some.Contains("theme"));

            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            Assert.That(In<SelectableTextBlock>(E, "StateText").Text, Is.EqualTo("Email.yaml does not exist."));
            Assert.That(In<Border>(E, "EmailForm").IsVisible, Is.False);
            Click(In<Button>(E, "CreateButton"));
            Assert.That(File.Exists(Email), Is.True);
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Email), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Assert.That(Settings.Email.IsEditable, Is.True);
            Assert.That(In<TextBlock>(E, "StatusText").Text, Is.EqualTo("Created Email.yaml with the default settings."));
            Window.Close();
        }

        // 61, 62, 63: keyboard: every field is reachable with Tab, in reading order, and has its label and accessible name
        [AvaloniaTest]
        public void Keyboard_ReachesEveryFieldInOrder_AndEveryFieldIsLabelled()
        {
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            Tabs(Window).Focus(NavigationMethod.Tab);
            ((TabItem)Tabs(Window).SelectedItem!).Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            var Visited = new List<string?>();
            for (int i = 0; i < 11; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                IInputElement? F = Focused(Window);
                Visited.Add(F is TabItem T ? "Tab:" + T.Header : (F as Control)?.Name ?? F?.GetType().Name);
            }
            // D69: Ntfy.yaml is the third settings file
            Assert.That(Visited, Is.EqualTo(new[]
            {
                "Tab:Campertron", "GeneralFileButton", "EmailFileButton", "NtfyFileButton", "OpenFolderButton", "OutputToComboBox", "AutoRefreshCheckBox",
                "RefreshIntervalBox", "LastRefreshBox", "ReloadButton", "Tab:Settings",
            }), string.Join(", ", Visited));

            // Space on the second radio button switches files; each label targets its field and names it
            In<RadioButton>(Named<SettingsView>(Window, "SettingsContent"), "EmailFileButton").Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.IsEmailSelected, Is.True);
            Control E = Editor(Window, "EmailEditor");
            foreach (Control Root in new[] { G, E })
            {
                foreach (Label L in Root.GetVisualDescendants().OfType<Label>())
                {
                    Control Target = (Control)L.Target!;
                    Assert.That(Target, Is.Not.Null);
                    string Expected = ((string)L.Content!).Replace("_", "");
                    if (Target.Name == "AddRecipientButton")
                    {
                        // Alt+R goes to Add recipient; the list of rows itself is named Recipients
                        Assert.That(ControlAutomationPeer.CreatePeerForElement(Target).GetName(), Is.EqualTo("Add recipient"));
                        Assert.That(AutomationProperties.GetName(In<ItemsControl>(E, "RecipientsList")), Is.EqualTo(Expected));
                        continue;
                    }
                    Assert.That(AutomationProperties.GetName(Target), Is.EqualTo(Expected), Target.Name);
                }
            }
            Assert.That(G.GetVisualDescendants().OfType<Label>().Select(L => ((Control)L.Target!).Name), Is.EqualTo(new[] { "OutputToComboBox", "RefreshIntervalBox", "LastRefreshBox" }));
            Assert.That(E.GetVisualDescendants().OfType<Label>().Select(L => ((Control)L.Target!).Name),
                Is.EqualTo(new[] { "SmtpServerBox", "SmtpPortBox", "SmtpUsernameBox", "SmtpPasswordFileBox", "SmtpPasswordBox", "SendFromAddressBox", "AddRecipientButton" }));
            Assert.That(AutomationProperties.GetName(In<CheckBox>(G, "AutoRefreshCheckBox")), Is.EqualTo("Refresh the campground data automatically"));
            Window.Close();
        }

        // 69, 71: the preview is read-only text; errors use the theme's error brush and the notice cards its accent, in
        // Light and Dark, with nothing logged
        [AvaloniaTest]
        public void ThePreviewIsReadOnly_AndTheStylesFollowTheTheme([Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            ThemeVariant Theme = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = Theme;
            var (Window, _, Settings) = ShowSettings();
            Control G = Editor(Window, "GeneralEditor");
            Type(In<TextBox>(G, "RefreshIntervalBox"), "soon");
            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            SelectableTextBlock Preview = In<SelectableTextBlock>(G, "PreviewText");
            Assert.That(Preview, Is.Not.InstanceOf<TextBox>());
            Assert.That(G.GetVisualDescendants().OfType<TextBox>().Where(B => B.TemplatedParent == null).Select(B => B.Name), Is.EquivalentTo(new[] { "RefreshIntervalBox", "LastRefreshBox" }));
            Application.Current.TryGetResource("SeverityErrorTextBrush", Theme, out object? Error); // D74: the error severity's text
            TextBlock Message = G.GetVisualDescendants().OfType<TextBlock>().Single(T => T.Text == "Error: Enter a number of days, such as 30 or 7.5.");
            Assert.That(((ISolidColorBrush)Message.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Error!).Color));

            File.WriteAllText(General, GeneralText.Replace("# end", "# theirs"));
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Application.Current.TryGetResource("SystemControlHighlightAccentBrush", Theme, out object? Accent);
            Assert.That(((ISolidColorBrush)In<Border>(G, "ConflictCard").BorderBrush!).Color, Is.EqualTo(((ISolidColorBrush)Accent!).Color));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // 72, 73: at the supported sizes the forms, long messages and the preview stay inside the window (no horizontal
        // scrolling; long text wraps)
        [AvaloniaTest]
        public void TheEditors_FitEverySupportedWindowSize([Values(720, 1024, 1600)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            File.WriteAllText(Email, EmailText.Replace("camper@example.org\n", "camper@example.org\n# " + string.Concat(Enumerable.Repeat("a long comment ", 30)) + "\n"));
            var (Window, _, Settings) = ShowSettings();
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1000);
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            Type(In<TextBox>(E, "SmtpPortBox"), "a port number far too long to be one and written out in words so that the message wraps");
            Type(In<TextBox>(E, "SendFromAddressBox"), string.Concat(Enumerable.Repeat("x", 300)));
            Window.UpdateLayout();

            ScrollViewer Scroll = In<ScrollViewer>(Named<SettingsView>(Window, "SettingsContent"), "SettingsScrollViewer");
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            foreach (Control C in E.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && (c is TextBlock || c is TextBox || c is Button)))
            {
                Point? Right = C.TranslatePoint(new Point(C.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), C.Name ?? C.GetType().Name);
            }
            Assert.That(Scroll.Bounds.Bottom, Is.LessThanOrEqualTo(Window.ClientSize.Height + 0.5));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // Open settings folder: the established folder, as a directory, through the launcher; a failure is shown, never thrown
        [AvaloniaTest]
        public void OpenSettingsFolder_PassesOnlyTheSettingsFolder_AndReportsAFailure()
        {
            var (Window, _, Settings) = ShowSettings();
            var Opened = new List<string>();
            Settings.FolderLauncher = Dir =>
            {
                Opened.Add(Dir.FullName);
                return Task.FromResult(true);
            };
            Click(In<Button>(Named<SettingsView>(Window, "SettingsContent"), "OpenFolderButton"));
            Assert.That(Opened, Is.EqualTo(new[] { Path.GetFullPath(Folder) }));
            Assert.That(Settings.HasFolderStatus, Is.False);

            Settings.FolderLauncher = _ => Task.FromResult(false);
            WaitFor(Settings.OpenFolder());
            Assert.That(Settings.FolderStatus, Is.EqualTo("Campertron couldn't open the settings folder. It is " + Folder));
            Settings.FolderLauncher = _ => throw new InvalidOperationException("no file manager");
            WaitFor(Settings.OpenFolder());
            Assert.That(Settings.HasFolderStatus, Is.True);
            Window.Close();
        }

        // 32, 53, 54, 75, 76: a whole session of edits and saves writes only General.yaml, Email.yaml and their backups: no
        // search file, no database, no web request; the settings code refers to no search, refresh, mail or web code
        [AvaloniaTest]
        public void EditingSettings_StartsNoSearchRefreshOrEmail_AndTouchesNoOtherFile()
        {
            File.WriteAllText(Path.Join(Folder, "ZionConfig.yaml"), "displayName: mine # a search file\nautoRun: true\ncampgroundID: '232445'\n");
            File.WriteAllText(Path.Join(Folder, "Other.yaml"), "displayName: other\nautoRun: false\n");
            using var Requests = new WebRequestListener();
            Requests.Prove();
            var (Window, _, Settings) = ShowSettings();
            Dictionary<string, string> Before = Fingerprint(Folder);
            Control G = Editor(Window, "GeneralEditor");
            In<CheckBox>(G, "AutoRefreshCheckBox").IsChecked = true;
            Type(In<TextBox>(G, "RefreshIntervalBox"), "1");
            Click(In<Button>(G, "SaveButton"));
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control E = Editor(Window, "EmailEditor");
            Type(In<SecretTextBox>(E, "SmtpPasswordBox"), NewSecret);
            Click(In<Button>(E, "SaveButton"));
            Settings.CheckForExternalChanges();
            Window.Close();
            Dispatcher.UIThread.RunJobs();

            Dictionary<string, string> After = Fingerprint(Folder);
            Assert.That(After.Keys, Is.EquivalentTo(Before.Keys.Concat(new[] { "General.yaml.bak", "Email.yaml.bak" })));
            foreach (string Name in Before.Keys.Where(N => N != "General.yaml" && N != "Email.yaml"))
            {
                Assert.That(After[Name], Is.EqualTo(Before[Name]), Name);
            }
            Assert.That(Requests.Count, Is.Zero);

            Type[] SettingsTypes =
            {
                typeof(SettingsViewModel), typeof(SettingsFileViewModel), typeof(SettingsFileViewModel<GeneralConfig>), typeof(GeneralSettingsViewModel),
                typeof(EmailSettingsViewModel), typeof(SettingField), typeof(TextSettingField), typeof(ListSettingField), typeof(ListRow),
                typeof(SettingsView), typeof(SecretTextBox), typeof(SecretTextBoxAutomationPeer),
                // D69: the Ntfy.yaml editor
                typeof(SettingsFileViewModel<NtfyConfig>), typeof(NtfySettingsViewModel), typeof(NtfySettingsView),
            };
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.", "CampertronLibrary.function.Base.Web::", "System.Net.", "CampsiteConfig",
                "CampertronLibrary.function.Base.ConfigFileReplace::", "CampertronLibrary.function.Base.YamlDocument::", "System.IO.File::",
                "System.Diagnostics.Process", "System.Console",
                // D69: the GUI never reads ntfy's token (the file or the variable), the SMTP password's sources or the
                // environment, and never publishes
                "CampertronLibrary.function.Base.NtfyToken", "CampertronLibrary.function.Base.SecretFile", "CampertronLibrary.function.Base.SmtpPassword",
                "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.Notifications", "System.Environment::GetEnvironmentVariable",
                "System.IO.FileStream", "System.IO.StreamReader",
            };
            List<string> Called = ReferencedMembers(SettingsTypes).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called.Where(m => m.StartsWith("CampertronLibrary.function.Base.ConfigEditSession`1", StringComparison.Ordinal) && m.EndsWith("::Save", StringComparison.Ordinal)), Is.Not.Empty);
            Assert.That(Called.Where(m => Forbidden.Any(f => m.StartsWith(f, StringComparison.Ordinal))), Is.Empty);
        }
    }

    internal static class SettingsTestExtensions
    {
        // the Email session behind the Email form (the tests' view of what a save writes)
        internal static ConfigEditSession<EmailConfig> Session(this SettingsViewModel Settings) =>
            (ConfigEditSession<EmailConfig>)typeof(SettingsFileViewModel<EmailConfig>).GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Settings.Email)!;
    }
}
