using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ConfigFileReplace on Windows (D40), run natively by the Windows CI job: ordinary files (their access rules, which an
    // ordinary file only inherits from its folder, and their attributes), read-only files, files with access rules of their
    // own or moved in from a folder with other rules, files and backups held open by another program, a locked temporary
    // file, and reparse points (symbolic links, junctions). Every refusal must leave the file byte for byte as it was. Each
    // test has its own folder: under CAMPERTRON_TEST_USER_FOLDER if the CI job sets it (a fresh folder in the runner's
    // LocalApplicationData, never CampertronConfig or CampertronCache), else under the temporary folder. Skipped elsewhere.
    public class ConfigFileReplaceWindowsTests
    {
        private string Parent = string.Empty;
        private string Directory_ = string.Empty;
        private readonly List<string> OtherFolders = new();
        private string Settings => Path.Join(Directory_, "Settings.yaml");
        private string Backup => Settings + ".bak";
        private const string Original = "# my settings\r\nsmtpServer: 'mail.example.org' # host\r\nsmtpPassword: S3cr3t-Pa55\r\n";
        private const string Edited = "# my settings\r\nsmtpServer: 'smtp.example.org' # host\r\nsmtpPassword: S3cr3t-Pa55\r\n";
        private StringWriter Output = null!;
        private TextWriter? OriginalOut;
        private Action<string, string> OriginalStep = null!;
        private readonly List<(string Name, string Path)> Steps = new();

        [SetUp]
        public void Setup()
        {
            string? UserFolder = Environment.GetEnvironmentVariable("CAMPERTRON_TEST_USER_FOLDER");
            Parent = !string.IsNullOrEmpty(UserFolder) && Directory.Exists(UserFolder) ? UserFolder : Path.GetTempPath();
            Directory_ = Path.Join(Parent, "CampertronWindowsTests-" + Guid.NewGuid().ToString("N"));
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
            foreach (string Folder in OtherFolders.Append(Directory_))
            {
                if (Directory.Exists(Folder))
                {
                    // read-only files can't be deleted; links (also dangling ones) are left to Directory.Delete
                    foreach (string File_ in Directory.GetFiles(Folder))
                    {
                        FileAttributes Attributes = File.GetAttributes(File_);
                        if ((Attributes & FileAttributes.ReadOnly) != 0 && (Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            File.SetAttributes(File_, Attributes & ~FileAttributes.ReadOnly);
                        }
                    }
                    Directory.Delete(Folder, true);
                }
            }
            OtherFolders.Clear();
        }

        // ---- ordinary files ----

        [Test]
        public void AnOrdinaryFile_HasOnlyInheritedAccessRules_WhichCampertronAccepts()
        {
            if (!OnWindows())
            {
                return;
            }
            FileSecurity Security = new FileInfo(Settings).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            int Explicit = Security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Count;
            int Inherited = Security.GetAccessRules(false, true, typeof(SecurityIdentifier)).Count;
            TestContext.Progress.WriteLine($"Windows ACL facts for a new file in {Parent}: owner {Security.GetOwner(typeof(SecurityIdentifier))}, " +
                $"protected {Security.AreAccessRulesProtected}, explicit rules {Explicit}, inherited rules {Inherited}, " +
                $"DACL {Security.GetSecurityDescriptorSddlForm(AccessControlSections.Access)}, attributes {File.GetAttributes(Settings)}");

            Assert.That(Security.AreAccessRulesProtected, Is.False);
            Assert.That(Explicit, Is.EqualTo(0), "an ordinary file has no access rules of its own");
            Assert.That(Inherited, Is.GreaterThan(0));
            Assert.That(ConfigFileReplace.Read(Settings).AccessRules, Is.EqualTo(Sddl(Settings)));
        }

        [Test]
        public void AnOrdinaryGeneralYaml_GetsOnlyTheNewTimestamp_AndKeepsItsCommentsCrlfBomAccessRulesAndAttributes()
        {
            if (!OnWindows())
            {
                return;
            }
            string GeneralYaml = Path.Join(Directory_, "General.yaml");
            const string Text = "# Campertron settings\r\noutputTo: Console   # where results go\r\n\r\nrefreshRidbDataDayInterval: 30\r\n" +
                "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC\r\nautoRefresh: false\r\n# end\r\n";
            byte[] Bom = { 0xEF, 0xBB, 0xBF };
            byte[] Before = Bom.Concat(Encoding.UTF8.GetBytes(Text)).ToArray();
            File.WriteAllBytes(GeneralYaml, Before);
            string Rules = Sddl(GeneralYaml);
            FileAttributes Attributes = File.GetAttributes(GeneralYaml);
            var NewTime = new DateTime(2026, 11, 15, 1, 2, 3, DateTimeKind.Utc);

            Yaml.GeneralConfigReplaceYaml(new GeneralConfig { OutputTo = OutputType.HtmlFile, LastRidbDataRefresh = NewTime }, Directory_);

            byte[] Expected = Bom.Concat(Encoding.UTF8.GetBytes(Text.Replace("2020-01-02T03:04:05.0000000Z", "2026-11-15T01:02:03.0000000Z"))).ToArray();
            Assert.That(File.ReadAllBytes(GeneralYaml), Is.EqualTo(Expected), "only the timestamp changed: comments, CRLF and the BOM stay");
            Assert.That(File.ReadAllBytes(GeneralYaml + ".bak"), Is.EqualTo(Before), "the backup is exactly the previous content");
            Assert.That(Sddl(GeneralYaml), Is.EqualTo(Rules), "the same access rules, never broader ones");
            Assert.That(Sddl(GeneralYaml + ".bak"), Is.EqualTo(Rules));
            Assert.That(File.GetAttributes(GeneralYaml), Is.EqualTo(Attributes));
            GeneralConfig Loaded = Yaml.GeneralConfigGetConfig(Directory_);
            Assert.That((Loaded.LastRidbDataRefresh, Loaded.OutputTo), Is.EqualTo(((DateTime?)NewTime, OutputType.Console)));
            Assert.That(Leftovers("General.yaml"), Is.Empty);
        }

        [Test]
        public void AHiddenFile_StaysHidden_AndSoDoesItsBackup()
        {
            if (!OnWindows())
            {
                return;
            }
            File.SetAttributes(Settings, FileAttributes.Hidden | FileAttributes.Archive);

            Replace(Edited);

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Edited));
            Assert.That(File.GetAttributes(Settings) & FileAttributes.Hidden, Is.EqualTo(FileAttributes.Hidden));
            Assert.That(File.GetAttributes(Backup) & FileAttributes.Hidden, Is.EqualTo(FileAttributes.Hidden));
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Original));
        }

        [Test]
        public void AReadOnlyFile_IsRefused_AndLeftAsItIs()
        {
            if (!OnWindows())
            {
                return;
            }
            File.SetAttributes(Settings, FileAttributes.ReadOnly);

            Assert.That(() => Replace(Edited), Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("read-only"));

            AssertUnchanged();
            Assert.That(File.GetAttributes(Settings) & FileAttributes.ReadOnly, Is.EqualTo(FileAttributes.ReadOnly));
            Assert.That(Steps, Is.Empty, "refused before anything was written");
        }

        // ---- access rules ----

        [Test]
        public void AFileWithAnExplicitAccessRule_IsRefused()
        {
            if (!OnWindows())
            {
                return;
            }
            AddExplicitRule(Settings);

            Assert.That(() => Replace(Edited), Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("access rules of its own"));

            AssertUnchanged();
            Assert.That(Steps, Is.Empty);
        }

        [Test]
        public void AFileWithProtectedAccessRules_IsRefused()
        {
            if (!OnWindows())
            {
                return;
            }
            var Info = new FileInfo(Settings);
            FileSecurity Security = Info.GetAccessControl(AccessControlSections.Access);
            Security.SetAccessRuleProtection(true, true);
            Info.SetAccessControl(Security);

            Assert.That(() => Replace(Edited), Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("access rules of its own"));

            AssertUnchanged();
        }

        [Test]
        public void AFileMovedInFromAFolderWithOtherRules_IsRefused_SoTheReplacementIsNeverBroader()
        {
            if (!OnWindows())
            {
                return;
            }
            // new files in the settings folder get one more rule (Everyone may read) than files of the other folder
            var Folder = new DirectoryInfo(Directory_);
            DirectorySecurity FolderSecurity = Folder.GetAccessControl(AccessControlSections.Access);
            FolderSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadData, InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Folder.SetAccessControl(FolderSecurity);
            string Other = Path.Join(Parent, "CampertronWindowsTestsOther-" + Guid.NewGuid().ToString("N"));
            OtherFolders.Add(Other);
            Directory.CreateDirectory(Other);
            File.WriteAllText(Path.Join(Other, "Moved.yaml"), Original);
            File.Delete(Settings);
            File.Move(Path.Join(Other, "Moved.yaml"), Settings);
            string Rules = Sddl(Settings);
            Assert.That(Rules, Does.Not.Contain(";WD)"), "a moved file keeps the rules it had");

            // which check refuses it depends on how Windows recorded the moved file's rules (as its own, or still as
            // inherited); either way nothing is written
            UnauthorizedAccessException Refusal = Assert.Throws<UnauthorizedAccessException>(() => Replace(Edited))!;
            TestContext.Progress.WriteLine($"Windows ACL facts for a file moved in from another folder: DACL {Rules}, explicit rules " +
                $"{new FileInfo(Settings).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, false, typeof(SecurityIdentifier)).Count}; refused: {Refusal.Message}");
            Assert.That(Refusal.Message, Does.Contain("access rules of its own").Or.Contain("would not get"));

            AssertUnchanged();
            Assert.That(Sddl(Settings), Is.EqualTo(Rules));
            Assert.That(File.Exists(Backup), Is.False, "refused before the backup");

            // a file created in that folder has its rules and is replaced with them
            string Fresh = Path.Join(Directory_, "Fresh.yaml");
            File.WriteAllText(Fresh, Original);
            string FreshRules = Sddl(Fresh);
            Assert.That(FreshRules, Does.Contain(";WD)"));
            ConfigFileReplace.Replace(ConfigFileReplace.Read(Fresh), Edited, Accept);
            Assert.That((File.ReadAllText(Fresh), Sddl(Fresh), Sddl(Fresh + ".bak")), Is.EqualTo((Edited, FreshRules, FreshRules)));
        }

        [Test]
        public void AFileWhoseFolderNowGivesNewFilesOtherRules_IsRefused_SoTheReplacementIsNeverBroader()
        {
            if (!OnWindows())
            {
                return;
            }
            // the folder now gives new files one more rule (Everyone may read), written with the low-level SetFileSecurityW,
            // which does not propagate inheritable rules; Windows then records the file's rules as its own (protected), so
            // the file is refused as a file with rules of its own, or, if its rules stayed inherited, by the comparison
            DirectorySecurity FolderSecurity = new DirectoryInfo(Directory_).GetAccessControl(AccessControlSections.Access);
            FolderSecurity.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.ReadData, InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Assert.That(SetFileSecurity(Directory_, DaclSecurityInformation, FolderSecurity.GetSecurityDescriptorBinaryForm()), Is.True,
                "SetFileSecurityW failed with error " + Marshal.GetLastPInvokeError());
            string Rules = Sddl(Settings);
            Assert.That(Rules, Does.Not.Contain(";WD)"), "the file did not get the new rule");
            string Probe = Path.Join(Directory_, "probe.txt");
            File.WriteAllText(Probe, "x");
            Assert.That(Sddl(Probe), Does.Contain(";WD)"), "a new file in the folder gets the new rule");
            File.Delete(Probe);

            UnauthorizedAccessException Refusal = Assert.Throws<UnauthorizedAccessException>(() => Replace(Edited))!;
            TestContext.Progress.WriteLine($"Windows ACL facts for a file whose folder now gives new files other rules: DACL {Rules}; refused: {Refusal.Message}");
            Assert.That(Refusal.Message, Does.Contain("access rules of its own").Or.Contain("would not get"));

            AssertUnchanged();
            Assert.That(Sddl(Settings), Is.EqualTo(Rules));
            Assert.That(File.Exists(Backup), Is.False, "refused before the backup");
        }

        [TestCase(1, TestName = "ATemporaryFileWithOtherAccessRules_IsNeverRenamed(the new content)")]
        [TestCase(2, TestName = "ATemporaryFileWithOtherAccessRules_IsNeverRenamed(the backup)")]
        public void ATemporaryFileWithOtherAccessRules_IsNeverRenamed(int Which)
        {
            if (!OnWindows())
            {
                return;
            }
            // the comparison itself: the temporary file (of the new content, or of the backup) gets one more rule while it
            // is written, as a folder giving new files other rules would; it must be refused before anything is renamed
            File.WriteAllText(Backup, "an older backup\r\n");
            string Rules = Sddl(Settings);
            int Flushed = 0;
            FailAt("temporary-flushed", () =>
            {
                if (++Flushed == Which && OperatingSystem.IsWindows())
                {
                    AddExplicitRule(Steps.Last().Path);
                }
            });

            Assert.That(() => Replace(Edited), Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("would not get"));

            AssertUnchanged();
            Assert.That(Sddl(Settings), Is.EqualTo(Rules));
            Assert.That(File.ReadAllText(Backup), Is.EqualTo("an older backup\r\n"), "the previous backup was not replaced");
            Assert.That(Steps.Select(S => S.Name), Does.Not.Contain("backup-written").And.Not.Contain("before-replace"));
        }

        [Test]
        public void AnAccessRuleChangeByAnotherProgram_IsAChangeToo()
        {
            if (!OnWindows())
            {
                return;
            }
            FailAt("before-precondition", () =>
            {
                if (OperatingSystem.IsWindows())
                {
                    AddExplicitRule(Settings);
                }
            });

            Assert.That(() => Replace(Edited), Throws.TypeOf<IOException>().With.Message.Contains("was changed by another program"));

            AssertUnchanged();
        }

        // ---- other programs holding the files open ----

        [Test]
        public void AFileOpenWithoutDeleteSharing_IsNotReplaced()
        {
            if (!OnWindows())
            {
                return;
            }
            using (new FileStream(Settings, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.That(() => Replace(Edited), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }

            AssertUnchanged();
            Assert.That(Steps.Select(S => S.Name), Does.Contain("before-replace").And.Not.Contain("after-replace"), "only the rename failed");
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Original), "the backup holds the content that is still there");
        }

        [Test]
        public void AFileOpenForWritingByAnotherProgram_IsNotEvenRead()
        {
            if (!OnWindows())
            {
                return;
            }
            using (new FileStream(Settings, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(() => ConfigFileReplace.Read(Settings), Throws.InstanceOf<IOException>());
            }

            AssertUnchanged();
            Assert.That(Steps, Is.Empty);
            Assert.That(Directory.GetFiles(Directory_).Select(Path.GetFileName), Is.EqualTo(new[] { "Settings.yaml" }));
        }

        [Test]
        public void ABackupOpenByAnotherProgram_StopsTheReplacement()
        {
            if (!OnWindows())
            {
                return;
            }
            File.WriteAllText(Backup, "an older backup\r\n");
            using (new FileStream(Backup, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.That(() => Replace(Edited), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }

            AssertUnchanged();
            Assert.That(File.ReadAllText(Backup), Is.EqualTo("an older backup\r\n"));
            Assert.That(Steps.Select(S => S.Name), Does.Not.Contain("before-replace"));
        }

        [Test]
        public void ALockedTemporaryFile_StopsTheRename_AndOnlyThatFileIsLeftForTheCleanup()
        {
            if (!OnWindows())
            {
                return;
            }
            string Unrelated = Path.Join(Directory_, "unrelated.txt");
            File.WriteAllText(Unrelated, "keep");
            FileStream? Lock = null;
            FailAt("before-replace", () => Lock = new FileStream(Steps.Last().Path, FileMode.Open, FileAccess.Read, FileShare.Read));
            try
            {
                Assert.That(() => Replace(Edited), Throws.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            }
            finally
            {
                Lock?.Dispose();
            }

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original));
            Assert.That(Output.ToString(), Does.Contain("Could not remove the temporary file"), "reported, without hiding the failure");
            List<string> Left = Leftovers();
            Assert.That(Left, Has.Count.EqualTo(1), "only the locked temporary file is left");
            File.SetLastWriteTimeUtc(Path.Join(Directory_, Left[0]), DateTime.UtcNow - ConfigFileReplace.StaleTemporaryAge - TimeSpan.FromMinutes(1));

            ConfigFileReplace.RemoveStaleTemporaryFiles(Directory_, "Settings.yaml");

            Assert.That(Leftovers(), Is.Empty);
            Assert.That(File.ReadAllText(Unrelated), Is.EqualTo("keep"));
            Assert.That((File.ReadAllText(Settings), File.ReadAllText(Backup)), Is.EqualTo((Original, Original)));
        }

        // ---- reparse points ----

        [Test]
        public void AJunctionWithTheFilesName_IsRefused_AndNotFollowed()
        {
            if (!OnWindows())
            {
                return;
            }
            string Real = Path.Join(Directory_, "real");
            Directory.CreateDirectory(Real);
            File.WriteAllText(Path.Join(Real, "Settings.yaml"), Original);
            File.Delete(Settings);
            var Start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string Argument in new[] { "/c", "mklink", "/J", Settings, Real })
            {
                Start.ArgumentList.Add(Argument);
            }
            using (Process Make = Process.Start(Start)!)
            {
                Make.WaitForExit();
                if (Make.ExitCode != 0)
                {
                    Assert.Ignore("mklink /J failed with exit code " + Make.ExitCode + ": " + Make.StandardError.ReadToEnd());
                }
            }
            Assert.That(File.GetAttributes(Settings) & FileAttributes.ReparsePoint, Is.EqualTo(FileAttributes.ReparsePoint));

            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<IOException>().With.Message.Contains("is a directory"));
            Assert.That(ConfigFileReplace.Create(Settings, Edited, Accept), Is.False, "a creation never writes into it");

            Assert.That(File.ReadAllText(Path.Join(Real, "Settings.yaml")), Is.EqualTo(Original));
            Assert.That(Directory.GetFiles(Real).Select(Path.GetFileName), Is.EqualTo(new[] { "Settings.yaml" }));
            Assert.That(Directory.GetFiles(Directory_).Where(f => f.EndsWith(".tmp") || f.EndsWith(".bak")), Is.Empty);
        }

        [Test]
        public void ASymbolicLinkWithTheFilesName_IsNeitherFollowedNorReplaced_AndACreationWritesNothing()
        {
            if (!OnWindows())
            {
                return;
            }
            string Target = Path.Join(Directory_, "Target.yaml");
            File.WriteAllText(Target, Original);
            File.Delete(Settings);
            CreateFileLinkOrIgnore(Settings, Target);
            string Dangling = Path.Join(Directory_, "Dangling.yaml");
            CreateFileLinkOrIgnore(Dangling, Path.Join(Directory_, "nowhere.yaml"));

            Assert.That(() => ConfigFileReplace.Read(Settings), Throws.TypeOf<IOException>().With.Message.Contains("reparse point"));
            Assert.That(ConfigFileReplace.Create(Dangling, Edited, Accept), Is.False);

            Assert.That(new FileInfo(Settings).LinkTarget, Is.EqualTo(Target), "the link itself is kept");
            Assert.That(File.ReadAllText(Target), Is.EqualTo(Original));
            Assert.That(File.Exists(Path.Join(Directory_, "nowhere.yaml")), Is.False, "nothing written through the link");
            Assert.That(Directory.GetFiles(Directory_).Where(f => f.EndsWith(".tmp") || f.EndsWith(".bak")), Is.Empty);
        }

        [Test]
        public void TheBackupName_IsReplacedByRename_NeverWrittenThroughALink()
        {
            if (!OnWindows())
            {
                return;
            }
            string Outside = Path.Join(Directory_, "outside.txt");
            File.WriteAllText(Outside, "not a backup");
            CreateFileLinkOrIgnore(Backup, Outside);

            Replace(Edited);

            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Edited));
            Assert.That(new FileInfo(Backup).LinkTarget, Is.Null, "the old backup name was replaced as a whole");
            Assert.That(File.ReadAllText(Backup), Is.EqualTo(Original));
            Assert.That(File.ReadAllText(Outside), Is.EqualTo("not a backup"));
        }

        // ---- helpers ----

        // Each test starts with "if (!OnWindows()) return;": elsewhere the test is skipped (Assert.Ignore, so it is counted
        // as skipped; a [SupportedOSPlatform] on the class would make NUnit leave it out of the counts), and the platform
        // analyzer knows that the rest of the test runs only on Windows
        [SupportedOSPlatformGuard("windows")]
        private static bool OnWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only (run natively by the Windows CI job)");
            }
            return OperatingSystem.IsWindows();
        }

        private static string? Accept(string Text) => null;

        private void Replace(string NewText) => ConfigFileReplace.Replace(ConfigFileReplace.Read(Settings), NewText, Accept);

        private void FailAt(string StepName, Action Action)
        {
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                Steps.Add((Name, Path_));
                if (Name == StepName)
                {
                    Action();
                }
            };
        }

        // the low-level call that sets a DACL without propagating its inheritable rules to existing files (test only)
        private const uint DaclSecurityInformation = 4;

        [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetFileSecurity(string FileName, uint SecurityInformation, byte[] SecurityDescriptor);

        [SupportedOSPlatform("windows")]
        private static string Sddl(string Path_) =>
            new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        // one more rule of the file's own: the current user may read it (which it may already: no access is widened)
        [SupportedOSPlatform("windows")]
        private static void AddExplicitRule(string Path_)
        {
            var Info = new FileInfo(Path_);
            FileSecurity Security = Info.GetAccessControl(AccessControlSections.Access);
            Security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.Read, AccessControlType.Allow));
            Info.SetAccessControl(Security);
        }

        private static void CreateFileLinkOrIgnore(string Link, string Target)
        {
            try
            {
                File.CreateSymbolicLink(Link, Target);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Assert.Ignore("this Windows account can't create a symbolic link (Developer Mode or the privilege is missing): " + ex.GetType().Name);
            }
        }

        private void AssertUnchanged()
        {
            Assert.That(File.ReadAllText(Settings), Is.EqualTo(Original), "the file's bytes");
            Assert.That(Leftovers(), Is.Empty, "no temporary file left");
            Assert.That(Output.ToString(), Does.Not.Contain("S3cr3t"));
        }

        private List<string> Leftovers(string Name = "Settings.yaml") =>
            Directory.GetFiles(Directory_).Select(f => Path.GetFileName(f)!)
                .Where(n => ConfigFileReplace.IsTemporaryName(Name, n) || ConfigFileReplace.IsTemporaryName(Name + ".bak", n)).ToList();
    }
}
