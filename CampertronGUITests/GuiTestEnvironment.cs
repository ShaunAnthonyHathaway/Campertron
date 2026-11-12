using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronGUITests
{
    // Runs once for the whole assembly, before any GUI test.
    //
    // The GUI reads LocalApplicationData/CampertronConfig directly: MainWindow's constructor calls Load.GetConfig() and
    // ReactiveViewModel queries RecreationDotOrg.db there. This fixture points LocalApplicationData at a new temporary
    // directory (XDG_DATA_HOME, in this test process only) and creates a tiny, deterministic database there from the
    // EF model, as AddressStateTests does. Because the database already exists, has the corrected address layout and
    // none of the omitted RIDB tables, and General.yaml sets autoRefresh: false, Load.GetConfig() neither extracts the
    // 226 MB embedded database, nor repairs it, nor refreshes it. The tests therefore never touch the user's real
    // data, the network or the embedded database, and they do not depend on current RIDB contents.
    //
    // Only Linux honours XDG_DATA_HOME for LocalApplicationData, so on any other OS the GUI tests are ignored rather
    // than risk the user's real data folder.
    [SetUpFixture]
    public class GuiTestEnvironment
    {
        public const string StateUtah = "UT";
        public const string StateColorado = "CO";
        public const string CityAlpha = "ALPHAVILLE";
        public const string CityBravo = "BRAVOTOWN";
        public const string ParkOne = "Fixture Recreation Area One";
        public const string ParkTwo = "Fixture Recreation Area Two";
        public const string CampgroundAlphaID = "990001";
        public const string CampgroundAlpha = "FIXTURE CAMPGROUND ALPHA";
        public const string CampgroundBravoID = "990002";
        public const string CampgroundBravo = "FIXTURE CAMPGROUND BRAVO";
        // beta.8 layout fixtures: a long park name, a campground with an empty city and no contact, directions or fee
        // data, and a campground with a long (trailing-space padded) name, a sentence-long phone value and a long
        // description.
        public const string ParkThree = "Fixture Recreation Area Three With An Exceptionally Long Name For Layout And Trimming Tests";
        public const string EmptyCity = "";
        public const string CampgroundCharlieID = "990003";
        public const string CampgroundCharlie = "FIXTURE CAMPGROUND CHARLIE";
        public const string CampgroundDeltaID = "990004";
        public const string CampgroundDeltaTrimmed = "FIXTURE CAMPGROUND DELTA WITH A VERY LONG NAME THAT KEEPS GOING FOR LAYOUT TESTS (Bravotown, CO)";
        public const string CampgroundDelta = CampgroundDeltaTrimmed + "      ";
        public const string DeltaPhone = "FOR RESERVATIONS CALL (877) 444-6777, FOR OTHER, NON REAL TIME INFORMATION, CALL (555) 010-0100 DURING OFFICE HOURS.";

        // A database created here is a few hundred KB; the embedded RIDB database extracts to 226 MB.
        public const long MaxFixtureDbBytes = 5_000_000;

        private static string? _Root;
        private static string? _OriginalXdgDataHome;

        public static string Root => _Root ?? throw new InvalidOperationException("GUI test environment not initialized");
        public static string ConfigPath => Path.Join(Root, "CampertronConfig");
        public static string DbFile => Path.Join(ConfigPath, "RecreationDotOrg.db");

        [OneTimeSetUp]
        public void CreateIsolatedEnvironment()
        {
            if (!OperatingSystem.IsLinux())
            {
                Assert.Ignore("GUI tests isolate LocalApplicationData through XDG_DATA_HOME, which only Linux honours.");
            }

            _OriginalXdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            _Root = Path.Join(Path.GetTempPath(), "CampertronGUITests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_Root);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _Root);

            // Fail closed: never run a GUI test unless LocalApplicationData really is the temporary directory.
            string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (LocalAppData != _Root)
            {
                throw new InvalidOperationException($"LocalApplicationData resolves to '{LocalAppData}', not the isolated '{_Root}'");
            }

            Directory.CreateDirectory(ConfigPath);
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"),
                "outputTo: UnitTest\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            CreateFixtureDatabase();

            // Load.GetConfig() would replace a database that LegacyAddressLayout flags; the fixture must not be flagged.
            Assert.That(LegacyAddressLayout.DetectRepairReason(DbFile), Is.EqualTo(LegacyAddressLayout.RepairReason.None));
        }

        [OneTimeTearDown]
        public void DeleteIsolatedEnvironment()
        {
            SqliteConnection.ClearAllPools();
            if (_Root != null)
            {
                Environment.SetEnvironmentVariable("XDG_DATA_HOME", _OriginalXdgDataHome);
                if (Directory.Exists(_Root))
                {
                    Directory.Delete(_Root, true);
                }
                _Root = null;
            }
        }

        private static void CreateFixtureDatabase()
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990101", RecAreaName = ParkOne });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990102", RecAreaName = ParkTwo });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "990103", RecAreaName = ParkThree });
                db.FacilitiesEntries.Add(NewCampground(CampgroundAlphaID, CampgroundAlpha, "990101"));
                db.FacilitiesEntries.Add(NewCampground(CampgroundBravoID, CampgroundBravo, "990102"));
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = CampgroundCharlieID,
                    FacilityName = CampgroundCharlie,
                    FacilityDescription = "<p>Sparse fixture</p>",
                    FacilityTypeDescription = "Campground",
                    ParentRecAreaID = "990103",
                    Reservable = true,
                    Enabled = true
                });
                FacilitiesData Delta = NewCampground(CampgroundDeltaID, CampgroundDelta, "990103");
                Delta.FacilityPhone = DeltaPhone;
                Delta.FacilityDescription = "<h2>Overview</h2>" + string.Concat(Enumerable.Repeat(
                    "<p>A long fixture paragraph that describes the campground, its trails, its river, its seasons and its rules in detail.</p>", 40));
                db.FacilitiesEntries.Add(Delta);
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundAlphaID, CityAlpha, StateUtah, "84000"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundBravoID, CityBravo, StateColorado, "80000"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundCharlieID, EmptyCity, StateUtah, "84001"));
                db.FacilityAddressesEntries.Add(NewAddress(CampgroundDeltaID, CityBravo, StateColorado, "80001"));
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
        }

        private static FacilitiesData NewCampground(string FacilityID, string FacilityName, string RecAreaID)
        {
            return new FacilitiesData
            {
                FacilityID = FacilityID,
                FacilityName = FacilityName,
                FacilityDescription = "<p>Fixture description for " + FacilityName + "</p>",
                FacilityDirections = "Fixture directions",
                FacilityUseFeeDescription = "Fixture fee",
                FacilityPhone = "555-0100",
                FacilityEmail = "fixture@example.invalid",
                FacilityTypeDescription = "Campground",
                ParentRecAreaID = RecAreaID,
                Reservable = true,
                Enabled = true
            };
        }

        private static FacilityAddressesRecdata NewAddress(string FacilityID, string City, string State, string PostalCode)
        {
            return new FacilityAddressesRecdata
            {
                FacilityAddressID = "A-" + FacilityID,
                FacilityID = FacilityID,
                FacilityAddressType = "Physical",
                City = City,
                PostalCode = PostalCode,
                AddressStateCode = State
            };
        }
    }
}
