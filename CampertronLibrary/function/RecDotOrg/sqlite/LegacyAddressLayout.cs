using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // One-time startup repair of an installed RecreationDotOrg.db that no current Campertron would produce.
    // It replaces two generations of databases with the bundled one:
    //  - LegacyLayout: built from the RIDB export of 2024-11-18, in which FacilityAddresses.PostalCode held
    //    the state and AddressStateCode the ZIP code (swapped).
    //  - ObsoleteTables: corrected column layout, but still containing RIDB tables that Campertron no longer
    //    keeps (RidbDatabase.OmittedTables). Only code from before PR #17 created those tables, and every
    //    current refresh drops them, so they mark a database written by a version before 0.1.0-beta.4,
    //    whose importer also left AddressStateCode un-normalized (full state names next to the codes).
    // A database is replaced only when its condition is positively detected, it contains nothing Campertron
    // does not create, and the bundled database passes RidbDatabase.Validate and needs no repair itself.
    // The old database is renamed to a backup, never deleted. A database that satisfies the current
    // invariants, including any later "Refresh Ridb Data", is never replaced, so the repair runs at most
    // once. See docs/ai-context/business-rules.md §8.
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

        public enum RepairReason
        {
            None,
            LegacyLayout,
            ObsoleteTables
        }

        // The cache files (CachePath) derived from RecreationDotOrg.db, by suffix: {CampgroundID}-CampInfo,
        // -Campsites, -CampsitesByPark and -Cached, {CampsiteID}-Equipment and -Attributes. They are rebuilt
        // from the database when missing. Other files in the cache directory are never touched.
        public static readonly IReadOnlyList<string> DerivedCacheSuffixes = new[]
        {
            "-CampInfo.json", "-Campsites.json", "-CampsitesByPark.json", "-Cached.json", "-Equipment.json", "-Attributes.json"
        };

        // Omitted RIDB tables (RidbDatabase.OmittedTables) present in the database: one sqlite_master query.
        public static List<string> PresentOmittedTables(string DbFile)
        {
            var Present = new List<string>();
            using (var Connection = OpenConnection(DbFile, SqliteOpenMode.ReadOnly))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                using (var Reader = Command.ExecuteReader())
                {
                    while (Reader.Read())
                    {
                        string Name = Reader.GetString(0);
                        if (RidbDatabase.OmittedTables.Contains(Name))
                        {
                            Present.Add(Name);
                        }
                    }
                }
            }
            return Present;
        }

        // The values the GUI state lists show: the same rows and conditions as Read.UniqueStates, in plain SQL
        // so it also works on databases with an older schema. Distinct, ordered with ordinal comparison.
        public static List<string> GuiStateValues(string DbFile)
        {
            var Values = new SortedSet<string>(StringComparer.Ordinal);
            using (var Connection = OpenConnection(DbFile, SqliteOpenMode.ReadOnly))
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText =
                    "SELECT DISTINCT a.AddressStateCode FROM FacilitiesEntries f " +
                    "JOIN FacilityAddressesEntries a ON f.FacilityID = a.FacilityID " +
                    "WHERE f.FacilityTypeDescription = 'Campground' AND a.AddressStateCode IS NOT NULL " +
                    "AND length(trim(a.AddressStateCode)) > 0 AND a.FacilityAddressType IN ('Physical', 'Default') " +
                    "AND f.Reservable = 1 AND f.Enabled = 1";
                using (var Reader = Command.ExecuteReader())
                {
                    while (Reader.Read())
                    {
                        Values.Add(Reader.GetString(0));
                    }
                }
            }
            return Values.ToList();
        }

        // GUI state values that are not exactly two uppercase letters (full names, padded values, ZIP codes).
        public static List<string> NonNormalizedGuiStates(string DbFile)
        {
            return GuiStateValues(DbFile).Where(v => !StateCodePattern.IsMatch(v)).ToList();
        }

        // Why the installed database must be replaced, or None. Cost: the layout scan of FacilityAddresses
        // that the repair has always done, plus one sqlite_master query.
        public static RepairReason DetectRepairReason(string DbFile)
        {
            Layout Current = DetectLayout(DbFile);
            if (Current == Layout.LegacySwapped)
            {
                return RepairReason.LegacyLayout;
            }
            if (Current == Layout.Corrected && PresentOmittedTables(DbFile).Count > 0)
            {
                return RepairReason.ObsoleteTables;
            }
            return RepairReason.None;
        }

        // Production entry point (Load.DbExistsCheck, shared by the console and the GUI): uses the database
        // embedded in CampertronLibrary as the replacement.
        public static bool RepairIfNeeded(string ConfigPath, string? CachePath)
        {
            if (_BundledDbUsable == false)
            {
                return false;
            }
            bool? BundledUsable = null;
            bool Repaired = RepairIfNeeded(ConfigPath, CachePath, data.Load.ExtractEmbeddedDbTo, ref BundledUsable);
            if (BundledUsable.HasValue)
            {
                _BundledDbUsable = BundledUsable;
            }
            return Repaired;
        }

        public static bool RepairIfNeeded(string ConfigPath, string? CachePath, Action<string> ExtractBundledDbTo)
        {
            bool? BundledUsable = null;
            return RepairIfNeeded(ConfigPath, CachePath, ExtractBundledDbTo, ref BundledUsable);
        }

        private static string Describe(RepairReason Reason, string DbFile)
        {
            if (Reason == RepairReason.LegacyLayout)
            {
                return "uses the 2024 RIDB address layout";
            }
            string Detail = "was built by a Campertron version before 0.1.0-beta.4 (it still contains RIDB tables that are no longer used";
            try
            {
                List<string> Invalid = NonNormalizedGuiStates(DbFile);
                if (Invalid.Count > 0)
                {
                    Detail += $", and its state list contains {Invalid.Count} value(s) that are not two-letter codes, such as '{Invalid[0].Trim()}'";
                }
            }
            catch (SqliteException) { }
            return Detail + ")";
        }

        private static bool RepairIfNeeded(string ConfigPath, string? CachePath, Action<string> ExtractBundledDbTo, ref bool? BundledUsable)
        {
            string DbFile = Path.Join(ConfigPath, "RecreationDotOrg.db");
            string TempDirectory = Path.Join(ConfigPath, "RecreationDotOrg.repair-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (!File.Exists(DbFile))
                {
                    return false;
                }
                RepairReason Reason = DetectRepairReason(DbFile);
                if (Reason == RepairReason.None)
                {
                    return false;
                }
                string Condition = Describe(Reason, DbFile);
                if (!HasOnlyKnownSchemaObjects(DbFile))
                {
                    Console.WriteLine($"{DbFile} {Condition}, but it contains tables or other objects that Campertron does not create, so it was not replaced automatically. Use \"Refresh Ridb Data\" to rebuild it.");
                    return false;
                }
                string LockFile = Path.Join(ConfigPath, "RecreationDotOrg.repair.lock");
                using (var Lock = new FileStream(LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                    // re-check under the lock in case another process repaired the database meanwhile
                    if (!File.Exists(DbFile) || DetectRepairReason(DbFile) != Reason)
                    {
                        return false;
                    }
                    Directory.CreateDirectory(TempDirectory);
                    string TempDb = Path.Join(TempDirectory, "RecreationDotOrg.db");
                    ExtractBundledDbTo(TempDb);
                    // the replacement must satisfy every current invariant, or the repair would not be final
                    bool BundledValid = File.Exists(TempDb) && DetectLayout(TempDb) == Layout.Corrected &&
                                        HasOnlyKnownSchemaObjects(TempDb) && DetectRepairReason(TempDb) == RepairReason.None &&
                                        RidbDatabase.Validate(TempDirectory).Count == 0;
                    SqliteConnection.ClearAllPools();
                    if (!BundledValid)
                    {
                        BundledUsable = false;
                        Console.WriteLine($"{DbFile} {Condition}, but the bundled database did not pass validation, so it was not replaced. Use \"Refresh Ridb Data\" to rebuild it.");
                        return false;
                    }
                    BundledUsable = true;
                    if (!CheckpointAndRelease(DbFile))
                    {
                        Console.WriteLine($"{DbFile} {Condition} but is in use, so it was not replaced. It will be checked again on the next start.");
                        return false;
                    }
                    string Label = Reason == RepairReason.LegacyLayout ? "legacy-layout" : "pre-0.1.0-beta.4";
                    string BackupFile = DbFile + "." + Label + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".bak";
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
                    Console.WriteLine($"Replaced {DbFile}, which {Condition}, with the bundled database. The previous database was kept as {BackupFile} and can be deleted.");
                    ClearDerivedCache(CachePath);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SqliteException || ex is InvalidDataException)
            {
                Console.WriteLine($"Could not check or repair {DbFile}: {ex.Message}. The database was left unchanged.");
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

        // After a replacement, removes the cache files derived from the previous database (DerivedCacheSuffixes),
        // so searches rebuild them from the new data. Returns the number of files removed. A file that can't
        // be removed is reported and left; the repair itself stands.
        public static int ClearDerivedCache(string? CachePath)
        {
            if (string.IsNullOrEmpty(CachePath) || !Directory.Exists(CachePath))
            {
                return 0;
            }
            int Removed = 0;
            foreach (string File_ in Directory.GetFiles(CachePath))
            {
                string Name = Path.GetFileName(File_);
                if (!DerivedCacheSuffixes.Any(s => Name.EndsWith(s, StringComparison.Ordinal)))
                {
                    continue;
                }
                try
                {
                    File.Delete(File_);
                    Removed++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.WriteLine($"Could not remove the cached file {File_}: {ex.Message}");
                }
            }
            if (Removed > 0)
            {
                Console.WriteLine($"Removed {Removed} cached campground file(s) derived from the previous database; they are rebuilt on the next search.");
            }
            return Removed;
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
