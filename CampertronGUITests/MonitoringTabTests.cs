using System.Net.Mail;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.monitoring;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's Monitoring tab (v0.4.0's phase 4, D115 and D118; business-rules-gui §41) over the application's
    // monitoring, the library's MonitoringController: the tab between Results and Settings, its state, Start, Stop and
    // Restart only when the controller allows them, a refused start in the start's words with the desktop GUI's fix, a failed
    // send and a block in fixed words, the last check's openings, the five minutes after a block shared with the Results
    // tab, and a monitoring that belongs to the application: leaving the tab stops only the tab's reading, the reading never
    // acts, and nothing starts it but the user's Start.
    public class MonitoringTabTests : DesktopMonitoringTestBase
    {
        // ---- composition: the application's controller, started by nothing but the user ----

        [AvaloniaTest]
        public void TheTab_ShowsTheApplicationsController_AndTheStartStartsNothing()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            Assert.That(ViewModel.Monitoring, Is.Not.Null, "the application's monitoring exists once the startup is ready");
            Assert.That(ViewModel.MonitoringTab, Is.Not.Null);
            MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
            Assert.That((View.Generation, View.Phase, View.IsActive), Is.EqualTo((0L, MonitoringControlPhase.Idle, false)), "no start of the GUI starts it (D115)");

            ShowMonitoringTab(Window);
            Assert.That(TabView(Window).DataContext, Is.SameAs(ViewModel.MonitoringTab), "the tab shows the application's monitoring");
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Assert.That(Tab.StateText, Is.EqualTo("Not running."));
            Assert.That((Tab.CanStart, Tab.CanStop, Tab.CanRestart), Is.EqualTo((true, false, false)));
            Assert.That(Tab.UnavailableText, Is.EqualTo(MonitoringWording.StopUnavailableIdle), "why Stop and Restart can't be used, in words");
            Assert.That(InTab<TextBlock>(Window, "NoCheckText").IsEffectivelyVisible, Is.True);
            Assert.That((InTab<Button>(Window, "StartMonitoringButton").IsEffectivelyEnabled, InTab<Button>(Window, "StopMonitoringButton").IsEffectivelyEnabled,
                InTab<Button>(Window, "RestartMonitoringButton").IsEffectivelyEnabled), Is.EqualTo((true, false, false)));
            Assert.That(Tab.TimerRunning, Is.True, "the tab reads the monitoring while it is shown");
            for (int i = 0; i < 5; i++)
            {
                Timers.Tick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.That(ViewModel.Monitoring.Controller.View().Generation, Is.Zero, "reading starts nothing");
            Assert.That((Requested.Count, Emails.Count, Prepared, LockIsHeld()), Is.EqualTo((0, 0, 0, false)), "nothing asked, sent, prepared or locked");
            Window.Close();
        }

        // the desktop GUI's monitoring is the shared controller's: no service, loop or wait of its own (the IL of the
        // desktop GUI's own classes never starts, stops or waits for a MonitoringService)
        [Test]
        public void TheDesktopGui_ComposesTheSharedController_AndHasNoMonitoringEngineOfItsOwn()
        {
            System.Reflection.Assembly Gui = typeof(MainWindowViewModel).Assembly;
            List<string> Members = ReferencedMembers(Gui.GetTypes()).Select(R => R.Member).ToList();
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Forms.MonitoringController::.ctor"), "the desktop GUI composes the shared controller");
            Assert.That(Members, Does.Contain("CampertronLibrary.function.Forms.MonitoringController::Act"));
            Assert.That(Members.Where(M => M.StartsWith("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::", StringComparison.Ordinal)), Is.Empty,
                "no MonitoringService of the desktop GUI's own: its start, stop, wait and status are the controller's");
            Assert.That(Members, Does.Not.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringLock::TryAcquire"), "no lock of its own");
            Assert.That(Members, Does.Not.Contain("CampertronLibrary.function.Forms.MonitoringController::StartWithHost"), "never a start with the GUI (D115)");
            // the tab's view model only reads and acts: never the controller's own end, pause or resume
            List<string> Tab = ReferencedMembers(typeof(MonitoringViewModel)).Select(R => R.Member).ToList();
            Assert.That(Tab, Does.Contain("CampertronLibrary.function.Forms.MonitoringController::View").And.Contain("CampertronLibrary.function.Forms.MonitoringController::Act"));
            Assert.That(Tab.Where(M => M.StartsWith("CampertronLibrary.function.Forms.MonitoringController::", StringComparison.Ordinal))
                .Except(new[] { "CampertronLibrary.function.Forms.MonitoringController::View", "CampertronLibrary.function.Forms.MonitoringController::Act" }), Is.Empty,
                "the tab never shuts down, pauses or resumes the monitoring");
        }

        // the desktop GUI's own words in the shared ones: every one of them set, none a value, a path or a server's text
        [Test]
        public void TheDesktopsOwnWords_AreFixedWords_AndTheSharedWordsStayTheLayers()
        {
            MonitoringHostWords Host = DesktopMonitoringWording.Host;
            string[] Words = { Host.EmailMissingFix, Host.NoCacheFolderFix, Host.NoCampgroundDataFix, Host.RefusedFileFix, Host.InvalidYamlFix, Host.UnreadableFileFix,
                Host.BlockedFix, Host.NotificationFix, Host.NotStartedWithHost, Host.UnavailableShuttingDown, Host.ShuttingDownText, Host.StaleText };
            Assert.That(Words, Has.All.Not.Empty);
            Assert.That(Words, Has.None.Contain("/").And.None.Contain("\\").And.None.Contain("web mode").And.None.Contain("page"),
                "the desktop GUI's own sentences: no path and not the web UI's");
            Assert.That(Host.BlockedFix, Does.Contain("Results tab"));
            Assert.That(Host.EmailMissingFix, Does.Contain("Settings > Campertron > Email.yaml"));
            Assert.That(DesktopMonitoringWording.Intro + DesktopMonitoringWording.StopHelp + DesktopMonitoringWording.LastCheckNote + DesktopMonitoringWording.CloseText,
                Does.Not.Contain("web mode").And.Not.Contain("results page"));
            // the state words are the layer's, with the desktop GUI's sentences only where the layer takes them
            Assert.That(MonitoringWording.Heading, Is.EqualTo("Monitoring"));
            Assert.That(DesktopMonitoringWording.Unavailable(new MonitoringController(new MonitoringControllerOptions
            {
                SettingsFolder = Folders.Config, PrepareCampgroundData = () => { }, CooldownText = () => null, HoldAfterBlock = () => { }, Words = Host
            }).View()), Is.EqualTo(MonitoringWording.StopUnavailableIdle));
        }

        // ---- a start, its check, Stop and Restart ----

        [AvaloniaTest]
        public void AValidStart_Runs_ShowsItsFactsAndTheLastChecksOpenings_AndStopEndsIt()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(InTab<Button>(Window, "StartMonitoringButton").Command!);
            Waited.Await();
            Waiting(ViewModel);
            Tab.Update();
            Settle(Window);

            Assert.That(Tab.StateText, Is.EqualTo("Running: the next check starts at 2026-11-26 09:31."));
            Assert.That((Tab.CanStart, Tab.CanStop, Tab.CanRestart), Is.EqualTo((false, true, true)));
            Assert.That(Tab.UnavailableText, Is.EqualTo(MonitoringWording.StartUnavailableRunning));
            Assert.That(InTab<Grid>(Window, "MonitoringFacts").IsEffectivelyVisible, Is.True);
            Assert.That((Tab.StartedText, Tab.NotificationsText, Tab.SearchesText, Tab.LastFinishedText, Tab.NextCheckText),
                Is.EqualTo(("2026-11-26 09:30", "Email", "1 (the Enabled (autoRun) searches it read when it started)", "Check 1 at 2026-11-26 09:30", "2026-11-26 09:31")));
            Assert.That(Tab.CheckSummary, Does.StartWith("Check 1 at 2026-11-26 09:30: 1 search, ").And.EndWith("; 1 of 1 email sent."));
            Assert.That(Tab.CheckSearches.Select(S => S.Title), Is.EqualTo(new[] { "First trip" }));
            Assert.That(Tab.SelectedSearch, Is.SameAs(Tab.CheckSearches[0]), "the first search with openings");
            Assert.That(Tab.SelectedRows, Is.Not.Empty);
            Assert.That(InTab<ListBox>(Window, "CheckRowsList").IsEffectivelyVisible, Is.True);
            Assert.That(Emails.Count, Is.EqualTo(1), "the monitoring notified the new openings (the recording stand-in)");
            Assert.That(Markers(), Is.Not.Empty, "what was delivered is recorded in Camp.History");
            Assert.That(LockIsHeld(), Is.True, "the monitoring holds the settings folder's lock");
            Assert.That(Prepared, Is.EqualTo(1), "the start prepared the campground data");

            Press(InTab<Button>(Window, "StopMonitoringButton").Command!);
            MonitoringControlView Stopped = Ended(ViewModel);
            Tab.Update();
            Assert.That(Stopped.ThisEnd!.Failed, Is.False);
            Assert.That(Tab.StateText, Is.EqualTo("Not running: it was stopped at 2026-11-26 09:30. It does not start again by itself: select Start to start it again."));
            Assert.That((Tab.CanStart, Tab.CanStop, Tab.CanRestart), Is.EqualTo((true, false, false)));
            Assert.That(LockIsHeld(), Is.False);
            Window.Close();
        }

        [AvaloniaTest]
        public void Restart_StopsAndStartsAgain_ReadingTheSearchFilesAgain()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            // a search file saved meanwhile: the running monitoring says so, and Restart reads it
            WriteSearch("Beta.yaml", "Second trip");
            Until(ViewModel, V => V.Status.SearchFilesChangedSinceStart, "the files changed since the start");
            Tab.Update();
            Assert.That(Tab.ShowsFilesChanged, Is.True);
            Assert.That(Tab.FilesChangedText, Is.EqualTo(MonitoringWording.FilesChanged));
            Assert.That(SeverityOf(InTab<TextBlock>(Window, "MonitoringFilesChangedText")), Is.EqualTo("Warning"));
            Press(Tab.RestartCommand);
            Waited.Await();
            MonitoringControlView Again = Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running && V.LastCheck != null, "the restarted monitoring's check");
            Assert.That(Again.Status.Searches, Is.EqualTo(2), "Restart read the search files again");
            Tab.Update();
            Assert.That(Tab.ShowsFilesChanged, Is.False);
            Assert.That(Tab.SearchesText, Does.StartWith("2 "));
            Window.Close();
        }

        // ---- refused starts: the start's own words, the desktop GUI's fix ----

        [AvaloniaTest]
        public void AStartWithoutEmailYaml_IsRefused_InTheStartsWords_WithTheDesktopsFix()
        {
            WriteSearch();
            File.Delete(Folders.In("Email.yaml"));
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            Ended(ViewModel);
            Tab.Update();
            Settle(Window);
            Assert.That(Tab.ShowsStartRefused, Is.True);
            Assert.That(Tab.StartRefusedText!.Split(Environment.NewLine), Is.EqualTo(new[]
            {
                "email mode can't start: Email.yaml does not exist yet.", DesktopMonitoringWording.Host.EmailMissingFix, "Nothing was searched or sent."
            }));
            Assert.That(SeverityOf(InTab<TextBlock>(Window, "MonitoringStartRefusedText")), Is.EqualTo("Error"));
            Assert.That(Tab.StateText, Is.EqualTo("Not running: the last start did not start it."));
            Assert.That((Tab.CanStart, Requested.Count, LockIsHeld()), Is.EqualTo((true, 0, false)), "nothing searched or locked; Start again is available");
            Window.Close();
        }

        [AvaloniaTest]
        public void AStartWithoutAUsableSearch_IsRefused_AndNothingIsSearched()
        {
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            MonitoringControlView View = Ended(ViewModel);
            Tab.Update();
            Assert.That(View.ThisStart!.Problem, Is.Not.EqualTo(MonitoringStartProblem.None));
            Assert.That(Tab.ShowsStartRefused, Is.True);
            Assert.That(Tab.StartRefusedText, Does.Contain("Nothing was searched or sent.").Or.Contain("no search"), Tab.StartRefusedText);
            Assert.That(SeverityOf(InTab<TextBlock>(Window, "MonitoringStartRefusedText")), Is.EqualTo("Error"));
            Assert.That((Requested.Count, Emails.Count, LockIsHeld()), Is.EqualTo((0, 0, false)));
            Window.Close();
        }

        [AvaloniaTest]
        public void AStartWhenNoMonitoringIsTurnedOn_SaysSoAsInformation()
        {
            WriteSearch();
            Folders.Write("General.yaml", "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            Ended(ViewModel);
            Tab.Update();
            Settle(Window);
            Assert.That(Tab.StartRefusedText, Is.EqualTo("the monitoring is not turned on: outputTo in General.yaml is not Email or Ntfy."));
            Assert.That(SeverityOf(InTab<TextBlock>(Window, "MonitoringStartRefusedText")), Is.EqualTo("Info"));
            Assert.That(Prepared, Is.Zero, "nothing prepared without a monitoring");
            Window.Close();
        }

        // ---- one action at a time; a tab that is out of date starts nothing ----

        [AvaloniaTest]
        public void RepeatedAndOutOfDateActions_StartOneMonitoringOnly()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Assert.That(Tab.Act(MonitoringControlAction.Start), Is.EqualTo(MonitoringControlOutcome.Accepted));
            Assert.That(Tab.CanStart, Is.False, "Start is unavailable at once, before the start has finished");
            Assert.That(Tab.StartCommand.CanExecute(null), Is.False, "a second press of the button does nothing");
            // a second Start sent anyway: refused by the controller, in words
            MonitoringControlOutcome? Second = Tab.Act(MonitoringControlAction.Start);
            Assert.That(Second, Is.AnyOf(MonitoringControlOutcome.Busy, MonitoringControlOutcome.AlreadyRunning, MonitoringControlOutcome.Stale));
            Assert.That(Tab.Notice, Is.Not.Null.And.StartsWith("Nothing was"), "what it did, in words");
            Waited.Await();
            Waiting(ViewModel);
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.EqualTo(1), "one start only");
            // an action for a moment the tab no longer shows (its reading is behind): nothing is done
            MonitoringControlView Shown = Tab.View;
            Assert.That(ViewModel.Monitoring.Controller.Act(MonitoringControlAction.Stop, ViewModel.Monitoring.Controller.View().Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
            Ended(ViewModel);
            Assert.That(ViewModel.Monitoring.Controller.Act(MonitoringControlAction.Restart, Shown.Token), Is.EqualTo(MonitoringControlOutcome.Stale));
            Assert.That(DesktopMonitoringWording.Outcome(MonitoringControlOutcome.Stale, null), Is.EqualTo(DesktopMonitoringWording.Host.StaleText));
            Assert.That(ViewModel.Monitoring.Controller.View().Generation, Is.EqualTo(1), "nothing started again");
            Window.Close();
        }

        // The tab acts only for the moment it showed (its Token): a tab that still shows the first run, while the monitoring
        // was restarted behind it, stops nothing of the run it never showed and says so
        [AvaloniaTest]
        public void AnActionOfAnOutOfDateTab_DoesNothing_AndSaysSo()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            Tab.Update();
            Assert.That(Tab.View.Token, Is.EqualTo("1-running"));
            // behind the tab (before its next reading): a restart, so the monitoring is now the second run
            MonitoringController Controller = ViewModel.Monitoring!.Controller;
            Assert.That(Controller.Act(MonitoringControlAction.Restart, Controller.View().Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
            Waited.Await();
            Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running && V.LastCheck != null, "the second run");
            Assert.That(Tab.View.Token, Is.EqualTo("1-running"), "the tab has not read the monitoring since");
            Assert.That(Tab.Act(MonitoringControlAction.Stop), Is.EqualTo(MonitoringControlOutcome.Stale));
            Assert.That(Tab.Notice, Is.EqualTo(DesktopMonitoringWording.Host.StaleText));
            MonitoringControlView After = Controller.View();
            Assert.That((After.Generation, After.Phase, After.Status.StopRequested), Is.EqualTo((2L, MonitoringControlPhase.Running, false)), "the run the tab never showed goes on");
            Assert.That(Tab.View.Token, Is.EqualTo("2-running"), "and the tab shows it now");
            Window.Close();
        }

        // ---- the tab's reading never acts, and leaving the tab never stops the monitoring ----

        [AvaloniaTest]
        public void LeavingTheTab_StopsOnlyItsReading_AndTheMonitoringGoesOn()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            foreach (int Other in new[] { MainWindow.SearchesTabIndex, MainWindow.ResultsTabIndex, MainWindow.SettingsTabIndex, MainWindow.CampsitesTabIndex })
            {
                Tabs(Window).SelectedIndex = Other;
                Settle(Window);
                Assert.That(Tab.IsShown, Is.False);
                Assert.That(Tab.TimerRunning, Is.False, "a hidden tab reads nothing");
                MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
                Assert.That((View.Phase, View.Generation, View.Status.StopRequested), Is.EqualTo((MonitoringControlPhase.Running, 1L, false)), "the monitoring goes on in another tab");
            }
            int Reads = Tab.Reads;
            Timers.Tick();
            Assert.That(Tab.Reads, Is.EqualTo(Reads), "the stopped timer reads nothing");
            // the next check goes on while another tab is shown
            Waited.Next();
            Waited.Await();
            Until(ViewModel, V => V.Status.CheckNumber == 2, "the second check, with another tab shown");
            ShowMonitoringTab(Window);
            Assert.That(Tab.TimerRunning, Is.True);
            Assert.That(Tab.LastFinishedText, Is.EqualTo("Check 2 at 2026-11-26 09:30"), "shown again, the tab reads the monitoring as it is");
            Assert.That(LockIsHeld(), Is.True);
            Window.Close();
        }

        [AvaloniaTest]
        public void TheTabsReading_NeverStartsStopsRepeatsOrResumes()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            MonitoringController Controller = ViewModel.Monitoring!.Controller;
            for (int i = 0; i < 20; i++)
            {
                Timers.Tick();
                Tab.Update();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.That(Controller.View().Generation, Is.Zero, "reading an idle monitoring starts nothing");
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            int Checks = Waited.Count;
            for (int i = 0; i < 20; i++)
            {
                Timers.Tick();
                Dispatcher.UIThread.RunJobs();
            }
            MonitoringControlView Running = Controller.View();
            Assert.That((Running.Generation, Running.Status.CheckNumber, Waited.Count, Running.Status.StopRequested), Is.EqualTo((1L, 1, Checks, false)),
                "reading a running monitoring repeats no check and stops nothing");
            // a failed monitoring is never started again by the reading (D76)
            Send = _ => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "s3cret server answer");
            Press(Tab.StopCommand);
            Ended(ViewModel);
            Tab.Update();
            // the openings are new again (the first start's deliveries were recorded), so the next check sends
            Directory.Delete(Folders.In("Camp.History"), true);
            Press(Tab.StartCommand);
            Until(ViewModel, V => V.ShowsFailure, "the failed send ended it");
            long Generation = Controller.View().Generation;
            for (int i = 0; i < 20; i++)
            {
                Timers.Tick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.That(Controller.View().Generation, Is.EqualTo(Generation), "nothing starts it again");
            Window.Close();
        }

        // ---- a failure: fixed words, the GUI goes on, nothing starts again (D76) ----

        // PT1 (v0.4.0's phase 13): an ntfy server's error stops the desktop GUI's monitoring as any failed send does; its words
        // say to check the server and that the monitoring starts again only when Start is selected, never a restart policy
        // (no restart policy restarts a monitoring the window goes on after); nothing restarts it, nothing undelivered is recorded
        [AvaloniaTest]
        public void AnNtfyServersError_EndsTheMonitoringAsFailed_SaysToCheckTheServerAndSelectStart_NeverARestartPolicy()
        {
            WriteSearch();
            Folders.Write("General.yaml", "outputTo: Ntfy\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            Folders.Write("Ntfy.yaml", "enabled: true\nserver: https://ntfy.example.invalid\ntopic: gui-pt1-topic-4c2e\n");
            int Publishes = 0;
            CampertronLibrary.function.Base.Notifications.PublishNtfy = (Settings, Message) => { Interlocked.Increment(ref Publishes); throw new CampertronLibrary.function.Base.NtfyException(CampertronLibrary.function.Base.NtfyFailure.ServerError, 503); };
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            MonitoringControlView Failed = Until(ViewModel, V => V.ShowsFailure, "the failed publish ended it");
            Tab.Update();
            Settle(Window);
            Assert.That(Failed.ThisEnd!.Failed, Is.True);
            Assert.That(Tab.StateText, Does.StartWith("Not running: it stopped at ").And.EndWith(" because a check failed. It does not start again by itself."));
            Assert.That(Tab.FailureText!.Split('\n').Select(L => L.TrimEnd('\r')), Is.EqualTo(new[]
            {
                "the ntfy notification could not be sent: the ntfy server failed (HTTP 503).",
                "The server has a problem: check it.",
                "The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again.",
                DesktopMonitoringWording.Host.NotificationFix,
            }));
            Assert.That(DesktopMonitoringWording.Host.NotificationFix, Does.StartWith("The monitoring does not start again by itself: ").And.EndWith("then select Start."));
            string Shown = string.Join("\n", Window.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text));
            Assert.That(Shown, Does.Not.Contain("restart policy").And.Not.Contain("start Campertron again").And.Not.Contain("gui-pt1-topic-4c2e"));
            Assert.That(Markers(), Is.Empty, "a failed delivery's openings are not recorded (D76)");
            Assert.That(Window.IsVisible, Is.True, "the GUI goes on");
            Assert.That((Tab.CanStart, LockIsHeld(), Publishes, ViewModel.Monitoring!.Controller.View().Generation), Is.EqualTo((true, false, 1, 1L)), "nothing started again by itself");
            Window.Close();
        }

        [AvaloniaTest]
        public void AFailedSend_EndsTheMonitoringAsFailed_InFixedWords_AndTheWindowGoesOn()
        {
            WriteSearch();
            Send = _ => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "s3cret server answer for someone@example.com at /etc/campertron");
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            MonitoringControlView Failed = Until(ViewModel, V => V.ShowsFailure, "the failed send ended it");
            Tab.Update();
            Settle(Window);
            Assert.That(Failed.ThisEnd!.Failed, Is.True);
            Assert.That(Tab.StateText, Does.StartWith("Not running: it stopped at 2026-11-26 09:30 because a check failed."));
            Assert.That(Tab.FailureText, Does.StartWith("the email could not be sent: "));
            Assert.That(Tab.FailureText, Does.Contain("The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again."));
            Assert.That(Tab.FailureText, Does.EndWith(DesktopMonitoringWording.Host.NotificationFix));
            Assert.That(SeverityOf(InTab<TextBlock>(Window, "MonitoringFailureText")), Is.EqualTo("Error"));
            Assert.That(Markers(), Is.Empty, "a failed delivery's openings are not recorded (D76)");
            Assert.That(Window.IsVisible, Is.True, "the GUI goes on");
            Assert.That((Tab.CanStart, LockIsHeld()), Is.EqualTo((true, false)));
            // no server text, address, path or secret anywhere in the window
            string Shown = string.Join("\n", Window.GetVisualDescendants().OfType<TextBlock>().Select(T => T.Text));
            Assert.That(Shown, Does.Not.Contain("s3cret").And.Not.Contain("someone@example.com").And.Not.Contain("/etc/campertron").And.Not.Contain(ResultsFolders.Sentinel)
                .And.Not.Contain(Folders.Config));
            Window.Close();
        }

        // ---- a block: five minutes for the monitoring's Start and the Results tab's ----

        [AvaloniaTest]
        public void ABlock_EndsTheMonitoring_AndHoldsStartHereAndInTheResultsTab()
        {
            WriteSearch();
            Answer = Blocked;
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Press(Tab.StartCommand);
            MonitoringControlView Blocked_ = Until(ViewModel, V => V.ShowsFailure, "the block ended it");
            Dispatcher.UIThread.RunJobs();
            Tab.Update();
            Assert.That(Blocked_.ThisEnd!.Blocked, Is.True);
            Assert.That(Tab.FailureText, Does.StartWith("recreation.gov limited Campertron's requests (too many requests)"));
            Assert.That(Tab.FailureText, Does.EndWith(DesktopMonitoringWording.Host.BlockedFix));
            Assert.That(Tab.CooldownText, Does.StartWith("recreation.gov limited Campertron's requests"));
            Assert.That(Tab.CanStart, Is.False, "the monitoring's Start waits the five minutes");
            Assert.That(Tab.Act(MonitoringControlAction.Start), Is.EqualTo(MonitoringControlOutcome.Stale).Or.EqualTo(MonitoringControlOutcome.CoolingDown));
            // the Results tab shares the hold (as the web UI's results page shares the monitoring's)
            Assert.That(ViewModel.Results!.IsCoolingDown, Is.True);
            Assert.That(ViewModel.Results.CooldownText, Does.StartWith("recreation.gov limited Campertron's requests"), "the Results tab says so, with the time left");
            Time.Advance(TimeSpan.FromMinutes(5));
            Tab.Update();
            Timers.Tick();
            Dispatcher.UIThread.RunJobs();
            Assert.That((Tab.CanStart, Tab.HasCooldown, ViewModel.Results.IsCoolingDown, ViewModel.Results.HasCooldown), Is.EqualTo((true, false, false, false)));
            Window.Close();
        }

        // ---- accessibility: names, words, focus, announcements ----

        [AvaloniaTest]
        public void EveryControl_IsNamed_AndEveryStateIsInWords()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            foreach (string Name in new[] { "StartMonitoringButton", "StopMonitoringButton", "RestartMonitoringButton" })
            {
                Button Action = InTab<Button>(Window, Name);
                AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Action);
                Assert.That(Peer.GetName(), Is.EqualTo(Action.Content as string).And.Not.Empty, Name);
                Assert.That(AutomationProperties.GetHelpText(Action), Is.Not.Empty, Name);
            }
            AutomationPeer State = ControlAutomationPeer.CreatePeerForElement(InTab<AnnouncedText>(Window, "MonitoringStateText"));
            Assert.That(State.GetName(), Is.EqualTo("Not running."), "the state line is read as its words");
            Assert.That(AutomationProperties.GetLiveSetting(InTab<AnnouncedText>(Window, "MonitoringStateText")), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(AutomationProperties.GetHeadingLevel(InTab<TextBlock>(Window, "MonitoringHeading")), Is.EqualTo(1));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(InTab<ComboBox>(Window, "CheckSearchChoice")).GetName(), Is.EqualTo(MonitoringWording.ResultsOfName));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(InTab<ListBox>(Window, "CheckRowsList")).GetName(), Is.EqualTo(ResultsWording.OpeningsName));
            // the tab's names are no implementation words
            Assert.That(ControlAutomationPeer.CreatePeerForElement((TabItem)Tabs(Window).Items[MainWindow.MonitoringTabIndex]!).GetName(), Is.EqualTo("Monitoring"));
            Window.Close();
        }

        [AvaloniaTest]
        public void AnAction_MovesTheFocusToTheState_AndAReadingNeverMovesIt()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            Button Start = InTab<Button>(Window, "StartMonitoringButton");
            Start.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Settle(Window);
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.EqualTo(1), "Enter on Start started it");
            Assert.That(Focused(Window), Is.SameAs(InTab<AnnouncedText>(Window, "MonitoringStateText")), "after an action, the state has the focus");
            Waited.Await();
            Waiting(ViewModel);
            Tab.Update();
            Settle(Window);
            Button Stop = InTab<Button>(Window, "StopMonitoringButton");
            Stop.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 5; i++)
            {
                Timers.Tick();
                Settle(Window);
            }
            Waited.Next();
            Waited.Await();
            Until(ViewModel, V => V.Status.CheckNumber == 2, "the second check");
            Timers.Tick();
            Settle(Window);
            Assert.That(Focused(Window), Is.SameAs(Stop), "the tab's reading never moves the focus or replaces the focused control");
            Assert.That(InTab<Button>(Window, "StopMonitoringButton"), Is.SameAs(Stop));
            Window.Close();
        }

        // the state line is announced when the monitoring's state changes, never at each check
        [AvaloniaTest]
        public void TheStateLine_IsAnnouncedWhenTheStateChanges_NotAtEachCheck()
        {
            // a clock that moves a minute with each wait, so each check's state line says another time
            Clock = () => Fixed.AddMinutes(Waited.Count);
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            AnnouncedText Line = InTab<AnnouncedText>(Window, "MonitoringStateText");
            var Names = new List<string?>();
            AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Line);
            Peer.PropertyChanged += (_, e) =>
            {
                if (e.Property == AutomationElementIdentifiers.NameProperty)
                {
                    Names.Add(e.NewValue as string);
                }
            };
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            Tab.Update();
            Settle(Window);
            int AfterStart = Line.Announced;
            Assert.That(AfterStart, Is.GreaterThan(0), "the start is announced");
            Assert.That(Names, Is.Not.Empty);
            var Shown = new HashSet<string> { Tab.StateText };
            for (int Check = 2; Check <= 4; Check++)
            {
                Waited.Next();
                Waited.Await();
                int Number = Check;
                Until(ViewModel, V => V.Status.CheckNumber == Number && V.Status.State == MonitoringState.Waiting, "the next check");
                Tab.Update();
                Settle(Window);
                Shown.Add(Tab.StateText);
            }
            Assert.That(Shown, Has.Count.EqualTo(4), "each check's state line said another time");
            Assert.That(Line.Announced, Is.EqualTo(AfterStart), "the checks are shown and read when asked for, never announced");
            Assert.That(Tab.LastFinishedText, Does.StartWith("Check 4 at "));
            Press(Tab.StopCommand);
            Ended(ViewModel);
            Tab.Update();
            Settle(Window);
            Assert.That(Line.Announced, Is.GreaterThan(AfterStart), "the stop is announced");
            Assert.That(Names.Last(), Is.EqualTo(Tab.StateText), "with the words shown");
            Window.Close();
        }

        // ---- the booking page of a campsite of the last check, only on the user's action ----

        [AvaloniaTest]
        public void ACampsiteOfTheLastCheck_OpensItsBookingPage_OnlyWhenAsked()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            MonitoringViewModel Tab = ViewModel.MonitoringTab!;
            var Opened = new List<Uri>();
            var Copied = new List<string>();
            Tab.LinkLauncher = Link => { Opened.Add(Link); return Task.FromResult(true); };
            Tab.CopyText = Text => { Copied.Add(Text); return Task.FromResult(true); };
            Press(Tab.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            Tab.Update();
            Settle(Window);
            Assert.That(Tab.OpenBookingCommand.CanExecute(null), Is.False, "nothing chosen yet");
            Tab.SelectedRow = Tab.SelectedRows.First(R => R.HasCampsite);
            WaitFor(Tab.OpenBookingAsync());
            WaitFor(Tab.CopyBookingLinkAsync());
            WaitFor(Tab.CopyCampsiteIdAsync());
            Assert.That(Opened.Single().Host, Is.EqualTo("www.recreation.gov"));
            Assert.That(Copied, Is.EqualTo(new[] { Opened.Single().AbsoluteUri, Tab.SelectedRow!.CampsiteId }));
            Assert.That(Tab.ActionMessage, Does.StartWith("Copied the campsite ID"));
            Window.Close();
        }
    }
}
