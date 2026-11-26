using System.Net.Mail;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using MonitoringBusy = CampertronLibrary.function.Forms.MonitoringControlBusy;
using MonitoringCheck = CampertronLibrary.function.Forms.MonitoringControlCheck;
using MonitoringEnd = CampertronLibrary.function.Forms.MonitoringControlEnd;
using MonitoringOutcome = CampertronLibrary.function.Forms.MonitoringControlOutcome;
using MonitoringPhase = CampertronLibrary.function.Forms.MonitoringControlPhase;
using MonitoringStart = CampertronLibrary.function.Forms.MonitoringControlStart;
using MonitoringView = CampertronLibrary.function.Forms.MonitoringControlView;

namespace CampertronWebTests
{
    // Every word the web UI's monitoring page and its live answers show for the monitoring's states and decisions, exactly
    // (v0.4.0's phase 2: the monitoring's control and its words move into the library, and the web UI must say the same
    // words before and after): the fixed words, the state line of each state, why an action is unavailable, what an action
    // did, the last check's line and its searches' links, why a start did not start and why a running monitoring stopped,
    // each in full, and the moment a page sends back (its token) with what the page offers then.
    // The binding below names where each word comes from; everything after it holds only the expected words, so a move of
    // the words changes the binding and nothing else.
    public class MonitoringWordsContractTests
    {
        // ---- the binding: where the web UI's words come from ----

        private static Dictionary<string, string> FixedWords() => new()
        {
            ["Heading"] = MonitoringWording.Heading,
            ["Intro"] = WebMonitoringWording.Intro,
            ["LiveIntro"] = WebMonitoringWording.LiveIntro,
            ["LiveManual"] = WebMonitoringWording.LiveManual,
            ["LiveUpdating"] = WebMonitoringWording.LiveUpdating,
            ["LiveFailing"] = WebMonitoringWording.LiveFailing,
            ["LiveResumed"] = WebMonitoringWording.LiveResumed,
            ["LiveEndedNotShown"] = WebMonitoringWording.LiveEndedNotShown,
            ["LiveSignedOut"] = WebMonitoringWording.LiveSignedOut,
            ["LiveOutOfDate"] = WebMonitoringWording.LiveOutOfDate,
            ["Times"] = WebMonitoringWording.Times,
            ["StatusHeading"] = MonitoringWording.StatusHeading,
            ["ActionsHeading"] = MonitoringWording.ActionsHeading,
            ["ShowLatest"] = WebMonitoringWording.ShowLatest,
            ["StartLabel"] = MonitoringWording.StartLabel,
            ["StopLabel"] = MonitoringWording.StopLabel,
            ["RestartLabel"] = MonitoringWording.RestartLabel,
            ["StartHelp"] = MonitoringWording.StartHelp,
            ["StopHelp"] = WebMonitoringWording.StopHelp,
            ["RestartHelp"] = MonitoringWording.RestartHelp,
            ["StartUnavailableRunning"] = MonitoringWording.StartUnavailableRunning,
            ["StopUnavailableIdle"] = MonitoringWording.StopUnavailableIdle,
            ["UnavailableBusy"] = MonitoringWording.UnavailableBusy,
            ["UnavailableShuttingDown"] = WebMonitoringWording.UnavailableShuttingDown,
            ["RestartUnavailableCooldown"] = MonitoringWording.RestartUnavailableCooldown,
            ["FilesChanged"] = MonitoringWording.FilesChanged,
            ["LastCheckHeading"] = MonitoringWording.LastCheckHeading,
            ["NoCheckYet"] = MonitoringWording.NoCheckYet,
            ["LastCheckNote"] = WebMonitoringWording.LastCheckNote,
            ["NoNewOpening"] = MonitoringWording.NoNewOpening,
            ["ResultsOfName"] = MonitoringWording.ResultsOfName,
            ["NoNewOpeningShort"] = MonitoringWording.NoNewOpeningShort,
            ["StaleText"] = WebMonitoringWording.StaleText,
            ["BusyText"] = MonitoringWording.BusyText,
            ["AlreadyRunningText"] = MonitoringWording.AlreadyRunningText,
            ["NotRunningText"] = MonitoringWording.NotRunningText,
            ["ShuttingDownText"] = WebMonitoringWording.ShuttingDownText,
            ["UnavailablePaused"] = MonitoringWording.UnavailablePaused,
            ["PausedText"] = MonitoringWording.PausedText,
        };

