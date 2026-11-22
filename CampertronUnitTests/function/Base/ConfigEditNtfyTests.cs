using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Ntfy.yaml in the settings editing session (decision D69; business-rules §17 and §28): a missing file is a new file
    // that nothing writes until an edit is saved, which creates it create-only and owner-only; every setting is a targeted
    // change that keeps comments, line breaks and a byte-order mark; clearing tokenFile removes its line; the validation's
    // severities follow enabled and outputTo: Ntfy; a file another program created meanwhile is never written over; the
    // token is never read and no message, notice, preview of another key or console output holds the topic or a path.
    public class ConfigEditNtfyTests : ConfigEditTestBase
    {
        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");
        private string Topic = string.Empty;

        [SetUp]
        public void NewTopic()
        {
            Topic = "unitTopicCnry" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        private ConfigEditSession<NtfyConfig> OpenNtfy(bool Required = false)
        {
            var Session = new ConfigEditSession<NtfyConfig>(Folder, () => Now);
            Session.NtfyRequired = Required;
            return Session;
        }

        private void AssertNoTopic(ConfigEditSession<NtfyConfig> Session, string Where, params string[] Also)
        {
            // the preview may show the topic's own line (the editor shows what a save writes); nothing else may
            string Shown = string.Join("\n", new[] { Session.StateReason ?? "", Session.Conflict?.Message ?? "" }.Concat(Session.Notices.Select(N => N.ToString())));
            foreach (string Secret in Also.Prepend(Topic))
            {
                Assert.That(Shown, Does.Not.Contain(Secret), Where);
                Assert.That(Out.ToString() + Err.ToString(), Does.Not.Contain(Secret), Where + " (console)");
            }
        }

        // a missing Ntfy.yaml opens as a new file with the defaults; nothing is written by opening or looking again, Save is
        // offered only after an edit, and it creates exactly the reviewed text (no backup for a new file)
        [Test]
        public void AMissingNtfyYaml_IsANewFile_AndNothingIsWrittenUntilAnEditIsSaved()
        {
            var Session = OpenNtfy();
            Assert.That((Session.State, Session.IsNew, Session.IsDirty, Session.CanSave), Is.EqualTo((ConfigEditState.Editable, true, false, false)));
            Assert.That(Session.Notices.Select(N => (N.Code, N.Severity, N.Key)), Has.Member(("NewFile", ConfigSeverity.Info, (string?)null)));
            Assert.That((Session.Value("enabled"), Session.Value("server"), Session.Value("topic"), Session.Value("tokenFile"), Session.Value("priority"), Session.Value("tags"), Session.Value("timeoutSeconds")),
                Is.EqualTo(((object?)false, (object?)null, (object?)null, (object?)null, (object?)NtfyPriority.Default, (object?)null, (object?)30)));
            Assert.That(Session.Save().Message, Is.EqualTo("Nothing to save: Ntfy.yaml has no changes."));
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.None));
            Assert.That(Session.Create().Succeeded, Is.False, "Ntfy.yaml has no sample: Save creates it");
            Assert.That(Files(), Is.Empty);

            // the defaults are no edit
            Session.Set("enabled", false);
            Session.Set("priority", NtfyPriority.Default);
            Session.Set("timeoutSeconds", 30);
            Assert.That(Session.IsDirty, Is.False);

            Session.Set("server", "https://ntfy.example.invalid");
            Session.Set("topic", Topic);
            string Expected = ConfigEditSession<NtfyConfig>.NewNtfyText + "server: https://ntfy.example.invalid\ntopic: " + Topic + "\n";
            Assert.That(Session.Candidate, Is.EqualTo(Expected));
            Assert.That(Session.Preview.Select(L => L.Kind), Is.All.EqualTo(ConfigPreviewKind.Added), "a new file: every line is new");
            Assert.That(Session.CanSave, Is.True, "with ntfy off, what publishing needs is a warning");
            Assert.That(Files(), Is.Empty, "nothing is written before Save");
            AssertNoTopic(Session, "a new file with a topic", "ntfy.example.invalid");

            ConfigEditResult Created = Session.Save();
            Assert.That(Created.Succeeded, Is.True, Created.Message);
            Assert.That(Created.Message, Is.EqualTo("Created Ntfy.yaml with your settings."));
            Assert.That(Text(Ntfy), Is.EqualTo(Expected));
            Assert.That(Files(), Is.EqualTo(new[] { "Ntfy.yaml" }), "no backup and no temporary file");
            Assert.That((Session.IsNew, Session.IsDirty, Session.State), Is.EqualTo((false, false, ConfigEditState.Editable)));
            AssertNoTopic(Session, "after creating it");
        }

        // a created Ntfy.yaml is owner-only on Unix (its topic works like a password) and has only its folder's inherited
        // access rules on Windows; a later save keeps its mode or rules for the file and its backup
        [Test]
        public void ACreatedNtfyYaml_IsOwnerOnly_AndItsBackupsAreNeverBroader()
        {
            var Session = OpenNtfy();
            Session.Set("topic", Topic);
            Assert.That(Session.Save().Succeeded, Is.True);
            Session.Set("enabled", true);
            Session.Set("server", "https://ntfy.example.invalid");
            ConfigEditResult Saved = Session.Save();
            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Text(Ntfy + ".bak"), Is.EqualTo(ConfigEditSession<NtfyConfig>.NewNtfyText + "topic: " + Topic + "\n"));
            if (OperatingSystem.IsWindows())
            {
                FileSecurity Created = new FileInfo(Ntfy).GetAccessControl(AccessControlSections.Access);
                Assert.That(Created.GetAccessRules(true, false, typeof(SecurityIdentifier)).Count, Is.Zero, "only the folder's inherited rules");
                Assert.That(Sddl(Ntfy + ".bak"), Is.EqualTo(Sddl(Ntfy)));
            }
            else
            {
                Assert.That(File.GetUnixFileMode(Ntfy), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                Assert.That(File.GetUnixFileMode(Ntfy + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                Assert.That(Session.Notices.Select(N => N.Code), Has.None.EqualTo("NtfyReadableByOthers"));
                File.SetUnixFileMode(Ntfy, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                Session.Reload();
                Assert.That(Session.Notices.Single(N => N.Code == "NtfyReadableByOthers").Severity, Is.EqualTo(ConfigSeverity.Info));
            }
            AssertNoTopic(Session, "permissions");
        }

        [SupportedOSPlatform("windows")]
        private static string Sddl(string Path_) =>
            new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);

        // every setting is changed in place: comments, blank lines, CRLF line breaks and a byte-order mark stay, an unchanged
        // setting stays byte for byte, a list keeps its items' comments, and the backup is the exact previous file
        [Test]
        public void EverySetting_IsATargetedChange_KeepingCommentsLineBreaksAndTheByteOrderMark()
        {
            string Original = "# ntfy\r\nenabled: false # off\r\nserver: \"https://ntfy.example.invalid\"\r\n\r\ntopic: old_topic # private\r\npriority: 4\r\n" +
                "tags:\r\n  # first\r\n  - tent\r\n  - camping\r\ntimeoutSeconds: 10 # seconds\r\n";
            Write(Ntfy, Original, Bom: true);
            var Session = OpenNtfy();
            Assert.That(Session.Value("priority"), Is.EqualTo(NtfyPriority.High));
            Session.Set("enabled", true);
            Session.Set("server", "https://ntfy.example.net:8443");
            Session.Set("topic", Topic);
            Session.Set("priority", NtfyPriority.Min);
            Session.Set("timeoutSeconds", 60);
            Session.Set("tokenFile", Path.Join(Folder, "token"));
            Session.SetItems("tags", new[] { new ConfigListItem(1, "camping"), new ConfigListItem(null, "warning"), new ConfigListItem(0, "tent") });

            // the new key goes after the last setting; how a path is quoted depends on the platform's path, so its line is
            // checked by what it loads as
            string TokenLine = Session.Candidate!.Split("\r\n").Single(L => L.StartsWith("tokenFile: ", StringComparison.Ordinal));
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(TokenLine).TokenFile, Is.EqualTo(Path.Join(Folder, "token")));
            string Expected = "# ntfy\r\nenabled: true # off\r\nserver: \"https://ntfy.example.net:8443\"\r\n\r\ntopic: " + Topic + " # private\r\npriority: Min\r\n" +
                "tags:\r\n  - camping\r\n  - warning\r\n  # first\r\n  - tent\r\ntimeoutSeconds: 60 # seconds\r\n" + TokenLine + "\r\n";
            Assert.That(Session.Candidate, Is.EqualTo("﻿" + Expected));
            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "enabled", "server", "topic", "tokenFile", "priority", "tags", "timeoutSeconds" }));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllBytes(Ntfy), Is.EqualTo(Utf8(Expected, Bom: true)));
            Assert.That(File.ReadAllBytes(Ntfy + ".bak"), Is.EqualTo(Utf8(Original, Bom: true)));
            AssertNoTopic(Session, "after editing every setting");
        }

        // clearing tokenFile removes its line (never an empty token source); Remove removes a tokenFile written empty; a
        // comment on the line goes with it and is announced; only a key written only when set can be removed
        [Test]
        public void ClearingTokenFile_RemovesItsLine_AndOnlyAnOptionalKeyCanBeRemoved()
        {
            string TokenPath = Path.Join(Folder, "token");
            string FileText = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenPath + "' # mounted\ntimeoutSeconds: 30\n";
            Write(Ntfy, FileText);
            var Session = OpenNtfy();
            Assert.That(Session.IsRemovable("tokenFile") && !Session.IsRemovable("topic"), Is.True);
            Session.Set("tokenFile", null);
            Assert.That((Session.IsRemoved("tokenFile"), Session.IsChanged("tokenFile"), Session.Value("tokenFile")), Is.EqualTo((true, true, (object?)null)));
            Assert.That(Session.Candidate, Is.EqualTo(FileText.Replace("tokenFile: '" + TokenPath + "' # mounted\n", "")));
            Assert.That(Session.Notices.Single(N => N.Code == "CommentRemoved").Message, Is.EqualTo("Removing tokenFile also removes 1 comment that belongs to it."));
            Session.Set("tokenFile", TokenPath + "2");
            Assert.That(Session.IsRemoved("tokenFile"), Is.False, "a new path is a change, not a removal");
            Session.Revert("tokenFile");
            Assert.That(Session.IsDirty, Is.False);
            Assert.Throws<ArgumentException>(() => Session.Remove("topic"));

            Write(Ntfy, FileText.Replace("tokenFile: '" + TokenPath + "' # mounted", "tokenFile:"));
            Session.Reload();
            Assert.That(Session.Notices.Single(N => N.Code == "NtfyTokenFilePathEmpty").Severity, Is.EqualTo(ConfigSeverity.Error));
            Session.Set("tokenFile", null);
            Assert.That(Session.IsRemoved("tokenFile"), Is.True, "an empty tokenFile in the file is removed too");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Ntfy), Is.EqualTo(FileText.Replace("tokenFile: '" + TokenPath + "' # mounted\n", "")));

            // a key that is not in the file needs no removal
            Session.Remove("tokenFile");
            Assert.That(Session.IsDirty, Is.False);
        }

        // the validation's severities: warnings while ntfy is off, errors while it is on or outputTo: Ntfy requires it; http
        // is a warning; each problem at its key and item; the session reports the timeout's range as the validation does
        // (the GUI's field refuses it while ntfy is off as well)
        [Test]
        public void TheValidation_FollowsEnabledAndOutputToNtfy()
        {
            Write(Ntfy, "enabled: false\nserver: https://ntfy.example.invalid/" + Topic + "\ntopic: 'two words'\ntags: [ok, 'not ok']\ntimeoutSeconds: 0\ntokenFile: relative/token\n");
            var Session = OpenNtfy();
            (string?, int?, string)[] Problems =
            {
                ("server", null, "NtfyServerInvalid"), ("topic", null, "NtfyTopicInvalid"), ("tags", 1, "NtfyTagInvalid"),
                ("timeoutSeconds", null, "NtfyTimeoutOutOfRange"), ("tokenFile", null, "NtfyTokenFilePathNotAbsolute"),
            };
            // D71: values written but invalid are errors also while ntfy is off; only missing values are warnings then
            Assert.That(Session.Notices.Where(N => N.Severity == ConfigSeverity.Error).Select(N => (N.Key, N.Index, N.Code)), Is.EquivalentTo(Problems));
            Assert.That(Session.Notices.Single(N => N.Code == "NtfyDisabled").Severity, Is.EqualTo(ConfigSeverity.Info));
            Session.Set("server", "https://ntfy.example.org");
            Assert.That(Session.CanSave, Is.False, "off: a value that is not valid still blocks the save");
            Session.Set("topic", "");
            Session.SetItems("tags", new[] { new ConfigListItem(0, "ok") });
            Session.Set("timeoutSeconds", 30);
            Session.Set("tokenFile", Path.Join(Folder, "token"));
            Assert.That(Session.Notices.Where(N => N.Severity != ConfigSeverity.Info).Select(N => (N.Key, N.Code, N.Severity)), Is.EqualTo(new[] { ((string?)"topic", "NtfyTopicMissing", ConfigSeverity.Warning) }));
            Assert.That(Session.CanSave, Is.True, "off: a missing value is a warning");
            Session.RevertAll();
            Session.Set("server", "https://ntfy.example.org");

            Session.Set("enabled", true);
            Assert.That(Session.Notices.Where(N => N.Severity == ConfigSeverity.Error).Select(N => N.Code), Is.EquivalentTo(Problems.Skip(1).Select(P => P.Item3)));
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Message, Is.EqualTo("Ntfy.yaml has problems that must be fixed before it can be saved. Nothing was written."));

            Session.Set("enabled", false);
            Session.NtfyRequired = true;
            Assert.That(Session.Notices.Single(N => N.Code == "NtfyDisabled").Severity, Is.EqualTo(ConfigSeverity.Error), "outputTo: Ntfy needs ntfy on");
            Session.NtfyRequired = false;

            Session.Set("server", "http://ntfy.example.org");
            Session.Set("topic", Topic);
            Session.SetItems("tags", new[] { new ConfigListItem(0, "ok") });
            Session.Set("timeoutSeconds", 120);
            Session.Set("tokenFile", Path.Join(Folder, "token"));
            Session.Set("enabled", true);
            Assert.That(Session.Notices.Where(N => N.Severity != ConfigSeverity.Info).Select(N => (N.Code, N.Severity)), Is.EqualTo(new[] { ("NtfyServerNotHttps", ConfigSeverity.Warning) }));
            Assert.That(Session.Notices.Single(N => N.Code == "NtfyTokenFileSet").Severity, Is.EqualTo(ConfigSeverity.Info));
            Assert.That(Session.CanSave, Is.True);
            AssertNoTopic(Session, "every problem", "ntfy.example", "relative/token", Folder);
        }

        // the token is never read: the token file keeps its bytes and is never opened, CAMPERTRON_NTFY_TOKEN is never asked
        // for, nothing the session shows or writes holds the token, and the session's code refers to no token reader,
        // publisher, environment or network code
        [Test]
        public void TheToken_IsNeverRead_AndTheSessionRefersToNoTokenOrPublishingCode()
        {
            string FileCanary = "unitTokFileCnry" + Guid.NewGuid().ToString("N");
            string EnvCanary = "unitTokEnvCnry" + Guid.NewGuid().ToString("N");
            string TokenPath = Path.Join(Folder, "secrets", "ntfy_token");
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            File.WriteAllText(TokenPath, FileCanary + "\n");
            File.SetLastWriteTimeUtc(TokenPath, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            Write(Ntfy, "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\ntokenFile: '" + TokenPath + "'\n");
            Func<string, string?> OriginalEnvironment = NtfyToken.GetEnvironment;
            int Asked = 0;
            NtfyToken.GetEnvironment = Name => { Asked++; return EnvCanary; };
            try
            {
                var Session = OpenNtfy(Required: true);
                Session.Set("timeoutSeconds", 50);
                Assert.That(Session.Save().Succeeded, Is.True);
                Assert.That(Text(Ntfy) + Everything(Session) + Out + Err, Does.Not.Contain(FileCanary).And.Not.Contain(EnvCanary));
                Assert.That(Asked, Is.Zero);
                Assert.That(File.ReadAllText(TokenPath), Is.EqualTo(FileCanary + "\n"));
                Assert.That(File.GetLastWriteTimeUtc(TokenPath), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
                AssertNoTopic(Session, "with a token file", TokenPath);
            }
            finally
            {
                NtfyToken.GetEnvironment = OriginalEnvironment;
            }

            string[] Forbidden =
            {
                "CampertronLibrary.function.Base.NtfyToken", "CampertronLibrary.function.Base.SecretFile", "CampertronLibrary.function.Base.SmtpPassword",
                "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.Notifications", "System.Environment::GetEnvironmentVariable",
                "System.Net.", "CampertronLibrary.function.RecDotOrg.", "System.Console", "CampertronLibrary.function.Base.ConsoleUi",
            };
            List<string> Called = ConfigEditSessionTests.Referenced(typeof(ConfigEditSession<NtfyConfig>)).Concat(ConfigEditSessionTests.Referenced(typeof(ConfigEditPreview))).ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConfigFileReplace::Create"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConfigValidation::ValidateNtfy"));
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
        }

        // a Ntfy.yaml another program creates while the user prepares a new one is never written over: the edits are kept in
        // a conflict (also when it appears in the moment of the save) and can be applied to it, or discarded
        [Test]
        public void ANtfyYamlCreatedMeanwhile_IsNeverWrittenOver_AndBecomesAConflict()
        {
            string Theirs = "enabled: true # theirs\nserver: https://ntfy.example.invalid\ntopic: theirTopic\n";
            var Session = OpenNtfy();
            Session.Set("topic", Topic);
            Session.Set("timeoutSeconds", 45);
            Write(Ntfy, Theirs);
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.EqualTo(new[] { "topic" }));
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(Text(Ntfy), Is.EqualTo(Theirs));
            Assert.That(Session.ApplyEditsToExternalVersion(KeepMine: true).Succeeded, Is.True);
            Assert.That(Session.IsNew, Is.False);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Text(Ntfy), Is.EqualTo(Theirs.Replace("theirTopic", Topic) + "timeoutSeconds: 45\n"));

            // the race: another program creates the file between the session's last check and the creation
            File.Delete(Ntfy);
            File.Delete(Ntfy + ".bak");
            Session.Reload();
            Assert.That(Session.IsNew, Is.True);
            Session.Set("topic", Topic);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-create")
                {
                    File.WriteAllText(Ntfy, Theirs);
                }
            };
            ConfigEditResult Raced = Session.Save();
            ConfigFileReplace.Step = (Name, Path_) => { };
            Assert.That(Raced.Succeeded, Is.False);
            Assert.That(Raced.Message, Is.EqualTo("Ntfy.yaml was created by another program meanwhile; it was not written over. Your changes are kept here."));
            Assert.That(Text(Ntfy), Is.EqualTo(Theirs));
            Assert.That(Session.Conflict, Is.Not.Null);
            Assert.That(Session.IsChanged("topic"), Is.True, "the edits are kept");
            Assert.That(Files(), Is.EqualTo(new[] { "Ntfy.yaml" }), "no temporary file is left");
            Session.UseExternalVersion();
            Assert.That((Session.IsNew, Session.IsDirty, Session.Value("topic")), Is.EqualTo((false, false, (object?)"theirTopic")));
            AssertNoTopic(Session, "conflicts");
        }

        // a write that fails leaves the file as it was (or no file) and no temporary file, and keeps the edits
        [Test]
        public void AFailedWrite_LeavesTheFileAsItWas_AndKeepsTheEdits()
        {
            var Session = OpenNtfy();
            Session.Set("topic", Topic);
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "temporary-written" || Name == "before-replace")
                {
                    throw new IOException("Ntfy.yaml could not be written (injected).");
                }
            };
            ConfigEditResult Failed = Session.Save();
            Assert.That((Failed.Succeeded, Failed.Message), Is.EqualTo((false, "Ntfy.yaml could not be written (injected).")));
            Assert.That(Files(), Is.Empty, "no file and no temporary file");
            Assert.That(Session.IsNew && Session.IsChanged("topic"), Is.True);

            ConfigFileReplace.Step = (Name, Path_) => { };
            Assert.That(Session.Save().Succeeded, Is.True);
            byte[] Saved = File.ReadAllBytes(Ntfy);
            Session.Set("enabled", true);
            Session.Set("server", "https://ntfy.example.invalid");
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("Ntfy.yaml could not be replaced (injected).");
                }
            };
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllBytes(Ntfy), Is.EqualTo(Saved));
            Assert.That(Files().Where(F => F.EndsWith(".tmp", StringComparison.Ordinal)), Is.Empty);
            Assert.That(Session.IsChanged("enabled") && Session.IsChanged("server"), Is.True);
            AssertNoTopic(Session, "failed writes");
        }

        // a Ntfy.yaml the editor can't edit is read-only with a reason; an unknown key (refused by the loader, D20) is not
        // named (it could be a mistyped topic), and nothing is changed or normalized
        [Test]
        public void ANtfyYamlTheEditorCantEdit_IsReadOnly_AndNoMessageNamesItsUnknownKey()
        {
            string FileText = "enabled: true\ntopic: " + Topic + "\n" + Topic + "Key: x\n";
            Write(Ntfy, FileText);
            var Session = OpenNtfy();
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.ReadOnly));
            Assert.That(Session.IsNew, Is.False);
            Assert.Throws<InvalidOperationException>(() => Session.Set("enabled", false));
            AssertNoTopic(Session, "an unknown key");
            Assert.That(Text(Ntfy), Is.EqualTo(FileText));

            File.WriteAllBytes(Ntfy, Array.Empty<byte>());
            Session.Reload();
            Assert.That(Session.State, Is.EqualTo(ConfigEditState.Unreadable), "an empty Ntfy.yaml is never treated as a new file and written over");
            Assert.That(File.ReadAllBytes(Ntfy), Is.Empty);
        }
    }
}
