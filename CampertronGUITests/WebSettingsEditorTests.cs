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
    // Web.yaml in Settings > Campertron (v0.3.0's PR 5; the Full UI rule, D84: the web UI's settings are edited in both UIs,
    // with the same form, rules and words), in the real window on the headless platform: a missing Web.yaml is a new file
    // with D81's defaults that only Save creates, owner-only; a save that turns the password off while the web UI listens on
    // the network asks once in the window (D81 point 4), and an accepted state never asks again. The web UI's password is
    // never edited here. Each test has its own settings folder.
    public class WebSettingsEditorTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string GeneralText = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";
        private const string EmailText = "smtpServer: mail.example.org\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: ''\nsendToAddressList:\n- first@example.org\nsendFromAddress: camper@example.org\n";

        private string Folder = string.Empty;
        private string WebYaml => Path.Join(Folder, "Web.yaml");

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "web-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText);
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), EmailText);
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

        private (MainWindow Window, SettingsViewModel Settings, Control Editor) ShowWeb()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            Control View = Named<SettingsView>(Window, "SettingsContent");
            RadioButton Button = In<RadioButton>(View, "WebFileButton");
            Button.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            return (Window, ViewModel.Settings!, In<Control>(View, "WebEditor"));
        }

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        private static void Check(CheckBox Box, bool Value)
        {
            Box.IsChecked = Value;
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaTest]
        public void WebYaml_IsTheFourthSettingsFile_ANewFileWithTheDefaults_ThatOnlySaveCreates()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, Settings, Editor) = ShowWeb();
            Control View = Named<SettingsView>(Window, "SettingsContent");
            Assert.That(AutomationProperties.GetName(In<RadioButton>(View, "WebFileButton")), Is.EqualTo("Web.yaml"));
            Assert.That(Editor.IsEffectivelyVisible, Is.True);
            Assert.That(Settings.IsWebSelected, Is.True);
            Assert.That(Settings.Web.IsNew, Is.True);
            Assert.That(In<CheckBox>(Editor, "PasswordProtectionCheckBox").IsChecked, Is.True);
            Assert.That(In<CheckBox>(Editor, "NetworkListeningCheckBox").IsChecked, Is.True);
            Assert.That(In<TextBlock>(Editor, "WebExplanation").Text, Is.EqualTo(Settings.Web.Explanation));
            Assert.That(In<TextBlock>(Editor, "WebPrecedence").Text, Does.StartWith("The command line decides first: --listen"));
            Assert.That(In<Border>(Editor, "WebConfirmationCard").IsVisible, Is.False);
            Assert.That(In<Button>(Editor, "SaveButton").IsEffectivelyEnabled, Is.False, "nothing to save before a change");
            Settings.CheckForExternalChanges();
            Dispatcher.UIThread.RunJobs();
            Assert.That(File.Exists(WebYaml), Is.False, "looking writes nothing");
            Assert.That(Settings.HasUnsavedChanges, Is.False);

            //a host name in the list, then Save: the file with the defaults and the name, owner-only
            Control Hosts = In<Control>(Editor, "AllowedHostsList");
            Click(In<Button>(Hosts, "AddButton"));
            TextBox Row = Hosts.GetVisualDescendants().OfType<TextBox>().Single(B => B.Name == "RowBox");
            Row.Text = "campbox.home.example";
            Dispatcher.UIThread.RunJobs();
            Assert.That(Settings.HasUnsavedChanges, Is.True, "an automatic restart waits for Web.yaml's changes too (D62)");
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(Settings.Web.StatusIsError, Is.False, Settings.Web.StatusText);
            Assert.That(File.ReadAllText(WebYaml), Does.Contain("passwordProtection: true").And.Contain("networkListening: true").And.Contain("campbox.home.example"));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(WebYaml), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        [AvaloniaTest]
        public void TurningThePasswordOff_AsksOnceInTheWindow_AndTheAcceptedStateNeverAsksAgain()
        {
            File.WriteAllText(WebYaml, "# the web UI\npasswordProtection: true\nnetworkListening: true\n");
            var (Window, Settings, Editor) = ShowWeb();
            Border Card = In<Border>(Editor, "WebConfirmationCard");

            Check(In<CheckBox>(Editor, "PasswordProtectionCheckBox"), false);
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(Card.IsEffectivelyVisible, Is.True);
            Assert.That(In<TextBlock>(Editor, "WebConfirmationText").Text, Does.StartWith("Without a password, everyone who can reach this computer on the network"));
            Assert.That(In<TextBlock>(Editor, "WebConfirmationText").FindAncestorOfType<SeverityBlock>()!.Severity, Is.EqualTo("Warning"));
            Assert.That(In<Button>(Editor, "ConfirmSaveButton").Content, Is.EqualTo("Save without a password, reachable from the network"));
            Assert.That(File.ReadAllText(WebYaml), Does.Contain("passwordProtection: true"), "nothing written yet");

            //Don't save keeps the change and writes nothing
            Click(In<Button>(Editor, "CancelConfirmationButton"));
            Assert.That(Card.IsVisible, Is.False);
            Assert.That(Settings.Web.IsDirty, Is.True);

            Click(In<Button>(Editor, "SaveButton"));
            Click(In<Button>(Editor, "ConfirmSaveButton"));
            Assert.That(Card.IsVisible, Is.False);
            Assert.That(File.ReadAllText(WebYaml), Is.EqualTo("# the web UI\npasswordProtection: false\nnetworkListening: true\n"));

            //another setting of the accepted state: saved at once
            Control Proxies = In<Control>(Editor, "TrustedProxiesList");
            Click(In<Button>(Proxies, "AddButton"));
            Proxies.GetVisualDescendants().OfType<TextBox>().Single(B => B.Name == "RowBox").Text = "172.18.0.2";
            Dispatcher.UIThread.RunJobs();
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(Card.IsVisible, Is.False, "no second warning for the accepted state");
            Assert.That(File.ReadAllText(WebYaml), Does.Contain("172.18.0.2"));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
