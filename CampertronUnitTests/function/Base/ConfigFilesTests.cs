using System.Globalization;
using System.Reflection;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Which settings file a file is (ConfigFiles, D41): one ordinal, case-insensitive rule for every caller, the same in
    // every culture. Before D41 the search loader lower-cased the path with the current culture, so in Turkish
    // "EMAIL.yaml" became "emaıl.yaml" and was loaded as a search file. Discovery stays the top-level *.yaml files.
    public class ConfigFilesTests
    {
        private string Root = string.Empty;
        private CultureInfo OriginalCulture = null!;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronConfigFilesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalCulture = CultureInfo.CurrentCulture;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = OriginalCulture;
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Directory.Delete(Root, true);
        }

        private static readonly string[] Cultures = { "tr-TR", "az-Latn-AZ", "lt-LT", "de-DE", "ja-JP", "en-US", "" };

        [TestCase("General.yaml", "General")]
        [TestCase("general.yaml", "General")]
        [TestCase("GENERAL.YAML", "General")]
        [TestCase("GeNeRaL.yAmL", "General")]
        [TestCase("Email.yaml", "Email")]
        [TestCase("email.yaml", "Email")]
        [TestCase("EMAIL.YAML", "Email")]
        [TestCase("EMAIL.yaml", "Email")]
        [TestCase("eMaIl.YaMl", "Email")]
        [TestCase("ZionConfig.yaml", "Search")]
        [TestCase("General2.yaml", "Search")]
        [TestCase("MyEmail.yaml", "Search")]
        [TestCase("General.yml", "Search")]
        [TestCase(" General.yaml", "Search")]
        [TestCase("EMA\u0130L.yaml", "Search")]
        [TestCase("Ema\u0131l.yaml", "Search")]
        public void Classify_IsOrdinalAndCaseInsensitive_InEveryCulture(string Name, string ExpectedKind)
        {
            ConfigFileKind Expected = Enum.Parse<ConfigFileKind>(ExpectedKind);
            foreach (string Culture in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(Culture);
                Assert.That(ConfigFiles.Classify(Name), Is.EqualTo(Expected), Culture);
                Assert.That(ConfigFiles.Classify(Path.Join(Root, Name)), Is.EqualTo(Expected), "a path is classified by its file name");
            }
        }

        [Test]
        public void SearchFiles_AreTheTopLevelYamlFiles_ExceptGeneralAndEmail()
        {
            foreach (string Name in new[] { "General.yaml", "EMAIL.yaml", "Trip.yaml", "Other.yaml", "Old.yml", "Trip.yaml.bak", "General.yaml.bak",
                "General.yaml." + Guid.NewGuid().ToString("N") + ".tmp", "Trip.yaml." + Guid.NewGuid().ToString("N") + ".tmp", "notes.txt" })
            {
                File.WriteAllText(Path.Join(Root, Name), "x: 1\n");
            }
            Directory.CreateDirectory(Path.Join(Root, "sub"));
            File.WriteAllText(Path.Join(Root, "sub", "Nested.yaml"), "x: 1\n");

            foreach (string Culture in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(Culture);
                Assert.That(ConfigFiles.SearchFiles(Root).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal), Is.EqualTo(new[] { "Other.yaml", "Trip.yaml" }), Culture);
            }
        }

        [Test]
        public void InTurkish_TheSearchLoader_NeverReadsEmailYamlAsASearch()
        {
            // the email settings under an upper-case name, and one disabled search: before D41 the loader lower-cased
            // "EMAIL.yaml" to "emaıl.yaml" in Turkish and failed on its keys as a search file
            File.WriteAllText(Path.Join(Root, "EMAIL.yaml"), "smtpServer: smtp.example.org\nsmtpPort: 587\n");
            File.WriteAllText(Path.Join(Root, "GENERAL.yaml"), "outputTo: Console\n");
            File.WriteAllText(Path.Join(Root, "Trip.yaml"), "displayName: Trip\nautoRun: false\n");

            foreach (string Culture in Cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(Culture);
                Assert.That(Yaml.CampertronConfigGetConfigs(Root), Is.Empty, Culture);
            }
        }

        [Test]
        public void ViewConfig_AndTheFailureMessages_UseTheSameRule()
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            File.WriteAllText(Path.Join(Root, "EMAIL.yaml"), "smtpServer: x\n");
            File.WriteAllText(Path.Join(Root, "Trip.yaml"), "displayName: Trip\n");
            var Config = new CampertronInternalConfig { ConfigPath = Root, CachePath = Root, GeneralConfig = new GeneralConfig() };

            List<string> Lines = Menu.ViewConfigLines(Config, DateTime.UtcNow, CultureInfo.InvariantCulture).Select(L => L.Text).ToList();
            Assert.That(Lines.Single(L => L.Contains("Searches")), Does.EndWith("1 search file"));

            var Failure = new InvalidOperationException("inner text");
            ConsoleUi.NoteFile(Failure, Path.Join(Root, "EMAIL.yaml"));
            List<string> Described = ConsoleUi.DescribeFailure(Failure);
            Assert.That(Described[2], Is.EqualTo("Fix the file, or delete it and Campertron creates a new one."), "EMAIL.yaml is Email.yaml");
        }

        [Test]
        public void EveryFormerCaller_UsesTheSharedRule_AndNoneLowerCasesAName()
        {
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            MethodBase[] Callers =
            {
                // the loader's body (since D64 the public method passes its warning and count to this overload)
                typeof(Yaml).GetMethod(nameof(Yaml.CampertronConfigGetConfigs), All, new[] { typeof(string), typeof(bool), typeof(bool), typeof(int).MakeByRefType() })!,
                // View Config's count since D60: CurrentConfiguration, shared with the GUI's Settings > System
                typeof(CurrentConfiguration).GetMethod(nameof(CurrentConfiguration.SearchFileCount), All)!,
                typeof(ConsoleUi).GetMethod(nameof(ConsoleUi.DescribeFailure), All)!
            };
            Assert.That(YamlDocumentPreservationTests.ReferencedMembers(typeof(Menu).GetMethod(nameof(Menu.ViewConfigLines), All)!)
                .Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name), Does.Contain("CampertronLibrary.function.Base.CurrentConfiguration::Describe"),
                "View Config shows CurrentConfiguration's items");
            foreach (MethodBase Caller in Callers)
            {
                List<string> Called = YamlDocumentPreservationTests.ReferencedMembers(Caller).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
                Assert.That(Called.Any(C => C.StartsWith("CampertronLibrary.function.Base.ConfigFiles::", StringComparison.Ordinal)), Is.True, Caller.Name);
                Assert.That(Called.Where(C => C.StartsWith("System.String::ToLower", StringComparison.Ordinal) || C.StartsWith("System.String::ToUpper", StringComparison.Ordinal)), Is.Empty, Caller.Name);
            }
            List<string> ClassifyCalls = YamlDocumentPreservationTests.ReferencedMembers(typeof(ConfigFiles).GetMethod(nameof(ConfigFiles.Classify), All)!)
                .Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
            Assert.That(ClassifyCalls, Does.Contain("System.String::Equals"));
            Assert.That(ClassifyCalls.Where(C => C.Contains("ToLower") || C.Contains("ToUpper") || C.Contains("CultureInfo")), Is.Empty);
        }
    }
}