        private static string StateText(MonitoringView V) => WebMonitoringWording.StateText(V);
        private static string Unavailable(MonitoringView V) => WebMonitoringWording.Unavailable(V);
        private static string? OutcomeText(MonitoringOutcome Outcome, string? Cooldown) => WebMonitoringWording.Outcome(Outcome, Cooldown);
        private static string CheckSummary(MonitoringCheck Check, bool Ended) => MonitoringWording.CheckSummary(Check, Ended);
        private static string SearchLabel(ResultSearchViewModel Search) => MonitoringWording.SearchLabel(Search);
        private static string UnnamedSearch(int Number) => MonitoringWording.UnnamedSearch(Number);
        private static string Mode(CampertronInternalConfig? Config) => MonitoringWording.Mode(Config);
        private static string When(DateTime At) => MonitoringWording.When(At);
        private static IReadOnlyList<string> EmailMissing(string Mode) => MonitoringWording.EmailMissing(Mode, WebMonitoringWording.Host);
        private static IReadOnlyList<string> NoCacheFolder(string Mode) => MonitoringWording.NoCacheFolder(Mode, WebMonitoringWording.Host);
        private static IReadOnlyList<string> NoCampgroundData(string Mode) => MonitoringWording.NoCampgroundData(Mode, WebMonitoringWording.Host);
        private static IReadOnlyList<string> StartFailed(Exception? Error, string Mode) => MonitoringWording.StartFailed(Error, Mode, WebMonitoringWording.Host);
        private static IReadOnlyList<string> Failure(Exception Error, bool Blocked) => MonitoringWording.Failure(Error, Blocked, null, WebMonitoringWording.Host);
        private static string LiveState(MonitoringView V) => WebMonitoringLive.State(V);
        private static MonitoringStart Start(long Generation, bool Started, bool WithHost) =>
            new MonitoringStart(Generation, Started, Started ? MonitoringStartProblem.None : MonitoringStartProblem.StartFailed, Array.Empty<string>(), At, WithHost, 1, null);

        // ---- the expected words (nothing below names where a word comes from) ----

        private static readonly DateTime At = new DateTime(2026, 11, 23, 14, 5, 0);
        private const string Stopping = "Stopping: a check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them first…";
        private const string Undelivered = "The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.";
        private const string NotificationFix = "The monitoring does not start again by itself: fix the problem (the settings pages send a test email and a test notification), then select Start.";
        private const string NothingDone = "Nothing was searched or sent.";

        private static MonitoringStatus Status(MonitoringState State, int Check = 0, bool StopRequested = false, DateTime? Next = null) =>
            new MonitoringStatus { State = State, CheckNumber = Check, StopRequested = StopRequested, NextCheckAt = Next };

        private static MonitoringView View(MonitoringStatus Status, MonitoringBusy Busy = MonitoringBusy.None, MonitoringStart? LastStart = null, MonitoringEnd? LastEnd = null,
            string? Cooldown = null, bool ShuttingDown = false, bool Paused = false, bool Resumes = false) => new MonitoringView
            {
                Generation = 2, Busy = Busy, Status = Status, LastStart = LastStart, LastEnd = LastEnd, Cooldown = Cooldown, ShuttingDown = ShuttingDown, Paused = Paused, Resumes = Resumes
            };

        private static MonitoringCheck Check(int Searches, int NewOpenings, int Emails = 0, int EmailsSent = 0, int Ntfy = 0, int NtfySent = 0, bool Delivered = false) =>
            new MonitoringCheck(2, 3, At, Searches, NewOpenings, Emails, Ntfy, EmailsSent, NtfySent, Delivered, Array.Empty<ResultSearchViewModel>());

