using System.Diagnostics;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.Transfer;
using CampertronLibrary.function.Transfer.Apply;

namespace CampertronUnitTests.function.Transfer
{
    // The preview's revision and its one use, and the apply's refusals before anything is written (D134;
    // v0.4.0-settings-transfer.md §Preview, §Confirmation, §Apply lock, §Cancellation boundary): the revision binds the package,
    // the receiver's files, the choices and the decisions; a preview is applied at most once and only by its own revision; a
    // change since the preview, an unsaved editor, a refresh, a starting monitoring, a held lock or a cancellation refuse with
    // nothing written; the lock is released at every end
    public class TransferPreviewAndLockTests : TransferApplyTestBase
    {
        private TransferPackage Usual() => ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), SmtpSecret(), TopicSecret());

        private bool LockIsFree()
        {
            using FileStream? Taken = TransferApplyLock.TryAcquire(Folder, out _);
            return Taken != null;
        }

        [Test]
        public void TheRevision_IsAKeyedDigest_TheSameForTheSameInputs()
        {
            Receiver();
            TransferPreview A = PreviewOf(Usual()), B = PreviewOf(Usual());
            Assert.That(A.Revision, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(B.Revision, Is.EqualTo(A.Revision), "the same package, folder and choices (the transaction's identifier is left out)");
            Assert.That(A.Plan.TransactionId, Is.Not.EqualTo(B.Plan.TransactionId));
            Assert.That(A.Revision, Is.Not.EqualTo(System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ReceiverEmail)))));
        }

        [Test]
        public void TheRevision_ChangesWithThePackage_TheReceiver_TheChoices_AndTheDecisions()
        {
            Receiver();
            string Base = PreviewOf(Usual()).Revision;
            var Revisions = new Dictionary<string, string>
            {
                ["package"] = PreviewOf(ImportPackage(General(PackageGeneral.Replace("30", "31")), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), SmtpSecret(), TopicSecret())).Revision,
                ["package secret"] = PreviewOf(ImportPackage(General(PackageGeneral), Email(PackageEmail), Ntfy(PackageNtfy), Search("Trip.yaml", PackageSearch), SmtpSecret("another-secret"), TopicSecret())).Revision,
                ["left-out file"] = PreviewOf(Usual(), new TransferImportChoices { LeftOut = new[] { "Trip.yaml" } }).Revision,
                ["left-out secret"] = PreviewOf(Usual(), new TransferImportChoices { SecretsLeftOut = new[] { TransferSecretKind.NtfyTopic } }).Revision
            };
            Write("Mine.yaml", ReceiverSearch);
            Revisions["receiver's new search"] = PreviewOf(Usual()).Revision;
            File.SetLastWriteTimeUtc(In("Mine.yaml"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Revisions["receiver's file touched"] = PreviewOf(Usual()).Revision;
            Write("Email.yaml", ReceiverEmail.Replace("25", "26"));
            Revisions["receiver's text"] = PreviewOf(Usual()).Revision;
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(In("Ntfy.yaml"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);
                TransferPreview TopicRefused = PreviewOf(Usual());
                Assert.That(TopicRefused.Secrets.Single(S => S.Kind == TransferSecretKind.NtfyTopic).Decision, Is.EqualTo(TransferImportSecretDecision.RefusedReadableByOthers));
                Revisions["topic's decision"] = TopicRefused.Revision;
            }
            Write("General.yaml", "outputTo: Email\nrefreshRidbDataDayInterval: 14\nautoRefresh: true\n");
            Revisions["outputTo's decision"] = PreviewOf(ImportPackage(General("outputTo: Ntfy\nrefreshRidbDataDayInterval: 14\nautoRefresh: true\n"))).Revision;
            var Seen = new HashSet<string> { Base };
            foreach ((string What, string Revision) in Revisions)
            {
                Assert.That(Seen.Add(Revision), Is.True, What + " gives a revision of its own");
            }
        }

        [Test]
        public void APreview_IsAppliedOnce_ByItsOwnRevision_AndAReplayIsRefused()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(Usual());
            Assert.That(TransferApply.Apply(Preview, "0".PadLeft(64, '0'), Idle).Failure!.Problem, Is.EqualTo(TransferImportProblem.NotConfirmed));
            Assert.That(TransferApply.Apply(Preview, null, Idle).Failure!.Problem, Is.EqualTo(TransferImportProblem.NotConfirmed));
            Assert.That(Applied(Preview).Applied, Is.True, "a confirmation of another revision does not use the preview up");
            Dictionary<string, string> After = Snapshot();
            TransferApplyResult Replay = Applied(Preview);
            Assert.That(Replay.Failure!.Problem, Is.EqualTo(TransferImportProblem.AlreadyUsed));
            Assert.That(Replay.Message, Is.EqualTo("This preview was already applied or tried; look at the preview again."));
            Assert.That(Snapshot(), Is.EqualTo(After));
        }

        [Test]
        public void APreviewThatFailed_IsNotAppliedAgain_ButANewOneIs()
        {
            Receiver();
            TransferPreview Preview = PreviewOf(Usual());
            FailAt("write:Ntfy.yaml");
            Assert.That(Applied(Preview).Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
            TransferApply.Step = (Name, Path_) => Steps.Add(Name);
            ConfigFileReplace.Step = (Name, Path_) => Steps.Add("replace:" + Name);
            Assert.That(Applied(Preview).Failure!.Problem, Is.EqualTo(TransferImportProblem.AlreadyUsed));
            Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True);
        }

        [Test]
        public void AChangeSinceThePreview_RefusesItAsStale_AndNothingIsWritten()
        {
            foreach (Action Change in new Action[]
            {
                () => Write("Email.yaml", ReceiverEmail.Replace("old.example.org", "changed.example.org")),
                () => Write("Mine.yaml", ReceiverSearch),
                () => File.Delete(In("Here.yaml")),
                () => Write("trip.yaml", ReceiverSearch),
                () => File.SetLastWriteTimeUtc(In("General.yaml"), new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            })
            {
                foreach (string Entry in Directory.EnumerateFileSystemEntries(Folder))
                {
                    if (Directory.Exists(Entry)) { Directory.Delete(Entry, true); } else { File.Delete(Entry); }
                }
                Receiver(Searches: new[] { "Here.yaml" });
                TransferPreview Preview = PreviewOf(Usual());
                Change();
                Dictionary<string, string> Before = Snapshot();
                TransferApplyResult Result = Applied(Preview);
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferImportProblem.Stale));
                Assert.That(Snapshot(R => R == TransferApplyLock.FileName), Is.EqualTo(Before), "nothing but the lock's empty file");
                Assert.That(LockIsFree(), Is.True);
            }
        }

        [Test]
        public void AHostsUnsavedChange_ARefresh_OrAStartingMonitoring_Refuses_InThatOrder()
        {
            Receiver();
            Dictionary<string, string> Before = Snapshot();
            (Func<bool>, Func<bool>, Func<bool>, TransferImportProblem)[] Cases =
            {
                (() => true, () => true, () => true, TransferImportProblem.UnsavedChanges),
                (() => false, () => true, () => true, TransferImportProblem.RefreshRunning),
                (() => false, () => false, () => true, TransferImportProblem.MonitoringStarting)
            };
            foreach ((Func<bool> Unsaved, Func<bool> Refreshing, Func<bool> Starting, TransferImportProblem Expected) in Cases)
            {
                TransferPreview Preview = PreviewOf(Usual());
                TransferApplyResult Result = TransferApply.Apply(Preview, Preview.Revision, new TransferApplyHost(Unsaved, Refreshing, Starting));
                Assert.That(Result.Failure!.Problem, Is.EqualTo(Expected));
                Assert.That(Result.Outcome, Is.EqualTo(TransferApplyOutcome.Refused));
                AssertShowsNoSecretOrPath(Preview, Result);
            }
            Assert.That(Snapshot(), Is.EqualTo(Before), "not even the lock's file");
            Assert.That(Steps, Is.Empty);
        }

        [Test]
        public void ARunningMonitoringsLock_IsNoReason_TheApplyHasItsOwn()
        {
            Receiver();
            using FileStream? Monitoring = MonitoringLock.TryAcquire(Folder, out _);
            Assert.That(Monitoring, Is.Not.Null);
            Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True, "a monitoring that runs keeps its settings until its next start");
        }

        [Test]
        public void ALockHeldInThisProcess_Refuses_AndTheHolderKeepsIt()
        {
            Receiver();
            using FileStream? Held = TransferApplyLock.TryAcquire(Folder, out _);
            Assert.That(Held, Is.Not.Null);
            Dictionary<string, string> Before = Snapshot();
            TransferApplyResult Result = Applied(PreviewOf(Usual()));
            Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferImportProblem.LockHeld));
            Assert.That(Result.Message, Is.EqualTo("Another import is being applied to this settings folder; nothing was imported."));
            Assert.That(Snapshot(), Is.EqualTo(Before));
        }

        [Test]
        public void ALockHeldByAnotherProcess_Refuses_UntilItEnds()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("Linux: another process takes the same advisory lock with flock(1)");
            }
            string? Flock = new[] { "/usr/bin/flock", "/bin/flock" }.FirstOrDefault(File.Exists);
            if (Flock == null)
            {
                Assert.Ignore("flock(1) is not installed");
            }
            Receiver();
            string LockPath = In(TransferApplyLock.FileName);
            string Ready = Path.Join(Path.GetTempPath(), "CampertronTransferHolder-" + Guid.NewGuid().ToString("N"));
            var Start = new ProcessStartInfo(Flock!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string Argument in new[] { "--exclusive", "--nonblock", LockPath, "/bin/sh", "-c", "touch \"$0\"; exec sleep 300", Ready })
            {
                Start.ArgumentList.Add(Argument);
            }
            using Process Holder = Process.Start(Start)!;
            try
            {
                Assert.That(SpinWait.SpinUntil(() => File.Exists(Ready) || Holder.HasExited, TimeSpan.FromSeconds(60)), Is.True);
                Assert.That(Holder.HasExited, Is.False);
                Assert.That(Applied(PreviewOf(Usual())).Failure!.Problem, Is.EqualTo(TransferImportProblem.LockHeld));
                Holder.Kill(true);
                Assert.That(Holder.WaitForExit(60000), Is.True);
                Assert.That(SpinWait.SpinUntil(LockIsFree, TimeSpan.FromSeconds(60)), Is.True, "a lock is never stale: it ended with its process");
                Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True);
            }
            finally
            {
                if (!Holder.HasExited)
                {
                    Holder.Kill(true);
                }
                File.Delete(Ready);
            }
        }

        [Test]
        public void ALockFileLeftBehind_LocksNothing_AndIsNeverWrittenOrDeleted()
        {
            Receiver();
            Write(TransferApplyLock.FileName, "left by something else");
            Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True);
            Assert.That(Read_(TransferApplyLock.FileName), Is.EqualTo("left by something else"));
        }

        [Test]
        public void TheLock_IsOwnerOnly_AndReleasedAtEveryEnd()
        {
            Receiver();
            Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True);
            Assert.That(LockIsFree(), Is.True, "after a successful apply");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(ModeOf(In(TransferApplyLock.FileName)), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            Write("Email.yaml", ReceiverEmail);
            FailAt("verify");
            Assert.That(Applied(PreviewOf(Usual())).Outcome, Is.EqualTo(TransferApplyOutcome.FailedNothingChanged));
            Assert.That(LockIsFree(), Is.True, "after a rollback");
            FailAt("release");
            Write("Email.yaml", ReceiverEmail.Replace("25", "2525"));
            Assert.That(Applied(PreviewOf(Usual())).Applied, Is.True, "a failure at the release changes nothing of the result");
            Assert.That(LockIsFree(), Is.True, "and the lock is released all the same");
            FailAt("lock");
            Assert.That(Applied(PreviewOf(Usual())).Failure!.Problem, Is.EqualTo(TransferImportProblem.LockUnavailable));
        }

        [Test]
        public void AFolderThatCanNotHoldTheLock_IsRefused_WithNothingWritten()
        {
            if (!OnUnix())
            {
                return;
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root writes to a read-only folder");
            }
            Receiver();
            TransferPreview Preview = PreviewOf(Usual());
            Dictionary<string, string> Before = Snapshot();
            File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                TransferApplyResult Result = Applied(Preview);
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferImportProblem.LockUnavailable));
                Assert.That(Result.Message, Does.Not.Contain(TransferApplyLock.FileName).And.Not.Contain(Folder));
            }
            finally
            {
                File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Assert.That(Snapshot(), Is.EqualTo(Before));
        }

        [Test]
        public void ACancellationBeforeTheLock_WritesNothing_AndOneAfterIt_IsNotLookedAt()
        {
            Receiver();
            using (var Cancelled = new CancellationTokenSource())
            {
                Cancelled.Cancel();
                Dictionary<string, string> Before = Snapshot();
                TransferPreview Preview = PreviewOf(Usual());
                TransferApplyResult Result = TransferApply.Apply(Preview, Preview.Revision, Idle, Cancelled.Token);
                Assert.That(Result.Failure!.Problem, Is.EqualTo(TransferImportProblem.Cancelled));
                Assert.That(Snapshot(), Is.EqualTo(Before));
            }
            using (var During = new CancellationTokenSource())
            {
                TransferApply.Step = (Name, Path_) =>
                {
                    Steps.Add(Name);
                    if (Name == "write:Email.yaml")
                    {
                        During.Cancel();
                    }
                };
                TransferPreview Preview = PreviewOf(Usual());
                TransferApplyResult Result = TransferApply.Apply(Preview, Preview.Revision, Idle, During.Token);
                Assert.That(Result.Applied, Is.True, "under the lock the apply ends applied or rolled back: never abandoned");
                Assert.That(Steps, Does.Contain("write:General.yaml").And.Contain("commit"));
            }
        }

        [Test]
        public void APreviewThatCanNotBeApplied_IsRefused_WithItsReasons()
        {
            TransferPreview Missing = TransferImport.Preview(Path.Join(Folder, "missing"), Usual());
            Assert.That(Missing.CanApply, Is.False);
            Assert.That(Missing.Refusals.Single().Message, Is.EqualTo("The settings folder does not exist, so nothing can be imported into it."));
            Assert.That(Applied(Missing).Failure!.Problem, Is.EqualTo(TransferImportProblem.NotApplicable));
            Assert.That(Directory.Exists(Path.Join(Folder, "missing")), Is.False, "never created");
        }
    }
}
