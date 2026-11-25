using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
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
    // Ntfy.yaml in Settings > Campertron (decision D69, business-rules §17 and §28), in the real window on the headless
    // platform. Each test has its own settings folder; the topic, server and token are unique canaries that must appear
    // only in the Ntfy.yaml editor's own boxes and preview, never in another part of the window, a message or the current
    // configuration, and the token file is never read. Nothing is published, connected or sent.
    public class NtfySettingsEditorTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string GeneralText = "# Campertron settings\noutputTo: Console   # where results go\n\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC\nautoRefresh: false\n# end\n";
        private const string EmailText = "smtpServer: mail.example.org\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: ''\nsendToAddressList:\n- first@example.org\nsendFromAddress: camper@example.org\n";
        private static readonly UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private string Folder = string.Empty;
        private string Topic = string.Empty;
        private string General => Path.Join(Folder, "General.yaml");
        private string Email => Path.Join(Folder, "Email.yaml");
        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "ntfy-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(General, GeneralText);
            File.WriteAllText(Email, EmailText);
            Topic = "guiTopicCnry" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

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

        // the real window with this test's settings folder, on Settings > Campertron with Ntfy.yaml chosen
        private (MainWindow Window, MainWindowViewModel ViewModel, SettingsViewModel Settings, Control Editor) ShowNtfy()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            ViewModel.Settings!.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            return (Window, ViewModel, ViewModel.Settings, In<Control>(Named<SettingsView>(Window, "SettingsContent"), "NtfyEditor"));
        }

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

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

        private static void Choose(ComboBox Box, object Value)
        {
            Box.SelectedItem = Box.Items.Cast<SettingChoice>().Single(c => c.Value.Equals(Value));
            Dispatcher.UIThread.RunJobs();
        }

        // what the window exposes outside the three boxes that hold ntfy's values and the preview of the lines a save writes
        private static List<string> ExposedOutsideTheValues(Window Window, Control Editor) => SettingsEditorTests.Exposed(Window,
            In<TextBox>(Editor, "NtfyServerBox"), In<TextBox>(Editor, "NtfyTopicBox"), In<TextBox>(Editor, "NtfyTokenFileBox"), In<SelectableTextBlock>(Editor, "PreviewText"));

        private static List<Control> TagRows(Control Editor) => In<ItemsControl>(Editor, "NtfyTagsList").GetRealizedContainers().ToList();

        private static void AddTag(Control Editor, string Tag)
        {
            Click(In<Button>(Editor, "AddTagButton"));
            Type(In<TextBox>(TagRows(Editor).Last(), "TagBox"), Tag);
        }

        // ---- selection and creation ----

        // the selector offers the three settings files; a missing Ntfy.yaml opens as a new file with the defaults, and
        // nothing is written by looking at it, switching to it, or the tab's checks for changes on disk
        [AvaloniaTest]
        public void NtfyYaml_IsTheThirdSettingsFile_AndViewingAMissingOneCreatesNothing()
        {
            using var Log = new AvaloniaLogCapture();
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            Dictionary<string, string> Before = Fingerprint(Folder);
            var (Window, ViewModel, Settings, Editor) = ShowNtfy();
            Control View = Named<SettingsView>(Window, "SettingsContent");
            Border Card = In<Border>(View, "SettingsFilesCard");

            Assert.That(Card.GetVisualDescendants().OfType<RadioButton>().Select(r => (r.Name, r.Content as string, AutomationProperties.GetName(r))), Is.EqualTo(new[]
            {
                ("GeneralFileButton", "General.yaml", "General.yaml"), ("EmailFileButton", "Email.yaml", "Email.yaml"), ("NtfyFileButton", "Ntfy.yaml", "Ntfy.yaml"),
                // v0.3.0's PR 5: Web.yaml, the fourth settings file
                ("WebFileButton", "Web.yaml", "Web.yaml"),
            }));
            Assert.That(In<RadioButton>(View, "NtfyFileButton").IsChecked, Is.True);
            Assert.That(Editor.IsEffectivelyVisible, Is.True);
            Assert.That(In<Control>(View, "GeneralEditor").IsEffectivelyVisible || In<Control>(View, "EmailEditor").IsEffectivelyVisible, Is.False);
            Assert.That(Settings.Ntfy.IsNew && Settings.Ntfy.IsEditable && !Settings.Ntfy.IsMissing, Is.True);
            Assert.That(Settings.Ntfy.FileNotices.Select(N => N.Text), Does.Contain("Info: Ntfy.yaml does not exist yet. The form shows the default settings (ntfy off); nothing is written until you change a setting and save, and Save creates the file."));

            // the defaults: ntfy off, no server, topic or token file (the boxes show placeholders, never values), priority Default, no tags, 30 s
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.False);
            foreach ((string Box, string Placeholder) in new[] { ("NtfyServerBox", "for example https://ntfy.sh"), ("NtfyTopicBox", "a long, random topic"), ("NtfyTokenFileBox", "optional: the token file's full path") })
            {
                Assert.That(In<TextBox>(Editor, Box).Text ?? string.Empty, Is.Empty, Box);
                Assert.That(In<TextBox>(Editor, Box).PlaceholderText, Is.EqualTo(Placeholder), Box + ": a placeholder that can't be taken for a value (no example topic or token path)");
            }
            Assert.That(((SettingChoice)In<ComboBox>(Editor, "NtfyPriorityComboBox").SelectedItem!).Value, Is.EqualTo(NtfyPriority.Default));
            Assert.That(Settings.Ntfy.Tags.Rows, Is.Empty);
            Assert.That(In<TextBox>(Editor, "NtfyTimeoutBox").Text, Is.EqualTo("30"));
            Assert.That(In<Button>(Editor, "SaveButton").IsEffectivelyEnabled, Is.False, "nothing to save before a change");
            Assert.That(In<Button>(Editor, "RevertButton").IsEffectivelyEnabled, Is.False);
            Assert.That(In<Border>(Editor, "PreviewCard").IsVisible, Is.False);
            Assert.That(In<Button>(Editor, "RemoveTokenFileButton").IsVisible, Is.False);
            // the server's box is full width (since v0.3.0's PR 5 the topic's box shares its row with Generate a random topic)
            Assert.That(In<TextBox>(Editor, "NtfyTokenFileBox").Bounds.Width, Is.EqualTo(In<TextBox>(Editor, "NtfyServerBox").Bounds.Width),
                "the hidden Remove tokenFile button takes no space");

            // looking again, switching files and tabs, and the window's checks write nothing
            Settings.IsGeneralSelected = true;
            Settings.IsNtfySelected = true;
            Tabs(Window).SelectedIndex = 0;
            Tabs(Window).SelectedIndex = SettingsTab;
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(File.Exists(Ntfy), Is.False);
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Fingerprint(Folder).Where(F => !F.Key.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal)),
                Is.EquivalentTo(Before.Where(F => !F.Key.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal))));
            Assert.That(Log.Entries, Is.Empty);
        }

        // a new Ntfy.yaml is written only by Save, with exactly the settings entered (the preview shows every line), owner-only
        // on Unix; General.yaml and Email.yaml are not touched, and the General form's ntfy line follows the saved file
        [AvaloniaTest]
        public void ANewNtfyYaml_IsCreatedOnlyBySave_WithExactlyTheEnteredSettings()
        {
            var (Window, ViewModel, Settings, Editor) = ShowNtfy();
            Dictionary<string, string> Others() => Fingerprint(Folder).Where(F => F.Key == "General.yaml" || F.Key == "Email.yaml").ToDictionary(F => F.Key, F => F.Value);
            Dictionary<string, string> OthersBefore = Others();
            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Type(In<TextBox>(Editor, "NtfyServerBox"), "https://ntfy.example.invalid");
            Type(In<TextBox>(Editor, "NtfyTopicBox"), Topic);
            Choose(In<ComboBox>(Editor, "NtfyPriorityComboBox"), NtfyPriority.High);
            AddTag(Editor, "tent");
            AddTag(Editor, "warning");
            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "45");

            string Expected = ConfigEditSession<NtfyConfig>.NewNtfyText.Replace("enabled: false", "enabled: true")
                + "server: https://ntfy.example.invalid\ntopic: " + Topic + "\npriority: High\ntimeoutSeconds: 45\ntags: [tent, warning]\n";
            Assert.That(File.Exists(Ntfy), Is.False, "nothing is written before Save");
            Assert.That(Settings.Ntfy.IsDirty && Settings.Ntfy.CanSave && Settings.Ntfy.IsNew, Is.True);
            Window.UpdateLayout();
            Assert.That(ExposedOutsideTheValues(Window, Editor).Where(T => T.Contains(Topic, StringComparison.Ordinal) || T.Contains("ntfy.example.invalid", StringComparison.Ordinal)), Is.Empty,
                "a new file's topic and server only in their boxes and the preview, not in a notice, the mode or a status");
            Assert.That(In<SelectableTextBlock>(Editor, "PreviewText").Text, Is.EqualTo(string.Join("\n", Expected.TrimEnd('\n').Split('\n').Select(L => "+ " + L))));
            Assert.That(In<TextBlock>(Editor, "PreviewSummary").Text, Is.EqualTo("Changes: enabled, server, topic, priority, tags, timeoutSeconds"));

            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Created Ntfy.yaml with your settings."));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Expected));
            NtfyConfig Saved = Yaml.LoadSettingsText<NtfyConfig>(File.ReadAllText(Ntfy));
            Assert.That((Saved.Enabled, Saved.Server, Saved.Topic, Saved.Priority, Saved.TimeoutSeconds, Saved.TokenFileWritten), Is.EqualTo((true, "https://ntfy.example.invalid", Topic, NtfyPriority.High, 45, false)));
            Assert.That(Saved.Tags, Is.EqualTo(new[] { "tent", "warning" }));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Ntfy), Is.EqualTo(OwnerOnly), "the topic works like a password");
            }
            Assert.That(File.Exists(Ntfy + ".bak"), Is.False, "a created file has no backup");
            Assert.That(Directory.GetFiles(Folder, "*.tmp"), Is.Empty);
            Assert.That(Settings.Ntfy.IsNew || Settings.Ntfy.IsDirty, Is.False);
            Assert.That(Others(), Is.EqualTo(OthersBefore), "General.yaml (outputTo is never changed by the Ntfy.yaml editor) and Email.yaml are not written, not even touched");
            Assert.That(Settings.General.IsDirty, Is.False);
            Assert.That(Settings.General.NtfyStatus, Does.StartWith("Ntfy.yaml: ntfy is on."));
            Assert.That(Settings.General.NtfyStatus, Does.Not.Contain(Topic));
            Window.Close();
        }

        // an existing Ntfy.yaml: every setting is edited in place, keeping its comments, CRLF line breaks and byte-order mark;
        // the backup is the previous file (since D112 without the replaced topic) and the file keeps its mode
        [AvaloniaTest]
        public void EveryNtfySetting_IsEditedInPlace_KeepingCommentsLineBreaksAndTheByteOrderMark()
        {
            string TokenPath = Path.Join(Folder, "secrets", "ntfy_token");
            string Text = "# ntfy for Campertron\r\nenabled: false # off for now\r\nserver: 'https://ntfy.example.invalid' # mine\r\ntopic: oldTopic_1\r\n\r\n" +
                "priority: 2 # low\r\ntags:\r\n  # the first one\r\n  - tent\r\n  - camping\r\ntimeoutSeconds: 10\r\n# end\r\n";
            File.WriteAllBytes(Ntfy, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(Text)).ToArray());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Ntfy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            byte[] Original = File.ReadAllBytes(Ntfy);
            var (Window, _, Settings, Editor) = ShowNtfy();
            Assert.That(Settings.Ntfy.IsNew, Is.False);
            Assert.That(In<TextBox>(Editor, "NtfyServerBox").Text, Is.EqualTo("https://ntfy.example.invalid"));
            Assert.That(((SettingChoice)In<ComboBox>(Editor, "NtfyPriorityComboBox").SelectedItem!).Value, Is.EqualTo(NtfyPriority.Low), "a number reads as its priority");
            Assert.That(Settings.Ntfy.Tags.Rows.Select(R => R.Text), Is.EqualTo(new[] { "tent", "camping" }));

            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Type(In<TextBox>(Editor, "NtfyServerBox"), "https://ntfy.example.net:8443");
            Type(In<TextBox>(Editor, "NtfyTopicBox"), Topic);
            Type(In<TextBox>(Editor, "NtfyTokenFileBox"), TokenPath);
            Choose(In<ComboBox>(Editor, "NtfyPriorityComboBox"), NtfyPriority.Max);
            Type(In<TextBox>(TagRows(Editor)[1], "TagBox"), "warning");
            Click(In<Button>(TagRows(Editor)[1], "TagUpButton"));
            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "120");
            Click(In<Button>(Editor, "SaveButton"));

            // a new key goes after the last setting (business-rules §13), before the comment after it
            string Expected = "# ntfy for Campertron\r\nenabled: true # off for now\r\nserver: 'https://ntfy.example.net:8443' # mine\r\ntopic: " + Topic + "\r\n\r\n" +
                "priority: Max # low\r\ntags:\r\n  - warning\r\n  # the first one\r\n  - tent\r\ntimeoutSeconds: 120\r\ntokenFile: " + TokenPath + "\r\n# end\r\n";
            byte[] Written = File.ReadAllBytes(Ntfy);
            Assert.That(Written.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "the byte-order mark is kept");
            Assert.That(System.Text.Encoding.UTF8.GetString(Written.Skip(3).ToArray()), Is.EqualTo(Expected));
            // since D112 the backup is the previous file without its replaced topic (until D112 the exact previous file)
            Assert.That(File.ReadAllBytes(Ntfy + ".bak"), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(Text.Replace("topic: oldTopic_1", "topic: ''"))).ToArray()));
            Assert.That(Directory.GetFiles(Folder).Where(F => File.ReadAllText(F).Contains("oldTopic_1", StringComparison.Ordinal)), Is.Empty, "the replaced topic is in no file of the folder");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Ntfy), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead), "an existing file keeps its mode");
                Assert.That(File.GetUnixFileMode(Ntfy + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead), "its backup too, never broader");
                Assert.That(Settings.Ntfy.FileNotices.Select(N => N.Text), Does.Contain(
                    "Info: Other users of this computer may be able to read Ntfy.yaml (its permissions allow it), and its topic works like a password on a server without access control."));
            }
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without its topic."));
            Window.Close();
        }

        // D112: Generate a random topic in the window, then Save: the old topic is in no file of the folder, the backup keeps
        // the previous settings, and the status says so; a later save of another setting keeps the exact backup
        [AvaloniaTest]
        public void ARandomTopicSaved_LeavesTheOldTopicInNoFileOfTheFolder()
        {
            string Text = "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntimeoutSeconds: 10\n";
            File.WriteAllText(Ntfy, Text);
            var (Window, _, Settings, Editor) = ShowNtfy();
            Click(In<Button>(Editor, "GenerateTopicButton"));
            string Random = In<TextBox>(Editor, "NtfyTopicBox").Text!;
            Assert.That(Random, Does.Match("^campertron-[a-z0-9]{26}$"));
            Click(In<Button>(Editor, "SaveButton"));

            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without its topic."));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Text.Replace(Topic, Random)));
            Assert.That(File.ReadAllText(Ntfy + ".bak"), Is.EqualTo(Text.Replace("topic: " + Topic, "topic: ''")));
            Assert.That(Directory.GetFiles(Folder).Where(F => File.ReadAllText(F).Contains(Topic, StringComparison.Ordinal)), Is.Empty);

            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "20");
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak."));
            Assert.That(File.ReadAllText(Ntfy + ".bak"), Is.EqualTo(Text.Replace(Topic, Random)), "the exact previous file: the topic the saved file still holds");
            Window.Close();
        }

        // a Ntfy.yaml the editor can't edit safely (an unknown key, refused by the loader) is shown read-only with its reason,
        // which names neither the key nor a value, and is never changed
        [AvaloniaTest]
        public void AnNtfyYamlTheEditorCantEdit_IsReadOnly_AndNeverNamesAValue()
        {
            string Text = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n" + Topic + "Key: 1\n";
            File.WriteAllText(Ntfy, Text);
            var (Window, _, Settings, Editor) = ShowNtfy();
            Assert.That(Settings.Ntfy.IsEditable, Is.False);
            Assert.That(In<Border>(Editor, "StateCard").IsVisible, Is.True);
            Assert.That(In<SelectableTextBlock>(Editor, "StateText").Text, Does.StartWith("Ntfy.yaml can't be edited here because Campertron can't read its settings"));
            Assert.That(In<TextBox>(Editor, "NtfyTopicBox").IsEffectivelyEnabled, Is.False);
            Assert.That(Settings.Ntfy.FileNotices.Select(N => N.Text).Concat(new[] { Settings.Ntfy.StateText ?? string.Empty }), Has.None.Contains(Topic), "the unknown key is not named");
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Text));
            Window.Close();
        }

        // ---- validation ----

        // each problem is shown at its field with its severity in words; problems are warnings while ntfy is off and errors
        // that block the save while it is on or outputTo: Ntfy (as the General form shows it) requires it; http only warns;
        // the timeout's range is the GUI's own error, also while ntfy is off; no message holds a value
        [AvaloniaTest]
        public void Validation_IsShownAtEachField_AndFollowsEnabledAndOutputTo()
        {
            File.WriteAllText(Ntfy, "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            var (Window, _, Settings, Editor) = ShowNtfy();
            NtfySettingsViewModel N = Settings.Ntfy;
            Button Save = In<Button>(Editor, "SaveButton");
            CheckBox Enabled = In<CheckBox>(Editor, "NtfyEnabledCheckBox");
            TextBox Server = In<TextBox>(Editor, "NtfyServerBox");
            Assert.That(N.Enabled.MessageText, Is.EqualTo("Info: enabled is false: Campertron publishes nothing to ntfy."));

            Type(Server, "http://ntfy.example.invalid");
            Assert.That(N.Server.MessageText, Does.StartWith("Warning: server uses http: the topic, the access token and every message travel unencrypted"));
            Assert.That(AutomationProperties.GetHelpText(Server), Is.EqualTo(N.Server.MessageText));
            Assert.That(Save.IsEffectivelyEnabled, Is.True, "a warning does not block");
            Enabled.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(N.Server.IsWarning && Save.IsEffectivelyEnabled, Is.True, "http stays a warning while ntfy is on: a deliberate choice");

            foreach (string Bad in new[] { "ftp://ntfy.example.invalid", "https://user:pw@ntfy.example.invalid", "https://ntfy.example.invalid/" + Topic, "https://ntfy.example.invalid?auth=x", "https://ntfy.example.invalid#x", "ntfy.example.invalid" })
            {
                Type(Server, Bad);
                Assert.That(N.Server.MessageText, Does.StartWith("Error: server must be the ntfy server's address only"), Bad);
                Assert.That(Save.IsEffectivelyEnabled, Is.False, Bad);
                Assert.That(N.Server.MessageText, Does.Not.Contain("ntfy.example.invalid").And.Not.Contain(Topic));
            }
            Enabled.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            // D71: a value written but invalid stays an error while ntfy is off, so it is never saved
            Assert.That(N.Server.MessageText, Does.StartWith("Error: server must be the ntfy server's address only"), "off: still an error");
            Assert.That(Save.IsEffectivelyEnabled, Is.False, "off: a server that is not valid still blocks the save");
            Type(Server, "");
            Assert.That(N.Server.MessageText, Does.StartWith("Warning: server is empty"), "off: a missing server is a warning");
            Assert.That(Save.IsEffectivelyEnabled, Is.True);
            Type(Server, "https://ntfy.example.invalid");
            Enabled.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Type(In<TextBox>(Editor, "NtfyTopicBox"), "");
            Assert.That(N.Topic.MessageText, Is.EqualTo("Error: topic is empty: write the topic Campertron publishes to."));
            Type(In<TextBox>(Editor, "NtfyTopicBox"), "two words");
            Assert.That(N.Topic.MessageText, Is.EqualTo("Error: topic may hold only letters, digits, - and _, up to 64 characters (ntfy's rule)."));
            Type(In<TextBox>(Editor, "NtfyTopicBox"), Topic);
            Assert.That(N.Topic.HasMessage, Is.False);

            AddTag(Editor, "not a tag");
            Assert.That(N.Tags.Rows[0].MessageText, Is.EqualTo("Error: tags item 1 must be 1 to 64 letters, digits, _, + or - (an emoji short code such as tent, or a word)."));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Type(In<TextBox>(TagRows(Editor)[0], "TagBox"), "tent");
            Assert.That(Save.IsEffectivelyEnabled, Is.True);

            TextBox Timeout = In<TextBox>(Editor, "NtfyTimeoutBox");
            foreach (string Value in new[] { "0", "121", "-5", "thirty", "2.5", "" })
            {
                Type(Timeout, Value);
                Assert.That(N.TimeoutSeconds.MessageText, Is.EqualTo("Error: Enter a whole number of seconds from 1 to 120, such as 30."), Value);
                Assert.That(Save.IsEffectivelyEnabled, Is.False, Value);
            }
            Enabled.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Type(Timeout, "500");
            Assert.That(Save.IsEffectivelyEnabled, Is.False, "the range is enforced while ntfy is off too");
            Type(Timeout, "1");
            Assert.That(N.TimeoutSeconds.HasMessage, Is.False);
            Enabled.IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            TextBox TokenFile = In<TextBox>(Editor, "NtfyTokenFileBox");
            Type(TokenFile, "secrets/ntfy_token");
            Assert.That(N.TokenFile.MessageText, Does.StartWith("Error: tokenFile is not an absolute path"));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Type(TokenFile, Path.Join(Folder, "secrets", "ntfy_token"));
            // D75: the token file doesn't exist on this computer: a warning while outputTo is Console, which never blocks Save
            Assert.That(N.TokenFile.MessageText, Is.EqualTo("Warning: The unattended monitoring reads ntfy's access token from the file named by tokenFile when it starts. "
                + "tokenFile: ntfy_token doesn't exist on this computer, so the monitoring with ntfy on would refuse to start on this computer. If Campertron runs somewhere else, such as in a container, the file only has to exist there."));
            Assert.That(N.TokenFile.MessageText, Does.Not.Contain(Folder), "the path is never repeated in a message");
            Assert.That(Save.IsEffectivelyEnabled, Is.True);

            Assert.That(In<ComboBox>(Editor, "NtfyPriorityComboBox").Items.Cast<SettingChoice>().Select(c => c.Value),
                Is.EqualTo(new object[] { NtfyPriority.Min, NtfyPriority.Low, NtfyPriority.Default, NtfyPriority.High, NtfyPriority.Max }));

            // outputTo: Ntfy in the General form (not saved) requires ntfy: off is then an error that blocks the save
            Enabled.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(OutputType.Ntfy));
            Dispatcher.UIThread.RunJobs();
            Assert.That(N.Enabled.MessageText, Is.EqualTo("Error: enabled is false, and outputTo: Ntfy needs ntfy: set enabled: true, or choose another outputTo."));
            Assert.That(Save.IsEffectivelyEnabled, Is.False);
            Enabled.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Save.IsEffectivelyEnabled, Is.True);
            foreach (SettingField Field in new SettingField[] { N.Enabled, N.Server, N.Topic, N.TokenFile, N.Priority, N.Tags, N.TimeoutSeconds })
            {
                Assert.That(Field.MessageText + Field.HelpText, Does.Not.Contain(Topic), Field.Key);
            }
            Window.Close();
        }

        // ---- Save, Revert, Reload and unsaved changes ----

        // Save only with a change and no blocking problem, Revert only with a change, Reload only without; edits stay while
        // the user switches files and tabs, and they keep an automatic restart from happening (D62)
        [AvaloniaTest]
        public void SaveRevertReload_AndUnsavedChanges_FollowTheSettingsConventions()
        {
            string Text = "enabled: false # off\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";
            File.WriteAllText(Ntfy, Text);
            var (Window, ViewModel, Settings, Editor) = ShowNtfy();
            Button Save = In<Button>(Editor, "SaveButton");
            Button Revert = In<Button>(Editor, "RevertButton");
            Button Reload = In<Button>(Editor, "ReloadButton");
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((false, false, true)));

            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((true, true, false)));
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.True, "no automatic restart over unsaved Ntfy.yaml changes");

            // the edit survives switching files and tabs
            Settings.IsEmailSelected = true;
            Tabs(Window).SelectedIndex = 0;
            Tabs(Window).SelectedIndex = SettingsTab;
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.True);
            Assert.That(Settings.Ntfy.IsDirty, Is.True);

            // changing a value back is no edit
            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Ntfy.IsDirty || Save.IsEffectivelyEnabled, Is.False);

            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "15");
            Click(Revert);
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.False);
            Assert.That(In<TextBox>(Editor, "NtfyTimeoutBox").Text, Is.EqualTo("30"));
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Your changes to Ntfy.yaml were reverted."));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Text));
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.False);

            File.WriteAllText(Ntfy, Text.Replace("enabled: false", "enabled: true"));
            Click(Reload);
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.True);
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Reloaded Ntfy.yaml."));
            Assert.That((Save.IsEffectivelyEnabled, Revert.IsEffectivelyEnabled, Reload.IsEffectivelyEnabled), Is.EqualTo((false, false, true)));
            Window.Close();
        }

        // a change by another program: a clean editor shows it; an edited one opens the conflict card and writes nothing until
        // the user chooses; a Ntfy.yaml another program creates while the user prepares a new one is never written over
        [AvaloniaTest]
        public void ChangesByAnotherProgram_ReloadACleanEditor_AndOpenAConflictForAnEditedOne()
        {
            var (Window, _, Settings, Editor) = ShowNtfy();
            Type(In<TextBox>(Editor, "NtfyTopicBox"), Topic);
            string Theirs = "enabled: true # theirs\nserver: https://ntfy.example.invalid\ntopic: theirTopic_1\n";
            File.WriteAllText(Ntfy, Theirs);
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<Border>(Editor, "ConflictCard").IsVisible, Is.True);
            Assert.That(In<SelectableTextBlock>(Editor, "ConflictText").Text, Is.EqualTo("Ntfy.yaml was created by another program while you were preparing a new one. Both set: topic. Your changes are kept here; nothing was written."));
            Assert.That(In<Button>(Editor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Assert.That(In<Button>(Editor, "ApplyMineButton").Content, Is.EqualTo("Keep my values for topic"));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Theirs), "never written over");

            Click(In<Button>(Editor, "ApplyMineButton"));
            Assert.That(Settings.Ntfy.IsNew, Is.False);
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.True, "their other settings are kept");
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Theirs.Replace("theirTopic_1", Topic)));

            // clean: another program's change is shown; edited: a conflict until the user chooses
            File.WriteAllText(Ntfy, Theirs.Replace("enabled: true", "enabled: false"));
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked, Is.False);
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Ntfy.yaml was changed on disk and has been reloaded."));
            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "60");
            File.WriteAllText(Ntfy, Theirs);
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<SelectableTextBlock>(Editor, "ConflictText").Text, Does.StartWith("Ntfy.yaml was changed by another program while you were editing it, in other settings than yours."));
            Click(In<Button>(Editor, "UseTheirsButton"));
            Assert.That(Settings.Ntfy.IsDirty, Is.False);
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Theirs));

            // deleted on disk: a clean editor becomes a new file again
            File.Delete(Ntfy);
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.Ntfy.IsNew, Is.True);
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Ntfy.yaml was deleted on disk."));
            Assert.That(File.Exists(Ntfy), Is.False);
            Window.Close();
        }

        // ---- security ----

        // the access token is never read: the token file keeps its bytes and time, CAMPERTRON_NTFY_TOKEN is never asked for,
        // and neither appears anywhere in the window; the topic is shown only in the Ntfy.yaml editor's own box and preview,
        // never elsewhere in the window, in a message, in the current configuration or what Copy copies
        [AvaloniaTest]
        public void TheTokenIsNeverRead_AndTheTopicIsShownOnlyInTheNtfyEditor()
        {
            string FileCanary = "tokFileCnry" + Guid.NewGuid().ToString("N");
            string EnvCanary = "tokEnvCnry" + Guid.NewGuid().ToString("N");
            string TokenPath = Path.Join(Folder, "secrets", "ntfy_token");
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            File.WriteAllText(TokenPath, FileCanary + "\n");
            File.SetLastWriteTimeUtc(TokenPath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            File.WriteAllText(Ntfy, "enabled: true\nserver: https://ntfy-canary.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenPath + "'\n");
            Func<string, string?> OriginalEnvironment = NtfyToken.GetEnvironment;
            int Asked = 0;
            NtfyToken.GetEnvironment = Name => { Asked++; return EnvCanary; };
            try
            {
                var (Window, ViewModel, Settings, Editor) = ShowNtfy();
                Assert.That(In<TextBox>(Editor, "NtfyTopicBox").Text, Is.EqualTo(Topic));
                Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "40");
                Assert.That(In<SelectableTextBlock>(Editor, "PreviewText").Text, Does.Not.Contain(FileCanary));
                Click(In<Button>(Editor, "SaveButton"));
                Assert.That(File.ReadAllText(Ntfy), Does.Not.Contain(FileCanary).And.Not.Contain(EnvCanary), "the token is never serialized");

                List<string> All = SettingsEditorTests.Exposed(Window);
                Assert.That(All.Count, Is.GreaterThan(100), "the scan saw the window");
                Assert.That(All.Where(T => T.Contains(FileCanary, StringComparison.Ordinal) || T.Contains(EnvCanary, StringComparison.Ordinal)), Is.Empty, "no token anywhere");
                List<string> Outside = ExposedOutsideTheValues(Window, Editor);
                Assert.That(Outside.Where(T => T.Contains(Topic, StringComparison.Ordinal) || T.Contains("ntfy-canary.example.invalid", StringComparison.Ordinal)
                    || T.Contains(TokenPath, StringComparison.Ordinal)), Is.Empty, "the topic, server and token path only in their boxes and the preview, nowhere else in the window or the editor");
                IEnumerable<string> Messages = Settings.Ntfy.FileNotices.Select(N => N.Text).Concat(new[] { Settings.Ntfy.StatusText ?? "", Settings.General.NtfyStatus, Settings.Ntfy.ModeText });
                Assert.That(Messages.Where(T => T.Contains(Topic, StringComparison.Ordinal) || T.Contains(TokenPath, StringComparison.Ordinal)), Is.Empty);

                // Settings > System's current configuration and what Copy copies never read Ntfy.yaml
                SettingsTabs(Window).SelectedIndex = MainWindow.SystemSettingsTabIndex;
                Dispatcher.UIThread.RunJobs();
                CurrentConfigurationViewModel Configuration = ViewModel.SystemSettings.Configuration!;
                string? Copied = null;
                Configuration.CopyText = Text => { Copied = Text; return Task.FromResult(true); };
                WaitFor(Configuration.Copy());
                Assert.That(Copied, Is.Not.Null);
                Assert.That(Copied + Configuration.Text, Does.Not.Contain(Topic).And.Not.Contain("ntfy-canary").And.Not.Contain(TokenPath));

                Assert.That(File.ReadAllText(TokenPath), Is.EqualTo(FileCanary + "\n"));
                Assert.That(File.GetLastWriteTimeUtc(TokenPath), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)), "the token file is never written");
                Assert.That(Directory.GetFileSystemEntries(Path.GetDirectoryName(TokenPath)!), Is.EqualTo(new[] { TokenPath }));
                Assert.That(Asked, Is.Zero, "the GUI never reads CAMPERTRON_NTFY_TOKEN");
                Window.Close();
            }
            finally
            {
                NtfyToken.GetEnvironment = OriginalEnvironment;
            }
        }

        // clearing tokenFile removes its line (never an empty token source); an empty tokenFile in the file is removed with
        // Remove tokenFile; a comment on its line goes with it and is announced
        [AvaloniaTest]
        public void ClearingTokenFile_RemovesItsLine_AndRemoveTokenFileRemovesAnEmptyOne()
        {
            string TokenPath = Path.Join(Folder, "ntfy_token");
            string Text = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenPath + "' # mounted\ntimeoutSeconds: 30\n";
            File.WriteAllText(Ntfy, Text);
            var (Window, _, Settings, Editor) = ShowNtfy();
            Assert.That(In<Button>(Editor, "RemoveTokenFileButton").IsVisible, Is.True);
            Type(In<TextBox>(Editor, "NtfyTokenFileBox"), "");
            Assert.That(Settings.Ntfy.TokenFile.IsChanged, Is.True);
            Assert.That(Settings.Ntfy.TokenFile.MessageText, Is.EqualTo("Warning: Removing tokenFile also removes 1 comment that belongs to it."));
            Assert.That(In<SelectableTextBlock>(Editor, "PreviewText").Text, Does.Contain("- tokenFile: '" + TokenPath + "' # mounted"));
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Text.Replace("tokenFile: '" + TokenPath + "' # mounted\n", "")));
            Assert.That(In<Button>(Editor, "RemoveTokenFileButton").IsVisible, Is.False);

            File.WriteAllText(Ntfy, Text.Replace("tokenFile: '" + TokenPath + "' # mounted", "tokenFile:"));
            Click(In<Button>(Editor, "ReloadButton"));
            Assert.That(Settings.Ntfy.TokenFile.MessageText, Does.StartWith("Error: tokenFile is empty"));
            Assert.That(In<Button>(Editor, "SaveButton").IsEffectivelyEnabled, Is.False);
            Click(In<Button>(Editor, "RemoveTokenFileButton"));
            Assert.That(Settings.Ntfy.TokenFile.HasMessage, Is.False);
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Ntfy), Is.EqualTo(Text.Replace("tokenFile: '" + TokenPath + "' # mounted\n", "")));
            Window.Close();
        }

        // ---- provider modes, layout, keyboard ----

        // the editor says what ntfy does for the outputTo the General form shows and never changes outputTo; Choose outputTo in
        // General.yaml shows General.yaml
        [AvaloniaTest]
        public void TheModeFollowsTheGeneralOutput_AndTheEditorNeverChangesOutputTo()
        {
            var (Window, _, Settings, Editor) = ShowNtfy();
            TextBlock Mode = In<TextBlock>(Editor, "NtfyModeText");
            Assert.That(Mode.Text, Does.StartWith("outputTo is Console: nothing is sent. ntfy is used only by the unattended monitoring"));
            Assert.That(In<TextBlock>(Editor, "NtfyModeHelp").Text, Does.Contain("restart it or its container"));

            ComboBox Output = In<ComboBox>(In<Control>(Named<SettingsView>(Window, "SettingsContent"), "GeneralEditor"), "OutputToComboBox");
            Choose(Output, OutputType.Email);
            Assert.That(Mode.Text, Is.EqualTo("outputTo is Email: the unattended monitoring sends emails only, because Enabled is off."));
            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Mode.Text, Is.EqualTo("outputTo is Email: the unattended monitoring sends emails and, while Enabled is on, ntfy notifications too."));
            Choose(Output, OutputType.Ntfy);
            Assert.That(Mode.Text, Does.StartWith("outputTo is Ntfy: the unattended monitoring sends new openings with ntfy only."));
            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Mode.Text, Does.StartWith("outputTo is Ntfy, which needs ntfy: turn Enabled on"));

            Click(In<Button>(Editor, "ShowGeneralButton"));
            Assert.That(Settings.IsGeneralSelected, Is.True);
            Assert.That(((SettingChoice)Output.SelectedItem!).Value, Is.EqualTo(OutputType.Ntfy), "the user's unsaved choice, not the editor's");
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText), "nothing changed outputTo in the file");
            Window.Close();
        }

        // at the supported sizes, in Light and Dark, the editor with long messages stays inside the window (no horizontal
        // scrolling), errors use the theme's error brush, and nothing is logged
        [AvaloniaTest]
        public void TheNtfyEditor_FitsEverySupportedSize_InLightAndDark([Values(720, 1024, 1600)] int Width, [Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            ThemeVariant Theme = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = Theme;
            var (Window, _, Settings, Editor) = ShowNtfy();
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1000);
            In<CheckBox>(Editor, "NtfyEnabledCheckBox").IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Type(In<TextBox>(Editor, "NtfyServerBox"), "https://" + string.Concat(Enumerable.Repeat("x", 300)) + ".example.invalid/path");
            Type(In<TextBox>(Editor, "NtfyTimeoutBox"), "a timeout far too long to be one and written out in words so that the message wraps");
            AddTag(Editor, string.Concat(Enumerable.Repeat("t", 200)));
            Window.UpdateLayout();

            ScrollViewer Scroll = In<ScrollViewer>(Named<SettingsView>(Window, "SettingsContent"), "SettingsScrollViewer");
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            foreach (Control C in Editor.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && (c is TextBlock || c is TextBox || c is Button || c is CheckBox || c is ComboBox)))
            {
                Point? Right = C.TranslatePoint(new Point(C.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), C.Name ?? C.GetType().Name);
            }
            Application.Current.TryGetResource("SeverityErrorTextBrush", Theme, out object? Error); // D74: the error severity's text
            TextBlock Message = Editor.GetVisualDescendants().OfType<TextBlock>().First(T => T.Text != null && T.Text.StartsWith("Error: server must be", StringComparison.Ordinal));
            Assert.That(((ISolidColorBrush)Message.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Error!).Color));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        // keyboard: from the file selector Tab reaches every field of the editor in reading order; every field's label
        // targets it and names it
        [AvaloniaTest]
        public void Keyboard_ReachesEveryNtfyFieldInOrder_AndEveryFieldIsLabelled()
        {
            var (Window, _, Settings, Editor) = ShowNtfy();
            In<RadioButton>(Named<SettingsView>(Window, "SettingsContent"), "NtfyFileButton").Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            var Visited = new List<string?>();
            for (int i = 0; i < 12; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                IInputElement? F = Focused(Window);
                Visited.Add(F is TabItem T ? "Tab:" + T.Header : (F as Control)?.Name ?? F?.GetType().Name);
            }
            // v0.3.0's PR 5: the fourth file's button (Web.yaml) and Generate a random topic, after the topic's box
            Assert.That(Visited, Is.EqualTo(new[]
            {
                "WebFileButton", "OpenFolderButton", "ShowGeneralButton", "NtfyEnabledCheckBox", "NtfyServerBox", "NtfyTopicBox", "GenerateTopicButton", "NtfyTokenFileBox",
                "NtfyPriorityComboBox", "AddTagButton", "NtfyTimeoutBox", "ReloadButton",
            }), string.Join(", ", Visited));
            foreach (Label L in Editor.GetVisualDescendants().OfType<Label>())
            {
                Control Target = (Control)L.Target!;
                string Expected = ((string)L.Content!).Replace("_", "");
                if (Target.Name == "AddTagButton")
                {
                    Assert.That(ControlAutomationPeer.CreatePeerForElement(Target).GetName(), Is.EqualTo("Add tag"));
                    Assert.That(AutomationProperties.GetName(In<ItemsControl>(Editor, "NtfyTagsList")), Is.EqualTo(Expected));
                    continue;
                }
                Assert.That(AutomationProperties.GetName(Target), Is.EqualTo(Expected), Target.Name);
            }
            Assert.That(Editor.GetVisualDescendants().OfType<Label>().Select(L => ((Control)L.Target!).Name),
                Is.EqualTo(new[] { "NtfyServerBox", "NtfyTopicBox", "NtfyTokenFileBox", "NtfyPriorityComboBox", "AddTagButton", "NtfyTimeoutBox" }));
            Assert.That(AutomationProperties.GetName(In<CheckBox>(Editor, "NtfyEnabledCheckBox")), Is.EqualTo("Enabled: send new openings with ntfy"));
            // D88: the token is read only for the test notification the user asks for
            Assert.That(AutomationProperties.GetHelpText(In<TextBox>(Editor, "NtfyTokenFileBox")), Does.Contain("CAMPERTRON_NTFY_TOKEN").And.Contain("this window reads the token only to send a test notification when you ask for one, never shows it"));
            Window.Close();
        }
    }
}
