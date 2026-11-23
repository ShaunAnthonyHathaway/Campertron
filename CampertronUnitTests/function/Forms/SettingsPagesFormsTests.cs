using System.Collections.Concurrent;
using System.Net.Mail;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Forms
{
    // The forms of v0.3.0's PR 5 at the layer's own level, as both UIs drive them (business-rules-settings §32): Web.yaml's
    // editor with D81's one confirmation, the test email and test notification (D86, D88), the random topic (D86) and the
    // SMTP password's state for a UI that never shows it (D82). Temporary folders; the email's and ntfy's transports are
    // their seams; nothing reaches the network.
    public class SettingsPagesFormsTests : ConfigEditTestBase
    {
        private string WebYaml => Path.Join(Folder, "Web.yaml");
        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private readonly List<string?> SentWith = new();
        private int EnvironmentReads;

        [SetUp]
        public void Seams()
        {
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            SentWith.Clear();
            EnvironmentReads = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => SentWith.Add(CampsiteConfig.SmtpCredential(Settings).Password);
            SmtpPassword.GetEnvironment = Name => { EnvironmentReads++; return null; };
            NtfyToken.GetEnvironment = Name => { EnvironmentReads++; return null; };
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
        }

        private SettingsViewModel Settings(bool? ListensOnNetwork = null, FormHost? Host = null) => new SettingsViewModel(Folder, () => Now, Host, ListensOnNetwork);

        private const string EmailText = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: u\nsmtpPassword: PAGES-cnry-secret\n" +
            "sendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        // ---- Web.yaml and its one confirmation (D81 point 4) ----

        [Test]
        public void AMissingWebYaml_IsANewFileWithTheDefaults_ThatOnlyASaveCreates()
        {
            WebSettingsViewModel Web = Settings().Web;
            Assert.That(Web.IsNew, Is.True);
            Assert.That(Web.IsMissing, Is.False, "a new file, not a missing one: there is no Create");
            Assert.That((Web.PasswordProtection.IsChecked, Web.NetworkListening.IsChecked), Is.EqualTo((true, true)), "D81's defaults");
            Assert.That(Web.AllowedHosts.Rows, Is.Empty);
            Assert.That(Web.FieldList.Select(F => F.Key), Is.EqualTo(new[] { "passwordProtection", "networkListening", "allowedHosts", "trustedProxies" }));
            Assert.That(Web.FileNotices.Select(N => N.Text), Has.Some.StartsWith("Info: Web.yaml does not exist yet. The form shows the default settings (a password required, reachable from the network)"));
            Web.CheckForExternalChanges();
            Assert.That(File.Exists(WebYaml), Is.False, "looking writes nothing");

            Web.AllowedHosts.AddRow();
            Web.AllowedHosts.Rows[0].Text = "campertron.home.example";
            Web.Save();
            Assert.That(Web.StatusIsError, Is.False, Web.StatusText);
            Assert.That(Text(WebYaml), Does.Contain("passwordProtection: true").And.Contain("networkListening: true").And.Contain("campertron.home.example"));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(WebYaml), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "owner-only, as a new Ntfy.yaml");
            }
            Assert.That(Web.HasConfirmation, Is.False, "nothing was open, nothing asked");
        }

        [Test]
        public void TurningThePasswordOff_WhileListeningOnTheNetwork_AsksOnce_ThenNeverAgainForThatState()
        {
            Write(WebYaml, "# mine\npasswordProtection: true\nnetworkListening: true\n");
            WebSettingsViewModel Web = Settings().Web;
            Web.PasswordProtection.IsChecked = false;
            Web.Save();
            Assert.That(Web.HasConfirmation, Is.True);
            Assert.That(Web.ConfirmationText, Is.EqualTo("Without a password, everyone who can reach this computer on the network can open Campertron's web UI and change its settings, searches and notifications. Turn the password off only on a network you trust, or let the web UI listen on this computer only."));
            Assert.That(Web.ConfirmLabel, Is.EqualTo("Save without a password, reachable from the network"));
            Assert.That(Text(WebYaml), Does.Contain("passwordProtection: true"), "nothing written until it is confirmed");
            Assert.That(Web.IsDirty, Is.True, "the change is kept");

            Web.ConfirmSave();
            Assert.That(Web.HasConfirmation, Is.False);
            Assert.That(Web.StatusIsError, Is.False, Web.StatusText);
            Assert.That(Text(WebYaml), Is.EqualTo("# mine\npasswordProtection: false\nnetworkListening: true\n"), "the comment kept, one line changed");

            //the accepted state: another save does not ask again, nor does a new form of the same file
            Web.AllowedHosts.AddRow();
            Web.AllowedHosts.Rows[0].Text = "box.example";
            Web.Save();
            Assert.That(Web.HasConfirmation, Is.False);
            Assert.That(Web.StatusIsError, Is.False, Web.StatusText);
            WebSettingsViewModel Again = Settings().Web;
            Again.TrustedProxies.AddRow();
            Again.TrustedProxies.Rows[0].Text = "172.18.0.0/16";
            Again.Save();
            Assert.That(Again.HasConfirmation, Is.False, "never a reminder for the saved state");
            Assert.That(Again.Validation(), Is.Empty, "the validation has no finding for an open state");

            //protected again, then opened again: it asks again, once
            Again.PasswordProtection.IsChecked = true;
            Again.Save();
            Again.PasswordProtection.IsChecked = false;
            Again.Save();
            Assert.That(Again.HasConfirmation, Is.True);
        }

        [Test]
        public void TheConfirmation_IsDroppedByAnyChange_AndCancelWritesNothing()
        {
            Write(WebYaml, "passwordProtection: true\n");
            WebSettingsViewModel Web = Settings().Web;
            Web.PasswordProtection.IsChecked = false;
            Web.Save();
            Assert.That(Web.HasConfirmation, Is.True);
            Web.CancelConfirmation();
            Assert.That(Web.HasConfirmation, Is.False);
            Assert.That(Web.StatusText, Is.EqualTo("Nothing was saved. Your changes are still here."));
            Assert.That(Text(WebYaml), Is.EqualTo("passwordProtection: true\n"));

            Web.Save();
            Assert.That(Web.HasConfirmation, Is.True);
            Web.NetworkListening.IsChecked = false;
            Assert.That(Web.HasConfirmation, Is.False, "a change of a setting drops the question");
            Web.Save();
            Assert.That(Web.HasConfirmation, Is.False, "this computer only: nothing is open, nothing to confirm");
            Assert.That(Text(WebYaml), Does.Contain("passwordProtection: false").And.Contain("networkListening: false"));

            Web.PasswordProtection.IsChecked = true;
            Web.Revert();
            Assert.That(Web.HasConfirmation, Is.False);
        }

        [Test]
        public void ThisProcesssListen_DecidesForIt_AsTheRuleSays()
        {
            Write(WebYaml, "passwordProtection: true\nnetworkListening: false\n");
            //a web UI started with --listen 0.0.0.0 listens on the network whatever networkListening says
            WebSettingsViewModel Listening = Settings(ListensOnNetwork: true).Web;
            Listening.PasswordProtection.IsChecked = false;
            Listening.Save();
            Assert.That(Listening.HasConfirmation, Is.True);

            //the desktop GUI (no --listen): the file decides, and it says this computer only
            WebSettingsViewModel Desktop = Settings().Web;
            Desktop.PasswordProtection.IsChecked = false;
            Desktop.Save();
            Assert.That(Desktop.HasConfirmation, Is.False);
            Assert.That(Text(WebYaml), Does.Contain("passwordProtection: false"));
        }

        [Test]
        public void AHostOrProxyTheValidationRefuses_BlocksTheSave_AtItsRow()
        {
            WebSettingsViewModel Web = Settings().Web;
            Web.AllowedHosts.AddRow();
            Web.AllowedHosts.Rows[0].Text = "http://bad.example:80";
            Web.TrustedProxies.AddRow();
            Web.TrustedProxies.Rows[0].Text = "10.9.8.7/8";
            Assert.That(Web.AllowedHosts.Rows[0].MessageText, Does.StartWith("Error: allowedHosts item 1 must be a host name"));
            Assert.That(Web.TrustedProxies.Rows[0].MessageText, Does.StartWith("Error: trustedProxies item 1 must be an IP address"));
            Assert.That(Web.CanSave, Is.False);
            Web.Save();
            Assert.That(File.Exists(WebYaml), Is.False);
            Assert.That(Web.StatusIsError, Is.True);
        }

        // ---- the test email and the test notification (D86, D88) ----

        [Test]
        public void TheTest_IsOfferedOnlyForASavedFileWithoutChanges_AndSaysWhyNot()
        {
            SettingsViewModel All = Settings();
            Assert.That(All.Email.Test.CanSend, Is.False);
            Assert.That(All.Email.Test.UnavailableReason, Is.EqualTo("Email.yaml does not exist yet: create and save it first."));
            Assert.That(All.Ntfy.Test.UnavailableReason, Is.EqualTo("Ntfy.yaml does not exist yet: enter the server and the topic, and save it first."));

            Write(Email, EmailText);
            All = Settings();
            Assert.That(All.Email.Test.CanSend, Is.True);
            Assert.That(All.Email.Test.ButtonLabel, Is.EqualTo("Send a test email"));
            All.Email.SmtpServer.Text = "smtp2.example.invalid";
            Assert.That(All.Email.Test.CanSend, Is.False);
            Assert.That(All.Email.Test.UnavailableReason, Is.EqualTo("The test uses Email.yaml as it is saved: save or revert your changes first."));
            Assert.That(All.Email.Test.TryBegin(), Is.False);
            All.Email.Revert();
            Assert.That(All.Email.Test.CanSend, Is.True);
        }

        [Test]
        public void ATest_IsOneAtATime_AndItsResultIsTheLibrarysFixedWords()
        {
            Write(Email, EmailText);
            TestNotificationForm Test = Settings().Email.Test;
            Assert.That(EnvironmentReads, Is.Zero, "opening the form reads no secret source");
            Assert.That(Test.TryBegin(), Is.True);
            Assert.That((Test.IsSending, Test.CanSend), Is.EqualTo((true, false)));
            Assert.That(Test.TryBegin(), Is.False, "no second test while one is being sent");
            TestNotificationResult Result = Test.Send(() => new DateTime(2026, 11, 23, 9, 30, 0), CancellationToken.None);
            Test.End(Result);
            Assert.That(SentWith, Is.EqualTo(new[] { "PAGES-cnry-secret" }));
            Assert.That(Test.ResultText, Does.StartWith("Info: the SMTP server accepted the test email for 1 recipient at 09:30."));
            Assert.That((Test.IsSending, Test.CanSend, Test.ResultIsError), Is.EqualTo((false, true, false)));
            Assert.That(EnvironmentReads, Is.GreaterThan(0), "the variable was looked at for the send, by the precedence");
        }

        [Test]
        public void TheDesktopsRun_SendsOnAWorkerThread_AndEndsOnTheFormsThread()
        {
            Write(Email, EmailText);
            var Posted = new BlockingCollection<Action>();
            int FormThread = Environment.CurrentManagedThreadId;
            int? SendThread = null;
            using var Release = new ManualResetEventSlim(false);
            CampsiteConfig.SendResultsEmail = (Settings_, Message) => { SendThread = Environment.CurrentManagedThreadId; Release.Wait(TimeSpan.FromSeconds(30)); };
            var Host = new FormHost(() => Job => Posted.Add(Job), (Due, Job) => throw new NotSupportedException(), (Every, Job) => throw new NotSupportedException());
            TestNotificationForm Test = Settings(Host: Host).Email.Test;

            Test.SendCommand.Execute(null);
            Assert.That(Test.IsSending, Is.True, "the form says so at once; the form's thread never waits for the send");
            Assert.That(Test.SendCommand.CanExecute(null), Is.False);
            Release.Set();
            Assert.That(Posted.TryTake(out Action? End, TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(Test.IsSending, Is.True, "nothing of the form changes on the worker thread");
            End!();
            Assert.That(SendThread, Is.Not.Null.And.Not.EqualTo(FormThread));
            Assert.That(Test.IsSending, Is.False);
            Assert.That(Test.HasResult, Is.True);
            Assert.That(Test.ResultIsError, Is.False, Test.ResultText);

            //a form without a host is a host's mistake
            Assert.That(() => Settings().Email.Test.Start(), Throws.InvalidOperationException);
        }

        [Test]
        public void AfterATest_NoObjectOfTheFormsHoldsTheSecret()
        {
            string Secret = "SECRET-cnry-" + Guid.NewGuid().ToString("N");
            string SecretFile_ = Path.Join(Folder, "..", "CampertronPagesSecret-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(SecretFile_, Secret);
            try
            {
                Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: u\nsmtpPasswordFile: " + Path.GetFullPath(SecretFile_) +
                    "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
                string TokenFile = Path.GetFullPath(SecretFile_);
                Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: pagesTopic0123456789abcd\ntokenFile: " + TokenFile + "\n");
                var Handler = new OkHandler();
                NtfyPublisher.CreateHandler = () => Handler;
                SettingsViewModel All = Settings();
                foreach (TestNotificationForm Test in new[] { All.Email.Test, All.Ntfy.Test })
                {
                    Assert.That(Test.TryBegin(), Is.True);
                    Test.End(Test.Send(() => Now, CancellationToken.None));
                    Assert.That(Test.ResultIsError, Is.False, Test.ResultText);
                }
                Assert.That(SentWith, Is.EqualTo(new[] { Secret }), "the email signed in with the file's password");
                Assert.That(Handler.Authorization, Is.EqualTo("Bearer " + Secret));
                List<string> Strings = ReachableStrings(All);
                Assert.That(Strings.Count, Is.GreaterThan(100), "the walk saw the forms");
                Assert.That(Strings.Where(S => S.Contains(Secret, StringComparison.Ordinal)), Is.Empty, "no form, field, session, result or command holds the secret");
            }
            finally
            {
                File.Delete(SecretFile_);
            }
        }

        private sealed class OkHandler : HttpMessageHandler
        {
            internal string? Authorization;
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Authorization = Request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"x1\"}") };
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        // every string an object holds, through its fields, collections and delegates (the object graph), once each
        internal static List<string> ReachableStrings(object Root)
        {
            var Seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var Found = new List<string>();
            var Pending = new Stack<object>();
            Pending.Push(Root);
            while (Pending.Count > 0)
            {
                object Next = Pending.Pop();
                if (!Seen.Add(Next))
                {
                    continue;
                }
                switch (Next)
                {
                    case string Text:
                        Found.Add(Text);
                        continue;
                    case char[] Chars:
                        Found.Add(new string(Chars));
                        continue;
                    case Type or MemberInfo or Assembly or Module or Thread or System.Globalization.CultureInfo:
                        continue;
                    case Array Items:
                        foreach (object? Item in Items)
                        {
                            if (Item != null && !Item.GetType().IsPrimitive)
                            {
                                Pending.Push(Item);
                            }
                        }
                        continue;
                }
                for (Type? T = Next.GetType(); T != null && T != typeof(object); T = T.BaseType)
                {
                    foreach (FieldInfo Field in T.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (Field.FieldType.IsPrimitive || Field.FieldType.IsPointer || Field.FieldType.IsByRefLike)
                        {
                            continue;
                        }
                        if (Field.GetValue(Next) is object Value)
                        {
                            Pending.Push(Value);
                        }
                    }
                }
            }
            return Found;
        }

        // ---- the random topic (D86) ----

        [Test]
        public void AGeneratedTopic_IsAnUnsavedChange_ThatRevertUndoesAndSaveWrites_AndTheTokenIsUntouched()
        {
            const string Original = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: myOldTopic0123456789abcdef\ntokenFile: /run/secrets/ntfy_token\n";
            Write(Ntfy, Original);
            NtfySettingsViewModel Form = Settings().Ntfy;
            Assert.That(Form.GenerateTopicLabel, Is.EqualTo("Generate a random topic"));
            Assert.That(Form.GenerateTopicHelp, Does.Contain("cryptographic random number generator").And.Contain("anyone who knows the topic can read its notifications")
                .And.Contain("an access token, where the server uses one, protects them better").And.Contain("Revert brings back the previous topic").And.Contain("No server is asked"));

            Form.GenerateTopicCommand.Execute(null);
            string Generated = Form.Topic.Text;
            Assert.That(Generated, Does.Match("^campertron-[a-z0-9]{26}$"));
            Assert.That(Form.Topic.IsChanged, Is.True);
            Assert.That(Form.IsDirty, Is.True);
            Assert.That(Form.StatusText, Is.EqualTo(NtfySettingsViewModel.TopicGenerated));
            Assert.That(Form.TokenFile.Text, Is.EqualTo("/run/secrets/ntfy_token"), "the token's file is not touched");
            Assert.That(Text(Ntfy), Is.EqualTo(Original), "nothing written");

            Form.Revert();
            Assert.That(Form.Topic.Text, Is.EqualTo("myOldTopic0123456789abcdef"));

            Form.GenerateTopicCommand.Execute(null);
            Generated = Form.Topic.Text;
            Form.Save();
            Assert.That(Form.StatusIsError, Is.False, Form.StatusText);
            Assert.That(Text(Ntfy), Is.EqualTo(Original.Replace("myOldTopic0123456789abcdef", Generated)), "only the topic's line changed");
        }

        // ---- the SMTP password for a UI that never shows it (D82) ----

        [Test]
        public void ThePasswordsState_IsSaidInWords_AndRemoveIsClearingTheBox()
        {
            Write(Email, EmailText);
            EmailSettingsViewModel Form = Settings().Email;
            Assert.That(Form.PasswordState, Is.EqualTo("A plain-text password is set in Email.yaml. It is never shown here."));
            Assert.That(Form.CanRemovePassword, Is.True);
            Form.SmtpPassword.Text = "another-cnry";
            Assert.That(Form.PasswordState, Is.EqualTo("A new plain-text password was entered: Save writes it into Email.yaml. It is never shown here."));
            Form.RemovePasswordCommand.Execute(null);
            Assert.That(Form.SmtpPassword.Text, Is.Empty);
            Assert.That(Form.PasswordState, Is.EqualTo("The plain-text password will be removed from Email.yaml when you save."));
            Assert.That(Form.PreviewSummary, Is.EqualTo("Changes: smtpPassword (hidden)"));
            Assert.That(Form.PreviewText, Does.Not.Contain("PAGES-cnry-secret").And.Not.Contain("another-cnry"));
            Form.Save();
            Assert.That(Text(Email), Does.Contain("smtpPassword: ''").Or.Contain("smtpPassword: \"\"").Or.Contain("smtpPassword:\n"));
            Assert.That(Settings().Email.PasswordState, Is.EqualTo("No plain-text password is set in Email.yaml."));
            Assert.That(Settings().Email.CanRemovePassword, Is.False);
        }

        [Test]
        public void TheWordsThatTheTestChanged_SayWhenASecretIsRead()
        {
            EmailSettingsViewModel Form = Settings().Email;
            Assert.That(Form.PasswordSources, Does.EndWith("This window reads the password from its source only to send a test email when you ask for one, and never shows it or copies it into Email.yaml."));
            Assert.That(Form.SmtpPasswordFile.Hint, Does.Contain("this window opens it only to send a test email when you ask for one, and never shows the password"));
            Assert.That(Settings().Ntfy.TokenFile.Hint, Does.Contain("this window reads the token only to send a test notification when you ask for one"));
            Assert.That(Settings().Ntfy.ModeHelp, Does.StartWith("The console's Search, HtmlFile and the desktop GUI's Results tab need no notification provider."));
            Assert.That(Form.Test.Explanation, Does.Contain("The password is read only when you press the button, for this one email; it is never shown, kept or written.")
                .And.Contain("records nothing in Camp.History and does not start, stop or change the monitoring"));
        }

        [Test]
        public void AReload_WithUnsavedChanges_IsRefusedWithWords_AndAGenerationCountsEveryRefill()
        {
            Write(Email, EmailText);
            EmailSettingsViewModel Form = Settings().Email;
            int Before = Form.Generation;
            Form.SmtpServer.Text = "smtp2.example.invalid";
            Assert.That(Form.Generation, Is.EqualTo(Before), "a user's change is no refill");
            Form.ReloadUnlessChanged();
            Assert.That(Form.StatusText, Is.EqualTo(SettingsFileViewModel.ReloadRefused));
            Assert.That(Form.SmtpServer.Text, Is.EqualTo("smtp2.example.invalid"));
            Form.Revert();
            Assert.That(Form.Generation, Is.GreaterThan(Before));
            int AfterRevert = Form.Generation;
            Form.ReloadUnlessChanged();
            Assert.That(Form.StatusText, Is.EqualTo("Reloaded Email.yaml."));
            Assert.That(Form.Generation, Is.GreaterThan(AfterRevert));
        }
    }

    internal static class WebSettingsTestExtensions
    {
        // the validation's diagnostics of the form's file as edited (the session's notices)
        internal static IReadOnlyList<ConfigEditNotice> Validation(this WebSettingsViewModel Web) =>
            ((ConfigEditSession<WebConfig>)typeof(SettingsFileViewModel<WebConfig>).GetProperty("Session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Web)!).Notices;
    }
}
