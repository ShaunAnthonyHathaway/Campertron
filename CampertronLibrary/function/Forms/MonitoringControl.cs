using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using static ConsoleConfig;

namespace CampertronLibrary.function.Forms
{
    internal enum MonitoringControlAction
    {
        Start,
        Stop,
        Restart
    }

    // what the host's controller is doing with its monitoring, besides the service's own state
    internal enum MonitoringControlBusy
    {
        None,
        Starting,
        Stopping,
        Restarting,
        // the run has ended and its end is being recorded (a moment)
        Ending,
        // the campground data's refresh stops the monitoring for its time (v0.3.0's PR 9, D83 point 6)
        Pausing
    }

    internal enum MonitoringControlPhase
    {
        Idle,
        Busy,
        Running
    }

    // what an action did
    internal enum MonitoringControlOutcome
    {
        Accepted,
        // the action was for another moment of the monitoring (another start, a stop, an end): nothing was done
        Stale,
        Busy,
        AlreadyRunning,
        NotRunning,
        CoolingDown,
        ShuttingDown,
        // the campground data is being refreshed: the monitoring is paused, and starts again by itself afterwards when it ran
        PausedForRefresh
    }

    // A start the host asked for: started, or why not in the start's words (Lines; the problem first). Output: the
    // notifications ("Email", "Email and ntfy", "Ntfy") of a start that started. WithHost: the host's own start of the
    // monitoring (MonitoringController.StartWithHost; the web UI's when web mode starts)
    internal sealed record MonitoringControlStart(long Generation, bool Started, MonitoringStartProblem Problem, IReadOnlyList<string> Lines, DateTime At,
        bool WithHost, int Searches, string? Output, bool AfterRefresh = false);

    // How a run ended: stopped, or failed with the fixed words of MonitoringWording.Failure (never a server's text, an
    // exception's message, a secret, an address or a path)
    internal sealed record MonitoringControlEnd(long Generation, bool Failed, bool Blocked, IReadOnlyList<string> Lines, DateTime At);

    // The last check of the running or last monitoring, as a host shows it: each search's new openings (the monitoring's
    // output stream leaves out what Camp.History holds, but a stay of several nights whole when any night of it is new, D104
    // and TD-61, so these are what the check notifies; the shared result model, its
    // rows built once here and only read afterwards) and what it sent. Immutable: a new check, or a notification of this one
    // sent, makes a new one.
    internal sealed record MonitoringControlCheck(long Generation, int Number, DateTime At, int SearchCount, int NewOpenings, int Emails, int NtfyMessages,
        int EmailsSent, int NtfySent, bool Delivered, IReadOnlyList<ResultSearchViewModel> Searches);

    // One moment of the monitoring as a host shows it: nothing in it changes afterwards, and nothing in it is a lock
    internal sealed class MonitoringControlView
    {
        internal long Generation { get; init; }
        internal MonitoringControlBusy Busy { get; init; }
        internal MonitoringStatus Status { get; init; } = null!;
        internal MonitoringControlStart? LastStart { get; init; }
        internal MonitoringControlEnd? LastEnd { get; init; }
        internal MonitoringControlCheck? LastCheck { get; init; }
        internal string? Cooldown { get; init; }
        internal bool ShuttingDown { get; init; }
        // the campground data is being refreshed (v0.3.0's PR 9, D83 point 6): no action until it has ended, and Resumes
        // says whether the monitoring then starts again by itself (it ran when the refresh began)
        internal bool Paused { get; init; }
        internal bool Resumes { get; init; }

        internal MonitoringControlPhase Phase => Busy != MonitoringControlBusy.None ? MonitoringControlPhase.Busy
            : Status.IsRunning ? MonitoringControlPhase.Running : MonitoringControlPhase.Idle;
        // what a host sends back with an action: the start it showed and its phase. An action applies only to that
        internal string Token => Generation.ToString(CultureInfo.InvariantCulture) + "-" + Phase.ToString().ToLowerInvariant();
        internal bool StopAsked => Status.StopRequested || Busy == MonitoringControlBusy.Stopping || Busy == MonitoringControlBusy.Restarting;
        internal bool CanStart => !ShuttingDown && !Paused && Phase == MonitoringControlPhase.Idle && Cooldown == null;
        internal bool CanStop => !ShuttingDown && !Paused && Phase == MonitoringControlPhase.Running && !Status.StopRequested;
        internal bool CanRestart => CanStop && Cooldown == null;
        // the start and the end of this generation (an older one's are not this monitoring's)
        internal MonitoringControlStart? ThisStart => LastStart != null && LastStart.Generation == Generation ? LastStart : null;
        internal MonitoringControlEnd? ThisEnd => LastEnd != null && LastEnd.Generation == Generation ? LastEnd : null;
    }

