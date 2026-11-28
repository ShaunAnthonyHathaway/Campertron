using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The imported secrets (D132 points 4 and 5, D134's secret-replacement policy; v0.4.0-settings-transfer.md §Secret opt-in,
    // §Environment-variable handling, §Receiver-kept keys): the SMTP password and the token each into a new owner-only file of
    // Campertron's whose name holds the transaction's random identifier, created only, named by the settings file only after it
    // read back; never smtpPassword; the topic into Ntfy.yaml only where that file is owner-only; an earlier file of Campertron's
    // removed only when nothing can need it; a user's own file never touched; no value anywhere it does not belong
    public class TransferSecretImportTests : TransferApplyTestBase
    {
        private static readonly Regex PasswordName = new Regex(@"^Email\.password\.([0-9a-f]{32})$");
        private static readonly Regex TokenName = new Regex(@"^Ntfy\.token\.([0-9a-f]{32})$");

        private TransferPackage WithSecrets(bool Password = true, bool Token = true, bool Topic = true)
        {
            var Entries = new List<(TransferEntryKind, string, string)> { General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy) };
            if (Password)
            {
                Entries.Add(SmtpSecret());
            }
            if (Token)
            {
                Entries.Add(TokenSecret());
            }
            if (Topic)
            {
                Entries.Add(TopicSecret());
            }
            return ImportPackage(Entries.ToArray());
        }

        private void AssertSecretsOnlyInTheirFiles(string PasswordFile, string TokenFile)
        {
            AssertCanariesOnlyIn(new Dictionary<string, string[]>
            {
                [PasswordFile] = new[] { SmtpCanary },
                [TokenFile] = new[] { TokenCanary },
                ["Ntfy.yaml"] = new[] { TopicCanary },
                ["Email.yaml"] = new[] { ReceiverPasswordCanary }
            });
        }

        [Test]
        public void APackageWithoutASecret_WritesNoSecretFile_AndKeepsTheReceiversSources()
        {
            Receiver();
            Write("Email.yaml", ReceiverEmail + "smtpPasswordFile: " + In("mine.txt") + "\n");
            TransferApplyResult Result = Import(ImportPackage(Email(PackageEmail), Ntfy(PackageNtfy)));
            Assert.That(Directory.EnumerateFiles(Folder).Select(F => Path.GetFileName(F)).Where(N => N.StartsWith("Email.password", StringComparison.Ordinal) || N.StartsWith("Ntfy.token", StringComparison.Ordinal)), Is.Empty);
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!.SmtpPasswordFile, Is.EqualTo(In("mine.txt")));
            Assert.That(Result.Warnings, Is.Empty);
        }

        [Test]
        public void EveryOptedInSecret_GoesToItsOwnNewOwnerOnlyFile_OrNtfyYaml_AndNowhereElse()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(WithSecrets());
            Assert.That(Preview.Secrets.Select(S => (S.Kind, S.Decision)), Is.EqualTo(new[]
            {
                (TransferSecretKind.SmtpPassword, TransferImportSecretDecision.Imported), (TransferSecretKind.NtfyToken, TransferImportSecretDecision.Imported),
                (TransferSecretKind.NtfyTopic, TransferImportSecretDecision.Imported)
            }));
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Applied, Is.True, Result.Message);
            string Password = SecretFileOf("Email.password")!;
            string Token = SecretFileOf("Ntfy.token")!;
            Match P = PasswordName.Match(Password), T = TokenName.Match(Token);
            Assert.That(P.Success && T.Success, Is.True, "Email.password.<32 hex> and Ntfy.token.<32 hex>");
            Assert.That(P.Groups[1].Value, Is.EqualTo(T.Groups[1].Value), "one transaction's identifier");
            Assert.That(File.ReadAllText(In(Password)), Is.EqualTo(SmtpCanary), "the secret alone, as SecretFile reads it");
            Assert.That(SecretFile.Read(In(Token), out string? TokenRead), Is.Null);
            Assert.That(TokenRead, Is.EqualTo(TokenCanary));
            EmailConfig E = Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!;
            Assert.That(E.SmtpPasswordFile, Is.EqualTo(In(Password)), "named by its full path");
            Assert.That(E.SmtpPassword, Is.EqualTo(ReceiverPasswordCanary), "smtpPassword stays this computer's own, never the imported one");
            Assert.That(SmtpPassword.SelectedSource(E), Is.EqualTo(SmtpPasswordSource.File));
            NtfyConfig N = Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!;
            Assert.That(N.TokenFile, Is.EqualTo(In(Token)));
            Assert.That(N.Topic, Is.EqualTo(TopicCanary));
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(ModeOf(In(Password)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
                Assert.That(ModeOf(In(Token)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            EnvironmentRead = false; // SelectedSource above asks whether the variable is set; the import never did
            AssertSecretsOnlyInTheirFiles(Password, Token);
            AssertShowsNoSecretOrPath(Preview, Result);
            Assert.That(Directory.EnumerateFiles(Folder).Where(F => F.EndsWith(".tmp", StringComparison.Ordinal)), Is.Empty, "no temporary file is left");
        }

        [Test]
        public void ThePreview_SaysEachSecretsSourceChange_ByKindOnly()
        {
            Receiver();
            Write("Ntfy.yaml", ReceiverNtfy + "tokenFile: " + In("mine-token.txt") + "\n");
            TransferPreview Preview = PreviewOf(WithSecrets());
            Assert.That(Preview.Secrets.Single(S => S.Kind == TransferSecretKind.SmtpPassword).Before, Is.EqualTo(TransferImportSecretSource.VariableOrPlainText));
            Assert.That(Preview.Secrets.Single(S => S.Kind == TransferSecretKind.NtfyToken).Before, Is.EqualTo(TransferImportSecretSource.File));
            Assert.That(Preview.Lines(), Does.Contain("The SMTP password then comes from that new file instead of Email.yaml's plain-text smtpPassword (or CAMPERTRON_EMAIL_PASSWORD, where it is set)."));
            Assert.That(Preview.Lines(), Does.Contain("Ntfy's access token then comes from that new file instead of the file tokenFile named."));
            Assert.That(Preview.Warnings, Does.Contain(TransferImportWording.PathIsThisComputers));
            AssertShowsNoSecretOrPath(Preview);
        }

        [Test]
        public void AReceiverThatUsesOnlyAnEnvironmentVariable_KeepsIt_AndNothingReadsIt()
        {
            Write("Email.yaml", PackageEmail.Replace("there@", "here@"));
            Write("Ntfy.yaml", ReceiverNtfy);
            Import(ImportPackage(Email(PackageEmail), Ntfy(PackageNtfy)));
            EmailConfig E = Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!;
            Assert.That(E.SmtpPasswordFileWritten, Is.False, "no file is named: the variable stays the source where it is set");
            Assert.That(E.SmtpPassword, Is.Null);
            // the TearDown checks that nothing read CAMPERTRON_EMAIL_PASSWORD or CAMPERTRON_NTFY_TOKEN
            TransferPreview Preview = PreviewOf(ImportPackage(Email(PackageEmail.Replace("587", "465")), SmtpSecret()));
            Assert.That(Preview.Secrets.Single().Before, Is.EqualTo(TransferImportSecretSource.VariableOrNone));
            Assert.That(Preview.Lines(), Does.Contain("The SMTP password then comes from that new file instead of CAMPERTRON_EMAIL_PASSWORD, where it is set."));
        }

        [Test]
        public void ASecretLeftOut_OrWithoutItsFile_IsNotImported_AndTheReceiversStays()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(WithSecrets(), new TransferImportChoices { SecretsLeftOut = new[] { TransferSecretKind.SmtpPassword }, LeftOut = new[] { "Ntfy.yaml" } });
            Assert.That(Preview.Secrets.Select(S => (S.Kind, S.Decision)), Is.EqualTo(new[]
            {
                (TransferSecretKind.SmtpPassword, TransferImportSecretDecision.LeftOut), (TransferSecretKind.NtfyToken, TransferImportSecretDecision.FileLeftOut),
                (TransferSecretKind.NtfyTopic, TransferImportSecretDecision.FileLeftOut)
            }));
            Assert.That(Preview.Lines(), Does.Contain("The SMTP password is left out of the import, as chosen; this computer's own stays as it is.")
                .And.Contain("Ntfy's access token is not imported, because Ntfy.yaml is left out of the import."));
            Applied(Preview);
            Assert.That(SecretFileOf("Email.password"), Is.Null);
            Assert.That(SecretFileOf("Ntfy.token"), Is.Null);
            Assert.That(Read_("Ntfy.yaml"), Is.EqualTo(ReceiverNtfy));
            AssertCanariesOnlyIn(new Dictionary<string, string[]> { ["Email.yaml"] = new[] { ReceiverPasswordCanary }, ["Ntfy.yaml"] = new[] { ReceiverTopicCanary } });
        }

        [Test]
        public void ATopicOrTokenNtfyWouldRefuse_IsNotImported_AndTheRestIs()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(ImportPackage(Ntfy(PackageNtfy), TokenSecret("bad token with spaces"), TopicSecret("not a topic!")));
            Assert.That(Preview.Secrets.Select(S => S.Decision), Is.All.EqualTo(TransferImportSecretDecision.RefusedUnusable));
            Assert.That(Preview.Lines(), Does.Contain("Ntfy's topic is not imported: it is not a topic ntfy accepts. This computer's own topic stays as it is."));
            Applied(Preview);
            NtfyConfig N = Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!;
            Assert.That(N.Topic, Is.EqualTo(ReceiverTopicCanary));
            Assert.That(N.TokenFileWritten, Is.False);
            Assert.That(Read_("Ntfy.yaml"), Does.Not.Contain("bad token").And.Not.Contain("not a topic"));
        }

        [Test]
        public void TheTopicOmitted_KeepsTheReceiversTopic_OrLeavesNtfyIncomplete_WithoutRefusingTheRest()
        {
            Receiver();
            Import(ImportPackage(Ntfy(PackageNtfy)));
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!.Topic, Is.EqualTo(ReceiverTopicCanary));

            File.Delete(In("Ntfy.yaml"));
            TransferPreview Preview = PreviewOf(ImportPackage(General(PackageGeneral), Ntfy(PackageNtfy)));
            Assert.That(Preview.CanApply, Is.True, "an enabled ntfy without a topic stays incomplete: not a refusal");
            Assert.That(Preview.NtfyIncomplete, Is.True);
            Assert.That(Preview.Warnings, Does.Contain(TransferImportWording.NtfyIncomplete));
            Assert.That(Preview.Findings.Select(F => F.Code), Does.Contain(ConfigCode.NtfyTopicMissing), "in the validation's own words");
            Applied(Preview);
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!.Topic, Is.Null);
            Assert.That(Read_("General.yaml"), Does.Contain("refreshRidbDataDayInterval: 30"), "the rest of the package is imported");
        }

        [Test]
        public void TheTopic_IsRefused_WhereOthersCouldReadNtfyYaml_AndTheFilesModeIsNotChanged()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            var Readable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            File.SetUnixFileMode(In("Ntfy.yaml"), Readable);
            TransferPreview Preview = PreviewOf(WithSecrets());
            Assert.That(Preview.Secrets.Single(S => S.Kind == TransferSecretKind.NtfyTopic).Decision, Is.EqualTo(TransferImportSecretDecision.RefusedReadableByOthers));
            Assert.That(Preview.Lines(), Does.Contain("Ntfy's topic is not imported: other users of this computer could read Ntfy.yaml. This computer's own topic stays as it is."));
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Applied, Is.True, "the rest is imported");
            Assert.That(ModeOf(In("Ntfy.yaml")), Is.EqualTo(Readable), "an existing file's mode is never changed to make an import possible");
            Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!.Topic, Is.EqualTo(ReceiverTopicCanary));
            Assert.That(Read_("Ntfy.yaml"), Does.Not.Contain(TopicCanary));
            Assert.That(SecretFileOf("Ntfy.token"), Is.Not.Null, "the token is imported");

            // a group-only permission is broad too
            File.WriteAllText(In("Ntfy.yaml"), ReceiverNtfy);
            File.SetUnixFileMode(In("Ntfy.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            Assert.That(PreviewOf(WithSecrets(Password: false, Token: false)).Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.RefusedReadableByOthers));
            // a new Ntfy.yaml is created owner-only, so the topic goes into it
            File.Delete(In("Ntfy.yaml"));
            Assert.That(PreviewOf(WithSecrets(Password: false, Token: false)).Secrets.Single().Decision, Is.EqualTo(TransferImportSecretDecision.Imported));
        }

        [Test]
        public void TheTransactionsIdentifier_IsRandom_AndDiffersForEveryPreview()
        {
            Receiver();
            var Names = new HashSet<string>();
            for (int At = 0; At < 20; At++)
            {
                TransferPreview Preview = PreviewOf(WithSecrets(Token: false, Topic: false));
                Names.Add(Preview.Plan.Secrets.Single().FileName);
            }
            Assert.That(Names, Has.Count.EqualTo(20), "no two transactions share a name");
            Assert.That(Names, Has.All.Match(PasswordName.ToString()));
        }

        [Test]
        public void ATakenName_IsNeverWrittenOver_AndTheApplyChangesNothing()
        {
            Receiver();
            const string Fixed = "0123456789abcdef0123456789abcdef";
            TransferImport.NewTransactionId = () => Fixed;
            Write("Email.password." + Fixed, "a file that took the name");
            Dictionary<string, string> Before = Settings();
            TransferPreview Preview = PreviewOf(WithSecrets(Token: false, Topic: false));
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Result.Message);
            Assert.That(Read_("Email.password." + Fixed), Is.EqualTo("a file that took the name"), "create-only: never overwritten");
            Assert.That(Settings(), Is.EqualTo(Before));
            AssertShowsNoSecretOrPath(Preview, Result);
        }

        [Test]
        public void ASecondImport_GetsANewFile_AndTheEarlierFileOfCampertrons_IsRemovedOnlyOnceNothingNamesIt()
        {
            Receiver();
            Import(WithSecrets(Topic: false));
            string FirstPassword = SecretFileOf("Email.password")!;
            string FirstToken = SecretFileOf("Ntfy.token")!;
            TransferApplyResult Second = Import(WithSecrets(Topic: false));
            Assert.That(Second.Warnings, Is.Empty);
            string SecondPassword = SecretFileOf("Email.password")!;
            Assert.That(SecondPassword, Is.Not.EqualTo(FirstPassword), "a new file, never the earlier one replaced in place");
            Assert.That(File.Exists(In(FirstPassword)), Is.False, "the earlier file of Campertron's, superseded and named by nothing, is removed");
            Assert.That(File.Exists(In(FirstToken)), Is.False);
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!.SmtpPasswordFile, Is.EqualTo(In(SecondPassword)));
            Assert.That(Steps.Count(S => S.StartsWith("cleanup:", StringComparison.Ordinal)), Is.EqualTo(2), "a bounded cleanup: the files this import superseded, nothing else");
        }

        [Test]
        public void AnEarlierFileThatCanNotBeProvenCampertrons_Stays_WithOneFixedWarning_AndTheImportIsApplied()
        {
            Receiver();
            Import(WithSecrets(Token: false, Topic: false));
            string First = SecretFileOf("Email.password")!;
            // changed after the stale check: its metadata is no longer what the build read under the lock
            TransferApply.Step = (Name, Path_) =>
            {
                Steps.Add(Name);
                if (Name == "commit")
                {
                    File.AppendAllText(In(First), "x");
                }
            };
            TransferApplyResult Result = Import(WithSecrets(Token: false, Topic: false));
            Assert.That(Result.Applied, Is.True, "an uncertain cleanup never rolls a successful import back");
            Assert.That(Result.Warnings, Is.EqualTo(new[] { TransferImportWording.CleanupWarning }));
            Assert.That(File.Exists(In(First)), Is.True, "left, since it can't be proven Campertron's and unneeded");
            Assert.That(Result.Warnings.Single(), Does.Not.Contain(First).And.Not.Contain(Folder));
        }

        [Test]
        public void AnEarlierFileWithBroaderPermissions_Stays()
        {
            if (!OnUnix())
            {
                return;
            }
            Receiver();
            Import(WithSecrets(Token: false, Topic: false));
            string First = SecretFileOf("Email.password")!;
            File.SetUnixFileMode(In(First), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            TransferApplyResult Result = Import(WithSecrets(Token: false, Topic: false));
            Assert.That(Result.Warnings, Is.EqualTo(new[] { TransferImportWording.CleanupWarning }));
            Assert.That(File.Exists(In(First)), Is.True);
        }

        [Test]
        public void AnEarlierFileStillNamedByAnotherSetting_Stays()
        {
            Receiver();
            Import(WithSecrets(Token: false, Topic: false));
            string First = SecretFileOf("Email.password")!;
            // Ntfy.yaml's tokenFile names it too
            Write("Ntfy.yaml", ReceiverNtfy + "tokenFile: " + In(First) + "\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(In("Ntfy.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            TransferApplyResult Result = Import(ImportPackage(Email(PackageEmail), SmtpSecret()));
            Assert.That(Result.Warnings, Is.EqualTo(new[] { TransferImportWording.CleanupWarning }));
            Assert.That(File.Exists(In(First)), Is.True);
        }

        [Test]
        public void AUsersOwnSecretFile_IsNeverTouched_InTheFolderOrElsewhere()
        {
            Receiver();
            string Elsewhere = Path.Join(Path.GetTempPath(), "CampertronTransferUserSecret-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(Elsewhere, ReceiverTokenCanary);
            try
            {
                Write("Email.password", ReceiverPasswordCanary);
                Write("Email.yaml", ReceiverEmail + "smtpPasswordFile: " + In("Email.password") + "\n");
                Write("Ntfy.yaml", ReceiverNtfy + "tokenFile: " + Elsewhere + "\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(In("Email.password"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    File.SetUnixFileMode(In("Ntfy.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                DateTime Time = File.GetLastWriteTimeUtc(In("Email.password"));
                TransferApplyResult Result = Import(WithSecrets(Topic: false));
                Assert.That(Result.Warnings, Is.Empty, "a user's file is not Campertron's: nothing to warn about");
                Assert.That(Read_("Email.password"), Is.EqualTo(ReceiverPasswordCanary), "never deleted or overwritten");
                Assert.That(File.GetLastWriteTimeUtc(In("Email.password")), Is.EqualTo(Time));
                Assert.That(File.ReadAllText(Elsewhere), Is.EqualTo(ReceiverTokenCanary));
                Assert.That(Steps.Where(S => S.StartsWith("cleanup:", StringComparison.Ordinal)), Is.Empty, "no cleanup of a file that is not a transaction's");
            }
            finally
            {
                File.Delete(Elsewhere);
            }
        }

        [Test]
        public void TheImportedPassword_IsNeverWrittenAsPlainText_InAnySettingsFile()
        {
            Receiver(Email: false);
            Import(WithSecrets(Token: false, Topic: false));
            string Password = SecretFileOf("Email.password")!;
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!.SmtpPassword, Is.Null, "smtpPassword is never written by an import");
            AssertCanariesOnlyIn(new Dictionary<string, string[]> { [Password] = new[] { SmtpCanary }, ["Ntfy.yaml"] = new[] { ReceiverTopicCanary } });
        }

        [Test]
        public void APackageFileThatHoldsASecretOutsideItsSetting_IsRefused()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(ImportPackage(Email(PackageEmail + "# old password: " + SmtpCanary + "\n"), SmtpSecret()));
            Assert.That(Preview.Refusals.Select(R => (R.Problem, R.Name)), Does.Contain((TransferImportProblem.PackageFileHoldsSecret, (string?)"Email.yaml")));
            Assert.That(Preview.CanApply, Is.False);
            AssertShowsNoSecretOrPath(Preview);
        }

        [Test]
        public void AnEncryptedExport_ReadBack_ImportsThroughTheWholePath()
        {
            // the sender: a settings folder of its own, exported through phase 16a's package unchanged
            string Sender = Path.Join(Path.GetTempPath(), "CampertronTransferSender-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Sender);
            try
            {
                File.WriteAllText(Path.Join(Sender, "General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 21\nlastRidbDataRefresh: 2026-11-20T00:00:00.0000000Z\nautoRefresh: false\n");
                File.WriteAllText(Path.Join(Sender, "Email.yaml"), PackageEmail + "smtpPassword: " + SmtpCanary + "\n");
                File.WriteAllText(Path.Join(Sender, "Ntfy.yaml"), PackageNtfy + "topic: " + TopicCanary + "\n");
                File.WriteAllText(Path.Join(Sender, "Trip.yaml"), PackageSearch);
                TransferExportResult Exported = TransferExport.Build(Sender, new TransferExportSelection
                {
                    General = true, Email = true, Ntfy = true, Searches = new[] { "Trip.yaml" }, SmtpPassword = true, NtfyTopic = true
                }, PassphraseCanary, ExportedBy);
                Assert.That(Exported.Failure, Is.Null);
                // the sender's export asks whether a variable is set (yes or no, D82); the receiver never does (the TearDown)
                EnvironmentRead = false;
                TransferPackage Package_ = Read(Exported.Package!, PassphraseCanary).Package!;
                Assert.That(Package_.Encrypted, Is.True);
                Receiver();
                TransferPreview Preview = PreviewOf(Package_);
                Assert.That(Preview.PackageWords, Is.EqualTo("This settings package was made by Campertron 0.4.0 (package format 1). It was encrypted with a passphrase."));
                TransferApplyResult Result = Applied(Preview);
                Assert.That(Result.Applied, Is.True, Result.Message);
                Assert.That(Read_(SecretFileOf("Email.password")!), Is.EqualTo(SmtpCanary));
                Assert.That(Yaml.LoadSettingsText<NtfyConfig>(Read_("Ntfy.yaml"))!.Topic, Is.EqualTo(TopicCanary));
                Assert.That(Yaml.LoadSettingsText<GeneralConfig>(Read_("General.yaml"))!.RefreshRidbDataDayInterval, Is.EqualTo(21));
                Assert.That(Read_("General.yaml"), Does.Contain("2026-10-01"), "this computer's refresh time, not the sender's");
                AssertShowsNoSecretOrPath(Preview, Result);
            }
            finally
            {
                Directory.Delete(Sender, true);
            }
        }
    }
}
