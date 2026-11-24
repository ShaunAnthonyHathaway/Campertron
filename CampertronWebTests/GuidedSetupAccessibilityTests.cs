using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;

namespace CampertronWebTests
{
    // The guided setup (v0.3.0's PR 10, D100) as markup, in the states a user meets: step 1 as it opens and with nothing
    // chosen, the email step with no Email.yaml, with its form, with a value refused, after a Save and continue that is not
    // ready yet and after a test, the ntfy step, the search step empty and ready, a search's page as it was created and
    // with a value refused, the review not ready and ready, done and not done, and the start page's section. Each is
    // well-formed, named, labelled, usable with the keyboard alone and without a script; its steps are a list with the
    // current one marked and each step's state in words; its errors are listed at the top and tied to their fields; a
    // message says its severity in words; a disabled action says why; the SMTP password's box is never filled in; every
    // form posts to this web UI with its token and the revision it showed (Start, the monitoring page's own, its moment);
    // nothing is inline or from another site. Read as XML (no HTML parser, D92).
    public class GuidedSetupAccessibilityTests
    {
        private const string Canary = "A11Y-guided-cnry";
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);

        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using WebTestHost Host = await GuidedSetupPageTests.Start();
            string In(string Name) => Path.Join(Host.Folder, Name);
            using Browser Owner = await Host.SignedIn();
            Found.Add(("step 1", await Owner.Get("/guided-setup")));
            Found.Add(("step 1 with nothing chosen", await Owner.Get("/guided-setup?continue=1")));
            const string Email = "/guided-setup/email/notifications";
            WebPage Missing = await Owner.Get(Email);
            Found.Add(("the email step without Email.yaml", Missing));
            WebPage Created = await Owner.Post(Email, GuidedForm.Fields(Missing, Email, "create").ToArray());
            Found.Add(("the email step with its form", Created));
            Found.Add(("the email step with a value refused", await Owner.Post(Email, GuidedForm.Fields(Created, Email, "continue", ("f.smtpPort", "x"), ("secret.smtpPassword", Canary)).ToArray())));
            WebPage NotReady = await Owner.Post(Email, GuidedForm.Fields(await Owner.Get(Email), Email, "continue", ("f.smtpServer", "smtp.example.invalid"), ("f.smtpPort", "587"), ("secret.smtpPassword", Canary)).ToArray());
            Found.Add(("the email step not ready", NotReady));
            Found.Add(("the email step's test", await Owner.Post(Email + "?handler=test", GuidedForm.Fields(NotReady, Email, null).ToArray())));
            Found.Add(("the ntfy step", await Owner.Get("/guided-setup/ntfy/notifications")));
            Found.Add(("the search step without a search", await Owner.Get("/guided-setup/email/search")));
            WebPage List = await Owner.Get("/guided-setup/email/search");
            WebPage New = await Owner.Post("/guided-setup/email/search?handler=create", GuidedForm.Fields(List, "/guided-setup/email/search", null, ("new.name", "Lake trip")).ToArray());
            WebPage Search = await Owner.Get(New.Location!);
            Found.Add(("a search's page as it was created", Search));
            Found.Add(("a search's page with a value refused", await Owner.Post(New.Location!, GuidedForm.Fields(Search, New.Location!, "continue", ("f.totalHumans", "many")).ToArray())));
            Found.Add(("the review, not ready", await Owner.Get("/guided-setup/email/turn-on")));
            Found.Add(("done, not finished", await Owner.Get("/guided-setup/email/done")));
            File.WriteAllText(In("Email.yaml"), GuidedSetupPageTests.ValidEmail(Canary));
            File.WriteAllText(In("Trip.yaml"), SearchesPageTests.Search("First trip"));
            Found.Add(("the search step, ready", await Owner.Get("/guided-setup/email/search")));
            WebPage Review = await Owner.Get("/guided-setup/email/turn-on");
            Found.Add(("the review, ready", Review));
            await Owner.Post("/guided-setup/email/turn-on?handler=turnon", GuidedForm.Fields(Review, "/guided-setup/email/turn-on", null).ToArray());
            Found.Add(("done", await Owner.Get("/guided-setup/email/done")));
            Found.Add(("the start page's section", await Owner.StartPage()));
            return Found;
        }

        [Test]
        public async Task EveryGuidedPage_IsWellFormed_Labelled_KeyboardUsable_AndSaysItsStateInWords()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(17));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
                Assert.That(Page.Body, Does.Not.Contain(Canary), What + ": the SMTP password is never in a page");
                XElement Html = Page.Xml().Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();
                XElement Main = Html.Descendants("main").Single();
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                string Title = Html.Descendants("title").Single().Value;
                Assert.That(Title, Does.EndWith(" – Campertron"), What);
                Assert.That(Main.Descendants("h1").Count(), Is.EqualTo(1), What);
                int Level = 1;
                foreach (XElement Head in Elements.Where(E => E.Name.LocalName.Length == 2 && E.Name.LocalName[0] == 'h' && char.IsDigit(E.Name.LocalName[1])))
                {
                    int This = Head.Name.LocalName[1] - '0';
                    Assert.That(This, Is.LessThanOrEqualTo(Level + 1), What + ": heading " + Head.Value);
                    Assert.That(Head.Value.Trim(), Is.Not.Empty, What);
                    Level = This;
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
                    if (E.Name == "a" && Attr(E, "href") is string Href && Href.StartsWith('#'))
                    {
                        Assert.That(Ids, Does.Contain(Href.Substring(1)), What + ": the link " + E.Value);
                    }
                }
                foreach (XElement Input in Main.Descendants("input").Where(E => Attr(E, "type") != "hidden"))
                {
                    string Id = Attr(Input, "id")!;
                    Assert.That(Main.Descendants("label").Count(L => Attr(L, "for") == Id && L.Value.Trim().Length > 0), Is.EqualTo(1), What + ": the label of " + Id);
                    if (Attr(Input, "type") == "radio")
                    {
                        Assert.That(Input.Ancestors("fieldset").FirstOrDefault()?.Element("legend")?.Value.Trim(), Is.Not.Null.And.Not.Empty, What + ": the group of " + Id);
                    }
                    if (Attr(Input, "type") == "password")
                    {
                        Assert.That((Attr(Input, "value"), Attr(Input, "autocomplete")), Is.EqualTo(((string?)null, "new-password")), What + ": " + Id + " is never filled in");
                    }
                    if (Attr(Input, "aria-invalid") == "true" && Attr(Input, "name") != "choice")
                    {
                        string Message = Attr(Input, "aria-describedby")!.Split(' ').First(Part => Part.EndsWith("-msg", StringComparison.Ordinal));
                        Assert.That(Elements.Single(E => Attr(E, "id") == Message).Value, Does.StartWith("Error: "), What + ": the field's error is tied to it");
                    }
                }
                foreach (XElement Button in Main.Descendants("button"))
                {
                    Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                    Assert.That(Attr(Button, "type") ?? "submit", Is.EqualTo("submit"), What);
                    if (Attr(Button, "formaction") is string Target)
                    {
                        Assert.That(Target, Does.StartWith("/guided-setup/").And.EndWith("?handler=test").Or.EndWith("?handler=create"), What);
                    }
                }
                XElement? Turn = Main.Descendants("button").FirstOrDefault(B => B.Value.StartsWith("Turn the monitoring on", StringComparison.Ordinal));
                if (Turn != null && Attr(Turn, "disabled") != null)
                {
                    string Why = string.Join(" ", Attr(Turn, "aria-describedby")!.Split(' ').Select(Id => Elements.Single(E => Attr(E, "id") == Id).Value));
                    Assert.That(Why, Does.Contain("the monitoring would refuse the saved files"), What + ": why it is unavailable, in words");
                }
                foreach (XElement Form in Main.Descendants("form"))
                {
                    string Action = Attr(Form, "action")!;
                    Assert.That(Action, Does.StartWith("/").And.Not.StartWith("//"), What);
                    if (Attr(Form, "method") == "post")
                    {
                        Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                        string Moment = Action.StartsWith("/monitoring", StringComparison.Ordinal) ? "state" : "revision";
                        Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == Moment && Attr(I, "type") == "hidden"), Is.EqualTo(1), What + ": the moment the page showed");
                    }
                    else
                    {
                        Assert.That((Attr(Form, "method"), Action), Is.EqualTo(("get", "/guided-setup")), What + ": only step 1's choice is a GET, which saves nothing");
                    }
                }
                foreach (XElement Editor in Main.Descendants("form").Where(F => Attr(F, "method") == "post" && F.Descendants("button").Any(B => Attr(B, "value") == "check")))
                {
                    XElement Default = Editor.Descendants("button").First();
                    Assert.That((Attr(Default, "value"), Attr(Default, "tabindex"), Attr(Default, "aria-hidden")), Is.EqualTo(("check", "-1", "true")), What + ": Enter applies and checks, nothing more");
                }
                XElement? Summary = Elements.FirstOrDefault(E => Attr(E, "id") == "error-summary");
                Assert.That(Title.StartsWith("Error: ", StringComparison.Ordinal), Is.EqualTo(Summary != null), What);
                foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ') is string[] C && (C.Contains("message") || C.Contains("field-message")) && Attr(E, "id") != "error-summary"))
                {
                    string Text = string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    if (Message.Element("h3") != null)
                    {
                        Text = string.Join(" ", Message.Elements().Where(E => E.Name != "h3" && E.Name != "div").SelectMany(E => E.DescendantNodes().OfType<XText>()).Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    }
                    Assert.That(Text, Does.Match("^(Error|Warning|Info): |^[A-Za-z]+\\.yaml: (Error|Warning|Info): "), What + ": " + Text);
                }
                Assert.That(Elements.Count(E => Attr(E, "autofocus") != null), Is.LessThanOrEqualTo(1), What);
                Assert.That(Elements.Where(E => Attr(E, "tabindex") is string Tab && Tab != "-1"), Is.Empty, What);
                Assert.That(Html.Descendants("body").Single().Elements().First().Attribute("href")?.Value, Is.EqualTo("#main"), What);
                foreach (XElement Nav in Html.Descendants("nav"))
                {
                    Assert.That(Attr(Nav, "aria-label"), Is.Not.Null.And.Not.Empty, What);
                    Assert.That(Nav.Descendants("a").Count(A => Attr(A, "aria-current") == "page"), Is.LessThanOrEqualTo(1), What);
                }
                if (Elements.FirstOrDefault(E => E.Name == "nav" && Attr(E, "aria-label") == GuidedSetupWording.StepsName) is XElement Steps)
                {
                    List<XElement> Items = Steps.Descendants("li").ToList();
                    Assert.That(Items, Has.Count.EqualTo(GuidedSetupWording.StepCount), What);
                    Assert.That(Items.Count(Li => Attr(Li, "aria-current") == "step"), Is.EqualTo(What.StartsWith("done", StringComparison.Ordinal) ? 0 : 1), What + ": the current step marked");
                    Assert.That(Items.Select(Li => Li.Value), Has.All.Match("\\((done|current step|to do)\\)$"), What + ": each step's state in words");
                }
                else
                {
                    Assert.That(What, Is.EqualTo("the start page's section"));
                }
                var Links = Elements.Where(E => E.Name == "a").Select(A => (Text: A.Value.Trim(), Href: Attr(A, "href")!)).ToList();
                Assert.That(Links.Select(L => L.Text), Has.None.Matches("^(?i)(open|link|here|click here|more|details|view)$"), What);
                Assert.That(Links.Where(L => !L.Href.StartsWith("/", StringComparison.Ordinal) && !L.Href.StartsWith("#", StringComparison.Ordinal)), Is.Empty, What + ": every link stays in the web UI");
                Assert.That(Elements.Where(E => E.Name == "script" || E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
                foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
                {
                    string Name = Attribute.Name.LocalName;
                    Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
                }
            }
            //the states the list says, as the pages show them
            Assert.That(All.Single(P => P.What == "step 1 with nothing chosen").Page.Xml().Descendants().Single(E => Attr(E, "id") == "error-summary").Attribute("role")?.Value, Is.EqualTo("alert"));
            Assert.That(All.Single(P => P.What == "the email step with a value refused").Page.Xml().Descendants().Count(E => Attr(E, "aria-invalid") == "true"), Is.EqualTo(1));
            Assert.That(All.Single(P => P.What == "the email step not ready").Page.Text(), Does.Contain(GuidedSetupWording.NotReady(GuidedSetupMethod.Email).Substring(9)));
            Assert.That(All.Single(P => P.What == "the email step's test").Page.Xml().Descendants().Single(E => Attr(E, "id") == "test-result").Value, Does.StartWith("Warning: ").Or.StartWith("Error: "));
            Assert.That(All.Single(P => P.What == "a search's page with a value refused").Page.Xml().Descendants().Single(E => Attr(E, "id") == "error-summary").Attribute("role")?.Value, Is.EqualTo("alert"));
            Assert.That(All.Single(P => P.What == "done").Page.Text(), Does.Contain(GuidedSetupWording.DoneHeading(GuidedSetupMethod.Email)));
            Assert.That(All.Single(P => P.What == "done, not finished").Page.Text(), Does.Contain(GuidedSetupWording.NotDoneHeading));
        }
    }
}
