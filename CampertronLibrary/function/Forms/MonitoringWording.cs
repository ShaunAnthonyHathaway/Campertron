using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronLibrary.function.Forms
{
    // What a host of the monitoring controller says in its own words (v0.4.0's phase 2): where its user fixes a settings
    // or search file, what restarts the host, what else waits the five minutes after a block, where the test email and
    // notification are sent from, what its own start of the monitoring is called (MonitoringController.StartWithHost), its
    // stop (Shutdown) and an action for a moment its view no longer shows. Every other word of the monitoring's states and
    // decisions is MonitoringWording's, the same in every host. Fixed words: never a value, a path, a server's text or an
    // exception's message.
    internal sealed class MonitoringHostWords
    {
        // the fix after "<mode> can't start: Email.yaml does not exist yet."
        internal required string EmailMissingFix { get; init; }
        // the fix after "Campertron couldn't find its cache folder"
        internal required string NoCacheFolderFix { get; init; }
        // the fix after "Campertron couldn't read its campground data"
        internal required string NoCampgroundDataFix { get; init; }
        // the fix after a start that a settings or search file refused: its validation's words, no valid YAML, a file that
        // can't be read or used
        internal required string RefusedFileFix { get; init; }
        internal required string InvalidYamlFix { get; init; }
        internal required string UnreadableFileFix { get; init; }
        // after a block: what waits the five minutes
        internal required string BlockedFix { get; init; }
        // the last line after a failed notification: fix it, test it, start again
        internal required string NotificationFix { get; init; }
        // the state after a start with the host that did not start, after "Not running: "
        internal required string NotStartedWithHost { get; init; }
        // the host stops: why no action is available, and what an action did
        internal required string UnavailableShuttingDown { get; init; }
        internal required string ShuttingDownText { get; init; }
        // an action for a moment of the monitoring the host's view no longer shows: nothing was done
        internal required string StaleText { get; init; }
    }

    // The words of the monitoring's states and decisions (v0.3.0's PR 8, D98; business-rules-settings §35), for every host of
    // the monitoring controller (MonitoringController; v0.4.0's phase 2 moved them here from the web UI's
    // WebMonitoringWording, unchanged; the web UI is the only host so far). A start's refusal is said in the monitoring
    // start's own words (D83 point 2: MonitoringStartResult.Lines, which hold no value, secret or path), and a failure in
    // fixed words of its kind: the test notification's words for a failed email (TestNotification.DescribeSmtpFailure: the
    // failure's kind and the SMTP status number, never the server's text), ntfy's own (NtfyPublisher.Describe), the
    // secrets' sources' (SmtpPassword.Describe, NtfyToken.Describe), a block, or an unexpected problem by its type's name.
    // Never an exception's message, a server's answer, a recipient, a topic, a password, a token, an address or a path.
    // What only one host says is its MonitoringHostWords.
    public static class MonitoringWording
    {
        public const string Heading = "Monitoring";
        public const string StatusHeading = "Status";
        public const string ActionsHeading = "Start and stop";
        public const string StartLabel = "Start";
        public const string StopLabel = "Stop";
        public const string RestartLabel = "Restart";
        public const string StartHelp = "Start reads General.yaml, Email.yaml, Ntfy.yaml and the search files now and checks them as email and ntfy mode's start does; then the monitoring checks the Enabled (autoRun) searches every minute.";
        public const string RestartHelp = "Restart stops the monitoring and starts it again, so that it reads the settings and the search files again.";
        internal const string StartUnavailableRunning = "Start is unavailable while the monitoring runs: stop or restart it instead.";
        internal const string StopUnavailableIdle = "Stop and Restart are available while the monitoring runs.";
        internal const string UnavailableBusy = "Start, Stop and Restart are unavailable while the monitoring is starting or stopping, and available again once it has.";
        internal const string RestartUnavailableCooldown = "Restart is unavailable until then, because it starts the monitoring again.";
        internal const string FilesChanged = "The search files changed since the monitoring started. It checks the searches it read when it started: select Restart to read them again.";
        public const string LastCheckHeading = "The last check";
        public const string NoCheckYet = "No check has finished since the monitoring started.";
        internal const string NoNewOpening = "No new opening for this search at this check.";
        public const string ResultsOfName = "Searches of the last check";
        internal const string NoNewOpeningShort = "no new opening";
        internal const string BusyText = "Nothing was done: the monitoring is starting or stopping. Try again once it has.";
        internal const string AlreadyRunningText = "Nothing was started: the monitoring is already running.";
        internal const string NotRunningText = "Nothing was stopped: the monitoring is not running.";
        // the campground data's refresh pauses the monitoring (v0.3.0's PR 9, D83 point 6)
        internal const string UnavailablePaused = "Start, Stop and Restart are unavailable while the campground data is refreshed (Settings > System). They are available again when the refresh has finished.";
        internal const string PausedText = "Nothing was done: the campground data is being refreshed (Settings > System). The monitoring is paused until the refresh has finished.";

        // ---- the facts of a monitoring that started (v0.3.0's PR 8 wrote them in the web UI's page; v0.4.0's phase 4 moved
        // them here for the desktop GUI's Monitoring tab too) ----

        public const string StartedLabel = "Started";
        public const string NotificationsLabel = "Notifications";
        public const string SearchesLabel = "Searches checked";
        public const string LastFinishedLabel = "Last finished check";
        public const string NextCheckLabel = "Next check";
        internal const string NoFinishedCheck = "None yet";
        internal const string AfterRefreshNote = " (again after the campground data's refresh)";

        // when it started, and why when it was not the user's Start: the host's own start in the host's words (WithHostNote;
        // the web UI's " (when web mode started)"), or again after the campground data's refresh. Only once it started
        internal static string StartedText(MonitoringControlView View, string WithHostNote)
        {
            MonitoringControlStart? Start = View.ThisStart;
            return When(View.Status.StartedAt!.Value) + (Start != null && Start.Started && Start.WithHost ? WithHostNote
                : Start != null && Start.Started && Start.AfterRefresh ? AfterRefreshNote : "");
        }

        internal static string SearchesText(int Searches) => Searches.ToString(CultureInfo.InvariantCulture) + " (the Enabled (autoRun) searches it read when it started)";

        internal static string LastFinishedText(MonitoringStatus Status) => Status.LastCheckAt is DateTime At
            ? "Check " + Status.CheckNumber.ToString(CultureInfo.InvariantCulture) + " at " + When(At) : NoFinishedCheck;

        // the mode's name in the start's words ("email mode", "ntfy mode"), or "the monitoring" before General.yaml was read
        internal static string Mode(CampertronInternalConfig? Config) => Config?.GeneralConfig == null ? "the monitoring" : Notifications.ModeName(Config);

        internal static string When(DateTime At) => At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // why an action is unavailable, in words (a disabled button says nothing by itself)
        internal static string Unavailable(MonitoringControlView V, MonitoringHostWords Host)
        {
            var Lines = new List<string>();
            if (V.ShuttingDown)
            {
                Lines.Add(Host.UnavailableShuttingDown);
            }
            else if (V.Paused)
            {
                Lines.Add(UnavailablePaused);
            }
            else if (V.Phase == MonitoringControlPhase.Busy || V.Status.StopRequested)
            {
                Lines.Add(UnavailableBusy);
            }
            else if (V.Phase == MonitoringControlPhase.Running)
            {
                Lines.Add(StartUnavailableRunning);
                if (V.Cooldown != null)
                {
                    Lines.Add(RestartUnavailableCooldown);
                }
            }
            else
            {
                Lines.Add(StopUnavailableIdle);
            }
            return string.Join(" ", Lines);
        }

        // ---- the state, in words (never by color alone) ----

        internal static string StateText(MonitoringControlView View, MonitoringHostWords Host)
        {
            MonitoringStatus Status = View.Status;
            switch (View.Busy)
            {
                case MonitoringControlBusy.Starting:
                    return "Starting: reading the settings and the search files and preparing the campsite details…";
                case MonitoringControlBusy.Restarting:
                    return Status.IsRunning ? Stopping : "Restarting: starting the monitoring again…";
                case MonitoringControlBusy.Stopping:
                case MonitoringControlBusy.Ending:
                    return Stopping;
                case MonitoringControlBusy.Pausing:
                    return "Pausing for the campground data's refresh: a check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them first…";
            }
            if (View.Paused && !Status.IsRunning && View.Resumes)
            {
                return "Paused while the campground data is refreshed (Settings > System): the monitoring starts again by itself when the refresh has finished.";
            }
            if (Status.IsRunning)
            {
                if (Status.StopRequested)
                {
                    return Stopping;
                }
                return Status.State switch
                {
                    MonitoringState.Sending => "Running: check " + Status.CheckNumber.ToString(CultureInfo.InvariantCulture) + " is sending its notifications.",
                    MonitoringState.Waiting when Status.NextCheckAt is DateTime Next => "Running: the next check starts at " + When(Next) + ".",
                    MonitoringState.Starting => "Starting…",
                    _ => "Running: check " + (Status.CheckNumber + 1).ToString(CultureInfo.InvariantCulture) + " is searching.",
                };
            }
            if (View.ThisEnd is MonitoringControlEnd End)
            {
                return End.Failed ? "Not running: it stopped at " + When(End.At) + " because a check failed. It does not start again by itself."
                    : "Not running: it was stopped at " + When(End.At) + ". It does not start again by itself: select Start to start it again.";
            }
            if (View.ThisStart is MonitoringControlStart Start && !Start.Started)
            {
                return "Not running: " + (Start.WithHost ? Host.NotStartedWithHost : "the last start did not start it.");
            }
            return "Not running.";
        }

        private const string Stopping = "Stopping: a check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them first…";

        // what an action did, when it did not do what was asked
        internal static string? Outcome(MonitoringControlOutcome Outcome, string? Cooldown, MonitoringHostWords Host) => Outcome switch
        {
            MonitoringControlOutcome.Stale => Host.StaleText,
            MonitoringControlOutcome.Busy => BusyText,
            MonitoringControlOutcome.AlreadyRunning => AlreadyRunningText,
            MonitoringControlOutcome.NotRunning => NotRunningText,
            MonitoringControlOutcome.CoolingDown => "Nothing was started: " + (Cooldown ?? "recreation.gov limited Campertron's requests a few minutes ago."),
            MonitoringControlOutcome.ShuttingDown => Host.ShuttingDownText,
            MonitoringControlOutcome.PausedForRefresh => PausedText,
            _ => null
        };

        // the last check in a line: "Check 3 at 2026-11-23 14:05: 2 searches, 4 new openings; 1 of 1 email sent." ("… sent so
        // far." while its monitoring still runs and its deliveries have not all gone out)
        internal static string CheckSummary(MonitoringControlCheck Check, bool Ended)
        {
            string Head = "Check " + Check.Number.ToString(CultureInfo.InvariantCulture) + " at " + When(Check.At) + ": " + ConsoleUi.Count(Check.SearchCount, "search", "searches") + ", ";
            if (Check.NewOpenings <= 0)
            {
                return Head + "nothing new to notify.";
            }
            var Sent = new List<string>();
            if (Check.Emails > 0)
            {
                Sent.Add(Check.EmailsSent.ToString(CultureInfo.InvariantCulture) + " of " + ConsoleUi.Count(Check.Emails, "email", "emails"));
            }
            if (Check.NtfyMessages > 0)
            {
                Sent.Add(Check.NtfySent.ToString(CultureInfo.InvariantCulture) + " of " + ConsoleUi.Count(Check.NtfyMessages, "ntfy notification", "ntfy notifications"));
            }
            string Sending = Sent.Count == 0 ? "nothing to send." : string.Join(" and ", Sent) + " sent" + (Check.Delivered || Ended ? "." : " so far.");
            return Head + ConsoleUi.Count(Check.NewOpenings, "new opening", "new openings") + "; " + Sending;
        }

        // a search of the last check, as a list of them names it
        internal static string SearchLabel(ResultSearchViewModel Search) =>
            Search.Title + " · " + (Search.Openings > 0 ? Search.CountsText : NoNewOpeningShort);

        internal static string UnnamedSearch(int Number) => "Search " + Number.ToString(CultureInfo.InvariantCulture);

        // ---- the host's lines of a start and an end (MonitoringControllerOptions.Line; the web UI writes each after its
        // prefix, "Web UI: monitoring: ") ----

        internal static string StartedLine(string? Output, int Searches) => "started (" + Output + "; " + ConsoleUi.Count(Searches, "search", "searches") + " checked every minute).";
        internal static string NotStartedLine(string Problem) => "not started: " + Problem;
        internal static string FailedLine(string Problem) => "stopped because a check failed: " + Problem;
        internal const string PausedLine = "paused while the campground data is refreshed.";
        internal const string StoppedLine = "stopped.";

        // ---- why a start did not start the monitoring ----

        internal static IReadOnlyList<string> EmailMissing(string Mode, MonitoringHostWords Host) => new[]
        {
            Mode + " can't start: " + ConfigFiles.EmailFileName + " does not exist yet.",
            Host.EmailMissingFix,
            "Nothing was searched or sent."
        };

        internal static IReadOnlyList<string> NoCacheFolder(string Mode, MonitoringHostWords Host) => new[]
        {
            Mode + " can't start: Campertron couldn't find its cache folder.",
            Host.NoCacheFolderFix,
            "Nothing was searched or sent."
        };

        internal static IReadOnlyList<string> NoCampgroundData(string Mode, MonitoringHostWords Host) => new[]
        {
            Mode + " can't start: Campertron couldn't read its campground data, so it can't prepare the searches' campsite details.",
            Host.NoCampgroundDataFix,
            "Nothing was searched or sent."
        };

        // A start that threw (a settings file or a search file refused, anything unexpected): the file's name with the
        // validation's value-free words, a file that is no valid YAML with its line, else the type's name. Never the
        // exception's message or a path
        internal static IReadOnlyList<string> StartFailed(Exception? Error, string Mode, MonitoringHostWords Host)
        {
            var Chain = new List<Exception>();
            for (Exception? Next = Error; Next != null; Next = Next.InnerException)
            {
                Chain.Add(Next);
            }
            string? FilePath = Chain.Select(E => E.Data.Contains(ConsoleUi.FileKey) ? E.Data[ConsoleUi.FileKey] as string : null).FirstOrDefault(P => !string.IsNullOrEmpty(P));
            string File = FilePath != null ? Path.GetFileName(FilePath) : "A settings file";
            string Problem;
            string Fix;
            if (Chain.OfType<ConfigValidationException>().FirstOrDefault() is ConfigValidationException Refused)
            {
                Problem = File + " can't be used: " + string.Join(" ", Refused.Diagnostics.Select(D => D.Message).Distinct());
                Fix = Host.RefusedFileFix;
            }
            else if (Chain.OfType<YamlDotNet.Core.YamlException>().FirstOrDefault() is YamlDotNet.Core.YamlException Yaml)
            {
                Problem = File + " can't be read: it is not valid YAML (line " + Yaml.Start.Line.ToString(CultureInfo.InvariantCulture) + ", column " + Yaml.Start.Column.ToString(CultureInfo.InvariantCulture) + ").";
                Fix = Host.InvalidYamlFix;
            }
            else if (FilePath != null)
            {
                Problem = File + " can't be read or used (" + (Error?.GetType().Name ?? "unknown") + ").";
                Fix = Host.UnreadableFileFix;
            }
            else
            {
                Problem = "an unexpected problem stopped the start (" + (Error?.GetType().Name ?? "unknown") + ").";
                Fix = "Select Start to try again.";
            }
            return new[] { Mode + " can't start: " + Problem, Fix, "Nothing was searched or sent." };
        }

        // ---- why a running monitoring stopped (D76's send-failure rule, D83 point 4) ----

        internal static IReadOnlyList<string> Failure(Exception Error, bool Blocked, EmailConfig? Email, MonitoringHostWords Host)
        {
            var Lines = new List<string>();
            if (Blocked)
            {
                Lines.Add("recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it.");
                Lines.Add(Host.BlockedFix);
                return Lines;
            }
            IReadOnlyList<string>? Notification = NotificationFailure(Error, Email);
            if (Notification != null)
            {
                Lines.AddRange(Notification);
                Lines.Add("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.");
                Lines.Add(Host.NotificationFix);
                return Lines;
            }
            Lines.Add("an unexpected problem stopped the monitoring (" + Unwrap(Error).GetType().Name + ").");
            Lines.Add("What a provider delivered before it is recorded in Camp.History; nothing else is.");
            Lines.Add("The monitoring does not start again by itself: select Start to start it again.");
            return Lines;
        }

        // a failed send of the check, by its provider: null when the failure was no send's
        private static IReadOnlyList<string>? NotificationFailure(Exception Error, EmailConfig? Email)
        {
            Exception Shown = Unwrap(Error);
            if (Shown is NotificationException Both)
            {
                var Lines = new List<string> { "the notifications could not be sent: both the email and the ntfy notification failed." };
                foreach ((NotificationProvider Provider, Exception Failure) in Both.Failures)
                {
                    IReadOnlyList<string> Own = NotificationFailure(Failure, Email) ?? new[] { "an unexpected problem stopped the send (" + Unwrap(Failure).GetType().Name + ")." };
                    Lines.Add((Provider == NotificationProvider.Email ? "Email: " : "ntfy: ") + Own[0]);
                }
                return Lines;
            }
            var Chain = new List<Exception>();
            for (Exception? Next = Error; Next != null; Next = Next.InnerException)
            {
                Chain.Add(Next);
            }
            if (Chain.OfType<NtfyException>().FirstOrDefault() is NtfyException Ntfy)
            {
                return NtfyPublisher.Describe(Ntfy.Failure, Ntfy.Status, Ntfy.TimeoutSeconds, Ntfy.ExceptionType);
            }
            if (Chain.OfType<NtfyTokenException>().FirstOrDefault() is NtfyTokenException Token)
            {
                List<string> Own = NtfyToken.Describe(Token.Result);
                return new[] { "the ntfy notification could not be sent: " + Own[0] }.Concat(Own.Skip(1)).ToList();
            }
            if (Chain.OfType<SmtpPasswordException>().FirstOrDefault() is SmtpPasswordException Password)
            {
                List<string> Own = SmtpPassword.Describe(Password.Result);
                return new[] { "the email could not be sent: " + Own[0] }.Concat(Own.Skip(1)).ToList();
            }
            if (Chain.Any(E => E is SmtpException))
            {
                return new[] { "the email could not be sent: " + TestNotification.DescribeSmtpFailure(Error), TestNotification.SmtpCheck(Error, Email) };
            }
            return null;
        }

        private static Exception Unwrap(Exception Error)
        {
            Exception Shown = Error;
            while ((Shown is AggregateException || Shown is System.Reflection.TargetInvocationException) && Shown.InnerException != null)
            {
                Shown = Shown.InnerException;
            }
            return Shown;
        }
    }
}
