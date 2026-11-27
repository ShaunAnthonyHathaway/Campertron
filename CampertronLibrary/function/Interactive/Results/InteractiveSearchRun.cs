using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Results.InteractiveResultsWording;

namespace CampertronLibrary.function.Interactive.Results
{
    // One manual search of the console, from an accepted Start to its end (v0.4.0's phase 10, D126; business-rules §43): the
    // shared run (CampsiteSearch.Run, through the seams) of ResultsStart's plan on a thread of its own, which the console
    // waits for, as phase 9's refresh is waited for (D125):
    //  - While it runs the console's thread shows its progress from the shared snapshots, the latest one only (an earlier
    //    one never replaces it): the stage when it changes (ResultsWording.Stage), each chosen search's state when it
    //    changes (ResultsWording.State), and the counts with the elapsed time (ResultsWording.ProgressDetails) at most once
    //    every CountInterval and never the same counts twice; nothing after Cancel but its end. It asks nothing meanwhile.
    //  - Esc and Ctrl+C, read as keys for the search's whole time (never ending the process), cancel it through its token at
    //    its next checkpoint (the shared run's: before a request, during one, between months, while campsite details are
    //    prepared or results built); a key again says once that it is already canceling. The end of the input, a terminal
    //    that is gone and a request to stop the process (SIGTERM, SIGHUP) cancel it the same way, and the console ends once
    //    it has stopped. The console never leaves a search running: it waits for its end before anything else.
    //  - Its end is taken over by the session's Results only when it is the latest run (InteractiveResultsState.Ended): the
    //    shared ResultsOutcome, its status line, the rows' states, and five minutes without Start after a block. A run that
    //    threw is the shared failed run named by its type (ResultsOutcome.Unexpected). Nothing is retried by the console.
    internal static class InteractiveSearchRun
    {
        internal static InteractiveStep Execute(InteractivePrompts Prompts, InteractiveResultsState State, ResultsStart Prepared)
        {
            InteractiveResultsSeams Seams = InteractiveResults.Seams;
            SearchRunPlan Plan = Prepared.Plan!;
            long Id = State.Started(Prepared.Files);
            var Op = new SearchOperation(Id, Prepared.Files, Seams.Time.GetTimestamp());
            Dictionary<string, string> Titles = State.List.Rows.ToDictionary(R => R.FileName, R => R.Title + " (" + R.FileName + ")", StringComparer.Ordinal);
            var Screen = new SearchRunScreen(Prompts, Seams, Titles);
            using (Prompts.Terminal.ReadKeysAsTheyAreTyped())
            using (Seams.WhenAskedToStop(Op.AskToStop))
            {
                //a background thread of its own (it can't keep the process alive by itself), which the console waits for
                Op.Work = Task.Factory.StartNew(() => Work(Seams, Plan, Op), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Screen.Say(RW.Searching(Op.Files.Count));
                foreach (string File in Op.Files)
                {
                    Screen.Say(W.Searching(Screen.TitleOf(File)));
                }
                try
                {
                    Screen.Say(W.RunningHow);
                    bool SaidStop = false;
                    while (!Op.Work.Wait(Seams.PollInterval))
                    {
                        Screen.Progress(Op);
                        if (Op.StopAsked && !SaidStop)
                        {
                            SaidStop = true;
                            Screen.Say(W.AskedToStop);
                            Cancel(Op, Screen, false);
                        }
                        while (Prompts.Terminal.NextKeyTyped() is TerminalInputKind Key)
                        {
                            if (Key == TerminalInputKind.Escape || Key == TerminalInputKind.Interrupt)
                            {
                                Cancel(Op, Screen, true);
                            }
                            else if (Key == TerminalInputKind.EndOfInput)
                            {
                                Screen.InputEnd();
                                break;
                            }
                        }
                        if ((Screen.InputEnded || Screen.TerminalGone) && !Op.CancelRequested)
                        {
                            Cancel(Op, Screen, false);
                        }
                    }
                }
                finally
                {
                    //whatever ended the console's watch (an unexpected problem of the terminal too), the run is never left
                    //running: it is canceled and waited for (its thread returns a result for everything, so the wait ends)
                    if (!Op.Work.IsCompleted)
                    {
                        if (!Op.CancelRequested)
                        {
                            Op.Cancel();
                        }
                        Op.Work.Wait();
                    }
                }
            }
            SearchRunResult Result = Op.Work.Result;
            long EndedAt = Seams.Time.GetTimestamp();
            Op.Ended();
            State.Ended(Op, Result, EndedAt, ResultText.Elapsed(Seams.Time.GetElapsedTime(Op.Started, EndedAt)));
            if (State.Status != null)
            {
                Screen.Say(InteractiveResults.Safe(State.Status));
            }
            return Screen.InputEnded || Screen.TerminalGone ? InteractiveStep.EndOfInput : Op.StopAsked ? InteractiveStep.Stopped : InteractiveStep.Done;
        }

        // Esc, Ctrl+C, the end of the input or a request to stop: the search stops at its next checkpoint; said once (a key
        // pressed again while it cancels: once more, that it already cancels)
        private static void Cancel(SearchOperation Op, SearchRunScreen Screen, bool Asked)
        {
            if (Op.CancelRequested)
            {
                if (Asked)
                {
                    Screen.SayOnce(W.StillCanceling);
                }
                return;
            }
            Op.Cancel();
            Screen.Say(RW.Canceling);
        }

        // the shared run on its own thread: its result, or for anything it throws (it throws for nothing it reports) the
        // shared failed run named by the exception's type only
        private static SearchRunResult Work(InteractiveResultsSeams Seams, SearchRunPlan Plan, SearchOperation Op)
        {
            try
            {
                return Seams.Run(Plan, Op, Op.Token, Seams.Options);
            }
            catch (Exception ex)
            {
                return ResultsOutcome.Unexpected(ex);
            }
        }
    }

