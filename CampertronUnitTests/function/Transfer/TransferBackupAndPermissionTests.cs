using System.Runtime.InteropServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The backup set and the owner-only rules on Linux and macOS (D111, D112, D134; v0.4.0-settings-transfer.md §Backup set,
    // §Unix permissions): the set is owner-only, holds every replaced file without a secret and a list of names, is bounded to
    // one, replaces the previous one only once it is ready, and is read by no loader; a new secret file is 0600 from its
    // creation whatever the umask, verified before anything names it; an existing file's mode is never changed. (Windows'
    // access rules: TransferWindowsAclTests.)
    public class TransferBackupAndPermissionTests : TransferApplyTestBase
    {
        private const UnixFileMode Owner = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private string Backup(string Name) => Path.Join(Folder, TransferBackupSet.FolderName, Name);

        private TransferPackage Usual() => ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Here.yaml", PackageSearch), Search("Trip.yaml", PackageSearch),
            SmtpSecret(), TokenSecret(), TopicSecret());

        [Test]
        public void TheBackupSet_HoldsEveryReplacedFile_WithoutASecret_AndListsNamesOnly()
        {
            Receiver(Searches: new[] { "Here.yaml" });
            Write("Email.yaml", ReceiverEmail + "smtpPasswordFile: " + In("mine.txt") + "\n");
            string GeneralBefore = Read_("General.yaml"), HereBefore = Read_("Here.yaml");
            Import(Usual());
            Assert.That(File.ReadAllText(Backup("General.yaml.bak")), Is.EqualTo(GeneralBefore), "General.yaml's bytes as they were");
            Assert.That(File.ReadAllText(Backup("Here.yaml.bak")), Is.EqualTo(HereBefore));
            string EmailBackup = File.ReadAllText(Backup("Email.yaml.bak"));
            Assert.That(EmailBackup, Does.Contain("smtpPassword: ''").And.Not.Contain(ReceiverPasswordCanary), "the plain-text password left out (D111)");
            Assert.That(EmailBackup, Does.Contain("smtpPasswordFile: " + In("mine.txt")), "the source's reference is no secret and stays, so the file can be restored");
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(EmailBackup)!.SmtpServer, Is.EqualTo("old.example.org"), "the earlier settings");
            Assert.That(File.ReadAllText(Backup("Ntfy.yaml.bak")), Does.Contain("topic: ''").And.Not.Contain(ReceiverTopicCanary), "the topic left out (D112)");
            Assert.That(File.Exists(Backup("Trip.yaml.bak")), Is.False, "a created file has nothing to back up");
            string Contents = File.ReadAllText(Backup(TransferBackupSet.ContentsName));
            Assert.That(Contents, Does.StartWith("Campertron settings transfer backup\n"));
            Assert.That(Contents, Does.Contain("  General.yaml\n").And.Contain("  Email.yaml\n").And.Contain("  Here.yaml\n").And.Contain("  Trip.yaml\n"));
            Assert.That(Contents, Does.Match(@"  Email\.password\.[0-9a-f]{32}\n").And.Match(@"  Ntfy\.token\.[0-9a-f]{32}\n"), "the files the import created, by name");
            Assert.That(Contents, Does.Contain("Written by Campertron " + TransferFormat.CurrentExportedBy() + " at "));
            Assert.That(Contents, Does.Not.Contain(Folder).And.Not.Contain(Path.DirectorySeparatorChar + "tmp"), "no path");
            Assert.That(Directory.EnumerateFiles(Path.Join(Folder, TransferBackupSet.FolderName)).Select(F => Path.GetFileName(F)).Order(),
                Is.EqualTo(new[] { "Contents.txt", "Email.yaml.bak", "General.yaml.bak", "Here.yaml.bak", "Ntfy.yaml.bak" }));
            AssertCanariesOnlyIn(new Dictionary<string, string[]>
            {
                [SecretFileOf("Email.password")!] = new[] { SmtpCanary },
                [SecretFileOf("Ntfy.token")!] = new[] { TokenCanary },
                ["Ntfy.yaml"] = new[] { TopicCanary },
                ["Email.yaml"] = new[] { ReceiverPasswordCanary }
            });
        }

        [Test]
        public void TheRollingBackupsBesideTheFiles_HoldNoSecretEither()
        {
            Receiver();
            Import(Usual());
            Assert.That(Read_("Email.yaml.bak"), Does.Contain("smtpPassword: ''").And.Not.Contain(ReceiverPasswordCanary));
            Assert.That(Read_("Ntfy.yaml.bak"), Does.Contain("topic: ''").And.Not.Contain(ReceiverTopicCanary));
            Assert.That(Read_("General.yaml.bak"), Is.EqualTo(ReceiverGeneral));
        }

        [Test]
        public void OneSet_IsKept_TheLastApplys_AndNoTemporaryFolderStays()
        {
            Receiver();
            Import(Usual());
            Write("Email.yaml", ReceiverEmail.Replace("25", "26"));
            Import(ImportPackage(Email(PackageEmail.Replace("587", "465"))));
            Assert.That(Directory.EnumerateDirectories(Folder).Select(D => Path.GetFileName(D)), Is.EqualTo(new[] { TransferBackupSet.FolderName }), "one set, no leftover");
            Assert.That(Directory.EnumerateFiles(Path.Join(Folder, TransferBackupSet.FolderName)).Select(F => Path.GetFileName(F)).Order(), Is.EqualTo(new[] { "Contents.txt", "Email.yaml.bak" }));
            Assert.That(File.ReadAllText(Backup("Email.yaml.bak")), Does.Contain("smtpPort: 26"), "the second apply's earlier version");
        }

        [Test]
        public void ANewSet_ReplacesThePreviousOne_OnlyOnceItIsReady()
        {
            Receiver();
            Import(Usual());
            Dictionary<string, string> FirstSet = Snapshot(R => !R.StartsWith(TransferBackupSet.FolderName, StringComparison.Ordinal));
            Write("Email.yaml", ReceiverEmail.Replace("25", "26"));
            Dictionary<string, string> Before = Settings();
            foreach (string Step in new[] { "backup-folder", "backup-file:Email.yaml.bak", "backup-file:Contents.txt", "backup-install" })
            {
                FailAt(Step);
                TransferApplyResult Result = Applied(PreviewOf(ImportPackage(Email(PackageEmail.Replace("587", "465")))));
                Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Step);
                Assert.That(Snapshot(R => !R.StartsWith(TransferBackupSet.FolderName, StringComparison.Ordinal)), Is.EqualTo(FirstSet), Step + ": the previous set is as it was");
                Assert.That(Settings(), Is.EqualTo(Before), Step + ": nothing of the settings was written");
                Assert.That(Directory.EnumerateDirectories(Folder).Select(D => Path.GetFileName(D)), Is.EqualTo(new[] { TransferBackupSet.FolderName }), Step + ": no temporary folder stays");
            }
        }

        [Test]
        public void ABackupFolderThatHoldsAnythingElse_RefusesTheApply_AndIsLeftAsItIs()
        {
            Receiver();
            Directory.CreateDirectory(In(TransferBackupSet.FolderName));
            Write(Path.Join(TransferBackupSet.FolderName, "notes.txt"), "the user's");
            Dictionary<string, string> Before = Snapshot();
            TransferApplyResult Result = Applied(PreviewOf(Usual()));
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
            Assert.That(Snapshot(R => R == TransferApplyLock.FileName), Is.EqualTo(Before));
        }

        [Test]
        public void ALinkAtTheBackupFoldersName_RefusesTheApply_AndNothingIsWrittenThroughIt()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            string Elsewhere = Path.Join(Path.GetTempPath(), "CampertronTransferLinked-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Elsewhere);
            try
            {
                Directory.CreateSymbolicLink(In(TransferBackupSet.FolderName), Elsewhere);
                Assert.That(Applied(PreviewOf(Usual())).Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
                Assert.That(Directory.EnumerateFileSystemEntries(Elsewhere), Is.Empty);
                Assert.That(new FileInfo(In(TransferBackupSet.FolderName)).LinkTarget, Is.Not.Null, "left as it is");
            }
            finally
            {
                Directory.Delete(Elsewhere, true);
            }
        }

        [Test]
        public void NoLoader_ReadsTheBackupSet()
        {
            Receiver(Searches: new[] { "Here.yaml" });
            Import(Usual());
            Assert.That(ConfigFiles.SearchFiles(Folder).Select(F => Path.GetFileName(F)).Order(), Is.EqualTo(new[] { "Here.yaml", "Trip.yaml" }));
            Assert.That(Yaml.CampertronConfigGetConfigs(Folder, false, false, null, null, out int Read).Count, Is.EqualTo(2));
            Assert.That(Read, Is.EqualTo(2));
            Assert.That(ConfigValidation.ValidateFolder(Folder, DateTime.Now).Select(D => D.FileName), Has.None.Contains(".bak").And.None.Contains("Contents"));
        }

        [Test]
        public void OnUnix_TheSecretFilesAre0600_TheSetsFolder0700_AndItsFiles0600()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            Import(Usual());
            Assert.That(ModeOf(In(SecretFileOf("Email.password")!)), Is.EqualTo(Owner));
            Assert.That(ModeOf(In(SecretFileOf("Ntfy.token")!)), Is.EqualTo(Owner));
            Assert.That(ModeOf(In(TransferBackupSet.FolderName)), Is.EqualTo(Owner | UnixFileMode.UserExecute));
            foreach (string File_ in Directory.EnumerateFiles(In(TransferBackupSet.FolderName)))
            {
                Assert.That(ModeOf(File_), Is.EqualTo(Owner), File_);
            }
        }

        [Test]
        public void OnUnix_AnExistingFilesMode_IsKept_AndANewEmailOrNtfyYaml_IsOwnerOnly()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver(Ntfy: false, Searches: new[] { "Here.yaml" });
            var Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(In("Email.yaml"), Shared);
            File.SetUnixFileMode(In("General.yaml"), Shared | UnixFileMode.OtherRead);
            File.SetUnixFileMode(In("Here.yaml"), Shared);
            Import(Usual());
            Assert.That(ModeOf(In("Email.yaml")), Is.EqualTo(Shared), "the safe writer keeps a replaced file's mode; the import never narrows or widens it");
            Assert.That(ModeOf(In("General.yaml")), Is.EqualTo(Shared | UnixFileMode.OtherRead));
            Assert.That(ModeOf(In("Here.yaml")), Is.EqualTo(Shared));
            Assert.That(ModeOf(In("Ntfy.yaml")), Is.EqualTo(Owner), "a new Ntfy.yaml is created owner-only");
        }

        [Test]
        public void OnUnix_ASecretFileThatIsNotOwnerOnly_IsRemoved_AndTheApplyChangesNothing()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            Dictionary<string, string> Before = Settings();
            foreach (string Step in new[] { "secret-permission:smtp-password", "secret-verify:smtp-password", "secret-placed:ntfy-token" })
            {
                TransferApply.Step = (Name, Path_) =>
                {
                    Steps.Add(Name);
                    if (Name == Step && !OperatingSystem.IsWindows())
                    {
                        // another program, or a file system that keeps no modes, widens it
                        File.SetUnixFileMode(Path_!, Owner | UnixFileMode.OtherRead);
                    }
                };
                TransferApplyResult Result = Applied(PreviewOf(Usual()));
                Assert.That(Directory.EnumerateFiles(Folder).Where(F => F.EndsWith(".tmp", StringComparison.Ordinal)), Is.Empty, Step + ": the writer's temporary file is removed");
                if (Step != "secret-placed:ntfy-token")
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Step);
                    Assert.That(Settings(), Is.EqualTo(Before), Step + ": nothing of the settings, and no secret file");
                    continue;
                }
                // a placed file that is no longer as this apply made it is not removed (the secret-replacement policy: only an
                // unchanged file of the transaction is): the result says so, by kind, and the settings are as they were
                Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored), Step);
                Assert.That(Result.NotRestored, Is.EqualTo(new[] { "the new file for ntfy's access token" }));
                Assert.That(Result.Message, Does.Not.Contain(Folder).And.Not.Contain("Ntfy.token."));
                string Token = SecretFileOf("Ntfy.token")!;
                Assert.That(Settings().Where(E => E.Key != Token), Is.EquivalentTo(Before), Step + ": every settings file as it was");
                Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!.TokenFileWritten, Is.False, "nothing names it");
                File.Delete(In(Token));
            }
        }

        [Test]
        public void OnLinux_TheSecretFile_Is0600_WhateverTheUmask()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("Linux: the umask is set through libc");
            }
            Receiver();
            uint Previous = umask(0x1BF); // 0o277: the owner's write bit taken away from every new file
            try
            {
                Import(ImportPackage(Email(PackageEmail), SmtpSecret()));
            }
            finally
            {
                umask(Previous);
            }
            Assert.That(ModeOf(In(SecretFileOf("Email.password")!)), Is.EqualTo(Owner), "created never wider, then given exactly 0600");
        }

        [DllImport("libc", SetLastError = true)]
        private static extern uint umask(uint Mask);

        [Test]
        public void OnUnix_ALinkAtTheNewSecretFilesName_IsNeverFollowed()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            const string Fixed = "fedcba9876543210fedcba9876543210";
            TransferImport.NewTransactionId = () => Fixed;
            string Target = Path.Join(Path.GetTempPath(), "CampertronTransferLinkTarget-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(Target, "the link's target");
            try
            {
                File.CreateSymbolicLink(In("Email.password." + Fixed), Target);
                Dictionary<string, string> Before = Settings();
                Assert.That(Applied(PreviewOf(ImportPackage(Email(PackageEmail), SmtpSecret()))).Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
                Assert.That(File.ReadAllText(Target), Is.EqualTo("the link's target"));
                Assert.That(Settings(), Is.EqualTo(Before));
            }
            finally
            {
                File.Delete(Target);
            }
        }

        [Test]
        public void TheProtectionChecks_RefuseALink_AFolder_AndAWiderMode()
        {
            if (!OnUnix())
            {
                return;
            }
            string File_ = In("probe");
            TransferProtection.CreateOwnerOnlyFile(File_, new byte[] { 1 });
            Assert.That(TransferProtection.IsOwnerOnlyFile(File_), Is.True);
            File.SetUnixFileMode(File_, Owner | UnixFileMode.GroupRead);
            Assert.That(TransferProtection.IsOwnerOnlyFile(File_), Is.False);
            File.SetUnixFileMode(File_, Owner);
            File.CreateSymbolicLink(In("probe-link"), File_);
            Assert.That(TransferProtection.IsOwnerOnlyFile(In("probe-link")), Is.False, "a link is never Campertron's own file");
            Assert.That(() => TransferProtection.CreateOwnerOnlyFile(File_, new byte[] { 2 }), Throws.InstanceOf<IOException>(), "create-new");
            TransferProtection.CreateOwnerOnlyFolder(In("probe-folder"));
            Assert.That(TransferProtection.IsOwnerOnlyFolder(In("probe-folder")), Is.True);
            Assert.That(TransferProtection.IsOwnerOnlyFile(In("probe-folder")), Is.False);
            Assert.That(() => TransferProtection.CreateOwnerOnlyFolder(In("probe-folder")), Throws.InstanceOf<IOException>());
            Assert.That(TransferProtection.TopicAllowed(Folder, true, Owner), Is.True);
            Assert.That(TransferProtection.TopicAllowed(Folder, true, Owner | UnixFileMode.OtherWrite), Is.False, "any permission for others");
            Assert.That(TransferProtection.TopicAllowed(Folder, true, null), Is.False, "unknown is a refusal");
            Assert.That(TransferProtection.TopicAllowed(Folder, false, null), Is.True, "a new Ntfy.yaml is created 0600");
        }
    }
}
