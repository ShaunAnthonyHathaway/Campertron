using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using Microsoft.Data.Sqlite;

namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // The values a search's include and exclude lists can be set to for one campground (Read.FilterChoicesByCampground,
    // decision D51, business-rules §22), on a copy of the shipped database: exactly the equipment, campsite types, campsite
    // names and "Name:Value" attributes of the campground's own campsites, as stored (never trimmed or case-changed), each
    // with its number of campsites, as an independent SQL reading of the same database gives them; nothing for a blank,
    // unknown or campsite-less campground; the same with and without Campertron's index.
    public class FilterChoicesTests
    {
        private string Indexed = string.Empty;
        private string Plain = string.Empty;

        [OneTimeSetUp]
        public void CopyShipped()
        {
            string Root = Path.Join(Path.GetTempPath(), "CampertronFilterChoicesTests-" + Guid.NewGuid().ToString("N"));
            Plain = Path.Join(Root, "plain");
            Indexed = Path.Join(Root, "indexed");
            Directory.CreateDirectory(Plain);
            Directory.CreateDirectory(Indexed);
            Load.ExtractEmbeddedDbTo(Path.Join(Plain, "RecreationDotOrg.db"));
            File.Copy(Path.Join(Plain, "RecreationDotOrg.db"), Path.Join(Indexed, "RecreationDotOrg.db"));
            Assert.That(RidbIndexes.Create(Path.Join(Indexed, "RecreationDotOrg.db")), Is.True);
        }

        [OneTimeTearDown]
        public void Remove()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(Path.GetDirectoryName(Plain)!, true);
        }

        private List<string[]> Sql(string Query, params (string Name, object Value)[] Parameters)
        {
            var Rows = new List<string[]>();
            using (var Connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Join(Indexed, "RecreationDotOrg.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                Connection.Open();
                using (var Command = Connection.CreateCommand())
                {
                    Command.CommandText = Query;
                    foreach (var (Name, Value) in Parameters)
                    {
                        Command.Parameters.AddWithValue(Name, Value);
                    }
                    using (var Reader = Command.ExecuteReader())
                    {
                        while (Reader.Read())
                        {
                            Rows.Add(Enumerable.Range(0, Reader.FieldCount).Select(i => Reader.IsDBNull(i) ? null! : Reader.GetValue(i).ToString()!).ToArray());
                        }
                    }
                }
            }
            return Rows;
        }

        private static List<string> Lines(IEnumerable<FilterChoice> Choices) => Choices.Select(C => C.Value + " #" + C.Campsites).OrderBy(L => L, StringComparer.Ordinal).ToList();
        private static List<string> Lines(IEnumerable<string[]> Rows) => Rows.Select(R => R[0] + " #" + R[1]).OrderBy(L => L, StringComparer.Ordinal).ToList();

        // the oracle: the same values by GROUP BY over the campground's campsites
        private void AssertAsTheDatabaseSays(string Facility)
        {
            CampgroundFilterChoices Choices = Read.FilterChoicesByCampground(Facility, Indexed);
            var Id = ("$id", (object)Facility);
            Assert.That(Choices.FacilityID, Is.EqualTo(Facility));
            Assert.That(Choices.Campsites.ToString(), Is.EqualTo(Sql("SELECT count(*) FROM CampsitesEntries WHERE FacilityID = $id", Id).Single()[0]));
            Assert.That(Lines(Choices.Equipment), Is.EqualTo(Lines(Sql(
                "SELECT e.EquipmentName, count(DISTINCT e.CampsiteID) FROM CampsitesEntries c JOIN PermittedEquipmentEntries e ON e.CampsiteID = c.CampsiteID WHERE c.FacilityID = $id GROUP BY e.EquipmentName", Id))), Facility);
            Assert.That(Lines(Choices.CampsiteTypes), Is.EqualTo(Lines(Sql(
                "SELECT CampsiteType, count(*) FROM CampsitesEntries WHERE FacilityID = $id AND CampsiteType IS NOT NULL GROUP BY CampsiteType", Id))), Facility);
            Assert.That(Lines(Choices.CampsiteNames), Is.EqualTo(Lines(Sql(
                "SELECT CampsiteName, count(*) FROM CampsitesEntries WHERE FacilityID = $id AND CampsiteName IS NOT NULL GROUP BY CampsiteName", Id))), Facility);
            Assert.That(Lines(Choices.Attributes), Is.EqualTo(Lines(Sql(
                "SELECT coalesce(a.AttributeName, '') || ':' || coalesce(a.AttributeValue, ''), count(DISTINCT a.EntityID) FROM CampsitesEntries c JOIN CampsiteAttributesEntries a ON a.EntityID = c.CampsiteID " +
                "WHERE c.FacilityID = $id GROUP BY 1", Id))), Facility);
            foreach (IReadOnlyList<FilterChoice> List in new[] { Choices.Equipment, Choices.CampsiteTypes, Choices.CampsiteNames, Choices.Attributes })
            {
                Assert.That(List, Is.Ordered.Using(FilterChoice.ListOrder));
                Assert.That(List.Select(C => C.Value), Is.Unique);
            }
            Assert.That(Choices.Attributes.All(C => C.Value == C.AttributeName + ":" + C.AttributeValue));
        }

        [TestCase("232445")] // Watchman: the example search's campground
        [TestCase("258830")] // the largest campground, 360 campsite records, duplicate names
        [TestCase("232490")] // Mather
        public void TheChoices_AreTheCampgroundsOwnValues(string Facility)
        {
            AssertAsTheDatabaseSays(Facility);
        }

        [Test]
        public void MeasuredFacts_OfTheShippedData()
        {
            CampgroundFilterChoices Watchman = Read.FilterChoicesByCampground("232445", Indexed);
            Assert.That(Watchman.CampsiteTypes.Select(C => (C.Value, C.Campsites)), Is.EqualTo(new[]
            {
                ("GROUP TENT ONLY AREA NONELECTRIC", 6), ("MANAGEMENT", 8), ("STANDARD ELECTRIC", 92), ("STANDARD NONELECTRIC", 68), ("WALK TO", 18)
            }));
            Assert.That(Watchman.Equipment, Has.Count.EqualTo(11));
            Assert.That(Watchman.Equipment.Single(C => C.Value == "Tent").Campsites, Is.EqualTo(186));
            Assert.That(Watchman.Equipment.Select(C => C.Value), Does.Contain("LARGE TENT OVER 9X12`"));

            CampgroundFilterChoices Largest = Read.FilterChoicesByCampground("258830", Indexed);
            Assert.That(Largest.Campsites, Is.EqualTo(360));
            Assert.That(Largest.CampsiteNames.Sum(C => C.Campsites), Is.EqualTo(360));
            Assert.That(Largest.CampsiteNames.Count, Is.LessThan(360), "some names belong to several campsite records");
            Assert.That(Largest.CampsiteNames.Any(C => C.Campsites > 1), Is.True);
        }

        [Test]
        public void Values_AreKeptAsStored_UntrimmedBlankColonsAndCase()
        {
            // a campground with an untrimmed campsite name, one with a blank name, one with a colon in an attribute value and
            // one with two attribute values that differ only by case, each found in the data
            string[] Untrimmed = Sql("SELECT FacilityID, CampsiteName FROM CampsitesEntries WHERE CampsiteName <> trim(CampsiteName) ORDER BY FacilityID, CampsiteName LIMIT 1").Single();
            Assert.That(Read.FilterChoicesByCampground(Untrimmed[0], Indexed).CampsiteNames.Select(C => C.Value), Does.Contain(Untrimmed[1]));
            Assert.That(Untrimmed[1], Is.Not.EqualTo(Untrimmed[1].Trim()));

            string[] Blank = Sql("SELECT FacilityID, CampsiteName FROM CampsitesEntries WHERE trim(CampsiteName) = '' ORDER BY FacilityID LIMIT 1").Single();
            Assert.That(Read.FilterChoicesByCampground(Blank[0], Indexed).CampsiteNames.Select(C => C.Value), Does.Contain(Blank[1]));

            string[] Colon = Sql("SELECT c.FacilityID, a.AttributeName, a.AttributeValue FROM CampsiteAttributesEntries a JOIN CampsitesEntries c ON c.CampsiteID = a.EntityID " +
                "WHERE instr(a.AttributeValue, ':') > 0 ORDER BY c.FacilityID LIMIT 1").Single();
            FilterChoice WithColon = Read.FilterChoicesByCampground(Colon[0], Indexed).Attributes.Single(C => C.AttributeName == Colon[1] && C.AttributeValue == Colon[2]);
            Assert.That(WithColon.Value, Is.EqualTo(Colon[1] + ":" + Colon[2]));
            AssertAsTheDatabaseSays(Colon[0]);

            string[] Cased = Sql("SELECT c.FacilityID, a.AttributeName FROM CampsiteAttributesEntries a JOIN CampsitesEntries c ON c.CampsiteID = a.EntityID " +
                "GROUP BY c.FacilityID, a.AttributeName, upper(a.AttributeValue) HAVING count(DISTINCT a.AttributeValue) > 1 ORDER BY c.FacilityID LIMIT 1").Single();
            List<FilterChoice> All = Read.FilterChoicesByCampground(Cased[0], Indexed).Attributes.ToList();
            var Groups = All.Where(C => C.AttributeName == Cased[1]).GroupBy(C => C.AttributeValue, StringComparer.OrdinalIgnoreCase).Where(G => G.Count() > 1).ToList();
            Assert.That(Groups, Is.Not.Empty, "two spellings, both kept");
            foreach (var Group in Groups)
            {
                List<int> At = Group.Select(C => All.IndexOf(C)).OrderBy(i => i).ToList();
                Assert.That(At.Last() - At.First(), Is.EqualTo(At.Count - 1), "case variants are next to each other");
            }
            AssertAsTheDatabaseSays(Cased[0]);
        }

        [Test]
        public void RowsOfCampsitesThatAreGone_AreNeverOffered()
        {
            // the shipped data has equipment and attribute rows whose campsite is not in CampsitesEntries
            Assert.That(int.Parse(Sql("SELECT count(*) FROM PermittedEquipmentEntries e WHERE NOT EXISTS (SELECT 1 FROM CampsitesEntries c WHERE c.CampsiteID = e.CampsiteID)").Single()[0]), Is.GreaterThan(0));
            Assert.That(int.Parse(Sql("SELECT count(*) FROM CampsiteAttributesEntries a WHERE NOT EXISTS (SELECT 1 FROM CampsitesEntries c WHERE c.CampsiteID = a.EntityID)").Single()[0]), Is.GreaterThan(0));
            // the choices count only the campground's campsites: never more campsites than it has
            CampgroundFilterChoices Choices = Read.FilterChoicesByCampground("258830", Indexed);
            Assert.That(Choices.Equipment.Concat(Choices.Attributes).All(C => C.Campsites <= Choices.Campsites));
        }

        [Test]
        public void ABlankUnknownOrCampsitelessCampground_HasNoChoices()
        {
            // a blank ID runs no query: a folder without a database is never opened (or created)
            string NoDatabase = Path.Join(Path.GetTempPath(), "CampertronNoDb-" + Guid.NewGuid().ToString("N"));
            foreach (string? Blank in new[] { null, "", "  " })
            {
                CampgroundFilterChoices None = Read.FilterChoicesByCampground(Blank, NoDatabase);
                Assert.That(None.Campsites + None.Equipment.Count + None.CampsiteTypes.Count + None.CampsiteNames.Count + None.Attributes.Count, Is.Zero);
            }
            Assert.That(Directory.Exists(NoDatabase), Is.False);

            string Campsiteless = Sql("SELECT f.FacilityID FROM FacilitiesEntries f WHERE f.FacilityTypeDescription = 'Campground' AND f.Reservable = 1 AND f.Enabled = 1 " +
                "AND NOT EXISTS (SELECT 1 FROM CampsitesEntries c WHERE c.FacilityID = f.FacilityID) ORDER BY f.FacilityID LIMIT 1").Single()[0];
            foreach (string Id in new[] { Campsiteless, "999999999", " 232445", "232445 ", "0232445", "2324" })
            {
                CampgroundFilterChoices None = Read.FilterChoicesByCampground(Id, Indexed);
                Assert.That((None.FacilityID, None.Campsites, None.Equipment.Count, None.CampsiteNames.Count, None.Attributes.Count), Is.EqualTo((Id, 0, 0, 0, 0)), Id);
            }
        }

        [Test]
        public void WithoutTheIndex_TheChoicesAreTheSame()
        {
            foreach (string Facility in new[] { "232445", "258830" })
            {
                CampgroundFilterChoices A = Read.FilterChoicesByCampground(Facility, Indexed);
                CampgroundFilterChoices B = Read.FilterChoicesByCampground(Facility, Plain);
                Assert.That(Lines(B.Equipment.Concat(B.CampsiteTypes).Concat(B.CampsiteNames).Concat(B.Attributes)),
                    Is.EqualTo(Lines(A.Equipment.Concat(A.CampsiteTypes).Concat(A.CampsiteNames).Concat(A.Attributes))));
            }
            Assert.That(RidbIndexes.Exists(Path.Join(Plain, "RecreationDotOrg.db")), Is.False, "reading never creates the index");
        }
    }
}
