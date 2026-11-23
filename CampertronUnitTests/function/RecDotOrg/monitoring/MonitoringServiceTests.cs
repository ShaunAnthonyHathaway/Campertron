using System.Net.Mail;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // The host-neutral monitoring service (v0.3.0, decisions D83 and D90; business-rules.md §29): its start and what
    // refuses it, its checks and the one-minute wait, Stop (the wait ends at once, a check that searches is abandoned, a
    // check that sends finishes its notifications and records), Restart, the status, a failed send (the monitoring ends as
    // failed, what was not delivered is not recorded), the any-provider rule, a blocked request, and that two services
    // share nothing. The console's adapter is tested by MonitoringConsoleTests and, unchanged, by the email and ntfy mode
    // suites (EmailStartTests, NtfyStartTests, SmtpPasswordEmailModeTests and the others).
    public class MonitoringServiceTests : MonitoringTestBase
    {
        // ---- start, the first check, the wait and the status ----

        [Test]
        public void Start_RunsTheFirstCheck_ThenWaitsAMinute_AndTheStatusSaysSo()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService();
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.NotStarted));
            Assert.That(File.Exists(LockPath), Is.False, "no lock file before the first monitoring");

            MonitoringStartResult Started = Run.Service.Start(Settings());

            Assert.That((Started.Started, Started.Problem, Started.Lines.Count, Started.Error), Is.EqualTo((true, MonitoringStartProblem.None, 0, (Exception?)null)));
            Run.AwaitWaits(1);
            MonitoringStatus Status = Run.Service.GetStatus();
            Assert.That(Status.State, Is.EqualTo(MonitoringState.Waiting));
            Assert.That((Status.IsRunning, Status.StopRequested, Status.Searches, Status.CheckNumber), Is.EqualTo((true, false, 1, 1)));
            Assert.That(Status.StartedAt, Is.Not.Null);
            Assert.That(Status.LastCheckAt, Is.Not.Null);
            Assert.That(Status.LastCheckAt!.Value, Is.GreaterThanOrEqualTo(Status.StartedAt!.Value));
            Assert.That(Status.NextCheckAt, Is.EqualTo(Status.LastCheckAt + TimeSpan.FromMinutes(1)), "the next check is a minute after the last");
            Assert.That(Status.LastError, Is.Null);
            Assert.That(Status.SearchFilesChangedSinceStart, Is.False);
            Assert.That(Status.LastStart, Is.SameAs(Started));
            Assert.That(Run.Delays, Is.EqualTo(new[] { TimeSpan.FromMinutes(1) }), "checks are one minute apart");
            Assert.That(MonitoringService.CheckInterval, Is.EqualTo(TimeSpan.FromMinutes(1)));

            Assert.That(Run.Kinds, Is.EqualTo(new[] { MonitoringEventKind.Starting, MonitoringEventKind.SearchesLoaded, MonitoringEventKind.CheckReported }));
            Assert.That(Run.Of(MonitoringEventKind.SearchesLoaded).Single().Searches!.Select(S => S.DisplayName), Is.EqualTo(new[] { "Trip" }));
            MonitoringEvent Check = Run.Of(MonitoringEventKind.CheckReported).Single();
            Assert.That((Check.CheckNumber, Check.SearchCount, Check.NewOpenings), Is.EqualTo((1, 1, 0)), "nothing new");
            Assert.That(Requested, Is.Not.Empty.And.All.Contains("/campground/" + Campground + "/month"));
            Assert.That((Emails.Count, Published.Count, Markers().Count), Is.EqualTo((0, 0, 0)));
            Assert.That(LockIsHeld(), Is.True, "the monitoring holds the settings folder's lock while it runs");

            MonitoringStatus Stopped = Run.Service.Stop();
            Assert.That((Stopped.State, Stopped.IsRunning, Stopped.StopRequested, Stopped.NextCheckAt, Stopped.LastError), Is.EqualTo((MonitoringState.Stopped, false, false, (DateTime?)null, (MonitoringFailure?)null)));
            Assert.That(Stopped.CheckNumber, Is.EqualTo(1), "the status keeps what the run did");
            Assert.That(LockIsHeld(), Is.False, "the lock is released");
            Assert.That(File.Exists(LockPath), Is.True, "its file stays, empty");
            Assert.That(new FileInfo(LockPath).Length, Is.EqualTo(0));
            AssertNothingOnTheConsole();
        }

        [Test]
        public void EveryCheck_IsNumbered_AndTheChecksGoOnUntilStopped()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService(PassWaits: 2);
            Assert.That(Run.Service.Start(Settings()).Started, Is.True);
            Run.AwaitWaits(3);

            Assert.That(Run.Of(MonitoringEventKind.CheckReported).Select(E => E.CheckNumber), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Run.Service.GetStatus().CheckNumber, Is.EqualTo(3));
            Assert.That(Run.Delays, Has.All.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(Run.Of(MonitoringEventKind.SearchesLoaded), Has.Count.EqualTo(1), "the search files are read once, at the start");
            Assert.That(Run.Service.Stop().State, Is.EqualTo(MonitoringState.Stopped));
        }

        [Test]
        public void ACheckWithOpenings_SendsRecordsAndReportsThem()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Recorder Run = NewService();
            Assert.That(Run.Service.Start(Settings()).Started, Is.True);
            Run.AwaitWaits(1);

            MonitoringEvent Check = Run.Of(MonitoringEventKind.CheckReported).Single();
            Assert.That(Check.NewOpenings, Is.GreaterThan(0));
            Assert.That((Check.Emails, Check.NtfyMessages), Is.EqualTo((1, 0)));
            Assert.That(Emails, Is.EqualTo(new[] { "Trip" }));
            MonitoringEvent Sent = Run.Of(MonitoringEventKind.EmailSent).Single();
            Assert.That((Sent.SearchName, Sent.Openings), Is.EqualTo(("Trip", Check.NewOpenings)));
            Assert.That(Run.Kinds, Is.EqualTo(new[] { MonitoringEventKind.Starting, MonitoringEventKind.SearchesLoaded, MonitoringEventKind.CheckReported,
                MonitoringEventKind.EmailSent, MonitoringEventKind.DeliveriesFinished }));
            Assert.That(Markers(), Has.Count.EqualTo(Check.NewOpenings), "every notified opening has its marker in Camp.History");
            Assert.That(Markers(), Has.All.EndsWith("/" + Sites(Campground)[0]));
            Run.Service.Stop();
            AssertNothingOnTheConsole();
        }

        [Test]
        public void TheNextCheck_SendsNothingAgain()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Recorder Run = NewService(PassWaits: 1);
            Run.Service.Start(Settings());
            Run.AwaitWaits(2);

            List<MonitoringEvent> Checks = Run.Of(MonitoringEventKind.CheckReported);
            Assert.That(Checks.Select(E => E.CheckNumber), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Checks[0].NewOpenings, Is.GreaterThan(0));
            Assert.That(Checks[1].NewOpenings, Is.EqualTo(0), "the openings are in Camp.History");
            Assert.That(Emails, Has.Count.EqualTo(1), "no duplicate");
            Run.Service.Stop();
        }

        // ---- stop ----

        [Test]
        public void Stop_EndsTheServicesOwnOneMinuteWaitAtOnce()
        {
            WriteSearch("Trip.yaml", "Trip");
            var Reported = new ManualResetEventSlim(false);
            Recorder Run = NewService(DefaultWait: true, OnEvent: Event =>
            {
                if (Event.Kind == MonitoringEventKind.CheckReported)
                {
                    Reported.Set();
                }
            });
            Run.Service.Start(Settings());
            Assert.That(Reported.Wait(Long), Is.True);

            var Clock = System.Diagnostics.Stopwatch.StartNew();
            MonitoringStatus Stopped = Run.Service.Stop();
            Clock.Stop();

            Assert.That(Stopped.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "not the minute between two checks");
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Has.Count.EqualTo(1), "no second check");
            Assert.That(LockIsHeld(), Is.False);
        }

        // cancel mid-check: a check that is still searching is abandoned, even when its searches find openings
        [Test]
        public void Stop_WhileACheckSearches_AbandonsTheCheck_NothingIsSentOrRecorded()
        {
            WriteSearch("Trip.yaml", "Trip");
            var InRequest = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            Answer = Url =>
            {
                InRequest.Set();
                Release.Wait(Long);
                return Openings(Url);
            };
            Recorder Run = NewService();
            Run.Service.Start(Settings());
            Assert.That(InRequest.Wait(Long), Is.True, "the check is asking recreation.gov");
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.Checking));

            Task<MonitoringStatus> Stopping = Task.Run(() => Run.Service.Stop());
            Assert.That(SpinWait.SpinUntil(() => Run.Service.GetStatus().StopRequested, Long), Is.True);
            Assert.That(Stopping.IsCompleted, Is.False, "the request in flight is not interrupted");
            Release.Set();

            Assert.That(Stopping.Wait(Long), Is.True);
            Assert.That(Stopping.Result.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Stopping.Result.LastError, Is.Null, "a stop is not a failure");
            Assert.That(Stopping.Result.CheckNumber, Is.EqualTo(0), "the abandoned check was never reported");
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty);
            Assert.That((Emails.Count, Published.Count, Markers().Count), Is.EqualTo((0, 0, 0)), "nothing sent, nothing recorded: the openings come again");
            Assert.That(Directory.Exists(HistoryPath), Is.False);
            Assert.That(LockIsHeld(), Is.False);
        }

        // the console's search waits 30 seconds before it asks again, without limit: a stop ends that wait
        [Test]
        public void Stop_EndsTheWaitBeforeARetryAtOnce()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Url => throw new HttpRequestException("the test's recreation.gov is down");
            var Warned = new ManualResetEventSlim(false);
            Recorder Run = NewService(OnEvent: Event =>
            {
                if (Event.Kind == MonitoringEventKind.Warning)
                {
                    Warned.Set();
                }
            });
            Run.Service.Start(Settings());
            Assert.That(Warned.Wait(Long), Is.True);

            var Clock = System.Diagnostics.Stopwatch.StartNew();
            MonitoringStatus Stopped = Run.Service.Stop();
            Clock.Stop();

            Assert.That(Stopped.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(25)), "not the 30 seconds before the next attempt");
            Assert.That(Run.Of(MonitoringEventKind.Warning).First().Text, Does.StartWith("the request to recreation.gov for FIXTURE CAMPGROUND, ")
                .And.Contain("failed (attempt 1: the test's recreation.gov is down); retrying in 30 seconds."), "the console's warning, as an event");
            Assert.That(Requested, Has.Count.EqualTo(1), "no further attempt");
            AssertNothingOnTheConsole();
        }

        [Test]
        public void Stop_WhileACheckSends_WaitsForEveryNotificationAndItsRecords()
        {
            WriteSearch("A.yaml", "Alpha");
            WriteSearch("B.yaml", "Bravo", Id: OtherCampground);
            Answer = Openings;
            var InSend = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                if (Name == "Alpha")
                {
                    InSend.Set();
                    Release.Wait(Long);
                }
            };
            Recorder Run = NewService();
            Run.Service.Start(Settings(Ntfy: true));
            Assert.That(InSend.Wait(Long), Is.True, "the first email is being sent");
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.Sending));

            Task<MonitoringStatus> Stopping = Task.Run(() => Run.Service.Stop());
            Assert.That(SpinWait.SpinUntil(() => Run.Service.GetStatus().StopRequested, Long), Is.True);
            Assert.That(Stopping.Wait(TimeSpan.FromMilliseconds(400)), Is.False, "Stop waits while the check sends");
            MonitoringStatus Meanwhile = Run.Service.GetStatus();
            Assert.That((Meanwhile.State, Meanwhile.IsRunning, Meanwhile.StopRequested), Is.EqualTo((MonitoringState.Sending, true, true)));
            Assert.That(LockIsHeld(), Is.True, "the lock is held until the check has finished");
            Release.Set();

            Assert.That(Stopping.Wait(Long), Is.True);
            Assert.That(Stopping.Result.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Emails, Is.EqualTo(new[] { "Alpha", "Bravo" }), "the email in flight and the one after it");
            Assert.That(Published, Is.EqualTo(new[] { "Alpha", "Bravo" }), "and every ntfy notification");
            MonitoringEvent Check = Run.Of(MonitoringEventKind.CheckReported).Single();
            Assert.That(Markers(), Has.Count.EqualTo(Check.NewOpenings), "every opening of the check is recorded");
            Assert.That(Run.Kinds.Last(), Is.EqualTo(MonitoringEventKind.DeliveriesFinished));
            Assert.That(Stopping.Result.CheckNumber, Is.EqualTo(1));
            Assert.That(Stopping.Result.LastCheckAt, Is.Not.Null, "the check finished");
            Assert.That(Run.Delays, Is.Empty, "and no wait for a next check followed");
            Assert.That(LockIsHeld(), Is.False);
        }

        [Test]
        public void TheHostsToken_StopsTheMonitoring_AsStopDoes()
        {
            WriteSearch("Trip.yaml", "Trip");
            using var Host = new CancellationTokenSource();
            Recorder Run = NewService();
            Run.Service.Start(Settings(), Host.Token);
            Run.AwaitWaits(1);

            Host.Cancel();

            MonitoringStatus Status = Ended(Run.Service);
            Assert.That((Status.State, Status.LastError), Is.EqualTo((MonitoringState.Stopped, (MonitoringFailure?)null)));
            Assert.That(LockIsHeld(), Is.False);
        }

        [Test]
        public void Stop_WithNothingRunning_ReturnsTheStatus()
        {
            Recorder Run = NewService();
            Assert.That(Run.Service.Stop().State, Is.EqualTo(MonitoringState.NotStarted));
            Assert.That(Run.Service.WaitForEnd().State, Is.EqualTo(MonitoringState.NotStarted));
            WriteSearch("Trip.yaml", "Trip");
            Run.Service.Start(Settings());
            Run.AwaitWaits(1);
            Run.Service.Stop();
            Assert.That(Run.Service.Stop().State, Is.EqualTo(MonitoringState.Stopped), "a second stop changes nothing");
        }

        // a host's event handler may ask for the stop itself: the run ends when the handler returns
        [Test]
        public void Stop_FromAnEvent_AsksAndDoesNotWaitForItself()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder? Run = null;
            Run = NewService(OnEvent: Event =>
            {
                if (Event.Kind == MonitoringEventKind.CheckReported)
                {
                    Run!.Service.Stop();
                }
            });
            Run.Service.Start(Settings());

            MonitoringStatus Status = Ended(Run.Service);
            Assert.That(Status.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Run.Delays, Is.Empty, "no wait for a next check");
        }

        // ---- restart, and the search files since the start ----

        [Test]
        public void Restart_ReadsTheSearchFilesAgain_AndCountsItsChecksFromOne()
        {
            string Trip = WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService(PassWaits: 1);
            Run.Service.Start(Settings());
            Run.AwaitWaits(2);
            Assert.That(Run.Service.GetStatus().CheckNumber, Is.EqualTo(2));
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.False);

            WriteSearch("Other.yaml", "Other", Id: OtherCampground);
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.True, "a search file was added");
            Assert.That(Run.Service.GetStatus().Searches, Is.EqualTo(1), "the run still checks what it read at its start");
            int RequestsBefore = Requested.Count;

            Run.PassWaits = 2;
            MonitoringStartResult Restarted = Run.Service.Restart(Settings());

            Assert.That(Restarted.Started, Is.True);
            Run.AwaitWaits(1);
            MonitoringStatus Status = Run.Service.GetStatus();
            Assert.That((Status.State, Status.Searches, Status.CheckNumber, Status.SearchFilesChangedSinceStart), Is.EqualTo((MonitoringState.Waiting, 2, 1, false)));
            Assert.That(Run.Of(MonitoringEventKind.SearchesLoaded).Select(E => E.Searches!.Count), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(Run.Of(MonitoringEventKind.CheckReported).Select(E => (E.CheckNumber, E.SearchCount)), Is.EqualTo(new[] { (1, 1), (2, 1), (1, 2) }));
            Assert.That(Requested.Skip(RequestsBefore).Any(Url => Url.Contains("/campground/" + OtherCampground + "/")), Is.True, "the new search is checked");

            // a file rewritten with another size, and a file removed, are changes too; the files' contents are never read
            File.AppendAllText(Trip, "# a comment\n");
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.True, "a search file was changed");
            Run.Service.Restart(Settings());
            Run.AwaitWaits(1);
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.False);
            File.Delete(Path.Join(ConfigPath, "Other.yaml"));
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.True, "a search file was removed");
            // a file that is not a search file is not one of them
            Run.Service.Restart(Settings());
            Run.AwaitWaits(1);
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: Email\n");
            File.WriteAllText(Path.Join(ConfigPath, "notes.txt"), "not a search file");
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.False);
            Run.Service.Stop();
            Assert.That(Run.Service.GetStatus().SearchFilesChangedSinceStart, Is.False, "the stopped run's files are still compared");
        }

        // ---- what refuses a start ----

        private void AssertRefused(Recorder Run, MonitoringStartResult Result, MonitoringStartProblem Problem, bool LockFile = false)
        {
            Assert.That((Result.Started, Result.Problem), Is.EqualTo((false, Problem)));
            MonitoringStatus Status = Run.Service.GetStatus();
            Assert.That((Status.State, Status.IsRunning, Status.CheckNumber), Is.EqualTo((MonitoringState.NotStarted, false, 0)));
            Assert.That(Status.LastStart, Is.SameAs(Result), "the status keeps why");
            Assert.That(Requested, Is.Empty, "nothing asked of recreation.gov");
            Assert.That((Emails.Count, Published.Count, Markers().Count, Run.Delays.Count), Is.EqualTo((0, 0, 0, 0)));
            Assert.That(File.Exists(LockPath), Is.EqualTo(LockFile), "a start that is refused creates no lock file");
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty);
            Assert.That(string.Join("\n", Result.Lines), Does.Not.Contain(Password).And.Not.Contain(Topic));
            if (Problem != MonitoringStartProblem.StartFailed)
            {
                Assert.That(string.Join("\n", Result.Lines), Does.Not.Contain(Root), "no path (a start that threw names its file, as the console's report does)");
            }
            AssertNothingOnTheConsole();
        }

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.UnitTest)]
        public void Start_IsRefused_WhenOutputToIsNotAMonitoring(OutputType Output)
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService();
            MonitoringStartResult Result = Run.Service.Start(Settings(Output));
            AssertRefused(Run, Result, MonitoringStartProblem.NotMonitoring);
            Assert.That(Result.Lines, Is.EqualTo(new[] { "the monitoring is not turned on: outputTo in General.yaml is not Email or Ntfy." }));
            Assert.That(Run.Events, Is.Empty);
        }

        [Test]
        public void Start_IsRefused_WithTheStartsException_WhenEmailYamlCannotSend()
        {
            WriteSearch("Trip.yaml", "Trip");
            CampertronInternalConfig Incomplete = Settings();
            Incomplete.EmailConfig = new EmailConfig();
            Recorder Run = NewService();

            MonitoringStartResult Result = Run.Service.Start(Incomplete);

            AssertRefused(Run, Result, MonitoringStartProblem.StartFailed);
            Assert.That(Result.Error, Is.InstanceOf<ConfigValidationException>(), "the exception the console's start threw, for the console to rethrow");
            Assert.That(Result.Lines[0], Does.StartWith("Email.yaml could not be used: "));
            Assert.That(Result.Lines[1], Is.EqualTo("File: " + Path.Join(ConfigPath, "Email.yaml")), "the console's report of it, which names the file");
            Assert.That(Run.Events, Is.Empty, "refused before the start announced itself");
        }

        [Test]
        public void Start_IsRefused_WhenTheSmtpPasswordsSourceCannotBeUsed()
        {
            WriteSearch("Trip.yaml", "Trip");
            CampertronInternalConfig Config = Settings();
            Config.EmailConfig = Yaml.LoadSettingsText<EmailConfig>("smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: tester\nsmtpPasswordFile: " +
                Path.Join(Root, "no-such-password-file") + "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n");
            Recorder Run = NewService();

            MonitoringStartResult Result = Run.Service.Start(Config);

            AssertRefused(Run, Result, MonitoringStartProblem.SmtpPassword);
            Assert.That(Result.Error, Is.Null);
            Assert.That(Result.Lines[0], Is.EqualTo("email mode can't start: the SMTP password can't be used: the file that smtpPasswordFile in Email.yaml names does not exist, or a folder on its path can't be read."));
            Assert.That(Result.Lines, Has.Count.EqualTo(3));
            Assert.That(Run.Kinds, Is.EqualTo(new[] { MonitoringEventKind.Starting }));
        }

        [Test]
        public void Start_IsRefused_WhenNtfysTokenSourceCannotBeUsed()
        {
            WriteSearch("Trip.yaml", "Trip");
            CampertronInternalConfig Config = Settings(OutputType.Ntfy, Ntfy: true);
            Config.NtfyConfig = Yaml.LoadSettingsText<NtfyConfig>(NtfyText + "tokenFile: " + Path.Join(Root, "no-such-token-file") + "\n");
            Recorder Run = NewService();

            MonitoringStartResult Result = Run.Service.Start(Config);

            AssertRefused(Run, Result, MonitoringStartProblem.NtfyToken);
            Assert.That(Result.Lines[0], Does.StartWith("ntfy mode can't start: "));
        }

        [Test]
        public void Start_IsRefused_WithoutASearchToCheck_AndCreatesNoExampleSearch()
        {
            Recorder Run = NewService();

            MonitoringStartResult None = Run.Service.Start(Settings());
            AssertRefused(Run, None, MonitoringStartProblem.NoSearchToCheck);
            Assert.That(None.Lines.Select((Line, i) => (i == 0 ? "Error: " : "  ") + Line), Is.EqualTo(Base.EmailStartTests.NoSearchFileMessage), "D64's words");
            Assert.That(Directory.GetFiles(ConfigPath), Is.Empty, "no example search, no file at all (D65)");

            WriteSearch("Trip.yaml", "Trip", AutoRun: false);
            MonitoringStartResult NotEnabled = Run.Service.Start(Settings());
            AssertRefused(Run, NotEnabled, MonitoringStartProblem.NoSearchToCheck);
            Assert.That(NotEnabled.Lines.Select((Line, i) => (i == 0 ? "Error: " : "  ") + Line), Is.EqualTo(Base.EmailStartTests.NoneEnabledMessage(1)));
        }

        [Test]
        public void Start_IsRefused_WhileTheServiceIsRunning()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService();
            Run.Service.Start(Settings());
            Run.AwaitWaits(1);

            MonitoringStartResult Again = Run.Service.Start(Settings());

            Assert.That((Again.Started, Again.Problem), Is.EqualTo((false, MonitoringStartProblem.AlreadyRunning)));
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.Waiting), "the running monitoring goes on");
            Assert.That(Run.Service.GetStatus().LastStart!.Started, Is.True);
            Assert.That(Run.Of(MonitoringEventKind.Starting), Has.Count.EqualTo(1));
        }

        // ---- the lock between services ----

        [Test]
        public void ASecondService_OnTheSameSettingsFolder_IsRefusedByTheLock_UntilTheFirstStops()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder First = NewService();
            Recorder Second = NewService();
            First.Service.Start(Settings());
            First.AwaitWaits(1);
            int RequestsBefore = Requested.Count;

            MonitoringStartResult Refused = Second.Service.Start(Settings());

            Assert.That((Refused.Started, Refused.Problem, Refused.Error), Is.EqualTo((false, MonitoringStartProblem.LockHeld, (Exception?)null)));
            Assert.That(Refused.Lines, Is.EqualTo(MonitoringLock.Describe(MonitoringLockProblem.Held, "email mode")));
            Assert.That(Second.Service.GetStatus().State, Is.EqualTo(MonitoringState.NotStarted));
            Assert.That(Requested, Has.Count.EqualTo(RequestsBefore), "the second monitoring asked nothing");
            Assert.That(Second.Of(MonitoringEventKind.SearchesLoaded), Is.Empty);
            Assert.That(First.Service.GetStatus().State, Is.EqualTo(MonitoringState.Waiting), "the first goes on");

            First.Service.Stop();
            Assert.That(Second.Service.Start(Settings()).Started, Is.True, "the lock is free once the first has stopped");
            Second.AwaitWaits(1);
            Assert.That(LockIsHeld(), Is.True);
            AssertNothingOnTheConsole();
        }

        // ---- a check that fails ----

        [Test]
        public void AFailedSend_EndsTheMonitoringAsFailed_RecordsNothing_AndNothingStartsItAgain()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException("the test refused the email");
            Recorder Run = NewService();
            Run.Service.Start(Settings());

            MonitoringStatus Status = Ended(Run.Service);

            Assert.That(Status.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(Status.IsRunning, Is.False);
            Assert.That(Status.LastError, Is.Not.Null);
            Assert.That(Status.LastError!.Error, Is.InstanceOf<SmtpException>(), "the send's own failure, for the console to rethrow");
            Assert.That(Status.LastError.Blocked, Is.False);
            Assert.That(Status.LastError.Lines[0], Is.EqualTo("the email could not be sent: the test refused the email"), "the console's words for it");
            Assert.That(string.Join("\n", Status.LastError.Lines), Does.Not.Contain(Password));
            Assert.That(Emails, Is.EqualTo(new[] { "Trip" }), "one attempt");
            Assert.That(Markers(), Is.Empty, "a failed delivery's openings are not recorded");
            Assert.That(Directory.Exists(HistoryPath), Is.False);
            Assert.That(Run.Of(MonitoringEventKind.EmailSent), Is.Empty);
            Assert.That(Run.Kinds, Does.Not.Contain(MonitoringEventKind.DeliveriesFinished));
            Assert.That(Run.Delays, Is.Empty, "no wait: the monitoring stopped");
            Assert.That(LockIsHeld(), Is.False, "the lock is released");
            Thread.Sleep(200);
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.Failed), "nothing starts it again by itself");
            Assert.That(Emails, Has.Count.EqualTo(1));

            // a new start checks again: the openings were not recorded, so they are sent now
            Send = Name => { };
            Assert.That(Run.Service.Start(Settings()).Started, Is.True);
            Run.AwaitWaits(1);
            Assert.That(Run.Service.GetStatus().LastError, Is.Null, "a new start has no error");
            Assert.That(Emails, Is.EqualTo(new[] { "Trip", "Trip" }));
            Assert.That(Markers(), Is.Not.Empty);
            Run.Service.Stop();
            AssertNothingOnTheConsole();
        }

        // D67's rule, through the service: an opening is recorded once any enabled provider delivered it
        [Test]
        public void WhenOneProviderFails_TheOthersDeliveriesAreRecorded_AndTheMonitoringStillEndsAsFailed()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException("the test refused the email");
            Recorder Run = NewService();
            Run.Service.Start(Settings(Ntfy: true));

            MonitoringStatus Status = Ended(Run.Service);

            Assert.That(Status.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(Status.LastError!.Error, Is.InstanceOf<SmtpException>());
            Assert.That((Emails.Count, Published.Count), Is.EqualTo((1, 1)), "ntfy went on after the email failed");
            int Notified = Run.Of(MonitoringEventKind.NtfySent).Single().Openings;
            Assert.That(Markers(), Has.Count.EqualTo(Notified), "what ntfy delivered is recorded");

            // the next start: nothing new, so nothing is sent twice
            Send = Name => { };
            Run.Service.Start(Settings(Ntfy: true));
            Run.AwaitWaits(1);
            Assert.That((Emails.Count, Published.Count), Is.EqualTo((1, 1)), "no duplicate of what was delivered");
            Assert.That(Run.Of(MonitoringEventKind.CheckReported).Last().NewOpenings, Is.EqualTo(0));
            Run.Service.Stop();
        }

        [Test]
        public void WhenEveryProviderFails_NothingIsRecorded()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException("the test refused the email");
            Publish = Name => throw new NtfyException(NtfyFailure.ServerError, 503);
            Recorder Run = NewService();
            Run.Service.Start(Settings(Ntfy: true));

            MonitoringStatus Status = Ended(Run.Service);

            Assert.That(Status.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(Status.LastError!.Error, Is.InstanceOf<NotificationException>());
            Assert.That(Status.LastError.Lines[0], Is.EqualTo("the notifications could not be sent: both the email and the ntfy notification failed."));
            Assert.That(string.Join("\n", Status.LastError.Lines), Does.Not.Contain(Topic).And.Not.Contain(Password));
            Assert.That(Markers(), Is.Empty);
            Assert.That(LockIsHeld(), Is.False);
        }

        [Test]
        public void NtfyMode_PublishesAndRecords_WithoutAnyEmail()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Recorder Run = NewService();
            Run.Service.Start(Settings(OutputType.Ntfy, Ntfy: true));
            Run.AwaitWaits(1);

            Assert.That((Emails.Count, Published.Count), Is.EqualTo((0, 1)));
            MonitoringEvent Check = Run.Of(MonitoringEventKind.CheckReported).Single();
            Assert.That((Check.Emails, Check.NtfyMessages), Is.EqualTo((0, 1)));
            Assert.That(Markers(), Has.Count.EqualTo(Check.NewOpenings));
            Run.Service.Stop();
        }

        // without a host's own handling, a blocked request ends the monitoring as failed; the process goes on
        [Test]
        public void ABlockedRequest_EndsTheMonitoringAsFailed_WithoutTheConsolesWaitAndExit()
        {
            WriteSearch("A.yaml", "Alpha");
            WriteSearch("B.yaml", "Bravo", Id: OtherCampground);
            Answer = Url => "<html>Request blocked</html>";
            Recorder Run = NewService();
            Run.Service.Start(Settings());

            MonitoringStatus Status = Ended(Run.Service);

            Assert.That(Status.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(Status.LastError!.Blocked, Is.True);
            Assert.That(Status.LastError.Error, Is.InstanceOf<MonitoringBlockedException>());
            Assert.That(Status.LastError.Error.Message, Does.StartWith("recreation.gov blocked a request for exceeding its request limit ("));
            Assert.That(BlockedSteps, Is.Empty, "the console's five-minute wait and exit are the console's only");
            Assert.That((Emails.Count, Markers().Count), Is.EqualTo((0, 0)));
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty, "the check was not reported");
            Assert.That(Requested.Count, Is.LessThanOrEqualTo(2), "the other search stops instead of asking for all its months");
            Assert.That(LockIsHeld(), Is.False);
            AssertNothingOnTheConsole();
        }

        [Test]
        public void AHostsOwnBlocked_IsCalledWithTheRequest_AndTheCheckGoesOnAsTheHostDecides()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Url => "REQUEST BLOCKED";
            var Described = new System.Collections.Concurrent.ConcurrentQueue<string>();
            Recorder Run = NewService(Blocked: Request => Described.Enqueue(Request));
            Run.Service.Start(Settings());
            Run.AwaitWaits(1);

            Assert.That(Described, Is.Not.Empty.And.All.StartsWith("FIXTURE CAMPGROUND, "));
            Assert.That(Run.Service.GetStatus().State, Is.EqualTo(MonitoringState.Waiting), "the host's handler returned, so the check went on");
            Run.Service.Stop();
        }

        [Test]
        public void AnUnexpectedFailureInACheck_EndsTheMonitoringAsFailed_AndReleasesTheLock()
        {
            WriteSearch("Trip.yaml", "Trip");
            Recorder Run = NewService(OnEvent: Event =>
            {
                if (Event.Kind == MonitoringEventKind.CheckReported)
                {
                    throw new InvalidOperationException("the host's handler failed");
                }
            });
            Run.Service.Start(Settings());

            MonitoringStatus Status = Ended(Run.Service);

            Assert.That(Status.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(Status.LastError!.Error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(Status.LastError.Lines[0], Is.EqualTo("Campertron stopped because of an unexpected problem: InvalidOperationException: the host's handler failed"));
            Assert.That(LockIsHeld(), Is.False);
        }

        // ---- no state outside a service ----

        [Test]
        public void TwoServices_OnTwoSettingsFolders_ShareNothing()
        {
            WriteSearch("Trip.yaml", "Trip");
            string OtherFolder = Path.Join(Root, "config2");
            Directory.CreateDirectory(OtherFolder);
            File.Copy(Path.Join(ConfigPath, "Trip.yaml"), Path.Join(OtherFolder, "One.yaml"));
            File.WriteAllText(Path.Join(OtherFolder, "Two.yaml"), Base.EmailStartTests.SearchText("Two", true, "campgroundID: '" + OtherCampground + "'"));
            Recorder First = NewService(PassWaits: 2);
            Recorder Second = NewService();

            Assert.That(First.Service.Start(Settings()).Started, Is.True);
            Assert.That(Second.Service.Start(Settings(Folder: OtherFolder)).Started, Is.True, "another settings folder has its own lock");
            First.AwaitWaits(3);
            Second.AwaitWaits(1);

            MonitoringStatus A = First.Service.GetStatus();
            MonitoringStatus B = Second.Service.GetStatus();
            Assert.That((A.CheckNumber, A.Searches), Is.EqualTo((3, 1)));
            Assert.That((B.CheckNumber, B.Searches), Is.EqualTo((1, 2)), "its own check number, not the first service's");
            Assert.That(First.Of(MonitoringEventKind.CheckReported).Select(E => E.CheckNumber), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(Second.Of(MonitoringEventKind.CheckReported).Select(E => E.CheckNumber), Is.EqualTo(new[] { 1 }));
            Assert.That(LockIsHeld(ConfigPath) && LockIsHeld(OtherFolder), Is.True);

            First.Service.Stop();
            Assert.That(Second.Service.GetStatus().State, Is.EqualTo(MonitoringState.Waiting), "stopping one leaves the other running");
            Assert.That(LockIsHeld(OtherFolder), Is.True);
            Assert.That(LockIsHeld(ConfigPath), Is.False);

            // a new service starts from nothing, whatever others did
            Recorder Third = NewService();
            Assert.That(Third.Service.GetStatus().CheckNumber, Is.EqualTo(0));
            Third.Service.Start(Settings());
            Third.AwaitWaits(1);
            Assert.That(Third.Service.GetStatus().CheckNumber, Is.EqualTo(1));
        }

        // the loop's former statics are gone, and the service's types keep nothing static that can change
        [Test]
        public void TheMonitoring_KeepsNoStaticState()
        {
            const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Assert.That(typeof(Load).GetMember("CheckWait", Statics | BindingFlags.Instance), Is.Empty, "the wait is the service's option");
            Assert.That(typeof(Load).GetMember("CheckNumber", Statics | BindingFlags.Instance), Is.Empty, "the check number is the service's");
            Type[] Own = typeof(MonitoringService).Assembly.GetTypes()
                .Where(T => T.Namespace == typeof(MonitoringService).Namespace || (T.Namespace == null && T.Name.StartsWith("Monitoring", StringComparison.Ordinal)))
                .ToArray();
            Assert.That(Own.Select(T => T.Name), Is.SupersetOf(new[] { "MonitoringService", "MonitoringConsole", "MonitoringLock", "MonitoringRun", "MonitoringOptions", "MonitoringStatus" }));
            List<string> Mutable = Own.SelectMany(T => T.GetFields(Statics))
                .Where(F => !F.IsLiteral && !F.IsInitOnly)
                // the compiler's caches of lambdas and delegates, which hold no state of a run
                .Where(F => !F.Name.Contains('<') && !(F.DeclaringType?.Name.Contains('<') ?? false))
                .Select(F => F.DeclaringType!.Name + "." + F.Name).ToList();
            Assert.That(Mutable, Is.Empty);
            Assert.That(Own.SelectMany(T => T.GetProperties(Statics)).Where(P => P.CanWrite).Select(P => P.DeclaringType!.Name + "." + P.Name), Is.Empty);

            // the service never writes to a console and never ends the process: from its IL (the adapter is the console's)
            List<string> Called = new[] { typeof(MonitoringService), typeof(MonitoringRun), typeof(MonitoringLock) }
                .SelectMany(T => T.GetMethods(Statics | BindingFlags.Instance).Cast<MethodBase>().Concat(T.GetConstructors(Statics | BindingFlags.Instance))
                    .Concat(T.GetNestedTypes(Statics).SelectMany(N => N.GetMethods(Statics | BindingFlags.Instance))))
                .SelectMany(M => Base.YamlDocumentPreservationTests.ReferencedMembers(M)).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).Distinct().ToList();
            Assert.That(Called.Where(C => C.StartsWith("System.Console::", StringComparison.Ordinal) || C.StartsWith("System.Environment::Exit", StringComparison.Ordinal)), Is.Empty);
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Base.ConsoleUi::", StringComparison.Ordinal)),
                Is.EquivalentTo(new[] { "CampertronLibrary.function.Base.ConsoleUi::DescribeFailure" }), "only the failure's words, which read and write nothing");
        }
    }
}
