using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using static ConsoleConfig;

namespace CampertronLibrary.function.RecDotOrg.monitoring
{
    // What a check's searches use of the monitoring that runs them (RunningData.Monitoring; null for the console's Search
    // and for an interactive search, which has its own SearchHost): where a warning and a status line go, what a blocked
    // request does, and the token that ends the searching. A check's waits (a retry, heavy traffic, another search's
    // month) end as soon as the token is canceled; nothing here is read once a check sends its notifications.
    internal sealed class MonitoringRun
    {
        internal MonitoringRun(CancellationToken Token, Action<string> Status, Action<string> Warning, Action<string> Blocked)
        {
            this.Token = Token;
            this.Status = Status;
            this.Warning = Warning;
            this.Blocked = Blocked;
        }
        internal CancellationToken Token { get; }
        internal Action<string> Status { get; }
        internal Action<string> Warning { get; }
        // given the request's description (AvailabilityApi.DescribeRequest)
        internal Action<string> Blocked { get; }
        internal void ThrowIfStopped() => Token.ThrowIfCancellationRequested();
        // waits as long as the console's search sleeps, or throws as soon as the monitoring is stopped
        internal void Pause(int Milliseconds)
        {
            if (Token.WaitHandle.WaitOne(Milliseconds))
            {
                throw new OperationCanceledException(Token);
            }
        }
        // true for what ends a check instead of being tried again: the stop, and a blocked request without a host's own
        // handling
        internal bool EndsTheCheck(Exception Error)
        {
            return Error is MonitoringBlockedException || (Error is OperationCanceledException && Token.IsCancellationRequested);
        }
    }

    // The unattended monitoring as a service (v0.3.0, decisions D83 and D90; business-rules.md §29), in the pattern of the
    // search service (D45) and the refresh service (D61): the loop that Load.RunConsoleSearch ran for outputTo: Email and
    // Ntfy, with an explicit start, stop, restart and status, and without static state.
    //  - Start checks what the console's start checked, in its order: Email.yaml and Ntfy.yaml (they throw; the result
    //    carries the exception), then the SMTP password and ntfy's token from their configured sources, once (D66, D67),
    //    then the search files, read once and never created (D64, D65), then the monitoring lock (MonitoringLock), then
    //    each campground's cache. A start that is refused has searched, sent, written and created nothing: the lock file
    //    is created only by a start that passed every check before it. Then the checks run on a thread of the service's own.
    //  - A check: the searches (in parallel, as before), then its report: nothing new, or its notifications through
    //    every enabled provider and Camp.History's markers (Notifications.Deliver, ER-03), then the one-minute wait.
    //  - Stop asks the run to end and waits until it has: the wait between two checks ends at once; a check that is
    //    searching ends at its next checkpoint and nothing of it is sent or recorded (its openings come again); a check
    //    that is sending finishes every notification and its records first. Stopping saves nothing.
    //  - A check that fails (a send that failed, D76; a blocked request; anything unexpected) ends the run as Failed:
    //    what was not delivered is not recorded, the lock is released, and nothing starts the monitoring again by itself.
    //  - Restart is Stop and Start: the search files are read again, and the check number starts from 1.
    // The service never writes to a console, never waits for a key and never ends the process (a host's own Blocked may,
    // as the console's does). Its events go to Options.Report. One service runs one monitoring at a time; the monitoring
    // lock keeps another service or process from monitoring the same settings folder.
    // A host calls Start and Restart from its own threads, never from one of the service's events (an event of a check
    // comes on the run's thread, which a Stop on another thread may be waiting for); Stop may be called from an event.
    internal sealed class MonitoringService
    {
        // checks are one minute apart, as they always were (ER-02)
        internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

        private readonly MonitoringOptions _Options;
        // Start, Stop and Restart, one at a time
        private readonly object _Control = new object();
        // the status's fields and the current run
        private readonly object _Sync = new object();
        private Run? _Run;
        private MonitoringState _State = MonitoringState.NotStarted;
        private MonitoringStartResult? _LastStart;
        private MonitoringFailure? _LastError;
        private DateTime? _StartedAt;
        private DateTime? _LastCheckAt;
        private DateTime? _NextCheckAt;
        private int _CheckNumber;
        private int _Searches;

