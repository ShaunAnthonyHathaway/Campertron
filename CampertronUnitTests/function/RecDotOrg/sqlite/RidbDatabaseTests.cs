using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Tests for the refresh-time database validation (RidbDatabase.Validate) and for
    // Clear.ClearDatabase dropping the omitted RIDB tables. Each test uses its own temporary
    // SQLite database created from the EF model; no user directories, network or bundled database.
    public class RidbDatabaseTests
    {
        private string ConfigPath = string.Empty;
        private string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [SetUp]
        public void Setup()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronRidbDatabaseTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(ConfigPath))
            {
                Directory.Delete(ConfigPath, true);
            }
        }

        private void CreateValidDb(string WatchmanState = "UT", bool IncludeWatchman = true)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "2994", RecAreaName = "Zion National Park" });
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = "232445",
                    FacilityName = IncludeWatchman ? RidbDatabase.WatchmanName : "SOUTH CAMPGROUND",
                    FacilityTypeDescription = "Campground",
                    ParentRecAreaID = "2994",
                    Reservable = true,
                    Enabled = true
                });
                db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata
                {
                    FacilityAddressID = "A1",
                    FacilityID = "232445",
                    FacilityAddressType = "Default",
                    City = "SPRINGDALE",
                    PostalCode = "84767",
                    AddressStateCode = WatchmanState
                });
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
        }

        private void ExecuteSql(string Sql)
        {
            using (var Connection = new SqliteConnection($"Data Source={DbFile};Pooling=False"))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = Sql;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private List<string> Tables()
        {
            var Result = new List<string>();
            using (var Connection = new SqliteConnection($"Data Source={DbFile};Mode=ReadOnly;Pooling=False"))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                    using (var Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read()) Result.Add(Reader.GetString(0));
                    }
                }
            }
            return Result;
        }

        [Test]
        public void OmittedTables_AreNotMappedByTheModel()
        {
            Assert.That(RidbDatabase.OmittedTables, Has.Count.EqualTo(15));
            Assert.That(RidbDatabase.RequiredColumns().Keys, Is.EquivalentTo(new[]
            {
                "CampsiteAttributesEntries", "CampsitesEntries", "FacilitiesEntries",
                "FacilityAddressesEntries", "PermittedEquipmentEntries", "RecAreaEntries"
            }));
            Assert.That(RidbDatabase.RequiredColumns().Keys, Has.None.AnyOf(RidbDatabase.OmittedTables.ToArray()));
        }

        [Test]
        public void Validate_ValidDatabase_Passes()
        {
            CreateValidDb();
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
        }

        [Test]
        public void Validate_OmittedTablePresent_Fails()
        {
            CreateValidDb();
            ExecuteSql("CREATE TABLE MediaEntries (EntityMediaID TEXT)");
            Assert.That(RidbDatabase.Validate(ConfigPath), Has.Some.Contains("MediaEntries should have been omitted"));
        }

        [Test]
        public void Validate_MissingRequiredTable_Fails()
        {
            CreateValidDb();
            ExecuteSql("DROP TABLE PermittedEquipmentEntries");
            Assert.That(RidbDatabase.Validate(ConfigPath), Has.Some.Contains("required table PermittedEquipmentEntries is missing"));
        }

        [Test]
        public void Validate_MissingRequiredColumn_Fails()
        {
            CreateValidDb();
            ExecuteSql("ALTER TABLE FacilityAddressesEntries DROP COLUMN FacilityStreetAddress1");
            Assert.That(RidbDatabase.Validate(ConfigPath), Has.Some.Contains("required column FacilityAddressesEntries.FacilityStreetAddress1 is missing"));
        }

        [Test]
        public void Validate_ZipCodeInStateList_Fails()
        {
            CreateValidDb(WatchmanState: "84767");
            List<string> Problems = RidbDatabase.Validate(ConfigPath);
            Assert.That(Problems, Has.Some.Contains("'84767', which is not a two-letter state code"));
            Assert.That(Problems, Has.Some.Contains("not listed under UT / SPRINGDALE"));
        }

        [Test]
        public void Validate_WatchmanMissing_Fails()
        {
            CreateValidDb(IncludeWatchman: false);
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.EqualTo(new List<string> { "WATCHMAN CAMPGROUND is not listed under UT / SPRINGDALE" }));
        }

        [Test]
        public void Validate_EmptyDatabase_Fails()
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
            }
            Assert.That(RidbDatabase.Validate(ConfigPath), Has.Some.Contains("the state list is empty"));
        }

        [Test]
        public void Validate_CorruptFile_Fails()
        {
            CreateValidDb();
            byte[] Bytes = File.ReadAllBytes(DbFile);
            for (int i = 4096; i < Bytes.Length; i++) Bytes[i] = 0x5A;
            File.WriteAllBytes(DbFile, Bytes);
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Not.Empty);
        }

        [Test]
        public void Validate_MissingFile_Fails()
        {
            Assert.That(RidbDatabase.Validate(ConfigPath), Has.Some.Contains("does not exist"));
        }

        [Test]
        public void ClearDatabase_DropsOmittedTablesAndEmptiesImportedTables()
        {
            // like an existing full database: all 15 omitted tables present, with data
            CreateValidDb();
            foreach (string Omitted in RidbDatabase.OmittedTables)
            {
                ExecuteSql($"CREATE TABLE \"{Omitted}\" (Id TEXT); INSERT INTO \"{Omitted}\" VALUES ('x');");
            }

            Clear.ClearDatabase(ConfigPath);

            List<string> Remaining = Tables();
            Assert.That(Remaining, Has.None.AnyOf(RidbDatabase.OmittedTables.ToArray()));
            Assert.That(Remaining, Is.SupersetOf(RidbDatabase.RequiredColumns().Keys));
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                Assert.That(db.FacilitiesEntries.Count(), Is.EqualTo(0));
                Assert.That(db.FacilityAddressesEntries.Count(), Is.EqualTo(0));
                Assert.That(db.RecAreaEntries.Count(), Is.EqualTo(0));
            }
        }

        [Test]
        public void ClearDatabase_SlimDatabase_Succeeds()
        {
            CreateValidDb();
            Assert.DoesNotThrow(() => Clear.ClearDatabase(ConfigPath));
            Assert.That(Tables(), Is.SupersetOf(RidbDatabase.RequiredColumns().Keys));
        }
    }
}
