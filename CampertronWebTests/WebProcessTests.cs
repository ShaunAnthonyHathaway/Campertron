using System.Diagnostics;
using System.Net;

namespace CampertronWebTests
{
    // The console's own program in web mode, as a process (D80, D85, D92): its command line, its exit codes, every byte it
    // writes, and that it takes nothing from the environment. The process gets a data folder and a home folder of its own
    // in the temporary folder (XDG_DATA_HOME, HOME), so it never sees a real settings folder.
    [Platform("Linux")]
    public class WebProcessTests
    {
        private sealed class Started : IDisposable
        {
            internal Process Process = null!;
            internal string Root = string.Empty;
            internal string Data => Path.Join(Root, "data");
            internal string Home => Path.Join(Root, "home");
            internal string Settings => Path.Join(Data, "CampertronConfig");
            internal List<string> Out { get; } = new();
            internal List<string> Err { get; } = new();

            internal bool WaitForLine(string Part, int Seconds = 30)
            {
                Stopwatch Watch = Stopwatch.StartNew();
                while (Watch.Elapsed < TimeSpan.FromSeconds(Seconds))
                {
                    lock (Out)
                    {
                        if (Out.Any(Line => Line.Contains(Part)))
                        {
                            return true;
                        }
                    }
                    if (Process.HasExited)
                    {
                        return false;
                    }
                    Thread.Sleep(50);
                }
                return false;
            }

            // the signal a terminal's Ctrl+C or a container's stop sends
            internal int Stop()
            {
                if (!Process.HasExited)
                {
                    using Process Kill = Process.Start("kill", "-TERM " + Process.Id);
                    Kill.WaitForExit();
                }
                if (!Process.WaitForExit(20000))
                {
                    Process.Kill();
                    Assert.Fail("the web UI did not stop within 20 seconds of SIGTERM");
                }
                Process.WaitForExit();
                return Process.ExitCode;
            }

            public void Dispose()
            {
                if (!Process.HasExited)
                {
                    Process.Kill();
                    Process.WaitForExit();
                }
                Process.Dispose();
                Directory.Delete(Root, true);
            }
        }

        private static Started Run(string Arguments, Action<Started>? Prepare = null, params (string Name, string Value)[] Environment_)
        {
            var Result = new Started { Root = WebTestHost.NewFolder() };
            Directory.CreateDirectory(Result.Data);
            Directory.CreateDirectory(Result.Home);
            Prepare?.Invoke(Result);
            var Info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = Result.Root
            };
            Info.ArgumentList.Add(Path.Join(AppContext.BaseDirectory, "CampertronConsole.dll"));
            foreach (string Argument in Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                Info.ArgumentList.Add(Argument);
            }
            Info.Environment["XDG_DATA_HOME"] = Result.Data;
            Info.Environment["HOME"] = Result.Home;
            Info.Environment.Remove("DOTNET_RUNNING_IN_CONTAINER");
            foreach ((string Name, string Value) in Environment_)
            {
                Info.Environment[Name] = Value;
            }
            Result.Process = new Process { StartInfo = Info };
            Result.Process.OutputDataReceived += (Sender, Event) => { if (Event.Data != null) { lock (Result.Out) { Result.Out.Add(Event.Data); } } };
            Result.Process.ErrorDataReceived += (Sender, Event) => { if (Event.Data != null) { lock (Result.Err) { Result.Err.Add(Event.Data); } } };
            Result.Process.Start();
            Result.Process.BeginOutputReadLine();
            Result.Process.BeginErrorReadLine();
            return Result;
        }

        private const string Stamp = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ";

