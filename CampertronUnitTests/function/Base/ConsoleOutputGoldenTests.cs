using System.Globalization;
using CampertronLibrary.function.Base;
using Calendar = CampertronLibrary.function.Base.Calendar;

namespace CampertronUnitTests.function.Base
{
    // The producers' values (text, write types, colors) are a kept contract: beta.9 tagged them with presentation
    // metadata without changing them, and the HTML results page and the console results are rendered from that
    // metadata. Since the console-output polish the console no longer writes this text (ConsoleResults renders the
    // metadata; ConsoleResultsTests), so the text is pinned here at the value level: Golden is what the values spell
    // when written one after another, byte for byte the console output before the tagging (master da9484c). en-US is
    // set explicitly because the month name and short date follow the current culture.
    public class ConsoleOutputGoldenTests
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

        private static List<ConsoleConfig.ConsoleConfigValue> RepresentativeStream() =>
            new[]
            {
                OutputStreams.Header("Zion tents"),
                OutputStreams.Month(new DateTime(2026, 11, 5), OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 13))),
                OutputStreams.EmptyMonth(new DateTime(2026, 12, 1)),
                OutputStreams.ConsecutiveSite(OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 14), "Fir Loop (Sites 60-106)", "RV NONELECTRIC"),
                                              OutputStreams.Hit("4384", "086", new DateTime(2026, 11, 13), "Fir Loop (Sites 60-106)", "RV NONELECTRIC"))
            }.SelectMany(p => p).ToList();

        private const string Golden =
            "\n" +
            "*** Zion National Park 🌲 - WATCHMAN CAMPGROUND - 🌳 Zion tents ***\n" +
            "\n" +
            "Searching for entries during November/2026 🔎\n" +
            "\n" +
            "\tDate:\t   11/13/2026 (Friday) 📆 Checkin:12:00 PM CheckOut:11:00 AM\n" +
            "\tSite:\t   C002 ➰ Loop C (STANDARD NONELECTRIC)\n" +
            "\tEquipment: Tent,CAR\n" +
            "\tURL:\t   https://www.recreation.gov/camping/campsites/369\n" +
            "\n" +
            "\n" +
            "Searching for entries during December/2026 🔎\n" +
            "\n" +
            "\tNo entries found on 12/2026\n" +
            "\n" +
            "\n" +
            "\tSite:\t   086 ➰ Fir Loop (Sites 60-106) (RV NONELECTRIC)\n" +
            "\tEquipment: Tent,CAR\n" +
            "\tURL:\t   https://www.recreation.gov/camping/campsites/4384\n" +
            "\tDate:\t   11/14/2026 (Saturday) 📆 Checkin:12:00 PM CheckOut:11:00 AM\n" +
            "\tDate:\t   11/13/2026 (Friday) 📆 Checkin:12:00 PM CheckOut:11:00 AM\n" +
            "\n";

        // what the values spell in order: a line value ends its line, a Write value continues it
        private static string Spell(IEnumerable<ConsoleConfig.ConsoleConfigValue> Values)
        {
            var Text = new System.Text.StringBuilder();
            foreach (ConsoleConfig.ConsoleConfigValue Value in Values)
            {
                Text.Append(Value.ConfigValue);
                if (Value.ConfigType == ConsoleConfig.ConfigType.WriteLine || Value.ConfigType == ConsoleConfig.ConfigType.WriteLineColor)
                {
                    Text.Append('\n');
                }
            }
            return Text.ToString();
        }

        [Test]
        public void ProducersText_IsByteForByteUnchanged()
        {
            Assert.That(Spell(RepresentativeStream()), Is.EqualTo(Golden));
        }

        // the calendar written into Console streams keeps its text too; its values are now tagged Calendar
        [Test]
        public void CalendarValues_KeepTheirText_AndAreTaggedCalendar()
        {
            var Values = new List<ConsoleConfig.ConsoleConfigValue>();
            Calendar.GenerateCalendar(11, 2026, new List<DateTime> { new DateTime(2026, 11, 13) }, ref Values);

            Assert.That(Spell(Values), Is.EqualTo(
                "\t\t   November 2026\n" +
                "\t\tMo Tu We Th Fr Sa Su\n" +
                "\t\t                   1\n" +
                "\t\t 2  3  4  5  6  7  8\n" +
                "\t\t 9 10 11 12 13 14 15\n" +
                "\t\t16 17 18 19 20 21 22\n" +
                "\t\t23 24 25 26 27 28 29\n" +
                "\t\t30\n\n"));
            Assert.That(Values.All(p => p.Kind == ConsoleConfig.OutputKind.Calendar), Is.True);
            Assert.That(Values.Single(p => p.Color == ConsoleColor.Blue).ConfigValue, Is.EqualTo(" 13"));
        }

        [Test]
        public void Producers_KeepTheirWriteTypesAndColors()
        {
            // (type, color) per value, as before the tagging; colors are not visible in redirected console output
            var W = ConsoleConfig.ConfigType.Write;
            var WC = ConsoleConfig.ConfigType.WriteColor;
            var WL = ConsoleConfig.ConfigType.WriteLine;
            var WLC = ConsoleConfig.ConfigType.WriteLineColor;
            var Header = new[] { (WL, (ConsoleColor)0), (WC, ConsoleColor.DarkGreen), (W, (ConsoleColor)0), (WC, ConsoleColor.DarkYellow), (W, (ConsoleColor)0), (WC, ConsoleColor.DarkGreen), (WL, (ConsoleColor)0) };
            var MonthHeader = new[] { (WL, (ConsoleColor)0), (WLC, ConsoleColor.Yellow), (WL, (ConsoleColor)0) };
            var NoEntries = new[] { (WLC, ConsoleColor.DarkRed), (WL, (ConsoleColor)0) };
            var Single = new[] { (W, (ConsoleColor)0), (WC, ConsoleColor.Cyan), (W, (ConsoleColor)0), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0), (W, (ConsoleColor)0), (WC, ConsoleColor.Blue), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0) };
            var Consecutive = new[] { (W, (ConsoleColor)0), (WC, ConsoleColor.Cyan), (W, (ConsoleColor)0), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0), (WL, (ConsoleColor)0), (W, (ConsoleColor)0), (WC, ConsoleColor.Blue), (WL, (ConsoleColor)0) };
            AvailabilityData Hit = OutputStreams.Hit("369", "C002", new DateTime(2026, 11, 13));

            Assert.That(Shape(OutputStreams.Header("x")), Is.EqualTo(Header));
            Assert.That(Shape(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.MonthHeaderValues(new DateTime(2026, 11, 1))), Is.EqualTo(MonthHeader));
            Assert.That(Shape(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.NoEntriesValues(new DateTime(2026, 11, 1))), Is.EqualTo(NoEntries));
            Assert.That(Shape(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.AvailabilityValues(Hit, "a", "b", false)), Is.EqualTo(Single));
            Assert.That(Shape(CampertronLibrary.function.RecDotOrg.api.AvailabilityApi.AvailabilityValues(Hit, "a", "b", true)), Is.EqualTo(Consecutive));
        }

        private static (ConsoleConfig.ConfigType, ConsoleColor)[] Shape(List<ConsoleConfig.ConsoleConfigValue> Values) =>
            Values.Select(p => (p.ConfigType, p.Color)).ToArray();
    }
}
