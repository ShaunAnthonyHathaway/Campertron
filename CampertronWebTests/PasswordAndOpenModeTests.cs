using System.Net;
using CampertronWeb.Pages;

namespace CampertronWebTests
{
    // The password change, and the web UI without password protection (D81, D92)
    public class PasswordAndOpenModeTests
    {
        private const string NewPassword = "a brand new passphrase";

        [Test]
        public async Task AChangedPassword_ReplacesTheHash_AndSignsOutEveryBrowser()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Phone = await Host.SignedIn();
            using Browser Desktop = await Host.SignedIn();
            string Before = File.ReadAllText(Path.Join(Host.Folder, "Web.password"));
            await Desktop.Get("/account/password");
            WebPage Changed = await Desktop.Post("/account/password", ("Current", WebTestHost.Password), ("Password", NewPassword), ("Repeat", NewPassword));
            Assert.That(Changed.Location, Is.EqualTo("/signin?notice=password-changed"));
            Assert.That((await Desktop.Get("/")).Location, Is.EqualTo("/signin"), "this browser is signed out");
            Assert.That((await Phone.Get("/")).Location, Is.EqualTo("/signin"), "and every other one (D81)");
            Assert.That(Host.Service<WebSessions>().Count, Is.Zero);

            string After = File.ReadAllText(Path.Join(Host.Folder, "Web.password"));
            Assert.That(After, Is.Not.EqualTo(Before).And.Match(@"^\$pbkdf2-sha256\$i=100000\$"));
            Assert.That(After, Does.Not.Contain(NewPassword));
            Assert.That(Directory.GetFiles(Host.Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Web.password" }), "no backup of the old hash and no temporary file");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Path.Join(Host.Folder, "Web.password")), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            using Browser Again = Host.Browser();
            await Again.Get("/signin?notice=password-changed");
            Assert.That((await Again.Post("/signin", ("Password", WebTestHost.Password))).Text(), Does.Contain(SignInModel.WrongPassword), "the old password is gone");
            Assert.That((await Again.Post("/signin", ("Password", NewPassword))).Location, Is.EqualTo("/"));
        }

        [Test]
        public async Task AChange_NeedsTheCurrentPassword_AndAUsableNewOne_AndPutsNoPasswordBackIntoThePage()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            await Owner.Get("/account/password");
            WebPage Wrong = await Owner.Post("/account/password", ("Current", "not the current one"), ("Password", NewPassword), ("Repeat", NewPassword));
            Assert.That(Wrong.Text(), Does.Contain(PasswordModel.WrongCurrent));
            Assert.That(Host.Lines.Single(), Does.StartWith("Web UI: failed sign-in from 127.0.0.1"));
            WebPage Short = await Owner.Post("/account/password", ("Current", WebTestHost.Password), ("Password", "too short"), ("Repeat", "too short"));
            Assert.That(Short.Text(), Does.Contain("The password is too short"));
            WebPage Different = await Owner.Post("/account/password", ("Current", WebTestHost.Password), ("Password", NewPassword), ("Repeat", NewPassword + "!"));
            Assert.That(Different.Text(), Does.Contain("The two passwords are not the same"));
            foreach (WebPage Page in new[] { Wrong, Short, Different })
            {
                Assert.That(Page.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Page.Body, Does.Not.Contain(NewPassword).And.Not.Contain(WebTestHost.Password).And.Not.Contain("not the current one").And.Not.Contain("too short\""));
                Assert.That(Page.Xml().Descendants("input").Where(Input => (string?)Input.Attribute("type") == "password").Select(Input => Input.Attribute("value")), Has.All.Null);
            }
            Assert.That(new WebPasswordFile(Host.Folder).Verify(WebTestHost.Password), Is.True, "nothing changed");
            Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "and nobody was signed out");
        }

        [Test]
        public async Task WithoutPasswordProtection_ThereIsNoSignIn_ButEveryOtherCheckStays()
        {
            await using WebTestHost Host = await WebTestHost.Start("passwordProtection: false\n");
            using Browser Visitor = Host.Browser();
            WebPage Start = await Visitor.Get("/");
            Assert.That(Start.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Start.Text(), Does.Contain("It is used without a password.").And.Contain("Not required: everyone who can reach this web UI can use it"));
            Assert.That(Start.Body, Does.Not.Contain("Sign out").And.Not.Contain("Change password"));
            foreach (string Path_ in new[] { "/signin", "/setup", "/account/password", "/signout" })
            {
                Assert.That((await Visitor.Get(Path_)).Location, Is.EqualTo("/"), Path_);
            }
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False, "no password is ever asked for or created");

            //the checks that do not depend on a password (this version's open web UI has no form, so no POST has a token)
            Assert.That(Visitor.Token, Is.Null);
            Assert.That((await Visitor.Post("/signout")).Status, Is.EqualTo(HttpStatusCode.BadRequest), "the antiforgery token");
            Visitor.Origin = "http://evil.example";
            Assert.That((await Visitor.Post("/signout")).Status, Is.EqualTo(HttpStatusCode.Forbidden), "the Origin check");
            Visitor.Headers["Host"] = "evil.example";
            Assert.That((await Visitor.Get("/")).Status, Is.EqualTo(HttpStatusCode.BadRequest), "the Host check");
        }

        [Test]
        public async Task WithoutPasswordProtection_AnExistingPasswordIsNotAskedFor_AndASessionCookieOpensNothingMore()
        {
            string Folder = WebTestHost.NewFolder();
            try
            {
                string Cookie;
                await using (WebTestHost Protected = await WebTestHost.Start(WithPassword: true, Folder: Folder))
                {
                    using Browser Owner = await Protected.SignedIn();
                    Cookie = Owner.Cookie("campertron-session-0")!;
                }
                await using WebTestHost Open = await WebTestHost.Start("passwordProtection: false\n", Folder: Folder);
                using Browser Visitor = Open.Browser();
                Visitor.Cookies.Add(Open.Root, new Cookie("campertron-session-0", Cookie));
                WebPage Start = await Visitor.Get("/");
                Assert.That(Start.Status, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(Start.Body, Does.Not.Contain("You are signed in").And.Not.Contain("Sign out"));
            }
            finally
            {
                Directory.Delete(Folder, true);
            }
        }
    }
}
