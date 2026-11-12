using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ResultsHtml renders the HTML results page from the tagged output streams. The structural tests parse the page
    // with System.Xml.Linq: the generated markup is kept XML-compatible for that, but a successful parse only shows
    // the structure asserted here, not full HTML validity. Cultures are set explicitly (producers and renderer).
    public class ResultsHtmlTests
    {
        private static readonly CultureInfo EnUs = new CultureInfo("en-US");
        private static readonly DateTimeOffset Generated = new DateTimeOffset(2026, 11, 12, 7, 5, 0, TimeSpan.FromHours(-7));
        private CultureInfo? OriginalCulture;

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = EnUs;
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture!;
        }

        private static string Render(params ConsoleConfig.ConsoleConfigItem[] Searches) => ResultsHtml.Render(Searches, Generated, EnUs);

        private static XDocument Parse(string Html)
        {
            var Settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore };
            using var Reader = XmlReader.Create(new StringReader(Html), Settings);
            return XDocument.Load(Reader);
        }

        private static IEnumerable<XElement> All(XDocument Doc, string Name) => Doc.Descendants(Name);
        private static string? Attr(XElement E, string Name) => E.Attribute(Name)?.Value;

        private static ConsoleConfig.ConsoleConfigItem ZionSearch(string Name = "Zion tents") =>
            OutputStreams.Search(Name,
                OutputStreams.Header(Name),
                OutputStreams.Month(new DateTime(2026, 11, 1),
                    OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 13)),
                    OutputStreams.Hit("388", "B033", new DateTime(2026, 11, 13), "Loop B", "STANDARD ELECTRIC"),
                    OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 14))));

        // ---- document shell ----

        [Test]
        public void Document_HasTheHtml5Shell()
        {
            string Html = Render(ZionSearch());
            XDocument Doc = Parse(Html);

            Assert.That(Html, Does.StartWith("<!DOCTYPE html>\n"));
            Assert.That(Doc.Root!.Name.LocalName, Is.EqualTo("html"));
            Assert.That(Attr(Doc.Root, "lang"), Is.EqualTo("en"));
            XElement Head = Doc.Root.Element("head")!;
            Assert.That(Head.Elements("meta").Any(m => Attr(m, "charset") == "utf-8"), Is.True);
            Assert.That(Head.Elements("meta").Any(m => Attr(m, "name") == "viewport" && Attr(m, "content") == "width=device-width, initial-scale=1"), Is.True);
            Assert.That(Head.Elements("meta").Any(m => Attr(m, "name") == "color-scheme" && Attr(m, "content") == "light dark"), Is.True);
            Assert.That(Head.Element("title")!.Value, Is.EqualTo("Campertron results"));
        }

        [Test]
        public void Document_HasLandmarksOneH1AndNoSkippedHeadingLevels()
        {
            XDocument Doc = Parse(Render(ZionSearch("A"), ZionSearch("B")));
            XElement Body = Doc.Root!.Element("body")!;

            Assert.That(Body.Elements().Select(e => e.Name.LocalName), Is.EqualTo(new[] { "header", "main", "footer" }));
            Assert.That(All(Doc, "h1").Count(), Is.EqualTo(1));
            Assert.That(All(Doc, "nav").Single().Parent!.Name.LocalName, Is.EqualTo("header"));
            Assert.That(Attr(All(Doc, "nav").Single(), "aria-label"), Is.EqualTo("Searches"));

            int Previous = 0;
            foreach (XElement Heading in Body.Descendants().Where(e => e.Name.LocalName.Length == 2 && e.Name.LocalName[0] == 'h' && char.IsDigit(e.Name.LocalName[1])))
            {
                int Level = Heading.Name.LocalName[1] - '0';
                Assert.That(Level, Is.LessThanOrEqualTo(Previous + 1), Heading.ToString());
                Previous = Level;
            }
        }

        [Test]
        public void Generated_IsMachineReadableUtcAndShownInTheGivenCulture()
        {
            XElement Time = Parse(Render(ZionSearch())).Descendants("header").Descendants("time").Single();

            Assert.That(Attr(Time, "datetime"), Is.EqualTo("2026-11-12T14:05:00Z"));
            // local time in the given culture ("g"); ICU may put a narrow no-break space before AM
            Assert.That(Time.Value, Is.EqualTo(Generated.ToString("g", EnUs)));
            Assert.That(Time.Value, Does.StartWith("11/12/2026 7:05"));
        }

        // ---- searches ----

        [Test]
        public void Searches_OneSectionEach_InTheSuppliedOrder_WithTheirOwnResults()
        {
            var Third = OutputStreams.Search("Charlie", OutputStreams.Header("Charlie"), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("3", "C3", new DateTime(2026, 11, 20))));
            var First = OutputStreams.Search("Alpha", OutputStreams.Header("Alpha"), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("1", "A1", new DateTime(2026, 11, 20))));
            var Second = OutputStreams.Search("Bravo", OutputStreams.Header("Bravo"), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("2", "B2", new DateTime(2026, 11, 20))));
            XDocument Doc = Parse(Render(Third, First, Second));

            List<XElement> Sections = All(Doc, "section").ToList();
            Assert.That(Sections.Select(s => s.Element("h2")!.Value), Is.EqualTo(new[] { "Charlie", "Alpha", "Bravo" }));
            Assert.That(Sections.Select(s => Attr(s, "id")), Is.EqualTo(new[] { "search-1", "search-2", "search-3" }));
            Assert.That(Sections.Select(s => Attr(s, "aria-labelledby")), Is.EqualTo(new[] { "search-1-title", "search-2-title", "search-3-title" }));
            Assert.That(Sections.Select(s => s.Descendants("a").Single().Attribute("href")!.Value), Is.EqualTo(new[]
            {
                ResultsHtml.BookingUrlPrefix + "3", ResultsHtml.BookingUrlPrefix + "1", ResultsHtml.BookingUrlPrefix + "2"
            }));
            Assert.That(All(Doc, "nav").Single().Descendants("a").Select(a => Attr(a, "href") + " " + a.Value),
                        Is.EqualTo(new[] { "#search-1 Charlie", "#search-2 Alpha", "#search-3 Bravo" }));
        }

        [Test]
        public void SingleSearch_HasNoNavigation_AndFallsBackToTheItemName()
        {
            var Untitled = OutputStreams.Search("From item name", OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("1", "A1", new DateTime(2026, 11, 20))));
            XDocument Doc = Parse(Render(Untitled));

            Assert.That(All(Doc, "nav"), Is.Empty);
            Assert.That(All(Doc, "h2").Single().Value, Is.EqualTo("From item name"));
            Assert.That(Parse(Render(OutputStreams.Search(null))).Descendants("h2").Single().Value, Is.EqualTo("Search 1"));
        }

        // ---- ordinary (single-night) results ----

        [Test]
        public void OrdinaryResults_GroupedByMonthAndDate_InStreamOrder()
        {
            XDocument Doc = Parse(Render(ZionSearch()));
            XElement Section = All(Doc, "section").Single();

            Assert.That(Section.Element("p")!.Value, Is.EqualTo("WATCHMAN CAMPGROUND · Zion National Park · Campground ID 232445"));
            Assert.That(Section.Elements("p").Single(p => Attr(p, "class") == "summary").Value, Is.EqualTo("3 availability entries for 2 sites on 2 dates"));
            Assert.That(Section.Elements("p").Any(p => p.Value == "Check-in 12:00 PM · Check-out 11:00 AM for every site shown."), Is.True);
            Assert.That(Section.Elements("h3").Single().Value, Is.EqualTo("November 2026"));

            List<XElement> Dates = Section.Elements("h4").ToList();
            Assert.That(Dates.Select(h => h.Value), Is.EqualTo(new[] { "Friday, 11/13/2026 · 2 sites", "Saturday, 11/14/2026 · 1 site" }));
            Assert.That(Dates.Select(h => Attr(h.Element("time")!, "datetime")), Is.EqualTo(new[] { "2026-11-13", "2026-11-14" }));

            List<XElement> Lists = Section.Elements("ul").ToList();
            Assert.That(Lists.Select(l => l.Elements("li").Count()), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(Lists[0].Elements("li").Select(li => li.Element("strong")!.Value), Is.EqualTo(new[] { "Site C002", "Site B033" }));
            XElement First = Lists[0].Elements("li").First();
            Assert.That(First.Value, Does.Contain("Loop C · STANDARD NONELECTRIC"));
            Assert.That(First.Value, Does.Contain("Equipment: Tent, CAR"));
        }

        [Test]
        public void BookingLinks_UseTheCampsiteIdAndDescriptiveText()
        {
            List<XElement> Links = Parse(Render(ZionSearch())).Descendants("main").Descendants("a").ToList();

            Assert.That(Links.Select(a => Attr(a, "href")), Is.EqualTo(new[]
            {
                "https://www.recreation.gov/camping/campsites/369", "https://www.recreation.gov/camping/campsites/388", "https://www.recreation.gov/camping/campsites/369"
            }));
            Assert.That(Links.Select(a => a.Value), Is.EqualTo(new[] { "Book Site C002 on recreation.gov", "Book Site B033 on recreation.gov", "Book Site C002 on recreation.gov" }));
        }

        [Test]
        public void SiteNames_AlreadyStartingWithSite_AreNotPrefixedTwice()
        {
            var Search = OutputStreams.Search("x", OutputStreams.Header("x"), OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("7", "Site 60 (Pole 10)", new DateTime(2026, 11, 20))));
            XElement Link = Parse(Render(Search)).Descendants("main").Descendants("a").Single();
            Assert.That(Link.Value, Is.EqualTo("Book Site 60 (Pole 10) on recreation.gov"));
        }

        [Test]
        public void DifferentCheckinTimes_AreShownPerSite()
        {
            var Values = OutputStreams.Header("x");
            Values.AddRange(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.MonthHeaderValues(new DateTime(2026, 11, 1)));
            Values.AddRange(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.AvailabilityValues(OutputStreams.Hit("1", "A", new DateTime(2026, 11, 20)), "12:00 PM", "11:00 AM", false));
            Values.AddRange(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.AvailabilityValues(OutputStreams.Hit("2", "B", new DateTime(2026, 11, 20)), "2:00 PM", "", false));
            XDocument Doc = Parse(Render(new ConsoleConfig.ConsoleConfigItem { Name = "x", Values = Values }));

            Assert.That(All(Doc, "p").Any(p => p.Value.EndsWith("for every site shown.")), Is.False);
            List<XElement> Sites = All(Doc, "li").Where(li => Attr(li, "class") == "site").ToList();
            Assert.That(Sites[0].Value, Does.Contain("Check-in 12:00 PM · Check-out 11:00 AM"));
            Assert.That(Sites[1].Value, Does.Contain("Check-in 2:00 PM"));
            Assert.That(Sites[1].Value, Does.Not.Contain("Check-out"));
        }

        // ---- consecutive-day results ----

        [Test]
        public void ConsecutiveResults_OneCardPerSite_WithItsDatesChronologically()
        {
            var Search = OutputStreams.Search("Mather weekends",
                OutputStreams.Header("Mather weekends", "Grand Canyon National Park", "MATHER CAMPGROUND", "232490", 2),
                OutputStreams.ConsecutiveSite(OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 14), "Fir Loop", "RV NONELECTRIC"),
                                              OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 13), "Fir Loop", "RV NONELECTRIC")),
                OutputStreams.ConsecutiveSite(OutputStreams.Hit("4398", "114", new DateTime(2026, 11, 20), "Juniper Loop", "RV NONELECTRIC"),
                                              OutputStreams.Hit("4398", "114", new DateTime(2026, 11, 21), "Juniper Loop", "RV NONELECTRIC")));
            XElement Section = Parse(Render(Search)).Descendants("section").Single();

            Assert.That(Section.Elements("p").Any(p => p.Value == "Dates shown belong to runs of at least 2 consecutive available dates."), Is.True);
            Assert.That(Section.Elements("p").Single(p => Attr(p, "class") == "summary").Value, Is.EqualTo("4 availability entries for 2 sites on 4 dates"));
            List<XElement> Cards = Section.Element("ul")!.Elements("li").ToList();
            Assert.That(Cards.Select(c => c.Element("h3")!.Value), Is.EqualTo(new[] { "Site 086", "Site 114" }));
            Assert.That(Cards[0].Descendants("time").Select(t => Attr(t, "datetime") + " " + t.Value), Is.EqualTo(new[]
            {
                "2026-11-13 Friday, 11/13/2026", "2026-11-14 Saturday, 11/14/2026"
            }));
            Assert.That(Cards[0].Value, Does.Contain("Fir Loop · RV NONELECTRIC"));
            Assert.That(Cards[0].Descendants("a").Single().Attribute("href")!.Value, Is.EqualTo(ResultsHtml.BookingUrlPrefix + "4384"));
            Assert.That(Cards[1].Descendants("a").Single().Value, Is.EqualTo("Book Site 114 on recreation.gov"));
        }

        // ---- empty states ----

        [Test]
        public void EmptyOrdinarySearch_ShowsTheMessageUnderEachMonth()
        {
            var Search = OutputStreams.Search("Nothing", OutputStreams.Header("Nothing"), OutputStreams.EmptyMonth(new DateTime(2026, 11, 1)), OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)));
            XElement Section = Parse(Render(Search)).Descendants("section").Single();

            Assert.That(Section.Elements().Select(e => e.Name.LocalName + ":" + e.Value).Skip(2), Is.EqualTo(new[]
            {
                "h3:November 2026", "p:" + ResultsHtml.EmptyMessage, "h3:December 2026", "p:" + ResultsHtml.EmptyMessage
            }));
            Assert.That(Section.Elements("p").Count(p => Attr(p, "class") == "empty"), Is.EqualTo(2));
            Assert.That(Section.Elements("p").Any(p => Attr(p, "class") == "summary"), Is.False);
        }

        [Test]
        public void EmptyConsecutiveSearch_ShowsTheMessageOnce()
        {
            var Search = OutputStreams.Search("Nothing", OutputStreams.Header("Nothing", ConsecutiveDays: 3));
            XElement Section = Parse(Render(Search)).Descendants("section").Single();

            Assert.That(Section.Elements("p").Where(p => Attr(p, "class") == "empty").Select(p => p.Value), Is.EqualTo(new[] { "No new matching availability to display." }));
        }

        [Test]
        public void EmptySearch_IsNamedInTheNavigation()
        {
            var Empty = OutputStreams.Search("Nothing", OutputStreams.Header("Nothing", ConsecutiveDays: 2));
            XElement Nav = Parse(Render(ZionSearch(), Empty)).Descendants("nav").Single();
            Assert.That(Nav.Elements("ul").Elements("li").Select(li => li.Value), Is.EqualTo(new[]
            {
                "Zion tents · 3 availability entries", "Nothing · no new matching availability"
            }));
        }

        [Test]
        public void NoSearches_ShowTheEmptyMessage()
        {
            XDocument Doc = Parse(ResultsHtml.Render(Array.Empty<ConsoleConfig.ConsoleConfigItem>(), Generated, EnUs));
            Assert.That(Doc.Descendants("main").Single().Elements().Select(e => e.Name.LocalName + ":" + e.Value), Is.EqualTo(new[] { "p:" + ResultsHtml.EmptyMessage }));
        }

        // ---- encoding ----

        [Test]
        public void DynamicValues_AreEncoded_NeverInterpretedAsMarkup()
        {
            const string Name = "Watchman & South \"all\" <any> it's";
            var Search = OutputStreams.Search(Name,
                OutputStreams.Header(Name, "Arapaho & Roosevelt <National> Forests", "Zephyr Cove RV & \"Campground\"", "1<2"),
                OutputStreams.Month(new DateTime(2026, 11, 1),
                    OutputStreams.Hit("12&3", "A&B \"x\" 'y' <b>", new DateTime(2026, 11, 20), "<30ft RV", "RV <ELECTRIC> & more", "<script>alert(1)</script>", "Tent & Trailer")));
            string Html = Render(Search, ZionSearch());
            XDocument Doc = Parse(Html);

            Assert.That(Html, Does.Not.Contain("<any>"));
            Assert.That(Html, Does.Not.Contain("<b>"));
            Assert.That(Html, Does.Not.Contain("<30ft"));
            Assert.That(Html, Does.Not.Contain("<script"));
            Assert.That(Html, Does.Contain("&lt;any&gt;"));
            Assert.That(Html, Does.Contain("&lt;30ft RV"));
            Assert.That(Html, Does.Contain("it&#39;s"));
            Assert.That(Html, Does.Contain("&quot;all&quot;"));
            Assert.That(Doc.Descendants().Select(e => e.Name.LocalName), Has.None.EqualTo("any").And.None.EqualTo("b").And.None.EqualTo("script").And.None.EqualTo("National"));

            XElement Section = Doc.Descendants("section").First();
            Assert.That(Section.Element("h2")!.Value, Is.EqualTo(Name));
            Assert.That(Section.Element("p")!.Value, Is.EqualTo("Zephyr Cove RV & \"Campground\" · Arapaho & Roosevelt <National> Forests · Campground ID 1<2"));
            XElement Site = Section.Descendants("li").Single();
            Assert.That(Site.Element("strong")!.Value, Is.EqualTo("Site A&B \"x\" 'y' <b>"));
            Assert.That(Site.Value, Does.Contain("<30ft RV · RV <ELECTRIC> & more"));
            Assert.That(Site.Value, Does.Contain("Equipment: <script>alert(1)</script>, Tent & Trailer"));
            XElement Link = Site.Descendants("a").Single();
            Assert.That(Attr(Link, "href"), Is.EqualTo(ResultsHtml.BookingUrlPrefix + "12%263"));
            Assert.That(Link.Value, Is.EqualTo("Book Site A&B \"x\" 'y' <b> on recreation.gov"));
            Assert.That(Doc.Descendants("nav").Single().Descendants("a").First().Value, Is.EqualTo(Name));
        }

        [Test]
        public void LongNamesAndContent_AreKeptWhole_AndCanWrap()
        {
            string Long = string.Concat(Enumerable.Repeat("Very long campground display name without natural breaks ", 8)) + new string('X', 200);
            var Search = OutputStreams.Search(Long, OutputStreams.Header(Long, Long, Long),
                OutputStreams.Month(new DateTime(2026, 11, 1), OutputStreams.Hit("1", Long, new DateTime(2026, 11, 20), Long, Long, Enumerable.Repeat("LARGE TENT OVER 9X12`", 40).ToArray())));
            string Html = Render(Search);
            XDocument Doc = Parse(Html);

            Assert.That(Doc.Descendants("h2").Single().Value, Is.EqualTo(Long.Trim()));
            Assert.That(Doc.Descendants("strong").Single().Value, Is.EqualTo("Site " + Long.Trim()));
            Assert.That(Html, Does.Contain("overflow-wrap:anywhere"));
        }

        // ---- dates ----

        [Test]
        public void Dates_FollowTheGivenCulture_WithTheWeekday()
        {
            CultureInfo German = new CultureInfo("de-DE");
            if (German.DateTimeFormat.GetDayName(DayOfWeek.Friday) != "Freitag")
            {
                Assert.Ignore("culture data not available (invariant globalization)");
            }
            XDocument Doc = Parse(ResultsHtml.Render(new[] { ZionSearch() }, Generated, German));

            Assert.That(Doc.Descendants("h3").First().Value, Is.EqualTo("November 2026"));
            Assert.That(Doc.Descendants("h4").Select(h => h.Element("time")!.Value), Is.EqualTo(new[] { "Freitag, 13.11.2026", "Samstag, 14.11.2026" }));
            Assert.That(Doc.Descendants("h4").Select(h => Attr(h.Element("time")!, "datetime")), Is.EqualTo(new[] { "2026-11-13", "2026-11-14" }));
            Assert.That(ResultsHtml.DateText(new DateTime(2026, 11, 13), EnUs), Is.EqualTo("Friday, 11/13/2026"));
        }

        // ---- untyped values (fallback) ----

        [Test]
        public void UntypedValues_NonblankLinesAreKeptEncoded_BlankLinesDropped()
        {
            var Values = new List<ConsoleConfig.ConsoleConfigValue>
            {
                CampsiteConfig.AddConsoleConfigItem(true),
                CampsiteConfig.AddConsoleConfigItem("\tPart one & ", true),
                CampsiteConfig.AddConsoleConfigItem("<part two>", ConsoleColor.Blue, true),
                CampsiteConfig.AddConsoleConfigItem(true),
                CampsiteConfig.AddConsoleConfigItem(true),
                CampsiteConfig.AddConsoleConfigItem("   "),
                CampsiteConfig.AddConsoleConfigItem("Second line", ConsoleColor.Red),
                CampsiteConfig.AddConsoleConfigItem(true)
            };
            XElement Section = Parse(Render(new ConsoleConfig.ConsoleConfigItem { Name = "Legacy", Values = Values })).Descendants("section").Single();

            Assert.That(Section.Elements("p").Where(p => Attr(p, "class") == "note").Select(p => p.Value), Is.EqualTo(new[] { "Part one & <part two>", "Second line" }));
            Assert.That(Section.Elements("p").Where(p => Attr(p, "class") == "empty").Select(p => p.Value), Is.EqualTo(new[] { ResultsHtml.EmptyMessage }));
        }

        [Test]
        public void ConsoleDecorations_AreNotRendered()
        {
            string Html = Render(ZionSearch(), OutputStreams.Search("m", OutputStreams.Header("m", ConsecutiveDays: 2),
                OutputStreams.ConsecutiveSite(OutputStreams.Hit("1", "A", new DateTime(2026, 11, 20)), OutputStreams.Hit("1", "A", new DateTime(2026, 11, 21)))),
                OutputStreams.Search("e", OutputStreams.Header("e"), OutputStreams.EmptyMonth(new DateTime(2026, 12, 1))));

            foreach (string Decoration in new[] { "🌲", "🌳", "🔎", "📆", "➰", "***", "Searching for entries", "No entries found", "\tDate:", "URL:" })
            {
                Assert.That(Html, Does.Not.Contain(Decoration));
            }
        }

        // ---- self-contained page and CSS capabilities ----

        [Test]
        public void Page_IsSelfContained()
        {
            string Html = Render(ZionSearch(), ZionSearch("Second"));
            XDocument Doc = Parse(Html);

            Assert.That(Html, Does.Not.Contain("<script").IgnoreCase);
            Assert.That(Html, Does.Not.Contain("@import").IgnoreCase);
            Assert.That(Html, Does.Not.Contain("url(").IgnoreCase);
            Assert.That(Doc.Descendants().Select(e => e.Name.LocalName), Has.None.EqualTo("script").And.None.EqualTo("link").And.None.EqualTo("img").And.None.EqualTo("iframe"));
            Assert.That(Doc.Descendants().Where(e => e.Attribute("src") != null), Is.Empty);
            Assert.That(Doc.Descendants().Where(e => e.Name.LocalName.StartsWith("on") || e.Attributes().Any(a => a.Name.LocalName.StartsWith("on"))), Is.Empty);
            Assert.That(Doc.Descendants("a").Select(a => Attr(a, "href")!),
                        Has.All.Matches<string>(h => h.StartsWith("#search-") || h.StartsWith(ResultsHtml.BookingUrlPrefix)));
        }

        [Test]
        public void Css_HasTheNarrowWidthDarkSchemePrintAndFocusRules()
        {
            string Css = Parse(Render(ZionSearch())).Descendants("style").Single().Value;

            Assert.That(Css, Does.Contain("color-scheme:light dark"));
            Assert.That(Css, Does.Contain("@media (prefers-color-scheme: dark)"));
            Assert.That(Css, Does.Contain("@media (max-width: 40rem)"));
            Assert.That(Css, Does.Contain("@media print"));
            Assert.That(Css, Does.Contain(":focus-visible"));
            Assert.That(Css, Does.Contain("system-ui"));
            Assert.That(Css, Does.Contain("max-width:76rem"));
        }
    }
}
