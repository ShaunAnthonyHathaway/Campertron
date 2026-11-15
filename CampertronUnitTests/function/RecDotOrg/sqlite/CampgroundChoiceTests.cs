namespace CampertronUnitTests.function.RecDotOrg.sqlite
{
    // CampgroundChoice (decision D42, TD-38): a GUI list entry identified by its FacilityID. The ID is kept exactly as
    // the database stores it (a string, never parsed as a number), equality uses nothing else, and the label follows the
    // rules "<Name> — <City>, <State> — ID <ID>", "<Name> — <State> — ID <ID>", "<Name> — ID <ID>", with
    // "Unnamed campground" for a blank name. Pure: no database.
    public class CampgroundChoiceTests
    {
        private const string Dash = " \u2014 ";

        [Test]
        public void TheFacilityID_IsKeptExactly_AndABlankOneIsRefused()
        {
            Assert.That(new CampgroundChoice("0990010", "A", null, null).FacilityID, Is.EqualTo("0990010"));
            Assert.That(new CampgroundChoice("0990010", "A", null, null).IdText, Is.EqualTo("ID 0990010"));
            Assert.That(new CampgroundChoice(" 12 ", "A", null, null).FacilityID, Is.EqualTo(" 12 "), "not trimmed");
            Assert.That(new CampgroundChoice("10138237", "A", null, null).FacilityID, Is.EqualTo("10138237"));
            foreach (string? Blank in new[] { null, "", " ", "\t" })
            {
                Assert.That(() => new CampgroundChoice(Blank!, "A", "CITY", "UT"), Throws.ArgumentException, Blank ?? "null");
            }
        }

        [Test]
        public void Equality_UsesTheFacilityIDOnly()
        {
            var A = new CampgroundChoice("990005", "TWIN", "IDENTITYVILLE", "UT");
            var SameId = new CampgroundChoice("990005", "Another name", null, "CO");
            var SameEverythingElse = new CampgroundChoice("990012", "TWIN", "IDENTITYVILLE", "UT");

            Assert.That(A, Is.EqualTo(SameId));
            Assert.That(A.GetHashCode(), Is.EqualTo(SameId.GetHashCode()));
            Assert.That(A, Is.Not.EqualTo(SameEverythingElse));
            Assert.That(A.Title, Is.EqualTo(SameEverythingElse.Title), "same name and place");
            Assert.That(new CampgroundChoice("0990010", "A", null, null), Is.Not.EqualTo(new CampgroundChoice("990010", "A", null, null)));
            Assert.That(A.Equals((object)"990005"), Is.False, "never equal to the ID text");
            Assert.That(A.Equals(null), Is.False);
            Assert.That(new HashSet<CampgroundChoice> { A, SameId, SameEverythingElse }, Has.Count.EqualTo(2));
        }

        [Test]
        public void Labels_FollowTheRules_AndTrimOnlyForDisplay()
        {
            Assert.That(new CampgroundChoice("1", "WATCHMAN CAMPGROUND", "SPRINGDALE", "UT").Label,
                Is.EqualTo("WATCHMAN CAMPGROUND" + Dash + "SPRINGDALE, UT" + Dash + "ID 1"));
            Assert.That(new CampgroundChoice("2", "NO CITY", null, "UT").Label, Is.EqualTo("NO CITY" + Dash + "UT" + Dash + "ID 2"));
            Assert.That(new CampgroundChoice("3", "EMPTY CITY", "  ", "UT").Label, Is.EqualTo("EMPTY CITY" + Dash + "UT" + Dash + "ID 3"));
            Assert.That(new CampgroundChoice("4", "NOWHERE", null, null).Label, Is.EqualTo("NOWHERE" + Dash + "ID 4"));
            // a city without a state is not a usable location (the queries never return one)
            Assert.That(new CampgroundChoice("5", "CITY ONLY", "SOMEWHERE", null).Label, Is.EqualTo("CITY ONLY" + Dash + "ID 5"));
            Assert.That(new CampgroundChoice("5", "CITY ONLY", "SOMEWHERE", " ").Location, Is.Null);
            foreach (string? Blank in new[] { null, "", "   ", "\t\n" })
            {
                var Unnamed = new CampgroundChoice("6", Blank, null, null);
                Assert.That(Unnamed.IsUnnamed, Is.True);
                Assert.That(Unnamed.Name, Is.Null);
                Assert.That(Unnamed.Label, Is.EqualTo("Unnamed campground" + Dash + "ID 6"));
            }
            Assert.That(new CampgroundChoice("7", "", "CITY", "UT").Label, Is.EqualTo("Unnamed campground" + Dash + "CITY, UT" + Dash + "ID 7"));
            var Padded = new CampgroundChoice("8", "  PADDED  NAME ", " TOWN ", " CO ");
            Assert.That(Padded.Name, Is.EqualTo("PADDED  NAME"), "only the ends are trimmed");
            Assert.That(Padded.Title, Is.EqualTo("PADDED  NAME" + Dash + "TOWN, CO"));
            Assert.That(Padded.ToString(), Is.EqualTo(Padded.Label));
        }

        [Test]
        public void ListOrder_IsByNameThenStateCityAndID_WithUnnamedCampgroundsLast()
        {
            var Expected = new[]
            {
                new CampgroundChoice("4", "ALPHA ", null, null),
                new CampgroundChoice("5", "alpha", "B", "CO"),
                new CampgroundChoice("3", "Alpha", "A", "UT"),
                new CampgroundChoice("2", "ALPHA", "B", "UT"),
                new CampgroundChoice("9", "ALPHA", "B", "UT"),
                new CampgroundChoice("10", "ALPHA", "B", "UT"),
                new CampgroundChoice("010", "ALPHA", "B", "UT"),
                new CampgroundChoice("1", "BRAVO", null, null),
                new CampgroundChoice("8", "  ", null, null),
                new CampgroundChoice("7", "", "A", "UT"),
            };
            var Rng = new Random(42);
            for (int Round = 0; Round < 20; Round++)
            {
                List<CampgroundChoice> Shuffled = Expected.OrderBy(_ => Rng.Next()).ToList();
                Shuffled.Sort(CampgroundChoice.ListOrder);
                Assert.That(Shuffled.Select(c => c.FacilityID), Is.EqualTo(Expected.Select(c => c.FacilityID)));
            }
        }
    }
}
