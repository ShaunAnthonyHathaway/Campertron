using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // The eight include and exclude lists through the real search (Plan, Run, ProcessMonth, FilterData) on Watchman's real
    // campsite data (decision D51, business-rules §3): every campsite is open on one night, and the campsites found must be
    // exactly what an independent reading of OR semantics and filterMatching gives from the same cached data. The
    // discriminating cases show that the rules before D51 (AND for excludeEquipment, excludeAttributes and
    // includeCampsiteType) would give other campsites.
    public class FilterSemanticsTests : SearchTestBase
    {
        private sealed record Campsite(string Id, string? Name, string? Type, List<string> Equipment, List<string> Attributes);

        private List<Campsite> Watchman = new();
        private DateTime Night;

        [SetUp]
        public void SetUp()
        {
            Watchman = SearchFixture.Sites[SearchFixture.Watchman].Select(S => new Campsite(S.CampsiteID!, S.CampsiteName, S.CampsiteType,
                JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Join(SearchFixture.CachePath, S.CampsiteID + "-Equipment.json")))!,
                JsonSerializer.Deserialize<List<AttributeValuePair>>(File.ReadAllText(Path.Join(SearchFixture.CachePath, S.CampsiteID + "-Attributes.json")))!
                    .Select(A => $"{A.AttributeName}:{A.AttributeValue}").ToList())).ToList();
            Night = MonthA.AddDays(9);
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthJson(MonthA, Watchman.Select(C => C.Id).Distinct(), (Id, Day) => Day == 10));
        }

        // the campsites a search with these lines finds on the open night
        private List<string> Found(string Lines)
        {
            string File1 = WriteSearch("Filter-" + Guid.NewGuid().ToString("N") + ".yaml", SearchFixture.Watchman, Night, Night.AddDays(1), Extra: Lines);
            SearchRunPlan Plan = PlanOf(File1);
            Assert.That(Plan.Entries.Single().State, Is.EqualTo(SearchPlanState.Ready), string.Join(" ", Plan.Entries.Single().Diagnostics.Select(D => D.Message)));
            SearchRunResult Result = RunOf(Plan);
            return Result.Searches.Single().Openings.Select(O => O.CampsiteId).Distinct().OrderBy(Id => Id, StringComparer.Ordinal).ToList();
        }

        private static string List(string Key, params string[] Values) =>
            Key + ":\n" + string.Concat(Values.Select(V => "- '" + V.Replace("'", "''") + "'\n"));

        private List<string> Where(Func<Campsite, bool> Keep) =>
            Watchman.Where(Keep).Select(C => C.Id).Distinct().OrderBy(Id => Id, StringComparer.Ordinal).ToList();

        private List<string> WithType(string Type) => Where(C => C.Type == Type);

        [Test]
        public void IncludeCampsiteType_IsOr_InBothModes()
        {
            List<string> Expected = Where(C => C.Type == "STANDARD ELECTRIC" || C.Type == "WALK TO");
            Assert.That(Expected, Has.Count.GreaterThan(WithType("STANDARD ELECTRIC").Count), "both types are present");
            foreach (string Mode in new[] { "", "filterMatching: Exact\n", "filterMatching: Contains\n" })
            {
                Assert.That(Found(Mode + List("includeCampsiteType", "STANDARD ELECTRIC", "WALK TO")), Is.EqualTo(Expected), Mode);
            }
            // the rule before D51 wanted a type that contains both values: none
            Assert.That(Where(C => C.Type!.Contains("STANDARD ELECTRIC") && C.Type.Contains("WALK TO")), Is.Empty);
        }

        [Test]
        public void ExcludeEquipment_IsOr_InBothModes()
        {
            List<string> Expected = Where(C => !C.Equipment.Contains("RV") && !C.Equipment.Contains("SMALL TENT"));
            List<string> OldRule = Where(C => !(C.Equipment.Contains("RV") && C.Equipment.Contains("SMALL TENT")));
            Assert.That(OldRule, Is.Not.EqualTo(Expected), "the data tells OR from AND");
            foreach (string Mode in new[] { "", "filterMatching: Exact\n" })
            {
                Assert.That(Found(Mode + List("excludeEquipment", "RV", "SMALL TENT")), Is.EqualTo(Expected), Mode);
            }
        }

        [Test]
        public void ExcludeAttributes_IsOr_InBothModes()
        {
            List<string> Expected = Where(C => !C.Attributes.Contains("Shade:No") && !C.Attributes.Contains("Campfire Allowed:No"));
            List<string> OldRule = Where(C => !(C.Attributes.Contains("Shade:No") && C.Attributes.Contains("Campfire Allowed:No")));
            Assert.That(OldRule, Is.Not.EqualTo(Expected), "the data tells OR from AND");
            foreach (string Mode in new[] { "", "filterMatching: Exact\n" })
            {
                Assert.That(Found(Mode + List("excludeAttributes", "Shade:No", "Campfire Allowed:No")), Is.EqualTo(Expected), Mode);
            }
        }

        [Test]
        public void TheListsThatWereAlreadyOr_AreUnchanged()
        {
            Assert.That(Found(List("includeEquipment", "RV", "SMALL TENT")), Is.EqualTo(Where(C => C.Equipment.Contains("RV") || C.Equipment.Contains("SMALL TENT"))));
            Assert.That(Found(List("includeAttributes", "Shade:No", "Campfire Allowed:No")), Is.EqualTo(Where(C => C.Attributes.Contains("Shade:No") || C.Attributes.Contains("Campfire Allowed:No"))));
            Assert.That(Found(List("excludeCampsiteType", "STANDARD", "WALK")), Is.EqualTo(Where(C => !C.Type!.Contains("STANDARD") && !C.Type.Contains("WALK"))));
            string[] Names = Watchman.Select(C => C.Name!).Distinct().Take(3).ToArray();
            Assert.That(Found(List("includeSites", Names)), Is.EqualTo(Where(C => Names.Contains(C.Name))));
            Assert.That(Found(List("excludeSites", Names)), Is.EqualTo(Where(C => !Names.Contains(C.Name))));
        }

        [Test]
        public void ExactAndContains_DifferWherePartsAreUsed()
        {
            // "GROUP" is part of Watchman's group type: Contains (and a file without filterMatching) leaves group sites out,
            // Exact leaves nothing out
            List<string> NonGroup = Where(C => !C.Type!.Contains("GROUP"));
            Assert.That(NonGroup, Has.Count.LessThan(Watchman.Count));
            Assert.That(Found(List("excludeCampsiteType", "GROUP")), Is.EqualTo(NonGroup));
            Assert.That(Found("filterMatching: Contains\n" + List("excludeCampsiteType", "GROUP")), Is.EqualTo(NonGroup));
            Assert.That(Found("filterMatching: Exact\n" + List("excludeCampsiteType", "GROUP")), Is.EqualTo(Where(C => true)));
            // an equipment value that contains the campsite's: RV/MOTORHOME holds RV
            Assert.That(Found(List("includeEquipment", "RV/MOTORHOME")), Is.EqualTo(Where(C => C.Equipment.Contains("RV"))));
            Assert.That(Found("filterMatching: Exact\n" + List("includeEquipment", "RV/MOTORHOME")), Is.Empty);
            // case counts in both
            Assert.That(Found(List("includeEquipment", "tent")), Is.Empty);
            Assert.That(Found("filterMatching: Exact\n" + List("includeEquipment", "TENT")), Is.Empty);
        }

        [Test]
        public void Plan_RefusesAMalformedFilterMatching_EvenInADisabledFile()
        {
            foreach (string Line in new[] { "filterMatching:\n", "filterMatching: 1\n", "filterMatching: Exact\nfilterMatching: Exact\n" })
            {
                string File1 = WriteSearch("Bad-" + Guid.NewGuid().ToString("N") + ".yaml", SearchFixture.Watchman, Night, Night.AddDays(1), AutoRun: false, Extra: Line);
                SearchPlanEntry Entry = PlanOf(File1).Entries.Single();
                Assert.That(Entry.State, Is.EqualTo(SearchPlanState.Invalid), Line);
                Assert.That(Entry.Diagnostics.Select(D => D.Code), Does.Contain(ConfigCode.FilterMatchingInvalid), Line);
            }
            Assert.That(Gov.Requests, Is.Empty, "nothing was searched");
        }

        [Test]
        public void TheExampleSearch_FindsWhatItFoundBefore_NowWithExactValues()
        {
            // the example before D51 (no filterMatching, "GROUP") and since (Exact, Watchman's group type): the same campsites
            string Before = List("includeEquipment", "Tent", "SMALL TENT", "LARGE TENT OVER 9X12`") + List("includeAttributes", "Shade:Yes", "Campfire Allowed:Yes")
                + List("excludeCampsiteType", "GROUP");
            CampertronConfig Sample = Yaml.SearchSampleConfig();
            string Since = "filterMatching: " + Sample.FilterMatching + "\n" + List("includeEquipment", Sample.IncludeEquipment!.ToArray())
                + List("includeAttributes", Sample.IncludeAttributes!.ToArray()) + List("excludeCampsiteType", Sample.ExcludeCampsiteType!.ToArray());
            List<string> Old = Found(Before);
            Assert.That(Old, Is.Not.Empty);
            Assert.That(Found(Since), Is.EqualTo(Old));
            Assert.That(Sample.FilterMatching, Is.EqualTo(FilterMatchMode.Exact));
        }
    }
}
