using System.Globalization;
using System.Text.Json;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Tests for PopulateDbFromRIDBFiles.Populate, which inserts the six RIDB tables with prepared SQLite
    // commands (RidbTableInserter, decision D25). The result must be identical to what the previous EF Core
    // Add/SaveChanges import wrote, including the rowid order, which follows EF Core's SaveChanges order and
    // depends on the current culture (TD-36). The comparison uses TestOnlyEfReferencePopulation, a copy of the
    // previous import that exists only in this test project.
    //
    // Every test works on a copy of the shipped database with its tables emptied by Clear.ClearDatabase, in its
    // own temporary directory. No test touches LocalApplicationData, the network or the user's data.
    public class PopulateTests
    {
        private static readonly string[] DataTables =
        {
            "CampsiteAttributesEntries", "CampsitesEntries", "FacilitiesEntries",
            "FacilityAddressesEntries", "PermittedEquipmentEntries", "RecAreaEntries"
        };

        private string TemplateDirectory = string.Empty;
        private List<string> ShippedSchema = new List<string>();
        private Dictionary<string, HashSet<string>> ShippedStorageTypes = new Dictionary<string, HashSet<string>>();
        private readonly List<string> Directories = new List<string>();

        [OneTimeSetUp]
        public void CreateTemplate()
        {
            // The shipped database, emptied: real schema (including __EFMigrationsHistory), no data rows.
            TemplateDirectory = NewDirectory("CampertronPopulateTemplate-");
            string DbFile = Path.Join(TemplateDirectory, "RecreationDotOrg.db");
            Load.ExtractEmbeddedDbTo(DbFile);
            ShippedSchema = Schema(DbFile);
            ShippedStorageTypes = StorageTypes(DbFile);
            Clear.ClearDatabase(TemplateDirectory);
            Checkpoint(DbFile);
        }

        [OneTimeTearDown]
        public void DeleteDirectories()
        {
            SqliteConnection.ClearAllPools();
            foreach (string ThisDirectory in Directories.Where(Directory.Exists))
            {
                Directory.Delete(ThisDirectory, true);
            }
        }

        [TearDown]
        public void ReleaseConnections()
        {
            SqliteConnection.ClearAllPools();
        }

        // ---- equivalence with the previous EF Core import ----

        [Test]
        public void Populate_AllSixTables_MatchTheEfReferenceRowForRowAndInRowidOrder()
        {
            InCulture("en-US", () =>
            {
                RidbFixture Fixture = RidbFixture.Create();
                string Optimized = PopulateWith(Fixture, PopulateDbFromRIDBFiles.Populate);
                string Reference = PopulateWith(Fixture, TestOnlyEfReferencePopulation.Populate);

                Assert.That(Differences(Optimized, Reference), Is.Empty);
                Assert.That(RowCounts(Optimized), Is.EqualTo(Fixture.ExpectedRowCounts()));
                Assert.That(RowCounts(Optimized).Values, Has.All.GreaterThan(0));
            });
        }

        [Test]
        public void Populate_KeepsTheShippedSchemaPrimaryKeysAndIndexes()
        {
            string Optimized = PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate);

            Assert.That(Schema(DbFileIn(Optimized)), Is.EqualTo(ShippedSchema));
            foreach (RidbTableInserter.TableMapping Mapping in RidbTableInserter.Mappings)
            {
                Assert.That(PrimaryKeyColumns(DbFileIn(Optimized), Mapping.Table), Is.EqualTo(Mapping.KeyColumns), Mapping.Table);
            }
        }

        [Test]
        public void Populate_StoresTheSameSqliteTypesAsTheShippedDatabase()
        {
            string Optimized = PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate);

            foreach (var Column in StorageTypes(DbFileIn(Optimized)))
            {
                Assert.That(ShippedStorageTypes.ContainsKey(Column.Key), Is.True, Column.Key);
                Assert.That(ShippedStorageTypes[Column.Key].IsSupersetOf(Column.Value), Is.True,
                    $"{Column.Key}: {string.Join(",", Column.Value)} not in shipped {string.Join(",", ShippedStorageTypes[Column.Key])}");
            }
        }

        [TestCase("en-US")]
        [TestCase("da-DK")]
        [TestCase("tr-TR")]
        [TestCase("")]
        public void Populate_RowidOrderMatchesTheEfReferenceInCulture(string CultureName)
        {
            InCulture(CultureName, () =>
            {
                RidbFixture Fixture = RidbFixture.Create();
                string Optimized = PopulateWith(Fixture, PopulateDbFromRIDBFiles.Populate);
                string Reference = PopulateWith(Fixture, TestOnlyEfReferencePopulation.Populate);

                Assert.That(Differences(Optimized, Reference), Is.Empty);
                foreach (RidbTableInserter.TableMapping Mapping in RidbTableInserter.Mappings)
                {
                    Assert.That(KeysInRowidOrder(Optimized, Mapping), Is.EqualTo(KeysInRowidOrder(Reference, Mapping)), Mapping.Table);
                }
            });
        }

        [Test]
        public void Populate_RowOrderFollowsTheCurrentCultureLikeEfCore()
        {
            // da-DK sorts "aa" after "z"; en-US sorts it first. The order is culture-dependent (TD-36), and
            // must follow the culture the same way SaveChanges did.
            if (CultureInfo.GetCultureInfo("da-DK").CompareInfo.Compare("aa", "zz") <= 0)
            {
                Assert.Ignore("da-DK collation data is not available (invariant globalization)");
            }
            List<string> EnglishOrder = new List<string>();
            List<string> DanishOrder = new List<string>();
            InCulture("en-US", () => EnglishOrder = KeysInRowidOrder(PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate), RidbTableInserter.RecAreas));
            InCulture("da-DK", () => DanishOrder = KeysInRowidOrder(PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate), RidbTableInserter.RecAreas));

            Assert.That(EnglishOrder.IndexOf("aa-1"), Is.LessThan(EnglishOrder.IndexOf("zz")));
            Assert.That(DanishOrder.IndexOf("aa-1"), Is.GreaterThan(DanishOrder.IndexOf("zz")));
            Assert.That(DanishOrder, Is.EquivalentTo(EnglishOrder));
        }

        [Test]
        public void Populate_OrdersCompositeKeysComponentByComponent()
        {
            InCulture("en-US", () =>
            {
                string Optimized = PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate);

                // CampsiteAttributes: AttributeID (integer order), then EntityID.
                List<string> Attributes = KeysInRowidOrder(Optimized, RidbTableInserter.CampsiteAttributes);
                List<int> AttributeIDs = Attributes.Select(k => int.Parse(k.Split('|')[0])).ToList();
                Assert.That(AttributeIDs, Is.Ordered);
                Assert.That(AttributeIDs.First(), Is.EqualTo(2));
                Assert.That(AttributeIDs.Last(), Is.EqualTo(33));
                List<string> EntitiesOfAttribute2 = Attributes.Where(k => k.StartsWith("2|")).Select(k => k.Substring(2)).ToList();
                Assert.That(EntitiesOfAttribute2, Is.EqualTo(EntitiesOfAttribute2.OrderBy(k => k, Comparer<string>.Default).ToList()));

                // PermittedEquipment: CampsiteID first, then EquipmentName (key order of the EF model).
                List<string> Equipment = KeysInRowidOrder(Optimized, RidbTableInserter.PermittedEquipment);
                Assert.That(Equipment, Is.EqualTo(Equipment
                    .OrderBy(k => k.Split('|')[0], Comparer<string>.Default)
                    .ThenBy(k => k.Split('|')[1], Comparer<string>.Default).ToList()));
            });
        }

        [Test]
        public void Populate_OrdersMixedCaseLeadingSpaceHyphenAndNumericKeysLikeTheEfReference()
        {
            InCulture("en-US", () =>
            {
                RidbFixture Fixture = RidbFixture.Create();
                List<string> Optimized = KeysInRowidOrder(PopulateWith(Fixture, PopulateDbFromRIDBFiles.Populate), RidbTableInserter.Campsites);
                List<string> Reference = KeysInRowidOrder(PopulateWith(Fixture, TestOnlyEfReferencePopulation.Populate), RidbTableInserter.Campsites);

                Assert.That(Optimized, Is.EqualTo(Reference));
                Assert.That(Optimized, Is.EquivalentTo(RidbFixture.Keys));
                // not source order, and not ordinal order
                Assert.That(Optimized, Is.Not.EqualTo(RidbFixture.Keys));
                Assert.That(Optimized, Is.Not.EqualTo(RidbFixture.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList()));
                Assert.That(Optimized.First(), Is.EqualTo(" AP22488"));
            });
        }

        [Test]
        public void Populate_NormalizesFacilityAddressesAsBefore()
        {
            string Optimized = PopulateWith(RidbFixture.Create(), PopulateDbFromRIDBFiles.Populate);

            using (var db = new RecreationDotOrgContext(Optimized))
            {
                var Address = db.FacilityAddressesEntries.Single(a => a.FacilityAddressID == "A-10");
                Assert.That(Address.AddressStateCode, Is.EqualTo("UT"));
                Assert.That(Address.City, Is.EqualTo("SPRINGDALE"));
                Assert.That(Address.PostalCode, Is.EqualTo(" 84767-0001 "));
                Assert.That(db.FacilityAddressesEntries.Single(a => a.FacilityAddressID == "A-9").AddressStateCode, Is.EqualTo("AZ"));
                Assert.That(db.FacilityAddressesEntries.Single(a => a.FacilityAddressID == "A-100").AddressStateCode, Is.Null);
                // a null FacilityAddresses row is skipped, as before
                Assert.That(db.FacilityAddressesEntries.Count(), Is.EqualTo(RidbFixture.Keys.Length));
            }
        }

        [Test]
        public void Populate_Success_DeletesTheExtractDirectory()
        {
            string ConfigPath = NewDatabase();
            string RidbDirectory = RidbFixture.Create().Write(NewDirectory("CampertronPopulateRidb-"));

            PopulateDbFromRIDBFiles.Populate(RidbDirectory, ConfigPath);

            Assert.That(Directory.Exists(RidbDirectory), Is.False);
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Not.Empty, "the small fixture has no Watchman, so validation reports it");
        }

        // ---- model drift: the hand-typed mappings must match the EF model and the shipped database ----

        [Test]
        public void Mappings_MatchTheEfModelExactly()
        {
            using (var db = new RecreationDotOrgContext(Path.GetTempPath()))
            {
                List<IEntityType> EntityTypes = db.Model.GetEntityTypes().ToList();
                Assert.That(RidbTableInserter.Mappings.Select(m => m.Table),
                    Is.EquivalentTo(EntityTypes.Select(e => e.GetTableName())), "one mapping per mapped table");
                Assert.That(RidbTableInserter.Mappings.Select(m => m.Table),
                    Is.EqualTo(RidbTableInserter.Mappings.Select(m => m.Table).OrderBy(t => t, StringComparer.Ordinal)),
                    "tables are inserted in ordinal table-name order, as SaveChanges does");

                foreach (IEntityType EntityType in EntityTypes)
                {
                    string Table = EntityType.GetTableName()!;
                    var Mapping = RidbTableInserter.Mappings.Single(m => m.Table == Table);
                    var Identifier = StoreObjectIdentifier.Table(Table, EntityType.GetSchema());
                    var Expected = EntityType.GetProperties()
                        .Select(p => (Name: p.GetColumnName(Identifier)!, Type: p.GetColumnType(Identifier).ToUpperInvariant(), Nullable: p.IsColumnNullable(Identifier)))
                        .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                    var Actual = Mapping.Columns
                        .Select(c => (Name: c.Name, Type: c.Type.ToString().ToUpperInvariant(), Nullable: c.Nullable))
                        .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                    Assert.That(Actual, Is.EqualTo(Expected), Table + " columns, SQLite types and nullability");
                    Assert.That(Mapping.Columns.Select(c => c.Name), Is.Unique, Table);
                    Assert.That(Mapping.KeyColumns, Is.EqualTo(EntityType.FindPrimaryKey()!.Properties.Select(p => p.GetColumnName(Identifier))),
                        Table + " primary key and key order");
                    Assert.That(Mapping.InsertSql, Is.EqualTo(ExpectedInsertSql(Mapping)), Table + " INSERT statement");

                    // every CLR property is mapped, except GEOJSON, which is intentionally unmapped
                    var ClrProperties = EntityType.ClrType.GetProperties().Select(p => p.Name).Where(n => n != "GEOJSON");
                    Assert.That(Mapping.Columns.Select(c => c.Name), Is.EquivalentTo(ClrProperties), Table + " CLR properties");
                }
            }
        }

        [Test]
        public void Mappings_MatchTheShippedDatabaseColumns()
        {
            string DbFile = DbFileIn(NewDatabase());
            foreach (RidbTableInserter.TableMapping Mapping in RidbTableInserter.Mappings)
            {
                var Shipped = new List<(string Name, string Type, bool Nullable)>();
                using (var Connection = Open(DbFile))
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = "SELECT name, type, \"notnull\" FROM pragma_table_info($table) ORDER BY name";
                    Command.Parameters.AddWithValue("$table", Mapping.Table);
                    using (var Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read()) Shipped.Add((Reader.GetString(0), Reader.GetString(1), Reader.GetInt64(2) == 0));
                    }
                }
                var Actual = Mapping.Columns.Select(c => (c.Name, c.Type.ToString().ToUpperInvariant(), c.Nullable))
                    .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                Assert.That(Actual, Is.EqualTo(Shipped.OrderBy(c => c.Name, StringComparer.Ordinal).ToList()), Mapping.Table);
            }
        }

        // ---- failures: same end state as before (committed ClearDatabase, then all six tables empty) ----

        [Test]
        public void Refresh_MalformedJson_FailsAndLeavesAllSixTablesEmpty()
        {
            AssertFailedRefreshLeavesEmptyTables(Directory =>
                File.WriteAllText(Path.Join(Directory, "RecAreas_API_v1.json"), "{\"RECDATA\":[{\"RecAreaID\":\"1\""));
        }

        [Test]
        public void Refresh_DuplicatePrimaryKeyInTheLastTable_RollsBackEveryInsertedTable()
        {
            // RecAreaEntries is inserted last, after thousands of successful commands in the other five tables.
            AssertFailedRefreshLeavesEmptyTables(Directory =>
                RewriteRecData(Directory, "RecAreas", Rows => Rows.Add(Rows[0]!.DeepClone())));
        }

        [Test]
        public void Refresh_DuplicateCompositeKey_FailsAndLeavesAllSixTablesEmpty()
        {
            AssertFailedRefreshLeavesEmptyTables(Directory =>
                RewriteRecData(Directory, "CampsiteAttributes", Rows => Rows.Add(Rows[3]!.DeepClone())));
        }

        [Test]
        public void Refresh_NullRow_FailsInsteadOfBeingSkipped()
        {
            AssertFailedRefreshLeavesEmptyTables(Directory =>
                RewriteRecData(Directory, "Campsites", Rows => Rows.Insert(2, null)));
        }

        [Test]
        public void Refresh_NullPrimaryKeyAfterSuccessfulInserts_RollsBackEveryInsertedTable()
        {
            AssertFailedRefreshLeavesEmptyTables(Directory =>
                RewriteRecData(Directory, "RecAreas", Rows => Rows[Rows.Count - 1]!["RecAreaID"] = null));
        }

        // Seeds a populated database, then runs the refresh sequence of RefreshRidbRecreationData.RefreshData
        // (Clear.ClearDatabase, then Populate) with broken input. The exception must propagate, every data
        // table must be empty, validation must fail and the extract directory must remain, for the optimized
        // import and, as the established behavior, for the EF reference.
        private void AssertFailedRefreshLeavesEmptyTables(Action<string> Break)
        {
            foreach (var (Name, Import) in new (string, Action<string, string>)[]
            {
                ("optimized", PopulateDbFromRIDBFiles.Populate),
                ("EF reference", TestOnlyEfReferencePopulation.Populate)
            })
            {
                string ConfigPath = NewDatabase();
                PopulateDbFromRIDBFiles.Populate(RidbFixture.Create().Write(NewDirectory("CampertronPopulateRidb-")), ConfigPath);
                Assert.That(RowCounts(ConfigPath).Values, Has.All.GreaterThan(0), Name + ": seeded");

                string RidbDirectory = RidbFixture.Create().Write(NewDirectory("CampertronPopulateRidb-"));
                Break(RidbDirectory);
                Clear.ClearDatabase(ConfigPath);
                Assert.Catch<Exception>(() => Import(RidbDirectory, ConfigPath), Name + ": the failure must propagate");
                SqliteConnection.ClearAllPools();

                Assert.That(RowCounts(ConfigPath).Values, Has.All.EqualTo(0), Name + ": no partially populated tables");
                Assert.That(RidbDatabase.Validate(ConfigPath), Does.Contain("the state list is empty"), Name);
                Assert.That(Directory.Exists(RidbDirectory), Is.True, Name + ": the extract directory is kept after a failure");
            }
        }

        private static void RewriteRecData(string RidbDirectory, string File, Action<System.Text.Json.Nodes.JsonArray> Change)
        {
            string Path = System.IO.Path.Join(RidbDirectory, File + "_API_v1.json");
            var Root = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(Path))!;
            Change(Root["RECDATA"]!.AsArray());
            System.IO.File.WriteAllText(Path, Root.ToJsonString());
        }

        // ---- helpers ----

        private string PopulateWith(RidbFixture Fixture, Action<string, string> Import)
        {
            string ConfigPath = NewDatabase();
            Import(Fixture.Write(NewDirectory("CampertronPopulateRidb-")), ConfigPath);
            SqliteConnection.ClearAllPools();
            return ConfigPath;
        }

        private string NewDatabase()
        {
            string ConfigPath = NewDirectory("CampertronPopulateTests-");
            File.Copy(Path.Join(TemplateDirectory, "RecreationDotOrg.db"), DbFileIn(ConfigPath));
            return ConfigPath;
        }

        private string NewDirectory(string Prefix)
        {
            string NewPath = Path.Join(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(NewPath);
            Directories.Add(NewPath);
            return NewPath;
        }

        private static string DbFileIn(string ConfigPath) => Path.Join(ConfigPath, "RecreationDotOrg.db");

        private static SqliteConnection Open(string DbFile)
        {
            var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Pooling = false }.ToString());
            Connection.Open();
            return Connection;
        }

        private static List<string> Column(SqliteConnection Connection, string Sql)
        {
            var Values = new List<string>();
            using (var Command = Connection.CreateCommand())
            {
                Command.CommandText = Sql;
                using (var Reader = Command.ExecuteReader())
                {
                    while (Reader.Read()) Values.Add(Reader.IsDBNull(0) ? "<null>" : Reader.GetValue(0).ToString()!);
                }
            }
            return Values;
        }

        private static void Checkpoint(string DbFile)
        {
            using (var Connection = Open(DbFile))
            {
                Column(Connection, "PRAGMA wal_checkpoint(TRUNCATE)");
            }
        }

        private static List<string> Schema(string DbFile)
        {
            using (var Connection = Open(DbFile))
            {
                return Column(Connection, "SELECT type || '|' || name || '|' || tbl_name || '|' || ifnull(sql, '') FROM sqlite_master ORDER BY type, name");
            }
        }

        private static List<string> PrimaryKeyColumns(string DbFile, string Table)
        {
            using (var Connection = Open(DbFile))
            {
                return Column(Connection, $"SELECT name FROM pragma_table_info('{Table}') WHERE pk > 0 ORDER BY pk");
            }
        }

        // "table.column" -> the set of SQLite storage types (typeof) of its non-null values
        private static Dictionary<string, HashSet<string>> StorageTypes(string DbFile)
        {
            var Result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            using (var Connection = Open(DbFile))
            {
                foreach (string Table in DataTables)
                {
                    foreach (string Name in Column(Connection, $"SELECT name FROM pragma_table_info('{Table}')"))
                    {
                        Result[Table + "." + Name] = Column(Connection, $"SELECT DISTINCT typeof(\"{Name}\") FROM \"{Table}\" WHERE \"{Name}\" IS NOT NULL").ToHashSet();
                    }
                }
            }
            return Result;
        }

        private static Dictionary<string, long> RowCounts(string ConfigPath)
        {
            using (var Connection = Open(DbFileIn(ConfigPath)))
            {
                return DataTables.ToDictionary(t => t, t => long.Parse(Column(Connection, $"SELECT count(*) FROM \"{t}\"")[0]));
            }
        }

        private static List<string> KeysInRowidOrder(string ConfigPath, RidbTableInserter.TableMapping Mapping)
        {
            using (var Connection = Open(DbFileIn(ConfigPath)))
            {
                return Column(Connection, $"SELECT {string.Join(" || '|' || ", Mapping.KeyColumns.Select(k => "\"" + k + "\""))} FROM \"{Mapping.Table}\" ORDER BY rowid");
            }
        }

        // Row-for-row comparison of two databases: counts, EXCEPT in both directions, rowid-aware EXCEPT and the
        // storage type of every value. Returns a description of every difference.
        private static List<string> Differences(string ConfigPathA, string ConfigPathB)
        {
            var Problems = new List<string>();
            using (var Connection = Open(DbFileIn(ConfigPathA)))
            {
                using (var Attach = Connection.CreateCommand())
                {
                    Attach.CommandText = "ATTACH DATABASE $file AS other";
                    Attach.Parameters.AddWithValue("$file", DbFileIn(ConfigPathB));
                    Attach.ExecuteNonQuery();
                }
                foreach (string Table in DataTables)
                {
                    List<string> Names = Column(Connection, $"SELECT name FROM pragma_table_info('{Table}')");
                    string Typed = string.Join(", ", Names.Select(n => $"\"{n}\", typeof(\"{n}\")"));
                    var Checks = new Dictionary<string, string>
                    {
                        ["count"] = $"SELECT (SELECT count(*) FROM main.\"{Table}\") - (SELECT count(*) FROM other.\"{Table}\")",
                        ["only in first"] = $"SELECT count(*) FROM (SELECT * FROM main.\"{Table}\" EXCEPT SELECT * FROM other.\"{Table}\")",
                        ["only in second"] = $"SELECT count(*) FROM (SELECT * FROM other.\"{Table}\" EXCEPT SELECT * FROM main.\"{Table}\")",
                        ["rowid"] = $"SELECT count(*) FROM (SELECT rowid, * FROM main.\"{Table}\" EXCEPT SELECT rowid, * FROM other.\"{Table}\")",
                        ["rowid (reverse)"] = $"SELECT count(*) FROM (SELECT rowid, * FROM other.\"{Table}\" EXCEPT SELECT rowid, * FROM main.\"{Table}\")",
                        ["storage types"] = $"SELECT count(*) FROM (SELECT rowid, {Typed} FROM main.\"{Table}\" EXCEPT SELECT rowid, {Typed} FROM other.\"{Table}\")"
                    };
                    foreach (var Check in Checks)
                    {
                        string Value = Column(Connection, Check.Value)[0];
                        if (Value != "0") Problems.Add($"{Table}: {Check.Key} = {Value}");
                    }
                }
            }
            return Problems;
        }

        private static string ExpectedInsertSql(RidbTableInserter.TableMapping Mapping) =>
            "INSERT INTO \"" + Mapping.Table + "\" (" + string.Join(", ", Mapping.Columns.Select(c => "\"" + c.Name + "\"")) + ") " +
            "VALUES (" + string.Join(", ", Mapping.Columns.Select(c => "$" + c.Name)) + ")";

        private static void InCulture(string Name, Action Body)
        {
            CultureInfo Culture = CultureInfo.CurrentCulture, UiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(Name);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(Name);
                Body();
            }
            finally
            {
                CultureInfo.CurrentCulture = Culture;
                CultureInfo.CurrentUICulture = UiCulture;
            }
        }
    }

    // Deterministic RIDB export fixture for the six imported files. The keys mix case, leading spaces,
    // hyphens, numeric strings and culture-sensitive letters, so that culture-sensitive, ordinal and source
    // order all differ.
    internal sealed class RidbFixture
    {
        public static readonly string[] Keys =
        {
            "AP21", "a08a65e9-76e7", " AP22488", "10", "9", "100", "ap21000", "aa-1", "ab-1", "Aa2", "zz",
            "BAH0409", "eb34b293-bd2b", "RA1014", "-x", "x-", "Ångström", "éclair", "Zebra", "AP1401871"
        };

        public List<CampsiteAttributesRecdata> CampsiteAttributes = new List<CampsiteAttributesRecdata>();
        public List<CampsitesRecdata> Campsites = new List<CampsitesRecdata>();
        public List<FacilitiesData> Facilities = new List<FacilitiesData>();
        public List<FacilityAddressesRecdata?> FacilityAddresses = new List<FacilityAddressesRecdata?>();
        public List<PermittedEquipmentRecdata> PermittedEquipment = new List<PermittedEquipmentRecdata>();
        public List<RecAreaFacilityRecData> RecAreas = new List<RecAreaFacilityRecData>();

        public static RidbFixture Create()
        {
            var Fixture = new RidbFixture();
            string[] StateValues = { "utah", " ut ", "Arizona", "AZ", "California", "UT" };
            for (int i = 0; i < Keys.Length; i++)
            {
                string Key = Keys[i];
                string? Maybe(string Value) => i % 3 == 0 ? null : Value;
                foreach (int AttributeID in new[] { 33, 2, 10 })
                {
                    Fixture.CampsiteAttributes.Add(new CampsiteAttributesRecdata { AttributeID = AttributeID, EntityID = Key, AttributeName = "Attribute " + AttributeID, AttributeValue = Maybe("Value " + i), EntityType = "Campsite" });
                }
                Fixture.Campsites.Add(new CampsitesRecdata
                {
                    CampsiteID = Key, FacilityID = "F-" + i, CampsiteName = Maybe("Site " + i), CampsiteType = "STANDARD NONELECTRIC", TypeOfUse = Maybe("Overnight"),
                    Loop = Maybe("Loop " + (i % 4)), CampsiteAccessible = i % 2 == 0, CampsiteLongitude = -112.98 + i * 0.25, CampsiteLatitude = i == 4 ? 0 : 37.2 - i * 0.125,
                    CreatedDate = "2020-01-0" + (i % 9 + 1), LastUpdatedDate = Maybe("2026-11-10")
                });
                Fixture.Facilities.Add(new FacilitiesData
                {
                    FacilityID = Key, LegacyFacilityID = Maybe("L" + i), OrgFacilityID = "AN" + i, ParentOrgID = "128", ParentRecAreaID = Keys[(i + 1) % Keys.Length],
                    FacilityName = "FACILITY " + i, FacilityDescription = Maybe("<p>Overview</p>"), FacilityTypeDescription = "Campground", FacilityUseFeeDescription = Maybe("$20"),
                    FacilityDirections = Maybe("North"), FacilityPhone = Maybe("555-0100"), FacilityEmail = Maybe("a@example.invalid"), FacilityReservationURL = Maybe("https://example.invalid"),
                    FacilityMapURL = Maybe("https://example.invalid/map"), FacilityAdaAccess = Maybe("Y"), GEOJSON = i % 2 == 0 ? new GEOJSON { TYPE = "Point", COORDINATES = new List<double> { -112.9, 37.2 } } : null,
                    FacilityLongitude = -112.5 - i, FacilityLatitude = 37.25 + i, Keywords = Maybe("tent,rv"), StayLimit = Maybe("14"), Reservable = i % 2 == 1, Enabled = i % 5 != 0,
                    LastUpdatedDate = "2026-11-10"
                });
                Fixture.FacilityAddresses.Add(new FacilityAddressesRecdata
                {
                    FacilityAddressID = "A-" + Key, FacilityID = Key, FacilityAddressType = "Default", FacilityStreetAddress1 = Maybe("1 Main St"), FacilityStreetAddress2 = null,
                    FacilityStreetAddress3 = Maybe(""), City = Key == "10" ? " springdale, " : Maybe(" Town " + i + ","),
                    PostalCode = Key == "10" ? " 84767-0001 " : Maybe("8" + i.ToString("0000")),
                    AddressStateCode = Key == "10" ? "  utah " : Key == "9" ? "Arizona" : Key == "100" ? null : StateValues[i % StateValues.Length],
                    AddressCountryCode = "USA", LastUpdatedDate = Maybe("2026-11-10")
                });
                foreach (string Equipment in new[] { "Tent", " RV", "tent", "Trailer" })
                {
                    Fixture.PermittedEquipment.Add(new PermittedEquipmentRecdata { CampsiteID = Key, EquipmentName = Equipment, MaxLength = i % 2 == 0 ? null : 20 + i * 0.5 });
                }
                Fixture.RecAreas.Add(new RecAreaFacilityRecData
                {
                    RecAreaID = Key, OrgRecAreaID = Maybe("O" + i), ParentOrgID = "128", RecAreaName = "REC AREA " + i, RecAreaDescription = Maybe("Description"),
                    RecAreaFeeDescription = Maybe(""), RecAreaDirections = Maybe("South"), RecAreaPhone = Maybe("555-0101"), RecAreaEmail = Maybe("r@example.invalid"),
                    RecAreaReservationURL = Maybe("https://example.invalid/r"), RecAreaMapURL = Maybe("https://example.invalid/rm"), RecAreaLongitude = -110 - i * 0.5, RecAreaLatitude = 36 + i * 0.5,
                    StayLimit = Maybe("7"), Keywords = Maybe("lake"), Reservable = i % 3 == 1, Enabled = true, LastUpdatedDate = "2026-11-10"
                });
            }
            Fixture.FacilityAddresses.Insert(5, null);
            return Fixture;
        }

        public Dictionary<string, long> ExpectedRowCounts() => new Dictionary<string, long>
        {
            ["CampsiteAttributesEntries"] = CampsiteAttributes.Count,
            ["CampsitesEntries"] = Campsites.Count,
            ["FacilitiesEntries"] = Facilities.Count,
            ["FacilityAddressesEntries"] = FacilityAddresses.Count(a => a != null),
            ["PermittedEquipmentEntries"] = PermittedEquipment.Count,
            ["RecAreaEntries"] = RecAreas.Count
        };

        // Writes the six *_API_v1.json files in the RIDB export format ({"RECDATA":[...]}) and returns the directory.
        public string Write(string RidbDirectory)
        {
            void Save(string Name, object Rows) =>
                File.WriteAllText(Path.Join(RidbDirectory, Name + "_API_v1.json"), JsonSerializer.Serialize(new Dictionary<string, object> { ["RECDATA"] = Rows }));
            Save("CampsiteAttributes", CampsiteAttributes);
            Save("Campsites", Campsites);
            Save("Facilities", Facilities);
            Save("FacilityAddresses", FacilityAddresses);
            Save("PermittedEquipment", PermittedEquipment);
            Save("RecAreas", RecAreas);
            return RidbDirectory;
        }
    }

    // TEST-ONLY reference: the RIDB import as it was before decision D25 (EF Core Add for every row, then one
    // SaveChanges). It exists only in this test project, so production code cannot call it, and is used only to
    // prove that PopulateDbFromRIDBFiles.Populate writes the same database.
    internal static class TestOnlyEfReferencePopulation
    {
        public static void Populate(string BaseDirectory, string ConfigPath)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                CampsiteAttributesEntries? CampsiteAttributesData = LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "/CampsiteAttributes_API_v1.json");
                if (CampsiteAttributesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsiteAttributesData.RecEntries) db.CampsiteAttributesEntries.Add(ThisData);
                }
                CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "/Campsites_API_v1.json");
                if (CampsitesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsitesData.RecEntries) db.CampsitesEntries.Add(ThisData);
                }
                FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "/Facilities_API_v1.json");
                if (FacilityData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityData.RecEntries) db.FacilitiesEntries.Add(ThisData);
                }
                FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "/FacilityAddresses_API_v1.json");
                if (FacilityAddressesData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityAddressesData.RecEntries)
                    {
                        if (ThisData != null)
                        {
                            string? FixedStateCode = ThisData.AddressStateCode?.Trim()?.ToUpper();
                            if (FixedStateCode != null)
                            {
                                ThisData.AddressStateCode = CampertronLibrary.function.Base.Convert.StateNameToAbbreviation(FixedStateCode);
                            }
                            ThisData.City = ThisData.City?.Trim().Trim(',').ToUpper();
                            db.FacilityAddressesEntries.Add(ThisData);
                        }
                    }
                }
                PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "/PermittedEquipment_API_v1.json");
                if (PermittedEquipmentData?.RecEntries != null)
                {
                    foreach (var ThisData in PermittedEquipmentData.RecEntries) db.PermittedEquipmentEntries.Add(ThisData);
                }
                RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "/RecAreas_API_v1.json");
                if (RecAreaData?.RecEntries != null)
                {
                    foreach (var ThisData in RecAreaData.RecEntries) db.RecAreaEntries.Add(ThisData);
                }
                db.SaveChanges();
                if (Directory.Exists(BaseDirectory))
                {
                    Directory.Delete(BaseDirectory, true);
                }
            }
        }
    }
}
