using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using static CampertronWebTests.SystemPageTests;

namespace CampertronWebTests
{
    // The system and campgrounds pages (v0.3.0's PR 9) as markup, in the states a user meets: the system page at rest, its
    // confirmation, a refresh that downloads, one that installs, after a cancel, after a success, after a failure, the
    // refresh unavailable while a search runs, an out-of-date page; the campgrounds pages with no data, at rest, with a park,
    // a city, a long state list's middle part and its chosen campground, and an address that names nothing they hold. Each
    // is well-formed, named, labelled, usable with the keyboard alone and without a script; its headings in order; every
    // message says its severity in words; a disabled action says why in words; a link says where it goes and stays in the
    // web UI; nothing inline or from another site. Read as XML (no HTML parser, D92).
    public class SystemAndCampgroundsAccessibilityTests
    {
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);

        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        private static async Task<List<(string What, WebPage Page)>> SystemPages()
        {
            var Found = new List<(string, WebPage)>();
            var InRun = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            await using SystemHost S = await SystemHost.Start(Run: (Plan, Progress, Token, Options) =>
            {
                InRun.Set();
                Release.Wait(SystemPageTests.Long);
                return ResultsPageTests.Returns()(Plan, Progress, Token, Options);
            });
            using Browser Owner = await S.Host.SignedIn();
            WebPage Rest = await Owner.Get(Address);
            Found.Add(("at rest", Rest));
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            Found.Add(("the confirmation", Confirm));
            WebPage Downloading = await StartRefresh(Owner, Confirm);
            S.Fake.Await(S.Fake.Downloading, "downloading");
            Found.Add(("downloading", await Owner.Get(Address)));
            Found.Add(("an out-of-date page", await StartRefresh(Owner, Confirm)));
            Found.Add(("canceling", await CancelRefresh(Owner, await Owner.Get(Address))));
            Found.Add(("canceled", await RefreshEnded(Owner)));
            S.Fake.LetDownload.Set();
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Installing, "installing");
            Found.Add(("installing", await Owner.Get(Address)));
            S.Fake.LetInstall.Set();
            Found.Add(("refreshed", await RefreshEnded(Owner)));
            S.Fake.Before = () => FakeRefresh.Result(RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading, false, new HttpRequestException(SystemPageTests.ServerText));
            await StartRefresh(Owner);
            Found.Add(("failed", await RefreshEnded(Owner)));
            File.WriteAllText(S.In("Alpha.yaml"), ResultsPageTests.Search("First trip"));
            await Owner.Get("/results");
            await ResultsPageTests.Act(Owner, "/results?handler=start", ResultsPageTests.Fields(await Owner.Get("/results"), null, ("Alpha.yaml", true)));
            Assert.That(InRun.Wait(SystemPageTests.Long), Is.True);
            Found.Add(("a search runs", await Owner.Get(Address)));
            Found.Add(("a search runs, the confirmation", await Owner.Get(Address + "?confirm=refresh")));
            Release.Set();
            await ResultsPageTests.Ended(Owner);
            return Found;
        }

        private static async Task<List<(string What, WebPage Page)>> CampgroundPages()
        {
            var Found = new List<(string, WebPage)>();
            await using (WebTestHost Empty = await WebTestHost.Start(WithPassword: true))
            {
                using Browser Owner = await Empty.SignedIn();
                Found.Add(("no data", await Owner.Get("/campgrounds/park")));
            }
            (WebTestHost Host, CampgroundsPageTests.Data _) = await CampgroundsPageTests.Start();
            await using (Host)
            {
                using Browser Owner = await Host.SignedIn();
                Found.Add(("by park", await Owner.Get("/campgrounds/park")));
                Found.Add(("a park", await Owner.Get("/campgrounds/park?park=" + Uri.EscapeDataString("=Yosemite National Park"))));
                Found.Add(("a park's campground", await Owner.Get("/campgrounds/park?park=" + Uri.EscapeDataString("=Yosemite National Park") + "&id=0232445")));
                Found.Add(("by city", await Owner.Get("/campgrounds/city")));
                Found.Add(("a city", await Owner.Get("/campgrounds/city?state=CA&city=%3D")));
                Found.Add(("a long list's part", await Owner.Get("/campgrounds/state?state=CA&part=3")));
                Found.Add(("its chosen campground", await Owner.Get("/campgrounds/state?state=CA&id=9000321")));
                Found.Add(("nothing the lists hold", await Owner.Get("/campgrounds/state?state=UT&id=1")));
                Found.Add(("hostile names", await Owner.Get("/campgrounds/park?park=" + Uri.EscapeDataString("=<b>Zion</b> & Co") + "&id=10241505")));
            }
            return Found;
        }

        // what every page of the web UI must be (as the other pages' tests check them)
        private static void CheckPage(string What, WebPage Page, string Title)
        {
            Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
            XElement Html = Page.Xml().Root!;
            List<XElement> Elements = Html.DescendantsAndSelf().ToList();
            Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
            Assert.That(Html.Descendants("title").Single().Value, Is.EqualTo(Title + " – Campertron"), What);
            XElement Main = Html.Descendants("main").Single();
            Assert.That(Main.Descendants("h1").Count(), Is.EqualTo(1), What);
            int Level = 1;
            foreach (XElement Head in Elements.Where(E => E.Name.LocalName.Length == 2 && E.Name.LocalName[0] == 'h' && char.IsDigit(E.Name.LocalName[1])))
            {
                int This = Head.Name.LocalName[1] - '0';
                Assert.That(This, Is.LessThanOrEqualTo(Level + 1), What + ": heading " + Head.Value);
                Assert.That(Head.Value.Trim(), Is.Not.Empty, What);
                Level = This;
            }
            foreach (XElement Section in Main.Descendants("section"))
            {
                string? Named = Attr(Section, "aria-labelledby");
                Assert.That(Named, Is.Not.Null, What + ": a section is named by its heading");
                Assert.That(Elements.Single(E => Attr(E, "id") == Named).Name.LocalName, Does.Match("^h[234]$"), What);
            }
            List<string> Ids = Elements.Select(E => Attr(E, "id")).Where(Id => Id != null).Select(Id => Id!).ToList();
            Assert.That(Ids, Is.Unique, What);
            foreach (XElement E in Elements)
            {
                foreach (string Pointer in new[] { "aria-describedby", "aria-labelledby", "for" })
                {
                    foreach (string Id in (Attr(E, Pointer) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        Assert.That(Ids, Does.Contain(Id), What + ": " + Pointer + " of " + E.Name + " " + Attr(E, "id"));
                    }
                }
            }
            //every control is named: a select and an input by its label, a button by its words
            foreach (XElement Control in Main.Descendants().Where(E => E.Name == "select" || (E.Name == "input" && Attr(E, "type") != "hidden")))
            {
                Assert.That(Main.Descendants("label").Count(L => Attr(L, "for") == Attr(Control, "id")), Is.EqualTo(1), What + ": the label of " + Attr(Control, "id"));
            }
            foreach (XElement Button in Main.Descendants("button"))
            {
                Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                if (Attr(Button, "disabled") != null)
                {
                    string Why = string.Join(" ", (Attr(Button, "aria-describedby") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Id => Elements.Single(E => Attr(E, "id") == Id).Value));
                    Assert.That(Why, Does.Contain("Unavailable").Or.Contain("can't be canceled").Or.Contain("Canceling").Or.Contain("couldn't find"), What + ": why " + Button.Value + " is unavailable");
                }
            }
            //every form posts to this web UI with its token, or is a GET that only looks
            foreach (XElement Form in Main.Descendants("form"))
            {
                Assert.That(Attr(Form, "action"), Does.StartWith("/"), What);
                if (Attr(Form, "method") == "post")
                {
                    Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                    Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "state" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                }
                else
                {
                    Assert.That(Attr(Form, "method"), Is.EqualTo("get"), What);
                    Assert.That(Form.Descendants("input").Where(I => Attr(I, "name") == "__RequestVerificationToken"), Is.Empty, What);
                }
            }
            foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ').Contains("message")))
            {
                Assert.That(string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0)), Does.Match("^(Error|Warning|Info): "), What);
            }
            Assert.That(Elements.Count(E => Attr(E, "autofocus") != null), Is.LessThanOrEqualTo(1), What);
            Assert.That(Elements.Where(E => Attr(E, "tabindex") is string Tab && Tab != "-1"), Is.Empty, What);
            Assert.That(Html.Descendants("body").Single().Elements().First().Attribute("href")?.Value, Is.EqualTo("#main"), What);
            Assert.That(Elements.Where(E => E.Name == "table" || (Attr(E, "class") ?? "").Split(' ').Intersect(new[] { "bounded", "long" }).Any()), Is.Empty, What + ": nothing that scrolls inside the page");
            foreach (XElement Nav in Html.Descendants("nav"))
            {
                Assert.That(Attr(Nav, "aria-label"), Is.Not.Null.And.Not.Empty, What);
                Assert.That(Nav.Descendants("a").Count(A => Attr(A, "aria-current") == "page"), Is.LessThanOrEqualTo(1), What);
            }
            var Links = Elements.Where(E => E.Name == "a").Select(A => (Text: A.Value.Trim(), Href: Attr(A, "href")!)).ToList();
            Assert.That(Links.Select(L => L.Text), Has.None.Matches("^(?i)(open|link|here|click here|more|details|view)$"), What);
            Assert.That(Links.Where(L => !L.Href.StartsWith("/", StringComparison.Ordinal) && !L.Href.StartsWith("#", StringComparison.Ordinal)), Is.Empty, What + ": every link stays in the web UI");
            //nothing inline and nothing from another site: a script only the web UI's own, while a refresh runs (D109; LiveMarkup)
            Assert.That(Elements.Where(E => (E.Name == "script" && !LiveMarkup.IsLiveScript(E)) || E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
            foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
            {
                string Name = Attribute.Name.LocalName;
                Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
            }
            Assert.That(Page.Body, Does.Not.Contain(SystemPageTests.ServerText).And.Not.Contain(MonitoringPageTests.Sentinel), What);
        }

        [Test]
        public async Task EverySystemPage_IsWellFormed_Labelled_KeyboardUsable_AndSaysItsStateInWords()
        {
            List<(string What, WebPage Page)> All = await SystemPages();
            Assert.That(All, Has.Count.EqualTo(11));
            foreach ((string What, WebPage Page) in All)
            {
                CheckPage(What, Page, "System");
                List<string> Sections = Page.Xml().Descendants("main").Single().Elements("div").Single(D => Attr(D, "id") == "live-view").Elements("section").Select(S => Attr(S, "aria-labelledby")!).ToList();
                LiveMarkup.Check(What, Page, ResultsPageTests.Element(Page, "progress") != null, WebSystemWording.ShowLatest);
                Assert.That(Sections, Is.EqualTo(new[] { "configuration-heading", "data-heading" }), What);
                Assert.That(Page.Xml().Descendants("dl").Single(D => Attr(D, "id") == "configuration").Elements("dt").Count(), Is.EqualTo(8), What);
                if (ResultsPageTests.Element(Page, "progress") is XElement Bar)
                {
                    Assert.That(Page.Xml().Descendants("label").Single(L => Attr(L, "for") == "progress").Value, Is.EqualTo(RefreshWording.ProgressName), What);
                    Assert.That((Attr(ResultsPageTests.Element(Page, "status")!, "role"), Attr(ResultsPageTests.Element(Page, "status")!, "tabindex")), Is.EqualTo(((string?)null, "-1")),
                        What + ": the progress in words; the page's script announces only what changes the user's view (D109)");
                    Assert.That(Page.Xml().Descendants("a").Count(A => A.Value == WebSystemWording.ShowLatest && Attr(A, "href") == Address), Is.EqualTo(1), What);
                    _ = Bar;
                }
            }
            string Text(string What) => All.Single(P => P.What == What).Page.Text();
            Assert.That(Text("at rest"), Does.Contain(RefreshWording.LastRefreshedLabel).And.Contain(RefreshWording.RefreshLabel));
            Assert.That(Text("the confirmation"), Does.Contain(RefreshWording.ConfirmationHeading).And.Contain(RefreshWording.StartLabel).And.Contain(WebSystemWording.ConfirmationBackLink));
            Assert.That(Text("downloading"), Does.Contain("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            Assert.That(Text("an out-of-date page"), Does.Contain("Warning: This page was out of date"));
            Assert.That(Text("canceling"), Does.Contain(RefreshWording.CancelingText).Or.Contain("The refresh was canceled."));
            Assert.That(Text("canceled"), Does.Contain("Info: The refresh was canceled. The campground data was not changed."));
            Assert.That(Text("installing"), Does.Contain(RefreshWording.FinishingText));
            Assert.That(Text("refreshed"), Does.Contain("Info: The campground data was refreshed.").And.Contain(WebSystemWording.RestartNote));
            Assert.That(Text("failed"), Does.Contain("Error: The download from Recreation.gov failed."));
            Assert.That(Text("a search runs"), Does.Contain(RefreshWording.SearchRunningReason));
            Assert.That(Text("a search runs, the confirmation"), Does.Contain(RefreshWording.SearchRunningReason));
        }

        [Test]
        public async Task EveryCampgroundsPage_IsWellFormed_Labelled_KeyboardUsable_AndBounded()
        {
            List<(string What, WebPage Page)> All = await CampgroundPages();
            Assert.That(All, Has.Count.EqualTo(10));
            foreach ((string What, WebPage Page) in All)
            {
                string View = What == "by city" || What == "a city" ? CampgroundsWording.ByCity
                    : What.Contains("list", StringComparison.Ordinal) || What.Contains("chosen", StringComparison.Ordinal) || What.Contains("nothing", StringComparison.Ordinal) ? CampgroundsWording.ByState
                    : CampgroundsWording.ByPark;
                CheckPage(What, Page, CampgroundsWording.Heading + ": " + View);
                Assert.That(Page.Xml().Descendants("ul").Where(U => Attr(U, "aria-label") == CampgroundsWording.CampgroundsListName).SelectMany(U => U.Elements("li")).Count(),
                    Is.LessThanOrEqualTo(CampertronWeb.WebLookupPage.PageSize), What + ": a part of a list at most");
                foreach (XElement Button in Page.Xml().Descendants("main").Single().Descendants("button"))
                {
                    Assert.That(Attr(Button, "disabled"), Is.Null, What + ": nothing unavailable on a page that only looks");
                }
            }
            string Text(string What) => All.Single(P => P.What == What).Page.Text();
            Assert.That(Text("no data"), Does.Contain("Error: " + CampgroundsWording.NoData));
            Assert.That(Text("a park's campground"), Does.Contain(CampgroundsWording.CampgroundIdLabel).And.Contain("0232445").And.Contain(CampgroundsWording.CampgroundIdNote));
            Assert.That(Text("a long list's part"), Does.Contain("Showing campgrounds 101 to 150 of 1,234."));
            Assert.That(Text("nothing the lists hold"), Does.Contain("Warning: " + CampgroundsWording.NotInList));
            Assert.That(Text("hostile names"), Does.Contain("<script>alert(1)</script> Camp"));
        }
    }
}
