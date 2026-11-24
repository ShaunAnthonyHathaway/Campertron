using System.Collections.Concurrent;
using System.Text.Json;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronWebTests.ResultsLiveTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The results page's automatic progress (D108) in a real browser: headless Chrome runs the web UI's own script under
    // the web UI's Content-Security-Policy against a test's web UI, with a run the test steers (Chrome.cs: a profile of its
    // own, nothing beyond the loopback address). While the run goes on the page changes in place: its status, stage,
    // counts, elapsed time, bar and each file's state, and when it last got them; never a reload, never the focus or the
    // scroll position, never an announcement for an ordinary update. A failed, stalled or unusable answer is said at once,
    // as the page's problem and not the search's, and the updates come back without another search. An older answer,
    // another run's answer or markup in an answer changes nothing. At the run's end the results part is put in place once,
    // the end is announced once and nothing more is asked. Cancel stays the page's protected POST; a session that ends
    // stops the updates.
    public class ResultsLiveBrowserTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);
        // the page's time limit for an answer: the web UI's own ten seconds, so a busy build machine never makes an
        // ordinary answer late; short only where a test holds an answer back
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(1200);
        private const string StartButton_ = "button[formaction='/results?handler=start']";
        private const string CancelButton = "button[formaction='/results?handler=cancel']";
        private const string LiveText = "document.getElementById('live').textContent";
        private const string LiveClass = "document.getElementById('live').className";
        private const string StatusText = "document.getElementById('status').textContent";

        // what the test does with each of the page's questions (Chrome's Fetch domain)
        private sealed class Questions
        {
            internal volatile string Mode = "pass";
            internal readonly ConcurrentQueue<string> Crafted = new();
            internal readonly ConcurrentQueue<string> Held = new();
            private int _Answered;
            internal int Answered => Volatile.Read(ref _Answered);
            internal void Count() => Interlocked.Increment(ref _Answered);
        }

        // a web UI with a run the test steers and a signed-in Chrome on its results page, the run started from the page
        private sealed class Scene : IAsyncDisposable
        {
            internal ResultsHost R = null!;
            internal Steered Steer = new Steered();
            internal Chrome B = null!;
            internal string Key = string.Empty;
            internal Questions? Asked;

            internal static async Task<Scene> Start(bool Intercept = false, int Height = 768, TimeSpan? Limit = null)
            {
                Chrome.Require();
                var Scene_ = new Scene();
                Scene_.R = await ResultsHost.Start(Run: Scene_.Steer.Run, LiveInterval: Interval, LiveTimeout: Limit ?? Timeout);
                File.WriteAllText(Scene_.R.In("Alpha.yaml"), Search("First trip"));
                Scene_.B = await Chrome.Start(Height: Height);
                if (Intercept)
                {
                    Scene_.Asked = new Questions();
                    Scene_.B.OnEvent = Scene_.Answer;
                    await Scene_.B.Send("Fetch.enable", new { patterns = new[] { new { urlPattern = "*handler=progress*", requestStage = "Request" } } });
                }
                await Scene_.B.Open(Scene_.R.Host.Root + "signin");
                await Scene_.B.Evaluate("document.getElementById('password').value = " + JsonSerializer.Serialize(WebTestHost.Password));
                await Scene_.B.Press("main form button[type=submit]");
                await Scene_.Begin();
                return Scene_;
            }

            // the page's Start, and the script running for the new run
            internal async Task Begin()
            {
                await B.Open(R.Host.Root + "results");
                await B.Press(StartButton_);
                await B.Until("document.getElementById('live') && " + LiveText + ".indexOf('This page updates by itself') === 0", "the script's first word");
                Key = (await B.Text("document.getElementById('live').getAttribute('data-run')"))!;
                //the announcements the page makes from now on, and a mark that a reload would lose
                await B.Evaluate("window.__kept = 1; window.__said = []; new MutationObserver(function () { window.__said.push(document.getElementById('results-announce').textContent); })" +
                                 ".observe(document.getElementById('results-announce'), { childList: true, characterData: true, subtree: true })");
            }

            private async Task Answer(string Method, JsonElement Parameters)
            {
                if (Method != "Fetch.requestPaused" || Asked == null)
                {
                    return;
                }
                string Id = Parameters.GetProperty("requestId").GetString()!;
                try
                {
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
                                await B.Fulfill(Id, 200, "application/json; charset=utf-8", "{\"format\":1,\"state\":\"running\",");
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

            // the said announcements, in order
            internal async Task<List<string>> Said() => (await B.Evaluate("window.__said")).EnumerateArray().Select(E => E.GetString()!).ToList();

            internal async Task NoErrorsOrViolations()
            {
                Assert.That((await B.Evaluate("window.__violations")).EnumerateArray().Select(E => E.GetString()), Is.Empty, "nothing the policy refused");
                Assert.That((await B.Evaluate("window.__errors")).EnumerateArray().Select(E => E.GetString()), Is.Empty, "no script error");
            }

            // waits for the page's next questions to be asked and answered
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

            internal string Crafted(string State, long Seq, string Status, string? Run = null) => JsonSerializer.Serialize(new
            {
                format = 1,
                state = State,
                run = Run ?? Key,
                seq = Seq,
                status = Status,
                details = Status + " details",
                bar = new { indeterminate = false, value = 2, max = 4 },
                rows = new[] { new { row = 0, state = Status + " row" } }
            });

            public async ValueTask DisposeAsync()
            {
                if (B != null)
                {
                    await B.DisposeAsync();
                }
                if (R != null)
                {
                    await R.DisposeAsync();
                }
            }
        }

        [Test]
        public async Task TheRunningPage_ChangesInPlace_KeepsTheFocusAndTheScroll_AndShowsTheResultsOnceWithoutAReload()
        {
            await using Scene S = await Scene.Start(Height: 300);
            Chrome B = S.B;
            Assert.That(await B.Text(StatusText), Is.EqualTo(ResultsWording.Searching(1)));
            Assert.That(await B.Text(LiveClass), Is.EqualTo("hint"));
            Assert.That(await B.Text(LiveText), Does.Match("^This page updates by itself while the search runs\\. Last update: .+\\.$").And.Not.Contain(WebResultsLiveWording.TimePlaceholder),
                "the last update's time");
            await S.Questions_(2);
            Assert.That(await B.Text(LiveText), Does.Not.Contain(WebResultsLiveWording.TimePlaceholder).And.StartWith("This page updates by itself"), "after the page's own answers too");
            //the user is at Cancel, scrolled down
            await B.Evaluate("document.querySelector(\"" + CancelButton + "\").focus(); window.scrollTo(0, document.documentElement.scrollHeight)");
            double Y = await B.Number("window.scrollY");
            Assert.That(Y, Is.GreaterThan(0), "the page scrolls in a low window");

            //the stage, the counts, the bar and the file's state, as the run reports them
            S.Steer.Report(SearchRunStage.CheckingAvailability, RequestsPlanned: 4, RequestsCompleted: 1, Searches: new[] { ("Alpha.yaml", SearchState.Searching) });
            await B.Until("document.getElementById('progress-details').textContent.indexOf('Availability: 1 of 4 month requests finished') >= 0", "the counts");
            Assert.That(await B.Text("document.getElementById('progress-details').textContent"), Does.StartWith("Checking availability at recreation.gov"));
            Assert.That(await B.Text("document.getElementById('file-0-state').textContent"), Is.EqualTo("Searching"));
            Assert.That(await B.Text("document.getElementById('progress').getAttribute('value') + '/' + document.getElementById('progress').getAttribute('max')"), Is.EqualTo("1/4"));
            //the elapsed time is the web UI's clock, not one of the page's own
            S.R.Time.Advance(TimeSpan.FromSeconds(65));
            await B.Until("/Elapsed 1:05$/.test(document.getElementById('progress-details').textContent)", "the elapsed time");
            S.Steer.Report(SearchRunStage.PreparingCampsiteDetails, CampgroundsPlanned: 2, CampgroundsPrepared: 1, Searches: new[] { ("Alpha.yaml", SearchState.Searching) });
            await B.Until("document.getElementById('progress-details').textContent.indexOf('Preparing campsite details') === 0", "the next stage");
            await S.Questions_(3);

            //nothing of that moved the user: the same document, the focus at Cancel, the same scroll position, no announcement
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "never a reload");
            Assert.That(await B.Is("document.activeElement === document.querySelector(\"" + CancelButton + "\")"), Is.True, "the focus stays at Cancel");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y), "the scroll position stays");
            Assert.That(await S.Said(), Is.Empty, "an ordinary update is never announced");
            Assert.That(await B.Is("document.querySelector(\"" + CancelButton + "\").disabled === false"), Is.True, "Cancel stays available");
            Assert.That(await B.Is("Array.prototype.some.call(document.querySelectorAll('a[href=\"/results\"]'), function (A) { return A.textContent === 'Show the latest progress'; })"), Is.True, "the manual way stays");
            Assert.That(S.Steer.Runs, Is.EqualTo(1));

            //the end: the results part in place, once; the focus from the removed Cancel to the status; the end announced once
            S.Steer.Finish(Steered.End(SearchRunStatus.Completed, Result("Alpha.yaml", new[] { Opening("990000101", new DateTime(2026, 12, 1)) }, Name: "First trip")));
            await B.Until("document.getElementById('results-heading') && !document.getElementById('live')", "the results");
            string? Status_ = await B.Text(StatusText);
            Assert.That(Status_, Does.StartWith("Finished: 1 opening found in 1 search."));
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "the results came without a reload");
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("status"));
            Assert.That(await S.Said(), Is.EqualTo(new[] { Status_ }), "the end, announced once");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y), "the scroll position stays");
            Assert.That(await B.Is("document.querySelector('a[href^=\"https://www.recreation.gov/camping/campsites/\"]')"), Is.True, "the booking link");
            Assert.That(await B.Is("document.querySelector(\"" + StartButton_ + "\").disabled === false && !document.querySelector(\"" + CancelButton + "\")"), Is.True, "the choice is open again");
            int Asked = B.Requests("handler=progress");
            await Task.Delay(Interval * 8);
            Assert.That(B.Requests("handler=progress"), Is.EqualTo(Asked), "nothing more is asked after the end");
            Assert.That(B.Fetches(Url => Url == S.R.Host.Root + "results"), Is.EqualTo(1), "the results part was asked for once");
            await S.NoErrorsOrViolations();

            //the part put in place is the page's: its form starts the next run (its token and revision are current)
            await B.Press(StartButton_);
            await B.Until("document.getElementById('live') && " + LiveText + ".indexOf('This page updates by itself') === 0", "the next run");
            Assert.That(S.Steer.Runs, Is.EqualTo(2));
            Assert.That(S.R.Gov.Requests, Is.Zero);
            Assert.That(File.Exists(S.R.In("Camp.History")) || File.Exists(S.R.In(MonitoringLock.FileName)), Is.False, "a manual search: no Camp.History, no lock");
            Assert.That(S.R.Host.Lines.Concat(S.R.Host.Warnings).Concat(S.R.Host.Errors), Is.Empty, "nothing sent, nothing said");
        }

        [Test]
        public async Task AFailedStalledOrUnusableAnswer_IsSaidAtOnceAsThePagesProblem_AndTheUpdatesComeBackWithoutAnotherSearch()
        {
            await using Scene S = await Scene.Start(Intercept: true, Limit: ShortTimeout);
            Chrome B = S.B;
            Questions Q = S.Asked!;
            string Searching = ResultsWording.Searching(1);
            foreach (string Mode in new[] { "fail", "malformed", "markup", "error", "hold" })
            {
                Q.Mode = Mode;
                await B.Until(LiveClass + " === 'message warning'", "the warning for " + Mode, Seconds: 20);
                Assert.That(await B.Text(LiveText), Does.Match("^Warning: This page can't get the search's progress right now \\(last update: .+\\)\\. That does not stop the search")
                    .And.Not.Contain(WebResultsLiveWording.TimePlaceholder), Mode);
                Assert.That(await B.Text(StatusText), Is.EqualTo(Searching), "the search is not said to have failed: " + Mode);
                await S.Questions_(Mode == "hold" ? 1 : 3);
                List<string> Said = await S.Said();
                Assert.That(Said.Last(), Is.EqualTo(await B.Text(LiveText)).Or.StartWith("Warning: This page can't get"), Mode);
                Assert.That(Said.Count(T => T.StartsWith("Warning: ", StringComparison.Ordinal)), Is.EqualTo(Said.Count(T => T == WebResultsLiveWording.Resumed) + 1), "said once for each time it began to fail: " + Mode);
                Q.Mode = "pass";
                while (Q.Held.TryDequeue(out string? Held))
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
                await B.Until(LiveClass + " === 'hint'", "the updates again after " + Mode, Seconds: 20);
                Assert.That(await B.Text(LiveText), Does.StartWith("This page updates by itself"));
                Assert.That((await S.Said()).Last(), Is.EqualTo(WebResultsLiveWording.Resumed), "said once when they came back: " + Mode);
            }
            //the run went on all the while: the next report shows, and nothing started it again
            S.Steer.Report(SearchRunStage.CheckingAvailability, RequestsPlanned: 2, RequestsCompleted: 1, Searches: new[] { ("Alpha.yaml", SearchState.Searching) });
            await B.Until("document.getElementById('progress-details').textContent.indexOf('1 of 2 month requests') >= 0", "the progress after the failures");
            Assert.That(S.Steer.Runs, Is.EqualTo(1), "no update started or repeated the search");
            Assert.That(await B.Is("window.__kept === 1"), Is.True);

            //the end, after a failure: the results all the same
            Q.Mode = "fail";
            await B.Until(LiveClass + " === 'message warning'", "the warning before the end");
            S.Steer.Finish(Steered.End(SearchRunStatus.Completed, Result("Alpha.yaml", Array.Empty<SearchOpening>(), Name: "First trip")));
            await Task.Delay(Interval * 4);
            Assert.That(await B.Is("!document.getElementById('results-heading')"), Is.True, "without an answer the page doesn't claim the end");
            Q.Mode = "pass";
            await B.Until("document.getElementById('results-heading') && !document.getElementById('live')", "the results after the failure", Seconds: 20);
            Assert.That(await B.Text(StatusText), Does.StartWith("Finished: "));
            Assert.That(S.Steer.Runs, Is.EqualTo(1));
            await S.NoErrorsOrViolations();
        }

        [Test]
        public async Task AnOlderAnswer_ARunningAnswerAfterCanceling_MarkupOrAnotherRunsAnswer_NeverChangesWhatThePageShows()
        {
            await using Scene S = await Scene.Start(Intercept: true);
            Chrome B = S.B;
            Questions Q = S.Asked!;
            async Task Give(string Body)
            {
                int Before = Q.Answered;
                Q.Crafted.Enqueue(Body);
                var Clock = System.Diagnostics.Stopwatch.StartNew();
                while (Q.Answered < Before + 2 && Clock.Elapsed < TimeSpan.FromSeconds(20))
                {
                    await Task.Delay(20);
                }
                Assert.That(Q.Answered, Is.GreaterThanOrEqualTo(Before + 2), "the crafted answer and the next were taken");
            }

            //a newer answer is shown; an older one, the same one again and the real (older) ones are not
            await Give(S.Crafted("running", 900_000, "Newer state"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Newer state"));
            Assert.That(await B.Text("document.getElementById('file-0-state').textContent"), Is.EqualTo("Newer state row"));
            await Give(S.Crafted("running", 800_000, "Older state"));
            await Give(S.Crafted("running", 900_000, "The same number again"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Newer state"), "an older or repeated answer never replaces a newer one");
            Assert.That(await B.Text("document.getElementById('progress-details').textContent"), Is.EqualTo("Newer state details"));

            //once canceling, a running answer, however new, never makes the run look active again
            await Give(S.Crafted("canceling", 950_000, "Canceling as crafted"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Canceling as crafted"));
            await Give(S.Crafted("running", 990_000, "Running again"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Canceling as crafted"), "canceling wins");

            //markup in an answer is text
            await Give(S.Crafted("canceling", 960_000, "<img src=x onerror=\"window.__pwned=1\"><b>bold</b>"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("<img src=x onerror=\"window.__pwned=1\"><b>bold</b>"));
            Assert.That(await B.Is("!document.querySelector('main img, main b') && window.__pwned === undefined"), Is.True, "never markup");

            //an answer the page can't use whole is used not at all
            await Give("{\"format\":1,\"state\":\"canceling\",\"run\":" + JsonSerializer.Serialize(S.Key) + ",\"seq\":970000,\"status\":\"Half\",\"bar\":{\"indeterminate\":false,\"value\":1,\"max\":0},\"rows\":[]}");
            Assert.That(await B.Text(StatusText), Is.EqualTo("<img src=x onerror=\"window.__pwned=1\"><b>bold</b>"));
            await B.Until(LiveClass + " === 'hint'", "the updates again");

            //another run's answer: the page says it no longer shows the current search and stops asking
            List<string> Said = await S.Said();
            Assert.That(Said, Has.Count.EqualTo(2), "the unusable answer's warning and the updates again, nothing else");
            Assert.That((Said[0], Said[1]), Is.EqualTo((Said[0], WebResultsLiveWording.Resumed)));
            Assert.That(Said[0], Does.StartWith("Warning: This page can't get"));
            Q.Crafted.Enqueue(S.Crafted("running", 999_999, "Another run", Run: "0000000000000000-9"));
            await B.Until(LiveText + ".indexOf('Warning: This page stopped updating: it no longer shows') === 0", "the out-of-date page");
            Assert.That((await S.Said()).Last(), Is.EqualTo("Warning: " + WebResultsLiveWording.OutOfDate));
            Assert.That(await B.Text(StatusText), Is.Not.EqualTo("Another run"));
            int Asked = B.Requests("handler=progress");
            await Task.Delay(Interval * 8);
            Assert.That(B.Requests("handler=progress"), Is.EqualTo(Asked), "it stops asking");
            Assert.That(await B.Is("Array.prototype.some.call(document.querySelectorAll('a[href=\"/results\"]'), function (A) { return A.textContent === 'Show the latest progress'; })"), Is.True, "the manual way stays");
            Assert.That(S.Steer.Runs, Is.EqualTo(1));
            await S.NoErrorsOrViolations();
        }

        [Test]
        public async Task CancelFromThePage_StaysItsProtectedPost_AndASessionThatEnds_StopsTheUpdatesWithoutShowingMore()
        {
            await using Scene S = await Scene.Start();
            Chrome B = S.B;
            //Cancel is the page's form: a POST with its token, then the page again, which follows the canceling run
            await B.Press(CancelButton);
            Assert.That(S.Steer.Canceled.Wait(TimeSpan.FromSeconds(15)), Is.True, "Cancel stopped the run");
            await B.Until("document.getElementById('results-heading') && !document.getElementById('live')", "the canceled run's results");
            Assert.That(await B.Text(StatusText), Does.StartWith("Canceled."));
            int Asked = B.Requests("handler=progress");
            await Task.Delay(Interval * 8);
            Assert.That(B.Requests("handler=progress"), Is.EqualTo(Asked), "nothing more is asked after the end");
            Assert.That(B.Events.Count(E => E.Method == "Network.requestWillBeSent" && E.Params.GetProperty("type").GetString() == "Fetch"
                && E.Params.GetProperty("request").GetProperty("method").GetString() != "GET"), Is.Zero, "the page's script sends GETs only");

            //a new run, and every session ends (a password change): the run is canceled, and the page says it is signed out
            await S.Begin();
            S.R.Host.Service<WebSessions>().Clear();
            await B.Until(LiveText + ".indexOf('Warning: This page stopped updating: this browser is no longer signed in') === 0", "the signed-out page");
            Assert.That((await S.Said()).Last(), Is.EqualTo("Warning: " + WebResultsLiveWording.SignedOut));
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (S.Steer.Cancels < 2 && Clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                await Task.Delay(20);
            }
            Assert.That(S.Steer.Cancels, Is.EqualTo(2), "the session's run was canceled with it");
            Asked = B.Requests("handler=progress");
            await Task.Delay(Interval * 8);
            Assert.That(B.Requests("handler=progress"), Is.EqualTo(Asked), "it stops asking");
            Assert.That(await B.Is("!document.getElementById('results-heading')"), Is.True, "nothing more of the run is shown");
            Assert.That(S.Steer.Runs, Is.EqualTo(2));
            await S.NoErrorsOrViolations();
        }
    }
}