        private static readonly MonitoringStatus Idle = Status(MonitoringState.NotStarted);
        private static readonly MonitoringStatus Stopped = Status(MonitoringState.Stopped);
        private static readonly MonitoringStatus Waiting = Status(MonitoringState.Waiting, 1);

        [Test]
        public void TheFixedWords_AreThePagesWords()
        {
            var Expected = new Dictionary<string, string>
            {
                ["Heading"] = "Monitoring",
                ["Intro"] = "The monitoring checks the Enabled (autoRun) search files every minute and notifies new openings by email or ntfy, as outputTo in General.yaml says (Email or Ntfy), exactly as CampertronConsole's email and ntfy mode do: openings it notified are recorded in Camp.History and notified again only after a check has seen them unavailable. It belongs to this web UI, not to a browser: it goes on when you sign out or close the page, until it is stopped, a check fails or web mode stops, and it starts by itself when web mode starts.",
                ["LiveIntro"] = "While the monitoring runs, this page shows its status as it goes, in a browser that runs the web UI's own script; Show the latest status shows the page as it is now.",
                ["LiveManual"] = "Select Show the latest status to see how the monitoring is going.",
                ["LiveUpdating"] = "This page updates by itself while the monitoring runs. Last update: {time}.",
                ["LiveFailing"] = "This page can't get the monitoring's status right now (last update: {time}). That does not stop the monitoring, and the page keeps trying. Show the latest status shows the page as it is now.",
                ["LiveResumed"] = "This page gets the monitoring's status again.",
                ["LiveEndedNotShown"] = "The monitoring is no longer running, but this page can't show how it ended right now, and it keeps trying. Show the latest status shows it.",
                ["LiveSignedOut"] = "This page stopped updating: this browser is no longer signed in. The monitoring belongs to this web UI and goes on; sign in again to see how it goes.",
                ["LiveOutOfDate"] = "This page stopped updating: it no longer shows the web UI's monitoring (the web UI started again). Show the latest status shows the page as it is now.",
                ["Times"] = "Times are those of the computer that runs Campertron.",
                ["StatusHeading"] = "Status",
                ["ActionsHeading"] = "Start and stop",
                ["ShowLatest"] = "Show the latest status",
                ["StartLabel"] = "Start",
                ["StopLabel"] = "Stop",
                ["RestartLabel"] = "Restart",
                ["StartHelp"] = "Start reads General.yaml, Email.yaml, Ntfy.yaml and the search files now and checks them as email and ntfy mode's start does; then the monitoring checks the Enabled (autoRun) searches every minute.",
                ["StopHelp"] = "Stop ends the monitoring until you start it again or web mode starts again: it saves nothing. A check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them in Camp.History first.",
                ["RestartHelp"] = "Restart stops the monitoring and starts it again, so that it reads the settings and the search files again.",
                ["StartUnavailableRunning"] = "Start is unavailable while the monitoring runs: stop or restart it instead.",
                ["StopUnavailableIdle"] = "Stop and Restart are available while the monitoring runs.",
                ["UnavailableBusy"] = "Start, Stop and Restart are unavailable while the monitoring is starting or stopping, and available again once it has.",
                ["UnavailableShuttingDown"] = "Start, Stop and Restart are unavailable: web mode is stopping.",
                ["RestartUnavailableCooldown"] = "Restart is unavailable until then, because it starts the monitoring again.",
                ["FilesChanged"] = "The search files changed since the monitoring started. It checks the searches it read when it started: select Restart to read them again.",
                ["LastCheckHeading"] = "The last check",
                ["NoCheckYet"] = "No check has finished since the monitoring started.",
                ["LastCheckNote"] = "These are the new openings of the monitoring's last check: the openings it found that are not in Camp.History, which it notifies. An opening it notified is not shown or notified again while it stays available; once a check has seen it unavailable, it is notified again when it becomes available again. A stay of several nights is shown whole when any of its nights is new. They are the monitoring's own: the results page shows only the searches you run there, and neither replaces the other. Availability may have changed since then: the booking page shows what is available now.",
                ["NoNewOpening"] = "No new opening for this search at this check.",
                ["ResultsOfName"] = "Searches of the last check",
                ["NoNewOpeningShort"] = "no new opening",
                ["StaleText"] = "This page was out of date (the monitoring started, stopped or ended, or the page was sent again), so nothing was started or stopped. The page shows the monitoring as it is now.",
                ["BusyText"] = "Nothing was done: the monitoring is starting or stopping. Try again once it has.",
                ["AlreadyRunningText"] = "Nothing was started: the monitoring is already running.",
                ["NotRunningText"] = "Nothing was stopped: the monitoring is not running.",
                ["ShuttingDownText"] = "Nothing was done: web mode is stopping.",
                ["UnavailablePaused"] = "Start, Stop and Restart are unavailable while the campground data is refreshed (Settings > System). They are available again when the refresh has finished.",
                ["PausedText"] = "Nothing was done: the campground data is being refreshed (Settings > System). The monitoring is paused until the refresh has finished.",
            };
            Assert.That(FixedWords(), Is.EqualTo(Expected));
        }

