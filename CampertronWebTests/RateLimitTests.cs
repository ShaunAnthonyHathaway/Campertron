using System.Net;

namespace CampertronWebTests
{
    // The rate limits (D81, D92): a client's POSTs with a password or a setup code, all clients' together, and every
    // request of a client; the health answer is not counted
    public class RateLimitTests
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

        [Test]
        public async Task TheSixthPasswordOfAMinute_IsRefused_AlsoTheRightOne_AndTheNextMinuteItIsTakenAgain()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.RateWindow = Window);
            using Browser Visitor = Host.Browser();
            await Visitor.Get("/signin");
            for (int i = 0; i < CampertronWebHost.CredentialsPerClient; i++)
            {
                Assert.That((await Visitor.Post("/signin", ("Password", "wrong password " + i))).Status, Is.EqualTo(HttpStatusCode.OK), "attempt " + (i + 1));
            }
            //a forwarded address from someone who is no trusted proxy gives no new count
            Visitor.Headers["X-Forwarded-For"] = "203.0.113.77";
            WebPage Limited = await Visitor.Post("/signin", ("Password", WebTestHost.Password));
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests), "the right password is not even looked at");
            Assert.That(Limited.Header("Retry-After"), Is.EqualTo("2"));
            Assert.That(Limited.Text(), Does.Contain("Campertron received too many requests from this address."));
            Assert.That(Limited.SetCookies.Any(One => One.StartsWith("campertron-session", StringComparison.Ordinal)), Is.False);
            Assert.That((await Visitor.Get("/signin")).Status, Is.EqualTo(HttpStatusCode.OK), "reading the page is not limited with the passwords");

            await Task.Delay(Window + TimeSpan.FromMilliseconds(400));
            Assert.That((await Visitor.Post("/signin", ("Password", WebTestHost.Password))).Location, Is.EqualTo("/"), "a new window takes it again");
        }

        [Test]
        public async Task TheSetupCodeAndThePasswordChange_AreLimitedTheSameWay()
        {
            await using (WebTestHost Fresh = await WebTestHost.Start(Configure: Settings => Settings.RateWindow = Window))
            {
                using Browser Visitor = Fresh.Browser();
                string Code = Fresh.NewSetupCode();
                await Visitor.Get("/setup");
                for (int i = 0; i < CampertronWebHost.CredentialsPerClient; i++)
                {
                    Assert.That((await Visitor.Post("/setup", ("Code", "AAAA-AAAA-AAA" + "ABCDE"[i]), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))).Status,
                        Is.EqualTo(HttpStatusCode.OK));
                }
                Assert.That((await Visitor.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))).Status,
                    Is.EqualTo(HttpStatusCode.TooManyRequests), "guessing the code is held off");
                Assert.That((await Visitor.Post("/setup?handler=NewCode")).Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
                Assert.That(File.Exists(Path.Join(Fresh.Folder, "Web.password")), Is.False);
            }
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.RateWindow = Window);
            using Browser Owner = await Host.SignedIn();
            await Owner.Get("/account/password");
            for (int i = 0; i < CampertronWebHost.CredentialsPerClient - 1; i++)
            {
                Assert.That((await Owner.Post("/account/password", ("Current", "wrong"), ("Password", "a new password 1"), ("Repeat", "a new password 1"))).Status, Is.EqualTo(HttpStatusCode.OK));
            }
            Assert.That((await Owner.Post("/account/password", ("Current", WebTestHost.Password), ("Password", "a new password 1"), ("Repeat", "a new password 1"))).Status,
                Is.EqualTo(HttpStatusCode.TooManyRequests), "the sign-in was the first of the five");
            Assert.That(new WebPasswordFile(Host.Folder).Verify(WebTestHost.Password), Is.True);
        }

        [Test]
        public async Task EveryRequestOfAClientIsCounted_ButNeverTheHealthAnswer()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.RateWindow = TimeSpan.FromSeconds(30));
            using Browser Visitor = Host.Browser();
            for (int i = 0; i < CampertronWebHost.RequestsPerClient; i++)
            {
                HttpStatusCode Status = (await Visitor.Get("/assets/site.css")).Status;
                if (Status != HttpStatusCode.OK)
                {
                    Assert.Fail("request " + (i + 1) + " was answered with " + Status);
                }
            }
            WebPage Limited = await Visitor.Get("/assets/site.css");
            Assert.That(Limited.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Limited.Header("Retry-After"), Is.EqualTo("30"));
            Assert.That((await Visitor.Get("/signin")).Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            for (int i = 0; i < 20; i++)
            {
                Assert.That((await Visitor.Get("/healthz")).Status, Is.EqualTo(HttpStatusCode.OK), "a container's health check never fails because a browser was busy");
            }
        }

        [Test]
        public void AClient_IsItsAddress_AndAnIPv6ClientItsNetwork()
        {
            static string Key(string Address)
            {
                var Context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
                Context.Connection.RemoteIpAddress = IPAddress.Parse(Address);
                return WebSecurity.ClientKey(Context);
            }
            Assert.That(Key("192.168.1.20"), Is.EqualTo("192.168.1.20"));
            Assert.That(Key("::ffff:192.168.1.20"), Is.EqualTo("192.168.1.20"));
            Assert.That(Key("2001:db8:1:2:aaaa:bbbb:cccc:dddd"), Is.EqualTo("2001:db8:1:2::/64"));
            Assert.That(Key("2001:db8:1:2::1"), Is.EqualTo(Key("2001:db8:1:2:ffff:ffff:ffff:ffff")), "one device has as many addresses of its network as it likes");
            Assert.That(Key("2001:db8:1:3::1"), Is.Not.EqualTo(Key("2001:db8:1:2::1")));
            Assert.That(WebSecurity.ClientKey(new Microsoft.AspNetCore.Http.DefaultHttpContext()), Is.EqualTo("unknown"));
        }
    }
}
