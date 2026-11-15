using System.Reflection;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // The settings editing session (CM5, decision D43; business-rules §17) on General.yaml and Email.yaml: opening (missing,
    // empty, comment-only, malformed, unknown keys, unsupported layouts), edits and dirty state, revert and reload, the
    // verified save through the CM2 writer (backup, permissions, comments, line breaks, BOM), failures that write nothing,
    // external changes and conflicts, and the preview.
    public class ConfigEditSessionTests : ConfigEditTestBase
    {
        private const string GeneralText = "# Campertron settings\noutputTo: Console   # where results go\n\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC\nautoRefresh: false\n# end\n";

        private const string EmailText = "# SMTP settings\nsmtpServer: 'mail.example.org' # host\nsmtpPort: 587\nsmtpUsername: camper\n" +
            "smtpPassword: S3ntinel-Secret-9f2c\nsendToAddressList:\n  # family\n  - first@example.org # one\n  - second@example.org\nsendFromAddress: camper@example.org\n";

        // 1
        [Test]
        public void GeneralYaml_OpensWithItsSettings_AndNothingEdited()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();

            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            Assert.That(Session.Keys, Is.EqualTo(new[] { "outputTo", "refreshRidbDataDayInterval", "lastRidbDataRefresh", "autoRefresh" }));
            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.Console));
            Assert.That(Session.Value("refreshRidbDataDayInterval"), Is.EqualTo(30d));
            Assert.That(Session.Value("lastRidbDataRefresh"), Is.EqualTo(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.That(((DateTime)Session.Value("lastRidbDataRefresh")!).Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(Session.Value("autoRefresh"), Is.EqualTo(false));
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Candidate, Is.Null);
            Assert.That(Session.Preview, Is.Empty);
            Assert.That(Session.Keys.Select(Session.LockReason), Is.All.Null);
        }

        // 2
        [Test]
        public void EmailYaml_OpensWithItsSettings()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();

            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            Assert.That(Session.Keys, Is.EqualTo(new[] { "smtpServer", "smtpPort", "smtpUsername", "smtpPassword", "sendToAddressList", "sendFromAddress" }));
            Assert.That(Session.Value("smtpServer"), Is.EqualTo("mail.example.org"));
            Assert.That(Session.Value("smtpPort"), Is.EqualTo(587));
            Assert.That(Session.Items("sendToAddressList").Select(R => (R.OriginalIndex, R.Value)),
                Is.EqualTo(new (int?, string)[] { (0, "first@example.org"), (1, "second@example.org") }));
            Assert.That(Session.SecretKeys, Is.EqualTo(new[] { "smtpPassword" }));
            Assert.That(Session.IsList("sendToAddressList"), Is.True);
        }

        // 3 (and the create-only sample)
        [Test]
        public void AMissingFile_IsMissing_AndCreateWritesTheSampleOnlyIfTheNameIsFree()
        {
            var Session = OpenGeneral();
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Missing));
            Assert.That(Session.StateReason, Is.EqualTo("General.yaml does not exist."));
            Assert.That(() => Session.Set("autoRefresh", true), Throws.InvalidOperationException);
            Assert.That(Session.Save().Succeeded, Is.False);

            ConfigEditResult Created = Session.Create();
            Assert.That(Created.Succeeded, Is.True, Created.Message);
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            // the serializer's text, as the start writes it (with the platform's line breaks: CRLF on Windows)
            Assert.That(Text(General).Replace("\r\n", "\n"), Does.StartWith("outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: "));
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml" }));

            // created by another program while Create writes: never written over, opened as it is
            var EmailSession = OpenEmail();
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    Write(Email, "smtpServer: theirs\n");
                }
            };
            ConfigEditResult Raced = EmailSession.Create();
            Assert.That(Raced.Succeeded, Is.False);
            Assert.That(Raced.Message, Is.EqualTo("Email.yaml was created by another program meanwhile; it was opened as it is."));
            Assert.That(Text(Email), Is.EqualTo("smtpServer: theirs\n"));
            Assert.That(EmailSession.Value("smtpServer"), Is.EqualTo("theirs"));
            Assert.That(Files(), Is.EqualTo(new[] { "Email.yaml", "General.yaml" }));
        }

        // 4, 5, 6, 7, 8
        [TestCase("", "Unreadable", "General.yaml is empty or is not a regular file; it was left as it is.", null)]
        [TestCase("# only a comment\n", "ReadOnly", "can't be edited safely here", "Empty")]
        [TestCase("outputTo: [Console\nautoRefresh: false\n", "ReadOnly", "can't be edited safely here", "Malformed")]
        [TestCase("outputTo: Console\ntheme: dark\n", "ReadOnly", "Campertron can't read its settings", "UnknownKey")]
        [TestCase("{outputTo: Console}\n", "ReadOnly", "can't be edited safely here", null)]
        [TestCase("outputTo: Console\noutputTo: Email\n", "ReadOnly", "can't be edited safely here", "DuplicateKey")]
        public void FilesThatCantBeEditedSafely_OpenReadOnly_WithAReason(string Content, string ExpectedState, string Reason, string? Code)
        {
            var Expected = Enum.Parse<ConfigEditState>(ExpectedState);
            Write(General, Content);
            byte[] Before = File.ReadAllBytes(General);
            var Session = OpenGeneral();

            Assert.That(Session.State, Is.EqualTo(Expected));
            Assert.That(Session.StateReason, Does.Contain(Reason));
            if (Code != null)
            {
                Assert.That(Session.Notices.Select(N => N.Code), Does.Contain(Code));
            }
            Assert.That(() => Session.Set("autoRefresh", true), Throws.InvalidOperationException);
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllBytes(General), Is.EqualTo(Before));
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml" }));
        }

        // 8: a field with an anchor, a tag or a multi-line value is locked, the rest stays editable
        [Test]
        public void AFieldWrittenInAFormTheEditorKeeps_IsLocked_TheOthersStayEditable()
        {
            Write(Email, "smtpServer: &host mail.example.org\nsmtpPort: !!int 587\nsmtpPassword: |\n  multi\n  line\nsmtpUsername: camper\n");
            var Session = OpenEmail();

            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));
            Assert.That(Session.LockReason("smtpServer"), Does.Contain("anchor"));
            Assert.That(Session.LockReason("smtpPort"), Does.Contain("tag"));
            Assert.That(Session.LockReason("smtpPassword"), Does.Contain("multi-line"));
            Assert.That(Session.LockReason("smtpUsername"), Is.Null);
            Assert.That(Session.LockReason("sendFromAddress"), Is.Null, "a missing key can be added");
            Session.Set("smtpUsername", "someone");
            Assert.That(Session.Candidate, Does.Contain("smtpUsername: someone\n"));
            Session.Set("smtpServer", "other.example.org");
            Assert.That(Session.CanSave, Is.False, "the editor refuses the locked field");
            Assert.That(Session.Notices.Any(N => N.Source == ConfigEditSource.Editor && N.Key == "smtpServer" && N.Severity == ConfigSeverity.Error), Is.True);
        }

        // 9, 10, 11
        [Test]
        public void DirtyState_IsTrackedPerField_AndRevertsPerFieldAndForTheFile()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();

            Session.Set("autoRefresh", true);
            Session.Set("refreshRidbDataDayInterval", 14.5);
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "refreshRidbDataDayInterval", "autoRefresh" }));
            Assert.That(Session.IsChanged("outputTo"), Is.False);
            // the value the file holds is no edit
            Session.Set("refreshRidbDataDayInterval", 30d);
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "autoRefresh" }));
            Session.Set("refreshRidbDataDayInterval", 14.5);
            Session.Revert("autoRefresh");
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "refreshRidbDataDayInterval" }));
            Assert.That(Session.Value("autoRefresh"), Is.EqualTo(false));
            Session.RevertAll();
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Candidate, Is.Null);
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
        }

        // 12
        [Test]
        public void Reload_DiscardsTheEdits_AndShowsTheFileAsItIs()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Write(General, GeneralText.Replace("outputTo: Console", "outputTo: HtmlFile"));

            Session.Reload();

            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.HtmlFile));
            Assert.That(Session.Value("autoRefresh"), Is.EqualTo(false));
        }

        // 13
        [Test]
        public void NoEdit_WritesNothing()
        {
            Write(General, GeneralText);
            DateTime Time = File.GetLastWriteTimeUtc(General);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Session.Set("autoRefresh", false);

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Is.EqualTo("Nothing to save: General.yaml has no changes."));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
            Assert.That(File.GetLastWriteTimeUtc(General), Is.EqualTo(Time));
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml" }), "no backup, no temporary file");
        }

        // 14, 33: only the edited values change; comments, blank lines, quoting, CRLF and the BOM stay; one exact backup
        [Test]
        public void Save_WritesOnlyTheEditedValues_KeepsEverythingElse_AndBacksUpTheExactOldBytes([Values(false, true)] bool CrLfAndBom)
        {
            string Original = CrLfAndBom ? GeneralText.Replace("\n", "\r\n") : GeneralText;
            Write(General, Original, CrLfAndBom);
            byte[] OldBytes = File.ReadAllBytes(General);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(General, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            }
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Session.Set("refreshRidbDataDayInterval", 7d);
            Session.Set("outputTo", OutputType.HtmlFile);
            string Reviewed = Session.Candidate!;

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.True, Result.Message);
            Assert.That(Result.Message, Is.EqualTo("Saved General.yaml. The previous version is in General.yaml.bak."));
            string Expected = Original.Replace("outputTo: Console", "outputTo: HtmlFile").Replace("DayInterval: 30", "DayInterval: 7").Replace("autoRefresh: false", "autoRefresh: true");
            Assert.That(File.ReadAllBytes(General), Is.EqualTo(Utf8(Expected, CrLfAndBom)));
            Assert.That(Reviewed, Is.EqualTo((CrLfAndBom ? "\uFEFF" : "") + Expected), "the reviewed text is what was written");
            Assert.That(File.ReadAllBytes(General + ".bak"), Is.EqualTo(OldBytes));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(General), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
                Assert.That(File.GetUnixFileMode(General + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
            }
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Value("autoRefresh"), Is.EqualTo(true));
            Assert.That(Session.Sha256, Is.EqualTo(System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(General)))));
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml", "General.yaml.bak" }));
            // 31: the timestamp was not edited, so its text is exactly as it was
            Assert.That(Text(General), Does.Contain("lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC"));
        }

        // 15: the writer's verification fails (the temporary copy is tampered with): nothing is written, the edits stay
        [Test]
        public void AFailedVerification_WritesNothing_AndKeepsTheEdits()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "temporary-written" && !Path_.Contains(".bak.", StringComparison.Ordinal))
                {
                    File.AppendAllText(Path_, "autoRefresh: false\n");
                }
            };

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Does.Contain("did not read back as written"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
            Assert.That(Session.IsDirty, Is.True);
            Assert.That(Session.CanSave, Is.True);
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml" }));
        }

        // 15: an edit the YAML document editor can't make safely blocks the save
        [Test]
        public void AnEditTheEditorRefuses_BlocksTheSave()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpUsername", "\uD800lone surrogate");

            Assert.That(Session.Candidate, Is.Null);
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Notices.Single(N => N.Source == ConfigEditSource.Editor).Key, Is.EqualTo("smtpUsername"));
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText));
        }

        // 16
        [Test]
        public void AFailedReplacement_KeepsTheFileAndTheEdits()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("General.yaml could not be replaced (injected).");
                }
            };

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Is.EqualTo("General.yaml could not be replaced (injected)."));
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText));
            Assert.That(Session.IsDirty, Is.True);
            Assert.That(Session.Conflict, Is.Null);
            ConfigFileReplace.Step = (Name, Path_) => { };
            Assert.That(Session.Save().Succeeded, Is.True, "the edits can be saved again");
        }

        // 17
        [Test]
        public void AReadBackMismatchAfterTheReplacement_IsReported()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "after-replace")
                {
                    File.AppendAllText(Path_, "# changed right after the rename\n");
                }
            };

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Is.EqualTo("General.yaml was replaced, but reading it back did not give what was written; the previous content is in General.yaml.bak."));
            Assert.That(File.ReadAllText(General + ".bak"), Is.EqualTo(GeneralText));
            Assert.That(Session.IsDirty, Is.True, "nothing is reported as saved");
            Assert.That(Session.Conflict, Is.Not.Null, "the file is not the version the edits were made on");
        }

        // 18
        [Test]
        public void AFileChangedSinceItWasRead_IsNeverOverwritten()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            string Theirs = GeneralText.Replace("outputTo: Console", "outputTo: Email");
            Write(General, Theirs);

            ConfigEditResult Result = Session.Save();

            Assert.That(Result.Succeeded, Is.False);
            Assert.That(Result.Message, Is.EqualTo("General.yaml was changed by another program while you were editing it. Nothing was written."));
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs));
            Assert.That(Session.Conflict!.Kind, Is.EqualTo(ConfigConflictKind.Changed));
            Assert.That(Session.IsDirty, Is.True);

            // also when the change comes between the writer's own checks (the CM2 precondition)
            Session.ApplyEditsToExternalVersion(false);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-precondition")
                {
                    Write(General, Theirs + "# later\n");
                }
            };
            ConfigEditResult Later = Session.Save();
            Assert.That(Later.Succeeded, Is.False);
            Assert.That(Later.Message, Does.Contain("was changed by another program while Campertron was updating it"));
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs + "# later\n"));
            Assert.That(Session.Conflict, Is.Not.Null);
        }

        // 12, 19: clean sessions follow the file; an edit of another setting is applied to the new version on request
        [Test]
        public void ExternalChanges_ReloadACleanSession_AndOtherSettingsKeepBothChanges()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.None));
            Write(General, GeneralText.Replace("# end", "# end (theirs)"));
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Reloaded));
            Assert.That(Session.IsDirty, Is.False);

            Session.Set("autoRefresh", true);
            string Theirs = GeneralText.Replace("# end", "# end (theirs)").Replace("outputTo: Console ", "outputTo: HtmlFile");
            Write(General, Theirs);
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            ConfigConflict Conflict = Session.Conflict!;
            Assert.That(Conflict.OverlappingKeys, Is.Empty);
            Assert.That(Conflict.CanApplyEdits, Is.True);
            Assert.That(Conflict.Message, Does.Contain("in other settings than yours"));
            Assert.That(Session.CanSave, Is.False);
            // not resolved: nothing changes, also after another check (conflict cancellation)
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs));

            ConfigEditResult Applied = Session.ApplyEditsToExternalVersion(false);
            Assert.That(Applied.Succeeded, Is.True, Applied.Message);
            Assert.That(Session.Conflict, Is.Null);
            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.HtmlFile), "their change is the new base");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs.Replace("autoRefresh: false", "autoRefresh: true")));
            Assert.That(File.ReadAllText(General + ".bak"), Is.EqualTo(Theirs), "their version is the backup");
        }

        // 20
        [Test]
        public void TheSameSettingChangedOnBothSides_NeedsAnExplicitChoice()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("outputTo", OutputType.HtmlFile);
            string Theirs = GeneralText.Replace("outputTo: Console ", "outputTo: Email   ");
            Write(General, Theirs);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.EqualTo(new[] { "outputTo" }));
            Assert.That(Session.Conflict.Message, Does.Contain("Both changed: outputTo."));
            ConfigEditResult Refused = Session.ApplyEditsToExternalVersion(false);
            Assert.That(Refused.Succeeded, Is.False);
            Assert.That(Session.Conflict, Is.Not.Null);
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs));

            Assert.That(Session.ApplyEditsToExternalVersion(true).Succeeded, Is.True);
            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.HtmlFile));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(General), Is.EqualTo(Theirs.Replace("outputTo: Email", "outputTo: HtmlFile")), "their spacing and comment, my value");
            Assert.That(File.ReadAllText(General + ".bak"), Is.EqualTo(Theirs));

            // the other way: their version wins and the edits are discarded
            Session.Set("autoRefresh", true);
            Write(General, Theirs);
            Session.CheckForExternalChange();
            Session.UseExternalVersion();
            Assert.That(Session.IsDirty, Is.False);
            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.Email));
        }

        // 21
        [Test]
        public void ADeletedFile_IsMissingWhenClean_AndAConflictWhenEdited()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            File.Delete(General);
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Deleted));
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Missing));
            Write(General, GeneralText);
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Reloaded), "recreated by another program");
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Editable));

            Session.Set("autoRefresh", true);
            File.Delete(General);
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.Kind, Is.EqualTo(ConfigConflictKind.Deleted));
            Assert.That(Session.Conflict.Message, Is.EqualTo("General.yaml was deleted by another program while you were editing it. Your changes are kept here; nothing was written."));
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.Exists(General), Is.False);

            // recreated by another program meanwhile: their file is never written over
            Write(General, "outputTo: Email\n");
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.Kind, Is.EqualTo(ConfigConflictKind.Changed));
            File.Delete(General);
            Session.CheckForExternalChange();
            ConfigEditResult Recreated = Session.RecreateWithEdits();
            Assert.That(Recreated.Succeeded, Is.True, Recreated.Message);
            Assert.That(File.ReadAllText(General), Is.EqualTo(GeneralText.Replace("autoRefresh: false", "autoRefresh: true")));
            Assert.That(Session.IsDirty, Is.False);
        }

        // 22
        [Test]
        public void APermissionChange_KeepsTheEdits_AndTheSaveKeepsTheNewPermissions()
        {
            if (!OnUnix("ConfigEditEmailTests.TheAccessRulesOnWindows_AreNeverBroadened"))
            {
                return;
            }
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            File.SetUnixFileMode(General, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.MetadataChanged));
            Assert.That(Session.Conflict, Is.Null);
            Assert.That(Session.IsDirty, Is.True);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.GetUnixFileMode(General), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(General + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));

            // a permission change between the session's check and the writer's own checks stops the write
            Session.Set("autoRefresh", false);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                // (the analyzer's platform guard does not reach into a lambda)
                if (Name == "before-precondition" && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(General, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
                }
            };
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllText(General), Does.Contain("autoRefresh: true"));
        }

        // 23
        [Test]
        public void ALineBreakOrByteOrderMarkChange_IsAContentChange_ThatTheEditsApplyTo()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Write(General, GeneralText.Replace("\n", "\r\n"), Bom: true);

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.Empty);
            Assert.That(Session.ApplyEditsToExternalVersion(false).Succeeded, Is.True);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(General), Is.EqualTo(Utf8(GeneralText.Replace("\n", "\r\n").Replace("autoRefresh: false", "autoRefresh: true"), Bom: true)));
        }

        // an external version the editor can't edit: only "use theirs" resolves it
        [Test]
        public void AnUnsupportedExternalVersion_CantTakeTheEdits_AndIsNeverOverwritten()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Write(General, "outputTo: Console\noutputTo: Email\n");

            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.CanApplyEdits, Is.False);
            Assert.That(Session.Conflict.Message, Does.Contain("the new version can't be edited here"));
            Assert.That(Session.ApplyEditsToExternalVersion(true).Succeeded, Is.False);
            Assert.That(File.ReadAllText(General), Is.EqualTo("outputTo: Console\noutputTo: Email\n"));
            Session.UseExternalVersion();
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.ReadOnly));
        }

        // 24: only the changed lines with one line of context (a single line between two changes is shown), line numbers,
        // gaps between distant changes
        [Test]
        public void ThePreview_ShowsExactlyTheChangedLines()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("outputTo", OutputType.HtmlFile);
            Session.Set("autoRefresh", true);

            string Shown = string.Join("\n", Session.Preview.Select(L => $"{L.Kind} {L.OldLine} {L.NewLine} {L.Text}"));
            Assert.That(Shown, Is.EqualTo(string.Join("\n", new[]
            {
                "Context 1 1 # Campertron settings",
                "Removed 2  outputTo: Console   # where results go",
                "Added  2 outputTo: HtmlFile   # where results go",
                "Context 3 3 ",
                "Context 4 4 refreshRidbDataDayInterval: 30",
                "Context 5 5 lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z' # UTC",
                "Removed 6  autoRefresh: false",
                "Added  6 autoRefresh: true",
                "Context 7 7 # end",
            })));

            // distant changes: the unchanged lines between them are one gap marker
            Write(Email, EmailText);
            var Mail = OpenEmail();
            Mail.Set("smtpServer", "smtp.example.org");
            Mail.Set("sendFromAddress", "other@example.org");
            Assert.That(Mail.Preview.Select(L => L.Kind.ToString() + (L.Kind == ConfigPreviewKind.Gap ? "" : " " + (L.OldLine ?? L.NewLine))), Is.EqualTo(new[]
            {
                "Context 1", "Removed 2", "Added 2", "Context 3", "Gap", "Context 9", "Removed 10", "Added 10",
            }));
        }

        // 30, 32: editing the timestamp and the refresh settings writes values only; nothing runs a refresh
        [Test]
        public void TheRefreshSettings_AreValuesOnly_AndTheirProblemsMapToTheirFields()
        {
            Write(General, GeneralText);
            var Session = OpenGeneral();
            Session.Set("autoRefresh", true);
            Session.Set("refreshRidbDataDayInterval", 0d);
            Assert.That(Session.Notices.Single(N => N.Code == "RefreshEveryStart").Key, Is.EqualTo("refreshRidbDataDayInterval"));
            Assert.That(Session.CanSave, Is.True, "a warning does not block");
            Session.Set("refreshRidbDataDayInterval", double.PositiveInfinity);
            ConfigEditNotice Invalid = Session.Notices.Single(N => N.Code == "RefreshIntervalInvalid");
            Assert.That((Invalid.Key, Invalid.Severity), Is.EqualTo(("refreshRidbDataDayInterval", ConfigSeverity.Error)));
            Assert.That(Session.CanSave, Is.False, "an error blocks");
            Session.Set("autoRefresh", false);
            Assert.That(Session.Notices.Single(N => N.Code == "RefreshIntervalInvalid").Severity, Is.EqualTo(ConfigSeverity.Warning), "unused while autoRefresh is false");
            Session.Set("refreshRidbDataDayInterval", 30d);
            Session.Set("lastRidbDataRefresh", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Session.Set("autoRefresh", true);
            Assert.That(Session.Notices.Single(N => N.Code == "LastRefreshInFuture").Key, Is.EqualTo("lastRidbDataRefresh"));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(General), Does.Contain("lastRidbDataRefresh: '2027-01-01T00:00:00.0000000Z' # UTC"));
            Assert.That(Files(), Is.EqualTo(new[] { "General.yaml", "General.yaml.bak" }), "no database, cache or download");

            // the session refers to no refresh, download, search or email code
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.", "CampertronLibrary.function.Base.Web", "System.Net.", "CampsiteConfig",
                "CampertronLibrary.function.Base.ConsoleUi", "System.Console", "System.Diagnostics.Process",
            };
            List<string> Called = Referenced(typeof(ConfigEditSession<GeneralConfig>)).Concat(Referenced(typeof(ConfigEditPreview))).ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Replace"));
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
        }

        // 28 (UnitTest is kept and reported, never offered: the GUI's part), 29: problems map to their keys
        [Test]
        public void OutputToUnitTest_IsKeptAndReported()
        {
            Write(General, "outputTo: UnitTest\nautoRefresh: false\n");
            var Session = OpenGeneral();

            Assert.That(Session.Value("outputTo"), Is.EqualTo(OutputType.UnitTest));
            ConfigEditNotice Test = Session.Notices.Single(N => N.Code == "TestOutput");
            Assert.That((Test.Key, Test.Severity), Is.EqualTo(("outputTo", ConfigSeverity.Info)));
            Session.Set("autoRefresh", true);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(General), Is.EqualTo("outputTo: UnitTest\nautoRefresh: true\n"));
        }

        // the members a type's methods (and their compiler-generated helpers) refer to, from their IL
        internal static List<string> Referenced(Type Type)
        {
            var Types = new List<Type> { Type };
            Types.AddRange(Type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            return Types.SelectMany(T => T.GetMethods(All).Cast<MethodBase>().Concat(T.GetConstructors(All)))
                .SelectMany(YamlDocumentPreservationTests.ReferencedMembers)
                .Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name)
                .Distinct().ToList();
        }
    }
}