        [Test]
        public void EachState_IsSaidInItsOwnWords()
        {
            var Cases = new (string Name, MonitoringView View, string Expected)[]
            {
                ("starting", View(Idle, MonitoringBusy.Starting), "Starting: reading the settings and the search files and preparing the campsite details…"),
                ("restarting, still running", View(Waiting, MonitoringBusy.Restarting), Stopping),
                ("restarting, stopped", View(Stopped, MonitoringBusy.Restarting), "Restarting: starting the monitoring again…"),
                ("stopping", View(Waiting, MonitoringBusy.Stopping), Stopping),
                ("ending", View(Stopped, MonitoringBusy.Ending), Stopping),
                ("pausing", View(Waiting, MonitoringBusy.Pausing), "Pausing for the campground data's refresh: a check that is searching ends at its next step and sends nothing; a check that is sending finishes its notifications and records them first…"),
                ("paused, resumes", View(Stopped, Paused: true, Resumes: true), "Paused while the campground data is refreshed (Settings > System): the monitoring starts again by itself when the refresh has finished."),
                ("paused, does not resume", View(Idle, Paused: true), "Not running."),
                ("running, stop asked", View(Status(MonitoringState.Waiting, 1, true)), Stopping),
                ("sending", View(Status(MonitoringState.Sending, 3)), "Running: check 3 is sending its notifications."),
                ("waiting", View(Status(MonitoringState.Waiting, 3, Next: At.AddMinutes(1))), "Running: the next check starts at 2026-11-23 14:06."),
                ("waiting, no next check", View(Status(MonitoringState.Waiting, 2)), "Running: check 3 is searching."),
                ("the service starting", View(Status(MonitoringState.Starting)), "Starting…"),
                ("checking", View(Status(MonitoringState.Checking, 0)), "Running: check 1 is searching."),
                ("failed", View(Stopped, LastEnd: new MonitoringEnd(2, true, false, new[] { "x" }, At)), "Not running: it stopped at 2026-11-23 14:05 because a check failed. It does not start again by itself."),
                ("stopped", View(Stopped, LastEnd: new MonitoringEnd(2, false, false, Array.Empty<string>(), At)), "Not running: it was stopped at 2026-11-23 14:05. It does not start again by itself: select Start to start it again."),
                ("an earlier monitoring's end", View(Stopped, LastEnd: new MonitoringEnd(1, true, false, new[] { "x" }, At)), "Not running."),
                ("refused with the host", View(Idle, LastStart: Start(2, false, true)), "Not running: it did not start when web mode started."),
                ("refused on request", View(Idle, LastStart: Start(2, false, false)), "Not running: the last start did not start it."),
                ("an earlier refusal", View(Idle, LastStart: Start(1, false, true)), "Not running."),
                ("started, no end yet", View(Stopped, LastStart: Start(2, true, false)), "Not running."),
                ("never started", View(Idle), "Not running."),
            };
            foreach ((string Name, MonitoringView V, string Expected) in Cases)
            {
                Assert.That(StateText(V), Is.EqualTo(Expected), Name);
            }
        }

