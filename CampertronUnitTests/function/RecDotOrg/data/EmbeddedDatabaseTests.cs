using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.data
{
    // Tests for the bundled database (embedded resource content/RecreationDotOrg.db.br) and its
    // extraction. Each test extracts into its own temporary directory; no user directories or network.
    public class EmbeddedDatabaseTests
    {
        private string ConfigPath = string.Empty;
        private string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [SetUp]
        public void Setup()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronEmbeddedDbTests-" + Guid.NewGuid().ToString("N"));
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

        [Test]
        public void EmbeddedDatabase_PassesRefreshValidation()
        {
            Load.ExtractEmbeddedDbTo(DbFile);

            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
            Assert.That(LegacyAddressLayout.DetectLayout(DbFile), Is.EqualTo(LegacyAddressLayout.Layout.Corrected));
            Assert.That(LegacyAddressLayout.HasOnlyKnownSchemaObjects(DbFile), Is.True);
            // no temporary extraction file is left behind (SQLite's -wal/-shm files from the checks are expected)
            Assert.That(Directory.GetFiles(ConfigPath, "*.partial-*"), Is.Empty);
        }

        // The GUI "By City" and "By State" lists on the shipped data (business-rules §8): states are two-letter
        // codes from AddressStateCode, never ZIP codes from PostalCode. A build that read PostalCode, or a DB with
        // the swapped 2024 layout, shows "01005" as a state with the city BARRE instead.
        [Test]
        public void EmbeddedDatabase_GuiStateQueries_ReturnStateCodesNotZipCodes()
        {
            Load.ExtractEmbeddedDbTo(DbFile);

            List<string> States = Read.UniqueStates(ConfigPath);
            Assert.That(States, Is.Not.Empty);
            Assert.That(States, Has.All.Match("^[A-Z]{2}$"));
            Assert.That(States, Has.None.Match("[0-9]"));
            Assert.That(States, Does.Not.Contain("01005"));
            Assert.That(States, Is.SupersetOf(new[] { "AZ", "MA", "UT" }));

            Assert.That(Read.UniqueCities("MA", ConfigPath), Does.Contain("BARRE"));
            Assert.That(Read.UniqueParksByState("MA", ConfigPath), Is.Not.Empty);
            Assert.That(Read.UniqueCities("01005", ConfigPath), Is.Empty);
            Assert.That(Read.UniqueParksByState("01005", ConfigPath), Is.Empty);
            Assert.That(Read.UniqueParks("UT", "SPRINGDALE", ConfigPath), Does.Contain("WATCHMAN CAMPGROUND"));
        }

        [Test]
        public void ExtractEmbeddedDb_ExistingDestination_IsNotOverwritten()
        {
            File.WriteAllText(DbFile, "existing");

            Assert.Throws<IOException>(() => Load.ExtractEmbeddedDbTo(DbFile));

            Assert.That(File.ReadAllText(DbFile), Is.EqualTo("existing"));
            Assert.That(Directory.GetFiles(ConfigPath), Is.EqualTo(new[] { DbFile }));
        }
    }
}
