using System.Text.RegularExpressions;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Regression tests for the RIDB FacilityAddresses layout: the state is in AddressStateCode and
    // PostalCode holds the ZIP/postal code. Each test uses its own temporary SQLite database created
    // from the EF model, so these tests do not touch LocalApplicationData, the network or the embedded DB.
    public class AddressStateTests
    {
        private const string WatchmanID = "232445";
        private const string WatchmanName = "WATCHMAN CAMPGROUND";
        private const string ZionRecAreaID = "2994";
        private const string ZionRecAreaName = "Zion National Park";
        private const string NoZipID = "900001";
        private const string NoZipName = "NO ZIP TEST CAMPGROUND";
        private const string NoZipRecAreaID = "900100";
        private const string NoZipRecAreaName = "No Zip Test Recreation Area";

        private string ConfigPath = string.Empty;

        [SetUp]
        public void Setup()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronAddressStateTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
            }
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

        private void SeedCorrectedRidbLayout()
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = ZionRecAreaID, RecAreaName = ZionRecAreaName });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = NoZipRecAreaID, RecAreaName = NoZipRecAreaName });
                db.FacilitiesEntries.Add(NewCampground(WatchmanID, WatchmanName, ZionRecAreaID));
                db.FacilitiesEntries.Add(NewCampground(NoZipID, NoZipName, NoZipRecAreaID));
                db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata
                {
                    FacilityAddressID = "A-" + WatchmanID,
                    FacilityID = WatchmanID,
                    FacilityAddressType = "Default",
                    City = "SPRINGDALE",
                    PostalCode = "84767",
                    AddressStateCode = "UT"
                });
                db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata
                {
                    FacilityAddressID = "A-" + NoZipID,
                    FacilityID = NoZipID,
                    FacilityAddressType = "Physical",
                    City = "ESTES PARK",
                    PostalCode = "",
                    AddressStateCode = "CO"
                });
                db.SaveChanges();
            }
        }

        private static FacilitiesData NewCampground(string FacilityID, string FacilityName, string RecAreaID)
        {
            return new FacilitiesData
            {
                FacilityID = FacilityID,
                FacilityName = FacilityName,
                FacilityTypeDescription = "Campground",
                ParentRecAreaID = RecAreaID,
                Reservable = true,
                Enabled = true
            };
        }

        [Test]
        public void UniqueStates_ReturnsStateCodesNotPostalCodes()
        {
            SeedCorrectedRidbLayout();

            List<string> States = Read.UniqueStates(ConfigPath);

            Assert.That(States, Is.EqualTo(new List<string> { "CO", "UT" }));
            Assert.That(States, Has.None.Matches<string>(s => Regex.IsMatch(s, "[0-9]")));
        }

        [Test]
        public void StateFiltering_UsesAddressStateCode()
        {
            SeedCorrectedRidbLayout();

            Assert.That(Read.UniqueCities("UT", ConfigPath), Is.EqualTo(new List<string> { "SPRINGDALE" }));
            Assert.That(Read.UniqueParks("UT", "SPRINGDALE", ConfigPath), Is.EqualTo(new List<string> { WatchmanName }));
            Assert.That(Read.UniqueParksByState("UT", ConfigPath), Is.EqualTo(new List<string> { WatchmanName }));

            // A ZIP code is not a state and must not select anything.
            Assert.That(Read.UniqueCities("84767", ConfigPath), Is.Empty);
            Assert.That(Read.UniqueParks("84767", "SPRINGDALE", ConfigPath), Is.Empty);
            Assert.That(Read.UniqueParksByState("84767", ConfigPath), Is.Empty);
        }

        [Test]
        public void CampgroundWithEmptyPostalCode_IsIncludedWhenStateIsPresent()
        {
            SeedCorrectedRidbLayout();

            Assert.That(Read.UniqueStates(ConfigPath), Does.Contain("CO"));
            Assert.That(Read.UniqueCities("CO", ConfigPath), Is.EqualTo(new List<string> { "ESTES PARK" }));
            Assert.That(Read.UniqueParks("CO", "ESTES PARK", ConfigPath), Is.EqualTo(new List<string> { NoZipName }));
            Assert.That(Read.UniqueParksByState("CO", ConfigPath), Is.EqualTo(new List<string> { NoZipName }));
            Assert.That(Read.UniqueCampgrounds(ConfigPath), Does.Contain(NoZipRecAreaName));
            Assert.That(Read.UniqueCampgroundsByPark(NoZipRecAreaName, ConfigPath), Is.EqualTo(new List<string> { NoZipName }));
            Assert.That(Read.UniqueParks(ConfigPath), Does.Contain(NoZipName));
        }

        [Test]
        public void Populate_Watchman_ResolvesUtahFromAddressStateCodeAndKeepsPostalCode()
        {
            string RidbDirectory = WriteRidbFixture(
                "{\"FacilityAddressID\":\"A-" + WatchmanID + "\",\"FacilityID\":\"" + WatchmanID + "\",\"FacilityAddressType\":\"Default\",\"City\":\"Springdale\",\"PostalCode\":\"84767\",\"AddressStateCode\":\"UT\",\"AddressCountryCode\":\"USA\"}");

            PopulateDbFromRIDBFiles.Populate(RidbDirectory, ConfigPath);

            FacilityAddressesRecdata Address = GetAddress("A-" + WatchmanID);
            Assert.That(Address.AddressStateCode, Is.EqualTo("UT"));
            Assert.That(Address.PostalCode, Is.EqualTo("84767"));
            Assert.That(Address.City, Is.EqualTo("SPRINGDALE"));
            Assert.That(Read.UniqueStates(ConfigPath), Is.EqualTo(new List<string> { "UT" }));
            Assert.That(Read.UniqueParksByState("UT", ConfigPath), Is.EqualTo(new List<string> { WatchmanName }));
            Assert.That(Read.UniqueParks("UT", "SPRINGDALE", ConfigPath), Is.EqualTo(new List<string> { WatchmanName }));
        }

        [Test]
        public void Populate_NormalizesFullStateNameInAddressStateCodeAndLeavesPostalCodeUnchanged()
        {
            string RidbDirectory = WriteRidbFixture(
                "{\"FacilityAddressID\":\"A-" + WatchmanID + "\",\"FacilityID\":\"" + WatchmanID + "\",\"FacilityAddressType\":\"Default\",\"City\":\"Springdale\",\"PostalCode\":\"84767-0001\",\"AddressStateCode\":\" Utah \",\"AddressCountryCode\":\"USA\"}");

            PopulateDbFromRIDBFiles.Populate(RidbDirectory, ConfigPath);

            FacilityAddressesRecdata Address = GetAddress("A-" + WatchmanID);
            Assert.That(Address.AddressStateCode, Is.EqualTo("UT"));
            Assert.That(Address.PostalCode, Is.EqualTo("84767-0001"));
            Assert.That(Read.UniqueStates(ConfigPath), Is.EqualTo(new List<string> { "UT" }));
        }

        private FacilityAddressesRecdata GetAddress(string FacilityAddressID)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                return db.FacilityAddressesEntries.Single(a => a.FacilityAddressID == FacilityAddressID);
            }
        }

        // Writes only the 6 RIDB export files that PopulateDbFromRIDBFiles.Populate reads (the files of
        // RidbDatabase.OmittedTables are deliberately absent). All are empty except RecAreas,
        // Facilities (Watchman) and FacilityAddresses (the given record).
        private string WriteRidbFixture(string FacilityAddressJson)
        {
            string RidbDirectory = Path.Join(ConfigPath, "Extract");
            Directory.CreateDirectory(RidbDirectory);
            string[] RidbFiles =
            {
                "CampsiteAttributes", "Campsites", "Facilities", "FacilityAddresses", "PermittedEquipment", "RecAreas"
            };
            foreach (string RidbFile in RidbFiles)
            {
                File.WriteAllText(Path.Join(RidbDirectory, RidbFile + "_API_v1.json"), "{\"RECDATA\":[]}");
            }
            File.WriteAllText(Path.Join(RidbDirectory, "RecAreas_API_v1.json"),
                "{\"RECDATA\":[{\"RecAreaID\":\"" + ZionRecAreaID + "\",\"RecAreaName\":\"" + ZionRecAreaName + "\"}]}");
            File.WriteAllText(Path.Join(RidbDirectory, "Facilities_API_v1.json"),
                "{\"RECDATA\":[{\"FacilityID\":\"" + WatchmanID + "\",\"FacilityName\":\"" + WatchmanName + "\",\"FacilityTypeDescription\":\"Campground\",\"ParentRecAreaID\":\"" + ZionRecAreaID + "\",\"Reservable\":true,\"Enabled\":true}]}");
            File.WriteAllText(Path.Join(RidbDirectory, "FacilityAddresses_API_v1.json"),
                "{\"RECDATA\":[" + FacilityAddressJson + "]}");
            return RidbDirectory;
        }
    }
}