        internal MonitoringService(MonitoringOptions? Options = null)
        {
            _Options = Options ?? new MonitoringOptions();
        }

        // one start's own state
        private sealed class Run
        {
            internal CampertronInternalConfig Config = null!;
            internal RunningData Data = null!;
            internal CancellationTokenSource Cancel = null!;
            internal FileStream? Lock;
            internal Thread? Thread;
            internal readonly ManualResetEventSlim Ended = new ManualResetEventSlim(false);
            internal List<string> SearchFiles = new List<string>();
            // a blocked request without a host's own handling: what the run failed with
            internal Exception? Blocked;
            internal bool StopRequested;
        }

        // ---- control ----

        // Starts the monitoring with the settings the host loaded, or says why not. Returns when the first check is about
        // to run (or the start was refused); HostToken, when the host has one, stops the run as Stop does, without waiting
        internal MonitoringStartResult Start(CampertronInternalConfig Config, CancellationToken HostToken = default)
        {
            lock (_Control)
            {
                if (GetStatus().IsRunning)
                {
                    return new MonitoringStartResult(MonitoringStartProblem.AlreadyRunning, new[] { "the monitoring is already running." }, null);
                }
                lock (_Sync)
                {
                    _Run = null;
                    _State = MonitoringState.Starting;
                    _LastStart = null;
                    _LastError = null;
                    _StartedAt = null;
                    _LastCheckAt = null;
                    _NextCheckAt = null;
                    _CheckNumber = 0;
                    _Searches = 0;
                }
                MonitoringStartResult Result;
                Run? Started = null;
                try
                {
                    Result = StartRun(Config, HostToken, out Started);
                }
                catch (Exception ex)
                {
                    Result = new MonitoringStartResult(MonitoringStartProblem.StartFailed, ConsoleUi.DescribeFailure(ex, Config), ex);
                }
                lock (_Sync)
                {
                    _LastStart = Result;
                    if (!Result.Started)
                    {
                        _State = MonitoringState.NotStarted;
                    }
                }
                //the checks begin only now, so the first event of a check never comes before Start's result is kept
                Started?.Thread!.Start();
                return Result;
            }
        }

        // Asks the run to end and waits until it has (see the class comment for what a check does then). Returns the
        // status at once when nothing runs. Called from one of the service's own events, it only asks: the run ends when
        // that event returns
        internal MonitoringStatus Stop()
        {
            Run? Current = RequestStop();
            if (Current != null && Current.Thread != Thread.CurrentThread)
            {
                lock (_Control)
                {
                    Current.Ended.Wait();
                }
            }
            return GetStatus();
        }

        // Stop, then Start with the settings given: the search files are read again and the check number starts from 1
        internal MonitoringStartResult Restart(CampertronInternalConfig Config, CancellationToken HostToken = default)
        {
            lock (_Control)
            {
                Stop();
                return Start(Config, HostToken);
            }
        }

        // Waits until the current run has ended (stopped or failed) and returns the status; at once when nothing runs.
        // The console's adapter waits here for as long as its process runs
        internal MonitoringStatus WaitForEnd()
        {
            Run? Current;
            lock (_Sync)
            {
                Current = _Run;
            }
            Current?.Ended.Wait();
            return GetStatus();
        }

        // the same with a limit: null when the run has not ended in time
        internal MonitoringStatus? WaitForEnd(TimeSpan Timeout)
        {
            Run? Current;
            lock (_Sync)
            {
                Current = _Run;
            }
            if (Current != null && !Current.Ended.Wait(Timeout))
            {
                return null;
            }
            return GetStatus();
        }

        private Run? RequestStop()
        {
            Run? Current;
            lock (_Sync)
            {
                Current = _Run;
                if (Current == null || Current.Ended.IsSet)
                {
                    return null;
                }
                Current.StopRequested = true;
            }
            try
            {
                Current.Cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                //the run ended meanwhile
            }
            return Current;
        }

