// The host-neutral monitoring service's options, events, status and results (v0.3.0, decisions D83 and D90;
// business-rules.md §29). MonitoringService runs the unattended monitoring's checks (outputTo: Email or Ntfy): its start
// checks, the monitoring lock, the search files read once, then a check every minute until it is stopped or a check
// fails. The console's email and ntfy mode reach it through MonitoringConsole, which writes its events through ConsoleUi
// exactly as the loop wrote them before the service and keeps the console's exit codes. Nothing here writes to a
// console, waits for a key or ends the process, and nothing here is static state: a service's check number, status and
// lock belong to that service.

// what a monitoring service is doing
internal enum MonitoringState
{
    // never started, or its last start was refused (MonitoringStatus.LastStart says why)
    NotStarted,
    // Start is checking the settings, the secrets, the search files and the lock
    Starting,
    // a check is searching; a stop ends it at its next checkpoint, and nothing of it is sent or recorded
    Checking,
    // a check is sending its notifications and recording them; a stop waits until it has finished both
    Sending,
    // between two checks
    Waiting,
    // stopped on request; the lock is released
    Stopped,
    // a check failed (MonitoringStatus.LastError); the lock is released and nothing starts the monitoring again by itself
    Failed
}

// why a start did not start the monitoring
internal enum MonitoringStartProblem
{
    None,
    // this service is already monitoring
    AlreadyRunning,
    // outputTo is not Email or Ntfy: there is no monitoring to start
    NotMonitoring,
    // the start threw (MonitoringStartResult.Error): Email.yaml or Ntfy.yaml refused, a search file refused, or anything
    // else. The console rethrows it, so the failure is reported as it always was
    StartFailed,
    // the SMTP password's configured source can't be used (D66)
    SmtpPassword,
    // ntfy's token source can't be used (D67)
    NtfyToken,
    // no search to check (D64): no search file, none Enabled (autoRun), or none with a campgroundID
    NoSearchToCheck,
    // another monitoring holds the settings folder's monitoring lock (D83)
    LockHeld,
    // the monitoring lock's file can't be created or opened in the settings folder
    LockUnavailable
}

// how a start ended. Lines are the words for the user, the first one the problem (the console writes it after
// "Error: "); they hold no value of a settings file, no password, token or topic, and no path, except that the words of
// a start that threw (StartFailed) are ConsoleUi.DescribeFailure's, which name the settings file the failure belongs to
internal sealed class MonitoringStartResult
{
    internal MonitoringStartResult(MonitoringStartProblem Problem, IReadOnlyList<string> Lines, Exception? Error)
    {
        this.Problem = Problem;
        this.Lines = Lines;
        this.Error = Error;
    }
    internal bool Started => Problem == MonitoringStartProblem.None;
    internal MonitoringStartProblem Problem { get; }
    internal IReadOnlyList<string> Lines { get; }
    // the exception of a start that threw (StartFailed), else null
    internal Exception? Error { get; }
}

// what the service reports while it starts and checks, in the order it happens and on the thread it happens on (the
// caller's during Start, the service's own afterwards)
internal enum MonitoringEventKind
{
    // the settings passed their checks: the monitoring is about to resolve its secrets and read the search files
    Starting,
    // a status line (Text): a campground's details are being prepared
    Status,
    // a warning (Text): a search's dates, a request that is tried again
    Warning,
    // the search files were read and their campgrounds prepared (Searches): what every check of this run checks
    SearchesLoaded,
    // a check searched (CheckNumber, SearchCount) and found NewOpenings to notify (0: nothing new); Emails and
    // NtfyMessages are what it sends now
    CheckReported,
    // one search's email was sent (SearchName, Openings)
    EmailSent,
    // one search's ntfy notification was published (SearchName, Openings)
    NtfySent,
    // every notification of the check was sent and recorded; the next check follows after the wait
    DeliveriesFinished
}

