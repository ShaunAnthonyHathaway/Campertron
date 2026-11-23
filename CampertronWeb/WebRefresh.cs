using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronWeb
{
    // The system page's refresh (tests only change it): the library's refresh service, the clock, and the last refresh's
    // date as the card shows it (General.yaml read now, read-only)
    internal sealed class WebRefreshSeams
    {
        internal Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>?, CancellationToken, RidbRefreshResult> Run { get; init; } =
            (Request, Progress, Token) => RidbRefreshService.Run(Request, Progress, Token);
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal Func<string, string> LastRefreshed { get; init; } = ConfigPath => CurrentConfiguration.LastRefreshed(ConfigPath, DateTime.UtcNow, CultureInfo.CurrentCulture);
    }

    // what a page's Start refresh or Cancel did
    internal enum WebRefreshOutcome
    {
        Accepted,
        // the page showed another moment of the refresh (another refresh, its end, the page sent again): nothing was done
        Stale,
        AlreadyRunning,
        NotRunning,
        // a search of the results page goes on (the data operations' gate, D61)
        SearchRunning,
        // the settings or the cache folder is not known
        NoFolders,
        // the refresh has reached the cache step: it finishes, and Cancel can't stop it
        TooLate,
        ShuttingDown
    }

    // One refresh of the web UI: its ID, its start, its cancellation, the latest progress the service reported and, once it
    // has ended, the service's result. Its worker writes them; any request reads them (Volatile)
    internal sealed class WebRefreshOperation : IProgress<RidbRefreshProgress>
    {
        private readonly CancellationTokenSource _Cancel = new CancellationTokenSource();
        private readonly object _CancelLock = new object();
        private bool _Disposed;
        private RidbRefreshProgress? _Progress;
        private RidbRefreshResult? _Result;
        private long _EndedAt;
        private int _CancelRequested;
        private int _Pausing = 1;
        private int _ResumedMonitoring;

        internal WebRefreshOperation(long Id, long Started)
        {
            this.Id = Id;
            this.Started = Started;
        }

        internal long Id { get; }
        internal long Started { get; }
        internal long EndedAt => Volatile.Read(ref _EndedAt);
        internal Task? Work { get; set; }
        internal CancellationToken Token => _Cancel.Token;
        internal RidbRefreshProgress? Progress => Volatile.Read(ref _Progress);
        internal RidbRefreshResult? Result => Volatile.Read(ref _Result);
        internal bool CancelRequested => Volatile.Read(ref _CancelRequested) != 0;
        // waiting for the monitoring to pause, before the service starts
        internal bool Pausing => Volatile.Read(ref _Pausing) != 0;
        // the refresh paused a running monitoring, which starts again after it
        internal bool ResumedMonitoring => Volatile.Read(ref _ResumedMonitoring) != 0;
        // the refresh can still stop: until the service reports the cache step (RidbRefreshProgress.CanCancel)
        internal bool Cancellable => Progress?.CanCancel ?? true;

        // the service's progress, from its thread: the latest snapshot (by its sequence), never a list of them
        public void Report(RidbRefreshProgress Value)
        {
            RidbRefreshProgress? Seen = Volatile.Read(ref _Progress);
            while (Seen == null || Value.Sequence > Seen.Sequence)
            {
                RidbRefreshProgress? Was = Interlocked.CompareExchange(ref _Progress, Value, Seen);
                if (ReferenceEquals(Was, Seen))
                {
                    return;
                }
                Seen = Was;
            }
        }

        internal void Paused(bool Resume)
        {
            Volatile.Write(ref _ResumedMonitoring, Resume ? 1 : 0);
            Volatile.Write(ref _Pausing, 0);
        }

        // safe to repeat and from any thread; the service reads the token only until the cache step
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

        // the refresh has ended: its result, written last, so a request that sees it sees the end's time too
        internal void Ended(RidbRefreshResult Result, long At)
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

    // One moment of the refresh as a page shows it: nothing in it changes afterwards, and nothing in it is a lock
    internal sealed class WebRefreshView
    {
        // the running refresh, or null; else the last one that ended (since web mode started), or null
        internal WebRefreshOperation? Running { get; init; }
        internal WebRefreshOperation? Last { get; init; }
        internal bool PathsAvailable { get; init; }
        internal bool SearchRunning { get; init; }
        internal bool ShuttingDown { get; init; }
        internal string LastRefreshed { get; init; } = CurrentConfiguration.Unknown;
        internal TimeSpan Elapsed { get; init; }

        internal bool IsRunning => Running != null;
        // what a page sends back with an action: the refresh it showed and whether it ran. An action applies only to that
        internal string Token => ((Running ?? Last)?.Id ?? 0).ToString(CultureInfo.InvariantCulture) + "-" + (IsRunning ? "running" : "idle");
        internal bool Cancellable => Running != null && Running.Cancellable;
        // the shared rules (RefreshRules): a confirmation first, Start refresh from it only, Cancel until the cache step
        internal bool CanRequest => RefreshRules.CanRequest(ShuttingDown, IsRunning, false, false, PathsAvailable, !SearchRunning);
        internal bool CanBegin => RefreshRules.CanBegin(ShuttingDown, IsRunning, false, PathsAvailable, !SearchRunning);
        internal bool CanCancel => RefreshRules.CanCancel(IsRunning, Running?.CancelRequested == true, Cancellable);
        internal bool IsFinishing => RefreshRules.IsFinishing(IsRunning, Cancellable);
        internal string? UnavailableReason => ShuttingDown ? WebSystemWording.UnavailableShuttingDown : RefreshRules.UnavailableReason(IsRunning, false, PathsAvailable, SearchRunning);
    }

    // The web UI's refresh of the campground data (v0.3.0's PR 9, D99; business-rules-settings §36): the library's
    // RidbRefreshService (D61), the one the console's "Refresh Ridb Data" and the desktop GUI's Settings > System run, with
    // the shared rules and words (RefreshRules, RefreshWording). It belongs to this web UI, not to a browser: the campground
    // data is the web UI's (every session, the results page and the monitoring use it), so once started it goes on when its
    // session signs out or ends, every session sees it and may cancel it until the cache step, and only web mode's stop
    // cancels it by itself. One object per web UI; no static state; every member is safe from any thread.
    //  - It starts only from the confirmation's POST, for the moment the page showed (its Token), never by itself: no GET,
    //    no missing or old database, no start of web mode starts it.
    //  - One data operation at a time in the web UI (WebResultsRuns' gate, D61): no refresh while a search of the results
    //    page runs, and no search while the refresh runs. Refreshes of other processes are kept apart by the refresh lock.
    //  - It pauses the web UI's monitoring for its time (D83 point 6): WebMonitoring.PauseForRefresh before the service
    //    starts, ResumeAfterRefresh after it has ended, whatever its end.
    //  - It runs on a thread of its own; a page reads one immutable view and writes it without a lock the refresh needs.
    //  - The installed database, the cache and General.yaml are the service's: a failure before the installation changes
    //    nothing; the installation is SQLite's online backup in one transaction (D26). After an installation nothing in this
    //    process is reloaded (no live reload, D61): the web UI can't restart itself (the automatic restart is desktop-only,
    //    D84), so the page says to restart web mode.
    //  - When web mode stops, a refresh that can still stop is canceled, and web mode waits for it to stop at its next
    //    checkpoint or, past the cache step, to finish its installation, check and record, as the desktop GUI waits.
    internal sealed class WebRefresh
    {
        internal const string LinePrefix = "Web UI: campground data: ";

        private readonly WebHostSettings _Settings;
        private readonly WebResultsRuns _Runs;
        private readonly WebMonitoring _Monitoring;
        private readonly WebRefreshSeams _Seams;
        private readonly object _Lock = new object();
        private WebRefreshOperation? _Current;
        private WebRefreshOperation? _Last;
        private long _NextId;
        private bool _ShuttingDown;

        internal WebRefresh(WebHostSettings Settings, WebResultsRuns Runs, WebMonitoring Monitoring)
        {
            _Settings = Settings;
            _Runs = Runs;
            _Monitoring = Monitoring;
            _Seams = Settings.Refresh;
        }

        private bool PathsAvailable => Path.IsPathFullyQualified(_Settings.SettingsFolder) && _Settings.CachePath != null && Path.IsPathFullyQualified(_Settings.CachePath);

        // ---- what a page shows ----

        internal WebRefreshView View()
        {
            WebRefreshOperation? Running;
            WebRefreshOperation? Last;
            bool ShuttingDown;
            lock (_Lock)
            {
                Running = _Current;
                Last = _Last;
                ShuttingDown = _ShuttingDown;
            }
            WebRefreshOperation? Shown = Running ?? Last;
            TimeSpan Elapsed = Shown == null ? TimeSpan.Zero
                : Running != null ? _Seams.Time.GetElapsedTime(Shown.Started)
                : _Seams.Time.GetElapsedTime(Shown.Started, Shown.EndedAt);
            return new WebRefreshView
            {
                Running = Running,
                Last = Last,
                PathsAvailable = PathsAvailable,
                SearchRunning = Running == null && _Runs.IsSearching,
                ShuttingDown = ShuttingDown,
                LastRefreshed = _Seams.LastRefreshed(_Settings.SettingsFolder),
                Elapsed = Elapsed
            };
        }

        // ---- a page's actions ----

        // Start refresh, in the confirmation, for the moment of the refresh the page showed (Token). Never waits for the
        // refresh: the page that follows shows how it goes
        internal WebRefreshOutcome Begin(string? Token)
        {
            lock (_Lock)
            {
                if (_ShuttingDown)
                {
                    return WebRefreshOutcome.ShuttingDown;
                }
                if (!string.Equals(Token, TokenLocked(), StringComparison.Ordinal))
                {
                    return WebRefreshOutcome.Stale;
                }
                if (_Current != null)
                {
                    return WebRefreshOutcome.AlreadyRunning;
                }
                if (!PathsAvailable)
                {
                    return WebRefreshOutcome.NoFolders;
                }
                if (!_Runs.TryBeginRefresh())
                {
                    return WebRefreshOutcome.SearchRunning;
                }
                var Op = new WebRefreshOperation(++_NextId, _Seams.Time.GetTimestamp());
                _Current = Op;
                var Request = new RidbRefreshRequest(_Settings.SettingsFolder, _Settings.CachePath!, _Settings.ContainerMode);
                //a background thread of its own (never a foreground thread: it can't keep the process alive by itself)
                Op.Work = Task.Factory.StartNew(() => Execute(Op, Request), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            _Settings.Line(LinePrefix + "refresh started.");
            return WebRefreshOutcome.Accepted;
        }

        // Cancel, for the refresh the page showed: it stops at its next checkpoint and removes what it downloaded; safe to
        // repeat, never starts anything. From the cache step on it finishes (TooLate)
        internal WebRefreshOutcome Cancel(string? Token)
        {
            lock (_Lock)
            {
                if (_ShuttingDown)
                {
                    return WebRefreshOutcome.ShuttingDown;
                }
                if (!string.Equals(Token, TokenLocked(), StringComparison.Ordinal))
                {
                    return WebRefreshOutcome.Stale;
                }
                if (_Current == null)
                {
                    return WebRefreshOutcome.NotRunning;
                }
                if (!_Current.Cancellable)
                {
                    return WebRefreshOutcome.TooLate;
                }
                _Current.Cancel();
                return WebRefreshOutcome.Accepted;
            }
        }

        private string TokenLocked() => ((_Current ?? _Last)?.Id ?? 0).ToString(CultureInfo.InvariantCulture) + "-" + (_Current != null ? "running" : "idle");

        // Web mode stops: no refresh starts any more; a refresh that can still stop is canceled; then web mode waits for it
        // to stop at its checkpoint or, past the cache step, to finish (bounded by the step under way: the installation's
        // busy timeout is 30 seconds), so the process never ends in the middle of an installation it could finish
        internal void BeginShutdown()
        {
            WebRefreshOperation? Op;
            lock (_Lock)
            {
                _ShuttingDown = true;
                Op = _Current;
            }
            Op?.Cancel();
        }

        internal void Shutdown()
        {
            BeginShutdown();
            WebRefreshOperation? Op;
            lock (_Lock)
            {
                Op = _Current;
            }
            try
            {
                Op?.Work?.Wait();
            }
            catch (AggregateException)
            {
                //the refresh's end is its own; nothing more to do while web mode stops
            }
        }

        // ---- the refresh, on its own thread ----

        private void Execute(WebRefreshOperation Op, RidbRefreshRequest Request)
        {
            bool Resume = false;
            RidbRefreshResult Result;
            try
            {
                //the monitoring reads the cache folder the refresh clears: it pauses first (D83 point 6)
                Resume = _Monitoring.PauseForRefresh();
                Op.Paused(Resume);
                Result = Op.Token.IsCancellationRequested
                    ? new RidbRefreshResult(RidbRefreshOutcome.Canceled, RidbRefreshStage.AcquiringLock, false, null, _Seams.Time.GetElapsedTime(Op.Started),
                        RidbRefreshResult.NoProblems, null, null, true)
                    : _Seams.Run(Request, Op, Op.Token);
            }
            catch (Exception ex)
            {
                //the service throws only for a request it can't start (an unusable cache folder); anything else is unexpected,
                //named by its type only
                Result = new RidbRefreshResult(ex is InvalidOperationException ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected,
                    Op.Progress?.Stage ?? RidbRefreshStage.AcquiringLock, false, null, _Seams.Time.GetElapsedTime(Op.Started), RidbRefreshResult.NoProblems, null, ex,
                    Op.CancelRequested);
            }
            //the end is recorded before the gate is free and the monitoring starts again, so a page that sees either sees it.
            //The gate is freed and the monitoring started again under the refresh's lock, before the refresh is no longer
            //the current one: a Start that comes meanwhile finds this refresh still running, never a gate held by nobody,
            //and a next refresh pauses the monitoring only after this one has asked it to start again (lock order: the
            //refresh's, then the results runs' or the monitoring's; neither takes the refresh's). Never once web mode stops
            Op.Ended(Result, _Seams.Time.GetTimestamp());
            lock (_Lock)
            {
                _Runs.EndRefresh();
                _Monitoring.ResumeAfterRefresh(Resume && !_ShuttingDown);
                if (ReferenceEquals(_Current, Op))
                {
                    _Current = null;
                }
                _Last = Op;
            }
            _Settings.Line(LinePrefix + WebSystemWording.ConsoleResult(Result));
        }
    }
}
