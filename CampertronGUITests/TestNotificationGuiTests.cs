using System.Net;
using System.Net.Mail;
using System.Reflection;
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
    // The test email and the test notification (D86, D88; v0.3.0's PR 5), the first time the desktop GUI sends anything, and
    // the random ntfy topic (D86), in the real window on the headless platform. The button is offered only for a saved file
    // without changes; the send runs on a worker thread while the window goes on (the window's thread never waits), and its
    // result is a message in its severity. The secret source is read only on the click: the variables' seams count every
    // read. The email's transport is CampsiteConfig's send seam and ntfy's the publisher's handler seam: nothing reaches a
    // server. Nothing is written into the settings folder, Camp.History or a lock.
    public class TestNotificationGuiTests
    {
        private const int SettingsTab = MainWindow.SettingsTabIndex;
        private const string GeneralText = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";

        private string Folder = string.Empty;
        private string Password = string.Empty;
        private string Topic = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private int EnvironmentReads;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(GuiTestEnvironment.Root, "test-notification-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Password = "GuiTestCnry" + Guid.NewGuid().ToString("N");
            Topic = "guiTestTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            File.WriteAllText(Path.Join(Folder, "General.yaml"), GeneralText);
            File.WriteAllText(Path.Join(Folder, "Email.yaml"), "smtpServer: mail.example.invalid\nsmtpPort: 587\nsmtpUsername: camper\nsmtpPassword: " + Password +
                "\nsendToAddressList:\n- first@example.invalid\nsendFromAddress: camper@example.invalid\n");
            File.WriteAllText(Path.Join(Folder, "Ntfy.yaml"), "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            EnvironmentReads = 0;
            SmtpPassword.GetEnvironment = Name => { Interlocked.Increment(ref EnvironmentReads); return null; };
            NtfyToken.GetEnvironment = Name => { Interlocked.Increment(ref EnvironmentReads); return null; };
        }

        [TearDown]
        public void DeleteFolder()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, true);
            }
        }

        private (MainWindow Window, SettingsViewModel Settings, Control View) Show()
        {
            File.Copy(GuiTestEnvironment.DbFile, Path.Join(Folder, "RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folder });
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Tabs(Window).SelectedIndex = SettingsTab;
            Dispatcher.UIThread.RunJobs();
            return (Window, ViewModel.Settings!, Named<SettingsView>(Window, "SettingsContent"));
        }

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        // the settings folder's files but the campground data (SQLite may touch its own files while the window reads them)
        private static Dictionary<string, string> Settled(string Folder) =>
            Fingerprint(Folder).Where(F => !F.Key.StartsWith("RecreationDotOrg.db", StringComparison.Ordinal)).ToDictionary(F => F.Key, F => F.Value);

        // runs the UI thread's jobs until Done or the time is up
        private static bool Pump(Func<bool> Done)
        {
            DateTime Until = DateTime.UtcNow.AddSeconds(30);
            while (!Done() && DateTime.UtcNow < Until)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(10);
            }
            Dispatcher.UIThread.RunJobs();
            return Done();
        }

        [AvaloniaTest]
        public void TheTestEmail_IsSentOnlyOnTheClick_OnAWorkerThread_WhileTheWindowGoesOn()
        {
            using var Release = new ManualResetEventSlim(false);
            var Sent = new List<(int Thread, string? Password)>();
            CampsiteConfig.SendResultsEmail = (Settings_, Message) =>
            {
                lock (Sent)
                {
                    Sent.Add((Environment.CurrentManagedThreadId, CampsiteConfig.SmtpCredential(Settings_).Password));
                }
                Release.Wait(TimeSpan.FromSeconds(30));
            };
            var (Window, Settings, View) = Show();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control Editor = In<Control>(View, "EmailEditor");
            Button Test = In<Button>(Editor, "EmailTestButton");
            Assert.That(Test.Content, Is.EqualTo("Send a test email"));
            Assert.That(In<Border>(Editor, "EmailTestCard").IsEffectivelyVisible, Is.True);
            Assert.That(Test.IsEffectivelyEnabled, Is.True);
            Assert.That(EnvironmentReads, Is.Zero, "showing the window reads no secret source");

            //a change: the test uses the saved file, so it is not offered, and the window says why
            In<TextBox>(Editor, "SmtpServerBox").Text = "mail2.example.invalid";
            Dispatcher.UIThread.RunJobs();
            Assert.That(Test.IsEffectivelyEnabled, Is.False);
            Assert.That(In<TextBlock>(Editor, "EmailTestUnavailable").Text, Is.EqualTo("The test uses Email.yaml as it is saved: save or revert your changes first."));
            Assert.That(In<TextBlock>(Editor, "EmailTestUnavailable").IsVisible, Is.True);
            Settings.Email.Revert();
            Dispatcher.UIThread.RunJobs();
            Assert.That(EnvironmentReads, Is.Zero, "editing and reverting read no secret source");

            Dictionary<string, string> Before = Settled(Folder);
            int UiThread = Environment.CurrentManagedThreadId;
            Test.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<TextBlock>(Editor, "EmailTestBusy").IsVisible, Is.True, "Sending a test email… is shown at once");
            Assert.That(In<TextBlock>(Editor, "EmailTestBusy").Text, Is.EqualTo("Sending a test email…"));
            Assert.That(Test.IsEffectivelyEnabled, Is.False, "one test at a time");
            //the window goes on while the send waits: another tab, another file
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Release.Set();
            Assert.That(Pump(() => Settings.Email.Test.HasResult), Is.True);
            TextBlock Result = In<TextBlock>(Editor, "EmailTestResult");
            Assert.That(Result.Text, Does.StartWith("Info: the SMTP server accepted the test email for 1 recipient at "));
            Assert.That(Result.FindAncestorOfType<SeverityBlock>()!.Severity, Is.EqualTo("Info"));
            Assert.That(In<TextBlock>(Editor, "EmailTestBusy").IsVisible, Is.False);
            Assert.That(Test.IsEffectivelyEnabled, Is.True);
            Assert.That(Sent, Has.Count.EqualTo(1));
            Assert.That(Sent[0].Thread, Is.Not.EqualTo(UiThread), "the send ran on a worker thread");
            Assert.That(Sent[0].Password, Is.EqualTo(Password), "the source email mode would use (here the plain-text password)");
            Assert.That(EnvironmentReads, Is.GreaterThan(0), "the precedence looked at the variable, at the click");
            Assert.That(Settled(Folder), Is.EqualTo(Before), "nothing written: no file, no Camp.History, no lock");
            Assert.That(SettingsEditorTests.Exposed(Window, In<SecretTextBox>(Editor, "SmtpPasswordBox")).Where(T => T.Contains(Password)), Is.Empty, "the password appears nowhere else");
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaTest]
        public void AFailedTest_IsAnErrorMessage_AndTheWindowStaysUsable()
        {
            CampsiteConfig.SendResultsEmail = (Settings_, Message) => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "550 CANARY from the server " + Password);
            var (Window, Settings, View) = Show();
            Settings.IsEmailSelected = true;
            Dispatcher.UIThread.RunJobs();
            Control Editor = In<Control>(View, "EmailEditor");
            In<Button>(Editor, "EmailTestButton").Command!.Execute(null);
            Assert.That(Pump(() => Settings.Email.Test.HasResult), Is.True);
            TextBlock Result = In<TextBlock>(Editor, "EmailTestResult");
            Assert.That(Result.Text, Is.EqualTo("Error: the test email could not be sent: the SMTP server refused an address of the email (SMTP 550). Check sendFromAddress and sendToAddressList in Email.yaml."));
            Assert.That(Result.FindAncestorOfType<SeverityBlock>()!.Severity, Is.EqualTo("Error"));
            Assert.That(Result.Text, Does.Not.Contain("CANARY").And.Not.Contain(Password));
            Assert.That(In<Button>(Editor, "EmailTestButton").IsEffectivelyEnabled, Is.True, "no monitoring stops: a test is only a message");
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        private sealed class OkHandler : HttpMessageHandler
        {
            internal int Publishes;
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Interlocked.Increment(ref Publishes);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"gui1\"}") };
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        [AvaloniaTest]
        public void TheNtfyEditor_GeneratesARandomTopicAsAnUnsavedChange_AndSendsATestNotification()
        {
            var Handler = new OkHandler();
            NtfyPublisher.CreateHandler = () => Handler;
            var (Window, Settings, View) = Show();
            Settings.IsNtfySelected = true;
            Dispatcher.UIThread.RunJobs();
            Control Editor = In<Control>(View, "NtfyEditor");
            Button Generate = In<Button>(Editor, "GenerateTopicButton");
            Assert.That(Generate.Content, Is.EqualTo("Generate a random topic"));
            Assert.That(In<TextBlock>(Editor, "GenerateTopicHelpText").Text, Is.EqualTo(Settings.Ntfy.GenerateTopicHelp));
            Assert.That(Avalonia.Automation.AutomationProperties.GetHelpText(Generate), Is.EqualTo(Settings.Ntfy.GenerateTopicHelp));

            Generate.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            string Generated = In<TextBox>(Editor, "NtfyTopicBox").Text!;
            Assert.That(Generated, Does.Match("^campertron-[a-z0-9]{26}$"));
            Assert.That(Settings.Ntfy.Topic.IsChanged, Is.True);
            Assert.That(In<Border>(Editor, "PreviewCard").IsVisible, Is.True);
            Assert.That(In<TextBlock>(Editor, "StatusText").Text, Is.EqualTo("A new random topic is in Topic. Save to keep it, or Revert to bring back the previous topic; after saving, subscribe to the new topic in the ntfy app."));
            Assert.That(In<Button>(Editor, "NtfyTestButton").IsEffectivelyEnabled, Is.False, "the test uses the saved file");
            Assert.That(File.ReadAllText(Path.Join(Folder, "Ntfy.yaml")), Does.Contain(Topic), "nothing written");
            In<Button>(Editor, "RevertButton").Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(In<TextBox>(Editor, "NtfyTopicBox").Text, Is.EqualTo(Topic));

            In<Button>(Editor, "NtfyTestButton").Command!.Execute(null);
            Assert.That(Pump(() => Settings.Ntfy.Test.HasResult), Is.True);
            Assert.That(In<TextBlock>(Editor, "NtfyTestResult").Text, Does.StartWith("Info: the ntfy server accepted the test notification at "));
            Assert.That(Handler.Publishes, Is.EqualTo(1), "with Enabled off: the test needs no Enabled");
            Window.Close();
            Dispatcher.UIThread.RunJobs();
        }

        // The settings forms reach a sender or a secret source only through TestNotificationForm, and it only in Send (the
        // click's path); the GUI itself never calls the library's test or its senders directly
        [AvaloniaTest]
        public void OnlyTheTestFormsSend_ReachesTheLibrarysTest_AndTheGuiReachesItOnlyThroughTheButton()
        {
            Type[] Forms =
            {
                typeof(SettingsViewModel), typeof(SettingsFileViewModel), typeof(SettingsFileViewModel<GeneralConfig>), typeof(SettingsFileViewModel<EmailConfig>),
                typeof(SettingsFileViewModel<NtfyConfig>), typeof(SettingsFileViewModel<WebConfig>), typeof(GeneralSettingsViewModel), typeof(EmailSettingsViewModel),
                typeof(NtfySettingsViewModel), typeof(WebSettingsViewModel), typeof(TestNotificationForm),
            };
            string[] Senders = { "CampertronLibrary.function.Base.TestNotification::", "CampertronLibrary.function.Base.SmtpPassword::", "CampertronLibrary.function.Base.NtfyToken::",
                "CampertronLibrary.function.Base.SecretFile::", "CampertronLibrary.function.Base.NtfyPublisher::", "CampertronLibrary.function.Base.CampsiteConfig::", "System.Net.Mail" };
            var Reaching = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in Forms)
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    if (ReferencedMembers(Method).Any(M => Senders.Any(S => M.StartsWith(S, StringComparison.Ordinal))))
                    {
                        Reaching.Add(T.Name + "." + Method.Name);
                    }
                }
            }
            Assert.That(Reaching, Is.EqualTo(new[] { "TestNotificationForm.Send" }));
            Assert.That(ReferencedMembers(typeof(TestNotificationForm).GetMethod("Send", Declared)!).Where(M => Senders.Any(S => M.StartsWith(S, StringComparison.Ordinal))),
                Is.EquivalentTo(new[] { "CampertronLibrary.function.Base.TestNotification::SendEmail", "CampertronLibrary.function.Base.TestNotification::SendNtfy" }));

            //the desktop GUI's own code never calls a test or a sender: only the button's command does, through the form
            List<string> Gui = ReferencedMembers(typeof(MainWindow).Assembly.GetTypes()).Select(R => R.Member).ToList();
            Assert.That(Gui.Where(M => M.StartsWith("CampertronLibrary.function.Base.TestNotification::", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.Forms.TestNotificationForm::Send", StringComparison.Ordinal)
                || M.StartsWith("CampertronLibrary.function.Forms.TestNotificationForm::Start", StringComparison.Ordinal)), Is.Empty);
        }
    }
}
