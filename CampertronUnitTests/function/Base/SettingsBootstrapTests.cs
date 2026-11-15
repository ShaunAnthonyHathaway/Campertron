using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // How the settings folder's files are loaded before a search (business-rules §1, §12): which search files run, when
    // the example search is created, and what an unusable file says. Every test works in its own temporary settings
    // folder and checks the user's files byte for byte. No network: no search here asks recreation.gov for anything.
    //
    // TD-13: when no search file had autoRun: true, Yaml.CampertronConfigGetConfigs wrote the example ZionConfig.yaml,
    // over a ZionConfig.yaml of the user's (its comments and values were lost), and ran the example. Now the example is
    // created only when there is no search file at all, and with search files of which none is enabled nothing runs.
    public class SettingsBootstrapTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private string ExamplePath => Path.Join(ConfigPath, "ZionConfig.yaml");
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private bool OriginalUnacknowledged;
        private CultureInfo OriginalCulture = null!;
        private Action<string> OriginalOpen = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private readonly List<string> OpenedPages = new();
        private int Sends;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronSettingsBootstrapTests-" + Guid.NewGuid().ToString("N"));
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
            OriginalOpen = CampsiteConfig.OpenHtmlFile;
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OpenedPages.Clear();
            Sends = 0;
            CampsiteConfig.OpenHtmlFile = FilePath => OpenedPages.Add(FilePath);
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sends++; Message.Dispose(); };
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
            CampsiteConfig.OpenHtmlFile = OriginalOpen;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // a search file as a person writes one: comments, blank lines, odd spacing, quoting and key order, CRLF line ends
        private static string UserSearch(string Name, bool AutoRun) =>
            "# my own search: keep this comment\r\n" +
            "displayName:   \"" + Name + "\"   # trailing comment\r\n" +
            "\r\n" +
            "autoRun: " + (AutoRun ? "true" : "false") + "\r\n" +
            "campgroundID: '232490'\r\n" +
            "totalHumans: 4\r\n" +
            "searchBy: DaysOut\r\n" +
            "searchValue: 21\r\n" +
            "showFriday: true\r\n" +
            "showSaturday: true\r\n" +
            "consecutiveDays: 2\r\n" +
            "# end\r\n";

        private string WriteSearch(string FileName, string Content)
        {
            string FilePath = Path.Join(ConfigPath, FileName);
            File.WriteAllText(FilePath, Content);
            // an old time stamp, so a rewrite with the same bytes would still show
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return FilePath;
        }

        // every file in the settings folder with its bytes and its last write time
        private Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot() =>
            Directory.GetFiles(ConfigPath, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(ConfigPath, p), p => (File.ReadAllBytes(p), File.GetLastWriteTimeUtc(p)));

        private void AssertUnchanged(Dictionary<string, (byte[] Bytes, DateTime Written)> Before)
        {
            Dictionary<string, (byte[] Bytes, DateTime Written)> After = Snapshot();
            Assert.That(After.Keys, Is.EquivalentTo(Before.Keys), "no file added or removed");
            foreach (var File_ in Before)
            {
                Assert.That(After[File_.Key].Bytes, Is.EqualTo(File_.Value.Bytes), File_.Key + ": the same bytes");
                Assert.That(After[File_.Key].Written, Is.EqualTo(File_.Value.Written), File_.Key + ": not written again");
            }
        }

        // ---- TD-13: the example search ----

        [Test]
        public void NoSearchFile_CreatesTheExampleOnce_AndRunsIt()
        {
            WriteSearch("General.yaml", "outputTo: Console\n");
            WriteSearch("Email.yaml", "smtpServer: ''\n");

            List<CampertronConfig> First = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(First.Select(p => p.DisplayName), Is.EqualTo(new[] { "Zion non-group tent friendly sites" }), "the example runs");
            Assert.That(First[0].GetSearchDates(), Is.Not.Empty);
            Assert.That(File.Exists(ExamplePath), Is.True);
            Assert.That(Out.ToString(), Does.Contain("No search files found; creating the example search " + ExamplePath));
            string Example = File.ReadAllText(ExamplePath);
            Assert.That(Example, Does.Contain("autoRun: true").And.Contain("campgroundID: 232445").And.Contain("displayName: Zion non-group tent friendly sites"));

            // the next search uses the example as a search file of its own and does not write it again
            File.SetLastWriteTimeUtc(ExamplePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var Before = Snapshot();
            Out.GetStringBuilder().Clear();
            List<CampertronConfig> Second = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Second.Select(p => p.DisplayName), Is.EqualTo(new[] { "Zion non-group tent friendly sites" }));
            AssertUnchanged(Before);
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "no creation message the second time");
        }

        [Test]
        public void EnabledSearchFile_IsUsedAsBefore_AndNotRewritten()
        {
            WriteSearch("Trip.yaml", UserSearch("My trip", true));
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Searches.Select(p => p.DisplayName), Is.EqualTo(new[] { "My trip" }));
            Assert.That(Searches[0].CampgroundID, Is.EqualTo("232490"));
            Assert.That(Searches[0].ConsecutiveDays, Is.EqualTo(2));
            AssertUnchanged(Before);
            Assert.That(File.Exists(ExamplePath), Is.False);
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }

        [Test]
        public void EditedExampleFile_WithAutoRunFalse_IsKeptByteForByte_AndNothingRuns()
        {
            // the audit's case: the user's own ZionConfig.yaml, switched off, used to be replaced by the example
            WriteSearch("ZionConfig.yaml", UserSearch("Zion, my version", false));
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Searches, Is.Empty, "no search runs, and not the example either");
            AssertUnchanged(Before);
            Assert.That(Out.ToString(), Is.Empty, "no 'creating the example' line");
            Assert.That(Err.ToString().TrimEnd(), Is.EqualTo("Warning: No search file has autoRun: true, so no search runs. Set autoRun: true in a search file to run that search."));
        }

        [Test]
        public void EditedExampleFile_WithAutoRunTrue_IsUsedAsWritten()
        {
            WriteSearch("ZionConfig.yaml", UserSearch("Zion, my version", true));
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Searches.Select(p => p.DisplayName), Is.EqualTo(new[] { "Zion, my version" }), "the user's values, not the example's");
            Assert.That(Searches[0].SearchValue, Is.EqualTo(21));
            AssertUnchanged(Before);
        }

        [Test]
        public void SeveralSearchFiles_NoneEnabled_AreAllKept_NoExample_NoSearch()
        {
            WriteSearch("ZionConfig.yaml", UserSearch("Zion", false));
            WriteSearch("Mather.yaml", UserSearch("Mather", false));
            WriteSearch("Watchman weekends.yaml", "displayName: Watchman weekends\nautoRun: false\ncampgroundID: '232445'\n");
            WriteSearch("General.yaml", "outputTo: HtmlFile\n");
            WriteSearch("Email.yaml", "smtpServer: ''\n");
            File.WriteAllText(Path.Join(ConfigPath, "notes.txt"), "not a search");
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Searches, Is.Empty);
            AssertUnchanged(Before);
            Assert.That(Err.ToString(), Does.Contain("No search file has autoRun: true"));
        }

        [Test]
        public void EnabledAndDisabledSearchFiles_OnlyTheEnabledRun_TheOthersStayAsTheyAre()
        {
            WriteSearch("ZionConfig.yaml", UserSearch("Zion", false));
            WriteSearch("Trip.yaml", UserSearch("Trip", true));
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(Searches.Select(p => p.DisplayName), Is.EqualTo(new[] { "Trip" }));
            AssertUnchanged(Before);
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }

        // With no enabled search, every output type does what it does with an empty list of searches: it searches
        // nothing (no request; the runs here have no network and claim no URL), writes no settings file and sends no
        // email. The menu modes show their empty results, the Email check counts 0 searches.
        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.Email)]
        [TestCase(OutputType.UnitTest)]
        public void NoEnabledSearch_EveryOutputType_SearchesNothing(OutputType Output)
        {
            WriteSearch("ZionConfig.yaml", UserSearch("Zion", false));
            WriteSearch("Mather.yaml", UserSearch("Mather", false));
            var Before = Snapshot();
            var Config = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = new EmailConfig()
            };

            RunningData RunData = Load.Preload(Config);
            Load.ReportResults(Config, ref RunData);

            Assert.That(RunData.CampertronConfigFiles, Is.Empty);
            Assert.That(RunData.UniqueCampgroundIds, Is.Empty);
            Assert.That(RunData.Urls, Is.Empty, "nothing asked of recreation.gov");
            Assert.That(Sends, Is.EqualTo(0), "no email");
            AssertUnchanged(Before);
            Assert.That(Err.ToString(), Does.Contain("No search file has autoRun: true"));
            string Written = Out.ToString();
            switch (Output)
            {
                case OutputType.Console:
                    Assert.That(Written, Does.Contain("Running 0 searches:").And.Contain("Done: 0 searches, no matching availability."));
                    Assert.That(OpenedPages, Is.Empty);
                    break;
                case OutputType.HtmlFile:
                    Assert.That(OpenedPages, Has.Count.EqualTo(1), "the results page of the run, with no search on it");
                    Assert.That(File.ReadAllText(OpenedPages[0]), Does.Contain("No new matching availability to display."));
                    break;
                case OutputType.Email:
                    Assert.That(Written, Does.Contain(": 0 searches, nothing new."));
                    Assert.That(Directory.Exists(Path.Join(ConfigPath, "Camp.History")), Is.False);
                    break;
                case OutputType.UnitTest:
                    // no scope lines (Preload skips them for UnitTest); the run's empty results as for Console
                    Assert.That(Written, Does.Not.Contain("Running"));
                    Assert.That(Written, Does.Contain("Done: 0 searches, no matching availability."));
                    Assert.That(OpenedPages, Is.Empty);
                    break;
            }
        }

        // ---- D40: the samples are created atomically, only where no file exists, byte for byte as before ----

        // what the sample writers wrote before D40 (captured from master 1ce41ef); the bytes must not change
        private const string EmailSample = "smtpServer: ''\nsmtpPort: 0\nsmtpUsername: ''\nsmtpPassword: ''\nsendToAddressList: []\nsendFromAddress: ''\n";
        private const string ZionSample =
            "displayName: Zion non-group tent friendly sites\nautoRun: true\ncampgroundID: 232445\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "searchValueDates: []\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\n" +
            "showSunday: true\nincludeEquipment:\n- Tent\n- SMALL TENT\n- LARGE TENT OVER 9X12`\nexcludeEquipment: []\nincludeSites: []\nexcludeSites: []\n" +
            "includeAttributes:\n- Shade:Yes\n- Campfire Allowed:Yes\nexcludeAttributes: []\nincludeCampsiteType: []\nexcludeCampsiteType:\n- GROUP\nconsecutiveDays: 1\n";

        // the line break of Campertron's serializer on this platform (the samples are its output)
        private static readonly string SerializerEol = new YamlDotNet.Serialization.SerializerBuilder().Build().Serialize(new GeneralConfig()).EndsWith("\r\n") ? "\r\n" : "\n";

        private List<string> NonSettingsFiles() =>
            Directory.GetFiles(ConfigPath).Select(Path.GetFileName).Where(n => n!.EndsWith(".tmp") || n.EndsWith(".bak")).Select(n => n!).ToList();

        [Test]
        public void AFirstStart_CreatesEachSampleCompletely_WithTheFormerBytes_AndNoBackup()
        {
            GeneralConfig General = Yaml.GeneralConfigGetConfig(ConfigPath);
            Yaml.EmailConfigGetConfig(ConfigPath);
            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            Assert.That(File.ReadAllText(Path.Join(ConfigPath, "Email.yaml")), Is.EqualTo(EmailSample.Replace("\n", SerializerEol)));
            Assert.That(File.ReadAllText(ExamplePath), Is.EqualTo(ZionSample.Replace("\n", SerializerEol)));
            Assert.That(File.ReadAllText(Path.Join(ConfigPath, "General.yaml")), Is.EqualTo(
                ("outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: " + General.LastRidbDataRefresh!.Value.ToString("o") + "\nautoRefresh: false\n").Replace("\n", SerializerEol)));
            Assert.That(General.LastRidbDataRefresh!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(Searches.Single().DisplayName, Is.EqualTo("Zion non-group tent friendly sites"));
            Assert.That(NonSettingsFiles(), Is.Empty, "no temporary file and no backup");
        }

        [Test]
        public void ASampleThatAnotherProgramCreatesMeanwhile_IsKept_AndRead()
        {
            Action<string, string> Original = ConfigFileReplace.Step;
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    File.WriteAllText(Path_, Path.GetFileName(Path_) switch
                    {
                        "General.yaml" => "outputTo: HtmlFile # the other program's\n",
                        "Email.yaml" => "smtpServer: other.example.org\n",
                        _ => UserSearch("Another program's search", true)
                    });
                }
            };
            try
            {
                Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).OutputTo, Is.EqualTo(OutputType.HtmlFile));
                Assert.That(Yaml.EmailConfigGetConfig(ConfigPath).SmtpServer, Is.EqualTo("other.example.org"));
                Assert.That(Yaml.CampertronConfigGetConfigs(ConfigPath).Single().DisplayName, Is.EqualTo("Another program's search"));
            }
            finally
            {
                ConfigFileReplace.Step = Original;
            }

            Assert.That(File.ReadAllText(Path.Join(ConfigPath, "General.yaml")), Is.EqualTo("outputTo: HtmlFile # the other program's\n"));
            Assert.That(File.ReadAllText(ExamplePath), Is.EqualTo(UserSearch("Another program's search", true)));
            Assert.That(NonSettingsFiles(), Is.Empty);
        }

        [Test]
        public void AFailedSampleCreation_LeavesNoFile()
        {
            Action<string, string> Original = ConfigFileReplace.Step;
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "temporary-written")
                {
                    throw new IOException("injected");
                }
            };
            try
            {
                Assert.That(() => Yaml.EmailConfigGetConfig(ConfigPath), Throws.TypeOf<IOException>());
                Assert.That(() => Yaml.GeneralConfigGetConfig(ConfigPath), Throws.TypeOf<IOException>());
            }
            finally
            {
                ConfigFileReplace.Step = Original;
            }

            Assert.That(Directory.GetFiles(ConfigPath), Is.Empty, "no settings file, no empty or partial one, no temporary file");
        }

        [Test]
        public void TheSampleWriters_NeverTruncateOrOverwriteAnExistingFile()
        {
            string General = Path.Join(ConfigPath, "General.yaml");
            string Email = Path.Join(ConfigPath, "Email.yaml");
            File.WriteAllText(General, "outputTo: Email # mine\n");
            File.WriteAllText(Email, "smtpServer: mine\n");
            WriteSearch("ZionConfig.yaml", UserSearch("Mine", false));
            var Before = Snapshot();

            Assert.That(() => Yaml.GeneralConfigConvertToYaml(new GeneralConfig(), "General", ConfigPath), Throws.InstanceOf<IOException>().With.Message.Contains("already exists"));
            Assert.That(() => Yaml.EmailConfigConvertToYaml(new EmailConfig(), "Email", ConfigPath), Throws.InstanceOf<IOException>().With.Message.Contains("already exists"));
            Assert.That(() => Yaml.CampertronConfigConvertToYaml(new CampertronConfig(), "ZionConfig", ConfigPath), Throws.InstanceOf<IOException>().With.Message.Contains("already exists"));

            AssertUnchanged(Before);
        }

        [Test]
        public void TemporaryAndBackupFiles_AreNeverSearchFiles()
        {
            WriteSearch("Trip.yaml.bak", UserSearch("A backup", true));
            WriteSearch("Trip.yaml." + Guid.NewGuid().ToString("N") + ".tmp", UserSearch("A temporary file", true));
            var Before = Snapshot();

            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(ConfigPath);

            // neither counts as a search file, so the folder has none and the example is created
            Assert.That(Searches.Select(S => S.DisplayName), Is.EqualTo(new[] { "Zion non-group tent friendly sites" }));
            foreach (var File_ in Before)
            {
                Assert.That(File.ReadAllBytes(Path.Join(ConfigPath, File_.Key)), Is.EqualTo(File_.Value.Bytes));
            }
        }
    }
}
