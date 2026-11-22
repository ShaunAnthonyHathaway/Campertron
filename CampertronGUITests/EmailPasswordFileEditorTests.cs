using Avalonia;
using Avalonia.Automation;
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
    // The SMTP password file in the Email.yaml editor (decision D70, business-rules §17 and §27), in the real window on the
    // headless platform. Each test has its own settings folder with a password file and an ntfy token file whose contents
    // are unique canaries: only the file's path is edited, the files are never opened or changed, CAMPERTRON_EMAIL_PASSWORD
    // is never asked for, and neither a password nor the path appears outside the field and its preview.
    public class EmailPasswordFileEditorTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string Sources =
            "Email mode takes the SMTP password from the first of these that is set up, and never falls back to the next one when it can't be used:\n" +
            "1. The SMTP password file (smtpPasswordFile), recommended: the password stays out of Email.yaml.\n" +
            "2. The environment variable CAMPERTRON_EMAIL_PASSWORD, set where email mode runs, never in this window. Whoever can inspect that process, its service manager or its container (for example with docker inspect) may see it.\n" +
            "3. The plain-text password (smtpPassword), stored in Email.yaml.\n" +
            "This window never opens the password file, never reads CAMPERTRON_EMAIL_PASSWORD and never copies a password into Email.yaml.";
        private const string NotAbsolute = "Error: smtpPasswordFile is not an absolute path: Campertron reads the password file only by its full path, never relative to another folder.";
        private const string NotUsed = "Warning: smtpPassword is not used, because smtpPasswordFile is set: clear smtpPassword, so the password is not also kept in Email.yaml as plain text.";
        private const string FileSet = "Info: Email mode reads the SMTP password from the file named by smtpPasswordFile when it starts; smtpPassword is not used.";
        private static readonly DateTime Stamp = new(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        private string Folder = string.Empty;
        private string PlainCanary = string.Empty;
        private string FileCanary = string.Empty;
        private string TokenCanary = string.Empty;
        private string Secrets = string.Empty;
        private string PasswordFile = string.Empty;
        private string TokenFile = string.Empty;
        private string General => Path.Join(Folder, "General.yaml");
        private string Email => Path.Join(Folder, "Email.yaml");
        private string EmailText => "# Campertron email settings\nsmtpServer: mail.example.org   # my provider\nsmtpPort: 587\nsmtpUsername: camper\n" +
            "smtpPassword: " + PlainCanary + " # the old way\nsendToAddressList:\n- first@example.org\nsendFromAddress: camper@example.org\n# end\n";

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "email-password-file-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            PlainCanary = "plainCnry" + Guid.NewGuid().ToString("N");
            FileCanary = "fileCnry" + Guid.NewGuid().ToString("N");
            TokenCanary = "tokCnry" + Guid.NewGuid().ToString("N");
            Secrets = Path.Join(Folder, "secrets");
            Directory.CreateDirectory(Secrets);
            PasswordFile = Path.Join(Secrets, "smtp_password");
            TokenFile = Path.Join(Secrets, "ntfy_token");
            File.WriteAllText(PasswordFile, FileCanary + "\n");
            File.WriteAllText(TokenFile, TokenCanary + "\n");
            File.SetLastWriteTimeUtc(PasswordFile, Stamp);
            File.SetLastWriteTimeUtc(TokenFile, Stamp);
            File.WriteAllText(General, "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n");
            File.WriteAllText(Email, EmailText);
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

        // the secret files: their bytes, times and the folder's entries never change
        private void AssertSecretsUntouched()
        {
            Assert.That(File.ReadAllText(PasswordFile), Is.EqualTo(FileCanary + "\n"));
            Assert.That(File.ReadAllText(TokenFile), Is.EqualTo(TokenCanary + "\n"));
            Assert.That(File.GetLastWriteTimeUtc(PasswordFile), Is.EqualTo(Stamp));
            Assert.That(File.GetLastWriteTimeUtc(TokenFile), Is.EqualTo(Stamp));
            Assert.That(Directory.GetFileSystemEntries(Secrets).OrderBy(F => F, StringComparer.Ordinal), Is.EqualTo(new[] { TokenFile, PasswordFile }.OrderBy(F => F, StringComparer.Ordinal)));
        }

        // the real window with this test's settings folder, on Settings > Campertron with Email.yaml chosen
        private (MainWindow Window, MainWindowViewModel ViewModel, SettingsViewModel Settings, Control Editor) ShowEmail()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            ViewModel.Settings!.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            return (Window, ViewModel, ViewModel.Settings, In<Control>(Named<SettingsView>(Window, "SettingsContent"), "EmailEditor"));
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

        // what the window exposes outside the password-file box and the preview of the lines a save writes
        private static List<string> ExposedOutsideTheField(Window Window, Control Editor) =>
            SettingsEditorTests.Exposed(Window, In<TextBox>(Editor, "SmtpPasswordFileBox"), In<SelectableTextBlock>(Editor, "PreviewText"));

        // an Email.yaml without smtpPasswordFile: the field is empty, recommended and explained; a valid absolute path is
        // saved as one new line after the last setting, everything else byte for byte (comments, the plain-text password,
        // its comment), with an exact backup; the plain-text password is now reported as not used, never cleared
        [AvaloniaTest]
        public void AValidPasswordFile_IsSaved_AndThePlainTextPasswordIsKeptAndReportedAsNotUsed()
        {
            var (Window, _, Settings, Editor) = ShowEmail();
            EmailSettingsViewModel E = Settings.Email;
            TextBox Box = In<TextBox>(Editor, "SmtpPasswordFileBox");
            Assert.That(Box.Text ?? string.Empty, Is.Empty);
            Assert.That(Box.PlaceholderText, Is.EqualTo("the password file's full path"));
            Assert.That(In<Button>(Editor, "RemovePasswordFileButton").IsVisible, Is.False);
            Assert.That(In<TextBlock>(Editor, "PasswordSourcesText").Text, Is.EqualTo(Sources));
            Assert.That(E.SmtpPasswordFile.Label, Is.EqualTo("SMTP password fi_le (recommended)"));
            Assert.That(E.SmtpPassword.Label, Is.EqualTo("Pass_word (plain text)"));
            Assert.That(E.SmtpPassword.HasMessage, Is.False);

            Type(Box, PasswordFile);
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo(FileSet));
            Assert.That(E.SmtpPassword.MessageText, Is.EqualTo(NotUsed), "both sources: the plain-text password stays on disk and is not used");
            Assert.That(In<SelectableTextBlock>(Editor, "PreviewText").Text, Does.Contain("+ smtpPasswordFile: " + PasswordFile).And.Not.Contain(FileCanary));
            Click(In<Button>(Editor, "SaveButton"));

            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace("# end\n", "smtpPasswordFile: " + PasswordFile + "\n# end\n")));
            Assert.That(File.ReadAllText(Email + ".bak"), Is.EqualTo(EmailText));
            Assert.That(E.SmtpPassword.Text, Is.EqualTo(PlainCanary), "the plain-text password is never cleared or migrated");
            Assert.That(E.SmtpPassword.MessageText, Is.EqualTo(NotUsed));
            Assert.That(In<Button>(Editor, "RemovePasswordFileButton").IsVisible, Is.True);
            AssertSecretsUntouched();
            Window.Close();
        }

        // an existing smtpPasswordFile loads into its field; the path as the file has it is no edit; a relative path (also
        // with the Email output off, where the validation only warns) or blank text is the field's own error and blocks the
        // save, a valid path is accepted again, comments, CRLF and a byte-order mark stay
        [AvaloniaTest]
        public void AnExistingPasswordFile_Loads_AndARelativePathAlwaysPreventsSave([Values("Console", "Email")] string Output)
        {
            string Text = ("﻿" + EmailText.Replace("smtpPassword: " + PlainCanary + " # the old way", "smtpPassword: '' # cleared") + "smtpPasswordFile: '" + PasswordFile + "' # a Docker secret\n").Replace("\n", "\r\n");
            File.WriteAllText(Email, Text.Substring(1), new System.Text.UTF8Encoding(true));
            File.WriteAllText(General, File.ReadAllText(General).Replace("outputTo: Console", "outputTo: " + Output));
            var (Window, _, Settings, Editor) = ShowEmail();
            EmailSettingsViewModel E = Settings.Email;
            TextBox Box = In<TextBox>(Editor, "SmtpPasswordFileBox");
            Button Save = In<Button>(Editor, "SaveButton");
            Assert.That(Box.Text, Is.EqualTo(PasswordFile));
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo(FileSet));
            Assert.That(E.SmtpPassword.HasMessage, Is.False, "no plain-text password: nothing to warn about");
            Assert.That(E.IsDirty || Save.IsEffectivelyEnabled, Is.False);

            foreach (string Bad in new[] { "secrets/smtp_password", "./smtp_password", "smtp_password", "~/smtp_password", "   " })
            {
                Type(Box, Bad);
                Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo(Bad.Trim().Length == 0
                    ? "Error: smtpPasswordFile is empty: write the absolute path of the file that holds the SMTP password, or remove smtpPasswordFile."
                    : NotAbsolute), Bad);
                Assert.That(E.SmtpPasswordFile.IsError && !Save.IsEffectivelyEnabled, Is.True, Bad);
            }
            string Other = Path.Join(Secrets, "other_password");
            Type(Box, Other);
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo(FileSet));
            Click(Save);
            Assert.That(File.ReadAllBytes(Email), Is.EqualTo(new System.Text.UTF8Encoding(true).GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(Text.Substring(1).Replace(PasswordFile, Other))).ToArray()));
            Assert.That(File.Exists(Other), Is.False, "the password file is never created");
            AssertSecretsUntouched();
            Window.Close();
        }

        // clearing the field removes smtpPasswordFile's line, its comment with it (announced), and never touches the
        // plain-text password; Remove smtpPasswordFile removes a key written without a value
        [AvaloniaTest]
        public void ClearingThePasswordFile_RemovesItsLine_AndRemoveRemovesAnEmptyKey()
        {
            string Text = EmailText + "smtpPasswordFile: '" + PasswordFile + "' # a Docker secret\n";
            File.WriteAllText(Email, Text);
            var (Window, _, Settings, Editor) = ShowEmail();
            EmailSettingsViewModel E = Settings.Email;
            Assert.That(E.SmtpPassword.MessageText, Is.EqualTo(NotUsed), "both sources present");
            Type(In<TextBox>(Editor, "SmtpPasswordFileBox"), "");
            Assert.That(E.SmtpPasswordFile.MessageText, Is.EqualTo("Warning: Removing smtpPasswordFile also removes 1 comment that belongs to it."));
            Assert.That(E.SmtpPassword.HasMessage, Is.False, "without the file the plain-text password is used again");
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText));
            Assert.That(In<Button>(Editor, "RemovePasswordFileButton").IsVisible, Is.False);

            File.WriteAllText(Email, EmailText + "smtpPasswordFile:\n");
            Click(In<Button>(Editor, "ReloadButton"));
            Assert.That(In<TextBox>(Editor, "SmtpPasswordFileBox").Text ?? string.Empty, Is.Empty);
            Assert.That(E.SmtpPasswordFile.MessageText, Does.StartWith("Warning: smtpPasswordFile is empty"));
            Click(In<Button>(Editor, "RemovePasswordFileButton"));
            Assert.That(E.SmtpPasswordFile.HasMessage, Is.False);
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText));
            AssertSecretsUntouched();
            Window.Close();
        }

        // Revert drops an unsaved path, Reload shows a path another program wrote; an unrelated edit keeps smtpPasswordFile
        // and the plain-text password byte for byte; a write that fails leaves Email.yaml as it was and keeps the edit
        [AvaloniaTest]
        public void RevertReloadAndAnUnrelatedSave_KeepThePasswordSources_AndAFailedWriteChangesNothing()
        {
            string Text = EmailText + "smtpPasswordFile: " + PasswordFile + "\n";
            File.WriteAllText(Email, Text);
            var (Window, _, Settings, Editor) = ShowEmail();
            TextBox Box = In<TextBox>(Editor, "SmtpPasswordFileBox");
            Type(Box, Path.Join(Secrets, "another"));
            Click(In<Button>(Editor, "RevertButton"));
            Assert.That(Box.Text, Is.EqualTo(PasswordFile));
            Assert.That(Settings.Email.IsDirty, Is.False);

            string Elsewhere = Path.Join(Folder, "elsewhere", "smtp_password");
            File.WriteAllText(Email, Text.Replace(PasswordFile, Elsewhere));
            Click(In<Button>(Editor, "ReloadButton"));
            Assert.That(Box.Text, Is.EqualTo(Elsewhere));

            Type(In<TextBox>(Editor, "SmtpServerBox"), "smtp.example.org");
            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(Text.Replace(PasswordFile, Elsewhere).Replace("smtpServer: mail.example.org", "smtpServer: smtp.example.org")),
                "an unrelated save keeps smtpPasswordFile and the plain-text password");

            byte[] Before = File.ReadAllBytes(Email);
            Action<string, string> OriginalStep = ConfigFileReplace.Step;
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("Email.yaml could not be written (injected).");
                }
            };
            try
            {
                Type(Box, PasswordFile);
                Click(In<Button>(Editor, "SaveButton"));
            }
            finally
            {
                ConfigFileReplace.Step = OriginalStep;
            }
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("Email.yaml could not be written (injected)."));
            Assert.That(File.ReadAllBytes(Email), Is.EqualTo(Before));
            Assert.That(Directory.GetFiles(Folder, "*.tmp"), Is.Empty);
            Assert.That(Settings.Email.SmtpPasswordFile.IsChanged, Is.True, "the edit is kept");
            AssertSecretsUntouched();
            Window.Close();
        }

        // the external passwords are never read: the password file and the token file keep their bytes and times,
        // CAMPERTRON_EMAIL_PASSWORD and CAMPERTRON_NTFY_TOKEN are never asked for, no password appears anywhere in the window,
        // a log or the preview; the path only in its own field and the preview, never in a message, Current configuration
        // or what Copy copies
        [AvaloniaTest]
        public void TheExternalPasswords_AreNeverRead_AndThePathStaysInItsField()
        {
            using var Log = new AvaloniaLogCapture();
            string EnvCanary = "envCnry" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Email, EmailText + "smtpPasswordFile: " + PasswordFile + "\n");
            File.WriteAllText(Path.Join(Folder, "Ntfy.yaml"), "enabled: false\nserver: https://ntfy.example.invalid\ntopic: someTopic_1\ntokenFile: " + TokenFile + "\n");
            Func<string, string?> OriginalSmtp = SmtpPassword.GetEnvironment, OriginalNtfy = NtfyToken.GetEnvironment;
            int Asked = 0;
            SmtpPassword.GetEnvironment = Name => { Asked++; return EnvCanary; };
            NtfyToken.GetEnvironment = Name => { Asked++; return EnvCanary; };
            try
            {
                var (Window, ViewModel, Settings, Editor) = ShowEmail();
                Type(In<TextBox>(Editor, "SmtpPasswordFileBox"), Path.Join(Secrets, "renamed_password"));
                string Preview = In<SelectableTextBlock>(Editor, "PreviewText").Text ?? string.Empty;
                Assert.That(Preview, Does.Contain(Path.Join(Secrets, "renamed_password")).And.Not.Contain(FileCanary).And.Not.Contain(EnvCanary));
                // back to the canary file's path, so Current configuration below could only miss the password by never reading it
                Type(In<TextBox>(Editor, "SmtpPasswordFileBox"), PasswordFile);
                Type(In<TextBox>(Editor, "SmtpUsernameBox"), "camper2");
                Preview = In<SelectableTextBlock>(Editor, "PreviewText").Text ?? string.Empty;
                Assert.That(Preview, Does.Not.Contain(FileCanary).And.Not.Contain(EnvCanary).And.Not.Contain(PlainCanary), "the plain-text password is masked, the others never read");

                List<string> All = SettingsEditorTests.Exposed(Window);
                Assert.That(All.Count, Is.GreaterThan(100));
                Assert.That(All.Where(T => T.Contains(FileCanary, StringComparison.Ordinal) || T.Contains(EnvCanary, StringComparison.Ordinal)
                    || T.Contains(TokenCanary, StringComparison.Ordinal) || T.Contains(PlainCanary, StringComparison.Ordinal)), Is.Empty, "no password anywhere");
                Assert.That(ExposedOutsideTheField(Window, Editor).Where(T => T.Contains(Secrets, StringComparison.Ordinal)), Is.Empty, "the path only in its field and the preview");
                Click(In<Button>(Editor, "SaveButton"));
                Assert.That(File.ReadAllText(Email), Does.Not.Contain(FileCanary).And.Not.Contain(EnvCanary).And.Not.Contain(TokenCanary), "no external password copied into Email.yaml");
                Assert.That(File.ReadAllText(Email), Does.Contain("smtpPasswordFile: " + PasswordFile), "the canary file is still the password's source");
                Assert.That(File.ReadAllText(Email), Does.Contain("smtpPassword: " + PlainCanary + " # the old way"), "the plain-text password kept");

                SettingsTabs(Window).SelectedIndex = MainWindow.SystemSettingsTabIndex;
                Dispatcher.UIThread.RunJobs();
                CurrentConfigurationViewModel Configuration = ViewModel.SystemSettings.Configuration!;
                string? Copied = null;
                Configuration.CopyText = Text => { Copied = Text; return Task.FromResult(true); };
                WaitFor(Configuration.Copy());
                Assert.That(Copied, Is.Not.Null);
                Assert.That(Copied + Configuration.Text, Does.Not.Contain(Secrets).And.Not.Contain(FileCanary).And.Not.Contain(PlainCanary).And.Not.Contain(EnvCanary));
                Window.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.That(Log.Entries.Where(L => L.Contains(FileCanary, StringComparison.Ordinal) || L.Contains(Secrets, StringComparison.Ordinal)), Is.Empty);
                Assert.That(Asked, Is.Zero, "the GUI never reads CAMPERTRON_EMAIL_PASSWORD or CAMPERTRON_NTFY_TOKEN");
                AssertSecretsUntouched();
            }
            finally
            {
                SmtpPassword.GetEnvironment = OriginalSmtp;
                NtfyToken.GetEnvironment = OriginalNtfy;
            }
        }

        // the two GUI repairs together: an unsaved password file and an unsaved new Ntfy.yaml both survive switching files
        // and tabs and keep an automatic restart from happening; each Save writes only its own file
        [AvaloniaTest]
        public void UnsavedEmailAndNtfyEdits_SurviveSwitching_AndEachSaveWritesOnlyItsFile()
        {
            var (Window, ViewModel, Settings, Editor) = ShowEmail();
            Type(In<TextBox>(Editor, "SmtpPasswordFileBox"), PasswordFile);
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Control Ntfy = In<Control>(Named<SettingsView>(Window, "SettingsContent"), "NtfyEditor");
            Type(In<TextBox>(Ntfy, "NtfyTopicBox"), "localTopic_" + Guid.NewGuid().ToString("N"));
            Tabs(Window).SelectedIndex = 0;
            Tabs(Window).SelectedIndex = SettingsTab;
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<TextBox>(Editor, "SmtpPasswordFileBox").Text, Is.EqualTo(PasswordFile));
            Assert.That(Settings.Email.IsDirty && Settings.Ntfy.IsDirty, Is.True);
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.True);
            string GeneralBefore = File.ReadAllText(General);

            Click(In<Button>(Editor, "SaveButton"));
            Assert.That(File.Exists(Path.Join(Folder, "Ntfy.yaml")), Is.False, "the Email save writes no Ntfy.yaml");
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.True, "the unsaved Ntfy.yaml still holds a restart");
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            string EmailAfter = File.ReadAllText(Email);
            Click(In<Button>(Ntfy, "SaveButton"));
            Assert.That(File.Exists(Path.Join(Folder, "Ntfy.yaml")), Is.True);
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailAfter), "the Ntfy.yaml save writes no Email.yaml");
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralBefore));
            Assert.That(ViewModel.RestartHooks.HasUnsavedChanges(), Is.False);
            AssertSecretsUntouched();
            Window.Close();
        }

        // the explanation and the field fit every supported width in Light and Dark with a long path and errors, the error
        // in the theme's error brush; the keyboard reaches the field between the user name and the password; nothing logged
        [AvaloniaTest]
        public void TheField_FitsEverySupportedSize_InLightAndDark([Values(720, 1024, 1600)] int Width, [Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            ThemeVariant Theme = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Application.Current!.RequestedThemeVariant = Theme;
            var (Window, _, Settings, Editor) = ShowEmail();
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1000);
            Type(In<TextBox>(Editor, "SmtpPasswordFileBox"), "relative/" + string.Concat(Enumerable.Repeat("very-long-folder-name/", 20)) + "smtp_password");
            Window.UpdateLayout();
            ScrollViewer Scroll = In<ScrollViewer>(Named<SettingsView>(Window, "SettingsContent"), "SettingsScrollViewer");
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            foreach (Control C in Editor.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && (c is TextBlock || c is TextBox || c is Button)))
            {
                Point? Right = C.TranslatePoint(new Point(C.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), C.Name ?? C.GetType().Name);
            }
            Application.Current.TryGetResource("SystemControlErrorTextForegroundBrush", Theme, out object? Error);
            TextBlock Message = Editor.GetVisualDescendants().OfType<TextBlock>().First(T => T.Text == NotAbsolute);
            Assert.That(((ISolidColorBrush)Message.Foreground!).Color, Is.EqualTo(((ISolidColorBrush)Error!).Color));
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Log.Entries, Is.Empty);
        }

        [AvaloniaTest]
        public void Keyboard_ReachesThePasswordFile_BetweenTheUserNameAndThePassword_AndItIsLabelled()
        {
            var (Window, _, Settings, Editor) = ShowEmail();
            In<TextBox>(Editor, "SmtpUsernameBox").Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            var Visited = new List<string?>();
            for (int i = 0; i < 3; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Visited.Add((Focused(Window) as Control)?.Name);
            }
            Assert.That(Visited, Is.EqualTo(new[] { "SmtpPasswordFileBox", "SmtpPasswordBox", "RevealPasswordButton" }), string.Join(", ", Visited));
            TextBox Box = In<TextBox>(Editor, "SmtpPasswordFileBox");
            Assert.That(AutomationProperties.GetName(Box), Is.EqualTo("SMTP password file (recommended)"));
            Assert.That(AutomationProperties.GetHelpText(Box), Does.Contain("never opens it"));
            Assert.That(Editor.GetVisualDescendants().OfType<Label>().Single(L => L.Target == Box).Content, Is.EqualTo("SMTP password fi_le (recommended)"));
            Window.Close();
        }
    }
}
