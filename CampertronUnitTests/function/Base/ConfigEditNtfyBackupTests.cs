using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // D112 (business-rules-settings §14, §28): a save of Ntfy.yaml that replaces or removes the topic it holds never leaves
    // that topic in Ntfy.yaml.bak, as D111 does for Email.yaml's plain-text password. The backup is then the previous
    // settings with `topic: ''`, which a rollback can still use for every other setting; no file of the settings folder
    // holds a topic the saved file no longer has. Part of ConfigEditNtfyTests (one class, so the Windows CI job's filter
    // runs it natively). The topics are synthetic canaries, searched for in every file and everything the session shows.
    public partial class ConfigEditNtfyTests
    {
        private const string WithoutTopic = "Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak, without its topic.";
        private const string ExactNtfy = "Saved Ntfy.yaml. The previous version is in Ntfy.yaml.bak.";

        // the file a user has: ntfy on, the topic with a comment, the other settings to roll back to
        private string NtfyText(string TopicValue) =>
            "# ntfy (LOCAL TEST)\nenabled: true\nserver: https://ntfy.example.invalid\ntopic: " + TopicValue + " # private\npriority: high\ntags:\n  - tent\ntimeoutSeconds: 20\n";

        private string ClearedTopic(string Text, string TopicValue) => Text.Replace("topic: " + TopicValue, "topic: ''");

        private string[] NtfyFilesHolding(string Value) =>
            Directory.GetFiles(Folder).Where(F => Text(F).Contains(Value, StringComparison.Ordinal)).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToArray()!;

        private static string AnotherTopic() => "unitNewTopicCnry" + Guid.NewGuid().ToString("N").Substring(0, 20);

        // 2, 5, 6: a typed topic replaces the old one; the backup keeps every other previous setting, without the old topic
        [Test]
        public void ReplacingTheTopic_LeavesTheOldOneOutOfTheBackup_WhichKeepsThePreviousSettings()
        {
            string Next = AnotherTopic();
            Write(Ntfy, NtfyText(Topic));
            var Session = OpenNtfy();
            Session.Set("topic", Next);
            Session.Set("timeoutSeconds", 45);

            ConfigEditResult Saved = Session.Save();

            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Saved.Message, Is.EqualTo(WithoutTopic));
            Assert.That(Text(Ntfy), Is.EqualTo(NtfyText(Next).Replace("timeoutSeconds: 20", "timeoutSeconds: 45")));
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(ClearedTopic(NtfyText(Topic), Topic)), "the previous settings, comments and all, without the topic");
            NtfyConfig Backup = Yaml.LoadSettingsText<NtfyConfig>(Text(Ntfy + ".bak"));
            Assert.That((Backup.Enabled, Backup.Server, Backup.Topic, Backup.Priority, Backup.TimeoutSeconds), Is.EqualTo((true, "https://ntfy.example.invalid", "", NtfyPriority.High, 20)));
            Assert.That(NtfyFilesHolding(Topic), Is.Empty, "neither the file, its backup nor a temporary file holds the old topic");
            Assert.That(NtfyFilesHolding(Next), Is.EqualTo(new[] { "Ntfy.yaml" }), "the new topic only in the file");
            Assert.That(Files(), Is.EqualTo(new[] { "Ntfy.yaml", "Ntfy.yaml.bak" }));
            AssertNoTopic(Session, "after replacing the topic", Next);
        }

        // 22: a random topic (Generate a random topic's NtfyTopic) saved the same way
        [Test]
        public void ARandomTopic_SavedOverTheOldOne_LeavesTheOldOneOutOfTheBackup()
        {
            Write(Ntfy, NtfyText(Topic));
            var Session = OpenNtfy();
            string Random = NtfyTopic.New();
            Assert.That(NtfyEndpoint.IsValidTopic(Random), Is.True);
            Session.Set("topic", Random);

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Text(Ntfy)).Topic, Is.EqualTo(Random));
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
            Assert.That(NtfyFilesHolding(Random), Is.EqualTo(new[] { "Ntfy.yaml" }));
        }

        // 3: with ntfy off the topic can be cleared; the backup keeps the previous settings without it
        [Test]
        public void RemovingTheTopic_WithNtfyOff_LeavesItOutOfTheBackup()
        {
            Write(Ntfy, NtfyText(Topic));
            var Session = OpenNtfy();
            Session.Set("enabled", false);
            Session.Set("topic", "");

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Text(Ntfy)).Topic, Is.Empty);
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(ClearedTopic(NtfyText(Topic), Topic)), "the previous settings (ntfy on), without the topic");
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
            AssertNoTopic(Session, "after removing the topic");
        }

        // 4, 26: no later save brings the topic back; without a change nothing is written; reading writes nothing
        [Test]
        public void ALaterSave_NeverBringsTheTopicBack_AndNothingIsWrittenWithoutAChange()
        {
            string Next = AnotherTopic();
            Write(Ntfy, NtfyText(Topic));
            var Session = OpenNtfy();
            Session.Set("topic", Next);
            Assert.That(Session.Save().Succeeded, Is.True);
            string Replaced = Text(Ntfy);
            byte[] Backup = File.ReadAllBytes(Ntfy + ".bak");
            DateTime BackupWritten = File.GetLastWriteTimeUtc(Ntfy + ".bak");

            Assert.That(Session.Save().Message, Is.EqualTo("Nothing to save: Ntfy.yaml has no changes."));
            var Again = OpenNtfy(Required: true);
            Again.Reload();
            ConfigValidation.ValidateNtfy(Text(Ntfy), true);
            Assert.That(File.ReadAllBytes(Ntfy + ".bak"), Is.EqualTo(Backup));
            Assert.That(File.GetLastWriteTimeUtc(Ntfy + ".bak"), Is.EqualTo(BackupWritten), "nothing rewrites the backup without a save");

            Session.Set("timeoutSeconds", 60);
            Assert.That(Session.Save().Message, Is.EqualTo(ExactNtfy), "this save takes no topic out");
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(Replaced), "the exact previous file: the topic the saved file still holds");
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
            AssertNoTopic(Session, "a later save", Next);
        }

        // 7, 25: a save that keeps the topic keeps the exact previous bytes (a byte-order mark too), as before D112
        [Test]
        public void ASaveThatKeepsTheTopic_KeepsTheExactPreviousBytes()
        {
            Write(Ntfy, NtfyText(Topic), Bom: true);
            var Session = OpenNtfy();
            Session.Set("server", "https://ntfy.example.net");
            Assert.That(Session.Save().Message, Is.EqualTo(ExactNtfy));
            Assert.That(File.ReadAllBytes(Ntfy + ".bak"), Is.EqualTo(Utf8(NtfyText(Topic), Bom: true)), "the topic the saved file still holds, byte for byte");
        }

        // 8, 9, legacy: a backup an earlier version left with a replaced topic is never read (the loader, the session and the
        // validation take Ntfy.yaml only), never changed by opening, and replaced by the next save; copied back by hand it
        // restores the previous settings, the old topic too (the user's own restore)
        [Test]
        public void AStaleBackupOfAnEarlierVersion_IsNeverRead_AndTheNextSaveReplacesIt()
        {
            string Current = AnotherTopic();
            Write(Ntfy, NtfyText(Current));
            Write(Ntfy + ".bak", NtfyText(Topic));
            DateTime Then = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Ntfy + ".bak", Then);

            Assert.That(Yaml.NtfyConfigGetConfig(Folder)!.Topic, Is.EqualTo(Current));
            Assert.That(ConfigValidation.ValidateNtfyFile(Folder, Now, OutputType.Ntfy), Has.None.Matches<ConfigDiagnostic>(D => D.Message.Contains(Topic, StringComparison.Ordinal)));
            var Session = OpenNtfy();
            Assert.That(Session.OriginalValue("topic"), Is.EqualTo(Current));
            Assert.That(File.GetLastWriteTimeUtc(Ntfy + ".bak"), Is.EqualTo(Then), "opening, loading and checking never touch the backup");

            Session.Set("timeoutSeconds", 50);
            Assert.That(Session.Save().Message, Is.EqualTo(ExactNtfy));
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(NtfyText(Current)), "the next save of Ntfy.yaml replaces it");
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
        }

        // 10, 11, 12, 13: a failure at each step before the rename reports no success, leaves Ntfy.yaml as it was (still
        // usable, the old topic still in it) and puts the old topic in no new file; the edit stays, and saving again works
        [TestCase("temporary-writing")]
        [TestCase("temporary-written")]
        [TestCase("temporary-verified")]
        [TestCase("backup-written")]
        [TestCase("backup-installed")]
        [TestCase("before-precondition")]
        [TestCase("before-replace")]
        public void AFailureBeforeTheRename_KeepsTheFile_AndPutsTheTopicInNoNewFile(string StepName)
        {
            string Older = "olderTopicCnry" + Guid.NewGuid().ToString("N").Substring(0, 16);
            string Next = AnotherTopic();
            Write(Ntfy, NtfyText(Topic));
            Write(Ntfy + ".bak", NtfyText(Older));
            var Session = OpenNtfy();
            Session.Set("topic", Next);
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
            Assert.That(Text(Ntfy), Is.EqualTo(NtfyText(Topic)), "the file as it was");
            bool Installed = StepName == "backup-installed" || StepName == "before-precondition" || StepName == "before-replace";
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(Installed ? ClearedTopic(NtfyText(Topic), Topic) : NtfyText(Older)), "a whole backup: the older one or the new one without the topic");
            Assert.That(NtfyFilesHolding(Topic), Is.EqualTo(new[] { "Ntfy.yaml" }), "only the file that still holds it");
            Assert.That(NtfyFilesHolding(Next), Is.Empty, "the entered topic was never saved anywhere");
            Assert.That(Files(), Is.EqualTo(new[] { "Ntfy.yaml", "Ntfy.yaml.bak" }), "no temporary file is left");

            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
            Assert.That(NtfyFilesHolding(Older), Is.Empty, "the older backup is replaced too");
            AssertNoTopic(Session, "after a failed save", Next, Older);
        }

        // 10: a backup that would still hold the topic (it is inside another value this save changes) stops the save,
        // visibly and without the topic; two saves do it. A comment of the user's that holds it stays in both files
        [Test]
        public void ABackupThatWouldKeepTheTopic_StopsTheSave_AndTwoSavesDoIt()
        {
            string Next = AnotherTopic();
            string Inside = NtfyText(Topic).Replace("server: https://ntfy.example.invalid", "server: https://" + Topic + ".example.invalid");
            Write(Ntfy, Inside);
            var Session = OpenNtfy();
            Session.Set("server", "https://ntfy.example.net");
            Session.Set("topic", Next);

            ConfigEditResult Refused = Session.Save();

            Assert.That(Refused.Succeeded, Is.False);
            Assert.That(Refused.Message, Is.EqualTo("Nothing was saved: the backup of Ntfy.yaml would still hold the topic, because another setting this save changes holds it too. Save the other changes first, then the topic's change on its own."));
            Assert.That(Text(Ntfy), Is.EqualTo(Inside));
            Assert.That(Files(), Is.EqualTo(new[] { "Ntfy.yaml" }), "nothing was written");
            AssertNoTopic(Session, "a refused backup", Next);

            //as the message says: the other change first (its exact backup: the file still holds the topic), then the topic's
            var First = OpenNtfy();
            First.Set("server", "https://ntfy.example.net");
            Assert.That(First.Save().Message, Is.EqualTo(ExactNtfy));
            First.Set("topic", Next);
            Assert.That(First.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(NtfyFilesHolding(Topic), Is.Empty, "neither file holds the old topic");

            File.Delete(Ntfy + ".bak");
            Write(Ntfy, "# the old topic was " + Topic + "\n" + NtfyText(Topic));
            var Commented = OpenNtfy();
            Commented.Set("topic", Next);
            Assert.That(Commented.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(NtfyFilesHolding(Topic), Is.EqualTo(new[] { "Ntfy.yaml", "Ntfy.yaml.bak" }), "only the user's comment, in both");
        }

        // 14: the backup without the topic has exactly the file's mode (0600, 0640, 0644), never a broader one
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead)]
        [TestCase(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
        public void TheBackupWithoutTheTopic_HasExactlyTheFilesMode(UnixFileMode Mode)
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions (on Windows: TheBackupWithoutTheTopic_OnWindows_HasTheFilesAccessRules)");
                return;
            }
            Write(Ntfy, NtfyText(Topic));
            File.SetUnixFileMode(Ntfy, Mode);
            var Session = OpenNtfy();
            Session.Set("topic", AnotherTopic());
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(File.GetUnixFileMode(Ntfy), Is.EqualTo(Mode));
            Assert.That(File.GetUnixFileMode(Ntfy + ".bak"), Is.EqualTo(Mode));
        }

        // 14 on Windows: the backup without the topic takes exactly the file's access rules
        [Test]
        public void TheBackupWithoutTheTopic_OnWindows_HasTheFilesAccessRules()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only (run natively by the Windows CI job)");
                return;
            }
            Write(Ntfy, NtfyText(Topic));
            string Rules = Sddl(Ntfy);
            var Session = OpenNtfy();
            Session.Set("topic", AnotherTopic());
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(Sddl(Ntfy + ".bak"), Is.EqualTo(Rules));
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
        }

        // 15: a Ntfy.yaml that is a link keeps its backup next to the target; a backup name that is a link is replaced,
        // never written through (a link on Windows is refused: ConfigFileReplaceWindowsTests)
        [Test]
        public void ALinkedNtfyYamlOrBackupName_IsNeverWrittenThrough()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("a link is refused on Windows (ConfigFileReplaceWindowsTests)");
                return;
            }
            string Elsewhere = Path.Join(Folder, "elsewhere");
            Directory.CreateDirectory(Elsewhere);
            string Target = Path.Join(Elsewhere, "Ntfy.yaml");
            Write(Target, NtfyText(Topic));
            File.CreateSymbolicLink(Ntfy, Target);
            var Session = OpenNtfy();
            Session.Set("topic", AnotherTopic());
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(new FileInfo(Ntfy).LinkTarget, Is.EqualTo(Target));
            Assert.That(Text(Target + ".bak"), Is.EqualTo(ClearedTopic(NtfyText(Topic), Topic)));
            Assert.That(File.Exists(Ntfy + ".bak"), Is.False);

            File.Delete(Ntfy);
            File.Delete(Target);
            File.Delete(Target + ".bak");
            Write(Ntfy, NtfyText(Topic));
            string Outside = Path.Join(Elsewhere, "not-campertrons.txt");
            File.WriteAllText(Outside, "someone else's file\n");
            File.CreateSymbolicLink(Ntfy + ".bak", Outside);
            Session = OpenNtfy();
            Session.Set("topic", AnotherTopic());
            Assert.That(Session.Save().Message, Is.EqualTo(WithoutTopic));
            Assert.That(File.ReadAllText(Outside), Is.EqualTo("someone else's file\n"), "never written through the link");
            Assert.That(new FileInfo(Ntfy + ".bak").LinkTarget, Is.Null);
            Assert.That(NtfyFilesHolding(Topic), Is.Empty);
        }

        // 28, 29: each file has its own backup secret, and only its own: Email.yaml's plain-text password (D111),
        // Ntfy.yaml's topic (D112), none for General.yaml, Web.yaml and a search file, whose saves keep the exact bytes
        [Test]
        public void TheBackupSecret_IsEachFilesOwn_AndTheOtherFilesKeepTheirExactBackups()
        {
            Assert.That(new ConfigEditSession<EmailConfig>(Folder, () => Now).BackupSecretKey, Is.EqualTo("smtpPassword"));
            Assert.That(OpenNtfy().BackupSecretKey, Is.EqualTo("topic"));
            Assert.That(new ConfigEditSession<GeneralConfig>(Folder, () => Now).BackupSecretKey, Is.Null);
            Assert.That(new ConfigEditSession<WebConfig>(Folder, () => Now).BackupSecretKey, Is.Null);

            //a General.yaml and a Web.yaml save keep the exact bytes, whatever text they hold
            string GeneralText = "# the topic " + Topic + " in a comment\noutputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n";
            Write(General, GeneralText);
            var G = new ConfigEditSession<GeneralConfig>(Folder, () => Now);
            G.Set("refreshRidbDataDayInterval", 20.0);
            Assert.That(G.Save().Message, Is.EqualTo("Saved General.yaml. The previous version is in General.yaml.bak."));
            Assert.That(Text(General + ".bak"), Is.EqualTo(GeneralText));
            string WebText = "# " + Topic + "\npasswordProtection: true\nnetworkListening: true\n";
            Write(Path.Join(Folder, "Web.yaml"), WebText);
            var W = new ConfigEditSession<WebConfig>(Folder, () => Now);
            W.Set("networkListening", false);
            Assert.That(W.Save().Message, Is.EqualTo("Saved Web.yaml. The previous version is in Web.yaml.bak."));
            Assert.That(Text(Path.Join(Folder, "Web.yaml.bak")), Is.EqualTo(WebText));
            //a search file has none either
            string SearchText = "displayName: " + Topic + "\nautoRun: false\ncampgroundID: '232445'\ntotalHumans: 2\nsearchBy: StartEndDate\nsearchValue: 0\nsearchValueDates:\n- 2027-05-03\n- 2027-05-16\nconsecutiveDays: 1\n";
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            var S = new ConfigEditSession<CampertronConfig>(Folder, "Trip.yaml", () => Now, null);
            Assert.That(S.BackupSecretKey, Is.Null);
            S.Set("displayName", "Another name");
            Assert.That(S.Save().Message, Is.EqualTo("Saved Trip.yaml. The previous version is in Trip.yaml.bak."));
            Assert.That(Text(Path.Join(Folder, "Trip.yaml.bak")), Is.EqualTo(SearchText));
        }

        // 27, 29: Email.yaml keeps D111's words and rule with the shared code
        [Test]
        public void EmailYaml_KeepsD111sRuleAndWords()
        {
            const string Password = "D112-email-cnry-91aa";
            string EmailText = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: user\nsmtpPassword: " + Password + "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
            Write(Email, EmailText);
            var Session = new ConfigEditSession<EmailConfig>(Folder, () => Now);
            Session.Set("smtpPassword", "");
            Assert.That(Session.Save().Message, Is.EqualTo("Saved Email.yaml. The previous version is in Email.yaml.bak, without its plain-text password."));
            Assert.That(Text(Email + ".bak"), Is.EqualTo(EmailText.Replace(Password, "''")));
        }
    }
}
