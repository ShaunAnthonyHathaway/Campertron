using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.api;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's monitoring with the rest of the window (v0.4.0's phase 4, D115 and D118; business-rules-gui §41): a
    // refresh of the campground data pauses it for its time and starts it again afterwards, whatever its end, in this process,
    // or, after a refresh that restarts Campertron (D62), in the new process through the restart's one-time handoff, and only
    // when it ran just before; an ordinary start never starts it. The Results tab's run and the monitoring go on together as
    // in the web UI (manual-search semantics: the run notifies nothing and leaves Camp.History alone), and saving a settings
    // or search file while it runs is the settings editors' and the search editor's as before.
    public class MonitoringRefreshTests : DesktopMonitoringTestBase
    {
        private TestRefresh Refresh = null!;

        [SetUp]
        public void NewRefresh() => Refresh = new TestRefresh();

        private (MainWindow Window, MainWindowViewModel ViewModel) Show(RestartHandoff? Arrival = null)
        {
            var Store = new ThemePreferenceStore(Path.Join(Folders.Root, "preferences"));
            var System = new SystemSettingsViewModel(Store, Store.Load(), _ => { }, () => Application.Current?.ActualThemeVariant);
            return ShowWindow(Refresh: Refresh.Seams(), Arrival: Arrival, System: System);
        }

        private MainWindowViewModel Running(out MainWindow Window)
        {
            WriteSearch();
            (Window, MainWindowViewModel ViewModel) = Show();
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            return ViewModel;
        }

        private static CampgroundDataViewModel Card(MainWindowViewModel ViewModel) => ViewModel.SystemSettings.CampgroundData!;

        // Refresh campground data…, then Start refresh, until the test's refresh service is called
        private void BeginRefresh(MainWindow Window, MainWindowViewModel ViewModel)
        {
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Press(Card(ViewModel).RefreshCommand);
            Press(Card(ViewModel).StartCommand);
            Assert.That(Refresh.WaitForCall(), Is.True, "the refresh's service was called");
        }

        private static RidbRefreshResult Result(RidbRefreshOutcome Outcome, bool Installed = false) =>
            new RidbRefreshResult(Outcome, Installed ? RidbRefreshStage.Completed : RidbRefreshStage.Downloading, Installed, Installed ? DateTime.UtcNow : null,
                TimeSpan.FromSeconds(1), RidbRefreshResult.NoProblems, null, Outcome == RidbRefreshOutcome.Canceled ? new OperationCanceledException() : null, false);

        private static void EndRefresh(MainWindowViewModel ViewModel) => WaitUntil(() => !Card(ViewModel).IsRunning, () => "the refresh ended");

        // ---- the refresh pauses it, and starts it again here when Campertron does not restart ----

        [AvaloniaTest]
        public void ARefresh_PausesTheMonitoring_ForItsTime_AndAFailedRefreshStartsItAgainHere()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            Assert.That(CampgroundDataViewModel.ConfirmationText, Does.Contain(CampgroundDataViewModel.MonitoringPauseText), "the confirmation says so before it starts");
            BeginRefresh(Window, ViewModel);
            // while the refresh's service runs, the monitoring is paused: not running, its lock free, no action taken
            MonitoringControlView Paused = ViewModel.Monitoring!.Controller.View();
            Assert.That((Paused.Status.IsRunning, Paused.Paused, Paused.Resumes, Paused.CanStart), Is.EqualTo((false, true, true, false)));
            Assert.That(LockIsHeld(), Is.False, "no check runs while the refresh clears the cache folder");
            Assert.That(DesktopMonitoringWording.StateText(Paused), Does.StartWith("Paused while the campground data is refreshed (Settings > System)"));
            Assert.That(ViewModel.Monitoring.Controller.Act(MonitoringControlAction.Start, Paused.Token), Is.EqualTo(MonitoringControlOutcome.PausedForRefresh));
            Refresh.Finish(Result(RidbRefreshOutcome.DownloadFailed));
            EndRefresh(ViewModel);
            MonitoringControlView Again = Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running, "the monitoring started again after the refresh");
            Assert.That(Again.ThisStart!.AfterRefresh, Is.True);
            Assert.That(Refresh.Handoffs, Is.Empty, "no restart after a failed refresh");
            ShowMonitoringTab(Window);
            Assert.That(ViewModel.MonitoringTab!.StartedText, Is.EqualTo("2026-11-26 09:30" + MonitoringWording.AfterRefreshNote));
            Window.Close();
        }

        [AvaloniaTest]
        public void ACanceledRefresh_StartsTheMonitoringAgain_AndARefreshWithoutOne_StartsNothing()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            BeginRefresh(Window, ViewModel);
            Refresh.Finish(Result(RidbRefreshOutcome.Canceled));
            EndRefresh(ViewModel);
            Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running, "started again after the canceled refresh");
            Press(ViewModel.MonitoringTab!.StopCommandAfterUpdate());
            Ended(ViewModel);
            // not running when the refresh begins: nothing paused, nothing started afterwards
            BeginRefresh(Window, ViewModel);
            Assert.That(Card(ViewModel).StatusText, Does.Not.Contain("Pausing the monitoring"));
            Refresh.Finish(Result(RidbRefreshOutcome.DownloadFailed));
            EndRefresh(ViewModel);
            Thread.Sleep(100);
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.EqualTo(2), "a monitoring that did not run is not started");
            Window.Close();
        }

        // ---- a refresh that restarts Campertron hands the monitoring on (D62's handoff) ----

        [AvaloniaTest]
        public void ASuccessfulRefresh_HandsTheMonitoringOnToTheRestart_AndThisProcessDoesNotStartIt()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            ShowMonitoringTab(Window);
            BeginRefresh(Window, ViewModel);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitUntil(() => Refresh.Exits == 1, () => "the restarted process took over");
            RestartHandoff Handoff = Refresh.Handoffs.Single();
            Assert.That(Handoff.Monitoring, Is.True, "the new process starts the monitoring the refresh paused");
            MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
            Assert.That((View.Generation, View.Status.IsRunning), Is.EqualTo((1L, false)), "this process does not start it again: the new one does");
            Assert.That(LockIsHeld(), Is.False, "its lock is free for the new process");
            Assert.That(ViewModel.HoldsClose(true), Is.False, "the automatic restart's end is never held back by the close's question");
            Window.Close();
        }

        [AvaloniaTest]
        public void ARestartThatFails_StartsTheMonitoringAgainHere()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            Refresh.NextRestart = new RestartAttempt(RestartAttemptStatus.LaunchFailed, "Win32Exception");
            BeginRefresh(Window, ViewModel);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            MonitoringControlView Again = Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running, "started again after the failed restart");
            Assert.That(Again.ThisStart!.AfterRefresh, Is.True);
            Assert.That(Refresh.Handoffs.Single().Monitoring, Is.True);
            Assert.That(Refresh.Exits, Is.Zero);
            Window.Close();
        }

        [AvaloniaTest]
        public void ARefreshWithoutAMonitoring_HandsNothingOn()
        {
            WriteSearch();
            var (Window, ViewModel) = Show();
            BeginRefresh(Window, ViewModel);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitUntil(() => Refresh.Exits == 1, () => "the restarted process took over");
            Assert.That(Refresh.Handoffs.Single().Monitoring, Is.False);
            Assert.That(ViewModel.Monitoring!.Controller.View().Generation, Is.Zero);
            Window.Close();
        }

        // ---- the restarted process: its handoff alone starts it again; an ordinary start never does ----

        [AvaloniaTest]
        public void TheRestartedProcess_StartsTheMonitoringAgain_OnlyWhenItsHandoffSaysSo()
        {
            WriteSearch();
            var Resumes = new RestartHandoff(DateTime.UtcNow, RestartOutcome.Refreshed, null, RestartTopTab.Settings, RestartSettingsTab.System, Monitoring: true);
            var (Window, ViewModel) = Show(Resumes);
            Waited.Await();
            MonitoringControlView Started = Waiting(ViewModel);
            Assert.That((Started.Generation, Started.ThisStart!.AfterRefresh, Started.ThisStart.WithHost), Is.EqualTo((1L, true, false)));
            Assert.That(Card(ViewModel).StatusText, Is.EqualTo(CampgroundDataViewModel.RestartedText + CampgroundDataViewModel.MonitoringResumedText));
            Window.Close();
            ViewModel.Monitoring!.BeginShutdown().Wait(Long);

            var NotRunning = new RestartHandoff(DateTime.UtcNow, RestartOutcome.Refreshed, null, RestartTopTab.Settings, RestartSettingsTab.System, Monitoring: false);
            var (Second, Quiet) = Show(NotRunning);
            Thread.Sleep(100);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Quiet.Monitoring!.Controller.View().Generation, Is.Zero, "not running before the restart: not started after it");
            Assert.That(Card(Quiet).StatusText, Is.EqualTo(CampgroundDataViewModel.RestartedText));
            Second.Close();

            var (Third, Ordinary) = Show(Arrival: null);
            Thread.Sleep(100);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Ordinary.Monitoring!.Controller.View().Generation, Is.Zero, "an ordinary start never starts it (D115)");
            Assert.That((Requested.Count > 0, Prepared), Is.EqualTo((true, 1)), "only the first, restarted process asked recreation.gov");
            Third.Close();
        }

        // unsaved edits keep the window (no restart, D62): the monitoring starts again in this process
        [AvaloniaTest]
        public void UnsavedEditsKeepTheWindow_AndTheMonitoringStartsAgainHere()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            ShowSettingsTab(Window);
            ViewModel.Settings!.General.RefreshInterval.Text = "7";
            BeginRefresh(Window, ViewModel);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            Until(ViewModel, V => V.Generation == 2 && V.Phase == MonitoringControlPhase.Running, "started again here");
            Assert.That(Refresh.Handoffs, Is.Empty);
            Assert.That(Card(ViewModel).NeedsManualRestart, Is.True);
            Window.Close();
        }

        // ---- the Results tab and the editors while it runs ----

        // a run of the Results tab goes on beside the monitoring, as in the web UI (TD-54), with manual-search semantics: it
        // notifies nothing and never reads, writes, suppresses or re-arms Camp.History
        [AvaloniaTest]
        public void AResultsRun_GoesOnBesideTheMonitoring_AndNotifiesNothing()
        {
            var Gov = new TestGov();
            WriteSearch();
            Folders.Write("Results.yaml", ResultsFolders.Search("Manual trip", false));
            var Store = new ThemePreferenceStore(Path.Join(Folders.Root, "preferences"));
            var (Window, ViewModel) = ShowWindow(Results: ResultsTestHelpers.Seams(Gov, Timers, Time));
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            List<string> Before = Markers();
            int Sent = Emails.Count;
            Assert.That(Before, Is.Not.Empty, "the monitoring recorded what it delivered");
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            ResultsSearchRow Manual = ResultsTestHelpers.Row(ViewModel.Results!, "Results.yaml");
            Manual.IsSelected = true;
            ResultsTestHelpers.Row(ViewModel.Results!, "Alpha.yaml").IsSelected = false;
            SearchRunResult Run = ResultsTestHelpers.Run(ViewModel.Results!);
            Assert.That(Run.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Run.Searches.Single().Openings, Is.Not.Empty, "the run found the openings the monitoring had notified");
            Assert.That(Emails.Count, Is.EqualTo(Sent), "a manual run notifies nothing");
            Assert.That(Markers(), Is.EqualTo(Before), "a manual run never touches Camp.History");
            MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
            Assert.That((View.Phase, View.Generation), Is.EqualTo((MonitoringControlPhase.Running, 1L)), "the monitoring went on");
            Window.Close();
        }

        // saving a settings file or a search file while it runs is the editors' as before: saved, the monitoring goes on with
        // what it read when it started, and the tab says that the search files changed (Restart reads them)
        [AvaloniaTest]
        public void SavingSettingsAndSearchFilesWhileItRuns_IsTheEditorsAsBefore()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            ShowSettingsTab(Window);
            ViewModel.Settings!.General.RefreshInterval.Text = "14";
            Press(ViewModel.Settings.General.SaveCommand);
            Assert.That(File.ReadAllText(Folders.In("General.yaml")), Does.Contain("refreshRidbDataDayInterval: 14"));
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchFileViewModel Search = ViewModel.Searches!.Files.Single(F => F.FileName == "Alpha.yaml");
            ViewModel.Searches.SelectedFile = Search;
            Search.TotalHumans.Text = "3";
            Press(Search.SaveCommand);
            Assert.That(File.ReadAllText(Folders.In("Alpha.yaml")), Does.Contain("totalHumans: 3"));
            MonitoringControlView View = Until(ViewModel, V => V.Status.SearchFilesChangedSinceStart, "the files changed since its start");
            Assert.That((View.Phase, View.Generation), Is.EqualTo((MonitoringControlPhase.Running, 1L)), "saving neither stops nor restarts it");
            ShowMonitoringTab(Window);
            Assert.That(ViewModel.MonitoringTab!.ShowsFilesChanged, Is.True);
            Window.Close();
        }
    }

    internal static class MonitoringTestCommands
    {
        // the tab's Stop, once the tab has read the monitoring as it is now
        internal static System.Windows.Input.ICommand StopCommandAfterUpdate(this MonitoringViewModel Tab)
        {
            Tab.Update();
            return Tab.StopCommand;
        }
    }
}