internal sealed class MonitoringEvent
{
    internal MonitoringEvent(MonitoringEventKind Kind)
    {
        this.Kind = Kind;
    }
    internal MonitoringEventKind Kind { get; }
    internal string? Text { get; init; }
    internal IReadOnlyList<CampertronConfig>? Searches { get; init; }
    internal int CheckNumber { get; init; }
    internal int SearchCount { get; init; }
    internal int NewOpenings { get; init; }
    internal int Emails { get; init; }
    internal int NtfyMessages { get; init; }
    internal string? SearchName { get; init; }
    internal int Openings { get; init; }
    // CheckReported: each search the check searched with what it found (its output stream), in the order the start loaded
    // the searches (v0.3.0's PR 8, D98: the web UI's monitoring page shows the last check). The lists are the check's own: the
    // run lets go of them after the check, so a host copies what it keeps during the event and changes nothing. Null for a check
    // reported alone (MonitoringConsole.ReportCheck); the console's adapter never reads it
    internal IReadOnlyList<MonitoringCheckedSearch>? Checked { get; init; }
}

// one search of a check and what it found: its output stream as the console, the email and ntfy compose it from
internal sealed record MonitoringCheckedSearch(CampertronConfig Search, IReadOnlyList<ConsoleConfig.ConsoleConfigValue> Values);

// a check that failed and ended the monitoring. Lines are ConsoleUi.DescribeFailure's words for it (every password the
// run knows, ntfy's token and its topic removed); Error is the exception itself, which the console rethrows
internal sealed class MonitoringFailure
{
    internal MonitoringFailure(DateTime At, Exception Error, IReadOnlyList<string> Lines, bool Blocked)
    {
        this.At = At;
        this.Error = Error;
        this.Lines = Lines;
        this.Blocked = Blocked;
    }
    internal DateTime At { get; }
    internal Exception Error { get; }
    internal IReadOnlyList<string> Lines { get; }
    // recreation.gov blocked a request (too many requests)
    internal bool Blocked { get; }
}

// a snapshot of a service, safe to keep: nothing in it changes afterwards
internal sealed class MonitoringStatus
{
    internal MonitoringState State { get; init; }
    // a stop was asked for and the run has not ended yet
    internal bool StopRequested { get; init; }
    // when the current or last run started (the service's clock), null before the first
    internal DateTime? StartedAt { get; init; }
    // how many searches the current or last run checks at every check
    internal int Searches { get; init; }
    // the number of the last check the current or last run reported; 0 before its first. Every start counts from 1
    internal int CheckNumber { get; init; }
    // when that check finished: its notifications sent and recorded, or nothing new found
    internal DateTime? LastCheckAt { get; init; }
    // when the next check starts, while the service waits for it
    internal DateTime? NextCheckAt { get; init; }
    // why the last run ended, if a check failed
    internal MonitoringFailure? LastError { get; init; }
    // the search files in the settings folder are not the ones the current or last run read when it started (a file
    // added, removed, or its size or time of writing changed). Read from the files' metadata, never their contents
    internal bool SearchFilesChangedSinceStart { get; init; }
    // the last start's result, null before the first
    internal MonitoringStartResult? LastStart { get; init; }
    internal bool IsRunning => State == MonitoringState.Starting || State == MonitoringState.Checking || State == MonitoringState.Sending || State == MonitoringState.Waiting;
}

// what a host gives a monitoring service. Every member has a default that writes and ends nothing
internal sealed class MonitoringOptions
{
    // receives the service's events (the console's adapter writes its lines from them); null: nothing is reported
    internal Action<MonitoringEvent>? Report { get; init; }
    // the service's clock: the status's times and the time a results email carries
    internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
    // the wait between two checks: it returns after the delay, or sooner once the token is canceled (a stop). The default
    // waits on the token for the delay. Tests replace it to end or to hold the loop
    internal Action<TimeSpan, CancellationToken>? Wait { get; init; }
    // what a request that recreation.gov blocked does, given the request's description. Null (the default): the check
    // and the monitoring end as failed (MonitoringFailure.Blocked) and the process goes on. The console's adapter gives
    // its own: the error, the five-minute wait and exit code 1, as before the service (TD-44)
    internal Action<string>? Blocked { get; init; }
}

// recreation.gov blocked a request of a check and the host gave no Blocked of its own: the monitoring ends as failed
internal sealed class MonitoringBlockedException : Exception
{
    internal MonitoringBlockedException(string Request) : base("recreation.gov blocked a request for exceeding its request limit (" + Request + ").")
    {
    }
}
