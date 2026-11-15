using System.Diagnostics;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The bounded settings loader (TD-52, D41): every settings file is read and deserialized within the editor's limits
    // (YamlDocument, D39) by YamlDotNet's own parser, so the loader, the validation and the editor accept and refuse the
    // same documents, exactly at each boundary. Ordinary files, the samples and the documented examples load as before,
    // decoded as before. Each test uses its own temporary folder; no real settings folder is used.
    public class SettingsLoaderLimitsTests
    {
        private string Root = string.Empty;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private const string Secret = "S3cr3t-Pa55";

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronSettingsLoaderLimitsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Directory.Delete(Root, true);
        }

        private static Yaml.SettingsLimitException Limit(Action Load)
        {
            try
            {
                Load();
            }
            catch (Yaml.SettingsLimitException Refused)
            {
                Assert.That(Refused.Message, Does.Not.Contain(Secret));
                return Refused;
            }
            Assert.Fail("expected the limit to refuse the document");
            return null!;
        }

        private static bool EditorRefuses(string Text, YamlDocumentCode Code) => YamlDocument.Analyze(Text).Diagnostics.Any(D => D.Code == Code);

        private static string ExamplesFolder()
        {
            for (DirectoryInfo? At = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); At != null; At = At.Parent)
            {
                string Candidate = Path.Join(At.FullName, "examples", "0.1");
                if (Directory.Exists(Candidate))
                {
                    return Candidate;
                }
            }
            Assert.Fail("examples/0.1 not found above the test folder");
            return string.Empty;
        }

        // ---- ordinary files ----

        [Test]
        public void TheSamples_AndTheDocumentedExamples_Load_AndTheLoaderValidationAndEditorAgree()
        {
            Yaml.GeneralConfigGetConfig(Root);
            Yaml.EmailConfigGetConfig(Root);
            Yaml.CampertronConfigGetConfigs(Root);
            var Files = new List<string> { "General.yaml", "Email.yaml", "ZionConfig.yaml" }.Select(N => Path.Join(Root, N)).ToList();
            foreach (string Example in Directory.GetFiles(ExamplesFolder(), "*.yaml"))
            {
                string Copy = Path.Join(Root, Path.GetFileName(Example) == "ZionConfig.yaml" ? "ZionExample.yaml" : Path.GetFileName(Example));
                File.Copy(Example, Copy);
                Files.Add(Copy);
            }
            Assert.That(Files, Has.Count.EqualTo(5));

            foreach (string FilePath in Files)
            {
                string Text = Yaml.ReadSettingsText(FilePath).Text;
                List<ConfigDiagnostic> Found = ConfigValidation.ValidateFile(FilePath, DateTime.Now);
                Assert.That(Found.Where(D => D.Severity == ConfigSeverity.Error), Is.Empty, FilePath);
                switch (ConfigFiles.Classify(FilePath))
                {
                    case ConfigFileKind.General:
                        AssertAgree(Text, Yaml.GeneralConfigConvertFromYaml(FilePath));
                        break;
                    case ConfigFileKind.Email:
                        AssertAgree(Text, Yaml.EmailConfigConvertFromYaml(FilePath));
                        break;
                    default:
                        AssertAgree(Text, Yaml.CampertronConfigConvertFromYaml(FilePath));
                        break;
                }
            }
        }

        private static void AssertAgree<T>(string Text, T Loaded) where T : class
        {
            Assert.That(Loaded, Is.Not.Null);
            // the editor's check loads the same text with the same deserializer and compares it with the loader's result
            YamlDocumentResult Verified = YamlDocument.VerifyCandidate(Text, Array.Empty<YamlDocumentEdit>(), Loaded, Text);
            Assert.That(Verified.Succeeded, Is.True, string.Join("; ", Verified.Diagnostics));
            Assert.That(YamlDocument.Analyze(Text).Succeeded, Is.True);
        }

        [Test]
        public void TheDecoding_IsTheFormerOne_AndTheValidationSaysWhatTheEditorCantUse()
        {
            string General = Path.Join(Root, "General.yaml");
            File.WriteAllBytes(General, Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("outputTo: Email\n")).ToArray());
            Assert.That(Yaml.GeneralConfigConvertFromYaml(General).OutputTo, Is.EqualTo(OutputType.Email), "UTF-16 with a byte-order mark loads, as before");
            Assert.That(ConfigValidation.ValidateFile(General, DateTime.Now).Select(D => (D.Code, D.Severity)), Does.Contain((ConfigCode.NotUtf8, ConfigSeverity.Warning)));

            File.WriteAllBytes(General, Encoding.UTF8.GetBytes("outputTo: Email # caf").Append((byte)0xE9).Concat(Encoding.UTF8.GetBytes("\n")).ToArray());
            Assert.That(Yaml.GeneralConfigConvertFromYaml(General).OutputTo, Is.EqualTo(OutputType.Email), "an invalid UTF-8 byte in a comment loads, as before");
            Assert.That(ConfigValidation.ValidateFile(General, DateTime.Now).Select(D => (D.Code, D.Severity)), Does.Contain((ConfigCode.InvalidUtf8, ConfigSeverity.Warning)));

            File.WriteAllBytes(General, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("outputTo: Email\r\nautoRefresh: false\r\n")).ToArray());
            Assert.That(Yaml.GeneralConfigConvertFromYaml(General).OutputTo, Is.EqualTo(OutputType.Email));
            Assert.That(Yaml.ReadSettingsText(General).Text, Does.StartWith("\uFEFFoutputTo"), "the byte-order mark is kept as a leading U+FEFF, as the editor expects");
            Assert.That(ConfigValidation.ValidateFile(General, DateTime.Now), Is.Empty, "CRLF and a UTF-8 byte-order mark are ordinary");
        }

        [Test]
        public void TheScannersInvalidOperation_IsAYamlErrorForTheLoader_AndARefusalForTheEditor()
        {
            // YamlDotNet 16.3.0 throws InvalidOperationException for an unclosed flow list followed by another line
            const string Text = "outputTo: [Console\nrefreshRidbDataDayInterval: 30\n";
            Assert.That(() => Yaml.LoadSettingsText<GeneralConfig>(Text), Throws.TypeOf<YamlDotNet.Core.YamlException>());
            Assert.That(EditorRefuses(Text, YamlDocumentCode.Malformed), Is.True, "the editor refuses it instead of throwing");
            Assert.That(ConfigValidation.ValidateGeneral(Text, DateTime.Now).Single().Code, Is.EqualTo(ConfigCode.Malformed));
        }

        // ---- size ----

        [Test]
        public void TheCharacterLimit_IsExact_AndAByteOrderMarkCountsAsOne()
        {
            int Max = YamlDocument.MaxDocumentLength;
            string Head = "outputTo: Console\n# " + Secret + " ";
            string AtLimit = Head + new string('x', Max - Head.Length);
            Assert.That(AtLimit, Has.Length.EqualTo(Max));

            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(AtLimit).OutputTo, Is.EqualTo(OutputType.Console));
            Assert.That(Limit(() => Yaml.LoadSettingsText<GeneralConfig>(AtLimit + "x")).Code, Is.EqualTo(ConfigCode.FileTooLarge));
            Assert.That(Limit(() => Yaml.LoadSettingsText<GeneralConfig>("\uFEFF" + AtLimit)).Code, Is.EqualTo(ConfigCode.FileTooLarge), "the byte-order mark counts");
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>("\uFEFF" + AtLimit.Substring(0, Max - 1)).OutputTo, Is.EqualTo(OutputType.Console));
            Assert.That(EditorRefuses(AtLimit + "x", YamlDocumentCode.TooLarge) && !EditorRefuses(AtLimit, YamlDocumentCode.TooLarge), Is.True, "the editor's boundary is the same");

            string FilePath = Path.Join(Root, "General.yaml");
            File.WriteAllText(FilePath, AtLimit);
            Assert.That(Yaml.GeneralConfigConvertFromYaml(FilePath).OutputTo, Is.EqualTo(OutputType.Console));
            File.WriteAllBytes(FilePath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(AtLimit)).ToArray());
            Assert.That(Limit(() => Yaml.GeneralConfigConvertFromYaml(FilePath)).Code, Is.EqualTo(ConfigCode.FileTooLarge));
            Assert.That(ConfigValidation.ValidateFile(FilePath, DateTime.Now).Single().Code, Is.EqualTo(ConfigCode.FileTooLarge));
        }

        [Test]
        public void AFileOverTheByteLimit_IsRefusedBeforeItIsRead()
        {
            string FilePath = Path.Join(Root, "General.yaml");
            using (var Huge = new FileStream(FilePath, FileMode.CreateNew))
            {
                Huge.SetLength(64L * 1024 * 1024);
            }
            long Before = GC.GetAllocatedBytesForCurrentThread();
            Exception? Refused = null;
            try
            {
                Yaml.ReadSettingsText(FilePath);
            }
            catch (Exception ex)
            {
                Refused = ex;
            }
            long Allocated = GC.GetAllocatedBytesForCurrentThread() - Before;
            Assert.That(Refused, Is.TypeOf<Yaml.SettingsLimitException>());
            Assert.That(Allocated, Is.LessThan(1_000_000), "the 64 MB file was not read");

            File.Delete(FilePath);
            File.WriteAllBytes(FilePath, Encoding.ASCII.GetBytes("outputTo: Console\n#").Concat(new byte[ConfigFileReplace.MaxBytes - 18]).ToArray());
            Assert.That(new FileInfo(FilePath).Length, Is.EqualTo(ConfigFileReplace.MaxBytes + 1));
            Assert.That(Limit(() => Yaml.GeneralConfigGetConfig(Root)).Code, Is.EqualTo(ConfigCode.FileTooLarge), "one byte over");
            Assert.That(ConfigValidation.ValidateFile(FilePath, DateTime.Now).Single().Code, Is.EqualTo(ConfigCode.FileTooLarge));
        }

        // ---- events, nesting, aliases ----

        private static string FlowList(int Items) => "includeEquipment: [" + string.Join(", ", Enumerable.Repeat("a", Items)) + "]\n";

        [Test]
        public void TheEventLimit_IsExact()
        {
            // stream, document and mapping start and end, the key, the list's start and end: 9 events besides the items
            int AtLimit = YamlDocument.MaxParserEvents - 9;
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(FlowList(AtLimit)).IncludeEquipment, Has.Count.EqualTo(AtLimit), "a large list within the limit loads");
            Yaml.SettingsLimitException Over = Limit(() => Yaml.LoadSettingsText<CampertronConfig>(FlowList(AtLimit + 1)));
            Assert.That(Over.Code, Is.EqualTo(ConfigCode.TooManyEvents));
            Assert.That(Over.Line, Is.GreaterThan(0), "the position of the first event over the limit");
            Assert.That(EditorRefuses(FlowList(AtLimit + 1), YamlDocumentCode.TooManyEvents) && !EditorRefuses(FlowList(AtLimit), YamlDocumentCode.TooManyEvents), Is.True);
            Assert.That(ConfigValidation.ValidateSearch(FlowList(AtLimit + 1), "Trip.yaml", DateTime.Now).Single().Code, Is.EqualTo(ConfigCode.TooManyEvents));
        }

        private static string Nested(int Lists) => "includeEquipment: " + new string('[', Lists) + new string(']', Lists) + "\n";

        [Test]
        public void TheNestingLimit_IsExact()
        {
            // the root mapping is level 1: MaxDepth - 1 lists reach the limit
            int AtLimit = YamlDocument.MaxDepth - 1;
            Assert.That(Yaml.ScanSettings(Nested(AtLimit)), Is.Not.Empty, "within the limit the loader's own type check decides");
            Assert.That(() => Yaml.LoadSettingsText<CampertronConfig>(Nested(AtLimit)), Throws.TypeOf<YamlDotNet.Core.YamlException>());
            Assert.That(Limit(() => Yaml.LoadSettingsText<CampertronConfig>(Nested(AtLimit + 1))).Code, Is.EqualTo(ConfigCode.TooDeep));
            Assert.That(EditorRefuses(Nested(AtLimit + 1), YamlDocumentCode.TooDeep) && !EditorRefuses(Nested(AtLimit), YamlDocumentCode.TooDeep), Is.True);
            Assert.That(Limit(() => Yaml.LoadSettingsText<CampertronConfig>(Nested(100_000))).Code, Is.EqualTo(ConfigCode.TooDeep), "deep nesting never reaches the recursive deserializer");
        }

        private static string Aliased(int Aliases) => "displayName: &n x\nincludeEquipment: [" + string.Join(", ", Enumerable.Repeat("*n", Aliases)) + "]\n";

        [Test]
        public void TheAliasLimit_IsExact()
        {
            int AtLimit = YamlDocument.MaxAliases;
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>(Aliased(AtLimit)).IncludeEquipment, Is.EqualTo(Enumerable.Repeat("x", AtLimit)));
            Assert.That(Limit(() => Yaml.LoadSettingsText<CampertronConfig>(Aliased(AtLimit + 1))).Code, Is.EqualTo(ConfigCode.TooManyAliases));
            Assert.That(EditorRefuses(Aliased(AtLimit + 1), YamlDocumentCode.TooManyAliases) && !EditorRefuses(Aliased(AtLimit), YamlDocumentCode.TooManyAliases), Is.True);
        }

        [Test]
        public void AnAliasExpansionAttack_IsRefused_QuicklyAndWithoutExpanding()
        {
            // "billion laughs": nine levels of ten references each (80 aliases, within the limit). An alias is a
            // reference to the object already read, never a copy: YamlDotNet reads an alias to a list, where a text is
            // expected, as the text "(Collection)", so nothing is expanded, and the validation says so
            string[] Keys = { "includeEquipment", "excludeEquipment", "includeSites", "excludeSites", "includeAttributes", "excludeAttributes", "includeCampsiteType", "excludeCampsiteType", "searchValueDates" };
            var Text = new StringBuilder("includeEquipment: &l0 [" + string.Join(", ", Enumerable.Repeat("lol", 10)) + "]\n");
            for (int Level = 1; Level < Keys.Length; Level++)
            {
                Text.Append(Keys[Level] + ": &l" + Level + " [" + string.Join(", ", Enumerable.Repeat("*l" + (Level - 1), 10)) + "]\n");
            }
            var Clock = Stopwatch.StartNew();
            long Before = GC.GetAllocatedBytesForCurrentThread();
            CampertronConfig Loaded = Yaml.LoadSettingsText<CampertronConfig>(Text.ToString());
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - Before, Is.LessThan(20_000_000), "nothing was expanded");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            Assert.That(Loaded.SearchValueDates, Is.EqualTo(Enumerable.Repeat("(Collection)", 10)), "ten texts, not 10^9 items");
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateSearch(Text.ToString(), "Trip.yaml", DateTime.Now);
            Assert.That(Found.Count(D => D.Code == ConfigCode.NotAScalar && D.Severity == ConfigSeverity.Warning), Is.EqualTo(80), "each alias to a list is reported");

            // with unknown keys and one alias more than allowed, the limit refuses it before anything is deserialized
            var Over = new StringBuilder("a0: &a0 [" + string.Join(", ", Enumerable.Repeat("lol", 10)) + "]\n");
            for (int Level = 1; Level <= 10; Level++)
            {
                Over.Append("a" + Level + ": &a" + Level + " [" + string.Join(", ", Enumerable.Repeat("*a" + (Level - 1), 10)) + "]\n");
            }
            Over.Append("a11: *a10\n");
            Assert.That(Limit(() => Yaml.LoadSettingsText<CampertronConfig>(Over.ToString())).Code, Is.EqualTo(ConfigCode.TooManyAliases));
            Assert.That(ConfigValidation.ValidateSearch(Over.ToString(), "Trip.yaml", DateTime.Now).Single().Code, Is.EqualTo(ConfigCode.TooManyAliases));
        }

        [Test]
        public void ALargeScalar_WithinTheLimit_Loads()
        {
            string Name = new string('x', 1_000_000);
            Assert.That(Yaml.LoadSettingsText<CampertronConfig>("displayName: " + Name + "\n").DisplayName, Is.EqualTo(Name));
        }
    }
}
