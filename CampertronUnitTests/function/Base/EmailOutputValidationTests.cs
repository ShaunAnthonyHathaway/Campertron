using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // Email.yaml (ConfigValidation, D41; TD-51): what sending needs is an error with outputTo: Email and a warning
    // otherwise, and the Email loop checks it before it searches anything (Load.RunConsoleSearch), so the sample's empty
    // server and addresses no longer fail only at the first send. No connection is made, nothing is sent (the send is
    // replaced by a counter), and no value of Email.yaml reaches a message. Temporary folders only.
    public class EmailOutputValidationTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private CultureInfo OriginalCulture = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<TimeSpan> OriginalWait = null!;
        private int Sends;
        private int Waits;

        private sealed class StopTheLoop : Exception
        {
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronEmailOutputValidationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
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
            OriginalWait = Load.CheckWait;
            Sends = 0;
            Waits = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => { Sends++; Message.Dispose(); };
            Load.CheckWait = Wait => { Waits++; throw new StopTheLoop(); };
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
            Load.CheckWait = OriginalWait;
            Directory.Delete(Root, true);
        }

        private const string Valid = "smtpServer: smtp.example.org\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: pass\nsendToAddressList:\n- a@example.org\nsendFromAddress: b@example.org\n";

        private static string With(string Key, string Value) => Valid.Contains(Key + ":")
            ? Valid.Replace(Valid.Split('\n').Single(L => L.StartsWith(Key + ":")), Key + ": " + Value).Replace("\n- a@example.org", Key == "sendToAddressList" ? "" : "\n- a@example.org")
            : Valid + Key + ": " + Value + "\n";

        // ---- the rules ----

        [Test]
        public void AValidEmailYaml_HasNoDiagnostic_InEitherMode()
        {
            Assert.That(ConfigValidation.ValidateEmail(Valid, true), Is.Empty);
            Assert.That(ConfigValidation.ValidateEmail(Valid, false), Is.Empty);
            Assert.That(ConfigValidation.ValidateEmail(With("sendToAddressList", "['a@example.org, c@example.org', 'Name <d@example.org>']"), true), Is.Empty,
                "an entry may hold several addresses, as sending reads it (MailAddressCollection.Add)");
            Assert.That(ConfigValidation.ValidateEmail(With("smtpPort", "1"), true), Is.Empty);
            Assert.That(ConfigValidation.ValidateEmail(With("smtpPort", "65535"), true), Is.Empty);
        }

        [TestCase("smtpServer", "''", "SmtpServerMissing", null)]
        [TestCase("smtpServer", "'   '", "SmtpServerMissing", null)]
        [TestCase("smtpPort", "0", "SmtpPortOutOfRange", null)]
        [TestCase("smtpPort", "-25", "SmtpPortOutOfRange", null)]
        [TestCase("smtpPort", "65536", "SmtpPortOutOfRange", null)]
        [TestCase("sendFromAddress", "''", "SenderMissing", null)]
        [TestCase("sendFromAddress", "not-an-address", "SenderInvalid", null)]
        [TestCase("sendToAddressList", "~", "RecipientsMissing", null)]
        [TestCase("sendToAddressList", "[]", "RecipientsMissing", null)]
        [TestCase("sendToAddressList", "[not-an-address]", "RecipientInvalid", 0)]
        [TestCase("sendToAddressList", "[a@example.org, ~]", "RecipientInvalid", 1)]
        [TestCase("sendToAddressList", "[a@example.org, '  ']", "RecipientInvalid", 1)]
        public void WhatSendingNeeds_IsAnErrorInEmailOutput_AndAWarningOtherwise(string Key, string Value, string Code, int? Index)
        {
            string Text = With(Key, Value);
            foreach (bool EmailOutput in new[] { true, false })
            {
                ConfigDiagnostic D = ConfigValidation.ValidateEmail(Text, EmailOutput).Single();
                Assert.That((D.Code, D.Severity, D.Key, D.Index), Is.EqualTo((Enum.Parse<ConfigCode>(Code), EmailOutput ? ConfigSeverity.Error : ConfigSeverity.Warning, (string?)Key, Index)));
            }
        }

        [Test]
        public void MissingCredentials_AreAWarning_InBothModes()
        {
            // a relay may accept mail without signing in, so missing credentials never block the Email output
            foreach (string Text in new[] { With("smtpPassword", "''"), With("smtpUsername", "''"), Valid.Replace("smtpPassword: pass\n", "") })
            {
                foreach (bool EmailOutput in new[] { true, false })
                {
                    Assert.That(ConfigValidation.ValidateEmail(Text, EmailOutput).Select(D => (D.Code, D.Severity)), Is.EqualTo(new[] { (ConfigCode.CredentialsMissing, ConfigSeverity.Warning) }));
                }
            }
        }

        [Test]
        public void AnEmptyEmailYaml_IsAnErrorOnlyInEmailOutput_AndTheSampleIsIncomplete()
        {
            Assert.That(ConfigValidation.ValidateEmail("", true).Select(D => (D.Code, D.Severity)), Is.EqualTo(new[] { (ConfigCode.Empty, ConfigSeverity.Error) }));
            Assert.That(ConfigValidation.ValidateEmail("# nothing\n", false).Select(D => (D.Code, D.Severity)), Is.EqualTo(new[] { (ConfigCode.Empty, ConfigSeverity.Warning) }));
            Yaml.EmailConfigGetConfig(ConfigPath);
            List<ConfigDiagnostic> Sample = ConfigValidation.ValidateFile(Path.Join(ConfigPath, "Email.yaml"), DateTime.Now, OutputType.Email);
            Assert.That(Sample.Where(D => D.Severity == ConfigSeverity.Error).Select(D => D.Code), Is.EqualTo(new[]
            {
                ConfigCode.SmtpServerMissing, ConfigCode.SmtpPortOutOfRange, ConfigCode.SenderMissing, ConfigCode.RecipientsMissing
            }));
            Assert.That(ConfigValidation.ValidateFile(Path.Join(ConfigPath, "Email.yaml"), DateTime.Now, OutputType.Console).All(D => D.Severity != ConfigSeverity.Error), Is.True);
        }

        [Test]
        public void AnUnknownKeyOfEmailYaml_IsAnError_WithoutItsName()
        {
            ConfigDiagnostic D = ConfigValidation.ValidateEmail(Valid + "hunter2: x\n", false).Single();
            Assert.That((D.Code, D.Severity, D.Key, D.Line), Is.EqualTo((ConfigCode.UnknownKey, ConfigSeverity.Error, (string?)null, 8)));
            Assert.That(D.ToString(), Does.Not.Contain("hunter2"));
        }

        [Test]
        public void TheValidation_NeverConnectsSendsOrWrites()
        {
            const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly;
            var Called = new HashSet<string>();
            var Types = new Stack<Type>(new[] { typeof(ConfigValidation), typeof(ConfigFiles) });
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                foreach (System.Reflection.MethodBase M in T.GetMethods(All).Cast<System.Reflection.MethodBase>().Concat(T.GetConstructors(All)))
                {
                    foreach (System.Reflection.MemberInfo Member in YamlDocumentPreservationTests.ReferencedMembers(M))
                    {
                        Called.Add((Member.DeclaringType?.FullName ?? "?") + "::" + Member.Name);
                    }
                }
                foreach (Type Nested in T.GetNestedTypes(All))
                {
                    Types.Push(Nested);
                }
            }
            Assert.That(Called, Does.Contain("System.Net.Mail.MailAddress::TryCreate").And.Contain("System.Net.Mail.MailAddressCollection::Add"), "addresses are parsed, as sending parses them");
            string[] Forbidden =
            {
                "System.Net.Mail.SmtpClient", "System.Net.Sockets", "System.Net.Http", "System.Net.Dns", "System.Net.WebClient", "System.Diagnostics.Process",
                "System.IO.File::Write", "System.IO.File::Delete", "System.IO.File::Move", "System.IO.File::Create", "System.IO.File::Open", "System.IO.Directory::Create",
                "System.IO.Directory::Delete", "System.IO.Directory::Move", "System.IO.FileStream", "System.IO.StreamWriter", "System.Console",
                "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.RecDotOrg."
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
        }

        // ---- the start of the Email output ----

        private CampertronInternalConfig Config(OutputType Output, string EmailText)
        {
            File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), EmailText);
            return new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = Yaml.EmailConfigGetConfig(ConfigPath)
            };
        }

        [Test]
        public void TheSampleEmailYaml_StopsTheEmailOutput_BeforeAnythingIsSearched()
        {
            File.Delete(Path.Join(ConfigPath, "Email.yaml"));
            Yaml.EmailConfigGetConfig(ConfigPath);
            var Settings = new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = CachePath, GeneralConfig = new GeneralConfig { OutputTo = OutputType.Email }, EmailConfig = Yaml.EmailConfigGetConfig(ConfigPath) };

            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Settings))!;

            // with no search file, Preload would have created and run the example search: nothing of the search began
            Assert.That(Directory.GetFiles(ConfigPath).Select(Path.GetFileName), Is.EqualTo(new[] { "Email.yaml" }), "no example search was created");
            Assert.That(Directory.GetFileSystemEntries(CachePath), Is.Empty, "no campground data was prepared");
            Assert.That(Out.ToString(), Does.Not.Contain("Running").And.Not.Match(@"Check \d+:"));
            Assert.That((Waits, Sends), Is.EqualTo((0, 0)));
            List<string> Lines = ConsoleUi.DescribeFailure(Refused);
            Assert.That(Lines, Is.EqualTo(new[]
            {
                "Email.yaml could not be used: the Email output can't start: smtpServer is empty. smtpPort is not between 1 and 65535. sendFromAddress is empty. sendToAddressList has no address.",
                "File: " + Path.Join(ConfigPath, "Email.yaml"),
                "Fix the file, or delete it and Campertron creates a new one."
            }));
            Assert.That(ConsoleUi.ReportFailure(Refused), Is.EqualTo(1));
        }

        [Test]
        public void AnEmptyEmailYaml_StopsTheEmailOutputToo()
        {
            CampertronInternalConfig Settings = Config(OutputType.Email, "# nothing yet\n");
            Assert.That(Settings.EmailConfig, Is.Null);
            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Settings))!;
            Assert.That(ConsoleUi.DescribeFailure(Refused)[0], Does.StartWith("Email.yaml could not be used: the Email output can't start: The file has no settings"));
            Assert.That(Waits, Is.EqualTo(0));
        }

        [Test]
        public void AValidEmailYaml_StillStartsTheEmailLoop()
        {
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), "displayName: Trip\nautoRun: false\n");
            CampertronInternalConfig Settings = Config(OutputType.Email, Valid);

            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings), "the loop ran its first check and waited for the next");

            Assert.That(Out.ToString(), Does.Contain("Running 0 searches").And.Match(@"Check \d+: 0 searches, nothing new\."), "the check number counts every check of the process");
            Assert.That(Waits, Is.EqualTo(1));
        }

        [Test]
        public void OtherOutputTypes_StartWithAnIncompleteEmailYaml()
        {
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), "displayName: Trip\nautoRun: false\n");
            foreach (string Incomplete in new[] { "", "smtpServer: ''\nsmtpPort: 0\n" })
            {
                CampertronInternalConfig Settings = Config(OutputType.UnitTest, Incomplete);
                Assert.That(Load.RunConsoleSearch(Settings, true), Is.True, "a forced search with UnitTest output runs (0 searches) and returns");
                Assert.That(Load.RunConsoleSearch(Settings), Is.True);
            }
            Assert.That((Waits, Sends), Is.EqualTo((0, 0)));
        }

        [Test]
        public void TheFailure_NeverShowsAValueOfEmailYaml()
        {
            const string S = "V4lueS3cr3t";
            CampertronInternalConfig Settings = Config(OutputType.Email,
                "smtpServer: '   '\nsmtpPort: 70000\nsmtpUsername: user-" + S + "\nsmtpPassword: " + S + "-Pa55\nsendToAddressList: ['" + S + "', 'ok@example.org', '" + S + "@']\nsendFromAddress: " + S + "-from\n");
            Exception Refused = Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Settings))!;
            string Shown = string.Join("\n", ConsoleUi.DescribeFailure(Refused, Settings.EmailConfig!.SmtpPassword));
            Assert.That(Shown, Does.Contain("sendToAddressList item 1 is not a valid email address.").And.Contain("sendToAddressList item 3 is not a valid email address."));
            Assert.That(Shown, Does.Not.Contain(S));
            ConsoleUi.ReportFailure(Refused);
            Assert.That(Out.ToString() + Err.ToString(), Does.Not.Contain(S));
            foreach (ConfigDiagnostic D in ConfigValidation.ValidateFile(Path.Join(ConfigPath, "Email.yaml"), DateTime.Now, OutputType.Email))
            {
                Assert.That(D.ToString(), Does.Not.Contain(S));
            }
        }
    }
}
