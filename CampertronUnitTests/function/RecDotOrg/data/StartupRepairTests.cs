using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.data
{
    // The startup path shared by CampertronConsole and CampertronGUI (Load.GetConfig -> Load.DbExistsCheck ->
    // LegacyAddressLayout.RepairIfNeeded), with the real bundled database as the replacement. Each test uses
    // its own temporary config and cache directories; no user directories or network.
    public class StartupRepairTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "CampertronConfig");
        private string CachePath => Path.Join(Root, "CampertronCache");
        private string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronStartupRepairTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private CampertronInternalConfig Config() => new CampertronInternalConfig
        {
            ConfigPath = ConfigPath,
            CachePath = CachePath,
            GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest, AutoRefresh = false, RefreshRidbDataDayInterval = 30, LastRidbDataRefresh = DateTime.UtcNow }
        };

        [Test]
        public void DbExistsCheck_PreBeta4Install_IsRepairedOnceWithTheBundledDatabase()
        {
            // a pre-0.1.0-beta.4 database: corrected layout, the 15 omitted tables, un-normalized states
            Load.ExtractEmbeddedDbTo(DbFile);
            using (var Connection = new SqliteConnection($"Data Source={DbFile};Pooling=False"))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText =
                        "UPDATE FacilityAddressesEntries SET AddressStateCode = 'ARIZONA' WHERE FacilityAddressID IN " +
                        "(SELECT a.FacilityAddressID FROM FacilityAddressesEntries a JOIN FacilitiesEntries f ON f.FacilityID = a.FacilityID " +
                        "WHERE a.AddressStateCode = 'AZ' AND f.FacilityTypeDescription = 'Campground' AND f.Reservable = 1 AND f.Enabled = 1 " +
                        "AND a.FacilityAddressType IN ('Physical', 'Default') LIMIT 3);" +
                        string.Join(" ", RidbDatabase.OmittedTables.Select(t => $"CREATE TABLE \"{t}\" (\"Id\" TEXT NOT NULL PRIMARY KEY);"));
                    Command.ExecuteNonQuery();
                }
            }
            Assert.That(LegacyAddressLayout.DetectRepairReason(DbFile), Is.EqualTo(LegacyAddressLayout.RepairReason.ObsoleteTables));
            Assert.That(Read.UniqueStates(ConfigPath), Does.Contain("ARIZONA"));
            SqliteConnection.ClearAllPools();
            File.WriteAllText(Path.Join(CachePath, "232445-Campsites.json"), "[]");

            Assert.That(Load.DbExistsCheck(Config()), Is.True);

            Assert.That(Directory.GetFiles(ConfigPath, "RecreationDotOrg.db.pre-0.1.0-beta.4-*.bak"), Has.Length.EqualTo(1));
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
            List<string> States = Read.UniqueStates(ConfigPath);
            Assert.That(States, Has.All.Match("^[A-Z]{2}$"));
            Assert.That(States, Does.Contain("AZ"));
            Assert.That(States, Does.Not.Contain("ARIZONA"));
            Assert.That(Read.UniqueCities("MA", ConfigPath), Does.Contain("BARRE"));
            Assert.That(File.Exists(Path.Join(CachePath, "232445-Campsites.json")), Is.False);

            // second start: the installed database satisfies the current invariants, nothing is replaced
            SqliteConnection.ClearAllPools();
            DateTime Modified = File.GetLastWriteTimeUtc(DbFile);
            Assert.That(Load.DbExistsCheck(Config()), Is.True);
            Assert.That(Directory.GetFiles(ConfigPath, "*.bak"), Has.Length.EqualTo(1));
            Assert.That(File.GetLastWriteTimeUtc(DbFile), Is.EqualTo(Modified));
        }
    }
}
