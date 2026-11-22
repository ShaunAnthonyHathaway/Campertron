using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // The fix after v0.2.0 step 3's local test (§BU): ConfigValidation.MonitoringSecretFiles names the secret files the
    // unattended monitoring's start would read for an outputTo, by the start's own rules. For every combination of outputTo,
    // Ntfy.yaml (none, on, off), smtpPasswordFile (set, not written, not fully qualified) and tokenFile (set, not written),
    // with every named file missing, it names smtpPasswordFile exactly when the start's provider rule uses email
    // (Notifications.EmailEnabled) and its password check (Load.CheckSmtpPassword) refuses because of that file, and
    // tokenFile exactly when the start uses ntfy (Notifications.NtfyEnabled) and its token check (Load.CheckNtfyToken)
    // refuses. Nothing is sent or published; no secret file exists.
    public class MonitoringSecretFilesTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string Secrets => Path.Join(Root, "secrets");
        private TextWriter OriginalError = null!;
        private StringWriter Err = null!;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronMonitoringSecretFilesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(Secrets);
            OriginalError = Console.Error;
            Err = new StringWriter();
            Console.SetError(Err);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetError(OriginalError);
            Err.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private const string EmailBase = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: plain-fallback-never-used\n" +
            "sendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";

        [Test]
        public void TheFilesNamed_AreThoseTheStartReads_AndRefusesWhenMissing(
            [Values(OutputType.Console, OutputType.HtmlFile, OutputType.Email, OutputType.Ntfy)] OutputType Output,
            [Values("none", "on", "off")] string Ntfy,
            [Values("set", "unset", "relative")] string PasswordFile,
            [Values("set", "unset")] string TokenFile)
        {
            string Password = Path.Join(Secrets, "smtp_password"), Token = Path.Join(Secrets, "ntfy_token");
            string EmailText = EmailBase + PasswordFile switch
            {
                "set" => "smtpPasswordFile: '" + Password + "'\n",
                "relative" => "smtpPasswordFile: secrets/smtp_password\n",
                _ => string.Empty
            };
            File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), EmailText);
            if (Ntfy != "none")
            {
                File.WriteAllText(Path.Join(ConfigPath, "Ntfy.yaml"), "enabled: " + (Ntfy == "on" ? "true" : "false") + "\nserver: https://ntfy.example.invalid\ntopic: secretsTopic0123456789\n"
                    + (TokenFile == "set" ? "tokenFile: '" + Token + "'\n" : string.Empty));
            }
            var Settings = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = Yaml.LoadSettingsText<EmailConfig>(EmailText),
                NtfyConfig = Yaml.NtfyConfigGetConfig(ConfigPath)
            };

            List<ConfigValidation.MonitoringSecretFile> Found = ConfigValidation.MonitoringSecretFiles(ConfigPath, Output, DateTime.Now);

            bool ReadsPassword = Notifications.EmailEnabled(Settings) && PasswordFile == "set";
            bool ReadsToken = Notifications.NtfyEnabled(Settings) && TokenFile == "set";
            var Expected = new List<ConfigValidation.MonitoringSecretFile>();
            if (ReadsPassword)
            {
                Expected.Add(new ConfigValidation.MonitoringSecretFile("Email.yaml", "smtpPasswordFile", Password, "password"));
            }
            if (ReadsToken)
            {
                Expected.Add(new ConfigValidation.MonitoringSecretFile("Ntfy.yaml", "tokenFile", Token, "token"));
            }
            Assert.That(Found, Is.EqualTo(Expected));

            // the start's own checks: each refuses because of its missing file exactly when the file is named
            if (Notifications.EmailEnabled(Settings) && PasswordFile != "relative")
            {
                Assert.That(Load.CheckSmtpPassword(Settings), Is.EqualTo(!ReadsPassword), "email mode's password check");
            }
            if (Notifications.NtfyEnabled(Settings))
            {
                Assert.That(Load.CheckNtfyToken(Settings), Is.EqualTo(!ReadsToken), "the token check");
            }
            Assert.That(File.Exists(Password) || File.Exists(Token), Is.False, "nothing is created");
        }

        // a settings file that can't be loaded names nothing: the start's own refusal of it is MonitoringStartErrors'
        [Test]
        public void AnUnloadableSettingsFile_NamesNothing()
        {
            File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpPasswordFile: [broken\n");
            File.WriteAllText(Path.Join(ConfigPath, "Ntfy.yaml"), "enabled: [broken\n");
            Assert.That(ConfigValidation.MonitoringSecretFiles(ConfigPath, OutputType.Email, DateTime.Now), Is.Empty);
            Assert.That(ConfigValidation.MonitoringStartErrors(ConfigPath, OutputType.Email, DateTime.Now), Is.Not.Empty, "the start refuses them itself");
            File.Delete(Path.Join(ConfigPath, "Email.yaml"));
            File.Delete(Path.Join(ConfigPath, "Ntfy.yaml"));
            Assert.That(ConfigValidation.MonitoringSecretFiles(ConfigPath, OutputType.Email, DateTime.Now), Is.Empty, "no settings files");
        }
    }
}
