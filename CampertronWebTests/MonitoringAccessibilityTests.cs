using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static CampertronWebTests.MonitoringPageTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The monitoring page (v0.3.0's PR 8) as markup, in the states a user meets: never started, not turned on, a start
    // refused, running between two checks with new openings, many openings a page at a time, the second page, nothing new,
    // sending, stopping, stopped, a failed send, a blocked check with Start held, an out-of-date page, hostile names, and
    // without password protection. Each is well-formed, named, labelled, usable with the keyboard alone and without a
    // script; its headings in order; its landmarks and its status region; every message says its severity in words; a
    // disabled action says why in words; a link says where it goes and leaves the web UI only for a recreation.gov
    // campsite page; nothing inline or from another site. Read as XML (no HTML parser, D92).
    public class MonitoringAccessibilityTests
    {
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);
        private static readonly Regex BookingPage = new Regex("^https://www\\.recreation\\.gov/camping/campsites/[A-Za-z0-9%._~-]+$");

        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using (MonitoringHost M = await MonitoringHost.Start(OutputTo: null))
            {
                using Browser Owner = await M.Host.SignedIn();
                WebPage Never = await Owner.Get("/monitoring");
                Found.Add(("never started", Never));
                await Act(Owner, "start", Never);
                Found.Add(("not turned on", await Until(Owner, P => Element(P, "not-started") != null, "not turned on")));
                File.WriteAllText(M.In("General.yaml"), "outputTo: Email\nautoRefresh: false\n");
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                Found.Add(("a start refused", await Until(Owner, P => (Said(P, "not-started") ?? "").Contains("no search file", StringComparison.Ordinal), "a start refused")));
                Found.Add(("an out-of-date page", await Act(Owner, "stop", Never)));

                string[] Was = Sites;
                Sites = Enumerable.Range(1, 30).Select(N => "99000" + N.ToString("0000", CultureInfo.InvariantCulture)).ToArray();
                try
                {
                    File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-Campsites.json"), System.Text.Json.JsonSerializer.Serialize(Sites.Select(Site => new CampsitesRecdata
                    {
                        CampsiteID = Site, FacilityID = CampgroundId, CampsiteName = "Site " + Site.Substring(5), CampsiteType = "STANDARD NONELECTRIC", Loop = "A", TypeOfUse = "Overnight"
                    }).ToList()));
                    File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-CampsitesByPark.json"), System.Text.Json.JsonSerializer.Serialize(Sites.ToList()));
                    foreach (string Site in Sites)
                    {
                        File.WriteAllText(Path.Join(M.Cache, Site + "-Equipment.json"), "[\"Tent\"]");
                        File.WriteAllText(Path.Join(M.Cache, Site + "-Attributes.json"), "[]");
                    }
                    WriteSearch(M, "Alpha.yaml", "First trip");
                    File.WriteAllText(M.In("Bravo.yaml"), Search("Second trip", Consecutive: 2));
                    await Act(Owner, "start", await Owner.Get("/monitoring"));
                    M.Waits.Await();
                    WebPage Running = await Waiting(Owner);
                    Found.Add(("running, new openings", Running));
                    Found.Add(("running, the second page", await Owner.Get("/monitoring?run=3&check=1&search=0&part=2")));
                    Found.Add(("running, consecutive nights", await Owner.Get("/monitoring?run=3&check=1&search=1")));
                    M.Waits.Next();
                    M.Waits.Await();
                    Found.Add(("running, nothing new", await Until(Owner, P => (Said(P, "check-summary") ?? "").StartsWith("Check 2 ", StringComparison.Ordinal), "the second check")));
                    await Act(Owner, "stop", await Owner.Get("/monitoring"));
                    Found.Add(("stopped", await NotRunning(Owner)));
                }
                finally
                {
                    Sites = Was;
                    WriteCampground(M.Cache);
                }

                //sending, stopping, then a failed send
                File.Delete(M.In("Bravo.yaml"));
                Directory.Delete(M.HistoryPath, true);
                var InSend = new ManualResetEventSlim(false);
                var Release = new ManualResetEventSlim(false);
                Send = Name =>
                {
                    InSend.Set();
                    Release.Wait(Long);
                    throw new SmtpException(SmtpStatusCode.MailboxUnavailable, ServerText);
                };
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                Assert.That(InSend.Wait(Long), Is.True);
                WebPage Sending = await Owner.Get("/monitoring");
                Found.Add(("sending", Sending));
                Found.Add(("stopping", await Act(Owner, "stop", Sending)));
                Release.Set();
                Found.Add(("a failed send", await Until(Owner, P => Element(P, "failure") != null, "a failed send")));

                //a block, and Start held
                Send = Name => { };
                Answer = Blocked;
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                Found.Add(("a blocked check, Start held", await Until(Owner, P => Element(P, "failure") != null && Element(P, "cooldown") != null, "a block")));
                Answer = Available;
            }
            await using (MonitoringHost M = await MonitoringHost.Start())
            {
                File.WriteAllText(Path.Join(M.Cache, CampgroundId + "-Campsites.json"), System.Text.Json.JsonSerializer.Serialize(Campsites.Select((Site, Index) => new CampsitesRecdata
                {
                    CampsiteID = Site, FacilityID = CampgroundId, CampsiteName = "<script>alert(1)</script> " + Index, CampsiteType = "\"><b>x</b>", Loop = "A", TypeOfUse = "Overnight"
                }).ToList()));
                File.WriteAllText(M.In("Alpha.yaml"), Search("<img src=x onerror=alert(1)> & <b>trip</b>"));
                using Browser Owner = await M.Host.SignedIn();
                await Act(Owner, "start", await Owner.Get("/monitoring"));
                M.Waits.Await();
                Found.Add(("hostile names", await Waiting(Owner)));
            }
            await using (MonitoringHost M = await MonitoringHost.Start(WithPassword: false, WebYaml: "passwordProtection: false\nnetworkListening: false\n"))
            {
                using Browser Anyone = M.Host.Browser();
                Found.Add(("without password protection", await Anyone.Get("/monitoring")));
            }
            return Found;
        }

        [Test]
        public async Task EveryMonitoringPage_IsWellFormed_Labelled_KeyboardUsable_AndSaysItsStateInWords()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(15));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
                XElement Html = Page.Xml().Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                Assert.That(Html.Descendants("title").Single().Value, Is.EqualTo("Monitoring – Campertron"), What);
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
                //its parts are landmarks named by their headings: the status, the actions, the last check; the parts the page's
                //script puts in place (D109), each with the name of what it shows
                List<XElement> Parts = Main.Elements("div").Single(D => Attr(D, "id") == "live-view").Elements("section").ToList();
                Assert.That(Parts.Select(S => (Attr(S, "id"), Attr(S, "data-part") is string Name && System.Text.RegularExpressions.Regex.IsMatch(Name, "^[0-9a-f]{16}$"))),
                    Is.EqualTo(new[] { ((string?)WebMonitoringLive.StatusPart, true), ((string?)WebMonitoringLive.ActionsPart, true), ((string?)WebMonitoringLive.CheckPart, true) }), What);
                foreach (XElement Section in Parts)
                {
                    string? Named = Attr(Section, "aria-labelledby");
                    Assert.That(Named, Is.Not.Null, What);
                    Assert.That(Elements.Single(E => Attr(E, "id") == Named).Name.LocalName, Does.Match("^h[23]$"), What);
                }
                Assert.That(Parts.Select(S => Attr(S, "aria-labelledby")), Is.EqualTo(new[] { "status-heading", "actions-heading", "check-heading" }), What);

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

                //the state: a status region, in words; a link to the latest status
                XElement Status = Elements.Single(E => Attr(E, "id") == "status");
                bool Active = LiveMarkup.IsActive(Page);
                Assert.That((Attr(Status, "role"), Attr(Status, "tabindex")), Is.EqualTo((Active ? null : "status", "-1")), What + ": a status region, or, while the page's script follows the monitoring (D109), its announcements");
                LiveMarkup.Check(What, Page, Active, WebMonitoringWording.ShowLatest);
                Assert.That(Status.Value.Trim(), Does.Match("^(Not running|Running|Starting|Stopping|Restarting)"), What + ": the state in words, not by color");
                Assert.That(Elements.Count(E => E.Name == "a" && Attr(E, "href") == "/monitoring" && E.Value == WebMonitoringWording.ShowLatest), Is.EqualTo(1), What);

                //the one form posts to this web UI with its token and the moment it showed; each button says what it does and
                //has its own address; a disabled one is described by the words that say why
                XElement Form = MonitoringPageTests.Form(Page);
                Assert.That(Attr(Form, "method"), Is.EqualTo("post"), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "state" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "type") != "hidden"), Is.Zero, What + ": nothing typed: Enter starts nothing");
                Assert.That(Form.Descendants("button").Select(B => B.Value.Trim()), Is.EqualTo(new[] { "Start", "Stop", "Restart" }), What);
                foreach (XElement Button in Form.Descendants("button"))
                {
                    Assert.That(Attr(Button, "formaction"), Does.Match("^/monitoring\\?handler=(start|stop|restart)$"), What);
                    List<string> Described = (Attr(Button, "aria-describedby") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    Assert.That(Described.First(), Does.EndWith("-help"), What + ": what it does");
                    if (Attr(Button, "disabled") != null)
                    {
                        Assert.That(Described, Does.Contain("actions-unavailable"), What + ": why " + Button.Value + " is unavailable");
                        Assert.That(Said(Page, "actions-unavailable"), Is.Not.Empty, What);
                    }
                }

                //messages say their severity in words
                foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ').Contains("message")))
                {
                    Assert.That(string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0)), Does.Match("^(Error|Warning|Info): "), What);
                }

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

                //links: each says where it goes, the same words always go to the same page, only a recreation.gov campsite page
                //is outside the web UI
                var Links = Elements.Where(E => E.Name == "a").Select(A => (Text: A.Value.Trim(), Href: Attr(A, "href")!)).ToList();
                Assert.That(Links.Select(L => L.Text), Has.None.Matches("^(?i)(open|book|link|here|click here|more|details|view)$"), What);
                Assert.That(Links.GroupBy(L => L.Text).Where(G => G.Select(L => L.Href).Distinct().Count() > 1).Select(G => G.Key), Is.Empty, What + ": the same words, another page");
                Assert.That(Links.Where(L => !L.Href.StartsWith("/", StringComparison.Ordinal) && !L.Href.StartsWith("#", StringComparison.Ordinal) && !BookingPage.IsMatch(L.Href)), Is.Empty, What);
                Assert.That(Links.Where(L => BookingPage.IsMatch(L.Href)).Select(L => L.Text), Has.All.StartWith("Booking page of "), What);

                //nothing inline and nothing from another site
                Assert.That(Elements.Where(E => (E.Name == "script" && !LiveMarkup.IsLiveScript(E)) || E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
                foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
                {
                    string Name = Attribute.Name.LocalName;
                    Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
                    if (Name is "src" or "action" or "formaction")
                    {
                        Assert.That(Attribute.Value, Does.StartWith("/"), What + ": " + Name);
                    }
                }
                Assert.That(Page.Body, Does.Not.Contain(ServerText).And.Not.Contain(MonitoringPageTests.Sentinel), What);
            }

            //the states, as the pages say them
            string Text(string What) => All.Single(P => P.What == What).Page.Text();
            Assert.That(Text("never started"), Does.Contain("Not running.").And.Contain(WebMonitoringWording.NoCheckYet).And.Contain(WebMonitoringWording.StopUnavailableIdle));
            Assert.That(Text("not turned on"), Does.Contain("Info: the monitoring is not turned on"));
            Assert.That(Text("a start refused"), Does.Contain("Error: email mode has no search to check"));
            Assert.That(Text("an out-of-date page"), Does.Contain("Warning: " + WebMonitoringWording.StaleText));
            Assert.That(Text("running, new openings"), Does.Contain("Running: the next check starts at ").And.Contain("Showing openings 1 to 200 of ").And.Contain("Booking page of Site 0001"));
            Assert.That(Text("running, the second page"), Does.Contain("Showing openings 201 to "));
            Assert.That(Text("running, consecutive nights"), Does.Contain("Arrive "));
            Assert.That(Text("running, nothing new"), Does.Contain("nothing new to notify.").And.Contain(WebMonitoringWording.NoNewOpening));
            Assert.That(Text("sending"), Does.Contain("Running: check 1 is sending its notifications."));
            Assert.That(Text("stopping"), Does.Contain("Stopping: ").And.Contain(WebMonitoringWording.UnavailableBusy));
            Assert.That(Text("stopped"), Does.Contain("Not running: it was stopped at "));
            Assert.That(Text("a failed send"), Does.Contain("Error: the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(Text("a blocked check, Start held"), Does.Contain("Error: recreation.gov limited Campertron's requests").And.Contain("Start is available again in 5:00."));
            Assert.That(Text("hostile names"), Does.Contain("<script>alert(1)</script>").And.Contain("<img src=x onerror=alert(1)> & <b>trip</b>"));
            Assert.That(Text("without password protection"), Does.Contain("Not running."));
            Assert.That(All.Single(P => P.What == "without password protection").Page.Xml().Descendants("a").Select(A => A.Value), Does.Not.Contain("Change password"));
        }
    }
}