        [Test]
        public void WhyAnActionIsUnavailable_AndWhatAnActionDid_AreSaidInFull()
        {
            const string Running = "Start is unavailable while the monitoring runs: stop or restart it instead.";
            const string Busy = "Start, Stop and Restart are unavailable while the monitoring is starting or stopping, and available again once it has.";
            Assert.That(Unavailable(View(Waiting, ShuttingDown: true)), Is.EqualTo("Start, Stop and Restart are unavailable: web mode is stopping."));
            Assert.That(Unavailable(View(Waiting, Paused: true)), Is.EqualTo("Start, Stop and Restart are unavailable while the campground data is refreshed (Settings > System). They are available again when the refresh has finished."));
            Assert.That(Unavailable(View(Idle, MonitoringBusy.Starting)), Is.EqualTo(Busy));
            Assert.That(Unavailable(View(Status(MonitoringState.Waiting, 1, true))), Is.EqualTo(Busy));
            Assert.That(Unavailable(View(Waiting)), Is.EqualTo(Running));
            Assert.That(Unavailable(View(Waiting, Cooldown: "Start is available again in 4 minutes.")), Is.EqualTo(Running + " Restart is unavailable until then, because it starts the monitoring again."));
            Assert.That(Unavailable(View(Idle)), Is.EqualTo("Stop and Restart are available while the monitoring runs."));

            Assert.That(OutcomeText(MonitoringOutcome.Accepted, null), Is.Null);
            Assert.That(OutcomeText(MonitoringOutcome.Stale, null), Is.EqualTo("This page was out of date (the monitoring started, stopped or ended, or the page was sent again), so nothing was started or stopped. The page shows the monitoring as it is now."));
            Assert.That(OutcomeText(MonitoringOutcome.Busy, null), Is.EqualTo("Nothing was done: the monitoring is starting or stopping. Try again once it has."));
            Assert.That(OutcomeText(MonitoringOutcome.AlreadyRunning, null), Is.EqualTo("Nothing was started: the monitoring is already running."));
            Assert.That(OutcomeText(MonitoringOutcome.NotRunning, null), Is.EqualTo("Nothing was stopped: the monitoring is not running."));
            Assert.That(OutcomeText(MonitoringOutcome.CoolingDown, null), Is.EqualTo("Nothing was started: recreation.gov limited Campertron's requests a few minutes ago."));
            Assert.That(OutcomeText(MonitoringOutcome.CoolingDown, "Start is available again in 4 minutes."), Is.EqualTo("Nothing was started: Start is available again in 4 minutes."));
            Assert.That(OutcomeText(MonitoringOutcome.ShuttingDown, null), Is.EqualTo("Nothing was done: web mode is stopping."));
            Assert.That(OutcomeText(MonitoringOutcome.PausedForRefresh, null), Is.EqualTo("Nothing was done: the campground data is being refreshed (Settings > System). The monitoring is paused until the refresh has finished."));
        }