        [Test]
        public void WebMode_WritesOnlyItsOwnLines_StopsCleanlyOnSigterm_AndTakesNothingFromTheEnvironment()
        {
            int Port = WebStartTests.FreePort();
            using Started Web = Run("--web --listen 127.0.0.1 --port " + Port, null,
                ("ASPNETCORE_URLS", "http://127.0.0.1:1"), ("ASPNETCORE_ENVIRONMENT", "Development"), ("ASPNETCORE_HTTP_PORTS", "8080"), ("DOTNET_ENVIRONMENT", "Development"),
                ("Logging__LogLevel__Default", "Trace"), ("Logging__Console__LogLevel__Default", "Trace"), ("ASPNETCORE_DETAILEDERRORS", "true"));
            Assert.That(Web.WaitForLine("Web UI setup code: >>> "), Is.True, "web mode did not start: " + string.Join(" | ", Web.Err));

            using var Visitor = new Browser(new Uri("http://127.0.0.1:" + Port + "/"));
            Assert.That(Visitor.Get("/").Result.Location, Is.EqualTo("/setup"));
            Assert.That(Visitor.Get("/setup").Result.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Visitor.Get("/nothing-here?MARKER=1").Result.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Visitor.Post("/setup", ("Code", "AAAA-AAAA-AAAA"), ("Password", "MARKER-PASSWORD-1"), ("Repeat", "MARKER-PASSWORD-1")).Result.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Web.WaitForLine("failed sign-in"), Is.True);

            Assert.That(Web.Stop(), Is.Zero, "a stop that was asked for is no failure");
            Assert.That(Web.Err, Is.Empty, "nothing on standard error");
            Assert.That(Web.Out, Has.Count.EqualTo(6), string.Join("\n", Web.Out));
            Assert.That(Web.Out, Has.All.Match(Stamp));
            Assert.That(Web.Out[0], Does.Match(Stamp + @"CampertronConsole \S+ \| \.NET 11\.\S+ \(\w+\) \| settings ").And.EndWith(Web.Settings + " | web UI"));
            Assert.That(Web.Out[1], Does.EndWith("  Web UI: listening on http://127.0.0.1:" + Port + " (this computer only); open http://127.0.0.1:" + Port + "/ in a browser. A password is required."));
            Assert.That(Web.Out[2], Does.EndWith("  Web UI: no password is set yet. Enter this one-time setup code in the browser to set it (valid for 30 minutes, for one use):"));
            Assert.That(Web.Out[3], Does.Match(Stamp + @"Web UI setup code: >>> [A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4} <<<$"));
            Assert.That(Web.Out[4], Does.EndWith("  Web UI: failed sign-in from 127.0.0.1 (1 failed sign-in from it since the web UI started)."));
            Assert.That(Web.Out[5], Does.EndWith("  Web UI: stopped."));
            Assert.That(string.Join("\n", Web.Out), Does.Not.Contain("MARKER").And.Not.Contain("Microsoft").And.Not.Contain("info:").And.Not.Contain("warn:").And.Not.Contain("Now listening"));

            //what the start created: the two folders and the keys; no General.yaml, no Email.yaml, no database, no search
            Assert.That(Directory.GetFileSystemEntries(Web.Data).Select(Path.GetFileName), Is.EquivalentTo(new[] { "CampertronConfig", "CampertronCache" }));
            Assert.That(Directory.GetFileSystemEntries(Web.Settings).Select(Path.GetFileName), Is.EqualTo(new[] { "Web.keys" }));
            Assert.That(Directory.GetFileSystemEntries(Path.Join(Web.Data, "CampertronCache")), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Web.Home), Is.Empty, "nothing in the home folder: no ~/.aspnet keys");
            Assert.That(Directory.GetFileSystemEntries(Web.Root).Select(Path.GetFileName), Is.EquivalentTo(new[] { "data", "home" }), "nothing in the working directory");
        }

        [TestCase("--web --port", "--port needs a whole number from 1 to 65535.")]
        [TestCase("--web --port 0", "--port needs a whole number from 1 to 65535.")]
        [TestCase("--web --port 65536", "--port needs a whole number from 1 to 65535.")]
        [TestCase("--web --port=8790x", "--port needs a whole number from 1 to 65535.")]
        [TestCase("--web --listen example.com", "--listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost.")]
        [TestCase("--web --listen 127.1", "--listen needs one IP address, such as 127.0.0.1 (this computer only) or 0.0.0.0 (every network interface), or the word localhost.")]
        [TestCase("--web --verbose", "an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port.")]
        [TestCase("--web --version", "an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port.")]
        [TestCase("--web --healthcheck", "--web starts the web UI and --healthcheck asks a running one; use one of them.")]
        [TestCase("--port 8790", "--listen and --port are options of --web (the web UI) and of --healthcheck; without one of them nothing was started.")]
        [TestCase("--listen 127.0.0.1", "--listen and --port are options of --web (the web UI) and of --healthcheck; without one of them nothing was started.")]
        [TestCase("--web --web", "--web was given more than once.")]
        public void ACommandLineThatIsWrong_IsRefusedWithOneError_ExitCode1_AndNothingIsCreated(string Arguments, string Problem)
        {
            using Started Refused = Run(Arguments);
            Assert.That(Refused.Process.WaitForExit(30000), Is.True);
            Refused.Process.WaitForExit();
            Assert.That(Refused.Process.ExitCode, Is.EqualTo(1));
            Assert.That(Refused.Out, Is.Empty);
            Assert.That(Refused.Err, Is.EqualTo(new[] { "Error: " + Problem, "  " + WebArguments.Usage }));
            Assert.That(Directory.GetFileSystemEntries(Refused.Data), Is.Empty, "no folder was created");
            Assert.That(Directory.GetFileSystemEntries(Refused.Home), Is.Empty);
        }

        [Test]
        public void TheVersion_IsAnsweredAsBefore_AndStartsNoWebUi()
        {
            using Started Version = Run("--version");
            Assert.That(Version.Process.WaitForExit(30000), Is.True);
            Version.Process.WaitForExit();
            Assert.That(Version.Process.ExitCode, Is.Zero);
            Assert.That(Version.Out, Has.Count.EqualTo(2));
            Assert.That(Version.Out[0], Does.StartWith("CampertronConsole "));
            Assert.That(Version.Out[1], Does.StartWith("Framework => .NET 11."));
            Assert.That(Version.Err, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Version.Data), Is.Empty);
        }

        [Test]
        public void TheHealthCheck_AnswersForARunningWebUi_WithItsExitCode_AndCreatesNothing()
        {
            int Port = WebStartTests.FreePort();
            using (Started Nothing = Run("--healthcheck --port " + Port))
            {
                Assert.That(Nothing.Process.WaitForExit(30000), Is.True);
                Nothing.Process.WaitForExit();
                Assert.That(Nothing.Process.ExitCode, Is.EqualTo(1));
                Assert.That(Nothing.Out, Is.EqualTo(new[] { "Campertron's web UI does not answer." }));
                Assert.That(Nothing.Err, Is.Empty);
                Assert.That(Directory.GetFileSystemEntries(Nothing.Data), Is.Empty, "the health check reads and creates nothing");
            }
            using Started Web = Run("--web --listen 127.0.0.1 --port " + Port);
            Assert.That(Web.WaitForLine("Web UI: listening on"), Is.True, string.Join(" | ", Web.Err));
            //a proxy from the environment is not used for it
            using (Started Health = Run("--healthcheck --port " + Port, null, ("HTTP_PROXY", "http://127.0.0.1:9"), ("http_proxy", "http://127.0.0.1:9"), ("ALL_PROXY", "http://127.0.0.1:9")))
            {
                Assert.That(Health.Process.WaitForExit(30000), Is.True);
                Health.Process.WaitForExit();
                Assert.That(Health.Out, Is.EqualTo(new[] { "Campertron's web UI answers." }));
                Assert.That(Health.Process.ExitCode, Is.Zero);
                Assert.That(Directory.GetFileSystemEntries(Health.Data), Is.Empty);
            }
            Assert.That(Web.Stop(), Is.Zero);
        }

        [Test]
        public void InAContainerWithoutItsFolders_WebModeIsRefusedLikeEveryOtherStart()
        {
            using Started Container = Run("--web --listen 127.0.0.1 --port " + WebStartTests.FreePort(), null, ("DOTNET_RUNNING_IN_CONTAINER", "true"));
            Assert.That(Container.Process.WaitForExit(30000), Is.True);
            Container.Process.WaitForExit();
            if (Directory.Exists("/config"))
            {
                Assert.Ignore("this computer has a /config folder");
            }
            Assert.That(Container.Process.ExitCode, Is.EqualTo(1));
            Assert.That(Container.Err, Is.EqualTo(new[] { "Error: Config path does not exist", "  In a container, mount the settings folder at /config and the cache folder at /cache." }));
            Assert.That(Container.Out, Is.Empty);
        }
    }
}
