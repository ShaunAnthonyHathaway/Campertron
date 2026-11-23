using System.Net;
using CampertronLibrary.function.WebAccess;

namespace CampertronUnitTests.function.WebAccess
{
    // Web mode's command line (decisions D80, D85 and D92; business-rules-settings §31): read only when one of its four
    // arguments is given, then strictly
    public class WebArgumentsTests
    {
        private static string[] Split(string Line) => Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        [TestCase("")]
        [TestCase("--version")]
        [TestCase("--help")]
        [TestCase("web")]
        [TestCase("-web")]
        [TestCase("--Web")]
        [TestCase("--webui")]
        [TestCase("--porter 1")]
        [TestCase("--listening")]
        [TestCase("anything at all")]
        public void ACommandLineWithoutAWebArgument_IsNotWebModes_SoEveryOtherStartIsUnchanged(string Line)
        {
            Assert.That(WebArguments.IsRequested(Split(Line)), Is.False);
            WebArguments Parsed = WebArguments.Parse(Split(Line));
            Assert.That((Parsed.Command, Parsed.Error), Is.EqualTo((WebCommand.None, (string?)null)));
            Assert.That(WebArguments.IsRequested(null), Is.False);
            Assert.That(WebArguments.Parse(null).Command, Is.EqualTo(WebCommand.None));
        }

        [Test]
        public void Web_Alone_IsEveryInterfaceOnPort8790()
        {
            WebArguments Parsed = WebArguments.Parse(new[] { "--web" });
            Assert.That(Parsed.Error, Is.Null);
            Assert.That((Parsed.Command, Parsed.Listen, Parsed.ListenLocalhost, Parsed.ListenGiven, Parsed.Port, Parsed.PortGiven),
                Is.EqualTo((WebCommand.Web, (IPAddress?)null, false, false, 8790, false)));
            Assert.That(WebArguments.DefaultPort, Is.EqualTo(8790));
        }

        [TestCase("--web --listen 127.0.0.1", "127.0.0.1", false, 8790)]
        [TestCase("--web --listen=127.0.0.1 --port=8080", "127.0.0.1", false, 8080)]
        [TestCase("--port 1 --listen 0.0.0.0 --web", "0.0.0.0", false, 1)]
        [TestCase("--web --port 65535", null, false, 65535)]
        [TestCase("--web --listen ::1", "::1", false, 8790)]
        [TestCase("--web --listen ::", "::", false, 8790)]
        [TestCase("--web --listen 2001:db8::7", "2001:db8::7", false, 8790)]
        [TestCase("--web --listen 192.168.1.20 --port 80", "192.168.1.20", false, 80)]
        [TestCase("--web --listen localhost", null, true, 8790)]
        [TestCase("--healthcheck", null, false, 8790)]
        [TestCase("--healthcheck --port 9000 --listen 10.0.0.5", "10.0.0.5", false, 9000)]
        public void AValidCommandLine_IsRead(string Line, string? Listen, bool Localhost, int Port)
        {
            WebArguments Parsed = WebArguments.Parse(Split(Line));
            Assert.That(Parsed.Error, Is.Null);
            Assert.That(Parsed.Command, Is.EqualTo(Line.Contains("--healthcheck") ? WebCommand.HealthCheck : WebCommand.Web));
            Assert.That(Parsed.Listen, Is.EqualTo(Listen == null ? null : IPAddress.Parse(Listen)));
            Assert.That(Parsed.ListenLocalhost, Is.EqualTo(Localhost));
            Assert.That(Parsed.Port, Is.EqualTo(Port));
        }

        private const string PortProblem = "--port needs a whole number from 1 to 65535.";
        private const string ListenProblem = "--listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost.";
        private const string Unknown = "an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port.";
        private const string Alone = "--listen and --port are options of --web (the web UI) and of --healthcheck; without one of them nothing was started.";

