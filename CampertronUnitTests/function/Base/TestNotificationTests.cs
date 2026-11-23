using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The test email and the test notification of D86 and D88 (v0.3.0's PR 5; business-rules-settings §32): one message with
    // the saved Email.yaml or Ntfy.yaml, through the monitoring's loader, validation, secret source and transport. The SMTP
    // password and ntfy's token are read only when the test is sent, by the precedence of D66 and D67 with no fallback, and
    // their references end with the send. A failure is fixed words: never a secret, a path, an address or a server's text.
    // Nothing is searched, written, recorded in Camp.History or locked. The email's transport is CampsiteConfig's send seam
    // (and once the real SmtpClient against a loopback stand-in); ntfy's is NtfyPublisher's handler seam. Unique canaries are
    // searched for in every result.
    public class TestNotificationTests : ConfigEditTestBase
    {
        private string Secrets = string.Empty;
        private string Password = string.Empty;
        private string Token = string.Empty;
        private string Topic = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);
        private int EnvironmentReads;
        // what the transport got, read while the send runs (the message is disposed when the send ends)
        private sealed record Mail(MailMessage Message, string Subject, string From, string[] To, int Attachments, string[] Views, EmailConfig Settings, string? Password);
        private readonly List<Mail> Sent = new();
        private static Mail Snapshot(MailMessage M, EmailConfig S, string? P) =>
            new Mail(M, M.Subject, M.From?.Address ?? "", M.To.Select(A => A.Address).ToArray(), M.Attachments.Count, M.AlternateViews.Select(V => V.ContentType.MediaType).ToArray(), S, P);

        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");

        [SetUp]
        public void Seams()
        {
            Secrets = Path.Join(Path.GetTempPath(), "CampertronTestNotificationSecrets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Secrets);
            Password = "Pw-cnry-" + Guid.NewGuid().ToString("N");
            Token = "tk_cnry" + Guid.NewGuid().ToString("N");
            Topic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            Environment_.Clear();
            EnvironmentReads = 0;
            Sent.Clear();
            SmtpPassword.GetEnvironment = Name => { EnvironmentReads++; return Environment_.TryGetValue(Name, out string? Value) ? Value : null; };
            NtfyToken.GetEnvironment = Name => { EnvironmentReads++; return Environment_.TryGetValue(Name, out string? Value) ? Value : null; };
            //the email's transport seam, as ResultsEmailDeliveryTests use it: the password the send signs in with is the one
            //SmtpCredential gives the SmtpClient
            CampsiteConfig.SendResultsEmail = (Settings, Message) => Sent.Add(Snapshot(Message, Settings, CampsiteConfig.SmtpCredential(Settings).Password));
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            if (Directory.Exists(Secrets))
            {
                Directory.Delete(Secrets, true);
            }
        }

        private string EmailYaml(string? Extra = null) =>
            "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: test-user\nsendFromAddress: from@example.invalid\n" +
            "sendToAddressList:\n- one@example.invalid\n- two@example.invalid\n" + (Extra ?? string.Empty);

        private string PasswordFile(string Content)
        {
            string Path_ = Path.Join(Secrets, "smtp_password");
            File.WriteAllText(Path_, Content);
            return Path_;
        }

        private Dictionary<string, string> Fingerprint() =>
            Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories).ToDictionary(P => P, P => File.Exists(P) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P))) : "dir");

        private void AssertNoValue(string Text)
        {
            Assert.That(Text, Does.Not.Contain(Password).And.Not.Contain(Token).And.Not.Contain(Topic).And.Not.Contain(Secrets).And.Not.Contain(Folder)
                .And.Not.Contain("example.invalid").And.Not.Contain("CANARY"));
        }

        private TestNotificationResult SendEmail() => TestNotification.SendEmail(Folder, () => new DateTime(2026, 11, 23, 14, 3, 0));

        // ---- email ----

        [Test]
        public void ATestEmail_GoesToTheSavedRecipients_ThroughTheResultsEmailsTransport_AndSaysSo()
        {
            Write(Email, EmailYaml("smtpPassword: " + Password + "\n"));
            Dictionary<string, string> Before = Fingerprint();

            TestNotificationResult Result = SendEmail();

            Assert.That(Result.Sent, Is.True, Result.Text);
            Assert.That(Result.Text, Is.EqualTo("Info: the SMTP server accepted the test email for 2 recipients at 14:03. If it does not arrive, look in the spam folder, then check the recipients in Email.yaml."));
            Assert.That(Result.Severity, Is.EqualTo("Info"));
            Assert.That(Sent, Has.Count.EqualTo(1));
            Mail Message = Sent[0];
            Assert.That(Message.Subject, Is.EqualTo("Campertron test email"));
            Assert.That(Message.From, Is.EqualTo("from@example.invalid"));
            Assert.That(Message.To, Is.EqualTo(new[] { "one@example.invalid", "two@example.invalid" }));
            Assert.That(Message.Attachments, Is.Zero);
            Assert.That(Message.Views, Is.EqualTo(new[] { "text/plain", "text/html" }), "the results email's form: plain text first");
            Assert.That(Sent[0].Password, Is.EqualTo(Password), "the plain-text password when nothing else is set (D66's third source)");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing written: no file, no Camp.History, no monitoring lock");
            Assert.That(Directory.Exists(Path.Join(Folder, "Camp.History")), Is.False);
            Assert.That(File.Exists(Path.Join(Folder, "Campertron.monitoring.lock")), Is.False);
            AssertNoValue(Result.Text);
        }

        [Test]
        public void TheSmtpPassword_ComesFromTheSourceEmailModeUses_InItsOrder_ReadOnlyWhenTheTestIsSent()
        {
            //the file first, whatever the variable and smtpPassword say
            string FilePassword = Password + "-file";
            Environment_["CAMPERTRON_EMAIL_PASSWORD"] = Password + "-variable";
            Write(Email, EmailYaml("smtpPasswordFile: " + PasswordFile(FilePassword + "\n") + "\nsmtpPassword: " + Password + "-plain\n"));
            Assert.That(EnvironmentReads, Is.Zero);
            Assert.That(SendEmail().Sent, Is.True);
            Assert.That(Sent.Last().Password, Is.EqualTo(FilePassword), "one terminal line break removed (§27)");

            //then the variable
            Write(Email, EmailYaml("smtpPassword: " + Password + "-plain\n"));
            Assert.That(SendEmail().Sent, Is.True);
            Assert.That(Sent.Last().Password, Is.EqualTo(Password + "-variable"));
            Assert.That(EnvironmentReads, Is.GreaterThan(0), "the variable was read for the send");

            //then smtpPassword
            Environment_.Clear();
            Assert.That(SendEmail().Sent, Is.True);
            Assert.That(Sent.Last().Password, Is.EqualTo(Password + "-plain"));
        }

        [Test]
        public void ASourceThatCantBeUsed_IsSaid_AndNeverFallsBackToAnotherOne()
        {
            Environment_["CAMPERTRON_EMAIL_PASSWORD"] = Password + "-variable";
            Write(Email, EmailYaml("smtpPasswordFile: " + Path.Join(Secrets, "missing") + "\nsmtpPassword: " + Password + "\n"));
            TestNotificationResult Result = SendEmail();
            Assert.That(Result.Sent, Is.False);
            Assert.That(Result.Text, Does.StartWith("Error: the test email can't be sent: the SMTP password can't be used: the file that smtpPasswordFile in Email.yaml names does not exist"));
            Assert.That(Result.Text, Does.EndWith("Campertron never uses another password source when the one configured can't be used (the order is smtpPasswordFile, then CAMPERTRON_EMAIL_PASSWORD, then smtpPassword)."));
            Assert.That(Sent, Is.Empty, "never sent with the variable or smtpPassword instead");
            AssertNoValue(Result.Text);

            //a variable that is set but empty is its own problem (Unix), never smtpPassword
            if (!OperatingSystem.IsWindows())
            {
                Environment_["CAMPERTRON_EMAIL_PASSWORD"] = string.Empty;
                Write(Email, EmailYaml("smtpPassword: " + Password + "\n"));
                Result = SendEmail();
                Assert.That(Result.Sent, Is.False);
                Assert.That(Sent, Is.Empty);
                AssertNoValue(Result.Text);
            }
        }

        [Test]
        public void TheSecret_IsDroppedWhenTheSendEnds()
        {
            Write(Email, EmailYaml("smtpPasswordFile: " + PasswordFile(Password) + "\n"));
            Assert.That(SendEmail().Sent, Is.True);
            EmailConfig Used = Sent[0].Settings;
            Assert.That(Used.ResolvedPassword, Is.Null, "the resolved password's reference ends with the send");
            Assert.That(Used.SmtpPassword, Is.Null);
            Assert.That(() => Sent[0].Message.AlternateViews.Count, Throws.TypeOf<ObjectDisposedException>(), "the message is released with the send");

            //and a failed send too
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sent.Add(Snapshot(Message, Settings, null)); throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "550 CANARY " + Password); };
            TestNotificationResult Failed = SendEmail();
            Assert.That(Failed.Sent, Is.False);
            Assert.That(Sent.Last().Settings.ResolvedPassword, Is.Null);
            AssertNoValue(Failed.Text);
        }

        [Test]
        public void SavedSettingsTheStartWouldRefuse_AreSaid_AndNothingIsSent()
        {
            Write(Email, "smtpServer: ''\nsmtpPort: 587\nsmtpUsername: u\nsmtpPassword: " + Password + "\nsendFromAddress: from@example.invalid\nsendToAddressList:\n- one@example.invalid\n");
            TestNotificationResult Result = SendEmail();
            Assert.That(Result.Sent, Is.False);
            Assert.That(Result.Text, Does.StartWith("Error: the test email can't be sent: ").And.Contain("smtpServer").And.EndWith("Fix and save Email.yaml first."));
            Assert.That(Sent, Is.Empty);
            Assert.That(EnvironmentReads, Is.Zero, "no secret source is read for settings the start refuses");
            AssertNoValue(Result.Text);
        }

        [Test]
        public void AMissingOrUnreadableEmailYaml_IsSaid_AndNothingIsCreated()
        {
            TestNotificationResult Missing = SendEmail();
            Assert.That(Missing.Text, Is.EqualTo("Error: the test email can't be sent: Email.yaml does not exist yet. Create and save it first."));
            Assert.That(File.Exists(Email), Is.False, "the sample is never created by a test");

            Write(Email, EmailYaml("smtpPassword: " + Password + "\nunknownKey: CANARY\n"));
            TestNotificationResult Unreadable = SendEmail();
            Assert.That(Unreadable.Text, Is.EqualTo("Error: the test email can't be sent: Email.yaml can't be read. Fix it first: its editor shows what is wrong."));
            Assert.That(Sent, Is.Empty);
        }

        [TestCase(SmtpStatusCode.MustIssueStartTlsFirst, "the SMTP server refused the sign-in or wants a secure connection first (SMTP 530).")]
        [TestCase((SmtpStatusCode)535, "the SMTP server refused the sign-in or wants a secure connection first (SMTP 535).")]
        [TestCase(SmtpStatusCode.MailboxUnavailable, "the SMTP server refused an address of the email (SMTP 550).")]
        [TestCase(SmtpStatusCode.MailboxBusy, "the SMTP server did not take the email for now (SMTP 450).")]
        [TestCase(SmtpStatusCode.TransactionFailed, "the SMTP server refused the email (SMTP 554).")]
        [TestCase(SmtpStatusCode.GeneralFailure, "the SMTP server could not be reached, or did not answer in time.")]
        public void AFailedSend_IsSaidInFixedWords_WithoutTheServersText(SmtpStatusCode Code, string Expected)
        {
            Write(Email, EmailYaml("smtpPassword: " + Password + "\n"));
            CampsiteConfig.SendResultsEmail = (Settings, Message) => throw new SmtpException(Code, "5.7.8 CANARY server text one@example.invalid " + Password);
            TestNotificationResult Result = SendEmail();
            Assert.That(Result.Sent, Is.False);
            Assert.That(Result.Text, Does.StartWith("Error: the test email could not be sent: " + Expected));
            AssertNoValue(Result.Text);
        }

        [Test]
        public void ANetworkOrTlsFailure_IsSaidByItsKind()
        {
            Write(Email, EmailYaml("smtpPassword: " + Password + "\n"));
            var Cases = new (Exception Failure, string Expected)[]
            {
                (new SmtpException("CANARY", new SocketException((int)SocketError.HostNotFound)), "the SMTP server's name could not be found (DNS)."),
                (new SmtpException("CANARY", new SocketException((int)SocketError.ConnectionRefused)), "the SMTP server refused the connection."),
                (new SmtpException("CANARY", new SocketException((int)SocketError.TimedOut)), "the SMTP server did not answer in time."),
                (new SmtpException("CANARY", new AuthenticationException("CANARY certificate")), "the encrypted connection to the SMTP server failed"),
                (new InvalidOperationException("CANARY"), "an unexpected problem stopped the send (InvalidOperationException)."),
            };
            foreach ((Exception Failure, string Expected) in Cases)
            {
                CampsiteConfig.SendResultsEmail = (Settings, Message) => throw Failure;
                TestNotificationResult Result = SendEmail();
                Assert.That(Result.Text, Does.StartWith("Error: the test email could not be sent: " + Expected), Failure.GetType().Name);
                AssertNoValue(Result.Text);
            }
        }

        [Test]
        public void TheRealSmtpClient_AgainstALoopbackServerThatRefuses_GivesFixedWords()
        {
            //the results email's own transport (SmtpClient, EnableSsl), as the monitoring sends: a stand-in on the loopback
            //address that answers the greeting with a refusal and words of its own, which the result never repeats
            CampsiteConfig.SendResultsEmail = OriginalSend;
            using var Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();
            int Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
            Task Serving = Task.Run(() =>
            {
                using TcpClient Client = Listener.AcceptTcpClient();
                using NetworkStream Stream = Client.GetStream();
                byte[] Greeting = Encoding.ASCII.GetBytes("554 5.7.1 CANARY go away " + Password + "\r\n");
                Stream.Write(Greeting);
                Stream.Flush();
                Thread.Sleep(200);
            });
            Write(Email, "smtpServer: 127.0.0.1\nsmtpPort: " + Port + "\nsmtpUsername: u\nsmtpPassword: " + Password + "\nsendFromAddress: from@example.invalid\nsendToAddressList:\n- one@example.invalid\n");
            TestNotificationResult Result = TestNotification.SendEmail(Folder, () => DateTime.Now);
            Assert.That(Serving.Wait(TimeSpan.FromSeconds(30)), Is.True);
            Assert.That(Result.Sent, Is.False);
            Assert.That(Result.Text, Does.StartWith("Error: the test email could not be sent: the SMTP server"));
            Assert.That(Result.Text, Does.Not.Contain("go away").And.Not.Contain("CANARY").And.Not.Contain(Password));
        }

        // ---- ntfy ----

        private sealed class Recording : HttpMessageHandler
        {
            internal Func<HttpRequestMessage, HttpResponseMessage> Answer = R => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc123\",\"event\":\"message\"}", Encoding.UTF8, "application/json") };
            internal readonly List<(Uri? Uri, string? Authorization, byte[] Body)> Seen = new();
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Seen.Add((Request.RequestUri, Request.Headers.Authorization?.ToString(), Request.Content?.ReadAsByteArrayAsync(Cancel).GetAwaiter().GetResult() ?? Array.Empty<byte>()));
                return Answer(Request);
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        private Recording UseHandler()
        {
            var Handler = new Recording();
            NtfyPublisher.CreateHandler = () => Handler;
            return Handler;
        }

        private TestNotificationResult SendNtfy() => TestNotification.SendNtfy(Folder, () => new DateTime(2026, 11, 23, 14, 3, 0));

        [Test]
        public void ATestNotification_IsOnePublishToTheSavedServerAndTopic_WhetherEnabledIsOnOrOff()
        {
            Recording Handler = UseHandler();
            Write(Ntfy, "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\npriority: high\ntags:\n- tent\n");
            Dictionary<string, string> Before = Fingerprint();

            TestNotificationResult Result = SendNtfy();

            Assert.That(Result.Sent, Is.True, Result.Text);
            Assert.That(Result.Text, Is.EqualTo("Info: the ntfy server accepted the test notification at 14:03. It shows on every device subscribed to the topic. If it does not arrive, check the topic in the ntfy app."));
            Assert.That(Handler.Seen, Has.Count.EqualTo(1));
            Assert.That(Handler.Seen[0].Uri!.ToString(), Is.EqualTo("https://ntfy.example.invalid/"), "the server's root, never a topic URL");
            Assert.That(Handler.Seen[0].Authorization, Is.Null, "no token configured");
            using JsonDocument Json = JsonDocument.Parse(Handler.Seen[0].Body);
            Assert.That(Json.RootElement.GetProperty("topic").GetString(), Is.EqualTo(Topic));
            Assert.That(Json.RootElement.GetProperty("title").GetString(), Is.EqualTo("Campertron test notification"));
            Assert.That(Json.RootElement.GetProperty("message").GetString(), Does.StartWith("This is a test notification from Campertron"));
            Assert.That(Json.RootElement.GetProperty("priority").GetInt32(), Is.EqualTo(4));
            Assert.That(Json.RootElement.TryGetProperty("click", out _), Is.False, "no campground to open");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing written");
            AssertNoValue(Result.Text);
        }

        [Test]
        public void TheToken_ComesFromTheTokenFile_ThenTheVariable_ThenNone_ReadOnlyWhenTheTestIsSent_AndIsDroppedAfterwards()
        {
            Recording Handler = UseHandler();
            string TokenPath = Path.Join(Secrets, "ntfy_token");
            File.WriteAllText(TokenPath, Token + "-file\n");
            Environment_["CAMPERTRON_NTFY_TOKEN"] = Token + "-variable";
            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: " + TokenPath + "\n");
            Assert.That(EnvironmentReads, Is.Zero);
            Assert.That(SendNtfy().Sent, Is.True);
            Assert.That(Handler.Seen.Last().Authorization, Is.EqualTo("Bearer " + Token + "-file"));

            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            Assert.That(SendNtfy().Sent, Is.True);
            Assert.That(Handler.Seen.Last().Authorization, Is.EqualTo("Bearer " + Token + "-variable"));
            Assert.That(EnvironmentReads, Is.GreaterThan(0));

            Environment_.Clear();
            Assert.That(SendNtfy().Sent, Is.True);
            Assert.That(Handler.Seen.Last().Authorization, Is.Null);

            //a token file that is missing is said; never the variable, never without a token
            Environment_["CAMPERTRON_NTFY_TOKEN"] = Token + "-variable";
            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: " + Path.Join(Secrets, "missing") + "\n");
            int Publishes = Handler.Seen.Count;
            TestNotificationResult Missing = SendNtfy();
            Assert.That(Missing.Sent, Is.False);
            Assert.That(Missing.Text, Does.StartWith("Error: the test notification can't be sent: "));
            Assert.That(Handler.Seen, Has.Count.EqualTo(Publishes), "nothing published");
            AssertNoValue(Missing.Text);
        }

        [Test]
        public void AFailedPublish_IsSaidInTheMonitoringsWords_WithoutTheServersAnswer()
        {
            Recording Handler = UseHandler();
            Handler.Answer = R => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("CANARY server body " + Token) };
            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            TestNotificationResult Result = SendNtfy();
            Assert.That(Result.Sent, Is.False);
            Assert.That(Result.Text, Is.EqualTo("Error: the test notification could not be sent: the ntfy server wants an access token, or refused the one sent (HTTP 401). Check the token in tokenFile or CAMPERTRON_NTFY_TOKEN, and that it may publish to the topic."));
            AssertNoValue(Result.Text);
        }

        [Test]
        public void SavedNtfySettingsTheStartWouldRefuse_OrNoNtfyYaml_AreSaid_AndNothingIsPublished()
        {
            Recording Handler = UseHandler();
            TestNotificationResult Missing = SendNtfy();
            Assert.That(Missing.Text, Is.EqualTo("Error: the test notification can't be sent: Ntfy.yaml does not exist yet. Enter the server and the topic, and save it first."));
            Assert.That(File.Exists(Ntfy), Is.False);

            Write(Ntfy, "enabled: false\nserver: ftp://ntfy.example.invalid/path\ntopic: " + Topic + "\n");
            TestNotificationResult Refused = SendNtfy();
            Assert.That(Refused.Sent, Is.False);
            Assert.That(Refused.Text, Does.Contain("server").And.EndWith("Fix and save Ntfy.yaml first."));
            Assert.That(Handler.Seen, Is.Empty);
            AssertNoValue(Refused.Text);
        }

        [Test]
        public void TheValidationOfATest_IsTheMonitoringStartsOwn_ExceptThatNtfyNeedNotBeEnabled()
        {
            EmailConfig Empty = Yaml.LoadSettingsText<EmailConfig>("smtpPort: 587\n");
            Assert.That(ConfigValidation.TestEmailErrors(Empty).Select(D => D.Code), Is.EquivalentTo(
                Assert.Throws<ConfigValidationException>(() => ConfigValidation.ThrowIfEmailOutputRefused(Empty))!.Diagnostics.Select(D => D.Code)));
            NtfyConfig Off = Yaml.LoadSettingsText<NtfyConfig>("enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n");
            Assert.That(ConfigValidation.TestNtfyErrors(Off), Is.Empty, "enabled: false is no reason to refuse a test");
            Assert.Throws<ConfigValidationException>(() => ConfigValidation.ThrowIfNtfyOutputRefused(Off, true), "while outputTo: Ntfy's start would refuse it");
        }

        // ---- what a test never touches ----

        [Test]
        public void ATest_ReachesNoMonitoringHistoryLockOrSearch()
        {
            string[] Forbidden =
            {
                "MonitoringService", "MonitoringLock", "MonitoringConsole", "Notifications::Deliver", "Load::Record", "Load::HasUnreported", "Load::Report",
                "CampsiteSearch", "AvailabilityApi", "ConfigFileReplace", "File::Write", "File::Create", "Directory::Create", "File::Delete", "Yaml::EmailConfigGetConfig",
                "ConsoleUi", "System.Console",
            };
            var Called = new List<string>();
            foreach (MethodInfo Method in typeof(TestNotification).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Concat(typeof(CampsiteConfig).GetMethods(BindingFlags.Static | BindingFlags.NonPublic).Where(M => M.Name == "CreateTestEmail")))
            {
                Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => (M.DeclaringType?.FullName ?? "") + "::" + M.Name));
            }
            Assert.That(Called, Is.Not.Empty);
            Assert.That(Called.Where(C => Forbidden.Any(F => C.Contains(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.SmtpPassword::ResolveOnce").And.Contain("CampertronLibrary.function.Base.NtfyToken::ResolveOnce"),
                "the monitoring start's own resolvers, not a second implementation");
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.CampsiteConfig::get_SendResultsEmail").And.Contain("CampertronLibrary.function.Base.NtfyPublisher::Publish"),
                "the monitoring's own transports");
        }
    }
}
