using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using YamlMappingNode = YamlDotNet.RepresentationModel.YamlMappingNode;
using YamlNode = YamlDotNet.RepresentationModel.YamlNode;
using YamlScalarNode = YamlDotNet.RepresentationModel.YamlScalarNode;
using YamlSequenceNode = YamlDotNet.RepresentationModel.YamlSequenceNode;
using YamlStream = YamlDotNet.RepresentationModel.YamlStream;

namespace CampertronUnitTests.function.Base
{
    // The documented settings examples (the v0.2.0 release readiness, D68): examples/notifications holds one settings folder
    // for each way to be notified (email with the SMTP password as plain text, in a file or in CAMPERTRON_EMAIL_PASSWORD;
    // ntfy without and with an access token; email and ntfy together) and a Docker Compose file with read-only secrets, and
    // README.md shows Email.yaml, Ntfy.yaml and General.yaml. Each is read as Campertron reads it: the bounded loader, the
    // editor (YamlDocument) and the validation of the whole folder with the example search, as the console uses them.
    // The placeholder topic campertron-<random> is refused until it is replaced, which these tests pin, so an example copied
    // as it is never publishes to a topic everyone can read. The examples hold placeholders only (example.com, example.net,
    // ntfy.sh). Read-only on the repository; temporary folders only; no secret file or environment variable is read.
    public class DocumentedExamplesTests
    {
        private const string PlaceholderTopic = "campertron-<random>";
        private static readonly DateTime Now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Local);
        private static readonly string[] ExampleFolders =
            { "email", "email-password-file", "email-password-variable", "ntfy", "ntfy-token-file", "email-and-ntfy" };
        private string Root = string.Empty;
        private string Topic = string.Empty;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronDocumentedExamplesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Topic = "exampleTopic" + Guid.NewGuid().ToString("N").Substring(0, 24);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(Root, true);
        }

        private static string RepositoryRoot()
        {
            for (DirectoryInfo? At = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); At != null; At = At.Parent)
            {
                if (File.Exists(Path.Join(At.FullName, "Campertron.sln")) && Directory.Exists(Path.Join(At.FullName, "examples")))
                {
                    return At.FullName;
                }
            }
            Assert.Fail("the repository (Campertron.sln and examples/) was not found above the test folder");
            return string.Empty;
        }

        private static string Examples(params string[] Parts) => Path.Join(new[] { RepositoryRoot(), "examples", "notifications" }.Concat(Parts).ToArray());

        // the paths in the examples are container (Linux) paths, which Windows doesn't count as fully qualified
        private static void LinuxPathsOnly()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("the examples' secret paths are container paths (/run/secrets/...), not Windows paths");
            }
        }

        // one example folder and the Zion example search, in a temporary settings folder; the placeholder topic replaced
        // by a synthetic one when Replace is true
        private string SettingsFolder(string Example, bool Replace)
        {
            string Folder = Path.Join(Root, Example);
            Directory.CreateDirectory(Folder);
            foreach (string File_ in Directory.GetFiles(Examples(Example), "*.yaml"))
            {
                string Text = File.ReadAllText(File_);
                File.WriteAllText(Path.Join(Folder, Path.GetFileName(File_)), Replace ? Text.Replace(PlaceholderTopic, Topic) : Text);
            }
            File.Copy(Path.Join(RepositoryRoot(), "examples", "0.1", "ZionConfig.yaml"), Path.Join(Folder, "ZionConfig.yaml"));
            return Folder;
        }

        private static List<string> Errors(IEnumerable<ConfigDiagnostic> Found) =>
            Found.Where(D => D.Severity == ConfigSeverity.Error).Select(D => D.ToString()).ToList();

        // ---- the folders ----

        [Test]
        public void TheExampleFolders_AreTheDocumentedOnes_EachASettingsFolderTheLoaderAndTheEditorRead()
        {
            Assert.That(Directory.GetDirectories(Examples()).Select(Path.GetFileName).Order(StringComparer.Ordinal),
                Is.EqualTo(ExampleFolders.Append("docker").Order(StringComparer.Ordinal)));
            string Index = File.ReadAllText(Examples("README.md"));
            foreach (string Example in ExampleFolders.Append("docker"))
            {
                Assert.That(Index, Does.Contain("(" + Example + ")"), Example + " is linked from examples/notifications/README.md");
            }
            foreach (string Example in ExampleFolders)
            {
                string[] Files = Directory.GetFiles(Examples(Example)).Select(F => Path.GetFileName(F)!).Order(StringComparer.Ordinal).ToArray();
                Assert.That(Files, Does.Contain("General.yaml"), Example);
                Assert.That(Files.All(F => F is "General.yaml" or "Email.yaml" or "Ntfy.yaml"), Is.True, Example + ": " + string.Join(", ", Files));
                foreach (string File_ in Files)
                {
                    string Text = File.ReadAllText(Examples(Example, File_));
                    Assert.That(YamlDocument.Analyze(Text).Diagnostics, Is.Empty, Example + "/" + File_ + " is refused by the editor");
                    Assert.That(Text, Does.Not.Contain("\r"), Example + "/" + File_);
                    object Loaded = ConfigFiles.Classify(File_) switch
                    {
                        ConfigFileKind.General => Yaml.LoadSettingsText<GeneralConfig>(Text),
                        ConfigFileKind.Email => Yaml.LoadSettingsText<EmailConfig>(Text),
                        ConfigFileKind.Ntfy => Yaml.LoadSettingsText<NtfyConfig>(Text),
                        ConfigFileKind Other => throw new AssertionException(Example + "/" + File_ + " is classified as " + Other),
                    };
                    Assert.That(Loaded, Is.Not.Null, Example + "/" + File_);
                }
            }
        }

        //Codes: what the folder's Email.yaml and Ntfy.yaml say (ConfigCode names; the enum is internal). The ntfy folders have
        //no Email.yaml: every start creates an empty one, which ntfy mode doesn't use (FileMissing, a fact)
        [TestCase("email", OutputType.Email, "")]
        [TestCase("email-password-file", OutputType.Email, "PasswordFileSet")]
        [TestCase("email-password-variable", OutputType.Email, "CredentialsMissing")]
        [TestCase("ntfy", OutputType.Ntfy, "FileMissing")]
        [TestCase("ntfy-token-file", OutputType.Ntfy, "FileMissing NtfyTokenFileSet")]
        [TestCase("email-and-ntfy", OutputType.Email, "PasswordFileSet NtfyTokenFileSet")]
        public void EachExample_WithItsTopicChosen_ValidatesAsTheConsoleUsesIt(string Example, OutputType Output, string Codes)
        {
            ConfigCode[] Expected = Codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<ConfigCode>).ToArray();
            LinuxPathsOnly();
            string Folder = SettingsFolder(Example, Replace: true);
            Assert.That(Yaml.LoadSettingsText<GeneralConfig>(File.ReadAllText(Path.Join(Folder, "General.yaml"))).OutputTo, Is.EqualTo(Output));
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateFolder(Folder, Now);
            Assert.That(Errors(Found), Is.Empty);
            //what the folder says beyond its search and General.yaml: exactly the facts and the one expected warning
            Assert.That(Found.Where(D => D.FileKind is ConfigFileKind.Email or ConfigFileKind.Ntfy).Select(D => D.Code).Order(),
                Is.EqualTo(Expected.Order()), string.Join("\n", Found));
            Assert.That(Found.Where(D => D.FileKind is ConfigFileKind.General or ConfigFileKind.Search && D.Severity != ConfigSeverity.Info), Is.Empty,
                string.Join("\n", Found));
            if (Output == OutputType.Ntfy)
            {
                Assert.That(File.Exists(Path.Join(Folder, "Email.yaml")), Is.False, "ntfy mode doesn't use Email.yaml");
            }
            Assert.DoesNotThrow(() => ConfigValidation.ThrowIfNtfyOutputRefused(
                Yaml.LoadSettingsText<NtfyConfig>(File.Exists(Path.Join(Folder, "Ntfy.yaml")) ? File.ReadAllText(Path.Join(Folder, "Ntfy.yaml")) : "enabled: false\n"),
                Output == OutputType.Ntfy));
        }

        [TestCase("ntfy")]
        [TestCase("ntfy-token-file")]
        [TestCase("email-and-ntfy")]
        public void ThePlaceholderTopic_IsRefusedUntilItIsReplaced(string Example)
        {
            LinuxPathsOnly();
            string Folder = SettingsFolder(Example, Replace: false);
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateFolder(Folder, Now);
            Assert.That(Found.Where(D => D.Severity == ConfigSeverity.Error).Select(D => (D.Code, D.FileName, D.Key)),
                Is.EqualTo(new[] { (ConfigCode.NtfyTopicInvalid, "Ntfy.yaml", (string?)"topic") }), string.Join("\n", Found));
            Assert.That(Found.Single(D => D.Severity == ConfigSeverity.Error).Message, Does.Not.Contain("<random>"));
            var Refused = Assert.Throws<ConfigValidationException>(() => ConfigValidation.ThrowIfNtfyOutputRefused(
                Yaml.LoadSettingsText<NtfyConfig>(File.ReadAllText(Path.Join(Folder, "Ntfy.yaml"))), Required: true))!;
            Assert.That(Refused.Diagnostics.Select(D => D.Code), Is.EqualTo(new[] { ConfigCode.NtfyTopicInvalid }));
        }

        // ---- README.md ----

        private static List<string> ReadmeYamlBlocks()
        {
            string Readme = File.ReadAllText(Path.Join(RepositoryRoot(), "README.md")).Replace("\r\n", "\n");
            return Regex.Matches(Readme, "^( *)```yaml\n(.*?)^\\1```$", RegexOptions.Multiline | RegexOptions.Singleline)
                .Select(M => string.Join("\n", M.Groups[2].Value.Split('\n').Select(L => L.StartsWith(M.Groups[1].Value) ? L.Substring(M.Groups[1].Value.Length) : L)))
                .ToList();
        }

        [Test]
        public void TheReadmeSettingsExamples_ValidateAsWritten_ApartFromThePlaceholderTopic()
        {
            LinuxPathsOnly();
            List<string> Blocks = ReadmeYamlBlocks();
            List<string> Email = Blocks.Where(B => B.Contains("smtpServer:")).ToList();
            List<string> Ntfy = Blocks.Where(B => B.Contains("topic:")).ToList();
            List<string> General = Blocks.Where(B => B.Contains("outputTo:")).ToList();
            Assert.That((Email.Count, Ntfy.Count, General.Count), Is.EqualTo((2, 1, 1)), "README.md's Email.yaml, Ntfy.yaml and General.yaml examples");
            Assert.That(Email.Count(B => B.Contains("smtpPasswordFile:")), Is.EqualTo(1));
            foreach (string Block in Email)
            {
                Assert.That(Errors(ConfigValidation.ValidateEmail(Block, EmailOutput: true)), Is.Empty, Block);
                Assert.That(YamlDocument.Analyze(Block).Diagnostics, Is.Empty, Block);
            }
            foreach (string Block in General)
            {
                Assert.That(Errors(ConfigValidation.ValidateGeneral(Block, Now)), Is.Empty, Block);
            }
            string NtfyBlock = Ntfy.Single();
            Assert.That(NtfyBlock, Does.Contain("topic: " + PlaceholderTopic));
            Assert.That(ConfigValidation.ValidateNtfy(NtfyBlock, Required: true).Where(D => D.Severity == ConfigSeverity.Error).Select(D => D.Code),
                Is.EqualTo(new[] { ConfigCode.NtfyTopicInvalid }));
            Assert.That(Errors(ConfigValidation.ValidateNtfy(NtfyBlock.Replace(PlaceholderTopic, Topic), Required: true)), Is.Empty);
        }

        // ---- Docker Compose and the secret paths ----

        private static YamlMappingNode Mapping(YamlNode Node, string Key) => (YamlMappingNode)((YamlMappingNode)Node).Children[new YamlScalarNode(Key)];

        [Test]
        public void TheComposeExample_MountsTheSecretsAtThePathsTheExampleSettingsName()
        {
            var Stream = new YamlStream();
            using (var Reader = new StreamReader(Examples("docker", "compose.yaml")))
            {
                Stream.Load(Reader);
            }
            YamlMappingNode Document = (YamlMappingNode)Stream.Documents.Single().RootNode;
            YamlMappingNode Service = Mapping(Mapping(Document, "services"), "campertron");
            string Image = ((YamlScalarNode)Service.Children[new YamlScalarNode("image")]).Value!;
            Assert.That(Image, Is.EqualTo("ghcr.io/shaunanthonyhathaway/campertronconsole:<version>"));
            string[] Volumes = ((YamlSequenceNode)Service.Children[new YamlScalarNode("volumes")]).Select(V => ((YamlScalarNode)V).Value!).ToArray();
            Assert.That(Volumes.Select(V => V.Substring(V.LastIndexOf(':'))), Is.EqualTo(new[] { ":/config", ":/cache" }));
            string[] Secrets = ((YamlSequenceNode)Service.Children[new YamlScalarNode("secrets")]).Select(V => ((YamlScalarNode)V).Value!).ToArray();
            Assert.That(Secrets, Is.EqualTo(new[] { "smtp_password", "ntfy_token" }));
            YamlMappingNode Declared = Mapping(Document, "secrets");
            foreach (string Secret in Secrets)
            {
                Assert.That(((YamlScalarNode)Mapping(Declared, Secret).Children[new YamlScalarNode("file")]).Value, Does.EndWith("/" + Secret));
            }
            //Compose mounts a secret at /run/secrets/<name>: the paths the combined example's settings name
            EmailConfig Email = Yaml.LoadSettingsText<EmailConfig>(File.ReadAllText(Examples("email-and-ntfy", "Email.yaml")));
            NtfyConfig Ntfy = Yaml.LoadSettingsText<NtfyConfig>(File.ReadAllText(Examples("email-and-ntfy", "Ntfy.yaml")));
            Assert.That((Email.SmtpPasswordFile, Ntfy.TokenFile), Is.EqualTo(("/run/secrets/" + Secrets[0], "/run/secrets/" + Secrets[1])));
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(File.ReadAllText(Examples("email-password-file", "Email.yaml"))).SmtpPasswordFile, Is.EqualTo(Email.SmtpPasswordFile));
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(File.ReadAllText(Examples("ntfy-token-file", "Ntfy.yaml"))).TokenFile, Is.EqualTo(Ntfy.TokenFile));
        }

        // ---- what the documents name ----

        private static IEnumerable<string> Documents()
        {
            string Repository = RepositoryRoot();
            yield return Path.Join(Repository, "README.md");
            yield return Path.Join(Repository, "examples", "0.1", "README.md");
            foreach (string File_ in Directory.GetFiles(Examples(), "*", SearchOption.AllDirectories))
            {
                yield return File_;
            }
            foreach (string Notes in Directory.GetFiles(Path.Join(Repository, ".github", "release-notes"), "v*.md"))
            {
                yield return Notes;
            }
        }

        [Test]
        public void TheDocumentedEnvironmentVariables_AreTheOnesCampertronReads()
        {
            var Known = new[] { SmtpPassword.EnvironmentVariable, NtfyToken.EnvironmentVariable };
            var Named = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string Document in Documents())
            {
                foreach (Match M in Regex.Matches(File.ReadAllText(Document), "CAMPERTRON_[A-Z0-9_]+"))
                {
                    Named.TryAdd(M.Value, new HashSet<string>());
                    Named[M.Value].Add(Path.GetRelativePath(RepositoryRoot(), Document));
                }
            }
            Assert.That(Named.Keys.Except(Known), Is.Empty, string.Join("\n", Named.Select(N => N.Key + ": " + string.Join(", ", N.Value))));
            foreach (string Variable in Known)
            {
                Assert.That(Named.ContainsKey(Variable) && Named[Variable].Contains("README.md") && Named[Variable].Any(D => D.StartsWith("examples")), Is.True,
                    Variable + " is documented in README.md and the examples");
            }
        }

        [Test]
        public void TheExamples_HoldPlaceholdersOnly()
        {
            foreach (string Example in ExampleFolders)
            {
                foreach (string File_ in Directory.GetFiles(Examples(Example), "*.yaml"))
                {
                    string Text = File.ReadAllText(File_);
                    switch (ConfigFiles.Classify(Path.GetFileName(File_)))
                    {
                        case ConfigFileKind.Email:
                            EmailConfig Email = Yaml.LoadSettingsText<EmailConfig>(Text);
                            Assert.That(Email.SmtpServer, Is.EqualTo("smtp.example.com"), File_);
                            Assert.That(new[] { Email.SmtpUsername, Email.SendFromAddress }.Concat(Email.SendToAddressList ?? new List<string>()),
                                Is.All.EqualTo("you@example.com"), File_);
                            Assert.That(Email.SmtpPassword, Is.EqualTo("").Or.EqualTo("your-app-password"), File_);
                            break;
                        case ConfigFileKind.Ntfy:
                            NtfyConfig Ntfy = Yaml.LoadSettingsText<NtfyConfig>(Text);
                            Assert.That(Ntfy.Server, Is.EqualTo("https://ntfy.sh").Or.EqualTo("https://ntfy.example.net"), File_);
                            Assert.That(Ntfy.Topic, Is.EqualTo(PlaceholderTopic), File_);
                            break;
                    }
                }
            }
        }
    }
}