    // One run of the console's Results: its number (its generation), the files it was asked for, its start, its
    // cancellation, the latest progress the shared run reported (from the run's threads) and whether the process was asked
    // to stop (from a signal's thread); the console's thread reads them
    internal sealed class SearchOperation : IProgress<SearchProgress>
    {
        private readonly CancellationTokenSource _Cancel = new CancellationTokenSource();
        private SearchProgress? _Progress;
        private int _StopAsked;
        private int _CancelRequested;

        internal SearchOperation(long Id, IReadOnlyList<string> Files, long Started)
        {
            this.Id = Id;
            this.Files = Files;
            this.Started = Started;
        }

        internal long Id { get; }
        internal IReadOnlyList<string> Files { get; }
        internal long Started { get; }
        internal Task<SearchRunResult> Work { get; set; } = null!;
        internal CancellationToken Token => _Cancel.Token;
        internal SearchProgress? Progress => Volatile.Read(ref _Progress);
        internal bool CancelRequested => Volatile.Read(ref _CancelRequested) != 0;
        internal bool StopAsked => Volatile.Read(ref _StopAsked) != 0;

        // the shared run's progress, from its threads: the latest snapshot by its sequence, never an earlier one
        public void Report(SearchProgress Value)
        {
            SearchProgress? Seen = Volatile.Read(ref _Progress);
            while (Seen == null || Value.Sequence > Seen.Sequence)
            {
                SearchProgress? Was = Interlocked.CompareExchange(ref _Progress, Value, Seen);
                if (ReferenceEquals(Was, Seen))
                {
                    return;
                }
                Seen = Was;
            }
        }

        internal void AskToStop() => Interlocked.Exchange(ref _StopAsked, 1);

        // the console's thread only, while the run goes on; never starts anything
        internal void Cancel()
        {
            Volatile.Write(ref _CancelRequested, 1);
            _Cancel.Cancel();
        }