    // What a host gives its monitoring controller: where the settings and the cache are, how the campground data is
    // prepared, the five minutes after a block it shares with its searches, its lines and its own words; the clock and the
    // wait between two checks only tests change
    internal sealed class MonitoringControllerOptions
    {
        // the settings folder; the cache folder (null when not known: a start then says so)
        internal required string SettingsFolder { get; init; }
        internal string? CachePath { get; init; }
        // prepares the campground data as every start of Campertron does, never with the automatic refresh; an
        // IOException, UnauthorizedAccessException or InvalidDataException refuses the start in fixed words
        internal required Action PrepareCampgroundData { get; init; }
        // the hold after a block the host shares with its searches (BlockHold): its line with the time left, or null; and
        // its start after a check that recreation.gov blocked
        internal required Func<string?> CooldownText { get; init; }
        internal required Action HoldAfterBlock { get; init; }
        // the host's line of a start and an end (MonitoringWording's StartedLine, NotStartedLine, FailedLine, PausedLine and
        // StoppedLine), on the controller's threads; null: none
        internal Action<string>? Line { get; init; }
        // what only this host says (where its user fixes a file, what restarts it, its own start and stop)
        internal required MonitoringHostWords Words { get; init; }
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        // null: the service's own one-minute wait
        internal Action<TimeSpan, CancellationToken>? Wait { get; init; }
    }

    // A host's control of its unattended monitoring (v0.3.0's PR 8, D83 and D98; business-rules-settings §35; v0.4.0's
    // phase 2 moved it here from the web UI's WebMonitoring, unchanged): the library's MonitoringService (D90), the one the
    // console's email and ntfy mode run, owned by the host (the web UI: one per web UI, never a browser session's). One
    // object per host; no static state.
    //  - It starts with the host when the host asks (StartWithHost; the web UI when web mode starts, D83 point 2) when
    //    General.yaml's outputTo is Email or Ntfy and the start's checks pass, and on the host's Start; Stop and Restart from
    //    the host. Each start reads the settings as the console's start does, without creating a file (General.yaml,
    //    Email.yaml and Ntfy.yaml by the bounded loader, the campground data prepared as every start does, never
    //    refreshed), then the service's start runs its own checks in its order (the secrets from their sources, the search
    //    files with D64's gate, the monitoring lock, the campsite details).
    //  - An action applies only to the moment of the monitoring the host showed (its Token: the start it showed and whether
    //    that was idle, busy or running), so a view that is out of date starts or stops nothing. One action at a time;
    //    each runs on a thread of its own, so the host never waits for a start or a stop, and a slow view never holds
    //    anything the monitoring needs: a view is immutable (View) and is shown without a lock.
    //  - The check's results a host shows are one immutable snapshot of the last check (MonitoringControlCheck), replaced by
    //    the next: no history, nothing on disk. Camp.History, the notifications and the send-failure rule are the
    //    service's, unchanged (D76: a failed send ends the monitoring as failed, what was not delivered is not recorded,
    //    nothing starts it again by itself; the host goes on).
    //  - A check that recreation.gov blocked ends the monitoring as failed (the service's default for a host without its own
    //    handling) and holds Start for five minutes here and in the host's searches (HoldAfterBlock).
    //  - When the host stops (Shutdown), the monitoring is stopped (the host's token) and waited for at most StopWait.
    //  - A refresh of the campground data pauses it (v0.3.0's PR 9, D83 point 6, D99; PauseForRefresh): an action under way
    //    ends first, a running monitoring is stopped as Stop stops it (a check that searches sends nothing, one that sends
    //    finishes and records), no action is taken while the refresh runs, and when the refresh has ended, whatever its end,
    //    the monitoring starts again by itself if the pause stopped it (ResumeAfterRefresh): a new start, which reads the
    //    settings and the search files and prepares the campsite details from the campground data as it is then. The
    //    refresh clears the cache folder whose campsite details each check reads, so no check runs meanwhile.
    internal sealed class MonitoringController
    {
        internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);

