using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The results page (v0.3.0's PR 7) as markup, in the states a user meets: no search files, the files to choose, a run
    // going on and being canceled, another session's run, a run's results, many openings a page at a time, a search
    // without openings, consecutive nights, a failed run, a blocked run, an out-of-date page, a changed choice, hostile
    // names and no cache folder. Each is well-formed, named, labelled, usable with the keyboard alone and without a script;
    // its headings in order; a message says its severity in words; a link says where it goes and leaves the web UI only for
    // a recreation.gov campsite page; no table, no scrolling region, nothing inline or from another site. Read as XML (no
    // HTML parser, D92).
    public class ResultsAccessibilityTests
    {
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);
        private static readonly Regex BookingPage = new Regex("^https://www\\.recreation\\.gov/camping/campsites/[A-Za-z0-9%._~-]+$");

        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using (ResultsHost R = await ResultsHost.Start())
            {
                using Browser Owner = await R.Host.SignedIn();
                using Browser Other = await R.Host.SignedIn();
                Found.Add(("no search files", await Owner.Get("/results")));
                File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
                File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip", AutoRun: false));
                File.WriteAllText(R.In("Broken.yaml"), "displayName: [not, a, name\n");
                WebPage Files = await Owner.Get("/results");
                Found.Add(("the search files", Files));
                Found.Add(("the choice changed", await Act(Owner, "/results", Fields(Files, "all"))));
                Found.Add(("an out-of-date page", await Act(Owner, "/results?handler=start", Fields(Files))));
                R.Gov.Mode = Gov.Stall;
                await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
                Assert.That(R.Gov.Stalled.Wait(TimeSpan.FromSeconds(15)), Is.True);
                WebPage Running = await Owner.Get("/results");
                Found.Add(("a run going on", Running));
                Found.Add(("another session's run", await Other.Get("/results")));
                string Run = Attr(Form(Running).Descendants("button").Single(B => Attr(B, "formaction") == "/results?handler=cancel"), "value")!;
                Found.Add(("a run being canceled", await Act(Owner, "/results?handler=cancel", new[] { ("run", Run) })));
                await Ended(Owner);
                R.Gov.Mode = Gov.Available;
                await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
                Found.Add(("a run's results", await Ended(Owner)));
                R.Gov.Mode = Gov.RateLimited;
                await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
                Found.Add(("a blocked run", await Ended(Owner)));
            }
            var Night = new DateTime(2026, 12, 1);
            var Many = Enumerable.Range(0, 5).SelectMany(n => Enumerable.Range(0, 70).Select(s => Opening((1000 + s).ToString(System.Globalization.CultureInfo.InvariantCulture), Night.AddDays(n)))).ToList();
            var Hostile = new[] { Opening("7", Night, Name: "<script>alert(1)</script>"), Opening("8", Night, Url: "javascript:alert(1)") };
            SearchResult Stays = Result("Charlie.yaml", Many.Take(140), Name: "Third trip") with { ConsecutiveNights = 2 };
            await using (ResultsHost R = await ResultsHost.Start(Run: Returns(Result("Alpha.yaml", Many, Name: "First trip"), Result("Bravo.yaml", Array.Empty<SearchOpening>(), Name: "Second trip"),
                Stays, Result("Delta.yaml", Hostile, Name: "\"><b>Fourth</b>"))))
            {
                foreach (string Name in new[] { "Alpha", "Bravo", "Charlie", "Delta" })
                {
                    File.WriteAllText(R.In(Name + ".yaml"), Search(Name));
                }
                using Browser Owner = await R.Host.SignedIn();
                await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
                Found.Add(("many openings, a page of them", await Ended(Owner)));
                Found.Add(("many openings, the second page", await Owner.Get("/results?run=1&search=0&part=2")));
                Found.Add(("a search without openings", await Owner.Get("/results?run=1&search=1")));
                Found.Add(("consecutive nights", await Owner.Get("/results?run=1&search=2")));
                Found.Add(("hostile names and links", await Owner.Get("/results?run=1&search=3")));
            }
            await using (ResultsHost R = await ResultsHost.Start(Run: (Plan, Progress, Token, Options) => throw new InvalidOperationException("secret")))
            {
                File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
                using Browser Owner = await R.Host.SignedIn();
                await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
                Found.Add(("a failed run", await Ended(Owner)));
            }
            await using (ResultsHost R = await ResultsHost.Start(WithCache: false))
            {
                File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
                using Browser Owner = await R.Host.SignedIn();
                Found.Add(("no cache folder", await Owner.Get("/results")));
            }
            return Found;
        }

        [Test]
        public async Task EveryResultsPage_IsWellFormed_Labelled_KeyboardUsable_AndLinksOnlyWhereItSays()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(16));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
                XElement Html = Page.Xml().Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                Assert.That(Html.Descendants("title").Single().Value, Is.EqualTo("Results – Campertron"), What);
                Assert.That(Html.Descendants("main").Single().Descendants("h1").Count(), Is.EqualTo(1), What);
                int Level = 1;
                foreach (XElement Head in Elements.Where(E => E.Name.LocalName.Length == 2 && E.Name.LocalName[0] == 'h' && char.IsDigit(E.Name.LocalName[1])))
                {
                    int This = Head.Name.LocalName[1] - '0';
                    Assert.That(This, Is.LessThanOrEqualTo(Level + 1), What + ": heading " + Head.Value);
                    Assert.That(Head.Value.Trim(), Is.Not.Empty, What);
                    Level = This;
                }

                //ids unique; every pointer finds its element
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

                //every box has one visible label and is in the fieldset of the files to run; the bar has its label
                foreach (XElement Input in Elements.Where(E => E.Name == "input" && Attr(E, "type") != "hidden"))
                {
                    string Id = Attr(Input, "id")!;
                    Assert.That(Attr(Input, "type"), Is.EqualTo("checkbox"), What);
                    Assert.That(Elements.Count(E => E.Name == "label" && Attr(E, "for") == Id && E.Value.Trim().Length > 0), Is.EqualTo(1), What + ": the label of " + Id);
                    Assert.That(Input.Ancestors("fieldset").Single().Element("legend")!.Value, Is.EqualTo(ResultsWording.SearchFilesName), What);
                }
                foreach (XElement Bar in Elements.Where(E => E.Name == "progress"))
                {
                    Assert.That(Elements.Count(E => E.Name == "label" && Attr(E, "for") == Attr(Bar, "id")), Is.EqualTo(1), What + ": the bar's label");
                }

                //the one form posts to this web UI with its token and the revision it showed; Enter keeps the boxes; every
                //button says what it does; Start and Cancel have their own addresses
                XElement Form = ResultsPageTests.Form(Page);
                Assert.That((Attr(Form, "method"), Attr(Form, "action")), Is.EqualTo(("post", "/results")), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "revision" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                XElement Default = Form.Descendants("button").First();
                Assert.That((Attr(Default, "value"), Attr(Default, "tabindex"), Attr(Default, "aria-hidden")), Is.EqualTo(("keep", "-1", "true")), What + ": Enter keeps the boxes, nothing more");
                foreach (XElement Button in Elements.Where(E => E.Name == "button"))
                {
                    Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                    if (Attr(Button, "formaction") is string Target)
                    {
                        Assert.That(Target, Is.EqualTo("/results?handler=start").Or.EqualTo("/results?handler=cancel"), What);
                    }
                }

                //messages say their severity in words; the status is text
                foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ') is string[] C && (C.Contains("message") || C.Contains("field-message"))))
                {
                    Assert.That(string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0)), Does.Match("^(Error|Warning|Info): "), What);
                }
                Assert.That(Elements.Single(E => Attr(E, "id") == "status").Value.Trim(), Is.Not.Empty, What);

                //one thing at most takes the focus; the keyboard's order is the page's; no table, nothing that scrolls inside the page
                Assert.That(Elements.Count(E => Attr(E, "autofocus") != null), Is.LessThanOrEqualTo(1), What);
                Assert.That(Elements.Where(E => Attr(E, "tabindex") is string Tab && Tab != "-1"), Is.Empty, What);
                Assert.That(Html.Descendants("body").Single().Elements().First().Attribute("href")?.Value, Is.EqualTo("#main"), What + ": a way past the header");
                Assert.That(Elements.Where(E => E.Name == "table" || (Attr(E, "class") ?? "").Split(' ').Intersect(new[] { "bounded", "long" }).Any()), Is.Empty, What);
                foreach (XElement Nav in Html.Descendants("nav"))
                {
                    Assert.That(Attr(Nav, "aria-label"), Is.Not.Null.And.Not.Empty, What);
                    Assert.That(Nav.Descendants("a").Count(A => Attr(A, "aria-current") == "page"), Is.LessThanOrEqualTo(1), What);
                }

                //links: each says where it goes (never a bare "Open" or "Book"), the same words always go to the same page, and
                //only a recreation.gov campsite page is outside the web UI
                var Links = Elements.Where(E => E.Name == "a").Select(A => (Text: A.Value.Trim(), Href: Attr(A, "href")!)).ToList();
                Assert.That(Links.Select(L => L.Text), Has.None.Matches("^(?i)(open|book|link|here|click here|more|details|view)$"), What);
                Assert.That(Links.GroupBy(L => L.Text).Where(G => G.Select(L => L.Href).Distinct().Count() > 1).Select(G => G.Key), Is.Empty, What + ": the same words, another page");
                Assert.That(Links.Where(L => !L.Href.StartsWith("/", StringComparison.Ordinal) && !L.Href.StartsWith("#", StringComparison.Ordinal) && !BookingPage.IsMatch(L.Href)), Is.Empty, What);
                Assert.That(Links.Where(L => L.Href.StartsWith("//", StringComparison.Ordinal)), Is.Empty, What);
                Assert.That(Links.Where(L => BookingPage.IsMatch(L.Href)).Select(L => L.Text), Has.All.StartWith("Booking page of "), What);

                //nothing inline and nothing from another site: the only script is the web UI's own, while a run of the session
                //goes on (D108; since D109 the one of every live page), named by its address in the head, with nothing inside it
                Assert.That(Elements.Where(E => E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
                List<XElement> Scripts = Elements.Where(E => E.Name == "script").ToList();
                bool Running = Elements.Any(E => Attr(E, "id") == "progress");
                LiveMarkup.Check(What, Page, Running, "Show the latest progress");
                Assert.That(Scripts.Select(S => (Attr(S, "src"), Attr(S, "defer"), S.Value, S.Parent!.Name.LocalName)),
                    Is.EqualTo(Running ? new[] { ((string?)WebAssets.LiveScriptPath, (string?)"defer", "", "head") } : Array.Empty<(string?, string?, string, string)>()), What);
                //the announcements: one polite region outside the part the script replaces, empty until something changes
                List<XElement> Regions = Elements.Where(E => Attr(E, "role") == "status" || Attr(E, "aria-live") != null).ToList();
                Assert.That(Regions.Select(E => (Attr(E, "id"), Attr(E, "role"), Attr(E, "aria-live"), Attr(E, "aria-atomic"), E.Value)),
                    Is.EqualTo(Running ? new[] { ((string?)"live-announce", (string?)"status", (string?)"polite", (string?)"true", "") } : Array.Empty<(string?, string?, string?, string?, string)>()), What);
                Assert.That(Regions.All(E => !E.Ancestors().Any(A => Attr(A, "id") == "live-view")), Is.True, What);
                foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
                {
                    string Name = Attribute.Name.LocalName;
                    Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
                    if (Name is "src" or "action" or "formaction")
                    {
                        Assert.That(Attribute.Value, Does.StartWith("/"), What + ": " + Name);
                    }
                }
            }
            //the states, as the pages say them
            string Text(string What) => All.Single(P => P.What == What).Page.Text();
            Assert.That(Text("no search files"), Does.Contain(ResultsWording.NoSearchFiles));
            Assert.That(Text("an out-of-date page"), Does.Contain("This page was out of date"));
            Assert.That(Text("a run going on"), Does.Contain(ResultsWording.Searching(2)).And.Contain("Show the latest progress").And.Contain(WebResultsLiveWording.Manual));
            Assert.That(Text("another session's run"), Does.Contain(ResultsWording.OtherSessionRunning));
            Assert.That(Text("a run being canceled"), Does.Contain("Canceled").Or.Contain("Canceling"));
            Assert.That(Text("a run's results"), Does.Contain(ResultsWording.LastRunHeading).And.Contain("Booking page of Site 001"));
            Assert.That(Text("many openings, a page of them"), Does.Contain("Showing openings 1 to 200 of 350."));
            Assert.That(Text("many openings, the second page"), Does.Contain("Showing openings 201 to 350 of 350."));
            Assert.That(Text("a search without openings"), Does.Contain("No campsite matched this search in the months checked."));
            Assert.That(Text("consecutive nights"), Does.Contain("Arrive "));
            Assert.That(Text("hostile names and links"), Does.Contain("<script>alert(1)</script>").And.Contain(ResultsWording.NoBookingLink));
            Assert.That(Text("a failed run"), Does.Contain("Nothing could be searched.").And.Not.Contain("secret"));
            Assert.That(Text("a blocked run"), Does.Contain("Start is available again in 5:00."));
            Assert.That(Text("no cache folder"), Does.Contain(ResultsWording.NoCacheFolder));
        }
    }
}
