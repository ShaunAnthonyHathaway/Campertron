using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // One-time compatibility repair for databases built from the RIDB export of 2024-11-18, in which
    // FacilityAddresses.PostalCode held the state and AddressStateCode held the ZIP code (swapped).
    // The repair replaces such a database with the bundled one, but only when the layout is positively
    // detected, the database contains nothing Campertron does not create, and the bundled database is
    // itself verified to have the corrected layout. The old database is renamed to a backup, never deleted.
    // See docs/ai-context/business-rules.md §8.
    public static class LegacyAddressLayout
    {
        public enum Layout
        {
            Unknown,
            Corrected,
            LegacySwapped
        }

        // Minimum number of non-empty values in each column before any layout is reported.
        public const int MinimumSample = 100;
        // Share of non-empty values that must match the expected pattern.
        public const double Threshold = 0.95;

        // Every table Campertron has created in RecreationDotOrg.db. Indexes are only allowed as
        // SQLite's automatic primary-key indexes on these tables. Anything else means the database
        // was modified outside Campertron and is not replaced automatically.
        public static readonly HashSet<string> KnownTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "ActivityEntries", "CampsiteAttributesEntries", "CampsitesEntries", "EntityActivitiesEntries",
            "EventsEntries", "FacilitiesEntries", "FacilityAddressesEntries", "LinksEntries", "MediaEntries",
            "MemberToursEntries", "OrgEntitiesEntries", "OrganizationEntries", "PermitEntranceAttributesEntries",
            "PermitEntranceZonesEntries", "PermitEntrancesEntries", "PermittedEquipmentEntries",
            "RecAreaAddressesEntries", "RecAreaEntries", "RecAreaFacilitiesEntries", "TourAttributesEntries",
            "ToursEntries", "__EFMigrationsHistory", "sqlite_sequence"
        };

        private static readonly Regex StateCodePattern = new Regex("^[A-Z]{2}$");
        private static readonly Regex ZipCodePattern = new Regex("^[0-9]{5}(-?[0-9]{4})?$");

        // null = not checked yet in this process; false = the bundled database is not usable for the repair
        private static bool? _BundledDbUsable = null;

        public static Layout Classify(IEnumerable<(string? PostalCode, string? AddressStateCode)> Rows)
        {
            int PostalNonEmpty = 0, PostalState = 0, StateNonEmpty = 0, StateState = 0, StateZip = 0;
            foreach (var Row in Rows)
            {
                string Postal = Row.PostalCode?.Trim().ToUpper() ?? "";
                string State = Row.AddressStateCode?.Trim().ToUpper() ?? "";
                if (Postal.Length > 0)
                {
                    PostalNonEmpty++;
                    if (StateCodePattern.IsMatch(Postal)) PostalState++;
                }
                if (State.Length > 0)
                {
                    StateNonEmpty++;
                    if (StateCodePattern.IsMatch(State)) StateState++;
                    if (ZipCodePattern.IsMatch(State)) StateZip++;
                }
            }
            if (PostalNonEmpty < MinimumSample || StateNonEmpty < MinimumSample)
            {
                return Layout.Unknown;
            }
            if (PostalState >= Threshold * PostalNonEmpty && StateZip >= Threshold * StateNonEmpty)
            {
                return Layout.LegacySwapped;
            }
            if (StateState >= Threshold * StateNonEmpty && PostalState <= (1 - Threshold) * PostalNonEmpty)
            {
                return Layout.Corrected;
            }
            return Layout.Unknown;
        }

        public static Layout DetectLayout(string DbFile)
        {
            var Rows = new List<(string?, string?)>();
            using (var Connection = OpenConnection(DbFile, SqliteOpenMode.ReadOnly))
            {
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'FacilityAddressesEntries'";
                    if (Command.ExecuteScalar() == null)
                    {
                        return Layout.Unknown;
                    }
                    Command.CommandText = "SELECT PostalCode, AddressStateCode FROM FacilityAddressesEntries";
                    using (var Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read())
                        {
                            Rows.Add((Reader.IsDBNull(0) ? null : Reader.GetString(0), Reader.IsDBNull(1) ? null : Reader.GetString(1)));
                        }
                    }
                }
            }
            return Classify(Rows);
        }

        public static bool HasOnlyKnownSchemaObjects(string DbFile)
        {
            using (var Connection = OpenConnection(DbFile, SqliteOpenMode.ReadOnly))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = "SELECT type, name, tbl_name FROM sqlite_master";
                using (var Reader = Command.ExecuteReader())
                {
                    while (Reader.Read())
                    {
                        string Type = Reader.GetString(0);
                        string Name = Reader.GetString(1);
                        string Table = Reader.IsDBNull(2) ? "" : Reader.GetString(2);
                        bool Known = (Type == "table" && KnownTables.Contains(Name)) ||
                                     (Type == "index" && Name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal) && KnownTables.Contains(Table));
                        if (!Known)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        // Production entry point: uses the database embedded in CampertronLibrary as the replacement.
        public static bool RepairIfLegacy(string ConfigPath)
        {
            if (_BundledDbUsable == false)
            {
                return false;
            }
            bool? BundledUsable = null;
            bool Repaired = RepairIfLegacy(ConfigPath, data.Load.ExtractEmbeddedDbTo, ref BundledUsable);
            if (BundledUsable.HasValue)
            {
                _BundledDbUsable = BundledUsable;
            }
            return Repaired;
        }

        public static bool RepairIfLegacy(string ConfigPath, Action<string> ExtractBundledDbTo)
        {
            bool? BundledUsable = null;
            return RepairIfLegacy(ConfigPath, ExtractBundledDbTo, ref BundledUsable);
        }

        private static bool RepairIfLegacy(string ConfigPath, Action<string> ExtractBundledDbTo, ref bool? BundledUsable)
        {
            string DbFile = Path.Join(ConfigPath, "RecreationDotOrg.db");
            string TempDirectory = Path.Join(ConfigPath, "RecreationDotOrg.repair-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!File.Exists(DbFile) || DetectLayout(DbFile) != Layout.LegacySwapped)
                {
                    return false;
                }
                if (!HasOnlyKnownSchemaObjects(DbFile))
                {
                    Console.WriteLine($"{DbFile} uses the 2024 RIDB address layout but contains tables or other objects that Campertron does not create, so it was not replaced automatically. Use \"Refresh Ridb Data\" to rebuild it.");
                    return false;
                }
                string LockFile = Path.Join(ConfigPath, "RecreationDotOrg.repair.lock");
                using (var Lock = new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    // re-check under the lock in case another process repaired the database meanwhile
                    if (!File.Exists(DbFile) || DetectLayout(DbFile) != Layout.LegacySwapped)
                    {
                        return false;
                    }
                    Directory.CreateDirectory(TempDirectory);
                    string TempDb = Path.Join(TempDirectory, "RecreationDotOrg.db");
                    ExtractBundledDbTo(TempDb);
                    if (!File.Exists(TempDb) || DetectLayout(TempDb) != Layout.Corrected || !HasOnlyKnownSchemaObjects(TempDb))
                    {
                        BundledUsable = false;
                        Console.WriteLine($"{DbFile} uses the 2024 RIDB address layout, but the bundled database does not have the corrected layout, so it was not replaced. Use \"Refresh Ridb Data\" to rebuild it.");
                        return false;
                    }
                    BundledUsable = true;
                    if (!CheckpointAndRelease(DbFile))
                    {
                        Console.WriteLine($"{DbFile} uses the 2024 RIDB address layout but is in use, so it was not replaced. It will be checked again on the next start.");
                        return false;
                    }
                    string BackupFile = DbFile + ".legacy-layout-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
                    if (File.Exists(BackupFile))
                    {
                        return false;
                    }
                    File.Move(DbFile, BackupFile);
                    MoveSidecars(DbFile, BackupFile);
                    try
                    {
                        File.Move(TempDb, DbFile);
                    }
                    catch
                    {
                        // put the original database back exactly as it was
                        File.Move(BackupFile, DbFile);
                        MoveSidecars(BackupFile, DbFile);
                        throw;
                    }
                    Console.WriteLine($"Replaced {DbFile}, which used the 2024 RIDB address layout, with the bundled database. The previous database was kept as {BackupFile} and can be deleted.");
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SqliteException || ex is InvalidDataException)
            {
                Console.WriteLine($"Could not check or repair the RIDB address layout of {DbFile}: {ex.Message}. The database was left unchanged.");
                return false;
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                try
                {
                    if (Directory.Exists(TempDirectory))
                    {
                        Directory.Delete(TempDirectory, true);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static SqliteConnection OpenConnection(string DbFile, SqliteOpenMode Mode)
        {
            var Builder = new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = Mode, Pooling = false };
            var Connection = new SqliteConnection(Builder.ToString());
            Connection.Open();
            return Connection;
        }

        // Writes any WAL content into the main file so the database is self-contained before it is moved.
        // Returns false if another connection prevents a complete checkpoint.
        private static bool CheckpointAndRelease(string DbFile)
        {
            using (var Connection = OpenConnection(DbFile, SqliteOpenMode.ReadWrite))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                using (var Reader = Command.ExecuteReader())
                {
                    return Reader.Read() && Reader.GetInt64(0) == 0;
                }
            }
        }

        private static void MoveSidecars(string FromDb, string ToDb)
        {
            foreach (string Suffix in new[] { "-wal", "-shm" })
            {
                if (File.Exists(FromDb + Suffix))
                {
                    File.Move(FromDb + Suffix, ToDb + Suffix);
                }
            }
        }
    }
}
