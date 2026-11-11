using System.Security.Cryptography;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;
using static CampertronLibrary.function.RecDotOrg.sqlite.LegacyAddressLayout;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Tests for the one-time startup repair (LegacyAddressLayout.RepairIfNeeded) of databases that no current
    // Campertron would produce: the swapped 2024 layout (FacilityAddresses.PostalCode and AddressStateCode
    // swapped), and pre-0.1.0-beta.4 databases with the corrected layout that still contain the omitted RIDB
    // tables and un-normalized state values. Every test uses its own temporary directory and SQLite databases
    // created from the EF model; the bundled database is never used here (see StartupRepairTests).
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

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => CreateBundledDb(Target));

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

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => CreateBundledDb(Target));

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

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => { ExtractorCalled = true; CreateBundledDb(Target); });

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

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => CreateDb(Target, Legacy(200)));

            Assert.That(Repaired, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_MissingDatabase_DoesNothing()
        {
            bool ExtractorCalled = false;
            Assert.That(RepairIfNeeded(ConfigPath, null, Target => ExtractorCalled = true), Is.False);
            Assert.That(ExtractorCalled, Is.False);
            Assert.That(File.Exists(DbFile), Is.False);
        }

        // ---- pre-0.1.0-beta.4 databases (corrected layout, omitted tables, un-normalized states) ----

        [Test]
        public void Detect_PreBeta4Database_IsObsoleteTablesAndReportsNonNormalizedStates()
        {
            CreateStaleDb(DbFile);

            Assert.That(DetectLayout(DbFile), Is.EqualTo(Layout.Corrected), "the old Legacy classification alone does not catch it");
            Assert.That(PresentOmittedTables(DbFile), Is.EquivalentTo(RidbDatabase.OmittedTables));
            Assert.That(NonNormalizedGuiStates(DbFile), Is.EqualTo(new[] { "ARIZONA", "IN ", "UTAH" }));
            Assert.That(DetectRepairReason(DbFile), Is.EqualTo(RepairReason.ObsoleteTables));
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Not.Empty);
        }

        [Test]
        public void GuiStateValues_MatchesReadUniqueStates()
        {
            CreateStaleDb(DbFile);
            Assert.That(GuiStateValues(DbFile), Is.EqualTo(Read.UniqueStates(ConfigPath)));
        }

        [Test]
        public void Detect_CurrentDatabase_NeedsNoRepair()
        {
            CreateBundledDb(DbFile);
            Assert.That(DetectRepairReason(DbFile), Is.EqualTo(RepairReason.None));
            Assert.That(PresentOmittedTables(DbFile), Is.Empty);
            Assert.That(NonNormalizedGuiStates(DbFile), Is.Empty);
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
        }

        [Test]
        public void Repair_PreBeta4Database_IsReplacedAndSatisfiesCurrentInvariants()
        {
            CreateStaleDb(DbFile);
            string OriginalHash = Hash(DbFile);

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => CreateBundledDb(Target));

            Assert.That(Repaired, Is.True);
            string Backup = Directory.GetFiles(ConfigPath, "RecreationDotOrg.db.pre-0.1.0-beta.4-*.bak").Single();
            Assert.That(Hash(Backup), Is.EqualTo(OriginalHash));
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
            List<string> States = Read.UniqueStates(ConfigPath);
            Assert.That(States, Is.Not.Empty);
            Assert.That(States, Has.All.Match("^[A-Z]{2}$"));
            Assert.That(States, Has.None.Match("[0-9]"));
            Assert.That(States, Does.Contain("AZ"));
            Assert.That(States, Has.None.EqualTo("ARIZONA").IgnoreCase);
            Assert.That(PresentOmittedTables(DbFile), Is.Empty);
            Assert.That(Read.UniqueCities("MA", ConfigPath), Does.Contain("BARRE"));
            Assert.That(Read.UniqueParks("UT", "SPRINGDALE", ConfigPath), Does.Contain(RidbDatabase.WatchmanName));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_SecondStartAfterRepair_DoesNothing()
        {
            CreateStaleDb(DbFile);
            Assert.That(RepairIfNeeded(ConfigPath, null, Target => CreateBundledDb(Target)), Is.True);
            string RepairedHash = Hash(DbFile);
            bool ExtractorCalled = false;

            bool Again = RepairIfNeeded(ConfigPath, null, Target => { ExtractorCalled = true; CreateBundledDb(Target); });

            Assert.That(Again, Is.False);
            Assert.That(ExtractorCalled, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(RepairedHash));
            Assert.That(Directory.GetFiles(ConfigPath, "*.bak"), Has.Length.EqualTo(1));
        }

        [Test]
        public void Repair_CurrentEmbeddedStyleDatabase_IsNotTouched()
        {
            AssertDbNotRepaired(() => CreateBundledDb(DbFile));
        }

        [Test]
        public void Repair_NewerRefreshedDatabase_IsNotTouchedAlthoughItDiffersFromTheBundledOne()
        {
            // a later "Refresh Ridb Data": current importer, different data, no omitted tables
            AssertDbNotRepaired(() =>
            {
                CreateBundledDb(DbFile);
                AddGuiFacilities(DbFile, ("900001", "NEW CAMPGROUND", "ESTES PARK", "CO", "80517"));
            });
        }

        [Test]
        public void Repair_NonNormalizedStatesWithoutOmittedTables_IsReportedButNotReplaced()
        {
            // e.g. a current refresh whose RIDB data contains an unmappable state for a reservable campground:
            // replacing it would downgrade newer data to the bundled copy on every start
            AssertDbNotRepaired(() =>
            {
                CreateBundledDb(DbFile);
                AddGuiFacilities(DbFile, ("900002", "ODD CAMPGROUND", "SOMEWHERE", "STATE MAPPING BROKEN", "00000"));
            });
            Assert.That(NonNormalizedGuiStates(DbFile), Is.EqualTo(new[] { "STATE MAPPING BROKEN" }));
        }

        [Test]
        public void Repair_PreBeta4DatabaseWithUserObjects_IsNotTouched()
        {
            AssertDbNotRepaired(() =>
            {
                CreateStaleDb(DbFile);
                ExecuteSql(DbFile, "CREATE VIEW MyStates AS SELECT AddressStateCode FROM FacilityAddressesEntries;");
            });
        }

        [Test]
        public void Repair_AmbiguousLayoutWithOmittedTables_IsNotTouched()
        {
            AssertDbNotRepaired(() =>
            {
                CreateDb(DbFile, Corrected(100).Concat(Legacy(100)));
                AddOmittedTables(DbFile);
            });
            Assert.That(DetectRepairReason(DbFile), Is.EqualTo(RepairReason.None));
        }

        [Test]
        public void Repair_WhenBundledDatabaseFailsValidation_KeepsExistingDatabase()
        {
            CreateStaleDb(DbFile);
            string OriginalHash = Hash(DbFile);

            // corrected layout but no Watchman and an empty state list: RidbDatabase.Validate rejects it
            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => CreateDb(Target, Corrected(200)));

            Assert.That(Repaired, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            Assert.That(Directory.GetFiles(ConfigPath, "*.bak"), Is.Empty);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_WhenBundledDatabaseNeedsRepairItself_KeepsExistingDatabase()
        {
            CreateStaleDb(DbFile);
            string OriginalHash = Hash(DbFile);

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => { CreateBundledDb(Target); AddOmittedTables(Target); });

            Assert.That(Repaired, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_WhenExtractionFails_KeepsExistingDatabase()
        {
            CreateStaleDb(DbFile);
            string OriginalHash = Hash(DbFile);

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => throw new IOException("disk full"));

            Assert.That(Repaired, Is.False);
            Assert.That(Hash(DbFile), Is.EqualTo(OriginalHash));
            Assert.That(Directory.GetFiles(ConfigPath, "*.bak"), Is.Empty);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void Repair_LeavesUserFilesInConfigPathUntouched()
        {
            CreateStaleDb(DbFile);
            var UserFiles = new Dictionary<string, string>
            {
                ["General.yaml"] = "outputTo: Console\n",
                ["Email.yaml"] = "smtpServer: example\n",
                ["ZionConfig.yaml"] = "displayName: Zion\n",
                [Path.Join("Camp.History", "2026", "11", "11", "12345", "marker.txt")] = "x",
                ["notes.txt"] = "mine"
            };
            foreach (var File_ in UserFiles)
            {
                string Full = Path.Join(ConfigPath, File_.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(Full)!);
                File.WriteAllText(Full, File_.Value);
            }

            Assert.That(RepairIfNeeded(ConfigPath, null, Target => CreateBundledDb(Target)), Is.True);

            foreach (var File_ in UserFiles)
            {
                Assert.That(File.ReadAllText(Path.Join(ConfigPath, File_.Key)), Is.EqualTo(File_.Value), File_.Key);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Repair_RemovesOnlyDerivedCacheFiles(bool Legacy_)
        {
            if (Legacy_) CreateDb(DbFile, Legacy(200)); else CreateStaleDb(DbFile);
            string CachePath = Path.Join(ConfigPath, "cache");
            Directory.CreateDirectory(CachePath);
            string[] Derived = { "232445-CampInfo.json", "232445-Campsites.json", "232445-CampsitesByPark.json", "232445-Cached.json", "10146649-Equipment.json", "10146649-Attributes.json" };
            string[] Kept = { "3f2a9c.html", "notes.txt", "232445-Other.json" };
            foreach (string Name in Derived.Concat(Kept)) File.WriteAllText(Path.Join(CachePath, Name), "{}");

            Assert.That(RepairIfNeeded(ConfigPath, CachePath, Target => CreateBundledDb(Target)), Is.True);

            Assert.That(Directory.GetFiles(CachePath).Select(Path.GetFileName), Is.EquivalentTo(Kept));
        }

        [Test]
        public void Repair_NotNeeded_LeavesCacheUntouched()
        {
            CreateBundledDb(DbFile);
            string CachePath = Path.Join(ConfigPath, "cache");
            Directory.CreateDirectory(CachePath);
            File.WriteAllText(Path.Join(CachePath, "232445-Campsites.json"), "[]");

            Assert.That(RepairIfNeeded(ConfigPath, CachePath, Target => CreateBundledDb(Target)), Is.False);

            Assert.That(File.Exists(Path.Join(CachePath, "232445-Campsites.json")), Is.True);
        }

        // ---- helpers ----

        private void AssertNotRepaired(IEnumerable<(string?, string?)> Rows)
        {
            AssertDbNotRepaired(() => CreateDb(DbFile, Rows));
        }

        private void AssertDbNotRepaired(Action CreateInstalledDb)
        {
            CreateInstalledDb();
            string OriginalHash = Hash(DbFile);
            bool ExtractorCalled = false;

            bool Repaired = RepairIfNeeded(ConfigPath, null, Target => { ExtractorCalled = true; CreateBundledDb(Target); });

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

        // A database like the bundled one: corrected layout, current schema only, and campgrounds that the
        // GUI lists (Watchman under UT/SPRINGDALE, AZ, MA/BARRE). Passes RidbDatabase.Validate.
        private static void CreateBundledDb(string DbPath)
        {
            CreateDb(DbPath, Corrected(200));
            AddGuiFacilities(DbPath,
                ("232445", RidbDatabase.WatchmanName, "SPRINGDALE", "UT", "84767"),
                ("232446", "LOST DUTCHMAN CAMPGROUND", "APACHE JUNCTION", "AZ", "85119"),
                ("233800", "BARRE FALLS CAMPGROUND", "BARRE", "MA", "01005"));
        }

        // The class of database found on an upgraded installation (2026-11-11): written by the importer from
        // before PR #16 (AddressStateCode not normalized: full names, padding) with all 21 RIDB tables, but
        // with the corrected column layout of the current RIDB export.
        private static void CreateStaleDb(string DbPath)
        {
            CreateDb(DbPath, Corrected(200));
            AddGuiFacilities(DbPath,
                ("232445", RidbDatabase.WatchmanName, "SPRINGDALE", "UT", "84767"),
                ("232446", "LOST DUTCHMAN CAMPGROUND", "APACHE JUNCTION", "AZ", "85119"),
                ("232447", "CATALINA CAMPGROUND", "TUCSON", "ARIZONA", "85737"),
                ("232448", "SOUTH CAMPGROUND", "SPRINGDALE", "UTAH", "84767"),
                ("232449", "INDIANA CAMPGROUND", "BLOOMINGTON", "IN ", "47401"),
                ("233800", "BARRE FALLS CAMPGROUND", "BARRE", "MA", "01005"));
            AddOmittedTables(DbPath);
        }

        private static void AddGuiFacilities(string DbPath, params (string Id, string Name, string City, string State, string Postal)[] Facilities)
        {
            using (var db = new RecreationDotOrgContext(Path.GetDirectoryName(DbPath)!))
            {
                foreach (var F in Facilities)
                {
                    db.FacilitiesEntries.Add(new FacilitiesData { FacilityID = F.Id, FacilityName = F.Name, FacilityTypeDescription = "Campground", Reservable = true, Enabled = true });
                    db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata { FacilityAddressID = "F" + F.Id, FacilityID = F.Id, FacilityAddressType = "Physical", City = F.City, AddressStateCode = F.State, PostalCode = F.Postal });
                }
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
        }

        // The 15 tables that only databases written before PR #17 contain.
        private static void AddOmittedTables(string DbPath)
        {
            ExecuteSql(DbPath, string.Join(" ", RidbDatabase.OmittedTables.Select(t => $"CREATE TABLE \"{t}\" (\"Id\" TEXT NOT NULL CONSTRAINT \"PK_{t}\" PRIMARY KEY, \"Value\" TEXT NULL);")));
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
