using System.Globalization;
using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronWeb.Pages;
using static CampertronWebTests.MonitoringPageTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The web UI's campgrounds pages (v0.3.0's PR 9, D99; business-rules-settings §36) on the real host: CampertronGUI's
    // Campgrounds tab as pages, By Park, By City and By State, with the library's lookup queries (here a campground data the
    // test holds in memory, recording what each query was asked) and the shared words. Every page is a GET that only
    // looks; a campground is its FacilityID; a list of campgrounds is shown fifty at a time; an address that names what the
    // lists don't hold shows no choice and says so. One test reads the settings folder's own database path to show that a
    // page never creates one.
    public class CampgroundsPageTests
    {
        internal const string Twin = "232445";
        internal const string Zeroed = "0232445";

        // the campground data in memory, and what was asked of it
        internal sealed class Data
        {
            internal readonly List<string> Asked = new();
            internal List<string> Parks = new() { "Yosemite National Park", "<b>Zion</b> & Co" };
            internal List<string> States = new() { "CA", "UT" };
            internal Dictionary<string, List<string>> Cities = new() { ["CA"] = new() { "", "YOSEMITE VALLEY" }, ["UT"] = new() { "SPRINGDALE" } };
            internal List<CampgroundChoice> Many = Enumerable.Range(1, 1234).Select(N => new CampgroundChoice("9" + N.ToString("000000", CultureInfo.InvariantCulture), "Camp " + N.ToString("0000", CultureInfo.InvariantCulture), "TOWN", "CA")).ToList();

            internal WebLookupSeams Seams() => new WebLookupSeams
            {
                Parks = () => { Asked.Add("parks"); return Parks; },
                CampgroundsByPark = Park => { Asked.Add("park:" + Park); return Park == "Yosemite National Park"
                    ? new List<CampgroundChoice> { new(Twin, "UPPER PINES", "YOSEMITE VALLEY", "CA"), new(Zeroed, "UPPER PINES", "YOSEMITE VALLEY", "CA"), new("10004152", null, null, null) }
                    : new List<CampgroundChoice> { new("10241505", "<script>alert(1)</script> Camp", "SPRINGDALE", "UT") }; },
                States = () => { Asked.Add("states"); return States; },
                Cities = State => { Asked.Add("cities:" + State); return Cities.TryGetValue(State, out List<string>? Found) ? Found : new List<string>(); },
                CampgroundsByStateAndCity = (State, City) => { Asked.Add("city:" + State + "|" + City); return City.Length == 0
                    ? new List<CampgroundChoice> { new("777", "NO-CITY CAMP", null, "CA") } : new List<CampgroundChoice> { new(Twin, "UPPER PINES", City, State) }; },
                CampgroundsByState = State => { Asked.Add("state:" + State); return State == "CA" ? Many : new List<CampgroundChoice>(); },
                Details = Id => { Asked.Add("details:" + Id); return new FacilitiesData
                {
                    FacilityID = Id, FacilityName = Id == "10004152" ? "  " : "UPPER PINES " + Id, FacilityPhone = Id == Zeroed ? null : "209-372-8502",
                    FacilityEmail = "", FacilityDescription = "<b>Line one</b>\n\n\n\nLine two", FacilityDirections = "Turn left", FacilityUseFeeDescription = "$36"
                }; },
            };
        }

        internal static async Task<(WebTestHost Host, Data Data)> Start(bool WithPassword = true, string? WebYaml = null)
        {
            var D = new Data();
            WebTestHost Host = await WebTestHost.Start(WebYaml: WebYaml, WithPassword: WithPassword, Configure: Settings => Settings.Lookup = D.Seams());
            return (Host, D);
        }

        internal static XElement? ListOf(WebPage Page) => Page.Xml().Descendants("ul").SingleOrDefault(U => (string?)U.Attribute("aria-label") == CampgroundsWording.CampgroundsListName);
        internal static List<string> Links(WebPage Page) => ListOf(Page)?.Descendants("a").Select(A => A.Value).ToList() ?? new List<string>();
        internal static string? Chosen(WebPage Page) => ListOf(Page)?.Descendants("a").SingleOrDefault(A => (string?)A.Attribute("aria-current") == "page")?.Value;
        internal static string Details(WebPage Page) => string.Join(" ", Element(Page, "details")!.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0));
        internal static string Href(WebPage Page, string Text) => (string)Page.Xml().Descendants("a").First(A => A.Value == Text).Attribute("href")!;

        // ---- who may use it, and what a GET does ----

        [Test]
        public async Task TheCampgroundsPages_NeedTheOwner_AreGetsOnly_AndShowByParkFirst()
        {
            (WebTestHost Host, Data D) = await Start();
            await using (Host)
            {
                using Browser Visitor = Host.Browser();
                WebPage Refused = await Visitor.Get("/campgrounds/park");
                Assert.That((Refused.Status, Refused.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin?returnUrl=%2Fcampgrounds%2Fpark")));
                Assert.That(D.Asked, Is.Empty, "nothing is read for a visitor");

                using Browser Owner = await Host.SignedIn();
                string Before = Snapshot(Host.Folder);
                WebPage First = await Owner.Get("/campgrounds");
                Assert.That(First.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(First.Xml().Descendants("title").Single().Value, Is.EqualTo("Campgrounds: By Park – Campertron"));
                Assert.That(First.Xml().Descendants("nav").Single(N => (string?)N.Attribute("aria-label") == "Ways to look").Descendants("a").Select(A => A.Value),
                    Is.EqualTo(new[] { CampgroundsWording.ByPark, CampgroundsWording.ByCity, CampgroundsWording.ByState }));
                Assert.That(First.Xml().Descendants("nav").First().Descendants("a").Select(A => A.Value), Does.Contain("Campgrounds"));
                Assert.That(Details(First), Is.EqualTo(CampgroundsWording.NoCampgroundSelected + " " + CampgroundsWording.ByParkHint));
                Assert.That((await Owner.Get("/campgrounds/other")).Status, Is.EqualTo(HttpStatusCode.NotFound));
                WebPage Post = await Owner.Post("/campgrounds/park", ("park", "=Yosemite National Park"));
                Assert.That(Post.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), "nothing to post");
                Assert.That(First.Xml().Descendants("form").Select(F => (string?)F.Attribute("method")), Has.All.EqualTo("get").Or.EqualTo("post"));
                Assert.That(First.Xml().Descendants("main").Single().Descendants("form").Select(F => (string?)F.Attribute("method")), Has.All.EqualTo("get"));
                Assert.That(Snapshot(Host.Folder), Is.EqualTo(Before), "the pages create and change nothing");
            }
        }

        [Test]
        public async Task APageNeverCreatesTheCampgroundData_AndSaysWhenThereIsNone()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            string Database = Path.Join(Host.Folder, "RecreationDotOrg.db");
            Assert.That(File.Exists(Database), Is.False);
            foreach (string Address in new[] { "/campgrounds/park", "/campgrounds/city?state=UT", "/campgrounds/state?state=UT&id=232445" })
            {
                WebPage Page = await Owner.Get(Address);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Said(Page, "no-data"), Is.EqualTo("Error: " + CampgroundsWording.NoData + " " + WebCampgroundsWording.NoDataYet));
                Assert.That(File.Exists(Database), Is.False, Address + " created no database");
            }
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Web.password" }));
        }

        // ---- By Park ----

        [Test]
        public async Task ByPark_ListsTheParksCampgrounds_EachByItsFacilityID_AndShowsTheChosenOnesDetails()
        {
            (WebTestHost Host, Data D) = await Start();
            await using (Host)
            {
                using Browser Owner = await Host.SignedIn();
                WebPage Parks = await Owner.Get("/campgrounds/park");
                XElement Select = Parks.Xml().Descendants("select").Single();
                Assert.That((string?)Select.Attribute("name"), Is.EqualTo("park"));
                Assert.That(Select.Elements("option").Select(O => ((string?)O.Attribute("value"), O.Value)), Is.EqualTo(new[]
                {
                    ("", CampgroundsWording.ChooseOne), ("=Yosemite National Park", "Yosemite National Park"), ("=<b>Zion</b> & Co", "<b>Zion</b> & Co")
                }));
                Assert.That(ListOf(Parks), Is.Null, "no list before a park is chosen");

                D.Asked.Clear();
                WebPage Listed = await Owner.Get("/campgrounds/park?park=" + Uri.EscapeDataString("=Yosemite National Park"));
                Assert.That(D.Asked, Is.EqualTo(new[] { "parks", "park:Yosemite National Park" }), "one query for the list; no details before a campground is chosen");
                Assert.That(Links(Listed), Is.EqualTo(new[]
                {
                    "UPPER PINES — YOSEMITE VALLEY, CA — ID 232445", "UPPER PINES — YOSEMITE VALLEY, CA — ID 0232445", "Unnamed campground — ID 10004152"
                }));
                Assert.That(Listed.Xml().Descendants("option").Single(O => O.Attribute("selected") != null).Value, Is.EqualTo("Yosemite National Park"));

                //same name, same place: told apart by the ID, which is the address's and the details'
                D.Asked.Clear();
                WebPage Picked = await Owner.Get(Href(Listed, "UPPER PINES — YOSEMITE VALLEY, CA — ID 0232445"));
                Assert.That(D.Asked, Does.Contain("details:" + Zeroed).And.Not.Contain("details:" + Twin), "looked up by its exact FacilityID");
                Assert.That(Chosen(Picked), Is.EqualTo("UPPER PINES — YOSEMITE VALLEY, CA — ID 0232445"));
                Assert.That(Details(Picked), Is.EqualTo("UPPER PINES 0232445 Yosemite National Park " + CampgroundsWording.CampgroundIdLabel + " 0232445 "
                    + CampgroundsWording.CampgroundIdNote + " " + CampgroundsWording.SelectToCopy + " Contact Phone Not listed Email Not listed Description <b>Line one</b>\n\nLine two Directions Turn left Fees $36"));
                Assert.That(Said(Picked, "campground-id"), Is.EqualTo(Zeroed));

                WebPage Unnamed = await Owner.Get(Href(Listed, "Unnamed campground — ID 10004152"));
                Assert.That(Element(Unnamed, "details-heading")!.Value, Is.EqualTo(CampgroundChoice.UnnamedName));

                //hostile names are text
                WebPage Hostile = await Owner.Get("/campgrounds/park?park=" + Uri.EscapeDataString("=<b>Zion</b> & Co") + "&id=10241505");
                Assert.That(Hostile.Body, Does.Not.Contain("<script>alert(1)</script>").And.Not.Contain("<b>Zion</b>"));
                Assert.That(Chosen(Hostile), Is.EqualTo("<script>alert(1)</script> Camp — SPRINGDALE, UT — ID 10241505"));
            }
        }

        // ---- By City: the empty city is a city of its own ----

        [Test]
        public async Task ByCity_ListsTheStatesCities_TheEmptyCityToo_AndTheCitysCampgrounds()
        {
            (WebTestHost Host, Data D) = await Start();
            await using (Host)
            {
                using Browser Owner = await Host.SignedIn();
                WebPage States = await Owner.Get("/campgrounds/city");
                Assert.That(States.Xml().Descendants("select").Select(S => (string?)S.Attribute("name")), Is.EqualTo(new[] { "state" }), "a city after a state");
                WebPage Cities = await Owner.Get("/campgrounds/city?state=CA");
                XElement City = Cities.Xml().Descendants("select").Single(S => (string?)S.Attribute("name") == "city");
                Assert.That(City.Elements("option").Select(O => ((string?)O.Attribute("value"), O.Value)), Is.EqualTo(new[]
                {
                    ("", CampgroundsWording.ChooseOne), ("=", CampgroundsWording.NoCityListed), ("=YOSEMITE VALLEY", "YOSEMITE VALLEY")
                }));
                Assert.That(ListOf(Cities), Is.Null, "no list before a city is chosen");

                D.Asked.Clear();
                WebPage Empty = await Owner.Get("/campgrounds/city?state=CA&city=%3D");
                Assert.That(D.Asked, Does.Contain("city:CA|"), "the empty city, queried as it is stored");
                Assert.That(Links(Empty), Is.EqualTo(new[] { "NO-CITY CAMP — CA — ID 777" }));
                WebPage Picked = await Owner.Get(Href(Empty, "NO-CITY CAMP — CA — ID 777"));
                Assert.That(Details(Picked), Does.StartWith("UPPER PINES 777 CA "), "the context: the state alone when the city has no name");

                //a city of another state, or a city without its mark, is no choice
                D.Asked.Clear();
                WebPage Other = await Owner.Get("/campgrounds/city?state=CA&city=%3DSPRINGDALE");
                Assert.That(ListOf(Other), Is.Null);
                Assert.That(Said(Other, "page-notice"), Is.EqualTo("Warning: " + CampgroundsWording.NotInList));
                Assert.That(D.Asked.Where(A => A.StartsWith("city:", StringComparison.Ordinal)), Is.Empty);
                WebPage Unmarked = await Owner.Get("/campgrounds/city?state=CA&city=YOSEMITE%20VALLEY");
                Assert.That(ListOf(Unmarked), Is.Null);
            }
        }

        // ---- By State: a long list fifty at a time ----

        [Test]
        public async Task ByState_ShowsALongListFiftyAtATime_WithItsRangeInWords_AndTheChosenCampgroundsPart()
        {
            (WebTestHost Host, Data D) = await Start();
            await using (Host)
            {
                using Browser Owner = await Host.SignedIn();
                WebPage First = await Owner.Get("/campgrounds/state?state=CA");
                Assert.That(Links(First), Has.Count.EqualTo(WebLookupPage.PageSize));
                Assert.That(Links(First)[0], Is.EqualTo("Camp 0001 — TOWN, CA — ID 9000001"));
                Assert.That(Said(First, "list-range"), Is.EqualTo("Showing campgrounds 1 to 50 of 1,234."));
                Assert.That(First.Xml().Descendants("a").Select(A => A.Value), Does.Contain(CampgroundsWording.NextCampgrounds).And.Not.Contain(CampgroundsWording.PreviousCampgrounds));
                Assert.That(First.Body.Length, Is.LessThan(64 * 1024), "a part of the list, not the whole of it");

                WebPage Last = await Owner.Get("/campgrounds/state?state=CA&part=25");
                Assert.That(Links(Last), Has.Count.EqualTo(34));
                Assert.That(Said(Last, "list-range"), Is.EqualTo("Showing campgrounds 1,201 to 1,234 of 1,234."));
                Assert.That(Last.Xml().Descendants("a").Select(A => A.Value), Does.Contain(CampgroundsWording.PreviousCampgrounds).And.Not.Contain(CampgroundsWording.NextCampgrounds));
                foreach (string Wrong in new[] { "0", "26", "-1", "x", "99999999999" })
                {
                    WebPage Page = await Owner.Get("/campgrounds/state?state=CA&part=" + Wrong);
                    Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), Wrong);
                    Assert.That(Said(Page, "list-range"), Is.EqualTo("Showing campgrounds 1 to 50 of 1,234."), Wrong);
                }

                //a chosen campground: its own part when the address names none, and its link stays chosen across the parts' links
                WebPage Picked = await Owner.Get("/campgrounds/state?state=CA&id=9000321");
                Assert.That(Said(Picked, "list-range"), Is.EqualTo("Showing campgrounds 301 to 350 of 1,234."));
                Assert.That(Chosen(Picked), Is.EqualTo("Camp 0321 — TOWN, CA — ID 9000321"));
                Assert.That(Href(Picked, CampgroundsWording.NextCampgrounds), Is.EqualTo("/campgrounds/state?state=CA&id=9000321&part=8"));
                Assert.That(Details(Picked), Does.StartWith("UPPER PINES 9000321 CA "));

                //an ID the list does not hold: no details, said so
                D.Asked.Clear();
                WebPage Unknown = await Owner.Get("/campgrounds/state?state=UT&id=9000321");
                Assert.That(Said(Unknown, "page-notice"), Is.EqualTo("Warning: " + CampgroundsWording.NotInList));
                Assert.That(Details(Unknown), Does.StartWith(CampgroundsWording.NoCampgroundSelected));
                Assert.That(D.Asked.Where(A => A.StartsWith("details:", StringComparison.Ordinal)), Is.Empty, "never a campground of another list");
                Assert.That(Said(Unknown, "no-campgrounds"), Is.EqualTo(CampgroundsWording.NoCampgrounds));
                WebPage NoState = await Owner.Get("/campgrounds/state?state=ZZ&id=1");
                Assert.That(Said(NoState, "page-notice"), Is.EqualTo("Warning: " + CampgroundsWording.NotInList));
            }
        }

        [Test]
        public async Task WithoutPasswordProtection_ThePagesAreTheOwners()
        {
            (WebTestHost Host, Data D) = await Start(WithPassword: false, WebYaml: "passwordProtection: false\nnetworkListening: false\n");
            await using (Host)
            {
                using Browser Anyone = Host.Browser();
                WebPage Page = await Anyone.Get("/campgrounds/state?state=UT");
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Said(Page, "no-campgrounds"), Is.EqualTo(CampgroundsWording.NoCampgrounds));
            }
        }

        // ---- what the pages refer to ----

        [Test]
        public void ThePages_OnlyReadTheCampgroundData_AndNeverPrepareRefreshOrWriteIt()
        {
            List<string> Called = IlReferences.ReferencedMembers(typeof(WebCampgrounds), typeof(WebLookupPage), typeof(CampgroundsModel)).Select(C => C.Member).Distinct().ToList();
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.data.Load::", "CampertronWeb.WebCampgroundData::", "CampertronLibrary.function.RecDotOrg.api.", "CampertronWeb.WebRefresh",
                "CampertronLibrary.function.Base.ConfigFileReplace::", "CampertronLibrary.function.Base.Yaml::", "System.IO.File::Write", "System.IO.File::Delete", "System.IO.Directory::",
                "CampertronLibrary.function.RecDotOrg.search.", "System.Net.Http.", "Microsoft.Data.Sqlite.",
            };
            Assert.That(Called.Where(M => Forbidden.Any(F => M.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            string[] Reads = Called.Where(M => M.StartsWith("CampertronLibrary.function.RecDotOrg.sqlite.Read::", StringComparison.Ordinal)).ToArray();
            Assert.That(Reads, Is.EquivalentTo(new[]
            {
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::ParksWithCampgrounds", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByPark",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueStates", "CampertronLibrary.function.RecDotOrg.sqlite.Read::UniqueCities",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByStateAndCity", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesByState",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::GetCampgroundDetailsById",
            }), "the desktop GUI's Campgrounds tab's queries, and no other");
        }
    }
}
