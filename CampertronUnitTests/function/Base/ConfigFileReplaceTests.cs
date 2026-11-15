using System.Diagnostics;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ConfigFileReplace: the safe writer of Campertron's settings files (TD-37, D40). Reading is strict UTF-8; a replacement
    // goes through a same-directory temporary file, a rolling backup and a check that the file did not change, and keeps
    // the file's permissions and links; a creation never touches an existing name. Every failure before the rename must
    // leave the file byte for byte as it was and no temporary file behind. Each test uses its own temporary directory;
    // the write's steps are observed (and failures injected) through ConfigFileReplace.Step.
    public class ConfigFileReplaceTests
    {
        private string Directory_ = string.Empty;
        private string Settings => Path.Join(Directory_, "Settings.yaml");
        private string Backup => Settings + ".bak";
        private const string Original = "# my settings\nsmtpServer: 'mail.example.org' # host\nsmtpPassword: S3cr3t-Pa55\n";
        private const string Edited = "# my settings\nsmtpServer: 'smtp.example.org' # host\nsmtpPassword: S3cr3t-Pa55\n";
        private StringWriter Output = null!;
        private TextWriter? OriginalOut;
        private Action<string, string> OriginalStep = null!;
        private readonly List<(string Name, string Path)> Steps = new();

        [SetUp]
        public void Setup()
        {
            Directory_ = Path.Join(Path.GetTempPath(), "CampertronConfigFileReplaceTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Directory_);
            File.WriteAllText(Settings, Original);
            Output = new StringWriter();
            OriginalOut = Console.Out;
            Console.SetOut(Output);
            OriginalStep = ConfigFileReplace.Step;
            Steps.Clear();
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add((Name, Path_));
        }

        [TearDown]
        public void TearDown()
        {
            ConfigFileReplace.Step = OriginalStep;
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            Output.Dispose();
            SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(Directory_, true);
        }

        private static string? Accept(string Text) => null;

        private void Replace(string NewText, Func<string, string?>? Verify = null) =>
            ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), NewText, Verify ?? Accept);

        private void FailAt(string StepName, Action? Action = null)
        {
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                Steps.Add((Name, Path_));
                if (Name == StepName)
                {
                    if (Action == null)
                    {
                        throw new IOException("injected failure at " + StepName);
                    }
                    Action();
                }
            };
        }

        // ---- strict UTF-8 ----

        [Test]
        public void Read_DecodesUtf8_AndAByteOrderMarkBecomesALeadingFeff()
        {
            ConfigFileReplace.SettingsFile Plain = ConfigFileReplace.Read(Settings);
            Assert.That(Plain.Text, Is.EqualTo(Original));
            Assert.That(Plain.Bytes, Is.EqualTo(Encoding.UTF8.GetBytes(Original)));

            byte[] WithBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("a: \U0001F600\r\n")).ToArray();
            File.WriteAllBytes(Settings, WithBom);
            ConfigFileReplace.SettingsFile Bom = ConfigFileReplace.Read(Settings);
            Assert.That(Bom.Text, Is.EqualTo((char)0xFEFF + "a: \U0001F600\r\n"));
            Assert.That(ConfigFileReplace.Encode(Bom.Text, "x"), Is.EqualTo(WithBom), "the text encodes back to the same bytes, BOM included");
        }

        [Test]
        public void Read_InvalidUtf8_IsRefused_NeverReplacedWithFffd()
        {
            byte[] Invalid = { 0x61, 0x3A, 0x20, 0xC3, 0x28, 0x0A };
            File.WriteAllBytes(Settings, Invalid);

            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<InvalidDataException>().With.Message.Contains("not valid UTF-8"));
            Assert.That(File.ReadAllBytes(Settings), Is.EqualTo(Invalid));
        }

        [Test]
        public void Read_Utf16AndUtf32_AreRefused_NotConverted()
        {
            foreach (byte[] Bytes in new[]
            {
                Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("a: 1\n")).ToArray(),
                Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("a: 1\n")).ToArray(),
                Encoding.Unicode.GetBytes("a: 1\n"),
                new UTF32Encoding(true, true).GetPreamble().Concat(new UTF32Encoding(true, true).GetBytes("a: 1\n")).ToArray()
            })
            {
                File.WriteAllBytes(Settings, Bytes);
                Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<InvalidDataException>().With.Message.Contains("not UTF-8"));
                Assert.That(File.ReadAllBytes(Settings), Is.EqualTo(Bytes));
            }
        }

        // ---- a replacement ----

        [Test]
        public void Replace_GoesThroughASameDirectoryTemporaryFile_InOrder_AndLeavesTheExactBytesAndABackup()
        {
            string[] Before = Directory.GetFiles(Directory_);

            Replace(Edited);

            Assert.That(File.ReadAllBytes(Settings), Is.EqualTo(Encoding.UTF8.GetBytes(Edited)));
            Assert.That(File.ReadAllBytes(Backup), Is.EqualTo(Encoding.UTF8.GetBytes(Original)), "the backup is exactly the previous content");
            Assert.That(Steps.Select(S => S.Name), Is.EqualTo(new[]
            {
                "temporary-writing", "temporary-flushed", "temporary-written", "temporary-verified",
                "temporary-writing", "temporary-flushed", "backup-written", "backup-installed",
                "before-precondition", "before-replace", "after-replace"
            }));
            string Temporary = Steps[0].Path;
            Assert.That(Path.GetDirectoryName(Temporary), Is.EqualTo(Directory_), "the temporary file is in the file's directory");
            Assert.That(ConfigFileReplace.IsTemporaryName("Settings.yaml", Path.GetFileName(Temporary)), Is.True);
            Assert.That(Before, Does.Not.Contain(Temporary), "a new name, created new");
            Assert.That(ConfigFileReplace.IsTemporaryName("Settings.yaml.bak", Path.GetFileName(Steps[4].Path)), Is.True);
            Assert.That(Steps.Single(S => S.Name == "before-replace").Path, Is.EqualTo(Temporary), "the checked temporary file is the one renamed");
            Assert.That(Leftovers(), Is.Empty);
        }

        [Test]
        public void TemporaryNames_AreUnique_AndOnlyTheExactConventionCounts()
        {
            var Names = Enumerable.Range(0, 1000).Select(_ => ConfigFileReplace.TemporaryName("General.yaml")).ToList();
            Assert.That(Names.Distinct().Count(), Is.EqualTo(1000));
            string Id = Guid.NewGuid().ToString("N");
            Assert.That(Names.All(N => ConfigFileReplace.IsTemporaryName("General.yaml", N)), Is.True);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml." + Id.ToUpperInvariant() + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml." + Id.Substring(1) + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml.tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "Email.yaml." + Id + ".tmp"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml"), Is.False);
            Assert.That(ConfigFileReplace.IsTemporaryName("General.yaml", "General.yaml.bak"), Is.False);
        }

        [TestCase("temporary-writing")]
        [TestCase("temporary-flushed")]
        [TestCase("temporary-written")]
        [TestCase("temporary-verified")]
        [TestCase("backup-written")]
        [TestCase("backup-installed")]
        [TestCase("before-precondition")]
        [TestCase("before-replace")]
        public void AFailureBeforeTheRename_LeavesTheFileUnchanged_AndNoTemporaryFile(string StepName)
        {
            File.WriteAllText(Backup, "an older backup\n");
            FailAt(StepName);

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.EqualTo("injected failure at " + StepName));

            AssertUnchanged();
            bool BackupReplaced = StepName == "backup-installed" || StepName == "before-precondition" || StepName == "before-replace";
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(BackupReplaced ? Original : "an older backup\n"), "the backup is only ever a whole file");
        }

        [Test]
        public void TheTemporaryFile_IsReadBack_AndAWrongCopyIsNeverRenamed()
        {
            FailAt("temporary-written", () => File.AppendAllText(Steps.Last().Path, "x"));

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("did not read back as written"));

            AssertUnchanged();
        }

        [Test]
        public void TheCheck_IsAppliedBeforeWriting_ToTheTemporaryFile_AndToTheResult()
        {
            var Seen = new List<string>();
            Replace(Edited, Text => { Seen.Add(Text); return null; });
            Assert.That(Seen, Is.EqualTo(new[] { Edited, Edited, Edited }));

            File.WriteAllText(Settings, Original);
            Steps.Clear();
            Assert.That(() => Replace(Edited, Text => "not acceptable"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("not acceptable"));
            Assert.That(Steps, Is.Empty, "refused before anything is written");
            AssertUnchanged();

            int Calls = 0;
            Assert.That(() => Replace(Edited, Text => ++Calls == 2 ? "temporary copy refused" : null), Throws.TypeOf<InvalidDataException>());
            AssertUnchanged();
        }

        [Test]
        public void AChangeByAnotherProgram_IsNotOverwritten()
        {
            const string External = "smtpServer: changed.example.org\n";
            FailAt("before-precondition", () => File.WriteAllText(Settings, External));

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(External));
            Assert.That(Leftovers(), Is.Empty);
        }

        [Test]
        public void AChangeNoticedBeforeTheBackup_LeavesTheBackupAlone()
        {
            FailAt("temporary-verified", () => File.AppendAllText(Settings, "# added\n"));

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));

            Assert.That(File.Exists(Backup), Is.False);
            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original + "# added\n"));
        }

        [Test]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void APermissionChangeByAnotherProgram_IsAChangeToo()
        {
            RequireUnixPermissions();
            File.SetUnixFileMode(Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            FailAt("before-precondition", () => File.SetUnixFileMode(Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite));

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original));
        }

        [Test]
        public void AFinalReadBackMismatch_IsReported()
        {
            FailAt("after-replace", () => File.WriteAllText(Settings, "smtpServer: someone else\n"));

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("reading it back did not give what was written"));
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Original), "the previous content is still in the backup");
        }

        [Test]
        public void ABackupThatCantBeWritten_StopsTheReplacement()
        {
            Directory.CreateDirectory(Backup);
            File.WriteAllText(Path.Join(Backup, "keep.txt"), "keep");

            Assert.That(() => Replace(Edited), Throws.InstanceOf<IOException>());

            AssertUnchanged();
            Assert.That(File.ReadAllText(Path.Join(Backup, "keep.txt")), Is.EqualTo("keep"));
        }

        [Test]
        public void TheBackup_IsReplacedByRename_NeverWrittenThroughALink()
        {
            RequireUnixLinks();
            string Outside = Path.Join(Directory_, "outside.txt");
            File.WriteAllText(Outside, "not a backup");
            File.CreateSymbolicLink(Backup, Outside);

            Replace(Edited);

            Assert.That(new FileInfo(Backup).LinkTarget, Is.Null, "the old backup name was replaced as a whole");
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Original));
            Assert.That(File.ReadAllText(Outside), Is.EqualTo("not a backup"));
        }

        // ---- permissions ----

        [TestCase(0b110_000_000)]
        [TestCase(0b110_100_000)]
        [TestCase(0b110_100_100)]
        [TestCase(0b100_000_000)]
        [TestCase(0b100_100_100)]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void TheFileAndItsBackup_KeepThePermissions_AndTheTemporaryFileIsOwnerOnlyWhileItIsWritten(int Mode)
        {
            RequireUnixPermissions();
            var Expected = (UnixFileMode)Mode;
            File.SetUnixFileMode(Settings, Expected);
            var WhileWriting = new List<UnixFileMode>();
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                Steps.Add((Name, Path_));
                if (Name == "temporary-writing")
                {
                    WhileWriting.Add(File.GetUnixFileMode(Path_));
                }
            };

            Replace(Edited);

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Edited));
            Assert.That(File.GetUnixFileMode(Settings), Is.EqualTo(Expected));
            Assert.That(File.GetUnixFileMode(Backup), Is.EqualTo(Expected), "the backup is never more permissive than the file");
            Assert.That(WhileWriting, Is.EqualTo(new[] { UnixFileMode.UserRead | UnixFileMode.UserWrite, UnixFileMode.UserRead | UnixFileMode.UserWrite }));
        }

        [Test]
        public void ADirectoryWithoutWritePermission_LeavesEverythingAsItWas()
        {
            RequireUnixPermissions();
            SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.That(() => Replace(Edited), Throws.TypeOf<UnauthorizedAccessException>());

            SetMode(Directory_, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            AssertUnchanged();
        }

        [Test]
        [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
        public void AFileThatCantBeRead_IsLeftAlone()
        {
            RequireUnixPermissions();
            File.SetUnixFileMode(Settings, UnixFileMode.UserWrite);

            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<UnauthorizedAccessException>());

            File.SetUnixFileMode(Settings, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original));
        }

        // ---- links ----

        [Test]
        public void ASymbolicLink_StaysALink_AndItsTargetIsUpdatedInTheTargetsDirectory()
        {
            RequireUnixLinks();
            string Real = Path.Join(Directory_, "real");
            Directory.CreateDirectory(Real);
            string Target = Path.Join(Real, "Target.yaml");
            File.WriteAllText(Target, Original);
            File.Delete(Settings);
            File.CreateSymbolicLink(Path.Join(Directory_, "Middle.yaml"), Path.Join("real", "Target.yaml"));
            File.CreateSymbolicLink(Settings, "Middle.yaml");

            Replace(Edited);

            Assert.That(new FileInfo(Settings).LinkTarget, Is.EqualTo("Middle.yaml"));
            Assert.That(new FileInfo(Path.Join(Directory_, "Middle.yaml")).LinkTarget, Is.EqualTo(Path.Join("real", "Target.yaml")));
            Assert.That(File.ReadAllText(Target), Is.EqualTo(Edited));
            Assert.That(File.ReadAllText(Path.Join(Real, "Target.yaml.bak")), Is.EqualTo(Original));
            Assert.That(File.Exists(Backup), Is.False, "no backup next to the link");
            Assert.That(Steps.Where(S => S.Name.StartsWith("temporary") || S.Name.StartsWith("backup")).Select(S => Path.GetDirectoryName(S.Path)).Distinct(), Is.EqualTo(new[] { Real }));
            Assert.That(Directory.GetFiles(Directory_).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Settings.yaml", "Middle.yaml" }));
        }

        [Test]
        public void ADanglingLink_ACycle_ADirectory_AndANonRegularFile_AreRefused()
        {
            RequireUnixLinks();
            File.Delete(Settings);
            File.CreateSymbolicLink(Settings, "missing.yaml");
            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<FileNotFoundException>().With.Message.Contains("does not exist"));

            File.Delete(Settings);
            File.CreateSymbolicLink(Settings, "Loop.yaml");
            File.CreateSymbolicLink(Path.Join(Directory_, "Loop.yaml"), "Settings.yaml");
            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<IOException>().With.Message.Contains("cycle"));

            File.Delete(Settings);
            Directory.CreateDirectory(Path.Join(Directory_, "dir"));
            File.CreateSymbolicLink(Settings, "dir");
            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<IOException>().With.Message.Contains("is a directory"));

            File.Delete(Settings);
            string Fifo = Path.Join(Directory_, "fifo");
            using (Process? Make = Process.Start("mkfifo", Fifo))
            {
                Make!.WaitForExit();
            }
            File.CreateSymbolicLink(Settings, "fifo");
            var Reading = Task.Run(() => ConfigFileReplace.Read(Settings));
            Assert.That(Task.WhenAny(Reading, Task.Delay(TimeSpan.FromSeconds(10))).Result, Is.SameAs(Reading), "refused without opening it (opening a FIFO would block)");
            Assert.That(Reading.Exception?.InnerException, Is.TypeOf<InvalidDataException>());
        }

        [Test]
        public void ALinkThatPointsElsewhere_OrATargetThatChanges_StopsTheReplacement()
        {
            RequireUnixLinks();
            string First = Path.Join(Directory_, "First.yaml");
            string Second = Path.Join(Directory_, "Second.yaml");
            File.WriteAllText(First, Original);
            File.WriteAllText(Second, Original);
            File.Delete(Settings);
            File.CreateSymbolicLink(Settings, "First.yaml");
            FailAt("before-precondition", () => { File.Delete(Settings); File.CreateSymbolicLink(Settings, "Second.yaml"); });

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));
            Assert.That((File.ReadAllText(First), File.ReadAllText(Second)), Is.EqualTo((Original, Original)));

            File.Delete(Settings);
            File.CreateSymbolicLink(Settings, "First.yaml");
            FailAt("before-precondition", () => File.AppendAllText(First, "# edited\n"));
            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));
            Assert.That(File.ReadAllText(First), Is.EqualTo(Original + "# edited\n"));
            Assert.That(new FileInfo(Settings).LinkTarget, Is.EqualTo("First.yaml"));
        }

        [Test]
        public void OnWindows_AReparsePoint_IsRefused()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only: on this platform links are followed to their target (the tests above)");
            }
            string Target = Path.Join(Directory_, "Target.yaml");
            File.WriteAllText(Target, Original);
            File.Delete(Settings);
            try
            {
                File.CreateSymbolicLink(Settings, Target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Assert.Ignore("this Windows account can't create a symbolic link (Developer Mode or the privilege is missing): " + ex.GetType().Name);
            }

            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<IOException>().With.Message.Contains("reparse point"));
            Assert.That(File.ReadAllText(Target), Is.EqualTo(Original));
        }

        // ---- a creation ----

        [Test]
        public void Create_WritesTheExactBytes_OnlyWhenTheNameIsFree_WithoutABackup()
        {
            string New = Path.Join(Directory_, "New.yaml");

            Assert.That(ConfigFileReplace.Create(New, Original, Accept), Is.True);

            Assert.That(File.ReadAllBytes(New), Is.EqualTo(Encoding.UTF8.GetBytes(Original)));
            Assert.That(Steps.Select(S => S.Name), Is.EqualTo(new[] { "temporary-writing", "temporary-flushed", "temporary-written", "before-create", "after-create" }));
            Assert.That(File.Exists(New + ".bak"), Is.False);
            Assert.That(Leftovers("New.yaml"), Is.Empty);
            if (!OperatingSystem.IsWindows())
            {
                string Reference = Path.Join(Directory_, "reference");
                using (new StreamWriter(Reference)) { }
                Assert.That(File.GetUnixFileMode(New), Is.EqualTo(File.GetUnixFileMode(Reference)), "the permissions a new file gets, as before");
            }
        }

        [Test]
        public void Create_NeverTruncatesOrOverwrites_AnExistingFile_OrALink()
        {
            DateTime Time = File.GetLastWriteTimeUtc(Settings);
            Assert.That(ConfigFileReplace.Create(Settings, "other\n", Accept), Is.False);
            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original));
            Assert.That(File.GetLastWriteTimeUtc(Settings), Is.EqualTo(Time));

            if (!OperatingSystem.IsWindows())
            {
                string Link = Path.Join(Directory_, "Dangling.yaml");
                File.CreateSymbolicLink(Link, "nowhere.yaml");
                Assert.That(ConfigFileReplace.Create(Link, "other\n", Accept), Is.False);
                Assert.That(new FileInfo(Link).LinkTarget, Is.EqualTo("nowhere.yaml"));
                Assert.That(File.Exists(Path.Join(Directory_, "nowhere.yaml")), Is.False, "not written through the link");
            }
            Assert.That(Leftovers(), Is.Empty);
        }

        [Test]
        public void Create_AWinnerThatAppearsMeanwhile_IsKept()
        {
            string New = Path.Join(Directory_, "New.yaml");
            FailAt("before-create", () => File.WriteAllText(New, "the other program's\n"));

            Assert.That(ConfigFileReplace.Create(New, Original, Accept), Is.False);

            Assert.That(File.ReadAllText(New), Is.EqualTo("the other program's\n"));
            Assert.That(Leftovers("New.yaml"), Is.Empty);
        }

        [TestCase("temporary-writing")]
        [TestCase("temporary-written")]
        [TestCase("before-create")]
        public void Create_AFailure_LeavesNoFileAndNoTemporaryFile(string StepName)
        {
            string New = Path.Join(Directory_, "New.yaml");
            FailAt(StepName);

            Assert.That(() => ConfigFileReplace.Create(New, Original, Accept), Throws.TypeOf<IOException>());

            Assert.That(File.Exists(New), Is.False);
            Assert.That(Leftovers("New.yaml"), Is.Empty);
        }

        // ---- stale temporary files ----

        [Test]
        public void StaleTemporaryFiles_OnlyOldExactNames_AreRemoved_AndNeverReadAsSettings()
        {
            string Id() => Guid.NewGuid().ToString("N");
            DateTime Old = DateTime.UtcNow - ConfigFileReplace.StaleTemporaryAge - TimeSpan.FromMinutes(1);
            string OldTemporary = Write("Settings.yaml." + Id() + ".tmp", Old);
            string OldBackupTemporary = Write("Settings.yaml.bak." + Id() + ".tmp", Old);
            string Recent = Write("Settings.yaml." + Id() + ".tmp", DateTime.UtcNow);
            string[] Kept =
            {
                Write("Settings.yaml.tmp", Old), Write("Settings.yaml." + Id().ToUpperInvariant() + ".tmp", Old), Write("Email.yaml." + Id() + ".tmp", Old),
                Write("XSettings.yaml." + Id() + ".tmp", Old), Write("Settings.yaml.bak", Old), Write("Zion.yaml", Old)
            };
            string LinkName = Path.Join(Directory_, "Settings.yaml." + Id() + ".tmp");
            string DirName = Path.Join(Directory_, "Settings.yaml." + Id() + ".tmp");
            Directory.CreateDirectory(DirName);
            Directory.SetLastWriteTimeUtc(DirName, Old);
            string Outside = Write("outside.txt", Old);
            if (!OperatingSystem.IsWindows())
            {
                File.CreateSymbolicLink(LinkName, Outside);
            }

            ConfigFileReplace.RemoveStaleTemporaryFiles(Directory_, "Settings.yaml");

            Assert.That(File.Exists(OldTemporary), Is.False);
            Assert.That(File.Exists(OldBackupTemporary), Is.False);
            Assert.That(File.Exists(Recent), Is.True, "younger than the age limit: it may belong to a running write");
            foreach (string Name in Kept.Append(Outside).Append(Settings))
            {
                Assert.That(File.Exists(Name), Is.True, Name);
            }
            Assert.That(Directory.Exists(DirName), Is.True);
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(new FileInfo(LinkName).LinkTarget, Is.EqualTo(Outside), "a link with that name is not Campertron's");
            }
            Assert.That(Directory.GetFiles(Directory_, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "Settings.yaml", "Zion.yaml" }),
                "the loaders' *.yaml never sees a temporary or backup file");
            Assert.That(Output.ToString(), Does.Contain("Removed " + OldTemporary));
        }

        [Test]
        public void NoMessage_QuotesTheFilesContent()
        {
            var Messages = new List<string>();
            void Collect(Action Action)
            {
                try
                {
                    Action();
                    Messages.Add("no failure");
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
                {
                    Messages.Add(ex.Message);
                }
            }
            Collect(() => Replace(Edited, Text => "refused"));
            FailAt("before-precondition", () => File.WriteAllText(Settings, "smtpPassword: S3cr3t-Pa55-changed\n"));
            Collect(() => Replace(Edited));
            FailAt("after-replace", () => File.WriteAllText(Settings, "smtpPassword: S3cr3t-Pa55-other\n"));
            File.WriteAllText(Settings, Original);
            Collect(() => Replace(Edited));
            File.WriteAllBytes(Settings, Encoding.UTF8.GetBytes("smtpPassword: S3cr3t-Pa55").Append((byte)0xC3).ToArray());
            Collect(() => ConfigFileReplace.Read(Settings));
            File.WriteAllBytes(Settings, Encoding.Unicode.GetBytes("smtpPassword: S3cr3t-Pa55\n"));
            Collect(() => ConfigFileReplace.Read(Settings));

            Assert.That(Messages, Has.Count.EqualTo(5));
            Assert.That(Messages, Has.None.EqualTo("no failure"));
            foreach (string Message in Messages)
            {
                Assert.That(Message, Does.Not.Contain("Pa55").And.Not.Contain("S3cr3t"));
            }
        }

        // ---- helpers ----

        private string Write(string Name, DateTime Time)
        {
            string File_ = Path.Join(Directory_, Name);
            File.WriteAllText(File_, "x: 1\n");
            File.SetLastWriteTimeUtc(File_, Time);
            return File_;
        }

        private void AssertUnchanged()
        {
            Assert.That(File.ReadAllBytes(Settings), Is.EqualTo(Encoding.UTF8.GetBytes(Original)), "the file's bytes");
            Assert.That(Leftovers(), Is.Empty, "no temporary file left");
            foreach (string Message in Output.ToString().Split('\n').Append(string.Join(" ", Steps.Select(S => S.Name))))
            {
                Assert.That(Message, Does.Not.Contain("S3cr3t"));
            }
        }

        private List<string> Leftovers(string Name = "Settings.yaml") =>
            Directory.GetFiles(Directory_, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)!)
                .Where(n => ConfigFileReplace.IsTemporaryName(Name, n) || ConfigFileReplace.IsTemporaryName(Name + ".bak", n) || ConfigFileReplace.IsTemporaryName("Target.yaml", n)).ToList();

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

        private static void RequireUnixLinks()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix symbolic links; on Windows a reparse point is refused (OnWindows_AReparsePoint_IsRefused)");
            }
        }
    }
}
