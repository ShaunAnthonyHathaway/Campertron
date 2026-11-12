using CampertronLibrary.function.RecDotOrg.data;
using Microsoft.Data.Sqlite;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // Failure-safe RIDB refresh (TD-36, decision D26). The new database is built in a staging directory next to
    // the installed one (ConfigPath/RecreationDotOrg.refresh-<guid>/RecreationDotOrg.db), so the existing
    // ConfigPath-based import and validation run unchanged against it. Only after it has been populated,
    // validated and finalized, and the derived cache has been cleared, is it copied into the installed
    // RecreationDotOrg.db with SQLite's online backup API. That copy is one SQLite write transaction: it either
    // commits completely or leaves the installed database exactly as it was, including after a crash or power
    // loss (SQLite's own journal/WAL recovery). The installed database is never renamed, replaced or deleted.
    //
    // Lock order: the refresh lock (RefreshLockFileName) is taken first, for the whole refresh; the repair lock
    // (RepairLockFileName, shared with LegacyAddressLayout.RepairIfNeeded) is taken second, only around the
    // backup into the installed database. No code path takes the refresh lock while holding the repair lock.
    // See docs/ai-context/business-rules.md §8.
    public static class RidbStagedRefresh
    {
        public const string DbFileName = "RecreationDotOrg.db";
        public const string RefreshLockFileName = "RecreationDotOrg.refresh.lock";
        // the lock file LegacyAddressLayout.RepairIfNeeded takes before it renames the installed database
        public const string RepairLockFileName = "RecreationDotOrg.repair.lock";
        public const string StagingDirectoryPrefix = "RecreationDotOrg.refresh-";

        // How long the installation waits for another connection's write transaction on the installed database
        // (SQLite busy timeout), in milliseconds. Same as Microsoft.Data.Sqlite's default command timeout (30 s);
        // BackupDatabase itself does not apply that timeout.
        public static int InstallBusyTimeoutMilliseconds = 30000;

        public enum Outcome
        {
            // the validated database was installed and passed quick_check
            Installed,
            // the staging database failed RidbDatabase.Validate; the installed database was not changed
            ValidationFailed,
            // the backup committed, but quick_check on the installed database did not return ok
            InstalledButQuickCheckFailed
        }

        // Exclusive lock for the whole refresh, or null if another refresh holds it. The lock file is never deleted
        // (no DeleteOnClose), so two processes can't end up locking different files with the same name.
        public static FileStream? TryAcquireRefreshLock(string ConfigPath, out string Reason)
        {
            Reason = string.Empty;
            try
            {
                return new FileStream(Path.Join(ConfigPath, RefreshLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex)
            {
                Reason = ex.Message;
                return null;
            }
        }

        // True only for Campertron's exact staging-directory names: the prefix followed by a GUID in "N" format
        // (32 lowercase hexadecimal digits, as Guid.ToString("N") writes it).
        public static bool IsStagingDirectoryName(string Name)
        {
            if (!Name.StartsWith(StagingDirectoryPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            string Suffix = Name.Substring(StagingDirectoryPrefix.Length);
            return Suffix.Length == 32 &&
                   Suffix.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')) &&
                   Guid.TryParseExact(Suffix, "N", out _);
        }

        // Removes staging directories left by an interrupted refresh. Must be called while holding the refresh
        // lock, which proves that no other refresh is using them. Only exact staging names are touched; a link with
        // such a name is removed as a link, never followed. A directory that can't be removed is reported and left:
        // every refresh uses a new, unique staging path that must not exist yet, so a leftover can't be reused.
        public static void RemoveStaleStagingDirectories(string ConfigPath)
        {
            foreach (string Candidate in Directory.GetDirectories(ConfigPath))
            {
                if (!IsStagingDirectoryName(Path.GetFileName(Candidate)))
                {
                    continue;
                }
                if (RemoveDirectory(Candidate))
                {
                    Console.WriteLine($"Removed {Candidate}, left by an interrupted RIDB refresh");
                }
            }
        }

        // Builds, validates and installs a new database from the extracted RIDB files. Must be called while holding
        // the refresh lock. Problems receives the validation or quick_check problems. Any other failure throws; the
        // installed database is then unchanged (it is only written by the final backup, which is atomic).
        public static Outcome BuildValidateAndInstall(string ExtractDirectory, string ConfigPath, string CacheDirectory, List<string> Problems)
        {
            string InstalledDb = Path.Join(ConfigPath, DbFileName);
            string StagingDirectory = Path.Join(ConfigPath, StagingDirectoryPrefix + Guid.NewGuid().ToString("N"));
            string StagingDb = Path.Join(StagingDirectory, DbFileName);
            bool KeepStaging = false;
            try
            {
                if (Directory.Exists(StagingDirectory) || File.Exists(StagingDirectory))
                {
                    throw new IOException($"the staging path {StagingDirectory} already exists");
                }
                Directory.CreateDirectory(StagingDirectory);
                Console.WriteLine("Preparing a staging copy of the database");
                CreateStagingTemplate(InstalledDb, StagingDb);

                Console.WriteLine("Clearing Database");
                Clear.ClearDatabase(StagingDirectory);
                ReleasePooledConnections(StagingDb);

                Console.WriteLine($"Populating Database from {ExtractDirectory}");
                PopulateDbFromRIDBFiles.Populate(ExtractDirectory, StagingDirectory);
                ReleasePooledConnections(StagingDb);

                Console.WriteLine("Validating database");
                Problems.AddRange(RidbDatabase.Validate(StagingDirectory));
                ReleasePooledConnections(StagingDb);
                if (Problems.Count > 0)
                {
                    return Outcome.ValidationFailed;
                }
                // finalize: write the staging WAL into the staging file
                Checkpoint(StagingDb);

                // The derived cache is cleared before the installation: a crash in between leaves the old
                // database with an empty cache (rebuilt on demand), never the new database with old cache files.
                // If this fails, the installed database has not been touched.
                Clear.ClearRefreshCache(CacheDirectory);

                Install(StagingDb, InstalledDb, ConfigPath);
                Console.WriteLine("Installed the refreshed database");

                string QuickCheck = QuickCheckResult(InstalledDb);
                if (QuickCheck != "ok")
                {
                    KeepStaging = true;
                    Problems.Add($"quick_check on the installed database {InstalledDb} returned '{QuickCheck}' after the refreshed database was installed. " +
                                 $"The validated staging database was kept at {StagingDb}. General.yaml was not updated.");
                    return Outcome.InstalledButQuickCheckFailed;
                }
                return Outcome.Installed;
            }
            finally
            {
                ReleasePooledConnections(StagingDb);
                if (!KeepStaging)
                {
                    RemoveDirectory(StagingDirectory);
                }
            }
        }

        // The staging database starts as a copy of the installed database (the template the in-place refresh used),
        // or of the bundled database when nothing is installed. An installed database that can't be opened or copied
        // makes the refresh fail; it is only read here.
        private static void CreateStagingTemplate(string InstalledDb, string StagingDb)
        {
            if (!File.Exists(InstalledDb))
            {
                Console.WriteLine($"{InstalledDb} does not exist; the bundled database is used as the template");
                Load.ExtractEmbeddedDbTo(StagingDb);
                return;
            }
            using (SqliteConnection Source = OpenDedicated(InstalledDb, SqliteOpenMode.ReadOnly))
            using (SqliteConnection Destination = OpenDedicated(StagingDb, SqliteOpenMode.ReadWriteCreate))
            {
                Source.BackupDatabase(Destination);
            }
        }

        // Copies the validated staging database into the installed database with the online backup API, under the
        // repair lock. A missing installed database is first created from the bundled database by the same
        // write-then-move extraction startup uses, so the installed path never holds an empty or partial file;
        // if the backup into it then fails, it is removed again (with its -wal/-shm/-journal files), so the next
        // start extracts the bundled database as on a first run. Public only so tests can make the backup fail;
        // production calls it from BuildValidateAndInstall.
        public static void Install(string StagingDb, string InstalledDb, string ConfigPath)
        {
            Console.WriteLine("Installing the refreshed database");
            using (FileStream RepairLock = new FileStream(Path.Join(ConfigPath, RepairLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                bool Existed = File.Exists(InstalledDb);
                if (!Existed)
                {
                    Load.ExtractEmbeddedDbTo(InstalledDb);
                }
                try
                {
                    using (SqliteConnection Source = OpenDedicated(StagingDb, SqliteOpenMode.ReadOnly))
                    using (SqliteConnection Destination = OpenDedicated(InstalledDb, SqliteOpenMode.ReadWrite))
                    {
                        Execute(Destination, $"PRAGMA busy_timeout = {InstallBusyTimeoutMilliseconds}");
                        Source.BackupDatabase(Destination);
                    }
                }
                catch (Exception) when (!Existed)
                {
                    ReleasePooledConnections(InstalledDb);
                    RemoveCreatedDatabase(InstalledDb);
                    throw;
                }
            }
            // best effort: a reader that still uses the previous snapshot keeps the WAL until it is done
            try
            {
                if (!Checkpoint(InstalledDb))
                {
                    Console.WriteLine("Another connection is still reading the previous data; the database file is updated when it is done");
                }
            }
            catch (SqliteException ex)
            {
                Console.WriteLine($"The checkpoint after the installation was skipped: {ex.Message}");
            }
        }

        private static void RemoveCreatedDatabase(string InstalledDb)
        {
            foreach (string File_ in new[] { InstalledDb, InstalledDb + "-wal", InstalledDb + "-shm", InstalledDb + "-journal" })
            {
                try
                {
                    if (File.Exists(File_))
                    {
                        File.Delete(File_);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.WriteLine($"Could not remove {File_}: {ex.Message}");
                }
            }
        }

        // PRAGMA wal_checkpoint(TRUNCATE); true if it completed, false if another connection kept it from completing
        private static bool Checkpoint(string DbFile)
        {
            using (SqliteConnection Connection = OpenDedicated(DbFile, SqliteOpenMode.ReadWrite))
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                using (SqliteDataReader Reader = Command.ExecuteReader())
                {
                    return Reader.Read() && Reader.GetInt64(0) == 0;
                }
            }
        }

        // "ok", the first quick_check problem, or why the installed database could not be checked
        private static string QuickCheckResult(string DbFile)
        {
            try
            {
                using (SqliteConnection Connection = OpenDedicated(DbFile, SqliteOpenMode.ReadWrite))
                using (SqliteCommand Command = Connection.CreateCommand())
                {
                    Command.CommandText = "PRAGMA quick_check";
                    return System.Convert.ToString(Command.ExecuteScalar()) ?? "";
                }
            }
            catch (SqliteException ex)
            {
                return "could not be run: " + ex.Message;
            }
        }

        private static SqliteConnection OpenDedicated(string DbFile, SqliteOpenMode Mode)
        {
            var Builder = new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = Mode, Pooling = false };
            var Connection = new SqliteConnection(Builder.ToString());
            Connection.Open();
            return Connection;
        }

        private static void Execute(SqliteConnection Connection, string Sql)
        {
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = Sql;
                Command.ExecuteNonQuery();
            }
        }

        // Closes the idle pooled connections that RecreationDotOrgContext opened on DbFile (its connection string is
        // "Data Source={DbPath}"), and only those. Every context has been disposed by then.
        private static void ReleasePooledConnections(string DbFile)
        {
            using (var Key = new SqliteConnection($"Data Source={DbFile}"))
            {
                SqliteConnection.ClearPool(Key);
            }
        }

        // best effort; returns false (after reporting) if the directory could not be removed
        private static bool RemoveDirectory(string DirectoryPath)
        {
            try
            {
                var Info = new DirectoryInfo(DirectoryPath);
                if (!Info.Exists)
                {
                    return true;
                }
                if (Info.LinkTarget != null)
                {
                    Info.Delete();
                }
                else
                {
                    Info.Delete(true);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine($"Could not remove {DirectoryPath}: {ex.Message}");
                return false;
            }
        }
    }
}
