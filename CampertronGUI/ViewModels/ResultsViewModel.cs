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
    public sealed class ResultsViewModel : ViewModelBase
    {
        internal static readonly TimeSpan BlockCooldown = TimeSpan.FromMinutes(5);
        internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
        internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        private readonly ResultsSeams _Seams;
        private readonly CampgroundCatalog _Catalog;
        private readonly Func<string, bool> _IsUnsaved;
        private readonly DataOperationGate _Gate;
        private long _NextId;
        private Operation? _Current;
        private Task _PendingRun = Task.CompletedTask;
        private bool _Detached;
        private bool _IsLoaded;
        private IDisposable? _Ticker;
        private object? _TickerOwner;
        private long? _BlockedAt;

        public ResultsViewModel(string ConfigPath, string? CachePath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved)
            : this(ConfigPath, CachePath, Catalog, IsUnsaved, new ResultsSeams())
        {
        }

        // Gate: the window's data operations (V011-1); a tab of its own has a gate of its own
        internal ResultsViewModel(string ConfigPath, string? CachePath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved, ResultsSeams Seams,
            DataOperationGate? Gate = null)
        {
            this.ConfigPath = ConfigPath;
            this.CachePath = CachePath;
            _Catalog = Catalog;
            _IsUnsaved = IsUnsaved;
            _Seams = Seams;
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
            _StatusText = PathsAvailable ? "Choose the searches to run, then select Start." : "Campertron couldn't find its cache folder, so it can't search. Restart Campertron.";
        }

        // the folders Load.GetConfig prepared (never shown in the results)
        public string ConfigPath { get; }
        public string? CachePath { get; }
        public bool PathsAvailable => Path.IsPathFullyQualified(ConfigPath) && CachePath != null && Path.IsPathFullyQualified(CachePath);

        // ---- the search files ----

        public ObservableCollection<ResultsSearchRow> Searches { get; } = new ObservableCollection<ResultsSearchRow>();
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
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(Row.IsRunnable && Row.IsEnabled);
            }
        }

        public void SelectAllRunnable()
        {
            if (IsRunning)
            {
                return;
            }
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(Row.IsRunnable);
            }
        }

        public void ClearSelection()
        {
            if (IsRunning)
            {
                return;
            }
            foreach (ResultsSearchRow Row in Searches)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(false);
            }
        }

        // The settings folder's search files (the console's discovery and order) against the list. A new file is chosen
        // when it is enabled (autoRun: true) and can run; a file that can't run any more is unchosen; a file that is no
        // longer enabled is unchosen unless the user chose it. At Start (AtStart) nothing is chosen by itself, and a chosen
        // file that is gone stays in the list, still chosen, so the run reports it as missing. Returns the names of chosen
        // files that the disk unchose.
        private List<string> Rescan(bool AtStart)
        {
            var Unchosen = new List<string>();
            if (IsRunning)
            {
                return Unchosen;
            }
            List<string> Names;
            try
            {
                Names = Directory.Exists(ConfigPath) ? ConfigFiles.SortedSearchFileNames(ConfigPath) : new List<string>();
                ListNotice = Directory.Exists(ConfigPath) ? null : "The settings folder does not exist.";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Names = new List<string>();
                ListNotice = "Campertron couldn't read the settings folder.";
            }
            DateTime Now = _Seams.Now();
            var Old = Searches.ToDictionary(R => R.FileName, StringComparer.Ordinal);
            var Rows = new List<ResultsSearchRow>();
            foreach (string Name in Names)
            {
                SearchPlanEntry? Entry;
                try
                {
                    Entry = _Seams.Inspect(ConfigPath, Name, Now);
                }
                catch (Exception)
                {
                    Entry = null;
                }
                bool IsNew = !Old.TryGetValue(Name, out ResultsSearchRow? Row);
                Row ??= new ResultsSearchRow(Name, SelectionChanged);
                bool WasSelected = Row.IsSelected;
                bool WasRunnable = Row.IsRunnable;
                bool WasEnabled = Row.IsEnabled;
                Row.Update(Entry, _IsUnsaved(Name), CampgroundText(Entry?.CampgroundId));
                if (IsNew || (!WasRunnable && !Row.SelectedByUser))
                {
                    // the default: the enabled files that can run; never at Start, where the user has not seen the file
                    Row.SetSelected(!AtStart && Row.IsRunnable && Row.IsEnabled);
                }
                else if (WasSelected && WasEnabled && !Row.IsEnabled && !Row.SelectedByUser)
                {
                    Row.SetSelected(false);
                }
                if (WasSelected && !Row.IsSelected)
                {
                    Unchosen.Add(Name);
                }
                Rows.Add(Row);
            }
            if (AtStart)
            {
                // a chosen file deleted since it was listed: asked for anyway, so the run says it is missing
                foreach (ResultsSearchRow Gone in Old.Values.Where(R => R.IsSelected && !Names.Contains(R.FileName, StringComparer.Ordinal)))
                {
                    Gone.MarkMissing();
                    Rows.Insert(Math.Min(Rows.Count, Searches.IndexOf(Gone)), Gone);
                }
            }
            Searches.Clear();
            foreach (ResultsSearchRow Row in Rows)
            {
                Row.SetLocked(false);
                Searches.Add(Row);
            }
            this.RaisePropertyChanged(nameof(HasNoSearches));
            this.RaisePropertyChanged(nameof(IsLoaded));
            RefreshCommands();
            return Unchosen;
        }

        // the campground's name and place from the campground data (one query when the tab is first shown), or null
        private string? CampgroundText(string? Id)
        {
            if (string.IsNullOrWhiteSpace(Id) || _Catalog.LoadError != null)
            {
                return null;
            }
            CampgroundPickerEntry? Entry = _Catalog.Find(Id);
            return Entry?.Choice.Label;
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
        public string? DataOperationNotice => _Current == null && _Gate.Current == DataOperation.Refresh
            ? "Start is unavailable while the campground data is refreshed (Settings > System). It is available again when the refresh has finished."
            : null;
        public bool HasDataOperationNotice => DataOperationNotice != null;

        private void GateChanged()
        {
            RefreshCommands();
        }
        public bool CanCancel => _Current != null && !_Current.CancelRequested;
        public string CancelLabel => IsCanceling ? "Canceling…" : "Cancel";

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
            // the files on disk now: their bytes, read by the plan below, are what is searched
            List<string> Unchosen = Rescan(true);
            if (Unchosen.Count > 0)
            {
                ListNotice = "Search files changed on disk, so these are no longer chosen: " + string.Join(", ", Unchosen) + ". Check the choice, then select Start again.";
                StatusText = "Not started: the search files changed on disk.";
                return;
            }
            List<string> Names = Searches.Where(R => R.IsSelected).Select(R => R.FileName).ToList();
            if (Names.Count == 0)
            {
                StatusText = "Not started: no search is chosen.";
                return;
            }
            // GS1's plan reads each chosen file once (the run searches exactly these bytes) and checks it; a missing file is
            // a typed outcome of the run while the others are searched. Nothing is started, and the previous results stay,
            // unless at least one file can be searched.
            var Request = new SearchRunRequest(ConfigPath, CachePath!, Names);
            SearchRunPlan Plan;
            try
            {
                Plan = _Seams.Plan(Request, _Seams.Now());
            }
            catch (Exception)
            {
                StatusText = "Not started: Campertron couldn't read the chosen search files.";
                return;
            }
            if (!Plan.Entries.Any(E => E.State == SearchPlanState.Ready))
            {
                foreach (SearchPlanEntry Entry in Plan.Entries)
                {
                    ResultsSearchRow? Row = Searches.FirstOrDefault(R => R.FileName == Entry.FileName);
                    if (Row != null)
                    {
                        Row.RunState = "Can't be searched: " + (Entry.Issues.Count > 0 ? Entry.Issues[0].Message : CampsiteSearch.Message(SearchIssueCode.FileInvalid));
                    }
                }
                StatusText = "Not started: none of the chosen searches can be searched now.";
                return;
            }

            // the campground data must not be refreshed meanwhile (V011-1): the gate is held until the run has ended
            if (!_Gate.TryEnter(DataOperation.Search))
            {
                StatusText = "Not started: the campground data is being refreshed.";
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
                Row.RunState = Names.Contains(Row.FileName, StringComparer.Ordinal) ? "Waiting" : null;
            }
            IsRunning = true;
            IsCanceling = false;
            AppliedProgress = 0;
            ElapsedText = ResultText.Elapsed(TimeSpan.Zero);
            StageText = "Reading the searches";
            CampgroundsText = null;
            RequestsText = null;
            ProgressIsIndeterminate = true;
            StatusText = "Searching " + ResultSearchViewModel.Count(Names.Count, "search", "searches") + "…";
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
                SearchRunResult Result = Done.Status == TaskStatus.RanToCompletion ? Done.Result : Unexpected(Done.Exception?.GetBaseException());
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
                return Unexpected(ex);
            }
        }

        private static SearchRunResult Unexpected(Exception? ex)
        {
            var Problem = new SearchIssue(SearchIssueCode.InternalError, SearchIssueSeverity.Error, CampsiteSearch.Message(SearchIssueCode.InternalError),
                DefectType: ex?.GetType().FullName);
            return new SearchRunResult(SearchRunStatus.Failed, DateTime.UtcNow, TimeSpan.Zero, 0, 0, 0, SearchLists.Empty<SearchResult>(), SearchLists.Of(new[] { Problem }))
            {
                Defects = ex == null ? SearchLists.Empty<Exception>() : SearchLists.Of(new[] { ex }),
            };
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
            StatusText = "Canceling… Searches that already finished keep their results.";
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
                StageText = Stage(Snapshot.Stage, Snapshot.RequestsRetrying);
            }
            CampgroundsText = Snapshot.CampgroundsPlanned > 0
                ? "Campsite details: " + Snapshot.CampgroundsPrepared + " of " + ResultSearchViewModel.Count(Snapshot.CampgroundsPlanned, "campground", "campgrounds") + " ready"
                : null;
            RequestsText = Snapshot.RequestsPlanned > 0
                ? "Availability: " + Snapshot.RequestsCompleted + " of " + ResultSearchViewModel.Count(Snapshot.RequestsPlanned, "month request", "month requests") + " finished"
                  + (Snapshot.RequestsFailed > 0 ? ", " + Snapshot.RequestsFailed + " not checked" : string.Empty)
                  + (Snapshot.RequestsRetrying > 0 ? ", " + Snapshot.RequestsRetrying + " waiting to be tried again" : string.Empty)
                : null;
            // a bar only where the total is known: the campgrounds while their details are prepared, the month requests while
            // availability is checked; otherwise indeterminate
            if ((Snapshot.Stage == SearchRunStage.CheckingAvailability || Snapshot.Stage == SearchRunStage.Retrying) && Snapshot.RequestsPlanned > 0)
            {
                SetBar(false, Snapshot.RequestsCompleted, Snapshot.RequestsPlanned);
            }
            else if (Snapshot.Stage == SearchRunStage.PreparingCampsiteDetails && Snapshot.CampgroundsPlanned > 0)
            {
                SetBar(false, Snapshot.CampgroundsPrepared, Snapshot.CampgroundsPlanned);
            }
            else
            {
                SetBar(true, 0, 1);
            }
            foreach (SearchProgressItem Item in Snapshot.Searches)
            {
                ResultsSearchRow? Row = Searches.FirstOrDefault(R => R.FileName == Item.FileName);
                if (Row != null)
                {
                    Row.RunState = State(Item.State);
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
                _BlockedAt = _Seams.Time.GetTimestamp();
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

        private static string Stage(SearchRunStage Stage, int Retrying) => Stage switch
        {
            SearchRunStage.Validating => "Reading the searches",
            SearchRunStage.PreparingCampsiteDetails => "Preparing campsite details",
            SearchRunStage.CheckingAvailability => "Checking availability at recreation.gov",
            SearchRunStage.Retrying => "Checking availability; " + ResultSearchViewModel.Count(Retrying, "request is", "requests are")
                + " waiting to be tried again (each month is asked for at most three times)",
            SearchRunStage.BuildingResults => "Building the results",
            _ => "Finishing",
        };

        private static string State(SearchState State) => State switch
        {
            SearchState.Waiting => "Waiting",
            SearchState.Searching => "Searching",
            SearchState.Complete => "Complete",
            SearchState.Incomplete => "Incomplete",
            SearchState.Failed => "Failed",
            SearchState.Canceled => "Canceled",
            SearchState.Skipped => "Not searched",
            _ => "Can't be searched",
        };

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
        public string ProgressDetails => string.Join(" · ", new[] { StageText, CampgroundsText, RequestsText, ElapsedText == null ? null : "Elapsed " + ElapsedText }
            .Where(T => !string.IsNullOrEmpty(T)));
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
        public bool IsCoolingDown => _BlockedAt.HasValue && _Seams.Time.GetElapsedTime(_BlockedAt.Value) < BlockCooldown;

        private void UpdateCooldown()
        {
            if (!_BlockedAt.HasValue)
            {
                CooldownText = null;
                return;
            }
            TimeSpan Left = BlockCooldown - _Seams.Time.GetElapsedTime(_BlockedAt.Value);
            if (Left <= TimeSpan.Zero)
            {
                _BlockedAt = null;
                CooldownText = null;
                StopTicker();
                RefreshCommands();
                return;
            }
            CooldownText = "recreation.gov limited Campertron's requests. Start is available again in "
                + ResultText.Elapsed(Left + TimeSpan.FromSeconds(0.999)) + ".";
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
            if (_BlockedAt.HasValue)
            {
                UpdateCooldown();
            }
            else if (_Current == null)
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
        public string SelectedCampsiteText => SelectedRow?.CampsiteTitle is string Title ? Title + " (ID " + SelectedRow.CampsiteId + ")" : "Choose a campsite in the list.";

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
            LastResult = Result;
            _RunStatus = Result.Status;
            bool Blocked = Result.Status == SearchRunStatus.Blocked;
            List<ResultSearchViewModel> Shown = Result.Searches.Select(S => new ResultSearchViewModel(S, Blocked)).ToList();
            int Openings = Shown.Sum(S => S.Openings);
            string Found = ResultSearchViewModel.Count(Openings, "opening", "openings") + " found in " + ResultSearchViewModel.Count(Shown.Count, "search", "searches");
            RunSummary = Result.Status switch
            {
                SearchRunStatus.Completed => "Finished: " + Found + ".",
                SearchRunStatus.CompletedWithProblems => "Finished with problems: some searches or months were not checked. " + Found + ".",
                SearchRunStatus.Blocked => "Stopped: recreation.gov limited the requests, so no further request was made. What arrived before that is shown; the months not checked are listed. " + Found + ".",
                SearchRunStatus.Canceled => "Canceled. Searches that had finished keep their results; months not checked are listed, never as without availability. " + Found + ".",
                _ => "Nothing could be searched.",
            };
            StatusText = RunSummary + " Elapsed " + ElapsedText + ".";
            RunIssueLines = Result.Issues.Select(ResultText.Issue).Distinct(StringComparer.Ordinal).ToList();
            ResultSearches = Shown;
            SelectedResult = Shown.FirstOrDefault(S => S.Openings > 0) ?? Shown.FirstOrDefault();
            this.RaisePropertyChanged(nameof(RunHadProblems));
            foreach (ResultsSearchRow Row in Searches)
            {
                ResultSearchViewModel? Its = Shown.FirstOrDefault(S => S.FileName == Row.FileName);
                Row.RunState = Its?.StatusText ?? (Files.Contains(Row.FileName, StringComparer.Ordinal) ? "Not searched" : null);
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

        internal async Task OpenBookingAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            if (!BookingLink.TryCreate(Row.BookingUrl, Row.CampsiteId, out Uri? Link))
            {
                Report(Row, false, "Campertron did not open this link: it is not a recreation.gov campsite page.");
                return;
            }
            bool Opened;
            try
            {
                Opened = LinkLauncher != null && await LinkLauncher(Link);
            }
            catch (Exception)
            {
                Opened = false;
            }
            Report(Row, Opened, Opened ? "Opened the booking page of " + Row.CampsiteTitle + " in your browser."
                : "Campertron couldn't open the booking page. Use Copy booking link and paste it into a browser.");
        }

        internal async Task CopyCampsiteIdAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            bool Copied = await Copy(Row.CampsiteId);
            Report(Row, Copied, Copied ? "Copied the campsite ID " + Row.CampsiteId + "." : "Campertron couldn't copy the campsite ID.");
        }

        internal async Task CopyBookingLinkAsync()
        {
            ResultRow? Row = SelectedRow;
            if (Row?.CampsiteId == null)
            {
                return;
            }
            if (!BookingLink.TryCreate(Row.BookingUrl, Row.CampsiteId, out Uri? Link))
            {
                Report(Row, false, "Campertron did not copy this link: it is not a recreation.gov campsite page.");
                return;
            }
            bool Copied = await Copy(Link.AbsoluteUri);
            Report(Row, Copied, Copied ? "Copied the booking link of " + Row.CampsiteTitle + "." : "Campertron couldn't copy the booking link.");
        }

        private async Task<bool> Copy(string Text)
        {
            try
            {
                return CopyText != null && await CopyText(Text);
            }
            catch (Exception)
            {
                return false;
            }
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
