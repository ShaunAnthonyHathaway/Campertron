using System.Net;
using System.Xml.Linq;

namespace CampertronWebTests
{
    // Every page of the web UI, as markup (D80, D92): well-formed, named, labelled, usable with the keyboard alone and
    // without a script, with its errors tied to their fields, and with nothing from another site or inline. The pages
    // are read as XML, which they are written to be.
    public class AccessibilityTests
    {
        private static async Task<List<(string What, WebPage Page)>> Pages()
        {
            var Found = new List<(string, WebPage)>();
            await using (WebTestHost Fresh = await WebTestHost.Start())
            {
                using Browser Visitor = Fresh.Browser();
                Fresh.NewSetupCode();
                Found.Add(("the setup", await Visitor.Get("/setup")));
                Found.Add(("the setup with a wrong code", await Visitor.Post("/setup", ("Code", "AAAA-AAAA-AAAA"), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))));
                Found.Add(("the setup with a notice", await Visitor.Post("/setup?handler=NewCode")));
                string Code = Fresh.NewSetupCode();
                Found.Add(("the setup with a short password", await Visitor.Post("/setup", ("Code", Code), ("Password", "short"), ("Repeat", "short"))));
                Found.Add(("the setup with two passwords", await Visitor.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", "another password"))));
            }
            await using (WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.MapFailingPage = true))
            {
                using Browser Visitor = Host.Browser();
                Found.Add(("the sign-in", await Visitor.Get("/signin")));
                Found.Add(("the sign-in with a notice", await Visitor.Get("/signin?notice=signed-out")));
                Found.Add(("the sign-in with a wrong password", await Visitor.Post("/signin", ("Password", "wrong password"))));
                Found.Add(("a refused form", await Visitor.Post("/signin", ("Password", "x"), ("__RequestVerificationToken", "bad"))));
                Visitor.Origin = "http://evil.example";
                Found.Add(("a refused origin", await Visitor.Post("/signin", ("Password", "x"))));
                using Browser Owner = await Host.SignedIn();
                Found.Add(("the start page", await Owner.StartPage()));
                Found.Add(("page not found", await Owner.Get("/nothing-here")));
                Found.Add(("the password page", await Owner.Get("/account/password")));
                Found.Add(("the password page with a wrong password", await Owner.Post("/account/password", ("Current", "wrong"), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))));
                Found.Add(("the password page with a short password", await Owner.Post("/account/password", ("Current", WebTestHost.Password), ("Password", "short"), ("Repeat", "short"))));
                Found.Add(("the error page", await Owner.Get("/test/fail")));
                Owner.Headers["Host"] = "evil.example";
                Found.Add(("a refused name", await Owner.Get("/")));
            }
            await using (WebTestHost Open = await WebTestHost.Start("passwordProtection: false\n"))
            {
                using Browser Visitor = Open.Browser();
                Found.Add(("the start page without a password", await Visitor.StartPage()));
            }
            return Found;
        }

        private static string? Attr(XElement Element, string Name) => (string?)Element.Attribute(Name);

