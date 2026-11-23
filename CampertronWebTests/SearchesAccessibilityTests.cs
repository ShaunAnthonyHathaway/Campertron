using System.Net;
using CampertronLibrary.function.Forms;
using System.Xml.Linq;

namespace CampertronWebTests
{
    // The searches pages (v0.3.0's PR 6) as markup, in the states a user meets: the list with and without files, a file as
    // it opens, a value refused, a name refused for New search and for Save As, a new file's page, the campgrounds found, a
    // file that can't be edited, a conflict, a file deleted under the edits and an out-of-date page. Each is well-formed,
    // named, labelled, usable with the keyboard alone and without a script; its errors are listed at the top and tied to
    // their fields; a message says its severity in words; Enter presses Check changes; nothing is inline or from another
    // site; no page has a button to run, start, stop or delete anything. Read as XML (no HTML parser, D92).
    public class SearchesAccessibilityTests
    {
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);

        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using WebTestHost Host = await SearchesPageTests.Start();
            using Browser Owner = await Host.SignedIn();
            Found.Add(("the list without files", await Owner.Get("/searches")));
            File.WriteAllText(Path.Join(Host.Folder, "Alpha.yaml"), SearchesPageTests.Search("First trip", Extra: "includeEquipment:\n- Tent\n- Hammock\n- ''\n"));
            File.WriteAllText(Path.Join(Host.Folder, "Bravo.yaml"), SearchesPageTests.Search("Second trip", AutoRun: false));
            File.WriteAllText(Path.Join(Host.Folder, "Broken.yaml"), "displayName: [not, a, name\n");
            Found.Add(("the list", await Owner.Get("/searches")));
            WebPage Alpha = await Owner.Get("/searches/Alpha.yaml");
            Found.Add(("a file", Alpha));
            Found.Add(("a value refused", await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Alpha, "save", ("f.totalHumans", "two"), ("f.consecutiveDays", "x")).ToArray())));
            WebPage Refused = Found.Last().Item2;
            Found.Add(("an ID refused", await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Refused, "use-campground-id", (WebSettingsForm.CampgroundTyped, "nope")).ToArray())));
            WebPage Fresh = await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Found.Last().Item2, "revert").ToArray());
            Found.Add(("the campgrounds found", await Owner.Post("/searches/Alpha.yaml", SearchForm.Fields(Fresh, "find-campground", (WebSettingsForm.CampgroundFind, "pines")).ToArray())));
            Found.Add(("a name refused by Save As", await Owner.Post("/searches/Alpha.yaml?handler=saveas", SearchForm.Fields(Found.Last().Item2, "saveas", ("saveas.name", "Bravo")).ToArray())));
            WebPage List = await Owner.Get("/searches");
            Found.Add(("a name refused by New search", await Owner.Post("/searches?handler=create", SearchForm.Fields(List, "create", ("new.name", "../x")).ToArray())));
            WebPage Created = await Owner.Post("/searches?handler=create", SearchForm.Fields(Found.Last().Item2, "create", ("new.name", "Weekend")).ToArray());
            Found.Add(("a new file", await Owner.Get(Created.Location!)));
            Found.Add(("a file that can't be edited", await Owner.Get("/searches/Broken.yaml")));
            WebPage Bravo = await Owner.Get("/searches/Bravo.yaml");
            File.WriteAllText(Path.Join(Host.Folder, "Bravo.yaml"), SearchesPageTests.Search("Changed on disk", AutoRun: false));
            Found.Add(("a conflict", await Owner.Post("/searches/Bravo.yaml", SearchForm.Fields(Bravo, "save", ("f.totalHumans", "3")).ToArray())));
            Found.Add(("an out-of-date page", await Owner.Post("/searches/Bravo.yaml", SearchForm.Fields(Bravo, "save").ToArray())));
            WebPage Weekend = await Owner.Post("/searches/Weekend.yaml", SearchForm.Fields(Found.Single(P => P.Item1 == "a new file").Item2, "check", ("f.totalHumans", "5")).ToArray());
            File.Delete(Path.Join(Host.Folder, "Weekend.yaml"));
            Found.Add(("a file deleted under the edits", await Owner.Post("/searches/Weekend.yaml", SearchForm.Fields(Weekend, "check").ToArray())));
            return Found;
        }

        [Test]
        public async Task EverySearchesPage_IsWellFormed_Labelled_KeyboardUsable_AndHasNothingInlineOrFromAnotherSite()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(13));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
                XElement Html = Page.Xml().Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                string Title = Html.Descendants("title").Single().Value;
                Assert.That(Title, Does.Contain("Searches").And.EndWith(" – Campertron"), What);
                Assert.That(Html.Descendants("main").Single().Descendants("h1").Count(), Is.EqualTo(1), What);
                int Level = 1;
                foreach (XElement Head in Elements.Where(E => E.Name.LocalName.Length == 2 && E.Name.LocalName[0] == 'h' && char.IsDigit(E.Name.LocalName[1])))
                {
                    int This = Head.Name.LocalName[1] - '0';
                    Assert.That(This, Is.LessThanOrEqualTo(Level + 1), What + ": heading " + Head.Value);
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
                    if (E.Name == "a" && Attr(E, "href") is string Href && Href.StartsWith('#'))
                    {
                        Assert.That(Ids, Does.Contain(Href.Substring(1)), What + ": the link " + E.Value);
                    }
                }

                //every field has one visible label; a radio button is in a fieldset with a legend
                foreach (XElement Input in Elements.Where(E => E.Name == "input" && Attr(E, "type") != "hidden"))
                {
                    string Id = Attr(Input, "id")!;
                    Assert.That(Elements.Count(E => E.Name == "label" && Attr(E, "for") == Id && E.Value.Trim().Length > 0), Is.EqualTo(1), What + ": the label of " + Id);
                    Assert.That(Attr(Input, "name"), Is.Not.Null.And.Not.Empty, What);
                    if (Attr(Input, "type") == "radio")
                    {
                        XElement? Set = Input.Ancestors("fieldset").FirstOrDefault();
                        Assert.That(Set?.Element("legend")?.Value.Trim(), Is.Not.Null.And.Not.Empty, What + ": the group of " + Id);
                    }
                    if (Attr(Input, "aria-invalid") == "true")
                    {
                        string Message = Attr(Input, "aria-describedby")!.Split(' ').First(Part => Part.EndsWith("-msg", StringComparison.Ordinal) || Part.EndsWith("-message", StringComparison.Ordinal));
                        Assert.That(Elements.Single(E => Attr(E, "id") == Message).Value, Does.StartWith("Error: "), What + ": the field's error is tied to it and says that it is one");
                    }
                }
                //every button is a submit button that says what it does and changes nothing but the form, a file's save or a
                //new file; the one form posts to this web UI with its token and the revision it showed; Enter presses Check
                //changes, a button the keyboard does not stop at
                foreach (XElement Button in Elements.Where(E => E.Name == "button"))
                {
                    Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                    Assert.That(Attr(Button, "type"), Is.EqualTo("submit"), What);
                    string Value = Attr(Button, "value") ?? string.Empty;
                    Assert.That(Value, Does.Not.Contain("run").And.Not.Contain("start").And.Not.Contain("stop").And.Not.Contain("delete").And.Not.Contain("monitor"), What + ": " + Value);
                    if (Attr(Button, "formaction") is string Target)
                    {
                        Assert.That(Target, Is.EqualTo("/searches?handler=create").Or.Match("^/searches/[^?]+\\?handler=saveas$"), What);
                    }
                }
                XElement Form = SearchForm.Form(Page);
                Assert.That(Attr(Form, "method"), Is.EqualTo("post"), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "revision" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                XElement Default = Form.Descendants("button").First();
                Assert.That((Attr(Default, "value"), Attr(Default, "tabindex"), Attr(Default, "aria-hidden")), Is.EqualTo(("check", "-1", "true")), What + ": Enter applies and checks, nothing more");

                //the problems at the top, in the title too; messages say their severity in words
                XElement? Summary = Elements.FirstOrDefault(E => Attr(E, "id") == "error-summary");
                Assert.That(Title.StartsWith("Error: ", StringComparison.Ordinal), Is.EqualTo(Summary != null), What);
                if (Summary != null)
                {
                    Assert.That(Summary.Descendants("a").Count(), Is.GreaterThan(0), What);
                    Assert.That(Attr(Summary, "role"), Is.EqualTo("alert").Or.EqualTo("region"), What + ": a named summary has a role (an alert after a POST)");
                }
                //a list that scrolls holds a box the keyboard reaches (a list of disabled boxes is shown whole)
                foreach (XElement Region in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ') is string[] C && (C.Contains("bounded") || C.Contains("long"))))
                {
                    Assert.That(Region.Descendants("input").Any(I => I.Attribute("disabled") == null), Is.True, What + ": the scrolling region " + Attr(Region, "id"));
                }
                foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ') is string[] C && (C.Contains("message") || C.Contains("field-message")) && Attr(E, "id") != "error-summary"))
                {
                    string Text = string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    if (Message.Element("h3") != null)
                    {
                        Text = string.Join(" ", Message.Elements().Where(E => E.Name != "h3" && E.Name != "div").SelectMany(E => E.DescendantNodes().OfType<XText>()).Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    }
                    Assert.That(Text, Does.Match("^(Error|Warning|Info): "), What + ": " + Text);
                }

                //one thing at most takes the focus when the page opens; the keyboard's order is the page's
                Assert.That(Elements.Count(E => Attr(E, "autofocus") != null), Is.LessThanOrEqualTo(1), What);
                Assert.That(Elements.Where(E => Attr(E, "tabindex") is string Tab && Tab != "-1"), Is.Empty, What);
                Assert.That(Html.Descendants("body").Single().Elements().First().Attribute("href")?.Value, Is.EqualTo("#main"), What + ": a way past the header");
                foreach (XElement Nav in Html.Descendants("nav"))
                {
                    Assert.That(Attr(Nav, "aria-label"), Is.Not.Null.And.Not.Empty, What);
                    Assert.That(Nav.Descendants("a").Count(A => Attr(A, "aria-current") == "page"), Is.LessThanOrEqualTo(1), What);
                }

                //nothing inline and nothing from another site
                Assert.That(Elements.Where(E => E.Name == "script" || E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
                foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
                {
                    string Name = Attribute.Name.LocalName;
                    Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
                    if (Name is "href" or "src" or "action" or "formaction")
                    {
                        Assert.That(Attribute.Value, Does.StartWith("/").Or.StartWith("#"), What + ": " + Name);
                        Assert.That(Attribute.Value, Does.Not.StartWith("//"), What + ": " + Name);
                    }
                }
            }
            //the states the list says, as the pages show them
            Assert.That(All.Single(P => P.What == "the list without files").Page.Text(), Does.Contain(SearchesWording.NoFiles));
            Assert.That(All.Single(P => P.What == "a value refused").Page.Xml().Descendants().Count(E => Attr(E, "aria-invalid") == "true"), Is.EqualTo(2));
            Assert.That(All.Single(P => P.What == "an ID refused").Page.Xml().Descendants().Single(E => Attr(E, "id") == WebSettingsForm.CampgroundTypedId).Attribute("aria-invalid")?.Value, Is.EqualTo("true"));
            Assert.That(All.Single(P => P.What == "a file").Page.Text(), Does.Contain("Other values in includeEquipment"));
            Assert.That(All.Single(P => P.What == "a conflict").Page.Text(), Does.Contain("Changed by another program"));
            Assert.That(All.Single(P => P.What == "an out-of-date page").Page.Text(), Does.Contain("This page was out of date"));
            Assert.That(All.Single(P => P.What == "a file deleted under the edits").Page.Text(), Does.Contain("Create the file again with my changes"));
            Assert.That(All.Single(P => P.What == "a file that can't be edited").Page.Xml().Descendants("input").Where(I => Attr(I, "type") != "hidden" && Attr(I, "name") != "__RequestVerificationToken")
                .All(I => I.Attribute("disabled") != null), Is.True, "a file that can't be edited takes no entry");
            Assert.That(All.Single(P => P.What == "a file that can't be edited").Page.Xml().Descendants().Single(E => Attr(E, "id") == "error-summary").Attribute("role")?.Value, Is.EqualTo("region"),
                "a page asked for lists its problems in a region, not as an alert");
            Assert.That(All.Single(P => P.What == "a conflict").Page.Xml().Descendants().Count(E => (Attr(E, "class") ?? "").Split(' ').Contains("long") || (Attr(E, "class") ?? "").Split(' ').Contains("bounded")), Is.Zero,
                "a form that takes no entry while its conflict is open bounds no list");
            Assert.That(All.Single(P => P.What == "a new file").Page.Xml().Descendants().Single(E => Attr(E, "id") == "created").Attribute("role")?.Value, Is.EqualTo("status"));
        }
    }
}
