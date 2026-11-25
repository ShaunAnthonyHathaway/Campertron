using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CampertronWebTests
{
    // A headless Chrome for the tests of the live pages' script (D108, D109): the real browser runs the web UI's own script
    // under its Content-Security-Policy against a test's web UI on a loopback port, and the test reads and steers it through
    // the DevTools protocol (a WebSocket of .NET's own; no package). Its profile, home and caches are a temporary folder of
    // its own, removed afterwards; every host name fails to resolve (only the loopback address the tests use is reached),
    // and its background services are off, so nothing leaves the computer. On a computer without Chrome the browser tests
    // are skipped, except in CI (GitHub's Linux runners have Chrome), where a missing Chrome fails them.
    // Only what this class started is ever stopped (D109's correction of a leak): the process it started with its tree,
    // then any process left whose executable is that Chrome's own (its folder, from /proc/<pid>/exe) and whose command line
    // names this instance's own folder (a new GUID's), never a Chrome by its name or another profile's. It is stopped when
    // the test disposes it, when its own start fails (Start disposes it before it throws) and, for an instance a test never
    // disposed (a test run that is canceled), when the test process exits.
    internal sealed class Chrome : IAsyncDisposable
    {
        private static readonly string[] Places = { "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/google/chrome/chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser" };
        private readonly Process _Process;
        private readonly string _Folder;
        private readonly ClientWebSocket _Socket = new ClientWebSocket();
        private readonly SemaphoreSlim _Sending = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _Pending = new();
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private Task _Reader = Task.CompletedTask;
        private int _NextId;
        private string? _Session;
        private string? _Binaries;
        private int _Disposed;

        // the instances not yet disposed, stopped when the test process exits (a canceled run)
        private static readonly ConcurrentDictionary<Chrome, byte> Living = new();

        static Chrome()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (Chrome Left in Living.Keys)
                {
                    Left.StopNow();
                }
            };
        }

        // the process this instance started, its own folder (profile, home, caches) and the folder of its executable
        internal int Pid => _Process.Id;
        internal string Folder => _Folder;
        internal string? Binaries => _Binaries;

        // every event of the page, in order (the method and its parameters)
        internal ConcurrentQueue<(string Method, JsonElement Params)> Events { get; } = new();
        // called for each event on a thread of its own (it may send commands)
        internal Func<string, JsonElement, Task>? OnEvent { get; set; }

        private Chrome(Process Process, string Folder)
        {
            _Process = Process;
            _Folder = Folder;
        }

        // the browser to run, or the test is ignored (failed in CI)
        internal static string Require()
        {
            string? Named = Environment.GetEnvironmentVariable("CAMPERTRON_TEST_CHROME");
            string? Found = !string.IsNullOrEmpty(Named) ? (File.Exists(Named) ? Named : null)
                : OperatingSystem.IsLinux() ? Places.FirstOrDefault(File.Exists) : null;
            if (Found == null)
            {
                if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    Assert.Fail("The browser tests of the results page need Chrome, which GitHub's Linux runners have; none was found.");
                }
                Assert.Ignore("No Chrome on this computer (CAMPERTRON_TEST_CHROME names one): the results page's script is not run in a browser here.");
            }
            return Found!;
        }

        // Prepare: what a test does once the browser answers, before Start returns (a failure there disposes the browser)
        internal static async Task<Chrome> Start(int Width = 1024, int Height = 768, Func<Chrome, Task>? Prepare = null)
        {
            string Executable = Require();
            string Folder = Path.Join(Path.GetTempPath(), "CampertronChrome-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            var Info = new ProcessStartInfo(Executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string Argument in new[]
            {
                "--headless=new", "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage", "--no-first-run", "--no-default-browser-check",
                "--disable-background-networking", "--disable-component-update", "--disable-sync", "--disable-extensions", "--disable-default-apps",
                "--disable-breakpad", "--disable-crash-reporter", "--disable-domain-reliability", "--disable-client-side-phishing-detection",
                "--disable-features=Translate,MediaRouter,OptimizationHints,DialMediaRouteProvider,AutofillServerCommunication,CertificateTransparencyComponentUpdater",
                "--metrics-recording-only", "--mute-audio", "--password-store=basic", "--use-mock-keychain",
                "--host-resolver-rules=MAP * ~NOTFOUND , EXCLUDE 127.0.0.1",
                "--remote-debugging-port=0", "--user-data-dir=" + Path.Join(Folder, "profile"), "--window-size=" + Width + "," + Height, "about:blank"
            })
            {
                Info.ArgumentList.Add(Argument);
            }
            foreach ((string Name, string Sub) in new[] { ("HOME", "home"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"), ("XDG_DATA_HOME", "data") })
            {
                string Path_ = Path.Join(Folder, Sub);
                Directory.CreateDirectory(Path_);
                Info.Environment[Name] = Path_;
            }
            Process Started = Process.Start(Info) ?? throw new InvalidOperationException("Chrome did not start.");
            Started.OutputDataReceived += (_, _) => { };
            Started.ErrorDataReceived += (_, _) => { };
            Started.BeginOutputReadLine();
            Started.BeginErrorReadLine();
            var Browser = new Chrome(Started, Folder);
            Living[Browser] = 0;
            try
            {
                string PortFile = Path.Join(Folder, "profile", "DevToolsActivePort");
                var Clock = Stopwatch.StartNew();
                string[] Lines = Array.Empty<string>();
                while (Clock.Elapsed < TimeSpan.FromSeconds(30))
                {
                    if (Started.HasExited)
                    {
                        throw new InvalidOperationException("Chrome exited at its start with " + Started.ExitCode + ".");
                    }
                    if (File.Exists(PortFile) && (Lines = File.ReadAllLines(PortFile)).Length >= 2)
                    {
                        break;
                    }
                    await Task.Delay(50);
                }
                Assert.That(Lines, Has.Length.GreaterThanOrEqualTo(2), "Chrome's DevTools did not open");
                //the executable the process runs now (a wrapper script has exec'd it): where its own processes come from
                Browser._Binaries = Path.GetDirectoryName(ExecutableOf(Started.Id));
                await Browser._Socket.ConnectAsync(new Uri("ws://127.0.0.1:" + Lines[0].Trim() + Lines[1].Trim()), CancellationToken.None);
                Browser._Reader = Task.Run(Browser.Read);
                JsonElement Target = await Browser.Send("Target.createTarget", new { url = "about:blank" }, OfBrowser: true);
                JsonElement Attached = await Browser.Send("Target.attachToTarget", new { targetId = Target.GetProperty("targetId").GetString(), flatten = true }, OfBrowser: true);
                Browser._Session = Attached.GetProperty("sessionId").GetString();
                await Browser.Send("Page.enable");
                await Browser.Send("Runtime.enable");
                await Browser.Send("Network.enable");
                await Browser.Send("Emulation.setDeviceMetricsOverride", new { width = Width, height = Height, deviceScaleFactor = 1, mobile = false });
                await Browser.Send("Emulation.setFocusEmulationEnabled", new { enabled = true });
                //what the page meets under its policy and its own errors, from the first moment of every document
                await Browser.Send("Page.addScriptToEvaluateOnNewDocument", new
                {
                    source = "window.__violations = []; window.__errors = [];" +
                             "document.addEventListener('securitypolicyviolation', function (e) { window.__violations.push(e.violatedDirective + ' ' + e.blockedURI); });" +
                             "window.addEventListener('error', function (e) { window.__errors.push(String(e.message)); });"
                });
                if (Prepare != null)
                {
                    await Prepare(Browser);
                }
                return Browser;
            }
            catch
            {
                await Browser.DisposeAsync();
                throw;
            }
        }

        internal async Task<JsonElement> Send(string Method, object? Parameters = null, bool OfBrowser = false)
        {
            int Id = Interlocked.Increment(ref _NextId);
            var Message = new Dictionary<string, object?> { ["id"] = Id, ["method"] = Method, ["params"] = Parameters ?? new { } };
            if (!OfBrowser && _Session != null)
            {
                Message["sessionId"] = _Session;
            }
            var Done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _Pending[Id] = Done;
            byte[] Bytes = JsonSerializer.SerializeToUtf8Bytes(Message);
            await _Sending.WaitAsync();
            try
            {
                await _Socket.SendAsync(Bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _Sending.Release();
            }
            using var Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using (Timeout.Token.Register(() => Done.TrySetException(new TimeoutException("Chrome did not answer " + Method + "."))))
            {
                return await Done.Task;
            }
        }

        private async Task Read()
        {
            byte[] Buffer = new byte[64 * 1024];
            using var Message = new MemoryStream();
            try
            {
                while (!_Stop.IsCancellationRequested && _Socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult Got = await _Socket.ReceiveAsync(Buffer, _Stop.Token);
                    if (Got.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                    Message.Write(Buffer, 0, Got.Count);
                    if (!Got.EndOfMessage)
                    {
                        continue;
                    }
                    JsonElement Root;
                    using (JsonDocument Parsed = JsonDocument.Parse(Message.ToArray()))
                    {
                        Root = Parsed.RootElement.Clone();
                    }
                    Message.SetLength(0);
                    if (Root.TryGetProperty("id", out JsonElement Id) && _Pending.TryRemove(Id.GetInt32(), out TaskCompletionSource<JsonElement>? Done))
                    {
                        if (Root.TryGetProperty("error", out JsonElement Error))
                        {
                            Done.TrySetException(new InvalidOperationException("Chrome refused: " + Error));
                        }
                        else
                        {
                            Done.TrySetResult(Root.TryGetProperty("result", out JsonElement Result) ? Result : default);
                        }
                    }
                    else if (Root.TryGetProperty("method", out JsonElement Method))
                    {
                        string Name = Method.GetString()!;
                        JsonElement Parameters = Root.TryGetProperty("params", out JsonElement Found) ? Found : default;
                        Events.Enqueue((Name, Parameters));
                        if (OnEvent is Func<string, JsonElement, Task> Handler)
                        {
                            _ = Task.Run(() => Handler(Name, Parameters));
                        }
                    }
                }
            }
            catch (Exception Ended) when (Ended is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                //the browser went away or the test ended
            }
            foreach (TaskCompletionSource<JsonElement> Waiting in _Pending.Values)
            {
                Waiting.TrySetException(new InvalidOperationException("Chrome's DevTools closed."));
            }
        }

        // the page's expression, its value (JSON); a script error of the expression fails the test
        internal async Task<JsonElement> Evaluate(string Expression)
        {
            JsonElement Result = await Send("Runtime.evaluate", new { expression = Expression, returnByValue = true, awaitPromise = true });
            if (Result.TryGetProperty("exceptionDetails", out JsonElement Failure))
            {
                throw new InvalidOperationException("The expression failed in the page: " + Failure);
            }
            return Result.GetProperty("result").TryGetProperty("value", out JsonElement Value) ? Value : default;
        }

        internal async Task<string?> Text(string Expression)
        {
            JsonElement Value = await Evaluate(Expression);
            return Value.ValueKind == JsonValueKind.String ? Value.GetString() : Value.ValueKind == JsonValueKind.Undefined || Value.ValueKind == JsonValueKind.Null ? null : Value.ToString();
        }

        internal async Task<double> Number(string Expression) => (await Evaluate(Expression)).GetDouble();

        internal async Task<bool> Is(string Condition) => (await Evaluate("!!(" + Condition + ")")).ValueKind == JsonValueKind.True;

        // waits until the condition holds in the page, or fails with what the page shows
        internal async Task Until(string Condition, string What, double Seconds = 20)
        {
            var Clock = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (await Is(Condition))
                    {
                        return;
                    }
                }
                catch (InvalidOperationException) when (Clock.Elapsed < TimeSpan.FromSeconds(Seconds))
                {
                    //the page is between two documents
                }
                if (Clock.Elapsed > TimeSpan.FromSeconds(Seconds))
                {
                    Assert.Fail("The page never got there: " + What + ". It shows: " + await Text("document.body ? document.body.innerText.slice(0, 1500) : document.readyState"));
                }
                await Task.Delay(20);
            }
        }

        // a new document at the address, loaded
        internal async Task Open(string Address)
        {
            await Evaluate("window.__left = true");
            await Send("Page.navigate", new { url = Address });
            await Until("!window.__left && document.readyState === 'complete'", "the page " + Address);
        }

        // a form of the page sent with the button the selector names (as a click does), and the next document loaded
        internal async Task Press(string Selector)
        {
            await Evaluate("window.__left = true");
            await Evaluate("document.querySelector(" + JsonSerializer.Serialize(Selector) + ").click()");
            await Until("!window.__left && document.readyState === 'complete'", "the page after " + Selector);
        }

        // how many requests the page's script sent (fetch) to an address with this part (the network's own record)
        internal int Requests(string Part) => Fetches(Url => Url.Contains(Part, StringComparison.Ordinal));

        internal int Fetches(Func<string, bool> Which) => Events.Count(E => E.Method == "Network.requestWillBeSent"
            && E.Params.TryGetProperty("type", out JsonElement Type) && Type.GetString() == "Fetch"
            && Which(E.Params.GetProperty("request").GetProperty("url").GetString()!));

        // ---- what is this instance's own ----

        // the executable a process runs (/proc/<pid>/exe), or null (gone, or not this user's)
        internal static string? ExecutableOf(int Pid)
        {
            try
            {
                return new FileInfo("/proc/" + Pid.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/exe").ResolveLinkTarget(false)?.FullName;
            }
            catch (Exception Gone) when (Gone is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // the processes of this computer that are this instance's own: their executable is in the folder of the Chrome this
        // instance started, and their command line names this instance's own folder. Nothing else qualifies, so another
        // Chrome (another profile, another test's, a user's) is never one of them
        internal static List<int> ProcessesOf(string Folder, string? Binaries)
        {
            var Found = new List<int>();
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Binaries))
            {
                return Found;
            }
            foreach (string Entry in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(Entry), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Pid))
                {
                    continue;
                }
                string? Executable = ExecutableOf(Pid);
                if (Executable == null || !string.Equals(Path.GetDirectoryName(Executable), Binaries, StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    string CommandLine = File.ReadAllText(Path.Join(Entry, "cmdline")).Replace('\0', ' ');
                    if (CommandLine.Contains(Folder, StringComparison.Ordinal))
                    {
                        Found.Add(Pid);
                    }
                }
                catch (Exception Gone) when (Gone is IOException or UnauthorizedAccessException)
                {
                    //it ended meanwhile
                }
            }
            return Found;
        }

        // stops this instance's own processes left after its own process and tree, by the evidence above only
        private void StopLeftovers()
        {
            for (int Attempt = 0; Attempt < 3; Attempt++)
            {
                List<int> Left = ProcessesOf(_Folder, _Binaries);
                if (Left.Count == 0)
                {
                    return;
                }
                foreach (int Pid in Left)
                {
                    try
                    {
                        using Process One = Process.GetProcessById(Pid);
                        One.Kill(entireProcessTree: true);
                        One.WaitForExit(2000);
                    }
                    catch (Exception Gone) when (Gone is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                    {
                        //it ended meanwhile
                    }
                }
            }
        }

        // at the test process's exit: at once, without the DevTools
        private void StopNow()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) != 0)
            {
                return;
            }
            try
            {
                if (!_Process.HasExited)
                {
                    _Process.Kill(entireProcessTree: true);
                    _Process.WaitForExit(2000);
                }
            }
            catch (Exception Gone) when (Gone is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                //it ended meanwhile
            }
            StopLeftovers();
            try
            {
                Directory.Delete(_Folder, true);
            }
            catch (Exception Busy) when (Busy is IOException or UnauthorizedAccessException)
            {
                //the temporary folder stays; nothing of it is in use any more
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) != 0)
            {
                return;
            }
            Living.TryRemove(this, out _);
            try
            {
                if (_Socket.State == WebSocketState.Open)
                {
                    using var Quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    Task Closing = Send("Browser.close", OfBrowser: true);
                    await Task.WhenAny(Closing, Task.Delay(5000, Quick.Token));
                }
            }
            catch (Exception Ended) when (Ended is InvalidOperationException or WebSocketException or TimeoutException or OperationCanceledException)
            {
                //it closes anyway
            }
            _Stop.Cancel();
            if (!_Process.WaitForExit(5000))
            {
                _Process.Kill(entireProcessTree: true);
                _Process.WaitForExit(5000);
            }
            _Process.Dispose();
            StopLeftovers();
            _Socket.Dispose();
            try
            {
                await _Reader;
            }
            catch (Exception Ended) when (Ended is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                //ended with the socket
            }
            for (int Attempt = 0; Attempt < 10 && Directory.Exists(_Folder); Attempt++)
            {
                try
                {
                    Directory.Delete(_Folder, true);
                }
                catch (IOException)
                {
                    await Task.Delay(200);
                }
            }
        }

        // the answer of an intercepted request, as the test wants it (Fetch.fulfillRequest)
        internal Task Fulfill(string RequestId, int Status, string ContentType, string Body, params (string Name, string Value)[] Headers) =>
            Send("Fetch.fulfillRequest", new
            {
                requestId = RequestId,
                responseCode = Status,
                responseHeaders = new[] { new { name = "Content-Type", value = ContentType } }.Concat(Headers.Select(H => new { name = H.Name, value = H.Value })).ToArray(),
                body = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(Body))
            });
    }
}