        private readonly MonitoringControllerOptions _Options;
        private readonly MonitoringService _Service;
        private readonly Func<DateTime> _Now;
        private readonly object _Lock = new object();
        private readonly CancellationTokenSource _Shutdown = new CancellationTokenSource();
        private long _Generation;
        private MonitoringControlBusy _Busy;
        private bool _ShuttingDown;
        private Task? _Work;
        private Task? _Watcher;
        private MonitoringControlStart? _LastStart;
        private MonitoringControlEnd? _LastEnd;
        // written by the monitoring's own thread only (its events), read by any caller
        private MonitoringControlCheck? _LastCheck;
        private long _RunGeneration;
        // the campground data's refresh holds the monitoring (PauseForRefresh until ResumeAfterRefresh), and whether the pause
        // stopped a running monitoring, which then starts again
        private bool _Paused;
        private bool _Resumes;

        internal MonitoringController(MonitoringControllerOptions Options)
        {
            _Options = Options;
            _Now = Options.Now;
            _Service = new MonitoringService(new MonitoringOptions
            {
                Report = OnEvent,
                Now = _Now,
                Wait = Options.Wait
            });
        }

        // ---- what a host shows ----

        internal MonitoringControlView View()
        {
            lock (_Lock)
            {
                return ViewLocked();
            }
        }

        private MonitoringControlView ViewLocked()
        {
            MonitoringStatus Status = _Service.GetStatus();
            MonitoringControlBusy Busy = _Busy != MonitoringControlBusy.None ? _Busy
                : _Watcher != null && !_Watcher.IsCompleted && !Status.IsRunning ? MonitoringControlBusy.Ending
                : MonitoringControlBusy.None;
            MonitoringControlCheck? Check = Volatile.Read(ref _LastCheck);
            return new MonitoringControlView
            {
                Generation = _Generation,
                Busy = Busy,
                Status = Status,
                LastStart = _LastStart,
                LastEnd = _LastEnd,
                LastCheck = Check != null && Check.Generation == _Generation ? Check : null,
                Cooldown = _Options.CooldownText(),
                ShuttingDown = _ShuttingDown,
                Paused = _Paused,
                Resumes = _Resumes
            };
        }

        // ---- the start with the host, the host's actions, the stop with the host ----

        // D83 point 2: when the host starts (the web UI: web mode). A start whose outputTo is no monitoring changes nothing
        // and writes no line (the host's view says why it is not running)
        internal void StartWithHost()
        {
            lock (_Lock)
            {
                if (_ShuttingDown || _Paused || _Busy != MonitoringControlBusy.None)
                {
                    return;
                }
                Launch(MonitoringControlBusy.Starting, Generation => DoStart(Generation, true), true);
            }
        }

        // The host's Start, Stop or Restart, for the moment of the monitoring it showed (Token). Never waits for the action:
        // the view that follows shows how it goes
        internal MonitoringControlOutcome Act(MonitoringControlAction Action, string? Token)
        {
            lock (_Lock)
            {
                if (_ShuttingDown)
                {
                    return MonitoringControlOutcome.ShuttingDown;
                }
                MonitoringControlView Now = ViewLocked();
                if (!string.Equals(Token, Now.Token, StringComparison.Ordinal))
                {
                    return MonitoringControlOutcome.Stale;
                }
                if (Now.Phase == MonitoringControlPhase.Busy)
                {
                    return MonitoringControlOutcome.Busy;
                }
                if (Now.Paused)
                {
                    return MonitoringControlOutcome.PausedForRefresh;
                }
                switch (Action)
                {
                    case MonitoringControlAction.Start:
                        if (Now.Phase == MonitoringControlPhase.Running)
                        {
                            return MonitoringControlOutcome.AlreadyRunning;
                        }
                        if (Now.Cooldown != null)
                        {
                            return MonitoringControlOutcome.CoolingDown;
                        }
                        Launch(MonitoringControlBusy.Starting, Generation => DoStart(Generation, false), true);
                        return MonitoringControlOutcome.Accepted;
                    case MonitoringControlAction.Stop:
                        if (Now.Phase != MonitoringControlPhase.Running || Now.Status.StopRequested)
                        {
                            return MonitoringControlOutcome.NotRunning;
                        }
                        Launch(MonitoringControlBusy.Stopping, Generation => DoStop(), false);
                        return MonitoringControlOutcome.Accepted;
                    default:
                        if (Now.Phase != MonitoringControlPhase.Running || Now.Status.StopRequested)
                        {
                            return MonitoringControlOutcome.NotRunning;
                        }
                        if (Now.Cooldown != null)
                        {
                            return MonitoringControlOutcome.CoolingDown;
                        }
                        Launch(MonitoringControlBusy.Restarting, Generation =>
                        {
                            DoStop();
                            DoStart(Generation, false);
                        }, true);
                        return MonitoringControlOutcome.Accepted;
                }
            }
        }

