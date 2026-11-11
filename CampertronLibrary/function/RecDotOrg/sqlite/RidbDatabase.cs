using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // Rules for the content of RecreationDotOrg.db and the checks run after every RIDB refresh and
    // against the bundled database. See docs/ai-context/business-rules.md §8.
    public static class RidbDatabase
    {
        // RIDB export tables that Campertron never reads. They are not imported, are dropped by
        // Clear.ClearDatabase, and must be absent from the database. Their model classes
        // (class/RecDotOrg/*.cs) and JSON loaders (LoadJsonFromFile) are kept so a table can be
        // restored for a future feature: add its DbSet (and composite key, if any) back to
        // RecreationDotOrgContext, its import block back to PopulateDbFromRIDBFiles, remove it from
        // this list and refresh the database.
        public static readonly IReadOnlyList<string> OmittedTables = new[]
        {
            "ActivityEntries",                  // Activities_API_v1.json
            "EntityActivitiesEntries",          // EntityActivities_API_v1.json
            "EventsEntries",                    // Events_API_v1.json
            "LinksEntries",                     // Links_API_v1.json
            "MediaEntries",                     // Media_API_v1.json
            "MemberToursEntries",               // MemberTours_API_v1.json
            "OrganizationEntries",              // Organizations_API_v1.json
            "OrgEntitiesEntries",               // OrgEntities_API_v1.json
            "PermitEntranceAttributesEntries",  // PermitEntranceAttributes_API_v1.json
            "PermitEntrancesEntries",           // PermitEntrances_API_v1.json
            "PermitEntranceZonesEntries",       // PermitEntranceZones_API_v1.json
            "RecAreaAddressesEntries",          // RecAreaAddresses_API_v1.json
            "RecAreaFacilitiesEntries",         // RecAreaFacilities_API_v1.json
            "TourAttributesEntries",            // TourAttributes_API_v1.json
            "ToursEntries"                      // Tours_API_v1.json
        };

        public const string WatchmanName = "WATCHMAN CAMPGROUND";
        private static readonly Regex StateCodePattern = new Regex("^[A-Z]{2}$");

        // Every table and column the EF model maps; these must exist in the database.
        public static Dictionary<string, List<string>> RequiredColumns()
        {
            var Result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            using (var db = new RecreationDotOrgContext(Path.GetTempPath()))
            {
                foreach (var EntityType in db.Model.GetEntityTypes())
                {
                    string? Table = EntityType.GetTableName();
                    if (Table == null)
                    {
                        continue;
                    }
                    var Identifier = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(Table, EntityType.GetSchema());
                    Result[Table] = EntityType.GetProperties()
                        .Select(p => p.GetColumnName(Identifier))
                        .Where(c => c != null)
                        .Select(c => c!)
                        .ToList();
                }
            }
            return Result;
        }

        // Returns the problems found; an empty list means the database passed every check.
        public static List<string> Validate(string ConfigPath)
        {
            var Problems = new List<string>();
            string DbFile = Path.Join(ConfigPath, "RecreationDotOrg.db");
            if (!File.Exists(DbFile))
            {
                Problems.Add($"{DbFile} does not exist");
                return Problems;
            }
            try
            {
                var Builder = new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
                using (var Connection = new SqliteConnection(Builder.ToString()))
                {
                    Connection.Open();
                    string Integrity = ScalarString(Connection, "PRAGMA integrity_check");
                    if (Integrity != "ok")
                    {
                        Problems.Add($"integrity_check returned '{Integrity}'");
                        return Problems;
                    }
                    var Tables = new HashSet<string>(StringComparer.Ordinal);
                    using (var Command = Connection.CreateCommand())
                    {
                        Command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                        using (var Reader = Command.ExecuteReader())
                        {
                            while (Reader.Read()) Tables.Add(Reader.GetString(0));
                        }
                    }
                    foreach (var Required in RequiredColumns())
                    {
                        if (!Tables.Contains(Required.Key))
                        {
                            Problems.Add($"required table {Required.Key} is missing");
                            continue;
                        }
                        var Columns = new HashSet<string>(StringComparer.Ordinal);
                        using (var Command = Connection.CreateCommand())
                        {
                            Command.CommandText = "SELECT name FROM pragma_table_info($table)";
                            Command.Parameters.AddWithValue("$table", Required.Key);
                            using (var Reader = Command.ExecuteReader())
                            {
                                while (Reader.Read()) Columns.Add(Reader.GetString(0));
                            }
                        }
                        foreach (string Column in Required.Value.Where(c => !Columns.Contains(c)))
                        {
                            Problems.Add($"required column {Required.Key}.{Column} is missing");
                        }
                    }
                    foreach (string Omitted in OmittedTables.Where(Tables.Contains))
                    {
                        Problems.Add($"table {Omitted} should have been omitted but is present");
                    }
                }
                if (Problems.Count > 0)
                {
                    return Problems;
                }
                List<string> States = Read.UniqueStates(ConfigPath);
                if (States.Count == 0)
                {
                    Problems.Add("the state list is empty");
                }
                foreach (string State in States.Where(s => !StateCodePattern.IsMatch(s)).Take(10))
                {
                    Problems.Add($"the state list contains '{State}', which is not a two-letter state code");
                }
                if (!Read.UniqueParks("UT", "SPRINGDALE", ConfigPath).Contains(WatchmanName))
                {
                    Problems.Add($"{WatchmanName} is not listed under UT / SPRINGDALE");
                }
            }
            catch (Exception ex) when (ex is SqliteException || ex is IOException || ex is InvalidOperationException)
            {
                Problems.Add($"the database could not be read: {ex.Message}");
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
            return Problems;
        }

        private static string ScalarString(SqliteConnection Connection, string Sql)
        {
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = Sql;
                return System.Convert.ToString(Command.ExecuteScalar()) ?? "";
            }
        }
    }
}
