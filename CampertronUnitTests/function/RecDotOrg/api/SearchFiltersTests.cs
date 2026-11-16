using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // The eight include and exclude lists (decision D51, business-rules §3): OR semantics for every list, and the search's
    // filterMatching (Exact or Contains, Contains without it). Pure: no file, database or network.
    public class SearchFiltersTests
    {
        private static AvailabilityData Site(string Name, string Type, string[] Equipment, string[] Attributes)
        {
            var Lists = new AttributeValueLists
            {
                AttValuePair = Attributes.Select(A => new AttributeValuePair { AttributeName = A.Split(':')[0], AttributeValue = A.Substring(A.IndexOf(':') + 1) }).ToList()
            };
            Lists.GenerateStringList();
            return new AvailabilityData { CampsiteName = Name, CampsiteType = Type, PermittedEquipmentList = Equipment.ToList(), CampsiteAttributeLists = Lists };
        }

        private static readonly AvailabilityData TentSite = Site("A12", "STANDARD NONELECTRIC", new[] { "Tent", "SMALL TENT" }, new[] { "Shade:Yes", "Campfire Allowed:Yes" });
        private static readonly AvailabilityData RvSite = Site("B3", "STANDARD ELECTRIC", new[] { "RV", "Trailer" }, new[] { "Shade:No", "Driveway Surface:Paved" });
        private static readonly AvailabilityData GroupSite = Site("G1", "GROUP STANDARD NONELECTRIC", new[] { "Tent" }, new[] { "Shade:Yes" });
        private static readonly AvailabilityData[] All = { TentSite, RvSite, GroupSite };

        private static CampertronConfig Search(FilterMatchMode? Mode, Action<CampertronConfig> Set)
        {
            var C = new CampertronConfig { FilterMatching = Mode };
            Set(C);
            return C;
        }

        private static List<string?> Kept(CampertronConfig C) =>
            All.Where(S => SearchFilters.KeepsCampsiteType(C, S.CampsiteType) && SearchFilters.KeepsCampsite(C, S)).Select(S => S.CampsiteName).ToList();

        // ---- OR semantics, every list ----

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void IncludeEquipment_KeepsACampsiteWithAnyValue(FilterMatchMode Mode)
        {
            Assert.That(Kept(Search(Mode, C => C.IncludeEquipment = new() { "SMALL TENT", "RV" })), Is.EqualTo(new[] { "A12", "B3" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void ExcludeEquipment_DropsACampsiteWithAnyValue(FilterMatchMode Mode)
        {
            // before D51 a campsite was dropped only if it had every value (no campsite permits both SMALL TENT and RV, so
            // the old rule dropped nothing)
            Assert.That(Kept(Search(Mode, C => C.ExcludeEquipment = new() { "SMALL TENT", "RV" })), Is.EqualTo(new[] { "G1" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void IncludeSites_KeepsAnyNamedCampsite(FilterMatchMode Mode)
        {
            Assert.That(Kept(Search(Mode, C => C.IncludeSites = new() { "G1", "A12" })), Is.EqualTo(new[] { "A12", "G1" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void ExcludeSites_DropsAnyNamedCampsite(FilterMatchMode Mode)
        {
            Assert.That(Kept(Search(Mode, C => C.ExcludeSites = new() { "G1", "A12" })), Is.EqualTo(new[] { "B3" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void IncludeAttributes_KeepsACampsiteWithAnyValue(FilterMatchMode Mode)
        {
            Assert.That(Kept(Search(Mode, C => C.IncludeAttributes = new() { "Campfire Allowed:Yes", "Driveway Surface:Paved" })), Is.EqualTo(new[] { "A12", "B3" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void ExcludeAttributes_DropsACampsiteWithAnyValue(FilterMatchMode Mode)
        {
            // before D51 only a campsite with every value was dropped (none has Campfire Allowed:Yes and Shade:No)
            Assert.That(Kept(Search(Mode, C => C.ExcludeAttributes = new() { "Campfire Allowed:Yes", "Shade:No" })), Is.EqualTo(new[] { "G1" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void IncludeCampsiteType_KeepsACampsiteOfAnyType(FilterMatchMode Mode)
        {
            // before D51 the type had to match every value, which two different types never do
            Assert.That(Kept(Search(Mode, C => C.IncludeCampsiteType = new() { "STANDARD ELECTRIC", "GROUP STANDARD NONELECTRIC" })), Is.EqualTo(new[] { "B3", "G1" }));
        }

        [TestCase(FilterMatchMode.Exact)]
        [TestCase(FilterMatchMode.Contains)]
        public void ExcludeCampsiteType_DropsACampsiteOfAnyType(FilterMatchMode Mode)
        {
            Assert.That(Kept(Search(Mode, C => C.ExcludeCampsiteType = new() { "STANDARD ELECTRIC", "GROUP STANDARD NONELECTRIC" })), Is.EqualTo(new[] { "A12" }));
        }

        [Test]
        public void EmptyAndMissingLists_AreNoFilter_DuplicatesAndOrderChangeNothing()
        {
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => { })), Is.EqualTo(new[] { "A12", "B3", "G1" }));
            Assert.That(Kept(Search(FilterMatchMode.Exact, C =>
            {
                C.IncludeEquipment = new(); C.ExcludeEquipment = new(); C.IncludeSites = new(); C.ExcludeSites = new();
                C.IncludeAttributes = new(); C.ExcludeAttributes = new(); C.IncludeCampsiteType = new(); C.ExcludeCampsiteType = new();
            })), Is.EqualTo(new[] { "A12", "B3", "G1" }));
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.IncludeEquipment = new() { "RV", "Tent", "RV", "Tent" })), Is.EqualTo(Kept(Search(FilterMatchMode.Exact, C => C.IncludeEquipment = new() { "Tent", "RV" }))));
        }

        [Test]
        public void AnIncludeValueNothingHas_HidesEverything_AnExcludeValueNothingHas_RemovesNothing()
        {
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.IncludeEquipment = new() { "Hovercraft" })), Is.Empty);
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.ExcludeEquipment = new() { "Hovercraft" })), Is.EqualTo(new[] { "A12", "B3", "G1" }));
        }

        // ---- Exact and Contains ----

        [Test]
        public void Exact_IsTheWholeValue_CaseSpacesAndPunctuationCount()
        {
            Assert.That(SearchFilters.Matches(FilterField.Equipment, FilterMatchMode.Exact, "Tent", "Tent"), Is.True);
            foreach (string Other in new[] { "tent", "TENT", " Tent", "Tent ", "Tent.", "Ten", "SMALL TENT" })
            {
                Assert.That(SearchFilters.Matches(FilterField.Equipment, FilterMatchMode.Exact, Other, "Tent"), Is.False, Other);
            }
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.IncludeCampsiteType = new() { "STANDARD" })), Is.Empty, "a part of a type is no type");
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.ExcludeCampsiteType = new() { "GROUP" })), Is.EqualTo(new[] { "A12", "B3", "G1" }));
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.IncludeEquipment = new() { "RV/MOTORHOME" })), Is.Empty);
            Assert.That(Kept(Search(FilterMatchMode.Exact, C => C.IncludeAttributes = new() { "Shade:Yes " })), Is.Empty);
        }

        [Test]
        public void Contains_IsTheMatchingBeforeD51_InItsDirectionPerList()
        {
            // a campsite type contains the value
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.ExcludeCampsiteType = new() { "GROUP" })), Is.EqualTo(new[] { "A12", "B3" }));
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeCampsiteType = new() { "STANDARD" })), Is.EqualTo(new[] { "A12", "B3", "G1" }));
            // an equipment or attribute value contains the campsite's (the list value is the longer one)
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeEquipment = new() { "RV/MOTORHOME" })), Is.EqualTo(new[] { "B3" }));
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeEquipment = new() { "TENT" })), Is.Empty, "case counts");
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeAttributes = new() { "Shade:Yes (most of the day)" })), Is.EqualTo(new[] { "A12", "G1" }));
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeAttributes = new() { "Shade" })), Is.Empty, "the campsite's \"Name:Value\" must be inside the value");
            // campsite names match exactly, as they always have
            Assert.That(Kept(Search(FilterMatchMode.Contains, C => C.IncludeSites = new() { "A" })), Is.Empty);
            Assert.That(SearchFilters.Matches(FilterField.Sites, FilterMatchMode.Contains, "1", "10"), Is.False);
        }

        [Test]
        public void AFileWithoutFilterMatching_UsesContains()
        {
            var Legacy = Search(null, C => C.ExcludeCampsiteType = new() { "GROUP" });
            Assert.That(Legacy.EffectiveFilterMatching, Is.EqualTo(FilterMatchMode.Contains));
            Assert.That(Kept(Legacy), Is.EqualTo(new[] { "A12", "B3" }));
        }

        [Test]
        public void NullValues_MatchNothing_AndThrowNothing()
        {
            Assert.That(SearchFilters.Matches(FilterField.Equipment, FilterMatchMode.Contains, null, "Tent"), Is.False);
            Assert.That(SearchFilters.Matches(FilterField.CampsiteType, FilterMatchMode.Contains, "GROUP", null), Is.False);
            Assert.That(SearchFilters.Matches(FilterField.Sites, FilterMatchMode.Exact, null, null), Is.False);
            AvailabilityData NoData = new AvailabilityData { CampsiteName = null, CampsiteType = null, PermittedEquipmentList = new(), CampsiteAttributeLists = new AttributeValueLists { AttValuePair = new() } };
            NoData.CampsiteAttributeLists.GenerateStringList();
            var C = Search(FilterMatchMode.Contains, C => C.ExcludeEquipment = new() { "Tent" });
            Assert.That(SearchFilters.KeepsCampsite(C, NoData), Is.True, "a campsite without equipment is not excluded");
            Assert.That(SearchFilters.KeepsCampsite(Search(FilterMatchMode.Contains, C => C.IncludeEquipment = new() { "Tent" }), NoData), Is.False, "nor included");
        }

        // ---- negative controls: the rules before D51, written out, would fail the tests above ----

        private static bool OldExcludeEquipmentKeeps(List<string> Exclude, List<string> Equipment) => Exclude.Any(S1 => Equipment.Any(S1.Contains) == false);
        private static bool OldIncludeTypeKeeps(List<string> Include, string Type) => Include.All(Y => Type.Contains(Y));

        [Test]
        public void TheOldAndRules_WouldGiveOtherResults()
        {
            Assert.That(OldExcludeEquipmentKeeps(new() { "SMALL TENT", "RV" }, TentSite.PermittedEquipmentList!), Is.True, "the old rule kept a campsite with one of the excluded values");
            Assert.That(SearchFilters.Excludes(new List<string> { "SMALL TENT", "RV" }, FilterField.Equipment, FilterMatchMode.Contains, TentSite.PermittedEquipmentList), Is.True);
            Assert.That(OldIncludeTypeKeeps(new() { "STANDARD ELECTRIC", "GROUP STANDARD NONELECTRIC" }, "STANDARD ELECTRIC"), Is.False, "the old rule wanted every type");
            Assert.That(SearchFilters.Includes(new List<string> { "STANDARD ELECTRIC", "GROUP STANDARD NONELECTRIC" }, FilterField.CampsiteType, FilterMatchMode.Contains, new[] { "STANDARD ELECTRIC" }), Is.True);
        }
    }
}
