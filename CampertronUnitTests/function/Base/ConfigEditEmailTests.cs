using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // Email.yaml in the settings editing session (CM5, decision D43; business-rules §17): the SMTP password never reaches a
    // message, notice, preview line or the console (sentinel values are searched for in everything the session shows),
    // an unchanged password stays byte for byte, the recipient list keeps its comments, the validation's severity follows
    // the Email output, and the backup and a created Email.yaml are never readable by more users than the file.
    public class ConfigEditEmailTests : ConfigEditTestBase
    {
        private const string Secret = "S3ntinel-Secret-9f2c";
        private const string NewSecret = "N3w-Sentinel-77ab";
        private const string TheirSecret = "Th3irs-Sentinel-41de";

        private const string EmailText = "# SMTP settings\nsmtpServer: 'mail.example.org' # host\nsmtpPort: 587\nsmtpUsername: camper\n" +
            "smtpPassword: " + Secret + " # keep it safe\nsendToAddressList:\n  # family\n  - first@example.org # one\n  - second@example.org\n" +
            "  - third@example.org\nsendFromAddress: camper@example.org\n";

        private static readonly string[] Sentinels = { Secret, NewSecret, TheirSecret };

        private void AssertNoSecret(string Text, string Where)
        {
            foreach (string Sentinel in Sentinels)
            {
                Assert.That(Text, Does.Not.Contain(Sentinel), Where);
            }
        }

        private void AssertNothingShown(ConfigEditSession<EmailConfig> Session, string Where)
        {
            AssertNoSecret(Everything(Session), Where);
            AssertNoSecret(Out.ToString() + Err.ToString(), Where + " (console)");
        }

        // 38
        [Test]
        public void AnUnchangedPassword_StaysByteForByte()
        {
            Write(Email, EmailText.Replace(Secret, "'" + Secret + "'"));
            var Session = OpenEmail();
            Session.Set("smtpServer", "smtp.example.org");
            Session.Set("smtpPassword", Secret);

            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "smtpServer" }));
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace(Secret, "'" + Secret + "'").Replace("'mail.example.org'", "'smtp.example.org'")));
            AssertNothingShown(Session, "after saving another field");
        }

        // 39, 40, 41
        [Test]
        public void AChangedPassword_IsSavedThroughTheEditor_AndMaskedInThePreview()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.Set("smtpPassword", NewSecret);

            Assert.That(Session.ChangedKeys, Is.EqualTo(new[] { "smtpPassword" }));
            string[] Preview = Session.Preview.Select(L => $"{L.Kind} {L.Text}").ToArray();
            Assert.That(Preview, Is.EqualTo(new[]
            {
                "Context smtpUsername: camper",
                "Removed smtpPassword: " + ConfigEditSession<EmailConfig>.Mask + " # keep it safe",
                "Added smtpPassword: " + ConfigEditSession<EmailConfig>.Mask + " # keep it safe",
                "Context sendToAddressList:",
            }));
            AssertNothingShown(Session, "while editing");
            Assert.That(Session.Candidate, Does.Contain("smtpPassword: " + NewSecret + " # keep it safe"), "the text written is not masked");

            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText.Replace(Secret, NewSecret)));
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(File.ReadAllText(Email)).SmtpPassword, Is.EqualTo(NewSecret));
            // 55: the backup is the exact old file (it holds the old password, as an exact backup does)
            Assert.That(File.ReadAllText(Email + ".bak"), Is.EqualTo(EmailText));
            AssertNothingShown(Session, "after saving");
        }

        // 41: the password elsewhere in a shown line is masked too; a whitespace-only one is masked by its range
        [Test]
        public void ThePasswordIsMaskedWhereverItAppearsInThePreview()
        {
            Write(Email, EmailText.Replace("smtpUsername: camper", "smtpUsername: " + Secret + "@example.org"));
            var Session = OpenEmail();
            Session.Set("smtpUsername", "someone@example.org");
            Session.Set("smtpPassword", " ");

            AssertNothingShown(Session, "a password used in the user name");
            Assert.That(Session.Preview.Where(L => L.Kind == ConfigPreviewKind.Removed).Select(L => L.Text).First(), Is.EqualTo("smtpUsername: " + ConfigEditSession<EmailConfig>.Mask + "@example.org"));
            Assert.That(Session.Preview.Where(L => L.Text.StartsWith("smtpPassword:", StringComparison.Ordinal)).Select(L => L.Text), Is.All.StartWith("smtpPassword: " + ConfigEditSession<EmailConfig>.Mask));
        }

        // 41: no notice, conflict, result message or console output holds the password, in every state the session has
        [Test]
        public void NoMessageEverHoldsThePassword()
        {
            Write(Email, EmailText);
            var Session = OpenEmail(EmailOutput: true);
            var Results = new List<string>();
            Session.Set("smtpPassword", NewSecret);
            Session.Set("smtpPort", 70000);
            Session.Set("sendFromAddress", NewSecret);
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, NewSecret), new ConfigListItem(null, "") });
            AssertNothingShown(Session, "invalid values");
            Assert.That(Session.CanSave, Is.False);
            Results.Add(Session.Save().Message);
            Session.Set("smtpUsername", "\uD800" + NewSecret);
            AssertNothingShown(Session, "a refused edit");
            Session.Revert("smtpUsername");
            Session.Set("smtpPassword", "\uD800" + NewSecret);
            Assert.That(Session.Notices.Any(N => N.Source == ConfigEditSource.Editor && N.Key == "smtpPassword"), Is.True);
            AssertNothingShown(Session, "a refused password edit");
            Session.RevertAll();

            Session.Set("smtpPassword", NewSecret);
            Write(Email, EmailText.Replace(Secret, TheirSecret));
            Assert.That(Session.CheckForExternalChange(), Is.EqualTo(ConfigExternalChange.Conflict));
            Assert.That(Session.Conflict!.OverlappingKeys, Is.EqualTo(new[] { "smtpPassword" }));
            AssertNothingShown(Session, "both sides changed the password");
            Results.Add(Session.Save().Message);
            Results.Add(Session.ApplyEditsToExternalVersion(false).Message);
            Results.Add(Session.ApplyEditsToExternalVersion(true).Message);
            AssertNothingShown(Session, "applied to their version");
            Assert.That(Session.Preview.Count(L => L.Text.Contains(ConfigEditSession<EmailConfig>.Mask, StringComparison.Ordinal)), Is.EqualTo(2));
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    throw new IOException("Email.yaml could not be replaced (injected).");
                }
            };
            Results.Add(Session.Save().Message);
            ConfigFileReplace.Step = (Name, Path_) => { };
            Results.Add(Session.Save().Message);
            Assert.That(File.ReadAllText(Email), Does.Contain("smtpPassword: " + NewSecret));
            Write(Email, "smtpPassword: " + TheirSecret + "\nsmtpServer: x\nsmtpServer: y\n");
            Session.CheckForExternalChange();
            AssertNothingShown(Session, "an unsupported version");
            File.WriteAllBytes(Email, new byte[] { 0x73, 0x3A, 0x20, 0xC3, 0x28, 0x0A });
            Session.Reload();
            AssertNothingShown(Session, "an unreadable version");
            foreach (string Result in Results)
            {
                AssertNoSecret(Result, "result: " + Result);
            }
        }

        // 43, 44, 45, 46: insert, remove and reorder recipients; an item's comments go where it goes
        [Test]
        public void Recipients_AreInsertedRemovedAndReordered_WithTheirComments()
        {
            Write(Email, EmailText);
            var Session = OpenEmail();
            Session.SetItems("sendToAddressList", new[]
            {
                new ConfigListItem(1, "second@example.org"),
                new ConfigListItem(null, "new@example.org"),
                new ConfigListItem(0, "first@example.org"),
            });

            Assert.That(Session.Candidate, Is.EqualTo(EmailText.Replace(
                "  # family\n  - first@example.org # one\n  - second@example.org\n  - third@example.org\n",
                "  - second@example.org\n  - new@example.org\n  # family\n  - first@example.org # one\n")));
            Assert.That(Session.Notices.Where(N => N.Code == "CommentRemoved"), Is.Empty, "the removed item had no comment");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(Session.Items("sendToAddressList").Select(R => R.Value), Is.EqualTo(new[] { "second@example.org", "new@example.org", "first@example.org" }));

            // removing an item with comments says so before the save
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, "second@example.org"), new ConfigListItem(1, "new@example.org") });
            ConfigEditNotice Loss = Session.Notices.Single(N => N.Code == "CommentRemoved");
            Assert.That((Loss.Severity, Loss.Key, Loss.Index), Is.EqualTo((ConfigSeverity.Warning, "sendToAddressList", (int?)null)));
            Assert.That(Loss.Message, Is.EqualTo("Removing item 3 of sendToAddressList also removes 2 comments that belong to it."));
            Assert.That(Session.CanSave, Is.True, "a warning, not an error");

            // a changed value keeps its item's comment
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, "second@example.org"), new ConfigListItem(1, "new@example.org"), new ConfigListItem(2, "first@example.net") });
            Assert.That(Session.Candidate, Does.Contain("  # family\n  - first@example.net # one\n"));
        }

        // a missing or null list is filled in only when it is edited, and only with what was entered
        [Test]
        public void ANullList_StaysNullUntilItIsEdited()
        {
            Write(Email, "smtpServer: mail.example.org\nsendToAddressList:\n");
            var Session = OpenEmail();
            Assert.That(Session.Value("sendToAddressList"), Is.Null);
            Session.SetItems("sendToAddressList", Array.Empty<ConfigListItem>());
            Assert.That(Session.IsDirty, Is.False, "no item is no edit");
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(null, "a@example.org") });
            Assert.That(Session.Candidate, Is.EqualTo("smtpServer: mail.example.org\nsendToAddressList: [a@example.org]\n"));
            Assert.That(Session.Preview.Where(L => L.Kind == ConfigPreviewKind.Added).Select(L => L.Text), Is.EqualTo(new[] { "sendToAddressList: [a@example.org]" }));
        }

        // 47
        [Test]
        public void ARecipientLayoutTheEditorCantEditItemByItem_IsLockedAndRefused()
        {
            Write(Email, "smtpServer: mail.example.org\nsendToAddressList:\n  - a@example.org\n  - |\n    b@example.org\n");
            var Session = OpenEmail();
            Assert.That(Session.LockReason("sendToAddressList"), Does.Contain("list layout"));
            Assert.That(Session.LockReason("smtpServer"), Is.Null);

            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, "a@example.org") });
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Notices.Any(N => N.Source == ConfigEditSource.Editor && N.Key == "sendToAddressList" && N.Severity == ConfigSeverity.Error), Is.True);
            Assert.That(Session.Save().Succeeded, Is.False);
            Assert.That(File.ReadAllText(Email), Does.Contain("  - |\n    b@example.org\n"));
        }

        // 48, 49, 50, 51, 52: the validation's problems map to their fields and items; warnings outside the Email output,
        // errors that block the save in it
        [Test]
        public void TheValidation_MapsToFields_AndBlocksOnlyInTheEmailOutput()
        {
            Write(Email, EmailText);
            var Session = OpenEmail(EmailOutput: false);
            Session.Set("smtpPort", 0);
            Session.Set("sendFromAddress", "not an address");
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, "first@example.org"), new ConfigListItem(1, "nobody") });

            (string?, int?, ConfigSeverity)[] Expected =
            {
                ("smtpPort", null, ConfigSeverity.Warning), ("sendFromAddress", null, ConfigSeverity.Warning), ("sendToAddressList", 1, ConfigSeverity.Warning),
            };
            Assert.That(Session.Notices.Where(N => N.Source == ConfigEditSource.Validation).Select(N => (N.Key, N.Index, N.Severity)), Is.EquivalentTo(Expected));
            Assert.That(Session.CanSave, Is.True, "outside the Email output these are warnings");

            Session.EmailOutput = true;
            Assert.That(Session.Notices.Where(N => N.Source == ConfigEditSource.Validation).Select(N => N.Severity), Is.All.EqualTo(ConfigSeverity.Error));
            Assert.That(Session.CanSave, Is.False);
            Assert.That(Session.Save().Message, Is.EqualTo("Email.yaml has problems that must be fixed before it can be saved. Nothing was written."));
            Assert.That(File.ReadAllText(Email), Is.EqualTo(EmailText));

            Session.Set("smtpPort", 465);
            Session.Set("sendFromAddress", "camper@example.org");
            Session.SetItems("sendToAddressList", new[] { new ConfigListItem(0, "first@example.org"), new ConfigListItem(1, "second@example.org, third@example.net") });
            Assert.That(Session.Notices.Where(N => N.Severity != ConfigSeverity.Info), Is.Empty);
            Assert.That(Session.Save().Succeeded, Is.True);
        }

        // 56, 57: a GUI-created Email.yaml is 0600; a backup is never broader than the file
        [Test]
        public void ACreatedEmailYaml_IsOwnerOnly_AndBackupsKeepTheFilesMode()
        {
            if (!OnUnix("TheAccessRulesOnWindows_AreNeverBroadened"))
            {
                return;
            }
            var Session = OpenEmail();
            Assert.That(Session.Create().Succeeded, Is.True);
            Assert.That(File.GetUnixFileMode(Email), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(Session.Notices.Select(N => N.Code), Has.None.EqualTo("EmailReadableByOthers"));
            Assert.That(Text(Email), Is.EqualTo("smtpServer: ''\nsmtpPort: 0\nsmtpUsername: ''\nsmtpPassword: ''\nsendToAddressList: []\nsendFromAddress: ''\n"), "the start's sample");

            Session.Set("smtpPassword", NewSecret);
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.GetUnixFileMode(Email), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            Assert.That(File.GetUnixFileMode(Email + ".bak"), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));

            // a file others can read is kept as it is (the writer keeps a file's mode) and the editor says so
            File.SetUnixFileMode(Email, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            Session.Reload();
            ConfigEditNotice Readable = Session.Notices.Single(N => N.Code == "EmailReadableByOthers");
            Assert.That(Readable.Severity, Is.EqualTo(ConfigSeverity.Info));
            Session.Set("smtpServer", "smtp.example.org");
            Assert.That(Session.Save().Succeeded, Is.True);
            Assert.That(File.GetUnixFileMode(Email + ".bak"), Is.EqualTo(File.GetUnixFileMode(Email)));
            AssertNothingShown(Session, "permissions");
        }

        // 58
        [Test]
        public void TheAccessRulesOnWindows_AreNeverBroadened()
        {
            if (!OnWindows())
            {
                return;
            }
            var Session = OpenEmail();
            Assert.That(Session.Create().Succeeded, Is.True);
            FileSecurity Created = new FileInfo(Email).GetAccessControl(AccessControlSections.Access);
            Assert.That(Created.GetAccessRules(true, false, typeof(SecurityIdentifier)).Count, Is.Zero, "only the folder's inherited rules");
            string Rules = Sddl(Email);

            Session.Set("smtpPassword", NewSecret);
            ConfigEditResult Saved = Session.Save();
            Assert.That(Saved.Succeeded, Is.True, Saved.Message);
            Assert.That(Sddl(Email), Is.EqualTo(Rules));
            Assert.That(Sddl(Email + ".bak"), Is.EqualTo(Rules));
            AssertNothingShown(Session, "Windows");
        }

        [SupportedOSPlatformGuard("windows")]
        private static bool OnWindows()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only (run natively by the Windows CI job)");
            }
            return OperatingSystem.IsWindows();
        }

        [SupportedOSPlatform("windows")]
        private static string Sddl(string Path_) =>
            new FileInfo(Path_).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
    }
}