        internal MonitoringStatus GetStatus()
        {
            Run? Current;
            MonitoringStatus Snapshot;
            lock (_Sync)
            {
                Current = _Run;
                Snapshot = new MonitoringStatus
                {
                    State = _State,
                    StopRequested = Current != null && Current.StopRequested && !Current.Ended.IsSet,
                    StartedAt = _StartedAt,
                    Searches = _Searches,
                    CheckNumber = _CheckNumber,
                    LastCheckAt = _LastCheckAt,
                    NextCheckAt = _State == MonitoringState.Waiting ? _NextCheckAt : null,
                    LastError = _LastError,
                    LastStart = _LastStart,
                    SearchFilesChangedSinceStart = false
                };
            }
            if (Current == null || !SearchFilesChanged(Current))
            {
                return Snapshot;
            }
            return new MonitoringStatus
            {
                State = Snapshot.State,
                StopRequested = Snapshot.StopRequested,
                StartedAt = Snapshot.StartedAt,
                Searches = Snapshot.Searches,
                CheckNumber = Snapshot.CheckNumber,
                LastCheckAt = Snapshot.LastCheckAt,
                NextCheckAt = Snapshot.NextCheckAt,
                LastError = Snapshot.LastError,
                LastStart = Snapshot.LastStart,
                SearchFilesChangedSinceStart = true
            };
        }

        // ---- the start ----

        private MonitoringStartResult StartRun(CampertronInternalConfig Config, CancellationToken HostToken, out Run? Started)
        {
            Started = null;
            if (!Notifications.IsMonitoring(Config.GeneralConfig?.OutputTo))
            {
                return new MonitoringStartResult(MonitoringStartProblem.NotMonitoring,
                    new[] { "the monitoring is not turned on: outputTo in General.yaml is not Email or Ntfy." }, null);
            }
            //Email.yaml must hold what sending needs before anything is searched (TD-51, D41): the sample's empty server
            //and addresses used to fail only at the first send, after a whole search. Ntfy.yaml likewise when ntfy is
            //enabled or outputTo: Ntfy needs it (D67); a provider that can't be used is never ignored. Both throw
            if (Notifications.EmailEnabled(Config))
            {
                Load.CheckEmailOutputSettings(Config);
            }
            if (Notifications.NtfyEnabled(Config) || Notifications.NtfyRequired(Config))
            {
                Load.CheckNtfyOutputSettings(Config);
            }
            Report(new MonitoringEvent(MonitoringEventKind.Starting));
            //the SMTP password, from its configured source, once, here, for every send of these settings (D66): the file
            //smtpPasswordFile names, else CAMPERTRON_EMAIL_PASSWORD, else smtpPassword. A source that is configured but
            //can't be used stops the start, as D64's gate does: never a sign-in with another password
            if (Notifications.EmailEnabled(Config) && Load.SmtpPasswordStartProblem(Config) is List<string> PasswordProblem)
            {
                return new MonitoringStartResult(MonitoringStartProblem.SmtpPassword, PasswordProblem, null);
            }
            //ntfy's access token likewise (D67): tokenFile, else CAMPERTRON_NTFY_TOKEN, else none; never a publish
            //without the token when its source can't be used
            if (Notifications.NtfyEnabled(Config) && Load.NtfyTokenStartProblem(Config) is List<string> TokenProblem)
            {
                return new MonitoringStartResult(MonitoringStartProblem.NtfyToken, TokenProblem, null);
            }
            //the search files are read once, here, for every check of this run (a change needs a restart). The checks
            //start only with a search to check (D64): with none, the monitoring would check nothing every minute for as
            //long as it ran, which looks healthy to a supervisor. Without a search file nothing is created: no example
            //search is checked in the operator's name (D65)
            string ConfigPath = Config.ConfigPath!;
            List<string> SearchFiles = SearchFilesSnapshot(ConfigPath);
            List<CampertronConfig> Searches = Load.LoadMonitoringSearches(ConfigPath, Warning, null, out int SearchFileCount, out Load.EmailStartProblem? Problem);
            if (Problem != null)
            {
                return new MonitoringStartResult(MonitoringStartProblem.NoSearchToCheck,
                    Load.EmailStartProblemText(Problem.Value, SearchFileCount, Searches.Count, Notifications.ModeName(Config)), null);
            }
            //one monitoring at a time for a settings folder (D83): taken last of the checks, so a start that is refused for
            //any other reason has not even created the lock's file
            FileStream? Lock = MonitoringLock.TryAcquire(ConfigPath, out MonitoringLockProblem? LockProblem);
            if (Lock == null)
            {
                MonitoringLockProblem Why = LockProblem ?? MonitoringLockProblem.Unavailable;
                return new MonitoringStartResult(Why == MonitoringLockProblem.Held ? MonitoringStartProblem.LockHeld : MonitoringStartProblem.LockUnavailable,
                    MonitoringLock.Describe(Why, Notifications.ModeName(Config)), null);
            }
            Run New = new Run { Config = Config, Lock = Lock, SearchFiles = SearchFiles };
            try
            {
                New.Cancel = CancellationTokenSource.CreateLinkedTokenSource(HostToken);
                New.Data = Load.PrepareRun(Config, Searches, Status);
                //the campgrounds' names, for a request's description in a warning (the console's list of the searches looks
                //them up too): a host that lists nothing says the same words
                foreach (string CampgroundId in New.Data.UniqueCampgroundIds)
                {
                    Cache.GetCampInfo(CampgroundId, Config.CachePath);
                }
                New.Data.Monitoring = new MonitoringRun(New.Cancel.Token, Status, Warning, Description => RequestBlocked(New, Description));
                Report(new MonitoringEvent(MonitoringEventKind.SearchesLoaded) { Searches = Searches.AsReadOnly() });
                New.Thread = new Thread(() => RunChecks(New)) { IsBackground = true, Name = "Campertron monitoring" };
            }
            catch
            {
                New.Cancel?.Dispose();
                Lock.Dispose();
                throw;
            }
            lock (_Sync)
            {
                _Run = New;
                _StartedAt = _Options.Now();
                _Searches = Searches.Count;
                _State = MonitoringState.Checking;
            }
            Started = New;
            return new MonitoringStartResult(MonitoringStartProblem.None, Array.Empty<string>(), null);
        }

