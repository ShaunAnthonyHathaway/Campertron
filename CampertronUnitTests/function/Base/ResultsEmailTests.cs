using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Xml.Linq;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ResultsEmail (D72): what the body holds, the size rule that decides between the full results and the summary with
    // the attached page, the measured size against what System.Net.Mail writes, the attachment's name and the
    // plain-text alternative. The message as sent is covered by ResultsEmailDeliveryTests.
    public class ResultsEmailTests
    {
        private CultureInfo? OriginalCulture;

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture!;
        }

        private static readonly DateTime Today = new DateTime(2026, 11, 22, 8, 15, 0);

        private static ResultsEmail.Content Compose(ConsoleConfig.ConsoleConfigItem Search) =>
            ResultsEmail.Compose(Search, ResultsHtml.Render(new[] { Search }, CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, Today);

        private static XDocument Document(string Html) =>
            XDocument.Load(System.Xml.XmlReader.Create(new StringReader(Html), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Ignore }));

        // the HTML part's body as System.Net.Mail writes it, with the CRLF its last line ends with (which RFC 2046 counts
        // as part of the next boundary)
        private static int SentHtmlPartBytes(ResultsEmail.Content Content)
        {
            using MailMessage Message = CampsiteConfig.CreateResultsEmail(ResultsEmails.Settings(), Content);
            ResultsEmails.MimePart Html = ResultsEmails.Parse(ResultsEmails.Raw(Message)).Leaves().First(p => p.MediaType == "text/html");
            return Html.Body.Length + 2;
        }

        [Test]
        public void TheLimit_IsFixed_AndInclusive()
        {
            Assert.That(ResultsEmail.BodyLimitBytes, Is.EqualTo(80_000), "below Gmail's ~102 KB clipping, Email on Acid's 80 KB rule of thumb");
            Assert.That(ResultsEmail.FitsInBody(ResultsEmail.BodyLimitBytes), Is.True);
            Assert.That(ResultsEmail.FitsInBody(ResultsEmail.BodyLimitBytes + 1), Is.False);
            Assert.That(ResultsEmail.FitsInBody(0), Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(50)]
        [TestCase(51)]
        [TestCase(52)]
        [TestCase(53)]
        [TestCase(204)]
        [TestCase(4_999)]
        [TestCase(81_234)]
        public void TheMeasuredSize_IsTheHtmlPartAsSystemNetMailWritesIt(int Length)
        {
            // mixed one-, two- and three-byte characters, so the UTF-8 size differs from the length
            string Html = string.Concat(Enumerable.Range(0, Length).Select(i => (i % 7) switch { 0 => '·', 1 => 'é', 2 => '€', _ => (char)('a' + i % 26) }));
            var Content = new ResultsEmail.Content { Html = Html, Text = "text" };

            Assert.That(ResultsEmail.EncodedSize(Html), Is.EqualTo(SentHtmlPartBytes(Content)));
            Assert.That(ResultsEmail.EncodedSize(Html), Is.GreaterThan(Encoding.UTF8.GetByteCount(Html)), "the encoded size is larger than the HTML");
        }

        [Test]
        public void TheBoundary_TheLargestSearchThatFits_IsInTheBody_OneSiteMoreIsAttached()
        {
            // the largest number of sites whose full body fits (a binary search over one search's sites)
            int Low = 1, High = 2_000;
            while (Low < High)
            {
                int Middle = (Low + High + 1) / 2;
                if (Compose(ResultsEmails.Search("Boundary", Middle)).FullResultsInBody)
                {
                    Low = Middle;
                }
                else
                {
                    High = Middle - 1;
                }
            }
            ResultsEmail.Content Fits = Compose(ResultsEmails.Search("Boundary", Low));
            ResultsEmail.Content Over = Compose(ResultsEmails.Search("Boundary", Low + 1));

            Assert.That(Fits.FullResultsInBody, Is.True);
            Assert.That(Fits.AttachmentName, Is.Null);
            Assert.That(Fits.AttachmentHtml, Is.Null);
            Assert.That(Fits.FullBodyEncodedBytes, Is.LessThanOrEqualTo(ResultsEmail.BodyLimitBytes));
            Assert.That(SentHtmlPartBytes(Fits), Is.EqualTo(Fits.FullBodyEncodedBytes), "measured as sent");
            Assert.That(Fits.Html, Does.Contain(ResultsHtml.BookingUrlPrefix + (1000 + Low).ToString(CultureInfo.InvariantCulture) + "\""), "every site is in the body");

            Assert.That(Over.FullResultsInBody, Is.False);
            Assert.That(Over.FullBodyEncodedBytes, Is.GreaterThan(ResultsEmail.BodyLimitBytes));
            Assert.That(Over.FullBodyEncodedBytes - Fits.FullBodyEncodedBytes, Is.LessThan(1_500), "one site apart: the boundary is the limit");
            Assert.That(Over.AttachmentName, Is.EqualTo("Campertron-results-2026-11-22.html"));
            Assert.That(Over.AttachmentHtml, Is.EqualTo(ResultsHtml.Render(new[] { ResultsEmails.Search("Boundary", Low + 1) }, CultureInfo.CurrentCulture)));
            Assert.That(Over.Html, Does.Not.Contain(ResultsHtml.BookingUrlPrefix));
            Assert.That(ResultsEmail.EncodedSize(Over.Html), Is.LessThan(10_000), "the summary is short");
            Assert.That(Low, Is.InRange(50, 400), "about a hundred single-night sites fit in the body");
        }

        [TestCase("en-US")]
        [TestCase("th-TH")]
        [TestCase("ar-SA")]
        [TestCase("fa-IR")]
        public void TheAttachmentName_IsTheGregorianDate_InAnyCulture(string Culture)
        {
            CultureInfo.CurrentCulture = new CultureInfo(Culture);
            Assert.That(ResultsEmail.AttachmentName(new DateTime(2026, 11, 22, 23, 59, 59)), Is.EqualTo("Campertron-results-2026-11-22.html"));
            Assert.That(ResultsEmail.AttachmentName(new DateTime(2027, 1, 5)), Is.EqualTo("Campertron-results-2027-01-05.html"));
        }

        [Test]
        public void ThePlainText_ShowsTheSameResults_OneFactPerLine()
        {
            var Search = OutputStreams.Search("Zion tents", OutputStreams.Header("Zion tents"),
                OutputStreams.Month(new DateTime(2026, 11, 1),
                    OutputStreams.Hit("111", "A023", new DateTime(2026, 11, 27), "Loop A", "STANDARD NONELECTRIC", "Tent", "RV"),
                    OutputStreams.Hit("222", "B007", new DateTime(2026, 11, 27), "", "TENT ONLY NONELECTRIC")),
                OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)));

            ResultsEmail.Content Content = Compose(Search);

            Assert.That(Content.Text, Is.EqualTo(string.Join("\r\n", new[]
            {
                "Campertron results",
                "",
                "Zion tents",
                "WATCHMAN CAMPGROUND · Zion National Park · Campground ID 232445",
                "2 availability entries for 2 sites on 1 date",
                "Check-in 12:00 PM · Check-out 11:00 AM for every site shown.",
                "",
                "November 2026",
                "=============",
                "",
                "Friday, 11/27/2026 · 2 sites",
                "",
                "  Site A023 · Loop A · STANDARD NONELECTRIC",
                "  Equipment: Tent, RV",
                "  Book: https://www.recreation.gov/camping/campsites/111",
                "",
                "  Site B007 · TENT ONLY NONELECTRIC",
                "  Equipment: Tent, CAR",
                "  Book: https://www.recreation.gov/camping/campsites/222",
                "",
                "December 2026",
                "=============",
                "No new matching availability to display.",
                "",
                "Results come from recreation.gov and can change at any time. The booking links open the campsite on recreation.gov.",
                ""
            })));
            Assert.That(Content.Text.Replace("\r\n", string.Empty), Does.Not.Contain("\n").And.Not.Contain("\r"), "CRLF line breaks only");
            Assert.That(Content.Html.Replace("\r\n", string.Empty), Does.Not.Contain("\n").And.Not.Contain("\r"));
        }

        [Test]
        public void ConsecutiveDays_OneCardPerSite_ItsDatesInOrder_InBothParts()
        {
            var Search = OutputStreams.Search("Two nights", OutputStreams.Header("Two nights", ConsecutiveDays: 2),
                OutputStreams.ConsecutiveSite(
                    OutputStreams.Hit("333", "C010", new DateTime(2026, 11, 28)),
                    OutputStreams.Hit("333", "C010", new DateTime(2026, 11, 27))));

            ResultsEmail.Content Content = Compose(Search);
            XDocument Html = Document(Content.Html);

            Assert.That(Html.Descendants("h3").Select(p => p.Value), Is.EqualTo(new[] { "Site C010" }));
            Assert.That(Html.Descendants("p").Select(p => p.Value), Does.Contain("Dates shown belong to runs of at least 2 consecutive available dates."));
            Assert.That(Html.Descendants("li").Select(p => p.Value), Is.EqualTo(new[] { "Friday, 11/27/2026", "Saturday, 11/28/2026" }));
            Assert.That(Html.Descendants("a").Select(p => (string)p.Attribute("href")!), Is.EqualTo(new[] { ResultsHtml.BookingUrlPrefix + "333" }));
            string[] Text = Content.Text.Split("\r\n");
            int Card = Array.IndexOf(Text, "Site C010");
            Assert.That(Text.Skip(Card).Take(6), Is.EqualTo(new[]
            {
                "Site C010", "  Loop C · STANDARD NONELECTRIC", "  Equipment: Tent, CAR", "  Available dates:", "  - Friday, 11/27/2026", "  - Saturday, 11/28/2026"
            }));
            Assert.That(Text[Card + 6], Is.EqualTo("  Book: " + ResultsHtml.BookingUrlPrefix + "333"));
        }

        [Test]
        public void NoNewAvailability_IsSaid_InBothParts()
        {
            ResultsEmail.Content Content = Compose(OutputStreams.Search("Quiet", OutputStreams.Header("Quiet")));

            Assert.That(Content.FullResultsInBody, Is.True);
            Assert.That(Document(Content.Html).Descendants("p").Select(p => p.Value), Does.Contain(ResultsHtml.EmptyMessage));
            Assert.That(Content.Text.Split("\r\n"), Does.Contain(ResultsHtml.EmptyMessage));
            Assert.That(Document(Content.Html).Descendants("a"), Is.Empty);
        }

        [Test]
        public void TheBody_IsWellFormed_AndTheResultsPageIsUnchanged()
        {
            var Search = ResultsEmails.Search("Pages", 5);
            string Page = ResultsHtml.Render(new[] { Search }, CultureInfo.CurrentCulture);

            ResultsEmail.Content Content = ResultsEmail.Compose(Search, Page, CultureInfo.CurrentCulture, Today);

            Assert.That(Content.Html, Does.StartWith("<!DOCTYPE html>\r\n<html lang=\"en\">"));
            Assert.That(() => Document(Content.Html), Throws.Nothing);
            Assert.That(Content.Html, Is.Not.EqualTo(Page), "the body is the email's own rendering");
            Assert.That(Page, Does.Contain("<section class=\"search\""), "the page (HtmlFile, the attachment) keeps its layout");
            Assert.That(Page, Does.Not.Contain("\r\n"));
        }
    }
}
