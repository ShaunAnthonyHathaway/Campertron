using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // Where Campertron keeps its data on the desktop (architecture.md §Configuration, business-rules §1).
    //
    // TD-47: Load.GetConfig looked up LocalApplicationData without SpecialFolderOption.Create. On Linux a missing
    // ~/.local/share (or a missing $XDG_DATA_HOME) then resolved to "", and CampertronConfig and CampertronCache were
    // created in the working directory. Now the folder is created, and without a full path Campertron stops instead.
    //
    // The tests redirect LocalApplicationData through HOME and XDG_DATA_HOME (Linux; elsewhere they are ignored) and
    // change the working directory; all of it is restored in TearDown, and nothing outside the test directory is used.
    public class DataFolderTests
    {
        private string Root = string.Empty;
        private string Home => Path.Join(Root, "home");
        private string Work => Path.Join(Root, "work");
        private string? OriginalHome;
        private string? OriginalXdgDataHome;
        private string? OriginalContainer;
        private string? OriginalContainers;
        private string OriginalDirectory = string.Empty;
        private Func<string> OriginalLookup = null!;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronDataFolderTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(Work);
            OriginalHome = Environment.GetEnvironmentVariable("HOME");
            OriginalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            OriginalContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
            OriginalContainers = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS");
            OriginalDirectory = Environment.CurrentDirectory;
            OriginalLookup = Load.LocalApplicationDataLookup;
            // the desktop, whatever runs the tests
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", null);
        }

        [TearDown]
        public void TearDown()
        {
            Load.LocalApplicationDataLookup = OriginalLookup;
            Environment.CurrentDirectory = OriginalDirectory;
            Environment.SetEnvironmentVariable("HOME", OriginalHome);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", OriginalXdgDataHome);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", OriginalContainer);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", OriginalContainers);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // a home folder of its own (without .local/share unless the test creates it), no XDG_DATA_HOME, and the working
        // directory "work" in the test directory
        private void UseFreshHome()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("redirects LocalApplicationData through HOME and XDG_DATA_HOME (Linux)");
            }
            Environment.SetEnvironmentVariable("HOME", Home);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
            Environment.CurrentDirectory = Work;
        }

        private static string Local(string Home_) => Path.Join(Home_, ".local", "share");

        // ---- TD-47: the desktop data folder ----

        [Test]
        public void MissingDataFolder_IsCreated_AndNothingGoesIntoTheWorkingDirectory()
        {
            UseFreshHome();
            Assert.That(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Is.Empty,
                "the defect's condition: without Create the missing folder is \"\"");

            CampertronInternalConfig Config = Load.GetConfig();

            Assert.That(Directory.Exists(Local(Home)), Is.True, "created");
            Assert.That(Config.ConfigPath, Is.EqualTo(Path.Join(Local(Home), "CampertronConfig")));
            Assert.That(Config.CachePath, Is.EqualTo(Path.Join(Local(Home), "CampertronCache")));
            Assert.That(Config.ContainerMode, Is.False);
            Assert.That(Directory.Exists(Config.CachePath), Is.True);
            Assert.That(File.Exists(Path.Join(Config.ConfigPath, "General.yaml")), Is.True);
            Assert.That(File.Exists(Path.Join(Config.ConfigPath, "RecreationDotOrg.db")), Is.True);
            Assert.That(Directory.GetFileSystemEntries(Work), Is.Empty, "nothing in the working directory");
        }

        [Test]
        public void ExistingDataFolder_IsUsedAsBefore()
        {
            UseFreshHome();
            Directory.CreateDirectory(Local(Home));
            string Sentinel = Path.Join(Local(Home), "other-app", "keep.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Sentinel)!);
            File.WriteAllText(Sentinel, "not Campertron's");
            string Before = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string Folder = Load.DesktopDataFolder();

            Assert.That(Before, Is.EqualTo(Local(Home)), "it resolved before the change too");
            Assert.That(Folder, Is.EqualTo(Before), "the same folder");
            Assert.That(File.ReadAllText(Sentinel), Is.EqualTo("not Campertron's"));
            Assert.That(Directory.GetFileSystemEntries(Work), Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void XdgDataHome_IsStillTheOverride(bool Exists)
        {
            UseFreshHome();
            string Xdg = Path.Join(Root, "xdg-data");
            if (Exists)
            {
                Directory.CreateDirectory(Xdg);
            }
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Xdg);

            string Folder = Load.DesktopDataFolder();

            Assert.That(Folder, Is.EqualTo(Xdg));
            Assert.That(Directory.Exists(Xdg), Is.True, Exists ? "kept" : "created (before: \"\", the working directory)");
            Assert.That(Directory.Exists(Local(Home)), Is.False, "the home default is not used");
            Assert.That(Directory.GetFileSystemEntries(Work), Is.Empty);
        }

        [Test]
        public void Container_KeepsConfigAndCache_AndDoesNotLookUpTheDesktopFolder()
        {
            UseFreshHome();
            if (Directory.Exists("/config"))
            {
                Assert.Ignore("this machine has a /config folder; the container check needs it absent");
            }
            int Lookups = 0;
            Load.LocalApplicationDataLookup = () => { Lookups++; return OriginalLookup(); };
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");

            Exception? Failure = Assert.Throws<Exception>(() => Load.GetConfig());

            Assert.That(Failure!.Message, Is.EqualTo("Config path does not exist"), "/config, as before");
            Assert.That(Lookups, Is.EqualTo(0));
            Assert.That(Directory.Exists(Local(Home)), Is.False);
            Assert.That(Directory.GetFileSystemEntries(Work), Is.Empty);
        }

        // ---- TD-48: the RIDB refresh's extraction folder ----
        // Compression.Unzip extracted the export into LocalApplicationData/Extract, deleting that folder first, and the
        // import deleted it afterwards: a folder of that generic name belonging to another program was lost. Now the
        // folder is CampertronExtract, Campertron's own; nothing else in LocalApplicationData is touched.

        private string UseLocalAppData()
        {
            UseFreshHome();
            string LocalAppData = Path.Join(Root, "localappdata");
            Directory.CreateDirectory(LocalAppData);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", LocalAppData);
            Assert.That(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Is.EqualTo(LocalAppData));
            return LocalAppData;
        }
        private static string WriteZip(string Folder, params (string Name, string Content)[] Entries)
        {
            string Zip = Path.Join(Folder, "RIDBFullExport_V1_JSON.zip");
            using (var Archive = System.IO.Compression.ZipFile.Open(Zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var Entry in Entries)
                {
                    using var Writer = new StreamWriter(Archive.CreateEntry(Entry.Name).Open());
                    Writer.Write(Entry.Content);
                }
            }
            return Zip;
        }
        // a folder with a file and a subfolder, with fixed time stamps
        private static string Plant(string Folder, string Content)
        {
            Directory.CreateDirectory(Path.Join(Folder, "sub"));
            File.WriteAllText(Path.Join(Folder, "keep.txt"), Content);
            File.WriteAllText(Path.Join(Folder, "sub", "Facilities_API_v1.json"), "{\"RECDATA\": []}");
            foreach (string File_ in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories))
            {
                File.SetLastWriteTimeUtc(File_, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            }
            return Folder;
        }
        private static Dictionary<string, (string Content, DateTime Written)> Tree(string Folder) =>
            Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories).ToDictionary(
                p => Path.GetRelativePath(Folder, p),
                p => (File.Exists(p) ? File.ReadAllText(p) : "<dir>", File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue));

        [Test]
        public void Unzip_UsesItsOwnFolder_AndLeavesAnExtractFolderAlone()
        {
            string LocalAppData = UseLocalAppData();
            string Foreign = Plant(Path.Join(LocalAppData, "Extract"), "another program's file");
            string Sibling = Plant(Path.Join(LocalAppData, "SomeOtherApp"), "another program's file");
            var ForeignBefore = Tree(Foreign);
            var SiblingBefore = Tree(Sibling);
            string Zip = WriteZip(LocalAppData, ("Facilities_API_v1.json", "{\"RECDATA\": [1]}"), ("Campsites_API_v1.json", "{\"RECDATA\": [2]}"));

            string Extracted = Compression.Unzip(Zip);

            Assert.That(Compression.ExtractFolderName, Is.EqualTo("CampertronExtract"));
            Assert.That(Path.GetFileName(Compression.ExtractFolderName), Is.EqualTo(Compression.ExtractFolderName), "a plain folder name");
            Assert.That(Extracted, Is.EqualTo(Path.Join(LocalAppData, "CampertronExtract")));
            Assert.That(File.ReadAllText(Path.Join(Extracted, "Facilities_API_v1.json")), Is.EqualTo("{\"RECDATA\": [1]}"));
            Assert.That(File.ReadAllText(Path.Join(Extracted, "Campsites_API_v1.json")), Is.EqualTo("{\"RECDATA\": [2]}"));
            Assert.That(File.Exists(Zip), Is.False, "the download is deleted after the extraction, as before");
            Assert.That(Tree(Foreign), Is.EqualTo(ForeignBefore), "the Extract folder is untouched");
            Assert.That(Tree(Sibling), Is.EqualTo(SiblingBefore));
            Assert.That(Directory.GetFileSystemEntries(LocalAppData).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Extract", "SomeOtherApp", "CampertronExtract" }));
        }

        [Test]
        public void Unzip_ClearsOnlyItsOwnFolderFromAnEarlierRun()
        {
            string LocalAppData = UseLocalAppData();
            string Owned = Plant(Path.Join(LocalAppData, "CampertronExtract"), "left by an interrupted refresh");
            string Foreign = Plant(Path.Join(LocalAppData, "Extract"), "another program's file");
            var ForeignBefore = Tree(Foreign);
            string Zip = WriteZip(LocalAppData, ("RecAreas_API_v1.json", "{\"RECDATA\": []}"));

            string Extracted = Compression.Unzip(Zip);

            Assert.That(Extracted, Is.EqualTo(Owned));
            Assert.That(Directory.GetFileSystemEntries(Owned, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Owned, p)),
                Is.EqualTo(new[] { "RecAreas_API_v1.json" }), "only the new export: the earlier content is gone");
            Assert.That(Tree(Foreign), Is.EqualTo(ForeignBefore));
        }

        [Test]
        public void Unzip_OfABrokenDownload_ChangesNoOtherFolder()
        {
            string LocalAppData = UseLocalAppData();
            string Foreign = Plant(Path.Join(LocalAppData, "Extract"), "another program's file");
            string Sibling = Plant(Path.Join(LocalAppData, "SomeOtherApp"), "another program's file");
            var ForeignBefore = Tree(Foreign);
            var SiblingBefore = Tree(Sibling);
            string Zip = Path.Join(LocalAppData, "RIDBFullExport_V1_JSON.zip");
            File.WriteAllText(Zip, "an interrupted download, not a zip");

            Assert.That(() => Compression.Unzip(Zip), Throws.InstanceOf<InvalidDataException>());

            Assert.That(Tree(Foreign), Is.EqualTo(ForeignBefore));
            Assert.That(Tree(Sibling), Is.EqualTo(SiblingBefore));
        }

        [Test]
        public void Unzip_ThroughALinkWithItsName_DoesNotDeleteWhatTheLinkPointsTo()
        {
            string LocalAppData = UseLocalAppData();
            string Elsewhere = Plant(Path.Join(Root, "elsewhere"), "the link's target");
            var ElsewhereBefore = Tree(Elsewhere);
            Directory.CreateSymbolicLink(Path.Join(LocalAppData, "CampertronExtract"), Elsewhere);
            string Zip = WriteZip(LocalAppData, ("RecAreas_API_v1.json", "{\"RECDATA\": []}"));

            string Extracted = Compression.Unzip(Zip);

            Assert.That(Tree(Elsewhere), Is.EqualTo(ElsewhereBefore), "the target and its content are kept");
            Assert.That(new DirectoryInfo(Extracted).LinkTarget, Is.Null, "a real folder of its own replaced the link");
            Assert.That(File.Exists(Path.Join(Extracted, "RecAreas_API_v1.json")), Is.True);
        }

        [Test]
        public void Unzip_WithoutALocalApplicationDataFolder_StaysInItsOwnFolder()
        {
            // no LocalApplicationData folder at all (as in the container, open question #27): the extraction folder is
            // relative to the working directory, as before, and only that folder is used
            UseFreshHome();
            Assert.That(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Is.Empty);
            string Foreign = Plant(Path.Join(Work, "Extract"), "another program's file");
            string Sibling = Plant(Path.Join(Work, "SomeOtherApp"), "another program's file");
            var ForeignBefore = Tree(Foreign);
            var SiblingBefore = Tree(Sibling);
            string Zip = WriteZip(Root, ("RecAreas_API_v1.json", "{\"RECDATA\": []}"));

            string Extracted = Compression.Unzip(Zip);

            Assert.That(Extracted, Is.EqualTo("CampertronExtract"));
            Assert.That(File.Exists(Path.Join(Work, "CampertronExtract", "RecAreas_API_v1.json")), Is.True);
            Assert.That(Tree(Foreign), Is.EqualTo(ForeignBefore));
            Assert.That(Tree(Sibling), Is.EqualTo(SiblingBefore));
            Assert.That(Directory.GetFileSystemEntries(Work).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Extract", "SomeOtherApp", "CampertronExtract" }));
        }

        [TestCase("")]
        [TestCase("relative/folder")]
        public void WithoutAFullPath_CampertronStops_InsteadOfUsingTheWorkingDirectory(string Result)
        {
            Environment.CurrentDirectory = Work;
            Load.LocalApplicationDataLookup = () => Result;

            InvalidOperationException? Failure = Assert.Throws<InvalidOperationException>(() => Load.GetConfig());

            Assert.That(Failure!.Message, Does.StartWith("The local application data folder (LocalApplicationData) is not available"));
            Assert.That(Failure.Message, Does.Contain("does not use the working directory"));
            Assert.That(Directory.GetFileSystemEntries(Work), Is.Empty, "nothing created in the working directory");
            Assert.That(ConsoleUi.DescribeFailure(Failure)[0], Does.Contain("LocalApplicationData"), "the console names the problem");
        }
    }
}
