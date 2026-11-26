using System.Collections.Concurrent;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronUnitTests.function.RecDotOrg.monitoring;

namespace CampertronUnitTests.function.Forms
{
    // The monitoring controller (v0.4.0's phase 2; business-rules-settings §35) as any host gets it, with no web host:
    // a start that reads the settings files and starts the library's MonitoringService, a refused start in the start's
    // words with the host's own fix, the host's start, an action only for the moment the host showed and one at a time,
    // Stop, Restart, a failed send and a block (the run ends as failed, the host's searches held after a block, nothing
    // starts it again by itself), the refresh's pause, the host's stop, the monitoring lock between two controllers, the
    // host's lines without any prefix, and nothing written to a console. The web UI's use of it is MonitoringPageTests'.
    public class MonitoringControllerTests : MonitoringTestBase
    {
        private static readonly DateTime Fixed = new DateTime(2026, 11, 26, 9, 30, 0);

        // the host's own words: markers, so a line shows whose words it holds
        private static readonly MonitoringHostWords Host = new MonitoringHostWords
        {
            EmailMissingFix = "HOST: create Email.yaml.",
            NoCacheFolderFix = "HOST: restart the host.",
            NoCampgroundDataFix = "HOST: restart the host or check the folder.",
            RefusedFileFix = "HOST: fix the file.",
            InvalidYamlFix = "HOST: fix the YAML.",
            UnreadableFileFix = "HOST: check the file.",
            BlockedFix = "HOST: the host's searches wait too.",
            NotificationFix = "HOST: test the notification, then start.",
            NotStartedWithHost = "HOST: it did not start with the host.",
            UnavailableShuttingDown = "HOST: the host is stopping.",
            ShuttingDownText = "HOST: nothing was done, the host is stopping.",
            StaleText = "HOST: the view was out of date."
        };

        private readonly List<MonitoringController> Controllers = new();
        private readonly ConcurrentQueue<string> Lines = new();
        private int Prepared;
        private int Holds;
        private string? Cooldown;
        private Action Prepare = () => { };
        private Waits Waited = null!;

        [SetUp]
        public void SetUp()
        {
            Lines.Clear();
            Prepared = 0;
            Holds = 0;
            Cooldown = null;
            Prepare = () => { };
            Waited = new Waits();
        }

        // no test leaves a monitoring behind: its thread would use the next test's seams
        [TearDown]
        public void TearDown()
        {
            foreach (MonitoringController Controller in Controllers)
            {
                Controller.Shutdown();
            }
            Controllers.Clear();
        }

        // ---- the host ----

        // the waits between two checks: each holds until the monitoring is stopped or the test lets the next check begin
        private sealed class Waits
        {
            private readonly AutoResetEvent _Next = new AutoResetEvent(false);
            internal readonly SemaphoreSlim Began = new SemaphoreSlim(0);
            internal void Wait(TimeSpan Delay, CancellationToken Token)
            {
                Assert.That(Delay, Is.EqualTo(TimeSpan.FromMinutes(1)), "checks are one minute apart");
                Began.Release();
                WaitHandle.WaitAny(new[] { Token.WaitHandle, _Next });
            }
            internal void Await()
            {
                Assert.That(Began.Wait(Long), Is.True, "a wait between two checks began");
            }
        }

        private MonitoringController NewController(string? Folder = null, string? Cache = "", Waits? Wait = null)
        {
            var Controller = new MonitoringController(new MonitoringControllerOptions
            {
                SettingsFolder = Folder ?? ConfigPath,
                CachePath = Cache == "" ? CachePath : Cache,
                PrepareCampgroundData = () =>
                {
                    Interlocked.Increment(ref Prepared);
                    Prepare();
                },
                CooldownText = () => Volatile.Read(ref Cooldown),
                HoldAfterBlock = () => Interlocked.Increment(ref Holds),
                Line = Text => Lines.Enqueue(Text),
                Words = Host,
                Now = () => Fixed,
                Wait = (Wait ?? Waited).Wait
            });
            Controllers.Add(Controller);
            return Controller;
        }

