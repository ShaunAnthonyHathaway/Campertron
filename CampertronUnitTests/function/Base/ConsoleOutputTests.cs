using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // What the console writes around a search: the results through the writer (CampsiteConfig.ProcessConsoleConfig,
    // Load.OutputResults), the line of a finished search, the lines of the unattended (Email) loop, and View Config.
    // Plain capabilities are set explicitly, so nothing depends on the terminal the tests run in. In-memory, except
    // one temporary directory for View Config; no network, no database.
    public class ConsoleOutputTests
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

        // ---- the writer ----

        [Test]
        public void ProcessConsoleConfig_WritesTheResultsToStandardOutputOnly_WithoutEscapeSequences()
        {
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            CampsiteConfig.ProcessConsoleConfig(ConsoleResultsTests.Zion().Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Console }, new EmailConfig(), null);

            Assert.That(Out.ToString().Replace(Environment.NewLine, "\n"), Is.EqualTo(ConsoleResultsTests.Text(ConsoleResults.Render(new[] { ConsoleResultsTests.Zion() }, CultureInfo.CurrentCulture, int.MaxValue, false))));
            // an ordinal check: a culture-sensitive search ignores control characters
            Assert.That(Out.ToString().Contains('\u001b'), Is.False, "no escape sequence");
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void OutputResults_Console_WritesEverySearchByNameAndOneSummary()
        {
            var Config = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.Console }, EmailConfig = new EmailConfig() };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            Load.OutputResults(Config, new[] { ConsoleResultsTests.Zion(), ConsoleResultsTests.Nothing(), ConsoleResultsTests.Mather() }, ref Last);

            Assert.That(Out.ToString().Replace(Environment.NewLine, "\n"), Is.EqualTo("\n" + ConsoleResultsTests.GoldenRun));
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void CheckedLine_NamesTheSearchTheCampgroundAndItsMonths()
        {
            // StartEndDate two months ahead, so the months are known whatever today is
            DateTime Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1).AddMonths(2);
            CampertronConfig Config = ConsoleResultsTests.Search("Zion tents", "232445", SearchTypes.StartEndDate, 0, new List<string> { Start.ToString("yyyy-MM-dd"), Start.AddMonths(1).AddDays(3).ToString("yyyy-MM-dd") });
            string Months = Start.ToString("Y", EnUs) + ", " + Start.AddMonths(1).ToString("Y", EnUs);

            Assert.That(ConsoleResults.MonthsText(Config, DateTime.Now, EnUs), Is.EqualTo(Months));
            Assert.That(Load.CheckedText(Config, ConsoleResultsTests.CampInfo("232445"), 0.1375078), Is.EqualTo("Checked \"Zion tents\" at WATCHMAN CAMPGROUND: " + Months + " (0.1s)"));
            Assert.That(Load.CheckedText(Config, null, 12.96), Is.EqualTo("Checked \"Zion tents\" at campground ID 232445: " + Months + " (13.0s)"));
            Config.CampgroundID = null;
            Assert.That(Load.CheckedText(Config, null, 0), Is.EqualTo("Skipped \"Zion tents\": no campgroundID."));
        }

        // ---- the unattended loop ----

        [Test]
        public void CheckLine_SaysWhatACheckFound_AndWhenTheNextOneIs()
        {
            DateTime Now = new DateTime(2026, 11, 14, 23, 59, 30);

            Assert.That(Load.CheckText(1, 2, 0, Now), Is.EqualTo("Check 1: 2 searches, nothing new. Next check 00:00:30."));
            Assert.That(Load.CheckText(12, 1, 1, Now), Is.EqualTo("Check 12: 1 search, 1 new opening; sending 1 email."));
            Assert.That(Load.CheckText(3, 2, 8, Now), Is.EqualTo("Check 3: 2 searches, 8 new openings; sending 2 emails."));
            Assert.That(Load.NextCheckText(Now), Is.EqualTo("Next check 00:00:30."));
        }

        // ---- View Config ----

        [Test]
        public void ViewConfig_ShowsTheSettingsTheVersionAndReadableValues()
        {
            string Root = Path.Join(Path.GetTempPath(), "CampertronViewConfigTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            try
            {
                foreach (string Name in new[] { "General.yaml", "Email.yaml", "Zion.yaml", "Mather.yaml", "notes.txt" })
                {
                    File.WriteAllText(Path.Join(Root, Name), "");
                }
                var Config = new CampertronInternalConfig
                {
                    ConfigPath = Root, CachePath = "/somewhere/CampertronCache",
                    GeneralConfig = new GeneralConfig { OutputTo = OutputType.HtmlFile, LastRidbDataRefresh = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc) }
                };
                Assembly Library = typeof(RuntimeInfo).Assembly;
                string[] Version = RuntimeInfo.Describe(Library).Split(Environment.NewLine);

                List<ConsoleUi.StyledLine> Lines = Menu.ViewConfigLines(Config, new DateTime(2026, 11, 14, 21, 52, 0, DateTimeKind.Utc), EnUs, Library);

                Assert.That(Lines.Select(p => p.Text), Is.EqualTo(new[]
                {
                    "Version => " + Version[0],
                    "Config => " + Root,
                    "Cache => /somewhere/CampertronCache",
                    "Output => HtmlFile",
                    "Searches => 2 search files",
                    "RIDB refresh => 4 days ago (11/10/2026)",
                    "OS => " + RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")",
                    Version[1],
                }));
                Assert.That(Lines.Last().Text, Does.StartWith("Framework => .NET "), "the same line as --version");
                Assert.That(Lines.All(p => p.Spans[0].Text.EndsWith(" => ") && p.Spans[0].Color == ConsoleColor.Green && p.Spans[1].Color == null), Is.True, "the label is colored, the value is not");

                // a new General.yaml carries a date 50 years back: not "18262.9 days ago"
                Config.GeneralConfig.LastRidbDataRefresh = DateTime.UtcNow.AddYears(-50);
                Assert.That(Menu.ViewConfigLines(Config, DateTime.UtcNow, EnUs, Library)[5].Text, Is.EqualTo("RIDB refresh => never (the campground list that came with Campertron is in use)"));
                Config.GeneralConfig.LastRidbDataRefresh = new DateTime(2026, 11, 14, 20, 0, 0, DateTimeKind.Utc);
                Assert.That(Menu.ViewConfigLines(Config, new DateTime(2026, 11, 14, 21, 52, 0, DateTimeKind.Utc), EnUs, Library)[5].Text, Is.EqualTo("RIDB refresh => today (11/14/2026)"));
                Config.GeneralConfig.LastRidbDataRefresh = new DateTime(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);
                Assert.That(Menu.ViewConfigLines(Config, new DateTime(2026, 11, 14, 21, 52, 0, DateTimeKind.Utc), EnUs, Library)[5].Text, Is.EqualTo("RIDB refresh => 1 day ago (11/13/2026)"));
            }
            finally
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
