using System.Globalization;
using CampertronLibrary.function.Base;
using Calendar = CampertronLibrary.function.Base.Calendar;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // The console's results (ConsoleResults), rendered from the tagged streams of the production producers
    // (OutputStreams): exact text where the layout is the contract (plain-mode goldens, en-US, fixed dates), and
    // semantic assertions elsewhere. Also the calendar and the search-scope lines. In-memory only: no network, no
    // database, no user directories. The writer, the status lines and View Config are in ConsoleOutputTests.
    // A golden changes only together with an intended change of the presentation, in the same commit.
    public class ConsoleResultsTests
    {
        private CultureInfo OriginalCulture = null!;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private static readonly CultureInfo EnUs = new CultureInfo("en-US");

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = EnUs;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Err.Dispose();
            ConsoleUi.Current = OriginalCapabilities;
            CultureInfo.CurrentCulture = OriginalCulture;
        }

        // ---- fixtures ----

        internal static ConsoleConfig.ConsoleConfigItem Zion() => OutputStreams.Search("Zion tents",
            OutputStreams.Header("Zion tents"),
            OutputStreams.Month(new DateTime(2026, 11, 5),
                OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 13)),
                OutputStreams.Hit("370", "C003", new DateTime(2026, 11, 13)),
                OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 14))),
            OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)));

        // the dates in the order FilterConsecutiveDays can give them (not chronological)
        internal static ConsoleConfig.ConsoleConfigItem Mather() => OutputStreams.Search("Mather weekends",
            OutputStreams.Header("Mather weekends", "Grand Canyon National Park", "MATHER CAMPGROUND", "232490", 2),
            OutputStreams.ConsecutiveSite(
                OutputStreams.Hit("4384", "086", new DateTime(2026, 12, 5), "Fir Loop (Sites 60-106)", "RV NONELECTRIC"),
                OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 14), "Fir Loop (Sites 60-106)", "RV NONELECTRIC"),
                OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 13), "Fir Loop (Sites 60-106)", "RV NONELECTRIC")));

        internal static ConsoleConfig.ConsoleConfigItem Nothing() => OutputStreams.Search("Nothing",
            OutputStreams.Header("Nothing", "Some Park", "SOME CAMPGROUND", "1", 2));

        internal static string Text(IEnumerable<ConsoleUi.StyledLine> Lines) => string.Join("\n", Lines.Select(p => p.Text)) + "\n";
        private static string Render(int Width, bool Summary, params ConsoleConfig.ConsoleConfigItem[] Searches) =>
            Text(ConsoleResults.Render(Searches, CultureInfo.CurrentCulture, Width, Summary));

        internal const string GoldenRun =
            "== Mather weekends ==\n" +
            "MATHER CAMPGROUND, Grand Canyon National Park (ID 232490)\n" +
            "at least 2 consecutive nights | 3 openings at 1 site on 3 dates | check-in 12:00 PM, check-out 11:00 AM\n" +
            "Equipment (all sites): Tent, CAR\n" +
            "\n" +
            "  086  Fir Loop (Sites 60-106), RV NONELECTRIC\n" +
            "       https://www.recreation.gov/camping/campsites/4384\n" +
            "       Fri 11/13/2026, Sat 11/14/2026, Sat 12/5/2026\n" +
            "\n" +
            "  November 2026 (* = open)\n" +
            "   Mo  Tu  We  Th  Fr  Sa  Su\n" +
            "                            1\n" +
            "    2   3   4   5   6   7   8\n" +
            "    9  10  11  12 *13 *14  15\n" +
            "   16  17  18  19  20  21  22\n" +
            "   23  24  25  26  27  28  29\n" +
            "   30\n" +
            "\n" +
            "  December 2026 (* = open)\n" +
            "   Mo  Tu  We  Th  Fr  Sa  Su\n" +
            "        1   2   3   4  *5   6\n" +
            "    7   8   9  10  11  12  13\n" +
            "   14  15  16  17  18  19  20\n" +
            "   21  22  23  24  25  26  27\n" +
            "   28  29  30  31\n" +
            "\n" +
            "== Nothing ==\n" +
            "SOME CAMPGROUND, Some Park (ID 1)\n" +
            "No matching availability.\n" +
            "\n" +
            "== Zion tents ==\n" +
            "WATCHMAN CAMPGROUND, Zion National Park (ID 232445)\n" +
            "3 openings at 2 sites on 2 dates | check-in 12:00 PM, check-out 11:00 AM\n" +
            "Equipment (all sites): Tent, CAR\n" +
            "\n" +
            "Fri 11/13/2026 - 2 sites\n" +
            "  C002  Loop C, STANDARD NONELECTRIC\n" +
            "        https://www.recreation.gov/camping/campsites/369\n" +
            "  C003  Loop C, STANDARD NONELECTRIC\n" +
            "        https://www.recreation.gov/camping/campsites/370\n" +
            "Sat 11/14/2026 - 1 site\n" +
            "  C002  Loop C, STANDARD NONELECTRIC\n" +
            "        https://www.recreation.gov/camping/campsites/369\n" +
            "\n" +
            "  November 2026 (* = open)\n" +
            "   Mo  Tu  We  Th  Fr  Sa  Su\n" +
            "                            1\n" +
            "    2   3   4   5   6   7   8\n" +
            "    9  10  11  12 *13 *14  15\n" +
            "   16  17  18  19  20  21  22\n" +
            "   23  24  25  26  27  28  29\n" +
            "   30\n" +
            "\n" +
            "December 2026: no matching availability.\n" +
            "\n" +
            "Done: 3 searches, 6 openings at 3 sites. 1 search without availability.\n";

        // ---- the layout ----

        [Test]
        public void Run_PlainUnwrapped_IsExactlyTheGolden()
        {
            Assert.That(Render(int.MaxValue, true, Mather(), Nothing(), Zion()), Is.EqualTo(GoldenRun));
        }

        [Test]
        public void Run_At80Columns_WrapsOnlyTheListThatDoesNotFit()
        {
            string Expected = GoldenRun.Replace(
                "at least 2 consecutive nights | 3 openings at 1 site on 3 dates | check-in 12:00 PM, check-out 11:00 AM\n",
                "at least 2 consecutive nights | 3 openings at 1 site on 3 dates\n" +
                "  check-in 12:00 PM, check-out 11:00 AM\n");

            string Rendered = Render(80, true, Mather(), Nothing(), Zion());

            Assert.That(Rendered, Is.EqualTo(Expected));
            Assert.That(Rendered.Split('\n').All(p => p.Length <= 80), Is.True);
        }

        [Test]
        public void Run_At40Columns_WrapsListsWithAHangingIndent_AndNeverALink()
        {
            string Rendered = Render(40, true, Mather(), Nothing(), Zion());

            Assert.That(Rendered, Does.Contain(
                "at least 2 consecutive nights\n" +
                "  3 openings at 1 site on 3 dates\n" +
                "  check-in 12:00 PM, check-out 11:00 AM\n"));
            Assert.That(Rendered, Does.Contain(
                "       Fri 11/13/2026, Sat 11/14/2026,\n" +
                "       Sat 12/5/2026\n"));
            Assert.That(Rendered, Does.Contain(
                "3 openings at 2 sites on 2 dates\n" +
                "  check-in 12:00 PM, check-out 11:00 AM\n"));
            Assert.That(Rendered, Does.Contain("       https://www.recreation.gov/camping/campsites/4384\n"), "a link is one unbroken line");
            // the calendar fits a 40-column terminal
            Assert.That(Rendered.Split('\n').Where(p => p.StartsWith("   ") && p.Contains(" 2   3 ")).All(p => p.Length <= 40), Is.True);
            // unwrapping the wrapped text gives the unwrapped rendering: wrapping only moves line breaks
            Assert.That(Unwrap(Rendered), Is.EqualTo(Unwrap(GoldenRun)));
        }
        private static string Unwrap(string Rendered) =>
            string.Join(" ", Rendered.Split(new[] { ' ', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries));

        [Test]
        public void SingleSearch_HasNoRunSummary()
        {
            string Rendered = Render(int.MaxValue, false, Zion());

            Assert.That(Rendered, Does.StartWith("== Zion tents ==\n"));
            Assert.That(Rendered, Does.Not.Contain("Done:"));
            Assert.That(Rendered, Does.EndWith("December 2026: no matching availability.\n"));
        }

        [Test]
        public void TheTextIsPlain_NoTabsNoControlCharactersNoEmoji()
        {
            string Rendered = Render(int.MaxValue, true, Mather(), Nothing(), Zion());

            Assert.That(Rendered, Does.Not.Contain("\t"));
            Assert.That(Rendered.Where(c => c != '\n').All(c => c >= ' ' && c <= '~'), Is.True, "ASCII only around ASCII data: no escape sequence, no emoji, no control character");
        }

        // ---- rendered from the metadata ----

        [Test]
        public void Rendering_UsesTheMetadata_NotTheValuesText()
        {
            ConsoleConfig.ConsoleConfigItem Changed = Zion();
            foreach (ConsoleConfig.ConsoleConfigValue Value in Changed.Values!.Where(p => p.Kind != ConsoleConfig.OutputKind.Text))
            {
                Value.ConfigValue = "TEXT THAT MUST NOT BE SHOWN \t\u001b[31m";
            }

            Assert.That(Render(int.MaxValue, false, Changed), Is.EqualTo(Render(int.MaxValue, false, Zion())));
        }

        [Test]
        public void TheStreamsOwnCalendarValues_AreSkipped_TheCalendarIsDrawnFromTheResults()
        {
            // as AvailabilityApi adds it for Console output: after the month's hits, and after a consecutive campsite
            ConsoleConfig.ConsoleConfigItem WithCalendars = Zion();
            int AfterNovember = WithCalendars.Values!.FindIndex(p => p.Kind == ConsoleConfig.OutputKind.MonthHeader && p.Metadata!.Date == new DateTime(2026, 12, 1)) - 1;
            var CalendarValues = new List<ConsoleConfig.ConsoleConfigValue>();
            Calendar.GenerateCalendar(11, 2026, new List<DateTime> { new DateTime(2026, 11, 13), new DateTime(2026, 11, 14) }, ref CalendarValues);
            WithCalendars.Values.InsertRange(AfterNovember, CalendarValues);

            Assert.That(CalendarValues, Is.Not.Empty);
            Assert.That(Render(int.MaxValue, false, WithCalendars), Is.EqualTo(Render(int.MaxValue, false, Zion())));
        }

        [Test]
        public void ACalendarWithoutResults_IsNotShown()
        {
            // what TD-04 can leave in a consecutive-nights stream: another search's calendar with no campsite above it.
            // The console shows the stream's results only; this hides that symptom and does not fix TD-04.
            ConsoleConfig.ConsoleConfigItem Stream = Nothing();
            List<ConsoleConfig.ConsoleConfigValue> StreamValues = Stream.Values!;
            StreamValues.Add(CampsiteConfig.AddConsoleConfigItem(true));
            Calendar.GenerateCalendar(11, 2026, new List<DateTime> { new DateTime(2026, 11, 20) }, ref StreamValues);

            Assert.That(StreamValues.Count(p => p.Kind == ConsoleConfig.OutputKind.Calendar), Is.GreaterThan(30));
            Assert.That(Render(int.MaxValue, false, Stream), Is.EqualTo(Render(int.MaxValue, false, Nothing())));
        }

        [Test]
        public void UntypedText_IsKeptAsALine_BlankUntypedLinesAreDropped()
        {
            ConsoleConfig.ConsoleConfigItem Stream = Nothing();
            Stream.Values!.Add(CampsiteConfig.AddConsoleConfigItem("a note from another producer"));
            Stream.Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            Stream.Values.Add(CampsiteConfig.AddConsoleConfigItem("   ", true));
            Stream.Values.Add(CampsiteConfig.AddConsoleConfigItem(true));

            Assert.That(Render(int.MaxValue, false, Stream), Is.EqualTo(
                "== Nothing ==\nSOME CAMPGROUND, Some Park (ID 1)\nNo matching availability.\na note from another producer\n"));
        }

        // ---- data ----

        [Test]
        public void MissingValues_LeaveNoEmptyLabels()
        {
            // a campsite without loop, equipment or check-in data, and one without an ID
            var Month = AvailabilityApi.MonthHeaderValues(new DateTime(2026, 11, 1));
            Month.AddRange(AvailabilityApi.AvailabilityValues(new AvailabilityData { CampsiteID = "10206585", CampsiteName = "Site #1", CampsiteLoop = "", CampsiteType = "STANDARD NONELECTRIC", CampsiteAvailableDate = new DateTime(2026, 11, 20), PermittedEquipmentList = new List<string>() }, "", "", false));
            Month.AddRange(AvailabilityApi.AvailabilityValues(new AvailabilityData { CampsiteID = "", CampsiteName = "", CampsiteLoop = "", CampsiteType = "", CampsiteAvailableDate = new DateTime(2026, 11, 20), PermittedEquipmentList = new List<string>() }, "", "", false));
            ConsoleConfig.ConsoleConfigItem Stream = OutputStreams.Search("Bare", OutputStreams.Header("Bare", "", "", "999"), Month);

            string Rendered = Render(int.MaxValue, false, Stream);

            Assert.That(Rendered, Does.StartWith(
                "== Bare ==\n" +
                "Campground ID 999\n" +
                "2 openings at 2 sites on 1 date\n" +
                "\n" +
                "Fri 11/20/2026 - 2 sites\n" +
                "  Site #1   STANDARD NONELECTRIC\n" +
                "            https://www.recreation.gov/camping/campsites/10206585\n" +
                "  Campsite\n" +
                "            (no booking link available)\n"));
            Assert.That(Rendered, Does.Not.Contain("Equipment"));
            Assert.That(Rendered, Does.Not.Contain("check-in"));
            Assert.That(Rendered, Does.Not.Contain("check-out"));
        }

        [Test]
        public void EquipmentAndTimes_AreShownPerSite_WhenTheyDiffer()
        {
            var Month = AvailabilityApi.MonthHeaderValues(new DateTime(2026, 11, 1));
            Month.AddRange(AvailabilityApi.AvailabilityValues(OutputStreams.Hit("1", "A1", new DateTime(2026, 11, 20), "Loop A", "STANDARD", "Tent"), "12:00 PM", "11:00 AM", false));
            Month.AddRange(AvailabilityApi.AvailabilityValues(OutputStreams.Hit("2", "B22", new DateTime(2026, 11, 20), "Loop B", "RV", "RV", "Trailer"), "2:00 PM", "", false));
            ConsoleConfig.ConsoleConfigItem Stream = OutputStreams.Search("Mixed", OutputStreams.Header("Mixed"), Month);

            string Rendered = Render(int.MaxValue, false, Stream);

            Assert.That(Rendered, Does.Contain("2 openings at 2 sites on 1 date\n\n"), "no shared times or equipment in the search's summary");
            Assert.That(Rendered, Does.Not.Contain("Equipment (all sites)"));
            Assert.That(Rendered, Does.Contain(
                "  A1   Loop A, STANDARD\n" +
                "       https://www.recreation.gov/camping/campsites/1\n" +
                "       Equipment: Tent\n" +
                "       check-in 12:00 PM, check-out 11:00 AM\n" +
                "  B22  Loop B, RV\n" +
                "       https://www.recreation.gov/camping/campsites/2\n" +
                "       Equipment: RV, Trailer\n" +
                "       check-in 2:00 PM\n"));
        }

        [Test]
        public void LongAndNonAsciiValues_AreShownInFull_AndTheIdIsEscapedInTheLink()
        {
            string LongCampground = "Aquatic Park Cove, Black Point Overnight Anchoring, Sailing Vessels only (auxiliary engine OK)";
            string LongSite = "A site name that is much longer than the name column";
            ConsoleConfig.ConsoleConfigItem Stream = OutputStreams.Search("Piñon & Ünïcödé — trip",
                OutputStreams.Header("Piñon & Ünïcödé — trip", "San Francisco Maritime National Historical Park", LongCampground, "273757"),
                OutputStreams.Month(new DateTime(2026, 11, 1),
                    OutputStreams.Hit("10 4/3", LongSite, new DateTime(2026, 11, 20), "Aquatic Park Cove San Francisco Maritime NHP", "ANCHORAGE", "Boat"),
                    OutputStreams.Hit("7", "APA01", new DateTime(2026, 11, 20), "Aquatic Park Cove San Francisco Maritime NHP", "ANCHORAGE", "Boat")));

            string Unwrapped = Render(int.MaxValue, false, Stream);
            Assert.That(Unwrapped, Does.StartWith("== Piñon & Ünïcödé — trip ==\n" + LongCampground + ", San Francisco Maritime National Historical Park (ID 273757)\n"), "names are not shortened or re-encoded");
            Assert.That(Unwrapped, Does.Contain("  " + LongSite + "  Aquatic Park Cove San Francisco Maritime NHP, ANCHORAGE\n"));
            Assert.That(Unwrapped, Does.Contain("  APA01             Aquatic Park Cove San Francisco Maritime NHP, ANCHORAGE\n"), "the name column is capped, a long name does not push every site away");
            Assert.That(Unwrapped, Does.Contain("https://www.recreation.gov/camping/campsites/10%204%2F3\n"));

            foreach (int Width in new[] { 80, 40 })
            {
                string Rendered = Render(Width, false, Stream);

                Assert.That(Unwrap(Rendered), Is.EqualTo(Unwrap(Unwrapped)), "on a terminal the same words, wrapped at spaces");
                Assert.That(Rendered, Does.Contain("https://www.recreation.gov/camping/campsites/10%204%2F3\n"), "a link is never broken");
                Assert.That(Rendered.Split('\n').Where(p => !p.Contains("https://")).All(p => p.Length <= Width), Is.True, "every other line fits the terminal");
            }
        }

        [Test]
        public void ConsecutiveDates_AreInDateOrder_WhateverTheStreamOrder()
        {
            string Rendered = Render(int.MaxValue, false, Mather());

            Assert.That(Rendered, Does.Contain("       Fri 11/13/2026, Sat 11/14/2026, Sat 12/5/2026\n"));
            Assert.That(Rendered.IndexOf("November 2026 (", StringComparison.Ordinal), Is.LessThan(Rendered.IndexOf("December 2026 (", StringComparison.Ordinal)));
        }

        [Test]
        public void ASearchWithoutResults_SaysSoOnce()
        {
            ConsoleConfig.ConsoleConfigItem EmptyMonths = OutputStreams.Search("Empty months", OutputStreams.Header("Empty months"),
                OutputStreams.EmptyMonth(new DateTime(2026, 11, 1)), OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)));

            Assert.That(Render(int.MaxValue, true, EmptyMonths, Nothing()), Is.EqualTo(
                "== Empty months ==\n" +
                "WATCHMAN CAMPGROUND, Zion National Park (ID 232445)\n" +
                "No matching availability.\n" +
                "\n" +
                "== Nothing ==\n" +
                "SOME CAMPGROUND, Some Park (ID 1)\n" +
                "No matching availability.\n" +
                "\n" +
                "Done: 2 searches, no matching availability.\n"));
        }

        [Test]
        public void NoSearches_GivesOnlyTheSummary()
        {
            Assert.That(Render(int.MaxValue, true), Is.EqualTo("Done: 0 searches, no matching availability.\n"));
            Assert.That(Render(int.MaxValue, false), Is.EqualTo("\n"));
        }

        [Test]
        public void SummaryNameAndOpenings_MatchTheRendering()
        {
            var Searches = new[] { Mather(), Nothing(), Zion() };

            Assert.That(ConsoleResults.SummaryText(Searches, EnUs), Is.EqualTo("Done: 3 searches, 6 openings at 3 sites. 1 search without availability."));
            Assert.That(ConsoleResults.SummaryText(new[] { Zion() }, EnUs), Is.EqualTo("Done: 1 search, 3 openings at 2 sites."));
            Assert.That(ConsoleResults.SearchName(Zion()), Is.EqualTo("Zion tents"));
            Assert.That(ConsoleResults.Openings(Zion()), Is.EqualTo(3));
            Assert.That(ConsoleResults.Openings(Nothing()), Is.EqualTo(0));
            Assert.That(ConsoleResults.SearchName(new ConsoleConfig.ConsoleConfigItem { Values = new List<ConsoleConfig.ConsoleConfigValue>() }), Is.EqualTo("Search 1"));
        }

        [Test]
        public void DatesAndNumbers_FollowTheCulture()
        {
            CultureInfo German = new CultureInfo("de-DE");
            CultureInfo.CurrentCulture = German;   // the producers use the current culture

            string Rendered = Text(ConsoleResults.Render(new[] { Zion() }, German, int.MaxValue, true));

            Assert.That(Rendered, Does.Contain("Fr 13.11.2026 - 2 sites\n"));
            Assert.That(Rendered, Does.Contain("  November 2026 (* = open)\n"));
            Assert.That(Rendered, Does.Contain("Dezember 2026: no matching availability.\n"));
        }

        // ---- color ----

        [Test]
        public void Color_IsCarriedAsSpans_NeverAsText_AndMarksAreNotColorOnly()
        {
            List<ConsoleUi.StyledLine> Lines = ConsoleResults.Render(new[] { Zion() }, EnUs, int.MaxValue, true);

            ConsoleUi.StyledLine Title = Lines.Single(p => p.Text == "== Zion tents ==");
            Assert.That(Title.Spans.Single().Color, Is.EqualTo(ConsoleResults.TitleColor));
            ConsoleUi.StyledLine Date = Lines.Single(p => p.Text == "Fri 11/13/2026 - 2 sites");
            Assert.That(Date.Spans.Select(p => (p.Text, p.Color)), Is.EqualTo(new (string, ConsoleColor?)[] { ("Fri 11/13/2026", ConsoleResults.DateColor), (" - 2 sites", null) }));
            ConsoleUi.StyledLine Week = Lines.Single(p => p.Text == "    9  10  11  12 *13 *14  15");
            Assert.That(Week.Spans.Where(p => p.Color.HasValue).Select(p => p.Text), Is.EqualTo(new[] { "*13", "*14" }), "open days carry the mark with or without color");
            Assert.That(Lines.SelectMany(p => p.Spans).Where(p => p.Color.HasValue).Select(p => p.Color).Distinct(),
                Is.EquivalentTo(new ConsoleColor?[] { ConsoleResults.TitleColor, ConsoleResults.DateColor }));
            // readable on dark and light backgrounds: no blue, no dark gray, no dark red
            Assert.That(new[] { ConsoleResults.TitleColor, ConsoleResults.DateColor },
                Has.None.AnyOf(ConsoleColor.Blue, ConsoleColor.DarkBlue, ConsoleColor.DarkGray, ConsoleColor.DarkRed, ConsoleColor.Black, ConsoleColor.White));
        }

        // ---- the calendar ----

        [Test]
        public void Calendar_MarksOpenDays_AndStartsOnMonday()
        {
            // February 2027 starts on a Monday and has four full weeks; August 2026 ends on a Monday
            Assert.That(Text(Calendar.Lines(2027, 2, new[] { new DateTime(2027, 2, 1), new DateTime(2027, 2, 28), new DateTime(2027, 3, 1) }, EnUs, "", ConsoleColor.Cyan)), Is.EqualTo(
                "February 2027 (* = open)\n" +
                " Mo  Tu  We  Th  Fr  Sa  Su\n" +
                " *1   2   3   4   5   6   7\n" +
                "  8   9  10  11  12  13  14\n" +
                " 15  16  17  18  19  20  21\n" +
                " 22  23  24  25  26  27 *28\n"));
            Assert.That(Text(Calendar.Lines(2026, 8, Array.Empty<DateTime>(), EnUs, "> ", ConsoleColor.Cyan)), Is.EqualTo(
                "> August 2026 (* = open)\n" +
                ">  Mo  Tu  We  Th  Fr  Sa  Su\n" +
                ">                       1   2\n" +
                ">   3   4   5   6   7   8   9\n" +
                ">  10  11  12  13  14  15  16\n" +
                ">  17  18  19  20  21  22  23\n" +
                ">  24  25  26  27  28  29  30\n" +
                ">  31\n"));
        }

        // ---- the searches that run ----

        internal static CampertronConfig Search(string Name, string? CampgroundId, SearchTypes By, int Value, List<string>? Dates, int Consecutive = 1, int? Humans = 2, bool Weekend = false)
        {
            var Config = new CampertronConfig
            {
                DisplayName = Name, AutoRun = true, CampgroundID = CampgroundId, TotalHumans = Humans, SearchBy = By, SearchValue = Value, SearchValueDates = Dates,
                ShowMonday = !Weekend, ShowTuesday = !Weekend, ShowWednesday = !Weekend, ShowThursday = !Weekend, ShowFriday = true, ShowSaturday = true, ShowSunday = !Weekend,
                IncludeEquipment = new(), ExcludeEquipment = new(), IncludeSites = new(), ExcludeSites = new(),
                IncludeAttributes = new(), ExcludeAttributes = new(), IncludeCampsiteType = new(), ExcludeCampsiteType = new(),
                ConsecutiveDays = Consecutive
            };
            Config.GenerateSearchData();
            return Config;
        }
        internal static ReturnParkCampground? CampInfo(string Id) => Id switch
        {
            "232445" => new ReturnParkCampground { ParkName = "Zion National Park", CampsiteName = "WATCHMAN CAMPGROUND" },
            "232490" => new ReturnParkCampground { ParkName = "Grand Canyon National Park", CampsiteName = "MATHER CAMPGROUND" },
            "0" => new ReturnParkCampground(),
            _ => null
        };

        [Test]
        public void SearchScope_ListsEverySearchByName_WithItsCampgroundDatesAndRules()
        {
            CampertronConfig Zion_ = Search("Zion tents", "232445", SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-31" });
            Zion_.IncludeEquipment = new List<string> { "Tent" };
            Zion_.ExcludeCampsiteType = new List<string> { "GROUP" };
            CampertronConfig Mather_ = Search("Mather weekends", "232490", SearchTypes.SpecificDates, 0, new List<string> { "2027-03-05", "2027-03-12" }, Consecutive: 2, Humans: 1, Weekend: true);

            ConsoleResults.Scope Scope = ConsoleResults.DescribeSearches(new[] { Zion_, Mather_ }, CampInfo, EnUs, int.MaxValue);

            Assert.That(Text(Scope.Lines), Is.EqualTo(
                "Running 2 searches:\n" +
                "\n" +
                "  Mather weekends\n" +
                "    MATHER CAMPGROUND, Grand Canyon National Park (ID 232490)\n" +
                "    2 dates from 3/5/2027 to 3/12/2027 | Fri, Sat | at least 2 consecutive nights | 1 person\n" +
                "\n" +
                "  Zion tents\n" +
                "    WATCHMAN CAMPGROUND, Zion National Park (ID 232445)\n" +
                "    3/1/2027 to 3/31/2027 | every day | 2 people | 2 filters\n"));
            Assert.That(Scope.Warnings, Is.Empty);
            Assert.That(Scope.Lines.Where(p => p.Spans.Any(s => s.Color.HasValue)).Select(p => p.Text), Is.EqualTo(new[] { "  Mather weekends", "  Zion tents" }));
        }

        [Test]
        public void SearchScope_WrapsTheRules_AtTheTerminalWidth()
        {
            CampertronConfig Mather_ = Search("Mather weekends", "232490", SearchTypes.SpecificDates, 0, new List<string> { "2027-03-05", "2027-03-12" }, Consecutive: 2, Weekend: true);

            string Scope = Text(ConsoleResults.DescribeSearches(new[] { Mather_ }, CampInfo, EnUs, 60).Lines);

            Assert.That(Scope, Does.StartWith("Running 1 search:\n"));
            Assert.That(Scope, Does.EndWith(
                "    2 dates from 3/5/2027 to 3/12/2027 | Fri, Sat\n" +
                "      at least 2 consecutive nights | 2 people\n"));
        }

        [Test]
        public void SearchScope_WarnsAboutSearchesThatCannotFindAnything()
        {
            var Searches = new[]
            {
                Search("A no campground", null, SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-02" }),
                Search("B unknown campground", "999999999", SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-02" }),
                Search("C disabled campground", "0", SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-02" }),
                Search("D nobody", "232445", SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-02" }, Humans: null),
                Search("E zero nights", "232445", SearchTypes.StartEndDate, 0, new List<string> { "2027-03-01", "2027-03-02" }, Consecutive: 0),
                Search("F no dates", "232445", SearchTypes.StartEndDate, 0, null),
            };

            ConsoleResults.Scope Scope = ConsoleResults.DescribeSearches(Searches, CampInfo, EnUs, int.MaxValue);
            string Lines = Text(Scope.Lines);

            Assert.That(Scope.Warnings, Has.Count.EqualTo(6));
            Assert.That(Scope.Warnings[0], Does.StartWith("search \"A no campground\" has no campgroundID"));
            Assert.That(Scope.Warnings[1], Does.StartWith("search \"B unknown campground\": campground ID 999999999 is not in the campground list."));
            Assert.That(Scope.Warnings[2], Does.StartWith("search \"C disabled campground\": campground ID 0 is not in the campground list."));
            Assert.That(Scope.Warnings[3], Is.EqualTo("search \"D nobody\" has no totalHumans, so no campsite can match."));
            Assert.That(Scope.Warnings[4], Does.StartWith("search \"E zero nights\": consecutiveDays is 0, so no result is shown."));
            Assert.That(Scope.Warnings[5], Does.StartWith("search \"F no dates\" has no search dates"));
            Assert.That(Lines, Does.Contain("  A no campground\n    (no campground)\n"));
            Assert.That(Lines, Does.Contain("  B unknown campground\n    Campground ID 999999999 (not in the campground list)\n"));
            Assert.That(Lines, Does.Contain("  F no dates\n    WATCHMAN CAMPGROUND, Zion National Park (ID 232445)\n    no dates | every day | 2 people\n"));
        }
    }
}
