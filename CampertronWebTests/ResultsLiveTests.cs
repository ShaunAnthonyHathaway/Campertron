using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The results page's automatic progress (D108; business-rules-settings §34), as the web UI answers it: the page of a run
    // names the run for the web UI's own script (since D109 Assets/live.js, the one of every live page, WebLive), and
    // "?handler=progress" answers the run's state in the page's words (WebLiveAnswer), for the session that started it only. It only looks: asking never starts, repeats or cancels a search,
    // writes nothing, sends nothing and takes no lock. A page of another run, of an earlier start of the web UI or of
    // another session is told so and shown nothing. Cancel stays the page's protected POST, and wins. The script is the web
    // UI's own, for the owner only, under the unchanged Content-Security-Policy. The browser's side (the page updating in
    // place) is ResultsLiveBrowserTests'.
    public class ResultsLiveTests
    {
        internal const string Policy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self'; font-src 'self'; connect-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        internal static readonly string[] AnswerKeys = { "format", "state", "key", "seq", "rank", "view", "fields", "bar" };

        // A stand-in for GS1's run that the test steers: it reports the snapshots the test gives it, one at a time, and ends
        // as the test says or, once canceled, as canceled (as GS1 does). It counts its runs.
        internal sealed class Steered
        {
            private readonly BlockingCollection<(object Step, ManualResetEventSlim Done)> _Steps = new();
            private int _Runs;
            private int _Cancels;
            private long _Sequence;
            internal int Runs => Volatile.Read(ref _Runs);
            // how many runs stopped because they were canceled
            internal int Cancels => Volatile.Read(ref _Cancels);
            internal readonly ManualResetEventSlim Started = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim Canceled = new ManualResetEventSlim(false);

            internal Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult> Run => (Plan, Progress, Token, Options) =>
            {
                Interlocked.Increment(ref _Runs);
                Started.Set();
                while (true)
                {
                    (object Step, ManualResetEventSlim Done) Next;
                    try
                    {
                        Next = _Steps.Take(Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref _Cancels);
                        Canceled.Set();
                        return End(SearchRunStatus.Canceled);
                    }
                    try
                    {
                        switch (Next.Step)
                        {
                            case SearchProgress Snapshot:
                                Progress?.Report(Snapshot);
                                break;
                            case SearchRunResult Result:
                                return Result;
                            case Exception Failure:
                                throw Failure;
                        }
                    }
                    finally
                    {
                        Next.Done.Set();
                    }
                }
            };

            // a snapshot, reported before this returns
            internal void Report(SearchRunStage Stage, int RequestsPlanned = 0, int RequestsCompleted = 0, int CampgroundsPlanned = 0, int CampgroundsPrepared = 0,
                params (string File, SearchState State)[] Searches) =>
                Give(new SearchProgress(Interlocked.Increment(ref _Sequence), Stage, CampgroundsPlanned, CampgroundsPrepared, RequestsPlanned, RequestsCompleted, 0, 0,
                    Searches.Select(S => new SearchProgressItem(S.File, S.State)).ToList()));

            internal void Finish(SearchRunResult Result) => Give(Result, Wait: false);
            internal void Fail(Exception Failure) => Give(Failure, Wait: false);

            private void Give(object Step, bool Wait = true)
            {
                var Done = new ManualResetEventSlim(false);
                _Steps.Add((Step, Done));
                if (Wait)
                {
                    Assert.That(Done.Wait(TimeSpan.FromSeconds(15)), Is.True, "the run took the step");
                }
            }

            internal static SearchRunResult End(SearchRunStatus Status, params SearchResult[] Searches) =>
                new SearchRunResult(Status, DateTime.UtcNow, TimeSpan.FromSeconds(1), 1, 1, 0, Searches, Array.Empty<SearchIssue>());
        }

        // ---- the page, its run and its answers ----

        internal static XElement? Live(WebPage Page) => Element(Page, "live");
        internal static string KeyOf(WebPage Page) => (string)Live(Page)!.Attribute("data-key")!;

        internal static Task<(WebPage Page, JsonElement Answer)> Ask(Browser Who, string? Key) => AskAt(Who, "/results", Key);

        // a live page's answer (D108, D109): Page is the page's address
        internal static async Task<(WebPage Page, JsonElement Answer)> AskAt(Browser Who, string Page, string? Key)
        {
            WebPage Answer = await Who.Get(Page + "?handler=progress" + (Key == null ? string.Empty : "&key=" + Uri.EscapeDataString(Key)));
            Assert.That(Answer.Status, Is.EqualTo(HttpStatusCode.OK), Answer.Body);
            Assert.That(Answer.Header("Content-Type"), Is.EqualTo("application/json; charset=utf-8"));
            using JsonDocument Json = JsonDocument.Parse(Answer.Body);
            return (Answer, Json.RootElement.Clone());
        }

        internal static string State(JsonElement Answer) => Answer.GetProperty("state").GetString()!;
        internal static long Seq(JsonElement Answer) => Answer.GetProperty("seq").GetInt64();
        internal static string? Text(JsonElement Answer, string Name) => Answer.TryGetProperty(Name, out JsonElement Found) ? Found.GetString() : null;
        internal static int Rank(JsonElement Answer) => Answer.GetProperty("rank").GetInt32();
        // a text the answer gives for the page's element of that id, or null
        internal static string? Field(JsonElement Answer, string Id) =>
            Answer.TryGetProperty("fields", out JsonElement Fields) && Fields.TryGetProperty(Id, out JsonElement Found) ? Found.GetString() : null;
        internal static IEnumerable<string> FieldNames(JsonElement Answer) => Answer.TryGetProperty("fields", out JsonElement Fields) ? Fields.EnumerateObject().Select(P => P.Name) : Enumerable.Empty<string>();
        internal static IEnumerable<string> Names(JsonElement Answer) => Answer.EnumerateObject().Select(P => P.Name);

        // a run of the session started from the page, as the page shows it right after Start
        internal static async Task<WebPage> StartRun(Browser Owner)
        {
            WebPage Started = await Act(Owner, "/results?handler=start", Fields(await Owner.Get("/results")));
            Assert.That(Live(Started), Is.Not.Null, "the run goes on: " + Status(Started));
            return Started;
        }

        // ---- the page names its run for its own script ----

        [Test]
        public async Task APageOfARunningSearch_NamesItsRunForTheWebUIsOwnScript_AndNoOtherPageHasAScript()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            using Browser Other = await R.Host.SignedIn();
            WebPage Idle = await Owner.Get("/results");
            Assert.That(Idle.Xml().Descendants("script"), Is.Empty);
            Assert.That(Live(Idle), Is.Null);
            Assert.That(Element(Idle, "live-announce"), Is.Null);

            WebPage Running = await StartRun(Owner);
            XElement Script = Running.Xml().Descendants("script").Single();
            Assert.That(Script.Parent!.Name.LocalName, Is.EqualTo("head"));
            Assert.That(((string?)Script.Attribute("src"), (string?)Script.Attribute("defer"), Script.Value), Is.EqualTo(((string?)WebAssets.LiveScriptPath, (string?)"defer", "")));
            Assert.That(WebAssets.LiveScriptPath, Does.Match("^/assets/live\\.js\\?v=[0-9a-f]{16}$"));
            XElement Live_ = Live(Running)!;
            Assert.That(KeyOf(Running), Does.Match("^[0-9a-f]{16}-1$"), "this web UI's random name and the run's number");
            Assert.That(KeyOf(Running), Does.StartWith(R.Host.Service<WebResultsRuns>().Instance));
            Assert.That(((string?)Live_.Attribute("data-state"), (string?)Live_.Attribute("data-rank")), Is.EqualTo(((string?)"running", (string?)"1")));
            Assert.That((string?)Live_.Attribute("data-seq"), Does.Match("^[0-9]+$"));
            Assert.That((string?)Live_.Attribute("data-view"), Does.Match("^[0-9a-f]{16}$"), "a name of what the page's controls show, never a value");
            Assert.That(((string?)Live_.Attribute("data-interval"), (string?)Live_.Attribute("data-timeout")), Is.EqualTo(("2000", "10000")), "every two seconds; ten seconds for an answer");
            Assert.That(Live_.Value, Is.EqualTo(WebResultsLiveWording.Manual), "until the script runs, and without it: the page as it was sent");
            Assert.That((string?)Live_.Attribute("class"), Is.EqualTo("hint"));
            //the script's words are the page's: the web UI's own, warnings with their severity in words
            Assert.That(new[] { "data-updating", "data-resumed", "data-failing", "data-ended-failing", "data-signed-out", "data-out-of-date" }.Select(A => (string?)Live_.Attribute(A)),
                Is.EqualTo(new[] { WebResultsLiveWording.Updating, WebResultsLiveWording.Resumed, "Warning: " + WebResultsLiveWording.Failing, "Warning: " + WebResultsLiveWording.EndedNotShown,
                    "Warning: " + WebResultsLiveWording.SignedOut, "Warning: " + WebResultsLiveWording.OutOfDate }));
            Assert.That(WebResultsLiveWording.Updating, Does.Contain(WebResultsLiveWording.TimePlaceholder));
            Assert.That(WebResultsLiveWording.Failing, Does.Contain(WebResultsLiveWording.TimePlaceholder).And.Contain("does not stop the search"), "a page's problem is never said as the search's");
            Assert.That(Running.Xml().Descendants("a").Single(A => A.Value == "Show the latest progress").Attribute("href")!.Value, Is.EqualTo("/results"), "the manual way stays");
            XElement Announce = Element(Running, "live-announce")!;
            Assert.That(Announce.Ancestors().Any(A => (string?)A.Attribute("id") == "live-view"), Is.False, "outside the part the script replaces");
            Assert.That(Element(Running, "live-view")!.Descendants().Any(E => (string?)E.Attribute("id") == "status"), Is.True);
            Assert.That(Live_.Ancestors().Any(A => (string?)A.Attribute("id") == "live-view"), Is.True, "a page the script takes again names its own moment");

            //another session's page, the run being canceled's page and the ended run's page
            WebPage OthersPage = await Other.Get("/results");
            Assert.That(OthersPage.Xml().Descendants("script"), Is.Empty, "another session's run: no script, nothing of the run");
            Assert.That(Live(OthersPage), Is.Null);
            Assert.That(OthersPage.Body, Does.Not.Contain(KeyOf(Running)));
            WebPage Canceling = await Act(Owner, "/results?handler=cancel", new[] { ("run", "1") });
            if (Live(Canceling) is XElement Still)
            {
                Assert.That(((string?)Still.Attribute("data-state"), (string?)Still.Attribute("data-rank")), Is.EqualTo(((string?)"canceling", (string?)"2")));
            }
            Assert.That(Steer.Canceled.Wait(TimeSpan.FromSeconds(15)), Is.True);
            WebPage Done = await Ended(Owner);
            Assert.That(Done.Xml().Descendants("script"), Is.Empty, "an ended run: no script");
            Assert.That(Live(Done), Is.Null);
        }

        // ---- the answer: the run's state in the page's words, and nothing else ----

        [Test]
        public async Task TheProgressAnswer_IsTheRunsStateInThePagesWords_AndAskingStartsRepeatsOrWritesNothing()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            File.WriteAllText(R.In("Bravo.yaml"), Search("Second trip"));
            using Browser Owner = await R.Host.SignedIn();
            string Before = Snapshot(R.Folder);
            WebPage Running = await StartRun(Owner);
            Assert.That(Steer.Started.Wait(TimeSpan.FromSeconds(15)), Is.True);
            string Key = KeyOf(Running);
            long Shown = long.Parse((string)Live(Running)!.Attribute("data-seq")!, CultureInfo.InvariantCulture);

            //before any progress: what the page shows
            (WebPage First, JsonElement A) = await Ask(Owner, Key);
            Assert.That(First.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That(First.Header("X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(First.Header("Content-Security-Policy"), Is.EqualTo(Policy), "the policy is unchanged");
            Assert.That((State(A), Text(A, "key"), A.GetProperty("format").GetInt32(), Rank(A)), Is.EqualTo(("running", Key, 2, 1)));
            Assert.That(Seq(A), Is.GreaterThan(Shown), "newer than the page");
            Assert.That(Names(A), Is.SubsetOf(AnswerKeys), "nothing but the page's state");
            Assert.That(Text(A, "view"), Is.EqualTo((string?)Live(Running)!.Attribute("data-view")), "the page's own view: nothing to take again");
            Assert.That(FieldNames(A), Is.EquivalentTo(new[] { "status", "progress-details", "file-0-state", "file-1-state" }), "the page's own elements");
            Assert.That(Field(A, "status"), Is.EqualTo(ResultsWording.Searching(2)));
            Assert.That(Field(A, "progress-details"), Is.EqualTo("Reading the searches · Elapsed 0:00"));
            Assert.That(A.GetProperty("bar").GetProperty("indeterminate").GetBoolean(), Is.True);
            Assert.That((Field(A, "file-0-state"), Field(A, "file-1-state")), Is.EqualTo(((string?)ResultsWording.Waiting, (string?)ResultsWording.Waiting)), "each chosen file's row, by its place on the page");

            //the stage, the counts, the bar and each file's state as the run reports them; the elapsed time by the web UI's clock
            Steer.Report(SearchRunStage.CheckingAvailability, RequestsPlanned: 4, RequestsCompleted: 1, Searches: new[] { ("Alpha.yaml", SearchState.Searching), ("Bravo.yaml", SearchState.Waiting) });
            R.Time.Advance(TimeSpan.FromSeconds(65));
            (_, JsonElement B) = await Ask(Owner, Key);
            Assert.That(Seq(B), Is.GreaterThan(Seq(A)));
            Assert.That(Field(B, "progress-details"), Is.EqualTo("Checking availability at recreation.gov · Availability: 1 of 4 month requests finished · Elapsed 1:05"));
            Assert.That((B.GetProperty("bar").GetProperty("indeterminate").GetBoolean(), B.GetProperty("bar").GetProperty("value").GetDouble(), B.GetProperty("bar").GetProperty("max").GetDouble()),
                Is.EqualTo((false, 1.0, 4.0)));
            Assert.That((Field(B, "file-0-state"), Field(B, "file-1-state")), Is.EqualTo(((string?)"Searching", (string?)"Waiting")));
            Assert.That(Text(B, "view"), Is.EqualTo(Text(A, "view")), "progress changes no control: the same view");
            Steer.Report(SearchRunStage.CheckingAvailability, RequestsPlanned: 4, RequestsCompleted: 3, Searches: new[] { ("Alpha.yaml", SearchState.Complete), ("Bravo.yaml", SearchState.Searching) });
            R.Time.Advance(TimeSpan.FromSeconds(1));
            (_, JsonElement C) = await Ask(Owner, Key);
            Assert.That(Field(C, "progress-details"), Does.Contain("3 of 4 month requests finished").And.EndWith("Elapsed 1:06"));
            Assert.That((Field(C, "file-0-state"), Field(C, "file-1-state")), Is.EqualTo(((string?)"Complete", (string?)"Searching")));

            //asking again and again only looks: one run, nothing written, sent or said, no lock, no Camp.History
            long Last = Seq(C);
            for (int i = 0; i < 25; i++)
            {
                (_, JsonElement Again) = await Ask(Owner, Key);
                Assert.That(Seq(Again), Is.GreaterThan(Last), "every answer is newer than the one before");
                Last = Seq(Again);
                Assert.That(State(Again), Is.EqualTo("running"));
            }
            Assert.That(Steer.Runs, Is.EqualTo(1), "asking never starts or repeats a search");
            Assert.That(Snapshot(R.Folder), Is.EqualTo(Before));
            Assert.That(File.Exists(R.In("Camp.History")) || File.Exists(R.In(MonitoringLock.FileName)), Is.False);
            Assert.That(R.Host.Lines.Concat(R.Host.Warnings).Concat(R.Host.Errors), Is.Empty);
            Assert.That(R.Gov.Requests, Is.Zero);

            //the run's end: the answer says so and shows nothing more of it; the page itself has the results
            Steer.Finish(Steered.End(SearchRunStatus.Completed, Result("Alpha.yaml", new[] { Opening("990000101", new DateTime(2026, 12, 1)) }, Name: "First trip")));
            JsonElement End = default;
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                (_, End) = await Ask(Owner, Key);
                if (State(End) != "running")
                {
                    break;
                }
                await Task.Delay(20);
            }
            Assert.That((State(End), Text(End, "key"), Rank(End)), Is.EqualTo(("ended", Key, 3)));
            Assert.That(Names(End), Is.EquivalentTo(new[] { "format", "state", "key", "seq", "rank" }));
            (_, JsonElement Later) = await Ask(Owner, Key);
            Assert.That(State(Later), Is.EqualTo("ended"), "an ended run stays ended");
            WebPage Done = await Owner.Get("/results");
            Assert.That(Status(Done), Does.StartWith("Finished: 1 opening found in 1 search."));
            Assert.That(Live(Done), Is.Null);
            Assert.That(Steer.Runs, Is.EqualTo(1));
            Assert.That(Snapshot(R.Folder), Is.EqualTo(Before), "a manual search: no Camp.History, no lock, no settings file");
            Assert.That(R.Host.Lines.Concat(R.Host.Warnings).Concat(R.Host.Errors), Is.Empty, "nothing sent, nothing said");
        }

        [TestCase("Completed", "Finished: ")]
        [TestCase("CompletedWithProblems", "Finished with problems: ")]
        [TestCase("Blocked", "Stopped: recreation.gov limited the requests")]
        [TestCase("Failed", "Nothing could be searched.")]
        public async Task EveryEndOfARun_EndsTheRunningState(string How, string Said)
        {
            SearchRunStatus Status_ = Enum.Parse<SearchRunStatus>(How);
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            string Key = KeyOf(await StartRun(Owner));
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("running"));
            if (Status_ == SearchRunStatus.Failed)
            {
                Steer.Fail(new IOException("secret detail " + R.Folder));
            }
            else
            {
                Steer.Finish(Steered.End(Status_, Result("Alpha.yaml", Array.Empty<SearchOpening>(), Name: "First trip")));
            }
            WebPage Done = await Ended(Owner);
            Assert.That(Status(Done), Does.StartWith(Said));
            (WebPage Page, JsonElement Answer) = await Ask(Owner, Key);
            Assert.That((State(Answer), Text(Answer, "key")), Is.EqualTo(("ended", Key)));
            Assert.That(Page.Body, Does.Not.Contain("secret").And.Not.Contain(R.Folder).And.Not.Contain("IOException"));
            if (Status_ == SearchRunStatus.Blocked)
            {
                Assert.That(Element(Done, "cooldown")!.Value, Does.EndWith("Start is available again in 5:00."), "the five minutes after a block stay");
                Assert.That(Disabled(StartButton(Done)), Is.True);
            }
            Assert.That(Steer.Runs, Is.EqualTo(1));
        }

        // ---- Cancel stays the page's protected action, and wins ----

        [Test]
        public async Task Cancel_StaysAProtectedPost_AndOnceAsked_TheRunIsNeverShownRunningAgain()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            WebPage Running = await StartRun(Owner);
            string Key = KeyOf(Running);
            string Run = (string)Button(Running, B => (string?)B.Attribute("formaction") == "/results?handler=cancel").Attribute("value")!;
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("running"));

            //Cancel without the form's token, or from another site, cancels nothing; an answer is no way to cancel
            Assert.That((await Owner.Post("/results?handler=cancel", ("run", Run), ("__RequestVerificationToken", "forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/results?handler=cancel", ("run", Run))).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = R.Host.Root.GetLeftPart(UriPartial.Authority);
            await Owner.Get("/results?handler=progress&key=" + Key + "&handler=cancel");
            await Owner.Get("/results?handler=view&handler=cancel&run=" + Run);
            Assert.That((await Owner.Send(new HttpRequestMessage(HttpMethod.Head, "/results?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Steer.Canceled.IsSet, Is.False, "nothing canceled the run");
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("running"));

            //the page's Cancel: at once the answer says canceling, then ended; never running again
            await Owner.Get("/results");
            WebPage Canceled = await Act(Owner, "/results?handler=cancel", new[] { ("run", Run) });
            Assert.That(Steer.Canceled.Wait(TimeSpan.FromSeconds(15)), Is.True, "the run stopped");
            var Seen = new List<string>();
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Clock.Elapsed < TimeSpan.FromSeconds(15))
            {
                string Now = State((await Ask(Owner, Key)).Answer);
                Seen.Add(Now);
                if (Now == "ended")
                {
                    break;
                }
                await Task.Delay(10);
            }
            Assert.That(Seen.Last(), Is.EqualTo("ended"));
            Assert.That(Seen, Has.None.EqualTo("running"), "after Cancel the run is canceling, then ended");
            for (int i = 0; i < 5; i++)
            {
                Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("ended"));
            }
            Assert.That(Status(await Owner.Get("/results")), Does.StartWith("Canceled."));
            Assert.That(Canceled.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Steer.Runs, Is.EqualTo(1));
        }

        [Test]
        public async Task TheAnswerOfACancelingRun_SaysCanceling_WhileItFinishes()
        {
            await using ResultsHost R = await ResultsHost.Start();
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            R.Gov.Mode = Gov.Stall;
            using Browser Owner = await R.Host.SignedIn();
            WebPage Running = await StartRun(Owner);
            Assert.That(R.Gov.Stalled.Wait(TimeSpan.FromSeconds(15)), Is.True);
            string Key = KeyOf(Running);
            //the stalled request holds the run until it is released; between Cancel and its end the run is canceling
            string Run = (string)Button(Running, B => (string?)B.Attribute("formaction") == "/results?handler=cancel").Attribute("value")!;
            WebPage Canceling = await Act(Owner, "/results?handler=cancel", new[] { ("run", Run) });
            JsonElement Answer = (await Ask(Owner, Key)).Answer;
            Assert.That(State(Answer), Is.EqualTo("canceling").Or.EqualTo("ended"));
            if (State(Answer) == "canceling")
            {
                Assert.That((Field(Answer, "status"), Rank(Answer)), Is.EqualTo(((string?)ResultsWording.Canceling, 2)));
            }
            Assert.That(R.Gov.Released.Wait(TimeSpan.FromSeconds(15)), Is.True);
            await Ended(Owner);
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("ended"));
            Assert.That(Canceling.Status, Is.EqualTo(HttpStatusCode.OK));
        }

        // ---- a page of another run, of an earlier web UI or of nothing is shown nothing ----

        [Test]
        public async Task APageOfAnotherRun_OfAnEarlierWebUI_OrOfNoRun_IsToldSo_AndShownNothingOfARun()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            using Browser Fresh = await R.Host.SignedIn();
            //a session that never showed the page, and one that has no run
            Assert.That(State((await Ask(Fresh, "x")).Answer), Is.EqualTo("replaced"));
            await Owner.Get("/results");
            Assert.That(State((await Ask(Owner, null)).Answer), Is.EqualTo("replaced"));
            for (int i = 0; i < 5; i++)
            {
                await Ask(Owner, "x");
                await Ask(Fresh, null);
            }
            Assert.That(Steer.Runs, Is.Zero, "asking, with no run, never starts one");
            Assert.That(Status(await Owner.Get("/results")), Is.EqualTo(ResultsWording.Idle));

            string Old = KeyOf(await StartRun(Owner));
            Steer.Finish(Steered.End(SearchRunStatus.Completed));
            await Ended(Owner);
            WebPage Second = await StartRun(Owner);
            string Key = KeyOf(Second);
            Assert.That(Key, Is.Not.EqualTo(Old));
            string Instance = Key.Substring(0, Key.IndexOf('-'));
            string[] NotThisRun =
            {
                Old, "", "1", "2", Instance, Instance + "-", Instance + "-1", Instance + "-02", Instance.ToUpperInvariant() + "-2", "0000000000000000-2", Key + " ", Key + "\n",
                new string('a', 5000), "<script>alert(1)</script>", "../../General.yaml",
            };
            foreach (string Asked in NotThisRun)
            {
                (WebPage Page, JsonElement Answer) = await Ask(Owner, Asked);
                Assert.That(State(Answer), Is.EqualTo("replaced"), Asked);
                Assert.That(Names(Answer), Is.EquivalentTo(new[] { "format", "state", "seq" }), "nothing of a run, not even the key it asked for: " + Asked);
                Assert.That(Answer.GetProperty("format").GetInt32(), Is.EqualTo(2));
                Assert.That(Page.Body, Does.Not.Contain(Key).And.Not.Contain("script").And.Not.Contain("General"));
            }
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("running"), "the page of this run gets its state");
            Assert.That(Steer.Runs, Is.EqualTo(2), "two Starts, two runs: no answer started one");
        }

        // ---- only the session that started the run sees it ----

        [Test]
        public async Task OnlyTheSessionThatStartedTheRun_GetsItsState_AndASessionThatEnded_GetsTheSignInAndNothingElse()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            using Browser Other = await R.Host.SignedIn();
            using Browser Visitor = R.Host.Browser();
            WebPage Running = await StartRun(Owner);
            string Key = KeyOf(Running);
            Steer.Report(SearchRunStage.CheckingAvailability, RequestsPlanned: 2, RequestsCompleted: 1, Searches: new[] { ("Alpha.yaml", SearchState.Searching) });

            //another signed-in session: told its page shows no run of its own; one run in the web UI
            await Other.Get("/results");
            (WebPage OthersAnswer, JsonElement Told) = await Ask(Other, Key);
            Assert.That(State(Told), Is.EqualTo("replaced"));
            Assert.That(OthersAnswer.Body, Does.Not.Contain("Checking availability").And.Not.Contain("Alpha").And.Not.Contain(Key));
            WebPage Refused = await Act(Other, "/results?handler=start", Fields(await Other.Get("/results")));
            Assert.That(Status(Refused), Is.EqualTo(ResultsWording.NotStartedOtherSession));
            Assert.That(Steer.Runs, Is.EqualTo(1), "one search at a time in the web UI");

            //a browser that is not signed in: the sign-in, nothing of the run
            WebPage Visit = await Visitor.Get("/results?handler=progress&key=" + Key);
            Assert.That(Visit.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Visit.Location, Does.StartWith("/signin?returnUrl="));
            Assert.That(Visit.Body, Does.Not.Contain("Checking").And.Not.Contain(Key).And.Not.Contain("state"));

            //the wrong host name is refused before anything else
            Owner.Headers["Host"] = "evil.example";
            WebPage Host = await Owner.Get("/results?handler=progress&key=" + Key);
            Assert.That(Host.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(Host.Body, Does.Not.Contain("Checking").And.Not.Contain(Key));
            Owner.Headers.Remove("Host");
            Assert.That(Field((await Ask(Owner, Key)).Answer, "progress-details"), Does.StartWith("Checking availability"));

            //the owner signs out: its run is canceled, and its page's next question gets the sign-in
            WebPage Out = await Owner.Post("/signout");
            Assert.That(Out.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(Steer.Canceled.Wait(TimeSpan.FromSeconds(15)), Is.True, "a session that ends cancels its run");
            WebPage After = await Owner.Get("/results?handler=progress&key=" + Key);
            Assert.That((After.Status, After.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)));
            Assert.That(After.Body, Does.Not.Contain("Checking").And.Not.Contain(Key));
            Assert.That(Steer.Runs, Is.EqualTo(1));
        }

        [Test]
        public async Task ASessionThatExpires_GetsTheSignInForItsPagesQuestion()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            string Key = KeyOf(await StartRun(Owner));
            Assert.That(State((await Ask(Owner, Key)).Answer), Is.EqualTo("running"));
            //the idle time is the web UI's: asking never renews it (D109; LiveSessionTests), and a session past it has ended
            R.Host.Now += WebSessions.IdleTimeout;
            WebPage After = await Owner.Get("/results?handler=progress&key=" + Key);
            Assert.That((After.Status, After.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)));
            Assert.That(Steer.Canceled.Wait(TimeSpan.FromSeconds(15)), Is.True, "the session ended, and its run with it");
        }

        // ---- the request checks and the limits stay ----

        [Test]
        public async Task TheProgressQuestion_KeepsEveryRequestCheck_AndCountsInTheClientsLimit()
        {
            var Steer = new Steered();
            await using ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
            File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
            using Browser Owner = await R.Host.SignedIn();
            string Key = KeyOf(await StartRun(Owner));

            //the answer is a GET's only: a POST of that name is the page's choice, with its token and Origin, and starts nothing
            Assert.That((await Owner.Post("/results?handler=progress&key=" + Key, ("revision", "1"), ("__RequestVerificationToken", "forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/results?handler=progress&key=" + Key, ("revision", "1"))).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = R.Host.Root.GetLeftPart(UriPartial.Authority);
            await Owner.Get("/results");
            WebPage Posted = await Owner.Post("/results?handler=progress&key=" + Key, ("revision", "1"), ("action", "all"));
            Assert.That((Posted.Status, Posted.Location), Is.EqualTo((HttpStatusCode.Redirect, "/results")));
            foreach (string Method in new[] { "PUT", "DELETE", "PATCH", "OPTIONS" })
            {
                Assert.That((await Owner.Send(new HttpRequestMessage(new HttpMethod(Method), "/results?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), Method);
            }
            //no CORS: another site's page gets no permission to read an answer
            Owner.Headers["Origin"] = "http://evil.example";
            WebPage Cross = await Owner.Get("/results?handler=progress&key=" + Key);
            Owner.Headers.Remove("Origin");
            Assert.That(Cross.Header("Access-Control-Allow-Origin") + Cross.Header("Access-Control-Allow-Credentials"), Is.Empty);
            Assert.That(Steer.Runs, Is.EqualTo(1));

            //every question counts in the client's limit of requests (RequestsPerClient in a window): a page can't flood the
            //web UI, and is then refused in the fixed words
            int Answered = 0;
            WebPage Last;
            do
            {
                Last = await Owner.Get("/results?handler=progress&key=" + Key);
                Answered++;
            }
            while (Last.Status == HttpStatusCode.OK && Answered <= CampertronWebHost.RequestsPerClient + 1);
            Assert.That(Last.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Answered, Is.LessThanOrEqualTo(CampertronWebHost.RequestsPerClient));
            Assert.That(Last.Text(), Does.Contain("Campertron received too many requests from this address."));
            Assert.That(Last.Header("Content-Security-Policy"), Is.EqualTo(Policy));
            Assert.That(Steer.Runs, Is.EqualTo(1));
        }

        // ---- the script: the web UI's own, for the owner only ----

        [Test]
        public async Task TheScript_IsTheWebUIsOwnFile_ForASignedInSessionOnly_UnderTheSamePolicy()
        {
            await using ResultsHost R = await ResultsHost.Start();
            using Browser Visitor = R.Host.Browser();
            using Browser Owner = await R.Host.SignedIn();
            foreach (string Address in new[] { WebAssets.LiveScriptPath, "/assets/live.js" })
            {
                WebPage Refused = await Visitor.Get(Address);
                Assert.That((Refused.Status, Refused.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)), Address);
                Assert.That(Refused.Body, Does.Not.Contain("fetch"));
            }
            WebPage Script = await Owner.Get(WebAssets.LiveScriptPath);
            Assert.That(Script.Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Script.Header("Content-Type"), Is.EqualTo("text/javascript; charset=utf-8"));
            Assert.That(Script.Header("X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(Script.Header("Cache-Control").Split(", ").Order(StringComparer.Ordinal), Is.EqualTo(new[] { "immutable", "max-age=31536000", "private" }), "kept by the browser, never by a shared cache");
            Assert.That(Script.Header("Content-Security-Policy"), Is.EqualTo(Policy));
            Assert.That((await Owner.Get("/assets/live.js")).Header("Cache-Control"), Is.EqualTo("no-cache"), "without the current version: checked again");
            Assert.That((await Owner.Get("/assets/results.js")).Status, Is.EqualTo(HttpStatusCode.NotFound), "D108's name is gone: one script");
            using (Stream Embedded = typeof(WebAssets).Assembly.GetManifestResourceStream("CampertronWeb.Assets.live.js")!)
            using (var Copy = new MemoryStream())
            {
                Embedded.CopyTo(Copy);
                Assert.That(Script.Bytes, Is.EqualTo(Copy.ToArray()), "the embedded file, byte for byte");
            }
            Assert.That((await Owner.Get("/assets/site.css")).Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await Visitor.Get("/assets/site.css")).Status, Is.EqualTo(HttpStatusCode.OK), "the stylesheet stays anonymous for the sign-in");
            Assert.That((await Visitor.Get("/assets/other.js")).Status, Is.EqualTo(HttpStatusCode.NotFound));

            //what the script may do: ask the page's own address with a GET, for its two handlers only; set text and put the
            //web UI's own parts in place; never run or write markup from an answer, store anything or reach another site
            string Code = Script.Body;
            string WithoutComments = Regex.Replace(Code, "(?m)^\\s*//.*$", string.Empty);
            foreach (string Never in new[] { "innerHTML", "outerHTML", "insertAdjacentHTML", "document.write", "eval(", "Function(", "localStorage", "sessionStorage", "document.cookie",
                "XMLHttpRequest", "WebSocket", "EventSource", "postMessage", "http:", "https:", "'POST'", "\"POST\"", "location.reload", "location.assign", "location.replace", "location.href =", ".submit(", ".click(" })
            {
                Assert.That(WithoutComments, Does.Not.Contain(Never), Never);
            }
            Assert.That(Regex.Matches(WithoutComments, "window\\.fetch\\(").Count, Is.EqualTo(1), "one place asks");
            Assert.That(Regex.Matches(WithoutComments, "'[^'\\n]*/[^'\\n]*'").Select(M => M.Value).Distinct(), Is.EquivalentTo(new[] { "'application/json'", "'text/html'" }),
                "no address of its own (the page's), only the two kinds of answer it takes");
            Assert.That(WithoutComments, Does.Not.Contain("\"").And.Not.Contain("`"), "every text in single quotes, so the check above sees them all");
            Assert.That(Regex.Matches(WithoutComments, "location\\.[a-zA-Z]+").Select(M => M.Value).Distinct(), Is.EquivalentTo(new[] { "location.search", "location.pathname" }), "the page's own address only");
            Assert.That(Regex.Matches(WithoutComments, "address\\('([a-z]+)'").Select(M => M.Groups[1].Value).Distinct(), Is.EquivalentTo(new[] { "progress", "view" }), "its page's two handlers");
            Assert.That(WithoutComments, Does.Contain("Query.set('handler', Handler)"));
            Assert.That(WithoutComments, Does.Contain("method: 'GET'").And.Contain("redirect: 'manual'").And.Contain("credentials: 'same-origin'"));
            Assert.That(Code, Does.Contain("'use strict'"));
            Assert.That(Code.Length, Is.LessThan(24 * 1024), "a small script");
        }
    }
}
