using System.Globalization;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // Email mode and the SMTP password's sources (decision D66, business-rules §27): its start resolves the password once
    // (after the Email.yaml check and its start line, before the search files), keeps it for every send, and stops with one
    // error and exit code 1, before any search, request or email, when the source configured can't be used, never falling
    // back to another source. A send signs in with that password; a failure report hides every password the process knows
    // and says where the password came from. Starts that are not email mode never read the file. Temporary folders only;
    // every month is answered through the console's download seam (no network), the send is a recorder (no SMTP), the wait
    // between checks stops the loop, and the environment is the resolver's seam. Every secret is a unique canary, searched
    // for in everything written, with the secret file's path.
    public class SmtpPasswordEmailModeTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private string SecretsPath => Path.Join(Root, "secrets");
        private string Canary = string.Empty;
        private string EnvCanary = string.Empty;
        private string PlainCanary = string.Empty;
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
        private Func<string, string?> OriginalEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);
        private readonly List<string> Requested = new();
        private int Sends;
        private int Waits;
        private int EnvironmentReads;

        private sealed class StopTheLoop : Exception
        {
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronSmtpPasswordEmailModeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            Directory.CreateDirectory(SecretsPath);
            Canary = "File-Canary-" + Guid.NewGuid().ToString("N");
            EnvCanary = "Env-Canary-" + Guid.NewGuid().ToString("N");
            PlainCanary = "Plain-Canary-" + Guid.NewGuid().ToString("N");
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
            OriginalEnvironment = SmtpPassword.GetEnvironment;
            Environment_.Clear();
            Requested.Clear();
            Sends = 0;
            Waits = 0;
            EnvironmentReads = 0;
            SmtpPassword.GetEnvironment = Name =>
            {
                EnvironmentReads++;
                return Environment_.TryGetValue(Name, out string? Value) ? Value : null;
            };
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sends++; Message.Dispose(); };
            CheckWait = (Wait, Token) => { Waits++; throw new StopTheLoop(); };
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
            SmtpPassword.GetEnvironment = OriginalEnvironment;
            if (Directory.Exists(Root))
            {
                foreach (string Entry in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
                {
                    if (!OperatingSystem.IsWindows() && new FileInfo(Entry).LinkTarget == null)
                    {
                        File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                Directory.Delete(Root, true);
            }
        }

        // ---- the settings ----

        private string Secret(byte[] Bytes, string Name = "smtp_secret")
        {
            string FilePath = Path.Join(SecretsPath, Name);
            File.WriteAllBytes(FilePath, Bytes);
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return FilePath;
        }

        private string Secret(string Text) => Secret(Encoding.UTF8.GetBytes(Text));

        private static string Quoted(string FilePath) => "'" + FilePath.Replace("'", "''") + "'";

        // Email.yaml with a server that is never contacted (the send is a recorder), the plaintext canary unless told
        // otherwise, and smtpPasswordFile's line if given
        private string EmailText(string? PasswordFileLine, bool Plain = true) =>
            "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: start-user\nsmtpPassword: '" + (Plain ? PlainCanary : "") +
            "'\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n" + (PasswordFileLine == null ? "" : PasswordFileLine + "\n");

        private string WriteFile(string Name, string Text)
        {
            string FilePath = Path.Join(ConfigPath, Name);
            File.WriteAllText(FilePath, Text);
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            return FilePath;
        }

        private CampertronInternalConfig Settings(string EmailYaml, OutputType Output = OutputType.Email)
        {
            WriteFile("Email.yaml", EmailYaml);
            WriteFile("General.yaml", "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            return new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = Yaml.EmailConfigGetConfig(ConfigPath)
            };
        }

        // a usable enabled search, with its campground's search cache (so no database is needed)
        private void UsableSearch()
        {
            File.WriteAllText(Path.Join(CachePath, "232445-CampInfo.json"), JsonSerializer.Serialize(new ReturnParkCampground { ParkName = "Fixture Park", CampsiteName = "WATCHMAN CAMPGROUND" }));
            File.WriteAllText(Path.Join(CachePath, "232445-Campsites.json"), "[]");
            File.WriteAllText(Path.Join(CachePath, "232445-Cached.json"), "\"2026-11-21\"");
            WriteFile("Trip.yaml", EmailStartTests.SearchText("Trip", true));
        }

        // every file with its bytes (a file this user can't read: its length and mode) and its last write time
        private Dictionary<string, (byte[] Bytes, DateTime Written)> Snapshot() =>
            Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(p => Path.GetRelativePath(Root, p), p => (Contents(p), File.GetLastWriteTimeUtc(p)));

        private static byte[] Contents(string FilePath)
        {
            try
            {
                return File.ReadAllBytes(FilePath);
            }
            catch (UnauthorizedAccessException)
            {
                string Mode = OperatingSystem.IsWindows() ? "" : File.GetUnixFileMode(FilePath).ToString();
                return Encoding.UTF8.GetBytes("unreadable, " + new FileInfo(FilePath).Length + " bytes, " + Mode);
            }
        }

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

        private static string[] Lines(StringWriter Writer) =>
            Writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(L => L.Length > 0).ToArray();

        private const string StartLine = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  .* \| output Email$";

        // nothing written holds a password, the secret file's path or its name
        private void AssertNoSecretShown(string Shown)
        {
            Assert.That(Shown, Does.Not.Contain(Canary).And.Not.Contain(EnvCanary).And.Not.Contain(PlainCanary));
            Assert.That(Shown, Does.Not.Contain(SecretsPath).And.Not.Contain("smtp_secret"));
        }

        private void AssertNoSecretWritten() => AssertNoSecretShown(Out.ToString() + Err.ToString());

        // ---- a source that can be used: email mode starts, and its sends sign in with that password ----

        [TestCase("file")]
        [TestCase("variable")]
        [TestCase("plaintext")]
        public void EachSource_StartsEmailMode_AndTheSendsSignInWithItsPassword(string Source)
        {
            string? Line = null;
            if (Source == "file")
            {
                Line = "smtpPasswordFile: " + Quoted(Secret(Canary + "\n"));
                Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            }
            if (Source == "variable")
            {
                Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            }
            CampertronInternalConfig Config = Settings(EmailText(Line));
            UsableSearch();

            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config, CheckWait), "the loop ran its first check and waited for the next");

            string Expected = Source == "file" ? Canary : Source == "variable" ? EnvCanary : PlainCanary;
            Assert.That(Config.EmailConfig!.ResolvedPassword!.Source, Is.EqualTo(Source == "file" ? SmtpPasswordSource.File
                : Source == "variable" ? SmtpPasswordSource.Environment : SmtpPasswordSource.EmailYaml));
            System.Net.NetworkCredential Credential = CampsiteConfig.SmtpCredential(Config.EmailConfig);
            Assert.That((Credential.UserName, Credential.Password), Is.EqualTo(("start-user", Expected)));
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That(Lines(Out)[0], Does.Match(StartLine));
            Assert.That(Requested, Is.Not.Empty, "the search was checked");
            AssertNoSecretWritten();
        }

        // the start reads the file once: a later change, or the file's removal, needs a restart (the load-once policy)
        [Test]
        public void TheFile_IsReadOnceAtTheStart_ForEverySendOfTheProcess()
        {
            string FilePath = Secret(Canary);
            CampertronInternalConfig Config = Settings(EmailText("smtpPasswordFile: " + Quoted(FilePath)));
            UsableSearch();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config, CheckWait));

            File.WriteAllText(FilePath, EnvCanary);
            Assert.That(CampsiteConfig.SmtpCredential(Config.EmailConfig!).Password, Is.EqualTo(Canary));
            File.Delete(FilePath);
            Assert.That(CampsiteConfig.SmtpCredential(Config.EmailConfig!).Password, Is.EqualTo(Canary));
        }

        // ---- a source that can't be used: email mode stops at its start, with one error, before anything else ----

        private static string[] Expected(SmtpPasswordProblem Problem, SmtpPasswordSource Source)
        {
            List<string> Words = SmtpPassword.Describe(SmtpPasswordResult.Failed(Source, Problem));
            return new[] { "Error: email mode can't start: " + Words[0], "  " + Words[1], "  " + Words[2] };
        }

        private void AssertStopped(CampertronInternalConfig Config, SmtpPasswordProblem Problem, SmtpPasswordSource Source)
        {
            var Before = Snapshot();
            bool Result = Load.RunConsoleSearch(Config, CheckWait);

            Assert.That(Result, Is.False, "the start reports that email mode did not run (the console then exits with code 1)");
            Assert.That(Lines(Err), Is.EqualTo(Expected(Problem, Source)), "one error on standard error");
            Assert.That(Lines(Out), Has.Length.EqualTo(1).And.All.Match(StartLine), "standard output holds only the start line");
            Assert.That((Waits, Sends, Requested.Count), Is.EqualTo((0, 0, 0)), "no check, no request and no email");
            Assert.That(Config.EmailConfig!.ResolvedPassword, Is.Null, "nothing kept as a password");
            Assert.That(Load.RunConsole(Config, CheckWait), Is.EqualTo(1), "exit code 1");
            AssertNoSecretWritten();
            AssertUnchanged(Before);
        }

        [TestCase("missing", "FileMissing")]
        [TestCase("folder", "FileIsFolder")]
        [TestCase("empty", "FileEmpty")]
        [TestCase("line break only", "FileOnlyLineBreak")]
        [TestCase("two lines", "FileHasLineBreak")]
        [TestCase("byte-order mark", "FileHasByteOrderMark")]
        [TestCase("UTF-16", "FileHasNul")]
        [TestCase("not UTF-8", "FileNotUtf8")]
        [TestCase("too large", "FileTooLarge")]
        [TestCase("unreadable", "FileNotReadable")]
        public void AFileThatCantBeUsed_StopsEmailMode_WithoutAnotherSource(string Kind, string ProblemName)
        {
            string FilePath = Path.Join(SecretsPath, "smtp_secret");
            switch (Kind)
            {
                case "folder": Directory.CreateDirectory(FilePath); break;
                case "empty": Secret(Array.Empty<byte>()); break;
                case "line break only": Secret("\r\n"); break;
                case "two lines": Secret(Canary + "\n" + Canary + "\n"); break;
                case "byte-order mark": Secret(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Canary)).ToArray()); break;
                case "UTF-16": Secret(Encoding.Unicode.GetBytes(Canary)); break;
                case "not UTF-8": Secret(Encoding.UTF8.GetBytes(Canary).Concat(new byte[] { 0xFF }).ToArray()); break;
                case "too large": Secret(new string('x', SmtpPassword.MaxFileBytes) + Canary); break;
                case "unreadable":
                    if (OperatingSystem.IsWindows())
                    {
                        Assert.Ignore("Unix file modes only");
                        return;
                    }
                    Secret(Canary);
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
                    break;
            }
            Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            CampertronInternalConfig Config = Settings(EmailText("smtpPasswordFile: " + Quoted(FilePath)));
            UsableSearch();

            AssertStopped(Config, Enum.Parse<SmtpPasswordProblem>(ProblemName), SmtpPasswordSource.File);
        }

        [TestCase("", "EnvironmentEmpty")]
        [TestCase("with\nline break", "EnvironmentHasLineBreak")]
        public void AVariableThatCantBeUsed_StopsEmailMode_WithoutSmtpPassword(string Value, string ProblemName)
        {
            Environment_[SmtpPassword.EnvironmentVariable] = Value.Replace("with", EnvCanary);
            CampertronInternalConfig Config = Settings(EmailText(null));
            UsableSearch();

            AssertStopped(Config, Enum.Parse<SmtpPasswordProblem>(ProblemName), SmtpPasswordSource.Environment);
        }

        // the password comes before the search files: a folder without a usable search gets the password's error first
        [Test]
        public void ThePasswordsProblem_ComesBeforeTheSearchFiles()
        {
            CampertronInternalConfig Config = Settings(EmailText("smtpPasswordFile: " + Quoted(Path.Join(SecretsPath, "smtp_secret"))));
            AssertStopped(Config, SmtpPasswordProblem.FileMissing, SmtpPasswordSource.File);
        }

        // the path's form is Email.yaml's own check (ConfigValidation, D41), which comes first and names no value
        [TestCase("smtpPasswordFile:", "smtpPasswordFile is empty: ")]
        [TestCase("smtpPasswordFile: secrets/smtp_secret", "smtpPasswordFile is not an absolute path: ")]
        public void AnUnusablePath_IsRefusedByTheEmailYamlCheck_First(string Line, string Message)
        {
            Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            CampertronInternalConfig Config = Settings(EmailText(Line));
            UsableSearch();

            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Config, CheckWait))!;

            List<string> Described = ConsoleUi.DescribeFailure(Refused, Config.EmailConfig);
            Assert.That(Described[0], Does.StartWith("Email.yaml could not be used: the Email output can't start: " + Message));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "nothing was written before the refusal; Program.Main reports it");
            Assert.That(ConsoleUi.ReportFailure(Refused, Config.EmailConfig), Is.EqualTo(1));
            Assert.That((Waits, Sends, Requested.Count, EnvironmentReads), Is.EqualTo((0, 0, 0, 0)));
            AssertNoSecretWritten();
            AssertNoSecretShown(string.Join("\n", Described));
        }

        // ---- the starts that are not email mode never read the password ----

        [Test]
        public void AUnitTestStart_NeverReadsThePasswordOrTheVariable()
        {
            CampertronInternalConfig Config = Settings(EmailText("smtpPasswordFile: " + Quoted(Path.Join(SecretsPath, "smtp_secret"))), OutputType.UnitTest);
            UsableSearch();

            Assert.That(Load.RunConsoleSearch(Config, CheckWait), Is.True);
            Assert.That(Load.RunConsole(Config, CheckWait), Is.EqualTo(0));

            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "the silent start");
            Assert.That((EnvironmentReads, Config.EmailConfig!.ResolvedPassword), Is.EqualTo((0, (SmtpPasswordResult?)null)));
        }

        // ---- the send and the failure report ----

        // a send that began without the start's resolution resolves once; a source that can't be used throws before any
        // connection, and the report says what the start would have said
        [Test]
        public void ASendWithoutTheStart_ResolvesOnce_AndAProblemStopsItBeforeAnyConnection()
        {
            EmailConfig Email = Yaml.LoadSettingsText<EmailConfig>(EmailText("smtpPasswordFile: " + Quoted(Secret(Canary)), Plain: false));
            Assert.That(CampsiteConfig.SmtpCredential(Email).Password, Is.EqualTo(Canary));
            Assert.That(Email.ResolvedPassword!.Source, Is.EqualTo(SmtpPasswordSource.File));

            EmailConfig Broken = Yaml.LoadSettingsText<EmailConfig>(EmailText("smtpPasswordFile: " + Quoted(Path.Join(SecretsPath, "missing"))));
            SmtpPasswordException Thrown = Assert.Throws<SmtpPasswordException>(() => CampsiteConfig.SmtpCredential(Broken))!;
            List<string> Described = ConsoleUi.DescribeFailure(new AggregateException(Thrown), Broken);
            Assert.That(Described, Is.EqualTo(SmtpPassword.Describe(SmtpPasswordResult.Failed(SmtpPasswordSource.File, SmtpPasswordProblem.FileMissing))));
            AssertNoSecretShown(string.Join("\n", Described));
        }

        [TestCase("file", "Check the server, port and user name in Email.yaml, and the password in the file that smtpPasswordFile names.")]
        [TestCase("variable", "Check the server, port and user name in Email.yaml, and the password in CAMPERTRON_EMAIL_PASSWORD.")]
        [TestCase("plaintext", "Check the server, port, user name and password in Email.yaml.")]
        public void AFailedSend_HidesEveryPasswordTheProcessKnows_AndSaysWhereItCameFrom(string Source, string Hint)
        {
            string? Line = Source == "file" ? "smtpPasswordFile: " + Quoted(Secret(Canary)) : null;
            if (Source == "variable")
            {
                Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            }
            EmailConfig Email = Yaml.LoadSettingsText<EmailConfig>(EmailText(Line));
            Assert.That(SmtpPassword.ResolveOnce(Email).Succeeded, Is.True);
            string Password = Source == "file" ? Canary : Source == "variable" ? EnvCanary : PlainCanary;
            // a server or a library that quotes what it was given: the report must still hide it
            var Failure = new SmtpException("Failure sending mail: auth " + Password + " / " + PlainCanary + " refused.",
                new System.Net.Sockets.SocketException(111));

            List<string> Lines_ = ConsoleUi.DescribeFailure(Failure, Email);

            Assert.That(Lines_, Has.Count.EqualTo(2));
            Assert.That(Lines_[0], Does.StartWith("the email could not be sent: Failure sending mail: auth [hidden] / [hidden] refused."));
            Assert.That(Lines_[1], Is.EqualTo(Hint));
            Assert.That(ConsoleUi.ReportFailure(new InvalidOperationException("unexpected " + Password), Email), Is.EqualTo(1));
            Assert.That(Err.ToString(), Does.Contain("unexpected [hidden]"));
            AssertNoSecretWritten();
            AssertNoSecretShown(string.Join("\n", Lines_));
        }

        // Program.Main reports a failure with the run's email settings (the public overload), so the password of the
        // configured source is hidden too, not only smtpPassword
        [Test]
        public void ThePublicReport_TakesTheEmailSettings()
        {
            MethodInfo? Report = typeof(ConsoleUi).GetMethod(nameof(ConsoleUi.ReportFailure), BindingFlags.Public | BindingFlags.Static, new[] { typeof(Exception), typeof(EmailConfig) });
            Assert.That(Report, Is.Not.Null);
            Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            EmailConfig Email = Yaml.LoadSettingsText<EmailConfig>(EmailText(null, Plain: false));
            SmtpPassword.ResolveOnce(Email);
            Assert.That(Report!.Invoke(null, new object?[] { new Exception("boom " + EnvCanary), Email }), Is.EqualTo(1));
            Assert.That(Err.ToString(), Does.Contain("boom [hidden]"));
            AssertNoSecretWritten();
        }

        // ---- View Config ----

        // the console's View Config and the GUI's Current configuration never open Email.yaml (D60): no source, no path
        [Test]
        public void ViewConfig_ShowsNoPasswordAndNoSource()
        {
            Environment_[SmtpPassword.EnvironmentVariable] = EnvCanary;
            CampertronInternalConfig Config = Settings(EmailText("smtpPasswordFile: " + Quoted(Secret(Canary))));
            string Shown = CurrentConfiguration.Describe(Config, DateTime.UtcNow, CultureInfo.InvariantCulture, typeof(SmtpPassword).Assembly).Text + "\n" +
                CurrentConfiguration.Read(ConfigPath, CachePath, DateTime.UtcNow, CultureInfo.InvariantCulture, typeof(SmtpPassword).Assembly).Text;
            AssertNoSecretShown(Shown);
            Assert.That(Shown, Does.Not.Contain("smtpPassword").And.Not.Contain(SmtpPassword.EnvironmentVariable));
            Assert.That(EnvironmentReads, Is.EqualTo(0));
        }
    }
}
