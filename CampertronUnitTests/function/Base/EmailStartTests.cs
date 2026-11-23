using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // Email mode's start (open question #40, decisions D64 and D65): it starts its periodic checks only when its start
    // produced a search to check. With none (no search file, no search file Enabled (autoRun), or no enabled search with a
    // campgroundID) it says why once on standard error and returns, so the console exits with code 1: no check, no wait,
    // no request, no email and no file written or created. Before D64 it wrote one warning and then checked 0 searches
    // every minute for as long as it ran, which looked healthy to a supervisor; until D65 a folder without a search file
    // got the example search, created and checked by email mode. Everything else about the start is unchanged: the search
    // files read once, the refusals at load, the Email.yaml check first, the console's Search (which still creates the
    // example). An outputTo: UnitTest start is not email mode: it searches nothing and ends with 0 (the silent start).
    // Temporary folders only; every month is answered through the console's download seam (no network), the send is a
    // counter (no SMTP) and the wait between checks stops the loop.
    public class EmailStartTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private bool OriginalUnacknowledged;
        private CultureInfo OriginalCulture = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<TimeSpan, CancellationToken> CheckWait = null!;
        private Func<string, string> OriginalDownload = null!;
        private Action<string, string> OriginalStep = null!;
        private readonly List<string> Requested = new();
        private int Sends;
        private int Waits;
        // what the wait between two checks does after counting itself; by default it stops the loop after the first check
        private Action<int> OnWait = Number => throw new StopTheLoop();

        private sealed class StopTheLoop : Exception
        {
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronEmailStartTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            OriginalUnacknowledged = ConsoleUi.Unacknowledged;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalStep = ConfigFileReplace.Step;
            Requested.Clear();
            Sends = 0;
            Waits = 0;
            OnWait = Number => throw new StopTheLoop();
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sends++; Message.Dispose(); };
            CheckWait = (Wait, Token) => { Waits++; OnWait(Waits); };
            AvailabilityApi.ConsoleDownload = Url =>
            {
                lock (Requested)
                {
                    Requested.Add(Url);
                }
                return NoLiveNetwork.EmptyMonth;
            };
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
            CultureInfo.CurrentCulture = OriginalCulture;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            ConfigFileReplace.Step = OriginalStep;
            if (Directory.Exists(Root))
            {
                foreach (string File_ in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(File_, FileAttributes.Normal);
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(File_, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                Directory.Delete(Root, true);
            }
        }

        // ---- the settings ----

        // a valid Email.yaml with names that are never contacted (the send is a counter); its password and addresses must
        // never reach the output
        internal const string Password = "Em4il-Start-S3cret";
        private const string ValidEmail = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: start-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        // a search file: every day of a week from today, two people, single nights
        internal static string SearchText(string Name, bool AutoRun, string CampgroundLine = "campgroundID: '232445'") =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\n" + CampgroundLine + "\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";

        private string WriteFile(string Name, string Text)
        {
            string FilePath = Path.Join(ConfigPath, Name);
            File.WriteAllText(FilePath, Text);
            // an old time stamp, so a rewrite with the same bytes would still show
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return FilePath;
        }

        // a campground's search cache as a search builds it (the completion marker last), so no database is needed: the
        // campground's name and park and no campsite
        private void CacheCampground(string Id, string Name)
        {
            File.WriteAllText(Path.Join(CachePath, Id + "-CampInfo.json"), JsonSerializer.Serialize(new ReturnParkCampground { ParkName = "Fixture Park", CampsiteName = Name }));
            File.WriteAllText(Path.Join(CachePath, Id + "-Campsites.json"), "[]");
            File.WriteAllText(Path.Join(CachePath, Id + "-Cached.json"), "\"2026-11-21\"");
        }

        private CampertronInternalConfig EmailSettings(string EmailText = ValidEmail)
        {
            WriteFile("Email.yaml", EmailText);
            WriteFile("General.yaml", "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            return new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.Email },
                EmailConfig = Yaml.EmailConfigGetConfig(ConfigPath)
            };
        }

        // every file of the settings and cache folders with its bytes and its last write time
        private Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot() =>
            Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(Root, p), p => (File.ReadAllBytes(p), File.GetLastWriteTimeUtc(p)));

        private void AssertUnchanged(Dictionary<string, (byte[] Bytes, DateTime Written)> Before)
        {
            Dictionary<string, (byte[] Bytes, DateTime Written)> After = Snapshot();
            Assert.That(After.Keys, Is.EquivalentTo(Before.Keys), "no file added or removed");
            foreach (var File_ in Before)
            {
                Assert.That(After[File_.Key].Bytes, Is.EqualTo(File_.Value.Bytes), File_.Key + ": the same bytes");
                Assert.That(After[File_.Key].Written, Is.EqualTo(File_.Value.Written), File_.Key + ": not written again");
            }
            Assert.That(Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Root, p)),
                Is.EquivalentTo(new[] { "config", "cache" }), "no folder created, Camp.History included");
        }

        private static string[] Lines(StringWriter Writer) =>
            Writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(L => L.Length > 0).ToArray();

        // the start line of email mode, with the time; the only line on standard output when it does not start its checks
        private const string StartLine = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  .* \| output Email$";

        // ---- the messages (decision D64): one error, the reason without names, paths or values, and what to do ----

        private const string Again = "start Campertron again, or restart its container.";
        private const string ReadOnce = "Email mode reads the search files only when it starts.";

        internal static string[] NoneEnabledMessage(int SearchFiles) => new[]
        {
            "Error: email mode has no search to check, so it stopped: " + (SearchFiles == 1
                ? "the search file is not Enabled (autoRun)."
                : "none of the " + SearchFiles.ToString(CultureInfo.InvariantCulture) + " search files is Enabled (autoRun)."),
            "  Enable at least one valid search (set autoRun: true in its file, or check Enabled (autoRun) in CampertronGUI's Searches tab and save it), then " + Again,
            "  " + ReadOnce
        };

        internal static string[] NoCampgroundMessage(int Enabled) => new[]
        {
            "Error: email mode has no search to check, so it stopped: " + (Enabled == 1
                ? "the Enabled (autoRun) search has no campgroundID, so it would never be checked."
                : "none of the " + Enabled.ToString(CultureInfo.InvariantCulture) + " Enabled (autoRun) searches has a campgroundID, so none would ever be checked."),
            "  Set the campgroundID of at least one enabled search (CampertronGUI's Searches tab can choose the campground), then " + Again,
            "  " + ReadOnce
        };

        internal static readonly string[] NoSearchFileMessage =
        {
            "Error: email mode has no search to check, so it stopped: the settings folder has no search file.",
            "  Add a search file and enable it (set autoRun: true in it, or use New search and Enabled (autoRun) in CampertronGUI's Searches tab), then " + Again,
            "  " + ReadOnce
        };

        // email mode did not start its checks: it returned at once with only the start line and the expected error, and
        // nothing was waited for, asked of recreation.gov, sent or written
        private void AssertStopped(CampertronInternalConfig Settings, string[] Expected, Dictionary<string, (byte[] Bytes, DateTime Written)> Before)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            bool Result = Load.RunConsoleSearch(Settings, CheckWait);
            Clock.Stop();

            Assert.That(Result, Is.False, "the start reports that email mode did not run (the console then exits with code 1)");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "at once: no wait for a next check");
            Assert.That(Lines(Err), Is.EqualTo(Expected), "one error on standard error, nothing else (no warning before it)");
            string[] Written = Lines(Out);
            Assert.That(Written, Has.Length.EqualTo(1), "standard output holds only the start line: " + Out);
            Assert.That(Written[0], Does.Match(StartLine));
            Assert.That(Out.ToString() + Err.ToString(), Does.Not.Contain("Running").And.Not.Match(@"Check \d+:").And.Not.Contain("0 searches"));
            Assert.That((Waits, Sends), Is.EqualTo((0, 0)), "no wait for a next check and no email");
            Assert.That(Requested, Is.Empty, "nothing asked of recreation.gov");
            Assert.That(Out.ToString() + Err.ToString(), Does.Not.Contain(Password).And.Not.Contain("example.invalid"));
            Assert.That(Err.ToString(), Does.Not.Contain(Root).And.Not.Contain(".yaml"), "the error names no path and no file (the start line names the folders, as before)");
            AssertUnchanged(Before);
        }

        // ---- no search to check: email mode stops at once ----

        [TestCase(1)]
        [TestCase(2)]
        public void NoSearchFileIsEnabled_EmailModeStops_WithOneError(int Files)
        {
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", false));
            if (Files == 2)
            {
                WriteFile("Mather.yaml", SearchText("Mather", false, "campgroundID: '232447'"));
            }
            AssertStopped(Settings, NoneEnabledMessage(Files), Snapshot());
        }

        // a search without a campgroundID is skipped at every check (GetAvailabilitiesByCampground; the validation's
        // CampgroundIdMissing, "the search is skipped"; CampsiteSearch.LoadEntry's Skipped), so it is nothing to check
        // (the cache files a start builds for such an ID are there, as a real data folder has them after a start: so the
        // test fails on the checks a start without the gate would run, not on a missing database)
        [TestCase("campgroundID:", "")]
        [TestCase("campgroundID: ''", "")]
        [TestCase("campgroundID: '   '", "   ")]
        [TestCase("", "")]
        public void EnabledSearchesWithoutACampground_StopEmailModeToo(string CampgroundLine, string CacheId)
        {
            CacheCampground(CacheId, "");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true, CampgroundLine));
            WriteFile("Off.yaml", SearchText("Off", false));
            AssertStopped(Settings, NoCampgroundMessage(1), Snapshot());
        }

        [Test]
        public void SeveralEnabledSearchesWithoutACampground_StopEmailMode()
        {
            CacheCampground("", "");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true, "campgroundID:"));
            WriteFile("Other.yaml", SearchText("Other", true, "campgroundID: ''"));
            AssertStopped(Settings, NoCampgroundMessage(2), Snapshot());
        }

        // a settings folder without a search file has no search to check either: email mode stops with its error and
        // creates nothing, not the example search (D65; until then it created ZionConfig.yaml and checked it). Other
        // files of the folder are not search files and stay as they are. The watchman campground's cache is there, so a
        // start that created and checked the example would reach its checks instead of failing for want of a database.
        // Adding an enabled search and starting again starts the checks
        [TestCase(false)]
        [TestCase(true)]
        public void NoSearchFile_StopsEmailMode_AndCreatesNoFile(bool OtherFiles)
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            CampertronInternalConfig Settings = EmailSettings();
            if (OtherFiles)
            {
                WriteFile("notes.txt", "not a search\n");
                WriteFile("Trip.yaml.bak", SearchText("A backup", true));
                WriteFile("RecreationDotOrg.db", "not opened here\n");
            }
            List<string> Steps = new();
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add(Name + " " + Path.GetFileName(Path_));

            AssertStopped(Settings, NoSearchFileMessage, Snapshot());

            Assert.That(Steps, Is.Empty, "no settings or search file was created, replaced or even prepared");
            Assert.That(File.Exists(Path.Join(ConfigPath, "ZionConfig.yaml")), Is.False, "no example search");
            Assert.That(Out.ToString(), Does.Not.Contain("example"));

            // the operator adds an enabled search and starts email mode again: it reads the files and checks
            Out.GetStringBuilder().Clear();
            Err.GetStringBuilder().Clear();
            WriteFile("Trip.yaml", SearchText("Trip", true));
            AssertStartedAndChecked(Settings, 1);
            Assert.That(File.Exists(Path.Join(ConfigPath, "ZionConfig.yaml")), Is.False);
        }

        // first-run behavior outside email mode is unchanged: the console's Search (the menu's, here through outputTo:
        // UnitTest, which returns after its run) still creates the example search when there is no search file, and runs it
        [Test]
        public void TheConsolesSearch_WithNoSearchFile_StillCreatesAndRunsTheExample()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            var Settings = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest },
                EmailConfig = new EmailConfig()
            };

            Assert.That(Load.RunConsoleSearch(Settings, true), Is.True);

            string Example = Path.Join(ConfigPath, "ZionConfig.yaml");
            Assert.That(File.Exists(Example), Is.True);
            Assert.That(File.ReadAllText(Example), Does.Contain("autoRun: true").And.Contain("campgroundID: 232445"));
            Assert.That(Out.ToString(), Does.Contain("No search files found; creating the example search " + Example));
            Assert.That(Requested, Is.Not.Empty.And.All.Contains("/campground/232445/month"), "the example is searched");
            Assert.That((Waits, Sends), Is.EqualTo((0, 0)));
        }

        // ---- a search to check: email mode starts as before ----

        // email mode ran its first check and waited for the next (the wait stops the loop here)
        private void AssertStartedAndChecked(CampertronInternalConfig Settings, int Searches)
        {
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings, CheckWait), "the loop ran its first check and waited for the next");
            string Count = Searches == 1 ? "1 search" : Searches + " searches";
            Assert.That(Out.ToString(), Does.Contain("Running " + Count + ":"));
            Assert.That(Out.ToString(), Does.Match(@"Check \d+: " + Count + @", nothing new\. Next check \d{2}:\d{2}:\d{2}\."));
            Assert.That(Lines(Err).Where(L => L.StartsWith("Error: ")), Is.Empty, "no error");
            Assert.That((Waits, Sends), Is.EqualTo((1, 0)), "one wait for the next check; nothing new, so no email");
        }

        [Test]
        public void OneUsableEnabledSearch_StartsTheChecks()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true));

            AssertStartedAndChecked(Settings, 1);

            Assert.That(Requested, Is.Not.Empty.And.All.Contains("/campground/232445/month"));
            Assert.That(Lines(Out)[0], Does.Match(StartLine));
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void SeveralUsableEnabledSearches_StartTheChecks()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            CacheCampground("232447", "MATHER CAMPGROUND");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true));
            WriteFile("Mather.yaml", SearchText("Mather", true, "campgroundID: '232447'"));

            AssertStartedAndChecked(Settings, 2);

            Assert.That(Requested.Select(U => U.Contains("/232445/") ? "232445" : U.Contains("/232447/") ? "232447" : U).Distinct(), Is.EquivalentTo(new[] { "232445", "232447" }));
        }

        // a usable enabled search, one without a campground and one that is not enabled: the checks start, the search that
        // is not enabled is never loaded, and the one without a campground stays in the checks as before (skipped each time,
        // with its warning when the searches are listed)
        [Test]
        public void AMixedFolder_StartsTheChecks_AndNeverLoadsTheSearchThatIsNotEnabled()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            // what a start builds for a search without a campgroundID (no campground, no campsite)
            CacheCampground("", "");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true));
            WriteFile("Off.yaml", SearchText("Off", false, "campgroundID: '232447'"));
            WriteFile("NoId.yaml", SearchText("No ID", true, "campgroundID:"));

            AssertStartedAndChecked(Settings, 2);

            Assert.That(Requested, Is.Not.Empty.And.All.Contains("/campground/232445/month"), "only the usable search asks recreation.gov");
            Assert.That(Out.ToString(), Does.Not.Contain("Off"));
            Assert.That(Lines(Err), Is.EqualTo(new[] { "Warning: search \"No ID\" has no campgroundID, so it finds nothing. Look up the ID in CampertronGUI." }));
        }

        // the search files are read once, when email mode starts: a change after it is not seen by the next checks, and a
        // start after it (a restart) reads it, here finding no search to check
        [Test]
        public void TheSearchFilesAreReadOnce_AndAChangeNeedsARestart()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            CacheCampground("232447", "MATHER CAMPGROUND");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true));
            OnWait = Number =>
            {
                if (Number == 1)
                {
                    // after the first check: the search is switched off and another file appears
                    WriteFile("Trip.yaml", SearchText("Trip", false));
                    WriteFile("Mather.yaml", SearchText("Mather", false, "campgroundID: '232447'"));
                    return;
                }
                throw new StopTheLoop();
            };

            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings, CheckWait));

            Assert.That(Waits, Is.EqualTo(2), "two checks, then the second wait stopped the loop");
            Assert.That(System.Text.RegularExpressions.Regex.Matches(Out.ToString(), @"Check \d+: 1 search, nothing new").Count, Is.EqualTo(2),
                "the second check still checked the search loaded at the start");
            Assert.That(Requested, Has.All.Contains("/campground/232445/month"), "the new file was never read");

            // the restart reads the files again: none is enabled now
            Out.GetStringBuilder().Clear();
            Err.GetStringBuilder().Clear();
            Requested.Clear();
            Waits = 0;
            AssertStopped(Settings, NoneEnabledMessage(2), Snapshot());
        }

        // ---- what is refused before the search gate, as before ----

        // an incomplete Email.yaml stops email mode first (TD-51, D41), also when there is no search to check: one error
        [Test]
        public void AnIncompleteEmailYaml_StopsEmailModeFirst()
        {
            CampertronInternalConfig Settings = EmailSettings("smtpServer: ''\nsmtpPort: 0\n");
            WriteFile("Trip.yaml", SearchText("Trip", false));
            var Before = Snapshot();

            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Settings, CheckWait))!;

            Assert.That(ConsoleUi.DescribeFailure(Refused)[0], Does.StartWith("Email.yaml could not be used: the Email output can't start: "));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "nothing was written before the refusal; Program.Main reports it");
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
            AssertUnchanged(Before);
        }

        // an enabled search the validation refuses stops the start with its own message, also next to usable searches and
        // one that is not enabled (D41): nothing is checked
        [Test]
        public void AnEnabledSearchTheStartRefuses_StopsItWithItsOwnMessage()
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", true));
            WriteFile("Off.yaml", SearchText("Off", false));
            string Bad = WriteFile("Bad.yaml", SearchText("Bad", true).Replace("searchValue: 7", "searchValue: -1"));

            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Settings, CheckWait))!;

            List<string> Described = ConsoleUi.DescribeFailure(Refused);
            Assert.That(Described[0], Does.StartWith("Bad.yaml could not be used: searchValue is negative"));
            Assert.That(Described[1], Is.EqualTo("File: " + Bad));
            Assert.That(ConsoleUi.ReportFailure(Refused), Is.EqualTo(1));
            Assert.That(Out.ToString(), Does.Not.Contain("Running").And.Not.Match(@"Check \d+:"));
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
        }

        // a search file that can't be read or isn't YAML stops the start with its own message (D34, D37), as before
        [TestCase("malformed")]
        [TestCase("unreadable")]
        public void ASearchFileThatCantBeRead_StopsTheStartWithItsOwnMessage(string Kind)
        {
            CampertronInternalConfig Settings = EmailSettings();
            string FilePath = WriteFile("Trip.yaml", Kind == "malformed" ? "displayName: [unclosed\nautoRun: true\n" : SearchText("Trip", true));
            if (Kind == "unreadable")
            {
                if (OperatingSystem.IsWindows())
                {
                    Assert.Ignore("Unix file modes only");
                    return;
                }
                File.SetUnixFileMode(FilePath, UnixFileMode.None);
                try
                {
                    File.ReadAllBytes(FilePath);
                    Assert.Ignore("this user reads files without read permission (root)");
                }
                catch (UnauthorizedAccessException)
                {
                    // as expected: the file can't be read
                }
            }

            Exception Refused = Assert.Catch(() => Load.RunConsoleSearch(Settings, CheckWait))!;
            Assert.That(Refused, Is.InstanceOf(Kind == "malformed" ? typeof(YamlDotNet.Core.YamlException) : typeof(UnauthorizedAccessException)));

            List<string> Described = ConsoleUi.DescribeFailure(Refused);
            Assert.That(Described[0], Does.StartWith(Kind == "malformed" ? "Trip.yaml could not be read: " : "Trip.yaml could not be used: "));
            Assert.That(Described[1], Is.EqualTo("File: " + FilePath));
            Assert.That(ConsoleUi.ReportFailure(Refused), Is.EqualTo(1));
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
        }

        // the search files can't be listed (the settings folder is gone): an unexpected failure with its details, exit 1
        [Test]
        public void SearchFilesThatCantBeListed_AreAnUnexpectedFailure()
        {
            CampertronInternalConfig Settings = EmailSettings();
            Directory.Delete(ConfigPath, true);

            Exception Failure = Assert.Throws<DirectoryNotFoundException>(() => Load.RunConsoleSearch(Settings, CheckWait))!;

            Assert.That(ConsoleUi.DescribeFailure(Failure)[0], Does.StartWith("Campertron stopped because of an unexpected problem: DirectoryNotFoundException: "));
            Assert.That(ConsoleUi.ReportFailure(Failure), Is.EqualTo(1));
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
        }

        // ---- other output types: unchanged ----

        // the console's Search (here with outputTo: UnitTest, which returns after its run) with no enabled search runs
        // nothing, warns as before and is not stopped: the search gate is email mode's only
        [Test]
        public void TheConsolesSearch_WithNoEnabledSearch_IsUnchanged()
        {
            WriteFile("Trip.yaml", SearchText("Trip", false));
            var Settings = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest },
                EmailConfig = new EmailConfig()
            };
            var Before = Snapshot();

            Assert.That(Load.RunConsoleSearch(Settings, true), Is.True);

            Assert.That(Lines(Err), Is.EqualTo(new[] { "Warning: No search file has autoRun: true, so no search runs. Set autoRun: true in a search file to run that search." }));
            Assert.That(Out.ToString(), Does.Contain("Done: 0 searches, no matching availability."));
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
            AssertUnchanged(Before);
        }

        // on a terminal the error is the same text, its prefix in red; redirected (above) it has no color
        [Test]
        public void OnATerminal_TheErrorIsTheSameText_WithItsPrefixColored()
        {
            var Colors = new List<ConsoleColor?>();
            Action<ConsoleColor?> OriginalApply = ConsoleUi.ApplyColor;
            ConsoleUi.ApplyColor = Color => Colors.Add(Color);
            ConsoleUi.Current = new ConsoleUi.Capabilities { OutputIsTerminal = true, InputIsTerminal = true, ErrorIsTerminal = true, ControlSequences = true, Color = true, Width = 500 };
            try
            {
                CampertronInternalConfig Settings = EmailSettings();
                WriteFile("Trip.yaml", SearchText("Trip", false));
                Assert.That(Load.RunConsoleSearch(Settings, CheckWait), Is.False);
            }
            finally
            {
                ConsoleUi.ApplyColor = OriginalApply;
            }
            Assert.That(Lines(Err), Is.EqualTo(NoneEnabledMessage(1)));
            Assert.That(Colors, Is.EqualTo(new ConsoleColor?[] { ConsoleColor.Red, null }), "only the prefix is colored, and the color is reset");
            Assert.That(Err.ToString().Contains('\u001b'), Is.False, "no control sequence of its own (ordinal: a culture-sensitive search ignores it)");
        }

        // ---- the gate itself (D64): the search plan's rule, the messages, the exit code and the loop's invariant ----

        private static CampertronConfig Loaded(string? CampgroundID) => new CampertronConfig { DisplayName = "Search", AutoRun = true, CampgroundID = CampgroundID };

        // the error as ConsoleUi.Error writes it: the first line after "Error: ", the others indented
        private static string[] AsWritten(List<string> Text) => new[] { "Error: " + Text[0] }.Concat(Text.Skip(1).Select(L => "  " + L)).ToArray();

        [Test]
        public void TheGate_TellsTheThreeStatesApart_AndPassesASearchToCheck()
        {
            var None = new List<CampertronConfig>();
            Assert.That(Load.CheckSearchesToCheck(0, None), Is.EqualTo(Load.EmailStartProblem.NoSearchFile));
            Assert.That(Load.CheckSearchesToCheck(3, None), Is.EqualTo(Load.EmailStartProblem.NoneEnabled));
            Assert.That(Load.CheckSearchesToCheck(2, new[] { Loaded(null), Loaded(""), Loaded(" \t") }), Is.EqualTo(Load.EmailStartProblem.NoCampground));
            Assert.That(Load.CheckSearchesToCheck(1, new[] { Loaded("232445") }), Is.Null);
            Assert.That(Load.CheckSearchesToCheck(3, new[] { Loaded(null), Loaded("232445") }), Is.Null, "one search to check is enough");
            Assert.That(Load.CheckSearchesToCheck(1, new[] { Loaded("Zion") }), Is.Null, "an ID that is not a number is checked (the validation warns: it finds nothing)");
            Assert.That(Load.SearchesToCheck(new[] { Loaded(null), Loaded("232445"), Loaded(""), Loaded("232447") }).Select(S => S.CampgroundID), Is.EqualTo(new[] { "232445", "232447" }));

            Assert.That(AsWritten(Load.EmailStartProblemText(Load.EmailStartProblem.NoneEnabled, 1, 0)), Is.EqualTo(NoneEnabledMessage(1)));
            Assert.That(AsWritten(Load.EmailStartProblemText(Load.EmailStartProblem.NoneEnabled, 7, 0)), Is.EqualTo(NoneEnabledMessage(7)));
            Assert.That(AsWritten(Load.EmailStartProblemText(Load.EmailStartProblem.NoCampground, 4, 1)), Is.EqualTo(NoCampgroundMessage(1)));
            Assert.That(AsWritten(Load.EmailStartProblemText(Load.EmailStartProblem.NoCampground, 4, 3)), Is.EqualTo(NoCampgroundMessage(3)));
            Assert.That(AsWritten(Load.EmailStartProblemText(Load.EmailStartProblem.NoSearchFile, 0, 0)), Is.EqualTo(NoSearchFileMessage));
            foreach (Load.EmailStartProblem Problem in Enum.GetValues<Load.EmailStartProblem>())
            {
                string Text = string.Join("\n", Load.EmailStartProblemText(Problem, 2, 2));
                Assert.That(Text, Does.Contain("Enabled (autoRun)").And.Contain("restart its container").And.Not.Contain(".yaml").And.Not.Contain("/"), Problem.ToString());
                Assert.That(Text.All(C => C < 128), Is.True, Problem + ": plain ASCII, for any log");
            }
        }

        // the gate decides with the search plan's own rule, never a second one: of the enabled search files a start loads,
        // exactly those CampsiteSearch.LoadEntry plans as Ready are searches to check, and those it plans as Skipped (no
        // campgroundID) are not. Warnings (an ID that is not a number, no people, dates that have passed) leave a search Ready
        [Test]
        public void TheGate_AgreesWithTheSearchPlan()
        {
            var Files = new (string Name, string Text)[]
            {
                ("Valid.yaml", SearchText("Valid", true)),
                ("NullId.yaml", SearchText("NullId", true, "campgroundID:")),
                ("Empty.yaml", SearchText("Empty", true, "campgroundID: ''")),
                ("Blank.yaml", SearchText("Blank", true, "campgroundID: '  '")),
                ("Missing.yaml", SearchText("Missing", true, "")),
                ("Word.yaml", SearchText("Word", true, "campgroundID: Zion")),
                ("NoPeople.yaml", SearchText("NoPeople", true).Replace("totalHumans: 2", "totalHumans: 0")),
                ("Past.yaml", SearchText("Past", true).Replace("searchBy: DaysOut\nsearchValue: 7", "searchBy: StartEndDate\nsearchValueDates:\n- 2020-01-01\n- 2020-01-03")),
                ("Other.yaml", SearchText("Other", true, "campgroundID: '232447'")),
            };
            foreach (var (Name, Text) in Files)
            {
                WriteFile(Name, Text);
            }
            WriteFile("Off.yaml", SearchText("Off", false));

            // the load email mode's start uses (no warning, no example)
            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath, false, false, out int Read);

            Assert.That(Read, Is.EqualTo(Files.Length + 1), "every search file was read");
            Assert.That(Searches.Select(S => S.DisplayName), Is.EquivalentTo(Files.Select(F => Path.GetFileNameWithoutExtension(F.Name))), "the enabled ones were loaded");
            var ToCheck = Load.SearchesToCheck(Searches).Select(S => S.DisplayName).ToHashSet();
            foreach (var (Name, _) in Files)
            {
                var State = CampertronLibrary.function.RecDotOrg.search.CampsiteSearch.LoadEntry(ConfigPath, Name, DateTime.Now).State;
                Assert.That(State, Is.EqualTo(SearchPlanState.Ready).Or.EqualTo(SearchPlanState.Skipped), Name);
                Assert.That(ToCheck.Contains(Path.GetFileNameWithoutExtension(Name)), Is.EqualTo(State == SearchPlanState.Ready),
                    Name + " is a search to check exactly when the search plan has it Ready (" + State + ")");
            }
            Assert.That(ToCheck, Is.EquivalentTo(new[] { "Valid", "Word", "NoPeople", "Past", "Other" }));
            Assert.That(Err.ToString(), Does.Not.Contain("autoRun"), "the load email mode's start uses leaves the warning about no enabled search to the gate");
        }

        // the console's exit code comes from the run (Program.Main returns it): 1 when email mode had no search to check,
        // as for every handled failure, and 0 after an outputTo: UnitTest start
        [Test]
        public void RunConsole_Gives1_WhenEmailModeHasNoSearchToCheck_And0AfterAUnitTestStart()
        {
            CampertronInternalConfig Settings = EmailSettings();
            WriteFile("Trip.yaml", SearchText("Trip", false));

            Assert.That(Load.RunConsole(Settings, CheckWait), Is.EqualTo(1));
            Assert.That(Lines(Err), Is.EqualTo(NoneEnabledMessage(1)));

            Err.GetStringBuilder().Clear();
            var UnitTest = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest },
                EmailConfig = new EmailConfig()
            };
            Assert.That(Load.RunConsole(UnitTest, CheckWait), Is.EqualTo(0));
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)));
        }

        // an outputTo: UnitTest start (Campertron's tests, and the release workflow's silent clean-install start; the
        // validation reports it as TestOutput, "a start with it shows and searches nothing") is not email mode and cannot
        // stand for one: whatever the search files are, it reads none (one that is not YAML would stop a start that read
        // it), creates none, checks, waits for and sends nothing, writes nothing and ends at once with 0. A supervisor sees
        // a process that has ended, never one that seems to watch for openings (D65)
        [TestCase("no search file")]
        [TestCase("not enabled")]
        [TestCase("enabled")]
        [TestCase("not YAML")]
        public void AUnitTestStart_IsNotEmailMode_ItReadsNoSearchFile_AndEndsWith0(string Files)
        {
            CacheCampground("232445", "WATCHMAN CAMPGROUND");
            WriteFile("General.yaml", "outputTo: UnitTest\n");
            switch (Files)
            {
                case "not enabled":
                    WriteFile("Trip.yaml", SearchText("Trip", false));
                    break;
                case "enabled":
                    WriteFile("Trip.yaml", SearchText("Trip", true));
                    break;
                case "not YAML":
                    WriteFile("Trip.yaml", "displayName: [unclosed\nautoRun: true\n");
                    break;
            }
            var UnitTest = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest },
                EmailConfig = new EmailConfig()
            };
            List<string> Steps = new();
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add(Name + " " + Path.GetFileName(Path_));
            var Before = Snapshot();
            var Clock = System.Diagnostics.Stopwatch.StartNew();

            Assert.That(Load.RunConsole(UnitTest, CheckWait), Is.EqualTo(0));

            Clock.Stop();
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "it ends at once");
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "silent: no start line, no search listed, no warning or error");
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)), "no check, wait, request or email");
            Assert.That(Steps, Is.Empty, "no file created or replaced");
            AssertUnchanged(Before);
        }

        // every check of the Email loop requires a search to check: the start refuses a plan without one, so an empty plan
        // at a check is a defect, ended visibly (exit code 1 with the details for a report) and never a check of nothing
        [Test]
        public void TheLoopsInvariant_EndsACheckOfNothing_Visibly()
        {
            Exception Empty = Assert.Throws<InvalidOperationException>(() => Load.RequireSearchesToCheck(new List<CampertronConfig>()))!;
            Assert.Throws<InvalidOperationException>(() => Load.RequireSearchesToCheck(new[] { Loaded(null), Loaded("") }));
            Assert.DoesNotThrow(() => Load.RequireSearchesToCheck(new[] { Loaded(null), Loaded("232445") }));
            List<string> Described = ConsoleUi.DescribeFailure(Empty);
            Assert.That(Described[0], Is.EqualTo("Campertron stopped because of an unexpected problem: InvalidOperationException: email mode reached a check with no search to check, which its start refuses (D64)."));
            Assert.That(ConsoleUi.ReportFailure(Empty), Is.EqualTo(1));

            // the loop asks it, and the start asks the gate, from the IL of the run itself: since the monitoring service
            // (D90) the run is the service's (its start and its checks), and the console's run is its adapter
            const System.Reflection.BindingFlags Own = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;
            Type Service = typeof(CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService);
            List<string> Called = Service.GetMethods(Own).Concat(Service.GetNestedTypes(Own).SelectMany(T => T.GetMethods(Own)))
                .SelectMany(M => YamlDocumentPreservationTests.ReferencedMembers(M)).Select(M => (M.DeclaringType?.Name ?? "?") + "::" + M.Name).ToList();
            Assert.That(Called, Does.Contain("Load::RequireSearchesToCheck").And.Contain("Load::LoadMonitoringSearches").And.Contain("Load::PrepareRun"));
            var Run = typeof(Load).GetMethod(nameof(Load.RunConsoleSearch), new[] { typeof(CampertronInternalConfig), typeof(bool) })!;
            Assert.That(YamlDocumentPreservationTests.ReferencedMembers(Run).Select(M => (M.DeclaringType?.Name ?? "?") + "::" + M.Name), Does.Contain("MonitoringConsole::Run"));
            // since D73 the start's search check is one function the GUI uses too: the start's own reading and D64's rule
            var Check = typeof(Load).GetMethod(nameof(Load.LoadMonitoringSearches), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Assert.That(YamlDocumentPreservationTests.ReferencedMembers(Check).Select(M => (M.DeclaringType?.Name ?? "?") + "::" + M.Name),
                Does.Contain("Load::CheckSearchesToCheck").And.Contain("Yaml::CampertronConfigGetConfigs"));
            var RunConsole = typeof(Load).GetMethod(nameof(Load.RunConsole))!;
            Assert.That(YamlDocumentPreservationTests.ReferencedMembers(RunConsole).Select(M => M.Name), Does.Contain(nameof(Load.RunConsoleSearch)));
        }
    }
}
