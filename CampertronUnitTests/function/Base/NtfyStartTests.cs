using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // The unattended monitoring's start with ntfy (decision D67, business-rules §28): outputTo: Email (email, plus ntfy when
    // Ntfy.yaml enables it) or outputTo: Ntfy (ntfy only, no Email.yaml needed). Every enabled provider must be usable
    // before anything is searched: Email.yaml's and Ntfy.yaml's checks, then the start line, the SMTP password and ntfy's
    // token, then the search files and D64's gate. outputTo: Ntfy without ntfy enabled has no provider and stops; an
    // enabled provider that can't be used is never ignored; nothing is sent to test a provider. Interactive starts never
    // read Ntfy.yaml. Temporary folders only; months answered through the console's download seam (no network), the send
    // and the publish replaced by recorders, the wait stopping the loop, the environment the resolvers' seams. Canary
    // topics and tokens are searched for in everything written.
    public class NtfyStartTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private string Topic = string.Empty;
        private string Token = string.Empty;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private CultureInfo OriginalCulture = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private Action<TimeSpan> OriginalWait = null!;
        private Func<string, string> OriginalDownload = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);
        private readonly List<string> Requested = new();
        private int Sends;
        private int Publishes;
        private int Waits;

        private sealed class StopTheLoop : Exception
        {
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronNtfyStartTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            Topic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            Token = "tk_cnry" + Guid.NewGuid().ToString("N");
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalWait = Load.CheckWait;
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            Environment_.Clear();
            Requested.Clear();
            Sends = Publishes = Waits = 0;
            NtfyToken.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
            SmtpPassword.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sends++; Message.Dispose(); };
            Notifications.PublishNtfy = (Settings, Message) => Publishes++;
            Load.CheckWait = Wait => { Waits++; throw new StopTheLoop(); };
            AvailabilityApi.ConsoleDownload = Url => { lock (Requested) { Requested.Add(Url); } return NoLiveNetwork.EmptyMonth; };
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
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            Load.CheckWait = OriginalWait;
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private const string ValidEmail = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: plain\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        private string NtfyText(bool Enabled = true, string Extra = "") => "enabled: " + (Enabled ? "true" : "false") + "\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n" + Extra;

        private string WriteFile(string Name, string Text)
        {
            string FilePath = Path.Join(ConfigPath, Name);
            File.WriteAllText(FilePath, Text);
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return FilePath;
        }

        private void UsableSearch()
        {
            File.WriteAllText(Path.Join(CachePath, "232445-CampInfo.json"), JsonSerializer.Serialize(new ReturnParkCampground { ParkName = "Fixture Park", CampsiteName = "WATCHMAN CAMPGROUND" }));
            File.WriteAllText(Path.Join(CachePath, "232445-Campsites.json"), "[]");
            File.WriteAllText(Path.Join(CachePath, "232445-Cached.json"), "\"2026-11-21\"");
            WriteFile("Trip.yaml", EmailStartTests.SearchText("Trip", true));
        }

        // the settings as the console's start loads them (Load.GetConfig reads Ntfy.yaml for the monitoring only)
        private CampertronInternalConfig Settings(OutputType Output, string? Email = ValidEmail, string? Ntfy = null)
        {
            WriteFile("General.yaml", "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            if (Email != null)
            {
                WriteFile("Email.yaml", Email);
            }
            if (Ntfy != null)
            {
                WriteFile("Ntfy.yaml", Ntfy);
            }
            return new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = Email == null ? null : Yaml.LoadSettingsText<EmailConfig>(Email),
                NtfyConfig = Notifications.IsMonitoring(Output) ? Yaml.NtfyConfigGetConfig(ConfigPath) : null
            };
        }

        private static string[] Lines(StringWriter Writer) =>
            Writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(L => L.Length > 0).ToArray();

        private void AssertNothingSent()
        {
            Assert.That((Waits, Sends, Publishes, Requested.Count), Is.EqualTo((0, 0, 0, 0)), "no check, request, email or publish");
        }

        private void AssertNoSecret()
        {
            Assert.That(Out.ToString() + Err.ToString(), Does.Not.Contain(Topic).And.Not.Contain(Token).And.Not.Contain("ntfy.example.invalid"));
        }

        // ---- the providers that start the monitoring ----

        [Test]
        public void NtfyOnly_StartsWithoutEmailYaml_AndNeverTouchesEmail()
        {
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Email: null, Ntfy: NtfyText());
            UsableSearch();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config));
            Assert.That(Lines(Out)[0], Does.Match(@"\| output Ntfy$"));
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That(Sends, Is.EqualTo(0));
            Assert.That(Config.NtfyConfig!.ResolvedToken!.Source, Is.EqualTo(NtfyTokenSource.None), "no token configured: none");
            AssertNoSecret();
        }

        [Test]
        public void NtfyOnly_WithAnIncompleteEmailYaml_StillStarts()
        {
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Email: "smtpServer: ''\nsmtpPort: 0\n", Ntfy: NtfyText());
            UsableSearch();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config), "email is not a provider of outputTo: Ntfy");
            Assert.That(Config.EmailConfig!.ResolvedPassword, Is.Null, "the SMTP password is never read");
        }

        [Test]
        public void EmailAndNtfy_StartTogether_AndResolveBothSecretsOnce()
        {
            string TokenFile = Path.Join(Root, "ntfy_token");
            File.WriteAllText(TokenFile, Token + "\n");
            CampertronInternalConfig Config = Settings(OutputType.Email, Ntfy: NtfyText(Extra: "tokenFile: '" + TokenFile + "'\n"));
            UsableSearch();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config));
            Assert.That(Lines(Out)[0], Does.Match(@"\| output Email and ntfy$"));
            Assert.That(Lines(Out).Count(L => System.Text.RegularExpressions.Regex.IsMatch(L, @"  Check \d+: 1 search, nothing new\. Next check ")), Is.EqualTo(1),
                "one check of one search for both providers: " + Out);
            Assert.That(Config.EmailConfig!.ResolvedPassword!.Source, Is.EqualTo(SmtpPasswordSource.EmailYaml));
            Assert.That((Config.NtfyConfig!.ResolvedToken!.Source, Config.NtfyConfig.ResolvedToken.Token), Is.EqualTo((NtfyTokenSource.File, Token)));
            Assert.That(File.ReadAllText(TokenFile), Is.EqualTo(Token + "\n"));
            AssertNoSecret();
        }

        [TestCase(null)]
        [TestCase(false)]
        public void EmailWithNtfyMissingOrOff_IsEmailOnly_AsBefore(bool? Enabled)
        {
            CampertronInternalConfig Config = Settings(OutputType.Email, Ntfy: Enabled == null ? null : NtfyText(Enabled.Value));
            UsableSearch();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config));
            Assert.That(Lines(Out)[0], Does.Match(@"\| output Email$"));
            Assert.That(Config.NtfyConfig?.ResolvedToken, Is.Null, "ntfy's token is not read when ntfy is off");
        }

        // ---- no provider, or one that can't be used: the start stops, nothing is sent ----

        [TestCase(null, "The file does not exist, and outputTo: Ntfy needs it")]
        [TestCase("# nothing\n", "The file has no settings")]
        [TestCase("off", "enabled is false, and outputTo: Ntfy needs ntfy")]
        public void NtfyModeWithoutNtfy_HasNoProvider_AndStops(string? Ntfy, string Expected)
        {
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Email: ValidEmail, Ntfy: Ntfy == "off" ? NtfyText(false) : Ntfy);
            UsableSearch();
            ConfigValidationException Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Config))!;
            List<string> Described = ConsoleUi.DescribeFailure(Refused);
            Assert.That(Described[0], Does.StartWith("Ntfy.yaml could not be used: the ntfy output can't start: ").And.Contain(Expected));
            Assert.That(Described[1], Is.EqualTo("File: " + Path.Join(ConfigPath, "Ntfy.yaml")));
            Assert.That(Described[2], Is.EqualTo(Ntfy == null
                ? "Create it in the settings folder with enabled: true, the server and the topic (see the README), or choose another outputTo."
                : "Fix the file, or delete it to turn ntfy off (with outputTo: Ntfy, choose another outputTo too)."));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "nothing written before Program.Main reports it");
            AssertNothingSent();
        }

        [TestCase(OutputType.Email)]
        [TestCase(OutputType.Ntfy)]
        public void AnEnabledNtfyThatCantBeUsed_IsNeverIgnored(OutputType Output)
        {
            CampertronInternalConfig Config = Settings(Output, Ntfy: "enabled: true\nserver: https://ntfy.example.invalid/" + Topic + "\ntopic: " + Topic + "\n");
            UsableSearch();
            ConfigValidationException Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Config))!;
            Assert.That(Refused.Diagnostics.Select(D => D.Code), Is.EqualTo(new[] { ConfigCode.NtfyServerInvalid }));
            Assert.That(ConsoleUi.DescribeFailure(Refused, Config.EmailConfig).Aggregate((A, B) => A + "\n" + B), Does.Not.Contain(Topic));
            AssertNothingSent();
        }

        [TestCase(OutputType.Ntfy, "ntfy mode")]
        [TestCase(OutputType.Email, "email mode")]
        public void ATokenSourceThatCantBeUsed_StopsTheStart_WithoutAFallback(OutputType Output, string Mode)
        {
            Environment_[NtfyToken.EnvironmentVariable] = Token;
            CampertronInternalConfig Config = Settings(Output, Ntfy: NtfyText(Extra: "tokenFile: '" + Path.Join(Root, "absent") + "'\n"));
            UsableSearch();

            Assert.That(Load.RunConsoleSearch(Config), Is.False);
            Assert.That(Load.RunConsole(Config), Is.EqualTo(1));

            List<string> Words = NtfyToken.Describe(NtfyTokenResult.Failed(NtfyTokenSource.File, NtfyTokenProblem.FileMissing));
            Assert.That(Lines(Err).Take(3), Is.EqualTo(new[] { "Error: " + Mode + " can't start: " + Words[0], "  " + Words[1], "  " + Words[2] }));
            Assert.That(Lines(Out)[0], Does.Match(@"\| output " + (Output == OutputType.Ntfy ? "Ntfy" : "Email and ntfy") + "$"));
            AssertNothingSent();
            AssertNoSecret();
        }

        [Test]
        public void AnEmptyTokenVariable_StopsTheStart()
        {
            Environment_[NtfyToken.EnvironmentVariable] = "";
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Ntfy: NtfyText());
            UsableSearch();
            Assert.That(Load.RunConsoleSearch(Config), Is.False);
            Assert.That(Lines(Err)[0], Is.EqualTo("Error: ntfy mode can't start: the ntfy access token can't be used: CAMPERTRON_NTFY_TOKEN is set but empty."));
            AssertNothingSent();
        }

        // ---- the no-search gate and its words in ntfy mode (D64, D65) ----

        [Test]
        public void NtfyModeWithoutASearchToCheck_StopsWithTheGatesWords()
        {
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Email: null, Ntfy: NtfyText());
            Assert.That(Load.RunConsoleSearch(Config), Is.False);
            Assert.That(Lines(Err), Is.EqualTo(new[]
            {
                "Error: ntfy mode has no search to check, so it stopped: the settings folder has no search file.",
                "  Add a search file and enable it (set autoRun: true in it, or use New search and Enabled (autoRun) in CampertronGUI's Searches tab), then start Campertron again, or restart its container.",
                "  Ntfy mode reads the search files only when it starts."
            }));
            Assert.That(Directory.GetFiles(ConfigPath, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml", "Ntfy.yaml" }), "no example search");
            AssertNothingSent();
        }

        [Test]
        public void EmailModeWithNtfy_KeepsTheGatesEmailWords()
        {
            CampertronInternalConfig Config = Settings(OutputType.Email, Ntfy: NtfyText());
            Assert.That(Load.RunConsoleSearch(Config), Is.False);
            Assert.That(Lines(Err), Is.EqualTo(EmailStartTests.NoSearchFileMessage));
        }

        // ---- the failure report, View Config and the container's message ----

        [Test]
        public void TheFailureReport_HidesNtfysTokenAndTopic_AndTheSmtpPassword()
        {
            Environment_[NtfyToken.EnvironmentVariable] = Token;
            CampertronInternalConfig Config = Settings(OutputType.Email, Email: ValidEmail.Replace("plain", "Smtp-Cnry-Pw"), Ntfy: NtfyText());
            NtfyToken.ResolveOnce(Config.NtfyConfig!);
            System.Reflection.MethodInfo? Report = typeof(ConsoleUi).GetMethod(nameof(ConsoleUi.ReportFailure), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                new[] { typeof(Exception), typeof(CampertronInternalConfig) });
            Assert.That(Report, Is.Not.Null, "Program.Main reports with all the run's settings");

            Assert.That(Report!.Invoke(null, new object?[] { new InvalidOperationException("odd " + Token + " " + Topic + " Smtp-Cnry-Pw"), Config }), Is.EqualTo(1));

            Assert.That(Err.ToString(), Does.Contain("odd [hidden] [hidden] [hidden]"));
            AssertNoSecret();
            Assert.That(Err.ToString(), Does.Not.Contain("Smtp-Cnry-Pw"));
        }

        [Test]
        public void ViewConfigAndCopy_ShowNothingOfNtfy()
        {
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Ntfy: NtfyText(Extra: "tokenFile: '" + Path.Join(Root, "secret-token-file") + "'\n"));
            string Shown = CurrentConfiguration.Describe(Config, DateTime.UtcNow, CultureInfo.InvariantCulture, typeof(Notifications).Assembly).Text + "\n" +
                CurrentConfiguration.Read(ConfigPath, CachePath, DateTime.UtcNow, CultureInfo.InvariantCulture, typeof(Notifications).Assembly).Text;
            Assert.That(Shown, Does.Not.Contain(Topic).And.Not.Contain("ntfy.example.invalid").And.Not.Contain("secret-token-file"));
            Assert.That(Shown, Does.Contain("Output => Ntfy"), "outputTo itself is shown, as for every output");
        }

        [Test]
        public void AContainerOrAStartWithoutATerminal_PointsToEmailOrNtfy()
        {
            Assert.That(Menu.StartProblem(true, true), Is.EqualTo("Unsupported output type in container. The container sends results by email or ntfy only: set outputTo: Email or Ntfy in /config/General.yaml."));
            Assert.That(Menu.StartProblem(false, false), Is.EqualTo("the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email or Ntfy in General.yaml."));
        }

        // ---- starts that are not the monitoring never read Ntfy.yaml ----

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.UnitTest)]
        public void AStartThatIsNotTheMonitoring_NeverReadsNtfyYaml(OutputType Output)
        {
            string Data = Path.Join(Root, "data");
            Directory.CreateDirectory(Path.Join(Data, "CampertronConfig"));
            File.WriteAllText(Path.Join(Data, "CampertronConfig", "General.yaml"), "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Data, "CampertronConfig", "Ntfy.yaml"), "not: [valid\n" + Topic);
            File.Copy(Path.Join(Load.GetConfig().ConfigPath!, "RecreationDotOrg.db"), Path.Join(Data, "CampertronConfig", "RecreationDotOrg.db"));
            Func<string> Original = Load.LocalApplicationDataLookup;
            try
            {
                Load.LocalApplicationDataLookup = () => Data;
                CampertronInternalConfig Config = Load.GetConfig();
                Assert.That(Config.NtfyConfig, Is.Null, "a broken Ntfy.yaml does not stop the console's Search");
                Load.LocalApplicationDataLookup = Original;
            }
            finally
            {
                Load.LocalApplicationDataLookup = Original;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            }
        }

        [Test]
        public void TheMonitoringsStart_ReadsNtfyYaml_AndReportsItsLoaderFailure()
        {
            string Data = Path.Join(Root, "data");
            Directory.CreateDirectory(Path.Join(Data, "CampertronConfig"));
            File.WriteAllText(Path.Join(Data, "CampertronConfig", "General.yaml"), "outputTo: Ntfy\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Data, "CampertronConfig", "Ntfy.yaml"), "enabled: true\npriority: " + Topic + "\n");
            Func<string> Original = Load.LocalApplicationDataLookup;
            try
            {
                Load.LocalApplicationDataLookup = () => Data;
                Exception Failure = Assert.Catch(() => Load.GetConfig())!;
                Assert.That(ConsoleUi.DescribeFailure(Failure)[0], Does.StartWith("Ntfy.yaml could not be read: "));
            }
            finally
            {
                Load.LocalApplicationDataLookup = Original;
            }
        }
    }
}
