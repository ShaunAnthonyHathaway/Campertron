using System.Net;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using static CampertronWebTests.SystemPageTests;

namespace CampertronWebTests
{
    // The web UI's theme (D110; business-rules-settings §40; ER-44) as the web UI answers: System, Light and Dark on the
    // system page, System without a choice; this browser's choice in its own theme cookie, written into every page (the
    // layout's data-theme and color-scheme, the fixed pages' too) from the first byte; Apply theme a protected POST
    // (sign-in, antiforgery, Host, Origin, its own rate limit) that sets only that cookie, changes no file, session,
    // refresh or monitoring, and keeps the refresh's confirmation open; each browser its own choice, kept through a sign-out,
    // a password change and a restart of web mode; the stylesheet's three themes. The browser's side (what a page is drawn
    // in) is ThemeBrowserTests'.
    public class ThemeTests
    {
        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        // every page of a signed-in browser, the live system page's view and a page that is not there
        internal static readonly string[] Pages =
        {
            "/searches", "/results", "/monitoring", "/settings/general", "/settings/email", "/settings/ntfy", "/settings/web", "/settings/system",
            "/settings/system?confirm=refresh", "/settings/system?handler=view", "/campgrounds/park", "/campgrounds/city", "/campgrounds/state", "/guided-setup",
            "/account/password"
        };

        private static string ThemeCookie(WebTestHost Host) => Host.Service<WebTheme>().CookieName;

        private static void Choose(Browser Visitor, WebTestHost Host, string Value) => Visitor.Cookies.Add(Visitor.Root, new Cookie(ThemeCookie(Host), Value, "/"));

        // what a page asks the browser for: its data-theme and its color-scheme
        internal static (string? Theme, string? Scheme) Asked(WebPage Page)
        {
            XElement Html = Page.Xml().Root!;
            return ((string?)Html.Attribute("data-theme"), (string?)Html.Descendants("meta").SingleOrDefault(M => (string?)M.Attribute("name") == "color-scheme")?.Attribute("content"));
        }

        private static (string?, string) Expected(string? Value) => (Value ?? "system", Value ?? "light dark");

        // the page, and the pages a redirect inside the web UI leads to (the guided setup's first step)
        private static async Task<WebPage> Open(Browser Visitor, string Address)
        {
            WebPage Page = await Visitor.Get(Address);
            for (int i = 0; i < 3 && Page.Status == HttpStatusCode.Redirect && Page.Location!.StartsWith('/'); i++)
            {
                Page = await Visitor.Get(Page.Location);
            }
            return Page;
        }

        private static XElement ThemeForm(WebPage Page) => Page.Xml().Descendants("form").Single(F => (string?)F.Attribute("action") == "/settings/system?handler=theme");

        private static string? Checked(WebPage Page) => (string?)ThemeForm(Page).Descendants("input").SingleOrDefault(I => (string?)I.Attribute("checked") == "checked")?.Attribute("value");

        // the theme cookie a response sets, as its attributes (lower case, the value first), or null
        private static List<string>? SetTheme(WebPage Page, WebTestHost Host) =>
            Page.SetCookies.Where(One => One.StartsWith(ThemeCookie(Host) + "=", StringComparison.Ordinal))
                .Select(One => One.Split("; ").Select(Part => Part.ToLowerInvariant()).ToList()).SingleOrDefault();

        private static async Task<WebPage> Apply(Browser Owner, string Value, string? Confirm = null)
        {
            await Owner.Get(Address);
            var Fields = new List<(string, string)> { ("theme", Value) };
            if (Confirm != null)
            {
                Fields.Add(("confirm", Confirm));
            }
            return await Owner.Post(Address + "?handler=theme", Fields.ToArray());
        }

        // ---- System without a choice; the three choices ----

        [Test]
        public async Task WithoutAChoice_EveryPageIsSystem_AndTheSystemPageOffersTheThreeChoicesWithSystemChecked()
        {
            await using SystemHost S = await SystemHost.Start(More: Settings => Settings.MapFailingPage = true);
            using Browser Visitor = S.Host.Browser();
            Assert.That(Asked(await Visitor.Get("/signin")), Is.EqualTo(Expected(null)), "the sign-in");
            using Browser Owner = await S.Host.SignedIn();
            Assert.That(Asked(await Owner.StartPage()), Is.EqualTo(Expected(null)), "the start page");
            foreach (string Address_ in Pages)
            {
                WebPage Page = await Open(Owner, Address_);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), Address_);
                Assert.That(Asked(Page), Is.EqualTo(Expected(null)), Address_ + ": System, the browser's light or dark");
            }
            Assert.That(Asked(await Owner.Get("/nothing-here")), Is.EqualTo(Expected(null)), "a fixed page");

