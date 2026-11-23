using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CampertronWebTests
{
    // Web mode's start and stop as the console runs it (WebMode.Run), its console lines and its refusals (D80, D81, D92)
    public class WebStartTests
    {
        private sealed class Running : IAsyncDisposable
        {
            internal WebTestHost Host = new WebTestHost();
            internal WebHostSettings Settings = new WebHostSettings();
            internal CancellationTokenSource Stop = new CancellationTokenSource();
            internal Task<int> Run = Task.FromResult(-1);
            internal WebApplication? App;
            internal string Folder = string.Empty;
            internal List<string> Lines => Host.Lines;
            internal List<string> Errors => Host.Errors;

            public async ValueTask DisposeAsync()
            {
                Stop.Cancel();
                await Run;
                Directory.Delete(Folder, true);
            }
        }

        // runs web mode on a background thread until the test stops it; returns once it listens or has ended
        private static async Task<Running> Start(string[] Args, string? WebYaml = null, Action<string>? Prepare = null, int Port = 0)
        {
            var Running = new Running { Folder = WebTestHost.NewFolder() };
            if (WebYaml != null)
            {
                File.WriteAllText(Path.Join(Running.Folder, "Web.yaml"), WebYaml);
            }
            Prepare?.Invoke(Running.Folder);
            typeof(WebTestHost).GetProperty(nameof(WebTestHost.Folder), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(Running.Host, Running.Folder);
            Running.Settings = Running.Host.NewSettings();
            Running.Settings.Port = Port;
            var Listening = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Running.Run = Task.Run(() =>
            {
                try
                {
                    return WebMode.Run(Running.Settings, WebArguments.Parse(Args), Running.Stop.Token, App => { Running.App = App; Listening.TrySetResult(true); });
                }
                finally
                {
                    Listening.TrySetResult(false);
                }
            });
            await Listening.Task;
            return Running;
        }

        // a port nothing listens on (localhost can't be asked for "any free port")
        internal static int FreePort()
        {
            using var Probe = new TcpListener(IPAddress.Loopback, 0);
            Probe.Start();
            return ((IPEndPoint)Probe.LocalEndpoint).Port;
        }

        [Test]
        public async Task TheStart_SaysWhereItListens_AndTheSetupCode_AndTheStopSaysSo()
        {
            Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" });
            int Port = CampertronWebHost.EndPoints(Web.App!).Single().Port;
            Web.Stop.Cancel();
            Assert.That(await Web.Run, Is.Zero);
            Assert.That(Web.Lines, Has.Count.EqualTo(5));
            Assert.That(Web.Lines[0], Does.Match(@"^\S+ \S+ \| \.NET \S+ \(\w+\) \| settings ").And.EndWith(Web.Folder + " | web UI"));
            Assert.That(Web.Lines[1], Is.EqualTo("Web UI: listening on http://127.0.0.1:" + Port + " (this computer only); open http://127.0.0.1:" + Port + "/ in a browser. A password is required."));
            Assert.That(Web.Lines[2], Is.EqualTo("Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for 30 minutes, for one use):"));
            Assert.That(Web.Lines[3], Does.Match(@"^Web UI setup code: >>> [A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4}-[A-HJKMNP-TV-Z2-9]{4} <<<$"));
            Assert.That(Web.Lines[4], Is.EqualTo("Web UI: stopped."));
            Assert.That(Web.Errors, Is.Empty);
            Assert.That(Web.Host.Warnings, Is.Empty);
            //the start created the keys folder and nothing else: no Web.yaml, no General.yaml, no database, no example search
            Assert.That(Directory.GetFileSystemEntries(Web.Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Web.keys" }));
            Directory.Delete(Web.Folder, true);
        }

        [Test]
        public async Task TheCodeOfTheStart_SetsThePassword_AndTheNextStartShowsNone()
        {
            await using Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" });
            string Code = Web.Lines[3].Substring("Web UI setup code: >>> ".Length, 14);
            using var Owner = new Browser(new Uri("http://127.0.0.1:" + CampertronWebHost.EndPoints(Web.App!).Single().Port + "/"));
            await Owner.Get("/setup");
            Assert.That((await Owner.Post("/setup", ("Code", Code), ("Password", WebTestHost.Password), ("Repeat", WebTestHost.Password))).Location, Is.EqualTo("/signin?notice=password-set"));

            var Again = new WebTestHost();
            WebHostSettings Settings = Web.Host.NewSettings();
            var Second = new List<string>();
            Settings.Line = Second.Add;
            using var Stop = new CancellationTokenSource();
            Task<int> Run = Task.Run(() => WebMode.Run(Settings, WebArguments.Parse(new[] { "--web", "--listen", "127.0.0.1" }), Stop.Token, App => Stop.Cancel()));
            Assert.That(await Run, Is.Zero);
            Assert.That(Second, Has.None.Contains("setup code"), "a password is set: no code");
            Assert.That(Second, Has.Count.EqualTo(3));
            await Again.DisposeAsync();
        }

        [Test]
        public async Task WithoutListen_WebYamlDecides_AndTheDefaultIsEveryInterface()
        {
            await using (Running Default = await Start(new[] { "--web" }))
            {
                IPEndPoint Any = CampertronWebHost.EndPoints(Default.App!).Single();
                Assert.That(Any.Address.Equals(IPAddress.IPv6Any) || Any.Address.Equals(IPAddress.Any), Is.True, "reachable from the network by default (D81)");
                Assert.That(Default.Lines[1], Does.Contain("(every network interface of this computer); open http://<this computer's address>:" + Any.Port + "/ in a browser. A password is required."));
            }
            await using (Running Local = await Start(new[] { "--web" }, "networkListening: false\n", Port: FreePort()))
            {
                Assert.That(CampertronWebHost.EndPoints(Local.App!).Select(EndPoint => EndPoint.Address), Is.EqualTo(new[] { IPAddress.Loopback }), "this computer only");
                Assert.That(Local.Lines.Skip(1).First(), Does.Contain("(this computer only)"));
            }
            await using (Running Overridden = await Start(new[] { "--web", "--listen", "0.0.0.0" }, "networkListening: false\n"))
            {
                Assert.That(CampertronWebHost.EndPoints(Overridden.App!).Single().Address, Is.EqualTo(IPAddress.Any), "--listen decides for its process");
            }
            await using Running Named = await Start(new[] { "--web", "--listen", "localhost" }, Port: FreePort());
            Assert.That(CampertronWebHost.EndPoints(Named.App!).Select(EndPoint => EndPoint.Address), Is.EqualTo(new[] { IPAddress.Loopback }));
            //localhost is both loopback addresses where the computer has both
            using var Visitor = new Browser(new Uri("http://127.0.0.1:" + Named.Settings.Port + "/"));
            Assert.That((await Visitor.Get("/healthz")).Body, Is.EqualTo("ok"));
        }

        [Test]
        public async Task WithoutPasswordProtection_TheStartSaysSoOnce_AndShowsNoCode()
        {
            await using Running Open = await Start(new[] { "--web", "--listen", "127.0.0.1" }, "passwordProtection: false\n");
            Assert.That(Open.Lines, Has.Count.EqualTo(2));
            Assert.That(Open.Lines[1], Does.EndWith("No password is required (passwordProtection: false in Web.yaml)."));
            Assert.That(Open.Host.Warnings, Is.Empty, "a fact of the start line, not a warning that comes back (D81)");
        }

        [Test]
        public async Task AWebYamlWithAnError_RefusesTheStart_InFixedWords_AndCreatesNothing()
        {
            const string Bad = "passwordProtection: false\nallowedHosts:\n- http://secret-name.example:8790/\ntrustedProxies:\n- 10.9.8.7/8\n";
            Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" }, Bad);
            Assert.That(await Web.Run, Is.EqualTo(1));
            Assert.That(Web.App, Is.Null, "nothing listened");
            Assert.That(Web.Errors[0], Is.EqualTo("web mode can't start: Web.yaml can't be used."));
            Assert.That(Web.Errors, Does.Contain("allowedHosts item 1 must be a host name such as campertron.example.com: no port, no http://, no wildcard. An IP address needs no entry. (line 3)"));
            Assert.That(Web.Errors, Does.Contain("trustedProxies item 1 must be an IP address such as 172.18.0.2, or a network such as 172.18.0.0/16 (the network's own address and its prefix length). (line 5)"));
            Assert.That(Web.Errors.Last(), Is.EqualTo("The web UI was not started."));
            Assert.That(string.Join("\n", Web.Errors), Does.Not.Contain("secret-name").And.Not.Contain("10.9.8.7"), "no value of the file");
            Assert.That(Directory.GetFileSystemEntries(Web.Folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Web.yaml" }), "a refused start creates nothing");
            Assert.That(File.ReadAllText(Path.Join(Web.Folder, "Web.yaml")), Is.EqualTo(Bad));
            Directory.Delete(Web.Folder, true);
        }

        [TestCase("not a password record\n")]
        [TestCase("$pbkdf2-sha256$i=1000$c2FsdHNhbHRzYWx0c2FsdA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
        [TestCase("")]
        public async Task APasswordFileThatCantBeUsed_RefusesTheStart_AndIsLeftAsItIs(string Content)
        {
            Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" }, Prepare: Folder => File.WriteAllText(Path.Join(Folder, "Web.password"), Content));
            Assert.That(await Web.Run, Is.EqualTo(1));
            Assert.That(Web.App, Is.Null);
            Assert.That(Web.Errors[0], Is.EqualTo("web mode can't start: the web UI's password file can't be used."));
            Assert.That(Web.Errors, Does.Contain("To set a new password, delete the file and start web mode again: it then shows a one-time setup code."));
            Assert.That(File.ReadAllText(Path.Join(Web.Folder, "Web.password")), Is.EqualTo(Content), "never written over, never removed");
            Assert.That(Directory.Exists(Path.Join(Web.Folder, "Web.keys")), Is.False);
            Assert.That(Web.Lines, Has.None.Contains("setup code"), "the web UI never runs open, or with a new code, because its password got lost");
            Directory.Delete(Web.Folder, true);
        }

        [Test]
        public async Task WithoutPasswordProtection_AnUnusablePasswordFileIsNotAskedAbout()
        {
            await using Running Open = await Start(new[] { "--web", "--listen", "127.0.0.1" }, "passwordProtection: false\n", Folder => File.WriteAllText(Path.Join(Folder, "Web.password"), "garbage"));
            Assert.That(Open.App, Is.Not.Null);
            Assert.That(Open.Errors, Is.Empty);
        }

        [Test]
        public async Task KeysThatCantBeKept_RefuseTheStart()
        {
            Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" }, Prepare: Folder => File.WriteAllText(Path.Join(Folder, "Web.keys"), "a file where the folder should be"));
            Assert.That(await Web.Run, Is.EqualTo(1));
            Assert.That(Web.App, Is.Null, "never a web UI with keys in memory only, or a fixed key");
            Assert.That(Web.Errors[0], Is.EqualTo("web mode can't start: Web.keys in the settings folder is a link or a file, not a folder."));
            Assert.That(Web.Errors.Last(), Is.EqualTo("The web UI was not started."));
            Directory.Delete(Web.Folder, true);
        }

        [Test]
        public async Task APortThatIsTaken_RefusesTheStart()
        {
            using var Taken = new TcpListener(IPAddress.Loopback, 0);
            Taken.Start();
            int Port = ((IPEndPoint)Taken.LocalEndpoint).Port;
            Running Web = await Start(new[] { "--web", "--listen", "127.0.0.1" }, Port: Port);
            Assert.That(await Web.Run, Is.EqualTo(1));
            Assert.That(Web.Errors[0], Is.EqualTo("web mode can't start: it can't listen on port " + Port + " at the address it was given."));
            Assert.That(Web.Lines, Has.None.Contains("listening"));
            Assert.That(Web.Lines, Has.None.Contains("setup code"));
            Directory.Delete(Web.Folder, true);
        }

        [Test]
        public async Task AnAddressThisComputerDoesNotHave_RefusesTheStart_InTheSameFixedWords()
        {
            //192.0.2.0/24 is reserved for documentation: no computer has an address of it
            Running Web = await Start(new[] { "--web", "--listen", "192.0.2.55" }, Port: FreePort());
            Assert.That(await Web.Run, Is.EqualTo(1));
            Assert.That(Web.App, Is.Null);
            Assert.That(Web.Errors, Has.Count.EqualTo(3));
            Assert.That(Web.Errors[0], Is.EqualTo("web mode can't start: it can't listen on port " + Web.Settings.Port + " at the address it was given."));
            Assert.That(Web.Errors[1], Does.StartWith("Another program may be using the port, this computer may have no such address, or the port needs more rights"));
            Assert.That(Web.Errors[2], Is.EqualTo("The web UI was not started."));
            Assert.That(string.Join("\n", Web.Errors), Does.Not.Contain("SocketException").And.Not.Contain("   at "), "no exception text");
            Directory.Delete(Web.Folder, true);
        }

        // ---- ASP.NET Core's own output is off ----

        [Test]
        public async Task TheHost_HasNoLoggingProvider_AndWritesNothingToTheConsoleItself()
        {
            TextWriter Out = Console.Out, Error = Console.Error;
            var Written = new StringWriter();
            Console.SetOut(Written);
            Console.SetError(Written);
            try
            {
                await using WebTestHost Host = await WebTestHost.Start(WithPassword: true, Configure: Settings => Settings.MapFailingPage = true);
                Assert.That(Host.App.Services.GetServices<ILoggerProvider>(), Is.Empty, "no logging provider is registered");
                ILogger Logger = Host.App.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.Hosting.Lifetime");
                Assert.That(Enum.GetValues<LogLevel>().Where(Level => Level != LogLevel.None).Any(Logger.IsEnabled), Is.False, "nothing is logged at any level");
                Logger.LogCritical("MARKER-LOGGED");

                using Browser Owner = await Host.SignedIn();
                await Owner.Get("/test/fail?MARKER-QUERY=1");
                await Owner.Get("/nothing-here-MARKER-PATH");
                using Browser Visitor = Host.Browser();
                await Visitor.Get("/signin");
                await Visitor.Post("/signin", ("Password", "MARKER-WRONG-PASSWORD"));
                Visitor.Headers["Host"] = "marker-host.example";
                await Visitor.Get("/");
                await Host.App.StopAsync();
                Assert.That(Written.ToString(), Is.Empty, "the host wrote to the console by itself");
                Assert.That(string.Join("\n", Host.Lines.Concat(Host.Warnings).Concat(Host.Errors)), Does.Not.Contain("MARKER").And.Not.Contain("marker"),
                    "nothing of a request reaches Campertron's own lines");
                Assert.That(Host.Lines.Concat(Host.Warnings), Is.EquivalentTo(new[]
                {
                    "Web UI: a request failed unexpectedly (InvalidOperationException); the browser was shown an error page.",
                    "Web UI: failed sign-in from 127.0.0.1 (1 failed sign-in from it since the web UI started)."
                }));
            }
            finally
            {
                Console.SetOut(Out);
                Console.SetError(Error);
            }
        }

        [Test]
        public async Task TheHost_TakesNothingFromTheEnvironment()
        {
            string?[] Before = { Environment.GetEnvironmentVariable("ASPNETCORE_URLS"), Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") };
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:1");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", "8080");
            try
            {
                await using WebTestHost Host = await WebTestHost.Start("passwordProtection: false\n", Configure: Settings => Settings.MapFailingPage = true);
                Assert.That(Host.App.Environment.EnvironmentName, Is.EqualTo("Production"));
                Assert.That(Host.Port, Is.Not.EqualTo(1).And.Not.EqualTo(8080));
                using Browser Visitor = Host.Browser();
                WebPage Failed = await Visitor.Get("/test/fail");
                Assert.That(Failed.Body, Does.Not.Contain("InvalidOperationException").And.Not.Contain("secret detail"), "no developer exception page, whatever the environment says");
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", Before[0]);
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Before[1]);
                Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", Before[2]);
            }
        }

        // ---- the health check ----

        [Test]
        public async Task TheHealthAnswer_IsTwoLetters_ForEveryone_AndSaysNothingElse()
        {
            await using WebTestHost Host = await WebTestHost.Start(WithPassword: true);
            using Browser Visitor = Host.Browser();
            WebPage Health = await Visitor.Get("/healthz");
            Assert.That(Health.Status, Is.EqualTo(HttpStatusCode.OK), "no sign-in: a container's health check has no password");
            Assert.That(Health.Body, Is.EqualTo("ok"));
            Assert.That(Health.Header("Content-Type"), Is.EqualTo("text/plain; charset=utf-8"));
            Assert.That(Health.Header("Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(Health.SetCookies, Is.Empty);
            Assert.That((await Visitor.Get("/healthz/more")).Location, Does.StartWith("/signin"));
            Assert.That((await Visitor.Post("/healthz")).Status, Is.Not.EqualTo(HttpStatusCode.OK), "nothing is posted to it");

            var Said = new StringWriter();
            Assert.That(WebHealthCheck.Run(IPAddress.Loopback, Host.Port, TimeSpan.FromSeconds(5), Said), Is.Zero);
            Assert.That(Said.ToString().Trim(), Is.EqualTo("Campertron's web UI answers."));
            int Port = Host.Port;
            await Host.App.StopAsync();
            Said = new StringWriter();
            Assert.That(WebHealthCheck.Run(IPAddress.Loopback, Port, TimeSpan.FromSeconds(2), Said), Is.EqualTo(1));
            Assert.That(Said.ToString().Trim(), Is.EqualTo("Campertron's web UI does not answer."));
        }

        [Test]
        public async Task TheHealthCheck_IsNotHealthy_WhenSomethingElseAnswersOnThePort()
        {
            using var Other = new TcpListener(IPAddress.Loopback, 0);
            Other.Start();
            int Port = ((IPEndPoint)Other.LocalEndpoint).Port;
            Task Serve = Task.Run(async () =>
            {
                using TcpClient Client = await Other.AcceptTcpClientAsync();
                using NetworkStream Stream = Client.GetStream();
                byte[] Buffer = new byte[1024];
                _ = await Stream.ReadAsync(Buffer);
                await Stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello"));
            });
            Assert.That(WebHealthCheck.Ask(IPAddress.Loopback, Port, TimeSpan.FromSeconds(5)), Is.False, "200 is not enough: the answer must be the web UI's");
            await Serve;
        }
    }
}
