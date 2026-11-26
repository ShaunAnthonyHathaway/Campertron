using Avalonia.Threading;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // What the Results tab uses to search, wait and keep time. Production: GS1's CampsiteSearch, the Avalonia dispatcher
    // and the system's monotonic clock. Tests replace them to control every step.
    internal sealed class ResultsSeams
    {
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        // queues a job for the UI thread; never blocks. Null (production): a post to the dispatcher of the UI thread,
        // captured there when a run is accepted (Start), so no worker thread ever asks for Dispatcher.UIThread (TD-56)
        internal Action<Action>? Post { get; init; }
        // a one-shot timer on the UI thread
        internal Func<TimeSpan, Action, IDisposable> Delay { get; init; } = (Due, Job) => DispatcherTimer.RunOnce(Job, Due, DispatcherPriority.Background);
        // a repeating timer on the UI thread (the elapsed time and the cooldown)
        internal Func<TimeSpan, Action, IDisposable> Ticker { get; init; } = (Interval, Job) => DispatcherTimer.Run(() => { Job(); return true; }, Interval, DispatcherPriority.Background);
        // reads one search file as a plan would (listing only; never searches or writes)
        internal Func<string, string, DateTime, SearchPlanEntry> Inspect { get; init; } = CampsiteSearch.LoadEntry;
        internal Func<SearchRunRequest, DateTime, SearchRunPlan> Plan { get; init; } = CampsiteSearch.Plan;
        internal Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult> Run { get; init; } = CampsiteSearch.Run;
        // GS1's interactive host options (null: its defaults and the production transport)
        internal SearchHostOptions? Options { get; init; }
    }

    // The Results tab (GS2): chooses saved search files, runs them through GS1's host-neutral search service
    // (CampsiteSearch.Plan and Run: the console's own search, manual-search semantics, no Camp.History, no Email settings)
    // and shows the typed results. One run at a time, each owned by an operation with its own ID and cancellation; every
    // callback of a run checks that its operation is still the current one, so nothing of an earlier or abandoned run can
    // change what is shown. A run that recreation.gov blocks (the block page or HTTP 429) disables Start for five minutes in
    // this window only. Since V011-1 (D61) a run and a refresh of the campground data exclude each other (DataOperationGate):
    // Start is disabled, with its reason, while the campground data is refreshed. Nothing here writes a file, reads
    // General.yaml or Email.yaml, or contacts recreation.gov except through CampsiteSearch.Run.
    // Since the shared form layer (D79's PR 3) this class is the desktop GUI's run of a search: its worker thread, the
    // dispatcher, the timers and what the view binds. The list of search files and the rules of its choice
    // (ResultsSearchList), the words (ResultsWording), the progress throttle (ProgressGate), the five minutes after a block
    // (BlockHold) and the results' rows (ResultSearchViewModel, ResultRow) are the library's, for the web UI too; since
    // v0.3.0's PR 7 (D97) so are a Start's checks (ResultsStart), an ended run as the tab shows it (ResultsOutcome) and the
    // view's own words (ResultsWording), which the web UI's results page uses as well.
    public sealed class ResultsViewModel : ViewModelBase
    {
        internal static readonly TimeSpan BlockCooldown = BlockHold.Duration;
        internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
        internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        private readonly ResultsSeams _Seams;
        private readonly CampgroundCatalog _Catalog;
        private readonly ResultsSearchList _List;
        private readonly BlockHold _Hold;
        private readonly DataOperationGate _Gate;
        private long _NextId;
        private Operation? _Current;
        private Task _PendingRun = Task.CompletedTask;
        private bool _Detached;
        private bool _IsLoaded;
        private IDisposable? _Ticker;
        private object? _TickerOwner;

        public ResultsViewModel(string ConfigPath, string? CachePath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved)
            : this(ConfigPath, CachePath, Catalog, IsUnsaved, new ResultsSeams())
        {
        }

        // Gate: the window's data operations (V011-1); a tab of its own has a gate of its own. Hold: the five minutes after a
        // block, the window's, which its monitoring shares (v0.4.0's phase 4, as the web UI's results page and monitoring
        // share theirs); a tab of its own has a hold of its own
        internal ResultsViewModel(string ConfigPath, string? CachePath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved, ResultsSeams Seams,
            DataOperationGate? Gate = null, BlockHold? Hold = null)
        {
            this.ConfigPath = ConfigPath;
            this.CachePath = CachePath;
            _Catalog = Catalog;
            _Seams = Seams;
            _List = new ResultsSearchList(ConfigPath, Catalog, IsUnsaved, Seams.Inspect, Seams.Now, SelectionChanged);
            _Hold = Hold ?? new BlockHold(Seams.Time);
            _Gate = Gate ?? new DataOperationGate();
            _Gate.Changed += GateChanged;
            StartCommand = new RelayCommand(Start, () => CanStart);
            CancelCommand = new RelayCommand(Cancel, () => CanCancel);
            SelectEnabledCommand = new RelayCommand(SelectEnabled, () => !IsRunning && Searches.Any(R => R.IsRunnable));
            SelectAllCommand = new RelayCommand(SelectAllRunnable, () => !IsRunning && Searches.Any(R => R.IsRunnable));
            ClearSelectionCommand = new RelayCommand(ClearSelection, () => !IsRunning && Searches.Any(R => R.IsSelected));
            OpenBookingCommand = new RelayCommand(OpenBooking, () => SelectedRow?.HasCampsite == true);
            CopyIdCommand = new RelayCommand(CopyCampsiteId, () => SelectedRow?.HasCampsite == true);
            CopyLinkCommand = new RelayCommand(CopyBookingLink, () => SelectedRow?.HasCampsite == true);
            _StatusText = PathsAvailable ? ResultsWording.Idle : ResultsWording.NoCacheFolder;
        }

        // the folders Load.GetConfig prepared (never shown in the results)
        public string ConfigPath { get; }
        public string? CachePath { get; }
        public bool PathsAvailable => Path.IsPathFullyQualified(ConfigPath) && CachePath != null && Path.IsPathFullyQualified(CachePath);

        // ---- the search files ----

        // the list's rows (ResultsSearchList, the library's): the settings folder's search files and their choice
        public ObservableCollection<ResultsSearchRow> Searches => _List.Rows;
        public bool IsLoaded => _IsLoaded;
        public bool HasNoSearches => _IsLoaded && Searches.Count == 0;
        public RelayCommand SelectEnabledCommand { get; }
        public RelayCommand SelectAllCommand { get; }
        public RelayCommand ClearSelectionCommand { get; }

        private string? _ListNotice;
        // a problem with the settings folder or a selection that changed on disk, or null
        public string? ListNotice
        {
            get => _ListNotice;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ListNotice, value);
                this.RaisePropertyChanged(nameof(HasListNotice));
            }
        }
        public bool HasListNotice => ListNotice != null;

        // the tab is shown: the first time the campground data and the files are read, later the files again (not while
        // a search runs: its selection is fixed)
        public void Show()
        {
            if (_Detached)
            {
                return;
            }
            if (!_IsLoaded)
            {
                _IsLoaded = true;
                _Catalog.Ensure();
            }
            Rescan(false);
        }

        // the window was activated while the tab is shown: another program may have changed the files
        public void CheckForExternalChanges()
        {
            if (_IsLoaded && !_Detached)
            {
                Rescan(false);
            }
        }

        // Select enabled: the default choice again, every enabled file that can run (V011-3: "enabled" is autoRun: true;
        // nothing runs until Start)
        public void SelectEnabled()
        {
            if (IsRunning)
            {
                return;
            }
            _List.SelectEnabled();
        }

        public void SelectAllRunnable()
        {
            if (IsRunning)
            {
                return;
            }
            _List.SelectAllRunnable();
        }

        public void ClearSelection()
        {
            if (IsRunning)
            {
                return;
            }
            _List.ClearSelection();
        }

        // The settings folder's search files against the list, by the list's rules (ResultsSearchList.Rescan): the default
        // choice, what the disk unchooses, and at Start (AtStart) a chosen file that is gone kept so the run reports it as
        // missing. Not while a search runs: its selection is fixed. Returns the names of chosen files that the disk unchose.
        private List<string> Rescan(bool AtStart)
        {
            if (IsRunning)
            {
                return new List<string>();
            }
            List<string> Unchosen = _List.Rescan(AtStart, Notice => ListNotice = Notice);
            this.RaisePropertyChanged(nameof(HasNoSearches));
            this.RaisePropertyChanged(nameof(IsLoaded));
            RefreshCommands();
            return Unchosen;
        }

        private void SelectionChanged()
        {
            RefreshCommands();
        }

        public int SelectedCount => Searches.Count(R => R.IsSelected);

        // ---- running ----

        public RelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }

        private bool _IsRunning;
        public bool IsRunning
        {
            get => _IsRunning;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsRunning, value);
                this.RaisePropertyChanged(nameof(IsIdle));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }
        public bool IsIdle => !IsRunning;

        private bool _IsCanceling;
        public bool IsCanceling
        {
            get => _IsCanceling;
            private set => this.RaiseAndSetIfChanged(ref _IsCanceling, value);
        }

        public bool CanStart => !_Detached && _Current == null && PathsAvailable && !IsCoolingDown && _Gate.Current == DataOperation.None && Searches.Any(R => R.IsSelected);

        // why Start is unavailable because of the window's other data operation (V011-1), or null
        public string? DataOperationNotice => _Current == null && _Gate.Current == DataOperation.Refresh ? ResultsWording.RefreshRunning : null;
        public bool HasDataOperationNotice => DataOperationNotice != null;

        private void GateChanged()
        {
            RefreshCommands();
        }
        public bool CanCancel => _Current != null && !_Current.CancelRequested;
        public string CancelLabel => ResultsWording.CancelLabel(IsCanceling);

        // the task of the latest run (Program.Main waits a bounded time for it after the window closed)
        internal Task PendingRun => _PendingRun;
        // the current run's ID, or 0 (tests)
        internal long CurrentRunId => _Current?.Id ?? 0;

        public void Start()
        {
            if (!CanStart)
            {
                return;
            }
            // the files on disk now: their bytes, read by GS1's plan, are what is searched; a missing file is a typed outcome
            // of the run while the others are searched. Nothing is started, and the previous results stay, unless at least
            // one file can be searched (ResultsStart, the layer's rules, which the web UI's Results page follows too)
            ResultsStart Prepared = ResultsStart.Prepare(Searches, () => Rescan(true),
                Names => _Seams.Plan(new SearchRunRequest(ConfigPath, CachePath!, Names), _Seams.Now()));
            if (Prepared.Plan is not SearchRunPlan Plan)
            {
                if (Prepared.ListNotice != null)
                {
                    ListNotice = Prepared.ListNotice;
                }
                StatusText = Prepared.Status!;
                return;
            }
            IReadOnlyList<string> Names = Prepared.Files;

            // the campground data must not be refreshed meanwhile (V011-1): the gate is held until the run has ended
            if (!_Gate.TryEnter(DataOperation.Search))
            {
                StatusText = ResultsWording.NotStartedRefreshing;
                return;
            }
            // accepted: the previous run's results give way to this one. The run's progress and its end reach the UI through
            // the dispatcher of this thread, the UI thread, captured now: GS1's threads and the run's continuation post to it
            // and never ask for Dispatcher.UIThread (TD-56: in Avalonia 12 the first thread that asks while no UI dispatcher
            // exists becomes the UI thread, as between two headless tests)
            Action<Action> Post = _Seams.Post ?? UiPost();
            var Op = new Operation(++_NextId, _Seams.Time.GetTimestamp(), Names);
            Op.Gate = new ProgressGate(Snapshot => ApplyProgress(Op, Snapshot), Post, _Seams.Delay, _Seams.Time, ProgressInterval);
            _Current = Op;
            ClearResults();
            ListNotice = null;
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.SetLocked(true);
                Row.RunState = Names.Contains(Row.FileName, StringComparer.Ordinal) ? ResultsWording.Waiting : null;
            }
            IsRunning = true;
            IsCanceling = false;
            AppliedProgress = 0;
            ElapsedText = ResultText.Elapsed(TimeSpan.Zero);
            StageText = ResultsWording.ReadingTheSearches;
            CampgroundsText = null;
            RequestsText = null;
            ProgressIsIndeterminate = true;
            StatusText = ResultsWording.Searching(Names.Count);
            StartTicker();
            RefreshCommands();

            ResultsSeams Seams = _Seams;
            CancellationToken Token = Op.Token;
            ProgressGate Gate = Op.Gate;
            // a background thread of its own (never a foreground thread: it can't keep the process alive)
            Task<SearchRunResult> Work = Task.Factory.StartNew(() => Execute(Seams, Plan, Gate, Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Op.Work = Work;
            _PendingRun = Work;
            Work.ContinueWith(Done =>
            {
                SearchRunResult Result = Done.Status == TaskStatus.RanToCompletion ? Done.Result : ResultsOutcome.Unexpected(Done.Exception?.GetBaseException());
                Op.Dispose();
                Post(() => Complete(Op, Result));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        // a post to the UI thread's dispatcher, asked for here, on the UI thread (Start), never on a worker (TD-56)
        private static Action<Action> UiPost()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            return Job => Ui.Post(Job, DispatcherPriority.Background);
        }

        // the worker: GS1's run of the plan. GS1 never throws for an outcome; anything else becomes a failed run whose
        // problem names only the exception's type
        private static SearchRunResult Execute(ResultsSeams Seams, SearchRunPlan Plan, ProgressGate Gate, CancellationToken Token)
        {
            try
            {
                return Seams.Run(Plan, Gate, Token, Seams.Options);
            }
            catch (Exception ex)
            {
                return ResultsOutcome.Unexpected(ex);
            }
        }

        // Cancel: the run stops at its next checkpoint (GS1: well under a second); safe to repeat, never starts anything
        public void Cancel()
        {
            Operation? Op = _Current;
            if (Op == null || Op.CancelRequested)
            {
                return;
            }
            Op.Cancel();
            IsCanceling = true;
            this.RaisePropertyChanged(nameof(CancelLabel));
            StatusText = ResultsWording.Canceling;
            RefreshCommands();
        }

        // the window closed: no later callback changes anything, the run is canceled and the timers stop. The UI thread
        // never waits for the run; Program.Main waits a bounded time for it after the window has closed.
        public void Detach()
        {
            _Detached = true;
            _Gate.Changed -= GateChanged;
            Operation? Op = _Current;
            Op?.Cancel();
            Op?.Gate?.Close();
            StopTicker();
        }
        internal bool IsDetached => _Detached;

        private bool IsCurrent(Operation Op) => !_Detached && ReferenceEquals(Op, _Current);

        // a progress snapshot, on the UI thread (at most ten a second, ProgressGate)
        private void ApplyProgress(Operation Op, SearchProgress Snapshot)
        {
            if (!IsCurrent(Op))
            {
                return;
            }
            AppliedProgress = Snapshot.Sequence;
            if (!Op.CancelRequested)
            {
                StageText = ResultsWording.Stage(Snapshot.Stage, Snapshot.RequestsRetrying);
            }
            CampgroundsText = ResultsWording.Campgrounds(Snapshot);
            RequestsText = ResultsWording.Requests(Snapshot);
            // a bar only where the total is known (ResultsWording.Bar); otherwise indeterminate
            (bool Indeterminate, double Value, double Maximum) = ResultsWording.Bar(Snapshot);
            SetBar(Indeterminate, Value, Maximum);
            foreach (SearchProgressItem Item in Snapshot.Searches)
            {
                ResultsSearchRow? Row = Searches.FirstOrDefault(R => R.FileName == Item.FileName);
                if (Row != null)
                {
                    Row.RunState = ResultsWording.State(Item.State);
                }
            }
            ElapsedText = ResultText.Elapsed(_Seams.Time.GetElapsedTime(Op.Started));
        }

        // the run ended, on the UI thread: its result replaces the progress at once
        private void Complete(Operation Op, SearchRunResult Result)
        {
            if (ReferenceEquals(Op, _Current))
            {
                _Gate.Leave(DataOperation.Search);
            }
            if (!IsCurrent(Op))
            {
                return;
            }
            Op.Gate?.Close();
            _Current = null;
            TimeSpan Elapsed = _Seams.Time.GetElapsedTime(Op.Started);
            IsRunning = false;
            IsCanceling = false;
            this.RaisePropertyChanged(nameof(CancelLabel));
            ElapsedText = ResultText.Elapsed(Elapsed);
            SetBar(false, 1, 1);
            StageText = null;
            ShowResult(Result, Op.Files);
            if (Result.Status == SearchRunStatus.Blocked)
            {
                _Hold.Begin();
                UpdateCooldown();
            }
            else if (_Hold.IsSet)
            {
                // the monitoring's block (v0.4.0's phase 4) holds Start too: its countdown goes on
                UpdateCooldown();
            }
            else
            {
                StopTicker();
            }
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.SetLocked(false);
            }
            RefreshCommands();
        }

        // ---- progress and status ----

        private string _StatusText;
        // the tab's status line (a polite live region)
        public string StatusText
        {
            get => _StatusText;
            private set => this.RaiseAndSetIfChanged(ref _StatusText, value);
        }

        private string? _StageText;
        public string? StageText
        {
            get => _StageText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _StageText, value);
                this.RaisePropertyChanged(nameof(ProgressDetails));
                this.RaisePropertyChanged(nameof(HasProgressDetails));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }
        private string? _CampgroundsText;
        public string? CampgroundsText
        {
            get => _CampgroundsText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CampgroundsText, value);
                this.RaisePropertyChanged(nameof(ProgressDetails));
                this.RaisePropertyChanged(nameof(HasProgressDetails));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }
        private string? _RequestsText;
        public string? RequestsText
        {
            get => _RequestsText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _RequestsText, value);
                this.RaisePropertyChanged(nameof(ProgressDetails));
                this.RaisePropertyChanged(nameof(HasProgressDetails));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }
        private string? _ElapsedText;
        public string? ElapsedText
        {
            get => _ElapsedText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ElapsedText, value);
                this.RaisePropertyChanged(nameof(ProgressDetails));
                this.RaisePropertyChanged(nameof(HasProgressDetails));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }
        // the stage, the counts whose totals are known and the elapsed time, in one line
        public string ProgressDetails => ResultsWording.ProgressDetails(StageText, CampgroundsText, RequestsText, ElapsedText);
        public bool HasProgressDetails => ProgressDetails.Length > 0;
        // shown while a run goes on (afterwards the status line says how long it took)
        public bool ShowsProgressDetails => IsRunning && HasProgressDetails;

        private bool _ProgressIsIndeterminate = true;
        public bool ProgressIsIndeterminate
        {
            get => _ProgressIsIndeterminate;
            private set => this.RaiseAndSetIfChanged(ref _ProgressIsIndeterminate, value);
        }
        private double _ProgressValue;
        public double ProgressValue
        {
            get => _ProgressValue;
            private set => this.RaiseAndSetIfChanged(ref _ProgressValue, value);
        }
        private double _ProgressMaximum = 1;
        public double ProgressMaximum
        {
            get => _ProgressMaximum;
            private set => this.RaiseAndSetIfChanged(ref _ProgressMaximum, value);
        }
        // the sequence number of the last snapshot shown (tests)
        internal long AppliedProgress { get; private set; }

        private void SetBar(bool Indeterminate, double Value, double Maximum)
        {
            ProgressIsIndeterminate = Indeterminate;
            ProgressMaximum = Math.Max(1, Maximum);
            ProgressValue = Math.Min(Value, ProgressMaximum);
        }

        // ---- the five minutes after a block ----

        private string? _CooldownText;
        public string? CooldownText
        {
            get => _CooldownText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CooldownText, value);
                this.RaisePropertyChanged(nameof(HasCooldown));
            }
        }
        public bool HasCooldown => CooldownText != null;
        public bool IsCoolingDown => _Hold.IsActive;

        // the hold's line with the time left (BlockHold); once the five minutes have passed Start is available again
        private void UpdateCooldown()
        {
            if (!_Hold.IsSet)
            {
                // since v0.4.0's phase 4 the hold is shared: the monitoring's reading may have forgotten a block whose five
                // minutes passed, and Start is available again here too
                if (CooldownText != null)
                {
                    CooldownText = null;
                    RefreshCommands();
                }
                return;
            }
            string? Text = _Hold.Text();
            if (Text == null)
            {
                CooldownText = null;
                StopTicker();
                RefreshCommands();
                return;
            }
            CooldownText = Text;
            StartTicker();
            RefreshCommands();
        }

        // one ticker at a time; a callback of a ticker that was stopped (an earlier run's) does nothing
        private void StartTicker()
        {
            if (_Ticker == null && !_Detached)
            {
                object Owner = new object();
                _TickerOwner = Owner;
                _Ticker = _Seams.Ticker(TickInterval, () =>
                {
                    if (ReferenceEquals(Owner, _TickerOwner))
                    {
                        Tick();
                    }
                });
            }
        }

        private void StopTicker()
        {
            _TickerOwner = null;
            _Ticker?.Dispose();
            _Ticker = null;
        }

        // The window's monitoring was blocked (v0.4.0's phase 4): the shared hold now keeps Start unavailable here too, with its
        // countdown, as after a run of this tab that was blocked. On the UI thread; the monitoring's own thread never calls it
        internal void HoldStarted()
        {
            if (_Detached)
            {
                return;
            }
            UpdateCooldown();
            RefreshCommands();
        }

        // every second while a run goes on or Start waits after a block
        internal void Tick()
        {
            if (_Detached)
            {
                StopTicker();
                return;
            }
            if (_Current != null)
            {
                ElapsedText = ResultText.Elapsed(_Seams.Time.GetElapsedTime(_Current.Started));
            }
            if (_Hold.IsSet || CooldownText != null)
            {
                UpdateCooldown();
            }
            if (!_Hold.IsSet && _Current == null)
            {
                StopTicker();
            }
        }

        // ---- results ----

        private IReadOnlyList<ResultSearchViewModel> _ResultSearches = Array.Empty<ResultSearchViewModel>();
        public IReadOnlyList<ResultSearchViewModel> ResultSearches
        {
            get => _ResultSearches;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ResultSearches, value);
                this.RaisePropertyChanged(nameof(HasResults));
            }
        }
        public bool HasResults => ResultSearches.Count > 0 || RunSummary != null;

        private ResultSearchViewModel? _SelectedResult;
        public ResultSearchViewModel? SelectedResult
        {
            get => _SelectedResult;
            set
            {
                if (!ReferenceEquals(_SelectedResult, value))
                {
                    this.RaiseAndSetIfChanged(ref _SelectedResult, value);
                    this.RaisePropertyChanged(nameof(SelectedRows));
                    this.RaisePropertyChanged(nameof(SelectedEmptyText));
                    this.RaisePropertyChanged(nameof(HasSelectedEmptyText));
                    SelectedRow = null;
                }
            }
        }
        // the rows of the search shown (none while no search is shown)
        public IReadOnlyList<ResultRow> SelectedRows => SelectedResult?.Rows ?? Array.Empty<ResultRow>();
        public string SelectedEmptyText => SelectedResult?.EmptyText ?? string.Empty;
        public bool HasSelectedEmptyText => SelectedEmptyText.Length > 0;

        private ResultRow? _SelectedRow;
        public ResultRow? SelectedRow
        {
            get => _SelectedRow;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedRow, value);
                this.RaisePropertyChanged(nameof(SelectedCampsiteText));
                ActionMessage = null;
                OpenBookingCommand.Refresh();
                CopyIdCommand.Refresh();
                CopyLinkCommand.Refresh();
            }
        }
        public string SelectedCampsiteText => ResultsWording.SelectedCampsite(SelectedRow);

        private string? _RunSummary;
        public string? RunSummary
        {
            get => _RunSummary;
            private set
            {
                this.RaiseAndSetIfChanged(ref _RunSummary, value);
                this.RaisePropertyChanged(nameof(HasResults));
            }
        }
        private SearchRunStatus? _RunStatus;
        // the last run's status (tests and the summary's style)
        internal SearchRunStatus? RunStatus => _RunStatus;
        public bool RunHadProblems => _RunStatus.HasValue && _RunStatus != SearchRunStatus.Completed;
        private IReadOnlyList<string> _RunIssueLines = Array.Empty<string>();
        public IReadOnlyList<string> RunIssueLines
        {
            get => _RunIssueLines;
            private set
            {
                this.RaiseAndSetIfChanged(ref _RunIssueLines, value);
                this.RaisePropertyChanged(nameof(HasRunIssues));
            }
        }
        public bool HasRunIssues => RunIssueLines.Count > 0;
        // the last result (tests)
        internal SearchRunResult? LastResult { get; private set; }

        private void ClearResults()
        {
            SelectedResult = null;
            ResultSearches = Array.Empty<ResultSearchViewModel>();
            RunIssueLines = Array.Empty<string>();
            _RunStatus = null;
            LastResult = null;
            RunSummary = null;
            this.RaisePropertyChanged(nameof(RunHadProblems));
        }

        private void ShowResult(SearchRunResult Result, IReadOnlyList<string> Files)
        {
            // the ended run as the layer shows it (ResultsOutcome, which the web UI's Results page shows too)
            var Outcome = new ResultsOutcome(Result, Files);
            LastResult = Result;
            _RunStatus = Result.Status;
            RunSummary = Outcome.Summary;
            StatusText = ResultsWording.Finished(RunSummary, ElapsedText);
            RunIssueLines = Outcome.IssueLines;
            ResultSearches = Outcome.Searches;
            SelectedResult = Outcome.Shown;
            this.RaisePropertyChanged(nameof(RunHadProblems));
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.RunState = Outcome.RunStateOf(Row.FileName);
            }
        }

        // ---- the selected campsite: its booking page and copies (only on the user's action) ----

        public RelayCommand OpenBookingCommand { get; }
        public RelayCommand CopyIdCommand { get; }
        public RelayCommand CopyLinkCommand { get; }
        // set by the view: the platform's launcher (Avalonia's ILauncher, never a shell) and the clipboard
        public Func<Uri, Task<bool>>? LinkLauncher { get; set; }
        public Func<string, Task<bool>>? CopyText { get; set; }

        private string? _ActionMessage;
        public string? ActionMessage
        {
            get => _ActionMessage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ActionMessage, value);
                this.RaisePropertyChanged(nameof(HasActionMessage));
            }
        }
        public bool HasActionMessage => ActionMessage != null;
        private bool _ActionIsError;
        public bool ActionIsError
        {
            get => _ActionIsError;
            private set => this.RaiseAndSetIfChanged(ref _ActionIsError, value);
        }

        public async void OpenBooking() => await OpenBookingAsync();
        public async void CopyCampsiteId() => await CopyCampsiteIdAsync();
        public async void CopyBookingLink() => await CopyBookingLinkAsync();

        // the selected campsite's actions (CampsiteActions, which the Monitoring tab's last check uses too, v0.4.0's phase 4)
        internal async Task OpenBookingAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            (bool Succeeded, string Message) = await CampsiteActions.OpenBookingAsync(Row, LinkLauncher);
            Report(Row, Succeeded, Message);
        }

        internal async Task CopyCampsiteIdAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            (bool Succeeded, string Message) = await CampsiteActions.CopyIdAsync(Row, CopyText);
            Report(Row, Succeeded, Message);
        }

        internal async Task CopyBookingLinkAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            (bool Succeeded, string Message) = await CampsiteActions.CopyLinkAsync(Row, CopyText);
            Report(Row, Succeeded, Message);
        }

        // a message about the row it was for (the selection may have changed meanwhile)
        private void Report(ResultRow Row, bool Succeeded, string Message)
        {
            if (!ReferenceEquals(Row, SelectedRow) || _Detached)
            {
                return;
            }
            ActionMessage = Message;
            ActionIsError = !Succeeded;
        }

        private void RefreshCommands()
        {
            StartCommand.Refresh();
            CancelCommand.Refresh();
            SelectEnabledCommand.Refresh();
            SelectAllCommand.Refresh();
            ClearSelectionCommand.Refresh();
            this.RaisePropertyChanged(nameof(CanStart));
            this.RaisePropertyChanged(nameof(CanCancel));
            this.RaisePropertyChanged(nameof(IsCoolingDown));
            this.RaisePropertyChanged(nameof(SelectedCount));
            this.RaisePropertyChanged(nameof(DataOperationNotice));
            this.RaisePropertyChanged(nameof(HasDataOperationNotice));
        }

        // One run: its ID, cancellation, files, progress gate, start time and task. The token is handed to the worker
        // before anything can cancel or dispose it; Cancel and Dispose are serialized, so a late Cancel is harmless.
        private sealed class Operation
        {
            private readonly object _Lock = new object();
            private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
            private bool _Disposed;

            internal Operation(long Id, long Started, IReadOnlyList<string> Files)
            {
                this.Id = Id;
                this.Started = Started;
                this.Files = Files;
                Token = _Cts.Token;
            }

            internal long Id { get; }
            internal long Started { get; }
            internal IReadOnlyList<string> Files { get; }
            internal CancellationToken Token { get; }
            internal ProgressGate? Gate { get; set; }
            internal Task<SearchRunResult>? Work { get; set; }
            internal bool CancelRequested { get; private set; }

            internal void Cancel()
            {
                lock (_Lock)
                {
                    CancelRequested = true;
                    if (_Disposed)
                    {
                        return;
                    }
                    try
                    {
                        _Cts.Cancel();
                    }
                    catch (AggregateException)
                    {
                        // a registration of the run threw; the token is canceled anyway
                    }
                }
            }

            // after the run has ended (its worker no longer uses the token)
            internal void Dispose()
            {
                lock (_Lock)
                {
                    if (!_Disposed)
                    {
                        _Disposed = true;
                        _Cts.Dispose();
                    }
                }
            }
        }
    }
}
