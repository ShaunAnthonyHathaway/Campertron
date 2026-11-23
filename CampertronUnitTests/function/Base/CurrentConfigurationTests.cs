using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Campertron's current configuration (decision D60, V011-2): the eight View Config items built once by
    // CurrentConfiguration, written by the console's View Config (Describe → Menu.ViewConfigLines, byte for byte as before)
    // and shown by the GUI's Settings > System (Read). Read takes General.yaml as a start reads it but never creates or
    // writes anything, and nothing in it holds a value of Email.yaml, a search file's name or content, another General.yaml
    // value or an environment variable's value. Each test has its own temporary settings folder; no network, no database.
    public class CurrentConfigurationTests
    {
        private const string Sentinel = "CurrentConfig-Sentinel-7f3a";
        private static readonly CultureInfo EnUs = new CultureInfo("en-US");
        private static readonly DateTime Now = new DateTime(2026, 11, 14, 21, 52, 0, DateTimeKind.Utc);
        private static readonly Assembly Library = typeof(RuntimeInfo).Assembly;
        private const string GeneralText = "# my settings " + Sentinel + "\noutputTo: HtmlFile\nrefreshRidbDataDayInterval: 4321\n" +
            "lastRidbDataRefresh: '2026-11-10T00:00:00.0000000Z'\nautoRefresh: false\n";

        private string Root = string.Empty;
        private string Cache = string.Empty;
        private CultureInfo OriginalCulture = null!;

        [SetUp]
        public void Setup()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = EnUs;
            Root = Path.Join(Path.GetTempPath(), "CampertronCurrentConfigurationTests-" + Guid.NewGuid().ToString("N"));
            Cache = Path.Join(Root, "cache");
            Directory.CreateDirectory(Root);
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture;
            if (!OperatingSystem.IsWindows())
            {
                foreach (string Entry in Directory.GetFileSystemEntries(Root))
                {
                    if (new FileInfo(Entry).LinkTarget == null && (File.Exists(Entry) || Directory.Exists(Entry)))
                    {
                        File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }
            }
            Directory.Delete(Root, true);
        }

        private string Write(string Name, string Text)
        {
            string FilePath = Path.Join(Root, Name);
            File.WriteAllText(FilePath, Text);
            return FilePath;
        }

        private ConfigurationSummary Read(string? CachePath = "") =>
            CurrentConfiguration.Read(Root, CachePath == "" ? Cache : CachePath, Now, EnUs, Library);

        private static string Value(ConfigurationSummary Summary, string Label) => Summary.Items.Single(i => i.Label == Label).Value;

        // every entry below Folder (files, folders, links): its relative path, then a file's length, time and SHA-256
        private static Dictionary<string, string> Fingerprint(string Folder)
        {
            return Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories).ToDictionary(
                f => Path.GetRelativePath(Folder, f),
                f => new FileInfo(f).LinkTarget != null ? "link " + new FileInfo(f).LinkTarget
                    : Directory.Exists(f) ? "folder"
                    : new FileInfo(f).Length == 0 ? "empty or special " + File.GetLastWriteTimeUtc(f).Ticks
                    : new FileInfo(f).Length + " " + File.GetLastWriteTimeUtc(f).Ticks + " " + System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
        }

        private static string Os => RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")";
        private static string Framework => RuntimeInformation.FrameworkDescription + " (" + RuntimeInformation.ProcessArchitecture + ")";

        // ---- the items ----

        // The eight items in their order, with the labels of the console's View Config, read from the GUI's folders and
        // General.yaml as it is now.
        [Test]
        public void Read_GivesTheEightItemsInOrder()
        {
            Write("General.yaml", GeneralText);
            Write("Zion.yaml", "displayName: Zion\n");
            Write("Mather.yaml", "displayName: Mather\n");
            Write("notes.txt", "not a search file");

            ConfigurationSummary Summary = Read();

            Assert.That(CurrentConfiguration.Labels, Is.EqualTo(new[] { "Version", "Config", "Cache", "Output", "Searches", "RIDB refresh", "OS", "Framework" }));
            Assert.That(Summary.Items.Select(i => i.Label), Is.EqualTo(CurrentConfiguration.Labels));
            Assert.That(Summary.Items.Select(i => i.Value), Is.EqualTo(new[]
            {
                RuntimeInfo.Describe(Library).Split(Environment.NewLine)[0],
                Root,
                Cache,
                "HtmlFile",
                "2 search files",
                "4 days ago (11/10/2026)",
                Os,
                Framework,
            }));
            Assert.That(Summary.Items.Select(i => i.Text), Is.EqualTo(Summary.Items.Select(i => i.Label + " => " + i.Value)));
            Assert.That(Summary.Text, Is.EqualTo(string.Join(Environment.NewLine, Summary.Items.Select(i => i.Text))), "one line per item, no final line break");
            Assert.That(Summary.Text.EndsWith(Environment.NewLine), Is.False);
        }

        // The console's View Config is Describe's items, line for line: the same text and the same label color as before
        // D60 (ConsoleOutputTests keeps the lines' exact text), and for the same settings Read gives the same text.
        [Test]
        public void ViewConfig_WritesDescribesItems_AndReadMatchesItForTheSameSettings()
        {
            string General = Write("General.yaml", GeneralText);
            Write("Zion.yaml", "displayName: Zion\n");
            var Config = new CampertronInternalConfig { ConfigPath = Root, CachePath = Cache, GeneralConfig = Yaml.GeneralConfigConvertFromYaml(General) };

            ConfigurationSummary Described = CurrentConfiguration.Describe(Config, Now, EnUs, Library);
            List<ConsoleUi.StyledLine> Lines = Menu.ViewConfigLines(Config, Now, EnUs, Library);

            Assert.That(Lines.Select(l => l.Text), Is.EqualTo(Described.Items.Select(i => i.Text)));
            Assert.That(Lines.Select(l => l.Spans.Count), Is.All.EqualTo(2));
            Assert.That(Lines.Select(l => l.Spans[0].Text), Is.EqualTo(Described.Items.Select(i => i.Label + " => ")));
            Assert.That(Lines.All(l => l.Spans[0].Color == ConsoleColor.Green && l.Spans[1].Color == null), Is.True);
            Assert.That(Read().Text, Is.EqualTo(Described.Text), "the GUI shows what View Config shows for the same settings");
            Assert.That(Lines.Last().Text, Is.EqualTo(RuntimeInfo.Describe(Library).Split(Environment.NewLine)[1]), "Framework is --version's second line");
        }

        // The console keeps what View Config showed for settings it does not have: empty paths and output, "never".
        [Test]
        public void Describe_WithoutSettings_KeepsViewConfigsEmptyValues()
        {
            ConfigurationSummary Summary = CurrentConfiguration.Describe(new CampertronInternalConfig(), Now, EnUs, Library);

            Assert.That(Value(Summary, "Config"), Is.Empty);
            Assert.That(Value(Summary, "Cache"), Is.Empty);
            Assert.That(Value(Summary, "Output"), Is.Empty);
            Assert.That(Value(Summary, "Searches"), Is.EqualTo("0 search files"), "no folder: 0, as before");
            Assert.That(Value(Summary, "RIDB refresh"), Is.EqualTo("never (the campground list that came with Campertron is in use)"));
        }

        // The last RIDB refresh in words, as View Config says it: today, N days ago, or never for the sample's date 50 years
        // back and for none; and the GUI reads the file again each time, so a save shows at the next read.
        [Test]
        public void TheRefreshAndTheOutput_FollowGeneralYamlAsItIsNow()
        {
            string General = Write("General.yaml", GeneralText);
            Assert.That(Value(Read(), "RIDB refresh"), Is.EqualTo("4 days ago (11/10/2026)"));

            File.WriteAllText(General, GeneralText.Replace("2026-11-10T00:00:00", "2026-11-14T20:00:00").Replace("HtmlFile", "Email"));
            Assert.That(Value(Read(), "RIDB refresh"), Is.EqualTo("today (11/14/2026)"));
            Assert.That(Value(Read(), "Output"), Is.EqualTo("Email"));

            File.WriteAllText(General, GeneralText.Replace("2026-11-10T00:00:00", "2026-11-13T20:00:00"));
            Assert.That(Value(Read(), "RIDB refresh"), Is.EqualTo("1 day ago (11/13/2026)"));

            File.WriteAllText(General, GeneralText.Replace("2026-11-10T00:00:00", "1976-11-14T21:52:00"));
            Assert.That(Value(Read(), "RIDB refresh"), Is.EqualTo("never (the campground list that came with Campertron is in use)"), "the sample's date");

            File.WriteAllText(General, "outputTo: Console\n");
            ConfigurationSummary Defaults = Read();
            Assert.That(Value(Defaults, "RIDB refresh"), Is.EqualTo("never (the campground list that came with Campertron is in use)"), "no date");
            Assert.That(Value(Defaults, "Output"), Is.EqualTo("Console"));
            File.WriteAllText(General, "autoRefresh: false\n");
            Assert.That(Value(Read(), "Output"), Is.EqualTo("Console"), "a missing outputTo is its default, as a start reads it");
            File.WriteAllText(General, "outputTo: UnitTest\n");
            Assert.That(Value(Read(), "Output"), Is.EqualTo("UnitTest"), "loaded as a start loads it");
        }

        // Dates follow the culture passed in, counts the current culture (as ConsoleUi.Count does for every console count);
        // the days are counted from the UTC time.
        [TestCase("en-US", "1,234 days ago (6/29/2023)")]
        [TestCase("de-DE", "1.234 days ago (29.06.2023)")]
        [TestCase("tr-TR", "1.234 days ago (29.06.2023)")]
        [TestCase("ja-JP", "1,234 days ago (2023/06/29)")]
        public void TheRefreshDate_FollowsTheCulture(string Name, string Expected)
        {
            var Culture = new CultureInfo(Name);
            CultureInfo.CurrentCulture = Culture;
            DateTime Refreshed = Now.AddDays(-1234).AddHours(-1);
            Write("General.yaml", "outputTo: Console\nlastRidbDataRefresh: '" + Refreshed.ToString("o", CultureInfo.InvariantCulture) + "'\n");

            Assert.That(Value(CurrentConfiguration.Read(Root, Cache, Now, Culture, Library), "RIDB refresh"), Is.EqualTo(Expected));
            Assert.That(CurrentConfiguration.RefreshText(Refreshed, Now, Culture), Is.EqualTo(Expected));
            Assert.That(CurrentConfiguration.RefreshText(Now.AddDays(1), Now, Culture), Does.StartWith("today ("), "a date in the future is today, as before");
        }

        // ---- read-only ----

        // Without General.yaml the GUI says unknown for the two values it would read and creates nothing: no General.yaml (a
        // start would create the sample), no other file, no folder.
        [Test]
        public void WithoutGeneralYaml_OutputAndRefreshAreUnknown_AndNothingIsCreated()
        {
            Write("Zion.yaml", "displayName: Zion\n");
            Dictionary<string, string> Before = Fingerprint(Root);

            ConfigurationSummary Summary = Read();

            Assert.That(Value(Summary, "Output"), Is.EqualTo("unknown"));
            Assert.That(Value(Summary, "RIDB refresh"), Is.EqualTo("unknown"));
            Assert.That(Value(Summary, "Searches"), Is.EqualTo("1 search file"));
            Assert.That(Value(Summary, "Cache"), Is.EqualTo(Cache));
            Assert.That(File.Exists(Path.Join(Root, "General.yaml")), Is.False, "General.yaml was not created");
            Assert.That(Directory.Exists(Cache), Is.False, "the cache folder was not created");
            Assert.That(Fingerprint(Root), Is.EqualTo(Before));
        }

        // A General.yaml that a start can't use (empty, only comments, malformed, an unknown key, an undefined outputTo, over
        // the loader's limit, a non-finite interval with autoRefresh) is unknown, and stays exactly as it is.
        [TestCase("")]
        [TestCase("# only a comment\n")]
        [TestCase("outputTo: [Console\n")]
        [TestCase("outputTo: Console\nsearchBy: DaysOut\n")]
        [TestCase("outputTo: 7\n")]
        [TestCase("outputTo: Sometimes\n")]
        [TestCase("lastRidbDataRefresh: not a date\n")]
        [TestCase("autoRefresh: true\nrefreshRidbDataDayInterval: .nan\n")]
        [TestCase("BIG")]
        public void AGeneralYamlAStartRefuses_IsUnknown_AndUnchanged(string Text)
        {
            Write("General.yaml", Text == "BIG" ? "outputTo: Console\n#" + new string('x', 3_200_000) + "\n" : Text);
            Dictionary<string, string> Before = Fingerprint(Root);

            ConfigurationSummary Summary = Read();

            Assert.That(Value(Summary, "Output"), Is.EqualTo("unknown"));
            Assert.That(Value(Summary, "RIDB refresh"), Is.EqualTo("unknown"));
            Assert.That(Fingerprint(Root), Is.EqualTo(Before), "nothing changed in the folder");
        }

        // A General.yaml the GUI must not open or can't read: a folder of that name, a link to nothing, a FIFO (opening one
        // would block; it reports length 0 and is never opened) and a file without read permission.
        [Test]
        public void AGeneralYamlThatIsNoReadableFile_IsUnknown_WithoutBlocking()
        {
            string General = Path.Join(Root, "General.yaml");
            Directory.CreateDirectory(General);
            Assert.That(Value(Read(), "Output"), Is.EqualTo("unknown"), "a folder");
            Directory.Delete(General);
            File.CreateSymbolicLink(General, Path.Join(Root, "missing.yaml"));
            Assert.That(Value(Read(), "Output"), Is.EqualTo("unknown"), "a link to nothing");
            Assert.That(File.Exists(Path.Join(Root, "missing.yaml")), Is.False, "the link's target was not created");
            File.Delete(General);
            if (OperatingSystem.IsWindows())
            {
                Assert.Pass("FIFOs and Unix permissions are Unix only");
            }
            using (Process? Make = Process.Start("mkfifo", General))
            {
                Make!.WaitForExit();
            }
            Task<ConfigurationSummary> Reading = Task.Run(() => Read());
            Assert.That(Task.WhenAny(Reading, Task.Delay(TimeSpan.FromSeconds(10))).Result, Is.SameAs(Reading), "not opened: opening a FIFO would block");
            Assert.That(Value(Reading.Result, "Output"), Is.EqualTo("unknown"), "a FIFO");
            File.Delete(General);
            if (!OperatingSystem.IsWindows() && Environment.UserName != "root")
            {
                Write("General.yaml", GeneralText);
                File.SetUnixFileMode(General, UnixFileMode.None);
                Assert.That(Value(Read(), "Output"), Is.EqualTo("unknown"), "no read permission");
                Assert.That(File.GetUnixFileMode(General), Is.EqualTo(UnixFileMode.None), "its mode is unchanged");
            }
        }

        // ---- what is never shown ----

        // the text with a version's build metadata (`+` and a commit's hexadecimal hash, as RuntimeInfo shows it) replaced
        internal static string WithoutCommit(string Text) => System.Text.RegularExpressions.Regex.Replace(Text, @"\+[0-9a-f]{7,64}\b", "+<commit>");

        // Nothing of Email.yaml (its server, user name, password or addresses), no other General.yaml value or comment, no
        // search file's name or content and no environment variable's value appears in any item; the search files are only
        // counted.
        [Test]
        public void NoSecretNoOtherSettingNoSearchFileAndNoEnvironmentValue_IsShown()
        {
            Write("General.yaml", GeneralText);
            Write("Email.yaml", "smtpServer: smtp." + Sentinel + ".example\nsmtpPort: 587\nsmtpUsername: user-" + Sentinel + "\nsmtpPassword: pw-" + Sentinel +
                "\nsendToAddressList:\n  - to-" + Sentinel + "@example.org\nsendFromAddress: from-" + Sentinel + "@example.org\n");
            Write("Trip-" + Sentinel + ".yaml", "displayName: Search " + Sentinel + "\ncampgroundID: '232445'\n");
            Write("Other.yaml", "# " + Sentinel + "\ndisplayName: Other\n");
            string Variable = "CAMPERTRON_CURRENT_CONFIG_TEST";
            Environment.SetEnvironmentVariable(Variable, "env-" + Sentinel);
            try
            {
                foreach (ConfigurationSummary Summary in new[]
                {
                    Read(),
                    CurrentConfiguration.Describe(new CampertronInternalConfig
                    {
                        ConfigPath = Root, CachePath = Cache, GeneralConfig = Yaml.GeneralConfigConvertFromYaml(Path.Join(Root, "General.yaml")),
                        EmailConfig = Yaml.EmailConfigConvertFromYaml(Path.Join(Root, "Email.yaml"))
                    }, Now, EnUs, Library),
                })
                {
                    //the version's build metadata is a commit's hexadecimal hash, which can hold any digits (the merge commit
                    //c25412e…959f5870beb7 holds "587"): it is left out of the checks for digits, never for the sentinel
                    string Shown = WithoutCommit(Summary.Text);
                    Assert.That(Summary.Text, Does.Not.Contain(Sentinel));
                    Assert.That(Shown, Does.Not.Contain("4321"), "refreshRidbDataDayInterval");
                    Assert.That(Shown, Does.Not.Contain("smtp").IgnoreCase.And.Not.Contain("587").And.Not.Contain("@"));
                    Assert.That(Summary.Text, Does.Not.Contain("autoRefresh").And.Not.Contain("Trip").And.Not.Contain("Other.yaml"));
                    Assert.That(Value(Summary, "Searches"), Is.EqualTo("2 search files"), "counted, not named");
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(Variable, null);
            }
        }

        // The search files are counted by the console's rule, by name only: General.yaml and Email.yaml in any case, .yml
        // files, subfolders, backups and temporary files are not search files; an unreadable or malformed search file still
        // counts (it is never opened).
        [Test]
        public void TheSearchFiles_AreCountedByTheConsolesRule_WithoutBeingOpened()
        {
            Write("General.yaml", GeneralText);
            Write("EMAIL.yaml", "smtpServer: x\n");
            Write("GENERAL.yaml", "outputTo: Console\n");
            Write("one.yml", "displayName: yml\n");
            Write("Broken.yaml", "displayName: [unclosed\n");
            Write("Trip.yaml.bak", "displayName: backup\n");
            Write("Trip.yaml.0123456789abcdef0123456789abcdef.tmp", "displayName: temporary\n");
            Directory.CreateDirectory(Path.Join(Root, "sub"));
            File.WriteAllText(Path.Join(Root, "sub", "Nested.yaml"), "displayName: nested\n");
            string Locked = Write("Locked.yaml", "displayName: locked\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Locked, UnixFileMode.None);
            }

            Assert.That(Value(Read(), "Searches"), Is.EqualTo("2 search files"), "Broken.yaml and Locked.yaml");
            Assert.That(CurrentConfiguration.SearchFileCount(Root), Is.EqualTo(ConfigFiles.SearchFiles(Root).Count));
            Assert.That(CurrentConfiguration.SearchFileCount(Path.Join(Root, "no such folder")), Is.Zero, "a folder that can't be listed: 0, as before");
            Assert.That(CurrentConfiguration.SearchFileCount(null), Is.Zero);
        }

        // Without a cache folder the GUI says unknown; with neither folder nothing is created.
        [Test]
        public void WithoutACacheFolder_TheCacheIsUnknown()
        {
            Assert.That(Value(Read(null), "Cache"), Is.EqualTo("unknown"));
        }

        // The summary can't be changed after it was built.
        [Test]
        public void TheSummary_IsImmutable()
        {
            ConfigurationSummary Summary = Read();
            Assert.That(() => ((IList<ConfigurationItem>)Summary.Items).Add(new ConfigurationItem("X", "Y")), Throws.TypeOf<NotSupportedException>());
            Assert.That(typeof(ConfigurationItem).GetProperties().All(p => !p.CanWrite), Is.True);
            Assert.That(typeof(ConfigurationSummary).GetProperties().All(p => !p.CanWrite), Is.True);
        }

        // From its IL: CurrentConfiguration never loads Email.yaml, never creates or writes a file or folder (not through
        // General.yaml's start loader, which creates the sample, nor ConfigFileReplace), never reads an environment
        // variable, never opens the campground database and never makes a web request.
        [Test]
        public void TheSummary_ReadsNoEmailYamlNoEnvironmentNoDatabase_AndWritesNothing()
        {
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            List<string> Called = new[] { typeof(CurrentConfiguration), typeof(ConfigurationSummary), typeof(ConfigurationItem) }
                .SelectMany(T => T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                .SelectMany(M => YamlDocumentPreservationTests.ReferencedMembers(M))
                .Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).Distinct().ToList();

            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.Yaml::LoadSettingsFile"), "the scan sees the loader");
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConfigFiles::SearchFiles"), "the scan sees the count");
            string[] Forbidden =
            {
                "::EmailConfig", "EmailConfig::", "::GeneralConfigGetConfig", "::GeneralConfigGenerateSampleConfig", "::CreateSampleSettingsFile",
                "ConfigFileReplace::", "::GeneralConfigReplaceYaml", "System.IO.File::Write", "System.IO.File::Create", "System.IO.File::Open",
                "System.IO.File::Delete", "System.IO.File::Move", "System.IO.File::Copy", "System.IO.Directory::Create", "System.IO.Directory::Delete",
                "System.IO.FileStream::", "System.Environment::GetEnvironmentVariable", "System.Environment::ExpandEnvironmentVariables",
                "Microsoft.Data.Sqlite", "Microsoft.EntityFrameworkCore", "RecDotOrg", "System.Net.", "System.Console::",
            };
            foreach (string Bad in Forbidden)
            {
                Assert.That(Called.Where(C => C.Contains(Bad, StringComparison.Ordinal)), Is.Empty, Bad);
            }
        }
    }
}