        [Test]
        public void TheLastCheck_AndItsSearches_AreSaidInFull()
        {
            const string Head = "Check 3 at 2026-11-23 14:05: ";
            Assert.That(CheckSummary(Check(2, 0), false), Is.EqualTo(Head + "2 searches, nothing new to notify."));
            Assert.That(CheckSummary(Check(2, 4, Emails: 1, EmailsSent: 1, Delivered: true), false), Is.EqualTo(Head + "2 searches, 4 new openings; 1 of 1 email sent."));
            Assert.That(CheckSummary(Check(1, 1, Emails: 2, EmailsSent: 1), false), Is.EqualTo(Head + "1 search, 1 new opening; 1 of 2 emails sent so far."));
            Assert.That(CheckSummary(Check(1, 1, Emails: 2, EmailsSent: 1), true), Is.EqualTo(Head + "1 search, 1 new opening; 1 of 2 emails sent."));
            Assert.That(CheckSummary(Check(2, 3, Emails: 1, Ntfy: 2, NtfySent: 1), false), Is.EqualTo(Head + "2 searches, 3 new openings; 0 of 1 email and 1 of 2 ntfy notifications sent so far."));
            Assert.That(CheckSummary(Check(1, 1, Ntfy: 1, NtfySent: 1, Delivered: true), false), Is.EqualTo(Head + "1 search, 1 new opening; 1 of 1 ntfy notification sent."));
            Assert.That(CheckSummary(Check(1, 2), false), Is.EqualTo(Head + "1 search, 2 new openings; nothing to send."));

            var Night = new DateTime(2026, 12, 4);
            var Openings = new List<SearchOpening> { new SearchOpening("101", "Site 101", "A", "STANDARD NONELECTRIC", Array.Empty<string>(), Night, null, null, null, null, "https://www.recreation.gov/camping/campsites/101") };
            ResultSearchViewModel With(string? Name, IReadOnlyList<SearchOpening> Found) => new ResultSearchViewModel(new SearchResult("Trip.yaml", Name, true, "990001", null, null, 1, SearchState.Complete, 0, 0,
                Array.Empty<SearchMonthResult>(), Found, CampsiteSearch.SitesOf(Found), Array.Empty<SearchIssue>(), Array.Empty<ConfigDiagnostic>()), false);
            Assert.That(SearchLabel(With("Trip", Array.Empty<SearchOpening>())), Is.EqualTo("Trip · no new opening"));
            Assert.That(SearchLabel(With("Trip", Openings)), Is.EqualTo("Trip · 1 opening at 1 campsite on 1 night"));
            Assert.That(UnnamedSearch(2), Is.EqualTo("Search 2"));
            Assert.That(When(At), Is.EqualTo("2026-11-23 14:05"));
            Assert.That(Mode(null), Is.EqualTo("the monitoring"));
            Assert.That(Mode(new CampertronInternalConfig()), Is.EqualTo("the monitoring"));
            Assert.That(Mode(new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.Email } }), Is.EqualTo("email mode"));
            Assert.That(Mode(new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.Ntfy } }), Is.EqualTo("ntfy mode"));
        }

        [Test]
        public void WhyAStartDidNotStart_IsSaidInFull()
        {
            Assert.That(EmailMissing("email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: Email.yaml does not exist yet.",
                "Enter the SMTP server, its port, the sender and at least one recipient in Settings > Email.yaml and save it, then select Start.",
                NothingDone
            }));
            Assert.That(NoCacheFolder("ntfy mode"), Is.EqualTo(new[] { "ntfy mode can't start: Campertron couldn't find its cache folder.", "Restart web mode.", NothingDone }));
            Assert.That(NoCampgroundData("email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: Campertron couldn't read its campground data, so it can't prepare the searches' campsite details.",
                "Restart web mode; if that does not help, check that the settings folder can be written.",
                NothingDone
            }));

            Exception Noted(Exception Error, string File)
            {
                ConsoleUi.NoteFile(Error, "/home/someone/.local/share/CampertronConfig/" + File);
                return Error;
            }
            var Diagnostic = new ConfigDiagnostic(ConfigCode.InvalidChoice, ConfigSeverity.Error, ConfigFileKind.Email, "Email.yaml", "smtpPort", null, 0, 0, "smtpPort must be a whole number from 1 to 65535.");
            Assert.That(StartFailed(Noted(new ConfigValidationException("detail", new[] { Diagnostic }), "Email.yaml"), "email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: Email.yaml can't be used: smtpPort must be a whole number from 1 to 65535.",
                "Fix it on its settings or searches page, then select Start.",
                NothingDone
            }));
            var Yaml = new YamlDotNet.Core.YamlException(new YamlDotNet.Core.Mark(10, 3, 7), new YamlDotNet.Core.Mark(12, 3, 9), "detail");
            Assert.That(StartFailed(Noted(new InvalidDataException("detail", Yaml), "Trip.yaml"), "ntfy mode"), Is.EqualTo(new[]
            {
                "ntfy mode can't start: Trip.yaml can't be read: it is not valid YAML (line 3, column 7).",
                "Fix it on its settings or searches page, or in a text editor, then select Start.",
                NothingDone
            }));
            Assert.That(StartFailed(Noted(new IOException("detail"), "General.yaml"), "email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: General.yaml can't be read or used (IOException).",
                "Check it on its settings or searches page, then select Start.",
                NothingDone
            }));
            Assert.That(StartFailed(new InvalidOperationException("detail"), "email mode"), Is.EqualTo(new[]
            {
                "email mode can't start: an unexpected problem stopped the start (InvalidOperationException).",
                "Select Start to try again.",
                NothingDone
            }));
            Assert.That(StartFailed(null, "the monitoring"), Is.EqualTo(new[]
            {
                "the monitoring can't start: an unexpected problem stopped the start (unknown).",
                "Select Start to try again.",
                NothingDone
            }));
        }

        [Test]
        public void WhyARunningMonitoringStopped_IsSaidInFull()
        {
            Assert.That(Failure(new MonitoringBlockedException("FIXTURE CAMPGROUND, December 2026"), true), Is.EqualTo(new[]
            {
                "recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it.",
                "Start is available again after five minutes; until then the results page waits too."
            }));
            Assert.That(Failure(new SmtpException((SmtpStatusCode)535, "detail"), false), Is.EqualTo(new[]
            {
                "the email could not be sent: the SMTP server refused the sign-in or wants a secure connection first (SMTP 535).",
                "Check the server, port, user name and password in Email.yaml.",
                Undelivered,
                NotificationFix
            }));
            Assert.That(Failure(new NtfyException(NtfyFailure.Unauthorized, 401), false), Is.EqualTo(new[]
            {
                "the ntfy notification could not be sent: the ntfy server wants an access token, or refused the one sent (HTTP 401).",
                "Check the token in tokenFile or CAMPERTRON_NTFY_TOKEN, and that it may publish to the topic.",
                Undelivered,
                NotificationFix
            }));
            var Both = new NotificationException(new[] { (NotificationProvider.Email, (Exception)new SmtpException(SmtpStatusCode.ServiceNotAvailable, "detail")),
                (NotificationProvider.Ntfy, (Exception)new NtfyException(NtfyFailure.ServerError, 503)) });
            Assert.That(Failure(Both, false), Is.EqualTo(new[]
            {
                "the notifications could not be sent: both the email and the ntfy notification failed.",
                "Email: the email could not be sent: the SMTP server did not take the email for now (SMTP 421).",
                "ntfy: the ntfy notification could not be sent: the ntfy server failed (HTTP 503).",
                Undelivered,
                NotificationFix
            }));
            Assert.That(Failure(new SmtpPasswordException(SmtpPasswordResult.Failed(SmtpPasswordSource.File, SmtpPasswordProblem.FileMissing)), false), Is.EqualTo(new[]
            {
                "the email could not be sent: the SMTP password can't be used: the file that smtpPasswordFile in Email.yaml names does not exist, or a folder on its path can't be read.",
                "Create the file, or mount it into the container (a read-only bind mount or a Compose secret), or correct smtpPasswordFile.",
                "Campertron never uses another password source when the one configured can't be used (the order is smtpPasswordFile, then CAMPERTRON_EMAIL_PASSWORD, then smtpPassword).",
                Undelivered,
                NotificationFix
            }));
            Assert.That(Failure(new NtfyTokenException(NtfyTokenResult.Failed(NtfyTokenSource.File, NtfyTokenProblem.FileMissing)), false), Is.EqualTo(new[]
            {
                "the ntfy notification could not be sent: the ntfy access token can't be used: the file that tokenFile in Ntfy.yaml names does not exist, or a folder on its path can't be read.",
                "Create the file, or mount it into the container (a read-only bind mount or a Compose secret), or correct tokenFile.",
                "Campertron never publishes without the token, or with another token source, when the one configured can't be used (the order is tokenFile, then CAMPERTRON_NTFY_TOKEN, then none).",
                Undelivered,
                NotificationFix
            }));
            Assert.That(Failure(new AggregateException(new InvalidOperationException("detail")), false), Is.EqualTo(new[]
            {
                "an unexpected problem stopped the monitoring (InvalidOperationException).",
                "What a provider delivered before it is recorded in Camp.History; nothing else is.",
                "The monitoring does not start again by itself: select Start to start it again."
            }));
        }

        [Test]
        public void TheMomentAPageSendsBack_AndWhatThePageOffersThen_AreTheMonitoringsState()
        {
            MonitoringView Running = View(Waiting);
            Assert.That((Running.Phase, Running.Token, Running.CanStart, Running.CanStop, Running.CanRestart, Running.StopAsked), Is.EqualTo((MonitoringPhase.Running, "2-running", false, true, true, false)));
            MonitoringView Held = View(Waiting, Cooldown: "Start is available again in 4 minutes.");
            Assert.That((Held.CanStop, Held.CanRestart), Is.EqualTo((true, false)));
            MonitoringView Asked = View(Status(MonitoringState.Waiting, 1, true));
            Assert.That((Asked.Phase, Asked.CanStop, Asked.CanRestart, Asked.StopAsked), Is.EqualTo((MonitoringPhase.Running, false, false, true)));
            MonitoringView Busy = View(Waiting, MonitoringBusy.Stopping);
            Assert.That((Busy.Phase, Busy.Token, Busy.CanStart, Busy.CanStop, Busy.StopAsked), Is.EqualTo((MonitoringPhase.Busy, "2-busy", false, false, true)));
            Assert.That(View(Waiting, MonitoringBusy.Restarting).StopAsked, Is.True);
            Assert.That(View(Waiting, MonitoringBusy.Starting).StopAsked, Is.False);
            MonitoringView Free = View(Idle);
            Assert.That((Free.Phase, Free.Token, Free.CanStart, Free.CanStop, Free.CanRestart), Is.EqualTo((MonitoringPhase.Idle, "2-idle", true, false, false)));
            Assert.That(View(Idle, Cooldown: "Start is available again in 4 minutes.").CanStart, Is.False);
            Assert.That(View(Idle, Paused: true).CanStart, Is.False);
            Assert.That(View(Idle, ShuttingDown: true).CanStart, Is.False);
            Assert.That((View(Waiting, Paused: true).CanStop, View(Waiting, ShuttingDown: true).CanStop), Is.EqualTo((false, false)));
            MonitoringStart Mine = Start(2, true, false);
            MonitoringEnd Ended = new MonitoringEnd(2, false, false, Array.Empty<string>(), At);
            MonitoringView Current = View(Stopped, LastStart: Mine, LastEnd: Ended);
            Assert.That((Current.ThisStart, Current.ThisEnd), Is.EqualTo((Mine, Ended)));
            MonitoringView Earlier = View(Stopped, LastStart: Start(1, true, false), LastEnd: new MonitoringEnd(1, false, false, Array.Empty<string>(), At));
            Assert.That((Earlier.ThisStart, Earlier.ThisEnd), Is.EqualTo(((MonitoringStart?)null, (MonitoringEnd?)null)));

            Assert.That(LiveState(View(Idle, MonitoringBusy.Starting)), Is.EqualTo("starting"));
            Assert.That(LiveState(View(Waiting, MonitoringBusy.Stopping)), Is.EqualTo("stopping"));
            Assert.That(LiveState(View(Waiting, MonitoringBusy.Restarting)), Is.EqualTo("restarting"));
            Assert.That(LiveState(View(Stopped, MonitoringBusy.Ending)), Is.EqualTo("ending"));
            Assert.That(LiveState(View(Waiting, MonitoringBusy.Pausing)), Is.EqualTo("pausing"));
            Assert.That(LiveState(View(Waiting)), Is.EqualTo("running"));
            Assert.That(LiveState(View(Status(MonitoringState.Waiting, 1, true))), Is.EqualTo("stopping"));
            Assert.That(LiveState(View(Stopped, Paused: true, Resumes: true)), Is.EqualTo("paused"));
            Assert.That(LiveState(View(Stopped)), Is.EqualTo("ended"));
            Assert.That(LiveState(View(Waiting, ShuttingDown: true)), Is.EqualTo("ended"));
        }
    }
}