        // The campground data's refresh begins (v0.3.0's PR 9, D83 point 6), on the refresh's own thread: from now on no
        // action is taken; an action under way ends first (a start that prepares the campsite details, a stop that waits for
        // a check that sends); then a running monitoring is stopped as Stop stops it and its end recorded. True when the
        // pause stopped a running monitoring, which ResumeAfterRefresh then starts again. Waits as long as a stop waits (a
        // check that sends finishes its notifications first); never while the host stops
        internal bool PauseForRefresh()
        {
            bool Resume = false;
            while (true)
            {
                Task? Pending;
                lock (_Lock)
                {
                    _Paused = true;
                    _Resumes = Resume;
                    if (_ShuttingDown)
                    {
                        return false;
                    }
                    if (_Busy != MonitoringControlBusy.None)
                    {
                        //an action under way ends first; then the monitoring is looked at again
                        Pending = _Work;
                    }
                    else if (_Service.GetStatus().IsRunning)
                    {
                        Resume = true;
                        _Resumes = true;
                        Launch(MonitoringControlBusy.Pausing, Generation => DoStop(), false);
                        Pending = _Work;
                    }
                    else if (_Watcher != null && !_Watcher.IsCompleted)
                    {
                        //a run that ended records its end first
                        Pending = _Watcher;
                    }
                    else
                    {
                        return Resume;
                    }
                }
                try
                {
                    Pending?.Wait();
                }
                catch (AggregateException)
                {
                    //an action's end is its own; the monitoring is looked at again
                }
            }
        }

        // The campground data's refresh has ended, whatever its end: actions are taken again, and a monitoring the pause
        // stopped starts again by itself (a new start: the settings, the search files and the campsite details read as they
        // are now), unless the host stops
        internal void ResumeAfterRefresh(bool Resume)
        {
            lock (_Lock)
            {
                _Paused = false;
                _Resumes = false;
                if (Resume && !_ShuttingDown && _Busy == MonitoringControlBusy.None && !_Service.GetStatus().IsRunning)
                {
                    Launch(MonitoringControlBusy.Starting, Generation => DoStart(Generation, false, AfterRefresh: true), true);
                }
            }
        }

