using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronWeb
{
    // The Results page's run of GS1's search service (tests only change it): the listing's reader, the plan and the run
    // (CampsiteSearch, manual-search semantics), the run's options (a stand-in transport), the clocks
    internal sealed class WebResultsSeams
    {
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal Func<string, string, DateTime, SearchPlanEntry> Inspect { get; init; } = CampsiteSearch.LoadEntry;
        internal Func<SearchRunRequest, DateTime, SearchRunPlan> Plan { get; init; } = CampsiteSearch.Plan;
        internal Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult> Run { get; init; } = CampsiteSearch.Run;
        internal SearchHostOptions? Options { get; init; }
    }

    // The web UI's one search at a time (v0.3.0's PR 7, D97; business-rules-settings §34). However many browser sessions
    // there are, at most one run of the Results page goes on in a web UI, as in one window of the desktop GUI, so the web UI
    // asks recreation.gov no more than the desktop GUI does: the data operations' gate (DataOperationGate: a search, and
    // from the campground data's page a refresh, never together) and the five minutes after a block (BlockHold) are the web
    // UI's, not a session's, because recreation.gov limits the computer, not the browser. A run belongs to the session that
    // started it: only that session sees its progress and its results and can cancel it. When the web UI stops, its run is
    // canceled and waited for a bounded time (the cache's campsite details are written whole or not at all). One object
    // per web UI; no static state; every member is safe from any thread.
    internal sealed class WebResultsRuns
    {
        internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);

        private readonly object _Lock = new object();
        private readonly DataOperationGate _Gate = new DataOperationGate();
        private readonly BlockHold _Hold;
        private WebResultsRun? _Current;
        private long _NextId;

        internal WebResultsRuns(TimeProvider Time)
        {
            _Hold = new BlockHold(Time);
        }

        // this web UI's own random name (D108): a run's key is it and the run's number, so a page of a run of an earlier
        // start of the web UI, whose numbers start again at 1, never matches a run of this one
        internal string Instance { get; } = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        internal long NextId() => Interlocked.Increment(ref _NextId);

        // the key a page names a run by when it asks for the run's state (D108)
        internal string KeyOf(WebResultsRun Run) => Instance + "-" + Run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // a run of another session goes on
        internal bool IsBusyForOthers(WebResults Owner)
        {
            lock (_Lock)
            {
                return _Current != null && !ReferenceEquals(_Current.Owner, Owner);
            }
        }

        // recreation.gov blocked a run less than five minutes ago: Start is unavailable in every session
        internal bool IsCoolingDown
        {
            get
            {
                lock (_Lock)
                {
                    return _Hold.IsActive;
                }
            }
        }

        // the hold's line with the time left, or null
        internal string? CooldownText()
        {
            lock (_Lock)
            {
                return _Hold.Text();
            }
        }

        // the run may start now (no other run, no hold, no other data operation); it then holds the gate until End
        internal bool TryBegin(WebResultsRun Run)
        {
            lock (_Lock)
            {
                if (_Current != null || _Hold.IsActive || !_Gate.TryEnter(DataOperation.Search))
                {
                    return false;
                }
                _Current = Run;
                return true;
            }
        }

        // the run has ended (on its worker's thread): the gate is free again, and a block holds Start for five minutes
        internal void End(WebResultsRun Run, bool Blocked)
        {
            lock (_Lock)
            {
                if (ReferenceEquals(_Current, Run))
                {
                    _Current = null;
                    _Gate.Leave(DataOperation.Search);
                }
                if (Blocked)
                {
                    _Hold.Begin();
                }
            }
        }

        // The campground data's refresh (v0.3.0's PR 9, D99; WebRefresh): it may start only when no search runs, and then holds
        // the data operations' gate until EndRefresh, so no search starts while it clears the cache folder and installs the
        // new database, as in one window of the desktop GUI (D61)
        internal bool TryBeginRefresh()
        {
            lock (_Lock)
            {
                return _Current == null && _Gate.TryEnter(DataOperation.Refresh);
            }
        }

        internal void EndRefresh()
        {
            lock (_Lock)
            {
                _Gate.Leave(DataOperation.Refresh);
            }
        }

        // a refresh of the campground data goes on: Start is unavailable in every session
        internal bool IsRefreshing
        {
            get
            {
                lock (_Lock)
                {
                    return _Gate.Current == DataOperation.Refresh;
                }
            }
        }

        // a search of the results page goes on, in any session: the refresh is unavailable
        internal bool IsSearching
        {
            get
            {
                lock (_Lock)
                {
                    return _Current != null;
                }
            }
        }

        // recreation.gov blocked a check of the monitoring (v0.3.0's PR 8, D98): Start is held for five minutes, here and on
        // the monitoring page, as after a blocked run, because recreation.gov limits the computer, not one of its searches
        internal void HoldAfterBlock()
        {
            lock (_Lock)
            {
                _Hold.Begin();
            }
        }

        // the web UI stops: its run is canceled and waited for, at most StopWait
        internal void Stop()
        {
            WebResultsRun? Run;
            lock (_Lock)
            {
                Run = _Current;
            }
            if (Run == null)
            {
                return;
            }
            Run.Cancel();
            try
            {
                Run.Work?.Wait(StopWait);
            }
            catch (AggregateException)
            {
                //the run's end is its own result; nothing more to do while the web UI stops
            }
        }
    }

    // One run of the Results page: its ID, its session, the files it was asked for, its cancellation, its latest progress
    // and, once it has ended, GS1's immutable result. Its worker writes the progress and the result; a request of its
    // session reads them (Volatile) and shows them; nothing else of a session is touched by another thread.
    internal sealed class WebResultsRun : IProgress<SearchProgress>
    {
        private readonly CancellationTokenSource _Cancel = new CancellationTokenSource();
        private readonly object _CancelLock = new object();
        private bool _Disposed;
        private SearchProgress? _Progress;
        private SearchRunResult? _Result;
        private long _EndedAt;
        private int _CancelRequested;

        internal WebResultsRun(long Id, WebResults Owner, IReadOnlyList<string> Files, long Started)
        {
            this.Id = Id;
            this.Owner = Owner;
            this.Files = Files;
            this.Started = Started;
        }

        internal long Id { get; }
        internal WebResults Owner { get; }
        internal IReadOnlyList<string> Files { get; }
        // the run's start and end by the web UI's monotonic clock
        internal long Started { get; }
        internal long EndedAt => Volatile.Read(ref _EndedAt);
        // the worker and its end (the web UI waits for it when it stops)
        internal Task? Work { get; set; }
        internal CancellationToken Token => _Cancel.Token;
        internal SearchProgress? Progress => Volatile.Read(ref _Progress);
        internal SearchRunResult? Result => Volatile.Read(ref _Result);
        internal bool CancelRequested => Volatile.Read(ref _CancelRequested) != 0;

        // GS1's progress, from its threads: the latest snapshot is kept (by its sequence), never a list of them
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

        // safe to repeat and from any thread; never starts anything
        internal void Cancel()
        {
            Interlocked.Exchange(ref _CancelRequested, 1);
            lock (_CancelLock)
            {
                if (!_Disposed)
                {
                    _Cancel.Cancel();
                }
            }
        }

        // the run has ended: its result, written last, so a request that sees it sees the end's time too
        internal void Ended(SearchRunResult Result, long At)
        {
            lock (_CancelLock)
            {
                _Disposed = true;
                _Cancel.Dispose();
            }
            Volatile.Write(ref _EndedAt, At);
            Volatile.Write(ref _Result, Result);
        }
    }

    // The Results page of one browser session (v0.3.0's PR 7, D97; business-rules-settings §34): the desktop GUI's Results
    // tab for a page. The list of search files and the rules of its choice are the shared form layer's ResultsSearchList,
    // a Start's checks and words ResultsStart, an ended run's results ResultsOutcome and its rows ResultSearchViewModel,
    // a page of them ResultsListing, every word ResultsWording's (D91, ER-37), so both UIs choose, start, refuse and show
    // the same way. What is the web UI's own: a run's worker and how its progress and end reach the session (the next
    // request of the session takes them over: a page has no thread to post to), one run at a time in the web UI
    // (WebResultsRuns), and the revision a page sends, so a page that is out of date applies nothing and starts nothing.
    // Used under its workspace's lock only (one request of the session at a time), as a form is on its host's thread.
    // A run is GS1's manual search: no Camp.History, no Email.yaml or Ntfy.yaml, no notification, no file written but the
    // cache's campsite details, no monitoring and no monitoring lock. Nothing of a run is written to the console.
    internal sealed class WebResults
    {
        private readonly WebResultsRuns _Runs;
        private readonly WebResultsSeams _Seams;
        private readonly ResultsSearchList _List;
        private readonly CampgroundCatalog _Catalog;
        private readonly string _ConfigPath;
        private readonly string? _CachePath;
        private bool _Loaded;
        // the run that goes on, or the last one
        private WebResultsRun? _Run;
        // the last ended run's results as the page shows them, and when that run ended
        private ResultsOutcome? _Outcome;
        private long _OutcomeOf;
        private long _OutcomeEndedAt;
        // the latest progress, as the current request shows it, and the stage when Cancel was asked for
        private SearchProgress? _Progress;
        private string? _StageAtCancel;
        // after an action: what the next page shows once (its focus and its notice), and that it does not look at the disk
        // again (the action just did)
        private string? _Focus;
        private string? _Notice;
        private bool _Acted;

        internal WebResults(string ConfigPath, string? CachePath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved, WebResultsRuns Runs, WebResultsSeams Seams)
        {
            _ConfigPath = ConfigPath;
            _CachePath = CachePath;
            _Catalog = Catalog;
            _Runs = Runs;
            _Seams = Seams;
            _List = new ResultsSearchList(ConfigPath, Catalog, IsUnsaved, Seams.Inspect, Seams.Now, () => { });
            Status = PathsAvailable ? ResultsWording.Idle : ResultsWording.NoCacheFolder;
        }

        // the revision of the page's state (the list, its choice, a run started or ended): a page sends the one it showed
        internal int Revision { get; private set; } = 1;
        internal IReadOnlyList<ResultsSearchRow> Rows => _List.Rows;
        internal bool IsLoaded => _Loaded;
        internal string Status { get; private set; }
        // a problem with the settings folder, or chosen files that changed on disk, or null
        internal string? ListNotice { get; private set; }
        internal bool PathsAvailable => Path.IsPathFullyQualified(_ConfigPath) && _CachePath != null && Path.IsPathFullyQualified(_CachePath);
        // a run of this session goes on (as this request found it)
        internal bool IsRunning => _Run != null && _OutcomeOf != _Run.Id;
        internal long RunId => IsRunning ? _Run!.Id : 0;
        internal bool IsCanceling => IsRunning && _Run!.CancelRequested;
        // the key of the run that goes on, as the page names it when it asks for its state (D108), or null
        internal string? RunKey => IsRunning ? _Runs.KeyOf(_Run!) : null;
        internal ResultsOutcome? Outcome => _Outcome;
        internal long OutcomeId => _OutcomeOf;

        // A page has no script to follow its check boxes, so Start is offered whenever a run could start with a choice
        // (the desktop GUI's Start also needs a chosen file: here a Start without one says so, NotStartedNoChoice)
        internal bool CanStart => !IsRunning && PathsAvailable && !_Runs.IsCoolingDown && !_Runs.IsBusyForOthers(this) && !_Runs.IsRefreshing && Rows.Any(R => R.IsRunnable);
        internal bool CanChoose => !IsRunning && Rows.Any(R => R.IsRunnable);
        internal bool CanCancel => IsRunning && !_Run!.CancelRequested;
        internal string? CooldownText() => _Runs.CooldownText();
        internal string? OtherSessionNotice => !IsRunning && _Runs.IsBusyForOthers(this) ? ResultsWording.OtherSessionRunning : null;
        // the campground data is being refreshed (v0.3.0's PR 9): the desktop GUI's words (D61)
        internal string? RefreshNotice => !IsRunning && _Runs.IsRefreshing ? ResultsWording.RefreshRunning : null;

        // ---- a page asked for (GET) ----

        // the run's progress or end taken over; the first time the campground data and the files read, later the files
        // again (not while a run goes on: its selection is fixed; not right after an action, which just looked)
        internal void Show()
        {
            Follow();
            if (!_Loaded)
            {
                _Loaded = true;
                _Catalog.Ensure();
            }
            if (_Acted)
            {
                _Acted = false;
            }
            else if (!IsRunning)
            {
                Rescan(false);
            }
        }

        // what the page shows once after an action: the element with the focus and the page's notice
        internal (string? Focus, string? Notice) TakeAfterAction()
        {
            (string? Focus, string? Notice) Was = (_Focus, _Notice);
            _Focus = null;
            _Notice = null;
            return Was;
        }

        // ---- a page posted (POST) ----

        // The page sent: its revision and its check boxes. A page that is out of date applies nothing; otherwise a box the
        // page showed and the user changed is handed to its row, as a binding hands a check box's change (only a row the
        // page showed, by its exact name; the row's own rules refuse a file that can't run, and any change while a run
        // goes on). Shown holds a box's state as it was shown ("1" or "0") and its file's name; Chosen the boxes checked.
        internal bool Accept(string? SentRevision, IEnumerable<string> Shown, IEnumerable<string> Chosen, string StaleText)
        {
            Follow();
            bool Current = int.TryParse(SentRevision, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int Sent) && Sent == Revision;
            _Acted = true;
            if (!Current)
            {
                _Notice = StaleText;
                _Focus = "page-notice";
                return false;
            }
            var Checked = new HashSet<string>(Chosen, StringComparer.Ordinal);
            var Was = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string Each in Shown)
            {
                if (Each.Length > 1 && (Each[0] == '0' || Each[0] == '1'))
                {
                    Was[Each.Substring(1)] = Each[0] == '1';
                }
            }
            bool Changed = false;
            foreach (ResultsSearchRow Row in Rows)
            {
                if (Was.TryGetValue(Row.FileName, out bool Before) && Checked.Contains(Row.FileName) != Before)
                {
                    Row.IsSelected = !Before;
                    Changed = true;
                }
            }
            if (Changed)
            {
                Revision++;
            }
            return true;
        }

        internal void SelectEnabled() => Choose(_List.SelectEnabled);
        internal void SelectAllRunnable() => Choose(_List.SelectAllRunnable);
        internal void ClearSelection() => Choose(_List.ClearSelection);

        private void Choose(Action Rule)
        {
            if (IsRunning)
            {
                return;
            }
            Rule();
            Revision++;
            _Focus = "run-files";
        }

        // Start: ResultsStart's checks and words (the files read again, GS1's plan, at least one file that can run), the web
        // UI's one run, then a worker of its own; the page shows the run when it is asked for next
        internal void Start()
        {
            _Focus = "status";
            if (IsRunning)
            {
                return;
            }
            if (!PathsAvailable)
            {
                Status = ResultsWording.NoCacheFolder;
                return;
            }
            if (_Runs.IsCoolingDown)
            {
                return;
            }
            if (_Runs.IsBusyForOthers(this))
            {
                Status = ResultsWording.NotStartedOtherSession;
                return;
            }
            if (_Runs.IsRefreshing)
            {
                Status = ResultsWording.RefreshRunning;
                return;
            }
            ResultsStart Prepared = ResultsStart.Prepare(_List.Rows, () => Rescan(true),
                Names => _Seams.Plan(new SearchRunRequest(_ConfigPath, _CachePath!, Names), _Seams.Now()));
            Revision++;
            if (Prepared.Plan is not SearchRunPlan Plan)
            {
                if (Prepared.ListNotice != null)
                {
                    ListNotice = Prepared.ListNotice;
                }
                Status = Prepared.Status!;
                return;
            }
            var Run = new WebResultsRun(_Runs.NextId(), this, Prepared.Files, _Seams.Time.GetTimestamp());
            if (!_Runs.TryBegin(Run))
            {
                if (_Runs.IsRefreshing)
                {
                    Status = ResultsWording.RefreshRunning;
                }
                else if (!_Runs.IsCoolingDown)
                {
                    Status = ResultsWording.NotStartedOtherSession;
                }
                return;
            }
            //accepted: the previous run's results give way to this one
            _Run = Run;
            _Outcome = null;
            _OutcomeOf = 0;
            _Progress = null;
            _StageAtCancel = null;
            ListNotice = null;
            foreach (ResultsSearchRow Row in Rows)
            {
                Row.SetLocked(true);
                Row.RunState = Run.Files.Contains(Row.FileName, StringComparer.Ordinal) ? ResultsWording.Waiting : null;
            }
            Status = ResultsWording.Searching(Run.Files.Count);
            WebResultsSeams Seams = _Seams;
            WebResultsRuns Runs = _Runs;
            //a background thread of its own (never a foreground thread: it can't keep the process alive)
            Task<SearchRunResult> Work = Task.Factory.StartNew(() => Execute(Seams, Plan, Run), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Run.Work = Work.ContinueWith(Done =>
            {
                SearchRunResult Result = Done.Status == TaskStatus.RanToCompletion ? Done.Result : ResultsOutcome.Unexpected(Done.Exception?.GetBaseException());
                //the web UI's run is free before the session sees the end, so the session can start again at once
                Runs.End(Run, Result.Status == SearchRunStatus.Blocked);
                Run.Ended(Result, Seams.Time.GetTimestamp());
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        // the worker: GS1's run of the plan. GS1 never throws for an outcome; anything else becomes a failed run whose problem
        // names only the exception's type
        private static SearchRunResult Execute(WebResultsSeams Seams, SearchRunPlan Plan, WebResultsRun Run)
        {
            try
            {
                return Seams.Run(Plan, Run, Run.Token, Seams.Options);
            }
            catch (Exception ex)
            {
                return ResultsOutcome.Unexpected(ex);
            }
        }

        // Cancel: the run the page showed stops at its next checkpoint; safe to repeat, never starts anything
        internal void Cancel(string? SentRun)
        {
            Follow();
            _Acted = true;
            _Focus = "status";
            if (!IsRunning || _Run!.CancelRequested
                || !long.TryParse(SentRun, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long Id) || Id != _Run.Id)
            {
                return;
            }
            _StageAtCancel = StageText();
            _Run.Cancel();
            Status = ResultsWording.Canceling;
            Revision++;
        }

        // the session ended (a sign-out, a password change, the idle time): its run is canceled; from any thread
        internal void End() => Volatile.Read(ref _Run)?.Cancel();

        // ---- the page's automatic progress (D108) ----

        // What an open page of a run asks for while the run goes on: the run's progress or end taken over, as a GET takes
        // it over, and its state as the page shows it. It only looks: it starts, cancels, chooses and reads nothing (no
        // file, no campground data), and leaves the page's after-action focus and notice to the next page. Asked names the
        // run the page shows (RunKey); a page of another run, of an earlier start of the web UI or of no run is told so,
        // and is shown nothing of a run. Seq: the session's answer number (its workspace's, D109)
        internal WebLiveAnswer Observe(string? Asked, long Seq)
        {
            Follow();
            if (!_Loaded || _Run == null || !string.Equals(Asked, _Runs.KeyOf(_Run), StringComparison.Ordinal))
            {
                return WebLiveAnswer.Replaced(Seq);
            }
            string Key = _Runs.KeyOf(_Run);
            if (!IsRunning)
            {
                return WebLiveAnswer.Ended(Key, Seq, EndedRank);
            }
            return new WebLiveAnswer(WebLiveAnswer.FormatVersion, LiveState, Key, Seq, LiveRank, LiveView, LiveFields(), WebLiveBar.Of(Bar()));
        }

        // A run's states only go forward (D108): running, canceling (once Cancel was asked for), ended. The page and its
        // answers say the same of a moment (its state, rank, view and fields), so the script changes only what changed
        internal const int RunningRank = 1;
        internal const int CancelingRank = 2;
        internal const int EndedRank = 3;
        internal const string RunningState = "running";
        internal const string CancelingState = "canceling";
        internal string LiveState => IsCanceling ? CancelingState : RunningState;
        internal int LiveRank => IsCanceling ? CancelingRank : RunningRank;
        // what the page's markup around the progress shows (its controls: Cancel and its label; which rows have a state): a
        // canceling run's page has another, so the script takes the page again
        internal string LiveView => WebLive.Signature("results", RunKey, LiveState, ProgressDetails() != null,
            string.Join(",", Enumerable.Range(0, Rows.Count).Where(i => Rows[i].RunState != null)));
        // the run's state in the page's words, by the page's element ids: the status, the stage with its counts and the
        // elapsed time, and each chosen file's state at its row ("file-<row>-state")
        internal Dictionary<string, string> LiveFields()
        {
            var Fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = Status };
            if (ProgressDetails() is string Details)
            {
                Fields["progress-details"] = Details;
            }
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].RunState is string State)
                {
                    Fields["file-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-state"] = State;
                }
            }
            return Fields;
        }

        // ---- what the page shows ----

        // the stage, the counts whose totals are known and the elapsed time, while a run goes on
        internal string? ProgressDetails()
        {
            if (!IsRunning)
            {
                return null;
            }
            string Elapsed = ResultText.Elapsed(_Seams.Time.GetElapsedTime(_Run!.Started));
            string? Details = _Progress == null
                ? ResultsWording.ProgressDetails(_StageAtCancel ?? ResultsWording.ReadingTheSearches, null, null, Elapsed)
                : ResultsWording.ProgressDetails(StageText(), ResultsWording.Campgrounds(_Progress), ResultsWording.Requests(_Progress), Elapsed);
            return Details.Length > 0 ? Details : null;
        }

        // where the bar stands (ResultsWording.Bar): indeterminate until a total is known
        internal (bool Indeterminate, double Value, double Maximum) Bar() => _Progress == null ? (true, 0, 1) : ResultsWording.Bar(_Progress);

        // the last ended run's results: when it ended, which search is shown (the one the page asked for by its place in
        // the run, when the page was of this run; else the first with openings) and which of its rows (a page of
        // ResultsListing)
        internal string? EndedText() => _Outcome == null ? null : ResultsWording.EndedAgo(_Seams.Time.GetElapsedTime(_OutcomeEndedAt));

        internal int ShownIndex(long? Run, int? Search)
        {
            if (_Outcome == null || _Outcome.Searches.Count == 0)
            {
                return -1;
            }
            if (Run == _OutcomeOf && Search is int Asked && Asked >= 0 && Asked < _Outcome.Searches.Count)
            {
                return Asked;
            }
            return _Outcome.Shown is ResultSearchViewModel First ? IndexOf(_Outcome.Searches, First) : 0;
        }

        internal ResultsListing? Listing(int Index, long? Run, int? Page) =>
            _Outcome == null || Index < 0 ? null : ResultsListing.Of(_Outcome.Searches[Index], Run == _OutcomeOf ? Page ?? 1 : 1);

        private static int IndexOf(IReadOnlyList<ResultSearchViewModel> Searches, ResultSearchViewModel Search)
        {
            for (int i = 0; i < Searches.Count; i++)
            {
                if (ReferenceEquals(Searches[i], Search))
                {
                    return i;
                }
            }
            return 0;
        }

        private string StageText() => _Run != null && _Run.CancelRequested && _StageAtCancel != null ? _StageAtCancel
            : _Progress == null ? ResultsWording.ReadingTheSearches : ResultsWording.Stage(_Progress.Stage, _Progress.RequestsRetrying);

        // ---- the run, taken over by a request ----

        // what the run did since this session's last request: its latest progress (each chosen file's state at its row), or
        // its end (the results replace the progress at once; the rows can be chosen again)
        private void Follow()
        {
            if (_Run is not WebResultsRun Run || _OutcomeOf == Run.Id)
            {
                _Progress = null;
                return;
            }
            if (Run.Result is SearchRunResult Result)
            {
                _Outcome = new ResultsOutcome(Result, Run.Files);
                _OutcomeOf = Run.Id;
                _OutcomeEndedAt = Run.EndedAt;
                _Progress = null;
                Status = ResultsWording.Finished(_Outcome.Summary, ResultText.Elapsed(_Seams.Time.GetElapsedTime(Run.Started, Run.EndedAt)));
                foreach (ResultsSearchRow Row in Rows)
                {
                    Row.SetLocked(false);
                    Row.RunState = _Outcome.RunStateOf(Row.FileName);
                }
                Revision++;
                return;
            }
            _Progress = Run.Progress;
            if (_Progress != null)
            {
                foreach (SearchProgressItem Item in _Progress.Searches)
                {
                    ResultsSearchRow? Row = Rows.FirstOrDefault(R => R.FileName == Item.FileName);
                    if (Row != null)
                    {
                        Row.RunState = ResultsWording.State(Item.State);
                    }
                }
            }
        }

        // the settings folder's search files against the list (ResultsSearchList.Rescan); a change of the files, of
        // whether they can run or of the choice is a new revision
        private List<string> Rescan(bool AtStart)
        {
            string Before = Signature();
            List<string> Unchosen = _List.Rescan(AtStart, Notice => ListNotice = Notice);
            if (!string.Equals(Signature(), Before, StringComparison.Ordinal))
            {
                Revision++;
            }
            return Unchosen;
        }

        private string Signature() => string.Join("\n", Rows.Select(R => (R.IsSelected ? "1" : "0") + (R.IsRunnable ? "1" : "0") + (R.IsMissing ? "1" : "0") + R.FileName));
    }
}
