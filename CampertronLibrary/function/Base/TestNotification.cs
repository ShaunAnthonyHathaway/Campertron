using System.Globalization;
using System.Net.Mail;
using System.Net.Mime;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace CampertronLibrary.function.Base
{
    // what a test notification did: sent, or why not, in fixed words (D86, D88)
    internal sealed class TestNotificationResult
    {
        internal TestNotificationResult(bool Sent, string Text)
        {
            this.Sent = Sent;
            this.Text = Text;
        }
        internal bool Sent { get; }
        // "Info: …" for a delivery, "Error: …" for every other end (the severity in words, D74)
        internal string Text { get; }
        internal string Severity => Sent ? "Info" : "Error";
    }

    // The test notification of decisions D86 and D88 (v0.3.0's PR 5; business-rules-settings §32): on the user's action in
    // the desktop GUI or the web UI, one test message to the settings as Email.yaml or Ntfy.yaml is saved, through the same
    // loader, the same validation, the same secret source and the same transport the unattended monitoring uses:
    //  - the file is read by the bounded loader and never created (a missing file is said, never written);
    //  - the settings are judged by the monitoring start's own check of the file (ConfigValidation's TestEmailErrors and
    //    TestNtfyErrors: ThrowIfEmailOutputRefused's and ThrowIfNtfyOutputRefused's errors; a test needs no outputTo and,
    //    for ntfy, not Enabled);
    //  - the SMTP password or ntfy's token is resolved now, by SmtpPassword.ResolveOnce and NtfyToken.ResolveOnce, the
    //    functions email and ntfy mode's start call, with their precedence and no fallback (D66, D67): the file the setting
    //    names, then the environment variable, then (for email) smtpPassword. It is read only here, for this one send, and
    //    its references are dropped when the send ends: never shown, logged, cached, put into a form or written;
    //  - the email goes through CampsiteConfig.SendResultsEmail (SmtpClient, as every results email) and the ntfy message
    //    through NtfyPublisher.Publish (a JSON POST to the server's root, no redirect, bearer token only).
    // A test searches nothing, writes no file, never touches Camp.History, the monitoring lock or a MonitoringService, and a
    // failure is a result, never an exception the caller has to catch. The words are fixed: never the password, the token,
    // the topic, a path, an address, or an answer of the SMTP or ntfy server (a server's text is not repeated).
    internal static class TestNotification
    {
        internal const string EmailSubject = "Campertron test email";
        internal const string NtfyTitle = "Campertron test notification";
        internal const string EmailBody = "This is a test email from Campertron, sent because you asked for one in its settings. Campertron sends its emails about campsite openings the same way. It is not about a campsite: nothing was searched, and nothing was recorded.";
        internal const string NtfyBody = "This is a test notification from Campertron, sent because you asked for one in its settings. Campertron sends its notifications about campsite openings the same way. It is not about a campsite: nothing was searched, and nothing was recorded.";

        // ---- email ----

        // Sends one test email with Email.yaml as it is saved in ConfigPath. Now is asked for the confirmation's time
        internal static TestNotificationResult SendEmail(string ConfigPath, Func<DateTime> Now)
        {
            string FilePath = Path.Join(ConfigPath, ConfigFiles.EmailFileName);
            if (!File.Exists(FilePath))
            {
                return Failed("the test email can't be sent: " + ConfigFiles.EmailFileName + " does not exist yet.", "Create and save it first.");
            }
            EmailConfig? Email = null;
            MailMessage? Message = null;
            try
            {
                try
                {
                    Email = Yaml.LoadSettingsFile<EmailConfig>(FilePath);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failed("the test email can't be sent: " + ConfigFiles.EmailFileName + " can't be read.", "Fix it first: its editor shows what is wrong.");
                }
                List<ConfigDiagnostic> Errors = ConfigValidation.TestEmailErrors(Email);
                if (Errors.Count > 0)
                {
                    return Failed("the test email can't be sent: " + string.Join(" ", Errors.Select(D => D.Message).Distinct()), "Fix and save " + ConfigFiles.EmailFileName + " first.");
                }
                //the password from its configured source, now, by email mode's own function (no fallback)
                SmtpPasswordResult Password = SmtpPassword.ResolveOnce(Email);
                if (!Password.Succeeded)
                {
                    List<string> Lines = SmtpPassword.Describe(Password);
                    return Failed("the test email can't be sent: " + Lines[0], Lines.Skip(1).ToArray());
                }
                int Recipients = Email.SendToAddressList?.Count ?? 0;
                Message = CampsiteConfig.CreateTestEmail(Email);
                try
                {
                    CampsiteConfig.SendResultsEmail(Email, Message);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failed("the test email could not be sent: " + DescribeSmtpFailure(ex), SmtpCheck(ex, Email));
                }
                return new TestNotificationResult(true, "Info: the SMTP server accepted the test email for " + Count(Recipients, "recipient", "recipients")
                    + " at " + Now().ToString("HH:mm", CultureInfo.InvariantCulture) + ". If it does not arrive, look in the spam folder, then check the recipients in "
                    + ConfigFiles.EmailFileName + ".");
            }
            finally
            {
                //the password's references end with the send: the message, the resolved password and the settings
                Message?.Dispose();
                if (Email != null)
                {
                    Email.ResolvedPassword = null;
                    Email.SmtpPassword = null;
                }
            }
        }

        // what went wrong with a send, in fixed words: the kind of failure and the SMTP status number, never the server's
        // text (it can repeat an address or anything the server chose to say)
        internal static string DescribeSmtpFailure(Exception Error)
        {
            List<Exception> Chain = new List<Exception>();
            for (Exception? Next = Error; Next != null; Next = Next.InnerException)
            {
                Chain.Add(Next);
            }
            if (Chain.OfType<SmtpPasswordException>().FirstOrDefault() is SmtpPasswordException Password)
            {
                return SmtpPassword.Describe(Password.Result)[0];
            }
            if (Chain.OfType<SocketException>().FirstOrDefault() is SocketException Socket)
            {
                return Socket.SocketErrorCode switch
                {
                    SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => "the SMTP server's name could not be found (DNS).",
                    SocketError.ConnectionRefused => "the SMTP server refused the connection.",
                    SocketError.TimedOut => "the SMTP server did not answer in time.",
                    _ => "the network failed while sending."
                };
            }
            if (Chain.Any(E => E is AuthenticationException))
            {
                return "the encrypted connection to the SMTP server failed (its certificate is not trusted, or it does not offer TLS on that port).";
            }
            //SmtpClient wraps the server's refusal in its own "Failure sending mail": the code that says most is the innermost
            //one that is not GeneralFailure
            if ((Chain.OfType<SmtpException>().LastOrDefault(E => E.StatusCode != SmtpStatusCode.GeneralFailure) ?? Chain.OfType<SmtpException>().FirstOrDefault()) is SmtpException Smtp)
            {
                int Code = (int)Smtp.StatusCode;
                string Number = " (SMTP " + Code.ToString(CultureInfo.InvariantCulture) + ")";
                if (Code is 530 or 534 or 535 or 538)
                {
                    return "the SMTP server refused the sign-in or wants a secure connection first" + Number + ".";
                }
                if (Code is 501 or 550 or 551 or 552 or 553 or 555)
                {
                    return "the SMTP server refused an address of the email" + Number + ".";
                }
                if (Code >= 400 && Code < 500)
                {
                    return "the SMTP server did not take the email for now" + Number + ".";
                }
                if (Code >= 500 && Code < 600)
                {
                    return "the SMTP server refused the email" + Number + ".";
                }
                return "the SMTP server could not be reached, or did not answer in time.";
            }
            return "an unexpected problem stopped the send (" + Error.GetType().Name + ").";
        }

        // what to check after a failed send
        private static string SmtpCheck(Exception Error, EmailConfig Email)
        {
            int? Code = null;
            for (Exception? Next = Error; Next != null; Next = Next.InnerException)
            {
                if (Next is SmtpException Smtp && Smtp.StatusCode != SmtpStatusCode.GeneralFailure)
                {
                    Code = (int)Smtp.StatusCode;
                }
            }
            if (Code is 501 or 550 or 551 or 552 or 553 or 555)
            {
                return "Check sendFromAddress and sendToAddressList in " + ConfigFiles.EmailFileName + ".";
            }
            return SmtpPassword.SendFailureHint(Email);
        }

        // ---- ntfy ----

        // Publishes one test notification with Ntfy.yaml as it is saved in ConfigPath, whether Enabled is on or off. Cancel
        // ends a publish that is waiting (the host's shutdown)
        internal static TestNotificationResult SendNtfy(string ConfigPath, Func<DateTime> Now, CancellationToken Cancel = default)
        {
            string FilePath = Path.Join(ConfigPath, ConfigFiles.NtfyFileName);
            if (!File.Exists(FilePath))
            {
                return Failed("the test notification can't be sent: " + ConfigFiles.NtfyFileName + " does not exist yet.", "Enter the server and the topic, and save it first.");
            }
            NtfyConfig? Ntfy = null;
            try
            {
                try
                {
                    Ntfy = Yaml.LoadSettingsFile<NtfyConfig>(FilePath);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Failed("the test notification can't be sent: " + ConfigFiles.NtfyFileName + " can't be read.", "Fix it first: its editor shows what is wrong.");
                }
                List<ConfigDiagnostic> Errors = ConfigValidation.TestNtfyErrors(Ntfy);
                if (Errors.Count > 0)
                {
                    return Failed("the test notification can't be sent: " + string.Join(" ", Errors.Select(D => D.Message).Distinct()), "Fix and save " + ConfigFiles.NtfyFileName + " first.");
                }
                //the token from its configured source, now, by ntfy mode's own function (no fallback, never without it)
                NtfyTokenResult Token = NtfyToken.ResolveOnce(Ntfy);
                if (!Token.Succeeded)
                {
                    List<string> Lines = NtfyToken.Describe(Token);
                    return Failed("the test notification can't be sent: " + Lines[0], Lines.Skip(1).ToArray());
                }
                try
                {
                    NtfyPublisher.Publish(Ntfy, new NtfyMessage(NtfyTitle, NtfyBody, null, 0), Cancel);
                }
                catch (NtfyException Failure)
                {
                    List<string> Lines = NtfyPublisher.Describe(Failure.Failure, Failure.Status, Failure.TimeoutSeconds, Failure.ExceptionType);
                    return Failed("the test notification could not be sent: " + Lines[0].Substring(Lines[0].IndexOf(": ", StringComparison.Ordinal) + 2), Lines.Skip(1).ToArray());
                }
                catch (NtfyTokenException Failure)
                {
                    List<string> Lines = NtfyToken.Describe(Failure.Result);
                    return Failed("the test notification can't be sent: " + Lines[0], Lines.Skip(1).ToArray());
                }
                return new TestNotificationResult(true, "Info: the ntfy server accepted the test notification at " + Now().ToString("HH:mm", CultureInfo.InvariantCulture)
                    + ". It shows on every device subscribed to the topic. If it does not arrive, check the topic in the ntfy app.");
            }
            finally
            {
                //the token's references end with the publish
                if (Ntfy != null)
                {
                    Ntfy.ResolvedToken = null;
                }
            }
        }

        private static TestNotificationResult Failed(string Problem, params string[] Then)
        {
            var Text = new StringBuilder("Error: ").Append(Problem);
            foreach (string Line in Then.Where(L => !string.IsNullOrEmpty(L)))
            {
                Text.Append(' ').Append(Line);
            }
            return new TestNotificationResult(false, Text.ToString());
        }

        private static string Count(int N, string One, string Many) => N.ToString("N0", CultureInfo.InvariantCulture) + " " + (N == 1 ? One : Many);
    }
}
