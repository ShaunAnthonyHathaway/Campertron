using Microsoft.Data.Sqlite;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // Campertron's own secondary index of RecreationDotOrg.db (decision D51, business-rules §8): the campsite attributes
    // by campsite (CampsiteAttributesEntries.EntityID). The table's primary key starts with AttributeID, so every lookup of
    // one campsite's attributes scanned all 2,407,137 rows of the shipped data: about 99 ms for one campsite, so building a
    // campground's search cache (one lookup per campsite) took 3.7 s for Watchman and 6.6 s for a 360-campsite campground,
    // and loading a campground's filter choices 0.19 to 0.35 s. With the index: about 0.4 ms per campsite, 37 ms and 58 ms,
    // and 22 to 34 ms (measured 2026-11-16, build-and-test.md §AQ). It adds about 35 MB to the 226 MB database and takes
    // about 0.7 s to build once. It changes no row.
    //
    // Where it is made (CREATE INDEX IF NOT EXISTS, so every path is idempotent):
    //   - every start, after the startup repair and any automatic refresh (Load.DbExistsCheck, EnsureQuietly): the bundled
    //     database just extracted, and a database installed by an earlier version, get it once; a database that has it is
    //     only read (one sqlite_master query, nothing is written);
    //   - the RIDB refresh, on the staging database after the import and before its validation (Create), so an installed
    //     refresh always has it; the clear before the import drops it (Clear.ClearDatabase), so the import inserts without it
    //     and the index is built once at the end.
    // The bundled database itself does not have it (the embedded resource is unchanged); the next refresh of the embedded
    // database produces one that has it. The startup repair counts it among the objects Campertron creates
    // (LegacyAddressLayout.HasOnlyKnownSchemaObjects), and RidbDatabase.Validate does not require it.
    public static class RidbIndexes
    {
        public const string CampsiteAttributesByCampsite = "IX_CampsiteAttributesEntries_EntityID";
        private const string AttributesTable = "CampsiteAttributesEntries";
        private const string CreateSql = "CREATE INDEX IF NOT EXISTS \"" + CampsiteAttributesByCampsite + "\" ON \"" + AttributesTable + "\" (\"EntityID\")";
        // a start waits this long for another process's write (a refresh's installation) before it leaves the index for later
        internal static int StartupBusyTimeoutSeconds = 5;

        // the indexes Campertron creates, with their table
        public static readonly IReadOnlyDictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CampsiteAttributesByCampsite] = AttributesTable
        };

        // whether the database has the index (read-only; a database without the attributes table has none)
        public static bool Exists(string DbFile)
        {
            using (var Connection = Open(DbFile, SqliteOpenMode.ReadOnly))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'index' AND name = $name AND tbl_name = $table";
                Command.Parameters.AddWithValue("$name", CampsiteAttributesByCampsite);
                Command.Parameters.AddWithValue("$table", AttributesTable);
                return Convert.ToInt64(Command.ExecuteScalar()) > 0;
            }
        }

        // creates the index if the database has the attributes table and not the index yet; true if it was created. Throws
        // on failure (the database is then unchanged: CREATE INDEX is one transaction)
        public static bool Create(string DbFile, int BusyTimeoutSeconds = 30)
        {
            if (!File.Exists(DbFile))
            {
                throw new FileNotFoundException("The database does not exist.", DbFile);
            }
            if (Exists(DbFile) || !HasAttributesTable(DbFile))
            {
                return false;
            }
            //Microsoft.Data.Sqlite retries a busy command until its command timeout (whole seconds, 0 is none), so both bound
            //the wait for another process's write
            using (var Connection = Open(DbFile, SqliteOpenMode.ReadWrite, Math.Max(1, BusyTimeoutSeconds)))
            {
                Execute(Connection, $"PRAGMA busy_timeout = {Math.Max(1, BusyTimeoutSeconds) * 1000}");
                Execute(Connection, CreateSql);
            }
            return true;
        }

        // A start's best effort: creates the index if it is missing, and leaves the database as it is if it can't (read-only,
        // locked by another process longer than StartupBusyTimeoutMilliseconds, full disk, not a database). Searches work
        // without the index, only more slowly, and the next start tries again. Nothing is written to the console.
        public static bool EnsureQuietly(string DbFile)
        {
            try
            {
                return Create(DbFile, StartupBusyTimeoutSeconds);
            }
            catch (Exception ex) when (ex is SqliteException || ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                return false;
            }
        }

        // the SQL the refresh's clear runs before the import (Clear.ClearDatabase)
        internal const string DropSql = "DROP INDEX IF EXISTS \"" + CampsiteAttributesByCampsite + "\"";

        private static bool HasAttributesTable(string DbFile)
        {
            using (var Connection = Open(DbFile, SqliteOpenMode.ReadOnly))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $table";
                Command.Parameters.AddWithValue("$table", AttributesTable);
                return Convert.ToInt64(Command.ExecuteScalar()) > 0;
            }
        }

        // a dedicated connection (no pool, so nothing stays open on the file); ReadWrite never creates a database
        private static SqliteConnection Open(string DbFile, SqliteOpenMode Mode, int TimeoutSeconds = 30)
        {
            var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = Mode, Pooling = false, DefaultTimeout = TimeoutSeconds }.ToString());
            Connection.Open();
            return Connection;
        }

        private static void Execute(SqliteConnection Connection, string Sql)
        {
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = Sql;
                Command.ExecuteNonQuery();
            }
        }
    }
}
