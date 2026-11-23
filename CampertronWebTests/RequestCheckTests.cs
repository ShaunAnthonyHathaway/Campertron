using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CampertronWebTests
{
    // What every request passes before a page sees it: the method, the Host check, the Origin check, the antiforgery
    // token and a trusted proxy's forwarded headers (D81, D92)
    public class RequestCheckTests
    {
        // ---- antiforgery ----

        [Test]
        public async Task APostWithoutItsAntiforgeryToken_IsRefused_OnEveryForm()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            await Visitor.Get("/signin");
            string Good = Visitor.Token!;

            Visitor.Token = null;
            WebPage Missing = await Visitor.Post("/signin", ("Password", WebTestHost.Password));
            Assert.That(Missing.Status, Is.EqualTo(HttpStatusCode.BadRequest), "the right password without the token signs nobody in");
            Assert.That(Missing.Text(), Does.Contain("Campertron could not use this request."));
            WebPage Invalid = await Visitor.Post("/signin", ("Password", WebTestHost.Password), ("__RequestVerificationToken", "not-a-token"));
            Assert.That(Invalid.Status, Is.EqualTo(HttpStatusCode.BadRequest));

            //a token that belongs to another browser's form cookie
            using Browser Other = Host.Browser();
            await Other.Get("/signin");
            WebPage Foreign = await Visitor.Post("/signin", ("Password", WebTestHost.Password), ("__RequestVerificationToken", Other.Token!));
            Assert.That(Foreign.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await Visitor.Get("/")).Location, Is.EqualTo("/signin"));

            Visitor.Token = Good;
            Assert.That((await Visitor.Post("/signin", ("Password", WebTestHost.Password))).Location, Is.EqualTo("/"), "the page's own token is accepted");

            //signed in: the sign-out and the password change need their token too
            await Visitor.Get("/account/password");
            Visitor.Token = null;
            Assert.That((await Visitor.Post("/signout")).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await Visitor.Post("/account/password", ("Current", WebTestHost.Password), ("Password", "a new password 1"), ("Repeat", "a new password 1"))).Status,
                Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await Visitor.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "still signed in, and the password unchanged");
            Assert.That(new WebPasswordFile(Host.Folder).Verify(WebTestHost.Password), Is.True);
        }

        [Test]
        public async Task TheSetupsForms_NeedTheirAntiforgeryToken()
        {
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Visitor = Host.Browser();
            string Code = Host.NewSetupCode();
            await Visitor.Get("/setup");
            Visitor.Token = null;
            Assert.That((await Visitor.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await Visitor.Post("/setup?handler=NewCode")).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(File.Exists(Path.Join(Host.Folder, "Web.password")), Is.False);
        }

        // ---- the Origin check ----

        [TestCase("http://evil.example", null, null, false)]
        [TestCase("https://127.0.0.1:{port}", null, null, false)]
        [TestCase("http://127.0.0.1:1", null, null, false)]
        [TestCase("http://localhost:{port}", null, null, false)]
        [TestCase(null, null, null, false)]
        [TestCase("null", null, null, false)]
        [TestCase(null, "http://evil.example/page", null, false)]
        [TestCase("http://127.0.0.1:{port}", null, "cross-site", false)]
        [TestCase("http://127.0.0.1:{port}", null, "same-site", false)]
        [TestCase("http://127.0.0.1:{port}", null, null, true)]
        [TestCase("http://127.0.0.1:{port}", null, "same-origin", true)]
        [TestCase("null", "http://127.0.0.1:{port}/signin", null, true)]
        [TestCase(null, "http://127.0.0.1:{port}/signin?x=1", null, true)]
        public async Task AChange_IsAcceptedOnlyFromTheWebUisOwnPages(string? Origin, string? Referer, string? FetchSite, bool Accepted)
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            await Visitor.Get("/signin");
            Visitor.Origin = Origin?.Replace("{port}", Host.Port.ToString());
            if (Referer != null)
            {
                Visitor.Headers["Referer"] = Referer.Replace("{port}", Host.Port.ToString());
            }
            if (FetchSite != null)
            {
                Visitor.Headers["Sec-Fetch-Site"] = FetchSite;
            }
            //the right password and the right token: only where the request comes from differs
            WebPage Result = await Visitor.Post("/signin", ("Password", WebTestHost.Password));
            if (Accepted)
            {
                Assert.That(Result.Location, Is.EqualTo("/"));
            }
            else
            {
                Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(Result.Text(), Does.Contain("Campertron accepts a change only from its own pages"));
                Assert.That(Result.Body, Does.Not.Contain("evil.example"), "nothing of the request is repeated");
                Assert.That(Result.SetCookies.Any(One => One.StartsWith("campertron-session", StringComparison.Ordinal)), Is.False);
            }
        }

        // ---- the Host check ----

        [TestCase("127.0.0.1:{port}", true)]
        [TestCase("localhost:{port}", true)]
        [TestCase("LOCALHOST", true)]
        [TestCase("192.168.50.7:{port}", true)]
        [TestCase("[::1]:{port}", true)]
        [TestCase("campbox:{port}", true)]
        [TestCase("CampBox.local", true)]
        [TestCase("evil.example", false)]
        [TestCase("evil.example:{port}", false)]
        [TestCase("campbox.evil.example", false)]
        [TestCase("localhost.evil.example", false)]
        [TestCase("127.0.0.1.evil.example", false)]
        [TestCase("campertron.example.com", false)]
        public async Task TheWebUi_AnswersToAddressesAndItsOwnNames_AndToNoOtherName(string HostHeader, bool Answered)
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            Visitor.Headers["Host"] = HostHeader.Replace("{port}", Host.Port.ToString());
            foreach (string Path_ in new[] { "/signin", "/healthz", "/assets/site.css" })
            {
                WebPage Result = await Visitor.Get(Path_);
                if (Answered)
                {
                    Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.OK), Path_);
                }
                else
                {
                    Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.BadRequest), Path_);
                    Assert.That(Result.Text(), Does.Contain("Campertron's web UI answers to this computer's IP address"));
                    Assert.That(Result.Body, Does.Not.Contain("evil").And.Not.Contain("example.com"), "the name is not repeated");
                }
            }
        }

        [Test]
        public async Task ANameOfAllowedHosts_IsAnswered()
        {
            await using WebTestHost Host = await WebTestHost.Start("allowedHosts:\n- campertron.example.com\n- nas.home.arpa\n", WithPassword: true);
            using Browser Visitor = Host.Browser();
            foreach ((string Name, HttpStatusCode Expected) in new[] { ("campertron.example.com", HttpStatusCode.OK), ("NAS.home.arpa.", HttpStatusCode.OK), ("other.example.com", HttpStatusCode.BadRequest) })
            {
                Visitor.Headers["Host"] = Name;
                Assert.That((await Visitor.Get("/signin")).Status, Is.EqualTo(Expected), Name);
            }
        }

        // ---- methods ----

        [TestCase("PUT")]
        [TestCase("DELETE")]
        [TestCase("PATCH")]
        [TestCase("OPTIONS")]
        [TestCase("TRACE")]
        [TestCase("PROPFIND")]
        public async Task OnlyGetHeadAndPost_AreAnswered(string Method)
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            foreach (string Path_ in new[] { "/", "/signin", "/signout", "/healthz", "/assets/site.css" })
            {
                WebPage Result = await Owner.Send(new HttpRequestMessage(new HttpMethod(Method), Path_));
                Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), Path_);
                Assert.That(Result.Header("Allow"), Is.EqualTo("GET, HEAD, POST"));
                Assert.That(Result.Header("Access-Control-Allow-Origin"), Is.Empty, "no CORS");
            }
            Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "nothing changed: still signed in");
        }

        [Test]
        public async Task APage_AnswersOnlyThroughItsOwnHandlers()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Owner = await Host.SignedIn();
            using Browser Visitor = Host.Browser();
            //a handler name the page does not have is the page's own handler for the method: its first step still runs
            Assert.That((await Visitor.Get("/setup?handler=Anything")).Location, Is.EqualTo("/signin"), "the setup is over once a password is set, whatever the address says");
            Assert.That((await Owner.Get("/signin?handler=Anything")).Location, Is.EqualTo("/"));
            Assert.That((await Owner.Get("/signout?handler=Anything")).Location, Is.EqualTo("/"));
            Assert.That((await Owner.Get("/?handler=Anything")).Text(), Does.Contain("CampertronWebTests").Or.Contain("testhost").Or.Contain("Campertron"), "the start page's own handler ran");

            //a method a page has no handler for is refused: the page is never shown without its code having run
            await Owner.Get("/account/password");
            WebPage Posted = await Owner.Post("/");
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That(Posted.Text(), Does.Contain("does not accept this kind of request"));
            Assert.That((await Owner.Get("/")).Status, Is.EqualTo(HttpStatusCode.OK), "still signed in");
        }

        [Test]
        public async Task AHead_IsAnsweredLikeAGet_WithoutABody()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            WebPage Result = await Visitor.Send(new HttpRequestMessage(HttpMethod.Head, "/healthz"));
            Assert.That(Result.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Result.Body, Is.Empty);
            Result = await Visitor.Send(new HttpRequestMessage(HttpMethod.Head, "/"));
            Assert.That(Result.Location, Is.EqualTo("/signin"));
        }

        // ---- a reverse proxy's forwarded headers ----

        private static void Forwarded(Browser Visitor, string Proto = "https", string HostName = "campertron.example.com", string For = "203.0.113.9")
        {
            Visitor.Headers["X-Forwarded-Proto"] = Proto;
            Visitor.Headers["X-Forwarded-Host"] = HostName;
            Visitor.Headers["X-Forwarded-For"] = For;
        }

        [TestCase(null)]
        [TestCase("allowedHosts:\n- campertron.example.com\n")]
        [TestCase("allowedHosts:\n- campertron.example.com\ntrustedProxies:\n- 10.0.0.1\n- 192.168.0.0/16\n- fd00::/8\n")]
        public async Task ForwardedHeaders_FromAnyoneButATrustedProxy_AreNotRead(string? WebYaml)
        {
            await using WebTestHost Host = await WebTestHost.Start(WebYaml, WithPassword: true);
            using Browser Visitor = Host.Browser();
            Forwarded(Visitor);
            await Visitor.Get("/signin");

            //the scheme: the request stays HTTP, so the forwarded name's HTTPS address is not the web UI's own
            Visitor.Origin = "https://campertron.example.com";
            Assert.That((await Visitor.Post("/signin", ("Password", WebTestHost.Password))).Status, Is.EqualTo(HttpStatusCode.Forbidden));

            //the address: a failed sign-in is the connection's, not the one the header names
            Visitor.Origin = Host.Root.GetLeftPart(UriPartial.Authority);
            await Visitor.Post("/signin", ("Password", "wrong password"));
            Assert.That(Host.Lines.Single(Text => Text.Contains("failed sign-in")), Does.Contain("from 127.0.0.1 ").And.Not.Contain("203.0.113.9"));

            //the cookie: not Secure, because the request is not HTTPS
            WebPage Done = await Visitor.Post("/signin", ("Password", WebTestHost.Password));
            Assert.That(Done.Location, Is.EqualTo("/"));
            Assert.That(Done.SetCookies.Single(One => One.StartsWith("campertron-session-0=", StringComparison.Ordinal)), Does.Not.Contain("secure"));

            //the host name: a forwarded name does not replace the Host header's
            using Browser Other = Host.Browser();
            Forwarded(Other, HostName: "evil.example");
            Assert.That((await Other.Get("/signin")).Status, Is.EqualTo(HttpStatusCode.OK), "the forwarded name was not read at all");
        }

        [TestCase("127.0.0.1")]
        [TestCase("127.0.0.0/8")]
        public async Task ATrustedProxy_SaysTheBrowsersSchemeNameAndAddress(string Proxy)
        {
            await using WebTestHost Host = await WebTestHost.Start("allowedHosts:\n- campertron.example.com\ntrustedProxies:\n- " + Proxy + "\n", WithPassword: true);
            using Browser Visitor = Host.Browser();
            Forwarded(Visitor);
            await Visitor.Get("/signin");
            Visitor.Origin = "https://campertron.example.com";

            await Visitor.Post("/signin", ("Password", "wrong password"));
            Assert.That(Host.Lines.Single(Text => Text.Contains("failed sign-in")), Does.Contain("from 203.0.113.9 "));

            WebPage Done = await Visitor.Post("/signin", ("Password", WebTestHost.Password));
            Assert.That(Done.Location, Is.EqualTo("/"), "the proxy's HTTPS address is the web UI's own now");
            Assert.That(Done.SetCookies.Single(One => One.StartsWith("campertron-session-0=", StringComparison.Ordinal)), Does.Contain("; secure"), "HTTPS through the proxy: a Secure cookie");
            Assert.That(Done.Header("Strict-Transport-Security"), Is.Empty, "HSTS is the proxy's to send");

            //the plain HTTP address is not the web UI's own through the proxy
            using Browser Plain = Host.Browser();
            Forwarded(Plain);
            await Plain.Get("/signin");
            Plain.Origin = "http://campertron.example.com";
            Assert.That((await Plain.Post("/signin", ("Password", WebTestHost.Password))).Status, Is.EqualTo(HttpStatusCode.Forbidden));

            //a name the proxy forwards must still be one the web UI answers to
            using Browser Other = Host.Browser();
            Forwarded(Other, HostName: "evil.example");
            Assert.That((await Other.Get("/signin")).Status, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        // ---- addresses that name a file ----

        // one raw request, because HttpClient tidies ".." away before it sends
        private static async Task<string> Raw(int Port, string Target)
        {
            using var Client = new TcpClient();
            await Client.ConnectAsync(IPAddress.Loopback, Port);
            using NetworkStream Stream = Client.GetStream();
            byte[] Request = Encoding.ASCII.GetBytes("GET " + Target + " HTTP/1.1\r\nHost: 127.0.0.1:" + Port + "\r\nConnection: close\r\n\r\n");
            await Stream.WriteAsync(Request);
            using var Reader = new StreamReader(Stream, Encoding.UTF8);
            return await Reader.ReadToEndAsync();
        }

        [Test]
        public async Task NoAddress_ReachesAFileOfAFolder_SignedInOrNot()
        {
            await using WebTestHost Host = await WebTestHost.Start("passwordProtection: false\n# MARKER-IN-WEB-YAML\n");
            File.WriteAllText(Path.Join(Host.Folder, "General.yaml"), "outputTo: Console # MARKER-IN-GENERAL\n");
            File.WriteAllText(Path.Join(Host.Folder, "Email.yaml"), "smtpPassword: MARKER-SMTP-PASSWORD\n");
            File.WriteAllText(Path.Join(Host.Folder, "Web.password"), "MARKER-PASSWORD-FILE\n");
            File.WriteAllText(Path.Join(Host.Folder, "RecreationDotOrg.db"), "MARKER-DATABASE");
            string Key = Path.GetFileName(Directory.GetFiles(Path.Join(Host.Folder, "Web.keys")).FirstOrDefault() ?? "key-none.xml");
            string Folder = Host.Folder.TrimStart('/');
            string[] Targets =
            {
                "/Web.password", "/General.yaml", "/Email.yaml", "/Web.yaml", "/RecreationDotOrg.db", "/Web.keys/" + Key, "/Web.keys/",
                "/assets/Web.password", "/assets/../Web.password", "/assets/..%2fWeb.password", "/assets/%2e%2e/Web.password", "/assets/%2e%2e%2fGeneral.yaml",
                "/assets/site.css/../../General.yaml", "/assets/....//General.yaml", "/assets/..\\General.yaml", "/assets/%5c..%5cGeneral.yaml",
                "/" + Folder + "/General.yaml", "//" + Folder + "/General.yaml", "/assets//" + Folder + "/Email.yaml", "/assets/%2f" + Folder.Replace("/", "%2f") + "%2fEmail.yaml",
                "/../../../../etc/passwd", "/assets/../../../../etc/passwd", "/etc/passwd", "/CampertronWeb.dll", "/CampertronConsole.dll", "/CampertronConsole.runtimeconfig.json",
                "/appsettings.json", "/wwwroot/site.css", "/_content/CampertronWeb/site.css", "/Pages/Index.cshtml", "/.git/config", "/assets/", "/assets"
            };
            foreach (string Target in Targets)
            {
                string Answer = await Raw(Host.Port, Target);
                Assert.That(Answer, Does.StartWith("HTTP/1.1 404 ").Or.StartWith("HTTP/1.1 400 "), Target);
                Assert.That(Answer, Does.Not.Contain("MARKER").And.Not.Contain("root:").And.Not.Contain("<key ").And.Not.Contain("runtimeOptions"), Target);
            }
            //the one file the web UI serves is its own stylesheet, from memory
            Assert.That(await Raw(Host.Port, "/assets/site.css"), Does.StartWith("HTTP/1.1 200 OK").And.Contain("color-scheme"));

            //with a password, before the sign-in: the same addresses lead to the sign-in or nowhere, and show nothing
            await using WebTestHost Protected = await WebTestHost.Start(WithPassword: true);
            File.WriteAllText(Path.Join(Protected.Folder, "General.yaml"), "outputTo: Console # MARKER-IN-GENERAL\n");
            foreach (string Target in Targets)
            {
                string Answer = await Raw(Protected.Port, Target);
                Assert.That(Answer, Does.StartWith("HTTP/1.1 302 ").Or.StartWith("HTTP/1.1 404 ").Or.StartWith("HTTP/1.1 400 "), Target);
                Assert.That(Answer, Does.Not.Contain("MARKER").And.Not.Contain("root:").And.Not.Contain("pbkdf2").And.Not.Contain("<key "), Target);
            }
        }
    }
}
