using System.Runtime.InteropServices;
using Avalonia;
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
    // D75: the secret files the Settings tab names (Email.yaml's smtpPasswordFile, Ntfy.yaml's tokenFile), judged from their
    // metadata only: a missing file, a folder, an unreadable file (an error while that provider is in use, else a warning)
    // and a file others can read (a warning), each at its field, in words that name the setting and the file only. Save is
    // never blocked by them (the file may exist only where Campertron runs); a path that is not fully qualified still blocks,
    // as before. Reload and the window's activation look at the file again. The files are never opened: inotify watches
    // their folder for the whole session. Only what existed before D75 is used here, so this class also runs against an
    // older GUI: on 3c531ea it fails (the negative control of §BT).
    public class SecretFileGuiTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string PasswordSet = "Email mode reads the SMTP password from the file named by smtpPasswordFile when it starts; smtpPassword is not used.";
        private const string TokenSet = "The unattended monitoring reads ntfy's access token from the file named by tokenFile when it starts.";
        private const string Topic = "secretFileTopic0123456789ab";
        private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        private string Folder = string.Empty;
        private string Secrets = string.Empty;
        private string PasswordFile => Path.Join(Secrets, "smtp_password");
        private string TokenFile => Path.Join(Secrets, "ntfy_token");

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "secret-file-" + Guid.NewGuid().ToString("N"));
            Secrets = Path.Join(Folder, "secrets");
            Directory.CreateDirectory(Secrets);
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: ''\nsmtpPasswordFile: '" + PasswordFile +
                "'\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
            File.WriteAllText(Path.Join(Folder, "Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenFile + "'\n");
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

        private static bool IsRoot => Environment.UserName == "root";

        // a secret file in one of the states the GUI tells
        private void State(string FilePath, string Name)
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
            }
            File.WriteAllText(FilePath, "not-a-real-secret\n");
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

        // what the field says about the file in each state (after the setting's own Info)
        private static string? Said(string Key, string File, string Kind, string Name, bool InUse)
        {
            string Mode = Kind == "password" ? "email mode" : "the monitoring with ntfy on";
            string Consequence = "so " + Mode + (InUse ? " refuses" : " would refuse") + " to start on this computer.";
            return Name switch
            {
                "missing" => Key + ": " + File + " doesn't exist on this computer, " + Consequence + " If Campertron runs somewhere else, such as in a container, the file only has to exist there.",
                "folder" => Key + ": " + File + " is a folder, not a file, " + Consequence,
                "unreadable" => Key + ": you can't read " + File + " on this computer, " + Consequence + " Check the file's owner and permissions.",
                "others" => Key + ": other users on this computer can read " + File + ". Make it readable only by you, for example with chmod 600 " + File + ".",
                _ => null
            };
        }

        private static string Expected(string Base, string Key, string File, string Kind, string Name, bool InUse)
        {
            string? Text = Said(Key, File, Kind, Name, InUse);
            if (Text == null)
            {
                return "Info: " + Base;
            }
            string Severity = Name == "others" || !InUse ? "Warning" : "Error";
            return Severity + ": " + Base + " " + Text;
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

        private static Control Editor(MainWindow Window, string Name) =>
            Named<SettingsView>(Window, "SettingsContent").GetVisualDescendants().OfType<Control>().First(c => c.Name == Name);

        private static Button SaveButton(MainWindow Window, string EditorName) =>
            Editor(Window, EditorName).GetVisualDescendants().OfType<Button>().First(b => b.Name == "SaveButton");

        // ---- smtpPasswordFile ----

        [AvaloniaTest]
        public void ThePasswordFile_IsJudgedFromItsMetadata_AnErrorOnlyInUse_AndNeverBlocksSave(
            [Values("Console", "Email")] string Output, [Values("missing", "folder", "unreadable", "others", "owner")] string Name)
        {
            if (Name == "unreadable" && IsRoot)
            {
                Assert.Ignore("root may read any file");
            }
            General(Output);
            State(PasswordFile, Name);
            var (Window, Settings) = Show();
            Select(Window, Settings, "Email");
            EmailSettingsViewModel E = Settings.Email;
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo(Expected(PasswordSet, "smtpPasswordFile", "smtp_password", "password", Name, Output == "Email")));
            Assert.That(E.SmtpPasswordFile.MessageText, Does.Not.Contain(Secrets).And.Not.Contain(Folder), "the file's name only, never its folder");
            // another change saves: a file problem never blocks it
            E.SmtpServer.Text = "smtp2.example.invalid";
            Settle(Window);
            Assert.That(SaveButton(Window, "EmailEditor").IsEffectivelyEnabled, Is.True);
            Window.Close();
        }

        // ---- tokenFile ----

        [AvaloniaTest]
        public void TheTokenFile_IsJudgedFromItsMetadata_AnErrorOnlyInUse_AndNeverBlocksSave(
            [Values("Console", "Ntfy", "Email")] string Output, [Values("missing", "folder", "unreadable", "others", "owner")] string Name)
        {
            if (Name == "unreadable" && IsRoot)
            {
                Assert.Ignore("root may read any file");
            }
            General(Output);
            State(PasswordFile, "owner");
            State(TokenFile, Name);
            var (Window, Settings) = Show();
            Select(Window, Settings, "Ntfy");
            NtfySettingsViewModel N = Settings.Ntfy;
            Assert.That(N.TokenFile.MessageText, Is.EqualTo(Expected(TokenSet, "tokenFile", "ntfy_token", "token", Name, Output != "Console")));
            Assert.That(N.TokenFile.MessageText, Does.Not.Contain(Secrets));
            N.TimeoutSeconds.Text = "20";
            Settle(Window);
            Assert.That(SaveButton(Window, "NtfyEditor").IsEffectivelyEnabled, Is.True, "never blocked by the file");
            // with ntfy off the token isn't used: a warning at most
            N.Enabled.IsChecked = false;
            Settle(Window);
            if (Name != "owner" && Name != "others" && Output != "Console")
            {
                Assert.That(N.TokenFile.MessageText, Does.StartWith("Error: "), "Ntfy.yaml as saved still has ntfy on");
            }
            Window.Close();
        }

        // a path that is not fully qualified is still the field's own error and blocks Save, as before; the file never does
        [AvaloniaTest]
        public void APathError_StillBlocksSave_AMissingFileNever()
        {
            General("Email");
            var (Window, Settings) = Show();
            Select(Window, Settings, "Email");
            EmailSettingsViewModel E = Settings.Email;
            Assert.That(E.SmtpPasswordFile.MessageText, Does.StartWith("Error: ").And.Contain("smtp_password doesn't exist on this computer"));
            Assert.That(SaveButton(Window, "EmailEditor").IsEffectivelyEnabled, Is.False, "nothing changed yet");
            E.SmtpPasswordFile.Text = "secrets/smtp_password";
            Settle(Window);
            Assert.That(E.SmtpPasswordFile.MessageText, Does.StartWith("Error: smtpPasswordFile is not an absolute path"));
            Assert.That(SaveButton(Window, "EmailEditor").IsEffectivelyEnabled, Is.False, "a path error blocks");
            E.SmtpPasswordFile.Text = Path.Join(Secrets, "elsewhere");
            Settle(Window);
            Assert.That(E.SmtpPasswordFile.MessageText, Does.Contain("elsewhere doesn't exist on this computer"));
            Assert.That(SaveButton(Window, "EmailEditor").IsEffectivelyEnabled, Is.True, "a missing file doesn't block");
            SaveButton(Window, "EmailEditor").Command!.Execute(null);
            Settle(Window);
            Assert.That(File.ReadAllText(Path.Join(Folder, "Email.yaml")), Does.Contain(Path.Join(Secrets, "elsewhere")));
            Assert.That(File.Exists(Path.Join(Secrets, "elsewhere")), Is.False, "the GUI never creates the file");
            Window.Close();
        }

        // Reload and the window's activation (Settings.CheckForExternalChanges) look at the files again
        [AvaloniaTest]
        public void ReloadAndActivation_LookAtTheFilesAgain()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            General("Email");
            var (Window, Settings) = Show();
            Select(Window, Settings, "Email");
            EmailSettingsViewModel E = Settings.Email;
            Assert.That(E.SmtpPasswordFile.MessageText, Does.Contain("doesn't exist on this computer"));
            State(PasswordFile, "owner");
            Settings.CheckForExternalChanges();
            Settle(Window);
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo("Info: " + PasswordSet), "activation: the file is there now");
            File.SetUnixFileMode(PasswordFile, OwnerOnly | UnixFileMode.OtherRead);
            E.ReloadCommand.Execute(null);
            Settle(Window);
            Assert.That(E.SmtpPasswordFile.MessageText, Does.Contain("other users on this computer can read smtp_password"), "reload");
            Select(Window, Settings, "Ntfy");
            Assert.That(Settings.Ntfy.TokenFile.MessageText, Does.Contain("ntfy_token doesn't exist on this computer"));
            State(TokenFile, "owner");
            Settings.Ntfy.ReloadCommand.Execute(null);
            Settle(Window);
            Assert.That(Settings.Ntfy.TokenFile.MessageText, Is.EqualTo("Info: " + TokenSet));
            Window.Close();
        }

        // the secret files are never opened: inotify watches their folder while the editors open, outputTo changes, paths
        // are typed, files reload and the window is activated; the test's own read afterwards is seen
        [AvaloniaTest]
        public void TheFiles_AreNeverOpened_AsInotifySees()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("inotify is Linux's");
                return;
            }
            State(PasswordFile, "others");
            State(TokenFile, IsRoot ? "owner" : "unreadable");
            using var Watch = new Inotify(Secrets);
            General("Email");
            var (Window, Settings) = Show();
            foreach (string File_ in new[] { "General", "Email", "Ntfy", "Email" })
            {
                Select(Window, Settings, File_);
            }
            Settings.Email.SmtpPasswordFile.Text = PasswordFile + "x";
            Settle(Window);
            Settings.Email.SmtpPasswordFile.Text = PasswordFile;
            Settle(Window);
            Settings.Email.RevertCommand.Execute(null);
            Settings.CheckForExternalChanges();
            Settings.Ntfy.ReloadCommand.Execute(null);
            Settle(Window);
            Assert.That(Settings.Email.SmtpPasswordFile.MessageText, Does.Contain("can read smtp_password"), "the files were looked at");
            Assert.That(Watch.Events(), Is.Empty, "no open, read or close of a secret file");
            File.ReadAllBytes(PasswordFile);
            Assert.That(Watch.Events(), Does.Contain("smtp_password: open"), "the watch sees an open: the positive control");
            Window.Close();
        }

        // the inotify watch of one folder (Linux): its files' opens, reads and closes, as "name: open" lines
        internal sealed class Inotify : IDisposable
        {
            private const int NonBlock = 0x800;
            private const uint Access = 0x1, CloseNoWrite = 0x10, Open = 0x20;
            private readonly int _Handle;

            internal Inotify(string Folder)
            {
                _Handle = inotify_init1(NonBlock);
                Assert.That(_Handle, Is.GreaterThanOrEqualTo(0), "inotify_init1");
                Assert.That(inotify_add_watch(_Handle, Folder, Access | Open | CloseNoWrite), Is.GreaterThanOrEqualTo(0), "inotify_add_watch");
            }

            internal List<string> Events()
            {
                var Out = new List<string>();
                byte[] Buffer = new byte[64 * 1024];
                while (true)
                {
                    nint Count = read(_Handle, Buffer, Buffer.Length);
                    if (Count <= 0)
                    {
                        return Out;
                    }
                    int At = 0;
                    while (At + 16 <= Count)
                    {
                        uint Mask = BitConverter.ToUInt32(Buffer, At + 4);
                        int Length = BitConverter.ToInt32(Buffer, At + 12);
                        string Name = System.Text.Encoding.UTF8.GetString(Buffer, At + 16, Length).TrimEnd('\0');
                        if ((Mask & Open) != 0) { Out.Add(Name + ": open"); }
                        if ((Mask & Access) != 0) { Out.Add(Name + ": read"); }
                        if ((Mask & CloseNoWrite) != 0) { Out.Add(Name + ": close"); }
                        At += 16 + Length;
                    }
                }
            }

            public void Dispose() => close(_Handle);

            [DllImport("libc", SetLastError = true)] private static extern int inotify_init1(int Flags);
            [DllImport("libc", SetLastError = true)] private static extern int inotify_add_watch(int Handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string Path_, uint Mask);
            [DllImport("libc", SetLastError = true)] private static extern nint read(int Handle, byte[] Buffer, nint Count);
            [DllImport("libc")] private static extern int close(int Handle);
        }
    }
}
