using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using CampertronUnitTests.function.Base;
using CampertronUnitTests.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // The host-neutral RIDB refresh service (V011-1, decision D61): the console's refresh steps with typed progress,
    // cancellation and a typed result, nothing written to the console, a download folder of its own (TD-58) and a stalled
    // download that ends. Every test has its own settings, cache and work folders; the export comes from an in-memory HTTP
    // handler that serves a ZIP of the RIDB fixture (PopulateTests' RidbFixture), so no test reaches the network
    // (NoLiveNetwork) or LocalApplicationData, except the TD-58 tests, which point LocalApplicationData at a folder of
    // their own. A refresh that does not install must leave the installed database byte for byte, the cache, General.yaml
    // and lastRidbDataRefresh as they were, and none of its own files behind.
    public class RidbRefreshServiceTests
    {
        private static readonly DateTime PreviousRefresh = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        private static readonly DateTime RefreshTime = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        private const string OldMarker = "OLD FACILITY";
        private const string NewMarker = "NEW FACILITY";

        private string SharedDirectory = string.Empty;
        private string PreviousDb = string.Empty;
        private byte[] ValidExport = Array.Empty<byte>();

        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "CampertronConfig");
        private string CacheDirectory => Path.Join(Root, "CampertronCache");
        private string WorkRoot => Path.Join(Root, "work");
        private string InstalledDb => Path.Join(ConfigPath, "RecreationDotOrg.db");
        private string GeneralYaml => Path.Join(ConfigPath, "General.yaml");
        private string? OriginalXdgDataHome;
        private FakeExport Export = null!;

        [OneTimeSetUp]
        public void CreateDatabases()
        {
            SharedDirectory = NewTempDirectory("CampertronRefreshServiceShared-");
            string TemplateDirectory = Path.Join(SharedDirectory, "template");
            Directory.CreateDirectory(TemplateDirectory);
            string Emptied = Path.Join(TemplateDirectory, "RecreationDotOrg.db");
            Load.ExtractEmbeddedDbTo(Emptied);
            Clear.ClearDatabase(TemplateDirectory);
            SqliteConnection.ClearAllPools();
            string PreviousDirectory = Path.Join(SharedDirectory, "previous");
            Directory.CreateDirectory(PreviousDirectory);
            PreviousDb = Path.Join(PreviousDirectory, "RecreationDotOrg.db");
            File.Copy(Emptied, PreviousDb);
            PopulateDbFromRIDBFiles.Populate(ValidFixture(OldMarker).Write(NewTempDirectory("previous-ridb-", SharedDirectory)), PreviousDirectory);
            SqliteConnection.ClearAllPools();
            RidbIndexes.Create(PreviousDb);
            Checkpoint(PreviousDb);
            Assert.That(RidbDatabase.Validate(PreviousDirectory), Is.Empty);
            ValidExport = Zip(ValidFixture(NewMarker));
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
            Root = NewTempDirectory("CampertronRefreshServiceTests-");
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(WorkRoot);
            Yaml.GeneralConfigConvertToYaml(new GeneralConfig { OutputTo = OutputType.UnitTest, AutoRefresh = false, RefreshRidbDataDayInterval = 30, LastRidbDataRefresh = PreviousRefresh },
                "General", ConfigPath);
            File.WriteAllText(Path.Join(CacheDirectory, "232445-Campsites.json"), "[]");
            File.WriteAllText(Path.Join(CacheDirectory, "abc.html"), "<html></html>");
            File.Copy(PreviousDb, InstalledDb);
            Export = new FakeExport(ValidExport);
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
        }

        [TearDown]
        public void TearDown()
        {
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 30000;
            RidbStagedRefresh.QuickCheck = DefaultQuickCheck;
            Export.Dispose();
            if (OriginalXdgDataHome != null)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", OriginalXdgDataHome.Length > 0 ? OriginalXdgDataHome : null);
                OriginalXdgDataHome = null;
            }
            SqliteConnection.ClearAllPools();
            DeleteTree(Root);
        }

        private static readonly Func<string, string> DefaultQuickCheck = RidbStagedRefresh.QuickCheck;

        // ---- success ----

        [Test]
        public void Success_DownloadsExtractsAndInstalls_ThenRecordsTheRefresh_AndLeavesNothingBehind()
        {
            var Log = new List<string>();
            RidbRefreshResult Result = Run(Log: Log);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Result.Succeeded && Result.Installed, Is.True);
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.Completed));
            Assert.That(Result.RefreshedAt, Is.EqualTo(RefreshTime));
            Assert.That(Result.Error, Is.Null);
            Assert.That(Result.CancelRequested, Is.False);
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            Assert.That(QuickCheck(InstalledDb), Is.EqualTo("ok"));
            Assert.That(RidbDatabase.Validate(ConfigPath), Is.Empty);
            Assert.That(RidbIndexes.Exists(InstalledDb), Is.True);
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty, "the derived cache is cleared");
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(RefreshTime));
            Assert.That(Export.Requests, Is.EqualTo(1));
            Assert.That(Export.Urls.Single().AbsoluteUri, Is.EqualTo("https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip"));
            AssertNoLeftovers();
            AssertLockFree();
        }

        [Test]
        public void Log_HasTheConsoleRefreshLines_InTheirOrder()
        {
            var Log = new List<string>();
            RidbRefreshResult Result = Run(Log: Log);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            string Archive = Log[1].Substring("Extracting ".Length);
            Assert.That(Path.GetFileName(Archive), Is.EqualTo("RIDBFullExport_V1_JSON.zip"));
            Assert.That(Log, Is.EqualTo(new[]
            {
                "Downloading RIDBFullExport_V1_JSON.zip",
                Log[1],
                "Preparing a staging copy of the database",
                "Clearing Database",
                "Clearing database tables",
                "Populating Database from " + Path.Join(WorkRoot, "CampertronExtract"),
                "Processing CampsiteAttributes_API_v1.json",
                "Processing Campsites_API_v1.json",
                "Processing Facilities_API_v1.json",
                "Processing FacilityAddresses_API_v1.json",
                "Processing PermittedEquipment_API_v1.json",
                "Processing RecAreas_API_v1.json",
                "Saving changes to DB...Please wait",
                "Indexing the campsite attributes",
                "Validating database",
                "Clearing cache",
                "Installing the refreshed database",
                "Installed the refreshed database",
            }));
        }

        [Test]
        public void Log_PutsTheArchiveInAFolderOfItsOwn()
        {
            var Log = new List<string>();
            Run(Log: Log);

            string Archive = Log.Single(l => l.StartsWith("Extracting ", StringComparison.Ordinal)).Substring("Extracting ".Length);
            string Folder = Path.GetFileName(Path.GetDirectoryName(Archive)!);
            Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(Archive)), Is.EqualTo(WorkRoot));
            Assert.That(RidbRefreshService.IsDownloadFolderName(Folder), Is.True, Folder);
            Assert.That(File.Exists(Archive) || Directory.Exists(Path.GetDirectoryName(Archive)), Is.False, "removed afterwards");
        }

        [Test]
        public void Progress_ReportsEveryStageInOrder_WithCountsAndTheLastPointToCancel()
        {
            var Reports = new List<RidbRefreshProgress>();
            RidbRefreshResult Result = Run(Progress: new Synchronous(Reports.Add));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Reports.Select(r => r.Sequence), Is.Ordered.Ascending.And.Unique);
            List<RidbRefreshStage> Stages = Reports.Select(r => r.Stage).Distinct().ToList();
            Assert.That(Stages, Is.EqualTo(Enum.GetValues<RidbRefreshStage>()), "every stage, once, in the enum's order");
            Assert.That(Reports.Where(r => r.Stage < RidbRefreshStage.ClearingCache).All(r => r.CanCancel), Is.True);
            Assert.That(Reports.Where(r => r.Stage >= RidbRefreshStage.ClearingCache).All(r => !r.CanCancel), Is.True);
            // the bytes, with the length the server announced
            List<RidbRefreshProgress> Bytes = Reports.Where(r => r.Stage == RidbRefreshStage.Downloading && r.Done != null).ToList();
            Assert.That(Bytes.Select(r => r.Done), Is.Ordered.Ascending);
            Assert.That(Bytes.Last().Done, Is.EqualTo(ValidExport.Length));
            Assert.That(Bytes.All(r => r.Total == ValidExport.Length), Is.True);
            // the six files, as ZIP entries and as the import reads them
            List<RidbRefreshProgress> Entries = Reports.Where(r => r.Stage == RidbRefreshStage.Extracting && r.Done != null).ToList();
            Assert.That(Entries.Select(r => r.Done), Is.EqualTo(new long?[] { 0, 1, 2, 3, 4, 5, 6 }));
            Assert.That(Entries.All(r => r.Total == 6), Is.True);
            List<RidbRefreshProgress> Files = Reports.Where(r => r.Stage == RidbRefreshStage.Importing && r.Done != null).ToList();
            Assert.That(Files.Select(r => r.Done), Is.EqualTo(new long?[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(Files.All(r => r.Total == PopulateDbFromRIDBFiles.FileCount), Is.True);
            // the other stages count nothing
            Assert.That(Reports.Where(r => r.Stage != RidbRefreshStage.Downloading && r.Stage != RidbRefreshStage.Extracting && r.Stage != RidbRefreshStage.Importing)
                .All(r => r.Done == null && r.Total == null), Is.True);
        }

        [Test]
        public void Progress_WithoutALengthFromTheServer_HasNoTotal()
        {
            Export.SendLength = false;
            var Reports = new List<RidbRefreshProgress>();
            RidbRefreshResult Result = Run(Progress: new Synchronous(Reports.Add));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            List<RidbRefreshProgress> Bytes = Reports.Where(r => r.Stage == RidbRefreshStage.Downloading && r.Done != null).ToList();
            Assert.That(Bytes.All(r => r.Total == null), Is.True);
            Assert.That(Bytes.Last().Done, Is.EqualTo(ValidExport.Length));
        }

        [Test]
        public void TheService_WritesNothingToTheConsole_AndNeverWaitsForAKey()
        {
            TextWriter Out = Console.Out, Error = Console.Error;
            TextReader In = Console.In;
            var Captured = new StringWriter();
            try
            {
                Console.SetOut(Captured);
                Console.SetError(Captured);
                // a key wait would read standard input: it has nothing to give
                Console.SetIn(new StringReader(string.Empty));
                RidbRefreshResult Result = Run(Progress: new Synchronous(_ => { }));
                Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
                Assert.That(Run(Handler: new FakeExport(new byte[] { 1, 2, 3 })).Outcome, Is.EqualTo(RidbRefreshOutcome.ExtractionFailed));
            }
            finally
            {
                Console.SetOut(Out);
                Console.SetError(Error);
                Console.SetIn(In);
            }
            Assert.That(Captured.ToString(), Is.Empty);
        }

        // From its IL (with its lambdas and nested types): the service and its reporter never write to the console, wait
        // for a key, end the process, start one or use a GUI; the lines go only through Options.Log.
        [Test]
        public void TheService_HasNoConsoleKeyWaitExitOrGuiDependency()
        {
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            IEnumerable<Type> WithNested(Type T) => new[] { T }.Concat(T.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public).SelectMany(WithNested));
            List<string> Called = new[] { typeof(RidbRefreshService), typeof(RidbRefreshReporter) }.SelectMany(WithNested)
                .SelectMany(T => T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                .SelectMany(M => YamlDocumentPreservationTests.ReferencedMembers(M))
                .Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).Distinct().ToList();

            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.sqlite.RidbStagedRefresh::BuildValidateAndInstall"), "the scan sees the steps");
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.Web::Download"), "the scan sees the download");
            foreach (string Bad in new[] { "System.Console::", "ConsoleUi::", "System.Environment::Exit", "System.Environment::FailFast", "System.Diagnostics.Process::", "Avalonia", "Menu::" })
            {
                Assert.That(Called.Where(C => C.Contains(Bad, StringComparison.Ordinal)), Is.Empty, Bad);
            }
            Assert.That(typeof(RidbRefreshService).Assembly.GetReferencedAssemblies().Select(a => a.Name), Has.None.StartsWith("Avalonia"));
        }

        // ---- the lock ----

        [Test]
        public void AnotherRefreshHoldsTheLock_NothingIsDownloadedOrChanged()
        {
            Snapshot Before = TakeSnapshot();
            RidbRefreshResult Result;
            using (FileStream? Held = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out _))
            {
                Assert.That(Held, Is.Not.Null);
                Result = Run();
            }

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.AnotherRefreshRunning));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.AcquiringLock));
            Assert.That(Result.Detail, Is.Not.Empty);
            Assert.That(Result.Error, Is.Null);
            Assert.That(Export.Requests, Is.Zero);
            AssertNothingChanged(Before);
        }

        [Test]
        public void TheLock_IsReleasedAfterEveryEnd()
        {
            Assert.That(Run().Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed));
            AssertLockFree();
            Assert.That(Run(Handler: new FakeExport(ValidExport) { Status = HttpStatusCode.NotFound }).Outcome, Is.EqualTo(RidbRefreshOutcome.DownloadFailed));
            AssertLockFree();
            using var Cancel = new CancellationTokenSource();
            Assert.That(Run(Handler: new FakeExport(ValidExport), Progress: CancelAt(Cancel, RidbRefreshStage.Importing), Token: Cancel.Token).Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled));
            AssertLockFree();
            Assert.That(File.Exists(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName)), Is.True, "the lock file is never deleted");
        }

        // ---- what an interrupted refresh left ----

        [Test]
        public void Leftovers_OfAnInterruptedRefresh_AreRemoved_AndNothingThatOnlyLooksLikeThem()
        {
            string Id = Guid.NewGuid().ToString("N");
            string StaleStaging = Path.Join(ConfigPath, RidbStagedRefresh.StagingDirectoryPrefix + Id);
            string StaleDownload = Path.Join(WorkRoot, RidbRefreshService.DownloadFolderPrefix + Id);
            Directory.CreateDirectory(StaleStaging);
            Directory.CreateDirectory(StaleDownload);
            File.WriteAllText(Path.Join(StaleDownload, "RIDBFullExport_V1_JSON.zip"), "partial");
            string[] LookAlikes =
            {
                RidbRefreshService.DownloadFolderPrefix + Id.ToUpperInvariant(), RidbRefreshService.DownloadFolderPrefix + Guid.NewGuid().ToString("D"),
                RidbRefreshService.DownloadFolderPrefix + Id + "x", "X" + RidbRefreshService.DownloadFolderPrefix + Id, "CampertronDownload", "Extract",
            };
            foreach (string Name in LookAlikes)
            {
                Directory.CreateDirectory(Path.Join(WorkRoot, Name));
                File.WriteAllText(Path.Join(WorkRoot, Name, "keep.txt"), "keep");
            }
            var Log = new List<string>();

            Assert.That(Run(Log: Log).Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed));

            Assert.That(Directory.Exists(StaleStaging), Is.False);
            Assert.That(Directory.Exists(StaleDownload), Is.False);
            Assert.That(Log, Does.Contain("Removed " + StaleDownload + ", left by an interrupted RIDB refresh"));
            Assert.That(Log, Does.Contain("Removed " + StaleStaging + ", left by an interrupted RIDB refresh"));
            foreach (string Name in LookAlikes)
            {
                Assert.That(File.ReadAllText(Path.Join(WorkRoot, Name, "keep.txt")), Is.EqualTo("keep"), Name);
            }
        }

        [Test]
        public void IsDownloadFolderName_AcceptsOnlyTheExactConvention()
        {
            string Id = Guid.NewGuid().ToString("N");
            Assert.That(RidbRefreshService.IsDownloadFolderName("CampertronDownload-" + Id), Is.True);
            Assert.That(RidbRefreshService.IsDownloadFolderName("CampertronDownload-" + Id.ToUpperInvariant()), Is.False);
            Assert.That(RidbRefreshService.IsDownloadFolderName("CampertronDownload-" + Guid.NewGuid().ToString("D")), Is.False);
            Assert.That(RidbRefreshService.IsDownloadFolderName("CampertronDownload-" + Id + "0"), Is.False);
            Assert.That(RidbRefreshService.IsDownloadFolderName("CampertronDownload-" + new string('g', 32)), Is.False);
            Assert.That(RidbRefreshService.IsDownloadFolderName("campertrondownload-" + Id), Is.False);
            Assert.That(RidbRefreshService.IsDownloadFolderName("RIDBFullExport_V1_JSON.zip"), Is.False);
        }

        // ---- TD-58: the archive's name is never Campertron's to delete ----

        [Test]
        public void TD58_AFileNamedLikeTheExportInLocalApplicationData_IsNeverTouched()
        {
            string LocalAppData = RedirectLocalApplicationData();
            string Foreign = Path.Join(LocalAppData, "RIDBFullExport_V1_JSON.zip");
            File.WriteAllText(Foreign, "a RIDB export the user downloaded by hand");
            File.SetLastWriteTimeUtc(Foreign, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            string Hash = Sha256(Foreign);
            var Log = new List<string>();

            RidbRefreshResult Result = Run(Log: Log, DefaultWorkRoot: true);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Sha256(Foreign), Is.EqualTo(Hash));
            Assert.That(File.GetLastWriteTimeUtc(Foreign), Is.EqualTo(new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            string Archive = Log.Single(l => l.StartsWith("Extracting ", StringComparison.Ordinal)).Substring("Extracting ".Length);
            Assert.That(Archive, Is.Not.EqualTo(Foreign));
            Assert.That(RidbRefreshService.IsDownloadFolderName(Path.GetFileName(Path.GetDirectoryName(Archive)!)), Is.True);
            Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(Archive)), Is.EqualTo(LocalAppData));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData).Select(Path.GetFileName), Is.EquivalentTo(new[] { "RIDBFullExport_V1_JSON.zip" }),
                "no download folder or extraction left in LocalApplicationData");
        }

        [Test]
        public void TD58_AFailedDownload_LeavesAFileNamedLikeTheExportAlone()
        {
            string LocalAppData = RedirectLocalApplicationData();
            string Foreign = Path.Join(LocalAppData, "RIDBFullExport_V1_JSON.zip");
            File.WriteAllText(Foreign, "a RIDB export the user downloaded by hand");
            string Hash = Sha256(Foreign);

            RidbRefreshResult Result = Run(Handler: new FakeExport(ValidExport) { FailAfter = ValidExport.Length / 2 }, DefaultWorkRoot: true);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.DownloadFailed), Describe(Result));
            Assert.That(Sha256(Foreign), Is.EqualTo(Hash));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData).Select(Path.GetFileName), Is.EquivalentTo(new[] { "RIDBFullExport_V1_JSON.zip" }));
        }

        [Test]
        public void TheConsoleRefresh_NeverTouchesAFileNamedLikeTheExport()
        {
            string LocalAppData = RedirectLocalApplicationData();
            string Foreign = Path.Join(LocalAppData, "RIDBFullExport_V1_JSON.zip");
            File.WriteAllText(Foreign, "a RIDB export the user downloaded by hand");
            string Hash = Sha256(Foreign);
            var Config = ConsoleConfig();

            string Output = CaptureConsole(() => Assert.That(RefreshRidbRecreationData.RefreshData(false, Config, new RidbRefreshOptions { Handler = Export }), Is.True));

            Assert.That(Sha256(Foreign), Is.EqualTo(Hash));
            Assert.That(Output, Does.Contain("Extracting " + LocalAppData + Path.DirectorySeparatorChar + RidbRefreshService.DownloadFolderPrefix));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData).Select(Path.GetFileName), Is.EquivalentTo(new[] { "RIDBFullExport_V1_JSON.zip" }));
        }

        // ---- the container: the download and the extraction in the mounted cache folder (D85 point 5, v0.3.0's PR 9) ----

        [Test]
        public void DefaultWorkRoot_IsTheCacheFolderInTheContainer_AndLocalApplicationDataElsewhere()
        {
            Assert.That(RidbRefreshService.DefaultWorkRoot(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), CacheDirectory), Is.EqualTo(CacheDirectory));
            Assert.That(RidbRefreshService.DefaultWorkRoot(new RidbRefreshRequest(ConfigPath, CacheDirectory), CacheDirectory),
                Is.EqualTo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
            Assert.That(new RidbRefreshRequest(ConfigPath, CacheDirectory).InContainer, Is.False, "outside the container unless said");
        }

        [Test]
        public void InTheContainer_TheDownloadAndTheExtraction_AreInTheCacheFolder_AndNothingElseOfItIsTouched()
        {
            string LocalAppData = RedirectLocalApplicationData();
            //what the cache folder holds besides the campground's files: a folder of the user's, one that only looks like a
            //download folder, and one named like the extraction in another letter case
            string[] Others = { "photos", RidbRefreshService.DownloadFolderPrefix + "notaguid", "campertronextract" };
            foreach (string Name in Others)
            {
                Directory.CreateDirectory(Path.Join(CacheDirectory, Name));
                File.WriteAllText(Path.Join(CacheDirectory, Name, "keep.txt"), Name);
            }
            var Seen = new List<string>();
            var Log = new List<string>();
            var Progress = new Synchronous(P =>
            {
                if (P.Stage == RidbRefreshStage.PreparingDatabase && Seen.Count == 0)
                {
                    Seen.AddRange(Directory.GetDirectories(CacheDirectory).Select(Path.GetFileName).Select(n => n!));
                }
            });

            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), Progress, CancellationToken.None,
                Options(Log: Log, UseWorkRoot: false));
            SqliteConnection.ClearAllPools();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            string Archive = Log.Single(l => l.StartsWith("Extracting ", StringComparison.Ordinal)).Substring("Extracting ".Length);
            Assert.That(Path.GetDirectoryName(Path.GetDirectoryName(Archive)), Is.EqualTo(CacheDirectory), "the archive in a folder of its own in the cache folder");
            Assert.That(RidbRefreshService.IsDownloadFolderName(Path.GetFileName(Path.GetDirectoryName(Archive))!), Is.True);
            Assert.That(Log, Does.Contain("Populating Database from " + Path.Join(CacheDirectory, "CampertronExtract")));
            Assert.That(Seen, Does.Contain("CampertronExtract"), "extracted into the cache folder");
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty, "nothing in the container's own storage");
            foreach (string Name in Others)
            {
                Assert.That(File.ReadAllText(Path.Join(CacheDirectory, Name, "keep.txt")), Is.EqualTo(Name), Name);
            }
            Assert.That(Directory.GetDirectories(CacheDirectory).Select(Path.GetFileName), Is.EquivalentTo(Others), "no download folder or extraction left");
            Assert.That(Directory.GetFiles(CacheDirectory), Is.Empty, "the derived cache is cleared, as everywhere");
            Assert.That(StagingDirectories(), Is.Empty, "the staging database stays in the settings folder and is removed");
            Assert.That(Directory.GetFileSystemEntries(WorkRoot), Is.Empty, "the tests' work folder is not used");
        }

        [Test]
        public void InTheContainer_AFailedDownload_LeavesNothingInTheCacheFolder_AndTheDatabaseAsItWas()
        {
            Snapshot Before = TakeSnapshot();
            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), null, CancellationToken.None,
                Options(Handler: new FakeExport(ValidExport) { FailAfter = ValidExport.Length / 2 }, UseWorkRoot: false));
            SqliteConnection.ClearAllPools();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.DownloadFailed), Describe(Result));
            AssertNothingChanged(Before);
            Assert.That(Directory.GetDirectories(CacheDirectory), Is.Empty, "the download folder is removed");
            AssertLockFree();
        }

        [Test]
        public void InTheContainer_ALeftoverOfAnInterruptedRefresh_InTheCacheFolder_IsRemoved_AndNothingThatOnlyLooksLikeIt()
        {
            string Id = Guid.NewGuid().ToString("N");
            string Stale = Path.Join(CacheDirectory, RidbRefreshService.DownloadFolderPrefix + Id);
            Directory.CreateDirectory(Stale);
            File.WriteAllText(Path.Join(Stale, "RIDBFullExport_V1_JSON.zip"), "partial");
            string LookAlike = Path.Join(CacheDirectory, RidbRefreshService.DownloadFolderPrefix + Id.ToUpperInvariant());
            Directory.CreateDirectory(LookAlike);
            File.WriteAllText(Path.Join(LookAlike, "keep.txt"), "keep");
            var Log = new List<string>();

            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), null, CancellationToken.None,
                Options(Log: Log, UseWorkRoot: false));
            SqliteConnection.ClearAllPools();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Directory.Exists(Stale), Is.False);
            Assert.That(Log, Does.Contain("Removed " + Stale + ", left by an interrupted RIDB refresh"));
            Assert.That(File.ReadAllText(Path.Join(LookAlike, "keep.txt")), Is.EqualTo("keep"));
        }

        [Test]
        public void InTheContainer_LinksNamedLikeItsFolders_InTheCacheFolder_AreRemovedAsLinks_NeverFollowed()
        {
            RequireUnixPermissions();
            string Outside = Path.Join(Root, "outside");
            Directory.CreateDirectory(Outside);
            File.WriteAllText(Path.Join(Outside, "keep.txt"), "keep");
            string Download = Path.Join(CacheDirectory, RidbRefreshService.DownloadFolderPrefix + Guid.NewGuid().ToString("N"));
            Directory.CreateSymbolicLink(Download, Outside);
            Directory.CreateSymbolicLink(Path.Join(CacheDirectory, "CampertronExtract"), Outside);

            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), null, CancellationToken.None,
                Options(UseWorkRoot: false));
            SqliteConnection.ClearAllPools();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Directory.GetFileSystemEntries(Outside).Select(Path.GetFileName), Is.EqualTo(new[] { "keep.txt" }), "what the links pointed at is untouched");
            Assert.That(File.ReadAllText(Path.Join(Outside, "keep.txt")), Is.EqualTo("keep"));
            Assert.That(Directory.GetFileSystemEntries(CacheDirectory), Is.Empty, "the links are gone, nothing is left");
        }

        [Test]
        public void InTheContainer_ACacheFolderThatCantBeWritten_IsStorageFailed_BeforeAnyDownload_AndChangesNothing()
        {
            RequireUnixPermissions();
            if (!OperatingSystem.IsWindows())
            {
                Snapshot Before = TakeSnapshot();
                File.SetUnixFileMode(CacheDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                RidbRefreshResult Result;
                try
                {
                    Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InContainer: true), null, CancellationToken.None, Options(UseWorkRoot: false));
                    SqliteConnection.ClearAllPools();
                }
                finally
                {
                    File.SetUnixFileMode(CacheDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
                Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.StorageFailed), Describe(Result));
                Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.Preparing));
                Assert.That(Result.Installed, Is.False);
                Assert.That(Export.Requests, Is.Zero, "nothing was downloaded");
                AssertNothingChanged(Before);
                AssertLockFree();
            }
        }

        [Test]
        public void TheConsole_InTheContainer_DownloadsIntoTheCacheFolder()
        {
            string LocalAppData = RedirectLocalApplicationData();
            CampertronInternalConfig Config = ConsoleConfig();
            Config.ContainerMode = true;

            string Output = CaptureConsole(() => Assert.That(RefreshRidbRecreationData.RefreshData(false, Config, new RidbRefreshOptions { Handler = Export }), Is.True));

            Assert.That(Output, Does.Contain("Extracting " + CacheDirectory + Path.DirectorySeparatorChar + RidbRefreshService.DownloadFolderPrefix));
            Assert.That(Output, Does.Contain("Populating Database from " + Path.Join(CacheDirectory, "CampertronExtract")));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(CacheDirectory), Is.Empty);
        }

        // ---- failures before the installation: typed, nothing changed, nothing left ----

        [Test]
        public void HttpStatusFailure_IsDownloadFailed()
        {
            AssertFailsBeforeInstalling(new FakeExport(ValidExport) { Status = HttpStatusCode.NotFound }, RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading,
                Error => Assert.That(Error, Is.InstanceOf<HttpRequestException>()));
        }

        [Test]
        public void ConnectionFailure_IsDownloadFailed()
        {
            AssertFailsBeforeInstalling(new FakeExport(ValidExport) { ConnectFailure = new HttpRequestException("Connection refused (ridb.recreation.gov:443)") },
                RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading, Error => Assert.That(Error, Is.InstanceOf<HttpRequestException>()));
        }

        [Test]
        public void ABrokenTransfer_IsDownloadFailed_NotAStorageFailure()
        {
            AssertFailsBeforeInstalling(new FakeExport(ValidExport) { FailAfter = ValidExport.Length / 2 }, RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading,
                Error => Assert.That(Error, Is.InstanceOf<IOException>()));
        }

        [Test]
        public void AStalledDownload_EndsWithinTheStallTimeout_AsDownloadTimedOut()
        {
            var Stalled = new FakeExport(ValidExport) { StallAfter = ValidExport.Length / 2 };
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            AssertFailsBeforeInstalling(Stalled, RidbRefreshOutcome.DownloadTimedOut, RidbRefreshStage.Downloading,
                Error => Assert.That(Error, Is.InstanceOf<DownloadTimedOutException>().And.InstanceOf<HttpRequestException>()),
                Stall: TimeSpan.FromMilliseconds(300));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the stalled body ended (before V011-1 it waited forever)");
            Assert.That(Stalled.MaxPosition, Is.EqualTo(ValidExport.Length / 2), "it stalled where the server stopped sending");
        }

        [Test]
        public void ASilentServer_EndsAtTheHeaderTimeout_AsDownloadTimedOut()
        {
            var Silent = new FakeExport(ValidExport) { HeaderDelay = TimeSpan.FromMinutes(5) };
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            AssertFailsBeforeInstalling(Silent, RidbRefreshOutcome.DownloadTimedOut, RidbRefreshStage.Downloading,
                Error => Assert.That(Error, Is.InstanceOf<OperationCanceledException>()), Header: TimeSpan.FromMilliseconds(300));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void ADownloadThatIsNotAZip_IsExtractionFailed()
        {
            AssertFailsBeforeInstalling(new FakeExport(System.Text.Encoding.UTF8.GetBytes("<html>maintenance</html>")), RidbRefreshOutcome.ExtractionFailed, RidbRefreshStage.Extracting,
                Error => Assert.That(Error, Is.InstanceOf<InvalidDataException>()));
        }

        [Test]
        public void AnEntryOutsideTheExtractFolder_IsRefused_AndNothingIsWrittenThere()
        {
            byte[] Evil;
            using (var Stream = new MemoryStream())
            {
                using (var Archive = new ZipArchive(Stream, ZipArchiveMode.Create, true))
                {
                    using (var Writer = new StreamWriter(Archive.CreateEntry("../escaped.json").Open()))
                    {
                        Writer.Write("{}");
                    }
                }
                Evil = Stream.ToArray();
            }
            AssertFailsBeforeInstalling(new FakeExport(Evil), RidbRefreshOutcome.ExtractionFailed, RidbRefreshStage.Extracting,
                Error => Assert.That(Error, Is.InstanceOf<InvalidDataException>()));
            Assert.That(File.Exists(Path.Join(WorkRoot, "escaped.json")), Is.False);
            Assert.That(File.Exists(Path.Join(Root, "escaped.json")), Is.False);
        }

        [Test]
        public void MalformedJson_IsImportFailed()
        {
            string Ridb = ValidFixture(NewMarker).Write(NewTempDirectory("ridb-", Root));
            File.WriteAllText(Path.Join(Ridb, "Facilities_API_v1.json"), "{\"RECDATA\": [ {\"FacilityID\": ");
            AssertFailsBeforeInstalling(new FakeExport(ZipFolder(Ridb)), RidbRefreshOutcome.ImportFailed, RidbRefreshStage.Importing,
                Error => Assert.That(Error, Is.InstanceOf<System.Text.Json.JsonException>()));
        }

        [Test]
        public void AMissingRidbFile_IsImportFailed()
        {
            string Ridb = ValidFixture(NewMarker).Write(NewTempDirectory("ridb-", Root));
            File.Delete(Path.Join(Ridb, "RecAreas_API_v1.json"));
            AssertFailsBeforeInstalling(new FakeExport(ZipFolder(Ridb)), RidbRefreshOutcome.ImportFailed, RidbRefreshStage.Importing,
                Error => Assert.That(Error, Is.InstanceOf<FileNotFoundException>()));
        }

        [Test]
        public void ADuplicateKey_IsImportFailed()
        {
            RidbFixture Fixture = ValidFixture(NewMarker);
            Fixture.RecAreas.Add(Fixture.RecAreas[3]);
            AssertFailsBeforeInstalling(new FakeExport(Zip(Fixture)), RidbRefreshOutcome.ImportFailed, RidbRefreshStage.Saving,
                Error => Assert.That(Error, Is.InstanceOf<SqliteException>()));
        }

        [Test]
        public void ANullRow_IsImportFailed()
        {
            string Ridb = ValidFixture(NewMarker).Write(NewTempDirectory("ridb-", Root));
            string File_ = Path.Join(Ridb, "Campsites_API_v1.json");
            JsonNode Node = JsonNode.Parse(File.ReadAllText(File_))!;
            Node["RECDATA"]!.AsArray().Insert(2, null);
            File.WriteAllText(File_, Node.ToJsonString());
            AssertFailsBeforeInstalling(new FakeExport(ZipFolder(Ridb)), RidbRefreshOutcome.ImportFailed, RidbRefreshStage.Saving,
                Error => Assert.That(Error, Is.InstanceOf<InvalidDataException>()));
        }

        [Test]
        public void ValidationFailure_IsTyped_WithItsProblems_AndChangesNothing()
        {
            // the plain fixture has no Watchman campground, so RidbDatabase.Validate rejects the staging database
            Snapshot Before = TakeSnapshot();
            RidbRefreshResult Result = Run(Handler: new FakeExport(Zip(RidbFixture.Create())));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.ValidationFailed));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.Validating));
            Assert.That(Result.Problems, Has.Some.Contains("WATCHMAN CAMPGROUND is not listed"));
            Assert.That(Result.Error, Is.Null);
            Assert.That(Result.Installed, Is.False);
            AssertNothingChanged(Before);
            AssertNoLeftovers();
        }

        [Test]
        public void AWorkFolderThatCantBeWritten_IsStorageFailed()
        {
            RequireUnixPermissions();
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(WorkRoot, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                try
                {
                    AssertFailsBeforeInstalling(Export, RidbRefreshOutcome.StorageFailed, RidbRefreshStage.Preparing,
                        Error => Assert.That(Error, Is.InstanceOf<UnauthorizedAccessException>()), Requests: 0);
                }
                finally
                {
                    File.SetUnixFileMode(WorkRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }
            }
        }

        [Test]
        public void AnInstalledDatabaseThatIsNotOne_IsStorageFailed_AndIsLeftAsItIs()
        {
            File.WriteAllText(InstalledDb, "not a database");
            AssertFailsBeforeInstalling(Export, RidbRefreshOutcome.StorageFailed, RidbRefreshStage.PreparingDatabase,
                Error => Assert.That(Error, Is.InstanceOf<SqliteException>()), CheckDatabase: false);
            Assert.That(File.ReadAllText(InstalledDb), Is.EqualTo("not a database"));
        }

        [Test]
        public void ACacheFolderHoldingSettings_IsRefusedAtTheCacheStep_AsStorageFailed_WithTheDatabaseUnchanged()
        {
            // past the last point to cancel, but before the installation: the installed database is still the old one
            File.WriteAllText(Path.Join(CacheDirectory, "General.yaml"), "outputTo: Console\n");
            Snapshot Before = TakeSnapshot();
            RidbRefreshResult Result = Run();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.StorageFailed), Describe(Result));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.ClearingCache));
            Assert.That(Result.Error, Is.InstanceOf<InvalidOperationException>());
            AssertNothingChanged(Before);
            AssertNoLeftovers();
        }

        [Test]
        public void ACompetingWriter_MakesTheInstallationFail_AndTheDatabaseStaysAsItWas()
        {
            Snapshot Before = TakeSnapshot();
            RidbStagedRefresh.InstallBusyTimeoutMilliseconds = 300;
            RidbRefreshResult Result;
            using (SqliteConnection Writer = Open(InstalledDb, SqliteOpenMode.ReadWrite))
            using (SqliteTransaction Transaction = Writer.BeginTransaction())
            {
                using (SqliteCommand Command = Writer.CreateCommand())
                {
                    Command.Transaction = Transaction;
                    Command.CommandText = "UPDATE RecAreaEntries SET RecAreaName = RecAreaName WHERE rowid = 1";
                    Command.ExecuteNonQuery();
                }
                Result = Run();
                Transaction.Rollback();
            }

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.InstallationFailed), Describe(Result));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.Installing));
            Assert.That(Result.Installed, Is.False);
            Assert.That(((SqliteException)Result.Error!).SqliteErrorCode, Is.EqualTo(5), "SQLITE_BUSY");
            SqliteConnection.ClearAllPools();
            Checkpoint(InstalledDb);
            Assert.That(Marker(InstalledDb), Is.EqualTo(OldMarker));
            AssertTimestampUnchanged();
            AssertNoLeftovers();
        }

        // ---- after the installation ----

        [Test]
        public void AFailedQuickCheck_IsInstalledButCheckFailed_KeepsTheStagingDatabase_AndDoesNotRecordTheRefresh()
        {
            RidbStagedRefresh.QuickCheck = _ => "*** in database main *** Page 7: btreeInitPage() returns error code 11";
            RidbRefreshResult Result = Run();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.InstalledButCheckFailed), Describe(Result));
            Assert.That(Result.Installed, Is.True);
            Assert.That(Result.RefreshedAt, Is.Null);
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.CheckingDatabase));
            Assert.That(Result.Problems.Single(), Does.Contain("quick_check").And.Contain("btreeInitPage"));
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker), "installed");
            AssertTimestampUnchanged();
            Assert.That(StagingDirectories(), Has.Count.EqualTo(1), "kept for inspection");
            Assert.That(Directory.GetFileSystemEntries(WorkRoot), Is.Empty);
        }

        [Test]
        public void ATimestampThatCantBeWritten_IsAWarning_TheDataIsStillRefreshed()
        {
            // a real replacement failure after the installation: General.yaml is a directory, so the rename fails
            File.Delete(GeneralYaml);
            Directory.CreateDirectory(GeneralYaml);
            File.WriteAllText(Path.Join(GeneralYaml, "keep.txt"), "keep");
            RidbRefreshResult Result = Run();

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.RefreshedWithoutTimestamp), Describe(Result));
            Assert.That(Result.Succeeded && Result.Installed, Is.True);
            Assert.That(Result.RefreshedAt, Is.EqualTo(RefreshTime));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.RecordingRefresh));
            Assert.That(Result.Detail, Is.Not.Empty);
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            Assert.That(File.ReadAllText(Path.Join(GeneralYaml, "keep.txt")), Is.EqualTo("keep"));
            AssertNoLeftovers();
            AssertLockFree();
        }

        [Test]
        public void TheTimestamp_IsWrittenWhileTheLockIsStillHeld()
        {
            bool? LockHeldWhileRecording = null;
            RidbRefreshResult Result = Run(Progress: new Synchronous(P =>
            {
                if (P.Stage == RidbRefreshStage.RecordingRefresh && LockHeldWhileRecording == null)
                {
                    using FileStream? Probe = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out _);
                    LockHeldWhileRecording = Probe == null;
                }
            }));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed));
            Assert.That(LockHeldWhileRecording, Is.True);
        }

        // ---- cancellation ----

        // A cancel at any stage before the cache step stops the refresh at its next checkpoint: Canceled, not an error;
        // nothing installed, changed or recorded; the download, the extraction and the staging folder removed; the lock free.
        [TestCase("CleaningUp")]
        [TestCase("Preparing")]
        [TestCase("Downloading")]
        [TestCase("Extracting")]
        [TestCase("PreparingDatabase")]
        [TestCase("Importing")]
        [TestCase("Saving")]
        [TestCase("Indexing")]
        [TestCase("Validating")]
        [TestCase("Finalizing")]
        public void ACancelBeforeTheCacheStep_StopsSafely_AndChangesNothing(string StageName)
        {
            RidbRefreshStage At = Enum.Parse<RidbRefreshStage>(StageName);
            Snapshot Before = TakeSnapshot();
            using var Cancel = new CancellationTokenSource();
            RidbRefreshResult Result = Run(Progress: CancelAt(Cancel, At), Token: Cancel.Token);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled), Describe(Result));
            Assert.That(Result.Error, Is.InstanceOf<OperationCanceledException>());
            Assert.That(Result.CancelRequested, Is.True);
            Assert.That(Result.Installed, Is.False);
            Assert.That(Result.Stage, Is.LessThan(RidbRefreshStage.ClearingCache));
            AssertNothingChanged(Before);
            AssertNoLeftovers();
            AssertLockFree();
        }

        // the server stops sending after one byte, half the export or all but one byte; the cancel comes while it waits
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ACancelWhileDownloading_StopsTheTransferAtOnce(int Where)
        {
            int AfterBytes = Where == 1 ? 1 : Where == 2 ? ValidExport.Length / 2 : ValidExport.Length - 1;
            var Slow = new FakeExport(ValidExport) { StallAfter = AfterBytes };
            using var Cancel = new CancellationTokenSource();
            Snapshot Before = TakeSnapshot();
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            RidbRefreshResult Result = Run(Handler: Slow, Token: Cancel.Token, Stall: TimeSpan.FromMinutes(5),
                Progress: new Synchronous(P =>
                {
                    if (P.Stage == RidbRefreshStage.Downloading)
                    {
                        Cancel.CancelAfter(200);
                    }
                }));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled), Describe(Result));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.Downloading));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the stalled read was canceled, not waited out");
            Assert.That(Slow.MaxPosition, Is.EqualTo(AfterBytes));
            AssertNothingChanged(Before);
            AssertNoLeftovers();
        }

        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(6)]
        public void ACancelBetweenTheRidbFiles_StopsBeforeTheNextOne(int AfterFiles)
        {
            Snapshot Before = TakeSnapshot();
            using var Cancel = new CancellationTokenSource();
            var Reports = new List<RidbRefreshProgress>();
            RidbRefreshResult Result = Run(Token: Cancel.Token, Progress: new Synchronous(P =>
            {
                Reports.Add(P);
                if (P.Stage == RidbRefreshStage.Importing && P.Done == AfterFiles)
                {
                    Cancel.Cancel();
                }
            }));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled), Describe(Result));
            Assert.That(Reports.Where(r => r.Stage == RidbRefreshStage.Importing && r.Done != null).Max(r => r.Done), Is.EqualTo(AfterFiles), "no file read after the cancel");
            Assert.That(Reports.Any(r => r.Stage == RidbRefreshStage.Saving), Is.False, "nothing inserted");
            AssertNothingChanged(Before);
            AssertNoLeftovers();
        }

        [Test]
        public void ACancelBetweenTheZipEntries_StopsBeforeTheNextOne()
        {
            using var Cancel = new CancellationTokenSource();
            var Reports = new List<RidbRefreshProgress>();
            RidbRefreshResult Result = Run(Token: Cancel.Token, Progress: new Synchronous(P =>
            {
                Reports.Add(P);
                if (P.Stage == RidbRefreshStage.Extracting && P.Done == 2)
                {
                    Cancel.Cancel();
                }
            }));

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled), Describe(Result));
            Assert.That(Reports.Where(r => r.Stage == RidbRefreshStage.Extracting && r.Done != null).Max(r => r.Done), Is.EqualTo(2));
            AssertNoLeftovers();
        }

        [Test]
        public void ATokenCanceledBeforeTheStart_DownloadsNothing()
        {
            using var Cancel = new CancellationTokenSource();
            Cancel.Cancel();
            Snapshot Before = TakeSnapshot();
            RidbRefreshResult Result = Run(Token: Cancel.Token);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled));
            Assert.That(Result.Stage, Is.EqualTo(RidbRefreshStage.CleaningUp));
            Assert.That(Export.Requests, Is.Zero);
            AssertNothingChanged(Before);
            AssertNoLeftovers();
        }

        // From the cache step on the token is no longer read: the refresh installs, checks and records to its end.
        [TestCase("ClearingCache")]
        [TestCase("Installing")]
        [TestCase("CheckingDatabase")]
        [TestCase("RecordingRefresh")]
        public void ACancelFromTheCacheStepOn_IsTooLate_TheRefreshFinishes(string StageName)
        {
            RidbRefreshStage At = Enum.Parse<RidbRefreshStage>(StageName);
            using var Cancel = new CancellationTokenSource();
            RidbRefreshResult Result = Run(Progress: CancelAt(Cancel, At), Token: Cancel.Token);

            Assert.That(Result.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed), Describe(Result));
            Assert.That(Result.CancelRequested, Is.True, "the late cancel is reported");
            Assert.That(Marker(InstalledDb), Is.EqualTo(NewMarker));
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(RefreshTime));
            AssertNoLeftovers();
            AssertLockFree();
        }

        // ---- the classification of a failure ----

        [Test]
        public void Classify_TellsTheFailuresApart()
        {
            using var Canceled = new CancellationTokenSource();
            Canceled.Cancel();
            CancellationToken None = CancellationToken.None;
            Exception Transfer = new IOException("reset");
            Transfer.Data[Web.TransferFailureKey] = true;
            var Cases = new (RidbRefreshStage Stage, Exception Error, CancellationToken Token, bool Installed, RidbRefreshOutcome Expected)[]
            {
                (RidbRefreshStage.Importing, new OperationCanceledException(), Canceled.Token, false, RidbRefreshOutcome.Canceled),
                (RidbRefreshStage.ClearingCache, new OperationCanceledException(), Canceled.Token, false, RidbRefreshOutcome.Unexpected),
                (RidbRefreshStage.Downloading, new OperationCanceledException(), None, false, RidbRefreshOutcome.Unexpected),
                (RidbRefreshStage.Downloading, new TaskCanceledException("timeout", new TimeoutException()), None, false, RidbRefreshOutcome.DownloadTimedOut),
                (RidbRefreshStage.Downloading, new DownloadTimedOutException(TimeSpan.FromSeconds(60)), None, false, RidbRefreshOutcome.DownloadTimedOut),
                (RidbRefreshStage.Downloading, new HttpRequestException("404"), None, false, RidbRefreshOutcome.DownloadFailed),
                (RidbRefreshStage.Downloading, Transfer, None, false, RidbRefreshOutcome.DownloadFailed),
                (RidbRefreshStage.Downloading, new IOException("No space left on device"), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.Extracting, new InvalidDataException(), None, false, RidbRefreshOutcome.ExtractionFailed),
                (RidbRefreshStage.Extracting, new IOException("No space left on device"), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.Importing, new System.Text.Json.JsonException(), None, false, RidbRefreshOutcome.ImportFailed),
                (RidbRefreshStage.Importing, new FileNotFoundException(), None, false, RidbRefreshOutcome.ImportFailed),
                (RidbRefreshStage.Saving, new SqliteException("constraint", 19), None, false, RidbRefreshOutcome.ImportFailed),
                (RidbRefreshStage.Saving, new SqliteException("full", 13), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.PreparingDatabase, new SqliteException("not a database", 26), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.Indexing, new SqliteException("disk I/O error", 10 | (3 << 8)), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.ClearingCache, new InvalidOperationException(), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.Installing, new SqliteException("busy", 5), None, false, RidbRefreshOutcome.InstallationFailed),
                (RidbRefreshStage.Installing, new IOException("locked"), None, false, RidbRefreshOutcome.InstallationFailed),
                (RidbRefreshStage.CheckingDatabase, new NullReferenceException(), None, true, RidbRefreshOutcome.Unexpected),
                (RidbRefreshStage.CleaningUp, new UnauthorizedAccessException(), None, false, RidbRefreshOutcome.StorageFailed),
                (RidbRefreshStage.Importing, new NullReferenceException(), None, false, RidbRefreshOutcome.Unexpected),
            };
            foreach (var Case in Cases)
            {
                Assert.That(RidbRefreshService.Classify(Case.Stage, Case.Error, Case.Token, Case.Installed), Is.EqualTo(Case.Expected),
                    Case.Stage + " " + Case.Error.GetType().Name);
            }
        }

        [Test]
        public void AnUnusableRequest_ThrowsBeforeAnythingHappens()
        {
            Assert.That(() => RidbRefreshService.Run(new RidbRefreshRequest("", CacheDirectory), null, CancellationToken.None, Options()), Throws.ArgumentException);
            Assert.That(() => RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, ConfigPath), null, CancellationToken.None, Options()), Throws.InvalidOperationException);
            Assert.That(() => RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, "/"), null, CancellationToken.None, Options()), Throws.InvalidOperationException);
            Assert.That(File.Exists(Path.Join(ConfigPath, RidbStagedRefresh.RefreshLockFileName)), Is.False, "refused before the lock");
            Assert.That(Export.Requests, Is.Zero);
        }

        // ---- the console adapter ----

        [Test]
        public void TheConsole_PrintsTheLinesAndTheNotice_AndUpdatesItsSettings()
        {
            var Config = ConsoleConfig();
            bool Refreshed = false;
            string Output = CaptureConsole(() => Refreshed = RefreshRidbRecreationData.RefreshData(false, Config, new RidbRefreshOptions { Handler = Export, WorkRoot = WorkRoot }));

            Assert.That(Refreshed, Is.True, Output);
            List<string> Lines = Output.Replace("\r\n", "\n").Split('\n').ToList();
            Assert.That(Lines[0], Is.EqualTo("Downloading RIDBFullExport_V1_JSON.zip"));
            Assert.That(Lines[1], Does.StartWith("Extracting " + Path.Join(WorkRoot, RidbRefreshService.DownloadFolderPrefix)));
            Assert.That(Lines, Does.Contain("Installed the refreshed database"));
            Assert.That(Output, Does.Contain("The campground data was refreshed."));
            Assert.That(Config.GeneralConfig!.LastRidbDataRefresh, Is.GreaterThan(PreviousRefresh));
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(Config.GeneralConfig.LastRidbDataRefresh));
        }

        [Test]
        public void TheConsole_RethrowsADownloadFailureUnchanged_SoItEndsAsBefore()
        {
            var Config = ConsoleConfig();
            var Refused = new FakeExport(ValidExport) { ConnectFailure = new HttpRequestException("Connection refused (ridb.recreation.gov:443)") };
            HttpRequestException? Thrown = null;
            CaptureConsole(() => Thrown = Assert.Throws<HttpRequestException>(() => RefreshRidbRecreationData.RefreshData(false, Config, new RidbRefreshOptions { Handler = Refused, WorkRoot = WorkRoot })));

            Assert.That(Thrown, Is.SameAs(Refused.ConnectFailure), "the very exception");
            Assert.That(ConsoleUi.DescribeFailure(Thrown!), Is.EqualTo(new[] { "a download failed: Connection refused (ridb.recreation.gov:443)", "Check the internet connection and try again." }));
            Assert.That(Config.GeneralConfig!.LastRidbDataRefresh, Is.EqualTo(PreviousRefresh));
            AssertNoLeftovers();
        }

        [Test]
        public void TheConsole_ReportsAStalledDownloadAsADownloadFailure()
        {
            var Config = ConsoleConfig();
            Exception? Thrown = null;
            CaptureConsole(() => Thrown = Assert.Catch(() => RefreshRidbRecreationData.RefreshData(false, Config,
                new RidbRefreshOptions { Handler = new FakeExport(ValidExport) { StallAfter = 10 }, WorkRoot = WorkRoot, StallTimeout = TimeSpan.FromMilliseconds(200) })));

            Assert.That(ConsoleUi.DescribeFailure(Thrown!), Is.EqualTo(new[] { "a download failed: the download stalled: nothing was received for 0.2 seconds", "Check the internet connection and try again." }));
            AssertNoLeftovers();
        }

        [Test]
        public void TheConsole_AnotherRefresh_IsTheSameErrorAsBefore()
        {
            var Config = ConsoleConfig();
            string Output;
            using (FileStream? Held = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out _))
            {
                Output = CaptureConsole(() => Assert.That(RefreshRidbRecreationData.RefreshData(false, Config, new RidbRefreshOptions { Handler = Export, WorkRoot = WorkRoot }), Is.False));
            }

            Assert.That(Output, Does.Contain("Another RIDB refresh is running (").And.Contain("). Nothing was changed."));
            Assert.That(Export.Requests, Is.Zero);
        }

        // ---- helpers ----

        private RidbRefreshOptions Options(FakeExport? Handler = null, List<string>? Log = null, TimeSpan? Stall = null, TimeSpan? Header = null, bool UseWorkRoot = true) => new RidbRefreshOptions
        {
            Handler = Handler ?? Export,
            WorkRoot = UseWorkRoot ? WorkRoot : null,
            Log = Log == null ? null : Log.Add,
            StallTimeout = Stall ?? TimeSpan.FromSeconds(30),
            HeaderTimeout = Header ?? TimeSpan.FromSeconds(30),
            UtcNow = () => RefreshTime,
        };

        // DefaultWorkRoot: the production work folder, LocalApplicationData (only with RedirectLocalApplicationData)
        private RidbRefreshResult Run(FakeExport? Handler = null, IProgress<RidbRefreshProgress>? Progress = null, CancellationToken Token = default,
            List<string>? Log = null, TimeSpan? Stall = null, TimeSpan? Header = null, bool DefaultWorkRoot = false)
        {
            if (Handler != null)
            {
                Export = Handler;
            }
            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory), Progress, Token,
                Options(Handler, Log, Stall, Header, UseWorkRoot: !DefaultWorkRoot));
            SqliteConnection.ClearAllPools();
            return Result;
        }

        private void AssertFailsBeforeInstalling(FakeExport Handler, RidbRefreshOutcome Expected, RidbRefreshStage Stage, Action<Exception> CheckError,
            TimeSpan? Stall = null, TimeSpan? Header = null, int Requests = 1, bool CheckDatabase = true)
        {
            Snapshot Before = TakeSnapshot(CheckDatabase);
            RidbRefreshResult Result = Run(Handler: Handler, Stall: Stall, Header: Header);

            Assert.That(Result.Outcome, Is.EqualTo(Expected), Describe(Result));
            Assert.That(Result.Stage, Is.EqualTo(Stage));
            Assert.That(Result.Installed, Is.False);
            Assert.That(Result.RefreshedAt, Is.Null);
            Assert.That(Result.Error, Is.Not.Null);
            CheckError(Result.Error!);
            Assert.That(Handler.Requests, Is.EqualTo(Requests));
            AssertNothingChanged(Before, CheckDatabase);
            AssertNoLeftovers();
            AssertLockFree();
        }

        private static string Describe(RidbRefreshResult Result) =>
            Result.Outcome + " at " + Result.Stage + ": " + Result.Error + " " + string.Join("; ", Result.Problems);

        private static IProgress<RidbRefreshProgress> CancelAt(CancellationTokenSource Cancel, RidbRefreshStage Stage) =>
            new Synchronous(P =>
            {
                if (P.Stage == Stage)
                {
                    Cancel.Cancel();
                }
            });

        // an IProgress that runs on the reporting thread, so a test acts exactly at a step
        private sealed class Synchronous : IProgress<RidbRefreshProgress>
        {
            private readonly Action<RidbRefreshProgress> _Action;
            internal Synchronous(Action<RidbRefreshProgress> Action) => _Action = Action;
            public void Report(RidbRefreshProgress Value) => _Action(Value);
        }

        private CampertronInternalConfig ConsoleConfig() => new CampertronInternalConfig
        {
            ConfigPath = ConfigPath,
            CachePath = CacheDirectory,
            GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest, AutoRefresh = false, RefreshRidbDataDayInterval = 30, LastRidbDataRefresh = PreviousRefresh },
        };

        private static string CaptureConsole(Action Action)
        {
            TextWriter Out = Console.Out, Error = Console.Error;
            var Captured = new StringWriter();
            try
            {
                Console.SetOut(Captured);
                Console.SetError(Captured);
                Action();
            }
            finally
            {
                Console.SetOut(Out);
                Console.SetError(Error);
            }
            return Captured.ToString();
        }

        private sealed class Snapshot
        {
            public string Hash = string.Empty;
            public string Yaml = string.Empty;
            public List<string> Cache = new List<string>();
        }

        private Snapshot TakeSnapshot(bool CheckDatabase = true)
        {
            SqliteConnection.ClearAllPools();
            if (CheckDatabase)
            {
                Checkpoint(InstalledDb);
            }
            return new Snapshot
            {
                Hash = Sha256(InstalledDb),
                Yaml = File.ReadAllText(GeneralYaml),
                Cache = Directory.GetFiles(CacheDirectory).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList(),
            };
        }

        private void AssertNothingChanged(Snapshot Before, bool CheckDatabase = true)
        {
            SqliteConnection.ClearAllPools();
            if (CheckDatabase)
            {
                Checkpoint(InstalledDb);
            }
            Assert.That(Sha256(InstalledDb), Is.EqualTo(Before.Hash), "installed database bytes");
            Assert.That(File.ReadAllText(GeneralYaml), Is.EqualTo(Before.Yaml), "General.yaml");
            Assert.That(Directory.GetFiles(CacheDirectory).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(Before.Cache), "the cache");
            AssertTimestampUnchanged();
        }

        private void AssertTimestampUnchanged()
        {
            Assert.That(Yaml.GeneralConfigGetConfig(ConfigPath).LastRidbDataRefresh, Is.EqualTo(PreviousRefresh));
        }

        // no staging folder, no download folder, no extraction (the work folder is empty)
        private void AssertNoLeftovers()
        {
            Assert.That(StagingDirectories(), Is.Empty, "staging folders");
            Assert.That(Directory.GetFileSystemEntries(WorkRoot), Is.Empty, "download folders and the extraction");
        }

        private void AssertLockFree()
        {
            using FileStream? Again = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out _);
            Assert.That(Again, Is.Not.Null, "the refresh lock was released");
        }

        private List<string> StagingDirectories() =>
            Directory.GetFileSystemEntries(ConfigPath).Select(Path.GetFileName).Where(n => RidbStagedRefresh.IsStagingDirectoryName(n!)).Select(n => n!).ToList();

        private string RedirectLocalApplicationData()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("redirects LocalApplicationData through XDG_DATA_HOME (Linux)");
            }
            string LocalAppData = Path.Join(Root, "localappdata");
            Directory.CreateDirectory(LocalAppData);
            OriginalXdgDataHome ??= Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? string.Empty;
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", LocalAppData);
            Assert.That(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Is.EqualTo(LocalAppData));
            return LocalAppData;
        }

        private static void RequireUnixPermissions()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes");
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root ignores file modes");
            }
        }

        // RidbFixture with WATCHMAN CAMPGROUND, so it passes RidbDatabase.Validate, and a marker on the first facility
        private static RidbFixture ValidFixture(string Marker)
        {
            RidbFixture Fixture = RidbFixture.Create();
            Fixture.Facilities[3].FacilityName = RidbDatabase.WatchmanName;
            Fixture.Facilities[0].FacilityName = Marker;
            return Fixture;
        }

        private byte[] Zip(RidbFixture Fixture) => ZipFolder(Fixture.Write(NewTempDirectory("ridb-", SharedDirectory)));

        private static byte[] ZipFolder(string Folder)
        {
            string Archive = Path.Join(Path.GetDirectoryName(Folder)!, Path.GetFileName(Folder) + ".zip");
            ZipFile.CreateFromDirectory(Folder, Archive);
            byte[] Bytes = File.ReadAllBytes(Archive);
            File.Delete(Archive);
            Directory.Delete(Folder, true);
            return Bytes;
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
            if (!OperatingSystem.IsWindows())
            {
                foreach (string Entry in Directory.GetDirectories(Directory_, "*", SearchOption.AllDirectories))
                {
                    try { if (new DirectoryInfo(Entry).LinkTarget == null) File.SetUnixFileMode(Entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
                }
            }
            Directory.Delete(Directory_, true);
        }

        private static string Sha256(string File_) => System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(File_)));

        private static SqliteConnection Open(string DbFile, SqliteOpenMode Mode)
        {
            var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbFile, Mode = Mode, Pooling = false }.ToString());
            Connection.Open();
            return Connection;
        }

        private static void Checkpoint(string DbFile)
        {
            using SqliteConnection Connection = Open(DbFile, SqliteOpenMode.ReadWrite);
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            Command.ExecuteNonQuery();
        }

        private static string QuickCheck(string DbFile)
        {
            using SqliteConnection Connection = Open(DbFile, SqliteOpenMode.ReadOnly);
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = "PRAGMA quick_check";
            return System.Convert.ToString(Command.ExecuteScalar()) ?? "";
        }

        private static string? Marker(string DbFile)
        {
            using SqliteConnection Connection = Open(DbFile, SqliteOpenMode.ReadOnly);
            using SqliteCommand Command = Connection.CreateCommand();
            Command.CommandText = "SELECT FacilityName FROM FacilitiesEntries WHERE FacilityID = $id";
            Command.Parameters.AddWithValue("$id", RidbFixture.Keys[0]);
            return Command.ExecuteScalar() as string;
        }
    }

    // The RIDB server, in memory: the export's bytes as a response that can fail, stall, leave out its length or answer
    // late. Counts the requests; nothing leaves the process.
    internal sealed class FakeExport : HttpMessageHandler
    {
        private readonly byte[] _Body;
        private long _MaxPosition;

        internal FakeExport(byte[] Body)
        {
            _Body = Body;
        }

        internal HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        internal bool SendLength { get; set; } = true;
        // the body stops after this many bytes and sends nothing more until the request is canceled
        internal int StallAfter { get; init; } = -1;
        // the body breaks after this many bytes (as a reset connection)
        internal int FailAfter { get; init; } = -1;
        internal TimeSpan HeaderDelay { get; init; } = TimeSpan.Zero;
        internal HttpRequestException? ConnectFailure { get; init; }
        internal int Requests;
        internal readonly List<Uri> Urls = new List<Uri>();
        internal long MaxPosition => Interlocked.Read(ref _MaxPosition);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Token)
        {
            Interlocked.Increment(ref Requests);
            lock (Urls)
            {
                Urls.Add(Request.RequestUri!);
            }
            if (ConnectFailure != null)
            {
                throw ConnectFailure;
            }
            if (HeaderDelay > TimeSpan.Zero)
            {
                await Task.Delay(HeaderDelay, Token);
            }
            var Content = new StreamContent(new Body(this));
            if (SendLength)
            {
                Content.Headers.ContentLength = _Body.Length;
            }
            return new HttpResponseMessage(Status) { Content = Content, RequestMessage = Request };
        }

        private sealed class Body : Stream
        {
            private readonly FakeExport _Owner;
            private int _Position;
            internal Body(FakeExport Owner) => _Owner = Owner;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => _Position; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long Offset, SeekOrigin Origin) => throw new NotSupportedException();
            public override void SetLength(long Value) => throw new NotSupportedException();
            public override void Write(byte[] Buffer, int Offset, int Count) => throw new NotSupportedException();
            public override int Read(byte[] Buffer, int Offset, int Count) => ReadAsync(Buffer.AsMemory(Offset, Count)).AsTask().GetAwaiter().GetResult();
            public override async ValueTask<int> ReadAsync(Memory<byte> Buffer, CancellationToken Token = default)
            {
                if (_Owner.FailAfter >= 0 && _Position >= _Owner.FailAfter)
                {
                    throw new IOException("Unable to read data from the transport connection: Connection reset by peer.");
                }
                if (_Owner.StallAfter >= 0 && _Position >= _Owner.StallAfter)
                {
                    await Task.Delay(Timeout.Infinite, Token);
                }
                int Limit = _Owner._Body.Length;
                if (_Owner.StallAfter >= 0) Limit = Math.Min(Limit, _Owner.StallAfter);
                if (_Owner.FailAfter >= 0) Limit = Math.Min(Limit, _Owner.FailAfter);
                int Count = Math.Min(Buffer.Length, Limit - _Position);
                if (Count <= 0)
                {
                    return 0;
                }
                _Owner._Body.AsMemory(_Position, Count).CopyTo(Buffer);
                _Position += Count;
                long Seen = _Position;
                long Max;
                while ((Max = Interlocked.Read(ref _Owner._MaxPosition)) < Seen && Interlocked.CompareExchange(ref _Owner._MaxPosition, Seen, Max) != Max) { }
                return Count;
            }
        }
    }
}
