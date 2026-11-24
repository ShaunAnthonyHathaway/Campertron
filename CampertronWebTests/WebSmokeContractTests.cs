using System.Net;
using System.Text.RegularExpressions;
using CampertronLibrary.function.WebAccess;
using CampertronWeb;

namespace CampertronWebTests
{
    // Web mode's release smoke test (v0.3.0's PR 12, D102; .github/release-policy/web-smoke.py) runs a released
    // CampertronConsole --web and compares what it says and serves with words it holds as constants. These tests read
    // those constants and compare them with the web UI's own, so a change of the web UI that the smoke test would refuse
    // fails here, in every pull request's CI, and not first in a release run (whose pull-request path filter a change of
    // CampertronWeb alone does not start).
    public class WebSmokeContractTests
    {
        private static string? HarnessText;

        private static string Harness()
        {
            if (HarnessText == null)
            {
                string Root = TestContext.CurrentContext.TestDirectory;
                while (!File.Exists(Path.Join(Root, "Campertron.sln")))
                {
                    Root = Path.GetDirectoryName(Root) ?? throw new AssertionException("the repository was not found above the test folder");
                }
                HarnessText = File.ReadAllText(Path.Join(Root, ".github", "release-policy", "web-smoke.py"));
            }
            return HarnessText;
        }

        // NAME = "text" on a line of its own: a Python string without escapes
        private static string Constant(string Name)
        {
            Match Found = Regex.Match(Harness(), "^" + Name + " = \"([^\"\\\\]*)\"\\r?$", RegexOptions.Multiline);
            Assert.That(Found.Success, Is.True, "web-smoke.py has no constant " + Name);
            return Found.Groups[1].Value;
        }

        private static List<string> LinesOf(Action<WebConsoleLines> Say)
        {
            var Said = new List<string>();
            var Settings = new WebHostSettings { Line = Said.Add, Warning = Said.Add, Error = (Text, Details) => Said.Add(Text) };
            Say(new WebConsoleLines(Settings));
            return Said;
        }

        [Test]
        public void TheHealthAnswer_TheHealthCheck_AndThePort_AreTheOnesTheSmokeTestExpects()
        {
            Assert.That(Constant("HEALTH_PATH"), Is.EqualTo(CampertronWebHost.HealthPath));
            Assert.That(Constant("HEALTH_TEXT"), Is.EqualTo(CampertronWebHost.HealthText));
            Assert.That(Constant("HEALTHY_LINE"), Is.EqualTo(WebHealthCheck.Healthy));
            Assert.That(Constant("UNHEALTHY_LINE"), Is.EqualTo(WebHealthCheck.Unhealthy));
            Match Port = Regex.Match(Harness(), "^CONTAINER_PORT = (\\d+)\\r?$", RegexOptions.Multiline);
            Assert.That(Port.Success && int.Parse(Port.Groups[1].Value) == WebArguments.DefaultPort, Is.True, "the container's port is web mode's default");
        }

        [Test]
        public void TheContentSecurityPolicy_IsTheOneTheSmokeTestExpects()
        {
            Assert.That(Constant("CONTENT_SECURITY_POLICY"), Is.EqualTo(WebSecurity.ContentSecurityPolicy));
        }

        [Test]
        public void TheConsoleLines_AreTheOnesTheSmokeTestExpects()
        {
            string Loopback = Constant("LISTENING_LOOPBACK"), Every = Constant("LISTENING_EVERY_INTERFACE");
            string Shown(string Template, string Address, int Port) => Template.Replace("{address}", Address).Replace("{port}", Port.ToString());
            Assert.That(LinesOf(Lines => Lines.Listening(new[] { new IPEndPoint(IPAddress.Loopback, 50123) }, true)),
                Is.EqualTo(new[] { Shown(Loopback, "127.0.0.1", 50123) }));
            Assert.That(LinesOf(Lines => Lines.Listening(new[] { new IPEndPoint(IPAddress.IPv6Loopback, 50124) }, true)),
                Is.EqualTo(new[] { Shown(Loopback, "[::1]", 50124) }));
            Assert.That(LinesOf(Lines => Lines.Listening(new[] { new IPEndPoint(IPAddress.IPv6Any, 8790) }, true)),
                Is.EqualTo(new[] { Shown(Every, "[::]", 8790) }), "a container's web UI listens on every interface of the container");
            Assert.That(LinesOf(Lines => Lines.Listening(new[] { new IPEndPoint(IPAddress.Any, 8790) }, true)),
                Is.EqualTo(new[] { Shown(Every, "0.0.0.0", 8790) }));
            Assert.That(LinesOf(Lines => Lines.SetupCode("ABCD-EFGH-JKMN")),
                Is.EqualTo(new[] { Constant("SETUP_CODE_INTRO"), Constant("SETUP_CODE_LINE").Replace("{code}", "ABCD-EFGH-JKMN") }));
            Assert.That(LinesOf(Lines => Lines.Stopped()), Is.EqualTo(new[] { Constant("STOPPED_LINE") }));
        }

        [Test]
        public void TheSetupCode_HasTheShapeTheSmokeTestFindsAndRemoves()
        {
            string Alphabet = Constant("SETUP_CODE_ALPHABET");
            Assert.That(Alphabet, Is.EqualTo(WebSetupCode.Alphabet));
            string Group = "[" + Regex.Escape(Alphabet) + "]{4}";
            for (int i = 0; i < 20; i++)
            {
                Assert.That(new WebSetupCode().New(), Does.Match("^" + Group + "-" + Group + "-" + Group + "$"));
            }
        }

        [Test]
        public void AWrongListen_IsRefusedWithTheWordsTheSmokeTestExpects()
        {
            WebArguments Refused = WebArguments.Parse(new[] { "--web", "--listen", "smoke.invalid" });
            Assert.That(Constant("LISTEN_PROBLEM"), Is.EqualTo("Error: " + Refused.Error));
            Assert.That(WebArguments.Usage, Does.StartWith(Constant("USAGE_LINE")));
        }

        [Test]
        public async Task TheSetupPage_AndTheFilesWebModeWrites_AreTheOnesTheSmokeTestExpects()
        {
            Assert.That(Regex.Match(Harness(), "^OWN_FILES = \\(\"([^\"]+)\",\\)\\r?$", RegexOptions.Multiline).Groups[1].Value, Is.EqualTo(WebKeyRepository.FolderName));
            await using WebTestHost Host = await WebTestHost.Start();
            using Browser Visitor = Host.Browser();
            WebPage Setup = await Visitor.Get("/setup");
            Assert.That(Setup.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Setup.Body, Does.Contain(Constant("SETUP_PAGE_TITLE")));
            foreach (string Path_ in new[] { "/", "/settings/general", "/monitoring", "/guided-setup", "/Web.password" })
            {
                WebPage Page = await Visitor.Get(Path_);
                Assert.That((Page.Status, Page.Location), Is.EqualTo((HttpStatusCode.Redirect, "/setup")), Path_);
            }
        }
    }
}