        private void WriteSettings(string OutputTo = "Email", bool WithEmail = true, string? Folder = null)
        {
            File.WriteAllText(Path.Join(Folder ?? ConfigPath, "General.yaml"), "outputTo: " + OutputTo + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            if (WithEmail)
            {
                File.WriteAllText(Path.Join(Folder ?? ConfigPath, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: tester\nsmtpPassword: " + Password
                    + "\nsendToAddressList:\n  - to@example.invalid\nsendFromAddress: from@example.invalid\n");
            }
        }

        // the view once Wanted says so
        private static MonitoringControlView Until(MonitoringController Controller, Func<MonitoringControlView, bool> Wanted, string What)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                MonitoringControlView View = Controller.View();
                if (Wanted(View))
                {
                    return View;
                }
                Assert.That(Clock.Elapsed, Is.LessThan(Long), What);
                Thread.Sleep(10);
            }
        }

        // an action for the moment the host shows now
        private static MonitoringControlOutcome Act(MonitoringController Controller, MonitoringControlAction Action) => Controller.Act(Action, Controller.View().Token);

        private static MonitoringControlView Ended(MonitoringController Controller) =>
            Until(Controller, V => V.Busy == MonitoringControlBusy.None && !V.Status.IsRunning && (V.ThisEnd != null || (V.ThisStart != null && !V.ThisStart.Started)), "the run ended or the start was refused");

        private string Snapshot() => string.Join("\n", Directory.GetFileSystemEntries(ConfigPath, "*", SearchOption.AllDirectories).OrderBy(P => P, StringComparer.Ordinal)
            .Select(P => Path.GetRelativePath(ConfigPath, P) + (File.Exists(P) ? " " + new FileInfo(P).Length : "")));

        // ---- a start, its check, Stop ----

        [Test]
        public void AStart_ReadsTheSettingsFiles_RunsTheService_AndStopEndsIt_InTheHostsLinesWithoutAPrefix()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            MonitoringController Controller = NewController();
            MonitoringControlView Idle = Controller.View();
            Assert.That((Idle.Generation, Idle.Phase, Idle.Token, Idle.CanStart), Is.EqualTo((0L, MonitoringControlPhase.Idle, "0-idle", true)));

