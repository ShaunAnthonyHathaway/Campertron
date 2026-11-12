using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ConfigFileReplace and Yaml.GeneralConfigReplaceYaml: the interruption-safe General.yaml write used for the RIDB
    // refresh timestamp (TD-37). Every failure before the replacement must leave the existing General.yaml byte for
    // byte unchanged and no temporary file behind. Each test uses its own temporary directory.
    public class ConfigFileReplaceTests
    {
        private string Directory_ = string.Empty;
        private string GeneralYaml => Path.Join(Directory_, "General.yaml");
        private static readonly DateTime OldTimestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        private static readonly DateTime NewTimestamp = new DateTime(2026, 11, 11, 12, 13, 14, DateTimeKind.Utc);
        private StringWriter Output = null!;
        private TextWriter? OriginalOut;

        [SetUp]
        public void Setup()
        {
            Directory_ = Path.Join(Path.GetTempPath(), "CampertronConfigFileReplaceTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Directory_);
            Yaml.GeneralConfigConvertToYaml(Config(OldTimestamp), "General", Directory_);
            Output = new StringWriter();
            OriginalOut = Console.Out;
            Console.SetOut(Output);
        }

        [TearDown]
        public void TearDown()
        {
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            Output.Dispose();
            SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(Directory_, true);
        }

        private static GeneralConfig Config(DateTime Timestamp) => new GeneralConfig
        {
            OutputTo = OutputType.HtmlFile,
            RefreshRidbDataDayInterval = 12.5,
            LastRidbDataRefresh = Timestamp,
            AutoRefresh = true
        };

        // ---- success ----

        [Test]
        public void GeneralConfigReplaceYaml_WritesExactlyWhatTheExistingWriterWrites_AndKeepsUnrelatedValues()
        {
            string Reference = Path.Join(Directory_, "reference");
            Directory.CreateDirectory(Reference);
            Yaml.GeneralConfigConvertToYaml(Config(NewTimestamp), "General", Reference);

            Yaml.GeneralConfigReplaceYaml(Config(NewTimestamp), Directory_);

            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(File.ReadAllBytes(Path.Join(Reference, "General.yaml"))));
            GeneralConfig Loaded = Yaml.GeneralConfigGetConfig(Directory_);
            Assert.That(Loaded.LastRidbDataRefresh, Is.EqualTo(NewTimestamp));
            Assert.That(Loaded.OutputTo, Is.EqualTo(OutputType.HtmlFile));
            Assert.That(Loaded.RefreshRidbDataDayInterval, Is.EqualTo(12.5));
            Assert.That(Loaded.AutoRefresh, Is.True);
            Assert.That(TemporaryFiles(), Is.Empty);
        }

        // ---- failures before the replacement: General.yaml unchanged, no temporary file left ----

        [Test]
        public void Replace_SerializationFailsBeforeAnythingIsWritten_LeavesTheFileUnchanged()
        {
            byte[] Before = File.ReadAllBytes(GeneralYaml);

            Assert.That(() => ConfigFileReplace.Replace(GeneralYaml, Writer => throw new InvalidOperationException("injected serialization failure"), Text => true),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("injected serialization failure"));

            AssertUnchanged(Before);
        }

        [Test]
        public void Replace_TemporaryFileCannotBeCreated_LeavesTheFileUnchanged()
        {
            RequireUnixPermissions();
            byte[] Before = File.ReadAllBytes(GeneralYaml);
            SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.That(() => Yaml.GeneralConfigReplaceYaml(Config(NewTimestamp), Directory_), Throws.TypeOf<UnauthorizedAccessException>());
            }
            finally
            {
                SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            AssertUnchanged(Before);
        }

        [Test]
        public void Replace_TemporaryFileWriteFails_LeavesTheFileUnchanged_AndRemovesThePartialTemporaryFile()
        {
            byte[] Before = File.ReadAllBytes(GeneralYaml);

            Assert.That(() => ConfigFileReplace.Replace(GeneralYaml, Writer =>
            {
                Writer.Write("outputTo: Con");
                Writer.Flush();
                throw new IOException("injected write failure");
            }, Text => true), Throws.TypeOf<IOException>().With.Message.EqualTo("injected write failure"));

            AssertUnchanged(Before);
        }

        [Test]
        public void Replace_ValidationFails_LeavesTheFileUnchanged()
        {
            byte[] Before = File.ReadAllBytes(GeneralYaml);
            string? Seen = null;

            Assert.That(() => ConfigFileReplace.Replace(GeneralYaml, Writer => Writer.Write("outputTo: [broken"), Text => { Seen = Text; return false; }),
                Throws.TypeOf<InvalidDataException>());

            Assert.That(Seen, Is.EqualTo("outputTo: [broken"), "validation sees the closed temporary file's content");
            AssertUnchanged(Before);
        }

        [Test]
        public void Replace_ReplacementFails_LeavesTheFileUnchanged()
        {
            byte[] Before = File.ReadAllBytes(GeneralYaml);

            Assert.That(() => ConfigFileReplace.Replace(GeneralYaml, Writer => Writer.Write("outputTo: Console\n"), Text => true,
                (From, To) => throw new IOException("injected replacement failure")), Throws.TypeOf<IOException>().With.Message.EqualTo("injected replacement failure"));

            AssertUnchanged(Before);
        }

        [Test]
        public void Replace_DestinationIsADirectory_RealFileMoveFails_AndNothingIsLeft()
        {
            // a real (not injected) replacement failure: File.Move can't replace a directory
            string Target = Path.Join(Directory_, "Blocked.yaml");
            Directory.CreateDirectory(Target);
            File.WriteAllText(Path.Join(Target, "keep.txt"), "keep");

            Assert.That(() => ConfigFileReplace.Replace(Target, Writer => Writer.Write("x: 1\n"), Text => true), Throws.InstanceOf<IOException>());

            Assert.That(File.ReadAllText(Path.Join(Target, "keep.txt")), Is.EqualTo("keep"));
            Assert.That(Directory.GetFiles(Directory_).Where(f => ConfigFileReplace.IsTemporaryName("Blocked.yaml", Path.GetFileName(f))), Is.Empty);
        }

        [Test]
        public void Replace_CleanupFailure_DoesNotReplaceTheOriginalException()
        {
            RequireUnixPermissions();
            byte[] Before = File.ReadAllBytes(GeneralYaml);
            Exception? Thrown = null;
            try
            {
                // the validation fails, and the directory is made read-only first, so removing the temporary file fails too
                ConfigFileReplace.Replace(GeneralYaml, Writer => Writer.Write("outputTo: Console\n"), Text =>
                {
                    SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    return false;
                });
            }
            catch (Exception ex)
            {
                Thrown = ex;
            }
            finally
            {
                SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.That(Thrown, Is.TypeOf<InvalidDataException>(), "the validation failure, not the cleanup failure");
            Assert.That(Output.ToString(), Does.Contain("Could not remove the temporary file"));
            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(Before));
            Assert.That(TemporaryFiles(), Has.Count.EqualTo(1), "the temporary file that could not be removed");
        }

        // ---- stale temporary files ----

        [Test]
        public void StaleTemporaryFiles_AreNeverReadAsGeneralYaml_AndOnlyExactNamesAreRemoved()
        {
            byte[] Before = File.ReadAllBytes(GeneralYaml);
            string Id = Guid.NewGuid().ToString("N");
            string Stale = Path.Join(Directory_, "General.yaml." + Id + ".tmp");
            string StaleEmpty = Path.Join(Directory_, "General.yaml." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(Stale, "outputTo: Email\nlastRidbDataRefresh: 2099-01-01T00:00:00Z\n");
            File.WriteAllText(StaleEmpty, "");
            string[] LookAlikes = { "General.yaml.tmp", "General.yaml." + Id.ToUpperInvariant() + ".tmp", "Email.yaml." + Id + ".tmp", "General.yaml." + Id, "XGeneral.yaml." + Id + ".tmp" };
            foreach (string Name in LookAlikes)
            {
                File.WriteAllText(Path.Join(Directory_, Name), "keep");
            }

            // the loaders read only General.yaml; search configs are *.yaml files, which the temporary names are not
            Assert.That(Yaml.GeneralConfigGetConfig(Directory_).LastRidbDataRefresh, Is.EqualTo(OldTimestamp));
            Assert.That(Directory.GetFiles(Directory_, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml" }));

            ConfigFileReplace.RemoveStaleTemporaryFiles(Directory_, "General.yaml");

            Assert.That(File.Exists(Stale), Is.False);
            Assert.That(File.Exists(StaleEmpty), Is.False);
            foreach (string Name in LookAlikes)
            {
                Assert.That(File.Exists(Path.Join(Directory_, Name)), Is.True, Name);
            }
            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(Before));
        }

        [Test]
        public void IsTemporaryName_AcceptsOnlyTheExactConvention()
        {
            string Id = Guid.NewGuid().ToString("N");
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", ConfigFileReplace.TemporaryName("General.yaml")), Is.True);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml." + Id + ".tmp"), Is.True);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml." + Id.ToUpperInvariant() + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml." + Id.Substring(1) + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml.tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "Email.yaml." + Id + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml"), Is.False);
        }

        // ---- helpers ----

        private void AssertUnchanged(byte[] Before)
        {
            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(Before), "General.yaml bytes");
            Assert.That(Yaml.GeneralConfigGetConfig(Directory_).LastRidbDataRefresh, Is.EqualTo(OldTimestamp), "General.yaml still loads");
            Assert.That(TemporaryFiles(), Is.Empty, "no temporary file left");
        }

        private List<string> TemporaryFiles() =>
            Directory.GetFiles(Directory_).Select(f => Path.GetFileName(f)!).Where(n => ConfigFileReplace.IsTemporaryName("General.yaml", n)).ToList();

        private static void SetMode(string Path_, UnixFileMode Mode)
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(Path_))
            {
                File.SetUnixFileMode(Path_, Mode);
            }
        }

        private static void RequireUnixPermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("uses Unix file permissions");
            }
            if (Environment.IsPrivilegedProcess)
            {
                Assert.Ignore("permission checks do not apply to a privileged process");
            }
        }
    }
}
