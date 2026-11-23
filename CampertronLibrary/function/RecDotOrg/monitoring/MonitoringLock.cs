namespace CampertronLibrary.function.RecDotOrg.monitoring
{
    // why the monitoring lock was not taken
    internal enum MonitoringLockProblem
    {
        // another monitoring holds it: another process (a console in email or ntfy mode, a container that uses the same
        // settings folder) or another service of this process
        Held,
        // the lock file can't be created or opened (a settings folder that is missing, read-only or not this user's)
        Unavailable
    }

    // The monitoring lock (decisions D83 and D90, business-rules.md §29): one monitoring at a time for a settings folder,
    // so that two of them never both notify the same openings and never both write Camp.History. Modeled on the refresh
    // lock (RidbStagedRefresh.TryAcquireRefreshLock, D26): a file in the settings folder that the monitoring keeps open
    // without sharing for as long as it runs.
    //  - The lock is the open handle, not the file. The operating system releases it when the monitoring closes it or its
    //    process ends in any way (a kill, a crash, a power loss), so a lock is never stale: a lock file that is left
    //    behind, by a clean stop or a killed process, locks nothing, and the next monitoring simply opens it.
    //  - The file is never deleted (no DeleteOnClose), so two processes can't end up locking different files with the
    //    same name, and it stays empty: nothing is written to it and nothing is read from it.
    //  - It is created for its owner only (0600 on Unix; on Windows it has the settings folder's access rights). An
    //    existing file keeps the rights it has.
    //  - Windows refuses the second open itself (no sharing). On Linux and macOS .NET takes an advisory lock (flock) that
    //    every .NET process honors, also across containers that use the same folder on one host. A file system that
    //    does not support such locks (some network shares) gives no protection, as for the refresh lock.
    //  - It is separate from the refresh lock and the repair lock and is never taken while holding either, or the other
    //    way round by the monitoring. It does not make the campground cache safe for two processes (TD-54): a monitoring
    //    and a manual search can still build one campground's cache at once.
    internal static class MonitoringLock
    {
        internal const string FileName = "Campertron.monitoring.lock";

        // The lock, held until the stream is disposed, or null and why. Never throws for a lock it can't take
        internal static FileStream? TryAcquire(string ConfigPath, out MonitoringLockProblem? Problem)
        {
            Problem = null;
            try
            {
                var Options = new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 0
                };
                if (!OperatingSystem.IsWindows())
                {
                    Options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                }
                return new FileStream(Path.Join(ConfigPath, FileName), Options);
            }
            catch (IOException ex) when (IsHeld(ex))
            {
                Problem = MonitoringLockProblem.Held;
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                Problem = MonitoringLockProblem.Unavailable;
                return null;
            }
        }

        // true for the failure of an open that another open handle refuses: Windows' sharing and lock violations, and on
        // Unix the advisory lock that would block (EWOULDBLOCK, which .NET reports as an IOException with the error number
        // as its HResult: 11 on Linux, 35 on macOS and the BSDs). Any other failure of the open is not a held lock
        internal static bool IsHeld(IOException Error)
        {
            if (Error.GetType() != typeof(IOException))
            {
                //a missing folder or file, a path too long: never a held lock
                return false;
            }
            if (OperatingSystem.IsWindows())
            {
                const int SharingViolation = 32, LockViolation = 33;
                int Code = Error.HResult & 0xFFFF;
                return Code == SharingViolation || Code == LockViolation;
            }
            const int LinuxWouldBlock = 11, BsdWouldBlock = 35;
            return Error.HResult == (OperatingSystem.IsLinux() ? LinuxWouldBlock : BsdWouldBlock);
        }

        // the words of a start the lock refused: the problem (written after "Error: " and the mode's name), what to do, and
        // that nothing happened. Fixed text: no path and no value
        internal static List<string> Describe(MonitoringLockProblem Problem, string Mode)
        {
            const string Nothing = "Nothing was searched or sent.";
            if (Problem == MonitoringLockProblem.Held)
            {
                return new List<string>
                {
                    Mode + " can't start: another Campertron is already monitoring this settings folder.",
                    "Only one monitoring runs for a settings folder, so that every opening is notified once. Stop the other one first (another CampertronConsole in email or ntfy mode, or a container that uses the same settings folder), or give this one its own settings folder.",
                    Nothing
                };
            }
            return new List<string>
            {
                Mode + " can't start: the monitoring's lock file can't be created or opened in the settings folder.",
                "Campertron keeps the file " + FileName + " open there while it monitors, so that only one monitoring runs for a settings folder. Let the user that runs Campertron write to the settings folder.",
                Nothing
            };
        }
    }
}
