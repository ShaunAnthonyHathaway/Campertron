using System.Globalization;
using System.Net.Mail;
using System.Xml.Linq;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The results email as it is sent (D72), through ProcessConsoleConfig and its send seam (CampsiteConfig.SendResultsEmail:
    // a recorder, nothing is sent) and System.Net.Mail's own writer (a pickup directory in a temporary folder). Small
    // results are in the body, as plain text and HTML, without attachment; results too large for the body give a short
    // body and the results page attached as Campertron-results-yyyy-MM-dd.html. These tests use only what existed
    // before D72, so they also run against an older library: on 54d3142 they fail (the negative control of §BR).
    public class ResultsEmailDeliveryTests
    {
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<DateTime> OriginalNow = null!;
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
            CampsiteConfig.SendResultsEmail = (Email, Message) => Sent.Add(Message);
            OriginalNow = ConsoleUi.Now;
            ConsoleUi.Now = () => new DateTime(2026, 11, 22, 23, 59, 30);
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Captured = new StringWriter();
            Console.SetOut(Captured);
            Sent.Clear();
            Root = Path.Join(Path.GetTempPath(), "CampertronResultsEmailDeliveryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            ConsoleUi.Now = OriginalNow;
            Console.SetOut(OriginalOut!);
            Captured.Dispose();
            CultureInfo.CurrentCulture = OriginalCulture!;
            foreach (MailMessage Message in Sent)
            {
                Message.Dispose();
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private MailMessage Send(ConsoleConfig.ConsoleConfigItem Search, EmailConfig? Settings = null)
        {
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
            CampsiteConfig.ProcessConsoleConfig(Search.Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, Settings ?? ResultsEmails.Settings(), CacheDirectory);
            return Sent.Last();
        }

        private static XDocument Document(string Html) =>
            XDocument.Load(System.Xml.XmlReader.Create(new StringReader(Html), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore }));

        private static string Squeeze(string Value) => string.Join(" ", Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // an element's text with its lines (<br />) and parts apart
        private static string Words(XElement Element) => Squeeze(string.Join(" ", Element.DescendantNodes().OfType<XText>().Select(p => p.Value)));

        private static List<string> Lines(string Text) => Text.Split("\r\n").ToList();

        [Test]
        public void SmallResults_AreInTheBody_PlainTextThenHtml_WithoutAttachment()
        {
            MailMessage Message = Send(ResultsEmails.Search("Zion tents", 3));

            ResultsEmails.MimePart Mime = ResultsEmails.Parse(ResultsEmails.Raw(Message));
            Assert.That(Mime.MediaType, Is.EqualTo("multipart/alternative"), "no multipart/mixed: nothing is attached");
            Assert.That(Mime.Parts.Select(p => p.MediaType), Is.EqualTo(new[] { "text/plain", "text/html" }), "the HTML last, the part a client prefers");
            Assert.That(Mime.Parts.Select(p => ResultsEmails.Parameter(p.ContentType, "charset")), Has.All.EqualTo("utf-8"));
            Assert.That(Mime.Parts.Select(p => p.Headers["Content-Transfer-Encoding"]), Has.All.EqualTo("base64"));
            Assert.That(Message.Attachments, Is.Empty);
            Assert.That(Message.Subject, Is.EqualTo("Campertron Auto-Email"));

            XDocument Html = Document(Mime.Parts[1].Decoded);
            Assert.That(Html.Descendants("h1").Single().Value, Is.EqualTo("Campertron results"));
            Assert.That(Html.Descendants("h2").Single().Value, Is.EqualTo("Zion tents"));
            List<string> Paragraphs = Html.Descendants("p").Select(p => Squeeze(p.Value)).ToList();
            Assert.That(Paragraphs, Does.Contain("WATCHMAN CAMPGROUND · Zion National Park · Campground ID 232445"));
            Assert.That(Paragraphs, Does.Contain("3 availability entries for 3 sites on 3 dates"));
            Assert.That(Paragraphs, Does.Contain("Check-in 12:00 PM · Check-out 11:00 AM for every site shown."));
            Assert.That(Html.Descendants("h3").Select(p => p.Value), Is.EqualTo(new[] { "November 2026" }));
            Assert.That(Html.Descendants("h4").Select(p => Squeeze(p.Value)),
                        Is.EqualTo(new[] { "Sunday, 11/1/2026 · 1 site", "Monday, 11/2/2026 · 1 site", "Tuesday, 11/3/2026 · 1 site" }));
            Assert.That(Html.Descendants("time").Select(p => p.Attribute("datetime")!.Value), Is.EqualTo(new[] { "2026-11-01", "2026-11-02", "2026-11-03" }));
            List<XElement> Sites = Html.Descendants("div").Where(p => (string?)p.Attribute("class") == "cx-site").ToList();
            Assert.That(Sites.Select(Words), Is.EqualTo(new[]
            {
                "Site A001 · Loop C · STANDARD NONELECTRIC Equipment: Tent, CAR Book Site A001 on recreation.gov",
                "Site A002 · Loop C · STANDARD NONELECTRIC Equipment: Tent, CAR Book Site A002 on recreation.gov",
                "Site A003 · Loop C · STANDARD NONELECTRIC Equipment: Tent, CAR Book Site A003 on recreation.gov"
            }));
            Assert.That(Html.Descendants("a").Select(p => p.Attribute("href")!.Value), Is.EqualTo(new[]
            {
                ResultsHtml.BookingUrlPrefix + "1001", ResultsHtml.BookingUrlPrefix + "1002", ResultsHtml.BookingUrlPrefix + "1003"
            }), "the booking links are the only links");

            List<string> Text = Lines(Mime.Parts[0].Decoded);
            Assert.That(Text.Take(7), Is.EqualTo(new[]
            {
                "Campertron results", "", "Zion tents", "WATCHMAN CAMPGROUND · Zion National Park · Campground ID 232445",
                "3 availability entries for 3 sites on 3 dates", "Check-in 12:00 PM · Check-out 11:00 AM for every site shown.", ""
            }));
            Assert.That(Text, Does.Contain("November 2026").And.Contain("Sunday, 11/1/2026 · 1 site").And.Contain("  Site A001 · Loop C · STANDARD NONELECTRIC")
                .And.Contain("  Equipment: Tent, CAR").And.Contain("  Book: https://www.recreation.gov/camping/campsites/1001"));
            Assert.That(Captured.ToString(), Does.Contain("Email sent: Zion tents (3 openings)"), "the console line is unchanged");
        }

        [Test]
        public void LargeResults_GiveAShortBody_AndThePageAttached_UnderItsDatedName()
        {
            MailMessage Message = Send(ResultsEmails.Search("Every site", 400));
            string Page = File.ReadAllText(Directory.GetFiles(CacheDirectory, "*.html").Single());

            ResultsEmails.MimePart Mime = ResultsEmails.Parse(ResultsEmails.Raw(Message));
            Assert.That(Mime.MediaType, Is.EqualTo("multipart/mixed"));
            Assert.That(Mime.Parts.Select(p => p.MediaType), Is.EqualTo(new[] { "multipart/alternative", "text/html" }));
            Assert.That(Mime.Parts[0].Parts.Select(p => p.MediaType), Is.EqualTo(new[] { "text/plain", "text/html" }));
            ResultsEmails.MimePart File_ = Mime.Parts[1];
            Assert.That(File_.Headers["Content-Disposition"], Does.StartWith("attachment"));
            Assert.That(File_.MediaType, Is.EqualTo("text/html"));
            Assert.That(ResultsEmails.Parameter(File_.ContentType, "charset"), Is.EqualTo("utf-8"));
            Assert.That(ResultsEmails.Parameter(File_.Headers["Content-Disposition"], "filename"), Is.EqualTo("Campertron-results-2026-11-22.html"));
            Assert.That(ResultsEmails.Parameter(File_.ContentType, "name"), Is.EqualTo("Campertron-results-2026-11-22.html"));
            Assert.That(File_.Decoded, Is.EqualTo(Page), "the attachment is the full results page (the cache's)");
            Assert.That(Page, Does.Contain(ResultsHtml.BookingUrlPrefix + "1400"));

            XDocument Html = Document(Mime.Parts[0].Parts[1].Decoded);
            Assert.That(Html.Descendants("h1").Single().Value, Is.EqualTo("Campertron results"));
            Assert.That(Html.Descendants("h2").Single().Value, Is.EqualTo("Every site"));
            List<string> Paragraphs = Html.Descendants("p").Select(p => Squeeze(p.Value)).ToList();
            Assert.That(Paragraphs, Does.Contain("400 availability entries for 400 sites on 30 dates"));
            Assert.That(Paragraphs, Does.Contain("The full results are too large to show in this email. Open the attached file, Campertron-results-2026-11-22.html, in a web browser to see every site, date and booking link."));
            Assert.That(Html.Descendants("a"), Is.Empty, "a summary: no site and no link");
            Assert.That(Mime.Parts[0].Parts[1].Decoded.Length, Is.LessThan(8_000));

            string Text = Mime.Parts[0].Parts[0].Decoded;
            Assert.That(Lines(Text), Does.Contain("Every site").And.Contain("400 availability entries for 400 sites on 30 dates")
                .And.Contain("The full results are too large to show in this email. Open the attached file, Campertron-results-2026-11-22.html, in a web browser to see every site, date and booking link."));
            Assert.That(Text, Does.Not.Contain("Book:").And.Not.Contain("A001"));
            Assert.That(Captured.ToString(), Does.Contain("Email sent: Every site (400 openings)"), "the console line is unchanged");
        }

        [Test]
        public void Values_AreEscaped_InBothParts_AndTheOnlyLinksAreRecreationGovBookingLinks()
        {
            var Hit = OutputStreams.Hit("12\"><a href=x>", "<b>Bold</b> & \"Co\"", new DateTime(2026, 11, 20), "<script>alert(1)</script>", "Type\r\nBcc: loop@example.invalid", "Tent & RV", "<i>Van</i>");
            var Search = OutputStreams.Search("Tents & <RVs>", OutputStreams.Header("Tents & <RVs>\r\nBcc: name@example.invalid"),
                OutputStreams.Month(new DateTime(2026, 11, 1), Hit));

            MailMessage Message = Send(Search);
            string Raw = ResultsEmails.Raw(Message);
            ResultsEmails.MimePart Mime = ResultsEmails.Parse(Raw);
            string HtmlText = Mime.Parts.Single(p => p.MediaType == "text/html").Decoded;
            XDocument Html = Document(HtmlText);

            Assert.That(Html.Descendants().Select(p => p.Name.LocalName), Has.None.EqualTo("script").And.None.EqualTo("b").And.None.EqualTo("i"));
            Assert.That(HtmlText, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt;").And.Contain("&lt;b&gt;Bold&lt;/b&gt; &amp; &quot;Co&quot;"));
            Assert.That(Html.Descendants("a").Select(p => p.Attribute("href")!.Value),
                        Is.EqualTo(new[] { ResultsHtml.BookingUrlPrefix + "12%22%3E%3Ca%20href%3Dx%3E" }));
            Assert.That(Html.Descendants("h2").Single().Value, Does.Not.Contain("\n"), "a line break inside a value is a space");

            List<string> Text = Lines(Mime.Parts.Single(p => p.MediaType == "text/plain").Decoded);
            Assert.That(Text, Has.None.StartsWith("Bcc:"), "a value can't make a line of its own");
            Assert.That(Text, Does.Contain("  Book: " + ResultsHtml.BookingUrlPrefix + "12%22%3E%3Ca%20href%3Dx%3E"));
            Assert.That(Text, Does.Contain("  Site <b>Bold</b> & \"Co\" · <script>alert(1)</script> · Type  Bcc: loop@example.invalid"), "plain text, not HTML");
            string Headers = Raw.Substring(0, Raw.IndexOf("\r\n\r\n", StringComparison.Ordinal));
            Assert.That(Headers, Does.Not.Contain("Bcc"));
            Assert.That(Message.Bcc, Is.Empty);
        }

        [TestCase(3)]
        [TestCase(400)]
        public void TheEmail_ShowsNoSecretPathOrSetting(int Sites)
        {
            EmailConfig Settings = ResultsEmails.Settings();
            MailMessage Message = Send(ResultsEmails.Search("Canary search", Sites), Settings);
            string Raw = ResultsEmails.Raw(Message);
            List<string> Decoded = ResultsEmails.Parse(Raw).Leaves().Select(p => p.Decoded).ToList();
            string[] Never =
            {
                Settings.SmtpPassword!, Settings.SmtpUsername!, Settings.SmtpServer!, Root, CacheDirectory,
                Path.GetFileName(Directory.GetFiles(CacheDirectory, "*.html").Single()), "Email.yaml", "General.yaml", "Ntfy.yaml"
            };
            foreach (string Value in Never)
            {
                Assert.That(Raw, Does.Not.Contain(Value));
                Assert.That(Decoded, Has.None.Contain(Value));
            }
            Assert.That(Decoded, Has.None.Contain("@example.invalid"), "no address in the body or the attachment");
        }

        [TestCase(3)]
        [TestCase(400)]
        public void TheBody_IsReadableInLightAndDarkClients(int Sites)
        {
            MailMessage Message = Send(ResultsEmails.Search("Colors", Sites));
            string HtmlText = ResultsEmails.Html(Message);
            XDocument Html = Document(HtmlText);
            var Metas = Html.Descendants("meta").ToDictionary(p => (string?)p.Attribute("name") ?? (string?)p.Attribute("http-equiv") ?? string.Empty, p => (string)p.Attribute("content")!);
            Assert.That(Metas["color-scheme"], Is.EqualTo("light dark"));
            Assert.That(Metas["supported-color-schemes"], Is.EqualTo("light dark"));
            string Css = Html.Descendants("style").Single().Value;
            Assert.That(Css, Does.Contain("@media (prefers-color-scheme: dark)"));
            Assert.That(HtmlText, Does.Not.Contain("var(").And.Not.Contain("grid").And.Not.Contain("<script").And.Not.Contain("<img").And.Not.Contain("url("));

            // the dark rules by class, and what each sets
            var Dark = System.Text.RegularExpressions.Regex.Matches(Css, @"\.([\w-]+)\{([^}]*)\}").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            foreach (XElement Element in Html.Descendants().Where(p => p.Attribute("style") != null))
            {
                string Style = (string)Element.Attribute("style")!;
                string[] Classes = ((string?)Element.Attribute("class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool Background = Style.Contains("background-color:");
                bool Color = System.Text.RegularExpressions.Regex.IsMatch(Style, @"(^|;)color:");
                if (Background)
                {
                    Assert.That(Color, Is.True, "a background always comes with its text color, so an inverting client keeps the contrast: " + Style);
                    Assert.That(Classes.Any(c => Dark.TryGetValue(c, out string? Rule) && Rule.Contains("background-color:") && Rule.Contains(";color:")), Is.True, "and has its dark colors: " + Element.Name);
                }
                else if (Color)
                {
                    Assert.That(Classes.Any(c => Dark.TryGetValue(c, out string? Rule) && Rule.StartsWith("color:", StringComparison.Ordinal)), Is.True, "a text color has its dark color: " + Element.Name);
                }
            }
        }
    }
}
