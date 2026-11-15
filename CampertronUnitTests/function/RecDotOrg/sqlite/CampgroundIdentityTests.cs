using System.Diagnostics;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // The FacilityID-based campground queries the GUI uses since decision D42 (TD-38): Read.CampgroundChoicesByState,
    // CampgroundChoicesByStateAndCity, ParksWithCampgrounds, CampgroundChoicesByPark and GetCampgroundDetailsById. One
    // temporary SQLite database created from the EF model for the whole class (the tests only read it): no
    // LocalApplicationData, network or embedded database. Rows are inserted out of order, so the list order comes from
    // the queries, not from the insertion.
    public class CampgroundIdentityTests
    {
        private const string Dash = " \u2014 ";
        private const string Park = "Identity Park";
        private const string OtherPark = "Other Park";
        private const string AddresslessPark = "Addressless Park";
        private const string Springtown = "SPRINGTOWN";

        private string ConfigPath = string.Empty;

        // the identity park in list order, with each entry's label
        private static readonly (string ID, string Label)[] IdentityPark =
        {
            ("992008", "BLANK STATE CAMP" + Dash + "ID 992008"),
            ("0992011", "LEADING ZERO CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 0992011"),
            ("992009", "MAILING CAMP" + Dash + "ID 992009"),
            ("992010", "NO ADDRESS CAMP" + Dash + "ID 992010"),
            ("992006", "NO CITY CAMP" + Dash + "UT" + Dash + "ID 992006"),
            ("992007", "NULL CITY CAMP" + Dash + "UT" + Dash + "ID 992007"),
            ("992002", "TWIN CAMP" + Dash + "DENVERTOWN, CO" + Dash + "ID 992002"),
            ("992001", "TWIN CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992001"),
            ("992003", "twin camp" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992003"),
            ("992012", "TWO ADDRESS CAMP" + Dash + "DENVERTOWN, CO" + Dash + "ID 992012"),
            ("992005", "Unnamed campground" + Dash + "ID 992005"),
            ("992004", "Unnamed campground" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992004"),
        };

        [OneTimeSetUp]
        public void CreateDatabase()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronCampgroundIdentityTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "992102", RecAreaName = OtherPark });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "992101", RecAreaName = Park });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "992103", RecAreaName = AddresslessPark });

                Add(db, "992017", "OTHER PARK CAMP", "992102", ("Physical", "OTHERTOWN", "UT"));
                Add(db, "992012", "TWO ADDRESS CAMP", "992101", ("Default", Springtown, "UT"), ("Physical", "DENVERTOWN", "CO"));
                Add(db, "992003", "twin camp ", "992101", ("Physical", Springtown, "UT"));
                Add(db, "992004", "", "992101", ("Physical", Springtown, "UT"));
                Add(db, "992002", "TWIN CAMP", "992101", ("Physical", "DENVERTOWN", "CO"));
                Add(db, "992005", "   ", "992101");
                Add(db, "992006", "NO CITY CAMP", "992101", ("Physical", "", "UT"));
                Add(db, "992001", "TWIN CAMP", "992101", ("Physical", Springtown, "UT"));
                Add(db, "992007", "NULL CITY CAMP", "992101", ("Default", null, "UT"));
                Add(db, "992008", "BLANK STATE CAMP", "992101", ("Physical", "NOWHERE", ""));
                Add(db, "992009", "MAILING CAMP", "992101", ("Mailing", Springtown, "UT"));
                Add(db, "992010", "NO ADDRESS CAMP", "992101");
                Add(db, "0992011", "LEADING ZERO CAMP", "992101", ("Physical", Springtown, "UT"));
                Add(db, "992016", "ORPHAN CAMP", null, ("Physical", Springtown, "UT"));
                Add(db, "992018", "ADDRESSLESS CAMP", "992103");
                // reservable, enabled facilities that are not listable campgrounds, all named like the twins
                Add(db, "992013", "TWIN CAMP", "992101", ("Physical", Springtown, "UT")).FacilityTypeDescription = "Facility";
                Add(db, "992014", "TWIN CAMP", "992101", ("Physical", Springtown, "UT")).Enabled = false;
                Add(db, "992015", "TWIN CAMP", "992101", ("Physical", Springtown, "UT")).Reservable = false;
                db.SaveChanges();
            }
            SqliteConnection.ClearAllPools();
        }

        [OneTimeTearDown]
        public void DeleteDatabase()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(ConfigPath))
            {
                Directory.Delete(ConfigPath, true);
            }
        }

        private static FacilitiesData Add(RecreationDotOrgContext db, string FacilityID, string FacilityName, string? RecAreaID,
            params (string Type, string? City, string State)[] Addresses)
        {
            var Facility = new FacilitiesData
            {
                FacilityID = FacilityID,
                FacilityName = FacilityName,
                FacilityDescription = "<p>Description of " + FacilityID + "</p>",
                FacilityPhone = "555-" + FacilityID,
                FacilityTypeDescription = "Campground",
                ParentRecAreaID = RecAreaID,
                Reservable = true,
                Enabled = true
            };
            db.FacilitiesEntries.Add(Facility);
            int Index = 0;
            foreach (var (Type, City, State) in Addresses)
            {
                db.FacilityAddressesEntries.Add(new FacilityAddressesRecdata
                {
                    FacilityAddressID = "A-" + FacilityID + "-" + Index++,
                    FacilityID = FacilityID,
                    FacilityAddressType = Type,
                    City = City,
                    AddressStateCode = State,
                    PostalCode = "84000"
                });
            }
            return Facility;
        }

        // 1, 3, 4, 5, 8, 9: one choice per FacilityID, every ID kept, duplicates separate, blank names usable, the
        // campgrounds without a qualifying address included (without a location), in ListOrder.
        [Test]
        public void ByPark_ListsEveryCampgroundOncePerFacilityID_InListOrder()
        {
            List<CampgroundChoice> Choices = Read.CampgroundChoicesByPark(Park, ConfigPath);

            Assert.That(Choices.Select(c => c.FacilityID), Is.EqualTo(IdentityPark.Select(e => e.ID)));
            Assert.That(Choices.Select(c => c.Label), Is.EqualTo(IdentityPark.Select(e => e.Label)));
            Assert.That(Choices.Select(c => c.ToString()), Is.EqualTo(IdentityPark.Select(e => e.Label)));
            Assert.That(Choices.Count(c => string.Equals(c.Name, "TWIN CAMP", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(3));
            Assert.That(Choices.Where(c => c.IsUnnamed).Select(c => c.DisplayName), Is.All.EqualTo(CampgroundChoice.UnnamedName));
            Assert.That(Choices.Single(c => c.FacilityID == "992010").Location, Is.Null, "no address");
            Assert.That(Choices.Single(c => c.FacilityID == "992009").Location, Is.Null, "only a Mailing address");
            Assert.That(Choices.Single(c => c.FacilityID == "992008").Location, Is.Null, "an address with a blank state");
            // one entry for a campground with two qualifying addresses, located by its Physical one
            Assert.That(Choices.Single(c => c.FacilityID == "992012").Location, Is.EqualTo("DENVERTOWN, CO"));
            // the same query again gives the same list
            Assert.That(Read.CampgroundChoicesByPark(Park, ConfigPath).Select(c => c.Label), Is.EqualTo(Choices.Select(c => c.Label)));
        }

        // 6, 7, 8: the location filters keep their meaning: only campgrounds with a Physical or Default address there,
        // located by that address (also for a campground with addresses in two states).
        [Test]
        public void ByStateAndCity_KeepTheirLocationFilters()
        {
            Assert.That(Read.CampgroundChoicesByState("UT", ConfigPath).Select(c => c.Label), Is.EqualTo(new[]
            {
                "LEADING ZERO CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 0992011",
                "NO CITY CAMP" + Dash + "UT" + Dash + "ID 992006",
                "NULL CITY CAMP" + Dash + "UT" + Dash + "ID 992007",
                "ORPHAN CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992016",
                "OTHER PARK CAMP" + Dash + "OTHERTOWN, UT" + Dash + "ID 992017",
                "TWIN CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992001",
                "twin camp" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992003",
                "TWO ADDRESS CAMP" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992012",
                "Unnamed campground" + Dash + "SPRINGTOWN, UT" + Dash + "ID 992004",
            }));
            Assert.That(Read.CampgroundChoicesByState("CO", ConfigPath).Select(c => c.FacilityID), Is.EqualTo(new[] { "992002", "992012" }));
            Assert.That(Read.CampgroundChoicesByState("CO", ConfigPath)[1].Location, Is.EqualTo("DENVERTOWN, CO"));
            Assert.That(Read.CampgroundChoicesByStateAndCity("UT", Springtown, ConfigPath).Select(c => c.FacilityID),
                Is.EqualTo(new[] { "0992011", "992016", "992001", "992003", "992012", "992004" }));
            Assert.That(Read.CampgroundChoicesByStateAndCity("UT", "", ConfigPath).Select(c => c.Label),
                Is.EqualTo(new[] { "NO CITY CAMP" + Dash + "UT" + Dash + "ID 992006" }));
            // nothing to filter by: no query, no entries
            Assert.That(Read.CampgroundChoicesByState(null, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByStateAndCity("UT", null, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByStateAndCity(null, Springtown, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByPark(null, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByState("ZZ", ConfigPath), Is.Empty);
        }

        // 8: a park is not a location filter: it is listed when it has a campground, with or without an address
        // (UniqueCampgrounds, kept for its callers, needs an address).
        [Test]
        public void ParksWithCampgrounds_IncludesAParkWhoseCampgroundsHaveNoAddress()
        {
            Assert.That(Read.ParksWithCampgrounds(ConfigPath), Is.EqualTo(new[] { AddresslessPark, Park, OtherPark }));
            Assert.That(Read.UniqueCampgrounds(ConfigPath), Is.EqualTo(new[] { Park, OtherPark }));
            Assert.That(Read.CampgroundChoicesByPark(AddresslessPark, ConfigPath).Select(c => c.Label),
                Is.EqualTo(new[] { "ADDRESSLESS CAMP" + Dash + "ID 992018" }));
        }

        // 2, 10, 11, 12: the details of exactly the given FacilityID, matched exactly (no trimming, no numeric parsing,
        // leading zeros kept); a campground with the same name never answers for another; anything else is null.
        [Test]
        public void DetailsById_ReturnExactlyThatCampground()
        {
            foreach (string FacilityID in new[] { "992001", "992002", "992003", "0992011", "992004", "992005", "992010" })
            {
                FacilitiesData? Details = Read.GetCampgroundDetailsById(FacilityID, ConfigPath);
                Assert.That(Details, Is.Not.Null, FacilityID);
                Assert.That(Details!.FacilityID, Is.EqualTo(FacilityID));
                Assert.That(Details.FacilityDescription?.Trim(), Is.EqualTo("Description of " + FacilityID), "HTML converted to text");
                Assert.That(Details.FacilityPhone, Is.EqualTo("555-" + FacilityID));
            }
            Assert.That(Read.GetCampgroundDetailsById("992003", ConfigPath)!.FacilityName, Is.EqualTo("twin camp "), "stored values are not changed");

            string?[] NotACampground =
            {
                null, "", " ", "999999", "992011", "0992001", " 992001", "992001 ", "992001abc", "992001.0", "0", "TWIN CAMP",
                "992013", "992014", "992015", "992002' OR '1'='1",
            };
            foreach (string? FacilityID in NotACampground)
            {
                Assert.That(Read.GetCampgroundDetailsById(FacilityID, ConfigPath), Is.Null, FacilityID ?? "null");
            }
        }

        // 13, 14: every value is a parameter, never part of the SQL text, and each call runs exactly one command, however
        // many entries it returns.
        [Test]
        public void Queries_AreParameterized_AndRunOneCommandEach()
        {
            const string Hostile = "Z' OR '1'='1";
            using var Commands = new SqlRecorder();

            Assert.That(Read.CampgroundChoicesByState(Hostile, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByStateAndCity("UT", Hostile, ConfigPath), Is.Empty);
            Assert.That(Read.CampgroundChoicesByPark(Hostile, ConfigPath), Is.Empty);
            Assert.That(Read.GetCampgroundDetailsById(Hostile, ConfigPath), Is.Null);
            List<(string Sql, List<object?> Values)> Recorded = Commands.Take();
            Assert.That(Recorded, Has.Count.EqualTo(4));
            foreach (var (Sql, Values) in Recorded)
            {
                Assert.That(Sql, Does.Not.Contain("OR '1'"), "a value was written into the SQL");
                Assert.That(Values, Does.Contain(Hostile));
            }

            Assert.That(Read.CampgroundChoicesByPark(Park, ConfigPath), Has.Count.EqualTo(IdentityPark.Length));
            Assert.That(Commands.Take(), Has.Count.EqualTo(1), "by park");
            Assert.That(Read.CampgroundChoicesByState("UT", ConfigPath), Has.Count.EqualTo(9));
            Assert.That(Commands.Take(), Has.Count.EqualTo(1), "by state");
            Assert.That(Read.CampgroundChoicesByStateAndCity("UT", Springtown, ConfigPath), Has.Count.EqualTo(6));
            Assert.That(Commands.Take(), Has.Count.EqualTo(1), "by state and city");
            Assert.That(Read.ParksWithCampgrounds(ConfigPath), Has.Count.EqualTo(3));
            Assert.That(Commands.Take(), Has.Count.EqualTo(1), "parks");
            Assert.That(Read.GetCampgroundDetailsById("992001", ConfigPath), Is.Not.Null);
            var Details = Commands.Take();
            Assert.That(Details, Has.Count.EqualTo(1), "details");
            Assert.That(Details[0].Sql, Does.Not.Contain("FacilityName\" ="), "details are looked up by name");
            Assert.That(Details[0].Values, Is.EqualTo(new object?[] { "992001" }));
            Assert.That(Read.GetCampgroundDetailsById(" ", ConfigPath), Is.Null);
            Assert.That(Read.CampgroundChoicesByState(null, ConfigPath), Is.Empty);
            Assert.That(Commands.Take(), Is.Empty, "a blank ID or no filter runs no query");
        }

        // 15: the name-based methods are unchanged for their callers (database validation, ReleaseCheck, tests): they
        // still list a shared name once and look a name up as the first matching facility of any type, which is why the
        // GUI no longer uses them.
        [Test]
        public void NameBasedMethods_AreUnchanged()
        {
            Assert.That(Read.UniqueCampgroundsByPark(Park, ConfigPath), Is.EqualTo(new[]
            {
                "", "LEADING ZERO CAMP", "NO CITY CAMP", "NULL CITY CAMP", "TWIN CAMP", "TWO ADDRESS CAMP", "twin camp ",
            }));
            Assert.That(Read.UniqueParks("UT", Springtown, ConfigPath), Is.EqualTo(new[]
            {
                "", "LEADING ZERO CAMP", "ORPHAN CAMP", "TWIN CAMP", "TWO ADDRESS CAMP", "twin camp ",
            }));
            Assert.That(Read.UniqueParksByState("CO", ConfigPath), Is.EqualTo(new[] { "TWIN CAMP", "TWO ADDRESS CAMP" }));
            FacilitiesData? ByName = Read.GetCampgroundDetailsByName("TWIN CAMP", ConfigPath);
            Assert.That(ByName?.FacilityID, Is.AnyOf("992001", "992002", "992013"), "the first reservable, enabled facility of that name");
        }

        // Records the SQL text and parameter values of the EF Core commands executed on this thread.
        internal sealed class SqlRecorder : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
        {
            private readonly int _Thread = Environment.CurrentManagedThreadId;
            private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
            private readonly List<(string Sql, List<object?> Values)> _Commands = new List<(string, List<object?>)>();

            public SqlRecorder()
            {
                _Subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
            }

            public List<(string Sql, List<object?> Values)> Take()
            {
                lock (_Commands)
                {
                    var Taken = _Commands.ToList();
                    _Commands.Clear();
                    return Taken;
                }
            }

            public void OnNext(DiagnosticListener Listener)
            {
                if (Listener.Name == "Microsoft.EntityFrameworkCore")
                {
                    lock (_Subscriptions)
                    {
                        _Subscriptions.Add(Listener.Subscribe(this));
                    }
                }
            }

            public void OnNext(KeyValuePair<string, object?> Event)
            {
                if (Event.Key == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuting"
                    && Environment.CurrentManagedThreadId == _Thread
                    && Event.Value is Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData Data)
                {
                    lock (_Commands)
                    {
                        _Commands.Add((Data.Command.CommandText,
                            Data.Command.Parameters.Cast<System.Data.Common.DbParameter>().Select(p => (object?)p.Value).ToList()));
                    }
                }
            }

            public void OnCompleted() { }
            public void OnError(Exception error) { }

            public void Dispose()
            {
                lock (_Subscriptions)
                {
                    foreach (IDisposable Subscription in _Subscriptions)
                    {
                        Subscription.Dispose();
                    }
                }
            }
        }
    }
}