        // the search files as a start finds them: each file's name, size and time of writing (metadata only), in a fixed
        // order. The status compares it with the folder later (SearchFilesChangedSinceStart)
        internal static List<string> SearchFilesSnapshot(string ConfigPath)
        {
            var Snapshot = new List<string>();
            foreach (string FilePath in ConfigFiles.SearchFiles(ConfigPath))
            {
                var Info = new FileInfo(FilePath);
                Snapshot.Add(Info.Name + "|" + Info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                    Info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            Snapshot.Sort(StringComparer.Ordinal);
            return Snapshot;
        }

        private static bool SearchFilesChanged(Run Current)
        {
            try
            {
                return !SearchFilesSnapshot(Current.Config.ConfigPath!).SequenceEqual(Current.SearchFiles, StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                //a folder that can't be listed now is not the folder the start read
                return true;
            }
        }

        // ---- the checks ----

        private void RunChecks(Run Current)
        {
            Exception? Failure = null;
            try
            {
                CancellationToken Token = Current.Cancel.Token;
                RunningData RunData = Current.Data;
                CampertronInternalConfig Config = Current.Config;
                bool FirstCheck = true;
                while (true)
                {
                    Token.ThrowIfCancellationRequested();
                    //never a check of nothing: the start refused that, so an empty plan here is a defect, ended visibly
                    Load.RequireSearchesToCheck(RunData.CampertronConfigFiles);
                    if (!FirstCheck)
                    {
                        Load.RegenerateSearchDates(RunData.CampertronConfigFiles, DateTime.Now);
                    }
                    FirstCheck = false;
                    SetState(MonitoringState.Checking);
                    //each search with what it found, for the check's event (its host's view of the check): by the search
                    var Found = new System.Collections.Concurrent.ConcurrentDictionary<CampertronConfig, ConsoleConfigItem>(ReferenceEqualityComparer.Instance);
                    Parallel.ForEach(RunData.CampertronConfigFiles, ThisCampertronConfig =>
                    {
                        ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                        NewConfigItem.Name = ThisCampertronConfig.DisplayName;
                        NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisCampertronConfig, ref RunData, Config);
                        RunData.AllConsoleConfigItems.Add(NewConfigItem);
                        Found[ThisCampertronConfig] = NewConfigItem;
                    });
                    //a stop that came while the check searched: nothing of it is sent or recorded
                    Token.ThrowIfCancellationRequested();
                    int Number;
                    lock (_Sync)
                    {
                        Number = ++_CheckNumber;
                    }
                    //from here to the end of the report nothing reads the token: a check that sends finishes every
                    //notification and its records (D83)
                    var Checked = new List<MonitoringCheckedSearch>();
                    foreach (CampertronConfig Search in RunData.CampertronConfigFiles)
                    {
                        if (Found.TryGetValue(Search, out ConsoleConfigItem? Item))
                        {
                            Checked.Add(new MonitoringCheckedSearch(Search, (IReadOnlyList<ConsoleConfigValue>?)Item.Values ?? Array.Empty<ConsoleConfigValue>()));
                        }
                    }
                    ReportCheck(Config, RunData, Number, _Options.Report, _Options.Now, () => SetState(MonitoringState.Sending), Checked.AsReadOnly());
                    //clear data for next run
                    RunData.Urls.Clear();
                    RunData.AllConsoleConfigItems.Clear();
                    RunData.SiteData.Clear();
                    RunData.FilteredAvailableData.Clear();
                    DateTime Finished = _Options.Now();
                    lock (_Sync)
                    {
                        _LastCheckAt = Finished;
                        _NextCheckAt = Finished + CheckInterval;
                        _State = MonitoringState.Waiting;
                    }
                    if (Token.IsCancellationRequested)
                    {
                        break;
                    }
                    //the wait between two checks; the check's line has already said when the next one is
                    Wait(CheckInterval, Token);
                }
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
            finally
            {
                End(Current, Failure);
            }
        }

        private void Wait(TimeSpan Delay, CancellationToken Token)
        {
            if (_Options.Wait != null)
            {
                _Options.Wait(Delay, Token);
            }
            else
            {
                Token.WaitHandle.WaitOne(Delay);
            }
        }

        // The report of one check, after its searches: the re-arm, the gate (an opening without a Camp.History marker), the
        // check's event and, when there is news, its notifications. First the markers of the nights this check saw
        // unavailable are removed (D104: only from the months it fetched and read; a failure only warns), so that they are
        // notified again if they become available again; a later failure of the check undoes nothing of it. Camp.History
        // gets no marker before the notifications: each search's openings are recorded once a provider delivered them
        // (Notifications.Deliver), and the run's other hits after the last one, so a check whose notifications all went out
        // records what it always recorded, and one that fails records nothing of what was not delivered (TD-14, D67).
        // Throws what a failed delivery throws, after the check's other deliveries. Sending is called once, before the
        // first notification. Reached only by a check whose searches all completed
        internal static void ReportCheck(CampertronInternalConfig Config, RunningData RunData, int Number, Action<MonitoringEvent>? Report, Func<DateTime> Now,
            Action? Sending = null, IReadOnlyList<MonitoringCheckedSearch>? Checked = null)
        {
            //a night this check saw unavailable can be notified again (its months only: RunData.SiteData holds a month once
            //its answer was read)
            NotificationHistory.RearmResult Rearmed = NotificationHistory.Rearm(Config, RunData.SiteData);
            if (Rearmed.NotRemoved > 0)
            {
                Report?.Invoke(new MonitoringEvent(MonitoringEventKind.Warning) { Text = NotificationHistory.NotRemovedText(Rearmed.NotRemoved) });
            }
            //determine if there are new entries
            bool NewEntries = Load.HasUnreportedEntries(new List<AvailableData>(RunData.FilteredAvailableData), Config);
            bool Sends = NewEntries && RunData.FilteredAvailableData.Count() > 0;
            (int Emails, int NtfyMessages) = Notifications.Planned(Config, RunData.AllConsoleConfigItems);
            Report?.Invoke(new MonitoringEvent(MonitoringEventKind.CheckReported)
            {
                CheckNumber = Number,
                SearchCount = RunData.AllConsoleConfigItems.Count,
                NewOpenings = Sends ? RunData.FilteredAvailableData.Count() : 0,
                Emails = Emails,
                NtfyMessages = NtfyMessages,
                Checked = Checked
            });
            if (!Sends)
            {
                return;
            }
            Sending?.Invoke();
            GeneralConfig GenConfig = Config.GeneralConfig ?? throw new ArgumentException("GeneralConfig is not loaded", nameof(Config));
            List<ConsoleConfigItem> Ordered = RunData.AllConsoleConfigItems.OrderBy(p => p.Name).ToList();
            //each search to every enabled provider (email, ntfy); a search's openings are recorded once any provider
            //delivered them (never before; a later failure does not undo it), and a failure is thrown after the check
            Notifications.Deliver(Config, Ordered, Search => Load.RecordNotificationHistory(Load.OpeningsOf(Search.Values), Config), new DeliveryOutput(Now,
                (Name, Openings) => Report?.Invoke(new MonitoringEvent(MonitoringEventKind.EmailSent) { SearchName = Name, Openings = Openings }),
                (Name, Openings) => Report?.Invoke(new MonitoringEvent(MonitoringEventKind.NtfySent) { SearchName = Name, Openings = Openings })));
            //every notification of the check was sent: the run's other hits (those no notification shows, such as a search
            //with consecutiveDays below 1) are recorded now, so the history of the check is complete
            Load.RecordNotificationHistory(Load.HitsOf(RunData.FilteredAvailableData), Config);
            Report?.Invoke(new MonitoringEvent(MonitoringEventKind.DeliveriesFinished));
        }

        // a blocked request with no handling of the host's: the run fails with it, and its other searches stop at their
        // next checkpoint instead of asking recreation.gov again
        private void RequestBlocked(Run Current, string Description)
        {
            if (_Options.Blocked != null)
            {
                _Options.Blocked(Description);
                return;
            }
            var Blocked = new MonitoringBlockedException(Description);
            lock (_Sync)
            {
                Current.Blocked ??= Blocked;
            }
            Current.Cancel.Cancel();
            throw Blocked;
        }

        private void End(Run Current, Exception? Failure)
        {
            //the lock goes first: once the status says the run has ended, another monitoring can start
            try
            {
                Current.Lock?.Dispose();
            }
            catch (IOException)
            {
                //closing an empty file that was never written
            }
            Current.Lock = null;
            DateTime At = _Options.Now();
            lock (_Sync)
            {
                Exception? Error = Current.Blocked ?? (Failure != null && !(Current.Cancel.IsCancellationRequested && IsCancellation(Failure)) ? Failure : null);
                if (Error != null)
                {
                    _LastError = new MonitoringFailure(At, Error, ConsoleUi.DescribeFailure(Error, Current.Config), Current.Blocked != null);
                    _State = MonitoringState.Failed;
                }
                else
                {
                    _State = MonitoringState.Stopped;
                }
                _NextCheckAt = null;
            }
            Current.Cancel.Dispose();
            Current.Ended.Set();
        }

        // true when a failure is only the stop itself: the searches' own cancellation, alone or gathered by Parallel.ForEach
        private static bool IsCancellation(Exception Failure)
        {
            if (Failure is AggregateException Many)
            {
                return Many.Flatten().InnerExceptions.All(Inner => Inner is OperationCanceledException);
            }
            return Failure is OperationCanceledException;
        }

        private void SetState(MonitoringState State)
        {
            lock (_Sync)
            {
                _State = State;
            }
        }

        private void Report(MonitoringEvent Event) => _Options.Report?.Invoke(Event);
        private void Status(string Text) => Report(new MonitoringEvent(MonitoringEventKind.Status) { Text = Text });
        private void Warning(string Text) => Report(new MonitoringEvent(MonitoringEventKind.Warning) { Text = Text });
    }
}
