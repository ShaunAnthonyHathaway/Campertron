using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // The failure-safe RIDB refresh (RidbStagedRefresh through RefreshRidbRecreationData.RefreshData, TD-36,
    // decision D26). Every test has its own temporary config, cache and RIDB fixture directories; the network
    // download is replaced by the RefreshData overload's extract-directory provider. No test touches
    // LocalApplicationData or the network. A failed refresh must leave the installed database byte for byte
    // unchanged, the cache unchanged (if it failed before the cache step) and LastRidbDataRefresh unchanged.
    public class AtomicRefreshTests
    {
        private static readonly DateTime PreviousRefresh = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        private const string OldMarker = "OLD FACILITY";
        private const string NewMarker = "NEW FACILITY";

        private string SharedDirectory = string.Empty;
        private string EmptiedShippedDb = string.Empty;
        private string PreviousDb = string.Empty;

        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "CampertronConfig");
        private string CacheDirectory => Path.Join(Root, "CampertronCache");
        private string InstalledDb => Path.Join(ConfigPath, "RecreationDotOrg.db");
        private string GeneralYaml => Path.Join(ConfigPath, "General.yaml");
        private CampertronInternalConfig Config = new CampertronInternalConfig();
        private StringWriter Output = null!;
        private TextWriter? OriginalOut;

        [OneTimeSetUp]
        public void CreateDatabases()
        {
            SharedDirectory = NewTempDirectory("CampertronAtomicRefreshShared-");
            // the shipped database, emptied: the template the in-place refresh started from
            string TemplateDirectory = Path.Join(SharedDirectory, "template");
            Directory.CreateDirectory(TemplateDirectory);
            EmptiedShippedDb = Path.Join(TemplateDirectory, "RecreationDotOrg.db");
            Load.ExtractEmbeddedDbTo(EmptiedShippedDb);
            Clear.ClearDatabase(TemplateDirectory);
            SqliteConnection.ClearAllPools();
            Checkpoint(EmptiedShippedDb);
            // a valid installed database from an earlier refresh, recognizable by OldMarker
            PreviousDb = BuildInPlace(EmptiedShippedDb, ValidFixture(OldMarker), "previous");
            Assert.That(RidbDatabase.Validate(Path.GetDirectoryName(PreviousDb)!), Is.Empty);
            SqliteConnection.ClearAllPools();
        }

        [OneTimeTearDown]
        public void DeleteShared()
        {
            SqliteConnection.ClearAllPools();
            DeleteTree(SharedDirectory);
        }

        [SetUp]
        public void Setup()
        {
            Root = NewTempDirectory("CampertronAtomicRefreshTests-");
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CacheDirectory);
            Config = new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CacheDirectory,
                GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest, AutoRefresh = false, RefreshRidbDataDayInterval = 30, LastRidbDataRefresh = PreviousRefresh }
            };
            Yaml.GeneralConfigConvertToYaml(Config.GeneralConfig!, "General", ConfigPath);
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Campsites.json"), "[]");
            File.WriteAllText(Path.Join(CacheDirectory, "abc.html"), "<html></html>");
            Output = new StringWriter();
            OriginalOut = Console.Out;
            Console.SetOut(Output);
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
        }

        [TearDown]
        public void TearDown()
        {
            if (OriginalOut != null)
            {
                Console.SetOut(OriginalOut);
            }
            Output.Dispose();
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
            SqliteConnection.ClearAllPools();
            DeleteTree(Root);
        }

        // ---- success ----

        [Test]
        public void Refresh_Success_InstallsTheValidatedDatabaseByteIdenticalToTheInPlaceRefresh()
        {
            InstallPrevious();
            RidbFixture Fixture = ValidFixture(NewMarker);

            Assert.That(Refresh(WriteFixture(Fixture)), Is.True, Output.ToString());

            // the in-place refresh this replaces: the same installed database cleared and populated directly
            string Expected = BuildInPlace(PreviousDb, Fixture, "expected");
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Sha256(Expected)));
            Assert.That(Dump(InstalledDb), Is.EqualTo(Dump(Expected)));
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            Assert.That(QuickCheck(InstalledDb), Is.EqualTo("ok"));
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
            // the derived cache is invalidated, the timestamp advanced once and persisted
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty);
            Assert.That(Config.GeneralConfig!.LastRidbDataRefresh, Is.GreaterThan(PreviousRefresh));
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(Config.GeneralConfig.LastRidbDataRefresh));
            // nothing left behind except the lock file, which is never deleted
            Assert.That(StagingDirectories(), Is.Empty);
            Assert.That(File.Exists(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName)), Is.True);
            Assert.That(Output.ToString(), Does.Contain("Installed the refreshed database"));
        }

        [Test]
        public void Refresh_InstalledDatabaseMissing_InstallsFromTheBundledTemplate()
        {
            RidbFixture Fixture = ValidFixture(NewMarker);
            string Shipped = Path.Join(NewTempDirectory("CampertronAtomicRefreshShipped-", SharedDirectory), "RecreationDotOrg.db");
            Load.ExtractEmbeddedDbTo(Shipped);

            Assert.That(Refresh(WriteFixture(Fixture)), Is.True, Output.ToString());

            string Expected = BuildInPlace(Shipped, Fixture, "expected-missing");
            Assert.That(Dump(InstalledDb), Is.EqualTo(Dump(Expected)));
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Sha256(Expected)));
            Assert.That(QuickCheck(InstalledDb), Is.EqualTo("ok"));
            Assert.That(StagingDirectories(), Is.Empty);
            Assert.That(Output.ToString(), Does.Contain("the bundled database is used as the template"));
        }

        [Test]
        public void Refresh_SecondRefreshAfterSuccess_ReplacesTheDataAgain()
        {
            InstallPrevious();
            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());
            Assert.That(Refresh(WriteFixture(ValidFixture("THIRD FACILITY"))), Is.True, Output.ToString());

            Assert.That(Marker(InstalledDb), Is.EqualTo("THIRD FACILITY"));
            Assert.That(StagingDirectories(), Is.Empty);
        }

        // ---- failures before the installation: nothing changes ----

        [Test]
        public void Refresh_MalformedJson_Throws_AndChangesNothing()
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            string Ridb = WriteFixture(ValidFixture(NewMarker));
            File.WriteAllText(Path.Join(Ridb, "Facilities_API_v1.json"), "{\"RECDATA\": [ {\"FacilityID\": ");

            Assert.That(() => Refresh(Ridb), Throws.Exception);

            AssertNothingChanged(Before);
            Assert.That(Directory.Exists(Ridb), Is.True, "the extract directory remains after a failed import, as before");
        }

        [Test]
        public void Refresh_ExtractStepFails_Throws_AndChangesNothing()
        {
            // a failure at the orchestration boundary (download, extraction or anything before the import)
            InstallPrevious();
            Snapshot Before = TakeSnapshot();

            Assert.That(() => RefreshRidbRecreationData.RefreshData(false, Config, () => throw new InvalidDataException("injected"), CacheDirectory),
                Throws.TypeOf<InvalidDataException>());

            AssertNothingChanged(Before);
        }

        [TestCase("first")]
        [TestCase("last")]
        public void Refresh_DuplicateKey_Throws_AndChangesNothing(string Table)
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            RidbFixture Fixture = ValidFixture(NewMarker);
            if (Table == "first")
            {
                // CampsiteAttributesEntries is the first table inserted
                Fixture.CampsiteAttributes.Add(Fixture.CampsiteAttributes[0]);
            }
            else
            {
                // RecAreaEntries is the last; the five other tables were inserted before it fails
                Fixture.RecAreas.Add(Fixture.RecAreas[3]);
            }

            Assert.That(() => Refresh(WriteFixture(Fixture)), Throws.TypeOf<SqliteException>());

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_NullRow_Throws_AndChangesNothing()
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            string Ridb = WriteFixture(ValidFixture(NewMarker));
            RewriteRecData(Ridb, "Campsites", Rows => Rows.Insert(2, null));

            Assert.That(() => Refresh(Ridb), Throws.TypeOf<InvalidDataException>());

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_NullKey_Throws_AndChangesNothing()
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            string Ridb = WriteFixture(ValidFixture(NewMarker));
            RewriteRecData(Ridb, "Campsites", Rows => Rows[4]!["CampsiteID"] = null);

            Assert.That(() => Refresh(Ridb), Throws.TypeOf<SqliteException>());

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_StagingValidationFails_ReturnsFalse_AndChangesNothing()
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            // the plain fixture has no Watchman, so RidbDatabase.Validate rejects the staging database
            Assert.That(Refresh(WriteFixture(RidbFixture.Create())), Is.False);

            AssertNothingChanged(Before);
            Assert.That(Output.ToString(), Does.Contain("failed validation"));
            Assert.That(Output.ToString(), Does.Contain("WATCHMAN CAMPGROUND is not listed"));
            Assert.That(Output.ToString(), Does.Contain("The installed database was not changed."));
        }

        [Test]
        public void Refresh_StagingDirectoryCannotBeCreated_Throws_AndChangesNothing()
        {
            InstallPrevious();
            RequireUnixPermissions();
            // the lock file exists, so only creating the staging directory fails
            File.WriteAllText(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName), "");
            Snapshot Before = TakeSnapshot();
            UnixFileMode Mode = GetMode(ConfigPath);
            SetMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.TypeOf<UnauthorizedAccessException>());
            }
            finally
            {
                SetMode(ConfigPath, Mode);
            }

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_InstalledDatabaseCorrupt_Throws_AndLeavesItUnchanged()
        {
            byte[] Garbage = new byte[65536];
            new Random(36).NextBytes(Garbage);
            File.WriteAllBytes(InstalledDb, Garbage);
            Snapshot Before = TakeSnapshot();

            Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.TypeOf<SqliteException>());

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_InstalledDatabaseUnreadable_Throws_AndLeavesItUnchanged()
        {
            InstallPrevious();
            RequireUnixPermissions();
            Snapshot Before = TakeSnapshot();
            SetMode(InstalledDb, UnixFileMode.None);
            try
            {
                Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.TypeOf<SqliteException>());
            }
            finally
            {
                SetMode(InstalledDb, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_CacheCannotBeCleared_Throws_BeforeInstalling()
        {
            InstallPrevious();
            RequireUnixPermissions();
            Snapshot Before = TakeSnapshot();
            UnixFileMode Mode = GetMode(CacheDirectory);
            SetMode(CacheDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.InstanceOf<Exception>());
            }
            finally
            {
                SetMode(CacheDirectory, Mode);
            }

            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_CacheDirectoryMissing_Throws_BeforeInstalling()
        {
            // Directory.GetFiles on a missing cache directory threw in the in-place refresh too (after the clear)
            InstallPrevious();
            Directory.Delete(CacheDirectory, true);
            Snapshot Before = TakeSnapshot();

            Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.TypeOf<DirectoryNotFoundException>());

            AssertNothingChanged(Before);
        }

        // ---- failures of the installation: the old database stays ----

        [Test]
        public void Refresh_InstalledDatabaseReadOnly_Throws_AndLeavesItUnchanged()
        {
            InstallPrevious();
            RequireUnixPermissions();
            string Hash = Sha256(InstalledDb);
            SetMode(InstalledDb, UnixFileMode.UserRead);
            try
            {
                Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.TypeOf<SqliteException>());
            }
            finally
            {
                SetMode(InstalledDb, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            Assert.That(Marker(InstalledDb), Is.EqualTo(OldMarker));
            AssertTimestampUnchanged();
            // the cache was cleared just before the installation: old database with an empty cache is acceptable
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty);
            Assert.That(StagingDirectories(), Is.Empty);
        }

        [Test]
        public void Refresh_CompetingWriterHoldsTheWriteLock_FailsWithBusy_AndLeavesTheDatabaseUnchanged()
        {
            InstallPrevious();
            string Hash = Sha256(InstalledDb);
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 500;
            using (SqliteConnection Writer = OpenUnpooled(InstalledDb, SqliteOpenMode.ReadWrite))
            using (SqliteTransaction Transaction = Writer.BeginTransaction())
            {
                using (SqliteCommand Command = Writer.CreateCommand())
                {
                    Command.Transaction = Transaction;
                    Command.CommandText = "UPDATE RecAreaEntries SET RecAreaName = RecAreaName WHERE rowid = 1";
                    Command.ExecuteNonQuery();
                }
                var Clock = System.Diagnostics.Stopwatch.StartNew();
                SqliteException? Failure = null;
                try
                {
                    Refresh(WriteFixture(ValidFixture(NewMarker)));
                }
                catch (SqliteException ex)
                {
                    Failure = ex;
                }
                Clock.Stop();
                Assert.That(Failure, Is.Not.Null, Output.ToString());
                // SQLITE_BUSY, after the installation's busy timeout (BackupDatabase does not apply Default Timeout)
                Assert.That(Failure!.SqliteErrorCode, Is.EqualTo(5), Failure.Message);
                Assert.That(Output.ToString(), Does.Contain("Installing the refreshed database"));
                Transaction.Rollback();
            }

            SqliteConnection.ClearAllPools();
            Assert.That(Checkpoint(InstalledDb), Is.True);
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            Assert.That(Marker(InstalledDb), Is.EqualTo(OldMarker));
            AssertTimestampUnchanged();
            Assert.That(StagingDirectories(), Is.Empty);
        }

        [Test]
        public void Refresh_CompetingWriterReleasesWithinTheBusyTimeout_Succeeds()
        {
            InstallPrevious();
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
            var Writer = OpenUnpooled(InstalledDb, SqliteOpenMode.ReadWrite);
            var Transaction = Writer.BeginTransaction();
            using (SqliteCommand Command = Writer.CreateCommand())
            {
                Command.Transaction = Transaction;
                Command.CommandText = "UPDATE RecAreaEntries SET RecAreaName = RecAreaName WHERE rowid = 1";
                Command.ExecuteNonQuery();
            }
            // release the write lock shortly after the installation starts waiting for it
            var Release = Task.Run(() =>
            {
                // the installation creates the repair lock file just before the backup
                while (!File.Exists(Path.Join(ConfigPath, RidbStagedRefresh.RepairLockFileName))) Thread.Sleep(10);
                Thread.Sleep(300);
                Transaction.Rollback();
                Transaction.Dispose();
                Writer.Dispose();
            });

            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());
            Release.Wait();
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
        }

        [Test]
        public void Refresh_RepairLockHeld_Throws_AndLeavesTheDatabaseUnchanged()
        {
            InstallPrevious();
            string Hash = Sha256(InstalledDb);
            using (new FileStream(Path.Join(ConfigPath, RidbStagedRefresh.RepairLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(() => Refresh(WriteFixture(ValidFixture(NewMarker))), Throws.InstanceOf<IOException>());
            }

            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            AssertTimestampUnchanged();
            Assert.That(StagingDirectories(), Is.Empty);
        }

        [Test]
        public void RepairLockName_IsTheOneTheStartupRepairUses()
        {
            // LegacyAddressLayout.RepairIfNeeded opens Path.Join(ConfigPath, "RecreationDotOrg.repair.lock")
            Assert.That(RidbStagedRefresh.RepairLockFileName, Is.EqualTo("RecreationDotOrg.repair.lock"));
        }

        // ---- the timestamp ----

        [Test]
        public void Refresh_GeneralYamlCannotBeReplaced_StillSucceeds_AndWarns()
        {
            // a real replacement failure after the installation: General.yaml is a directory, so the rename fails
            InstallPrevious();
            File.Delete(GeneralYaml);
            Directory.CreateDirectory(GeneralYaml);
            File.WriteAllText(Path.Join(GeneralYaml, "keep.txt"), "keep");

            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());

            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            Assert.That(File.ReadAllText(Path.Join(GeneralYaml, "keep.txt")), Is.EqualTo("keep"));
            Assert.That(Output.ToString(), Does.Contain("The RIDB database refresh succeeded, but General.yaml could not be updated"));
            Assert.That(Directory.GetFiles(ConfigPath).Where(f => ConfigFileReplace.IsTemporaryName("General.yaml", Path.GetFileName(f))), Is.Empty);
        }

        [Test]
        public void Refresh_ReadOnlyGeneralYamlFile_IsReplacedLikeAnyOther_OnUnix()
        {
            // Since TD-37 the timestamp is written by renaming a temporary file over General.yaml; on Unix that needs
            // write permission on the directory, not on the file (the in-place StreamWriter failed here before)
            InstallPrevious();
            RequireUnixPermissions();
            SetMode(GeneralYaml, UnixFileMode.UserRead);

            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());

            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(Config.GeneralConfig!.LastRidbDataRefresh));
            Assert.That(Config.GeneralConfig.LastRidbDataRefresh, Is.GreaterThan(PreviousRefresh));
        }

        [Test]
        public void Refresh_Success_ReplacesGeneralYamlExactlyOnce_AndKeepsUnrelatedValues()
        {
            InstallPrevious();
            Config.GeneralConfig!.RefreshRidbDataDayInterval = 12.5;
            Config.GeneralConfig.AutoRefresh = true;
            Yaml.GeneralConfigConvertToYaml(Config.GeneralConfig, "General", ConfigPath);
            var Events = new System.Collections.Concurrent.ConcurrentQueue<string>();
            DateTime Before = DateTime.UtcNow;
            bool Result;
            using (var Watcher = new FileSystemWatcher(ConfigPath))
            {
                Watcher.Created += (o, e) => Events.Enqueue("created " + e.Name);
                Watcher.Changed += (o, e) => Events.Enqueue("changed " + e.Name);
                Watcher.Renamed += (o, e) => Events.Enqueue("renamed " + e.OldName + " -> " + e.Name);
                Watcher.EnableRaisingEvents = true;
                Result = Refresh(WriteFixture(ValidFixture(NewMarker)));
                Thread.Sleep(500);
            }

            Assert.That(Result, Is.True, Output.ToString());
            List<string> Replacements = Events.Where(e => e.StartsWith("renamed ") && e.EndsWith(" -> General.yaml")).ToList();
            Assert.That(Replacements, Has.Count.EqualTo(1), string.Join("; ", Events));
            Assert.That(Events.Where(e => e == "changed General.yaml" || e == "created General.yaml"), Is.Empty, "General.yaml is never written in place: " + string.Join("; ", Events));
            GeneralConfig Loaded = Yaml.GeneralConfigGetConfig(ConfigPath);
            Assert.That(Loaded.LastRidbDataRefresh, Is.EqualTo(Config.GeneralConfig.LastRidbDataRefresh));
            Assert.That(Loaded.LastRidbDataRefresh, Is.InRange(Before, DateTime.UtcNow));
            Assert.That(Loaded.RefreshRidbDataDayInterval, Is.EqualTo(12.5));
            Assert.That(Loaded.AutoRefresh, Is.True);
            Assert.That(Loaded.OutputTo, Is.EqualTo(OutputType.UnitTest));
            Assert.That(Directory.GetFiles(ConfigPath).Where(f => ConfigFileReplace.IsTemporaryName("General.yaml", Path.GetFileName(f))), Is.Empty);
        }

        [Test]
        public void Refresh_StaleTemporaryGeneralYaml_IsNeverUsed_AndIsRemovedByTheNextRefresh()
        {
            InstallPrevious();
            string Stale = Path.Join(ConfigPath, "General.yaml." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(Stale, "outputTo: Email\nlastRidbDataRefresh: 2099-01-01T00:00:00Z\n");
            Snapshot Before = TakeSnapshot();

            // a refresh that fails validation still removes the stale file at its start, and changes nothing else
            Assert.That(Refresh(WriteFixture(RidbFixture.Create())), Is.False);

            Assert.That(File.Exists(Stale), Is.False);
            AssertNothingChanged(Before);
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).OutputTo, Is.EqualTo(OutputType.UnitTest));
        }

        [Test]
        public void Install_IntoAMissingDatabase_ThatFails_RemovesTheCreatedDatabase_AndTheNextStartExtractsTheBundledOne()
        {
            // a staging database whose page size differs from the bundled one: SQLite rejects a backup into a WAL
            // destination with a different page size (SQLITE_READONLY), after the destination has been created
            string StagingDirectory = NewTempDirectory("staging-", Root);
            string StagingDb = Path.Join(StagingDirectory, "RecreationDotOrg.db");
            using (SqliteConnection Connection = OpenUnpooled(StagingDb, SqliteOpenMode.ReadWriteCreate))
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA page_size = 8192; PRAGMA journal_mode = WAL; CREATE TABLE t (x INTEGER);";
                Command.ExecuteNonQuery();
            }
            Assert.That(File.Exists(InstalledDb), Is.False);

            var Failure = Assert.Throws<SqliteException>(() => RidbStagedRefresh.Install(StagingDb, InstalledDb, ConfigPath));

            Assert.That(Failure!.SqliteErrorCode, Is.EqualTo(8), Failure.Message);
            foreach (string Suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                Assert.That(File.Exists(InstalledDb + Suffix), Is.False, "RecreationDotOrg.db" + Suffix);
            }
            Assert.That(Directory.GetFiles(ConfigPath).Where(f => Path.GetFileName(f).Contains(".partial-")), Is.Empty);
            // the next start sees no database and extracts the bundled one, as on a first run
            SqliteConnection.ClearAllPools();
            Assert.That(Load.DbExistsCheck(Config), Is.True);
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
        }

        [Test]
        public void Install_IntoAnExistingDatabase_ThatFails_LeavesItUnchanged()
        {
            InstallPrevious();
            string Hash = Sha256(InstalledDb);
            string StagingDirectory = NewTempDirectory("staging-", Root);
            string StagingDb = Path.Join(StagingDirectory, "RecreationDotOrg.db");
            using (SqliteConnection Connection = OpenUnpooled(StagingDb, SqliteOpenMode.ReadWriteCreate))
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA page_size = 8192; PRAGMA journal_mode = WAL; CREATE TABLE t (x INTEGER);";
                Command.ExecuteNonQuery();
            }

            Assert.That(() => RidbStagedRefresh.Install(StagingDb, InstalledDb, ConfigPath), Throws.TypeOf<SqliteException>());

            SqliteConnection.ClearAllPools();
            Assert.That(Checkpoint(InstalledDb), Is.True);
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Hash));
            Assert.That(Marker(InstalledDb), Is.EqualTo(OldMarker));
        }

        [Test]
        public void AutoRefresh_ThatDoesNotInstall_LeavesTheTimestampUnchanged()
        {
            // Load.DbExistsCheck used to write LastRidbDataRefresh before calling RefreshData. The refresh lock is
            // held here, so RefreshData returns false at once, without a download; the timestamp must not move.
            InstallPrevious();
            Config.GeneralConfig!.AutoRefresh = true;
            Yaml.GeneralConfigConvertToYaml(Config.GeneralConfig, "General", ConfigPath);
            string YamlBefore = File.ReadAllText(GeneralYaml);
            using (new FileStream(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.That(Load.DbExistsCheck(Config), Is.True);
            }

            Assert.That(Output.ToString(), Does.Contain("Another RIDB refresh is running"), "the auto-refresh was attempted");
            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(YamlBefore));
            AssertTimestampUnchanged();
            Assert.That(Marker(InstalledDb), Is.EqualTo(OldMarker));
        }

        // ---- locking, stale staging data, concurrency ----

        [Test]
        public void Refresh_AnotherRefreshHoldsTheLock_ReturnsFalse_AndTouchesNothing()
        {
            InstallPrevious();
            Snapshot Before = TakeSnapshot();
            string Stale = Path.Join(ConfigPath, RidbStagedRefresh.StagingDirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Stale);
            bool ExtractCalled = false;
            bool Result;
            using (new FileStream(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Result = RefreshRidbRecreationData.RefreshData(false, Config, () => { ExtractCalled = true; return WriteFixture(ValidFixture(NewMarker)); }, CacheDirectory);
            }

            Assert.That(Result, Is.False);
            Assert.That(ExtractCalled, Is.False, "nothing is downloaded or extracted while another refresh runs");
            Assert.That(Directory.Exists(Stale), Is.True, "the other refresh's staging directory is not removed");
            Assert.That(Output.ToString(), Does.Contain("Another RIDB refresh is running"));
            Directory.Delete(Stale);
            AssertNothingChanged(Before);
        }

        [Test]
        public void Refresh_LockIsReleasedAfterwards_AndItsFileKept()
        {
            InstallPrevious();
            Assert.That(Refresh(WriteFixture(RidbFixture.Create())), Is.False);
            using (FileStream? Again = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out _))
            {
                Assert.That(Again, Is.Not.Null);
            }
            Assert.That(File.Exists(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName)), Is.True);
        }

        [Test]
        public void Refresh_RemovesStaleStagingDirectories_AndNothingThatOnlyLooksLikeOne()
        {
            InstallPrevious();
            string Guid1 = Guid.NewGuid().ToString("N");
            string Stale = Path.Join(ConfigPath, RidbStagedRefresh.StagingDirectoryPrefix + Guid1);
            Directory.CreateDirectory(Stale);
            File.WriteAllText(Path.Join(Stale, "RecreationDotOrg.db"), "partial");
            string[] LookAlikes =
            {
                RidbStagedRefresh.StagingDirectoryPrefix + "notaguid",
                RidbStagedRefresh.StagingDirectoryPrefix + Guid1.ToUpperInvariant(),
                RidbStagedRefresh.StagingDirectoryPrefix + Guid.NewGuid().ToString("D"),
                RidbStagedRefresh.StagingDirectoryPrefix + Guid1 + "-x",
                RidbStagedRefresh.StagingDirectoryPrefix + Guid1.Substring(1),
                "X" + RidbStagedRefresh.StagingDirectoryPrefix + Guid1,
                "RecreationDotOrg.repair-" + Guid1
            };
            foreach (string Name in LookAlikes)
            {
                Directory.CreateDirectory(Path.Join(ConfigPath, Name));
                File.WriteAllText(Path.Join(ConfigPath, Name, "keep.txt"), "keep");
            }
            string FileWithStagingName = Path.Join(ConfigPath, RidbStagedRefresh.StagingDirectoryPrefix + Guid.NewGuid().ToString("N"));
            File.WriteAllText(FileWithStagingName, "keep");
            // a link with a staging name is removed as a link; its target is never followed
            string Outside = NewTempDirectory("CampertronAtomicRefreshOutside-", Root);
            File.WriteAllText(Path.Join(Outside, "keep.txt"), "keep");
            string Link = Path.Join(ConfigPath, RidbStagedRefresh.StagingDirectoryPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateSymbolicLink(Link, Outside);

            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());

            Assert.That(Directory.Exists(Stale), Is.False);
            Assert.That(Path.Exists(Link), Is.False);
            Assert.That(File.Exists(Path.Join(Outside, "keep.txt")), Is.True);
            foreach (string Name in LookAlikes)
            {
                Assert.That(File.Exists(Path.Join(ConfigPath, Name, "keep.txt")), Is.True, Name);
            }
            Assert.That(File.Exists(FileWithStagingName), Is.True);
        }

        [Test]
        public void IsStagingDirectoryName_AcceptsOnlyTheExactConvention()
        {
            string Id = Guid.NewGuid().ToString("N");
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh-" + Id), Is.True);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh-" + Id.ToUpperInvariant()), Is.False);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh-" + Guid.NewGuid().ToString("D")), Is.False);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh-" + Id + "0"), Is.False);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh-" + new string('g', 32)), Is.False);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("recreationdotorg.refresh-" + Id), Is.False);
            Assert.That(RidbStagedRefresh.IsStagingDirectoryName("RecreationDotOrg.refresh.lock"), Is.False);
        }

        // ---- connections in use during the installation ----

        [Test]
        public void Refresh_ReaderHoldsASnapshot_KeepsItsConsistentView_AndNewConnectionsSeeTheNewData()
        {
            InstallPrevious();
            using (SqliteConnection Reader = OpenUnpooled(InstalledDb, SqliteOpenMode.ReadOnly))
            using (SqliteTransaction Snapshot = Reader.BeginTransaction(deferred: true))
            {
                Assert.That(Marker(Reader, Snapshot), Is.EqualTo(OldMarker));

                Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());

                Assert.That(Marker(Reader, Snapshot), Is.EqualTo(OldMarker), "a reader keeps its snapshot");
                Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker), "new connections see the installed data");
                Assert.That(Output.ToString(), Does.Contain("Another connection is still reading the previous data"));
                Snapshot.Commit();
                Assert.That(Marker(Reader, null), Is.EqualTo(NewMarker));
            }
            Assert.That(Checkpoint(InstalledDb), Is.True);
            Assert.That(QuickCheck(InstalledDb), Is.EqualTo("ok"));
        }

        [Test]
        public void Refresh_PooledConnectionOpenDuringInstallation_SeesTheNewDataAfterwards()
        {
            InstallPrevious();
            // leaves idle pooled connections on the installed database, as the GUI does
            Assert.That(Read.UniqueParks("UT", "SPRINGDALE", ConfigPath), Does.Contain(RidbDatabase.WatchmanName));
            List<string> NamesBefore = Read.UniqueParksByState("AZ", ConfigPath);

            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker, AzFacilityName: "NEW AZ FACILITY"))), Is.True, Output.ToString());

            List<string> NamesAfter = Read.UniqueParksByState("AZ", ConfigPath);
            Assert.That(NamesBefore, Does.Not.Contain("NEW AZ FACILITY"));
            Assert.That(NamesAfter, Does.Contain("NEW AZ FACILITY"));
        }

        [Test]
        public void Refresh_LeavesNoHandleOnTheStagingDatabase()
        {
            if (!Directory.Exists("/proc/self/fd"))
            {
                Assert.Ignore("needs /proc/self/fd (Linux)");
            }
            InstallPrevious();
            Assert.That(Refresh(WriteFixture(ValidFixture(NewMarker))), Is.True, Output.ToString());
            Assert.That(StagingHandles(), Is.Empty);
            Assert.That(Refresh(WriteFixture(RidbFixture.Create())), Is.False);
            Assert.That(StagingHandles(), Is.Empty);
            Assert.That(() => Refresh(WriteFixture(MalformedOnPurpose())), Throws.Exception);
            Assert.That(StagingHandles(), Is.Empty);
            Assert.That(StagingDirectories(), Is.Empty);
        }

        // ---- helpers ----

        private sealed class Snapshot
        {
            public string Hash = string.Empty;
            public string Dump = string.Empty;
            public string Yaml = string.Empty;
            public List<string> Cache = new List<string>();
        }

        private Snapshot TakeSnapshot()
        {
            SqliteConnection.ClearAllPools();
            return new Snapshot
            {
                Hash = Sha256(InstalledDb),
                Dump = File.Exists(InstalledDb) && Marker(InstalledDb) != null ? Dump(InstalledDb) : string.Empty,
                Yaml = File.ReadAllText(GeneralYaml),
                Cache = Directory.Exists(CacheDirectory) ? Directory.GetFiles(CacheDirectory).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList() : new List<string>()
            };
        }

        private void AssertNothingChanged(Snapshot Before)
        {
            SqliteConnection.ClearAllPools();
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Before.Hash), "installed database bytes");
            if (Before.Dump.Length > 0)
            {
                Assert.That(Dump(InstalledDb), Is.EqualTo(Before.Dump), "installed database contents");
            }
            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(Before.Yaml), "General.yaml");
            AssertTimestampUnchanged();
            Assert.That(TakeSnapshot().Cache, Is.EqualTo(Before.Cache), "derived cache");
            Assert.That(StagingDirectories(), Is.Empty, "staging directory removed");
        }

        private void AssertTimestampUnchanged()
        {
            Assert.That(Config.GeneralConfig!.LastRidbDataRefresh, Is.EqualTo(PreviousRefresh));
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(PreviousRefresh));
        }

        private bool Refresh(string RidbDirectory) =>
            RefreshRidbRecreationData.RefreshData(false, Config, () => RidbDirectory, CacheDirectory);

        private void InstallPrevious() => File.Copy(PreviousDb, InstalledDb);

        private List<string> StagingDirectories() =>
            Directory.GetFileSystemEntries(ConfigPath).Select(Path.GetFileName).Where(n => RidbStagedRefresh.IsStagingDirectoryName(n!)).Select(n => n!).ToList();

        private static List<string> StagingHandles() =>
            Directory.GetFiles("/proc/self/fd").Select(f => { try { return new FileInfo(f).LinkTarget ?? ""; } catch { return ""; } })
                .Where(t => t.Contains(RidbStagedRefresh.StagingDirectoryPrefix, StringComparison.Ordinal)).ToList();

        // RidbFixture with WATCHMAN CAMPGROUND (Keys[3]: UT / SPRINGDALE, reservable, enabled), so it passes
        // RidbDatabase.Validate, and a marker name on the first facility
        private static RidbFixture ValidFixture(string Marker, string? AzFacilityName = null)
        {
            RidbFixture Fixture = RidbFixture.Create();
            Fixture.Facilities[3].FacilityName = RidbDatabase.WatchmanName;
            Fixture.Facilities[0].FacilityName = Marker;
            if (AzFacilityName != null)
            {
                // Keys[9] ("Aa2"): AddressStateCode "AZ", reservable, enabled
                Fixture.Facilities[9].FacilityName = AzFacilityName;
            }
            return Fixture;
        }

        private static RidbFixture MalformedOnPurpose()
        {
            RidbFixture Fixture = ValidFixture(NewMarker);
            Fixture.Campsites.Add(Fixture.Campsites[0]);
            return Fixture;
        }

        private string WriteFixture(RidbFixture Fixture) => Fixture.Write(NewTempDirectory("ridb-", Root));

        private static void RewriteRecData(string RidbDirectory, string Name, Action<JsonArray> Change)
        {
            string File_ = Path.Join(RidbDirectory, Name + "_API_v1.json");
            JsonNode Root_ = JsonNode.Parse(File.ReadAllText(File_))!;
            Change(Root_["RECDATA"]!.AsArray());
            File.WriteAllText(File_, Root_.ToJsonString());
        }

        // the in-place refresh the staged refresh replaces: clear the database, then populate it
        private string BuildInPlace(string SourceDb, RidbFixture Fixture, string Name)
        {
            string Directory_ = NewTempDirectory(Name + "-", SharedDirectory);
            string DbFile = Path.Join(Directory_, "RecreationDotOrg.db");
            File.Copy(SourceDb, DbFile);
            Clear.ClearDatabase(Directory_);
            PopulateDbFromRIDBFiles.Populate(Fixture.Write(NewTempDirectory(Name + "-ridb-", SharedDirectory)), Directory_);
            SqliteConnection.ClearAllPools();
            Checkpoint(DbFile);
            return DbFile;
        }

        private static string NewTempDirectory(string Prefix, string? Parent = null)
        {
            string Directory_ = Path.Join(Parent ?? Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Directory_);
            return Directory_;
        }

        private static void DeleteTree(string Directory_)
        {
            if (string.IsNullOrEmpty(Directory_) || !Directory.Exists(Directory_))
            {
                return;
            }
            foreach (string Entry in Directory.GetFileSystemEntries(Directory_, "*", SearchOption.AllDirectories))
            {
                try { if (new FileInfo(Entry).LinkTarget == null) SetMode(Entry, GetMode(Entry) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
            }
            Directory.Delete(Directory_, true);
        }

        private static UnixFileMode GetMode(string Path_)
        {
            return OperatingSystem.IsWindows() ? UnixFileMode.None : File.GetUnixFileMode(Path_);
        }

        private static void SetMode(string Path_, UnixFileMode Mode)
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(Path_, Mode);
            }
        }

        private static void RequireUnixPermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("uses Unix file permissions");
            }
            if (Environment.IsPrivilegedProcess)
            {
                Assert.Ignore("permission checks do not apply to a privileged process");
            }
        }

        private static string Sha256(string File_)
        {
            using (FileStream Stream = new FileStream(File_, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return System.Convert.ToHexString(SHA256.HashData(Stream));
            }
        }

        private static SqliteConnection OpenUnpooled(string DbFile, SqliteOpenMode Mode)
        {
            var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = Mode, Pooling = false }.ToString());
            Connection.Open();
            return Connection;
        }

        private static bool Checkpoint(string DbFile)
        {
            using (SqliteConnection Connection = OpenUnpooled(DbFile, SqliteOpenMode.ReadWrite))
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                using (SqliteDataReader Reader = Command.ExecuteReader())
                {
                    return Reader.Read() && Reader.GetInt64(0) == 0;
                }
            }
        }

        private static string QuickCheck(string DbFile)
        {
            using (SqliteConnection Connection = OpenUnpooled(DbFile, SqliteOpenMode.ReadOnly))
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.CommandText = "PRAGMA quick_check";
                return System.Convert.ToString(Command.ExecuteScalar()) ?? "";
            }
        }

        private static string? Marker(string DbFile)
        {
            try
            {
                using (SqliteConnection Connection = OpenUnpooled(DbFile, SqliteOpenMode.ReadOnly))
                {
                    return Marker(Connection, null);
                }
            }
            catch (SqliteException)
            {
                return null;
            }
        }

        private static string? Marker(SqliteConnection Connection, SqliteTransaction? Transaction)
        {
            using (SqliteCommand Command = Connection.CreateCommand())
            {
                Command.Transaction = Transaction;
                Command.CommandText = "SELECT FacilityName FROM FacilitiesEntries WHERE FacilityID = 'AP21'";
                return System.Convert.ToString(Command.ExecuteScalar());
            }
        }

        // schema and every row of every table, in rowid order
        private static string Dump(string DbFile)
        {
            var Lines = new List<string>();
            using (SqliteConnection Connection = OpenUnpooled(DbFile, SqliteOpenMode.ReadOnly))
            {
                var Tables = new List<string>();
                using (SqliteCommand Command = Connection.CreateCommand())
                {
                    Command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name";
                    using (SqliteDataReader Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read())
                        {
                            Lines.Add(string.Join("|", Enumerable.Range(0, 4).Select(i => Reader.IsDBNull(i) ? "null" : Reader.GetString(i))));
                            if (Reader.GetString(0) == "table") Tables.Add(Reader.GetString(1));
                        }
                    }
                }
                foreach (string Table in Tables)
                {
                    using (SqliteCommand Command = Connection.CreateCommand())
                    {
                        Command.CommandText = $"SELECT rowid, * FROM \"{Table}\" ORDER BY rowid";
                        using (SqliteDataReader Reader = Command.ExecuteReader())
                        {
                            while (Reader.Read())
                            {
                                Lines.Add(Table + ":" + string.Join("|", Enumerable.Range(0, Reader.FieldCount).Select(i => Reader.IsDBNull(i) ? "null" : Reader.GetDataTypeName(i) + "=" + System.Convert.ToString(Reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
                            }
                        }
                    }
                }
            }
            return string.Join("\n", Lines);
        }
    }
}