        // the run has ended (its thread is done): the token is no longer needed
        internal void Ended() => _Cancel.Dispose();
    }

    // What the console shows while a search runs (from the shared snapshots, in the shared words), at most a line for each
    // change and its counts at most once every CountInterval, never the same twice, and nothing after Cancel. A terminal
    // that is gone (a write's IOException) is remembered, and nothing more is written to it
    internal sealed class SearchRunScreen
    {
        private readonly InteractivePrompts Prompts;
        private readonly InteractiveResultsSeams Seams;
        private readonly Dictionary<string, string> Titles;
        private readonly Dictionary<string, SearchState> States = new Dictionary<string, SearchState>(StringComparer.Ordinal);
        private readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);
        private long Seen;
        private SearchRunStage? Stage;
        private string? Counts;
        private long CountsAt;
        private bool CountsShown;

        internal SearchRunScreen(InteractivePrompts Prompts, InteractiveResultsSeams Seams, Dictionary<string, string> Titles)
        {
            this.Prompts = Prompts;
            this.Seams = Seams;
            this.Titles = Titles;
        }

        internal bool TerminalGone { get; private set; }
        internal bool InputEnded { get; private set; }

        internal string TitleOf(string FileName) => Titles.TryGetValue(FileName, out string? Title) ? Title : FileName;

        internal void Say(string Text)
        {
            if (TerminalGone)
            {
                return;
            }
            try
            {
                Prompts.Say(InteractiveResults.Safe(Text));
            }
            catch (IOException)
            {
                TerminalGone = true;
            }
        }

        internal void SayOnce(string Text)
        {
            if (Said.Add(Text))
            {
                Say(Text);
            }
        }

        // the end of the input while the search runs (Ctrl+D, a terminal that is gone): said once; the search is canceled
        // and the console ends after it
        internal void InputEnd()
        {
            InputEnded = true;
            SayOnce(W.InputEndedDuringSearch);
        }

        internal void Progress(SearchOperation Op)
        {
            if (Op.Progress is not SearchProgress Snapshot || Snapshot.Sequence <= Seen || Op.CancelRequested)
            {
                return;
            }
            Seen = Snapshot.Sequence;
            if (Snapshot.Stage != Stage && IsUnderway(Snapshot.Stage))
            {
                Stage = Snapshot.Stage;
                Say(RW.Stage(Snapshot.Stage, Snapshot.RequestsRetrying));
            }
            foreach (SearchProgressItem Item in Snapshot.Searches)
            {
                bool Known = States.TryGetValue(Item.FileName, out SearchState Was);
                if ((!Known && Item.State != SearchState.Waiting) || (Known && Was != Item.State))
                {
                    Say(W.SearchState(TitleOf(Item.FileName), RW.State(Item.State)));
                }
                States[Item.FileName] = Item.State;
            }
            string? Counted = RW.ProgressDetails(null, RW.Campgrounds(Snapshot), RW.Requests(Snapshot), null);
            long Now = Seams.Time.GetTimestamp();
            if (Counted.Length > 0 && Counted != Counts && (!CountsShown || Seams.Time.GetElapsedTime(CountsAt, Now) >= Seams.CountInterval))
            {
                Counts = Counted;
                CountsAt = Now;
                CountsShown = true;
                Say("  " + RW.ProgressDetails(null, RW.Campgrounds(Snapshot), RW.Requests(Snapshot), ResultText.Elapsed(Seams.Time.GetElapsedTime(Op.Started, Now))));
            }
        }

        // the stages a run goes through; its end (Completed, Blocked, Canceled, …) is said by its status line
        private static bool IsUnderway(SearchRunStage Stage) => Stage == SearchRunStage.Validating || Stage == SearchRunStage.PreparingCampsiteDetails
            || Stage == SearchRunStage.CheckingAvailability || Stage == SearchRunStage.Retrying || Stage == SearchRunStage.BuildingResults;
    }
}
