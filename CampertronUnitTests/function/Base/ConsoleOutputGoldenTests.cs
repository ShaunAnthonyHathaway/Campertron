using System.Globalization;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // beta.9 tagged the producers' values with presentation metadata for the HTML results. The console must not
    // change: these golden values are the text, write types and colors the producers wrote before the tagging
    // (master da9484c), rendered through ProcessConsoleConfig with OutputType.Console. en-US is set explicitly
    // because the month name and short date follow the current culture.
    public class ConsoleOutputGoldenTests
    {
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;
        private StringWriter Output = null!;

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Output = new StringWriter();
            Console.SetOut(Output);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut!);
            Output.Dispose();
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

        [Test]
        public void ConsoleRendering_OfTheTaggedProducers_IsByteForByteUnchanged()
        {
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
            CampsiteConfig.ProcessConsoleConfig(RepresentativeStream(), ref Last, new GeneralConfig { OutputTo = OutputType.Console }, new EmailConfig(), null);

            Assert.That(Output.ToString(), Is.EqualTo(Golden.Replace("\n", Environment.NewLine)));
        }

        [Test]
        public void ConsoleRendering_IgnoresTheMetadata()
        {
            List<ConsoleConfig.ConsoleConfigValue> Tagged = RepresentativeStream();
            Assert.That(Tagged.Any(p => p.Kind != ConsoleConfig.OutputKind.Text && p.Metadata != null), Is.True);
            List<ConsoleConfig.ConsoleConfigValue> Untagged = Tagged
                .Select(p => new ConsoleConfig.ConsoleConfigValue { ConfigType = p.ConfigType, ConfigValue = p.ConfigValue, Color = p.Color })
                .ToList();

            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;
            CampsiteConfig.ProcessConsoleConfig(Tagged, ref Last, new GeneralConfig { OutputTo = OutputType.Console }, new EmailConfig(), null);
            string TaggedOutput = Output.ToString();
            Output.GetStringBuilder().Clear();
            Last = ConsoleConfig.ConfigType.WriteLine;
            CampsiteConfig.ProcessConsoleConfig(Untagged, ref Last, new GeneralConfig { OutputTo = OutputType.Console }, new EmailConfig(), null);

            Assert.That(TaggedOutput, Is.EqualTo(Output.ToString()));
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