        // The host stops: no action is taken any more, the monitoring is stopped as the host's token stops it (a check that
        // is searching ends at its next checkpoint; one that is sending finishes first) and waited for at most StopWait
        internal void Shutdown()
        {
            Task? Work;
            lock (_Lock)
            {
                _ShuttingDown = true;
                Work = _Work;
            }
            _Shutdown.Cancel();
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Work?.Wait(StopWait);
            }
            catch (AggregateException)
            {
                //an action's end is its own; nothing more to do while the host stops
            }
            TimeSpan Left = StopWait - Clock.Elapsed;
            _Service.WaitForEnd(Left > TimeSpan.Zero ? Left : TimeSpan.Zero);
            //the end's line before the host's own last line
            Task? Watcher;
            lock (_Lock)
            {
                Watcher = _Watcher;
            }
            Left = StopWait - Clock.Elapsed;
            Watcher?.Wait(Left > TimeSpan.Zero ? Left : TimeSpan.Zero);
        }

        // an action on a thread of its own (a long-running one: a start prepares the campsite details, a stop waits for a
        // check that sends), one at a time. Under the lock
        private void Launch(MonitoringControlBusy Kind, Action<long> Work, bool NewStart)
        {
            long Generation = NewStart ? ++_Generation : _Generation;
            _Busy = Kind;
            _Work = Task.Factory.StartNew(() =>
            {
                try
                {
                    Work(Generation);
                }
                finally
                {
                    lock (_Lock)
                    {
                        _Busy = MonitoringControlBusy.None;
                    }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void DoStop()
        {
            _Service.Stop();
            //the end is recorded (and its line written) before a restart starts again
            Task? Watcher;
            lock (_Lock)
            {
                Watcher = _Watcher;
            }
            Watcher?.Wait();
        }

        private void DoStart(long Generation, bool WithHost, bool AfterRefresh = false)
        {
            Volatile.Write(ref _RunGeneration, Generation);
            Volatile.Write(ref _LastCheck, null);
            CampertronInternalConfig? Config = new CampertronInternalConfig { ConfigPath = _Options.SettingsFolder, CachePath = _Options.CachePath };
            MonitoringControlStart Result;
            try
            {
                LoadSettings(Config, out IReadOnlyList<string>? Refused);
                if (Refused != null)
                {
                    Result = new MonitoringControlStart(Generation, false, MonitoringStartProblem.StartFailed, Refused, _Now(), WithHost, 0, null, AfterRefresh);
                }
                else
                {
                    MonitoringStartResult Started = _Service.Start(Config, _Shutdown.Token);
                    if (Started.Started)
                    {
                        Result = new MonitoringControlStart(Generation, true, MonitoringStartProblem.None, Array.Empty<string>(), _Now(), WithHost,
                            _Service.GetStatus().Searches, Notifications.OutputText(Config), AfterRefresh);
                        Watch(Generation, Config);
                        //the run's settings are the watcher's now: it drops their secrets when the run has ended
                        Config = null;
                    }
                    else
                    {
                        IReadOnlyList<string> Lines = Started.Error != null || Started.Problem == MonitoringStartProblem.StartFailed
                            ? MonitoringWording.StartFailed(Started.Error, MonitoringWording.Mode(Config), _Options.Words)
                            : Started.Lines;
                        Result = new MonitoringControlStart(Generation, false, Started.Problem, Lines, _Now(), WithHost, 0, null, AfterRefresh);
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Result = new MonitoringControlStart(Generation, false, MonitoringStartProblem.StartFailed,
                    MonitoringWording.StartFailed(ex, MonitoringWording.Mode(Config), _Options.Words), _Now(), WithHost, 0, null, AfterRefresh);
            }
            finally
            {
                if (Config != null)
                {
                    DropSecrets(Config);
                }
            }
            lock (_Lock)
            {
                _LastStart = Result;
            }
            if (Result.Started)
            {
                _Options.Line?.Invoke(MonitoringWording.StartedLine(Result.Output, Result.Searches));
            }
            else if (Result.Problem != MonitoringStartProblem.NotMonitoring)
            {
                _Options.Line?.Invoke(MonitoringWording.NotStartedLine(Result.Lines[0]));
            }
        }

        // The settings as the console's start reads them (Load.GetConfig), without creating a file: General.yaml (none: no
        // monitoring is turned on, which the service's start says), then for a monitoring Email.yaml (required for outputTo:
        // Email) and Ntfy.yaml when they exist, by the bounded loader; then the campground data as every start prepares it,
        // without the automatic refresh. Refused: the start's words when it can't go on; a file that can't be read throws
        // (StartFailed's words)
        private void LoadSettings(CampertronInternalConfig Config, out IReadOnlyList<string>? Refused)
        {
            Refused = null;
            string Folder = _Options.SettingsFolder;
            Config.GeneralConfig = Yaml.GeneralConfigGetExisting(Folder);
            if (!Notifications.IsMonitoring(Config.GeneralConfig?.OutputTo))
            {
                return;
            }
            string Mode = Notifications.ModeName(Config);
            Config.EmailConfig = Yaml.EmailConfigGetExisting(Folder);
            if (Config.EmailConfig == null && Notifications.EmailEnabled(Config))
            {
                Refused = MonitoringWording.EmailMissing(Mode, _Options.Words);
                return;
            }
            Config.NtfyConfig = Yaml.NtfyConfigGetConfig(Folder);
            if (_Options.CachePath == null || !Path.IsPathFullyQualified(_Options.CachePath))
            {
                Refused = MonitoringWording.NoCacheFolder(Mode, _Options.Words);
                return;
            }
            try
            {
                _Options.PrepareCampgroundData();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                Refused = MonitoringWording.NoCampgroundData(Mode, _Options.Words);
            }
        }

        // the run's end, recorded once: its words, the hold after a block, the host's line; then the run's secrets are
        // dropped (nothing uses its settings any more)
        private void Watch(long Generation, CampertronInternalConfig Config)
        {
            lock (_Lock)
            {
                _Watcher = Task.Factory.StartNew(() =>
                {
                    MonitoringStatus Ended = _Service.WaitForEnd();
                    MonitoringControlEnd End;
                    if (Ended.LastError is MonitoringFailure Failure)
                    {
                        if (Failure.Blocked)
                        {
                            _Options.HoldAfterBlock();
                        }
                        End = new MonitoringControlEnd(Generation, true, Failure.Blocked, MonitoringWording.Failure(Failure.Error, Failure.Blocked, Config.EmailConfig, _Options.Words), Failure.At);
                    }
                    else
                    {
                        End = new MonitoringControlEnd(Generation, false, false, Array.Empty<string>(), _Now());
                    }
                    DropSecrets(Config);
                    bool Paused;
                    lock (_Lock)
                    {
                        _LastEnd = End;
                        Paused = _Paused;
                    }
                    _Options.Line?.Invoke(End.Failed ? MonitoringWording.FailedLine(End.Lines[0])
                        : Paused ? MonitoringWording.PausedLine : MonitoringWording.StoppedLine);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        // the SMTP password and ntfy's token a start resolved, and the plain-text password, once nothing sends with them
        private static void DropSecrets(CampertronInternalConfig Config)
        {
            if (Config.EmailConfig != null)
            {
                Config.EmailConfig.ResolvedPassword = null;
                Config.EmailConfig.SmtpPassword = null;
            }
            if (Config.NtfyConfig != null)
            {
                Config.NtfyConfig.ResolvedToken = null;
            }
        }

        // ---- the service's events (on its threads) ----

        // The last check as a host shows it. Never fails the monitoring: a check goes on whatever this does
        private void OnEvent(MonitoringEvent Event)
        {
            try
            {
                switch (Event.Kind)
                {
                    case MonitoringEventKind.CheckReported:
                        Volatile.Write(ref _LastCheck, Snapshot(Event));
                        break;
                    case MonitoringEventKind.EmailSent:
                        Update(Check => Check with { EmailsSent = Check.EmailsSent + 1 });
                        break;
                    case MonitoringEventKind.NtfySent:
                        Update(Check => Check with { NtfySent = Check.NtfySent + 1 });
                        break;
                    case MonitoringEventKind.DeliveriesFinished:
                        Update(Check => Check with { Delivered = true });
                        break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                //the host has no view of this check; the monitoring goes on
                Volatile.Write(ref _LastCheck, null);
            }
        }

        private void Update(Func<MonitoringControlCheck, MonitoringControlCheck> Change)
        {
            if (Volatile.Read(ref _LastCheck) is MonitoringControlCheck Check)
            {
                Volatile.Write(ref _LastCheck, Change(Check));
            }
        }

        // a copy of what the check found, made during its event (the run lets go of its lists after the check): each
        // search's new openings from its output stream's typed metadata (GS1's OpeningsOf and SitesOf, never its text), as
        // the shared result model with its rows built now, so a view only reads them
        internal MonitoringControlCheck Snapshot(MonitoringEvent Event)
        {
            var Searches = new List<ResultSearchViewModel>();
            //by name, the order the check's notifications go out in (MonitoringService.ReportCheck)
            foreach (MonitoringCheckedSearch Checked in (Event.Checked ?? Array.Empty<MonitoringCheckedSearch>()).OrderBy(C => C.Search.DisplayName))
            {
                IReadOnlyList<SearchOpening> Openings = CampsiteSearch.OpeningsOf(Checked.Values, CancellationToken.None, out OutputMetadata? Header);
                CampertronConfig Search = Checked.Search;
                string Name = string.IsNullOrWhiteSpace(Search.DisplayName) ? MonitoringWording.UnnamedSearch(Searches.Count + 1) : Search.DisplayName.Trim();
                var Result = new SearchResult(Name, Search.DisplayName, Search.AutoRun, Search.CampgroundID, Header?.CampgroundName, Header?.ParkName,
                    Search.ConsecutiveDays, SearchState.Complete, 0, 0, Array.Empty<SearchMonthResult>(), Openings, CampsiteSearch.SitesOf(Openings),
                    Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>());
                var View = new ResultSearchViewModel(Result, false);
                //built once, here, on the monitoring's thread: a view only reads them afterwards
                _ = View.Rows;
                Searches.Add(View);
            }
            return new MonitoringControlCheck(Volatile.Read(ref _RunGeneration), Event.CheckNumber, _Now(), Event.SearchCount, Event.NewOpenings, Event.Emails,
                Event.NtfyMessages, 0, 0, Event.NewOpenings <= 0, Searches.AsReadOnly());
        }
    }
}
