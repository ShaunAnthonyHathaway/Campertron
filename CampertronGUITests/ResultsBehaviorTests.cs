using System.Diagnostics;
using System.Reflection;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Campertron;
using Campertron.ViewModels;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // The Results tab's run ownership, progress, block cooldown and result presentation (GS2, decision D46), on its view
    // model with seams the test controls: the UI thread's queue, the clock, the timers and, where the test needs every
    // step, GS1's run itself. The numbers refer to the GS2 test list.
    public class ResultsBehaviorTests
    {
        private ResultsFolders Folders = null!;

        [SetUp]
        public void CreateFolders() => Folders = new ResultsFolders();

        [TearDown]
        public void DeleteFolders() => Folders.Dispose();

        private static SearchProgress Snapshot(long Sequence, SearchRunStage Stage, int CampgroundsPlanned = 0, int CampgroundsPrepared = 0,
            int RequestsPlanned = 0, int RequestsCompleted = 0, int RequestsFailed = 0, int RequestsRetrying = 0, SearchState State = SearchState.Searching) =>
            new SearchProgress(Sequence, Stage, CampgroundsPlanned, CampgroundsPrepared, RequestsPlanned, RequestsCompleted, RequestsFailed, RequestsRetrying,
                SearchLists.Of(new[] { new SearchProgressItem("Alpha.yaml", State) }));

        private static SearchRunResult Completed(int Nights = 2) => RunResult(SearchRunStatus.Completed,
            Result("Alpha.yaml", SearchState.Complete, Enumerable.Range(1, Nights).Select(d => Opening("990000101", new DateTime(2027, 7, d)))));

        // the UI thread's queue, run by the test
        private sealed class Jobs
        {
            private readonly List<Action> _Queue = new List<Action>();
            public void Post(Action Job)
            {
                lock (_Queue)
                {
                    _Queue.Add(Job);
                }
            }
            public int Count
            {
                get
                {
                    lock (_Queue)
                    {
                        return _Queue.Count;
                    }
                }
            }
            public List<Action> Take()
            {
                lock (_Queue)
                {
                    List<Action> All = _Queue.ToList();
                    _Queue.Clear();
                    return All;
                }
            }
            public void RunAll()
            {
                foreach (Action Job in Take())
                {
                    Job();
                }
            }
            public void WaitFor(int AtLeast)
            {
                var Clock = Stopwatch.StartNew();
                while (Count < AtLeast && Clock.ElapsedMilliseconds < 10000)
                {
                    Thread.Sleep(1);
                }
                Assert.That(Count, Is.GreaterThanOrEqualTo(AtLeast), "the worker did not post");
            }
        }

        // ---- operations ----

        // 29, 30, 31: callbacks of an earlier run (its progress, its end, its ticker) change nothing once a later run owns
        // the tab
        [AvaloniaTest]
        public void AnEarlierRunsProgressEndAndTicker_AreIgnored()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var Queue = new Jobs();
            var Time = new ManualTime();
            var Timers = new ManualTimers();
            var Release = new SemaphoreSlim(0);
            var Progresses = new List<IProgress<SearchProgress>?>();
            var Seams = new ResultsSeams
            {
                Post = Queue.Post, Delay = Timers.Once, Ticker = Timers.Every, Time = Time,
                Run = (Plan, Progress, Token, Options) =>
                {
                    lock (Progresses)
                    {
                        Progresses.Add(Progress);
                    }
                    Release.Wait(TimeSpan.FromSeconds(20));
                    Progress?.Report(Snapshot(7, SearchRunStage.CheckingAvailability, RequestsPlanned: 4, RequestsCompleted: 2));
                    return Completed();
                },
            };
            ResultsViewModel ViewModel = Results(Folders, Seams);

            Start(ViewModel);
            long First = ViewModel.CurrentRunId;
            Release.Release();
            Queue.WaitFor(2);
            List<Action> FirstJobs = Queue.Take();
            foreach (Action Job in FirstJobs)
            {
                Job();
            }
            Assert.That(ViewModel.IsRunning, Is.False);
            Assert.That(ViewModel.LastResult, Is.Not.Null);
            ManualTimers.Timer FirstTicker = Timers.Tickers.Single();
            Assert.That(FirstTicker.Disposed, Is.True, "the first run's ticker stopped");

            Start(ViewModel);
            Assert.That(ViewModel.CurrentRunId, Is.GreaterThan(First));
            Assert.That(ViewModel.LastResult, Is.Null, "the second run cleared the results when it was accepted");
            Time.Advance(TimeSpan.FromSeconds(5));
            // the first run's jobs again (a late progress and a late end) and a late report to its gate
            foreach (Action Job in FirstJobs)
            {
                Job();
            }
            Progresses[0]!.Report(Snapshot(99, SearchRunStage.Retrying, RequestsPlanned: 9, RequestsRetrying: 3));
            Queue.RunAll();
            Time.Advance(TimeSpan.FromMilliseconds(200));
            Timers.FireOnce();
            Queue.RunAll();
            FirstTicker.Job();
            Assert.That(ViewModel.IsRunning, Is.True, "the first run's end did not end the second");
            Assert.That(ViewModel.LastResult, Is.Null, "the first run's result was not shown again");
            Assert.That(ViewModel.AppliedProgress, Is.Zero, "the first run's progress was not shown");
            Assert.That(ViewModel.RequestsText, Is.Null);
            Assert.That(ViewModel.ElapsedText, Is.EqualTo("0:00"), "the first run's ticker did nothing");
            Timers.Tick();
            Assert.That(ViewModel.ElapsedText, Is.EqualTo("0:05"), "the second run's ticker counts");

            Release.Release();
            Queue.WaitFor(2);
            Queue.RunAll();
            Assert.That(ViewModel.IsRunning, Is.False);
            Assert.That(ViewModel.AppliedProgress, Is.EqualTo(7));
        }

        // ---- the UI thread (TD-56) ----

        // A run's progress and its end reach the dispatcher of the thread that started the run, captured at Start; no worker
        // thread asks for Dispatcher.UIThread. In Avalonia 12 the first thread that asks while no UI dispatcher exists
        // becomes the UI thread, and Avalonia.Headless resets the dispatcher between two tests: a run's end that asked then
        // would make the next test fail before it starts (TD-56, as D49 for the startup's completion). A plain test, so that
        // this thread plays the UI thread; the reset stands for the moment between two tests.
        [Test]
        public void ALateEndAndProgress_GoToTheDispatcherThatStartedTheRun_AndNoWorkerThreadBecomesTheUiThread()
        {
            MethodInfo? Reset = typeof(Dispatcher).GetMethod("ResetBeforeUnitTests", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(Reset, Is.Not.Null, "Avalonia's reset between two headless tests (Dispatcher.ResetBeforeUnitTests) was not found: reassess this test after the Avalonia change");
            Reset!.Invoke(null, null);
            try
            {
                Dispatcher Ui = Dispatcher.UIThread;
                Assert.That(Ui.CheckAccess(), Is.True, "this thread is the UI thread of the run");
                Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
                using var Release = new ManualResetEventSlim();
                var Timers = new ManualTimers();
                // the production post (Post is null): the dispatcher captured at Start
                var ViewModel = new ResultsViewModel(Folders.Config, Folders.Cache, NoCampgrounds, _ => false, new ResultsSeams
                {
                    Delay = Timers.Once, Ticker = Timers.Every,
                    Run = (Plan, Progress, Token, Options) =>
                    {
                        Release.Wait(TimeSpan.FromSeconds(20));
                        Progress?.Report(Snapshot(3, SearchRunStage.CheckingAvailability, RequestsPlanned: 2, RequestsCompleted: 1));
                        return Completed();
                    },
                });
                ViewModel.Show();
                Assert.That(ViewModel.StartCommand.CanExecute(null), Is.True, ViewModel.StatusText);
                ViewModel.Start();
                Assert.That(ViewModel.IsRunning, Is.True);

                // what happens between two headless tests: no UI dispatcher until a thread asks for one
                Reset.Invoke(null, null);
                Release.Set();
                Assert.That(ViewModel.PendingRun.Wait(TimeSpan.FromSeconds(20)), Is.True);
                var Clock = Stopwatch.StartNew();
                while (ViewModel.IsRunning && Clock.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Ui.RunJobs();
                    Thread.Sleep(1);
                }

                Assert.That(ViewModel.IsRunning, Is.False, "the run's end did not reach the dispatcher that started it");
                Assert.That(ViewModel.AppliedProgress, Is.EqualTo(3), "its progress reached that dispatcher too");
                Assert.That(ViewModel.RunSummary, Does.StartWith("Finished:"));
                Assert.That(Dispatcher.UIThread.CheckAccess(), Is.True,
                    "a worker thread asked for Dispatcher.UIThread and became the UI thread; the next application could not start on this thread");
            }
            finally
            {
                Reset.Invoke(null, null);
            }
        }

        // The run's worker, its continuation and the progress gate never ask for Dispatcher.UIThread: only the method that
        // Start calls on the UI thread captures it, and the production seams have no post of their own (TD-56)
        [Test]
        public void TheRun_NeverAsksForTheUiDispatcherOnAWorkerThread()
        {
            const string UiThread = "Avalonia.Threading.Dispatcher::get_UIThread";
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            List<string> Of(Type T) => T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)).SelectMany(ReferencedMembers).ToList();
            List<string> FromLambdas = typeof(ResultsViewModel).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Of).ToList();

            Assert.That(FromLambdas, Does.Not.Contain(UiThread), "a worker thread must not ask for Dispatcher.UIThread");
            // the captured post, the continuation's call of it and Complete (the scan sees the calls at all)
            Assert.That(FromLambdas, Does.Contain("Avalonia.Threading.Dispatcher::Post").And.Contain(typeof(ResultsViewModel).FullName + "::Complete"));
            Assert.That(typeof(ResultsViewModel).GetMethods(Declared).Where(M => ReferencedMembers(M).Contains(UiThread)).Select(M => M.Name), Is.EqualTo(new[] { "UiPost" }));
            Assert.That(ReferencedMembers(typeof(ResultsSeams), typeof(ProgressGate), typeof(ProgressGate<>)).Select(C => C.Member), Does.Not.Contain(UiThread));
            Assert.That(new ResultsSeams().Post, Is.Null, "production posts to the dispatcher captured at Start");
        }

        // TD-56's review, quick operations: runs started one after the other, ended, canceled or failed at once, with the
        // production post: each end belongs to its own operation, IDs only grow, each accepted run replaces the previous
        // results, and after the last nothing runs, waits or holds the window's data-operation gate
        [AvaloniaTest]
        public void RunsInQuickSuccession_EndAsThemselves_AndReplaceTheResults()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            int Count = 0;
            var Timers = new ManualTimers();
            // A run ends only once the test has looked at the running operation: the stubbed run would otherwise end on its
            // worker thread while the test still reads the operation's ID, and on a busy machine it did (CI #275: the ID
            // was already 0). The canceled runs wait for their token instead
            using var Looked = new SemaphoreSlim(0);
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), Timers, Run: (Plan, Progress, Token, Options) =>
            {
                int N = Interlocked.Increment(ref Count);
                Progress?.Report(Snapshot(N, SearchRunStage.CheckingAvailability, RequestsPlanned: 2, RequestsCompleted: 1));
                if (N % 3 != 2)
                {
                    Looked.Wait(TimeSpan.FromSeconds(10));
                }
                switch (N % 3)
                {
                    case 0:
                        throw new InvalidOperationException("SECRET defect text");
                    case 2:
                        Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
                        return RunResult(SearchRunStatus.Canceled, Result("Alpha.yaml", SearchState.Canceled, Array.Empty<SearchOpening>()));
                    default:
                        return Completed(N);
                }
            }));
            long Last = 0;
            for (int i = 1; i <= 6; i++)
            {
                Start(ViewModel);
                Assert.That(ViewModel.CurrentRunId, Is.GreaterThan(Last), "a new operation " + i);
                Last = ViewModel.CurrentRunId;
                Assert.That(ViewModel.LastResult, Is.Null, "the accepted run cleared the previous results " + i);
                if (i % 3 == 2)
                {
                    ViewModel.Cancel();
                }
                else
                {
                    Looked.Release();
                }
                WaitForEnd(ViewModel);
                SearchRunStatus Expected = i % 3 == 0 ? SearchRunStatus.Failed : i % 3 == 2 ? SearchRunStatus.Canceled : SearchRunStatus.Completed;
                Assert.That(ViewModel.RunStatus, Is.EqualTo(Expected), "run " + i);
                if (Expected == SearchRunStatus.Completed)
                {
                    Assert.That(ViewModel.SelectedResult!.Nights, Is.EqualTo(i), "run " + i + "'s own result");
                }
                Assert.That(ViewModel.StatusText, Does.Not.Contain("SECRET"));
                Assert.That(ViewModel.CanStart, Is.True, "nothing holds the window after run " + i);
            }
            Assert.That(Count, Is.EqualTo(6));
            Assert.That(Timers.Live, Is.Empty, "no timer left");
            Assert.That(ViewModel.PendingRun.IsCompleted, Is.True);
        }

        // 32: a flood of snapshots reaches the UI at most ten times a second, always the newest, with at most one job or one
        // timer waiting at a time
        [Test]
        public void Progress_IsThrottled_ToTenASecond_WithOneJobAtATime()
        {
            var Time = new ManualTime();
            var Queue = new Queue<Action>();
            (TimeSpan At, Action Job, bool Disposed)? Pending = null;
            var Published = new List<(long Sequence, TimeSpan At)>();
            TimeSpan Now = TimeSpan.Zero;
            var Gate = new ProgressGate(P => Published.Add((P.Sequence, Now)), Queue.Enqueue, (Due, Job) =>
            {
                Assert.That(Pending == null || Pending.Value.Disposed, Is.True, "one timer at a time");
                Pending = (Now + Due, Job, false);
                return new Handle(() => Pending = Pending == null ? null : (Pending.Value.At, Pending.Value.Job, true));
            }, Time, ResultsViewModel.ProgressInterval);

            long Sequence = 0;
            int MostWaiting = 0;
            for (int Ms = 0; Ms < 1000; Ms++)
            {
                for (int Burst = 0; Burst < 5; Burst++)
                {
                    Gate.Report(Snapshot(++Sequence, SearchRunStage.CheckingAvailability, RequestsPlanned: 5000, RequestsCompleted: (int)Sequence));
                }
                MostWaiting = Math.Max(MostWaiting, Queue.Count + (Pending is { Disposed: false } ? 1 : 0));
                while (Queue.Count > 0)
                {
                    Queue.Dequeue()();
                }
                if (Pending is { Disposed: false } Due && Due.At <= Now)
                {
                    Pending = null;
                    Due.Job();
                }
                Time.Advance(TimeSpan.FromMilliseconds(1));
                Now += TimeSpan.FromMilliseconds(1);
            }
            Assert.That(MostWaiting, Is.LessThanOrEqualTo(1), "never more than one job or timer");
            Assert.That(Published.Count, Is.InRange(9, 10), "at most ten a second, and regularly");
            Assert.That(Published.Select(P => P.Sequence), Is.Ordered.Ascending);
            for (int i = 1; i < Published.Count; i++)
            {
                Assert.That(Published[i].At - Published[i - 1].At, Is.GreaterThanOrEqualTo(ResultsViewModel.ProgressInterval));
            }
            Assert.That(Gate.Posted, Is.LessThanOrEqualTo(Published.Count + 1));
            Gate.Close();
            Gate.Report(Snapshot(++Sequence, SearchRunStage.CheckingAvailability));
            Assert.That(Queue, Is.Empty, "nothing after Close");
        }

        private sealed class Handle : IDisposable
        {
            private readonly Action _Dispose;
            public Handle(Action Dispose) => _Dispose = Dispose;
            public void Dispose() => _Dispose();
        }

        // 33: the run's end is shown at once, without waiting for the progress timer, which stops
        [AvaloniaTest]
        public void TheEnd_IsShownAtOnce_AndStopsTheProgressTimer()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var Time = new ManualTime();
            var Timers = new ManualTimers();
            var Queue = new Jobs();
            var Shown = new SemaphoreSlim(0);
            ResultsViewModel ViewModel = Results(Folders, new ResultsSeams
            {
                Post = Queue.Post, Delay = Timers.Once, Ticker = Timers.Every, Time = Time,
                Run = (Plan, Progress, Token, Options) =>
                {
                    Progress?.Report(Snapshot(1, SearchRunStage.CheckingAvailability, RequestsPlanned: 50, RequestsCompleted: 1));
                    Shown.Wait(TimeSpan.FromSeconds(20));
                    for (int i = 2; i <= 50; i++)
                    {
                        Progress?.Report(Snapshot(i, SearchRunStage.CheckingAvailability, RequestsPlanned: 50, RequestsCompleted: i));
                    }
                    return Completed();
                },
            });
            Start(ViewModel);
            Queue.WaitFor(1);
            Queue.RunAll();
            Assert.That(ViewModel.AppliedProgress, Is.EqualTo(1));
            Shown.Release();
            Queue.WaitFor(2);
            Queue.RunAll();
            Assert.That(Timers.All.Count(T => !T.Repeat), Is.EqualTo(1), "the later snapshots waited for the interval");
            Assert.That(ViewModel.IsRunning, Is.False, "the end did not wait for the progress timer");
            Assert.That(ViewModel.RunSummary, Does.StartWith("Finished:"));
            Assert.That(ViewModel.StageText, Is.Null);
            Assert.That(Timers.Live, Is.Empty, "no timer left");
        }

        // 34, 35: the bar is determinate only where GS1 gives a total; a request waiting to be tried again is shown as
        // GS1 reports it (GS1 supplies no attempt number or delay, so none is shown)
        [AvaloniaTest]
        public void TheBar_IsDeterminateOnlyWithTotals_AndRetriesAreShownAsReported()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var Time = new ManualTime();
            var Queue = new Jobs();
            var Step = new SemaphoreSlim(0);
            var Reported = new SemaphoreSlim(0);
            SearchProgress[] Script =
            {
                Snapshot(1, SearchRunStage.Validating, State: SearchState.Waiting),
                Snapshot(2, SearchRunStage.PreparingCampsiteDetails, CampgroundsPlanned: 2, CampgroundsPrepared: 1),
                Snapshot(3, SearchRunStage.CheckingAvailability, 2, 2, RequestsPlanned: 4, RequestsCompleted: 1),
                Snapshot(4, SearchRunStage.Retrying, 2, 2, RequestsPlanned: 4, RequestsCompleted: 1, RequestsRetrying: 1),
                Snapshot(5, SearchRunStage.CheckingAvailability, 2, 2, RequestsPlanned: 4, RequestsCompleted: 3, RequestsFailed: 1),
                Snapshot(6, SearchRunStage.BuildingResults, 2, 2, RequestsPlanned: 4, RequestsCompleted: 4, RequestsFailed: 1),
            };
            ResultsViewModel ViewModel = Results(Folders, new ResultsSeams
            {
                Post = Queue.Post, Delay = new ManualTimers().Once, Ticker = new ManualTimers().Every, Time = Time,
                Run = (Plan, Progress, Token, Options) =>
                {
                    foreach (SearchProgress Each in Script)
                    {
                        Step.Wait(TimeSpan.FromSeconds(20));
                        Progress?.Report(Each);
                        Reported.Release();
                    }
                    Step.Wait(TimeSpan.FromSeconds(20));
                    return Completed();
                },
            });
            Start(ViewModel);
            void Next()
            {
                Time.Advance(TimeSpan.FromMilliseconds(150));
                Step.Release();
                Assert.That(Reported.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Queue.WaitFor(1);
                Queue.RunAll();
            }

            Next();
            Assert.That(ViewModel.ProgressIsIndeterminate, Is.True);
            Assert.That(ViewModel.ProgressDetails, Does.StartWith("Reading the searches"));
            Assert.That(ViewModel.Searches.Single().RunState, Is.EqualTo("Waiting"));
            Next();
            Assert.That(ViewModel.ProgressIsIndeterminate, Is.False);
            Assert.That((ViewModel.ProgressValue, ViewModel.ProgressMaximum), Is.EqualTo((1.0, 2.0)));
            Assert.That(ViewModel.CampgroundsText, Is.EqualTo("Campsite details: 1 of 2 campgrounds ready"));
            Next();
            Assert.That((ViewModel.ProgressIsIndeterminate, ViewModel.ProgressValue, ViewModel.ProgressMaximum), Is.EqualTo((false, 1.0, 4.0)));
            Assert.That(ViewModel.RequestsText, Is.EqualTo("Availability: 1 of 4 month requests finished"));
            Assert.That(ViewModel.Searches.Single().RunState, Is.EqualTo("Searching"));
            Next();
            Assert.That(ViewModel.StageText, Is.EqualTo("Checking availability; 1 request is waiting to be tried again (each month is asked for at most three times)"));
            Assert.That(ViewModel.RequestsText, Is.EqualTo("Availability: 1 of 4 month requests finished, 1 waiting to be tried again"));
            Assert.That(ViewModel.ProgressDetails, Does.Not.Contain("%").And.Not.Contain("attempt").And.Not.Contain("second"));
            Next();
            Assert.That(ViewModel.RequestsText, Is.EqualTo("Availability: 3 of 4 month requests finished, 1 not checked"));
            Next();
            Assert.That(ViewModel.ProgressIsIndeterminate, Is.True, "building the results has no total");
            Assert.That(ViewModel.StageText, Is.EqualTo("Building the results"));
            Step.Release();
            Queue.WaitFor(1);
            Queue.RunAll();
            Assert.That(ViewModel.IsRunning, Is.False);
        }

        // ---- a block ----

        // 36, 37, 38, 39, 40: a block (HTTP 429 or the block page) keeps what was complete, explains without the answer's
        // text, never ends the application, and disables Start for five minutes in this process only
        [AvaloniaTest]
        [TestCase(429)]
        [TestCase(200)]
        public void ABlockedRun_KeepsCompleteResults_AndStartWaitsFiveMinutes(int Status)
        {
            const string Body = "SECRET-ANSWER-TEXT";
            RunProbe.WriteCampground(Folders.Cache, "9900002", "SECOND CAMPGROUND", "Campertron Probe Park", new[] { "990000201", "990000202" });
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true, SearchBy: "SpecificDates", Dates: "[" + ResultsFolders.Date(3) + "]"));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true, Campground: "9900002", SearchBy: "SpecificDates", Dates: "[" + ResultsFolders.Date(40) + "]"));
            var AlphaAnswered = new ManualResetEventSlim(false);
            var Gov = new TestGov
            {
                Answer = (Request, Token) =>
                {
                    if (Request.CampgroundId == RunProbe.CampgroundId)
                    {
                        AlphaAnswered.Set();
                        return new SearchResponse(200, RunProbe.MonthJson(Request.Month, RunProbe.Campsites));
                    }
                    AlphaAnswered.Wait(TimeSpan.FromSeconds(10));
                    Thread.Sleep(200);
                    return Status == 429 ? new SearchResponse(429, Body) : new SearchResponse(200, "<html>REQUEST BLOCKED " + Body + "</html>");
                },
            };
            var Time = new ManualTime();
            var Timers = new ManualTimers();
            Dictionary<string, string> Before = GuiTestHelpers.Fingerprint(Folders.Config);
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, Timers, Time));
            SearchRunResult Result = Run(ViewModel);

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Blocked));
            ResultSearchViewModel Alpha = ViewModel.ResultSearches.Single(S => S.FileName == "Alpha.yaml");
            ResultSearchViewModel Beta = ViewModel.ResultSearches.Single(S => S.FileName == "Beta.yaml");
            Assert.That(Alpha.Kind, Is.EqualTo(ResultKind.Openings), "the complete search keeps its results");
            Assert.That(Alpha.Openings, Is.EqualTo(Result.Searches.Single(S => S.FileName == "Alpha.yaml").Openings.Count).And.GreaterThan(0));
            Assert.That(Beta.Kind, Is.EqualTo(ResultKind.Blocked));
            Assert.That(Beta.StatusText, Does.StartWith("Stopped: recreation.gov limited the requests"));
            Assert.That(Beta.MonthsText, Does.Contain("Not checked:").And.Contain(Status == 429 ? "asked for fewer requests" : "blocked the requests"));
            Assert.That(ViewModel.RunSummary, Does.StartWith("Stopped: recreation.gov limited the requests"));
            string AllText = string.Join("\n", new[] { ViewModel.StatusText, ViewModel.RunSummary, ViewModel.CooldownText }.Concat(ViewModel.RunIssueLines)
                .Concat(ViewModel.ResultSearches.SelectMany(S => S.IssueLines.Concat(new[] { S.StatusText, S.MonthsText, S.EmptyText }))));
            Assert.That(AllText, Does.Not.Contain(Body).And.Not.Contain("html"));

            Assert.That(ViewModel.IsCoolingDown, Is.True);
            Assert.That(ViewModel.StartCommand.CanExecute(null) || ViewModel.CanStart, Is.False);
            Assert.That(ViewModel.CooldownText, Does.Contain("Start is available again in 5:00."));
            Time.Advance(TimeSpan.FromSeconds(299));
            Timers.Tick();
            Assert.That(ViewModel.CooldownText, Does.Contain("in 0:01."));
            Assert.That(ViewModel.CanStart, Is.False);
            Time.Advance(TimeSpan.FromSeconds(1));
            Timers.Tick();
            Assert.That(ViewModel.IsCoolingDown || ViewModel.HasCooldown, Is.False, "the five minutes are over");
            Assert.That(ViewModel.StartCommand.CanExecute(null), Is.True);
            Assert.That(Timers.Live, Is.Empty, "the ticker stopped");

            Assert.That(GuiTestHelpers.Fingerprint(Folders.Config), Is.EqualTo(Before), "nothing written: the wait is not kept in a file");
            ResultsViewModel Another = Results(Folders, Seams(new TestGov(), new ManualTimers(), Time));
            Assert.That(Another.IsCoolingDown, Is.False, "another window (or a restart) does not wait");
        }

        // ---- the results ----

        private ResultsViewModel Present(params SearchResult[] Searches) => Present(SearchRunStatus.Completed, Searches);

        // the results of a run the test made, as the tab shows them
        private ResultsViewModel Present(SearchRunStatus Status, params SearchResult[] Searches)
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers(), Run: (_, _, _, _) => RunResult(Status, Searches)));
            Run(ViewModel);
            return ViewModel;
        }

        // 41, 42, 43, 44, 45, 46, 69: every end is told apart by its words (never by color alone), and a month that was not
        // checked is listed as not checked, never as a month without availability
        [AvaloniaTest]
        public void EachEnd_IsToldApartByItsText()
        {
            var July = new DateTime(2027, 7, 1);
            var August = new DateTime(2027, 8, 1);
            SearchMonthResult[] OneMissing = { new SearchMonthResult(July, true, null), new SearchMonthResult(August, false, SearchIssueCode.Timeout) };
            SearchMonthResult[] NoneChecked = { new SearchMonthResult(July, false, SearchIssueCode.HttpStatus) };
            var Problem = new SearchIssue(SearchIssueCode.Timeout, SearchIssueSeverity.Error, CampsiteSearch.Message(SearchIssueCode.Timeout), "Incomplete.yaml", August, null, 3);
            ResultsViewModel ViewModel = Present(SearchRunStatus.CompletedWithProblems,
                Result("Openings.yaml", SearchState.Complete, new[] { Opening("990000101", new DateTime(2027, 7, 3)) }),
                Result("Nothing.yaml", SearchState.Complete, Array.Empty<SearchOpening>()),
                Result("Incomplete.yaml", SearchState.Incomplete, Array.Empty<SearchOpening>(), Months: OneMissing, Issues: new[] { Problem }),
                Result("Failed.yaml", SearchState.Failed, Array.Empty<SearchOpening>(), Months: NoneChecked),
                Result("Canceled.yaml", SearchState.Canceled, Array.Empty<SearchOpening>(), Months: new[] { new SearchMonthResult(July, false, SearchIssueCode.Canceled) }),
                Result("Invalid.yaml", SearchState.Invalid, Array.Empty<SearchOpening>(), Months: Array.Empty<SearchMonthResult>()));
            Dictionary<string, ResultSearchViewModel> Shown = ViewModel.ResultSearches.ToDictionary(S => S.FileName);

            Assert.That(Shown.Values.Select(S => S.Kind), Is.EqualTo(new[] { ResultKind.Openings, ResultKind.NoAvailability, ResultKind.Incomplete, ResultKind.Failed, ResultKind.Canceled, ResultKind.NotSearched }));
            Assert.That(Shown["Openings.yaml"].StatusText, Is.EqualTo("Complete: 1 opening found"));
            Assert.That(Shown["Nothing.yaml"].StatusText, Is.EqualTo("Complete: no matching availability in the months checked"));
            Assert.That(Shown["Incomplete.yaml"].StatusText, Does.StartWith("Incomplete: some months were not checked"));
            Assert.That(Shown["Failed.yaml"].StatusText, Is.EqualTo("Failed: no month could be checked"));
            Assert.That(Shown["Canceled.yaml"].StatusText, Does.StartWith("Canceled"));
            Assert.That(Shown["Invalid.yaml"].StatusText, Is.EqualTo("Not searched: the file can't be searched"));
            Assert.That(Shown.Values.Select(S => S.StatusText).Distinct().Count(), Is.EqualTo(6), "each end has its own words");

            Assert.That(Shown["Incomplete.yaml"].MonthsText, Does.Contain("Months checked: " + ResultText.Month(July)).And.Contain("Not checked: " + ResultText.Month(August) + " (recreation.gov did not answer in time)"));
            Assert.That(Shown["Incomplete.yaml"].IssueLines.Single(), Does.StartWith("Error: recreation.gov did not answer in time").And.Contain("3 requests"));
            Assert.That(Shown["Incomplete.yaml"].EmptyText, Does.Contain("not checked are listed above; they are not without availability"));
            Assert.That(Shown["Failed.yaml"].MonthsText, Does.StartWith("No month was checked.").And.Contain("recreation.gov answered with an error"));
            Assert.That(Shown["Failed.yaml"].MonthsText + Shown["Failed.yaml"].EmptyText, Does.Not.Contain("no matching availability"));
            Assert.That(Shown["Nothing.yaml"].EmptyText, Is.EqualTo("No campsite matched this search in the months checked."));
            Assert.That(ViewModel.RunSummary, Does.StartWith("Finished with problems"));
            Assert.That(ViewModel.SelectedResult, Is.SameAs(Shown["Openings.yaml"]), "the first search with openings is shown first");
        }

        // 47, 49: single nights by date (ascending), each date's campsites in GS1's order (shorter ID first, then ordinal)
        [AvaloniaTest]
        public void SingleNights_AreGroupedByDate_InGS1Order()
        {
            var One = new DateTime(2027, 7, 3);
            var Two = new DateTime(2027, 7, 4);
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, new[]
            {
                Opening("7", One), Opening("10", One), Opening("101", One), Opening("7", Two),
            }));
            IReadOnlyList<ResultRow> Rows = ViewModel.SelectedResult!.Rows;
            Assert.That(Rows.Select(R => R is ResultDateRow D ? "date " + D.Text : R.CampsiteId), Is.EqualTo(new[]
            {
                "date " + ResultText.Night(One) + " · 3 campsites", "7", "10", "101", "date " + ResultText.Night(Two) + " · 1 campsite", "7",
            }));
            Assert.That(ViewModel.SelectedResult.CountsText, Is.EqualTo("4 openings at 3 campsites on 2 nights"));
        }

        // 48, 49: consecutive nights by campsite (GS1's order), each with its stays in date order and their date range
        [AvaloniaTest]
        public void ConsecutiveNights_AreGroupedByCampsite_WithTheirStays()
        {
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, new[]
            {
                Opening("10", new DateTime(2027, 7, 1)), Opening("10", new DateTime(2027, 7, 2)), Opening("10", new DateTime(2027, 7, 3)),
                Opening("7", new DateTime(2027, 7, 5)), Opening("7", new DateTime(2027, 7, 6)),
                Opening("10", new DateTime(2027, 7, 9)), Opening("10", new DateTime(2027, 7, 10)),
            }.OrderBy(O => O.Night), Consecutive: 2));
            IReadOnlyList<ResultRow> Rows = ViewModel.SelectedResult!.Rows;
            Assert.That(Rows.Select(R => R.GetType().Name + " " + R.CampsiteId), Is.EqualTo(new[]
            {
                "ResultSiteRow 7", "ResultStayRow 7", "ResultSiteRow 10", "ResultStayRow 10", "ResultStayRow 10",
            }));
            Assert.That(((ResultStayRow)Rows[3]).Text, Is.EqualTo("Arrive " + ResultText.Night(new DateTime(2027, 7, 1)) + ", leave " + ResultText.Night(new DateTime(2027, 7, 4)) + " · 3 nights"));
            Assert.That(((ResultSiteRow)Rows[2]).StaysText, Is.EqualTo("2 stays · 5 nights"));
            Assert.That(ViewModel.SelectedResult.CountsText, Is.EqualTo("2 campsites with at least 2 nights in a row (7 nights in all)"));
        }

        // 49: a real run's rows are GS1's openings in GS1's order, nothing added or dropped
        [AvaloniaTest]
        public void ARealRunsRows_AreItsOpenings_InItsOrder()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));
            SearchRunResult Result = Run(ViewModel);
            SearchResult Search = Result.Searches.Single();
            List<ResultOpeningRow> Rows = ViewModel.SelectedResult!.Rows.OfType<ResultOpeningRow>().ToList();
            Assert.That(Rows.Select(R => R.CampsiteId), Is.EqualTo(Search.Openings.Select(O => O.CampsiteId)));
            Assert.That(Rows.Select(R => R.AccessibleName.Split(':')[0]), Is.EqualTo(Search.Openings.Select(O => ResultText.Night(O.Night))));
            Assert.That(ViewModel.SelectedResult.Rows.OfType<ResultDateRow>().Count(), Is.EqualTo(Search.Openings.Select(O => O.Night).Distinct().Count()));
        }

        // 50: more than ten thousand openings are all kept (no truncation)
        [AvaloniaTest]
        public void TenThousandOpenings_AreAllKept()
        {
            List<SearchOpening> Many = Enumerable.Range(1, 30).SelectMany(Day => Enumerable.Range(1, 400)
                .Select(Site => Opening((990000000 + Site).ToString(), new DateTime(2027, 7, Day)))).ToList();
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, Many));
            Assert.That(ViewModel.SelectedResult!.Openings, Is.EqualTo(12000));
            Assert.That(ViewModel.SelectedResult.Rows.OfType<ResultOpeningRow>().Count(), Is.EqualTo(12000));
            Assert.That(ViewModel.SelectedResult.Rows, Has.Count.EqualTo(12030));
            Assert.That(ViewModel.LastResult!.Searches.Single().Openings, Has.Count.EqualTo(12000));
        }

        // 53, 54, 55: optional fields that are missing leave no empty label; occupancy is shown when present
        [AvaloniaTest]
        public void MissingFields_LeaveNoEmptyLabels_AndOccupancyIsShownWhenPresent()
        {
            string LongSite = "Site " + new string('W', 180);
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, new[]
            {
                Opening("1", new DateTime(2027, 7, 3), Name: null, Loop: null, Type: " ", Equipment: Array.Empty<string>(), Checkin: null, Checkout: "", Min: null, Max: null),
                Opening("2", new DateTime(2027, 7, 3), Name: LongSite, Min: 2, Max: 8),
                Opening("3", new DateTime(2027, 7, 3), Name: "086", Min: 4, Max: 4),
                Opening("4", new DateTime(2027, 7, 3), Name: "A012", Min: null, Max: 6),
            }));
            List<ResultOpeningRow> Rows = ViewModel.SelectedResult!.Rows.OfType<ResultOpeningRow>().ToList();
            Assert.That(Rows[0].Title, Is.EqualTo("Site 1"));
            Assert.That(Rows[0].Details, Is.Empty);
            Assert.That(Rows[0].HasDetails, Is.False);
            Assert.That(Rows[0].AccessibleName, Does.Not.Contain("check-in").And.Not.Contain("people").And.Not.Contain(", ,").And.Not.Contain("·  ·"));
            Assert.That(Rows[1].Title, Is.EqualTo(LongSite), "a long name is kept whole (the view trims it)");
            Assert.That(Rows[1].Details, Is.EqualTo("A · STANDARD NONELECTRIC · Tent · check-in 2:00 PM · checkout 12:00 PM · 2 to 8 people"));
            Assert.That(Rows[2].Title, Is.EqualTo("Site 086"));
            Assert.That(Rows[2].Details, Does.EndWith("4 people"));
            Assert.That(Rows[3].Title, Is.EqualTo("Site A012"));
            Assert.That(Rows[3].Details, Does.EndWith("up to 6 people"));
        }

        // 52: a long campground and park name is kept whole in the summary (the view trims and wraps it)
        [AvaloniaTest]
        public void ALongCampgroundName_IsKept()
        {
            string Long = "THE VERY LONG NAMED CAMPGROUND " + new string('X', 150);
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, new[] { Opening("1", new DateTime(2027, 7, 3)) }, Campground: Long, Park: null));
            Assert.That(ViewModel.SelectedResult!.CampgroundText, Is.EqualTo(Long + " · ID " + RunProbe.CampgroundId));
        }

        // ---- booking links and copies ----

        // 56, 57, 58: only the exact recreation.gov campsite page GS1 builds for the campsite is accepted
        [Test]
        public void BookingLinks_AreCheckedBeforeUse()
        {
            Assert.That(BookingLink.TryCreate("https://www.recreation.gov/camping/campsites/990000101", "990000101", out Uri? Link), Is.True);
            Assert.That(Link!.AbsoluteUri, Is.EqualTo("https://www.recreation.gov/camping/campsites/990000101"));
            Assert.That(BookingLink.TryCreate(CampsiteSearch.BookingUrl("a b/c"), "a b/c", out _), Is.True, "an escaped ID");
            string[] Refused =
            {
                "http://www.recreation.gov/camping/campsites/990000101",
                "https://recreation.gov/camping/campsites/990000101",
                "https://www.recreation.gov.example.com/camping/campsites/990000101",
                "https://WWW.RECREATION.GOV/camping/campsites/990000101",
                "https://user@www.recreation.gov/camping/campsites/990000101",
                "https://www.recreation.gov:8443/camping/campsites/990000101",
                "https://www.recreation.gov/camping/campsites/990000101?x=1",
                "https://www.recreation.gov/camping/campsites/990000101#x",
                "https://www.recreation.gov/camping/campsites/990000102",
                "https://www.recreation.gov/camping/campgrounds/990000101",
                "file:///etc/passwd",
                "/camping/campsites/990000101",
                "",
            };
            foreach (string Url in Refused)
            {
                Assert.That(BookingLink.TryCreate(Url, "990000101", out _), Is.False, Url);
            }
            Assert.That(BookingLink.TryCreate(CampsiteSearch.BookingUrl(".."), "..", out _), Is.False, "a dot segment");
            Assert.That(BookingLink.TryCreate(null, "990000101", out _), Is.False);
            Assert.That(BookingLink.TryCreate("https://www.recreation.gov/camping/campsites/", "", out _), Is.False);
        }

        // 59, 60, 61, 62: copies and the booking page happen only on the user's action, through the view's launcher and
        // clipboard; a link that fails the check is neither opened nor copied; failures are reported without a value
        [AvaloniaTest]
        public async Task CopiesAndTheBookingPage_HappenOnlyOnTheUsersAction()
        {
            var Copied = new List<string>();
            var Opened = new List<Uri>();
            ResultsViewModel ViewModel = Present(Result("Alpha.yaml", SearchState.Complete, new[]
            {
                Opening("990000101", new DateTime(2027, 7, 3)),
                Opening("990000102", new DateTime(2027, 7, 3), Url: "http://evil.example.com/camping/campsites/990000102"),
            }));
            bool LaunchResult = true;
            Exception? LaunchThrows = null;
            ViewModel.CopyText = Text => { Copied.Add(Text); return Task.FromResult(true); };
            ViewModel.LinkLauncher = Link =>
            {
                if (LaunchThrows != null)
                {
                    throw LaunchThrows;
                }
                Opened.Add(Link);
                return Task.FromResult(LaunchResult);
            };
            IReadOnlyList<ResultRow> Rows = ViewModel.SelectedResult!.Rows;
            ViewModel.SelectedRow = Rows[0];
            Assert.That(ViewModel.CopyIdCommand.CanExecute(null) || ViewModel.OpenBookingCommand.CanExecute(null), Is.False, "a date heading has no campsite");
            ViewModel.SelectedRow = Rows[1];
            ViewModel.SelectedResult = ViewModel.ResultSearches[0];
            Assert.That(Copied, Is.Empty, "nothing copied without an action");
            Assert.That(Opened, Is.Empty);

            ViewModel.SelectedRow = Rows[1];
            await ViewModel.CopyCampsiteIdAsync();
            await ViewModel.CopyBookingLinkAsync();
            await ViewModel.OpenBookingAsync();
            Assert.That(Copied, Is.EqualTo(new[] { "990000101", "https://www.recreation.gov/camping/campsites/990000101" }));
            Assert.That(Opened.Single().AbsoluteUri, Is.EqualTo("https://www.recreation.gov/camping/campsites/990000101"));
            Assert.That(ViewModel.ActionMessage, Does.StartWith("Opened the booking page of Site 001"));

            LaunchResult = false;
            await ViewModel.OpenBookingAsync();
            Assert.That(ViewModel.ActionMessage, Is.EqualTo("Campertron couldn't open the booking page. Use Copy booking link and paste it into a browser."));
            Assert.That(ViewModel.ActionIsError, Is.True);
            LaunchThrows = new InvalidOperationException("SECRET launcher text");
            await ViewModel.OpenBookingAsync();
            Assert.That(ViewModel.ActionMessage, Does.Not.Contain("SECRET"));
            ViewModel.CopyText = _ => throw new InvalidOperationException("SECRET clipboard text");
            await ViewModel.CopyCampsiteIdAsync();
            Assert.That(ViewModel.ActionMessage, Is.EqualTo("Campertron couldn't copy the campsite ID."));

            // a link that is not the campsite's recreation.gov page: not opened, not copied
            ViewModel.CopyText = Text => { Copied.Add(Text); return Task.FromResult(true); };
            LaunchThrows = null;
            ViewModel.SelectedRow = Rows[2];
            int OpenedBefore = Opened.Count, CopiedBefore = Copied.Count;
            await ViewModel.OpenBookingAsync();
            Assert.That(ViewModel.ActionMessage, Is.EqualTo("Campertron did not open this link: it is not a recreation.gov campsite page."));
            await ViewModel.CopyBookingLinkAsync();
            Assert.That(ViewModel.ActionMessage, Is.EqualTo("Campertron did not copy this link: it is not a recreation.gov campsite page."));
            Assert.That((Opened.Count, Copied.Count), Is.EqualTo((OpenedBefore, CopiedBefore)));
        }
    }
}
