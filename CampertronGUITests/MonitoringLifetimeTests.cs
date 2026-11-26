using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI monitoring's lifetime (v0.4.0's phase 4, D115; business-rules-gui §41): the monitoring is the
    // application's; the user's close of the window while it is on asks in the window (Keep Campertron open, the default and
    // Escape, changes nothing; Stop the monitoring and close stops it within the controller's bound, off the UI thread, then
    // closes), one question at a time; Campertron's own programmatic close (the automatic restart's, the smoke test's) and the
    // operating system's shutdown never ask; and whatever closes the window, the monitoring is stopped with it, nothing of it
    // is left running and no action starts it afterwards.
    public class MonitoringLifetimeTests : DesktopMonitoringTestBase
    {
        // the user's close, as the platform asks for it (the window's own close button or Alt+F4): Avalonia's Window.HandleClosing,
        // which the platform's close notification calls (not programmatic; the headless platform has no button to press).
        // True when the window held it back; otherwise the platform would close it
        private static bool UserClose(MainWindow Window, WindowCloseReason Reason = WindowCloseReason.WindowClosing)
        {
            System.Reflection.MethodInfo Handle = typeof(Window).GetMethod("HandleClosing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                new[] { typeof(WindowCloseReason) }) ?? throw new AssertionException("Avalonia's Window.HandleClosing(WindowCloseReason) was not found");
            bool Held = (bool)Handle.Invoke(Window, new object[] { Reason })!;
            Dispatcher.UIThread.RunJobs();
            return Held;
        }

        private MainWindowViewModel Running(out MainWindow Window)
        {
            WriteSearch();
            (Window, MainWindowViewModel ViewModel) = ShowWindow();
            ShowMonitoringTab(Window);
            Press(ViewModel.MonitoringTab!.StartCommand);
            Waited.Await();
            Waiting(ViewModel);
            ViewModel.MonitoringTab.Update();
            Settle(Window);
            return ViewModel;
        }

        [AvaloniaTest]
        public void ClosingWithoutAMonitoring_ClosesAsBefore()
        {
            WriteSearch();
            var (Window, ViewModel) = ShowWindow();
            Assert.That(UserClose(Window), Is.False, "nothing runs: the close goes on");
            Assert.That((ViewModel.IsConfirmingClose, Window.IsVisible), Is.EqualTo((false, false)), "closed at once, as before");
        }

        [AvaloniaTest]
        public void ClosingWhileItRuns_Asks_AndKeepOpenLeavesEverythingAsItWas()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            Button Stop = InTab<Button>(Window, "StopMonitoringButton");
            Stop.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            Assert.That(UserClose(Window), Is.True, "the close is held back");
            Settle(Window);
            Assert.That(ViewModel.IsConfirmingClose, Is.True);
            Assert.That(Named<Border>(Window, "CloseConfirmationScrim").IsEffectivelyVisible, Is.True);
            Assert.That(Named<TextBlock>(Window, "CloseConfirmationHeading").Text, Is.EqualTo(DesktopMonitoringWording.CloseHeading));
            Assert.That(Named<TextBlock>(Window, "CloseConfirmationText").Text, Is.EqualTo(DesktopMonitoringWording.CloseText));
            Assert.That(ViewModel.CloseStateText, Is.EqualTo("Running: the next check starts at 2026-11-26 09:31."), "the question says what runs");
            Assert.That(Focused(Window), Is.SameAs(Named<Button>(Window, "KeepOpenButton")), "the first, safe choice has the focus");
            Assert.That(Tabs(Window).IsEffectivelyEnabled, Is.False, "the tabs wait for the answer");
            // asked again: the same question, never a second one
            Assert.That(UserClose(Window), Is.True);
            Assert.That(Window.GetVisualDescendants().OfType<Border>().Count(B => B.Name == "CloseConfirmation" && B.IsEffectivelyVisible), Is.EqualTo(1));

            Press(ViewModel.KeepOpenCommand);
            Settle(Window);
            Assert.That(ViewModel.IsConfirmingClose, Is.False);
            Assert.That(Named<Border>(Window, "CloseConfirmationScrim").IsEffectivelyVisible, Is.False);
            Assert.That(Focused(Window), Is.SameAs(Stop), "the focus goes back where it was");
            MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
            Assert.That((View.Phase, View.Generation, View.Status.StopRequested, View.ShuttingDown), Is.EqualTo((MonitoringControlPhase.Running, 1L, false, false)),
                "canceling the close stops nothing");
            Assert.That(LockIsHeld(), Is.True);
            Assert.That(Window.IsVisible, Is.True);

            // Escape is Keep Campertron open
            Assert.That(UserClose(Window), Is.True);
            Settle(Window);
            Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(Window);
            Assert.That(ViewModel.IsConfirmingClose, Is.False);
            Assert.That(ViewModel.Monitoring.Controller.View().Phase, Is.EqualTo(MonitoringControlPhase.Running));
            Window.Close();
        }

        [AvaloniaTest]
        public void StopAndClose_StopsTheMonitoring_ThenClosesTheWindow_AndNothingIsLeft()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            bool Closed = false;
            Window.Closed += (_, _) => Closed = true;
            Assert.That(UserClose(Window), Is.True);
            Settle(Window);
            Press(ViewModel.StopAndCloseCommand);
            WaitUntil(() => Closed, () => "the window closes once the monitoring has stopped");
            MonitoringControlView View = ViewModel.Monitoring!.Controller.View();
            Assert.That((View.Status.IsRunning, View.ShuttingDown, View.IsActive), Is.EqualTo((false, true, false)));
            Assert.That(ViewModel.Monitoring.Stopping!.IsCompleted, Is.True);
            Assert.That(LockIsHeld(), Is.False, "no monitoring is left: its lock is free");
            Assert.That(ViewModel.MonitoringTab!.IsDetached, Is.True);
            Assert.That(ViewModel.Monitoring.Controller.Act(MonitoringControlAction.Start, ViewModel.Monitoring.Controller.View().Token),
                Is.EqualTo(MonitoringControlOutcome.ShuttingDown), "nothing starts it after the close");
            Assert.That(DesktopMonitoringWording.Outcome(MonitoringControlOutcome.ShuttingDown, null), Is.EqualTo(DesktopMonitoringWording.Host.ShuttingDownText));
            int Checks = Waited.Count;
            Thread.Sleep(200);
            Assert.That(Waited.Count, Is.EqualTo(Checks), "no check after the close");
        }

        // a check that is sending finishes its notifications and records them before the window closes (D76, ER-36); the
        // window shows that it is stopping and asks nothing more meanwhile
        [AvaloniaTest]
        public void StopAndClose_WhileACheckSends_WaitsForItsNotifications_ShowingThatItStops()
        {
            WriteSearch();
            var Sending = new ManualResetEventSlim(false);
            var Release = new ManualResetEventSlim(false);
            Send = _ =>
            {
                Sending.Set();
                Release.Wait(TimeSpan.FromSeconds(8));
            };
            var (Window, ViewModel) = ShowWindow();
            bool Closed = false;
            Window.Closed += (_, _) => Closed = true;
            Press(ViewModel.MonitoringTab!.StartCommand);
            Assert.That(Sending.Wait(Long), Is.True, "the first check sends");
            Assert.That(UserClose(Window), Is.True);
            Settle(Window);
            Press(ViewModel.StopAndCloseCommand);
            Settle(Window);
            Assert.That(ViewModel.IsStoppingForClose, Is.True);
            Assert.That(Named<TextBlock>(Window, "CloseStoppingText").IsEffectivelyVisible, Is.True);
            Assert.That(Named<TextBlock>(Window, "CloseStoppingText").Text, Is.EqualTo(DesktopMonitoringWording.StoppingText));
            Assert.That(Focused(Window), Is.SameAs(Named<TextBlock>(Window, "CloseStoppingText")), "its words have the focus");
            Assert.That((ViewModel.KeepOpenCommand.CanExecute(null), ViewModel.StopAndCloseCommand.CanExecute(null)), Is.EqualTo((false, false)), "no second choice");
            Assert.That(UserClose(Window), Is.True, "a close meanwhile waits for the stop");
            Assert.That(Closed, Is.False, "the window waits while the check sends");
            Release.Set();
            WaitUntil(() => Closed, () => "the window closes once the check has sent and recorded");
            Assert.That(Emails.Count, Is.EqualTo(1));
            Assert.That(Markers(), Is.Not.Empty, "what the check delivered is recorded");
            Assert.That(LockIsHeld(), Is.False);
        }

        // Campertron's own close (the automatic restart's end, the smoke test's) and the operating system's shutdown never
        // ask; the monitoring stops with the window all the same
        [AvaloniaTest]
        public void AProgrammaticClose_OrTheSystemsShutdown_DoesNotAsk_ButStopsTheMonitoring()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            Assert.That(UserClose(Window, WindowCloseReason.OSShutdown), Is.False, "the operating system's shutdown is never held back");
            Assert.That((ViewModel.IsConfirmingClose, Window.IsVisible), Is.EqualTo((false, false)), "closed at once");
            Assert.That(ViewModel.PendingMonitoringStop, Is.Not.Null, "the window's close began the monitoring's stop");
            Assert.That(ViewModel.PendingMonitoringStop!.Wait(Long), Is.True);
            Assert.That((ViewModel.Monitoring!.Controller.View().Status.IsRunning, LockIsHeld()), Is.EqualTo((false, false)), "nothing of it is left");

            MainWindowViewModel Again = Running(out MainWindow Second);
            Second.Close();
            Assert.That((Again.IsConfirmingClose, Second.IsVisible), Is.EqualTo((false, false)), "a programmatic close is never held back");
            Assert.That(Again.PendingMonitoringStop!.Wait(Long), Is.True);
            Assert.That((Again.Monitoring!.Controller.View().Status.IsRunning, LockIsHeld()), Is.EqualTo((false, false)));
        }

        // a monitoring that has stopped or failed is not asked about: nothing would be stopped, and the close goes on
        [AvaloniaTest]
        public void AMonitoringThatEnded_IsNotAskedAbout()
        {
            MainWindowViewModel ViewModel = Running(out MainWindow Window);
            Press(ViewModel.MonitoringTab!.StopCommand);
            Ended(ViewModel);
            Assert.That(UserClose(Window), Is.False, "stopped: the close goes on");
            Assert.That((ViewModel.IsConfirmingClose, Window.IsVisible), Is.EqualTo((false, false)));

            // failed (D76): not running, nothing to ask about
            Send = _ => throw new System.Net.Mail.SmtpException(System.Net.Mail.SmtpStatusCode.MailboxUnavailable, "refused");
            Directory.Delete(Folders.In("Camp.History"), true);
            var (Second, Failed) = ShowWindow();
            Press(Failed.MonitoringTab!.StartCommand);
            Until(Failed, V => V.ShowsFailure, "the failed send ended it");
            Assert.That(UserClose(Second), Is.False);
            Assert.That((Failed.IsConfirmingClose, Second.IsVisible), Is.EqualTo((false, false)));
        }

        // the close's question is the shared view's IsActive: starting, running, restarting and a pause that starts it again
        // are on; idle, stopping, ended and the host's stop are not
        [Test]
        public void WhatTheCloseAsksAbout_IsTheSharedViewsIsActive()
        {
            MonitoringStatus Idle = new MonitoringStatus { State = MonitoringState.NotStarted };
            MonitoringStatus Runs = new MonitoringStatus { State = MonitoringState.Waiting };
            MonitoringStatus Stopping = new MonitoringStatus { State = MonitoringState.Waiting, StopRequested = true };
            Assert.That(Runs.IsRunning && Stopping.IsRunning && !Idle.IsRunning, Is.True, "the statuses mean what the cases say");
            Assert.That(new MonitoringControlView { Status = Idle }.IsActive, Is.False);
            Assert.That(new MonitoringControlView { Status = Runs }.IsActive, Is.True);
            Assert.That(new MonitoringControlView { Status = Stopping }.IsActive, Is.False, "a stop asked for");
            Assert.That(new MonitoringControlView { Status = Idle, Busy = MonitoringControlBusy.Starting }.IsActive, Is.True);
            Assert.That(new MonitoringControlView { Status = Runs, Busy = MonitoringControlBusy.Restarting }.IsActive, Is.True);
            Assert.That(new MonitoringControlView { Status = Runs, Busy = MonitoringControlBusy.Stopping }.IsActive, Is.False);
            Assert.That(new MonitoringControlView { Status = Idle, Busy = MonitoringControlBusy.Ending }.IsActive, Is.False);
            Assert.That(new MonitoringControlView { Status = Idle, Paused = true, Resumes = true }.IsActive, Is.True, "paused for a refresh that starts it again");
            Assert.That(new MonitoringControlView { Status = Idle, Paused = true, Resumes = false }.IsActive, Is.False);
            Assert.That(new MonitoringControlView { Status = Runs, ShuttingDown = true }.IsActive, Is.False, "the host already stops");
        }
    }
}
