using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // What the console says when it cannot continue (ConsoleUi.DescribeFailure / ReportFailure, used by
    // CampertronConsole's Main), and when the menu cannot start (Menu.StartProblem). The settings errors are real
    // ones: the production loaders read invalid files in a temporary directory. No network; nothing is sent.
    public class ConsoleFailureTests
    {
        private string Root = string.Empty;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private bool OriginalUnacknowledged;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronConsoleFailureTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            OriginalUnacknowledged = ConsoleUi.Unacknowledged;
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
            ConsoleUi.Unacknowledged = OriginalUnacknowledged;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private string Write(string Name, string Content)
        {
            string FilePath = Path.Join(Root, Name);
            File.WriteAllText(FilePath, Content);
            return FilePath;
        }
        private static Exception Catch(Action Act)
        {
            try
            {
                Act();
            }
            catch (Exception ex)
            {
                return ex;
            }
            Assert.Fail("expected an exception");
            return null!;
        }
        private const string ValidSearch =
            "displayName: Trip\nautoRun: true\ncampgroundID: 232445\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nsearchValueDates: []\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
            "includeEquipment: []\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\nincludeAttributes: []\nexcludeAttributes: []\n" +
            "includeCampsiteType: []\nexcludeCampsiteType: []\nconsecutiveDays: 1\n";

        // ---- settings files ----

        [Test]
        public void InvalidGeneralYaml_NamesTheFileThePositionAndTheValue()
        {
            string FilePath = Write("General.yaml", "outputTo: Screen\nrefreshRidbDataDayInterval: 30\n");

            Exception Failure = Catch(() => Yaml.GeneralConfigGetConfig(Root));
            List<string> Lines = ConsoleUi.DescribeFailure(Failure);

            Assert.That(Failure, Is.InstanceOf<YamlDotNet.Core.YamlException>(), "the exception itself is unchanged");
            Assert.That(Lines[0], Does.StartWith("General.yaml could not be read: "));
            Assert.That(Lines[0], Does.Contain("Screen"), "the reason, not only 'Exception during deserialization'");
            Assert.That(Lines[0], Does.EndWith("(line 1, column 11)"));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
            Assert.That(Lines[2], Is.EqualTo("Fix the file, or delete it and Campertron creates a new one."));
            Assert.That(Lines, Has.Count.EqualTo(3), "no stack trace for a settings error");
        }

        [Test]
        public void MalformedAndUnknownKeyGeneralYaml_AreDescribedTheSameWay()
        {
            Write("General.yaml", "outputTo: Console\ncolour: true\n");
            List<string> Unknown = ConsoleUi.DescribeFailure(Catch(() => Yaml.GeneralConfigGetConfig(Root)));
            Assert.That(Unknown[0], Is.EqualTo("General.yaml could not be read: Property 'colour' not found on type 'GeneralConfig' (line 2, column 1)"));

            Write("General.yaml", "outputTo: [Console\nrefreshRidbDataDayInterval: 30\n");
            List<string> Malformed = ConsoleUi.DescribeFailure(Catch(() => Yaml.GeneralConfigGetConfig(Root)));
            Assert.That(Malformed[0], Does.StartWith("General.yaml could not be read: "));
            Assert.That(Malformed[0], Does.Match(@"\(line \d+, column \d+\)$"));
        }

        [Test]
        public void InvalidEmailYaml_NeverQuotesAValueOfTheFile()
        {
            // a password typed into the wrong key: the inner exception quotes the value, the message must not
            string FilePath = Write("Email.yaml", "smtpServer: smtp.example.invalid\nsmtpPort: hunter2-SECRET\nsmtpUsername: user@example.invalid\nsmtpPassword: other-SECRET\n");

            Exception Failure = Catch(() => Yaml.EmailConfigGetConfig(Root));
            Assert.That(Failure.ToString() + Failure.InnerException?.Message, Does.Contain("hunter2-SECRET"), "the test is meaningful: the exception does carry the value");
            List<string> Lines = ConsoleUi.DescribeFailure(Failure);
            string All = string.Join("\n", Lines);

            Assert.That(Lines[0], Is.EqualTo("Email.yaml could not be read: Exception during deserialization (line 2, column 11)"));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
            Assert.That(All, Does.Not.Contain("SECRET"));
            Assert.That(All, Does.Not.Contain("user@example.invalid"));
            Assert.That(All, Does.Not.Contain("smtp.example.invalid"));
        }

        [Test]
        public void InvalidSearchFile_NamesThatFile()
        {
            Write("Good.yaml", ValidSearch);
            string FilePath = Write("Trip.yaml", ValidSearch.Replace("displayName:", "displayname:"));

            Exception Failure = Catch(() => Yaml.CampertronConfigGetConfigs(Root));
            List<string> Lines = ConsoleUi.DescribeFailure(Failure);

            Assert.That(Failure, Is.InstanceOf<YamlDotNet.Core.YamlException>());
            Assert.That(Lines[0], Is.EqualTo("Trip.yaml could not be read: Property 'displayname' not found on type 'CampertronConfig' (line 1, column 1)"));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
            Assert.That(Lines[2], Is.EqualTo("Fix the file, or move it out of the settings folder."));
        }

        [Test]
        public void SearchFileWithUnusableValues_IsNamedToo()
        {
            // StartEndDate needs two dates; the failure is not a YAML error (business-rules §2)
            string FilePath = Write("Trip.yaml", ValidSearch.Replace("searchBy: DaysOut", "searchBy: StartEndDate").Replace("searchValueDates: []", "searchValueDates: [\"2026-11-20\"]"));

            Exception Failure = Catch(() => Yaml.CampertronConfigGetConfigs(Root));
            List<string> Lines = ConsoleUi.DescribeFailure(Failure);

            Assert.That(Failure, Is.InstanceOf<ArgumentOutOfRangeException>(), "the exception itself is unchanged");
            Assert.That(Lines[0], Does.StartWith("Trip.yaml could not be used: "));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
        }

        [Test]
        public void ValidSettings_LoadAsBefore()
        {
            Write("General.yaml", "outputTo: HtmlFile\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-10T00:00:00.0000000Z\nautoRefresh: false\n");
            Write("Trip.yaml", ValidSearch);

            Assert.That(Yaml.GeneralConfigGetConfig(Root).OutputTo, Is.EqualTo(OutputType.HtmlFile));
            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(Root);
            Assert.That(Searches.Select(p => p.DisplayName), Is.EqualTo(new[] { "Trip" }));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }

        [Test]
        public void NoteFile_PassesTheExceptionThrough_AndKeepsTheFirstFile()
        {
            var Failure = new InvalidOperationException("x");

            Assert.That(ConsoleUi.NoteFile(Failure, "/a/first.yaml"), Is.False, "an exception filter that never catches");
            Assert.That(ConsoleUi.NoteFile(Failure, "/a/second.yaml"), Is.False);
            Assert.That(Failure.Data[ConsoleUi.FileKey], Is.EqualTo("/a/first.yaml"));
        }

        // ---- other failures ----

        [TestCase("Config path does not exist")]
        [TestCase("Cache path does not exist")]
        public void MissingContainerFolders_KeepTheirMessage(string Message)
        {
            List<string> Lines = ConsoleUi.DescribeFailure(new Exception(Message));

            Assert.That(Lines[0], Is.EqualTo(Message), "the release workflow looks for this text");
            Assert.That(Lines[1], Does.Contain("/config").And.Contain("/cache"));
            Assert.That(Lines, Has.Count.EqualTo(2));
        }

        [Test]
        public void EmailFailure_SaysWhatFailed_WithoutCredentials()
        {
            var Failure = new SmtpException("Failure sending mail.", new System.Net.Sockets.SocketException(111));

            List<string> Lines = ConsoleUi.DescribeFailure(Failure, "p4ssw0rd");

            Assert.That(Lines[0], Does.StartWith("the email could not be sent: Failure sending mail."));
            Assert.That(Lines[1], Is.EqualTo("Check the server, port, user name and password in Email.yaml."));
            Assert.That(Lines, Has.Count.EqualTo(2));
        }

        [Test]
        public void FailedDownload_IsANetworkProblem_NotAnUnexpectedOne()
        {
            // as Web.Download(...).Result throws it when the campground data cannot be downloaded
            var Failure = new AggregateException(new HttpRequestException("Connection refused (ridb.recreation.gov:443)"));

            List<string> Lines = ConsoleUi.DescribeFailure(Failure);

            Assert.That(Lines, Is.EqualTo(new[] { "a download failed: Connection refused (ridb.recreation.gov:443)", "Check the internet connection and try again." }));
        }

        [Test]
        public void TheSmtpPassword_IsRemovedFromEverything()
        {
            // a server answer or exception text that repeats the password
            var Failure = new SmtpException("Authentication failed for password p4ssw0rd", new InvalidOperationException("p4ssw0rd rejected"));

            string All = string.Join("\n", ConsoleUi.DescribeFailure(Failure, "p4ssw0rd"));
            string Unexpected = string.Join("\n", ConsoleUi.DescribeFailure(new InvalidOperationException("token p4ssw0rd"), "p4ssw0rd"));

            Assert.That(All, Does.Not.Contain("p4ssw0rd"));
            Assert.That(All, Does.Contain("[hidden]"));
            Assert.That(Unexpected, Does.Not.Contain("p4ssw0rd"));
        }

        [Test]
        public void UnexpectedFailure_KeepsItsDetailsForAReport()
        {
            Exception Failure = Catch(() => throw new InvalidOperationException("something broke"));

            List<string> Lines = ConsoleUi.DescribeFailure(new AggregateException(Failure));

            Assert.That(Lines[0], Is.EqualTo("Campertron stopped because of an unexpected problem: InvalidOperationException: something broke"));
            Assert.That(Lines[1], Is.EqualTo("Details, for a problem report:"));
            Assert.That(string.Join("\n", Lines.Skip(2)), Does.Contain("System.InvalidOperationException: something broke").And.Contain("ConsoleFailureTests"));
        }

        [Test]
        public void ReportFailure_WritesToStandardErrorOnly_AndReturnsExitCode1()
        {
            string FilePath = Write("General.yaml", "outputTo: Screen\n");
            Exception Failure = Catch(() => Yaml.GeneralConfigGetConfig(Root));

            int ExitCode = ConsoleUi.ReportFailure(Failure);

            Assert.That(ExitCode, Is.EqualTo(1));
            Assert.That(Out.ToString(), Is.Empty);
            string[] Lines = Err.ToString().Replace(Environment.NewLine, "\n").TrimEnd('\n').Split('\n');
            Assert.That(Lines[0], Does.StartWith("Error: General.yaml could not be read: "));
            Assert.That(Lines[1], Is.EqualTo("  File: " + FilePath));
            Assert.That(Lines[2], Is.EqualTo("  Fix the file, or delete it and Campertron creates a new one."));
            // an ordinal check: a culture-sensitive search ignores control characters
            Assert.That(Err.ToString().Contains('\u001b'), Is.False, "no escape sequence");
        }

        [Test]
        public void Reason_IsTheInnermostMessageOnOneLine()
        {
            var Failure = new AggregateException("outer", new HttpRequestException("middle", new IOException("Connection\r\nrefused")));

            Assert.That(ConsoleUi.Reason(Failure), Is.EqualTo("Connection refused"));
            Assert.That(ConsoleUi.Reason(new InvalidOperationException(string.Empty)), Is.EqualTo("InvalidOperationException"));
        }

        // ---- the menu's start ----

        [Test]
        public void Menu_InAContainer_KeepsItsMessage_AndSaysWhatToDo()
        {
            string? Problem = Menu.StartProblem(true, true);

            Assert.That(Problem, Does.StartWith("Unsupported output type in container"), "kept text (README, build-and-test.md)");
            Assert.That(Problem, Does.Contain("outputTo: Email"));
            Assert.That(Menu.StartProblem(true, false), Is.EqualTo(Problem), "the container message comes first");
        }

        [Test]
        public void Menu_WithoutAnInteractiveTerminal_SaysSo_InsteadOfCrashing()
        {
            Assert.That(Menu.StartProblem(false, false), Does.Contain("needs an interactive terminal").And.Contain("outputTo: Email"));
            Assert.That(Menu.StartProblem(false, true), Is.Null);
        }

        [Test]
        public void UnitTestOutput_StaysSilent()
        {
            // the release smoke tests start the console with outputTo: UnitTest and expect no menu and a clean exit
            var Config = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest }, ConfigPath = Root, CachePath = Root };

            Assert.That(Load.RunConsoleSearch(Config), Is.True);
            Menu.Init(Config);

            Assert.That(Out.ToString(), Is.Empty);
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void FirstStart_TheLibraryWritesNothingByItself_TheConsoleAsksForItsNotice()
        {
            // The GUI starts through the same Load.GetConfig, and its smoke test's standard output must hold only its
            // own report lines (release-policy check_gui_smoke_report). Only CampertronConsole sets FirstStartNotice.
            Assert.That(Load.FirstStartNotice, Is.Null, "unset unless the console program sets it");
            string Db = Path.Join(Root, "RecreationDotOrg.db");
            var Config = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.Console }, ConfigPath = Root, CachePath = Root };
            int Notices = 0;
            try
            {
                Assert.That(Load.DbExistsCheck(Config), Is.True);
                Assert.That(File.Exists(Db), Is.True, "the bundled database was extracted");
                Assert.That(Out.ToString() + Err.ToString(), Is.Empty);

                Load.FirstStartNotice = () => Notices++;
                Load.DbExistsCheck(Config);
                Assert.That(Notices, Is.EqualTo(0), "not a first start: the database exists");

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(Db);
                Load.DbExistsCheck(Config);
                Assert.That(Notices, Is.EqualTo(1));

                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                File.Delete(Db);
                Config.GeneralConfig.OutputTo = OutputType.UnitTest;
                Load.DbExistsCheck(Config);
                Assert.That(Notices, Is.EqualTo(1), "outputTo: UnitTest stays silent");
                Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
            }
            finally
            {
                Load.FirstStartNotice = null;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
        }

        // ---- requests to recreation.gov ----

        [Test]
        public void DescribeRequest_NamesTheCampgroundAndTheMonth()
        {
            System.Globalization.CultureInfo Original = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            try
            {
                const string Url = "https://www.recreation.gov/api/camps/availability/campground/990001/month?start_date=2026-11-01T00%3A00%3A00.000Z";
                Assert.That(AvailabilityApi.DescribeRequest(Url), Is.EqualTo("campground 990001, November 2026"));

                File.WriteAllText(Path.Join(Root, "990001-CampInfo.json"), "{\"ParkName\":\"Test Park\",\"CampsiteName\":\"TEST CAMPGROUND\"}");
                Assert.That(Cache.GetCampInfo("990001", Root)?.CampsiteName, Is.EqualTo("TEST CAMPGROUND"));
                Assert.That(AvailabilityApi.DescribeRequest(Url), Is.EqualTo("TEST CAMPGROUND, November 2026"));

                Assert.That(AvailabilityApi.DescribeRequest("https://example.invalid/other"), Is.EqualTo("https://example.invalid/other"));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = Original;
                Cache.KnownCampgroundNames.TryRemove("990001", out _);
            }
        }

        [Test]
        public void GetCampInfo_ReturnsNull_WhenTheCampgroundIsNotCachedOrUnreadable()
        {
            Assert.That(Cache.GetCampInfo("990002", Root), Is.Null);
            Assert.That(Cache.GetCampInfo(null, Root), Is.Null);
            Assert.That(Cache.GetCampInfo("990002", null), Is.Null);
            File.WriteAllText(Path.Join(Root, "990003-CampInfo.json"), "not json");
            Assert.That(Cache.GetCampInfo("990003", Root), Is.Null);
        }
    }
}
