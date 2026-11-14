using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // HtmlFile output writes one page for the whole run and opens it once (beta.9, TD-06 for HtmlFile); Email still
    // sends one email per search. The browser launch is replaced by a recorder (CampsiteConfig.OpenHtmlFile) and SMTP
    // goes to a closed loopback port, so no browser starts and no email is sent. Each test uses its own temporary
    // cache directory.
    public class HtmlResultsOutputTests
    {
        private string Root = string.Empty;
        private string CacheDirectory => Path.Join(Root, "cache");
        private Action<string> OriginalOpen = null!;
        private CultureInfo? OriginalCulture;
        private readonly List<(string Path, string[] HtmlFilesAtLaunch, string Content)> Launches = new();

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronHtmlResultsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOpen = CampsiteConfig.OpenHtmlFile;
            Launches.Clear();
            CampsiteConfig.OpenHtmlFile = FilePath =>
                Launches.Add((FilePath, Directory.GetFiles(CacheDirectory, "*.html"), File.ReadAllText(FilePath)));
        }

        [TearDown]
        public void TearDown()
        {
            CampsiteConfig.OpenHtmlFile = OriginalOpen;
            CultureInfo.CurrentCulture = OriginalCulture!;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private CampertronInternalConfig Config(OutputType Output, EmailConfig? Email = null) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            EmailConfig = Email ?? new EmailConfig(),
            CachePath = CacheDirectory
        };

        private static ConsoleConfig.ConsoleConfigItem Search(string Name, string CampsiteId) =>
            OutputStreams.Search(Name, OutputStreams.Header(Name),
                OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit(CampsiteId, "S" + CampsiteId, new DateTime(2026, 11, 20))));

        // the searches in a run arrive in parallel-completion order
        private static List<ConsoleConfig.ConsoleConfigItem> UnorderedRun() => new()
        {
            Search("Charlie", "3"), Search("Alpha", "1"), Search("Bravo", "2")
        };

        [Test]
        public void HtmlFileRun_WritesOnePage_OpensItOnce_AndKeepsIt()
        {
            File.WriteAllText(Path.Join(CacheDirectory, "previous.html"), "old results");
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Cached.json"), "x");
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Load.OutputResults(Config(OutputType.HtmlFile), UnorderedRun(), ref Last);

            string[] Html = Directory.GetFiles(CacheDirectory, "*.html");
            Assert.That(Html, Has.Length.EqualTo(1), "one page for the run; the previous results are replaced");
            Assert.That(Launches, Has.Count.EqualTo(1), "one browser launch");
            Assert.That(Launches[0].Path, Is.EqualTo(Html[0]));
            Assert.That(Launches[0].HtmlFilesAtLaunch, Is.EqualTo(Html), "the page exists when it is opened and is the only one");
            Assert.That(File.ReadAllText(Html[0]), Is.EqualTo(Launches[0].Content), "not rewritten after it was opened");
            Assert.That(File.Exists(Path.Join(CacheDirectory, "232445-Cached.json")), Is.True);
        }

        [Test]
        public void HtmlFileRun_ContainsEverySearchOnce_InNameOrder_WithItsOwnResults()
        {
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Load.OutputResults(Config(OutputType.HtmlFile), UnorderedRun(), ref Last);

            string Page = Launches.Single().Content;
            var Sections = System.Text.RegularExpressions.Regex.Matches(Page, "<section [^>]*>(.*?)</section>", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value).ToList();
            Assert.That(Sections, Has.Count.EqualTo(3));
            Assert.That(Sections.Select(s => System.Text.RegularExpressions.Regex.Match(s, "<h2[^>]*>(.*?)</h2>").Groups[1].Value), Is.EqualTo(new[] { "Alpha", "Bravo", "Charlie" }));
            string[] Ids = { "1", "2", "3" };
            for (int i = 0; i < 3; i++)
            {
                Assert.That(Sections[i], Does.Contain("href=\"" + ResultsHtml.BookingUrlPrefix + Ids[i] + "\""));
                foreach (string Other in Ids.Where(id => id != Ids[i]))
                {
                    Assert.That(Sections[i], Does.Not.Contain("href=\"" + ResultsHtml.BookingUrlPrefix + Other + "\""));
                }
            }

            // same run again, other arrival order: same order on the page
            Launches.Clear();
            var Reversed = UnorderedRun();
            Reversed.Reverse();
            Load.OutputResults(Config(OutputType.HtmlFile), Reversed, ref Last);
            Assert.That(System.Text.RegularExpressions.Regex.Matches(Launches.Single().Content, "<h2[^>]*>(.*?)</h2>").Select(m => m.Groups[1].Value),
                        Is.EqualTo(new[] { "Alpha", "Bravo", "Charlie" }));
            Assert.That(Directory.GetFiles(CacheDirectory, "*.html"), Has.Length.EqualTo(1));
        }

        [Test]
        public void ProcessConsoleConfig_HtmlFile_OneStream_IsOnePageAndOneLaunch()
        {
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
            CampsiteConfig.ProcessConsoleConfig(Search("Alpha", "1").Values!, ref Last, new GeneralConfig { OutputTo = OutputType.HtmlFile }, new EmailConfig(), CacheDirectory);

            Assert.That(Launches, Has.Count.EqualTo(1));
            Assert.That(Launches[0].Content, Does.Contain("<h2 id=\"search-1-title\">Alpha</h2>"));
            Assert.That(Directory.GetFiles(CacheDirectory, "*.html"), Is.EqualTo(new[] { Launches[0].Path }));
        }

        [Test]
        public void EmailRun_StillOneEmailPerSearch_FirstSearchByName_WithOnlyItsResults()
        {
            // the first email fails at the refused connection: its page (body and attachment) is written by then
            var Email = new EmailConfig { SmtpServer = "127.0.0.1", SmtpPort = ClosedLoopbackPort(), SmtpUsername = "u", SmtpPassword = "p", SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "to@example.invalid" } };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Assert.That(() => Load.OutputResults(Config(OutputType.Email, Email), UnorderedRun(), ref Last), Throws.InstanceOf<SmtpException>());

            string Page = File.ReadAllText(Directory.GetFiles(CacheDirectory, "*.html").Single());
            Assert.That(System.Text.RegularExpressions.Regex.Matches(Page, "<h2[^>]*>(.*?)</h2>").Select(m => m.Groups[1].Value), Is.EqualTo(new[] { "Alpha" }));
            Assert.That(Page, Does.Not.Contain("<nav"));
            Assert.That(Launches, Is.Empty, "email never opens a browser");
        }

        [Test]
        public void EmailOutput_EachSearch_IsOneCompleteDocument_WithOnlyThatSearch_Encoded_WithItsBookingLinks()
        {
            // what Load.OutputResults does for Email, one ProcessConsoleConfig call per search; each call fails at the
            // refused SMTP connection, after its page (the body and the attachment) has been written
            var Email = new EmailConfig { SmtpServer = "127.0.0.1", SmtpPort = ClosedLoopbackPort(), SmtpUsername = "u", SmtpPassword = "p", SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "to@example.invalid" } };
            var Searches = new[] { Search("Alpha", "1"), Search("Tents & <RVs>", "2&x"), Search("Charlie", "3") };
            foreach (ConsoleConfig.ConsoleConfigItem ThisSearch in Searches)
            {
                ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
                Assert.That(() => CampsiteConfig.ProcessConsoleConfig(ThisSearch.Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, Email, CacheDirectory),
                            Throws.InstanceOf<SmtpException>());

                string Page = File.ReadAllText(Directory.GetFiles(CacheDirectory, "*.html").Single());
                var Doc = System.Xml.Linq.XDocument.Load(System.Xml.XmlReader.Create(new StringReader(Page), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore }));
                Assert.That(Page, Does.StartWith("<!DOCTYPE html>\n<html lang=\"en\">"));
                Assert.That(Page.TrimEnd(), Does.EndWith("</html>"));
                Assert.That(Doc.Descendants("html").Count(), Is.EqualTo(1));
                Assert.That(Doc.Descendants("body").Count(), Is.EqualTo(1));
                Assert.That(Doc.Descendants("section").Count(), Is.EqualTo(1));
                Assert.That(Doc.Descendants("h2").Single().Value, Is.EqualTo(ThisSearch.Name));
                Assert.That(Doc.Descendants("nav"), Is.Empty, "no navigation list that could link to searches not in this email");
                Assert.That(Doc.Descendants("a").Select(a => a.Attribute("href")!.Value),
                            Is.EqualTo(new[] { ResultsHtml.BookingUrlPrefix + Uri.EscapeDataString(ThisSearch.Values!.First(v => v.Kind == ConsoleConfig.OutputKind.Availability).Metadata!.CampsiteID!) }));
                Assert.That(Launches, Is.Empty);
            }
        }

        [Test]
        public void ResultsEmail_SubjectRecipientsUnchanged_BodyIsThePage_AttachmentHasTheSameBytes()
        {
            string Page = ResultsHtml.Render(new[] { Search("Tents & <RVs>", "1") }, CultureInfo.CurrentCulture);
            string HtmlFile = CampsiteConfig.WriteHtmlResults(CacheDirectory, Page);
            var Email = new EmailConfig { SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "to1@example.invalid", "to2@example.invalid" } };

            using MailMessage Message = CampsiteConfig.CreateResultsEmail(Email, Page, HtmlFile);

            Assert.That(Message.Subject, Is.EqualTo("Campertron Auto-Email"));
            Assert.That(Message.From!.Address, Is.EqualTo("from@example.invalid"));
            Assert.That(Message.To.Select(t => t.Address), Is.EqualTo(new[] { "to1@example.invalid", "to2@example.invalid" }));
            Assert.That(Message.IsBodyHtml, Is.True);
            Assert.That(Message.Body, Is.EqualTo(Page));
            Assert.That(Page, Does.Contain("<h2 id=\"search-1-title\">Tents &amp; &lt;RVs&gt;</h2>"));
            Attachment File_ = Message.Attachments.Single();
            Assert.That(File_.Name, Is.EqualTo(Path.GetFileName(HtmlFile)));
            Assert.That(File_.ContentType.MediaType, Is.EqualTo(System.Net.Mime.MediaTypeNames.Application.Octet));
            using var Bytes = new MemoryStream();
            File_.ContentStream.CopyTo(Bytes);
            Assert.That(Bytes.ToArray(), Is.EqualTo(new System.Text.UTF8Encoding(false).GetBytes(Page)), "the attachment is the body as UTF-8 without a byte order mark");
        }

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.UnitTest)]
        public void ConsoleRuns_WriteNoPage(OutputType Output_)
        {
            var OriginalOut = Console.Out;
            Console.SetOut(new StringWriter());
            try
            {
                ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
                Load.OutputResults(Config(Output_), UnorderedRun(), ref Last);
            }
            finally
            {
                Console.SetOut(OriginalOut);
            }

            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty);
            Assert.That(Launches, Is.Empty);
        }

        [Test]
        public void OutputResults_WithoutGeneralConfig_ThrowsBeforeWritingAnything()
        {
            // PostProcess reads config.GeneralConfig.OutputTo before calling OutputResults, so production never gets
            // here without it; a direct caller gets a clear exception and no page, launch or console output
            var Config_ = new CampertronInternalConfig { GeneralConfig = null, EmailConfig = new EmailConfig(), CachePath = CacheDirectory };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Assert.That(() => Load.OutputResults(Config_, UnorderedRun(), ref Last),
                        Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("config"));
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty);
            Assert.That(Launches, Is.Empty);
        }

        private static int ClosedLoopbackPort()
        {
            var Listener = new TcpListener(IPAddress.Loopback, 0);
            Listener.Start();
            int Port = ((IPEndPoint)Listener.LocalEndpoint).Port;
            Listener.Stop();
            return Port;
        }
    }
}
