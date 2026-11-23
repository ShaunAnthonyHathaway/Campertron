using System.Text;
using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.WebAccess
{
    // The web UI's password as it is kept: a PBKDF2-SHA256 record in an owner-only file of the settings folder, never
    // the password (decisions D81 and D92; business-rules-settings §31)
    public class WebPasswordTests
    {
        private const string Password = "correct horse battery";
        private const int Low = WebPasswordHash.MinIterations;
        private string Folder = string.Empty;
        private string File_ => Path.Join(Folder, "Web.password");
        private StringWriter Console_ = null!;
        private TextWriter OriginalOut = null!, OriginalError = null!;

        [SetUp]
        public void CreateFolder()
        {
            Folder = Path.Join(Path.GetTempPath(), "CampertronWebPasswordTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            Console_ = new StringWriter();
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Console.SetOut(Console_);
            Console.SetError(Console_);
        }

        [TearDown]
        public void DeleteFolder()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Assert.That(Console_.ToString(), Is.Empty, "nothing about a password is ever written to the console");
            Console_.Dispose();
            Directory.Delete(Folder, true);
        }

        // ---- the record ----

        [Test]
        public void ARecord_IsPbkdf2Sha256_With600000Iterations_ARandomSalt_AndNoPassword()
        {
            Assert.That(WebPasswordHash.Iterations, Is.EqualTo(600_000), "OWASP's number for PBKDF2-HMAC-SHA256");
            string One = WebPasswordHash.Create(Password);
            string Two = WebPasswordHash.Create(Password);
            Assert.That(One, Does.Match(@"^\$pbkdf2-sha256\$i=600000\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}$"), "16 bytes of salt and 32 of hash, base64 without padding");
            Assert.That(Two, Is.Not.EqualTo(One), "a new salt every time");
            Assert.That(One.Split('$')[3], Is.Not.EqualTo(Two.Split('$')[3]));
            Assert.That(One, Does.Not.Contain(Password));
            WebPasswordHash.Record Parsed = WebPasswordHash.Parse(One)!;
            Assert.That((Parsed.Iterations, Parsed.Salt.Length, Parsed.Hash.Length), Is.EqualTo((600_000, 16, 32)));
            Assert.That(WebPasswordHash.Verify(Password, Parsed), Is.True);
            Assert.That(WebPasswordHash.Verify(Password, WebPasswordHash.Parse(Two)!), Is.True);
        }

        [Test]
        public void TheHash_IsTheStandardPbkdf2_OfThePasswordsUtf8Bytes()
        {
            //a record written by hand from the known answer of PBKDF2-HMAC-SHA256
            byte[] Salt = Encoding.ASCII.GetBytes("0123456789abcdef");
            byte[] Expected = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), Salt, Low,
                System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            string Line = "$pbkdf2-sha256$i=" + Low + "$" + Convert.ToBase64String(Salt).TrimEnd('=') + "$" + Convert.ToBase64String(Expected).TrimEnd('=');
            Assert.That(WebPasswordHash.Verify(Password, WebPasswordHash.Parse(Line)!), Is.True);
        }

        [TestCase("correct horse battery ")]
        [TestCase(" correct horse battery")]
        [TestCase("Correct horse battery")]
        [TestCase("correct horse batter")]
        [TestCase("")]
        [TestCase(null)]
        public void AnotherPassword_IsNotTheRecords(string? Other)
        {
            WebPasswordHash.Record Record = WebPasswordHash.Parse(WebPasswordHash.Create(Password, Low))!;
            Assert.That(WebPasswordHash.Verify(Other, Record), Is.False);
            Assert.That(WebPasswordHash.Verify(new string('x', WebPasswordHash.MaxLength + 1), Record), Is.False, "longer than a password can be: not even hashed");
            Assert.That(WebPasswordHash.Verify("lone surrogate " + (char)0xD800 + " here", Record), Is.False);
        }

        [Test]
        public void ThePassword_IsHashedInOneUnicodeForm()
        {
            //"é" as one character and as "e" with a combining accent; a full-width digit and the plain one (NFKC)
            WebPasswordHash.Record Record = WebPasswordHash.Parse(WebPasswordHash.Create("café password 1", Low))!;
            Assert.That(WebPasswordHash.Verify("café password １", Record), Is.True);
            Assert.That(WebPasswordHash.Verify("cafe password 1", Record), Is.False);
        }

        [TestCase("")]
        [TestCase("not a record")]
        [TestCase("pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha1$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$argon2id$v=19$m=65536,t=3,p=4$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=1$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=99999$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=10000001$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=0600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=-600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=6e5$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2Fs dHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdB$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA$extra")]
        [TestCase("$pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
        [TestCase(" $pbkdf2-sha256$i=600000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public void WhatIsNotExactlyARecord_IsNone_AndNoPasswordMatchesIt(string Line)
        {
            Assert.That(WebPasswordHash.Parse(Line), Is.Null);
            Assert.That(WebPasswordHash.Parse(new string('$', 300)), Is.Null);
            Assert.That(WebPasswordHash.Parse(null), Is.Null);
        }

        [Test]
        public void ARecordOfAnotherIterationCountInRange_IsStillRead()
        {
            string Line = WebPasswordHash.Create(Password, 250_000);
            Assert.That(Line, Does.StartWith("$pbkdf2-sha256$i=250000$"));
            Assert.That(WebPasswordHash.Verify(Password, WebPasswordHash.Parse(Line)!), Is.True, "a later version may change the count; old records stay usable");
        }

        [TestCase(null, null, "Enter a password.")]
        [TestCase("", "", "Enter a password.")]
        [TestCase("too short", "too short", "The password is too short: use at least 10 characters.")]
        [TestCase("          ", "          ", "The password can't be only spaces.")]
        [TestCase("long enough 1", "long enough 2", "The two passwords are not the same; enter the new password twice.")]
        [TestCase("long enough 1", null, "The two passwords are not the same; enter the new password twice.")]
        [TestCase("long enough 1", "long enough 1", null)]
        [TestCase("ten chars!", "ten chars!", null)]
        [TestCase("  spaces at both ends  ", "  spaces at both ends  ", null)]
        public void ANewPassword_IsTenTo128Characters_Twice_AndTheProblemNeverRepeatsIt(string? Password_, string? Repeated, string? Problem)
        {
            Assert.That(WebPasswordHash.CheckNew(Password_, Repeated), Is.EqualTo(Problem));
            string Long = new string('a', 128);
            Assert.That(WebPasswordHash.CheckNew(Long, Long), Is.Null);
            Assert.That(WebPasswordHash.CheckNew(Long + "a", Long + "a"), Is.EqualTo("The password is too long: use at most 128 characters."));
            //half of a character pair is no text (an attribute can't carry one, so it is built here)
            string Broken = "lone surrogate " + (char)0xD800;
            Assert.That(WebPasswordHash.CheckNew(Broken, Broken), Is.EqualTo("The password has characters that can't be used; choose another."));
        }

        // ---- the file ----

        [Test]
        public void TheFirstPassword_CreatesTheFile_OwnerOnly_AndNeverOverAnother()
        {
            var Passwords = new WebPasswordFile(Folder);
            Assert.That((Passwords.State(), Passwords.Exists(), Passwords.Fingerprint()), Is.EqualTo((WebPasswordState.Missing, false, ((long, DateTime)?)null)));
            Assert.That(Passwords.Verify(Password), Is.False, "no file: no password matches");
            Assert.That(Passwords.Create(Password, Low), Is.True);
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Web.password" }), "no temporary file is left");
            string Text = File.ReadAllText(File_);
            Assert.That(Text, Does.Match(@"^\$pbkdf2-sha256\$i=100000\$[A-Za-z0-9+/]{22}\$[A-Za-z0-9+/]{43}\n$"));
            Assert.That(Text, Does.Not.Contain(Password));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(File_), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Assert.That((Passwords.State(), Passwords.Exists()), Is.EqualTo((WebPasswordState.Usable, true)));
            Assert.That(Passwords.Fingerprint()!.Value.Length, Is.EqualTo(Text.Length));
            Assert.That(Passwords.Verify(Password), Is.True);
            Assert.That(Passwords.Verify("another password"), Is.False);

            Assert.That(Passwords.Create("a second password", Low), Is.False, "a password is set: a setup never replaces it");
            Assert.That(File.ReadAllText(File_), Is.EqualTo(Text));
            Assert.That(Passwords.Verify(Password), Is.True);
        }

        [Test]
        public void AChange_ReplacesTheFileInOneStep_WithoutABackup()
        {
            var Passwords = new WebPasswordFile(Folder);
            Passwords.Create(Password, Low);
            string Before = File.ReadAllText(File_);
            Passwords.Replace("a second password", Low);
            Assert.That(File.ReadAllText(File_), Is.Not.EqualTo(Before));
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Web.password" }), "no backup of the old hash, no temporary file");
            Assert.That((Passwords.Verify("a second password"), Passwords.Verify(Password)), Is.EqualTo((true, false)));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(File_), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
        }

        [TestCase("")]
        [TestCase("\n")]
        [TestCase("not a record\n")]
        [TestCase("correct horse battery\n")]
        [TestCase("$pbkdf2-sha256$i=100000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n\n")]
        [TestCase("$pbkdf2-sha256$i=100000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\nmore\n")]
        [TestCase("﻿$pbkdf2-sha256$i=100000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
        public void AFileThatIsNoRecord_IsUnusable_MatchesNothing_AndIsNeverCreatedOver(string Content)
        {
            byte[] Bytes = new UTF8Encoding(false).GetBytes(Content);
            File.WriteAllBytes(File_, Bytes);
            var Passwords = new WebPasswordFile(Folder);
            Assert.That(Passwords.State(), Is.EqualTo(WebPasswordState.Unusable));
            Assert.That(Passwords.Verify(Password), Is.False);
            Assert.That(Passwords.Verify(Content.Trim()), Is.False, "a file with the plain password is no password either");
            Assert.That(Passwords.Create(Password, Low), Is.False);
            Assert.That(File.ReadAllBytes(File_), Is.EqualTo(Bytes));
        }

        [Test]
        public void ARecordWithOneLineBreak_LfOrCrLf_IsRead_AndOneWithoutToo()
        {
            string Record = WebPasswordHash.Create(Password, Low);
            foreach (string End in new[] { "\n", "\r\n", "" })
            {
                File.WriteAllText(File_, Record + End);
                Assert.That(new WebPasswordFile(Folder).Verify(Password), Is.True);
            }
            File.WriteAllBytes(File_, new byte[] { 0xff, 0xfe, 0x24, 0x00 });
            Assert.That(new WebPasswordFile(Folder).State(), Is.EqualTo(WebPasswordState.Unusable), "not UTF-8");
            File.WriteAllText(File_, new string('x', WebPasswordFile.MaxFileBytes + 1));
            Assert.That(new WebPasswordFile(Folder).State(), Is.EqualTo(WebPasswordState.Unusable), "too large to be a record: not read");
        }

        [Test]
        public void AFolderOrALink_IsNeverAPasswordFile()
        {
            var Passwords = new WebPasswordFile(Folder);
            Directory.CreateDirectory(File_);
            Assert.That((Passwords.State(), Passwords.Exists(), Passwords.Verify(Password), Passwords.Create(Password, Low)), Is.EqualTo((WebPasswordState.Unusable, true, false, false)));
            Assert.That(() => Passwords.Replace(Password, Low), Throws.InstanceOf<IOException>());
            Directory.Delete(File_);
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            //a link to a real record elsewhere: never followed
            string Elsewhere = Path.Join(Folder, "elsewhere");
            File.WriteAllText(Elsewhere, WebPasswordHash.Create(Password, Low) + "\n");
            File.CreateSymbolicLink(File_, Elsewhere);
            Assert.That((Passwords.State(), Passwords.Exists(), Passwords.Verify(Password), Passwords.Fingerprint()), Is.EqualTo((WebPasswordState.Unusable, true, false, ((long, DateTime)?)null)));
            Assert.That(Passwords.Create("a second password", Low), Is.False);
            Assert.That(() => Passwords.Replace("a second password", Low), Throws.InstanceOf<IOException>());
            Assert.That(new WebPasswordFile(Folder).Verify(Password), Is.False);
            File.Delete(File_);
            //a link to nothing still has the name
            File.CreateSymbolicLink(File_, Path.Join(Folder, "missing"));
            Assert.That((Passwords.State(), Passwords.Exists(), Passwords.Create(Password, Low)), Is.EqualTo((WebPasswordState.Unusable, true, false)));
            Assert.That(File.Exists(Path.Join(Folder, "missing")), Is.False);
        }

        [Test]
        public void AFileOtherUsersCanRead_IsMadeOwnerOnlyBeforeItIsUsed()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            File.WriteAllText(File_, WebPasswordHash.Create(Password, Low) + "\n");
            File.SetUnixFileMode(File_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            var Passwords = new WebPasswordFile(Folder);
            Assert.That(Passwords.Verify(Password), Is.True);
            Assert.That(File.GetUnixFileMode(File_), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "the hash would let others guess the password offline");
        }

        [Test]
        public void AFolderThatCantBeWritten_ThrowsWithoutThePasswordOrTheFolder_AndChangesNothing()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root writes to every folder");
            }
            var Passwords = new WebPasswordFile(Folder);
            Passwords.Create(Password, Low);
            string Before = File.ReadAllText(File_);
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Exception? Thrown = Assert.Catch(() => Passwords.Replace("a second password", Low));
                Assert.That(Thrown, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
                Assert.That(Thrown!.ToString(), Does.Not.Contain("a second password"));
                Assert.That(File.ReadAllText(File_), Is.EqualTo(Before));
                Assert.That(Passwords.Verify(Password), Is.True, "the password is still the one it was");
            }
            finally
            {
                File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        [Test]
        public void TheFingerprint_ChangesWhenTheFileIsWrittenAgain_AndIsNeverItsContent()
        {
            var Passwords = new WebPasswordFile(Folder);
            Passwords.Create(Password, Low);
            (long Length, DateTime Written) First = Passwords.Fingerprint()!.Value;
            Assert.That(Passwords.Fingerprint(), Is.EqualTo(First), "the same file: the same fingerprint");
            File.SetLastWriteTimeUtc(File_, First.Written.AddSeconds(2));
            Assert.That(Passwords.Fingerprint(), Is.Not.EqualTo(First));
        }
    }
}
