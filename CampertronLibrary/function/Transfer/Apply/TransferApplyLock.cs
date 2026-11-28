using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronLibrary.function.Transfer.Apply
{
    // why the apply lock was not taken
    internal enum TransferLockProblem
    {
        // another apply holds it: in another process (a console, the desktop app, the web UI on the same settings folder) or in
        // this one
        Held,
        // the lock file can't be created or opened (a settings folder that is missing, read-only or not this user's)
        Unavailable
    }

    // The settings transfer's apply lock (D132, D134; v0.4.0-settings-transfer.md §Apply lock): one apply at a time for a
    // settings folder, shared by the interactive console, the desktop GUI and the web UI and by every process. Modeled on the
    // monitoring lock (MonitoringLock, D90): a file in the settings folder held open without sharing for the whole apply, from
    // the stale check through the backup set, the writes, the read-back, a rollback and the cleanup.
    //  - The lock is the open handle, not the file: the operating system releases it when the apply closes it or its process
    //    ends in any way, so a lock is never stale; a file left behind locks nothing, and the next apply simply opens it. A
    //    held lock is never taken over.
    //  - The file is never deleted, stays empty, and is created for its owner only (0600 on Unix; on Windows the settings
    //    folder's access rules). An existing file keeps its rights.
    //  - Separate from the monitoring, refresh and repair locks: never one of them, and never taken while holding one.
    //  - An apply is never queued: a held lock refuses it. Its words are fixed and hold no path.
    internal static class TransferApplyLock
    {
        internal const string FileName = "Campertron.transfer.lock";

        // the lock, held until the stream is disposed, or null and why; never throws for a lock it can't take
        internal static FileStream? TryAcquire(string ConfigPath, out TransferLockProblem? Problem)
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
            catch (IOException ex) when (MonitoringLock.IsHeld(ex))
            {
                // the same classification of a refused open as the monitoring lock's (its convention, not its lock)
                Problem = TransferLockProblem.Held;
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
                Problem = TransferLockProblem.Unavailable;
                return null;
            }
        }
    }
}
