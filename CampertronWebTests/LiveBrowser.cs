using System.Collections.Concurrent;
using System.Text.Json;

namespace CampertronWebTests
{
    // What a test does with each of a live page's questions (Chrome's Fetch domain): passes it on, fails it, answers it
    // with something unusable, holds it back, or answers it with a crafted answer first
    internal sealed class Questions
    {
        internal volatile string Mode = "pass";
        // what is done with the script's request for the page itself ("?handler=view"): passed on, failed or an error
        internal volatile string ViewMode = "pass";
        internal readonly ConcurrentQueue<string> Crafted = new();
        internal readonly ConcurrentQueue<string> Held = new();
        private int _Answered;
        internal int Answered => Volatile.Read(ref _Answered);
        internal void Count() => Interlocked.Increment(ref _Answered);
    }

    // What the browser tests of the live pages share (D108, D109): a test's web UI and a signed-in headless Chrome
    // (Chrome.cs) on a live page, the page's questions as the test answers them, its announcements in order and a mark a
    // reload would lose. A scene is built whole or not at all: when any step of its setup fails, the browser and the web
    // UI it made are disposed before the failure is thrown (Build), so a failing test leaves no Chrome behind.
    internal abstract class LiveScene : IAsyncDisposable
    {
        internal Chrome B = null!;
        internal Questions? Asked;
        // the scene's web UI, or null before it exists
        protected abstract WebTestHost? Web { get; }
        protected abstract ValueTask DisposeWeb();

        // the scene Setup builds, or, when a step of it fails, nothing left of it
        internal static async Task<T> Build<T>(T Scene, Func<T, Task> Setup) where T : LiveScene
        {
            try
            {
                await Setup(Scene);
                return Scene;
            }
            catch
            {
                await Scene.DisposeAsync();
                throw;
            }
        }

        // the browser, its questions intercepted when the test answers them; Theme (D110): the browser's theme cookie set
        // first (null: none, System) and its light or dark setting (null: the browser's own)
        internal async Task Browse(bool Intercept = false, int Width = 1024, int Height = 768, string? Theme = null, string? Prefers = null)
        {
            B = await Chrome.Start(Width, Height);
            await ThemeLook.Prepare(B, Web!, Theme, Prefers);
            if (Intercept)
            {
                Asked = new Questions();
                B.OnEvent = Answer;
                await B.Send("Fetch.enable", new { patterns = new[] { new { urlPattern = "*handler=progress*", requestStage = "Request" }, new { urlPattern = "*handler=view*", requestStage = "Request" } } });
            }
        }

        internal async Task SignIn()
        {
            await B.Open(Web!.Root + "signin");
            await B.Evaluate("document.getElementById('password').value = " + JsonSerializer.Serialize(WebTestHost.Password));
            await B.Press("main form button[type=submit]");
        }

        // the page's script running for the page's operation
        internal Task Running() => B.Until("document.getElementById('live') && document.getElementById('live').textContent.indexOf('This page updates by itself') === 0", "the script's first word");

        // from now on: the page's announcements, in order, and a mark that a reload would lose
        internal Task Watch() => B.Evaluate("window.__kept = 1; window.__said = []; new MutationObserver(function () { window.__said.push(document.getElementById('live-announce').textContent); })" +
                                            ".observe(document.getElementById('live-announce'), { childList: true, characterData: true, subtree: true })");

        internal async Task<List<string>> Said() => (await B.Evaluate("window.__said")).EnumerateArray().Select(E => E.GetString()!).ToList();

        internal async Task NoErrorsOrViolations()
        {
            Assert.That((await B.Evaluate("window.__violations")).EnumerateArray().Select(E => E.GetString()), Is.Empty, "nothing the policy refused");
            Assert.That((await B.Evaluate("window.__errors")).EnumerateArray().Select(E => E.GetString()), Is.Empty, "no script error");
        }

        // waits for the page's next questions to be asked
        internal async Task Questions_(int Count)
        {
            int Before = B.Requests("handler=progress");
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (B.Requests("handler=progress") < Before + Count && Clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(20);
            }
            Assert.That(B.Requests("handler=progress"), Is.GreaterThanOrEqualTo(Before + Count), "the page went on asking");
        }

