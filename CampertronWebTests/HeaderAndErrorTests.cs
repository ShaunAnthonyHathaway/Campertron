using System.Net;

namespace CampertronWebTests
{
    // The headers of every answer, what a browser may keep, and what a failure shows (D81, D92)
    public class HeaderAndErrorTests
    {
        private static void AssertSecurityHeaders(WebPage Answer, string What)
        {
            Assert.That(Answer.Header("Content-Security-Policy"),
                Is.EqualTo("default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'"), What);
            Assert.That(Answer.Header("Content-Security-Policy"), Does.Not.Contain("unsafe"), What);
            Assert.That(Answer.Header("X-Content-Type-Options"), Is.EqualTo("nosniff"), What);
            Assert.That(Answer.Header("X-Frame-Options"), Is.EqualTo("DENY"), What);
            Assert.That(Answer.Header("Referrer-Policy"), Is.EqualTo("same-origin"), What);
            Assert.That(Answer.Header("Permissions-Policy"), Does.Contain("camera=()").And.Contain("geolocation=()").And.Contain("microphone=()"), What);
            Assert.That(Answer.Header("Cross-Origin-Opener-Policy"), Is.EqualTo("same-origin"), What);
            Assert.That(Answer.Header("Cross-Origin-Resource-Policy"), Is.EqualTo("same-origin"), What);
            Assert.That(Answer.Header("X-Robots-Tag"), Is.EqualTo("noindex, nofollow"), What);
            Assert.That(Answer.Header("Server"), Is.Empty, What);
            Assert.That(Answer.Header("X-Powered-By"), Is.Empty, What);
            Assert.That(Answer.Header("Strict-Transport-Security"), Is.Empty, What + ": the web UI speaks HTTP only");
            Assert.That(Answer.Header("Access-Control-Allow-Origin"), Is.Empty, What);
        }

        [Test]
        public async Task EveryAnswer_CarriesTheSecurityHeaders_AndNoPageIsKeptByTheBrowser()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            using Browser Visitor = Host.Browser();
            var Answers = new List<(string What, WebPage Answer, bool Stored)>
            {
                ("the start page", await Owner.Get("/"), false),
                ("the password page", await Owner.Get("/account/password"), false),
                ("the sign-in", await Visitor.Get("/signin"), false),
                ("a redirect to the sign-in", await Visitor.Get("/"), false),
                ("a page that does not exist", await Owner.Get("/nothing-here"), false),
                ("a page that does not exist, before the sign-in", await Visitor.Get("/nothing-here"), false),
                ("the health answer", await Visitor.Get("/healthz"), false),
                ("a refused method", await Visitor.Send(new HttpRequestMessage(HttpMethod.Put, "/")), false),
                ("a refused form", await Visitor.Post("/signin", ("Password", "x"), ("__RequestVerificationToken", "bad")), false),
                ("the stylesheet", await Visitor.Get(StylePath(await Visitor.Get("/signin"))), true),
            };
            Visitor.Headers["Host"] = "evil.example";
            Answers.Add(("a refused name", await Visitor.Get("/signin"), false));
            foreach ((string What, WebPage Answer, bool Stored) in Answers)
            {
                AssertSecurityHeaders(Answer, What);
                if (Stored)
                {
                    Assert.That(Answer.Header("Cache-Control"), Is.EqualTo("public, max-age=31536000, immutable"), What);
                }
                else
                {
                    Assert.That(Answer.Header("Cache-Control"), Does.Contain("no-store"), What);
                }
            }
        }

