using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // D111 (business-rules-settings §14, §27): a save of Email.yaml that replaces or removes the plain-text password the
    // file holds never leaves that password in Email.yaml.bak. The backup is then the previous settings with the password
    // cleared (''), which a rollback can still use for every other setting; no file of the settings folder (the file, its
    // backup, a temporary file) holds a password the saved file no longer has. Part of ConfigEditEmailTests (one class, so
    // the Windows CI job's filter runs it natively). The passwords are the class's sentinels, searched for in every file
    // and in everything the session shows.
    public partial class ConfigEditEmailTests
    {
        private const string WithoutPassword = "Saved Email.yaml. The previous version is in Email.yaml.bak, without its plain-text password.";
        private const string Exact = "Saved Email.yaml. The previous version is in Email.yaml.bak.";

        // the text with its plain-text password cleared, as Remove clears it (the comment on its line kept)
        private static string Cleared(string Text) => Text.Replace("smtpPassword: " + Secret, "smtpPassword: ''");

        // the settings folder's own files (not its subfolders) that hold Value: the file, its backup, a temporary file
        private string[] FilesHolding(string Value) =>
            Directory.GetFiles(Folder).Where(F => Text(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

        private static int Count(string Text, string Value) => Text.Split(Value).Length - 1;

        // 3, 4, 7, 9, 40: the removal saved, the other settings of the backup the previous ones
        [Test]
        public void RemovingThePassword_LeavesItOutOfTheBackup_WhichKeepsThePreviousSettings()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            Session.Set("smtpServer", "smtp.example.org");

            ConfigEditResult Saved = Session.Save();

            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Saved.Message, Is.EqualTo(WithoutPassword));
            Assert.That(Text(Email), Is.EqualTo(Cleared(EmailText).Replace("'mail.example.org'", "'smtp.example.org'")));
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)), "the previous settings, comments and all, without the password");
            EmailConfig Backup = Yaml.LoadSettingsText<EmailConfig>(Text(Email + ".bak"));
            Assert.That((Backup.SmtpServer, Backup.SmtpPort, Backup.SmtpUsername, Backup.SmtpPassword, Backup.SendFromAddress),
                Is.EqualTo(("mail.example.org", 587, "camper", "", "camper@example.org")));
            Assert.That(Backup.SendToAddressList, Is.EqualTo(new[] { "first@example.org", "second@example.org", "third@example.org" }));
            Assert.That(FilesHolding(Secret), Is.Empty, "neither the file, its backup nor a temporary file holds the removed password");
            Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml", "Email.yaml.bak" }), "no temporary file is left");
            AssertNothingShown(Session, "after removing the password");
        }

        // 6: the old password nowhere, the new one only in the file
        [Test]
        public void ReplacingThePassword_PutsTheNewOneOnlyInTheFile_AndTheOldOneNowhere()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", NewSecret);

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));

            Assert.That(FilesHolding(Secret), Is.Empty);
            Assert.That(FilesHolding(NewSecret), Is.EqualTo(new[] { "Email.yaml" }), "a backup never holds a password that was only entered");
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)));
            AssertNothingShown(Session, "after replacing the password");
        }

        // 2, 3, 4, 37: moving the password to smtpPasswordFile, in one save or in two; the file is used, and never touched
        [Test]
        public void MovingThePasswordToAFile_InOneSaveOrTwo_LeavesItOutOfTheBackup_AndTheFileIsUsed()
        {
            string FilePassword = "File-Sentinel-" + Guid.NewGuid().ToString("N");
            string PasswordFile = Path.Join(Folder, "secrets", "smtp_password");
            Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile)!);
            File.WriteAllText(PasswordFile, FilePassword + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(PasswordFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            byte[] FileBytes = File.ReadAllBytes(PasswordFile);
            DateTime FileWritten = File.GetLastWriteTimeUtc(PasswordFile);
            Func<string, string?> OriginalEnvironment = SmtpPassword.GetEnvironment;
            SmtpPassword.GetEnvironment = Name => null;
            try
            {
                //one save: the file named and the plain-text password removed
                Write(Email, EmailText);
                var Session = OpenEmail();
                Session.Set("smtpPasswordFile", PasswordFile);
                Session.Set("smtpPassword", "");
                Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
                Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)), "the previous settings: no password file yet, and no password");
                Assert.That(FilesHolding(Secret), Is.Empty);
                SmtpPasswordResult Used = SmtpPassword.Resolve(Yaml.EmailConfigConvertFromYaml(Email));
                Assert.That((Used.Source, Used.Password), Is.EqualTo((SmtpPasswordSource.File, FilePassword)), "the password file works");

                //two saves: the file named first (the backup is the exact previous file: the file still holds that password)
                File.Delete(Email + ".bak");
                Write(Email, EmailText);
                Session = OpenEmail();
                Session.Set("smtpPasswordFile", PasswordFile);
                Assert.That(Session.Save().Message, Is.EqualTo(Exact));
                Assert.That(Text(Email + ".bak"), Is.EqualTo(EmailText));
                Assert.That(FilesHolding(Secret), Is.EqualTo(new[] { "Email.yaml", "Email.yaml.bak" }), "the file still holds it: no password it no longer has");
                string Named = Text(Email);
                //then the plain-text password removed
                Session.Set("smtpPassword", "");
                Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
                Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(Named)));
                Assert.That(FilesHolding(Secret), Is.Empty);
                Assert.That(Yaml.LoadSettingsText<EmailConfig>(Text(Email + ".bak")).SmtpPasswordFile, Is.EqualTo(PasswordFile), "a rollback keeps the password file");
                Used = SmtpPassword.Resolve(Yaml.EmailConfigConvertFromYaml(Email));
                Assert.That((Used.Source, Used.Password), Is.EqualTo((SmtpPasswordSource.File, FilePassword)));

                //the password file is never written, moved or given other permissions
                Assert.That(File.ReadAllBytes(PasswordFile), Is.EqualTo(FileBytes));
                Assert.That(File.GetLastWriteTimeUtc(PasswordFile), Is.EqualTo(FileWritten));
                if (!OperatingSystem.IsWindows())
                {
                    Assert.That(File.GetUnixFileMode(PasswordFile), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                }
                Assert.That(FilesHolding(FilePassword), Is.Empty, "the file's password is never copied into the settings folder");
                AssertNothingShown(Session, "after moving the password");
                Assert.That(Everything(Session) + Out + Err, Does.Not.Contain(FilePassword));
            }
            finally
            {
                SmtpPassword.GetEnvironment = OriginalEnvironment;
            }
        }

        // 5, 38: no later save brings the password back; without a change nothing is written, and reading writes nothing
        [Test]
        public void ALaterSave_NeverBringsTheRemovedPasswordBack_AndNothingIsWrittenWithoutAChange()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            Assert.That(Session.Save().Succeeded, Is.True);
            string Removed = Text(Email);
            byte[] Backup = File.ReadAllBytes(Email + ".bak");
            DateTime BackupWritten = File.GetLastWriteTimeUtc(Email + ".bak");
            DateTime FileWritten = File.GetLastWriteTimeUtc(Email);

            Assert.That(Session.Save().Message, Is.EqualTo("Nothing to save: Email.yaml has no changes."));
            var Again = OpenEmail(true);
            Again.Reload();
            ConfigValidation.ValidateEmail(Text(Email), true, "Email.yaml");
            Assert.That(File.ReadAllBytes(Email + ".bak"), Is.EqualTo(Backup), "nothing rewrites the backup without a save");
            Assert.That(File.GetLastWriteTimeUtc(Email + ".bak"), Is.EqualTo(BackupWritten));
            Assert.That(File.GetLastWriteTimeUtc(Email), Is.EqualTo(FileWritten));

            //a later save, in the same session and in another one
            Session.Set("smtpPort", 465);
            Assert.That(Session.Save().Message, Is.EqualTo(Exact), "this save takes no password out: the file has none");
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Removed), "the exact previous file, which holds no password");
            Assert.That(FilesHolding(Secret), Is.Empty);
            string Ported = Text(Email);
            Again.Reload();
            Again.Set("smtpUsername", "someone");
            Assert.That(Again.Save().Message, Is.EqualTo(Exact));
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Ported));
            Assert.That(FilesHolding(Secret), Is.Empty);
            AssertNothingShown(Again, "a later save");
        }

        // 36: a save that takes no password out keeps the exact previous bytes (a byte-order mark too), as before D111
        [Test]
        public void ASaveThatKeepsThePasswordOrHadNone_KeepsTheExactPreviousBytes()
        {
            Write(Email, EmailText, Bom: true);
            var Session = OpenEmail();
            Session.Set("smtpPort", 465);
            Assert.That(Session.Save().Message, Is.EqualTo(Exact));
            Assert.That(File.ReadAllBytes(Email + ".bak"), Is.EqualTo(Utf8(EmailText, Bom: true)), "the password the file still holds, byte for byte");

            //a password entered where there was none: the backup had none, so it stays exact
            File.Delete(Email + ".bak");
            Write(Email, Cleared(EmailText));
            Session = OpenEmail();
            Session.Set("smtpPassword", NewSecret);
            Assert.That(Session.Save().Message, Is.EqualTo(Exact));
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)));
            Assert.That(FilesHolding(NewSecret), Is.EqualTo(new[] { "Email.yaml" }));
        }

        // 8, 10, legacy: a backup an earlier version left with a removed password is never read, never a password's source,
        // never changed by opening, loading or checking; the next save replaces it
        [Test]
        public void AStaleBackupOfAnEarlierVersion_IsNeverRead_AndTheNextSaveReplacesIt()
        {
            string Current = Cleared(EmailText);
            Write(Email, Current);
            Write(Email + ".bak", EmailText);
            DateTime Then = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Email + ".bak", Then);
            Func<string, string?> OriginalEnvironment = SmtpPassword.GetEnvironment;
            SmtpPassword.GetEnvironment = Name => null;
            try
            {
                Assert.That(Yaml.EmailConfigConvertFromYaml(Email).SmtpPassword, Is.Empty);
                Assert.That(Yaml.EmailConfigGetConfig(Folder).SmtpPassword, Is.Empty);
                SmtpPasswordResult Used = SmtpPassword.Resolve(Yaml.EmailConfigConvertFromYaml(Email));
                Assert.That((Used.Source, Used.Password), Is.EqualTo((SmtpPasswordSource.EmailYaml, "")), "the removed password is never a source again");
                Assert.That(ConfigFiles.SearchFiles(Folder), Is.Empty, "not a settings or search file");
                var Session = OpenEmail(true);
                Assert.That(Session.OriginalValue("smtpPassword"), Is.Empty);
                AssertNothingShown(Session, "opening with a stale backup");
                Assert.That(Text(Email + ".bak"), Is.EqualTo(EmailText));
                Assert.That(File.GetLastWriteTimeUtc(Email + ".bak"), Is.EqualTo(Then), "opening, loading and checking never touch the backup");

                Session.Set("smtpPort", 465);
                Assert.That(Session.Save().Message, Is.EqualTo(Exact));
                Assert.That(Text(Email + ".bak"), Is.EqualTo(Current), "the next save of Email.yaml replaces it");
                Assert.That(FilesHolding(Secret), Is.Empty);
            }
            finally
            {
                SmtpPassword.GetEnvironment = OriginalEnvironment;
            }
        }

        // 11, 13, 14, 15: a failure at each step before the rename reports no success, leaves Email.yaml as it was (still
        // usable, the password still in it) and puts the password in no new file; the backup is the older one or the new,
        // password-free one, never the bytes that held the password. The edit stays, and saving again works
        [TestCase("temporary-writing")]
        [TestCase("temporary-flushed")]
        [TestCase("temporary-written")]
        [TestCase("temporary-verified")]
        [TestCase("backup-written")]
        [TestCase("backup-installed")]
        [TestCase("before-precondition")]
        [TestCase("before-replace")]
        public void AFailureBeforeTheRename_KeepsTheFile_AndPutsThePasswordInNoNewFile(string StepName)
        {
            string Older = EmailText.Replace(Secret, TheirSecret);
            Write(Email, EmailText);
            Write(Email + ".bak", Older);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == StepName)
                {
                    throw new IOException("injected failure at " + StepName);
                }
            };

            ConfigEditResult Failed = Session.Save();
            ConfigFileReplace.Step = (Name, Path_) => { };

            Assert.That(Failed.Succeeded, Is.False);
            Assert.That(Failed.Message, Is.EqualTo("injected failure at " + StepName));
            Assert.That(Text(Email), Is.EqualTo(EmailText), "the file as it was");
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(Text(Email)).SmtpPassword, Is.EqualTo(Secret), "still usable: nothing was removed");
            bool Installed = StepName == "backup-installed" || StepName == "before-precondition" || StepName == "before-replace";
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Installed ? Cleared(EmailText) : Older), "a whole backup: the older one, or the new one without the password");
            Assert.That(FilesHolding(Secret), Is.EqualTo(new[] { "Email.yaml" }), "only the file that still holds it");
            Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml", "Email.yaml.bak" }), "no temporary file is left");
            Assert.That(Session.IsChanged("smtpPassword"), Is.True, "the edit stays");
            AssertNothingShown(Session, "after a failed save");

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(FilesHolding(Secret), Is.Empty);
            Assert.That(FilesHolding(TheirSecret), Is.Empty, "the older backup is replaced too");
        }

        // a read-back that fails after the rename says so, and where the previous settings are, without a password
        [Test]
        public void AReadBackThatFailsAfterTheRename_IsReported_WithoutAPassword()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", NewSecret);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "after-replace")
                {
                    File.AppendAllText(Path_, "# another program\n");
                }
            };

            ConfigEditResult Failed = Session.Save();
            ConfigFileReplace.Step = (Name, Path_) => { };

            Assert.That(Failed.Succeeded, Is.False);
            Assert.That(Failed.Message, Is.EqualTo("Email.yaml was replaced, but reading it back did not give what was written; the previous content, without its plain-text password, is in Email.yaml.bak."));
            Assert.That(FilesHolding(Secret), Is.Empty);
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)));
            AssertNothingShown(Session, "after a failed read-back");
        }

        // 14: a backup that would still hold the password (it is inside another value this save changes) stops the save,
        // visibly and without the password; two saves do it. A comment of the user's that holds it is the file's own text,
        // which the saved file keeps too
        [Test]
        public void ABackupThatWouldKeepThePassword_StopsTheSave_AndTwoSavesDoIt()
        {
            string Inside = EmailText.Replace("smtpUsername: camper", "smtpUsername: " + Secret + "@example.org");
            Write(Email, Inside);
            var Session = OpenEmail();
            Session.Set("smtpUsername", "camper");
            Session.Set("smtpPassword", "");

            ConfigEditResult Refused = Session.Save();

            Assert.That(Refused.Succeeded, Is.False);
            Assert.That(Refused.Message, Is.EqualTo("Nothing was saved: the backup of Email.yaml would still hold the plain-text password, because another setting this save changes holds it too. Save the password's change on its own first, then the other changes."));
            Assert.That(Text(Email), Is.EqualTo(Inside));
            Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml" }), "nothing was written");
            AssertNothingShown(Session, "a refused backup");

            var First = OpenEmail();
            First.Set("smtpUsername", "camper");
            Assert.That(First.Save().Message, Is.EqualTo(Exact));
            First.Set("smtpPassword", "");
            Assert.That(First.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(FilesHolding(Secret), Is.Empty);

            File.Delete(Email + ".bak");
            Write(Email, "# the old password was " + Secret + "\n" + EmailText);
            var Commented = OpenEmail();
            Commented.Set("smtpPassword", "");
            Assert.That(Commented.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(FilesHolding(Secret), Is.EqualTo(new[] { "Email.yaml", "Email.yaml.bak" }));
            Assert.That((Count(Text(Email), Secret), Count(Text(Email + ".bak"), Secret)), Is.EqualTo((1, 1)), "only the comment, in both");
        }

        // 20: the backup without the password has exactly the file's mode (0600, 0640, 0644, 0400), never a broader one
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
        [TestCase(UnixFileMode.UserRead)]
        public void TheBackupWithoutThePassword_HasExactlyTheFilesMode(UnixFileMode Mode)
        {
            if (!OnUnix("TheBackupWithoutThePassword_OnWindows_HasTheFilesAccessRules"))
            {
                return;
            }
            Write(Email, EmailText);
            File.SetUnixFileMode(Email, Mode);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(File.GetUnixFileMode(Email), Is.EqualTo(Mode));
            Assert.That(File.GetUnixFileMode(Email + ".bak"), Is.EqualTo(Mode));
            Assert.That(FilesHolding(Secret), Is.Empty);
        }

        // 20 on Windows: the backup without the password takes exactly the file's access rules
        [Test]
        public void TheBackupWithoutThePassword_OnWindows_HasTheFilesAccessRules()
        {
            if (!OnWindows())
            {
                return;
            }
            var Session = OpenEmail();
            Assert.That(Session.Create().Succeeded, Is.True);
            string Rules = Sddl(Email);
            Session.Set("smtpPassword", NewSecret);
            Assert.That(Session.Save().Succeeded, Is.True);
            Session.Set("smtpPassword", "");
            ConfigEditResult Saved = Session.Save();
            Assert.That(Saved.Message, Is.EqualTo(WithoutPassword));
            Assert.That(Sddl(Email + ".bak"), Is.EqualTo(Rules));
            Assert.That(FilesHolding(NewSecret), Is.Empty);
        }

        // 22: an Email.yaml that is a link keeps its backup next to the target, without the password; a backup name that is
        // a link is replaced, never written through (a link on Windows is refused: ConfigFileReplaceWindowsTests)
        [Test]
        public void ALinkedEmailYamlOrBackupName_IsNeverWrittenThrough()
        {
            if (!OnUnix("a link is refused on Windows (ConfigFileReplaceWindowsTests)"))
            {
                return;
            }
            string Elsewhere = Path.Join(Folder, "elsewhere");
            Directory.CreateDirectory(Elsewhere);
            string Target = Path.Join(Elsewhere, "Email.yaml");
            Write(Target, EmailText);
            File.CreateSymbolicLink(Email, Target);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(new FileInfo(Email).LinkTarget, Is.EqualTo(Target), "the link stays a link");
            Assert.That(Text(Target + ".bak"), Is.EqualTo(Cleared(EmailText)), "the backup is next to the target");
            Assert.That(File.Exists(Email + ".bak"), Is.False);
            Assert.That(Directory.GetFiles(Elsewhere).Where(F => Text(F).Contains(Secret, StringComparison.Ordinal)), Is.Empty);

            File.Delete(Email);
            File.Delete(Target);
            File.Delete(Target + ".bak");
            Write(Email, EmailText);
            string Outside = Path.Join(Elsewhere, "not-campertrons.txt");
            File.WriteAllText(Outside, "someone else's file\n");
            File.CreateSymbolicLink(Email + ".bak", Outside);
            Session = OpenEmail();
            Session.Set("smtpPassword", "");
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));
            Assert.That(File.ReadAllText(Outside), Is.EqualTo("someone else's file\n"), "never written through the link");
            Assert.That(new FileInfo(Email + ".bak").LinkTarget, Is.Null, "the link is replaced by the backup");
            Assert.That(Text(Email + ".bak"), Is.EqualTo(Cleared(EmailText)));
            Assert.That(FilesHolding(Secret), Is.Empty);
        }

        // 39: a second session opened before the removal can't save over it, so nothing brings the password back
        [Test]
        public void ASecondSessionOpenedBefore_CantBringTheRemovedPasswordBack()
        {
            Write(Email, EmailText);
            var First = OpenEmail();
            var Second = OpenEmail();
            First.Set("smtpPassword", "");
            Assert.That(First.Save().Message, Is.EqualTo(WithoutPassword));

            Second.Set("smtpPort", 465);
            ConfigEditResult Late = Second.Save();

            Assert.That(Late.Succeeded, Is.False);
            Assert.That(Late.Message, Is.EqualTo("Email.yaml was changed by another program while you were editing it. Nothing was written."));
            Assert.That(Second.Conflict, Is.Not.Null);
            Assert.That(FilesHolding(Secret), Is.Empty);
            AssertNothingShown(Second, "the second session");
        }

        // 16: an Email.yaml that does not load is never saved, so it gets no backup; nothing shows its password
        [Test]
        public void ACorruptEmailYaml_IsNeverSaved_AndGetsNoBackup()
        {
            Write(Email, "smtpServer: [unclosed\nsmtpPassword: " + Secret + "\n");
            var Session = OpenEmail();
            Assert.That(Session.State, Is.Not.EqualTo(ConfigEditState.Editable));
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml" }));
            AssertNothingShown(Session, "a corrupt Email.yaml");
        }

        // 19: a folder Campertron can't write in stops the save; the file stays, no backup is made, the message holds no
        // password (the web UI also says "the settings folder" instead of its path: WebPageText, SettingsPageTests)
        [Test]
        public void AFolderThatCantBeWritten_StopsTheSave_WithoutThePassword()
        {
            if (!OnUnix("read-only folders are tested by ConfigFileReplaceWindowsTests"))
            {
                return;
            }
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                string Probe = Path.Join(Folder, "probe");
                try
                {
                    File.WriteAllText(Probe, "");
                    File.Delete(Probe);
                    Assert.Ignore("the folder's mode does not apply to this user (root)");
                }
                catch (UnauthorizedAccessException)
                {
                }
                ConfigEditResult Failed = Session.Save();
                Assert.That(Failed.Succeeded, Is.False);
                Assert.That(Failed.Message, Does.Contain("denied"));
                Assert.That(Text(Email), Is.EqualTo(EmailText));
                Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml" }));
                AssertNothingShown(Session, "an unwritable folder");
                Assert.That(Failed.Message, Does.Not.Contain(Secret));
            }
            finally
            {
                File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        // 17, 18, 21: a password file that is missing, empty or broken does not stop the removal of the plain-text password
        // (it is never opened by the session), and the save never creates, writes or chmods it
        [TestCase("missing")]
        [TestCase("empty")]
        [TestCase("byte-order mark")]
        public void APasswordFileThatCantBeUsed_DoesNotStopTheRemoval_AndIsNeverTouched(string Kind)
        {
            string PasswordFile = Path.Join(Folder, "secrets", "smtp_password");
            Directory.CreateDirectory(Path.GetDirectoryName(PasswordFile)!);
            if (Kind != "missing")
            {
                File.WriteAllBytes(PasswordFile, Kind == "empty" ? Array.Empty<byte>() : new byte[] { 0xEF, 0xBB, 0xBF, (byte)'x' });
            }
            byte[]? Before = Kind == "missing" ? null : File.ReadAllBytes(PasswordFile);
            Write(Email, EmailText + "smtpPasswordFile: '" + PasswordFile + "'\n");
            var Session = OpenEmail();
            Session.Set("smtpPassword", "");

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutPassword));

            Assert.That(FilesHolding(Secret), Is.Empty);
            Assert.That(File.Exists(PasswordFile), Is.EqualTo(Kind != "missing"), "never created");
            if (Before != null)
            {
                Assert.That(File.ReadAllBytes(PasswordFile), Is.EqualTo(Before), "never written");
            }
            AssertNothingShown(Session, "a password file that can't be used");
        }
    }
}