            Assert.That(Controller.Act(MonitoringControlAction.Start, Idle.Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
            Waited.Await();
            MonitoringControlView Running = Until(Controller, V => V.Phase == MonitoringControlPhase.Running && V.LastCheck != null, "the first check");
            Assert.That((Running.Generation, Running.Token, Running.Status.State, Running.Status.CheckNumber), Is.EqualTo((1L, "1-running", MonitoringState.Waiting, 1)));
            MonitoringControlStart Start = Running.ThisStart!;
            Assert.That((Start.Started, Start.Problem, Start.WithHost, Start.AfterRefresh, Start.Searches, Start.Output, Start.At), Is.EqualTo((true, MonitoringStartProblem.None, false, false, 1, "Email", Fixed)));
            Assert.That((Running.LastCheck!.Generation, Running.LastCheck.Number, Running.LastCheck.SearchCount, Running.LastCheck.NewOpenings, Running.LastCheck.Delivered),
                Is.EqualTo((1L, 1, 1, 0, true)), "nothing new: nothing to deliver");
            Assert.That(Running.LastCheck.Searches.Select(S => S.Title), Is.EqualTo(new[] { "Trip" }));
            Assert.That(Prepared, Is.EqualTo(1), "the start prepared the campground data through the host");
            Assert.That(LockIsHeld(), Is.True, "the service holds the settings folder's lock");

            Assert.That(Act(Controller, MonitoringControlAction.Stop), Is.EqualTo(MonitoringControlOutcome.Accepted));
            MonitoringControlView Stopped = Ended(Controller);
            Assert.That((Stopped.Generation, Stopped.ThisEnd!.Failed, Stopped.ThisEnd.Blocked, Stopped.ThisEnd.At, Stopped.Status.State), Is.EqualTo((1L, false, false, Fixed, MonitoringState.Stopped)));
            Assert.That(LockIsHeld(), Is.False);
            Assert.That(Lines, Is.EqualTo(new[] { "started (Email; 1 search checked every minute).", "stopped." }), "the shared lines; a prefix is the host's own");
            Assert.That((Holds, Emails.Count, Markers().Count), Is.EqualTo((0, 0, 0)));
            AssertNothingOnTheConsole();
        }

        [Test]
        public void TheHostsOwnStart_IsMarkedAsSuch_AndAStartWithoutAMonitoringSaysNothing()
        {
            WriteSettings(OutputTo: "Console", WithEmail: false);
            MonitoringController Controller = NewController();
            Controller.StartWithHost();
            MonitoringControlView View = Ended(Controller);
            Assert.That((View.ThisStart!.WithHost, View.ThisStart.Started, View.ThisStart.Problem), Is.EqualTo((true, false, MonitoringStartProblem.NotMonitoring)));
            Assert.That(MonitoringWording.StateText(View, Host), Is.EqualTo("Not running: HOST: it did not start with the host."));
            Assert.That(Lines, Is.Empty, "a start whose outputTo is no monitoring writes no line");
            Assert.That(Prepared, Is.Zero, "nothing prepared without a monitoring");

            Controller.Act(MonitoringControlAction.Start, View.Token);
            MonitoringControlView OnRequest = Until(Controller, V => V.Generation == 2 && V.ThisStart != null, "the second start");
            Assert.That(OnRequest.ThisStart!.WithHost, Is.False);
            Assert.That(MonitoringWording.StateText(OnRequest, Host), Is.EqualTo("Not running: the last start did not start it."));
            AssertNothingOnTheConsole();
        }

        // ---- what a host shows, and what its end would stop (v0.4.0's phase 4) ----

        // The view's own answers, which both hosts show: the facts once it started, a refused start, a failure, and whether the
        // monitoring is on (what a host's end would stop: the desktop GUI asks before its window closes then)
        [Test]
        public void TheView_SaysWhatAHostShows_AndWhetherTheMonitoringIsOn()
        {
            WriteSettings();
            MonitoringController Controller = NewController();
            MonitoringControlView Idle = Controller.View();
            Assert.That((Idle.ShowsFacts, Idle.ShowsStartRefused, Idle.ShowsFailure, Idle.IsActive), Is.EqualTo((false, false, false, false)));

            // refused: no search file to check (D64)
            Act(Controller, MonitoringControlAction.Start);
            MonitoringControlView Refused = Ended(Controller);
            Assert.That((Refused.ShowsFacts, Refused.ShowsStartRefused, Refused.ShowsFailure, Refused.IsActive), Is.EqualTo((false, true, false, false)));

            WriteSearch("Trip.yaml", "Trip");
            Act(Controller, MonitoringControlAction.Start);
            Waited.Await();
            MonitoringControlView Running = Until(Controller, V => V.Phase == MonitoringControlPhase.Running && V.LastCheck != null, "the first check");
            Assert.That((Running.ShowsFacts, Running.ShowsStartRefused, Running.ShowsFailure, Running.IsActive), Is.EqualTo((true, false, false, true)));
            Assert.That(MonitoringWording.StartedText(Running, " (HOST)"), Is.EqualTo("2026-11-26 09:30"), "the user's start: no note");
            Assert.That(MonitoringWording.SearchesText(Running.Status.Searches), Is.EqualTo("1 (the Enabled (autoRun) searches it read when it started)"));
            Assert.That(MonitoringWording.LastFinishedText(Running.Status), Is.EqualTo("Check 1 at 2026-11-26 09:30"));

            // paused for a refresh that starts it again: on; resumed: on again, after the refresh
            Assert.That(Controller.PauseForRefresh(), Is.True);
            MonitoringControlView Paused = Controller.View();
            Assert.That((Paused.Status.IsRunning, Paused.IsActive), Is.EqualTo((false, true)), "paused, it starts again by itself: on");
            Controller.ResumeAfterRefresh(true);
            Waited.Await();
            MonitoringControlView Again = Until(Controller, V => V.Generation == 3 && V.Phase == MonitoringControlPhase.Running, "started again");
            Assert.That(MonitoringWording.StartedText(Again, " (HOST)"), Is.EqualTo("2026-11-26 09:30" + MonitoringWording.AfterRefreshNote));

            // stopped: not on, its facts and its end shown
            Act(Controller, MonitoringControlAction.Stop);
            MonitoringControlView Stopped = Ended(Controller);
            Assert.That((Stopped.ShowsFacts, Stopped.ShowsFailure, Stopped.IsActive), Is.EqualTo((true, false, false)));

            // the host's start: its own note after the time
            Controller.StartWithHost();
            Waited.Await();
            MonitoringControlView WithHost = Until(Controller, V => V.Generation == 4 && V.Phase == MonitoringControlPhase.Running, "the host's start");
            Assert.That(MonitoringWording.StartedText(WithHost, " (HOST)"), Is.EqualTo("2026-11-26 09:30 (HOST)"));

            // the host stops: nothing is on any more
            Controller.Shutdown();
            MonitoringControlView Down = Controller.View();
            Assert.That((Down.IsActive, Down.CanStart), Is.EqualTo((false, false)));
        }

        // ---- a refused start ----

        [Test]
        public void ARefusedStart_SaysTheStartsWordsWithTheHostsFix_AndCreatesNothing()
        {
            MonitoringController Controller = NewController();
            IReadOnlyList<string> Refused()
            {
                string Before = Snapshot();
                Assert.That(Act(Controller, MonitoringControlAction.Start), Is.EqualTo(MonitoringControlOutcome.Accepted));
                long Generation = Controller.View().Generation;
                MonitoringControlView View = Until(Controller, V => V.Busy == MonitoringControlBusy.None && V.ThisStart?.Generation == Generation, "the start's answer");
                Assert.That((View.ThisStart!.Started, View.Status.IsRunning), Is.EqualTo((false, false)));
                Assert.That(Snapshot(), Is.EqualTo(Before), "a refused start creates and changes nothing: no settings file, lock file or example search");
                Assert.That(Lines.Last(), Is.EqualTo("not started: " + View.ThisStart.Lines[0]));
                return View.ThisStart.Lines;
            }
            // outputTo: Email without Email.yaml
            WriteSettings(WithEmail: false);
            Assert.That(Refused(), Is.EqualTo(new[] { "email mode can't start: Email.yaml does not exist yet.", "HOST: create Email.yaml.", "Nothing was searched or sent." }));
            // an Email.yaml the start refuses: the validation's value-free words and the host's fix
            File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpServer: ''\nsmtpPort: 587\nsmtpPassword: " + Password + "\nsendToAddressList:\n  - to@example.invalid\nsendFromAddress: from@example.invalid\n");
            IReadOnlyList<string> Invalid = Refused();
            Assert.That(Invalid[0], Does.StartWith("email mode can't start: Email.yaml can't be used: ").And.Contain("smtpServer"));
            Assert.That(Invalid.Skip(1), Is.EqualTo(new[] { "HOST: fix the file.", "Nothing was searched or sent." }));
            Assert.That(string.Join("\n", Invalid), Does.Not.Contain(Password).And.Not.Contain(ConfigPath));
            // no search file: the service's own words (D64), no example search created (D65)
            WriteSettings();
            Assert.That(Refused()[0], Does.StartWith("email mode has no search to check, so it stopped: the settings folder has no search file."));
            WriteSearch("Trip.yaml", "Trip");
            // the campground data can't be prepared: the host's fix
            Prepare = () => throw new IOException("detail");
            Assert.That(Refused(), Is.EqualTo(new[]
            {
                "email mode can't start: Campertron couldn't read its campground data, so it can't prepare the searches' campsite details.",
                "HOST: restart the host or check the folder.",
                "Nothing was searched or sent."
            }));
            Prepare = () => { };
            Assert.That((Requested.Count, Emails.Count, Holds), Is.EqualTo((0, 0, 0)), "nothing searched or sent");
            Assert.That(string.Join("\n", Lines), Does.Not.Contain(Password).And.Not.Contain(ConfigPath));

            // no cache folder: the host's fix
            MonitoringController WithoutCache = NewController(Cache: null);
            WithoutCache.Act(MonitoringControlAction.Start, WithoutCache.View().Token);
            MonitoringControlView View = Ended(WithoutCache);
            Assert.That(View.ThisStart!.Lines, Is.EqualTo(new[] { "email mode can't start: Campertron couldn't find its cache folder.", "HOST: restart the host.", "Nothing was searched or sent." }));
            AssertNothingOnTheConsole();
        }

        // ---- the moment an action is for, one action at a time ----

        [Test]
        public void AnAction_AppliesOnlyToTheMomentTheHostShowed_AndOnlyOneAtATime()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Gate = new ManualResetEventSlim(false);
            Prepare = () => Gate.Wait(Long);
            MonitoringController Controller = NewController();
            string Idle = Controller.View().Token;
            Assert.That(Controller.Act(MonitoringControlAction.Start, "0-running"), Is.EqualTo(MonitoringControlOutcome.Stale));
            Assert.That(Controller.Act(MonitoringControlAction.Start, null), Is.EqualTo(MonitoringControlOutcome.Stale));
            Assert.That(Controller.Act(MonitoringControlAction.Start, Idle), Is.EqualTo(MonitoringControlOutcome.Accepted));
            // the start waits in the host's preparation: busy, and an action meanwhile does nothing
            MonitoringControlView Starting = Controller.View();
            Assert.That((Starting.Phase, Starting.Busy, Starting.Token, Starting.CanStart, Starting.CanStop), Is.EqualTo((MonitoringControlPhase.Busy, MonitoringControlBusy.Starting, "1-busy", false, false)));
            Assert.That(Controller.Act(MonitoringControlAction.Start, Idle), Is.EqualTo(MonitoringControlOutcome.Stale), "the idle moment is gone");
            Assert.That(Controller.Act(MonitoringControlAction.Stop, Starting.Token), Is.EqualTo(MonitoringControlOutcome.Busy));
            Gate.Set();
            Waited.Await();
            MonitoringControlView Running = Until(Controller, V => V.Phase == MonitoringControlPhase.Running, "running");
            Assert.That(Controller.Act(MonitoringControlAction.Start, Running.Token), Is.EqualTo(MonitoringControlOutcome.AlreadyRunning));
            Assert.That(Controller.Act(MonitoringControlAction.Stop, Starting.Token), Is.EqualTo(MonitoringControlOutcome.Stale));
            Assert.That(Controller.View().Generation, Is.EqualTo(1), "one start");
            Assert.That(Controller.Act(MonitoringControlAction.Stop, Running.Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
            MonitoringControlView Stopped = Ended(Controller);
            Assert.That(Controller.Act(MonitoringControlAction.Stop, Stopped.Token), Is.EqualTo(MonitoringControlOutcome.NotRunning));
            Assert.That(Controller.Act(MonitoringControlAction.Restart, Stopped.Token), Is.EqualTo(MonitoringControlOutcome.NotRunning));
            AssertNothingOnTheConsole();
        }

        [Test]
        public void Restart_StopsAndStartsAgain_AsANewStartThatReadsTheSearchFilesAgain()
        {
            WriteSettings();
            WriteSearch("A.yaml", "Alpha");
            MonitoringController Controller = NewController();
            Act(Controller, MonitoringControlAction.Start);
            Waited.Await();
            Until(Controller, V => V.Phase == MonitoringControlPhase.Running, "running");
            WriteSearch("B.yaml", "Bravo", Id: OtherCampground);
            Assert.That(Controller.View().Status.SearchFilesChangedSinceStart, Is.True);

            Assert.That(Act(Controller, MonitoringControlAction.Restart), Is.EqualTo(MonitoringControlOutcome.Accepted));
            Waited.Await();
            MonitoringControlView Again = Until(Controller, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running && V.LastCheck != null, "running again");
            Assert.That((Again.ThisStart!.Searches, Again.Status.CheckNumber, Again.ThisEnd, Again.Status.SearchFilesChangedSinceStart), Is.EqualTo((2, 1, (MonitoringControlEnd?)null, false)));
            Assert.That(Again.LastCheck!.Searches.Select(S => S.Title), Is.EqualTo(new[] { "Alpha", "Bravo" }), "the last check's searches by name");
            Assert.That(Lines, Is.EqualTo(new[] { "started (Email; 1 search checked every minute).", "stopped.", "started (Email; 2 searches checked every minute)." }));
            AssertNothingOnTheConsole();
        }

        // ---- a failed check ----

        [Test]
        public void AFailedSend_EndsTheRunAsFailed_InTheHostsWords_RecordsNothing_AndNothingStartsItAgain()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "s3cret-server-answer for someone@example.invalid");
            MonitoringController Controller = NewController();
            Act(Controller, MonitoringControlAction.Start);
            MonitoringControlView Failed = Until(Controller, V => V.ThisEnd != null && V.Busy == MonitoringControlBusy.None, "the failure");

            Assert.That((Failed.ThisEnd!.Failed, Failed.ThisEnd.Blocked, Failed.Status.State), Is.EqualTo((true, false, MonitoringState.Failed)));
            Assert.That(Failed.ThisEnd.Lines, Is.EqualTo(new[]
            {
                "the email could not be sent: the SMTP server refused an address of the email (SMTP 550).",
                "Check sendFromAddress and sendToAddressList in Email.yaml.",
                "The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.",
                "HOST: test the notification, then start."
            }));
            Assert.That(Lines.Last(), Is.EqualTo("stopped because a check failed: the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(string.Join("\n", Lines.Concat(Failed.ThisEnd.Lines)), Does.Not.Contain("s3cret").And.Not.Contain("someone@").And.Not.Contain(Password));
            Assert.That(Markers(), Is.Empty, "what was not delivered is not recorded");
            Assert.That((LockIsHeld(), Holds), Is.EqualTo((false, 0)));
            Assert.That(Failed.LastCheck!.EmailsSent, Is.Zero);
            Thread.Sleep(300);
            MonitoringControlView Later = Controller.View();
            Assert.That((Later.Generation, Later.Status.IsRunning, Later.Busy, Emails.Count), Is.EqualTo((1L, false, MonitoringControlBusy.None, 1)), "nothing starts it again by itself");
            Assert.That(MonitoringWording.StateText(Later, Host), Is.EqualTo("Not running: it stopped at 2026-11-26 09:30 because a check failed. It does not start again by itself."));
            AssertNothingOnTheConsole();
        }

        [Test]
        public void ABlockedCheck_EndsTheRun_HoldsTheHostsSearches_AndStartWaitsForTheHold()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Url => "<html>Request blocked</html>";
            MonitoringController Controller = NewController();
            Act(Controller, MonitoringControlAction.Start);
            MonitoringControlView Blocked = Until(Controller, V => V.ThisEnd != null && V.Busy == MonitoringControlBusy.None, "the block");
            Assert.That((Blocked.ThisEnd!.Failed, Blocked.ThisEnd.Blocked, Holds), Is.EqualTo((true, true, 1)));
            Assert.That(Blocked.ThisEnd.Lines, Is.EqualTo(new[]
            {
                "recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it.",
                "HOST: the host's searches wait too."
            }));
            Assert.That(BlockedSteps, Is.Empty, "the console's five-minute wait and exit are the console's only");
            Cooldown = "held for five minutes";
            MonitoringControlView Held = Controller.View();
            Assert.That((Held.Cooldown, Held.CanStart), Is.EqualTo(("held for five minutes", false)));
            Assert.That(Controller.Act(MonitoringControlAction.Start, Held.Token), Is.EqualTo(MonitoringControlOutcome.CoolingDown));
            Assert.That(MonitoringWording.Outcome(MonitoringControlOutcome.CoolingDown, Held.Cooldown, Host), Is.EqualTo("Nothing was started: held for five minutes"));
            Assert.That(Controller.View().Generation, Is.EqualTo(1));
            AssertNothingOnTheConsole();
        }

        // ---- the refresh's pause, the host's stop ----

        [Test]
        public void ThePauseForARefresh_StopsARunningMonitoring_RefusesActions_AndStartsItAgainAfterwards()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            MonitoringController Controller = NewController();
            Assert.That(Controller.PauseForRefresh(), Is.False, "nothing ran: nothing to start again");
            Assert.That(Controller.View().Paused, Is.True);
            Assert.That(Act(Controller, MonitoringControlAction.Start), Is.EqualTo(MonitoringControlOutcome.PausedForRefresh));
            Controller.ResumeAfterRefresh(false);
            Assert.That((Controller.View().Paused, Controller.View().Generation), Is.EqualTo((false, 0L)), "nothing started");

            Act(Controller, MonitoringControlAction.Start);
            Waited.Await();
            Until(Controller, V => V.Phase == MonitoringControlPhase.Running, "running");
            Assert.That(Controller.PauseForRefresh(), Is.True, "the pause stopped a running monitoring");
            MonitoringControlView Paused = Controller.View();
            Assert.That((Paused.Paused, Paused.Resumes, Paused.Status.IsRunning, Paused.Busy, Paused.CanStart), Is.EqualTo((true, true, false, MonitoringControlBusy.None, false)));
            Assert.That(MonitoringWording.StateText(Paused, Host), Is.EqualTo("Paused while the campground data is refreshed (Settings > System): the monitoring starts again by itself when the refresh has finished."));
            Assert.That(Controller.Act(MonitoringControlAction.Start, Paused.Token), Is.EqualTo(MonitoringControlOutcome.PausedForRefresh));
            Assert.That(LockIsHeld(), Is.False, "no check runs while the refresh clears the cache");
            Controller.StartWithHost();
            Assert.That(Controller.View().Generation, Is.EqualTo(1), "the host's own start waits for the refresh too");

            Controller.ResumeAfterRefresh(true);
            Waited.Await();
            MonitoringControlView Again = Until(Controller, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running, "running again");
            Assert.That((Again.ThisStart!.AfterRefresh, Again.ThisStart.WithHost, Again.Paused), Is.EqualTo((true, false, false)));
            Assert.That(Lines, Is.EqualTo(new[] { "started (Email; 1 search checked every minute).", "paused while the campground data is refreshed.", "started (Email; 1 search checked every minute)." }));
            AssertNothingOnTheConsole();
        }

