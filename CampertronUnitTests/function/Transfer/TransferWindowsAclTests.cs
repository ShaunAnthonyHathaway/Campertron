using System.Security.AccessControl;
using System.Security.Principal;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The binding Windows owner-only rule (D132, D134; v0.4.0-settings-transfer.md §Binding Windows owner-only ACL rule), checked
    // natively: a new secret file and the backup set's folder get a protected list of exactly one entry, full control for the
    // current user, who owns them, and no entry for Users, Authenticated Users, Everyone, the system account or Administrators;
    // the set's files inherit only that entry; a list that does not read back so is never used; Email.yaml, Ntfy.yaml,
    // General.yaml and the search files get no rules of their own and keep the folder's; the topic follows the inherited rules.
    // Skipped elsewhere: they run natively in the Windows CI job, whose list names every transfer test class and whose summary
    // requires each of these six to have passed there, none skipped (TD-70; test-coverage.md §Platforms).
    public class TransferWindowsAclTests : TransferApplyTestBase
    {
        private static readonly string[] Broad = { "S-1-5-32-545", "S-1-5-11", "S-1-1-0", "S-1-5-18", "S-1-5-32-544", "S-1-5-4", "S-1-5-32-546" };

        private TransferPackage Secrets() => ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Here.yaml", PackageSearch), SmtpSecret(), TokenSecret(), TopicSecret());

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static SecurityIdentifier User()
        {
            using WindowsIdentity Identity = WindowsIdentity.GetCurrent();
            return Identity.User!;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void AssertOneProtectedEntryForTheUser(FileSystemSecurity Security, string What, bool Folder)
        {
            Assert.That(Security.AreAccessRulesProtected, Is.True, What + ": inheritance disabled");
            Assert.That(Security.GetOwner(typeof(SecurityIdentifier)), Is.EqualTo(User()), What + ": owned by the current user");
            AuthorizationRuleCollection Rules = Security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            Assert.That(Rules.Count, Is.EqualTo(1), What + ": exactly one entry");
            var Rule = (FileSystemAccessRule)Rules[0]!;
            Assert.That(Rule.IdentityReference, Is.EqualTo(User()), What);
            Assert.That(Rule.AccessControlType, Is.EqualTo(AccessControlType.Allow));
            Assert.That(Rule.FileSystemRights & FileSystemRights.FullControl, Is.EqualTo(FileSystemRights.FullControl));
            Assert.That(Rule.IsInherited, Is.False);
            Assert.That(Rule.InheritanceFlags, Is.EqualTo(Folder ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None));
            foreach (string Sid in Broad)
            {
                Assert.That(Security.GetSecurityDescriptorSddlForm(AccessControlSections.Access), Does.Not.Contain(Sid), What + ": no broad identity");
            }
        }

        [Test]
        public void ANewSecretFile_HasOneProtectedEntry_ForTheCurrentUser_WhoOwnsIt()
        {
            if (!OnWindows())
            {
                return;
            }
            Receiver();
            Import(Secrets());
            foreach (string Prefix in new[] { "Email.password.", "Ntfy.token." })
            {
                string File_ = In(SecretFileOf(Prefix)!);
                AssertOneProtectedEntryForTheUser(new FileInfo(File_).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), Prefix, false);
                Assert.That(File.GetAttributes(File_) & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
                Assert.That(TransferProtection.IsOwnerOnlyFile(File_), Is.True);
            }
        }

        [Test]
        public void TheBackupSetsFolder_HasThatEntry_AndItsFilesInheritOnlyIt()
        {
            if (!OnWindows())
            {
                return;
            }
            Receiver(Searches: new[] { "Here.yaml" });
            Import(Secrets());
            string Set = In(TransferBackupSet.FolderName);
            AssertOneProtectedEntryForTheUser(new DirectoryInfo(Set).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner), "the backup set's folder", true);
            foreach (string File_ in Directory.EnumerateFiles(Set))
            {
                AuthorizationRuleCollection Rules = new FileInfo(File_).GetAccessControl(AccessControlSections.Access).GetAccessRules(true, true, typeof(SecurityIdentifier));
                Assert.That(Rules.Count, Is.EqualTo(1), File_);
                Assert.That((((FileSystemAccessRule)Rules[0]!)).IdentityReference, Is.EqualTo(User()));
                Assert.That((((FileSystemAccessRule)Rules[0]!)).IsInherited, Is.True, "from the set's folder only");
            }
        }

        [Test]
        public void AListThatDoesNotReadBackOwnerOnly_IsNeverUsed_AndTheApplyChangesNothing()
        {
            if (!OnWindows())
            {
                return;
            }
            Receiver();
            Dictionary<string, string> Before = Settings();
            TransferApply.Step = (Name, Path_) =>
            {
                Steps.Add(Name);
                if (Name == "secret-verify:smtp-password" && OperatingSystem.IsWindows())
                {
                    // another program adds a broad entry: the verification sees it
                    var Info = new FileInfo(Path_!);
                    FileSecurity Security = Info.GetAccessControl(AccessControlSections.Access);
                    Security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
                    Info.SetAccessControl(Security);
                }
            };
            TransferApplyResult Result = Applied(PreviewOf(Secrets()));
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Result.Message);
            Assert.That(Settings(), Is.EqualTo(Before));
            Assert.That(Directory.EnumerateFiles(Folder, "*.tmp"), Is.Empty);
        }

        [Test]
        public void TheSettingsFiles_GetNoRulesOfTheirOwn_AndKeepTheFoldersRules()
        {
            if (!OnWindows())
            {
                return;
            }
            Receiver(Searches: new[] { "Here.yaml" });
            var Rules = new Dictionary<string, string>();
            foreach (string Name in new[] { "General.yaml", "Email.yaml", "Ntfy.yaml", "Here.yaml" })
            {
                Rules[Name] = new FileInfo(In(Name)).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            }
            Import(Secrets());
            foreach (string Name in Rules.Keys)
            {
                FileSecurity Security = new FileInfo(In(Name)).GetAccessControl(AccessControlSections.Access);
                Assert.That(Security.AreAccessRulesProtected, Is.False, Name + ": never protected");
                Assert.That(Security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Count, Is.EqualTo(0), Name + ": no explicit rule");
                Assert.That(Security.GetSecurityDescriptorSddlForm(AccessControlSections.Access), Is.EqualTo(Rules[Name]), Name + ": no rule weakened or tightened");
            }
        }

        [Test]
        public void TheTopic_IsImportedOnlyWhereTheInheritedRules_AllowNobodyButTheUserTheSystemAndAdministrators()
        {
            if (!OnWindows())
            {
                return;
            }
            // a settings folder whose rules a new file inherits: the user, the system account and the Administrators group
            var Security = new DirectorySecurity();
            Security.SetAccessRuleProtection(true, false);
            foreach (SecurityIdentifier Sid in new[] { User(), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            {
                Security.AddAccessRule(new FileSystemAccessRule(Sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            }
            new DirectoryInfo(Folder).SetAccessControl(Security);
            Assert.That(PreviewOf(ImportPackage(Ntfy(PackageNtfy), TopicSecret())).Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.Imported), "a new Ntfy.yaml");
            Write("Ntfy.yaml", ReceiverNtfy);
            Assert.That(PreviewOf(ImportPackage(Ntfy(PackageNtfy), TopicSecret())).Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.Imported), "an existing one that inherits them");
            // Users may read what the folder holds: the topic is refused, alone
            Security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(Folder).SetAccessControl(Security);
            File.Delete(In("Ntfy.yaml"));
            Write("Ntfy.yaml", ReceiverNtfy);
            TransferPreview Preview = PreviewOf(ImportPackage(Ntfy(PackageNtfy), TopicSecret()));
            Assert.That(Preview.Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.RefusedReadableByOthers));
            Assert.That(Preview.CanApply, Is.True, "the rest can be imported");
            File.Delete(In("Ntfy.yaml"));
            Assert.That(PreviewOf(ImportPackage(Ntfy(PackageNtfy), TopicSecret())).Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.RefusedReadableByOthers), "a new file would inherit Users too");
        }

        [Test]
        public void AReparsePointAtTheBackupSetsName_RefusesTheApply()
        {
            if (!OnWindows())
            {
                return;
            }
            Receiver();
            string Elsewhere = Path.Join(Path.GetTempPath(), "CampertronTransferJunction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Elsewhere);
            try
            {
                try
                {
                    Directory.CreateSymbolicLink(In(TransferBackupSet.FolderName), Elsewhere);
                }
                catch (IOException)
                {
                    Assert.Ignore("this account can't create a symbolic link");
                }
                catch (UnauthorizedAccessException)
                {
                    Assert.Ignore("this account can't create a symbolic link");
                }
                Assert.That(Applied(PreviewOf(Secrets())).Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
                Assert.That(Directory.EnumerateFileSystemEntries(Elsewhere), Is.Empty);
            }
            finally
            {
                Directory.Delete(Elsewhere, true);
            }
        }
    }
}
