using System.Diagnostics;
using System.Security.Cryptography;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Campertron's index of the campsite attributes by campsite (RidbIndexes, decision D51, business-rules §8): built once,
    // idempotent, changing no row, used by the per-campsite lookups, made at a start (the bundled database just extracted
    // and a database an earlier version installed) and by a refresh, dropped before a refresh's import, and a best effort at
    // a start (a read-only or busy database is left as it is). A copy of the shipped database in a temporary folder and small
    // fixture databases; never the user's data.
    public class RidbIndexesTests
    {
        private string Shipped = string.Empty;
        private string Root = string.Empty;
        private int OriginalBusy;

        [OneTimeSetUp]
        public void ExtractShipped()
        {
            Shipped = Path.Join(Path.GetTempPath(), "CampertronRidbIndexesShipped-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Shipped);
            Load.ExtractEmbeddedDbTo(Path.Join(Shipped, "RecreationDotOrg.db"));
        }

        [OneTimeTearDown]
        public void RemoveShipped()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Shipped, true);
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronRidbIndexesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalBusy = RidbIndexes.StartupBusyTimeoutSeconds;
        }

        [TearDown]
        public void TearDown()
        {
            RidbIndexes.StartupBusyTimeoutSeconds = OriginalBusy;
            SqliteConnection.ClearAllPools();
            foreach (string File_ in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(File_, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
            Directory.Delete(Root, true);
        }

        private string CopyOfShipped(string Name)
        {
            string Folder = Path.Join(Root, Name);
            Directory.CreateDirectory(Folder);
            File.Copy(Path.Join(Shipped, "RecreationDotOrg.db"), Path.Join(Folder, "RecreationDotOrg.db"));
            return Path.Join(Folder, "RecreationDotOrg.db");
        }

        // a small database with Campertron's tables (the EF model) and a few attribute rows
        private string Fixture(string Name)
        {
            string Folder = Path.Join(Root, Name);
            Directory.CreateDirectory(Folder);
            using (var db = new RecreationDotOrgContext(Folder))
            {
                db.Database.EnsureCreated();
                for (int i = 0; i < 20; i++)
                {
                    db.CampsiteAttributesEntries.Add(new CampsiteAttributesRecdata { AttributeID = i % 5, EntityID = "S" + (i / 5), AttributeName = "A" + (i % 5), AttributeValue = "V" + i });
                }
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
            return Path.Join(Folder, "RecreationDotOrg.db");
        }

        private static string Sha256(string File_)
        {
            using (var Stream = new FileStream(File_, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return Convert.ToHexString(SHA256.HashData(Stream));
            }
        }

        private static List<string> Scalars(string DbFile, string Sql)
        {
            var Rows = new List<string>();
            using (var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = Sql;
                    using (var Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read())
                        {
                            Rows.Add(string.Join("|", Enumerable.Range(0, Reader.FieldCount).Select(i => Reader.IsDBNull(i) ? "null" : Reader.GetValue(i).ToString())));
                        }
                    }
                }
            }
            return Rows;
        }

        // ---- the index on the shipped data ----

        [Test]
        public void OnTheShippedData_ItIsBuiltOnce_ChangesNoRow_AndServesTheLookups()
        {
            string Original = Path.Join(Shipped, "RecreationDotOrg.db");
            string DbFile = CopyOfShipped("built");
            Assert.That(RidbIndexes.Exists(DbFile), Is.False, "the bundled database does not have it");
            Assert.That(Scalars(DbFile, "EXPLAIN QUERY PLAN SELECT AttributeName FROM CampsiteAttributesEntries WHERE EntityID = '10146649'").Single(), Does.Contain("SCAN"));
            long Before = new FileInfo(DbFile).Length;

            var Clock = Stopwatch.StartNew();
            Assert.That(RidbIndexes.Create(DbFile), Is.True);
            TestContext.Out.WriteLine($"index built in {Clock.ElapsedMilliseconds} ms; {Before} -> {new FileInfo(DbFile).Length} bytes");

            Assert.That(RidbIndexes.Exists(DbFile), Is.True);
            Assert.That(Scalars(DbFile, "EXPLAIN QUERY PLAN SELECT AttributeName FROM CampsiteAttributesEntries WHERE EntityID = '10146649'").Single(),
                Does.Contain("USING INDEX " + RidbIndexes.CampsiteAttributesByCampsite));
            Assert.That(Scalars(DbFile, "PRAGMA integrity_check"), Is.EqualTo(new[] { "ok" }));
            Assert.That(RidbDatabase.Validate(Path.GetDirectoryName(DbFile)!), Is.Empty);
            Assert.That(LegacyAddressLayout.HasOnlyKnownSchemaObjects(DbFile), Is.True);
            // the schema gained exactly the index; every table holds exactly the rows it held
            List<string> Schema(string F) => Scalars(F, "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name");
            Assert.That(Schema(DbFile).Except(Schema(Original)), Is.EqualTo(new[] { "index|IX_CampsiteAttributesEntries_EntityID|CampsiteAttributesEntries|CREATE INDEX \"IX_CampsiteAttributesEntries_EntityID\" ON \"CampsiteAttributesEntries\" (\"EntityID\")" }));
            Assert.That(Schema(Original).Except(Schema(DbFile)), Is.Empty);
            using (var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                Connection.Open();
                using (var Attach = Connection.CreateCommand())
                {
                    Attach.CommandText = "ATTACH DATABASE $original AS original";
                    Attach.Parameters.AddWithValue("$original", Original);
                    Attach.ExecuteNonQuery();
                }
                foreach (string Table in new[] { "CampsiteAttributesEntries", "CampsitesEntries", "FacilitiesEntries", "FacilityAddressesEntries", "PermittedEquipmentEntries", "RecAreaEntries", "__EFMigrationsHistory" })
                {
                    using (var Command = Connection.CreateCommand())
                    {
                        Command.CommandText = $"SELECT (SELECT count(*) FROM main.\"{Table}\"), (SELECT count(*) FROM original.\"{Table}\"), " +
                            $"(SELECT count(*) FROM (SELECT * FROM main.\"{Table}\" EXCEPT SELECT * FROM original.\"{Table}\")), " +
                            $"(SELECT count(*) FROM (SELECT * FROM original.\"{Table}\" EXCEPT SELECT * FROM main.\"{Table}\"))";
                        using (var Reader = Command.ExecuteReader())
                        {
                            Reader.Read();
                            Assert.That((Reader.GetInt64(0), Reader.GetInt64(2), Reader.GetInt64(3)), Is.EqualTo((Reader.GetInt64(1), 0L, 0L)), Table);
                        }
                    }
                }
            }

            // idempotent: a second creation writes nothing
            SqliteConnection.ClearAllPools();
            string Hash = Sha256(DbFile);
            DateTime Written = File.GetLastWriteTimeUtc(DbFile);
            Assert.That(RidbIndexes.Create(DbFile), Is.False);
            Assert.That(RidbIndexes.EnsureQuietly(DbFile), Is.False);
            Assert.That(Sha256(DbFile), Is.EqualTo(Hash));
            Assert.That(File.GetLastWriteTimeUtc(DbFile), Is.EqualTo(Written));
        }

        // ---- the start ----

        [Test]
        public void AStart_IndexesAnInstalledDatabaseOnce_ThenOnlyReadsIt()
        {
            string DbFile = CopyOfShipped("installed");
            string ConfigPath = Path.GetDirectoryName(DbFile)!;
            var Config = new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = Path.Join(Root, "cache"), GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest } };

            Assert.That(Load.DbExistsCheck(Config), Is.True);
            Assert.That(RidbIndexes.Exists(DbFile), Is.True, "an installed database without it gets it");
            SqliteConnection.ClearAllPools();
            string Hash = Sha256(DbFile);
            Assert.That(Load.DbExistsCheck(Config), Is.True);
            SqliteConnection.ClearAllPools();
            Assert.That(Sha256(DbFile), Is.EqualTo(Hash), "the next start writes nothing");
        }

        [Test]
        public void AFirstStart_IndexesTheBundledDatabaseItExtracts()
        {
            string ConfigPath = Path.Join(Root, "first");
            Directory.CreateDirectory(ConfigPath);
            var Config = new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = Path.Join(Root, "cache"), GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest } };

            Assert.That(Load.DbExistsCheck(Config), Is.True);
            Assert.That(RidbIndexes.Exists(Path.Join(ConfigPath, "RecreationDotOrg.db")), Is.True);
        }

        [Test]
        public void AStart_LeavesAReadOnlyDatabaseAsItIs()
        {
            if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
            {
                // Unix file modes; a privileged process (root, also in a user namespace) writes through them
                Assert.Ignore("Unix file modes, unprivileged");
                return;
            }
            string DbFile = Fixture("readonly");
            string Hash = Sha256(DbFile);
            File.SetUnixFileMode(DbFile, UnixFileMode.UserRead);

            Assert.That(RidbIndexes.EnsureQuietly(DbFile), Is.False);
            Assert.That(() => RidbIndexes.Create(DbFile), Throws.InstanceOf<SqliteException>());
            Assert.That(Sha256(DbFile), Is.EqualTo(Hash));
            Assert.That(RidbIndexes.Exists(DbFile), Is.False);
        }

        [Test]
        public void AStart_DoesNotWaitLongForABusyDatabase_AndTheNextStartIndexesIt()
        {
            string DbFile = Fixture("busy");
            RidbIndexes.StartupBusyTimeoutSeconds = 1;
            using (var Writer = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Pooling = false }.ToString()))
            {
                Writer.Open();
                using (var Begin = Writer.CreateCommand())
                {
                    Begin.CommandText = "BEGIN IMMEDIATE";
                    Begin.ExecuteNonQuery();
                }
                var Clock = Stopwatch.StartNew();
                Assert.That(RidbIndexes.EnsureQuietly(DbFile), Is.False);
                Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            }
            Assert.That(RidbIndexes.Exists(DbFile), Is.False);
            Assert.That(RidbIndexes.EnsureQuietly(DbFile), Is.True);
            Assert.That(RidbIndexes.Exists(DbFile), Is.True);
        }

        [Test]
        public void AMissingDatabase_IsNeverCreated_AndADatabaseWithoutTheTable_IsLeftAlone()
        {
            string Missing = Path.Join(Root, "none", "RecreationDotOrg.db");
            Assert.That(() => RidbIndexes.Create(Missing), Throws.InstanceOf<FileNotFoundException>());
            Assert.That(RidbIndexes.EnsureQuietly(Missing), Is.False);
            Assert.That(File.Exists(Missing), Is.False);

            string Other = Path.Join(Root, "other.db");
            using (var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Other, Pooling = false }.ToString()))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = "CREATE TABLE Something (Id TEXT)";
                    Command.ExecuteNonQuery();
                }
            }
            string Hash = Sha256(Other);
            Assert.That(RidbIndexes.Create(Other), Is.False);
            Assert.That(Sha256(Other), Is.EqualTo(Hash));
        }

        // ---- the refresh ----

        [Test]
        public void TheRefreshesClear_DropsTheIndex_SoTheImportInsertsWithoutIt()
        {
            string DbFile = Fixture("clear");
            Assert.That(RidbIndexes.Create(DbFile), Is.True);
            Clear.ClearDatabase(Path.GetDirectoryName(DbFile)!);
            SqliteConnection.ClearAllPools();
            Assert.That(RidbIndexes.Exists(DbFile), Is.False);
            Assert.That(RidbIndexes.Create(DbFile), Is.True, "built again after the import");
        }
    }
}
