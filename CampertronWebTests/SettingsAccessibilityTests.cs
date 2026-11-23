using System.Net;
using System.Xml.Linq;

namespace CampertronWebTests
{
    // The settings pages (v0.3.0's PR 5) as markup, in the states a user meets: each file as it opens, a new file, a missing
    // file, a value refused, Web.yaml's confirmation, a test's result, a conflict and an out-of-date page. Each is
    // well-formed, named, labelled, usable with the keyboard alone and without a script; its errors are listed at the top
    // and tied to their fields; a message says its severity in words; the SMTP password's box is never filled in; nothing
    // is inline or from another site. Read as XML, which the pages are written to be (no HTML parser, D92).
    public class SettingsAccessibilityTests
    {
        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);

        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            Host.Settings.ListensOnNetwork = null;
            File.WriteAllText(Path.Join(Host.Folder, "General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Host.Folder, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: u\nsmtpPassword: A11Y-cnry\nsendToAddressList:\n- a@example.invalid\n- b@example.invalid\nsendFromAddress: f@example.invalid\n");
            using Browser Owner = await Host.SignedIn();
            foreach (string File_ in new[] { "general", "email", "ntfy", "web" })
            {
                Found.Add(("the " + File_ + " page", await Owner.Get("/settings/" + File_)));
            }
            WebPage Email = await Owner.Get("/settings/email");
            Found.Add(("a value refused", await Owner.Post("/settings/email", SettingsForm.Fields(Email, "save", ("f.smtpPort", "x"), ("f.sendToAddressList.1", "not an address")).ToArray())));
            WebPage Fresh = await Owner.Post("/settings/email", SettingsForm.Fields(Found.Last().Item2, "revert").ToArray());
            Found.Add(("a row added", await Owner.Post("/settings/email", SettingsForm.Fields(Fresh, "row-add.sendToAddressList").ToArray())));
            Found.Add(("a test's result", await Owner.Post("/settings/email?handler=test", SettingsForm.Fields(await Owner.Get("/settings/email"), null, ("f.smtpServer", "changed.example.invalid")).ToArray())));
            WebPage Web = await Owner.Get("/settings/web");
            Found.Add(("Web.yaml's confirmation", await Owner.Post("/settings/web", SettingsForm.Fields(Web, "save").Where(F => F.Item1 != "f.passwordProtection").ToArray())));
            WebPage General = await Owner.Get("/settings/general");
            File.WriteAllText(Path.Join(Host.Folder, "General.yaml"), "outputTo: HtmlFile\nrefreshRidbDataDayInterval: 30\nautoRefresh: false\n");
            Found.Add(("a conflict", await Owner.Post("/settings/general", SettingsForm.Fields(General, "save", ("f.refreshRidbDataDayInterval", "12")).ToArray())));
            Found.Add(("an out-of-date page", await Owner.Post("/settings/general", SettingsForm.Fields(General, "save").ToArray())));
            WebPage Tested = Found.Single(P => P.Item1 == "a test's result").Item2;
            await Owner.Post("/settings/email", SettingsForm.Fields(Tested, "revert").ToArray());
            File.Delete(Path.Join(Host.Folder, "Email.yaml"));
            Found.Add(("a missing file", await Owner.Get("/settings/email")));
            return Found;
        }

        [Test]
        public async Task EverySettingsPage_IsWellFormed_Labelled_KeyboardUsable_AndHasNothingInlineOrFromAnotherSite()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(11));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), What);
                Assert.That(Page.Body, Does.Not.Contain("A11Y-cnry"), What + ": the SMTP password is never in a page");
                XElement Html = Page.Xml().Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                string Title = Html.Descendants("title").Single().Value;
                Assert.That(Title, Does.Contain("Settings: ").And.EndWith(" – Campertron"), What);
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

                //every field has one visible label; a radio button is in a fieldset with a legend; the password's box is empty
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
                    if (Attr(Input, "type") == "password")
                    {
                        Assert.That(Attr(Input, "value"), Is.Null, What + ": " + Id + " is never filled in");
                        Assert.That(Attr(Input, "autocomplete"), Is.EqualTo("new-password"), What + ": no browser fills in its own saved password");
                    }
                    if (Attr(Input, "aria-invalid") == "true")
                    {
                        string Message = Attr(Input, "aria-describedby")!.Split(' ').First(Part => Part.EndsWith("-msg", StringComparison.Ordinal));
                        Assert.That(Elements.Single(E => Attr(E, "id") == Message).Value, Does.StartWith("Error: "), What + ": the field's error is tied to it and says that it is one");
                    }
                }
                //every button is a submit button that says what it does; the one form posts to this web UI with its token and
                //the revision it showed; Enter presses Check changes, a button the keyboard does not stop at
                foreach (XElement Button in Elements.Where(E => E.Name == "button"))
                {
                    Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                    Assert.That(Attr(Button, "type"), Is.EqualTo("submit"), What);
                    if (Attr(Button, "formaction") is string Target)
                    {
                        Assert.That(Target, Does.StartWith("/settings/").And.EndWith("?handler=test"), What);
                    }
                }
                XElement Form = Elements.Single(E => E.Name == "form" && (Attr(E, "action") ?? "").StartsWith("/settings/", StringComparison.Ordinal));
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
                }
                foreach (XElement Message in Elements.Where(E => (Attr(E, "class") ?? "").Split(' ') is string[] C && (C.Contains("message") || C.Contains("field-message")) && Attr(E, "id") != "error-summary"))
                {
                    string Text = string.Join(" ", Message.DescendantNodes().OfType<XText>().Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    if (Message.Element("h3") != null)
                    {
                        Text = string.Join(" ", Message.Elements().Where(E => E.Name != "h3" && E.Name != "div").SelectMany(E => E.DescendantNodes().OfType<XText>()).Select(T => T.Value.Trim()).Where(T => T.Length > 0));
                    }
                    Assert.That(Text, Does.Match("^(Error|Warning|Info): |^[A-Za-z]+\\.yaml: (Error|Warning|Info): "), What + ": " + Text);
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
            Assert.That(All.Single(P => P.What == "a value refused").Page.Xml().Descendants().Count(E => Attr(E, "aria-invalid") == "true"), Is.EqualTo(2));
            Assert.That(All.Single(P => P.What == "Web.yaml's confirmation").Page.Xml().Descendants().Single(E => Attr(E, "id") == "confirmation").Attribute("role")?.Value, Is.EqualTo("alertdialog"));
            Assert.That(All.Single(P => P.What == "a conflict").Page.Text(), Does.Contain("Changed by another program"));
            Assert.That(All.Single(P => P.What == "an out-of-date page").Page.Text(), Does.Contain("This page was out of date"));
            Assert.That(All.Single(P => P.What == "a missing file").Page.Text(), Does.Contain("Create Email.yaml"));
            Assert.That(All.Single(P => P.What == "a test's result").Page.Xml().Descendants().Single(E => Attr(E, "id") == "test-result").Value, Does.StartWith("Warning: The test uses Email.yaml as it is saved"));
        }
    }
}