        // The unified app icon (D89, v0.3.0's PR 11): every page names it, the web UI's own; its versioned address and
        // /favicon.ico (which browsers ask for by themselves) answer with exactly the committed campertron.ico, before the
        // setup, before the sign-in and signed in, with the security headers and without a cookie or a redirect
        [Test]
        public async Task TheIcon_IsTheUnifiedAppIcon_OnEveryPage_AndAtFaviconIco_WithoutASignIn()
        {
            string Root = TestContext.CurrentContext.TestDirectory;
            while (!File.Exists(Path.Join(Root, "Campertron.sln")))
            {
                Root = Path.GetDirectoryName(Root) ?? throw new AssertionException("the repository was not found above the test folder");
            }
            byte[] Icon = File.ReadAllBytes(Path.Join(Root, "assets", "icon", "campertron.ico"));

            void AssertIcon(WebPage Answer, string What, string CacheControl)
            {
                Assert.That(Answer.Status, Is.EqualTo(HttpStatusCode.OK), What);
                Assert.That(Answer.Header("Content-Type"), Is.EqualTo("image/x-icon"), What);
                Assert.That(Answer.Bytes, Is.EqualTo(Icon), What + ": the committed icon, nothing else");
                Assert.That(Answer.Header("Cache-Control"), Is.EqualTo(CacheControl), What);
                Assert.That(Answer.SetCookies, Is.Empty, What + ": no cookie");
                AssertSecurityHeaders(Answer, What);
            }

            //before a password is set: the setup page names the icon, and neither address leads to the setup
            await using (WebTestHost Fresh = await WebTestHost.Start())
            {
                using Browser Newcomer = Fresh.Browser();
                string Href = IconPath(await Newcomer.Get("/setup"));
                AssertIcon(await Newcomer.Get(Href), "the icon before the setup", "public, max-age=31536000, immutable");
                AssertIcon(await Newcomer.Get("/favicon.ico"), "/favicon.ico before the setup", "no-cache");
            }

            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            using Browser Owner = await Host.SignedIn();
            string Path_ = IconPath(await Visitor.Get("/signin"));
            Assert.That(Path_, Does.Match(@"^/assets/campertron\.ico\?v=[0-9a-f]{16}$"));
            foreach ((string What, WebPage Page) in new[]
            {
                ("the start page", await Owner.StartPage()), ("the settings", await Owner.Get("/settings/general")),
                ("the guided setup", await Owner.Get("/guided-setup")), ("a page that does not exist", await Owner.Get("/nothing-here")),
                ("a refused method's fixed page, before the sign-in", await Visitor.Send(new HttpRequestMessage(HttpMethod.Put, "/")))
            })
            {
                Assert.That(Page.Body, Does.StartWith("<!DOCTYPE html>"), What + " (" + Page.Status + ")");
                Assert.That(IconPath(Page), Is.EqualTo(Path_), What + ": the same icon on every page");
            }
            AssertIcon(await Visitor.Get(Path_), "the icon before the sign-in", "public, max-age=31536000, immutable");
            AssertIcon(await Visitor.Get("/favicon.ico"), "/favicon.ico before the sign-in", "no-cache");
            AssertIcon(await Owner.Get("/favicon.ico"), "/favicon.ico signed in", "no-cache");
            AssertIcon(await Visitor.Get("/assets/campertron.ico?v=0000000000000000"), "an old address of the icon", "no-cache");

            WebPage Head = await Visitor.Send(new HttpRequestMessage(HttpMethod.Head, "/favicon.ico"));
            Assert.That(Head.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Head.Header("Content-Length"), Is.EqualTo(Icon.Length.ToString()));
            Assert.That(Head.Bytes, Is.Empty);
            //it is an address to read, not to change: a POST is refused, and the icon stays as it was
            WebPage Posted = await Owner.Post("/favicon.ico", ("x", "y"));
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(Posted.Bytes, Is.Not.EqualTo(Icon));
            AssertIcon(await Visitor.Get("/favicon.ico"), "/favicon.ico after a refused POST", "no-cache");
        }

        private static string IconPath(WebPage Page) =>
            Page.Xml().Descendants("link").Single(Link => (string?)Link.Attribute("rel") == "icon").Attribute("href")!.Value;

        private static string StylePath(WebPage Page) =>
            Page.Xml().Descendants("link").Single(Link => (string?)Link.Attribute("rel") == "stylesheet").Attribute("href")!.Value;

