using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The RIDB refresh's timestamp write (Yaml.GeneralConfigReplaceYaml, D40): only lastRidbDataRefresh changes, in the
    // file as it is when the write starts, through the comment-preserving editor and the safe writer; everything else
    // stays byte for byte, and a file that can't be edited safely is left exactly as it is. Exact-byte assertions; each
    // test has its own temporary settings folder.
    public class GeneralYamlTimestampTests
    {
        private string Directory_ = string.Empty;
        private string GeneralYaml => Path.Join(Directory_, "General.yaml");
        private static readonly DateTime NewTime = new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Utc);
        private const string Old = "2020-01-02T03:04:05.0000000Z";
        private const string New = "2026-11-15T01:02:03.0000000Z";
        private const string Commented =
            "# Campertron general settings\n" +
            "outputTo: Console   # where results go\n" +
            "\n" +
            "# RIDB data\n" +
            "refreshRidbDataDayInterval: 30\n" +
            "# written by Campertron after each refresh\n" +
            "lastRidbDataRefresh: " + Old + " # UTC\n" +
            "# after the timestamp\n" +
            "autoRefresh: false\n" +
            "\n" +
            "# end\n";
        private Action<string, string> OriginalStep = null!;
        private readonly List<string> Steps = new();
        private TextWriter? OriginalOut;

        [SetUp]
        public void Setup()
        {
            Directory_ = Path.Join(Path.GetTempPath(), "CampertronGeneralYamlTimestampTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Directory_);
            OriginalStep = ConfigFileReplace.Step;
            Steps.Clear();
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add(Name);
            OriginalOut = Console.Out;
            Console.SetOut(new StringWriter());
        }

        [TearDown]
        public void TearDown()
        {
            ConfigFileReplace.Step = OriginalStep;
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            Directory.Delete(Directory_, true);
        }

        // the in-memory settings the refresh holds: deliberately different from the file in everything but the timestamp,
        // which must not matter (only the timestamp is written)
        private static GeneralConfig Stale(DateTime? Time) =>
            new GeneralConfig { OutputTo = OutputType.HtmlFile, RefreshRidbDataDayInterval = 99, AutoRefresh = true, LastRidbDataRefresh = Time };

        private string Update(string Text) => Update(Encoding.UTF8.GetBytes(Text));

        private string Update(byte[] Bytes)
        {
            File.WriteAllBytes(GeneralYaml, Bytes);
            Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_);
            return File.ReadAllText(GeneralYaml);
        }

        private void AssertRefused<T>(byte[] Bytes, string MessagePart) where T : Exception
        {
            File.WriteAllBytes(GeneralYaml, Bytes);
            Assert.That(() => Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_), Throws.TypeOf<T>().With.Message.Contains(MessagePart));
            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(Bytes), "General.yaml is left exactly as it was");
            Assert.That(Directory.GetFiles(Directory_).Select(Path.GetFileName), Is.EqualTo(new[] { "General.yaml" }), "no temporary file and no backup");
        }

        // ---- only the timestamp ----

        [Test]
        public void AFileThatAlreadyHasTheTimestamp_IsNotWritten()
        {
            string Text = Commented.Replace(Old, New);
            File.WriteAllText(GeneralYaml, Text);
            DateTime Time = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(GeneralYaml, Time);

            Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_);

            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(Text));
            Assert.That(File.GetLastWriteTimeUtc(GeneralYaml), Is.EqualTo(Time));
            Assert.That(Steps, Is.Empty);
            Assert.That(File.Exists(GeneralYaml + ".bak"), Is.False);
        }

        [Test]
        public void OnlyTheTimestampsRangeChanges_AndEveryCommentBlankLineAndValueStays()
        {
            byte[] Before = Encoding.UTF8.GetBytes(Commented);

            string Result = Update(Before);

            Assert.That(Result, Is.EqualTo(Commented.Replace(Old, New)));
            int At = Commented.IndexOf(Old, StringComparison.Ordinal);
            Assert.That(Result[..At], Is.EqualTo(Commented[..At]));
            Assert.That(Result[(At + New.Length)..], Is.EqualTo(Commented[(At + Old.Length)..]));
            Assert.That(File.ReadAllBytes(GeneralYaml + ".bak"), Is.EqualTo(Before), "the rolling backup holds the previous bytes");
            GeneralConfig Loaded = Yaml.GeneralConfigGetConfig(Directory_);
            Assert.That((Loaded.OutputTo, Loaded.RefreshRidbDataDayInterval, Loaded.AutoRefresh, Loaded.LastRidbDataRefresh),
                Is.EqualTo((OutputType.Console, 30.0, false, (DateTime?)NewTime)), "the file's own settings, not the in-memory ones");
        }

        [Test]
        public void ACustomOrderAndIndentation_Stay()
        {
            const string Text = "  autoRefresh: true\n  lastRidbDataRefresh: " + Old + "\n  outputTo: Email   # mail\n";

            Assert.That(Update(Text), Is.EqualTo(Text.Replace(Old, New)));
        }

        [TestCase("'{0}'")]
        [TestCase("\"{0}\"")]
        [TestCase("{0}")]
        public void TheTimestampKeepsItsQuoting(string Format)
        {
            string Text = "outputTo: Console\nlastRidbDataRefresh: " + string.Format(Format, Old) + "  # quoted or not\n";

            Assert.That(Update(Text), Is.EqualTo(Text.Replace(Old, New)));
        }

        [TestCase("\n")]
        [TestCase("\r\n")]
        [TestCase("\r")]
        public void LineBreaks_Stay(string Eol)
        {
            string Text = Commented.Replace("\n", Eol);

            Assert.That(Update(Text), Is.EqualTo(Text.Replace(Old, New)));
        }

        [Test]
        public void MixedLineBreaks_Stay_AndAnInsertedLineGetsTheMostFrequentOne()
        {
            const string Text = "outputTo: Console\r\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\r\n";

            Assert.That(Update(Text), Is.EqualTo(Text + "lastRidbDataRefresh: " + New + "\r\n"));
        }

        [Test]
        public void AMissingFinalLineBreak_StaysMissing()
        {
            Assert.That(Update("outputTo: Console\nlastRidbDataRefresh: " + Old), Is.EqualTo("outputTo: Console\nlastRidbDataRefresh: " + New));
            Assert.That(Update("outputTo: Console\nautoRefresh: false"), Is.EqualTo("outputTo: Console\nautoRefresh: false\nlastRidbDataRefresh: " + New));
        }

        [Test]
        public void AByteOrderMark_Unicode_AndAstralCharacters_Stay()
        {
            string Text = "# r\u00E9glages \u6771\u4EAC \U0001F3D5\noutputTo: Console\nlastRidbDataRefresh: " + Old + " # \U0001F600\n";
            byte[] Bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Text)).ToArray();

            Update(Bytes);

            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(Text.Replace(Old, New))).ToArray()));
        }

        [Test]
        public void AMissingTimestamp_IsAddedAfterTheLastSetting()
        {
            const string Text = "outputTo: Console # c\nautoRefresh: false\n# trailing comment\n";

            Assert.That(Update(Text), Is.EqualTo("outputTo: Console # c\nautoRefresh: false\nlastRidbDataRefresh: " + New + "\n# trailing comment\n"));
        }

        // ---- the file as it is now ----

        [Test]
        public void AnEditMadeBeforeTheWrite_IsKept_AndTheInMemorySettingsAreNotWritten()
        {
            // the process loaded General.yaml at its start; the user then changed it; the timestamp write keeps that change
            string Edited = Commented.Replace("autoRefresh: false", "autoRefresh: true # I turned this on") + "# another note\n";

            Assert.That(Update(Edited), Is.EqualTo(Edited.Replace(Old, New)));
        }

        [Test]
        public void AnEditMadeDuringTheWrite_IsNotOverwritten()
        {
            const string External = "outputTo: Email\nlastRidbDataRefresh: 2021-01-01T00:00:00.0000000Z\n";
            File.WriteAllText(GeneralYaml, Commented);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-precondition")
                {
                    File.WriteAllText(GeneralYaml, External);
                }
            };

            Assert.That(() => Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_), Throws.TypeOf<IOException>().With.Message.Contains("changed by another program"));

            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(External));
            Assert.That(Directory.GetFiles(Directory_).Where(F => F.EndsWith(".tmp")), Is.Empty);
        }

        [Test]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void ALinkedGeneralYaml_StaysALink_WithItsPermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix links and permissions");
            }
            string Real = Path.Join(Directory_, "elsewhere");
            Directory.CreateDirectory(Real);
            File.WriteAllText(Path.Join(Real, "General.yaml"), Commented);
            File.SetUnixFileMode(Path.Join(Real, "General.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.CreateSymbolicLink(GeneralYaml, Path.Join("elsewhere", "General.yaml"));

            Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_);

            Assert.That(new FileInfo(GeneralYaml).LinkTarget, Is.EqualTo(Path.Join("elsewhere", "General.yaml")));
            Assert.That(File.ReadAllText(Path.Join(Real, "General.yaml")), Is.EqualTo(Commented.Replace(Old, New)));
            Assert.That(File.GetUnixFileMode(Path.Join(Real, "General.yaml")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.ReadAllText(Path.Join(Real, "General.yaml.bak")), Is.EqualTo(Commented));
        }

        // ---- left exactly as it is ----

        [Test]
        public void AMalformedFile_IsLeftAlone()
        {
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: [Console\nlastRidbDataRefresh: " + Old + "\n"), "could not be read (line");
        }

        [Test]
        public void AFileTheEditorDoesNotSupport_IsLeftAlone()
        {
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\nlastRidbDataRefresh: &t " + Old + "\n"), "FieldLocked");
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\nlastRidbDataRefresh: !!timestamp " + Old + "\n"), "FieldLocked");
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\nautoRefresh: false\nautoRefresh: true\n"), "DuplicateKey");
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\n---\nautoRefresh: true\n"), "could not be read (line");
        }

        [Test]
        public void AFileWithKeysTheLoaderRejects_IsLeftAlone()
        {
            // Campertron's loader refuses unknown keys (D20), so the file's own settings can't be established
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\ntheme: dark\nlastRidbDataRefresh: " + Old + "\n"), "could not be read (line");
        }

        [Test]
        public void InvalidUtf8_AndUtf16_AreLeftAlone()
        {
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("outputTo: Console\n# caf").Append((byte)0xE9).Concat(Encoding.UTF8.GetBytes("\n")).ToArray(), "not valid UTF-8");
            AssertRefused<InvalidDataException>(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Commented)).ToArray(), "not UTF-8");
            AssertRefused<InvalidDataException>(Encoding.Unicode.GetBytes(Commented), "not UTF-8");
        }

        [Test]
        public void AnEmptyFile_IsLeftAlone()
        {
            AssertRefused<InvalidDataException>(Array.Empty<byte>(), "is empty");
            AssertRefused<InvalidDataException>(Encoding.UTF8.GetBytes("# only a comment\n"), "has no settings");
        }

        [Test]
        public void AMissingGeneralYaml_IsNotRecreatedFromTheInMemorySettings()
        {
            // before D40 the writer wrote the in-memory settings as a new General.yaml; now nothing is written (the refresh
            // warns, and the next start creates the sample)
            Assert.That(() => Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_), Throws.TypeOf<FileNotFoundException>().With.Message.Contains("does not exist"));
            Assert.That(Directory.GetFileSystemEntries(Directory_), Is.Empty);
            Assert.That(Steps, Is.Empty);
        }

        [Test]
        public void AModelMismatch_OrAWrongCandidate_StopsTheWriterBeforeAnythingIsWritten()
        {
            // the writer's check is YamlDocument.VerifyCandidate against the file's settings with the new timestamp
            File.WriteAllText(GeneralYaml, Commented);
            ConfigFileReplace.SettingsFile Current = ConfigFileReplace.Read(GeneralYaml);
            YamlDocumentEdit[] Edits = { YamlDocumentEdit.SetScalar("lastRidbDataRefresh", NewTime) };
            var Right = new GeneralConfig { OutputTo = OutputType.Console, RefreshRidbDataDayInterval = 30, AutoRefresh = false, LastRidbDataRefresh = NewTime };
            var Wrong = new GeneralConfig { OutputTo = OutputType.Console, RefreshRidbDataDayInterval = 30, AutoRefresh = true, LastRidbDataRefresh = NewTime };
            string Candidate = Commented.Replace(Old, New);
            Func<string, string?> Check(GeneralConfig Intended) => Text => YamlDocument.VerifyCandidate(Current.Text, Edits, Intended, Text).Succeeded ? null : "refused";

            Assert.That(() => ConfigFileReplace.Replace(Current, Candidate, Check(Wrong)), Throws.TypeOf<InvalidDataException>());
            Assert.That(() => ConfigFileReplace.Replace(Current, Candidate.Replace("# end", "# changed"), Check(Right)), Throws.TypeOf<InvalidDataException>());
            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(Commented));
            Assert.That(Steps, Is.Empty, "refused before any file was written");

            ConfigFileReplace.Replace(Current, Candidate, Check(Right));
            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(Candidate));
        }

        [Test]
        public void NoFailureMessage_QuotesTheFile()
        {
            foreach (string Text in new[] { "outputTo: [S3cr3t\n", "outputTo: Console\nS3cr3t: x\n", "outputTo: Console\nlastRidbDataRefresh: &S3cr3t " + Old + "\n" })
            {
                File.WriteAllText(GeneralYaml, Text);
                try
                {
                    Yaml.GeneralConfigReplaceYaml(Stale(NewTime), Directory_);
                    Assert.Fail("refused expected");
                }
                catch (InvalidDataException ex)
                {
                    Assert.That(ex.Message, Does.Not.Contain("S3cr3t"));
                }
            }
        }
    }
}
