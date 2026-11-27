using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Notifications (v0.4.0's phase 7, D122; business-rules §43): Email.yaml and Ntfy.yaml in the
    // shared forms (SettingsViewModel's Email and Ntfy forms, ER-37), hosted at a terminal by InteractiveSettingsForm, with
    // the forms' test email, test notification and random topic. Driven from the main menu by a terminal that answers a
    // choice by its label, so a test reads as what a user picks, and judged by the files written, the words shown and what
    // the stand-ins received: the email's send seam and ntfy's handler seam, never a server. The shared form, driven
    // directly in another folder, is the oracle where the console must write what the desktop GUI and the web UI write.
    // Unique canaries (passwords, topics, a token, a secret file's content) are searched for in every line written; a
    // failure's message never shows one (boolean checks).
    [NonParallelizable]
    public class InteractiveNotificationsTests : ConfigEditTestBase
    {
        private string Secrets = string.Empty;
        private string OldPassword = string.Empty;
        private string NewPassword = string.Empty;
        private string OldTopic = string.Empty;
        private string Token = string.Empty;
        private string FileSecret = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal);
        private readonly List<(string Subject, string[] To, string? Password)> Sent = new();
        private Func<EmailConfig, MailMessage, Exception?> EmailAnswer = (S, M) => null;
        private Recording Handler = null!;

        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");

        [SetUp]
        public void Seams()
        {
            Secrets = Path.Join(Path.GetTempPath(), "CampertronInteractiveSecrets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Secrets);
            OldPassword = "PwOld-cnry-" + Guid.NewGuid().ToString("N");
            NewPassword = "PwNew-cnry-" + Guid.NewGuid().ToString("N");
            OldTopic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            Token = "tk_cnry" + Guid.NewGuid().ToString("N");
            FileSecret = "FileSecret-cnry-" + Guid.NewGuid().ToString("N");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            Variables.Clear();
            Sent.Clear();
            EmailAnswer = (S, M) => null;
            SmtpPassword.GetEnvironment = Name => Variables.TryGetValue(Name, out string? Value) ? Value : null;
            NtfyToken.GetEnvironment = Name => Variables.TryGetValue(Name, out string? Value) ? Value : null;
            CampsiteConfig.SendResultsEmail = (Settings, Message) =>
            {
                Sent.Add((Message.Subject, Message.To.Select(A => A.Address).ToArray(), CampsiteConfig.SmtpCredential(Settings).Password));
                if (EmailAnswer(Settings, Message) is Exception Failure)
                {
                    throw Failure;
                }
            };
            Handler = new Recording();
            NtfyPublisher.CreateHandler = () => Handler;
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            Handler.Dispose();
            if (Directory.Exists(Secrets))
            {
                Directory.Delete(Secrets, true);
            }
        }

        private sealed class Recording : HttpMessageHandler
        {
            internal Func<HttpRequestMessage, HttpResponseMessage> Answer = R => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc123\",\"event\":\"message\"}", Encoding.UTF8, "application/json") };
            internal readonly List<(Uri? Uri, string? Authorization, string Body)> Seen = new();
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Seen.Add((Request.RequestUri, Request.Headers.Authorization?.ToString(), Request.Content?.ReadAsStringAsync(Cancel).GetAwaiter().GetResult() ?? string.Empty));
                return Answer(Request);
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        // ---- the terminal: a choice is answered by its label (MenuTerminal, shared with InteractiveSettingsTests) ----

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static readonly TerminalInput No = TerminalInput.Line("n");

        // from the main menu into Notifications, and back out to Exit
        private static readonly object[] IntoEmail = { P("Notifications"), P("Email") };
        private static readonly object[] IntoNtfy = { P("Notifications"), P("ntfy") };
        private static readonly object[] OutToExit = { P("Back to Notifications"), P("Back to the main menu"), P("Exit") };

        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private InteractiveSession Session(InteractiveTerminal Terminal, string? Settings = null, bool Container = false, bool NoFolder = false) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Settings ?? Folder, CachePath = Path.Join(Secrets, "cache"), ContainerMode = Container });

        private int Run(MenuTerminal Terminal, string? Settings = null) => InteractiveConsole.Run(Session(Terminal, Settings));

        private Dictionary<string, string> Fingerprint(string? Of = null) =>
            Directory.GetFileSystemEntries(Of ?? Folder, "*", SearchOption.AllDirectories).ToDictionary(P => P,
                P => File.Exists(P) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P))) + File.GetLastWriteTimeUtc(P).Ticks : "dir");

        // no canary in anything written: the terminal's lines, standard output and standard error (boolean checks: a
        // failure's message must not show the secret)
        private void AssertNoSecret(MenuTerminal Terminal, params string[] Canaries)
        {
            string All = Terminal.AllText + "\n" + Out + "\n" + Err;
            foreach (string Canary in Canaries.Concat(new[] { OldPassword, NewPassword, Token, FileSecret }).Distinct())
            {
                Assert.That(All.Contains(Canary, StringComparison.Ordinal), Is.False, "a secret was written (its canary is not repeated here)");
            }
        }

        private static string EmailYaml(string? Password = null, string? Extra = null) =>
            "# my email settings\nsmtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: test-user\n" + (Password == null ? string.Empty : "smtpPassword: " + Password + "\n")
            + "sendFromAddress: from@example.invalid\nsendToAddressList:\n- one@example.invalid\n" + (Extra ?? string.Empty);

        private string NtfyYaml(bool Enabled = true, string? Topic = null, string? Extra = null) =>
            "# my ntfy settings\nenabled: " + (Enabled ? "true" : "false") + "\nserver: https://ntfy.example.invalid\ntopic: " + (Topic ?? OldTopic) + "\npriority: 3\n" + (Extra ?? string.Empty);

        // the shared form itself, as the desktop GUI and the web UI drive it, in a folder of its own with the same files
        private string Oracle<T>(Func<SettingsViewModel, T> FormOf, Action<T> Edit, params (string Name, string Text)[] Files) where T : SettingsFileViewModel
        {
            string Other = Path.Join(Secrets, "oracle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Other);
            foreach ((string Name, string Text) in Files)
            {
                Write(Path.Join(Other, Name), Text);
            }
            T Form = FormOf(new SettingsViewModel(Other, () => Now));
            Edit(Form);
            Form.Save();
            Assert.That(Form.StatusIsError, Is.False, "the oracle's save failed: " + Form.StatusText);
            return Other;
        }

        // ---- entering Notifications ----

        [Test]
        public void EnteringNotifications_ShowsTheSummary_AndChangesNothing()
        {
            Write(General, "outputTo: Console\n");
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P("Notifications"), P("Back to the main menu"), P("Exit"));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain("Notifications (nothing is saved or sent until you choose to)"));
            Assert.That(Terminal.Texts, Does.Contain("  Email.yaml: not found"));
            Assert.That(Terminal.Texts, Does.Contain("  Ntfy.yaml: not found"));
            Assert.That(Terminal.Texts, Does.Contain("  outputTo (General.yaml): Console"));
            Assert.That(Terminal.Texts, Does.Contain("General.yaml's outputTo decides whether the unattended monitoring sends emails, ntfy notifications or nothing; choose it in Settings > General settings."));
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Guided setup", "Help", "Exit" }));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was created, changed or deleted");
        }

        [Test]
        public void TheNotificationsMenu_IsEmailNtfyAndBack_Only_NoGeneralOrWebYaml()
        {
            Write(General, "outputTo: Console\n");
            var Terminal = new MenuTerminal(P("Notifications"), End);

            Run(Terminal);

            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Email", "ntfy", "Back to the main menu" }), "General.yaml and Web.yaml are phase 7a's");
        }

        [Test]
        public void OpeningEmailAndNtfy_AndLeavingAtOnce_CreatesNothing_AndSendsNothing()
        {
            Write(General, "outputTo: Console\n");
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Back to Notifications"), P("ntfy") }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before), "no Email.yaml or Ntfy.yaml was created");
            Assert.That(Sent, Is.Empty);
            Assert.That(Handler.Seen, Is.Empty);
            Assert.That(Terminal.Texts, Does.Contain("Warning: Email.yaml does not exist."));
            Assert.That(Terminal.Texts, Does.Contain("Test email: Email.yaml does not exist yet: create and save it first."));
            Assert.That(Terminal.Texts, Does.Contain("Test notification: Ntfy.yaml does not exist yet: enter the server and the topic, and save it first."));
        }

        [TestCase(false, false, "The settings folder does not exist yet, so there are no notification settings to change. A start without arguments, or the desktop app, creates it.")]
        [TestCase(true, false, "The settings folder was not found, so there are no notification settings to change. In a container, mount the settings folder at /config.")]
        [TestCase(false, true, "This computer gives Campertron no folder for its settings, so there are no notification settings to change.")]
        public void WithoutTheSettingsFolder_NotificationsSaysSo_AndCreatesNothing(bool Container, bool NoFolder, string Words)
        {
            string Missing = Path.Join(Folder, "not-created");
            var Terminal = new MenuTerminal(P("Notifications"), P("Exit"));

            int Code = InteractiveConsole.Run(Session(Terminal, Missing, Container, NoFolder));

            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Words));
            Assert.That(Directory.Exists(Missing), Is.False, "the console never creates the settings folder");
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("  Email.yaml", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void TheSummary_SaysWhetherTheStartWouldAcceptEachFile_ShowsNoValue()
        {
            Write(General, "outputTo: Email\n");
            Write(Email, EmailYaml(OldPassword));
            Write(Ntfy, NtfyYaml());
            Variables["CAMPERTRON_EMAIL_PASSWORD"] = NewPassword;
            List<(string Label, string Value)> Ready = InteractiveNotifications.Summary(Folder, Now);
            Assert.That(Ready, Is.EqualTo(new List<(string, string)>
            {
                ("Email.yaml", "found; email mode's start would accept it as it is saved"),
                ("Ntfy.yaml", "found; ntfy is on, and the monitoring's start would accept it as it is saved"),
                ("outputTo (General.yaml)", "Email")
            }));

            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 0\n");
            Write(Ntfy, NtfyYaml(Extra: "timeoutSeconds: 500\n"));
            List<(string Label, string Value)> Refused = InteractiveNotifications.Summary(Folder, Now);
            Assert.That(Refused[0].Value, Is.EqualTo("found; email mode's start would refuse it as it is saved: choose Email to see why"));
            Assert.That(Refused[1].Value, Is.EqualTo("found; ntfy is on, but the monitoring's start would refuse it as it is saved: choose ntfy to see why"));

            Write(Ntfy, NtfyYaml(Enabled: false));
            Assert.That(InteractiveNotifications.Summary(Folder, Now)[1].Value, Is.EqualTo("found; ntfy is off"));

            File.WriteAllBytes(Email, new byte[] { 0x73, 0x3A, 0x20, 0xFF, 0xFE, 0x0A });
            Assert.That(InteractiveNotifications.Summary(Folder, Now)[0].Value, Is.EqualTo("found, but it can't be used as it is: choose it to see why"));

            File.Delete(General);
            Assert.That(InteractiveNotifications.Summary(Folder, Now)[2].Value, Is.EqualTo("not set up (General.yaml is missing or can't be used)"));
            string Said = string.Join("\n", Ready.Concat(Refused).Select(F => F.Label + F.Value));
            Assert.That(Said.Contains(OldPassword) || Said.Contains(OldTopic) || Said.Contains(NewPassword) || Said.Contains("example.invalid"), Is.False, "no value of a file or variable");
        }

        [Test]
        public void EscCtrlCOrFiveUnusableAnswers_AtTheNotificationsMenu_ReturnToTheMainMenu_AndTheEndOfTheInputEndsTheConsole()
        {
            Write(General, "outputTo: Console\n");
            var Terminal = new MenuTerminal(P("Notifications"), Esc, P("Notifications"), CtrlC, P("Notifications"),
                L("x"), L("9"), L(""), L("0"), L("-1"), P("Notifications"), End);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Prompts.Count(T => T == "Choose a number from 1 to 10: "), Is.EqualTo(4), "the main menu after each");
            Assert.That(Terminal.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False, "Ctrl+C below the main menu cancels, it does not stop");
        }

        // ---- Email.yaml ----

        [Test]
        public void AMissingEmailYaml_IsCreatedOnlyWhenConfirmed_WithTheSharedFormsSample()
        {
            Write(General, "outputTo: Console\n");
            Dictionary<string, string> Before = Fingerprint();
            var Refused = new MenuTerminal(Script(IntoEmail, new object[] { P("Create Email.yaml"), No, P("Create Email.yaml"), Esc }, OutToExit));
            Run(Refused);
            Assert.That(Fingerprint(), Is.EqualTo(Before), "No and Esc create nothing");
            Assert.That(Refused.Prompts, Does.Contain("Create Email.yaml with the default settings? [y/N]: "));

            var Created = new MenuTerminal(Script(IntoEmail, new object[] { P("Create Email.yaml"), Yes }, OutToExit));
            Run(Created);

            string Other = Path.Join(Secrets, "oracle-create");
            Directory.CreateDirectory(Other);
            new SettingsViewModel(Other, () => Now).Email.Create();
            Assert.That(File.ReadAllBytes(Email), Is.EqualTo(File.ReadAllBytes(Path.Join(Other, "Email.yaml"))), "the bytes the desktop GUI's and the web UI's Create write");
            Assert.That(Created.Texts, Does.Contain("Info: Created Email.yaml with the default settings."));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Email), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }

        [Test]
        public void EditingEveryEmailSetting_WritesTheBytesTheSharedFormWrites_AndItsBackup()
        {
            string PasswordFile = Path.Join(Secrets, "smtp_password");
            File.WriteAllText(PasswordFile, FileSecret);
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[]
            {
                P("Change SMTP server"), L("mail.example.invalid"),
                P("Change SMTP port"), L("465"),
                P("Change User name"), L("someone"),
                P("Change SMTP password file (recommended)"), L(PasswordFile),
                P("Change Password (plain text)"), L(NewPassword),
                P("Change From address"), L("sender@example.invalid"),
                P("Change Recipients (sendToAddressList)"),
                    P("Add recipient"), L("two@example.invalid"),
                    P("Add recipient"), L("three@example.invalid"),
                    P("Move a recipient up"), P("Recipient 3: three@example.invalid"),
                    P("Change a recipient"), P("Recipient 1: one@example.invalid"), L("first@example.invalid"),
                    P("Remove a recipient"), P("Recipient 3: two@example.invalid"),
                    P("Done"),
                P("Show the changes Save would write"),
                P("Save"), Yes
            }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Other = Oracle(S => S.Email, Form =>
            {
                Form.SmtpServer.Text = "mail.example.invalid";
                Form.SmtpPort.Text = "465";
                Form.SmtpUsername.Text = "someone";
                Form.SmtpPasswordFile.Text = PasswordFile;
                Form.SmtpPassword.Text = NewPassword;
                Form.SendFromAddress.Text = "sender@example.invalid";
                Form.Recipients.AddRow();
                Form.Recipients.Rows[1].Text = "two@example.invalid";
                Form.Recipients.AddRow();
                Form.Recipients.Rows[2].Text = "three@example.invalid";
                Form.Recipients.Rows[2].UpCommand!.Execute(null);
                Form.Recipients.Rows[0].Text = "first@example.invalid";
                Form.Recipients.Rows[2].RemoveCommand!.Execute(null);
            }, ("General.yaml", "outputTo: Console\n"), ("Email.yaml", EmailYaml(OldPassword)));
            Assert.That(File.ReadAllBytes(Email).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Email.yaml"))), Is.True, "the same YAML as the shared form writes");
            Assert.That(File.ReadAllBytes(Email + ".bak").SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Email.yaml.bak"))), Is.True, "and the same backup");
            Assert.That(Text(Email).Contains(NewPassword), Is.True);
            Assert.That(Text(Email), Does.StartWith("# my email settings\n"), "the user's comment is kept");
            Assert.That(Text(Email + ".bak").Contains(OldPassword), Is.False, "D111: the replaced password is not in the backup");
            Assert.That(Text(Email + ".bak"), Does.Contain("smtpServer: smtp.example.invalid").And.Contain("# my email settings"), "the backup keeps the previous settings");
            Assert.That(Terminal.Texts, Does.Contain("Info: Saved Email.yaml. The previous version is in Email.yaml.bak, without its plain-text password."));
            Assert.That(Terminal.Texts.Any(T => T.Contains("\u2022\u2022\u2022\u2022\u2022\u2022", StringComparison.Ordinal)), Is.True, "the preview masks the password");
            Assert.That(Terminal.SecretReads, Is.EqualTo(1), "the password was read as a secret");
            AssertNoSecret(Terminal);
        }

        // phase 7's rule (business-rules §43): a settings form's text answer reaches the form without the spaces around it, which
        // the Email form does not trim itself; since D124 the adapter's TextAsTyped hook keeps it so for the settings forms,
        // while a search form's answers go as typed (a finding of §DR's negative controls: NC45 changed it unseen)
        [Test]
        public void ASettingsFormsTextAnswer_ReachesTheFormWithoutTheSpacesAroundIt()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP server"), L("  mail.example.invalid  "), P("Save"), Yes }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Other = Oracle(S => S.Email, Form => Form.SmtpServer.Text = "mail.example.invalid", ("General.yaml", "outputTo: Console\n"), ("Email.yaml", EmailYaml(OldPassword)));
            Assert.That(File.ReadAllBytes(Email).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Email.yaml"))), Is.True, "the value without its spaces, as phase 7 hands it to the form");
            Assert.That(Text(Email), Does.Contain("smtpServer: mail.example.invalid\n"));
            AssertNoSecret(Terminal);
        }

        [Test]
        public void ThePlainTextPassword_IsNeverShown_TheFormSaysOnlyItsState()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change Password (plain text)"), Esc, P("Change SMTP server"), L("x.example.invalid"), P("Show the changes Save would write"), P("Revert changes"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("  Password (plain text): A plain-text password is set in Email.yaml. It is never shown here."));
            Assert.That(Terminal.Texts, Does.Contain("Cancelled."));
            Assert.That(Text(Email), Is.EqualTo(EmailYaml(OldPassword)), "Esc and Revert changed nothing");
            AssertNoSecret(Terminal);
        }

        [Test]
        public void RemovingThePlainTextPassword_LeavesItOutOfTheBackup_AndKeepsTheOtherSettings()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Remove the plain-text password"), P("Save"), Yes }, OutToExit));

            Run(Terminal);

            string Other = Oracle(S => S.Email, Form => Form.RemovePassword(), ("General.yaml", "outputTo: Console\n"), ("Email.yaml", EmailYaml(OldPassword)));
            Assert.That(File.ReadAllBytes(Email).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Email.yaml"))), Is.True);
            Assert.That(Text(Email).Contains(OldPassword) || Text(Email + ".bak").Contains(OldPassword), Is.False, "neither the file nor its backup holds the removed password");
            Assert.That(Text(Email + ".bak"), Does.Contain("smtpUsername: test-user").And.Contain("- one@example.invalid"));
            Assert.That(Terminal.Texts, Does.Contain("  Password (plain text): The plain-text password will be removed from Email.yaml when you save. (changed)"));
            AssertNoSecret(Terminal);
        }

        [Test]
        public void OnATerminalThatCantHideWhatIsTyped_NoPasswordIsRead_AndNothingChanges()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            var Asked = new MenuTerminal(Script(IntoEmail, new object[] { P("Change Password (plain text)") }, OutToExit)) { Hides = false };

            Run(Asked);

            Assert.That(Asked.Texts, Does.Contain("This needs a terminal that can hide what you type, and this one can't, so nothing was read."));
            Assert.That(Asked.Texts.Any(T => T.Contains("(changed)")), Is.False);
            Assert.That(Text(Email), Is.EqualTo(EmailYaml(OldPassword)));
        }

        [Test]
        public void ThePasswordFile_IsAPathOnly_NeverOpened_ARelativeOneIsTheFormsError_AndRemoveTakesItsLineOut()
        {
            string PasswordFile = Path.Join(Secrets, "smtp_password");
            File.WriteAllText(PasswordFile, FileSecret);
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(Extra: "smtpPasswordFile: " + PasswordFile + "\n"));
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[]
            {
                P("Change SMTP password file (recommended)"), L("relative/path"),
                P("Revert changes"), Yes,
                P("Remove smtpPasswordFile"), P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Text(Email), Does.Not.Contain("smtpPasswordFile"), "Remove smtpPasswordFile took its line out");
            Assert.That(Text(Email + ".bak"), Does.Contain("smtpPasswordFile: " + PasswordFile));
            string RelativeError = Terminal.Texts.Single(T => T.StartsWith("    Error: ", StringComparison.Ordinal) && T.Contains("smtpPasswordFile", StringComparison.Ordinal));
            Assert.That(ConfigValidation.PasswordFilePathProblem("relative/path")!.Value.Message, Is.EqualTo(RelativeError.Substring("    Error: ".Length)), "the form's own words");
            AssertNoSecret(Terminal);
        }

        [Test]
        public void TheVariables_AreSaidYesOrNoOnly_NeverTheirValues()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            Write(Ntfy, NtfyYaml());
            Variables["CAMPERTRON_EMAIL_PASSWORD"] = NewPassword;
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Back to Notifications"), P("ntfy") }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("CAMPERTRON_EMAIL_PASSWORD set where this console runs: Yes. Only whether it is set is shown, never its value. Email mode uses it when Email.yaml has no smtpPasswordFile, before the plain-text password."));
            Assert.That(Terminal.Texts, Does.Contain("CAMPERTRON_NTFY_TOKEN set where this console runs: No. Only whether it is set is shown, never its value. The monitoring uses it when Ntfy.yaml has no tokenFile."));
            AssertNoSecret(Terminal);
        }

        [Test]
        public void APortThatIsNotANumber_IsTheFormsError_NothingIsSaved_AndRevertTakesItBack()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP port"), L("five"), P("Revert changes"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a port number, such as 587."));
            Assert.That(Text(Email), Is.EqualTo(EmailYaml()));
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes to Email.yaml were reverted."));
        }

        [Test]
        public void AValueTheValidationRefuses_BlocksSave_InTheFormsWords_AndTheUserStaysAtTheForm()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change From address"), L("not an address"), P("Save"), Yes, P("Back to Notifications"), Yes, P("Back to the main menu"), P("Exit") }));

            Run(Terminal);

            var Oracle_ = new SettingsViewModel(Folder, () => Now).Email;
            Oracle_.SendFromAddress.Text = "not an address";
            Oracle_.Save();
            Assert.That(Oracle_.StatusIsError, Is.True);
            Assert.That(Terminal.Texts, Does.Contain(MessageSeverity.AccessibleText(Oracle_.StatusText, MessageSeverity.Error)), "the form's own refusal");
            Assert.That(Terminal.Texts, Does.Contain("    " + Oracle_.SendFromAddress.MessageText));
            Assert.That(Text(Email), Is.EqualTo(EmailYaml()), "nothing was saved");
            Assert.That(Terminal.Prompts, Does.Contain("Leave without saving your changes to Email.yaml? They are not kept. [y/N]: "), "the save failed, so the changes were still there");
        }

        [Test]
        public void AChangeOnDisk_ReloadsACleanForm_AndIsAConflictForAnEditedOne_NeverOverwritten()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            string Theirs = EmailYaml(Extra: "# another program\n").Replace("test-user", "their-user");
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[]
            {
                (Action)(() => Write(Email, EmailYaml().Replace("smtp.example.invalid", "disk.example.invalid"))),
                P("Change SMTP server"), Esc,
                P("Change SMTP port"), L("2525"),
                (Action)(() => Write(Email, Theirs)),
                P("Change SMTP server"), Esc,
                P("Discard my changes and use the file on disk"), No,
                P("Discard my changes and use the file on disk"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Info: Email.yaml was changed on disk and has been reloaded."));
            Assert.That(Terminal.Texts, Does.Contain("  SMTP server: disk.example.invalid"));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Email.yaml was changed by another program: Warning: ", StringComparison.Ordinal)), Is.True);
            int Conflict = Terminal.Texts.FindIndex(T => T.StartsWith("Email.yaml was changed by another program", StringComparison.Ordinal));
            Assert.That(Terminal.Prompts, Does.Contain("Discard your changes and use Email.yaml as it is on disk? [y/N]: "));
            Assert.That(Text(Email), Is.EqualTo(Theirs), "the other program's version was never written over");
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes were discarded; Email.yaml shows the version on disk."));
            Assert.That(Conflict, Is.GreaterThan(0));
        }

        [Test]
        public void Revert_AsksFirst_TakesBackTheEdits_AndNeverRestoresTheBackup()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            Write(Email + ".bak", "smtpServer: from-the-backup.example.invalid\n");
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP server"), L("x.example.invalid"), P("Revert changes"), No, P("Revert changes"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Prompts.Count(T => T == "Revert your changes to Email.yaml? [y/N]: "), Is.EqualTo(2));
            Assert.That(Terminal.Texts, Does.Contain("Your changes are still here; nothing was saved."), "No keeps them");
            Assert.That(Text(Email), Is.EqualTo(EmailYaml()));
            Assert.That(Text(Email + ".bak"), Is.EqualTo("smtpServer: from-the-backup.example.invalid\n"), "the backup is never read or restored");
            Assert.That(Terminal.Texts, Does.Contain("  SMTP server: smtp.example.invalid"));
        }

        [Test]
        public void Leaving_WithUnsavedChanges_Asks_NoStays_YesLeavesWithNothingSaved()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP server"), L("x.example.invalid"), P("Back to Notifications"), No, Esc, Esc, P("Back to Notifications"), Yes, P("Email") }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Prompts.Count(T => T == "Leave without saving your changes to Email.yaml? They are not kept. [y/N]: "), Is.EqualTo(3), "Back, then Esc at the choices; Esc at the question stays");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was saved");
            Assert.That(Terminal.Texts.Last(T => T.StartsWith("  SMTP server: ", StringComparison.Ordinal)), Is.EqualTo("  SMTP server: smtp.example.invalid"), "a new form shows only what was saved");
        }

        [TestCase("AtTheChoices")]
        [TestCase("AtAFieldsQuestion")]
        [TestCase("AtSavesQuestion")]
        [TestCase("AtThePassword")]
        [TestCase("AtTheLeaveQuestion")]
        public void TheEndOfTheInput_InAForm_SavesNothing_AndEndsTheConsoleWithZero(string Where)
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            Dictionary<string, string> Before = Fingerprint();
            object[] Steps = Where switch
            {
                "AtTheChoices" => new object[] { P("Change SMTP server"), L("x.example.invalid"), End },
                "AtAFieldsQuestion" => new object[] { P("Change SMTP server"), L("x.example.invalid"), P("Change SMTP port"), End },
                "AtSavesQuestion" => new object[] { P("Change SMTP server"), L("x.example.invalid"), P("Save"), End },
                "AtTheLeaveQuestion" => new object[] { P("Change SMTP server"), L("x.example.invalid"), P("Back to Notifications"), End },
                _ => new object[] { P("Change Password (plain text)"), End },
            };
            var Terminal = new MenuTerminal(Script(IntoEmail, Steps));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
        }

        [Test]
        public void EscOrCtrlC_AtAFieldsQuestion_KeepsTheValue_AndTheFormsChoicesComeAgain()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP server"), Esc, P("Change SMTP port"), CtrlC, P("Change Recipients (sendToAddressList)"), Esc }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(2));
            Assert.That(Terminal.Texts.Any(T => T.Contains("(changed)")), Is.False);
            Assert.That(Text(Email), Is.EqualTo(EmailYaml()));
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False);
        }

        [Test]
        public void ACancelledQuestion_WithUnsavedChanges_SavesNothing_AndKeepsTheChanges()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[]
            {
                P("Change SMTP server"), L("x.example.invalid"),
                P("Change SMTP port"), Esc,
                P("Change User name"), CtrlC,
                P("Save"), Esc,
                P("Save"), No,
                P("Revert changes"), CtrlC,
                (Action)(() => Assert.That(Fingerprint(), Is.EqualTo(Before), "no cancelled question saved")),
                P("Back to Notifications"), Yes, P("Back to the main menu"), P("Exit")
            }));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was saved");
            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(4), "Esc and Ctrl+C at the port, the user name, Save's and Revert's questions");
            Assert.That(Terminal.Texts.Count(T => T == "  SMTP server: x.example.invalid (changed)"), Is.GreaterThanOrEqualTo(5), "the change stayed through every cancel");
        }

        [Test]
        public void FiveUnusableAnswers_AtTheFormsChoices_LeaveTheForm()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml());
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { L("99"), L("x"), L(""), L("0"), L("1.5"), P("Back to the main menu"), P("Exit") }));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));
        }

        [Test]
        public void AFileThatCantBeRead_OffersOnlyReloadAndBack_AndWritesNothing()
        {
            Write(General, "outputTo: Console\n");
            File.WriteAllBytes(Email, new byte[] { 0x73, 0x3A, 0x20, 0xFF, 0xFE, 0x0A });
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Reload from disk"), End }));

            Run(Terminal);

            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Reload from disk", "Back to Notifications" }), "nothing to edit, save or send");
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Warning: ", StringComparison.Ordinal)), Is.True, "the form says why");
            Assert.That(Fingerprint(), Is.EqualTo(Before));
        }

        [Test]
        public void AValueWithControlCharacters_IsShownWithoutThem()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, "smtpServer: \"smtp\\e[2J\\a.example.invalid\"\nsmtpPort: 587\n");
            var Terminal = new MenuTerminal(Script(IntoEmail, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.AllText.Any(C => char.IsControl(C) && C != '\n'), Is.False, "no escape, bell or other control character reaches the terminal");
            Assert.That(Terminal.Texts, Does.Contain("  SMTP server: smtp\uFFFD[2J\uFFFD.example.invalid"));
        }

        // ---- the test email ----

        [Test]
        public void TheTestEmail_IsSentOnlyWhenConfirmed_ThroughTheSharedTest_WritingAndStartingNothing()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Send a test email"), No, P("Send a test email"), Esc, P("Send a test email"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Sent, Has.Count.EqualTo(1), "No and Esc sent nothing; yes sent one");
            Assert.That(Sent[0].Subject, Is.EqualTo(TestNotification.EmailSubject));
            Assert.That(Sent[0].To, Is.EqualTo(new[] { "one@example.invalid" }));
            Assert.That(Sent[0].Password == OldPassword, Is.True, "the saved plain-text password, by email mode's own resolver");
            Assert.That(Terminal.Texts, Does.Contain("Nothing was sent."));
            Assert.That(Terminal.Texts, Does.Contain("Sending a test email\u2026"));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Test email: Info: the SMTP server accepted the test email for 1 recipient at ", StringComparison.Ordinal)), Is.True);
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing written: no file, no Camp.History, no monitoring lock");
            Assert.That(Directory.Exists(Path.Join(Folder, "Camp.History")) || File.Exists(Path.Join(Folder, "Campertron.monitoring.lock")), Is.False);
            AssertNoSecret(Terminal);
        }

        [Test]
        public void TheTestEmail_IsNotOffered_WithUnsavedChanges_AndAFailureIsFixedWords()
        {
            Write(General, "outputTo: Console\n");
            Write(Email, EmailYaml(OldPassword));
            EmailAnswer = (S, M) => new SmtpException(SmtpStatusCode.MailboxUnavailable, "CANARY server text " + OldPassword);
            var Terminal = new MenuTerminal(Script(IntoEmail, new object[] { P("Change SMTP server"), L("x.example.invalid") }));
            Run(Terminal);
            Assert.That(Terminal.LastChoices, Does.Not.Contain("Send a test email"));
            Assert.That(Terminal.Texts, Does.Contain("Test email: The test uses Email.yaml as it is saved: save or revert your changes first."));

            var Failing = new MenuTerminal(Script(IntoEmail, new object[] { P("Send a test email"), Yes, P("Send a test email"), Yes }, OutToExit));
            Run(Failing);

            string Result = Failing.Texts.Last(T => T.StartsWith("Test email: ", StringComparison.Ordinal));
            Assert.That(Result, Does.StartWith("Test email: Error: the test email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(Failing.AllText.Contains("CANARY"), Is.False, "never the server's text");
            Assert.That(Sent, Has.Count.EqualTo(2), "the console goes on after a failed test: the test was offered and sent again");
            AssertNoSecret(Failing);
        }

        // ---- Ntfy.yaml ----

        [Test]
        public void AMissingNtfyYaml_IsANewFile_NothingIsWrittenUntilSave_ThenTheSharedFormsBytes_OwnerOnly()
        {
            Write(General, "outputTo: Console\n");
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[]
            {
                P("Change Enabled: send new openings with ntfy"), Yes,
                P("Change Server"), L("https://ntfy.example.invalid"),
                P("Change Topic"), L(OldTopic),
                (Action)(() => Assert.That(File.Exists(Ntfy), Is.False, "nothing is written while editing")),
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            string Other = Oracle(S => S.Ntfy, Form =>
            {
                Form.Enabled.IsChecked = true;
                Form.Server.Text = "https://ntfy.example.invalid";
                Form.Topic.Text = OldTopic;
            }, ("General.yaml", "outputTo: Console\n"));
            Assert.That(File.ReadAllBytes(Ntfy).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Ntfy.yaml"))), Is.True);
            Assert.That(Terminal.Texts, Does.Contain("Info: Created Ntfy.yaml with your settings."));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Ntfy), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }

        private static readonly Regex GeneratedTopic = new Regex("campertron-[a-z0-9]{26}", RegexOptions.CultureInvariant);

        [Test]
        public void TheRandomTopic_IsTheSharedGenerators_AnUnsavedChange_ShownOnlyWhenAskedFor()
        {
            Write(General, "outputTo: Console\n");
            Write(Ntfy, NtfyYaml());
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[]
            {
                P("Generate a random topic"),
                P("Show the topic"), No,
                P("Show the topic"), Yes,
                P("Revert changes"), Yes,
                P("Generate a random topic"),
                P("Back to Notifications"), Yes, P("Back to the main menu"), P("Exit")
            }));

            Run(Terminal);

            List<string> Shown = Terminal.Texts.Where(T => GeneratedTopic.IsMatch(T)).ToList();
            Assert.That(Shown, Has.Count.EqualTo(1), "a generated topic is written only once, when the user asked and answered yes");
            Assert.That(Shown[0], Does.Match("^  Topic: campertron-[a-z0-9]{26}$"), "NtfyTopic's form: campertron- and 26 letters and digits");
            Assert.That(Terminal.Texts, Does.Contain("Info: " + NtfySettingsViewModel.TopicGenerated));
            Assert.That(Terminal.Texts, Does.Contain("  Topic: set, not shown (choose Show the topic to see it) (changed)"));
            Assert.That(Text(Ntfy), Is.EqualTo(NtfyYaml()), "neither generated topic was saved: Revert, then leaving");
            Assert.That(File.Exists(Ntfy + ".bak"), Is.False);
            Assert.That(Terminal.AllText.Contains(OldTopic), Is.False, "the saved topic was never shown");
        }

        [Test]
        public void ReplacingTheTopic_LeavesTheOldOneOutOfTheBackup_AndTheLinesAreShownOnlyWhenAskedFor()
        {
            Write(General, "outputTo: Console\n");
            Write(Ntfy, NtfyYaml());
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[]
            {
                P("Generate a random topic"),
                P("Show the changes Save would write"), No,
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Prompts, Does.Contain("The lines can show the topic. Show them on this screen? Anyone who can see the screen can read them. [y/N]: "));
            Assert.That(Terminal.Texts.Any(T => GeneratedTopic.IsMatch(T)), Is.False, "No: no line shown");
            Assert.That(GeneratedTopic.IsMatch(Text(Ntfy)), Is.True, "the new topic was saved");
            Assert.That(Text(Ntfy + ".bak").Contains(OldTopic), Is.False, "D112: the replaced topic is not in the backup");
            Assert.That(Text(Ntfy + ".bak"), Does.Contain("server: https://ntfy.example.invalid").And.Contain("# my ntfy settings"));
            Assert.That(Terminal.Texts, Does.Contain("Info: Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without its topic."));
            Assert.That(Terminal.AllText.Contains(OldTopic), Is.False);
        }

        [Test]
        public void EveryNtfySetting_IsEditedThroughTheForm_AndWritesTheSharedFormsBytes()
        {
            string TokenFile = Path.Join(Secrets, "ntfy_token");
            File.WriteAllText(TokenFile, Token);
            Write(General, "outputTo: Console\n");
            Write(Ntfy, NtfyYaml(Extra: "tags:\n- tent\n"));
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[]
            {
                P("Change Access token file (optional)"), L(TokenFile),
                P("Change Priority"), P("High: more noticeable"),
                P("Change Tags (optional)"), P("Add tag"), L("camping"), P("Remove a tag"), P("Tag 1: tent"), P("Done"),
                P("Change Timeout (seconds)"), L("500"),
                P("Change Timeout (seconds)"), L("45"),
                P("Change Enabled: send new openings with ntfy"), No,
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a whole number of seconds from 1 to 120, such as 30."), "the form's own range check");
            string Other = Oracle(S => S.Ntfy, Form =>
            {
                Form.TokenFile.Text = TokenFile;
                Form.Priority.Selected = Form.Priority.Choices.Single(C => C.Label == "High: more noticeable");
                Form.Tags.AddRow();
                Form.Tags.Rows[1].Text = "camping";
                Form.Tags.Rows[0].RemoveCommand!.Execute(null);
                Form.TimeoutSeconds.Text = "500";
                Form.TimeoutSeconds.Text = "45";
                Form.Enabled.IsChecked = false;
            }, ("General.yaml", "outputTo: Console\n"), ("Ntfy.yaml", NtfyYaml(Extra: "tags:\n- tent\n")));
            Assert.That(File.ReadAllBytes(Ntfy).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Ntfy.yaml"))), Is.True, "the same YAML as the shared form writes");
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(NtfyYaml(Extra: "tags:\n- tent\n")), "the topic was not replaced, so the backup is the bytes read");
            Assert.That(Terminal.AllText.Contains(OldTopic), Is.False);
            AssertNoSecret(Terminal);

            var Removing = new MenuTerminal(Script(IntoNtfy, new object[] { P("Remove tokenFile"), P("Save"), Yes }, OutToExit));
            Run(Removing);
            Assert.That(Text(Ntfy), Does.Not.Contain("tokenFile"));
            AssertNoSecret(Removing);
        }

        [Test]
        public void TheTestNotification_IsSentOnlyWhenConfirmed_ThroughTheSharedTest_WritingNothing_AndAFailureIsFixedWords()
        {
            string TokenFile = Path.Join(Secrets, "ntfy_token");
            File.WriteAllText(TokenFile, Token);
            Write(General, "outputTo: Console\n");
            Write(Ntfy, NtfyYaml(Enabled: false, Extra: "tokenFile: " + TokenFile + "\n"));
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[] { P("Send a test notification"), No, P("Send a test notification"), CtrlC, P("Send a test notification"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Handler.Seen, Has.Count.EqualTo(1), "No and Ctrl+C sent nothing; yes sent one, also with ntfy off");
            Assert.That(Handler.Seen[0].Authorization == "Bearer " + Token, Is.True, "the token from the token file, by the monitoring's own resolver");
            Assert.That(Handler.Seen[0].Body.Contains(OldTopic), Is.True, "to the saved topic");
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Test notification: Info: the ntfy server accepted the test notification at ", StringComparison.Ordinal)), Is.True);
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing written: no file, no Camp.History, no monitoring lock");
            AssertNoSecret(Terminal, OldTopic);

            Handler.Answer = R => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("CANARY server body " + Token) };
            var Failing = new MenuTerminal(Script(IntoNtfy, new object[] { P("Send a test notification"), Yes }, OutToExit));
            Run(Failing);
            string Result = Failing.Texts.Last(T => T.StartsWith("Test notification: ", StringComparison.Ordinal));
            Assert.That(Result, Does.StartWith("Test notification: Error: the test notification could not be sent: "));
            Assert.That(Failing.AllText.Contains("CANARY"), Is.False, "never the server's body");
            AssertNoSecret(Failing, OldTopic);
        }

        // PT1's follow-up (v0.4.0's phase 13): an ntfy server's error at the test notification. The console goes on, so the
        // result says to check the server and to select the choice its form offers again, never a restart policy; the form
        // offers it again; one publish; the monitoring is not started (its status after the test: not running); nothing is
        // written (no Camp.History, no lock); never the topic, the token or the server's answer
        [Test]
        public void AServerErrorAtTheTestNotification_SaysToCheckTheServerAndSelectTheChoiceAgain_NeverARestartPolicy()
        {
            string TokenFile = Path.Join(Secrets, "ntfy_token");
            File.WriteAllText(TokenFile, Token);
            Write(General, "outputTo: Console\n");
            Write(Ntfy, NtfyYaml(Enabled: true, Extra: "tokenFile: " + TokenFile + "\n"));
            Handler.Answer = R => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("CANARY server body " + Token) };
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoNtfy, new object[] { P("Send a test notification"), Yes, P("Send a test notification"), No },
                new object[] { P("Back to Notifications"), P("Back to the main menu"), P("Monitoring"), P("Back to the main menu"), P("Exit") }));

            Assert.That(Run(Terminal), Is.Zero);

            //the form shows its last result each time it shows itself: one result, the same words each time
            string Result = Terminal.Texts.Where(T => T.StartsWith("Test notification: ", StringComparison.Ordinal)).Distinct().Single();
            Assert.That(Result, Is.EqualTo("Test notification: Error: the test notification could not be sent: the ntfy server failed (HTTP 500). "
                + "The server has a problem: check it, then select Send a test notification again."));
            Assert.That(Terminal.Texts.Count(T => T.Contains("Send a test notification", StringComparison.Ordinal) && !T.StartsWith("Test notification: ", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(2),
                "the choice the form offers, before the test and again after it");
            Assert.That(Terminal.AllText, Does.Not.Contain("restart policy").And.Not.Contain("start Campertron again").And.Not.Contain("CANARY"));
            Assert.That(Handler.Seen, Has.Count.EqualTo(1), "one publish; the second choice's No sent nothing");
            Assert.That(Terminal.Texts, Does.Contain("  Status: Not running."), "a test starts no monitoring");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing written: no file, no Camp.History, no monitoring lock");
            AssertNoSecret(Terminal, OldTopic);
        }

        // ---- what the notifications' code can reach ----

        private static List<string> Reached()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveNotifications).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveNotifications).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheNotificationsCode_ReachesTheSharedEmailAndNtfyForms_AndNothingThatParsesWritesSendsOrMonitorsByItself()
        {
            List<string> Called = Reached();
            string[] Forbidden =
            {
                //no YAML, writer, session, resolver, sender or generator of its own: only the forms reach them
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.ConfigValidation", "CampertronLibrary.function.Base.TestNotification::", "CampertronLibrary.function.Base.SmtpPassword::ResolveOnce",
                "CampertronLibrary.function.Base.SmtpPassword::Resolve", "CampertronLibrary.function.Base.NtfyToken::Resolve", "CampertronLibrary.function.Base.NtfyPublisher",
                "CampertronLibrary.function.Base.SecretFile", "CampertronLibrary.function.Base.NtfyTopic", "CampertronLibrary.function.Base.CampsiteConfig",
                "CampertronLibrary.function.Base.Notifications", "System.Security.Cryptography.", "System.Random",
                //no search, campground, results, refresh, monitoring, guided setup or transfer (General.yaml and Web.yaml are the
                //Settings menu's since phase 7a, D123: InteractiveSettingsTests checks them; the Notifications menu's own code below)
                "CampertronLibrary.function.Forms.Searches", "CampertronLibrary.function.Forms.SearchFile", "CampertronLibrary.function.Forms.Campground",
                "CampertronLibrary.function.Forms.Results", "CampertronLibrary.function.Forms.Refresh", "CampertronLibrary.function.Forms.Monitoring",
                "CampertronLibrary.function.Forms.GuidedSetupFlow", "CampertronLibrary.function.Forms.FilterSelector",
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.RecDotOrg.api.",
                "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "CampertronLibrary.function.Base.Menu",
                "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.RidbRefreshService",
                "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                //no file, folder, process, network or environment of its own
                "System.IO.File::Write", "System.IO.File::Create", "System.IO.File::Delete", "System.IO.File::Move", "System.IO.File::Copy", "System.IO.File::Open",
                "System.IO.File::Read", "System.IO.File::Append", "System.IO.File::Replace", "System.IO.Directory::Create", "System.IO.Directory::Delete",
                "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.StreamReader", "System.Diagnostics.Process", "System.Net.", "System.Environment::",
                "System.Console::"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            //and the scan sees the shared forms it does drive
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.Forms.SettingsViewModel::get_Email", "CampertronLibrary.function.Forms.SettingsViewModel::get_Ntfy",
                "CampertronLibrary.function.Forms.SettingsFileViewModel::Save", "CampertronLibrary.function.Forms.SettingsFileViewModel::Revert",
                "CampertronLibrary.function.Forms.SettingsFileViewModel::CheckForExternalChanges", "CampertronLibrary.function.Forms.TestNotificationForm::Send",
                "CampertronLibrary.function.Forms.NtfySettingsViewModel::get_GenerateTopicCommand", "CampertronLibrary.function.Forms.GuidedSetupStatus::Read",
                "CampertronLibrary.function.Interactive.InteractivePrompts::Secret", "CampertronLibrary.function.Base.SmtpPassword::VariableIsSet",
                "CampertronLibrary.function.Base.NtfyToken::VariableIsSet"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        // the Notifications menu's own code (InteractiveNotifications and its compiler-made types) opens only the Email and
        // Ntfy forms: General.yaml and Web.yaml are the Settings menu's (phase 7a, D123)
        [Test]
        public void TheNotificationsMenu_OpensOnlyTheEmailAndNtfyForms()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveNotifications).Assembly.GetTypes().Where(T => T == typeof(InteractiveNotifications) || T.DeclaringType == typeof(InteractiveNotifications)))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Forms.SettingsViewModel::get_Email").And.Contain("CampertronLibrary.function.Forms.SettingsViewModel::get_Ntfy"));
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Forms.SettingsViewModel::get_General", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Forms.SettingsViewModel::get_Web", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Forms.GeneralSettingsViewModel", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Forms.WebSettingsViewModel", StringComparison.Ordinal)), Is.Empty);
        }

        [Test]
        public void TheNotificationsCode_WritesNoEscapeSequence_AndKeepsNoStaticStateThatChanges()
        {
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveNotifications).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveNotifications).Namespace))
            {
                //a compiler's cached delegate is neither state nor shared data (FormLayerTests' rule)
                foreach (FieldInfo Field in T.GetFields(Declared).Where(F => !F.IsLiteral && !F.Name.Contains('<') && !T.Name.Contains('<')))
                {
                    Assert.That(Field.IsInitOnly, Is.True, T.Name + "." + Field.Name + " is static state that can change");
                }
                foreach (MethodBase Method in T.GetMethods(Declared | BindingFlags.Instance).Cast<MethodBase>().Concat(T.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)))
                {
                    Assert.That(ConsoleStartContractTests.StringLiterals(Method).Where(S => S.Contains('\u001b')), Is.Empty);
                }
                Assert.That(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!).Where(S => S.Any(char.IsControl)), Is.Empty);
            }
        }
    }
}
