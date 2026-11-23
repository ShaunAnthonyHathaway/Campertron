using System.Net.Mail;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronWebTests
{
    // The monitoring page's words for why a monitoring stopped and why a start did not start it (v0.3.0's PR 8, D98;
    // WebMonitoringWording): fixed words of each kind, the providers' and the secrets' own (the test notification's for a
    // failed email, ntfy's, the password's and the token's sources'), never an exception's message, a server's answer, a
    // recipient, a secret, an address or a path; a failed send keeps D76's rule in words (not recorded, no automatic restart).
    public class MonitoringWordingTests
    {
        private const string Secret = "s3cret-detail someone@example.invalid /home/someone/.config/password Bearer tk_123";

        private static void AssertSafe(IReadOnlyList<string> Lines)
        {
            string All = string.Join("\n", Lines);
            Assert.That(All, Does.Not.Contain("s3cret").And.Not.Contain("someone@").And.Not.Contain("/home/").And.Not.Contain("tk_123").And.Not.Contain("   at "));
            Assert.That(Lines, Has.All.Not.Empty);
        }

        [Test]
        public void AFailedEmail_IsSaidAsTheTestEmailSaysIt_WithD76sRule()
        {
            IReadOnlyList<string> Lines = WebMonitoringWording.Failure(new SmtpException((SmtpStatusCode)535, Secret), false, null);
            AssertSafe(Lines);
            Assert.That(Lines[0], Is.EqualTo("the email could not be sent: the SMTP server refused the sign-in or wants a secure connection first (SMTP 535)."));
            Assert.That(Lines[1], Is.EqualTo("Check the server, port, user name and password in Email.yaml."));
            Assert.That(Lines, Does.Contain("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again."));
            Assert.That(Lines.Last(), Does.StartWith("The monitoring does not start again by itself"));
            //wrapped as Parallel.ForEach or a send wraps it
            IReadOnlyList<string> Wrapped = WebMonitoringWording.Failure(new AggregateException(new InvalidOperationException(Secret, new SmtpException(SmtpStatusCode.MailboxUnavailable, Secret))), false, null);
            AssertSafe(Wrapped);
            Assert.That(Wrapped[0], Is.EqualTo("the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(Wrapped[1], Is.EqualTo("Check sendFromAddress and sendToAddressList in Email.yaml."));
        }

        [Test]
        public void AFailedNtfyNotification_IsSaidInNtfysOwnWords()
        {
            IReadOnlyList<string> Lines = WebMonitoringWording.Failure(new NtfyException(NtfyFailure.Unauthorized, 401), false, null);
            AssertSafe(Lines);
            Assert.That(Lines[0], Is.EqualTo("the ntfy notification could not be sent: the ntfy server wants an access token, or refused the one sent (HTTP 401)."));
            Assert.That(Lines[1], Is.EqualTo("Check the token in tokenFile or CAMPERTRON_NTFY_TOKEN, and that it may publish to the topic."));
        }

        [Test]
        public void BothProvidersFailing_SaysEachOnesWords()
        {
            var Both = new NotificationException(new[] { (NotificationProvider.Email, (Exception)new SmtpException(SmtpStatusCode.ServiceNotAvailable, Secret)),
                (NotificationProvider.Ntfy, (Exception)new NtfyException(NtfyFailure.ServerError, 503)) });
            IReadOnlyList<string> Lines = WebMonitoringWording.Failure(Both, false, null);
            AssertSafe(Lines);
            Assert.That(Lines[0], Is.EqualTo("the notifications could not be sent: both the email and the ntfy notification failed."));
            Assert.That(Lines[1], Is.EqualTo("Email: the email could not be sent: the SMTP server did not take the email for now (SMTP 421)."));
            Assert.That(Lines[2], Is.EqualTo("ntfy: the ntfy notification could not be sent: the ntfy server failed (HTTP 503)."));
            Assert.That(Lines, Does.Contain("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again."));
        }

        [Test]
        public void ASecretsSourceThatCantBeUsedAtASend_IsSaidInItsSourcesWords()
        {
            IReadOnlyList<string> Password = WebMonitoringWording.Failure(new SmtpPasswordException(SmtpPasswordResult.Failed(SmtpPasswordSource.File, SmtpPasswordProblem.FileMissing)), false, null);
            AssertSafe(Password);
            Assert.That(Password[0], Does.StartWith("the email could not be sent: the SMTP password can't be used: "));
            IReadOnlyList<string> Token = WebMonitoringWording.Failure(new NtfyTokenException(NtfyTokenResult.Failed(NtfyTokenSource.File, NtfyTokenProblem.FileMissing)), false, null);
            AssertSafe(Token);
            Assert.That(Token[0], Does.StartWith("the ntfy notification could not be sent: "));
        }

        [Test]
        public void ABlockAndAnUnexpectedProblem_AreSaidWithoutTheirDetails()
        {
            IReadOnlyList<string> Blocked = WebMonitoringWording.Failure(new MonitoringBlockedException("FIXTURE CAMPGROUND, December 2026"), true, null);
            AssertSafe(Blocked);
            Assert.That(string.Join(" ", Blocked), Does.Not.Contain("FIXTURE").And.Contain("five minutes"));
            IReadOnlyList<string> Unexpected = WebMonitoringWording.Failure(new InvalidOperationException(Secret), false, null);
            AssertSafe(Unexpected);
            Assert.That(Unexpected[0], Is.EqualTo("an unexpected problem stopped the monitoring (InvalidOperationException)."));
            Assert.That(Unexpected[1], Is.EqualTo("What a provider delivered before it is recorded in Camp.History; nothing else is."));
        }

        [Test]
        public void AStartThatThrew_SaysTheFileAndTheValidationsWords_NeverTheMessage()
        {
            Exception Noted(Exception Error, string File)
            {
                ConsoleUi.NoteFile(Error, "/home/someone/.local/share/CampertronConfig/" + File);
                return Error;
            }
            var Diagnostic = new ConfigDiagnostic(ConfigCode.InvalidChoice, ConfigSeverity.Error, ConfigFileKind.Email, "Email.yaml", "smtpPort", null, 0, 0, "smtpPort must be a whole number from 1 to 65535.");
            IReadOnlyList<string> Refused = WebMonitoringWording.StartFailed(Noted(new ConfigValidationException(Secret, new[] { Diagnostic }), "Email.yaml"), "email mode");
            AssertSafe(Refused);
            Assert.That(Refused[0], Is.EqualTo("email mode can't start: Email.yaml can't be used: smtpPort must be a whole number from 1 to 65535."));
            Assert.That(Refused.Last(), Is.EqualTo("Nothing was searched or sent."));

            var Yaml = new YamlDotNet.Core.YamlException(new YamlDotNet.Core.Mark(10, 3, 7), new YamlDotNet.Core.Mark(12, 3, 9), Secret);
            IReadOnlyList<string> Broken = WebMonitoringWording.StartFailed(Noted(new InvalidDataException(Secret, Yaml), "Trip.yaml"), "ntfy mode");
            AssertSafe(Broken);
            Assert.That(Broken[0], Is.EqualTo("ntfy mode can't start: Trip.yaml can't be read: it is not valid YAML (line 3, column 7)."));

            IReadOnlyList<string> Unreadable = WebMonitoringWording.StartFailed(Noted(new IOException(Secret), "General.yaml"), "email mode");
            AssertSafe(Unreadable);
            Assert.That(Unreadable[0], Is.EqualTo("email mode can't start: General.yaml can't be read or used (IOException)."));

            IReadOnlyList<string> Unexpected = WebMonitoringWording.StartFailed(new InvalidOperationException(Secret), "email mode");
            AssertSafe(Unexpected);
            Assert.That(Unexpected[0], Is.EqualTo("email mode can't start: an unexpected problem stopped the start (InvalidOperationException)."));
        }
    }
}