            WebPage System_ = await Owner.Get(Address);
            XElement Section = System_.Xml().Descendants("section").Single(E => (string?)E.Attribute("id") == "theme");
            Assert.That(Section.Element("h2")!.Value, Is.EqualTo(ThemeWording.Heading));
            Assert.That((string?)Section.Attribute("aria-labelledby"), Is.EqualTo("theme-heading"));
            XElement Form = ThemeForm(System_);
            Assert.That((string?)Form.Attribute("method"), Is.EqualTo("post"));
            XElement Group = Form.Element("fieldset")!;
            Assert.That(((string?)Group.Attribute("aria-labelledby"), (string?)Group.Attribute("aria-describedby")), Is.EqualTo(("theme-heading", "theme-intro")), "the group's name and description");
            //exactly the three choices, named as the desktop GUI names them, each with what it does
            var Choices = Group.Descendants("input").Select(I => (Id: (string)I.Attribute("id")!, Type: (string?)I.Attribute("type"), Name: (string?)I.Attribute("name"), Value: (string?)I.Attribute("value"),
                Label: Group.Descendants("label").Single(L => (string?)L.Attribute("for") == (string)I.Attribute("id")!).Value,
                Help: Group.Descendants("p").Single(P => (string?)P.Attribute("id") == (string?)I.Attribute("aria-describedby")).Value)).ToList();
            Assert.That(Choices.Select(C => (C.Type, C.Name, C.Value, C.Label, C.Help)), Is.EqualTo(new[]
            {
                ((string?)"radio", (string?)"theme", (string?)"system", ThemeWording.SystemLabel, WebSystemWording.ThemeSystemHelp),
                ("radio", "theme", "light", ThemeWording.LightLabel, ThemeWording.LightHelp),
                ("radio", "theme", "dark", ThemeWording.DarkLabel, ThemeWording.DarkHelp)
            }));
            Assert.That(Checked(System_), Is.EqualTo("system"), "System is the default");
            Assert.That(Form.Descendants("button").Single().Value, Is.EqualTo(WebSystemWording.ThemeApply));
            Assert.That(System_.Text(), Does.Contain(WebSystemWording.ThemeIntro).And.Contain(WebSystemWording.ThemeStorage));
            //the line that says which theme the page is shown in: for System, both names, of which the stylesheet shows one
            XElement Shown = System_.Xml().Descendants("p").Single(P => (string?)P.Attribute("id") == "theme-shown");
            Assert.That(Shown.Elements("span").Select(E => ((string?)E.Attribute("class"), E.Value)), Is.EqualTo(new[] { ((string?)"shown-light", "Light"), ("shown-dark", "Dark") }));
            Assert.That(Shown.Value, Is.EqualTo("Campertron is shown in its LightDark theme, as this browser asks."));
            Assert.That((string?)Shown.Attribute("autofocus"), Is.Null, "the focus only after a choice");
            //the theme comes first on the page, outside the parts the live script puts in place
            List<string?> Order = System_.Xml().Descendants("main").Single().Elements().Select(E => (string?)E.Attribute("id")).ToList();
            Assert.That(Order.IndexOf("theme"), Is.GreaterThanOrEqualTo(0).And.LessThan(Order.IndexOf("live-view")));
            Assert.That(System_.Xml().Descendants("div").Single(D => (string?)D.Attribute("id") == "live-view").Descendants("section").Any(E => (string?)E.Attribute("id") == "theme"), Is.False);
        }

        // ---- Light and Dark on every page ----

        [TestCase("light")]
        [TestCase("dark")]
        public async Task AChoice_IsWrittenIntoEveryPage_TheSignInTheSetupAndTheFixedPagesToo(string Value)
        {
            await using (SystemHost Fresh = await SystemHost.Start(WithPassword: false))
            {
                using Browser Visitor = Fresh.Host.Browser();
                Choose(Visitor, Fresh.Host, Value);
                Assert.That(Asked(await Visitor.Get("/setup")), Is.EqualTo(Expected(Value)), "the setup");
            }
            await using SystemHost S = await SystemHost.Start(More: Settings => Settings.MapFailingPage = true);
            using Browser Anonymous = S.Host.Browser();
            Choose(Anonymous, S.Host, Value);
            Assert.That(Asked(await Anonymous.Get("/signin")), Is.EqualTo(Expected(Value)), "the sign-in");
            Assert.That(Asked(await Anonymous.Get("/signin?notice=signed-out")), Is.EqualTo(Expected(Value)));
            using Browser Owner = await S.Host.SignedIn();
            Choose(Owner, S.Host, Value);
            Assert.That(Asked(await Owner.StartPage()), Is.EqualTo(Expected(Value)), "the start page");
            foreach (string Address_ in Pages)
            {
                WebPage Page = await Open(Owner, Address_);
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK), Address_);
                Assert.That(Asked(Page), Is.EqualTo(Expected(Value)), Address_);
            }
            WebPage System_ = await Owner.Get(Address);
            Assert.That(Checked(System_), Is.EqualTo(Value));
            string Name = Value == "dark" ? ThemeWording.DarkLabel : ThemeWording.LightLabel;
            Assert.That(System_.Xml().Descendants("p").Single(P => (string?)P.Attribute("id") == "theme-shown").Value, Is.EqualTo("Campertron is shown in its " + Name + " theme."));
            //the fixed pages: not found, the error page, a refused name and a refused origin
            Assert.That(Asked(await Owner.Get("/nothing-here")), Is.EqualTo(Expected(Value)), "not found");
            Assert.That(Asked(await Owner.Get("/test/fail")), Is.EqualTo(Expected(Value)), "the error page");
            Owner.Origin = "http://evil.example";
            Assert.That(Asked(await Owner.Post(Address + "?handler=theme", ("theme", "system"))), Is.EqualTo(Expected(Value)), "a refused origin");
            Owner.Origin = Owner.Root.GetLeftPart(UriPartial.Authority);
            Owner.Headers["Host"] = "evil.example";
            Assert.That(Asked(await Owner.Get("/")), Is.EqualTo(Expected(Value)), "a refused name");
        }

        [Test]
        public async Task AnyOtherCookieValue_IsSystem_AndNeverShown()
        {
            await using SystemHost S = await SystemHost.Start();
            foreach (string Value in new[] { "Dark", "LIGHT", "", "system", "light%20dark", "blue", "%3Cb%3Edark%3C%2Fb%3E", "dark,light" })
            {
                string Answer = await RawRequest.Send(S.Host.Port, "GET /signin HTTP/1.1\r\nHost: 127.0.0.1:" + S.Host.Port + "\r\nCookie: " + ThemeCookie(S.Host) + "=" + Value
                    + "\r\nConnection: close\r\n\r\n");
                Assert.That(Answer, Does.StartWith("HTTP/1.1 200"), Value);
                Assert.That(Answer, Does.Contain("<html lang=\"en\" data-theme=\"system\">").And.Contain("<meta name=\"color-scheme\" content=\"light dark\" />"), Value + ": System");
                if (Value is "blue" or "%3Cb%3Edark%3C%2Fb%3E" or "light%20dark" or "dark,light")
                {
                    Assert.That(Answer, Does.Not.Contain(Value).And.Not.Contain("<b>").And.Not.Contain("dark,light").And.Not.Contain("blue"), "the cookie's text is never repeated");
                }
            }
            //another web UI's theme cookie (another settings folder's name) is not this one's
            string Foreign = await RawRequest.Send(S.Host.Port, "GET /signin HTTP/1.1\r\nHost: 127.0.0.1:" + S.Host.Port + "\r\nCookie: campertron-theme-000000000000=dark\r\nConnection: close\r\n\r\n");
            Assert.That(Foreign, Does.Contain("<html lang=\"en\" data-theme=\"system\">"));
        }

        // ---- Apply theme ----

        [Test]
        public async Task ApplyTheme_SetsOnlyThisBrowsersCookie_ThenShowsThePageInIt_WithTheFocusOnWhatItIsShownIn()
        {
            await using SystemHost S = await SystemHost.Start();
            File.WriteAllText(S.In("Alpha.yaml"), ResultsPageTests.Search("First trip"));
            using Browser Owner = await S.Host.SignedIn();
            await Owner.Get("/monitoring");
            string Before = ResultsPageTests.Snapshot(S.Folder);
            string SessionBefore = Owner.Cookie(S.Host.SessionCookie)!;

            WebPage Dark = await Apply(Owner, "dark");
            Assert.That((Dark.Status, Dark.Location), Is.EqualTo((HttpStatusCode.Redirect, Address)), "a redirect after the POST");
            Assert.That(SetTheme(Dark, S.Host), Is.EquivalentTo(new[] { ThemeCookie(S.Host) + "=dark", "max-age=31536000", "path=/", "samesite=lax", "httponly" }),
                "only the theme, for a year, for every page, not for another site's forms, no script, not Secure over HTTP");
            Assert.That(Dark.SetCookies.Count(), Is.EqualTo(1), "no other cookie: not the session's, not the forms'");
            Assert.That(Dark.Body, Is.Empty);
            Assert.That(Owner.Cookie(S.Host.SessionCookie), Is.EqualTo(SessionBefore), "the session cookie is not issued again");

            WebPage Shown = await Owner.Get(Address);
            Assert.That(Asked(Shown), Is.EqualTo(Expected("dark")));
            Assert.That(Checked(Shown), Is.EqualTo("dark"));
            XElement Line = Shown.Xml().Descendants("p").Single(P => (string?)P.Attribute("id") == "theme-shown");
            Assert.That(((string?)Line.Attribute("autofocus"), (string?)Line.Attribute("tabindex"), Line.Value), Is.EqualTo(("autofocus", "-1", "Campertron is shown in its Dark theme.")));
            Assert.That(Shown.Xml().Descendants().Count(E => E.Attribute("autofocus") != null), Is.EqualTo(1));
            Assert.That(Shown.Xml().Descendants("main").Single().Descendants().Any(E => (string?)E.Attribute("class") == "message warning"), Is.False, "no notice");
            Assert.That((await Owner.Get(Address)).Xml().Descendants().Count(E => E.Attribute("autofocus") != null), Is.Zero, "the focus once");
            foreach (string Address_ in Pages)
            {
                Assert.That(Asked(await Open(Owner, Address_)), Is.EqualTo(Expected("dark")), "across the pages: " + Address_);
            }

            Assert.That(SetTheme(await Apply(Owner, "light"), S.Host), Is.EquivalentTo(new[] { ThemeCookie(S.Host) + "=light", "max-age=31536000", "path=/", "samesite=lax", "httponly" }));
            Assert.That(Asked(await Owner.Get("/results")), Is.EqualTo(Expected("light")));
            List<string> System_ = SetTheme(await Apply(Owner, "system"), S.Host)!;
            Assert.That(System_[0], Is.EqualTo(ThemeCookie(S.Host) + "="), "System: the cookie removed");
            Assert.That(System_, Does.Contain("expires=thu, 01 jan 1970 00:00:00 gmt").And.Contain("path=/"));
            Assert.That(Owner.Cookie(ThemeCookie(S.Host)), Is.Null);
            Assert.That(Asked(await Owner.Get("/results")), Is.EqualTo(Expected(null)));

            //nothing else changed: no file in the settings folder, the monitoring as it was, no line
            Assert.That(ResultsPageTests.Snapshot(S.Folder), Is.EqualTo(Before), "no settings file, no preferences file, nothing written");
            Assert.That(S.Host.Service<WebMonitoring>().Controller.View().Status.IsRunning, Is.False);
            Assert.That(S.Host.Lines.Concat(S.Host.Warnings).Concat(S.Host.Errors), Is.Empty);
        }

        [Test]
        public async Task ThroughATrustedProxysHttps_TheCookieIsSecure()
        {
            await using WebTestHost Host = await WebTestHost.Start("allowedHosts:\n- campertron.example.com\ntrustedProxies:\n- 127.0.0.1\n", WithPassword: true);
            using Browser Owner = Host.Browser();
            Owner.Headers["X-Forwarded-Proto"] = "https";
            Owner.Headers["X-Forwarded-Host"] = "campertron.example.com";
            Owner.Headers["X-Forwarded-For"] = "203.0.113.9";
            Owner.Origin = "https://campertron.example.com";
            await Owner.Get("/signin");
            Assert.That((await Owner.Post("/signin", ("Password", WebTestHost.Password))).Location, Is.EqualTo("/"));
            await Owner.Get(Address);
            WebPage Dark = await Owner.Post(Address + "?handler=theme", ("theme", "dark"));
            Assert.That(Dark.Location, Is.EqualTo(Address));
            Assert.That(Dark.SetCookies.Single(), Does.StartWith(Host.Service<WebTheme>().CookieName + "=dark;").And.Contain("; secure").And.Contain("; samesite=lax").And.Contain("; httponly"));
        }

        [Test]
        public async Task ApplyTheme_KeepsTheRefreshsConfirmationOpen_AndChangesNoRefresh()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            XElement Form = ThemeForm(Confirm);
            Assert.That(Form.Descendants("input").Single(I => (string?)I.Attribute("name") == "confirm").Attribute("value")!.Value, Is.EqualTo(CampertronWeb.Pages.SystemModel.ConfirmValue));
            WebPage Posted = await Owner.Post(Address + "?handler=theme", ("theme", "dark"), ("confirm", CampertronWeb.Pages.SystemModel.ConfirmValue));
            Assert.That(Posted.Location, Is.EqualTo(Address + "?confirm=refresh"), "back to the open confirmation");
            WebPage Again = await Owner.Get(Posted.Location!);
            Assert.That(StartForm(Again), Is.Not.Null, "the confirmation stays open");
            Assert.That(Again.Xml().Descendants().Single(E => E.Attribute("autofocus") != null).Attribute("id")!.Value, Is.EqualTo("theme-shown"), "the focus where the user acted");
            Assert.That(S.Fake.Calls, Is.Zero, "no refresh started");
            Assert.That((await Owner.Post(Address + "?handler=theme", ("theme", "light"), ("confirm", "something else"))).Location, Is.EqualTo(Address), "only the confirmation's own value");

            //a running refresh: a theme changes nothing of it
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "downloading");
            string Key = S.Host.Service<WebRefresh>().View().Token;
            WebPage During = await Apply(Owner, "dark");
            Assert.That(During.Location, Is.EqualTo(Address));
            WebPage After = await Owner.Get(Address);
            Assert.That(CancelForm(After), Is.Not.Null, "the refresh goes on");
            Assert.That(S.Host.Service<WebRefresh>().View().Token, Is.EqualTo(Key), "the same refresh, at the same moment");
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
            Assert.That(S.Host.Service<WebRefresh>().View().CancelRequested, Is.False, "nothing canceled");
        }

        // ---- protections ----

        [Test]
        public async Task ApplyTheme_IsAProtectedPost_WithTheFixedPagesForWhatItRefuses()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Visitor = S.Host.Browser();
            await Visitor.Get("/signin");
            WebPage Anonymous = await Visitor.Post(Address + "?handler=theme", ("theme", "dark"));
            Assert.That((Anonymous.Status, Anonymous.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin")), "the owner only");
            Assert.That(SetTheme(Anonymous, S.Host), Is.Null);

            using Browser Owner = await S.Host.SignedIn();
            await Owner.Get(Address);
            WebPage Forged = await Owner.Post(Address + "?handler=theme", ("theme", "dark"), ("__RequestVerificationToken", "forged"));
            Assert.That(Forged.Status, Is.EqualTo(HttpStatusCode.BadRequest), "antiforgery");
            Assert.That(Forged.Body, Is.EqualTo(WebFixedPages.Html(WebFixedPages.For(400).Title, WebFixedPages.For(400).Text)), "the fixed page");
            Owner.Origin = "http://evil.example";
            WebPage Foreign = await Owner.Post(Address + "?handler=theme", ("theme", "dark"));
            Assert.That(Foreign.Status, Is.EqualTo(HttpStatusCode.Forbidden), "the Origin check");
            Owner.Origin = null;
            Assert.That((await Owner.Post(Address + "?handler=theme", ("theme", "dark"))).Status, Is.EqualTo(HttpStatusCode.Forbidden), "no Origin and no Referer");
            Owner.Origin = Owner.Root.GetLeftPart(UriPartial.Authority);
            Owner.Headers["Host"] = "evil.example";
            Assert.That((await Owner.Post(Address + "?handler=theme", ("theme", "dark"))).Status, Is.EqualTo(HttpStatusCode.BadRequest), "the Host check");
            Owner.Headers.Remove("Host");
            foreach ((string Name, string Value)[] Fields in new[]
            {
                new[] { ("theme", "blue") }, new[] { ("theme", "Dark") }, new[] { ("theme", "") }, Array.Empty<(string, string)>(), new[] { ("theme", "dark"), ("theme", "light") }
            })
            {
                await Owner.Get(Address);
                WebPage Unknown = await Owner.Post(Address + "?handler=theme", Fields);
                Assert.That(Unknown.Status, Is.EqualTo(HttpStatusCode.BadRequest), string.Join(",", Fields));
                Assert.That(Unknown.Body, Is.EqualTo(WebFixedPages.Html(WebFixedPages.For(400).Title, WebFixedPages.For(400).Text)), "the fixed page; nothing of the value");
                Assert.That(SetTheme(Unknown, S.Host), Is.Null);
            }
            Assert.That(Owner.Cookie(ThemeCookie(S.Host)), Is.Null, "none of them chose a theme");
            //a GET changes nothing: it is the page
            WebPage Get = await Owner.Get(Address + "?handler=theme&theme=dark");
            Assert.That((Get.Status, SetTheme(Get, S.Host)), Is.EqualTo((HttpStatusCode.OK, (List<string>?)null)));
            Assert.That(Get.Header("Content-Security-Policy"), Is.EqualTo(WebSecurity.ContentSecurityPolicy));
            Assert.That(WebSecurity.ContentSecurityPolicy, Is.EqualTo("default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"),
                "the policy as D81 has it: nothing inline, nothing from another site");
            Assert.That(Get.Header("Access-Control-Allow-Origin"), Is.Empty);
        }

        [Test]
        public async Task ApplyTheme_NeverMakesOrKeepsASession()
        {
            await using SystemHost S = await SystemHost.Start();
            //the theme cookie alone signs no one in
            using Browser Stranger = S.Host.Browser();
            Choose(Stranger, S.Host, "dark");
            WebPage Refused = await Stranger.Get(Address);
            Assert.That((Refused.Status, Refused.Location), Is.EqualTo((HttpStatusCode.Redirect, "/signin?returnUrl=%2Fsettings%2Fsystem")));

            //a choice is the user's own request, as any other: the idle time counts from it, and it ends 8 hours after it
            using Browser Owner = await S.Host.SignedIn();
            S.Host.Now += TimeSpan.FromHours(7);
            Assert.That((await Apply(Owner, "dark")).Location, Is.EqualTo(Address));
            S.Host.Now += WebSessions.IdleTimeout - TimeSpan.FromMinutes(1);
            Assert.That((await Owner.Get(Address)).Status, Is.EqualTo(HttpStatusCode.OK), "8 hours from the last request");
            S.Host.Now += WebSessions.IdleTimeout;
            WebPage Ended = await Owner.Post(Address + "?handler=theme", ("theme", "light"));
            Assert.That(Ended.Location, Does.StartWith("/signin"), "an ended session chooses nothing");
            Assert.That(SetTheme(Ended, S.Host), Is.Null);
            Assert.That(Owner.Cookie(ThemeCookie(S.Host)), Is.EqualTo("dark"), "the choice stays this browser's");
            //and never past the session's lifetime: the cookie's year is the theme's, not the session's
            Assert.That(WebTheme.Lifetime, Is.GreaterThan(WebSessions.AbsoluteLifetime));
            Assert.That(WebSessions.IdleTimeout, Is.EqualTo(TimeSpan.FromHours(8)), "the idle time is D81's");
        }

        [Test]
        public async Task ThemeChoices_HaveTheirOwnRateLimit_AndNeverUseTheRefreshs()
        {
            TimeSpan Window = TimeSpan.FromSeconds(30);
            await using SystemHost S = await SystemHost.Start(More: Settings => Settings.RateWindow = Window);
            using Browser Owner = await S.Host.SignedIn();
            await Owner.Get(Address);
            for (int i = 0; i < CampertronWebHost.ThemeChoicesPerClient; i++)
            {
                Assert.That((await Owner.Post(Address + "?handler=theme", ("theme", i % 2 == 0 ? "dark" : "light"))).Status, Is.EqualTo(HttpStatusCode.Redirect), "choice " + (i + 1));
            }
            WebPage Limited = await Owner.Post(Address + "?handler=theme", ("theme", "dark"));
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(SetTheme(Limited, S.Host), Is.Null);
            Assert.That(Limited.Text(), Does.Contain("Campertron received too many requests from this address."));
            //the refresh's own limit is untouched: Start refresh is taken
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started after thirty theme choices");
            Assert.That(CampertronWebHost.ThemeChoicesPerClient, Is.GreaterThan(CampertronWebHost.RefreshActionsPerClient));
        }

        [TestCase("POST", "/settings/system", "theme", false, true)]
        [TestCase("POST", "/settings/system", "THEME", false, true)]
        [TestCase("POST", "/settings/system", "start", true, false)]
        [TestCase("POST", "/settings/system", "cancel", true, false)]
        [TestCase("POST", "/settings/system", "anything", true, false)]
        [TestCase("POST", "/settings/system", null, true, false)]
        [TestCase("GET", "/settings/system", "theme", false, false)]
        [TestCase("POST", "/settings/general", "theme", false, false)]
        public void ARefreshAction_IsLimitedWithTheRefreshs_AndOnlyTheThemesHandlerWithTheThemes(string Method, string Path_, string? Handler, bool Refresh, bool Theme)
        {
            var Context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            Context.Request.Method = Method;
            Context.Request.Path = Path_;
            Context.Request.QueryString = Handler == null ? Microsoft.AspNetCore.Http.QueryString.Empty : Microsoft.AspNetCore.Http.QueryString.Create("handler", Handler);
            Assert.That((CampertronWebHost.IsRefreshPost(Context), CampertronWebHost.IsThemePost(Context)), Is.EqualTo((Refresh, Theme)));
        }

        // ---- each browser its own; kept through a sign-out, a password change and a restart ----

        [Test]
        public async Task TheChoice_IsEachBrowsersOwn_NeverTheWebUIsOrASessions()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser A = await S.Host.SignedIn();
            using Browser B = await S.Host.SignedIn();
            using Browser C = await S.Host.SignedIn();
            await Apply(A, "dark");
            await Apply(B, "light");
            Assert.That(Asked(await A.Get("/results")), Is.EqualTo(Expected("dark")));
            Assert.That(Asked(await B.Get("/results")), Is.EqualTo(Expected("light")), "B's own, not A's");
            Assert.That(Asked(await C.Get("/results")), Is.EqualTo(Expected(null)), "a browser that chose nothing: System");
            await Apply(B, "system");
            Assert.That(Asked(await A.Get("/results")), Is.EqualTo(Expected("dark")), "B's choice never changes A's");
            using Browser New = await S.Host.SignedIn();
            Assert.That(Asked(await New.Get("/results")), Is.EqualTo(Expected(null)), "nothing of a choice is the web UI's");
        }

        [Test]
        public async Task WithoutPasswordProtection_TheChoiceIsStillEachBrowsersOwn()
        {
            await using SystemHost S = await SystemHost.Start(WithPassword: false, WebYaml: "passwordProtection: false\n");
            using Browser A = S.Host.Browser();
            using Browser B = S.Host.Browser();
            await Apply(A, "dark");
            Assert.That(Asked(await A.Get(Address)), Is.EqualTo(Expected("dark")));
            Assert.That(Asked(await B.Get(Address)), Is.EqualTo(Expected(null)), "one workspace for the web UI, but each browser's own theme");
        }

        [Test]
        public async Task TheChoice_OutlivesASignOut_APasswordChange_AndARestartOfWebMode()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                await using (WebTestHost Host = await WebTestHost.Start(WithPassword: true, Folder: Folder))
                {
                    using Browser Owner = await Host.SignedIn();
                    await Owner.Get(Address);
                    await Owner.Post(Address + "?handler=theme", ("theme", "dark"));
                    await Owner.Get("/");
                    WebPage Out = await Owner.Post("/signout");
                    Assert.That(Out.SetCookies.Any(One => One.StartsWith(Host.Service<WebTheme>().CookieName, StringComparison.Ordinal)), Is.False, "a sign-out keeps the theme");
                    Assert.That(Asked(await Owner.Get("/signin?notice=signed-out")), Is.EqualTo(Expected("dark")), "the sign-in in the chosen theme");
                    Assert.That((await Owner.Post("/signin", ("Password", WebTestHost.Password))).Location, Is.EqualTo("/"));
                    Assert.That(Asked(await Owner.Get(Address)), Is.EqualTo(Expected("dark")), "signed in again");
                    await Owner.Get("/account/password");
                    WebPage Changed = await Owner.Post("/account/password", ("Current", WebTestHost.Password), ("Password", "another long password"), ("Repeat", "another long password"));
                    Assert.That(Changed.Status, Is.EqualTo(HttpStatusCode.Redirect));
                    Assert.That(Changed.SetCookies.Any(One => One.StartsWith(Host.Service<WebTheme>().CookieName, StringComparison.Ordinal)), Is.False, "a password change keeps the theme");
                    Assert.That(Asked(await Owner.Get("/signin")), Is.EqualTo(Expected("dark")));
                }
                //web mode started again on the same settings folder: the browser's cookie is the same web UI's
                await using WebTestHost Again = await WebTestHost.Start(Folder: Folder);
                using Browser Returning = Again.Browser();
                Returning.Cookies.Add(Returning.Root, new Cookie(Again.Service<WebTheme>().CookieName, "dark", "/"));
                Assert.That(Asked(await Returning.Get("/signin")), Is.EqualTo(Expected("dark")));
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        // ---- the stylesheet, the words, and what the web UI can't reach ----

        [Test]
        public void TheStylesheet_HasTheThreeThemes_TheDarkColorsTheSameBothWays()
        {
            string Css = ThemeLook.Css();
            Dictionary<string, string> Root = ThemeLook.Rule(Css, ":root");
            Assert.That(Root["color-scheme"], Is.EqualTo("light dark"), "System: the browser's light or dark");
            Dictionary<string, string> FollowsDark = ThemeLook.Rule(Css, ":root:not([data-theme=\"light\"])");
            Dictionary<string, string> ForcedDark = ThemeLook.Rule(Css, ":root[data-theme=\"dark\"]");
            Dictionary<string, string> ForcedLight = ThemeLook.Rule(Css, ":root[data-theme=\"light\"]");
            Assert.That(ForcedLight, Is.EqualTo(new Dictionary<string, string> { ["color-scheme"] = "light" }), "Light: the light colors, the light scheme");
            Assert.That(ForcedDark["color-scheme"], Is.EqualTo("dark"));
            Assert.That(ForcedDark.Where(P => P.Key != "color-scheme"), Is.EquivalentTo(FollowsDark), "the dark colors the same both ways");
            Assert.That(FollowsDark.Keys, Is.EquivalentTo(Root.Keys.Where(K => K.StartsWith("--", StringComparison.Ordinal))), "every color has a dark one");
            Assert.That(FollowsDark.Values.Intersect(Root.Values), Is.Empty, "the two themes share no color");
            //the dark colors only for a browser that asks for dark and a page that did not choose Light, or for Dark
            Assert.That(Css, Does.Contain("@media (prefers-color-scheme: dark) {\n  :root:not([data-theme=\"light\"]) {"));
            Assert.That(System.Text.RegularExpressions.Regex.Matches(Css, "--page:").Count, Is.EqualTo(3), "the colors are set by these three rules only");
            Assert.That(Css, Does.Not.Contain("@import").And.Not.Contain("url("), "nothing from anywhere");
        }

        [Test]
        public void TheWords_AreTheSharedLayers_AndTheWebUICantReachTheDesktopGUIsPreferences()
        {
            Assert.That((ThemeWording.SystemLabel, ThemeWording.LightLabel, ThemeWording.DarkLabel), Is.EqualTo(("System", "Light", "Dark")));
            Assert.That(WebSystemWording.ThemeSystemHelp, Does.EndWith(ThemeWording.DefaultNote));
            Assert.That(WebSystemWording.Intro, Does.Not.Contain("CampertronGUI's own"), "the theme is no longer said to be the desktop GUI's alone");
            //the web UI's assembly refers to no desktop GUI and writes no file for a theme
            Assert.That(typeof(WebTheme).Assembly.GetReferencedAssemblies().Select(A => A.Name), Has.None.EqualTo("CampertronGUI"));
            List<string> Called = IlReferences.ReferencedMembers(typeof(WebTheme), typeof(CampertronWeb.Pages.SystemModel)).Select(C => C.Member).Distinct().ToList();
            Assert.That(Called.Where(M => M.StartsWith("System.IO.File", StringComparison.Ordinal) || M.StartsWith("System.IO.Directory", StringComparison.Ordinal)
                || M.Contains("ConfigFileReplace", StringComparison.Ordinal) || M.Contains("Yaml", StringComparison.Ordinal)), Is.Empty, "no file, no settings file");
        }
    }
}
