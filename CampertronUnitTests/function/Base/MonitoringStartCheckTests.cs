using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // D71: ConfigValidation.MonitoringStartErrors, the check the GUI makes before it saves outputTo: Email or Ntfy, is the
    // unattended monitoring's own start check (ThrowIfEmailOutputRefused, ThrowIfNtfyOutputRefused) on the files of a
    // settings folder: for every Email.yaml and Ntfy.yaml below, it finds errors exactly when the start refuses, with the
    // start's words. Nothing is searched, sent or published; no password or token is looked for. Temporary folders only.
    public class MonitoringStartCheckTests
    {
        private string Root = string.Empty;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronMonitoringStartCheckTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(Root, true);
        }

        private const string Email = "smtpServer: smtp.example.org\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: pass\nsendFromAddress: b@example.org\n";
        private const string Topic = "dTopic0123456789abcdefABCDEF";

        private static string Recipients(string List) => Email + "sendToAddressList: " + List + "\n";

        // what the monitoring's start says about Email.yaml (CheckEmailOutputSettings): null when it starts
        private static string? EmailStart(string Text)
        {
            try
            {
                ConfigValidation.ThrowIfEmailOutputRefused(Yaml.LoadSettingsText<EmailConfig>(Text));
                return null;
            }
            catch (ConfigValidationException Refused)
            {
                return string.Join(" ", Refused.Diagnostics.Select(D => D.Message));
            }
        }

        private static string? NtfyStart(string? Text, bool Required)
        {
            try
            {
                ConfigValidation.ThrowIfNtfyOutputRefused(Text == null ? null : Yaml.LoadSettingsText<NtfyConfig>(Text), Required, Text != null);
                return null;
            }
            catch (ConfigValidationException Refused)
            {
                return string.Join(" ", Refused.Diagnostics.Select(D => D.Message));
            }
        }

        private string? Check(OutputType Output)
        {
            List<ConfigDiagnostic> Found = ConfigValidation.MonitoringStartErrors(Root, Output, DateTime.Now);
            Assert.That(Found.Select(D => D.Severity), Is.All.EqualTo(ConfigSeverity.Error));
            return Found.Count == 0 ? null : string.Join(" ", Found.Select(D => D.Message));
        }

        [TestCase("[a@example.org]", true)]
        [TestCase("['  a@example.org  ']", true)]
        [TestCase("[a@example.org, A@Example.org]", true)]
        [TestCase("['a@example.org, c@example.org', 'Name <d@example.org>']", true)]
        [TestCase("[a@example.org, '']", false)]
        [TestCase("[a@example.org, '   ']", false)]
        [TestCase("[a@example.org, ~]", false)]
        [TestCase("[a@example.org, nobody]", false)]
        [TestCase("['', '  ']", false)]
        [TestCase("[]", false)]
        [TestCase("~", false)]
        public void EmailYaml_IsRefusedExactlyWhenTheStartRefusesIt(string List, bool Starts)
        {
            string Text = Recipients(List);
            File.WriteAllText(Path.Join(Root, "Email.yaml"), Text);
            Assert.That(EmailStart(Text) == null, Is.EqualTo(Starts), "the monitoring's own start");
            Assert.That(Check(OutputType.Email), Is.EqualTo(EmailStart(Text)), "the same errors, in the start's words");
            Assert.That(Check(OutputType.Console), Is.Null, "the Console output doesn't use Email.yaml");
            Assert.That(Check(OutputType.HtmlFile), Is.Null);
            Assert.That(Check(OutputType.Ntfy), Is.EqualTo(NtfyStart(null, Required: true)), "outputTo: Ntfy doesn't look at Email.yaml, only at the missing Ntfy.yaml");
        }

        [TestCase("smtpPort: 0\n")]
        [TestCase("smtpPort: 70000\n")]
        [TestCase("sendFromAddress: nobody\n")]
        [TestCase("smtpServer: ''\n")]
        [TestCase("smtpPasswordFile: relative/pw\n")]
        [TestCase("smtpPasswordFile:\n")]
        public void EveryEmailSetting_IsJudgedAsTheStartJudgesIt(string Line)
        {
            string Key = Line.Split(':')[0];
            string Text = string.Join("\n", Recipients("[a@example.org]").Split('\n').Where(L => !L.StartsWith(Key + ":", StringComparison.Ordinal))) + Line;
            File.WriteAllText(Path.Join(Root, "Email.yaml"), Text);
            Assert.That(EmailStart(Text), Is.Not.Null, Line);
            Assert.That(Check(OutputType.Email), Is.EqualTo(EmailStart(Text)), Line);
        }

        [Test]
        public void AMissingOrBrokenEmailYaml_IsRefusedForTheEmailOutput()
        {
            Assert.That(Check(OutputType.Email), Is.EqualTo("The file does not exist."));
            File.WriteAllText(Path.Join(Root, "Email.yaml"), "smtpServer: [unclosed\n");
            Assert.That(Check(OutputType.Email), Is.Not.Null, "a file the loader can't read");
        }

        // Ntfy.yaml: outputTo: Ntfy requires it; outputTo: Email looks at it only while ntfy is on
        [TestCase(null, false, true)]
        [TestCase("enabled: true\nserver: https://ntfy.sh\ntopic: " + Topic + "\n", true, true)]
        [TestCase("enabled: true\nserver: https://ntfy.sh/" + Topic + "\ntopic: " + Topic + "\n", false, false)]
        [TestCase("enabled: true\nserver: https://ntfy.sh\ntopic: " + Topic + "\ntags: [tent, '']\n", false, false)]
        [TestCase("enabled: true\nserver: https://ntfy.sh\ntopic: " + Topic + "\ntags: [tent, 'two words']\n", false, false)]
        [TestCase("enabled: false\nserver: https://ntfy.sh\ntopic: " + Topic + "\n", false, true)]
        [TestCase("enabled: false\nserver: https://ntfy.sh/" + Topic + "\ntopic: 'two words'\ntags: ['', 'x y']\ntimeoutSeconds: 0\n", false, true)]
        [TestCase("# nothing\n", false, true)]
        public void NtfyYaml_IsRefusedExactlyWhenTheStartRefusesIt(string? Text, bool NtfyStarts, bool EmailStarts)
        {
            File.WriteAllText(Path.Join(Root, "Email.yaml"), Recipients("[a@example.org]"));
            if (Text != null)
            {
                File.WriteAllText(Path.Join(Root, "Ntfy.yaml"), Text);
            }
            Assert.That(NtfyStart(Text, Required: true) == null, Is.EqualTo(NtfyStarts), "outputTo: Ntfy, the monitoring's own start");
            Assert.That(NtfyStart(Text, Required: false) == null, Is.EqualTo(EmailStarts), "outputTo: Email, the monitoring's own start");
            Assert.That(Check(OutputType.Ntfy), Is.EqualTo(NtfyStart(Text, Required: true)));
            Assert.That(Check(OutputType.Email), Is.EqualTo(NtfyStart(Text, Required: false)));
            Assert.That(Check(OutputType.Console), Is.Null);
        }

        [Test]
        public void TheCheck_NamesNoValue_AndReadsNoSecret()
        {
            const string S = "V4lueS3cr3t";
            string TokenFile = Path.Join(Root, "token-" + S);
            File.WriteAllText(TokenFile, "tk_" + S);
            File.WriteAllText(Path.Join(Root, "Email.yaml"), "smtpServer: " + S + ".example.org\nsmtpPort: 70000\nsmtpUsername: " + S + "\nsmtpPassword: " + S
                + "\nsmtpPasswordFile: '" + Path.Join(Root, "pw-" + S) + "'\nsendToAddressList: ['" + S + "', '" + S + "@example.org', '']\nsendFromAddress: " + S + "\n");
            File.WriteAllText(Path.Join(Root, "Ntfy.yaml"), "enabled: true\nserver: https://" + S + ".example.org/" + S + "\ntopic: " + S + "\ntokenFile: '" + TokenFile + "'\n");
            DateTime TokenRead = File.GetLastAccessTimeUtc(TokenFile);
            foreach (OutputType Output in new[] { OutputType.Email, OutputType.Ntfy })
            {
                List<ConfigDiagnostic> Found = ConfigValidation.MonitoringStartErrors(Root, Output, DateTime.Now);
                Assert.That(Found, Is.Not.Empty);
                Assert.That(string.Join("\n", Found.Select(D => D.ToString() + " " + D.Message)), Does.Not.Contain(S).And.Not.Contain(Root));
            }
            Assert.That(File.Exists(Path.Join(Root, "pw-" + S)), Is.False, "the password file is never created");
            Assert.That(File.GetLastAccessTimeUtc(TokenFile), Is.EqualTo(TokenRead), "the token file is never read");
        }
    }
}
