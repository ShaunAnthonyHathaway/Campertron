using System.Runtime.InteropServices;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The SMTP password's sources (decision D66, business-rules §27): the file Email.yaml's smtpPasswordFile names, then
    // CAMPERTRON_EMAIL_PASSWORD, then the plaintext smtpPassword, with no fallback from a source that is configured but can't
    // be used. The file's contract: an absolute path, followed through links to a regular file of 1 to 4,096 bytes, strict
    // UTF-8 without a byte-order mark or NUL, at most one line break at its end removed and nothing trimmed. Every secret
    // here is a unique synthetic canary, and every message and description is searched for it and for the file's path.
    // Temporary folders only; the environment is the resolver's seam, never the process's.
    public class SmtpPasswordTests
    {
        private string Root = string.Empty;
        private string Canary = string.Empty;
        private string OtherCanary = string.Empty;
        private Func<string, string?> OriginalEnvironment = null!;
        private readonly Dictionary<string, string> Environment_ = new(StringComparer.Ordinal);

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronSmtpPasswordTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Canary = "Pw-Canary-" + Guid.NewGuid().ToString("N");
            OtherCanary = "Env-Canary-" + Guid.NewGuid().ToString("N");
            OriginalEnvironment = SmtpPassword.GetEnvironment;
            Environment_.Clear();
            SmtpPassword.GetEnvironment = Name => Environment_.TryGetValue(Name, out string? Value) ? Value : null;
        }

        [TearDown]
        public void TearDown()
        {
            SmtpPassword.GetEnvironment = OriginalEnvironment;
            if (Directory.Exists(Root))
            {
                foreach (string Entry in Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories))
                {
                    if (!OperatingSystem.IsWindows() && new FileInfo(Entry).LinkTarget == null && File.Exists(Entry))
                    {
                        File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                }
                Directory.Delete(Root, true);
            }
        }

        private string WriteSecret(string Name, byte[] Bytes)
        {
            string FilePath = Path.Join(Root, Name);
            File.WriteAllBytes(FilePath, Bytes);
            return FilePath;
        }

        private string WriteSecret(string Name, string Text) => WriteSecret(Name, Encoding.UTF8.GetBytes(Text));

        private static EmailConfig Load(string Text) => Yaml.LoadSettingsText<EmailConfig>(Text);

        private static string Quoted(string FilePath) => "'" + FilePath.Replace("'", "''") + "'";

        private string EmailText(string? PasswordFileLine, string SmtpPassword = "") =>
            "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: '" + SmtpPassword + "'\nsendToAddressList:\n- to@example.invalid\n" +
            "sendFromAddress: from@example.invalid\n" + (PasswordFileLine == null ? "" : PasswordFileLine + "\n");

        // what an operator could see: the description of a problem, and the result's text. Never a password or the path
        private void AssertNoValue(SmtpPasswordResult Result, string? FilePath = null)
        {
            string Shown = Result + "\n" + string.Join("\n", SmtpPassword.Describe(Result)) + "\n" + new SmtpPasswordException(Result).Message;
            Assert.That(Shown, Does.Not.Contain(Canary).And.Not.Contain(OtherCanary));
            Assert.That(Shown, Does.Not.Contain(Root));
            if (FilePath != null)
            {
                Assert.That(Shown, Does.Not.Contain(Path.GetFileName(FilePath)));
            }
        }

        // ---- precedence ----

        [TestCase("")]
        [TestCase("plain")]
        public void WithoutAFileOrTheVariable_ThePlaintextPasswordIsUsed_AsBefore(string Plain)
        {
            EmailConfig Email = Load(EmailText(null, Plain));

            SmtpPasswordResult Result = SmtpPassword.Resolve(Email);

            Assert.That((Result.Source, Result.Succeeded, Result.Password), Is.EqualTo((SmtpPasswordSource.EmailYaml, true, Plain)));
            Assert.That(Email.SmtpPasswordFileWritten, Is.False);
            Assert.That(SmtpPassword.SelectedSource(Email), Is.EqualTo(SmtpPasswordSource.EmailYaml));
        }

        [Test]
        public void AMissingSmtpPassword_IsStillNull_AsBefore()
        {
            EmailConfig Email = Load("smtpServer: s\nsmtpPort: 587\n");
            SmtpPasswordResult Result = SmtpPassword.Resolve(Email);
            Assert.That((Result.Source, Result.Succeeded, Result.Password), Is.EqualTo((SmtpPasswordSource.EmailYaml, true, (string?)null)));
        }

        [Test]
        public void TheVariable_ComesBeforeSmtpPassword()
        {
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary;
            SmtpPasswordResult Result = SmtpPassword.Resolve(Load(EmailText(null, Canary)));
            Assert.That((Result.Source, Result.Password), Is.EqualTo((SmtpPasswordSource.Environment, OtherCanary)));
        }

        [Test]
        public void TheFile_ComesBeforeTheVariable_AndSmtpPassword()
        {
            string FilePath = WriteSecret("smtp_password", Canary + "\n");
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary;
            EmailConfig Email = Load(EmailText("smtpPasswordFile: " + Quoted(FilePath), "plain-unused"));

            SmtpPasswordResult Result = SmtpPassword.Resolve(Email);

            Assert.That((Result.Source, Result.Succeeded, Result.Password), Is.EqualTo((SmtpPasswordSource.File, true, Canary)));
            Assert.That(Email.SmtpPasswordFile, Is.EqualTo(FilePath));
        }

        // a configured source that can't be used is a problem of that source: never the next one
        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("relative")]
        public void AFileThatCantBeUsed_NeverFallsBack_ToTheVariableOrSmtpPassword(string Kind)
        {
            string FilePath = Kind == "relative" ? "test_secret" : Path.Join(Root, "test_secret");
            if (Kind == "empty")
            {
                WriteSecret("test_secret", Array.Empty<byte>());
            }
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary;
            EmailConfig Email = Load(EmailText("smtpPasswordFile: " + Quoted(FilePath), Canary));

            SmtpPasswordResult Result = SmtpPassword.Resolve(Email);

            Assert.That(Result.Source, Is.EqualTo(SmtpPasswordSource.File));
            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Password, Is.Null);
            Assert.That(Result.Problem, Is.EqualTo(Kind == "missing" ? SmtpPasswordProblem.FileMissing
                : Kind == "empty" ? SmtpPasswordProblem.FileEmpty : SmtpPasswordProblem.FilePathNotAbsolute));
            AssertNoValue(Result, FilePath);
            Assert.That(() => SmtpPassword.ForSending(Email), Throws.TypeOf<SmtpPasswordException>());
            Assert.That(Email.ResolvedPassword, Is.Null, "a problem is never kept as a password");
        }

        [Test]
        public void AnEmptyVariable_NeverFallsBack_ToSmtpPassword()
        {
            Environment_[SmtpPassword.EnvironmentVariable] = "";
            SmtpPasswordResult Result = SmtpPassword.Resolve(Load(EmailText(null, Canary)));
            Assert.That((Result.Source, Result.Problem, Result.Password), Is.EqualTo((SmtpPasswordSource.Environment, (SmtpPasswordProblem?)SmtpPasswordProblem.EnvironmentEmpty, (string?)null)));
            AssertNoValue(Result);
        }

        // smtpPasswordFile written without a usable value (YAML reads `smtpPasswordFile:` and `~` as null) is the file
        // source configured without a path: a problem, not "no file"
        [TestCase("smtpPasswordFile:")]
        [TestCase("smtpPasswordFile: ~")]
        [TestCase("smtpPasswordFile: null")]
        [TestCase("smtpPasswordFile: ''")]
        [TestCase("smtpPasswordFile: '   '")]
        public void AWrittenButEmptySmtpPasswordFile_IsAProblem_NeverAnotherSource(string Line)
        {
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary;
            EmailConfig Email = Load(EmailText(Line, Canary));

            Assert.That(Email.SmtpPasswordFileWritten, Is.True);
            Assert.That(SmtpPassword.SelectedSource(Email), Is.EqualTo(SmtpPasswordSource.File));
            SmtpPasswordResult Result = SmtpPassword.Resolve(Email);
            Assert.That((Result.Source, Result.Problem), Is.EqualTo((SmtpPasswordSource.File, (SmtpPasswordProblem?)SmtpPasswordProblem.FilePathEmpty)));
            AssertNoValue(Result);
        }

        // ---- the file ----

        [TestCase("pw", "pw")]
        [TestCase("pw\n", "pw")]
        [TestCase("pw\r\n", "pw")]
        [TestCase(" pw \t", " pw \t")]
        [TestCase("  spaced  \n", "  spaced  ")]
        [TestCase("pässwörd ✓", "pässwörd ✓")]
        [TestCase("#not: yaml'\"", "#not: yaml'\"")]
        public void TheFile_IsThePassword_WithOnlyOneLineBreakAtItsEndRemoved(string Content, string Expected)
        {
            string FilePath = WriteSecret("secret", Content.Replace("pw", Canary));
            SmtpPasswordResult Result = SmtpPassword.ReadFile(FilePath);
            Assert.That((Result.Source, Result.Succeeded, Result.Password), Is.EqualTo((SmtpPasswordSource.File, true, Expected.Replace("pw", Canary))));
        }

        [TestCase("pw\n\n", "FileHasLineBreak")]
        [TestCase("pw\r", "FileHasLineBreak")]
        [TestCase("pw\n\r\n", "FileHasLineBreak")]
        [TestCase("first\nsecond", "FileHasLineBreak")]
        [TestCase("first\nsecond\n", "FileHasLineBreak")]
        [TestCase("\n", "FileOnlyLineBreak")]
        [TestCase("\r\n", "FileOnlyLineBreak")]
        [TestCase("\n\n", "FileHasLineBreak")]
        [TestCase("\r", "FileHasLineBreak")]
        public void LineBreaks_OtherThanOneAtTheEnd_AreAProblem(string Content, string Expected)
        {
            string FilePath = WriteSecret("secret", Content.Replace("pw", Canary));
            SmtpPasswordResult Result = SmtpPassword.ReadFile(FilePath);
            Assert.That((Result.Succeeded, Result.Problem), Is.EqualTo((false, (SmtpPasswordProblem?)Enum.Parse<SmtpPasswordProblem>(Expected))));
            AssertNoValue(Result, FilePath);
        }

        [Test]
        public void ContentThatIsNotPlainUtf8Text_IsAProblem()
        {
            byte[] Pw = Encoding.UTF8.GetBytes(Canary);
            (byte[] Bytes, SmtpPasswordProblem Problem)[] Cases =
            {
                (Array.Empty<byte>(), SmtpPasswordProblem.FileEmpty),
                (new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Pw).ToArray(), SmtpPasswordProblem.FileHasByteOrderMark),
                (Pw.Concat(new byte[] { 0 }).ToArray(), SmtpPasswordProblem.FileHasNul),
                (Encoding.Unicode.GetBytes(Canary), SmtpPasswordProblem.FileHasNul),
                (new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(Canary)).ToArray(), SmtpPasswordProblem.FileHasNul),
                (Pw.Concat(new byte[] { 0xC3, 0x28 }).ToArray(), SmtpPasswordProblem.FileNotUtf8),
                (Pw.Concat(new byte[] { 0xED, 0xA0, 0x80 }).ToArray(), SmtpPasswordProblem.FileNotUtf8),
                (Pw.Concat(new byte[] { 0xE2, 0x82 }).ToArray(), SmtpPasswordProblem.FileNotUtf8),
            };
            int i = 0;
            foreach ((byte[] Bytes, SmtpPasswordProblem Problem) in Cases)
            {
                string FilePath = WriteSecret("secret" + i++, Bytes);
                SmtpPasswordResult Result = SmtpPassword.ReadFile(FilePath);
                Assert.That((Result.Succeeded, Result.Problem), Is.EqualTo((false, (SmtpPasswordProblem?)Problem)), System.Convert.ToHexString(Bytes.Take(4).ToArray()));
                AssertNoValue(Result, FilePath);
            }
        }

        [Test]
        public void TheFile_IsAtMost4096Bytes()
        {
            string Largest = new string('x', SmtpPassword.MaxFileBytes - Canary.Length) + Canary;
            Assert.That(SmtpPassword.ReadFile(WriteSecret("largest", Largest)).Password, Is.EqualTo(Largest));
            SmtpPasswordResult TooLarge = SmtpPassword.ReadFile(WriteSecret("large", Largest + "\n"));
            Assert.That(TooLarge.Problem, Is.EqualTo(SmtpPasswordProblem.FileTooLarge), "4,097 bytes, even when the last is a line break");
            Assert.That(SmtpPassword.ReadFile(WriteSecret("huge", new byte[1024 * 1024])).Problem, Is.EqualTo(SmtpPasswordProblem.FileTooLarge));
            AssertNoValue(TooLarge);
        }

        [TestCase("smtp_password")]
        [TestCase("./smtp_password")]
        [TestCase("../smtp_password")]
        [TestCase("~/smtp_password")]
        [TestCase("secrets/smtp_password")]
        public void ARelativePath_IsAProblem_AndNothingIsReadRelativeToTheWorkingDirectory(string Relative)
        {
            SmtpPasswordResult Result = SmtpPassword.ReadFile(Relative);
            Assert.That((Result.Source, Result.Problem), Is.EqualTo((SmtpPasswordSource.File, (SmtpPasswordProblem?)SmtpPasswordProblem.FilePathNotAbsolute)));
        }

        [Test]
        public void ARootedButNotFullyQualifiedWindowsPath_IsAProblem()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(SmtpPassword.ReadFile("C:smtp_password").Problem, Is.EqualTo(SmtpPasswordProblem.FilePathNotAbsolute), "not rooted on Unix either");
                return;
            }
            Assert.That(SmtpPassword.ReadFile(@"\smtp_password").Problem, Is.EqualTo(SmtpPasswordProblem.FilePathNotAbsolute));
            Assert.That(SmtpPassword.ReadFile("C:smtp_password").Problem, Is.EqualTo(SmtpPasswordProblem.FilePathNotAbsolute));
        }

        [Test]
        public void AMissingFile_AFolder_AndAMissingFolder_AreProblems()
        {
            Assert.That(SmtpPassword.ReadFile(Path.Join(Root, "missing")).Problem, Is.EqualTo(SmtpPasswordProblem.FileMissing));
            Assert.That(SmtpPassword.ReadFile(Path.Join(Root, "no-folder", "smtp_password")).Problem, Is.EqualTo(SmtpPasswordProblem.FileMissing));
            Directory.CreateDirectory(Path.Join(Root, "folder"));
            Assert.That(SmtpPassword.ReadFile(Path.Join(Root, "folder")).Problem, Is.EqualTo(SmtpPasswordProblem.FileIsFolder));
            Assert.That(SmtpPassword.ReadFile(Path.Join(Root, "folder") + Path.DirectorySeparatorChar).Problem, Is.EqualTo(SmtpPasswordProblem.FileIsFolder));
        }

        // a Docker or Kubernetes secret is often a link (Kubernetes: a chain of them): followed to a regular file
        [Test]
        public void Links_AreFollowed_ToAFile_AndALinkToNothingOrToAFolderIsAProblem()
        {
            string Target = WriteSecret("target", Canary + "\n");
            string Link = Path.Join(Root, "link");
            string Chain = Path.Join(Root, "chain");
            try
            {
                File.CreateSymbolicLink(Link, Target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Assert.Ignore("this user can't create symbolic links (Windows without developer mode)");
            }
            File.CreateSymbolicLink(Chain, "link");
            Assert.That(SmtpPassword.ReadFile(Link).Password, Is.EqualTo(Canary));
            Assert.That(SmtpPassword.ReadFile(Chain).Password, Is.EqualTo(Canary), "a relative link of a link");

            string Dangling = Path.Join(Root, "dangling");
            File.CreateSymbolicLink(Dangling, Path.Join(Root, "nothing"));
            Assert.That(SmtpPassword.ReadFile(Dangling).Problem, Is.EqualTo(SmtpPasswordProblem.FileMissing));

            Directory.CreateDirectory(Path.Join(Root, "folder"));
            string ToFolder = Path.Join(Root, "to-folder");
            Directory.CreateSymbolicLink(ToFolder, Path.Join(Root, "folder"));
            Assert.That(SmtpPassword.ReadFile(ToFolder).Problem, Is.EqualTo(SmtpPasswordProblem.FileIsFolder));

            string Empty = WriteSecret("empty", Array.Empty<byte>());
            string ToEmpty = Path.Join(Root, "to-empty");
            File.CreateSymbolicLink(ToEmpty, Empty);
            Assert.That(SmtpPassword.ReadFile(ToEmpty).Problem, Is.EqualTo(SmtpPasswordProblem.FileEmpty), "the target's length, not the link's");
        }

        [Test]
        public void AFileThisUserCantRead_IsAProblem()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes only");
                return;
            }
            string FilePath = WriteSecret("secret", Canary);
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
            SmtpPasswordResult Result = SmtpPassword.ReadFile(FilePath);
            Assert.That(Result.Problem, Is.EqualTo(SmtpPasswordProblem.FileNotReadable));
            AssertNoValue(Result, FilePath);
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int mkfifo(string path, uint mode);

        // a FIFO or a device reports no length: it is never opened (opening a FIFO waits for a writer), so it is "empty"
        [Test]
        public void AFifoOrADevice_IsNeverOpened_AndIsAProblem()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix FIFOs and devices only");
                return;
            }
            string Fifo = Path.Join(Root, "fifo");
            Assert.That(mkfifo(Fifo, System.Convert.ToUInt32("600", 8)), Is.EqualTo(0), "mkfifo");
            Task<SmtpPasswordResult> Reading = Task.Run(() => SmtpPassword.ReadFile(Fifo));
            Assert.That(Reading.Wait(TimeSpan.FromSeconds(10)), Is.True, "the FIFO was not opened (that would wait for a writer)");
            Assert.That(Reading.Result.Problem, Is.EqualTo(SmtpPasswordProblem.FileEmpty));
            Assert.That(SmtpPassword.ReadFile("/dev/null").Problem, Is.EqualTo(SmtpPasswordProblem.FileEmpty));
        }

        // reading changes nothing: the file's bytes, time and mode, and its folder's entries
        [Test]
        public void ReadingTheFile_ChangesNothing()
        {
            string FilePath = WriteSecret("secret", Canary + "\n");
            File.SetLastWriteTimeUtc(FilePath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead);
            }
            string[] Before = Directory.GetFileSystemEntries(Root);
            UnixFileMode? Mode = null;
            if (!OperatingSystem.IsWindows())
            {
                Mode = File.GetUnixFileMode(FilePath);
            }

            Assert.That(SmtpPassword.ReadFile(FilePath).Password, Is.EqualTo(Canary));

            Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(Encoding.UTF8.GetBytes(Canary + "\n")));
            Assert.That(File.GetLastWriteTimeUtc(FilePath), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(Directory.GetFileSystemEntries(Root), Is.EquivalentTo(Before), "nothing created or removed beside it");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(FilePath), Is.EqualTo(Mode), "its mode is never changed");
            }
        }

        // ---- the variable ----

        [TestCase(" spaced ")]
        [TestCase("\tquoted'\"")]
        [TestCase("päss")]
        public void TheVariable_IsUsedAsItIs_NothingTrimmed(string Value)
        {
            Environment_[SmtpPassword.EnvironmentVariable] = Value + OtherCanary;
            SmtpPasswordResult Result = SmtpPassword.Resolve(Load(EmailText(null)));
            Assert.That(Result.Password, Is.EqualTo(Value + OtherCanary));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("a\nb")]
        public void AVariableWithALineBreak_IsAProblem(string Value)
        {
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary + Value;
            SmtpPasswordResult Result = SmtpPassword.Resolve(Load(EmailText(null, Canary)));
            Assert.That((Result.Source, Result.Problem), Is.EqualTo((SmtpPasswordSource.Environment, (SmtpPasswordProblem?)SmtpPasswordProblem.EnvironmentHasLineBreak)));
            AssertNoValue(Result);
        }

        // the production seam reads the process's environment, where "unset" and "set to nothing" differ on Unix
        [Test]
        public void TheProductionSeam_IsTheProcesssEnvironment()
        {
            Assert.That(OriginalEnvironment("CAMPERTRON_TEST_UNSET_" + Guid.NewGuid().ToString("N")), Is.Null);
            Assert.That(OriginalEnvironment("PATH"), Is.EqualTo(Environment.GetEnvironmentVariable("PATH")));
        }

        // ---- once, and the secrets a report hides ----

        [Test]
        public void ResolveOnce_ReadsTheFileOnce_AndKeepsThePasswordForTheProcess()
        {
            string FilePath = WriteSecret("secret", Canary + "\n");
            EmailConfig Email = Load(EmailText("smtpPasswordFile: " + Quoted(FilePath)));

            Assert.That(SmtpPassword.ResolveOnce(Email).Password, Is.EqualTo(Canary));
            File.WriteAllText(FilePath, OtherCanary);
            Assert.That(SmtpPassword.ForSending(Email), Is.EqualTo(Canary), "a changed file needs a restart (the load-once policy)");
            File.Delete(FilePath);
            Assert.That(SmtpPassword.ForSending(Email), Is.EqualTo(Canary));
            Assert.That(Email.ResolvedPassword!.ToString(), Is.EqualTo("SmtpPassword File: found"));
        }

        [Test]
        public void KnownSecrets_AreTheResolvedPassword_AndSmtpPassword()
        {
            Assert.That(SmtpPassword.KnownSecrets(null), Is.Empty);
            EmailConfig Email = Load(EmailText(null, Canary));
            Assert.That(SmtpPassword.KnownSecrets(Email), Is.EqualTo(new[] { Canary }));
            Environment_[SmtpPassword.EnvironmentVariable] = OtherCanary;
            SmtpPassword.ResolveOnce(Email);
            Assert.That(SmtpPassword.KnownSecrets(Email), Is.EqualTo(new[] { OtherCanary, Canary }));
            Assert.That(SmtpPassword.KnownSecrets(Load(EmailText(null))), Is.Empty);
        }

        // ---- the words: every problem says what to do, and none holds a value ----

        [Test]
        public void EveryProblem_HasItsOwnWords_WithoutAValue()
        {
            var Problems = new HashSet<string>();
            foreach (SmtpPasswordProblem Problem in Enum.GetValues<SmtpPasswordProblem>())
            {
                SmtpPasswordSource Source = Problem.ToString().StartsWith("Environment", StringComparison.Ordinal) ? SmtpPasswordSource.Environment : SmtpPasswordSource.File;
                List<string> Lines = SmtpPassword.Describe(SmtpPasswordResult.Failed(Source, Problem));
                Assert.That(Lines, Has.Count.EqualTo(3), Problem.ToString());
                Assert.That(Lines[0], Does.StartWith("the SMTP password can't be used: "));
                Assert.That(Lines[1], Is.Not.Empty, Problem + ": what to do");
                Assert.That(Lines[2], Is.EqualTo("Campertron never uses another password source when the one configured can't be used (the order is smtpPasswordFile, then CAMPERTRON_EMAIL_PASSWORD, then smtpPassword)."));
                Assert.That(Problems.Add(Lines[0]), Is.True, Problem + ": a problem of its own");
            }
            Assert.That(Enum.GetNames<SmtpPasswordProblem>(), Is.EqualTo(new[]
            {
                "FilePathEmpty", "FilePathNotAbsolute", "FileMissing", "FileIsFolder", "FileNotReadable", "FileEmpty", "FileTooLarge",
                "FileHasByteOrderMark", "FileNotUtf8", "FileHasNul", "FileOnlyLineBreak", "FileHasLineBreak", "EnvironmentEmpty", "EnvironmentHasLineBreak"
            }), "business-rules §27 documents each");
        }

        // ---- the validation of Email.yaml: the path's form only; the file is never opened by it ----

        [Test]
        public void TheValidation_ChecksThePathsForm_AndNeverOpensTheFile()
        {
            string Missing = Path.Join(Root, "not-there");
            foreach (bool EmailOutput in new[] { true, false })
            {
                ConfigSeverity Needed = EmailOutput ? ConfigSeverity.Error : ConfigSeverity.Warning;
                Assert.That(ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: " + Quoted(Missing)), EmailOutput).Select(D => (D.Code, D.Severity, D.Key)),
                    Is.EqualTo(new[] { (ConfigCode.PasswordFileSet, ConfigSeverity.Info, (string?)"smtpPasswordFile") }), "a file that is not there is email mode's problem, at its start");
                Assert.That(ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile:"), EmailOutput).Select(D => (D.Code, D.Severity, D.Key)),
                    Is.EqualTo(new[] { (ConfigCode.PasswordFilePathEmpty, Needed, (string?)"smtpPasswordFile") }));
                Assert.That(ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: secrets/pw"), EmailOutput).Select(D => (D.Code, D.Severity, D.Key)),
                    Is.EqualTo(new[] { (ConfigCode.PasswordFilePathNotAbsolute, Needed, (string?)"smtpPasswordFile") }));
                Assert.That(ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: " + Quoted(Missing), Canary), EmailOutput).Select(D => (D.Code, D.Severity, D.Key)),
                    Is.EqualTo(new[] { (ConfigCode.PasswordFileSet, ConfigSeverity.Info, (string?)"smtpPasswordFile"), (ConfigCode.PasswordNotUsed, ConfigSeverity.Warning, (string?)"smtpPassword") }));
                Assert.That(ConfigValidation.ValidateEmail(EmailText(null), EmailOutput).Select(D => D.Code), Is.EqualTo(new[] { ConfigCode.CredentialsMissing }),
                    "no source in the file: a warning, as before (a relay may accept mail without signing in)");
            }
            if (!OperatingSystem.IsWindows())
            {
                string Fifo = Path.Join(Root, "fifo");
                Assert.That(mkfifo(Fifo, System.Convert.ToUInt32("600", 8)), Is.EqualTo(0));
                Task<List<ConfigDiagnostic>> Validating = Task.Run(() => ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: " + Quoted(Fifo)), true));
                Assert.That(Validating.Wait(TimeSpan.FromSeconds(10)), Is.True, "the validation never opens the file");
            }
            foreach (ConfigDiagnostic D in ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: " + Quoted(Missing), Canary), true))
            {
                Assert.That(D.ToString(), Does.Not.Contain(Canary).And.Not.Contain(Root).And.Not.Contain("not-there"));
            }
        }

        // the variable belongs to the process that runs email mode: the validation of a file never reads it
        [Test]
        public void TheValidation_NeverReadsTheVariable()
        {
            int Asked = 0;
            SmtpPassword.GetEnvironment = Name => { Asked++; return OtherCanary; };
            ConfigValidation.ValidateEmail(EmailText(null), true);
            ConfigValidation.ValidateEmail(EmailText("smtpPasswordFile: /run/secrets/smtp_password"), false);
            Assert.That(Asked, Is.EqualTo(0));
        }

        // the start's sample Email.yaml keeps its bytes: smtpPasswordFile is written only when the file has it (D40)
        [Test]
        public void TheSampleEmailYaml_HasNoSmtpPasswordFile()
        {
            Yaml.EmailConfigGetConfig(Root);
            Assert.That(File.ReadAllText(Path.Join(Root, "Email.yaml")),
                Is.EqualTo("smtpServer: ''\nsmtpPort: 0\nsmtpUsername: ''\nsmtpPassword: ''\nsendToAddressList: []\nsendFromAddress: ''\n"));
        }
    }
}
