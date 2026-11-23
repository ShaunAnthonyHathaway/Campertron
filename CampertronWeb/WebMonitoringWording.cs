using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronWeb
{
    // The monitoring page's words (v0.3.0's PR 8, D98; business-rules-settings §35). The monitoring's control is web-only
    // (the Full UI rule's exception, D84), so these words are the web UI's; a start's refusal is said in the monitoring
    // start's own words (D83 point 2: MonitoringStartResult.Lines, which hold no value, secret or path), and a failure in
    // fixed words of its kind: the test notification's words for a failed email (TestNotification.DescribeSmtpFailure:
    // the failure's kind and the SMTP status number, never the server's text), ntfy's own (NtfyPublisher.Describe), the
    // secrets' sources' (SmtpPassword.Describe, NtfyToken.Describe), a block, or an unexpected problem by its type's name.
    // Never an exception's message, a server's answer, a recipient, a topic, a password, a token, an address or a path.
    internal static class WebMonitoringWording
    {
        internal const string Heading = "Monitoring";
        internal const string Intro = "The monitoring checks the Enabled (autoRun) search files every minute and notifies new openings by email or ntfy, as outputTo in General.yaml says (Email or Ntfy), exactly as CampertronConsole's email and ntfy mode do: openings it notified are recorded in Camp.History and not notified again. It belongs to this web UI, not to a browser: it goes on when you sign out or close the page, until it is stopped, a check fails or web mode stops, and it starts by itself when web mode starts.";
        internal const string NotUpdating = "This page does not update by itself: select Show the latest status to see how the monitoring is going.";
        internal const string Times = "Times are those of the computer that runs Campertron.";
        internal const string StatusHeading = "Status";
        internal const string ActionsHeading = "Start and stop";
        internal const string ShowLatest = "Show the latest status";
        internal const string StartLabel = "Start";
        internal const string StopLabel = "Stop";
        internal const string RestartLabel = "Restart";
        internal const string StartHelp = "Start reads General.yaml, Email.yaml, Ntfy.yaml and the search files now and checks them as email and ntfy mode's start does; then the monitoring checks the Enabled (autoRun) searches every minute.";
        internal const string StopHelp = "Stop ends the monitoring until you start it again or web mode starts again: it saves nothing. A check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them in Camp.History first.";
        internal const string RestartHelp = "Restart stops the monitoring and starts it again, so that it reads the settings and the search files again.";
        internal const string StartUnavailableRunning = "Start is unavailable while the monitoring runs: stop or restart it instead.";
        internal const string StopUnavailableIdle = "Stop and Restart are available while the monitoring runs.";
        internal const string UnavailableBusy = "Start, Stop and Restart are unavailable while the monitoring is starting or stopping. Show the latest status to see when it has.";
        internal const string UnavailableShuttingDown = "Start, Stop and Restart are unavailable: web mode is stopping.";
        internal const string RestartUnavailableCooldown = "Restart is unavailable until then, because it starts the monitoring again.";
        internal const string FilesChanged = "The search files changed since the monitoring started. It checks the searches it read when it started: select Restart to read them again.";
        internal const string LastCheckHeading = "The last check";
        internal const string NoCheckYet = "No check has finished since the monitoring started.";
        internal const string LastCheckNote = "These are the new openings of the monitoring's last check: the openings it found that are not in Camp.History yet, which it notifies. An opening it notified before is not shown again, as it is not notified again. They are the monitoring's own: the results page shows only the searches you run there, and neither replaces the other. Availability may have changed since then: the booking page shows what is available now.";
        internal const string NoNewOpening = "No new opening for this search at this check.";
        internal const string ResultsOfName = "Searches of the last check";
        internal const string NoNewOpeningShort = "no new opening";
        internal const string StaleText = "This page was out of date (the monitoring started, stopped or ended, or the page was sent again), so nothing was started or stopped. The page shows the monitoring as it is now.";
        internal const string BusyText = "Nothing was done: the monitoring is starting or stopping. Show the latest status, then try again.";
        internal const string AlreadyRunningText = "Nothing was started: the monitoring is already running.";
        internal const string NotRunningText = "Nothing was stopped: the monitoring is not running.";
        internal const string ShuttingDownText = "Nothing was done: web mode is stopping.";

        // the mode's name in the start's words ("email mode", "ntfy mode"), or "the monitoring" before General.yaml was read
        internal static string Mode(CampertronInternalConfig? Config) => Config?.GeneralConfig == null ? "the monitoring" : Notifications.ModeName(Config);

        internal static string When(DateTime At) => At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        // ---- the state, in words (never by color alone) ----

        internal static string StateText(WebMonitoringView View)
        {
            MonitoringStatus Status = View.Status;
            switch (View.Busy)
            {
                case WebMonitoringBusy.Starting:
                    return "Starting: reading the settings and the search files and preparing the campsite details…";
                case WebMonitoringBusy.Restarting:
                    return Status.IsRunning ? Stopping : "Restarting: starting the monitoring again…";
                case WebMonitoringBusy.Stopping:
                case WebMonitoringBusy.Ending:
                    return Stopping;
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
            if (View.ThisEnd is WebMonitoringEnd End)
            {
                return End.Failed ? "Not running: it stopped at " + When(End.At) + " because a check failed. It does not start again by itself."
                    : "Not running: it was stopped at " + When(End.At) + ". It does not start again by itself: select Start to start it again.";
            }
            if (View.ThisStart is WebMonitoringStart Start && !Start.Started)
            {
                return "Not running: " + (Start.WithWebMode ? "it did not start when web mode started." : "the last start did not start it.");
            }
            return "Not running.";
        }

        private const string Stopping = "Stopping: a check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them first…";

        // what a page's action did, when it did not do what was asked
        internal static string? Outcome(WebMonitoringOutcome Outcome, string? Cooldown) => Outcome switch
        {
            WebMonitoringOutcome.Stale => StaleText,
            WebMonitoringOutcome.Busy => BusyText,
            WebMonitoringOutcome.AlreadyRunning => AlreadyRunningText,
            WebMonitoringOutcome.NotRunning => NotRunningText,
            WebMonitoringOutcome.CoolingDown => "Nothing was started: " + (Cooldown ?? "recreation.gov limited Campertron's requests a few minutes ago."),
            WebMonitoringOutcome.ShuttingDown => ShuttingDownText,
            _ => null
        };

        // the last check in a line: "Check 3 at 2026-11-23 14:05: 2 searches, 4 new openings; 1 of 1 email sent." ("… sent so
        // far." while its monitoring still runs and its deliveries have not all gone out)
        internal static string CheckSummary(WebMonitoringCheck Check, bool Ended)
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

        // a search of the last check, as its link names it
        internal static string SearchLabel(ResultSearchViewModel Search) =>
            Search.Title + " · " + (Search.Openings > 0 ? Search.CountsText : NoNewOpeningShort);

        internal static string UnnamedSearch(int Number) => "Search " + Number.ToString(CultureInfo.InvariantCulture);

        // ---- why a start did not start the monitoring ----

        internal static IReadOnlyList<string> EmailMissing(string Mode) => new[]
        {
            Mode + " can't start: " + ConfigFiles.EmailFileName + " does not exist yet.",
            "Enter the SMTP server, its port, the sender and at least one recipient in Settings > " + ConfigFiles.EmailFileName + " and save it, then select Start.",
            "Nothing was searched or sent."
        };

        internal static IReadOnlyList<string> NoCacheFolder(string Mode) => new[]
        {
            Mode + " can't start: Campertron couldn't find its cache folder.",
            "Restart web mode.",
            "Nothing was searched or sent."
        };

        internal static IReadOnlyList<string> NoCampgroundData(string Mode) => new[]
        {
            Mode + " can't start: Campertron couldn't read its campground data, so it can't prepare the searches' campsite details.",
            "Restart web mode; if that does not help, check that the settings folder can be written.",
            "Nothing was searched or sent."
        };

        // A start that threw (a settings file or a search file refused, anything unexpected): the file's name with the
        // validation's value-free words, a file that is no valid YAML with its line, else the type's name. Never the
        // exception's message or a path
        internal static IReadOnlyList<string> StartFailed(Exception? Error, string Mode)
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
                Fix = "Fix it on its settings or searches page, then select Start.";
            }
            else if (Chain.OfType<YamlDotNet.Core.YamlException>().FirstOrDefault() is YamlDotNet.Core.YamlException Yaml)
            {
                Problem = File + " can't be read: it is not valid YAML (line " + Yaml.Start.Line.ToString(CultureInfo.InvariantCulture) + ", column " + Yaml.Start.Column.ToString(CultureInfo.InvariantCulture) + ").";
                Fix = "Fix it on its settings or searches page, or in a text editor, then select Start.";
            }
            else if (FilePath != null)
            {
                Problem = File + " can't be read or used (" + (Error?.GetType().Name ?? "unknown") + ").";
                Fix = "Check it on its settings or searches page, then select Start.";
            }
            else
            {
                Problem = "an unexpected problem stopped the start (" + (Error?.GetType().Name ?? "unknown") + ").";
                Fix = "Select Start to try again.";
            }
            return new[] { Mode + " can't start: " + Problem, Fix, "Nothing was searched or sent." };
        }

        // ---- why a running monitoring stopped (D76's send-failure rule, D83 point 4) ----

        internal static IReadOnlyList<string> Failure(Exception Error, bool Blocked, EmailConfig? Email)
        {
            var Lines = new List<string>();
            if (Blocked)
            {
                Lines.Add("recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it.");
                Lines.Add("Start is available again after five minutes; until then the results page waits too.");
                return Lines;
            }
            IReadOnlyList<string>? Notification = NotificationFailure(Error, Email);
            if (Notification != null)
            {
                Lines.AddRange(Notification);
                Lines.Add("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.");
                Lines.Add("The monitoring does not start again by itself: fix the problem (the settings pages send a test email and a test notification), then select Start.");
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
