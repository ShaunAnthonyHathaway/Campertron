using System.Diagnostics;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // The search editor's campground picker (CM6, decision D44; TD-53, open question #41): Read.CampgroundPickerEntries
    // lists every reservable, enabled campground once per FacilityID, also without a park or a usable address, in one
    // query, and CampgroundPickerEntry.Filter narrows it by name, city, state, park or exact ID. A fixture database
    // created from the EF model for the class; one test reads a copy of the shipped database in a temporary folder.
    public class CampgroundPickerTests
    {
        private const string Dash = " \u2014 ";
        private string ConfigPath = string.Empty;

        // every listable campground of the fixture, in list order: (ID, label, park)
        private static readonly (string ID, string Label, string? Park)[] Expected =
        {
            ("993006", "ADDRESSLESS CAMP" + Dash + "ID 993006", "Picker Park"),
            ("993010", "DANGLING PARK CAMP" + Dash + "ID 993010", null),
            ("0993011", "LEADING ZERO CAMP" + Dash + "PINETOWN, UT" + Dash + "ID 0993011", "Picker Park"),
            ("993005", "ORPHAN CAMP" + Dash + "ID 993005", null),
            ("993009", "ORPHAN WITH ADDRESS" + Dash + "PINETOWN, UT" + Dash + "ID 993009", null),
            ("993002", "TWIN CAMP" + Dash + "LAKETOWN, CO" + Dash + "ID 993002", "Picker Park"),
            ("993001", "TWIN CAMP" + Dash + "PINETOWN, UT" + Dash + "ID 993001", "Picker Park"),
            ("993003", "TWIN CAMP" + Dash + "PINETOWN, UT" + Dash + "ID 993003", "Other Picker Park"),
            ("993004", "Unnamed campground" + Dash + "ID 993004", null),
        };

        [OneTimeSetUp]
        public void CreateDatabase()
        {
            ConfigPath = Path.Join(Path.GetTempPath(), "CampertronCampgroundPickerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                db.Database.EnsureCreated();
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "993101", RecAreaName = "Picker Park" });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "993102", RecAreaName = "Other Picker Park" });
                db.RecAreaEntries.Add(new RecAreaFacilityRecData { RecAreaID = "993103", RecAreaName = "  " });
                Add(db, "993003", "TWIN CAMP", "993102", ("Physical", "PINETOWN", "UT"));
                Add(db, "993001", "TWIN CAMP", "993101", ("Physical", "PINETOWN", "UT"), ("Default", "ELSEWHERE", "AZ"));
                Add(db, "993002", "TWIN CAMP", "993101", ("Default", "LAKETOWN", "CO"));
                // the TD-53 kind: no park and no qualifying address, a blank name among them
                Add(db, "993004", "   ", "993103", ("Mailing", "PINETOWN", "UT"));
                Add(db, "993005", "ORPHAN CAMP", null);
                Add(db, "993006", "ADDRESSLESS CAMP", "993101");
                Add(db, "993009", "ORPHAN WITH ADDRESS", null, ("Physical", "PINETOWN", "UT"));
                Add(db, "993010", "DANGLING PARK CAMP", "993999");
                Add(db, "0993011", "LEADING ZERO CAMP", "993101", ("Physical", "PINETOWN", "UT"));
                // never listed: another kind of facility, disabled, not reservable
                Add(db, "993012", "TWIN CAMP", "993101", ("Physical", "PINETOWN", "UT")).FacilityTypeDescription = "Facility";
                Add(db, "993013", "TWIN CAMP", "993101", ("Physical", "PINETOWN", "UT")).Enabled = false;
                Add(db, "993014", "TWIN CAMP", "993101", ("Physical", "PINETOWN", "UT")).Reservable = false;
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
                    FacilityAddressID = "P-" + FacilityID + "-" + Index++,
                    FacilityID = FacilityID,
                    FacilityAddressType = Type,
                    City = City,
                    AddressStateCode = State,
                    PostalCode = "84000"
                });
            }
            return Facility;
        }

        // 43, 44, 45, 50, 42: every reservable, enabled campground once, duplicates separate, unnamed and location-less
        // ones included (nothing invented), FacilityIDs exactly as stored, in list order
        [Test]
        public void ThePicker_ListsEveryReservableEnabledCampground_OncePerFacilityID()
        {
            List<CampgroundPickerEntry> Entries = Read.CampgroundPickerEntries(ConfigPath);

            Assert.That(Entries.Select(E => E.FacilityID), Is.EqualTo(Expected.Select(E => E.ID)));
            Assert.That(Entries.Select(E => E.Label), Is.EqualTo(Expected.Select(E => E.Label)));
            Assert.That(Entries.Select(E => E.Park), Is.EqualTo(Expected.Select(E => E.Park)));
            Assert.That(Entries.Select(E => E.Choice), Is.Ordered.Using(CampgroundChoice.ListOrder));
            Assert.That(Entries.Single(E => E.FacilityID == "993001").Choice.Location, Is.EqualTo("PINETOWN, UT"), "the Physical address");
            Assert.That(Entries.Single(E => E.FacilityID == "993004").Choice.IsUnnamed, Is.True);
            Assert.That(Entries.Single(E => E.FacilityID == "993004").Choice.Location, Is.Null, "a Mailing address is no location");
            Assert.That(Entries.Single(E => E.FacilityID == "993004").Park, Is.Null, "a blank park name is no park");
            Assert.That(Entries.Single(E => E.FacilityID == "993004").AccessibleName, Is.EqualTo("Unnamed campground" + Dash + "ID 993004"));
            Assert.That(Entries.Single(E => E.FacilityID == "993001").AccessibleName, Is.EqualTo("TWIN CAMP" + Dash + "PINETOWN, UT" + Dash + "ID 993001" + Dash + "park Picker Park"));
            Assert.That(Entries.Count(E => E.Choice.Name == "TWIN CAMP"), Is.EqualTo(3));
            Assert.That(Entries.Select(E => E.FacilityID).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(Entries.Count));
            Assert.That(Read.CampgroundPickerEntries(ConfigPath).Select(E => E.Label), Is.EqualTo(Entries.Select(E => E.Label)), "the same list again");
        }

        // 51: one parameterless query for the whole list (no query per campground)
        [Test]
        public void ThePicker_ReadsTheListWithOneQuery()
        {
            using var Recorder = new CampgroundIdentityTests.SqlRecorder();
            Read.CampgroundPickerEntries(ConfigPath);
            List<(string Sql, List<object?> Values)> Commands = Recorder.Take();
            Assert.That(Commands, Has.Count.EqualTo(1));
            Assert.That(Commands[0].Values, Is.Empty);
            Assert.That(Commands[0].Sql, Does.Contain("LEFT JOIN"));
        }

        // the filter: each word is part of the name, city, state or park (ignoring case), or the whole text is exactly a
        // FacilityID (listed first); part of an ID never matches
        [TestCase("", new[] { "993006", "993010", "0993011", "993005", "993009", "993002", "993001", "993003", "993004" })]
        [TestCase("twin", new[] { "993002", "993001", "993003" })]
        [TestCase("twin pinetown", new[] { "993001", "993003" })]
        [TestCase("TWIN other", new[] { "993003" })]
        [TestCase("co", new[] { "993002" })]
        [TestCase("picker park addressless", new[] { "993006" })]
        [TestCase("unnamed", new[] { "993004" })]
        [TestCase("993003", new[] { "993003" })]
        [TestCase(" 993003 ", new[] { "993003" })]
        [TestCase("0993011", new[] { "0993011" })]
        [TestCase("993011", new string[0])]
        [TestCase("99300", new string[0])]
        [TestCase("nothing like it", new string[0])]
        public void TheFilter_MatchesWordsAndExactIDs(string Text, string[] IDs)
        {
            List<CampgroundPickerEntry> Entries = Read.CampgroundPickerEntries(ConfigPath);
            Assert.That(CampgroundPickerEntry.Filter(Entries, Text).Select(E => E.FacilityID), Is.EqualTo(IDs));
        }

        [Test]
        public void AnExactID_IsListedFirst_EvenIfItsNameMatchesLater()
        {
            var Entries = new List<CampgroundPickerEntry>
            {
                new CampgroundPickerEntry(new CampgroundChoice("1", "CAMP 42", null, null), null),
                new CampgroundPickerEntry(new CampgroundChoice("42", "ZULU CAMP", null, null), null),
            };
            Assert.That(CampgroundPickerEntry.Filter(Entries, "42").Select(E => E.FacilityID), Is.EqualTo(new[] { "42", "1" }));
        }

        // 50, TD-53: in the shipped database every reservable, enabled campground is in the picker, also each one without
        // a named park and without a qualifying address (22 when CM4 counted them) and every blank-name one; one query
        [Test]
        public void TheShippedDatabase_HasEveryReservableEnabledCampgroundInThePicker()
        {
            string Shipped = Path.Join(Path.GetTempPath(), "CampertronCampgroundPickerShipped-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Shipped);
            try
            {
                Load.ExtractEmbeddedDbTo(Path.Join(Shipped, "RecreationDotOrg.db"));
                var All = new HashSet<string>(StringComparer.Ordinal);
                var Unlisted = new HashSet<string>(StringComparer.Ordinal);
                var Blank = new HashSet<string>(StringComparer.Ordinal);
                using (var Connection = new SqliteConnection("Data Source=" + Path.Join(Shipped, "RecreationDotOrg.db") + ";Mode=ReadOnly"))
                {
                    Connection.Open();
                    using var Command = Connection.CreateCommand();
                    Command.CommandText =
                        "SELECT f.FacilityID, f.FacilityName, " +
                        "(SELECT COUNT(*) FROM RecAreaEntries r WHERE r.RecAreaID = f.ParentRecAreaID AND r.RecAreaName IS NOT NULL) AS Parks, " +
                        "(SELECT COUNT(*) FROM FacilityAddressesEntries a WHERE a.FacilityID = f.FacilityID AND a.AddressStateCode IS NOT NULL " +
                        "AND length(trim(a.AddressStateCode)) > 0 AND a.FacilityAddressType IN ('Physical', 'Default')) AS Addresses " +
                        "FROM FacilitiesEntries f WHERE f.FacilityTypeDescription = 'Campground' AND f.Reservable = 1 AND f.Enabled = 1";
                    using var Rows = Command.ExecuteReader();
                    while (Rows.Read())
                    {
                        string Id = Rows.GetString(0);
                        All.Add(Id);
                        if (Rows.GetInt64(2) == 0 && Rows.GetInt64(3) == 0)
                        {
                            Unlisted.Add(Id);
                        }
                        if (Rows.IsDBNull(1) || string.IsNullOrWhiteSpace(Rows.GetString(1)))
                        {
                            Blank.Add(Id);
                        }
                    }
                }
                SqliteConnection.ClearAllPools();

                using var Recorder = new CampgroundIdentityTests.SqlRecorder();
                var Clock = Stopwatch.StartNew();
                List<CampgroundPickerEntry> Entries = Read.CampgroundPickerEntries(Shipped);
                Clock.Stop();
                Assert.That(Recorder.Take(), Has.Count.EqualTo(1));
                TestContext.Progress.WriteLine($"CampgroundPickerEntries: {Entries.Count} campgrounds in {Clock.ElapsedMilliseconds} ms; " +
                    $"{Unlisted.Count} without a named park or a qualifying address; {Blank.Count} with a blank name");

                Assert.That(Entries.Select(E => E.FacilityID), Is.EquivalentTo(All));
                Assert.That(Entries.Select(E => E.FacilityID).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(Entries.Count));
                Assert.That(Unlisted, Is.Not.Empty);
                Assert.That(Unlisted, Is.SubsetOf(Entries.Select(E => E.FacilityID)));
                Assert.That(Entries.Where(E => Unlisted.Contains(E.FacilityID)).Select(E => (E.Park, E.Choice.Location)), Is.All.EqualTo(((string?)null, (string?)null)));
                Assert.That(Entries.Where(E => Blank.Contains(E.FacilityID)).Select(E => E.Choice.DisplayName), Is.All.EqualTo(CampgroundChoice.UnnamedName));
                // each of them can be found by its exact ID
                foreach (string Id in Unlisted)
                {
                    Assert.That(CampgroundPickerEntry.Filter(Entries, Id).First().FacilityID, Is.EqualTo(Id));
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(Shipped, true);
            }
        }
    }
}