        [Test]
        public async Task EveryPage_IsWellFormed_Named_Labelled_AndHasNothingInlineOrFromAnotherSite()
        {
            List<(string What, WebPage Page)> All = await Pages();
            Assert.That(All, Has.Count.EqualTo(18));
            foreach ((string What, WebPage Page) in All)
            {
                Assert.That(Page.Header("Content-Type"), Is.EqualTo("text/html; charset=utf-8"), What);
                Assert.That(Page.Body, Does.StartWith("<!DOCTYPE html>"), What);
                XDocument Xml = Page.Xml();
                XElement Html = Xml.Root!;
                List<XElement> Elements = Html.DescendantsAndSelf().ToList();

                //the document: its language, its name, one main part, one heading of the first level, a viewport
                Assert.That(Attr(Html, "lang"), Is.EqualTo("en"), What);
                string Title = Html.Descendants("title").Single().Value;
                Assert.That(Title, Does.EndWith(" – Campertron").And.Length.GreaterThan(" – Campertron".Length), What);
                XElement Main = Html.Descendants("main").Single();
                Assert.That(Attr(Main, "id"), Is.EqualTo("main"), What);
                XElement Heading = Html.Descendants("h1").Single();
                Assert.That(Heading.Value.Trim(), Is.Not.Empty, What);
                Assert.That(Main.Descendants("h1").Count(), Is.EqualTo(1), What + ": the heading is in the main part");
                Assert.That(Html.Descendants("meta").Any(Meta => Attr(Meta, "name") == "viewport" && Attr(Meta, "content") == "width=device-width, initial-scale=1"), Is.True, What + ": zoom is never turned off");
                //heading levels go down one step at a time
                int Level = 1;
                foreach (XElement Head in Elements.Where(E => E.Name.LocalName.Length == 2 && E.Name.LocalName[0] == 'h' && char.IsDigit(E.Name.LocalName[1])))
                {
                    int This = Head.Name.LocalName[1] - '0';
                    Assert.That(This, Is.LessThanOrEqualTo(Level + 1), What + ": heading " + Head.Value);
                    Level = This;
                }

                //ids are unique, and everything that points to one finds it
                List<string> Ids = Elements.Select(E => Attr(E, "id")).Where(Id => Id != null).Select(Id => Id!).ToList();
                Assert.That(Ids, Is.Unique, What);
                foreach (XElement E in Elements)
                {
                    foreach (string Pointer in new[] { "aria-describedby", "aria-labelledby", "for" })
                    {
                        foreach (string Id in (Attr(E, Pointer) ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            Assert.That(Ids, Does.Contain(Id), What + ": " + Pointer + " of " + E.Name);
                        }
                    }
                    if (E.Name == "a" && Attr(E, "href") is string Href && Href.StartsWith('#'))
                    {
                        Assert.That(Ids, Does.Contain(Href.Substring(1)), What + ": the link " + E.Value);
                    }
                }

                //every field has a visible label of its own, a name, and says what it is for; a password never has a value
                foreach (XElement Input in Elements.Where(E => E.Name == "input" && Attr(E, "type") != "hidden"))
                {
                    string Id = Attr(Input, "id")!;
                    Assert.That(Elements.Count(E => E.Name == "label" && Attr(E, "for") == Id && E.Value.Trim().Length > 0), Is.EqualTo(1), What + ": the label of " + Id);
                    Assert.That(Attr(Input, "name"), Is.Not.Null.And.Not.Empty, What);
                    Assert.That(Attr(Input, "autocomplete"), Is.AnyOf("current-password", "new-password", "one-time-code"), What + ": " + Id);
                    Assert.That(Attr(Input, "value"), Is.Null, What + ": " + Id + " is never filled in by the page");
                    if (Attr(Input, "aria-invalid") == "true")
                    {
                        string Error = Attr(Input, "aria-describedby")!.Split(' ').Single(Part => Part.EndsWith("-error", StringComparison.Ordinal));
                        Assert.That(Elements.Single(E => Attr(E, "id") == Error).Value, Does.StartWith("Error: "), What + ": the field's error is tied to it and says that it is one");
                    }
                }
                //every button says what it does; every form posts, with its antiforgery token, to this web UI
                foreach (XElement Button in Elements.Where(E => E.Name == "button"))
                {
                    Assert.That(Button.Value.Trim(), Is.Not.Empty, What);
                    Assert.That(Attr(Button, "type"), Is.EqualTo("submit"), What);
                }
                foreach (XElement Form in Elements.Where(E => E.Name == "form"))
                {
                    Assert.That(Attr(Form, "method"), Is.EqualTo("post"), What);
                    Assert.That(Attr(Form, "action"), Is.Not.Null, What + ": a form names its address (without one it posts to the page's current address and query)");
                    Assert.That(Attr(Form, "action"), Does.StartWith("/").And.Not.StartWith("//"), What);
                    Assert.That(Form.Descendants("input").Count(I => Attr(I, "name") == "__RequestVerificationToken" && Attr(I, "type") == "hidden"), Is.EqualTo(1), What);
                    Assert.That(Form.Descendants("button").Count(), Is.EqualTo(1), What);
                }

                //an error is said at the top, as an alert that takes the focus and leads to its field, and in the title
                List<XElement> Alerts = Elements.Where(E => Attr(E, "role") == "alert").ToList();
                Assert.That(Title.StartsWith("Error: ", StringComparison.Ordinal), Is.EqualTo(Alerts.Count > 0), What);
                foreach (XElement Alert in Alerts)
                {
                    Assert.That(Attr(Alert, "tabindex"), Is.EqualTo("-1"), What);
                    Assert.That(Attr(Alert, "autofocus"), Is.EqualTo("autofocus"), What);
                    Assert.That(Alert.Descendants("a").Count(), Is.GreaterThan(0), What);
                    Assert.That(Elements.Where(E => Attr(E, "aria-invalid") == "true"), Is.Not.Empty, What + ": the field in error is marked");
                }
                //one thing has the focus when the page opens: the error, or the first field; nothing else moves it
                int Fields = Elements.Count(E => E.Name == "input" && Attr(E, "type") != "hidden");
                Assert.That(Elements.Count(E => Attr(E, "autofocus") != null), Is.EqualTo(Fields > 0 ? 1 : 0), What);
                Assert.That(Elements.Where(E => Attr(E, "tabindex") is string Tab && Tab != "-1"), Is.Empty, What + ": the keyboard's order is the page's order");
                Assert.That(Elements.Where(E => Attr(E, "accesskey") != null), Is.Empty, What);

                //a way past the header, to the main part
                if (Html.Descendants("nav").Any() || Fields > 0)
                {
                    XElement Skip = Html.Descendants("body").Single().Elements().First();
                    Assert.That(Skip.Name.LocalName, Is.EqualTo("a"), What);
                    Assert.That(Attr(Skip, "href"), Is.EqualTo("#main"), What);
                }
                foreach (XElement Nav in Html.Descendants("nav"))
                {
                    Assert.That(Attr(Nav, "aria-label"), Is.Not.Null.And.Not.Empty, What);
                    Assert.That(Nav.Descendants("a").Count(A => Attr(A, "aria-current") == "page"), Is.LessThanOrEqualTo(1), What);
                }

                //nothing inline and nothing from another site: the Content-Security-Policy would refuse it, and a page
                //without scripts needs none
                Assert.That(Elements.Where(E => E.Name == "script" || E.Name == "style" || E.Name == "iframe" || E.Name == "object" || E.Name == "embed" || E.Name == "img"), Is.Empty, What);
                foreach (XAttribute Attribute in Elements.SelectMany(E => E.Attributes()))
                {
                    string Name = Attribute.Name.LocalName;
                    Assert.That(Name == "style" || Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), Is.False, What + ": the attribute " + Name);
                    if (Name is "href" or "src" or "action")
                    {
                        Assert.That(Attribute.Value, Does.StartWith("/").Or.StartWith("#"), What + ": " + Name);
                        Assert.That(Attribute.Value, Does.Not.StartWith("//"), What + ": " + Name);
                    }
                }
                Assert.That(Html.Descendants("link").Select(Link => Attr(Link, "rel")), Is.EqualTo(new[] { "stylesheet" }), What + ": one stylesheet, the web UI's own");
            }
        }