        [TestCase("--web --port", PortProblem)]
        [TestCase("--web --port=", PortProblem)]
        [TestCase("--web --port 0", PortProblem)]
        [TestCase("--web --port 65536", PortProblem)]
        [TestCase("--web --port -1", PortProblem)]
        [TestCase("--web --port 08790", PortProblem)]
        [TestCase("--web --port +8790", PortProblem)]
        [TestCase("--web --port 8790.0", PortProblem)]
        [TestCase("--web --port 0x50", PortProblem)]
        [TestCase("--web --port 99999999999", PortProblem)]
        [TestCase("--web --port ٨٧٩٠", PortProblem)]
        [TestCase("--web --port --listen 127.0.0.1", PortProblem)]
        [TestCase("--web --listen", ListenProblem)]
        [TestCase("--web --listen=", ListenProblem)]
        [TestCase("--web --listen example.com", ListenProblem)]
        [TestCase("--web --listen LOCALHOST", ListenProblem)]
        [TestCase("--web --listen *", ListenProblem)]
        [TestCase("--web --listen +", ListenProblem)]
        [TestCase("--web --listen 127.1", ListenProblem)]
        [TestCase("--web --listen 1", ListenProblem)]
        [TestCase("--web --listen 0x7f000001", ListenProblem)]
        [TestCase("--web --listen 010.0.0.1", ListenProblem)]
        [TestCase("--web --listen 256.0.0.1", ListenProblem)]
        [TestCase("--web --listen 127.0.0.1:8790", ListenProblem)]
        [TestCase("--web --listen http://127.0.0.1", ListenProblem)]
        [TestCase("--web --listen [::1]", ListenProblem)]
        [TestCase("--web --listen fe80::1%eth0", ListenProblem)]
        [TestCase("--web --listen 127.0.0.1,10.0.0.1", ListenProblem)]
        [TestCase("--web --listen --port 8790", ListenProblem)]
        [TestCase("--web --verbose", Unknown)]
        [TestCase("--web --version", Unknown)]
        [TestCase("--web 8790", Unknown)]
        [TestCase("--web --listen 127.0.0.1 extra", Unknown)]
        [TestCase("--healthcheck now", Unknown)]
        [TestCase("--web --PORT 8790", Unknown)]
        [TestCase("--port 8790", Alone)]
        [TestCase("--listen 127.0.0.1", Alone)]
        [TestCase("--listen=127.0.0.1 --port=1", Alone)]
        [TestCase("--web --healthcheck", "--web starts the web UI and --healthcheck asks a running one; use one of them.")]
        [TestCase("--web --web", "--web was given more than once.")]
        [TestCase("--healthcheck --healthcheck", "--healthcheck was given more than once.")]
        [TestCase("--web --port 1 --port 2", "--port was given more than once.")]
        [TestCase("--web --listen ::1 --listen=::1", "--listen was given more than once.")]
        public void AWrongCommandLine_IsRefusedInFixedWords_ThatNeverRepeatItsValues(string Line, string Problem)
        {
            Assert.That(WebArguments.IsRequested(Split(Line)), Is.True);
            WebArguments Parsed = WebArguments.Parse(Split(Line));
            Assert.That(Parsed.Error, Is.EqualTo(Problem));
            Assert.That(Parsed.Command, Is.EqualTo(WebCommand.None), "nothing is started");
        }

        [TestCase("--healthcheck", "127.0.0.1")]
        [TestCase("--healthcheck --listen 0.0.0.0", "127.0.0.1")]
        [TestCase("--healthcheck --listen ::", "::1")]
        [TestCase("--healthcheck --listen localhost", "127.0.0.1")]
        [TestCase("--healthcheck --listen 192.168.1.20", "192.168.1.20")]
        [TestCase("--healthcheck --listen ::1", "::1")]
        public void TheHealthCheck_AsksTheLoopbackAddress_UnlessListenNamesOneAddress(string Line, string Asked)
        {
            Assert.That(WebArguments.Parse(Split(Line)).HealthCheckAddress(), Is.EqualTo(IPAddress.Parse(Asked)));
        }
    }
}
