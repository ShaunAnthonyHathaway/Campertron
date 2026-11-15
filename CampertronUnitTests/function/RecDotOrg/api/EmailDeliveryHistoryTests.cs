using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // The Email output and its notification history (business-rules §5, §6). The SMTP send is replaced through
    // CampsiteConfig.SendResultsEmail (the production default is the SMTP client), so a test can record every message,
    // make a send succeed or fail, and look at Camp.History while a send is in progress. No email leaves the process.
    public class EmailDeliveryHistoryTests
    {
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private TextWriter? OriginalOut;
        private CultureInfo? OriginalCulture;
        private StringWriter Captured = null!;
        private readonly List<MailMessage> Sent = new();
        private string Root = string.Empty;
        private string CacheDirectory => Path.Join(Root, "cache");

        [SetUp]
        public void Setup()
        {
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Captured = new StringWriter();
            Console.SetOut(Captured);
            Sent.Clear();
            Root = Path.Join(Path.GetTempPath(), "CampertronEmailDeliveryHistoryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Console.SetOut(OriginalOut!);
            Captured.Dispose();
            CultureInfo.CurrentCulture = OriginalCulture!;
            // a message keeps its attachment (the results page) open until it is disposed
            foreach (MailMessage Message in Sent)
            {
                Message.Dispose();
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // addresses of the reserved .invalid domain and a placeholder password: nothing is sent anywhere
        private static EmailConfig TestEmail() => new EmailConfig
        {
            SmtpServer = "localhost",
            SmtpPort = 25,
            SmtpUsername = "tester",
            SmtpPassword = "not-a-real-password",
            SendFromAddress = "campertron@example.invalid",
            SendToAddressList = new List<string> { "someone@example.invalid" }
        };

        private static ConsoleConfig.ConsoleConfigItem Alpha() => OutputStreams.Search("Alpha", OutputStreams.Header("Alpha"),
            OutputStreams.Month(new DateTime(2027, 1, 1), OutputStreams.Hit("111", "A001", new DateTime(2027, 1, 3))));

        // ---- the send seam: the same message as before, reported as sent only after the send returned ----

        [Test]
        public void EmailOutput_HandsTheResultsMessageToTheSend_AndReportsItOnlyAfterwards()
        {
            string? WrittenWhileSending = null;
            CampsiteConfig.SendResultsEmail = (Email, Message) => { Sent.Add(Message); WrittenWhileSending = Captured.ToString(); };
            ConsoleConfig.ConsoleConfigItem Search = Alpha();
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            CampsiteConfig.ProcessConsoleConfig(Search.Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, TestEmail(), CacheDirectory);

            MailMessage Message = Sent.Single();
            string Page = ResultsHtml.Render(new[] { new ConsoleConfig.ConsoleConfigItem { Values = Search.Values } }, CultureInfo.CurrentCulture);
            Assert.That(Message.Subject, Is.EqualTo("Campertron Auto-Email"));
            Assert.That(Message.From!.Address, Is.EqualTo("campertron@example.invalid"));
            Assert.That(Message.To.Select(p => p.Address), Is.EqualTo(new[] { "someone@example.invalid" }));
            Assert.That(Message.IsBodyHtml, Is.True);
            Assert.That(Message.Body, Is.EqualTo(Page));
            Attachment File_ = Message.Attachments.Single();
            Assert.That(File_.ContentType.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(File_.Name, Is.EqualTo(Path.GetFileName(Directory.GetFiles(CacheDirectory, "*.html").Single())));
            using (var Copy = new MemoryStream())
            {
                File_.ContentStream.CopyTo(Copy);
                Assert.That(Copy.ToArray(), Is.EqualTo(new System.Text.UTF8Encoding(false).GetBytes(Page)), "the attachment is the page");
            }
            Assert.That(WrittenWhileSending, Does.Not.Contain("Email sent"), "not reported before the send returned");
            Assert.That(Captured.ToString(), Does.Contain("Email sent: Alpha (1 opening)"));
        }

        [Test]
        public void EmailOutput_AFailedSend_Propagates_AndIsNotReportedAsSent()
        {
            var Refused = new SmtpException("refused by the test");
            CampsiteConfig.SendResultsEmail = (Email, Message) => { Sent.Add(Message); throw Refused; };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            var Thrown = Assert.Throws<SmtpException>(() =>
                CampsiteConfig.ProcessConsoleConfig(Alpha().Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, TestEmail(), CacheDirectory));

            Assert.That(Thrown, Is.SameAs(Refused), "the send's own exception, for the console's failure message");
            Assert.That(Sent, Has.Count.EqualTo(1));
            Assert.That(Captured.ToString(), Does.Not.Contain("Email sent"));
        }
    }
}