        [Test]
        public void TheHostsStop_StopsTheMonitoring_AndEveryActionAfterIt_DoesNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            MonitoringController Controller = NewController();
            Act(Controller, MonitoringControlAction.Start);
            Waited.Await();
            Until(Controller, V => V.Phase == MonitoringControlPhase.Running, "running");

            Controller.Shutdown();
            MonitoringControlView Stopped = Controller.View();
            Assert.That((Stopped.ShuttingDown, Stopped.Status.IsRunning, Stopped.Status.State, Stopped.CanStart, Stopped.CanStop), Is.EqualTo((true, false, MonitoringState.Stopped, false, false)));
            Assert.That(LockIsHeld(), Is.False);
            Assert.That(Lines.Last(), Is.EqualTo("stopped."), "the end's line before the host's own last line");
            Assert.That(Controller.Act(MonitoringControlAction.Start, Stopped.Token), Is.EqualTo(MonitoringControlOutcome.ShuttingDown));
            Assert.That(MonitoringWording.Unavailable(Stopped, Host), Is.EqualTo("HOST: the host is stopping."));
            Controller.StartWithHost();
            Assert.That(Controller.PauseForRefresh(), Is.False);
            Controller.ResumeAfterRefresh(true);
            Assert.That(Controller.View().Generation, Is.EqualTo(1), "nothing starts once the host stops");
            AssertNothingOnTheConsole();
        }

        // ---- two hosts on one settings folder ----

        [Test]
        public void TwoControllers_OnOneSettingsFolder_TheLockRefusesTheSecondsStart()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            MonitoringController First = NewController();
            Act(First, MonitoringControlAction.Start);
            Waited.Await();
            Until(First, V => V.Phase == MonitoringControlPhase.Running, "the first runs");

            var OtherWaits = new Waits();
            MonitoringController Second = NewController(Wait: OtherWaits);
            Act(Second, MonitoringControlAction.Start);
            MonitoringControlView Refused = Ended(Second);
            Assert.That(Refused.ThisStart!.Problem, Is.EqualTo(MonitoringStartProblem.LockHeld));
            Assert.That(Lines.Last(), Is.EqualTo("not started: " + Refused.ThisStart.Lines[0]));
            Assert.That(First.View().Phase, Is.EqualTo(MonitoringControlPhase.Running), "the first goes on");
            AssertNothingOnTheConsole();
        }
    }
}
