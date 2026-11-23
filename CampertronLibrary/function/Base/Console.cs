using System.Net.Mail;
using System.Net;
using static ConsoleConfig;
using System.Text;
using System.Diagnostics;
using System.Net.Mime;

namespace CampertronLibrary.function.Base
{
    public static class CampsiteConfig
    {
        //writes one search's output stream: to the console (Console, UnitTest), as the HTML page (HtmlFile) or as an email
        //CachePath (CampertronInternalConfig.CachePath) is where HtmlFile and Email output write the HTML results
        public static void ProcessConsoleConfig(List<ConsoleConfig.ConsoleConfigValue> Config, ref ConsoleConfig.ConfigType LastConfigType, GeneralConfig GenConfig, EmailConfig emailConfig, string? CachePath)
        {
            if (GenConfig.OutputTo == OutputType.Console || GenConfig.OutputTo == OutputType.UnitTest)
            {
                //a single stream: the same rendering Load.OutputResults uses for a whole run, without the run summary
                WriteConsoleResults(new List<ConsoleConfigItem> { new ConsoleConfigItem { Values = Config } }, false);
                if (Config.Count > 0)
                {
                    LastConfigType = Config[Config.Count - 1].ConfigType;
                }
            }
            else if (GenConfig.OutputTo == OutputType.HtmlFile)
            {
                //a single stream: the same one-page path Load.OutputResults uses for a whole HtmlFile run
                WriteAndOpenHtmlResults(new List<ConsoleConfigItem> { new ConsoleConfigItem { Values = Config } }, CachePath);
            }
            else if (GenConfig.OutputTo == OutputType.Email)
            {
                //Email: one email per search (unchanged), and the console's line for it
                ConsoleConfigItem Sent = SendSearchEmail(Config, emailConfig, CachePath, ConsoleUi.Now);
                ConsoleUi.LoopLine(EmailSentText(ConsoleResults.SearchName(Sent), ConsoleResults.Openings(Sent)));
            }
        }
        //sends one search's results email, or throws; writes nothing to the console (the monitoring service's deliveries,
        //D90, and ProcessConsoleConfig). Only outputTo: Email sends one (since D67 outputTo: Ntfy sends none). Since D72 the
        //body shows this search's results (ResultsEmail) and the page is attached only when they are too large for the
        //body. Now is asked once, when the email is composed. Returns the search as the email showed it
        internal static ConsoleConfigItem SendSearchEmail(List<ConsoleConfig.ConsoleConfigValue> Config, EmailConfig emailConfig, string? CachePath, Func<DateTime> Now)
        {
            ConsoleConfigItem Search = new ConsoleConfigItem { Values = Config };
            string Html = ResultsHtml.Render(new List<ConsoleConfigItem> { Search }, System.Globalization.CultureInfo.CurrentCulture);

            //replace the old html file in the cache with the results (as before; the email never names the file)
            WriteHtmlResults(CachePath, Html);

            var message = CreateResultsEmail(emailConfig, ResultsEmail.Compose(Search, Html, System.Globalization.CultureInfo.CurrentCulture, Now()));

            //returns only when the email was sent, otherwise it throws
            SendResultsEmail(emailConfig, message);
            return new ConsoleConfigItem { Values = Config };
        }
        //the console's lines for a notification that was sent (the monitoring's log)
        internal static string EmailSentText(string SearchName, int Openings)
        {
            return "Email sent: " + SearchName + " (" + ConsoleUi.Count(Openings, "opening", "openings") + ")";
        }
        internal static string NtfySentText(string SearchName, int Openings)
        {
            return "ntfy notification sent: " + SearchName + " (" + ConsoleUi.Count(Openings, "opening", "openings") + ")";
        }
        //sends one results email through the SMTP server of Email.yaml, or throws. Replaceable so tests can record the
        //messages and make a send fail without a network
        internal static Action<EmailConfig, MailMessage> SendResultsEmail { get; set; } = SendWithSmtp;
        private static void SendWithSmtp(EmailConfig emailConfig, MailMessage message)
        {
            using (var client = new SmtpClient())
            {
                client.Port = emailConfig.SmtpPort;
                client.Host = emailConfig.SmtpServer;
                client.EnableSsl = true;
                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                client.UseDefaultCredentials = false;
                client.Credentials = SmtpCredential(emailConfig);
                client.Send(message);
            }
        }
        //the user name of Email.yaml and the password of its configured source (D66): the file smtpPasswordFile names,
        //CAMPERTRON_EMAIL_PASSWORD or smtpPassword, resolved once by email mode's start (SmtpPassword.ForSending). Throws
        //SmtpPasswordException, before any connection, when the source configured can't be used: never another source
        internal static NetworkCredential SmtpCredential(EmailConfig emailConfig)
        {
            return new NetworkCredential(emailConfig.SmtpUsername, SmtpPassword.ForSending(emailConfig));
        }
        //Console output: the searches' results (in the order given), rendered from their typed values by ConsoleResults
        //(grouped by date or by campsite, wrapped to the terminal's width), with the run's summary line if asked for
        public static void WriteConsoleResults(IEnumerable<ConsoleConfigItem> Searches, bool WithSummary)
        {
            ConsoleUi.Write(ConsoleResults.Render(Searches, System.Globalization.CultureInfo.CurrentCulture, ConsoleUi.Current.Width, WithSummary));
        }
        //HtmlFile output: renders every search of the run (in the order given) into one page, writes it as the only
        //*.html in CachePath and opens it once, so no search's page is deleted before the browser has opened it (TD-06)
        public static string WriteAndOpenHtmlResults(IEnumerable<ConsoleConfigItem> Searches, string? CachePath)
        {
            string Html = ResultsHtml.Render(Searches, System.Globalization.CultureInfo.CurrentCulture);
            string DbFile = WriteHtmlResults(CachePath, Html);
            OpenHtmlFile(DbFile);
            return DbFile;
        }
        //opens the results with the default browser (OS shell); replaceable so tests can record the request
        public static Action<string> OpenHtmlFile { get; set; } = OpenWithShell;
        public static void OpenWithShell(string FilePath)
        {
            var p = new Process();
            p.StartInfo = new ProcessStartInfo(FilePath)
            {
                UseShellExecute = true
            };
            p.Start();
        }
        //deletes the previous results (*.html directly in CachePath) and writes Html to a new {guid}.html there;
        //returns its path. CachePath is the configured cache directory (CampertronInternalConfig.CachePath, TD-05)
        public static string WriteHtmlResults(string? CachePath, string Html)
        {
            string cachepath = RecDotOrg.sqlite.Clear.CheckCacheDirectory(CachePath);
            if (Directory.Exists(cachepath) == false)
            {
                Directory.CreateDirectory(cachepath);
            }
            else
            {
                foreach(String ThisOldFile in Directory.GetFiles(cachepath, "*.html", SearchOption.TopDirectoryOnly))
                {
                    File.Delete(ThisOldFile);
                }
            }

            //create new html file with results
            string DbFile = Path.Join(cachepath, System.Guid.NewGuid().ToString() + ".html");
            TextWriter TW = new StreamWriter(DbFile);
            TW.Write(Html);
            TW.Close();
            return DbFile;
        }
        //the results email (D72): a multipart/alternative body, the plain text first and the HTML last (a client shows
        //the last part it can display, RFC 2046 §5.1.4), each UTF-8 and base64 encoded (the size ResultsEmail measured),
        //and, only when the full results don't fit in the body, the results page attached from memory as
        //Campertron-results-yyyy-MM-dd.html (text/html). Nothing is read from a file
        public static MailMessage CreateResultsEmail(EmailConfig emailConfig, ResultsEmail.Content Content)
        {
            var message = new System.Net.Mail.MailMessage();
            message.From = new MailAddress(emailConfig.SendFromAddress);
            foreach (String ThisToAddress in emailConfig.SendToAddressList)
            {
                message.To.Add(ThisToAddress);
            }
            message.Subject = ResultsEmail.Subject;
            message.AlternateViews.Add(Part(AlternateView.CreateAlternateViewFromString(Content.Text, Utf8, MediaTypeNames.Text.Plain)));
            message.AlternateViews.Add(Part(AlternateView.CreateAlternateViewFromString(Content.Html, Utf8, MediaTypeNames.Text.Html)));

            if (Content.AttachmentName != null)
            {
                var Type = new ContentType(MediaTypeNames.Text.Html) { CharSet = "utf-8", Name = Content.AttachmentName };
                Attachment Page = Part(new Attachment(new MemoryStream(Utf8.GetBytes(Content.AttachmentHtml ?? string.Empty)), Type));
                Page.ContentDisposition!.FileName = Content.AttachmentName;
                Page.ContentDisposition.Inline = false;
                message.Attachments.Add(Page);
            }
            return message;
        }
        //the test email of D86 and D88 (TestNotification.SendEmail): the same sender and recipients as a results email, the
        //same multipart/alternative form (plain text first, then HTML, each UTF-8 and base64), a fixed subject and text that
        //say it is a test, and no attachment. Nothing of the settings is in it
        internal static MailMessage CreateTestEmail(EmailConfig emailConfig)
        {
            var message = new System.Net.Mail.MailMessage();
            //the validation refused an Email.yaml without them before (ConfigValidation.TestEmailErrors)
            message.From = new MailAddress(emailConfig.SendFromAddress ?? string.Empty);
            foreach (String ThisToAddress in emailConfig.SendToAddressList ?? new List<string>())
            {
                message.To.Add(ThisToAddress);
            }
            message.Subject = TestNotification.EmailSubject;
            string Html = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>" + System.Net.WebUtility.HtmlEncode(TestNotification.EmailSubject)
                + "</title></head><body><p>" + System.Net.WebUtility.HtmlEncode(TestNotification.EmailBody) + "</p></body></html>";
            message.AlternateViews.Add(Part(AlternateView.CreateAlternateViewFromString(TestNotification.EmailBody, Utf8, MediaTypeNames.Text.Plain)));
            message.AlternateViews.Add(Part(AlternateView.CreateAlternateViewFromString(Html, Utf8, MediaTypeNames.Text.Html)));
            return message;
        }
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static T Part<T>(T Part) where T : AttachmentBase
        {
            Part.TransferEncoding = TransferEncoding.Base64;
            return Part;
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLine, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.Write, ConfigValue = WriteLine };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLineColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(String WriteLine, System.ConsoleColor WriteColor, bool WriteOnly)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteColor, ConfigValue = WriteLine, Color = WriteColor };
        }
        public static ConsoleConfig.ConsoleConfigValue AddConsoleConfigItem(bool WriteLine)
        {
            return new ConsoleConfig.ConsoleConfigValue() { ConfigType = ConsoleConfig.ConfigType.WriteLine };
        }
        //one colored line; the color is used on a terminal only and reset after the line (ConsoleUi)
        public static void WriteToConsole(String WriteText, System.ConsoleColor Color)
        {
            ConsoleUi.Line(WriteText, Color);
        }
        public static void WriteToConsole(ConsoleConfigValue WriteItem)
        {
            Console.ForegroundColor = WriteItem.Color;
            Console.Write(WriteItem.ConfigValue);
        }
        public static void WriteToConsole(List<ConsoleConfigValue> WriteTextLst)
        {
            foreach (ConsoleConfigValue WriteText in WriteTextLst)
            {
                WriteToConsole(WriteText);
            }
            Console.WriteLine();
        }
    }
}