        [Test]
        public async Task TheStylesheet_KeepsTheFocusVisible_AndFollowsTheBrowsersColorScheme()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            string Css = (await Visitor.Get("/assets/site.css")).Body;
            Assert.That(Css, Does.Contain(":focus-visible { outline: 3px solid var(--focus)"));
            Assert.That(Css, Does.Not.Contain("outline: none").And.Not.Contain("outline:none").And.Not.Contain("outline: 0"));
            Assert.That(Css, Does.Contain("color-scheme: light dark").And.Contain("@media (prefers-color-scheme: dark)"));
            Assert.That(Css, Does.Not.Contain("user-scalable").And.Not.Contain("!important"));

            //the colors of text on its background: at least 7:1 in both schemes (WCAG's formula)
            static double Luminance(string Hex)
            {
                double Channel(int At)
                {
                    double C = System.Convert.ToInt32(Hex.Substring(At, 2), 16) / 255.0;
                    return C <= 0.03928 ? C / 12.92 : Math.Pow((C + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
            }
            static double Contrast(string A, string B)
            {
                double La = Luminance(A), Lb = Luminance(B);
                return (Math.Max(La, Lb) + 0.05) / (Math.Min(La, Lb) + 0.05);
            }
            string[] Schemes = Css.Split("@media (prefers-color-scheme: dark)");
            foreach (string Scheme in new[] { Schemes[0], Schemes[1].Substring(0, Schemes[1].IndexOf("}\n}", StringComparison.Ordinal)) })
            {
                Dictionary<string, string> Colors = System.Text.RegularExpressions.Regex.Matches(Scheme, @"--([a-z-]+): (#[0-9a-f]{6});")
                    .ToDictionary(Match => Match.Groups[1].Value, Match => Match.Groups[2].Value);
                //v0.3.0's PR 5: the settings pages' warning and information colors (D74's amber and blue) join the error's
                Assert.That(Colors, Has.Count.EqualTo(17));
                foreach ((string Text, string Back) in new[] { ("text", "page"), ("text", "surface"), ("muted", "page"), ("link", "page"), ("link", "surface"), ("button-text", "button"),
                    ("error", "error-surface"), ("error", "page"), ("notice", "notice-surface"), ("warning", "warning-surface"), ("warning", "page"), ("info", "info-surface"),
                    ("info", "page"), ("muted", "surface") })
                {
                    Assert.That(Contrast(Colors[Text], Colors[Back]), Is.GreaterThanOrEqualTo(7.0), Text + " on " + Back + " (" + Colors[Text] + " on " + Colors[Back] + ")");
                }
                foreach (string Edge in new[] { "edge", "focus" })
                {
                    Assert.That(Contrast(Colors[Edge], Colors["page"]), Is.GreaterThanOrEqualTo(4.5), Edge + " on page");
                }
            }
        }
    }
}
