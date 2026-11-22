using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Ntfy.yaml and ntfy's access token (decision D67, business-rules §28): the file is a settings file, never a search file;
    // it is read only if it exists and is never created; its validation checks the form of every value (an absolute http or
    // https server address without anything else, ntfy's topic rule, tags, priority, timeout, the token file's path) and
    // never quotes a value; the token comes from tokenFile (the shared secret-file contract), then CAMPERTRON_NTFY_TOKEN,
    // then none, never falling back from a configured source. Unique canary topics and tokens are searched for in every
    // diagnostic and description. Temporary folders only; the environment is the resolver's seam.
    public class NtfyConfigTests
    {
        private string Root = string.Empty;
        private string Topic = string.Empty;
        private string Token = string.Empty;
        private string OtherToken = string.Empty;
        private Func<string, string?> OriginalEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronNtfyConfigTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Topic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            Token = "tk_cnry" + Guid.NewGuid().ToString("N");
            OtherToken = "tk_envcnry" + Guid.NewGuid().ToString("N");
            OriginalEnvironment = NtfyToken.GetEnvironment;
            Environment_.Clear();
            NtfyToken.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
        }

        [TearDown]
        public void TearDown()
        {
            NtfyToken.GetEnvironment = OriginalEnvironment;
            if (Directory.Exists(Root))
            {
                foreach (string Entry in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                Directory.Delete(Root, true);
            }
        }

        private string NtfyText(string? Extra = null, string Server = "https://ntfy.example.invalid", bool Enabled = true) =>
            "enabled: " + (Enabled ? "true" : "false") + "\nserver: " + Server + "\ntopic: " + Topic + "\n" + (Extra ?? "");

        private static NtfyConfig Load(string Text) => Yaml.LoadSettingsText<NtfyConfig>(Text);

        private static string Quoted(string FilePath) => "'" + FilePath.Replace("'", "''") + "'";

        private void AssertNoValue(string Shown)
        {
            Assert.That(Shown, Does.Not.Contain(Topic).And.Not.Contain(Token).And.Not.Contain(OtherToken).And.Not.Contain(Root));
            Assert.That(Shown, Does.Not.Contain("ntfy.example.invalid"));
        }

        private List<(ConfigCode, ConfigSeverity, string?)> Codes(string Text, bool Required = false)
        {
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateNtfy(Text, Required);
            foreach (ConfigDiagnostic D in Found)
            {
                AssertNoValue(D.ToString());
            }
            return Found.Select(D => (D.Code, D.Severity, D.Key)).ToList();
        }

        // ---- the file ----

        [TestCase("Ntfy.yaml")]
        [TestCase("NTFY.yaml")]
        [TestCase("ntfy.YAML")]
        public void NtfyYaml_IsASettingsFile_NeverASearchFile(string Name)
        {
            File.WriteAllText(Path.Join(Root, Name), NtfyText());
            File.WriteAllText(Path.Join(Root, "Trip.yaml"), "displayName: Trip\n");
            Assert.That(ConfigFiles.Classify(Name), Is.EqualTo(ConfigFileKind.Ntfy));
            Assert.That(ConfigFiles.SearchFiles(Root).Select(Path.GetFileName), Is.EqualTo(new[] { "Trip.yaml" }));
            Assert.That(SearchFileName.Check("Ntfy", out string FileName), Does.Contain("Ntfy.yaml"));
            Assert.That(FileName, Is.Empty);
        }

        [Test]
        public void TheLoader_ReadsTheFileIfItExists_AndNeverCreatesIt()
        {
            Assert.That(Yaml.NtfyConfigGetConfig(Root), Is.Null);
            Assert.That(Directory.GetFileSystemEntries(Root), Is.Empty, "nothing created");

            File.WriteAllText(Path.Join(Root, "Ntfy.yaml"), NtfyText("priority: high\ntags:\n- tent\n- zion\ntimeoutSeconds: 15\n"));
            NtfyConfig Ntfy = Yaml.NtfyConfigGetConfig(Root)!;
            Assert.That((Ntfy.Enabled, Ntfy.Server, Ntfy.Topic, Ntfy.Priority, Ntfy.TimeoutSeconds, Ntfy.TokenFileWritten),
                Is.EqualTo((true, "https://ntfy.example.invalid", Topic, NtfyPriority.High, 15, false)));
            Assert.That(Ntfy.EffectiveTags, Is.EqualTo(new[] { "tent", "zion" }));

            File.WriteAllText(Path.Join(Root, "Ntfy.yaml"), "# nothing yet\n");
            Assert.That(Yaml.NtfyConfigGetConfig(Root), Is.Null, "no settings: ntfy is off");
        }

        [Test]
        public void TheDefaults_AreDocumented()
        {
            NtfyConfig Ntfy = Load(NtfyText());
            Assert.That((Ntfy.Priority, Ntfy.TimeoutSeconds, Ntfy.Tags), Is.EqualTo((NtfyPriority.Default, 30, (List<string>?)null)));
            Assert.That(Ntfy.EffectiveTags, Is.EqualTo(new[] { "tent" }));
            Assert.That(Load(NtfyText("tags: []\n")).EffectiveTags, Is.Empty, "an empty list sends no tag");
            Assert.That(Load("server: https://ntfy.sh\n").Enabled, Is.False, "off unless enabled");
            Assert.That(Load(NtfyText("priority: 5\n")).Priority, Is.EqualTo(NtfyPriority.Max), "a number from 1 to 5 too");
        }

        [Test]
        public void AnUnknownKey_IsRefused_AndNotNamed()
        {
            string Text = NtfyText(Topic + ": x\n");
            Assert.That(() => Load(Text), Throws.InstanceOf<YamlDotNet.Core.YamlException>());
            ConfigDiagnostic D = ConfigValidation.ValidateNtfy(Text, false).Single(D_ => D_.Code == ConfigCode.UnknownKey);
            Assert.That(D.Key, Is.Null);
            AssertNoValue(D.ToString());
        }

        [Test]
        public void ALoaderFailure_IsReportedWithoutTheFilesValues()
        {
            string FilePath = Path.Join(Root, "Ntfy.yaml");
            File.WriteAllText(FilePath, NtfyText("priority: " + Topic + "\n"));
            Exception Failure = Assert.Catch(() => Yaml.NtfyConfigGetConfig(Root))!;
            List<string> Lines = ConsoleUi.DescribeFailure(Failure);
            Assert.That(Lines[0], Does.StartWith("Ntfy.yaml could not be read: "));
            Assert.That(Lines[1], Is.EqualTo("File: " + FilePath));
            Assert.That(Lines[2], Is.EqualTo("Fix the file, or delete it to turn ntfy off (with outputTo: Ntfy, choose another outputTo too)."));
        }

        // ---- the validation ----

        [Test]
        public void AUsableFile_HasNoProblem()
        {
            Assert.That(Codes(NtfyText()), Is.Empty);
            Assert.That(Codes(NtfyText(Server: "https://ntfy.sh")), Is.Empty);
            Assert.That(Codes(NtfyText(Server: "https://ntfy.example.invalid:8443/")), Is.Empty);
            Assert.That(Codes(NtfyText("priority: Max\ntags:\n- tent\n- warning\ntimeoutSeconds: 120\n")), Is.Empty);
        }

        [Test]
        public void Http_IsAllowedDeliberately_WithAWarning()
        {
            Assert.That(Codes(NtfyText(Server: "http://127.0.0.1:2586")), Is.EqualTo(new[] { (ConfigCode.NtfyServerNotHttps, ConfigSeverity.Warning, (string?)"server") }));
            Assert.That(NtfyEndpoint.TryServer("http://127.0.0.1:2586", out Uri? Root_), Is.True);
            Assert.That(Root_!.ToString(), Is.EqualTo("http://127.0.0.1:2586/"));
        }

        [TestCase("ntfy.example.invalid")]
        [TestCase("/ntfy")]
        [TestCase("ftp://ntfy.example.invalid")]
        [TestCase("file:///tmp/ntfy")]
        [TestCase("https://user:pw@ntfy.example.invalid")]
        [TestCase("https://user@ntfy.example.invalid")]
        [TestCase("https://ntfy.example.invalid/?topic=x")]
        [TestCase("https://ntfy.example.invalid/#x")]
        [TestCase("https://ntfy.example.invalid/sometopic")]
        [TestCase("https://ntfy.example.invalid/sub/path/")]
        [TestCase("https://ntfy.example.invalid/%2e%2e/")]
        [TestCase("'https://ntfy.example.invalid '")]
        [TestCase("'https://ntfy.example.invalid\\t'")]
        public void AServerThatIsNotJustAnAddress_IsRefused(string Server)
        {
            foreach (bool Enabled in new[] { true, false })
            {
                Assert.That(Codes(NtfyText(Server: Server, Enabled: Enabled)).Where(C => C.Item1 != ConfigCode.NtfyDisabled),
                    Is.EqualTo(new[] { (ConfigCode.NtfyServerInvalid, Enabled ? ConfigSeverity.Error : ConfigSeverity.Warning, (string?)"server") }), Server);
            }
        }

        [TestCase("")]
        [TestCase("~")]
        [TestCase("'  '")]
        public void AMissingServerOrTopic_IsAnErrorWhileEnabled(string Value)
        {
            Assert.That(Codes("enabled: true\nserver: " + Value + "\ntopic: " + Topic + "\n"), Is.EqualTo(new[] { (ConfigCode.NtfyServerMissing, ConfigSeverity.Error, (string?)"server") }));
            Assert.That(Codes("enabled: true\nserver: https://ntfy.sh\ntopic: " + Value + "\n"), Is.EqualTo(new[] { (ConfigCode.NtfyTopicMissing, ConfigSeverity.Error, (string?)"topic") }));
        }

        [TestCase("with space")]
        [TestCase("a/b")]
        [TestCase("..")]
        [TestCase("a?b=c")]
        [TestCase("a#b")]
        [TestCase("a%2Fb")]
        [TestCase("tést")]
        [TestCase("a.b")]
        public void ATopicOutsideNtfysRule_IsRefused(string Bad)
        {
            string Text = "enabled: true\nserver: https://ntfy.sh\ntopic: '" + Bad + "'\n";
            Assert.That(ConfigValidation.ValidateNtfy(Text, false).Select(D => (D.Code, D.Severity)), Is.EqualTo(new[] { (ConfigCode.NtfyTopicInvalid, ConfigSeverity.Error) }));
            Assert.That(NtfyEndpoint.IsValidTopic(new string('a', 64)), Is.True);
            Assert.That(NtfyEndpoint.IsValidTopic(new string('a', 65)), Is.False);
        }

        [Test]
        public void TagsPriorityAndTimeout_FollowTheirRules()
        {
            Assert.That(Codes(NtfyText("tags:\n- ok\n- 'two words'\n- 'a,b'\n- ''\n")), Is.EqualTo(new[]
            {
                (ConfigCode.NtfyTagInvalid, ConfigSeverity.Error, (string?)"tags"), (ConfigCode.NtfyTagInvalid, ConfigSeverity.Error, (string?)"tags"), (ConfigCode.NtfyTagInvalid, ConfigSeverity.Error, (string?)"tags")
            }));
            string Many = "tags:\n" + string.Concat(Enumerable.Range(0, 9).Select(i => "- " + new string((char)('a' + i), 60) + "\n"));
            Assert.That(Codes(NtfyText(Many)), Is.EqualTo(new[] { (ConfigCode.NtfyTagsTooLong, ConfigSeverity.Error, (string?)"tags") }));
            Assert.That(Codes(NtfyText("priority: 7\n")), Is.EqualTo(new[] { (ConfigCode.InvalidChoice, ConfigSeverity.Error, (string?)"priority") }));
            Assert.That(Codes(NtfyText("priority: loud\n")).Select(C => C.Item1), Is.EqualTo(new[] { ConfigCode.InvalidChoice }), "the loader refuses an unknown name");
            foreach (int Seconds in new[] { 0, -1, 121, 100000 })
            {
                Assert.That(Codes(NtfyText("timeoutSeconds: " + Seconds + "\n")), Is.EqualTo(new[] { (ConfigCode.NtfyTimeoutOutOfRange, ConfigSeverity.Error, (string?)"timeoutSeconds") }));
            }
        }

        [Test]
        public void ADisabledFile_IsAFact_AndItsProblemsAreWarnings_UnlessOutputToNtfyRequiresIt()
        {
            string Text = "enabled: false\nserver: nonsense\n";
            Assert.That(Codes(Text), Is.EqualTo(new[]
            {
                (ConfigCode.NtfyDisabled, ConfigSeverity.Info, (string?)"enabled"), (ConfigCode.NtfyServerInvalid, ConfigSeverity.Warning, (string?)"server"), (ConfigCode.NtfyTopicMissing, ConfigSeverity.Warning, (string?)"topic")
            }));
            Assert.That(Codes(Text, Required: true), Is.EqualTo(new[]
            {
                (ConfigCode.NtfyDisabled, ConfigSeverity.Error, (string?)"enabled"), (ConfigCode.NtfyServerInvalid, ConfigSeverity.Error, (string?)"server"), (ConfigCode.NtfyTopicMissing, ConfigSeverity.Error, (string?)"topic")
            }));
            Assert.That(Codes("# nothing\n", Required: true), Is.EqualTo(new[] { (ConfigCode.Empty, ConfigSeverity.Error, (string?)null) }));
            Assert.That(Codes("# nothing\n"), Is.EqualTo(new[] { (ConfigCode.Empty, ConfigSeverity.Warning, (string?)null) }));
        }

        [Test]
        public void TheTokenFilesForm_IsChecked_AndTheFileIsNeverOpenedByTheValidation()
        {
            string Missing = Path.Join(Root, "not-there");
            Assert.That(Codes(NtfyText("tokenFile: " + Quoted(Missing) + "\n")), Is.EqualTo(new[] { (ConfigCode.NtfyTokenFileSet, ConfigSeverity.Info, (string?)"tokenFile") }));
            Assert.That(Codes(NtfyText("tokenFile:\n")), Is.EqualTo(new[] { (ConfigCode.NtfyTokenFilePathEmpty, ConfigSeverity.Error, (string?)"tokenFile") }));
            Assert.That(Codes(NtfyText("tokenFile: secrets/token\n")), Is.EqualTo(new[] { (ConfigCode.NtfyTokenFilePathNotAbsolute, ConfigSeverity.Error, (string?)"tokenFile") }));
            int Asked = 0;
            NtfyToken.GetEnvironment = Name => { Asked++; return OtherToken; };
            Codes(NtfyText());
            Assert.That(Asked, Is.EqualTo(0), "the validation never reads CAMPERTRON_NTFY_TOKEN");
            System.Reflection.MethodInfo Check = typeof(ConfigValidation).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .Single(M => M.Name == "CheckNtfy" && M.GetParameters().Length == 4);
            List<string> Called = YamlDocumentPreservationTests.ReferencedMembers(Check).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.NtfyEndpoint::TryServer"), "the scan saw the method");
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Base.SecretFile::", StringComparison.Ordinal) ||
                C.StartsWith("CampertronLibrary.function.Base.NtfyToken::Resolve", StringComparison.Ordinal) || C.StartsWith("System.IO.File", StringComparison.Ordinal)), Is.Empty);
        }

        [Test]
        public void TheFolderValidation_IncludesNtfyYaml_AndOutputToNtfyNeedsIt()
        {
            File.WriteAllText(Path.Join(Root, "General.yaml"), "outputTo: Ntfy\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Root, "Email.yaml"), "smtpServer: ''\nsmtpPort: 0\n");
            DateTime Now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
            List<ConfigDiagnostic> Found = ConfigValidation.ValidateFolder(Root, Now);
            Assert.That(Found.Where(D => D.FileKind == ConfigFileKind.Ntfy).Select(D => (D.Code, D.Severity)), Is.EqualTo(new[] { (ConfigCode.FileMissing, ConfigSeverity.Error) }));
            Assert.That(Found.Where(D => D.FileKind == ConfigFileKind.Email).All(D => D.Severity != ConfigSeverity.Error), Is.True, "outputTo: Ntfy needs no Email.yaml");
            File.WriteAllText(Path.Join(Root, "Ntfy.yaml"), NtfyText(Enabled: false));
            Assert.That(ConfigValidation.ValidateFolder(Root, Now).Where(D => D.FileKind == ConfigFileKind.Ntfy).Select(D => (D.Code, D.Severity)),
                Is.EqualTo(new[] { (ConfigCode.NtfyDisabled, ConfigSeverity.Error) }));
            File.WriteAllText(Path.Join(Root, "General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            File.Delete(Path.Join(Root, "Ntfy.yaml"));
            Assert.That(ConfigValidation.ValidateFolder(Root, Now).Where(D => D.FileKind == ConfigFileKind.Ntfy), Is.Empty, "without Ntfy.yaml ntfy is off: nothing to say");
        }

        [Test]
        public void TheStartsCheck_RefusesWhatNtfyCantUse_NamingNoValue()
        {
            Assert.That(() => ConfigValidation.ThrowIfNtfyOutputRefused(Load(NtfyText()), true), Throws.Nothing);
            Assert.That(() => ConfigValidation.ThrowIfNtfyOutputRefused(Load(NtfyText(Enabled: false)), false), Throws.Nothing, "off and not required");
            ConfigValidationException Refused = Assert.Throws<ConfigValidationException>(() => ConfigValidation.ThrowIfNtfyOutputRefused(null, true))!;
            Assert.That(Refused.Message, Does.StartWith("the ntfy output can't start: "));
            Refused = Assert.Throws<ConfigValidationException>(() => ConfigValidation.ThrowIfNtfyOutputRefused(Load(NtfyText("timeoutSeconds: 0\n", Server: "https://x.invalid/" + Topic)), false))!;
            Assert.That(Refused.Diagnostics.Select(D => D.Code), Is.EqualTo(new[] { ConfigCode.NtfyServerInvalid, ConfigCode.NtfyTimeoutOutOfRange }));
            AssertNoValue(Refused.Message);
        }

        // ---- the token ----

        private string WriteToken(string Text, string Name = "ntfy_token")
        {
            string FilePath = Path.Join(Root, Name);
            File.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes(Text));
            return FilePath;
        }

        [Test]
        public void TheTokenComesFromTheFile_ThenTheVariable_ThenNone()
        {
            NtfyConfig None = Load(NtfyText());
            Assert.That((NtfyToken.SelectedSource(None), NtfyToken.Resolve(None).Token, NtfyToken.Resolve(None).Succeeded), Is.EqualTo((NtfyTokenSource.None, (string?)null, true)));
            Environment_[NtfyToken.EnvironmentVariable] = OtherToken;
            Assert.That(NtfyToken.Resolve(Load(NtfyText())).Token, Is.EqualTo(OtherToken));
            NtfyConfig WithFile = Load(NtfyText("tokenFile: " + Quoted(WriteToken(Token + "\n")) + "\n"));
            NtfyTokenResult Result = NtfyToken.Resolve(WithFile);
            Assert.That((Result.Source, Result.Token), Is.EqualTo((NtfyTokenSource.File, Token)));
            Assert.That(Result.ToString(), Is.EqualTo("NtfyToken File: found"));
        }

        [TestCase("missing", "FileMissing")]
        [TestCase("empty", "FileEmpty")]
        [TestCase("only a line break", "FileOnlyLineBreak")]
        [TestCase("two lines", "FileHasLineBreak")]
        [TestCase("a space", "FileInvalidCharacters")]
        [TestCase("non-ASCII", "FileInvalidCharacters")]
        [TestCase("a byte-order mark", "FileHasByteOrderMark")]
        [TestCase("relative", "FilePathNotAbsolute")]
        [TestCase("blank", "FilePathEmpty")]
        public void ATokenFileThatCantBeUsed_NeverFallsBack_ToTheVariableOrNoToken(string Kind, string ProblemName)
        {
            string Line = Kind switch
            {
                "missing" => "tokenFile: " + Quoted(Path.Join(Root, "absent")),
                "empty" => "tokenFile: " + Quoted(WriteToken("")),
                "only a line break" => "tokenFile: " + Quoted(WriteToken("\r\n")),
                "two lines" => "tokenFile: " + Quoted(WriteToken(Token + "\n" + Token + "\n")),
                "a space" => "tokenFile: " + Quoted(WriteToken(Token + " x")),
                "non-ASCII" => "tokenFile: " + Quoted(WriteToken(Token + "é")),
                "a byte-order mark" => "tokenFile: " + Quoted(WriteToken("﻿" + Token)),
                "relative" => "tokenFile: ntfy_token",
                _ => "tokenFile: ''"
            };
            Environment_[NtfyToken.EnvironmentVariable] = OtherToken;
            NtfyConfig Ntfy = Load(NtfyText(Line + "\n"));

            NtfyTokenResult Result = NtfyToken.Resolve(Ntfy);

            Assert.That((Result.Source, Result.Problem, Result.Token), Is.EqualTo((NtfyTokenSource.File, (NtfyTokenProblem?)Enum.Parse<NtfyTokenProblem>(ProblemName), (string?)null)));
            Assert.That(() => NtfyToken.ForPublishing(Ntfy), Throws.TypeOf<NtfyTokenException>());
            Assert.That(Ntfy.ResolvedToken, Is.Null);
            AssertNoValue(string.Join("\n", NtfyToken.Describe(Result)) + Result);
        }

        [TestCase("", "EnvironmentEmpty")]
        [TestCase(" lead", "EnvironmentInvalidCharacters")]
        [TestCase("with\nline", "EnvironmentInvalidCharacters")]
        [TestCase("tab\there", "EnvironmentInvalidCharacters")]
        public void AVariableThatCantBeUsed_NeverFallsBack_ToNoToken(string Value, string ProblemName)
        {
            Environment_[NtfyToken.EnvironmentVariable] = Value.Replace("lead", OtherToken);
            NtfyTokenResult Result = NtfyToken.Resolve(Load(NtfyText()));
            Assert.That((Result.Source, Result.Problem), Is.EqualTo((NtfyTokenSource.Environment, (NtfyTokenProblem?)Enum.Parse<NtfyTokenProblem>(ProblemName))));
            AssertNoValue(string.Join("\n", NtfyToken.Describe(Result)));
        }

        [Test]
        public void TheToken_IsReadOnce_AndTheSecretsAReportHidesAreTheTokenAndTheTopic()
        {
            string FilePath = WriteToken(Token);
            NtfyConfig Ntfy = Load(NtfyText("tokenFile: " + Quoted(FilePath) + "\n"));
            Assert.That(NtfyToken.KnownSecrets(Ntfy), Is.EqualTo(new[] { Topic }), "the topic, before the token is read");
            Assert.That(NtfyToken.ForPublishing(Ntfy), Is.EqualTo(Token));
            File.WriteAllText(FilePath, OtherToken);
            Assert.That(NtfyToken.ForPublishing(Ntfy), Is.EqualTo(Token), "a changed file needs a restart");
            Assert.That(NtfyToken.KnownSecrets(Ntfy), Is.EqualTo(new[] { Token, Topic }));
            Assert.That(File.ReadAllText(FilePath), Is.EqualTo(OtherToken), "the file is never written");
        }

        [Test]
        public void EveryTokenProblem_HasItsOwnWords_WithoutAValue()
        {
            var Seen = new HashSet<string>();
            foreach (NtfyTokenProblem Problem in Enum.GetValues<NtfyTokenProblem>())
            {
                NtfyTokenSource Source = Problem.ToString().StartsWith("Environment", StringComparison.Ordinal) ? NtfyTokenSource.Environment : NtfyTokenSource.File;
                List<string> Lines = NtfyToken.Describe(NtfyTokenResult.Failed(Source, Problem));
                Assert.That(Lines, Has.Count.EqualTo(3));
                Assert.That(Lines[0], Does.StartWith("the ntfy access token can't be used: "));
                Assert.That(Lines[1], Is.Not.Empty);
                Assert.That(Lines[2], Is.EqualTo("Campertron never publishes without the token, or with another token source, when the one configured can't be used (the order is tokenFile, then CAMPERTRON_NTFY_TOKEN, then none)."));
                Assert.That(Seen.Add(Lines[0]), Is.True, Problem.ToString());
            }
        }
    }
}
