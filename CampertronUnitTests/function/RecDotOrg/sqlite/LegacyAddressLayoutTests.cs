using System.Security.Cryptography;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;
using static CampertronLibrary.function.RecDotOrg.sqlite.LegacyAddressLayout;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Tests for the one-time repair of databases built from the RIDB export of 2024-11-18, which had
    // FacilityAddresses.PostalCode and AddressStateCode swapped. Every test uses its own temporary
    // directory and SQLite databases created from the EF model; the bundled database is never used.
    public class LegacyAddressLayoutTests
    {
        private string ConfigPath = string.Empty;
        private string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [SetUp]
        public void Setup()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronLegacyLayoutTests-" + Guid.NewGuid().ToString("N"));
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

        private static IEnumerable<(string?, string?)> Legacy(int Count) =>
            Enumerable.Range(0, Count).Select(i => ((string?)"UT", (string?)(84700 + i % 100).ToString()));

        private static IEnumerable<(string?, string?)> Corrected(int Count) =>
            Enumerable.Range(0, Count).Select(i => ((string?)(84700 + i % 100).ToString(), (string?)"UT"));

        // ---- classification ----

        [Test]
        public void Classify_SwappedColumns_IsLegacy()
        {
            Assert.That(Classify(Legacy(200)), Is.EqualTo(Layout.LegacySwapped));
        }

        [Test]
        public void Classify_LegacyWithEmptyAndUnmappedValues_IsLegacy()
        {
            // like the real 2024 DB: many empty AddressStateCode values and a few odd PostalCode values
            var Rows = Legacy(200)
                .Concat(Enumerable.Repeat(((string?)"CA", (string?)null), 300))
                .Concat(new[] { ((string?)"STATE MAPPING BROKEN", (string?)""), ((string?)"", (string?)"59874-9503") });
            Assert.That(Classify(Rows), Is.EqualTo(Layout.LegacySwapped));
        }

        [Test]
        public void Classify_CorrectedColumns_IsCorrected()
        {
            var Rows = Corrected(200).Concat(Enumerable.Repeat(((string?)"", (string?)"CO"), 300));
            Assert.That(Classify(Rows), Is.EqualTo(Layout.Corrected));
        }

        [Test]
        public void Classify_CorrectedWithAFewMalformedRows_IsNotLegacy()
        {
            Assert.That(Classify(Corrected(195).Concat(Legacy(5))), Is.EqualTo(Layout.Corrected));
        }

        [Test]
        public void Classify_MixedLayouts_IsUnknown()
        {
            Assert.That(Classify(Corrected(100).Concat(Legacy(100))), Is.EqualTo(Layout.Unknown));
        }

        [Test]
        public void Classify_TooFewRows_IsUnknown()
        {
            Assert.That(Classify(Legacy(MinimumSample - 1)), Is.EqualTo(Layout.Unknown));
            Assert.That(Classify(Enumerable.Empty<(string?, string?)>()), Is.EqualTo(Layout.Unknown));
        }

        [Test]
        public void Classify_StateCodesInPostalCodeButNoZipCodes_IsUnknown()
        {
            // PostalCode looks like the legacy layout, but AddressStateCode does not contain ZIP codes
            var Rows = Enumerable.Repeat(((string?)"UT", (string?)"Utah"), 200);
            Assert.That(Classify(Rows), Is.EqualTo(Layout.Unknown));
        }

        // ---- repair ----

        [Test]
        public void Repair_LegacyDatabase_IsReplacedAndOriginalKeptAsBackup()
        {
            CreateDb(DbFile, Legacy(200));
            string OriginalHash = Hash(DbFile);

            bool Repaired = RepairIfLegacy(ConfigPath, Target => CreateDb(Target, Corrected(200)));

            Assert.That(Repaired, Is.True);
            Assert.That(DetectLayout(DbFile), Is.EqualTo(Layout.Corrected));
            string[] Backups = Directory.GetFiles(ConfigPath, "RecreationDotOrg.db.legacy-layout-*.bak");
            Assert.That(Backups, Has.Length.EqualTo(1));
            Assert.That(Hash(Backups[0]), Is.EqualTo(OriginalHash));
            Assert.That(DetectLayout(Backups[0]), Is.EqualTo(Layout.LegacySwapped));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_LegacyDatabaseInWalMode_KeepsAllRowsInBackup()
        {
            CreateDb(DbFile, Legacy(200));
            ExecuteSql(DbFile, "PRAGMA journal_mode=WAL; INSERT INTO FacilityAddressesEntries (FacilityAddressID, PostalCode, AddressStateCode) VALUES ('wal-row', 'UT', '84767');");

            bool Repaired = RepairIfLegacy(ConfigPath, Target => CreateDb(Target, Corrected(200)));

            Assert.That(Repaired, Is.True);
            string Backup = Directory.GetFiles(ConfigPath, "RecreationDotOrg.db.legacy-layout-*.bak").Single();
            Assert.That(CountRows(Backup), Is.EqualTo(201));
            Assert.That(DetectLayout(DbFile), Is.EqualTo(Layout.Corrected));
        }

        [Test]
        public void Repair_CorrectedDatabase_IsNotTouched()
        {
            AssertNotRepaired(Corrected(200));
        }

        [Test]
        public void Repair_AmbiguousDatabase_IsNotTouched()
        {
            AssertNotRepaired(Corrected(100).Concat(Legacy(100)));
        }

        [Test]
        public void Repair_CorrectedDatabaseWithAFewMalformedRows_IsNotTouched()
        {
            AssertNotRepaired(Corrected(195).Concat(Legacy(5)));
        }

        [Test]
        public void Repair_LegacyDatabaseWithUserObjects_IsNotTouched()
        {
            CreateDb(DbFile, Legacy(200));
            ExecuteSql(DbFile, "CREATE TABLE UserNotes (Note TEXT); INSERT INTO UserNotes VALUES ('keep me');");
            Assert.That(HasOnlyKnownSchemaObjects(DbFile), Is.False);
            string OriginalHash = Hash(DbFile);
            bool ExtractorCalled = false;

            bool Repaired = RepairIfLegacy(ConfigPath, Target => { ExtractorCalled = true; CreateDb(Target, Corrected(200)); });

            Assert.That(Repaired, Is.False);
            Assert.That(ExtractorCalled, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_WhenBundledDatabaseIsAlsoLegacy_KeepsExistingDatabase()
        {
            CreateDb(DbFile, Legacy(200));
            string OriginalHash = Hash(DbFile);

            bool Repaired = RepairIfLegacy(ConfigPath, Target => CreateDb(Target, Legacy(200)));

            Assert.That(Repaired, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_MissingDatabase_DoesNothing()
        {
            bool ExtractorCalled = false;
            Assert.That(RepairIfLegacy(ConfigPath, Target => ExtractorCalled = true), Is.False);
            Assert.That(ExtractorCalled, Is.False);
            Assert.That(File.Exists(DbFile), Is.False);
        }

        // ---- helpers ----

        private void AssertNotRepaired(IEnumerable<(string?, string?)> Rows)
        {
            CreateDb(DbFile, Rows);
            string OriginalHash = Hash(DbFile);
            bool ExtractorCalled = false;

            bool Repaired = RepairIfLegacy(ConfigPath, Target => { ExtractorCalled = true; CreateDb(Target, Corrected(200)); });

            Assert.That(Repaired, Is.False);
            Assert.That(ExtractorCalled, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            AssertNoTemporaryFiles();
        }

        private void AssertNoTemporaryFiles()
        {
            Assert.That(Directory.GetDirectories(ConfigPath, "RecreationDotOrg.repair-*"), Is.Empty);
            Assert.That(File.Exists(Path.Join(ConfigPath, "RecreationDotOrg.repair.lock")), Is.False);
        }

        private static void CreateDb(string DbPath, IEnumerable<(string? PostalCode, string? AddressStateCode)> Rows)
        {
            Assert.That(Path.GetFileName(DbPath), Is.EqualTo("RecreationDotOrg.db"));
            using (var db = new RecreationDotOrgContext(Path.GetDirectoryName(DbPath)!))
            {
                db.Database.EnsureCreated();
                int i = 0;
                foreach (var Row in Rows)
                {
                    db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata
                    {
                        FacilityAddressID = "A" + i++,
                        FacilityAddressType = "Default",
                        PostalCode = Row.PostalCode,
                        AddressStateCode = Row.AddressStateCode
                    });
                }
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
        }

        private static void ExecuteSql(string DbPath, string Sql)
        {
            using (var Connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = Sql;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static long CountRows(string DbPath)
        {
            using (var Connection = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False"))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = "SELECT count(*) FROM FacilityAddressesEntries";
                    return (long)Command.ExecuteScalar()!;
                }
            }
        }

        private static string Hash(string DbPath)
        {
            SqliteConnection.ClearAllPools();
            using (var Stream = File.OpenRead(DbPath))
            {
                return System.Convert.ToHexString(SHA256.HashData(Stream));
            }
        }
    }
}
