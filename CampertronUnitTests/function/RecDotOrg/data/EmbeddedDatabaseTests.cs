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
