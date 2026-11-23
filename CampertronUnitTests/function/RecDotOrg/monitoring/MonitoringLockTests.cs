using System.Diagnostics;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // The monitoring lock (decisions D83 and D90, business-rules.md §29): a file in the settings folder that a monitoring
    // keeps open without sharing. Taking and releasing it, a second taker (in this process, and another process), a lock
    // file that was left behind (it locks nothing: the lock is the open handle), a holder that was killed, a folder that
    // can't hold the file, its owner-only rights, and its words. Temporary folders only.
    public class MonitoringLockTests
    {
        private string Root = string.Empty;
        private string LockPath => Path.Join(Root, MonitoringLock.FileName);

        [SetUp]
        public void SetUp()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronMonitoringLockTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(Root))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                Directory.Delete(Root, true);
            }
        }

        [Test]
        public void TheLock_IsTakenOnceAtATime_AndFreeAgainWhenItsHolderClosesIt()
        {
            Assert.That(MonitoringLock.FileName, Is.EqualTo("Campertron.monitoring.lock"));
            Assert.That(File.Exists(LockPath), Is.False);

            FileStream? First = MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? FirstProblem);
            Assert.That(First, Is.Not.Null);
            Assert.That(FirstProblem, Is.Null);
            Assert.That(File.Exists(LockPath), Is.True, "the lock's file is in the settings folder");

            FileStream? Second = MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? SecondProblem);
            Assert.That(Second, Is.Null, "one monitoring at a time");
            Assert.That(SecondProblem, Is.EqualTo(MonitoringLockProblem.Held));
            Assert.That(MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? Third), Is.Null);
            Assert.That(Third, Is.EqualTo(MonitoringLockProblem.Held), "asking again changes nothing");

            First!.Dispose();
            Assert.That(File.Exists(LockPath), Is.True, "the file is never deleted");
            Assert.That(new FileInfo(LockPath).Length, Is.EqualTo(0), "and stays empty");
            using FileStream? Again = MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? AgainProblem);
            Assert.That(Again, Is.Not.Null, "free again");
            Assert.That(AgainProblem, Is.Null);
        }

        [Test]
        public void TheLock_OfOneSettingsFolder_DoesNotLockAnother()
        {
            string Other = Path.Join(Root, "other");
            Directory.CreateDirectory(Other);
            using FileStream? First = MonitoringLock.TryAcquire(Root, out _);
            using FileStream? Second = MonitoringLock.TryAcquire(Other, out MonitoringLockProblem? Problem);
            Assert.That(First, Is.Not.Null);
            Assert.That(Second, Is.Not.Null);
            Assert.That(Problem, Is.Null);
        }

        // stale-lock handling: there is nothing to handle. A file that a stopped or killed monitoring left behind is not a
        // lock, whatever it holds and however old it is
        [Test]
        public void ALockFileLeftBehind_LocksNothing_AndIsLeftAsItIs()
        {
            File.WriteAllText(LockPath, "left by something else");
            File.SetLastWriteTimeUtc(LockPath, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));

            using (FileStream? Taken = MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? Problem))
            {
                Assert.That(Taken, Is.Not.Null);
                Assert.That(Problem, Is.Null);
            }

            Assert.That(File.ReadAllText(LockPath), Is.EqualTo("left by something else"), "nothing is written to the file");
            Assert.That(File.GetLastWriteTimeUtc(LockPath), Is.EqualTo(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        }

        [Test]
        [Platform("Linux,MacOsX")]
        public void TheLockFile_IsCreatedForItsOwnerOnly_AndAnExistingOneKeepsItsRights()
        {
            if (OperatingSystem.IsWindows())
            {
                //never reached (the Platform attribute); it tells the platform analyzer so
                return;
            }
            MonitoringLock.TryAcquire(Root, out _)!.Dispose();
            Assert.That(File.GetUnixFileMode(LockPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "0600");

            File.SetUnixFileMode(LockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            MonitoringLock.TryAcquire(Root, out _)!.Dispose();
            Assert.That(File.GetUnixFileMode(LockPath), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead),
                "an existing file's rights are not changed");
        }

        [Test]
        public void AFolderThatCannotHoldTheFile_IsUnavailable_NotHeld()
        {
            Assert.That(MonitoringLock.TryAcquire(Path.Join(Root, "missing"), out MonitoringLockProblem? Missing), Is.Null);
            Assert.That(Missing, Is.EqualTo(MonitoringLockProblem.Unavailable), "a settings folder that does not exist");

            // a folder where the lock's file should be
            Directory.CreateDirectory(LockPath);
            Assert.That(MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? IsFolder), Is.Null);
            Assert.That(IsFolder, Is.EqualTo(MonitoringLockProblem.Unavailable));
            Assert.That(Directory.Exists(LockPath), Is.True, "nothing is removed");
        }

        [Test]
        [Platform("Linux,MacOsX")]
        public void ASettingsFolderThatIsNotWritable_IsUnavailable_AndNothingIsCreated()
        {
            if (OperatingSystem.IsWindows())
            {
                //never reached (the Platform attribute); it tells the platform analyzer so
                return;
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root writes to a read-only folder");
            }
            File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.That(MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? Problem), Is.Null);

            Assert.That(Problem, Is.EqualTo(MonitoringLockProblem.Unavailable));
            Assert.That(Directory.GetFileSystemEntries(Root), Is.Empty);
        }

        // only the failure of an open that another open handle refuses is a held lock
        [Test]
        public void OnlyASharingFailure_IsAHeldLock()
        {
            using FileStream? Held = MonitoringLock.TryAcquire(Root, out _);
            IOException Sharing = Assert.Throws<IOException>(() => new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))!;
            Assert.That(MonitoringLock.IsHeld(Sharing), Is.True, "the open another handle refuses, as this platform reports it (HResult " + Sharing.HResult + ")");

            Assert.That(MonitoringLock.IsHeld(new IOException("some other failure")), Is.False);
            Assert.That(MonitoringLock.IsHeld(new IOException("Read-only file system", 30)), Is.False);
            Assert.That(MonitoringLock.IsHeld(new FileNotFoundException("missing")), Is.False);
            Assert.That(MonitoringLock.IsHeld(new DirectoryNotFoundException("missing")), Is.False);
            Assert.That(MonitoringLock.IsHeld(new PathTooLongException("too long")), Is.False);
        }

        // another process holds the lock: an exclusive advisory lock on the file, which is what a .NET process takes on
        // Linux for an open without sharing (flock(1) of util-linux takes the same). Then the holder is killed: the lock is
        // gone with its process, and the file it leaves is not a lock
        [Test]
        [Platform("Linux")]
        public void AnotherProcess_HoldsTheLock_UntilItEnds_EvenWhenItIsKilled()
        {
            string? Flock = new[] { "/usr/bin/flock", "/bin/flock" }.FirstOrDefault(File.Exists);
            if (Flock == null)
            {
                Assert.Ignore("flock(1) is not installed");
            }
            string Ready = Path.Join(Root, "holder-ready");
            var Start = new ProcessStartInfo(Flock!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string Argument in new[] { "--exclusive", "--nonblock", LockPath, "/bin/sh", "-c", "touch \"$0\"; exec sleep 300", Ready })
            {
                Start.ArgumentList.Add(Argument);
            }
            using Process Holder = Process.Start(Start)!;
            try
            {
                Assert.That(SpinWait.SpinUntil(() => File.Exists(Ready) || Holder.HasExited, TimeSpan.FromSeconds(60)), Is.True);
                Assert.That(Holder.HasExited, Is.False, "the other process holds the lock");

                Assert.That(MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? Problem), Is.Null, "refused while another process holds it");
                Assert.That(Problem, Is.EqualTo(MonitoringLockProblem.Held));

                Holder.Kill(true);
                Assert.That(Holder.WaitForExit(60000), Is.True);
                Assert.That(File.Exists(LockPath), Is.True, "the killed process left the file");
                FileStream? Taken = null;
                Assert.That(SpinWait.SpinUntil(() => (Taken = MonitoringLock.TryAcquire(Root, out _)) != null, TimeSpan.FromSeconds(60)), Is.True,
                    "a lock is never stale: it ended with its process");
                Taken!.Dispose();
            }
            finally
            {
                if (!Holder.HasExited)
                {
                    Holder.Kill(true);
                }
            }
        }

        // The same on Windows, natively (open question #49): there the lock is the open without sharing itself, which
        // Windows refuses to every other open, whatever program asks. The other process is Windows PowerShell, which is
        // part of every Windows: it opens the lock's file as the monitoring does (read and write, no sharing). First this
        // process holds the lock and the other one is refused by Windows (a sharing violation, error 32); then the other
        // one holds it and this process is refused, until the holder is killed
        [Test]
        public void OnWindows_AnotherProcess_IsRefusedWhileTheLockIsHeld_AndHoldsItUntilItEnds_EvenWhenItIsKilled()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows only: on Linux another process holds the lock in the test above");
            }
            string PowerShell = Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(PowerShell))
            {
                Assert.Ignore("Windows PowerShell is not installed");
            }
            string Ready = Path.Join(Root, "holder-ready");
            // exit code 32 for the open that a held lock refuses, 4 for any other failure; else it holds the lock until
            // it is killed. The paths come from the environment, so nothing of them is parsed as a command
            const string Script =
                "try { $f = [System.IO.File]::Open($env:CAMPERTRON_TEST_LOCK_FILE, 'OpenOrCreate', 'ReadWrite', 'None') } " +
                "catch { if (($_.Exception.GetBaseException().HResult -band 0xFFFF) -eq 32) { exit 32 } else { exit 4 } }; " +
                "[System.IO.File]::WriteAllText($env:CAMPERTRON_TEST_READY_FILE, 'held'); Start-Sleep -Seconds 300";
            ProcessStartInfo Other()
            {
                var Start = new ProcessStartInfo(PowerShell) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string Argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Script })
                {
                    Start.ArgumentList.Add(Argument);
                }
                Start.Environment["CAMPERTRON_TEST_LOCK_FILE"] = LockPath;
                Start.Environment["CAMPERTRON_TEST_READY_FILE"] = Ready;
                return Start;
            }

            // this process holds the lock: Windows refuses the other process's open
            using (FileStream? Mine = MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? MyProblem))
            {
                Assert.That(Mine, Is.Not.Null);
                Assert.That(MyProblem, Is.Null);
                using Process Refused = Process.Start(Other())!;
                try
                {
                    Assert.That(Refused.WaitForExit(60000), Is.True, "the other process ends at once: its open is refused");
                    Assert.That(Refused.ExitCode, Is.EqualTo(32), "a sharing violation, from Windows itself");
                    Assert.That(File.Exists(Ready), Is.False, "it never held the lock");
                }
                finally
                {
                    if (!Refused.HasExited)
                    {
                        Refused.Kill(true);
                    }
                }
            }

            // the other process holds the lock: this one is refused, until the holder is killed
            using Process Holder = Process.Start(Other())!;
            try
            {
                Assert.That(SpinWait.SpinUntil(() => File.Exists(Ready) || Holder.HasExited, TimeSpan.FromSeconds(60)), Is.True);
                Assert.That(Holder.HasExited, Is.False, "the other process holds the lock");

                Assert.That(MonitoringLock.TryAcquire(Root, out MonitoringLockProblem? Problem), Is.Null, "refused while another process holds it");
                Assert.That(Problem, Is.EqualTo(MonitoringLockProblem.Held));

                Holder.Kill(true);
                Assert.That(Holder.WaitForExit(60000), Is.True);
                Assert.That(File.Exists(LockPath), Is.True, "the killed process left the file");
                FileStream? Taken = null;
                Assert.That(SpinWait.SpinUntil(() => (Taken = MonitoringLock.TryAcquire(Root, out _)) != null, TimeSpan.FromSeconds(60)), Is.True,
                    "a lock is never stale: it ended with its process");
                Taken!.Dispose();
            }
            finally
            {
                if (!Holder.HasExited)
                {
                    Holder.Kill(true);
                }
            }
        }

        [Test]
        public void TheWords_SayWhatHappenedAndWhatToDo_WithoutAPathOrAValue()
        {
            Assert.That(MonitoringLock.Describe(MonitoringLockProblem.Held, "email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: another Campertron is already monitoring this settings folder.",
                "Only one monitoring runs for a settings folder, so that every opening is notified once. Stop the other one first (another CampertronConsole in email or ntfy mode, or a container that uses the same settings folder), or give this one its own settings folder.",
                "Nothing was searched or sent."
            }));
            Assert.That(MonitoringLock.Describe(MonitoringLockProblem.Unavailable, "ntfy mode"), Is.EqualTo(new[]
            {
                "ntfy mode can't start: the monitoring's lock file can't be created or opened in the settings folder.",
                "Campertron keeps the file Campertron.monitoring.lock open there while it monitors, so that only one monitoring runs for a settings folder. Let the user that runs Campertron write to the settings folder.",
                "Nothing was searched or sent."
            }));
        }

        // the lock's file is not a settings file, not a search file, and not one of the refresh's locks
        [Test]
        public void TheLockFile_IsNoSettingsFile_AndIsSeparateFromTheRefreshsLocks()
        {
            MonitoringLock.TryAcquire(Root, out _)!.Dispose();
            Assert.That(ConfigFiles.SearchFiles(Root), Is.Empty);
            Assert.That(CurrentConfiguration.SearchFileCount(Root), Is.EqualTo(0));
            Assert.That(MonitoringLock.FileName, Is.Not.EqualTo(RidbStagedRefresh.RefreshLockFileName).And.Not.EqualTo(RidbStagedRefresh.RepairLockFileName));

            // a refresh and a monitoring do not lock each other out
            using FileStream? Monitoring = MonitoringLock.TryAcquire(Root, out _);
            using FileStream? Refresh = RidbStagedRefresh.TryAcquireRefreshLock(Root, out _);
            Assert.That(Monitoring, Is.Not.Null);
            Assert.That(Refresh, Is.Not.Null);
        }
    }
}
