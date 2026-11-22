using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The fix after v0.2.0 step 3's local test (§BU): General.yaml's outputTo says what is wrong with every secret file the
    // providers of the chosen outputTo would read (Email.yaml's smtpPasswordFile for Email; Ntfy.yaml's tokenFile while ntfy
    // is on, for Email or Ntfy; both when combined), in D75's words after the settings file that names it, as an error (a
    // warning for a file others can read), for a newly chosen outputTo and a saved one, when the GUI opens and after the
    // window's activation. Save is never blocked by it; the other rules at outputTo stay. The General form's ntfy line says
    // the same about the token file and is styled by its severity. The files are never opened. Only what existed before the
    // fix is used here, so the class also runs against an older GUI: on b677f83 it fails (the negative control of §BU).
    public class SecretFileOutputTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string Topic = "secretOutputTopic0123456789";
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const string EmailText = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: ''\n";
        private const string Recipients = "sendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        private string Folder = string.Empty;
        private string Secrets = string.Empty;
        private string PasswordFile => Path.Join(Secrets, "smtp_password");
        private string TokenFile => Path.Join(Secrets, "ntfy_token");

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "secret-output-" + Guid.NewGuid().ToString("N"));
            Secrets = Path.Join(Folder, "secrets");
            Directory.CreateDirectory(Secrets);
            File.WriteAllText(Path.Join(Folder, "Watch.yaml"), "displayName: Watch\nautoRun: true\ncampgroundID: '232445'\n");
            General("Console");
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

        private void General(string Output) => File.WriteAllText(Path.Join(Folder, "General.yaml"),
            "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n");

        private void Email(bool WithFile) => File.WriteAllText(Path.Join(Folder, "Email.yaml"),
            EmailText + (WithFile ? "smtpPasswordFile: '" + PasswordFile + "'\n" : string.Empty) + Recipients);

        private void Ntfy(bool Enabled) => File.WriteAllText(Path.Join(Folder, "Ntfy.yaml"),
            "enabled: " + (Enabled ? "true" : "false") + "\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenFile + "'\n");

        private static bool IsRoot => Environment.UserName == "root";

        // a secret file in one of the states the GUI tells
        private static void State(string FilePath, string Name)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
            else if (Directory.Exists(FilePath))
            {
                Directory.Delete(FilePath);
            }
            switch (Name)
            {
                case "missing":
                    return;
                case "folder":
                    Directory.CreateDirectory(FilePath);
                    return;
                case "empty":
                    File.WriteAllText(FilePath, string.Empty);
                    break;
                case "large":
                    File.WriteAllText(FilePath, new string('x', 5000));
                    break;
                default:
                    File.WriteAllText(FilePath, "not-a-real-secret\n");
                    break;
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FilePath, Name switch
                {
                    "unreadable" => UnixFileMode.UserWrite,
                    "others" => OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                    _ => OwnerOnly
                });
            }
        }

        // D75's words for a file in each state, in use (null: nothing to say)
        private static string? Said(string Key, string File, string Kind, string Name, bool InUse = true)
        {
            string Mode = Kind == "password" ? "email mode" : "the monitoring with ntfy on";
            string Consequence = "so " + Mode + (InUse ? " refuses" : " would refuse") + " to start on this computer.";
            return Name switch
            {
                "missing" => Key + ": " + File + " doesn't exist on this computer, " + Consequence + " If Campertron runs somewhere else, such as in a container, the file only has to exist there.",
                "folder" => Key + ": " + File + " is a folder, not a file, " + Consequence,
                "unreadable" => Key + ": you can't read " + File + " on this computer, " + Consequence + " Check the file's owner and permissions.",
                "empty" => Key + ": " + File + " is empty, " + Consequence,
                "large" => Key + ": " + File + " is larger than 4,096 bytes, too large for a " + Kind + " file, " + Consequence,
                "others" => Key + ": other users on this computer can read " + File + ". Make it readable only by you, for example with chmod 600 " + File + ".",
                _ => null
            };
        }

        private (MainWindow Window, SettingsViewModel Settings) Show()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Settle(Window);
            return (Window, ViewModel.Settings!);
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static void Select(MainWindow Window, SettingsViewModel Settings, string File)
        {
            Settings.IsGeneralSelected = File == "General";
            Settings.IsEmailSelected = File == "Email";
            Settings.IsNtfySelected = File == "Ntfy";
            Settle(Window);
        }

        private static void Choose(MainWindow Window, SettingsViewModel Settings, OutputType Output)
        {
            Settings.General.OutputTo.Selected = Settings.General.OutputTo.Choices.Single(c => c.Value.Equals(Output));
            Settle(Window);
        }

        private static Control Editor(MainWindow Window, string Name) =>
            Named<SettingsView>(Window, "SettingsContent").GetVisualDescendants().OfType<Control>().First(c => c.Name == Name);

        private static Button SaveButton(MainWindow Window, string EditorName) =>
            Editor(Window, EditorName).GetVisualDescendants().OfType<Button>().First(b => b.Name == "SaveButton");

        // the TextBlock that shows outputTo's message, and the severity it is shown in (its block's, D74)
        private static string ShownSeverity(MainWindow Window, SettingsViewModel Settings)
        {
            string Text = Settings.General.OutputTo.MessageText;
            TextBlock Shown = Editor(Window, "GeneralEditor").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Text);
            Assert.That(Shown.IsEffectivelyVisible, Is.True, "the message is on screen");
            return SeverityOf(Shown);
        }

        private static TextBlock NtfyLine(MainWindow Window) =>
            Editor(Window, "GeneralEditor").GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "NtfyStatus");

        // ---- the maintainer's case ----

        // outputTo: Console saved; smtpPasswordFile set to a file that doesn't exist and saved in the Email.yaml editor (its
        // field says so in red); then outputTo Email in General.yaml: Output says the same, as an error, and Save still saves
        [AvaloniaTest]
        public void TheLocalTestsCase_AMissingPasswordFile_IsTold_AtOutput_WhenEmailIsChosen_AndSaveStillSaves()
        {
            Email(false);
            var (Window, Settings) = Show();
            Select(Window, Settings, "Email");
            Settings.Email.SmtpPasswordFile.Text = PasswordFile;
            Settle(Window);
            Assert.That(Settings.Email.SmtpPasswordFile.MessageText, Does.StartWith("Warning: ").And.Contain("smtp_password doesn't exist on this computer"),
                "with outputTo Console a warning at the field");
            SaveButton(Window, "EmailEditor").Command!.Execute(null);
            Settle(Window);
            Assert.That(File.ReadAllText(Path.Join(Folder, "Email.yaml")), Does.Contain("smtpPasswordFile: " + PasswordFile));

            Select(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "outputTo Console uses no secret file");
            Choose(Window, Settings, OutputType.Email);
            string Expected = "Error: Email.yaml: " + Said("smtpPasswordFile", "smtp_password", "password", "missing");
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo(Expected));
            Assert.That(ShownSeverity(Window, Settings), Is.EqualTo("Error"));
            Assert.That(Settings.General.OutputTo.MessageText, Does.Not.Contain(Secrets), "the file's name only, never its folder");
            Assert.That(SaveButton(Window, "GeneralEditor").IsEffectivelyEnabled, Is.True, "a file problem never blocks Save (D75)");
            SaveButton(Window, "GeneralEditor").Command!.Execute(null);
            Settle(Window);
            Assert.That(File.ReadAllText(Path.Join(Folder, "General.yaml")), Does.Contain("outputTo: Email"));
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo(Expected), "saved: still an error");
            Select(Window, Settings, "Email");
            Assert.That(Settings.Email.SmtpPasswordFile.MessageText, Does.StartWith("Error: "), "the field agrees");
            Window.Close();
        }

        // ---- every state, for each provider and both, newly chosen and already saved ----

        [AvaloniaTest]
        public void EveryState_IsTold_AtOutput_ForEmailNtfyAndBoth_ChosenOrSaved(
            [Values("Email", "Ntfy", "Both")] string Providers,
            [Values("missing", "folder", "unreadable", "empty", "large", "others", "owner")] string Name,
            [Values(false, true)] bool Saved)
        {
            if (Name == "unreadable" && IsRoot)
            {
                Assert.Ignore("root may read any file");
            }
            if (OperatingSystem.IsWindows() && Name is "unreadable" or "others")
            {
                Assert.Ignore("Unix permissions");
            }
            bool UsesEmail = Providers != "Ntfy", UsesNtfy = Providers != "Email";
            OutputType Output = Providers == "Ntfy" ? OutputType.Ntfy : OutputType.Email;
            Email(UsesEmail);
            if (UsesNtfy)
            {
                Ntfy(true);
            }
            State(PasswordFile, UsesEmail ? Name : "owner");
            State(TokenFile, UsesNtfy ? Name : "owner");
            General(Saved ? Output.ToString() : "Console");
            var (Window, Settings) = Show();
            Select(Window, Settings, "General");
            if (!Saved)
            {
                Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "Console: nothing to say");
                Choose(Window, Settings, Output);
            }
            var Parts = new List<string>();
            if (UsesEmail && Said("smtpPasswordFile", "smtp_password", "password", Name) is string P)
            {
                Parts.Add("Email.yaml: " + P);
            }
            if (UsesNtfy && Said("tokenFile", "ntfy_token", "token", Name) is string T)
            {
                Parts.Add("Ntfy.yaml: " + T);
            }
            if (Parts.Count == 0)
            {
                Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "an owner-only file: nothing to say");
            }
            else
            {
                string Severity = Name == "others" ? "Warning" : "Error";
                Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo(Severity + ": " + string.Join(" ", Parts)));
                Assert.That(ShownSeverity(Window, Settings), Is.EqualTo(Severity));
                Assert.That(Settings.General.OutputTo.MessageText, Does.Not.Contain(Secrets));
            }
            if (Saved)
            {
                Settings.General.RefreshInterval.Text = "31";
                Settle(Window);
            }
            Assert.That(SaveButton(Window, "GeneralEditor").IsEffectivelyEnabled, Is.True, "never blocked by a secret file");
            Window.Close();
        }

        // ---- the providers the chosen outputTo would not use say nothing at Output ----

        [AvaloniaTest]
        public void AProviderNotUsed_SaysNothingAtOutput()
        {
            Email(true);
            Ntfy(false);
            State(PasswordFile, "missing");
            State(TokenFile, "missing");
            var (Window, Settings) = Show();
            Select(Window, Settings, "General");
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "outputTo Console reads neither file");
            Choose(Window, Settings, OutputType.Email);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Error: Email.yaml: " + Said("smtpPasswordFile", "smtp_password", "password", "missing")),
                "ntfy is off: its token file isn't read");
            // outputTo: Ntfy reads no SMTP password; with ntfy off the start refuses for that (D71), and the token file is not yet read
            Choose(Window, Settings, OutputType.Ntfy);
            Assert.That(Settings.General.OutputTo.MessageText, Does.Not.Contain("smtpPasswordFile").And.Not.Contain("tokenFile"));
            Assert.That(Settings.General.OutputTo.MessageText, Does.Contain("Ntfy.yaml"), "the start's own refusal of ntfy off stays");
            Window.Close();
        }

        // ---- the other rules at outputTo stay ----

        [AvaloniaTest]
        public void TheOtherRulesAtOutput_Stay_AndStillBlock()
        {
            Email(true);
            State(PasswordFile, "missing");
            File.Delete(Path.Join(Folder, "Watch.yaml"));
            var (Window, Settings) = Show();
            Select(Window, Settings, "General");
            Choose(Window, Settings, OutputType.Email);
            string Message = Settings.General.OutputTo.MessageText;
            Assert.That(Message, Does.StartWith("Error: With outputTo: Email, Campertron would refuse to start: Campertron needs at least one enabled search"),
                "D73's check comes first, as before");
            Assert.That(Message, Does.EndWith("Email.yaml: " + Said("smtpPasswordFile", "smtp_password", "password", "missing")), "the file after it");
            Assert.That(SaveButton(Window, "GeneralEditor").IsEffectivelyEnabled, Is.False, "no search to check still blocks (D73)");
            File.WriteAllText(Path.Join(Folder, "Watch.yaml"), "displayName: Watch\nautoRun: true\ncampgroundID: '232445'\n");
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(SaveButton(Window, "GeneralEditor").IsEffectivelyEnabled, Is.True, "only the file is left: Save is allowed");
            // a path that is not fully qualified is the start's own refusal (D71) and still blocks; it gets no file message
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), EmailText + "smtpPasswordFile: secrets/smtp_password\n" + Recipients);
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(Settings.General.OutputTo.MessageText, Does.StartWith("Error: With outputTo: Email, Campertron would refuse to start because of Email.yaml."));
            Assert.That(Settings.General.OutputTo.MessageText, Does.Not.Contain("doesn't exist on this computer"));
            Assert.That(SaveButton(Window, "GeneralEditor").IsEffectivelyEnabled, Is.False);
            Window.Close();
        }

        // ---- the GUI's opening, and the window's activation after the files changed on disk ----

        [AvaloniaTest]
        public void TheGuiOpening_AndActivation_LookAtTheFilesAgain()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            Email(true);
            Ntfy(true);
            State(PasswordFile, "missing");
            State(TokenFile, "folder");
            General("Email");
            var (Window, Settings) = Show();
            // the General form is shown first: nothing else was opened
            Assert.That(Settings.IsGeneralSelected, Is.True);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Error: Email.yaml: " + Said("smtpPasswordFile", "smtp_password", "password", "missing")
                + " Ntfy.yaml: " + Said("tokenFile", "ntfy_token", "token", "folder")), "when the GUI opens");
            State(PasswordFile, "owner");
            State(TokenFile, "owner");
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(Settings.General.OutputTo.HasMessage, Is.False, "activation: both files are fine now");
            File.SetUnixFileMode(TokenFile, OwnerOnly | UnixFileMode.OtherRead);
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(Settings.General.OutputTo.MessageText, Is.EqualTo("Warning: Ntfy.yaml: " + Said("tokenFile", "ntfy_token", "token", "others")));
            Assert.That(ShownSeverity(Window, Settings), Is.EqualTo("Warning"));
            Window.Close();
        }

        // ---- the General form's ntfy line: the token file too, styled by its severity ----

        [AvaloniaTest]
        public void TheNtfyLine_SaysWhatIsWrongWithTheTokenFile_InItsSeverity()
        {
            Email(false);
            var (Window, Settings) = Show();
            Select(Window, Settings, "General");
            Assert.That(SeverityOf(NtfyLine(Window)), Is.EqualTo("Info"), "no Ntfy.yaml: ntfy is off");
            Choose(Window, Settings, OutputType.Ntfy);
            Assert.That(NtfyLine(Window).Text, Does.StartWith("Ntfy.yaml does not exist, and outputTo: Ntfy needs it"));
            Assert.That(SeverityOf(NtfyLine(Window)), Is.EqualTo("Error"), "the start would refuse it");
            Choose(Window, Settings, OutputType.Console);

            Ntfy(true);
            State(TokenFile, "missing");
            Settings.CheckForExternalChanges();
            Settle(Window);
            string NotInUse = "Ntfy.yaml: Warning: " + Said("tokenFile", "ntfy_token", "token", "missing", InUse: false);
            Assert.That(NtfyLine(Window).Text, Is.EqualTo(NotInUse), "outputTo Console: ntfy would refuse, a warning");
            Assert.That(SeverityOf(NtfyLine(Window)), Is.EqualTo("Warning"));
            SeverityBlock Block = (SeverityBlock)NtfyLine(Window).Parent!;
            Assert.That(Block.AccessibleText, Is.EqualTo(NotInUse), "read once, with its severity, not twice");
            Choose(Window, Settings, OutputType.Email);
            Assert.That(NtfyLine(Window).Text, Is.EqualTo("Ntfy.yaml: Error: " + Said("tokenFile", "ntfy_token", "token", "missing")));
            Assert.That(SeverityOf(NtfyLine(Window)), Is.EqualTo("Error"));
            Assert.That(NtfyLine(Window).Text, Does.Not.Contain(Secrets).And.Not.Contain(Topic));

            State(TokenFile, "owner");
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(NtfyLine(Window).Text, Does.StartWith("Ntfy.yaml: ntfy is on."));
            Assert.That(SeverityOf(NtfyLine(Window)), Is.EqualTo("Info"));
            Window.Close();
        }

        // ---- never opened ----

        // inotify watches the secrets' folder while the GUI opens with outputTo Email and both providers, outputTo changes,
        // and the window is activated; the test's own read afterwards is seen
        [AvaloniaTest]
        public void TheFiles_AreNeverOpened_AsInotifySees()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("inotify is Linux's");
                return;
            }
            Email(true);
            Ntfy(true);
            State(PasswordFile, "others");
            State(TokenFile, IsRoot ? "owner" : "unreadable");
            General("Email");
            using var Watch = new SecretFileGuiTests.Inotify(Secrets);
            var (Window, Settings) = Show();
            Select(Window, Settings, "General");
            Choose(Window, Settings, OutputType.Ntfy);
            Choose(Window, Settings, OutputType.Email);
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(Settings.General.OutputTo.MessageText, Does.Contain("can read smtp_password"), "the files were looked at");
            Assert.That(Watch.Events(), Is.Empty, "no open, read or close of a secret file");
            File.ReadAllBytes(PasswordFile);
            Assert.That(Watch.Events(), Does.Contain("smtp_password: open"), "the watch sees an open: the positive control");
            Window.Close();
        }
    }
}
