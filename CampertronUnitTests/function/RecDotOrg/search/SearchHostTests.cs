using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // The interactive run's bounds (GS1, decision D45): at most three requests per month whatever goes wrong, typed
    // failures that never read as months without availability, a block (the REQUEST BLOCKED page or HTTP 429) that stops
    // further requests and keeps what was received, prompt cancellation everywhere, every search waiting for a shared month
    // released, value-free problems, truthful progress, and the production transport. recreation.gov is FakeGov; the
    // production transport is checked against a stand-in on the loopback address.
    public class SearchHostTests : SearchTestBase
    {
        private List<string> Sites = new();
        private static readonly TimeSpan Prompt = TimeSpan.FromSeconds(1);

        [SetUp]
        public void SetUp()
        {
            Sites = SearchFixture.SiteIds(SearchFixture.Watchman, 4);
        }

        private string MonthOf(DateTime Month, Func<string, int, bool>? Available = null) => MonthJson(Month, Sites, Available ?? ((Id, Day) => Day <= 2));

        private string OneMonth(string Name = "Trip.yaml") => WriteSearch(Name, SearchFixture.Watchman, MonthA, MonthA.AddDays(9));

        private static SearchResponse Ok(string Body) => new SearchResponse(200, Body);

        // ---- failures, retries and blocks ----

        [Test]
        public void APartlyFailedSearch_KeepsItsCheckedMonths_AndNamesTheOthers()
        {
            string File1 = WriteSearch("Two.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(9));
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
            Gov.Answer(Url(SearchFixture.Watchman, MonthB), (R, T) => throw new HttpRequestException("connection refused"));

            SearchRunResult Result = RunOf(PlanOf(File1));

            SearchResult Search = Result.Searches.Single();
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.CompletedWithProblems));
            Assert.That(Search.State, Is.EqualTo(SearchState.Incomplete));
            Assert.That(Search.Months, Is.EqualTo(new[] { new SearchMonthResult(MonthA, true, null), new SearchMonthResult(MonthB, false, SearchIssueCode.NetworkFailure) }));
            Assert.That(Search.Openings, Has.Count.EqualTo(Sites.Count * 2));
            Assert.That(Search.Openings.All(O => O.Night.Month == MonthA.Month), "nothing of the failed month, and it is not a month without availability");
            SearchIssue Issue = Search.Issues.Single();
            Assert.That((Issue.Code, Issue.Month, Issue.Attempts), Is.EqualTo((SearchIssueCode.NetworkFailure, (DateTime?)MonthB, 3)));
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthB)), Is.EqualTo(3));
            Assert.That(Search.RequestsPlanned, Is.EqualTo(2));
            Assert.That(Search.RequestsCompleted, Is.EqualTo(2));
            Assert.That(Result.RequestsFailed, Is.EqualTo(1));
        }

        [Test]
        public void ACompleteFailure_IsAFailedRun_WithoutOpenings()
        {
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => throw new IOException("reset"));

            SearchRunResult Result = RunOf(PlanOf(File1));

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Failed));
            Assert.That(Result.Searches.Single().State, Is.EqualTo(SearchState.Failed));
            Assert.That(Result.Searches.Single().Openings, Is.Empty);
            Assert.That(Result.Searches.Single().Months.Single(), Is.EqualTo(new SearchMonthResult(MonthA, false, SearchIssueCode.NetworkFailure)));
        }

        [Test]
        public void TheBlockPage_StopsFurtherRequests_KeepsWhatCame_AndNeverExits()
        {
            //A arrives, B is the block page, C is never asked for
            string File1 = WriteSearch("Three.yaml", SearchFixture.Watchman, MonthA, MonthC.AddDays(4));
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
            Gov.Answer(Url(SearchFixture.Watchman, MonthB), (R, T) => Ok("<html><body><h1>Request blocked.</h1></body></html>"));
            Gov.Json(Url(SearchFixture.Watchman, MonthC), MonthOf(MonthC));

            SearchRunResult Result = RunOf(PlanOf(File1));

            SearchResult Search = Result.Searches.Single();
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Blocked));
            Assert.That(Result.Issues.Single().Code, Is.EqualTo(SearchIssueCode.RequestBlocked));
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthB)), Is.EqualTo(1), "a block is not tried again");
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthC)), Is.Zero, "no request after the block");
            Assert.That(Search.Months.Select(M => (M.Checked, M.Reason)), Is.EqualTo(new[]
            {
                (true, (SearchIssueCode?)null), (false, SearchIssueCode.RequestBlocked), (false, SearchIssueCode.RequestBlocked)
            }));
            Assert.That(Search.State, Is.EqualTo(SearchState.Incomplete));
            Assert.That(Search.Openings, Has.Count.EqualTo(Sites.Count * 2), "what came before the block is kept");
            //the console's wait and exit (AvailabilityApi.BlockedWait/ExitProcess) are never taken: the base checks ExitCalls
        }

        [Test]
        public void Http429_IsABlockToo()
        {
            string File1 = WriteSearch("Two.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(4));
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(429, "{\"error\":\"Too Many Requests\"}"));
            Gov.Json(Url(SearchFixture.Watchman, MonthB), MonthOf(MonthB));

            SearchRunResult Result = RunOf(PlanOf(File1));

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Blocked));
            Assert.That(Result.Issues.Single().Code, Is.EqualTo(SearchIssueCode.RateLimited));
            Assert.That(Result.Issues.Single().StatusCode, Is.EqualTo(429));
            Assert.That(Gov.Requests, Has.Count.EqualTo(1), "one request, no retry, nothing after it");
            Assert.That(Result.Searches.Single().State, Is.EqualTo(SearchState.Failed));
            Assert.That(Result.Searches.Single().Months.Select(M => M.Reason), Is.All.EqualTo(SearchIssueCode.RateLimited));
        }

        [Test]
        public void ARetry_ThatSucceeds_IsAComplete()
        {
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => R.Attempt == 1 ? throw new HttpRequestException("once") : Ok(MonthOf(MonthA)));

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Openings, Has.Count.EqualTo(Sites.Count * 2));
            Assert.That(Gov.Requests.Select(R => R.Attempt), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Search.Issues, Is.Empty);
        }

        // every retryable outcome ends after three requests in all (the console would try again forever)
        [TestCase("network", "NetworkFailure")]
        [TestCase("heavy", "HeavyTraffic")]
        [TestCase("malformed", "MalformedResponse")]
        [TestCase("empty", "MalformedResponse")]
        [TestCase("count", "UnexpectedResponse")]
        [TestCase("error", "UnexpectedResponse")]
        [TestCase("array", "UnexpectedResponse")]
        [TestCase("503", "HttpStatus")]
        [TestCase("mixed", "HeavyTraffic")]
        public void EveryRetryableOutcome_EndsAfterThreeRequestsInAll(string Kind, string ExpectedCode)
        {
            SearchIssueCode Expected = Enum.Parse<SearchIssueCode>(ExpectedCode);
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => Kind switch
            {
                "network" => throw new HttpRequestException("refused"),
                "heavy" => Ok("{\"campsites\":\"We are currently experiencing heavy traffic on our site\"}"),
                "malformed" => Ok("<html>not json"),
                "empty" => Ok(""),
                "count" => Ok("{\"count\":0}"),
                "error" => Ok("{\"error\":\"something\"}"),
                "array" => Ok("[]"),
                "503" => new SearchResponse(503, "{}"),
                //heavy traffic, then a failure, then heavy traffic: still three in all
                _ => R.Attempt == 2 ? throw new TimeoutException() : Ok("{\"campsites\":\"We are currently experiencing heavy traffic on our site\"}")
            });

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Gov.Requests.Select(R => R.Attempt), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Search.Months.Single(), Is.EqualTo(new SearchMonthResult(MonthA, false, Expected)));
            Assert.That(Search.Issues.Single().Attempts, Is.EqualTo(3));
            Assert.That(Search.State, Is.EqualTo(SearchState.Failed));
        }

        // what is not worth another request is asked for once
        [TestCase(404, "HttpStatus")]
        [TestCase(403, "HttpStatus")]
        public void AClientError_IsAskedOnce(int Status, string ExpectedCode)
        {
            SearchIssueCode Expected = Enum.Parse<SearchIssueCode>(ExpectedCode);
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(Status, "{\"error\":\"no\"}"));

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
            Assert.That(Search.Issues.Single().Code, Is.EqualTo(Expected));
            Assert.That(Search.Issues.Single().StatusCode, Is.EqualTo(Status));
        }

        [Test]
        public void ATooLargeAnswer_IsRefused_AndNotAskedAgain()
        {
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => Ok(MonthOf(MonthA)));

            SearchResult Search = RunOf(PlanOf(File1), Options(MaxBytes: 1000)).Searches.Single();

            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
            Assert.That(Search.Months.Single().Reason, Is.EqualTo(SearchIssueCode.ResponseTooLarge));
        }

        [Test]
        public void AValidEmptyMonth_IsChecked()
        {
            string File1 = OneMonth();
            Gov.Json(Url(SearchFixture.Watchman, MonthA), "{\"campsites\":{},\"count\":0}");

            SearchResult Search = RunOf(PlanOf(File1)).Searches.Single();

            Assert.That(Search.State, Is.EqualTo(SearchState.Complete));
            Assert.That(Search.Months.Single().Checked, Is.True);
            Assert.That(Search.Openings, Is.Empty);
            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void ARequestThatHangs_TimesOut_ThreeTimes()
        {
            string File1 = OneMonth();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) =>
            {
                T.WaitHandle.WaitOne();
                T.ThrowIfCancellationRequested();
                return Ok("{}");
            });
            Stopwatch Clock = Stopwatch.StartNew();

            SearchResult Search = RunOf(PlanOf(File1), Options(Timeout: TimeSpan.FromMilliseconds(150))).Searches.Single();

            Assert.That(Search.Months.Single().Reason, Is.EqualTo(SearchIssueCode.Timeout));
            Assert.That(Gov.Requests, Has.Count.EqualTo(3));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }

        [Test]
        public void AFailedSharedMonth_ReleasesEverySearchWaitingForIt()
        {
            //three searches of one month: one fetches, two wait; the fetch fails for good, and every one ends
            string[] Files = { OneMonth("One.yaml"), OneMonth("Two.yaml"), OneMonth("Three.yaml") };
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) =>
            {
                Thread.Sleep(50);
                throw new HttpRequestException("down");
            });

            SearchRunResult Result = RunOf(PlanOf(Files));

            Assert.That(Gov.Requests, Has.Count.EqualTo(3), "one month, three attempts in all, shared");
            Assert.That(Result.Searches.Select(S => S.Months.Single().Reason), Is.All.EqualTo(SearchIssueCode.NetworkFailure));
            Assert.That(Result.Searches.Select(S => S.State), Is.All.EqualTo(SearchState.Failed));
        }

        [Test]
        public void Problems_HoldNoValueOfTheAnswerTheExceptionOrTheFile()
        {
            string File1 = WriteSearch("Secret.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(2), DisplayName: "SENTINEL-NAME");
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => throw new HttpRequestException("SENTINEL-EXCEPTION"));
            Gov.Answer(Url(SearchFixture.Watchman, MonthB), (R, T) => Ok("{\"SENTINEL-BODY\": [1, 2"));

            SearchRunResult Result = RunOf(PlanOf(File1));

            string Messages = CampsiteSearchTests.Describe(Result);
            Assert.That(Messages, Does.Not.Contain("SENTINEL"));
            Assert.That(Result.Searches.SelectMany(S => S.Issues).Select(I => I.Message),
                Is.All.Matches<string>(M => Enum.GetValues<SearchIssueCode>().Any(C => CampsiteSearch.Message(C) == M)), "every message is a fixed text");
            Assert.That(Result.Searches.Single().DisplayName, Is.EqualTo("SENTINEL-NAME"), "the search's own name stays its name");
        }

        // ---- cancellation ----

        [Test]
        public void CanceledBeforeTheRun_MakesNoRequest()
        {
            string File1 = OneMonth();
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
            using var Cancel = new CancellationTokenSource();
            Cancel.Cancel();

            SearchRunResult Result = RunOf(PlanOf(File1), Token: Cancel.Token);

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Result.Searches.Single().State, Is.EqualTo(SearchState.Canceled));
            Assert.That(Result.Searches.Single().Months.Single(), Is.EqualTo(new SearchMonthResult(MonthA, false, SearchIssueCode.Canceled)));
            Assert.That(Gov.Requests, Is.Empty);
        }

        // cancels when Started is set and returns how long the run took after the cancel
        private TimeSpan CancelAfter(ManualResetEventSlim Started, SearchRunPlan Plan, SearchHostOptions With, out SearchRunResult Result, IProgress<SearchProgress>? Progress = null, CancellationTokenSource? Source = null)
        {
            using CancellationTokenSource Cancel = Source ?? new CancellationTokenSource();
            Task<SearchRunResult> Running = Task.Run(() => CampsiteSearch.Run(Plan, Progress, Cancel.Token, With));
            Assert.That(Started.Wait(TimeSpan.FromSeconds(60)), Is.True, "the run reached the point to cancel at");
            Stopwatch Clock = Stopwatch.StartNew();
            Cancel.Cancel();
            Assert.That(Running.Wait(TimeSpan.FromSeconds(60)), Is.True);
            TimeSpan Took = Clock.Elapsed;
            Result = Running.Result;
            return Took;
        }

        [Test]
        public void CanceledDuringARequest_EndsPromptly()
        {
            string File1 = OneMonth();
            using var Started = new ManualResetEventSlim();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) =>
            {
                Started.Set();
                T.WaitHandle.WaitOne();
                T.ThrowIfCancellationRequested();
                return Ok("{}");
            });

            TimeSpan Took = CancelAfter(Started, PlanOf(File1), Options(), out SearchRunResult Result);

            Assert.That(Took, Is.LessThan(Prompt));
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void CanceledDuringTheWaitBeforeARetry_EndsPromptly()
        {
            string File1 = OneMonth();
            using var Waiting = new ManualResetEventSlim();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) => throw new HttpRequestException("down"));
            var Progress = new ProgressLog(P =>
            {
                if (P.Stage == SearchRunStage.Retrying)
                {
                    Waiting.Set();
                }
            });

            TimeSpan Took = CancelAfter(Waiting, PlanOf(File1), Options(RetryDelay: TimeSpan.FromSeconds(30)), out SearchRunResult Result, Progress);

            Assert.That(Took, Is.LessThan(Prompt), "the 30-second wait ends at once");
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void CanceledWhileWaitingForASharedMonth_EndsPromptly()
        {
            string[] Files = { OneMonth("Fetches.yaml"), OneMonth("Waits.yaml"), OneMonth("WaitsToo.yaml") };
            using var Started = new ManualResetEventSlim();
            using var Searching = new ManualResetEventSlim();
            Gov.Answer(Url(SearchFixture.Watchman, MonthA), (R, T) =>
            {
                //the other searches are waiting for this month before the run is canceled
                Searching.Wait(TimeSpan.FromSeconds(5));
                Thread.Sleep(100);
                Started.Set();
                T.WaitHandle.WaitOne();
                T.ThrowIfCancellationRequested();
                return Ok("{}");
            });
            var Progress = new ProgressLog(P =>
            {
                if (P.Searches.Count(S => S.State == SearchState.Searching) == 3)
                {
                    Searching.Set();
                }
            });

            TimeSpan Took = CancelAfter(Started, PlanOf(Files), Options(), out SearchRunResult Result, Progress);

            Assert.That(Took, Is.LessThan(Prompt));
            Assert.That(Result.Searches.Select(S => S.State), Is.All.EqualTo(SearchState.Canceled));
            Assert.That(Gov.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void CanceledWhileTheResultsAreFiltered_EndsPromptly()
        {
            //every campsite available every day, and an attribute filter of 3,000 values that match nothing: filtering the
            //month takes seconds; it is canceled as soon as the month arrived, so only the checkpoints inside the month's
            //filtering can end it promptly
            List<string> All = SearchFixture.SiteIds(SearchFixture.Watchman);
            string Slow = "includeAttributes:\n" + string.Concat(Enumerable.Range(0, 3000).Select(i => "- 'No such attribute " + i + ":x'\n"));
            string File1 = WriteSearch("Big.yaml", SearchFixture.Watchman, MonthA, MonthA.AddMonths(1).AddDays(-1), Extra: Slow);
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthJson(MonthA, All, (Id, Day) => true));
            using var Arrived = new ManualResetEventSlim();
            using var Cancel = new CancellationTokenSource();
            Stopwatch Clock = new Stopwatch();
            var Progress = new ProgressLog(P =>
            {
                if (P.RequestsCompleted == 1 && !Arrived.IsSet)
                {
                    Clock.Start();
                    Cancel.Cancel();
                    Arrived.Set();
                }
            });

            Task<SearchRunResult> Running = Task.Run(() => CampsiteSearch.Run(PlanOf(File1), Progress, Cancel.Token, Options()));
            Assert.That(Running.Wait(TimeSpan.FromSeconds(60)), Is.True);
            Clock.Stop();

            Assert.That(Arrived.IsSet, Is.True);
            Assert.That(Clock.Elapsed, Is.LessThan(Prompt), "the filtering stops at its next campsite");
            Assert.That(Running.Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Running.Result.Searches.Single().Openings, Is.Empty);
        }

        [Test]
        public void CanceledWhileTheResultsAreBuilt_EndsCanceled()
        {
            string File1 = OneMonth();
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
            using var Cancel = new CancellationTokenSource();
            var Progress = new ProgressLog(P =>
            {
                if (P.Stage == SearchRunStage.BuildingResults)
                {
                    Cancel.Cancel();
                }
            });

            SearchRunResult Result = CampsiteSearch.Run(PlanOf(File1), Progress, Cancel.Token, Options());

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Result.Searches.Single().State, Is.EqualTo(SearchState.Canceled));
            Assert.That(Progress.Snapshots.Last().Stage, Is.EqualTo(SearchRunStage.Canceled));
        }

        [Test]
        public void CanceledWhileTheCampsiteDetailsArePrepared_LeavesNoCacheThatLooksComplete()
        {
            //a settings folder with the campground data (a link to the isolated run's database) and an empty cache
            string Cache = Path.Join(ConfigPath, "..", "cache-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.CreateSymbolicLink(Path.Join(ConfigPath, "RecreationDotOrg.db"), Path.Join(SearchFixture.InstalledConfigPath, "RecreationDotOrg.db"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
            {
                Assert.Ignore("symbolic links are not available here");
            }
            Directory.CreateDirectory(Cache);
            try
            {
                string File1 = OneMonth();
                Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
                SearchRunPlan Plan = CampsiteSearch.Plan(new SearchRunRequest(ConfigPath, Cache, new[] { File1 }), DateTime.Now);
                using var Cancel = new CancellationTokenSource();
                var Progress = new ProgressLog(P =>
                {
                    if (P.Stage == SearchRunStage.PreparingCampsiteDetails)
                    {
                        Cancel.Cancel();
                    }
                });

                SearchRunResult Canceled = CampsiteSearch.Run(Plan, Progress, Cancel.Token, Options());

                Assert.That(Canceled.Status, Is.EqualTo(SearchRunStatus.Canceled));
                Assert.That(File.Exists(Path.Join(Cache, SearchFixture.Watchman + "-Cached.json")), Is.False, "no completion marker");
                Assert.That(Gov.Requests, Is.Empty);

                //the next run builds the campground's details again and searches
                SearchRunResult Again = RunOf(CampsiteSearch.Plan(new SearchRunRequest(ConfigPath, Cache, new[] { File1 }), DateTime.Now));
                Assert.That(Again.Status, Is.EqualTo(SearchRunStatus.Completed));
                Assert.That(File.Exists(Path.Join(Cache, SearchFixture.Watchman + "-Cached.json")), Is.True);
                Assert.That(Again.Searches.Single().Openings, Has.Count.EqualTo(Sites.Count * 2));
            }
            finally
            {
                Directory.Delete(Cache, true);
            }
        }

        [Test]
        public void WithoutTheCampgroundData_TheDetailsAreNotPrepared_AndNoDatabaseIsCreated()
        {
            string Cache = Path.Join(ConfigPath, "..", "cache-" + Guid.NewGuid().ToString("N"));
            try
            {
                string File1 = OneMonth();
                SearchRunResult Result = RunOf(CampsiteSearch.Plan(new SearchRunRequest(ConfigPath, Cache, new[] { File1 }), DateTime.Now));

                Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Failed));
                Assert.That(Result.Searches.Single().Issues.Single().Code, Is.EqualTo(SearchIssueCode.CampsiteDataMissing));
                Assert.That(File.Exists(Path.Join(ConfigPath, "RecreationDotOrg.db")), Is.False, "opening a missing database would create an empty one");
                Assert.That(Gov.Requests, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(Cache))
                {
                    Directory.Delete(Cache, true);
                }
            }
        }

        // ---- progress ----

        [Test]
        public void Progress_IsTruthful_AndInOrder()
        {
            string Single = OneMonth("One.yaml");
            string Two = WriteSearch("Two.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(3));
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));
            Gov.Answer(Url(SearchFixture.Watchman, MonthB), (R, T) => R.Attempt == 1 ? throw new HttpRequestException("once") : Ok(MonthOf(MonthB)));
            var Progress = new ProgressLog();

            SearchRunResult Result = RunOf(PlanOf(Single, Two), Progress: Progress);

            List<SearchProgress> All = Progress.Snapshots;
            Assert.That(All.Select(P => P.Sequence), Is.EqualTo(Enumerable.Range(1, All.Count).Select(N => (long)N)), "one snapshot per change, in order");
            Assert.That(All.First().Stage, Is.EqualTo(SearchRunStage.Validating));
            Assert.That(All.First().Searches.Select(S => S.State), Is.All.EqualTo(SearchState.Waiting));
            List<SearchRunStage> Stages = All.Select(P => P.Stage).Distinct().ToList();
            Assert.That(Stages, Is.EqualTo(new[] { SearchRunStage.Validating, SearchRunStage.PreparingCampsiteDetails, SearchRunStage.CheckingAvailability,
                SearchRunStage.Retrying, SearchRunStage.BuildingResults, SearchRunStage.Completed }));
            Assert.That(All.Select(P => P.RequestsCompleted), Is.Ordered);
            Assert.That(All.Where(P => P.Stage >= SearchRunStage.CheckingAvailability).Select(P => P.RequestsPlanned), Is.All.EqualTo(2), "two unique months");
            Assert.That(All.Last().RequestsCompleted, Is.EqualTo(2));
            Assert.That(All.Last().CampgroundsPlanned, Is.EqualTo(1));
            Assert.That(All.Last().CampgroundsPrepared, Is.EqualTo(1));
            Assert.That(All.Last().Searches.Select(S => S.State), Is.All.EqualTo(SearchState.Complete));
            Assert.That(All.Count, Is.LessThan(40), "a snapshot per change, never per campsite or day");
            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
        }

        [Test]
        public void AFailingProgressHandler_DoesNotEndTheRun()
        {
            string File1 = OneMonth();
            Gov.Json(Url(SearchFixture.Watchman, MonthA), MonthOf(MonthA));

            SearchRunResult Result = RunOf(PlanOf(File1), Progress: new ProgressLog(P => throw new InvalidOperationException("handler")));

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Result.Defects, Is.Not.Empty, "the handler's failure is kept for diagnostics");
        }

        // ---- the production transport (one HttpClient per run) against a stand-in on the loopback address ----

        private sealed class LoopbackServer : IDisposable
        {
            private readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly Task Serving;
            internal LoopbackServer(Func<string, (int Status, string Body, bool Chunked, int DelayMs)> Respond)
            {
                Listener.Start();
                Serving = Task.Run(() =>
                {
                    while (true)
                    {
                        TcpClient Client;
                        try
                        {
                            Client = Listener.AcceptTcpClient();
                        }
                        catch (SocketException)
                        {
                            return;
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                        _ = Task.Run(() =>
                        {
                            using (Client)
                            {
                                try
                                {
                                    NetworkStream Stream = Client.GetStream();
                                    var Request = new StringBuilder();
                                    byte[] Buffer = new byte[4096];
                                    while (!Request.ToString().Contains("\r\n\r\n"))
                                    {
                                        int Read = Stream.Read(Buffer, 0, Buffer.Length);
                                        if (Read == 0)
                                        {
                                            return;
                                        }
                                        Request.Append(Encoding.ASCII.GetString(Buffer, 0, Read));
                                    }
                                    string Path = Request.ToString().Split(' ')[1];
                                    (int Status, string Body, bool Chunked, int DelayMs) = Respond(Path);
                                    Thread.Sleep(DelayMs);
                                    byte[] Content = Encoding.UTF8.GetBytes(Body);
                                    string Head = "HTTP/1.1 " + Status + " X\r\nContent-Type: application/json\r\nConnection: close\r\n" +
                                        (Chunked ? "Transfer-Encoding: chunked\r\n\r\n" : "Content-Length: " + Content.Length + "\r\n\r\n");
                                    Stream.Write(Encoding.ASCII.GetBytes(Head));
                                    if (Chunked)
                                    {
                                        Stream.Write(Encoding.ASCII.GetBytes(Content.Length.ToString("X") + "\r\n"));
                                        Stream.Write(Content);
                                        Stream.Write(Encoding.ASCII.GetBytes("\r\n0\r\n\r\n"));
                                    }
                                    else
                                    {
                                        Stream.Write(Content);
                                    }
                                }
                                catch (IOException)
                                {
                                    //the client gave up (a timeout or a cancellation)
                                }
                            }
                        });
                    }
                });
            }
            internal string Url(string Path) => "http://127.0.0.1:" + ((IPEndPoint)Listener.LocalEndpoint).Port + Path;
            public void Dispose()
            {
                Listener.Stop();
                Serving.Wait(TimeSpan.FromSeconds(10));
            }
        }

        [Test]
        public void TheProductionTransport_ReadsStatusAndBody_AndBoundsBothSizeAndTime()
        {
            string Big = new string('x', 5000);
            using var Server = new LoopbackServer(Path => Path switch
            {
                "/ok" => (200, "{\"campsites\":{},\"count\":0}", false, 0),
                "/busy" => (503, "busy", false, 0),
                "/big" => (200, Big, false, 0),
                "/bigchunked" => (200, Big, true, 0),
                _ => (200, "late", false, 5000)
            });
            using var Transport = new HttpSearchTransport(1000);
            SearchResponse Send(string Path, CancellationToken Token = default) => Transport.Send(new SearchRequest(Server.Url(Path), "1", MonthA, 1), Token);

            Assert.That(Send("/ok"), Is.EqualTo(new SearchResponse(200, "{\"campsites\":{},\"count\":0}")));
            Assert.That(Send("/busy"), Is.EqualTo(new SearchResponse(503, "busy")));
            Assert.That(Send("/big"), Is.EqualTo(new SearchResponse(200, null, true)), "Content-Length over the limit: not read");
            Assert.That(Send("/bigchunked"), Is.EqualTo(new SearchResponse(200, null, true)), "no length: stopped at the limit");
            using var Limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            Stopwatch Clock = Stopwatch.StartNew();
            Assert.That(() => Send("/slow", Limit.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Clock.Elapsed, Is.LessThan(Prompt));
        }
    }
}
