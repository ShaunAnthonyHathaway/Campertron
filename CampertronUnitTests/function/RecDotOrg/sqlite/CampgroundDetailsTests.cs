using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // Read.GetCampgroundDetailsByName (presentation-neutral values for the GUI, which owns its labels) and the
    // unchanged contract of Read.GetParkCampgroundByName (label prefixes added by the library). Each test uses its
    // own temporary SQLite database created from the EF model: no LocalApplicationData, network or embedded DB.
    public class CampgroundDetailsTests
    {
        private const string FullID = "991001";
        private const string FullName = "DETAILS FIXTURE CAMPGROUND";
        private const string SparseID = "991002";
        private const string SparseName = "SPARSE FIXTURE CAMPGROUND";
        private const string DisabledID = "991003";
        private const string DisabledName = "DISABLED FIXTURE CAMPGROUND";

        private string ConfigPath = string.Empty;

        [SetUp]
        public void Setup()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronCampgroundDetailsTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = FullID,
                    FacilityName = FullName,
                    FacilityDescription = "<h2>Overview</h2><p>A quiet place by the river.</p>",
                    FacilityDirections = "<p>Turn left at the store.</p>",
                    FacilityUseFeeDescription = "<p>$20 per night.</p>",
                    FacilityPhone = "555-0100",
                    FacilityEmail = "camp@example.invalid",
                    FacilityTypeDescription = "Campground",
                    Reservable = true,
                    Enabled = true
                });
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = SparseID,
                    FacilityName = SparseName,
                    FacilityDescription = "",
                    FacilityTypeDescription = "Campground",
                    Reservable = true,
                    Enabled = true
                });
                db.FacilitiesEntries.Add(new FacilitiesData
                {
                    FacilityID = DisabledID,
                    FacilityName = DisabledName,
                    FacilityDescription = "<p>Closed</p>",
                    FacilityTypeDescription = "Campground",
                    Reservable = true,
                    Enabled = false
                });
                db.SaveChanges();
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

        [Test]
        public void GetCampgroundDetailsByName_ReturnsUnprefixedValuesAndPlainText()
        {
            FacilitiesData? Details = Read.GetCampgroundDetailsByName(FullName, ConfigPath);

            Assert.That(Details, Is.Not.Null);
            Assert.That(Details!.FacilityID, Is.EqualTo(FullID));
            Assert.That(Details.FacilityName, Is.EqualTo(FullName));
            Assert.That(Details.FacilityPhone, Is.EqualTo("555-0100"));
            Assert.That(Details.FacilityEmail, Is.EqualTo("camp@example.invalid"));
            Assert.That(Details.FacilityDescription, Does.Contain("A quiet place by the river."));
            Assert.That(Details.FacilityDescription, Does.Contain("Overview"), "the source heading is content, not rewritten");
            Assert.That(Details.FacilityDescription, Does.Not.Contain("Description:"));
            Assert.That(Details.FacilityDescription, Does.Not.Contain("<"));
            Assert.That(Details.FacilityDirections!.Trim(), Is.EqualTo("Turn left at the store."));
            Assert.That(Details.FacilityUseFeeDescription!.Trim(), Is.EqualTo("$20 per night."));
        }

        [Test]
        public void GetCampgroundDetailsByName_MatchesNameLikeTheExistingLookup()
        {
            Assert.That(Read.GetCampgroundDetailsByName("  details fixture campground ", ConfigPath)?.FacilityID, Is.EqualTo(FullID));
        }

        [Test]
        public void GetCampgroundDetailsByName_KeepsMissingValuesMissing()
        {
            FacilitiesData? Details = Read.GetCampgroundDetailsByName(SparseName, ConfigPath);

            Assert.That(Details, Is.Not.Null);
            Assert.That(Details!.FacilityID, Is.EqualTo(SparseID));
            Assert.That(Details.FacilityPhone, Is.Null);
            Assert.That(Details.FacilityEmail, Is.Null);
            Assert.That(Details.FacilityDirections, Is.Null);
            Assert.That(Details.FacilityUseFeeDescription, Is.Null);
            Assert.That(Details.FacilityDescription, Is.Empty);
        }

        [Test]
        public void GetCampgroundDetailsByName_ReturnsNullForNoNameUnknownNameOrDisabledFacility()
        {
            Assert.That(Read.GetCampgroundDetailsByName(null, ConfigPath), Is.Null);
            Assert.That(Read.GetCampgroundDetailsByName("NO SUCH CAMPGROUND", ConfigPath), Is.Null);
            Assert.That(Read.GetCampgroundDetailsByName(DisabledName, ConfigPath), Is.Null);
        }

        // Pins the existing contract, which beta.8 deliberately leaves unchanged (decision D28).
        [Test]
        public void GetParkCampgroundByName_StillReturnsLabelPrefixedValues()
        {
            FacilitiesData? Entry = Read.GetParkCampgroundByName(FullName, ConfigPath);

            Assert.That(Entry, Is.Not.Null);
            Assert.That(Entry!.FacilityID, Is.EqualTo("ID:         " + FullID));
            Assert.That(Entry.FacilityName, Is.EqualTo("Name:   " + FullName));
            Assert.That(Entry.FacilityPhone, Is.EqualTo("Phone:  555-0100"));
            Assert.That(Entry.FacilityEmail, Is.EqualTo("Email:    camp@example.invalid"));
            Assert.That(Entry.FacilityDescription, Does.Contain("Description:\r\n"));
            Assert.That(Entry.FacilityDescription, Does.Not.Contain("Overview"));
            Assert.That(Entry.FacilityDirections, Does.StartWith("Directions: "));
            Assert.That(Entry.FacilityUseFeeDescription, Does.StartWith("UseFee: "));
        }

        [Test]
        public void GetParkCampgroundByName_StillReturnsAnEmptyEntryWhenNothingMatches()
        {
            FacilitiesData? Missing = Read.GetParkCampgroundByName("NO SUCH CAMPGROUND", ConfigPath);
            Assert.That(Missing, Is.Not.Null);
            Assert.That(Missing!.FacilityID, Is.Null);

            FacilitiesData? NoName = Read.GetParkCampgroundByName(null!, ConfigPath);
            Assert.That(NoName, Is.Not.Null);
            Assert.That(NoName!.FacilityID, Is.Null);

            // Prefixes are applied whenever the description is non-null, even when it is empty (existing behavior).
            FacilitiesData? Sparse = Read.GetParkCampgroundByName(SparseName, ConfigPath);
            Assert.That(Sparse!.FacilityID, Is.EqualTo("ID:         " + SparseID));
        }
    }
}
