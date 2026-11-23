using System.Net;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The sign-in, the session and the sign-out (D81, D92)
    public class SignInTests
    {
        [Test]
        public async Task EveryPageNeedsASignIn_AndTheSignInRemembersOnlyAPageOfThisWebUi()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            WebPage Start = await Visitor.Get("/");
            Assert.That(Start.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Start.Location, Is.EqualTo("/signin"));
            WebPage Account = await Visitor.Get("/account/password?x=1");
            Assert.That(Account.Location, Is.EqualTo("/signin?returnUrl=%2Faccount%2Fpassword%3Fx%3D1"));
            Assert.That((await Visitor.Get("/setup")).Location, Is.EqualTo("/signin"), "the setup is over once a password is set");
            Assert.That(Start.Body, Is.Empty, "nothing of a page before the sign-in");
        }

        [Test]
        public async Task TheRightPassword_SignsIn_WithASessionCookieThatScriptsAndOtherSitesNeverGet()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = Host.Browser();
            await Owner.Get("/signin");
            WebPage Done = await Owner.Post("/signin", ("Password", WebTestHost.Password));
            Assert.That(Done.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Done.Location, Is.EqualTo("/"));
            string Cookie = Done.SetCookies.Single(One => One.StartsWith("campertron-session-0=", StringComparison.Ordinal));
            Assert.That(Cookie, Does.Contain("; httponly").And.Contain("; samesite=strict").And.Contain("; path=/"));
            Assert.That(Cookie, Does.Not.Contain("secure"), "plain HTTP: a Secure cookie would never come back");
            Assert.That(Cookie, Does.Not.Contain("expires").And.Not.Contain("max-age").And.Not.Contain("domain"), "a session cookie, for this host only");
            Assert.That(Cookie, Does.Not.Contain(WebTestHost.Password));
            WebPage Start = await Owner.Get("/");
            Assert.That(Start.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Start.Text(), Does.Contain("You are signed in to Campertron's web UI."));
            Assert.That((await Owner.Get("/signin")).Location, Is.EqualTo("/"), "signed in: the sign-in has nothing to do");
        }

        [Test]
        public async Task AWrongPassword_SaysOneFixedSentence_SignsNobodyIn_AndIsCountedOnTheConsole()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            await Visitor.Get("/signin");
            foreach (string Wrong in new[] { "not the password", "", WebTestHost.Password + " ", new string('x', 129) })
            {
                WebPage Refused = await Visitor.Post("/signin", ("Password", Wrong));
                Assert.That(Refused.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Refused.Text(), Does.Contain(SignInModel.WrongPassword));
                Assert.That(Refused.SetCookies.Any(One => One.StartsWith("campertron-session", StringComparison.Ordinal)), Is.False);
                Assert.That(Wrong.Length > 3 ? Refused.Body.Contains(Wrong) : false, Is.False, "the password is never put back into the page");
            }
            Assert.That((await Visitor.Get("/")).Location, Is.EqualTo("/signin"));
            string Line = Host.Lines.Single(Text => Text.StartsWith("Web UI: failed sign-in", StringComparison.Ordinal));
            Assert.That(Line, Is.EqualTo("Web UI: failed sign-in from 127.0.0.1 (1 failed sign-in from it since the web UI started)."));
            Host.Now = Host.Now.AddMinutes(1);
            await Visitor.Post("/signin", ("Password", "wrong again"));
            Assert.That(Host.Lines.Last(), Is.EqualTo("Web UI: failed sign-in from 127.0.0.1 (5 failed sign-ins from it since the web UI started)."));
            Assert.That(string.Join("\n", Host.Lines), Does.Not.Contain("not the password").And.Not.Contain("wrong again"));
        }

        [TestCase("/account/password", "/account/password")]
        [TestCase("/?a=b", "/?a=b")]
        [TestCase("https://evil.example/", "/")]
        [TestCase("//evil.example/", "/")]
        [TestCase("/\\evil.example", "/")]
        [TestCase("evil.example", "/")]
        [TestCase("javascript:alert(1)", "/")]
        public async Task AfterTheSignIn_TheBrowserGoesOnlyToAnAddressOfThisWebUi(string ReturnUrl, string Expected)
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = Host.Browser();
            string Path_ = "/signin?notice=signed-out&returnUrl=" + Uri.EscapeDataString(ReturnUrl);
            //the form's own address carries the page that was asked for, only when it is this web UI's, and nothing else
            string Action = (string)(await Owner.Get(Path_)).Xml().Descendants("form").Single().Attribute("action")!;
            Assert.That(Action, Is.EqualTo(Expected == "/" ? "/signin" : "/signin?returnUrl=" + Uri.EscapeDataString(Expected)));
            WebPage Wrong = await Owner.Post(Action, ("Password", "not the password"));
            Assert.That((string)Wrong.Xml().Descendants("form").Single().Attribute("action")!, Is.EqualTo(Action), "kept after a wrong password");
            WebPage Done = await Owner.Post(Action, ("Password", WebTestHost.Password));
            Assert.That(Done.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Done.Location, Is.EqualTo(Expected));
        }

        [Test]
        public async Task SignOut_EndsTheSessionInTheWebUi_SoACopyOfTheCookieIsWorthNothing()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            string Stolen = Owner.Cookie("campertron-session-0")!;
            await Owner.Get("/");

            //a GET changes nothing
            Assert.That((await Owner.Get("/signout")).Location, Is.EqualTo("/"));
            Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK));

            WebPage Out = await Owner.Post("/signout");
            Assert.That(Out.Location, Is.EqualTo("/signin?notice=signed-out"));
            Assert.That(Out.SetCookies.Single(One => One.StartsWith("campertron-session-0=;", StringComparison.Ordinal)), Does.Contain("expires=Thu, 01 Jan 1970"));
            Assert.That((await Owner.Get("/")).Location, Is.EqualTo("/signin"));
            Assert.That(Host.Service<WebSessions>().Count, Is.Zero);

            using Browser Thief = Host.Browser();
            Thief.Cookies.Add(Host.Root, new Cookie("campertron-session-0", Stolen));
            WebPage Tried = await Thief.Get("/");
            Assert.That(Tried.Status, Is.EqualTo(HttpStatusCode.Redirect), "the cookie from before the sign-out opens nothing");
            Assert.That(Tried.Location, Is.EqualTo("/signin"));
        }

        [Test]
        public async Task ASession_EndsAfterEightIdleHours_AndAtTheLatestAfterSevenDays()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Idle = await Host.SignedIn();
            Host.Now = Host.Now.AddHours(7).AddMinutes(59);
            Assert.That((await Idle.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK));
            Host.Now = Host.Now.AddHours(8);
            Assert.That((await Idle.Get("/")).Location, Is.EqualTo("/signin"), "eight hours without a request");

            using Browser Busy = await Host.SignedIn();
            DateTime SignedIn = Host.Now;
            while (Host.Now - SignedIn < TimeSpan.FromDays(7) - TimeSpan.FromHours(7))
            {
                Host.Now = Host.Now.AddHours(7);
                Assert.That((await Busy.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "used every seven hours");
            }
            Host.Now = SignedIn.AddDays(7);
            Assert.That((await Busy.Get("/")).Location, Is.EqualTo("/signin"), "seven days after the sign-in");
        }

        [Test]
        public async Task ASession_EndsWhenThePasswordFileIsReplacedOrRemovedOutsideTheWebUi()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            string File_ = Path.Join(Host.Folder, "Web.password");
            File.SetLastWriteTimeUtc(File_, File.GetLastWriteTimeUtc(File_).AddSeconds(5));
            Assert.That((await Owner.Get("/")).Location, Is.EqualTo("/signin"), "the file is no longer the one the session signed in with");

            using Browser Again = await Host.SignedIn();
            File.Delete(File_);
            Assert.That((await Again.Get("/")).Location, Is.EqualTo("/setup"), "no password: the setup, and no session");
        }

        [Test]
        public async Task ARestart_EndsEverySession_AlthoughTheKeysAreTheSame()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Cookie;
                await using (WebTestHost Before = await WebTestHost.Start(WithPassword: true, Folder: Folder))
                {
                    using Browser Owner = await Before.SignedIn();
                    Cookie = Owner.Cookie("campertron-session-0")!;
                }
                await using WebTestHost After = await WebTestHost.Start(Folder: Folder);
                using Browser Same = After.Browser();
                Same.Cookies.Add(After.Root, new Cookie("campertron-session-0", Cookie));
                Assert.That((await Same.Get("/")).Location, Is.EqualTo("/signin"), "sessions are kept in memory only");
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }

        [Test]
        public void Sessions_AreAtMostThirtyTwo_TheLeastRecentlyUsedGoesFirst()
        {
            DateTime Now = new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
            var Sessions = new WebSessions(() => Now);
            (long, DateTime)? File_ = (60, Now);
            var Ids = new List<string>();
            for (int i = 0; i < WebSessions.MaxSessions; i++)
            {
                Now = Now.AddSeconds(1);
                Ids.Add(Sessions.Create(File_));
            }
            Assert.That(Ids.Distinct().Count(), Is.EqualTo(WebSessions.MaxSessions));
            Assert.That(Ids, Has.All.Match("^[0-9A-F]{32}$"), "128 random bits each");
            Now = Now.AddSeconds(1);
            Assert.That(Sessions.Use(Ids[0], File_), Is.True, "the first is used again, so the second is now the oldest");
            Now = Now.AddSeconds(1);
            string Newest = Sessions.Create(File_);
            Assert.That(Sessions.Count, Is.EqualTo(WebSessions.MaxSessions));
            Assert.That(Sessions.Use(Ids[1], File_), Is.False);
            Assert.That(Sessions.Use(Ids[0], File_), Is.True);
            Assert.That(Sessions.Use(Newest, File_), Is.True);
            Assert.That(Sessions.Use(Newest, null), Is.False, "no password file: no session");
            Assert.That(Sessions.Use("0123456789ABCDEF0123456789ABCDEF", File_), Is.False);
            Assert.That(Sessions.Use(null, File_), Is.False);
            Sessions.Clear();
            Assert.That(Sessions.Count, Is.Zero);
        }
    }
}