        // the page asks nothing more (after its end, or once it stopped)
        internal async Task AsksNoMore(TimeSpan Interval)
        {
            int Asked_ = B.Fetches(Url => Url.Contains("handler=", StringComparison.Ordinal));
            await Task.Delay(Interval * 8);
            Assert.That(B.Fetches(Url => Url.Contains("handler=", StringComparison.Ordinal)), Is.EqualTo(Asked_), "nothing more is asked");
        }

        // the crafted answer of a test, taken by the page's next question and the one after it
        internal async Task Give(string Body)
        {
            Questions Q = Asked!;
            int Before = Q.Answered;
            Q.Crafted.Enqueue(Body);
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Q.Answered < Before + 2 && Clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(20);
            }
            Assert.That(Q.Answered, Is.GreaterThanOrEqualTo(Before + 2), "the crafted answer and the next were taken");
        }

        // an answer as the web UI writes one (WebLiveAnswer), as the test crafts it
        internal static string Crafted(string Key, string State, long Seq, int Rank, string View, IDictionary<string, string>? Fields = null, object? Bar = null) =>
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["format"] = 2,
                ["state"] = State,
                ["key"] = Key,
                ["seq"] = Seq,
                ["rank"] = Rank,
                ["view"] = View,
                ["fields"] = Fields ?? new Dictionary<string, string>(),
                ["bar"] = Bar
            }.Where(P => P.Value != null).ToDictionary(P => P.Key, P => P.Value));

        // the attribute of the page's #live (its key, view, state, rank)
        internal Task<string?> LiveAttribute(string Name) => B.Text("document.getElementById('live').getAttribute(" + JsonSerializer.Serialize(Name) + ")");

        private async Task Answer(string Method, JsonElement Parameters)
        {
            if (Method != "Fetch.requestPaused" || Asked == null)
            {
                return;
            }
            string Id = Parameters.GetProperty("requestId").GetString()!;
            string Url = Parameters.GetProperty("request").GetProperty("url").GetString()!;
            try
            {
                if (Url.Contains("handler=view", StringComparison.Ordinal))
                {
                    switch (Asked.ViewMode)
                    {
                        case "fail":
                            await B.Send("Fetch.failRequest", new { requestId = Id, errorReason = "ConnectionRefused" });
                            break;
                        case "error":
                            await B.Fulfill(Id, 502, "text/html; charset=utf-8", "<p>bad gateway</p>");
                            break;
                        default:
                            await B.Send("Fetch.continueRequest", new { requestId = Id });
                            break;
                    }
                    return;
                }
                if (Asked.Crafted.TryDequeue(out string? Body))
                {
                    await B.Fulfill(Id, 200, "application/json; charset=utf-8", Body);
                }
                else
                {
                    switch (Asked.Mode)
                    {
                        case "fail":
                            await B.Send("Fetch.failRequest", new { requestId = Id, errorReason = "ConnectionRefused" });
                            break;
                        case "malformed":
                            await B.Fulfill(Id, 200, "application/json; charset=utf-8", "{\"format\":2,\"state\":\"running\",");
                            break;
                        case "markup":
                            await B.Fulfill(Id, 200, "text/html; charset=utf-8", "<p>not the answer</p>");
                            break;
                        case "error":
                            await B.Fulfill(Id, 500, "text/html; charset=utf-8", "<p>failure</p>");
                            break;
                        case "hold":
                            Asked.Held.Enqueue(Id);
                            return;
                        default:
                            await B.Send("Fetch.continueRequest", new { requestId = Id });
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                //the page gave the request up meanwhile (its own time limit, or it was left)
            }
            Asked.Count();
        }

        // the held questions let go, the page's own time limit may have given them up
        internal async Task Release()
        {
            while (Asked!.Held.TryDequeue(out string? Held))
            {
                try
                {
                    await B.Send("Fetch.continueRequest", new { requestId = Held });
                }
                catch (InvalidOperationException)
                {
                    //the page gave it up after its time limit
                }
            }
        }

        // what a scene lets go before it is disposed (a seam that holds its operation)
        protected virtual void Releasing()
        {
        }

        public async ValueTask DisposeAsync()
        {
            Releasing();
            try
            {
                if (B != null)
                {
                    await B.DisposeAsync();
                }
            }
            finally
            {
                if (Web != null)
                {
                    await DisposeWeb();
                }
            }
        }
    }
}
