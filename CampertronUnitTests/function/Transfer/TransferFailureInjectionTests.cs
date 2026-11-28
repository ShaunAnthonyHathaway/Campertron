using CampertronLibrary.function.Base;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // A failure at every step of an apply (D134; v0.4.0-settings-transfer.md §Rollback, §Rollback failure, §Threat model "Partial
    // overwrite"): each step the apply and the safe writer take in a complete import is recorded, then the same import fails at
    // that step in a fresh folder. Before the lock and at the stale check nothing is written; in the backup set's preparation the
    // previous set and every file stay; from the first secret file to the commit the rollback puts every settings, search and
    // secret file back byte for byte; after the commit the import stays applied. A failure in the rollback itself keeps the backup
    // set and names the files, never a path or a value
    public class TransferFailureInjectionTests : TransferApplyTestBase
    {
        private string OldPassword = string.Empty, OldToken = string.Empty;

        // a receiver that imported before (its earlier secret files of Campertron's, its backup set), and a package that
        // replaces and creates every kind of file and imports every secret
        private void Scenario()
        {
            foreach (string Entry in Directory.EnumerateFileSystemEntries(Folder))
            {
                if (Directory.Exists(Entry) && new FileInfo(Entry).LinkTarget == null) { Directory.Delete(Entry, true); } else { File.Delete(Entry); }
            }
            Receiver(Searches: new[] { "Here.yaml", "Mine.yaml" });
            TransferApply.Step = (Name, Path_) => { };
            ConfigFileReplace.Step = (Name, Path_) => { };
            TransferPreview Setup = PreviewOf(ImportPackage(Email(ReceiverEmail.Replace("smtpPassword: " + ReceiverPasswordCanary + "\n", string.Empty)), Ntfy(ReceiverNtfy.Replace("topic: " + ReceiverTopicCanary + "\n", string.Empty)),
                SmtpSecret(ReceiverPasswordCanary), TokenSecret(ReceiverTokenCanary)));
            Assert.That(Applied(Setup).Applied, Is.True, "the scenario's earlier import");
            OldPassword = SecretFileOf("Email.password")!;
            OldToken = SecretFileOf("Ntfy.token")!;
            Steps.Clear();
            TransferApply.Step = (Name, Path_) => Steps.Add(Name);
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add("replace:" + Name);
        }

        private static TransferPackage Everything() => ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Here.yaml", PackageSearch), Search("Trip.yaml", PackageSearch),
            SmtpSecret(), TokenSecret(), TopicSecret());

        private Dictionary<string, string[]> AllowedAfterRollback() => new Dictionary<string, string[]>
        {
            ["Email.yaml"] = new[] { ReceiverPasswordCanary },
            ["Ntfy.yaml"] = new[] { ReceiverTopicCanary },
            [OldPassword] = new[] { ReceiverPasswordCanary },
            [OldToken] = new[] { ReceiverTokenCanary }
        };

        private List<(string Step, int Occurrence)> RecordedSteps()
        {
            Scenario();
            TransferApplyResult Result = Applied(PreviewOf(Everything()));
            Assert.That(Result.Applied, Is.True, Result.Message);
            var Points = new List<(string, int)>();
            var Seen = new Dictionary<string, int>();
            foreach (string Step in Steps)
            {
                Seen[Step] = Seen.TryGetValue(Step, out int N) ? N + 1 : 1;
                Points.Add((Step, Seen[Step]));
            }
            return Points;
        }

        [Test]
        public void TheRecordedApply_TakesEveryStepTheDesignNames_InItsOrder()
        {
            List<string> Order = RecordedSteps().Select(P => P.Step).Where(S => !S.StartsWith("replace:", StringComparison.Ordinal)).ToList();
            Assert.That(Order.Take(3), Is.EqualTo(new[] { "lock", "stale-check", "backup-folder" }));
            int At(string Step) => Order.IndexOf(Step);
            Assert.That(At("backup-install"), Is.LessThan(At("secret-create:smtp-password")), "the backup set before any secret file");
            Assert.That(At("secret-placed:ntfy-token"), Is.LessThan(At("write:Here.yaml")), "the secret files before the settings that name them");
            Assert.That(At("write:Trip.yaml"), Is.LessThan(At("write:Ntfy.yaml")));
            Assert.That(At("write:Ntfy.yaml"), Is.LessThan(At("write:Email.yaml")));
            Assert.That(At("write:Email.yaml"), Is.LessThan(At("write:General.yaml")), "General.yaml last");
            Assert.That(Order.SkipWhile(S => S != "write:General.yaml").Skip(1).Take(6), Is.EqualTo(new[] { "readback:General.yaml", "verify", "compare", "secret-reference", "commit", "cleanup:smtp-password" }));
            Assert.That(Order.Last(), Is.EqualTo("release"));
        }

        [Test]
        public void AFailureAtEveryStep_EndsWithNothingChanged_OrStaysApplied_AfterTheCommit()
        {
            List<(string Step, int Occurrence)> Points = RecordedSteps();
            Assert.That(Points, Has.Count.GreaterThan(60), "the apply's and the safe writer's steps");
            var Checked = new List<string>();
            foreach ((string Step, int Occurrence) in Points)
            {
                string Label = Step + " #" + Occurrence;
                Scenario();
                Dictionary<string, string> Whole = Snapshot();
                Dictionary<string, string> Files = Settings();
                TransferPreview Preview = PreviewOf(Everything());
                FailAt(Step, Occurrence);
                TransferApplyResult Result = Applied(Preview);
                TransferApply.Step = (Name, Path_) => { };
                ConfigFileReplace.Step = (Name, Path_) => { };
                AssertShowsNoSecretOrPath(Preview, Result);
                using (FileStream? Free = TransferApplyLock.TryAcquire(Folder, out _))
                {
                    Assert.That(Free, Is.Not.Null, Label + ": the lock is released");
                }
                Assert.That(Directory.EnumerateFileSystemEntries(Folder, "*.tmp"), Is.Empty, Label + ": no temporary file stays");
                Assert.That(Directory.EnumerateDirectories(Folder).Select(D => Path.GetFileName(D)), Is.EqualTo(new[] { TransferBackupSet.FolderName }), Label + ": one backup set, no other folder");
                if (Step == "lock" || Step == "stale-check")
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.Refused), Label);
                    Assert.That(Snapshot(), Is.EqualTo(Whole), Label + ": nothing written");
                }
                else if (Step.StartsWith("backup-", StringComparison.Ordinal))
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Label);
                    Assert.That(Result.Message, Is.EqualTo(TransferImportWording.Apply(TransferImportProblem.FailedNothingChanged).Message));
                    Assert.That(Snapshot(), Is.EqualTo(Whole), Label + ": nothing written, the previous backup set as it was");
                }
                else if (Step.StartsWith("cleanup:", StringComparison.Ordinal))
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.Applied), Label + ": a cleanup never rolls a committed import back");
                    Assert.That(Result.Warnings, Is.EqualTo(new[] { TransferImportWording.CleanupWarning }), Label);
                    Assert.That(File.Exists(In(Step.EndsWith("smtp-password", StringComparison.Ordinal) ? OldPassword : OldToken)), Is.True, Label + ": the earlier file stays");
                }
                else if (Step == "release")
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.Applied), Label);
                }
                else
                {
                    Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged), Label + ": " + Result.Message);
                    Assert.That(Settings(), Is.EqualTo(Files), Label + ": every settings, search and secret file byte for byte as before, no new one");
                    Assert.That(File.Exists(Path.Join(Folder, TransferBackupSet.FolderName, TransferBackupSet.ContentsName)), Is.True, Label + ": the backup set stays");
                    AssertCanariesOnlyIn(AllowedAfterRollback());
                }
                Checked.Add(Label);
            }
            Assert.That(Checked.Where(C => C.StartsWith("replace:after-replace", StringComparison.Ordinal)), Is.Not.Empty, "a failure after the safe writer renamed a file is among them");
            Assert.That(Checked.Count(C => System.Text.RegularExpressions.Regex.IsMatch(C, "^secret-(create|permission|verify|placed):")), Is.EqualTo(8), "four steps for each new secret file");
        }

        [Test]
        public void AFailureInTheRollback_KeepsTheBackupSet_AndNamesTheFiles_NeverAPath()
        {
            foreach (string Failing in new[] { "rollback:General.yaml", "rollback:Email.yaml", "rollback:Ntfy.yaml", "rollback:Trip.yaml", "rollback:Here.yaml" })
            {
                Scenario();
                string File_ = Failing.Substring("rollback:".Length);
                TransferPreview Preview = PreviewOf(Everything());
                int Seen = 0;
                TransferApply.Step = (Name, Path_) =>
                {
                    Steps.Add(Name);
                    if (Name == "commit" || (Name == Failing && ++Seen == 1))
                    {
                        throw new IOException("injected at " + Name);
                    }
                };
                TransferApplyResult Result = Applied(Preview);
                Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored), Failing);
                // a settings file that still names a new secret file keeps that file too: it is never removed while named
                string[] Expected = File_ == "Email.yaml" ? new[] { File_, "the new file for the SMTP password" }
                    : File_ == "Ntfy.yaml" ? new[] { File_, "the new file for ntfy's access token" } : new[] { File_ };
                Assert.That(Result.NotRestored, Is.EqualTo(Expected), Failing);
                Assert.That(Result.Restored, Does.Not.Contain(File_).And.Contain("General.yaml").Or.Contain("Email.yaml"));
                Assert.That(Result.Message, Does.StartWith("The import failed, and not everything could be put back.").And.Contain("Not restored: " + string.Join(", ", Expected) + ".")
                    .And.Contain("holds the earlier settings without their passwords and topic"));
                string Set = Path.Join(Folder, TransferBackupSet.FolderName);
                Assert.That(File.ReadAllText(Path.Join(Set, TransferBackupSet.ContentsName)), Does.Contain("  " + File_ + "\n"), Failing + ": the backup set names it");
                if (File_ != "Trip.yaml")
                {
                    Assert.That(File.Exists(Path.Join(Set, File_ + ".bak")), Is.True, Failing + ": its earlier version is in the backup set");
                }
                AssertShowsNoSecretOrPath(Preview, Result);
                foreach (string Bak in Directory.EnumerateFiles(Set))
                {
                    AssertNoCanary(Bak, File.ReadAllBytes(Bak), AllCanaries);
                }
            }
        }

        [Test]
        public void ANewSecretFileTheRollbackCanNotRemove_IsNamedByItsKind()
        {
            Scenario();
            TransferPreview Preview = PreviewOf(Everything());
            string PasswordFile = Preview.Plan.Secrets.Single(S => S.Kind == TransferSecretKind.SmtpPassword).FileName;
            TransferApply.Step = (Name, Path_) =>
            {
                if (Name == "commit" || Name == "rollback:" + PasswordFile)
                {
                    throw new IOException("injected at " + Name);
                }
            };
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored));
            Assert.That(Result.NotRestored, Is.EqualTo(new[] { "the new file for the SMTP password" }));
            Assert.That(Result.Message, Does.Not.Contain(PasswordFile));
            Assert.That(Yaml.LoadSettingsText<EmailConfig>(Read_("Email.yaml"))!.SmtpPasswordFile, Is.EqualTo(In(OldPassword)), "the settings are restored; only the new file stays");
            Assert.That(File.Exists(In(PasswordFile)), Is.True);
        }

        [Test]
        public void AFailureOfTheRollbacksReadBack_IsAFailureToRestore()
        {
            Scenario();
            TransferPreview Preview = PreviewOf(Everything());
            TransferApply.Step = (Name, Path_) =>
            {
                if (Name == "commit" || Name == "rollback-verify")
                {
                    throw new IOException("injected at " + Name);
                }
            };
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored), "nothing is said to be as before when that could not be read back");
            Assert.That(Result.Restored, Is.Empty);
            Assert.That(Result.NotRestored, Is.SupersetOf(new[] { "General.yaml", "Email.yaml", "Ntfy.yaml", "Here.yaml", "Trip.yaml" }));
        }

        [Test]
        public void AFileAnotherProgramChangedAfterTheApplyWroteIt_IsNeverWrittenOver_ByTheRollback()
        {
            Scenario();
            Dictionary<string, string> Before = Settings();
            TransferPreview Preview = PreviewOf(Everything());
            const string Theirs = "enabled: false\nserver: https://other.example.net\n";
            TransferApply.Step = (Name, Path_) =>
            {
                if (Name == "verify")
                {
                    File.WriteAllText(In("Ntfy.yaml"), Theirs);
                }
            };
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored), "the read-back saw the change and rolled back what it could");
            Assert.That(Result.NotRestored, Does.Contain("Ntfy.yaml"));
            Assert.That(Read_("Ntfy.yaml"), Is.EqualTo(Theirs), "the other program's version is kept");
            Assert.That(Settings().Where(E => E.Key != "Ntfy.yaml"), Is.EquivalentTo(Before.Where(E => E.Key != "Ntfy.yaml")),
                "every other file is restored, the created search file and the new secret files removed");
        }

        [Test]
        public void ACreatedFileThatChangedSince_IsNotRemoved()
        {
            Scenario();
            TransferPreview Preview = PreviewOf(Everything());
            TransferApply.Step = (Name, Path_) =>
            {
                if (Name == "verify")
                {
                    File.AppendAllText(In("Trip.yaml"), "# edited meanwhile\n");
                }
            };
            TransferApplyResult Result = Applied(Preview);
            Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.FailedNotRestored));
            Assert.That(Result.NotRestored, Is.EqualTo(new[] { "Trip.yaml" }));
            Assert.That(Read_("Trip.yaml"), Does.EndWith("# edited meanwhile\n"), "ER-14's one exception covers only a file still exactly as the apply wrote it");
            Assert.That(Read_("Here.yaml"), Is.EqualTo(ReceiverSearch));
        }
    }
}
