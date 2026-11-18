using Avalonia.Threading;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using ReactiveUI.Reactive;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // What Settings > System > Campground data uses to refresh, wait and keep time. Production: the library's
    // RidbRefreshService, the dispatcher of the UI thread that started the refresh and the system's monotonic clock. Tests
    // replace them to control every step.
    internal sealed class RefreshSeams
    {
        internal Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>?, CancellationToken, RidbRefreshResult> Run { get; init; } =
            (Request, Progress, Token) => RidbRefreshService.Run(Request, Progress, Token);
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        // queues a job for the UI thread and never blocks; null: the dispatcher of the UI thread that started the refresh
        // (captured there: the worker never asks for Dispatcher.UIThread)
        internal Action<Action>? Post { get; init; }
        // a one-shot timer on the UI thread (the progress gate)
        internal Func<TimeSpan, Action, IDisposable> Delay { get; init; } = (Due, Job) => DispatcherTimer.RunOnce(Job, Due, DispatcherPriority.Background);
        // a repeating timer on the UI thread (the elapsed time)
        internal Func<TimeSpan, Action, IDisposable> Ticker { get; init; } = (Interval, Job) => DispatcherTimer.Run(() => { Job(); return true; }, Interval, DispatcherPriority.Background);
        // the last refresh in words, from General.yaml, read-only
        internal Func<string, string> LastRefreshed { get; init; } = CampgroundDataViewModel.ReadLastRefreshed;
        // The automatic restart after a successful refresh (D62), on a worker thread: the handoff, the new process and its
        // acknowledgment (AutomaticRestart). Tests replace it: no test starts a process.
        internal Func<RestartHandoff, Func<bool>, RestartAttempt> Restart { get; init; } =
            (Handoff, StillWanted) => AutomaticRestart.Run(Handoff, new RestartLaunchOptions(), StillWanted);
        // Ends this process once the new one has taken over; null: the application's desktop lifetime, and without one (the
        // designer, the headless tests) there is no automatic restart.
        internal Action? Exit { get; init; }
        // the clock of the handoff's creation time
        internal Func<DateTime> UtcNow { get; init; } = () => DateTime.UtcNow;
    }

    // What the automatic restart learns from the window (D62): MainWindow sets Capture (its normal bounds, maximized or not,
    // the tabs shown); MainWindowViewModel sets HasUnsavedChanges (an edit in Settings > Campertron or the Searches tab,
    // which a restart would lose, so there is none then).
    internal sealed class RestartHooks
    {
        internal Func<RestartCapture?>? Capture { get; set; }
        internal Func<bool> HasUnsavedChanges { get; init; } = () => false;
    }

    internal readonly record struct RestartCapture(RestartWindow? Window, RestartTopTab Tab, RestartSettingsTab SettingsTab);

    // what a finished refresh means to the user
    public enum RefreshResultKind { None, Success, Warning, Canceled, Failure }

    // Settings > System > Campground data (V011-1, decision D61; business-rules-gui.md §25): when the campground data was
    // last refreshed, and the console's "Refresh Ridb Data" as an action. Refresh campground data… shows a confirmation in
    // the card (network use, about 570 MB, several minutes, the automatic restart afterwards); nothing starts before Start
    // refresh.
    // The refresh runs on a worker thread through the library's RidbRefreshService, one at a time, owned by an operation
    // with its own ID and cancellation; every callback checks that its operation is still the current one. Progress reaches
    // the UI thread through a ProgressGate (at most ten updates a second): the stage, the bytes, files or entries where
    // their number is known, and the elapsed time; the status line, a polite live region, announces each stage and the
    // result. Cancel works until the cache step; from there the refresh finishes safely and Cancel is unavailable. The
    // Results tab's search and the refresh exclude each other (DataOperationGate), each disabled with its reason while the
    // other runs. Closing the window cancels a refresh that can still stop, and Program.Main waits for it either way
    // (App.WaitForPendingRefresh). Messages are fixed text: no path, exception message or stack trace is shown.
    // After a successful refresh Campertron restarts itself (decision D62, business-rules-gui.md §26): nothing is reloaded in
    // this process; a new one, given a one-time handoff of the window's place and size, opens at Settings > System with a
    // one-time status. If it can't (no process started or acknowledged, unsaved changes in another tab), this window stays
    // and says to restart Campertron manually; there is no second attempt.
    public sealed class CampgroundDataViewModel : ViewModelBase
    {
        internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);
        internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

        public const string Explanation = "Campertron's list of campgrounds and campsites comes from Recreation.gov's RIDB export. A refresh downloads the latest export "
            + "(about 570 MB) and builds a new campground database from it; it can take several minutes. Your settings and search files stay as they are.";
        public const string ConfirmationText = "Campertron will download Recreation.gov's RIDB export (about 570 MB, from ridb.recreation.gov) and build a new campground "
            + "database. This can take several minutes; you can cancel until the new data is being installed. After a successful refresh Campertron restarts "
            + "automatically to load the new data and reopens at Settings > System, in the same place on the screen when the system allows it. If it can't "
            + "restart, the new data stays installed and you can restart Campertron yourself.";
        internal const string SearchRunningReason = "Unavailable while a search runs in the Results tab. Refresh the campground data when the search has finished.";
        internal const string NoFoldersReason = "Campertron couldn't find its cache folder, so it can't refresh the campground data. Restart Campertron.";
        internal const string FinishingText = "Finishing safely: the new campground data is being installed. This can't be canceled now. If you close Campertron, it finishes first.";
        // the automatic restart (D62)
        internal const string RestartingText = "The campground data was refreshed. Restarting Campertron…";
        internal const string RestartFailedText = "The campground data was refreshed, but Campertron could not restart automatically. Restart Campertron manually to use the new data.";
        internal const string UnsavedChangesText = "The campground data was refreshed. Campertron didn't restart automatically because Settings or Searches have unsaved "
            + "changes: save or revert them, then restart Campertron to use the new data.";
        internal const string TimestampNote = " Campertron also couldn't record the date in General.yaml, so it may refresh again when it starts.";
        internal const string RestartedText = "The campground data was refreshed and Campertron restarted successfully.";
        internal const string RestartedWithoutTimestampText = "The campground data was refreshed and Campertron restarted successfully, but it couldn't record the date "
            + "in General.yaml, so it may refresh again when it starts.";

        private readonly DataOperationGate _Gate;
        private readonly RefreshSeams _Seams;
        private readonly RestartHooks _Hooks;
        private readonly Action? _Refreshed;
        private long _NextId;
        private Operation? _Current;
        private Task<RidbRefreshResult?> _PendingRefresh = Task.FromResult<RidbRefreshResult?>(null);
        private Task<RestartAttempt?> _PendingRestart = Task.FromResult<RestartAttempt?>(null);
        private RidbRefreshResult? _ShownResult;
        // read by the restart's worker (StillWanted), written on the UI thread
        private volatile bool _Detached;
        private IDisposable? _Ticker;
        private object? _TickerOwner;
        private RidbRefreshStage? _ShownStage;

        public CampgroundDataViewModel(string ConfigPath, string? CachePath, DataOperationGate Gate, Action? Refreshed)
            : this(ConfigPath, CachePath, Gate, Refreshed, new RefreshSeams())
        {
        }

        // Hooks: what the automatic restart learns from the window; Arrival: this process is the one an automatic restart
        // started (D62), and its one-time status says so.
        internal CampgroundDataViewModel(string ConfigPath, string? CachePath, DataOperationGate Gate, Action? Refreshed, RefreshSeams Seams,
            RestartHooks? Hooks = null, RestartOutcome? Arrival = null)
        {
            this.ConfigPath = ConfigPath;
            this.CachePath = CachePath;
            _Gate = Gate;
            _Refreshed = Refreshed;
            _Seams = Seams;
            _Hooks = Hooks ?? new RestartHooks();
            _LastRefreshedText = Seams.LastRefreshed(ConfigPath);
            if (Arrival is RestartOutcome Arrived)
            {
                _StatusText = Arrived == RestartOutcome.Refreshed ? RestartedText : RestartedWithoutTimestampText;
                _ResultKind = Arrived == RestartOutcome.Refreshed ? RefreshResultKind.Success : RefreshResultKind.Warning;
            }
            RefreshCommand = new RelayCommand(RequestRefresh, () => CanRequest);
            StartCommand = new RelayCommand(Start, () => IsConfirming && CanBegin);
            BackCommand = new RelayCommand(Back, () => IsConfirming);
            CancelCommand = new RelayCommand(Cancel, () => CanCancel);
            Gate.Changed += GateChanged;
        }

        // the folders Load.GetConfig prepared (never shown here)
        public string ConfigPath { get; }
        public string? CachePath { get; }
        public bool PathsAvailable => Path.IsPathFullyQualified(ConfigPath) && CachePath != null && Path.IsPathFullyQualified(CachePath);

        // ---- the last refresh ----

        private string _LastRefreshedText;
        // General.yaml's lastRidbDataRefresh in words, as the current configuration shows it ("unknown" when it can't be read)
        public string LastRefreshedText
        {
            get => _LastRefreshedText;
            private set => this.RaiseAndSetIfChanged(ref _LastRefreshedText, value);
        }

        // how often the last refresh was read (tests)
        internal int Reads { get; private set; } = 1;

        // Settings > System is shown, or the window is activated while it is shown: General.yaml is read again (another
        // program, the console or Settings > Campertron may have changed it)
        public void Show()
        {
            if (_Detached)
            {
                return;
            }
            ReadLastRefreshed();
            RefreshCommands();
        }

        private void ReadLastRefreshed()
        {
            Reads++;
            LastRefreshedText = _Seams.LastRefreshed(ConfigPath);
        }

        internal static string ReadLastRefreshed(string ConfigPath)
        {
            DateTime Now = DateTime.UtcNow;
            GeneralConfig? General = CurrentConfiguration.ReadGeneral(ConfigPath, Now);
            return General == null ? CurrentConfiguration.Unknown : CurrentConfiguration.RefreshText(General.LastRidbDataRefresh, Now, CultureInfo.CurrentCulture);
        }

        // ---- asking ----

        public RelayCommand RefreshCommand { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand CancelCommand { get; }

        private bool _IsConfirming;
        // the confirmation is shown in the card
        public bool IsConfirming
        {
            get => _IsConfirming;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsConfirming, value);
                this.RaisePropertyChanged(nameof(ShowsRefreshButton));
            }
        }

        public bool CanRequest => !_Detached && _Current == null && !IsRestarting && !IsConfirming && PathsAvailable && _Gate.Current == DataOperation.None;
        private bool CanBegin => !_Detached && _Current == null && !IsRestarting && PathsAvailable && _Gate.Current == DataOperation.None;
        public bool ShowsRefreshButton => !IsConfirming && !IsRunning && !IsRestarting;

        private bool _IsRestarting;
        // the refresh succeeded and Campertron is restarting (D62): no Refresh, no Cancel, the gate still held
        public bool IsRestarting
        {
            get => _IsRestarting;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsRestarting, value);
                this.RaisePropertyChanged(nameof(ShowsRefreshButton));
            }
        }

        // why Refresh campground data… (or Start refresh, in the confirmation) can't be used now, or null
        public string? UnavailableReason => _Current != null || IsRestarting ? null
            : !PathsAvailable ? NoFoldersReason
            : _Gate.Current == DataOperation.Search ? SearchRunningReason
            : null;
        public bool HasUnavailableReason => UnavailableReason != null;

        // Refresh campground data…: the confirmation, nothing else
        public void RequestRefresh()
        {
            if (!CanRequest)
            {
                return;
            }
            IsConfirming = true;
            RefreshCommands();
        }

        // Back (or Escape) in the confirmation: nothing starts
        public void Back()
        {
            if (!IsConfirming)
            {
                return;
            }
            IsConfirming = false;
            RefreshCommands();
        }

        // ---- running ----

        private bool _IsRunning;
        public bool IsRunning
        {
            get => _IsRunning;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsRunning, value);
                this.RaisePropertyChanged(nameof(ShowsRefreshButton));
                this.RaisePropertyChanged(nameof(ShowsProgressDetails));
            }
        }

        private bool _IsCancellable;
        // the refresh can still stop (until the cache step)
        public bool IsCancellable
        {
            get => _IsCancellable;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsCancellable, value);
                this.RaisePropertyChanged(nameof(IsFinishing));
            }
        }
        // past the cache step: the refresh installs, checks and records to its end
        public bool IsFinishing => IsRunning && !IsCancellable;

        private bool _IsCanceling;
        public bool IsCanceling
        {
            get => _IsCanceling;
            private set
            {
                this.RaiseAndSetIfChanged(ref _IsCanceling, value);
                this.RaisePropertyChanged(nameof(CancelLabel));
            }
        }
        public bool CanCancel => _Current != null && !_Current.CancelRequested && IsCancellable;
        public string CancelLabel => IsCanceling ? "Canceling…" : "Cancel";

        // the task of the latest refresh; Program.Main waits for it after the window closed (it never faults)
        internal Task<RidbRefreshResult?> PendingRefresh => _PendingRefresh;
        // the current refresh's ID, or 0 (tests)
        internal long CurrentRefreshId => _Current?.Id ?? 0;
        internal bool IsDetached => _Detached;

        // Start refresh, in the confirmation
        public void Start()
        {
            if (!IsConfirming || !CanBegin)
            {
                return;
            }
            if (!_Gate.TryEnter(DataOperation.Refresh))
            {
                RefreshCommands();
                return;
            }
            IsConfirming = false;
            Action<Action> Post = _Seams.Post ?? UiPost();
            var Op = new Operation(++_NextId, _Seams.Time.GetTimestamp());
            Op.Gate = new ProgressGate<RidbRefreshProgress>(P => P.Sequence, P => ApplyProgress(Op, P), Post, _Seams.Delay, _Seams.Time, ProgressInterval);
            _Current = Op;
            _ShownStage = null;
            ResultKind = RefreshResultKind.None;
            NeedsManualRestart = false;
            IsRunning = true;
            IsCancellable = true;
            IsCanceling = false;
            StageText = StageName(RidbRefreshStage.AcquiringLock);
            CountText = null;
            ElapsedText = ResultText.Elapsed(TimeSpan.Zero);
            SetBar(true, 0, 1);
            StatusText = "Refreshing the campground data: " + StageText + ".";
            StartTicker();
            RefreshCommands();

            var Request = new RidbRefreshRequest(ConfigPath, CachePath!);
            RefreshSeams Seams = _Seams;
            CancellationToken Token = Op.Token;
            ProgressGate<RidbRefreshProgress> Gate = Op.Gate;
            // a background thread of its own (never a foreground thread: Program.Main keeps the process alive for it)
            Task<RidbRefreshResult?> Work = Task.Factory.StartNew<RidbRefreshResult?>(() => Execute(Seams, Request, Gate, Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _PendingRefresh = Work;
            Work.ContinueWith(Done =>
            {
                RidbRefreshResult Result = Done.Result ?? Unexpected(null);
                Op.Dispose();
                Post(() => Complete(Op, Result));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        // the dispatcher of this (the UI) thread, for the worker's posts
        private static Action<Action> UiPost()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            return Job => Ui.Post(Job, DispatcherPriority.Background);
        }

        // the worker: the service never throws for an outcome; anything else becomes Unexpected, by type only
        private static RidbRefreshResult Execute(RefreshSeams Seams, RidbRefreshRequest Request, IProgress<RidbRefreshProgress> Progress, CancellationToken Token)
        {
            try
            {
                return Seams.Run(Request, Progress, Token);
            }
            catch (Exception ex)
            {
                return Unexpected(ex);
            }
        }

        private static RidbRefreshResult Unexpected(Exception? ex) =>
            new RidbRefreshResult(RidbRefreshOutcome.Unexpected, RidbRefreshStage.AcquiringLock, false, null, TimeSpan.Zero, RidbRefreshResult.NoProblems, null, ex, false);

        // Cancel: the refresh stops at its next checkpoint and removes what it downloaded; safe to repeat
        public void Cancel()
        {
            Operation? Op = _Current;
            if (Op == null || Op.CancelRequested || !IsCancellable)
            {
                return;
            }
            Op.Cancel();
            IsCanceling = true;
            StatusText = "Canceling: the refresh stops at its next safe point and removes what it downloaded. The campground data stays as it is.";
            RefreshCommands();
        }

        // The window closed: no later callback changes anything. A refresh that can still stop is canceled (the token is not
        // read from the cache step on, so one that is installing finishes). The UI thread never waits for it; Program.Main
        // waits for it after the window has closed.
        public void Detach()
        {
            _Detached = true;
            _Gate.Changed -= GateChanged;
            Operation? Op = _Current;
            Op?.Cancel();
            Op?.Gate?.Close();
            StopTicker();
        }

        private bool IsCurrent(Operation Op) => !_Detached && ReferenceEquals(Op, _Current);

        // a progress snapshot, on the UI thread (at most ten a second)
        private void ApplyProgress(Operation Op, RidbRefreshProgress Snapshot)
        {
            if (!IsCurrent(Op))
            {
                return;
            }
            bool StageChanged = _ShownStage != Snapshot.Stage;
            _ShownStage = Snapshot.Stage;
            IsCancellable = Snapshot.CanCancel;
            StageText = StageName(Snapshot.Stage);
            CountText = Count(Snapshot);
            if (Snapshot.Total is long Total && Total > 0 && Snapshot.Done is long Done
                && (Snapshot.Stage == RidbRefreshStage.Downloading || Snapshot.Stage == RidbRefreshStage.Extracting || Snapshot.Stage == RidbRefreshStage.Importing))
            {
                SetBar(false, Done, Total);
            }
            else if (Snapshot.Done == null || Snapshot.Total == null)
            {
                SetBar(true, 0, 1);
            }
            if (StageChanged)
            {
                // a polite announcement for each stage (never for each byte); after a cancel only the finishing stages
                if (!Snapshot.CanCancel)
                {
                    StatusText = (Op.CancelRequested ? "Too late to cancel: " + StageText : Sentence(StageText)) + ". " + FinishingText;
                }
                else if (!Op.CancelRequested)
                {
                    StatusText = "Refreshing the campground data: " + StageText + ".";
                }
            }
            ElapsedText = ResultText.Elapsed(_Seams.Time.GetElapsedTime(Op.Started));
            RefreshCommands();
        }

        // the refresh ended, on the UI thread: its result replaces the progress at once
        private void Complete(Operation Op, RidbRefreshResult Result)
        {
            // a successful refresh restarts Campertron (D62): the gate stays held until the restart has failed or this
            // process ends, so no search starts meanwhile
            bool Restarts = IsCurrent(Op) && Result.Installed
                && (Result.Outcome == RidbRefreshOutcome.Refreshed || Result.Outcome == RidbRefreshOutcome.RefreshedWithoutTimestamp);
            if (!Restarts && (ReferenceEquals(Op, _Current) || _Current == null))
            {
                _Gate.Leave(DataOperation.Refresh);
            }
            if (!IsCurrent(Op))
            {
                return;
            }
            Op.Gate?.Close();
            _Current = null;
            StopTicker();
            ElapsedText = ResultText.Elapsed(_Seams.Time.GetElapsedTime(Op.Started));
            IsRunning = false;
            IsCancellable = false;
            IsCanceling = false;
            CountText = null;
            SetBar(false, 1, 1);
            (RefreshResultKind Kind, string Text) = Describe(Result);
            LastResult = Result;
            _ShownResult = Result;
            ResultKind = Kind;
            if (!Restarts)
            {
                StatusText = Text + " Elapsed " + ElapsedText + ".";
            }
            if (Result.Installed)
            {
                // the date in this card and in the current configuration, and the General editor (a clean one reloads, one
                // with unsaved changes shows the change as a conflict); the tabs keep the data they show until the restart
                ReadLastRefreshed();
                _Refreshed?.Invoke();
            }
            if (Restarts)
            {
                BeginRestart(Result);
            }
            RefreshCommands();
        }

        // the last result (tests)
        internal RidbRefreshResult? LastResult { get; private set; }

        // ---- the automatic restart (D62) ----

        // the last restart attempt and why it ended (tests and diagnostics)
        internal RestartAttempt? LastRestart { get; private set; }
        // the latest restart's worker (tests); it never faults
        internal Task<RestartAttempt?> PendingRestart => _PendingRestart;

        // On the UI thread, right after a successful refresh: capture the window, then hand off and start the new process on
        // a worker thread. Not when another tab has unsaved changes (a restart would lose them) and not without a desktop
        // lifetime to end this process with.
        private void BeginRestart(RidbRefreshResult Result)
        {
            RestartOutcome Outcome = Result.Outcome == RidbRefreshOutcome.Refreshed ? RestartOutcome.Refreshed : RestartOutcome.RefreshedWithoutTimestamp;
            string Note = Outcome == RestartOutcome.Refreshed ? string.Empty : TimestampNote;
            if (_Hooks.HasUnsavedChanges())
            {
                EndRestart(new RestartAttempt(RestartAttemptStatus.Unavailable, "UnsavedChanges"), UnsavedChangesText + Note + " Elapsed " + ElapsedText + ".", RefreshResultKind.Warning);
                return;
            }
            Action? Exit = _Seams.Exit ?? ApplicationExit();
            if (Exit == null)
            {
                EndRestart(new RestartAttempt(RestartAttemptStatus.Unavailable, "NoDesktopLifetime"), RestartFailedText + Note + " Elapsed " + ElapsedText + ".", RefreshResultKind.Warning);
                return;
            }
            RestartCapture? Capture = _Hooks.Capture?.Invoke();
            var Handoff = new RestartHandoff(_Seams.UtcNow(), Outcome, Capture?.Window, Capture?.Tab ?? RestartTopTab.Settings, Capture?.SettingsTab ?? RestartSettingsTab.System);
            IsRestarting = true;
            StatusText = (Result.CancelRequested ? "The refresh had already reached the installation when Cancel was selected, so it finished. " : string.Empty) + RestartingText;
            Action<Action> Post = _Seams.Post ?? UiPost();
            RefreshSeams Seams = _Seams;
            Task<RestartAttempt?> Work = Task.Factory.StartNew<RestartAttempt?>(() =>
            {
                try
                {
                    return Seams.Restart(Handoff, () => !_Detached);
                }
                catch (Exception ex)
                {
                    return new RestartAttempt(RestartAttemptStatus.LaunchFailed, ex.GetType().Name);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _PendingRestart = Work;
            Work.ContinueWith(Done => Post(() => RestartEnded(Done.Result ?? new RestartAttempt(RestartAttemptStatus.LaunchFailed), Exit, Note)),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        // the restart's end, on the UI thread: acknowledged, this process ends; otherwise it stays, says so and frees the gate
        private void RestartEnded(RestartAttempt Attempt, Action Exit, string Note)
        {
            LastRestart = Attempt;
            if (_Detached)
            {
                // the window closed meanwhile: an ordinary close (the new process, if one started, carries on alone)
                return;
            }
            if (Attempt.Succeeded)
            {
                Exit();
                return;
            }
            EndRestart(Attempt, RestartFailedText + Note + " Elapsed " + ElapsedText + ".", RefreshResultKind.Warning);
        }

        private void EndRestart(RestartAttempt Attempt, string Text, RefreshResultKind Kind)
        {
            LastRestart = Attempt;
            System.Diagnostics.Trace.TraceWarning("Campertron: no automatic restart after the refresh (" + Attempt.Status + (Attempt.Detail == null ? string.Empty : ", " + Attempt.Detail) + ").");
            IsRestarting = false;
            NeedsManualRestart = true;
            _Gate.Leave(DataOperation.Refresh);
            ResultKind = Kind;
            StatusText = Text;
            RefreshCommands();
        }

        // the running application's way to end (its classic desktop lifetime), or null
        private static Action? ApplicationExit()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime Desktop)
            {
                return () => Desktop.Shutdown(0);
            }
            return null;
        }

        // whether this result was shown in the window (Program.Main reports one that ended after the window closed)
        internal bool WasShown(RidbRefreshResult Result) => ReferenceEquals(Result, _ShownResult);

        // ---- the words ----

        internal static string StageName(RidbRefreshStage Stage) => Stage switch
        {
            RidbRefreshStage.AcquiringLock => "starting",
            RidbRefreshStage.CleaningUp => "removing what an earlier refresh left",
            RidbRefreshStage.Preparing => "preparing the download",
            RidbRefreshStage.Downloading => "downloading the campground data from Recreation.gov",
            RidbRefreshStage.Extracting => "extracting the downloaded files",
            RidbRefreshStage.PreparingDatabase => "preparing a new campground database",
            RidbRefreshStage.Importing => "reading the campground data",
            RidbRefreshStage.Saving => "saving the campground data",
            RidbRefreshStage.Indexing => "indexing the campsite details",
            RidbRefreshStage.Validating => "checking the new campground database",
            RidbRefreshStage.Finalizing => "finishing the new campground database",
            RidbRefreshStage.ClearingCache => "clearing the cache",
            RidbRefreshStage.Installing => "installing the new campground data",
            RidbRefreshStage.CheckingDatabase => "checking the installed campground data",
            RidbRefreshStage.RecordingRefresh => "recording the refresh in General.yaml",
            _ => "finishing",
        };

        // a stage's words at the start of a sentence
        private static string Sentence(string? Text) => string.IsNullOrEmpty(Text) ? string.Empty : char.ToUpperInvariant(Text[0]) + Text.Substring(1);

        // the counts whose total is known (bytes while downloading, with the total when the server sent it)
        internal static string? Count(RidbRefreshProgress Snapshot)
        {
            if (Snapshot.Done is not long Done)
            {
                return null;
            }
            return Snapshot.Stage switch
            {
                RidbRefreshStage.Downloading => Snapshot.Total is long Total && Total > 0
                    ? Megabytes(Done) + " of " + Megabytes(Total) + " downloaded"
                    : Megabytes(Done) + " downloaded",
                RidbRefreshStage.Extracting => Snapshot.Total is long Entries ? Done + " of " + Entries + " files extracted" : null,
                RidbRefreshStage.Importing => Snapshot.Total is long Files ? Done + " of " + Files + " files read" : null,
                _ => null,
            };
        }

        private static string Megabytes(long Bytes) => (Bytes / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

        // the result in words, by its kind: fixed text, never a path or an exception's message (an unexpected problem is
        // named by its type only)
        internal static (RefreshResultKind Kind, string Text) Describe(RidbRefreshResult Result)
        {
            const string Unchanged = " The campground data was not changed.";
            // a success restarts Campertron (D62); the card then shows the restart's words instead
            return Result.Outcome switch
            {
                RidbRefreshOutcome.Refreshed => (RefreshResultKind.Success,
                    (Result.CancelRequested ? "The refresh had already reached the installation when Cancel was selected, so it finished. " : string.Empty)
                    + "The campground data was refreshed."),
                RidbRefreshOutcome.RefreshedWithoutTimestamp => (RefreshResultKind.Warning,
                    "The campground data was refreshed, but Campertron couldn't record the date in General.yaml, so it may refresh again when it starts."),
                RidbRefreshOutcome.Canceled => (RefreshResultKind.Canceled, "The refresh was canceled." + Unchanged),
                RidbRefreshOutcome.AnotherRefreshRunning => (RefreshResultKind.Failure,
                    "Another refresh of the campground data is running (in Campertron's console or another Campertron window). Try again when it has finished." + Unchanged),
                RidbRefreshOutcome.DownloadFailed => (RefreshResultKind.Failure,
                    "The download from Recreation.gov failed. Check the internet connection and try again." + Unchanged),
                RidbRefreshOutcome.DownloadTimedOut => (RefreshResultKind.Failure,
                    "The download from Recreation.gov stopped responding, so Campertron ended it. Try again later." + Unchanged),
                RidbRefreshOutcome.ExtractionFailed => (RefreshResultKind.Failure,
                    "The downloaded file is not a readable Recreation.gov export. Try again later." + Unchanged),
                RidbRefreshOutcome.ImportFailed => (RefreshResultKind.Failure,
                    "The downloaded campground data could not be imported. Try again later." + Unchanged),
                RidbRefreshOutcome.ValidationFailed => (RefreshResultKind.Failure,
                    "The downloaded campground data failed Campertron's checks, so it was not installed." + Unchanged),
                RidbRefreshOutcome.StorageFailed => (RefreshResultKind.Failure,
                    "Campertron couldn't read or write the files it needs (for example, the disk may be full or a folder may not be writable)." + Unchanged),
                RidbRefreshOutcome.InstallationFailed => (RefreshResultKind.Failure,
                    "Campertron couldn't install the new campground data (another program may have been using the database). Try again." + Unchanged),
                RidbRefreshOutcome.InstalledButCheckFailed => (RefreshResultKind.Failure,
                    "The new campground data was installed, but Campertron's check of it found a problem. Restart Campertron; if the campground list or searches fail, refresh the campground data again."),
                _ => Result.Installed
                    ? (RefreshResultKind.Failure, "The new campground data was installed, but an unexpected problem (" + (Result.ErrorType ?? "unknown") + ") stopped the refresh afterwards. Restart Campertron.")
                    : (RefreshResultKind.Failure, "The refresh stopped because of an unexpected problem (" + (Result.ErrorType ?? "unknown") + ")." + Unchanged),
            };
        }

        // ---- progress and status ----

        private string? _StatusText;
        // the card's status line, a polite live region: each stage, then the result
        public string? StatusText
        {
            get => _StatusText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _StatusText, value);
                this.RaisePropertyChanged(nameof(HasStatus));
            }
        }
        public bool HasStatus => StatusText != null;

        private RefreshResultKind _ResultKind;
        public RefreshResultKind ResultKind
        {
            get => _ResultKind;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ResultKind, value);
                this.RaisePropertyChanged(nameof(ResultIsError));
            }
        }
        // the status line shows a failure (its words say so too)
        public bool ResultIsError => ResultKind == RefreshResultKind.Failure;

        private bool _NeedsManualRestart;
        // the data was refreshed but Campertron didn't restart automatically: the user restarts it to use the new data (D62)
        public bool NeedsManualRestart
        {
            get => _NeedsManualRestart;
            private set => this.RaiseAndSetIfChanged(ref _NeedsManualRestart, value);
        }

        private string? _StageText;
        public string? StageText
        {
            get => _StageText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _StageText, value);
                RaiseDetails();
            }
        }
        private string? _CountText;
        public string? CountText
        {
            get => _CountText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _CountText, value);
                RaiseDetails();
            }
        }
        private string? _ElapsedText;
        public string? ElapsedText
        {
            get => _ElapsedText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ElapsedText, value);
                RaiseDetails();
            }
        }
        // the counts and the elapsed time, in one line (not a live region: the status line announces the stages)
        public string ProgressDetails => string.Join(" · ", new[] { CountText, ElapsedText == null ? null : "Elapsed " + ElapsedText }
            .Where(T => !string.IsNullOrEmpty(T)));
        public bool ShowsProgressDetails => IsRunning && ProgressDetails.Length > 0;

        private void RaiseDetails()
        {
            this.RaisePropertyChanged(nameof(ProgressDetails));
            this.RaisePropertyChanged(nameof(ShowsProgressDetails));
        }

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

        private void SetBar(bool Indeterminate, double Value, double Maximum)
        {
            ProgressIsIndeterminate = Indeterminate;
            ProgressMaximum = Math.Max(1, Maximum);
            ProgressValue = Math.Min(Value, ProgressMaximum);
        }

        // ---- the elapsed time ----

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

        // every second while a refresh runs
        internal void Tick()
        {
            if (_Detached || _Current == null)
            {
                StopTicker();
                return;
            }
            ElapsedText = ResultText.Elapsed(_Seams.Time.GetElapsedTime(_Current.Started));
        }

        private void GateChanged()
        {
            RefreshCommands();
        }

        private void RefreshCommands()
        {
            RefreshCommand.Refresh();
            StartCommand.Refresh();
            BackCommand.Refresh();
            CancelCommand.Refresh();
            this.RaisePropertyChanged(nameof(CanRequest));
            this.RaisePropertyChanged(nameof(CanCancel));
            this.RaisePropertyChanged(nameof(UnavailableReason));
            this.RaisePropertyChanged(nameof(HasUnavailableReason));
            this.RaisePropertyChanged(nameof(IsFinishing));
        }

        // One refresh: its ID, cancellation, progress gate and start time. The token is handed to the worker before anything
        // can cancel or dispose it; Cancel and Dispose are serialized, so a late Cancel is harmless.
        private sealed class Operation
        {
            private readonly object _Lock = new object();
            private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
            private bool _Disposed;

            internal Operation(long Id, long Started)
            {
                this.Id = Id;
                this.Started = Started;
                Token = _Cts.Token;
            }

            internal long Id { get; }
            internal long Started { get; }
            internal CancellationToken Token { get; }
            internal ProgressGate<RidbRefreshProgress>? Gate { get; set; }
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
                        // a registration threw; the token is canceled anyway
                    }
                }
            }

            // after the refresh has ended (its worker no longer uses the token)
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