        [Test]
        public async Task TheStylesheet_IsTheWebUisOwn_AndAnOldAddressOfItIsCheckedAgain()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            string Style = StylePath(await Visitor.Get("/signin"));
            Assert.That(Style, Does.Match(@"^/assets/site\.css\?v=[0-9a-f]{16}$"));
            WebPage Sheet = await Visitor.Get(Style);
            Assert.That(Sheet.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Sheet.Header("Content-Type"), Is.EqualTo("text/css; charset=utf-8"));
            Assert.That(Sheet.Body, Does.Contain("prefers-color-scheme: dark"));
            Assert.That(Sheet.Body, Does.Not.Contain("url(").And.Not.Contain("@import").And.Not.Contain("http"), "nothing is loaded from anywhere else");
            Assert.That((await Visitor.Get("/assets/site.css?v=0000000000000000")).Header("Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That((await Visitor.Get("/assets/site.css")).Header("Cache-Control"), Is.EqualTo("no-cache"));
            Assert.That((await Visitor.Get("/assets/other.css")).Status, Is.EqualTo(HttpStatusCode.NotFound));
        }

        [Test]
        public async Task AnUnexpectedFailure_ShowsAFixedPage_AndTheConsoleGetsOnlyTheFailuresTypeName()
        {
            await using WebTestHost Host = await WebTestHost.Start("passwordProtection: false\n", Configure: Settings => Settings.MapFailingPage = true);
            using Browser Visitor = Host.Browser();
            WebPage Failed = await Visitor.Get("/test/fail?token=SECRET-IN-QUERY");
            Assert.That(Failed.Status, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(Failed.Text(), Does.Contain("Campertron could not show this page because of an unexpected error. Nothing was changed."));
            foreach (string Leak in new[] { "secret detail", "/a/path", "InvalidOperationException", "CampertronWeb", "   at ", "SECRET-IN-QUERY", Host.Folder, ".cs:", "cshtml", "System." })
            {
                Assert.That(Failed.Body, Does.Not.Contain(Leak), "the error page shows " + Leak);
            }
            AssertSecurityHeaders(Failed, "the error page");
            Assert.That(Failed.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That(Host.Warnings, Is.EqualTo(new[] { "Web UI: a request failed unexpectedly (InvalidOperationException); the browser was shown an error page." }));
            await Visitor.Get("/test/fail");
            Assert.That(Host.Warnings, Has.Count.EqualTo(1), "one line a minute");
            Assert.That(Host.Lines, Is.Empty);
        }

        [Test]
        public async Task ARequestThatIsTooLarge_IsRefusedWithAFixedPage_BeforeAnythingOfItIsRead()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            //only the request's head, which says how large the body would be: the refusal does not wait for a body (a
            //client that is still sending one when the answer comes may see its connection closed instead)
            foreach (long Size in new[] { CampertronWebHost.MaxRequestBytes + 1, 2L * 1024 * 1024 * 1024 })
            {
                string Answer = await RawRequest.Send(Host.Port, "POST /signin HTTP/1.1\r\nHost: 127.0.0.1:" + Host.Port + "\r\nOrigin: http://127.0.0.1:" + Host.Port
                    + "\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: " + Size + "\r\nConnection: close\r\n\r\n");
                Assert.That(Answer, Does.StartWith("HTTP/1.1 413 "), Size + " bytes");
                Assert.That(Answer, Does.Contain("does not accept a request of this size").And.Contain("Content-Security-Policy: default-src 'none'").And.Contain("Cache-Control: no-store"));
            }
            //the largest size the web UI reads is not refused for its size
            string Allowed = await RawRequest.Send(Host.Port, "POST /signin HTTP/1.1\r\nHost: 127.0.0.1:" + Host.Port + "\r\nOrigin: http://127.0.0.1:" + Host.Port
                + "\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            Assert.That(Allowed, Does.StartWith("HTTP/1.1 400 "), "an empty form has no antiforgery token; its size is fine");
            Assert.That(Host.Warnings, Is.Empty, "a refusal is no failure");
        }

        [Test]
        public async Task AfterASignOut_NoPageComesBackFromTheBrowsersMemory()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            foreach (string Path_ in new[] { "/", "/account/password" })
            {
                WebPage Page = await Owner.Get(Path_);
                Assert.That(Page.Header("Cache-Control"), Does.Contain("no-store"), Path_ + ": the back button must ask again");
                Assert.That(Page.Header("ETag") + Page.Header("Last-Modified") + Page.Header("Expires"), Is.Empty.Or.EqualTo("-1"), Path_);
            }
            await Owner.Post("/signout");
            Assert.That((await Owner.Get("/account/password")).Location, Does.StartWith("/signin"), "asking again gets the sign-in");
        }
    }
}
