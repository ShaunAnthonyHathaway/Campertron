using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.data;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // R8 startup: the window opens first and shows "Preparing campground data" while Load.GetConfig runs on a background
    // thread; the tabs and their database-backed ReactiveViewModel exist only after it succeeds. A failure is shown in
    // the window with the settings folder and Try again. The tests replace Load.GetConfig with a loader they control;
    // after the gate opens it calls the real Load.GetConfig against the fixture environment.
    public class StartupTests
    {
        // A loader the test controls: each call waits for its gate (if any) and then fails or loads the real config.
        private sealed class TestLoader
        {
            private readonly Queue<(ManualResetEventSlim? Gate, Exception? Error)> _Plan = new Queue<(ManualResetEventSlim?, Exception?)>();
            private int _Calls;

            public int Calls => Volatile.Read(ref _Calls);

            public TestLoader Then(Exception? Error = null, ManualResetEventSlim? Gate = null)
            {
                _Plan.Enqueue((Gate, Error));
                return this;
            }

            public CampertronInternalConfig Load()
            {
                Interlocked.Increment(ref _Calls);
                (ManualResetEventSlim? Gate, Exception? Error) Step;
                lock (_Plan)
                {
                    Step = _Plan.Count > 0 ? _Plan.Dequeue() : (null, null);
                }
                if (Step.Gate != null && !Step.Gate.Wait(TimeSpan.FromSeconds(20)))
                {
                    throw new TimeoutException("test gate was never opened");
                }
                if (Step.Error != null)
                {
                    throw Step.Error;
                }
                return CampertronLibrary.function.RecDotOrg.data.Load.GetConfig();
            }
        }

        private const string InvalidSettings = "(Line: 5, Col: 1, Idx: 110) - (Line: 5, Col: 6, Idx: 115): Property 'theme' not found on type 'GeneralConfig'.";

        private static (MainWindow Window, MainWindowViewModel ViewModel) Show(TestLoader Loader)
        {
            return ShowMainWindow(new MainWindowViewModel(Loader.Load));
        }

        [AvaloniaTest]
        public void WhilePreparing_ShowsProgress_HidesTheTabs_AndRunsNoDatabaseQuery()
        {
            using var Queries = new QueryCounter(AllThreads: true);
            using var Gate = new ManualResetEventSlim();
            var Loader = new TestLoader().Then(Gate: Gate);
            var (Window, ViewModel) = Show(Loader);
            WaitUntil(() => Loader.Calls == 1, () => "the loader was not called once the window opened");

            Assert.That(ViewModel.State, Is.EqualTo(StartupState.Preparing));
            Assert.That(ViewModel.ReactiveViewModel, Is.Null, "no database-backed view model before the startup succeeded");
            StartupStatusView Status = Named<StartupStatusView>(Window, "StartupStatus");
            Assert.That(Status.IsVisible, Is.True);
            Assert.That(Named<Border>(Status, "PreparingCard").IsEffectivelyVisible, Is.True);
            Assert.That(Named<ScrollViewer>(Status, "ErrorScrollViewer").IsVisible, Is.False);
            Assert.That(Named<TextBlock>(Status, "PreparingHeading").Text, Is.EqualTo("Preparing campground data…"));
            Assert.That(Named<ProgressBar>(Status, "PreparingProgress").IsIndeterminate, Is.True);
            Window.UpdateLayout();
            Assert.That(Named<Border>(Status, "PreparingCard").Bounds.Width, Is.EqualTo(960).Within(1), "the card fills the capped column at 1024");
            Assert.That(Named<TabControl>(Window, "MainTabControl").IsVisible, Is.False);
            foreach (string Content in ContentNames)
            {
                Assert.That(Named<Grid>(Window, Content).DataContext, Is.Null, Content);
            }
            Assert.That(Named<ComboBox>(Window, "ParkComboBox").ItemCount, Is.EqualTo(0));
            Assert.That(Queries.TakeCount(), Is.EqualTo(0), "a database query ran before the startup succeeded");

            Gate.Set();
            WaitForState(ViewModel, StartupState.Ready);
            Assert.That(Named<TabControl>(Window, "MainTabControl").IsVisible, Is.True);
            Assert.That(Named<StartupStatusView>(Window, "StartupStatus").IsVisible, Is.False);
            Assert.That(Queries.TakeCount(), Is.GreaterThan(0), "the lists query the database once the startup succeeded");
            Window.Close();
        }

        [AvaloniaTest]
        public void Success_CreatesTheReadyViewModelWithTheLoadedConfigPath()
        {
            var Loader = new TestLoader();
            var (Window, ViewModel) = Show(Loader);
            WaitForState(ViewModel, StartupState.Ready);

            Assert.That(Loader.Calls, Is.EqualTo(1));
            Assert.That(ViewModel.ReactiveViewModel, Is.Not.Null);
            Assert.That(ViewModel.ConfigPath, Is.EqualTo(GuiTestEnvironment.ConfigPath));
            Assert.That(ViewModel.ReactiveViewModel!.ConfigPath, Is.EqualTo(GuiTestEnvironment.ConfigPath));
            Assert.That(ViewModel.ErrorMessage, Is.Null);
            for (int Index = 0; Index < ContentNames.Length; Index++)
            {
                Assert.That(SelectTab(Window, Index).DataContext, Is.SameAs(ViewModel.ReactiveViewModel), ContentNames[Index]);
            }
            SelectTab(Window, 0);
            Assert.That(Items(Named<ComboBox>(Window, "ParkComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.ParkIdentity, GuiTestEnvironment.ParkOne, GuiTestEnvironment.ParkThree, GuiTestEnvironment.ParkTwo }));
            Assert.That(ViewModel.Retry(), Is.False, "nothing to retry after success");
            Assert.That(Loader.Calls, Is.EqualTo(1));
            Window.Close();
        }

        [AvaloniaTest]
        public void Failure_KeepsTheWindowOpen_WithASelectableMessageFolderAndTryAgain()
        {
            using var Queries = new QueryCounter(AllThreads: true);
            var Loader = new TestLoader().Then(new InvalidDataException(InvalidSettings));
            var (Window, ViewModel) = Show(Loader);
            WaitForState(ViewModel, StartupState.Failed);

            Assert.That(Window.IsVisible, Is.True);
            Assert.That(ViewModel.ReactiveViewModel, Is.Null);
            Assert.That(Named<TabControl>(Window, "MainTabControl").IsVisible, Is.False);
            StartupStatusView Status = Named<StartupStatusView>(Window, "StartupStatus");
            Assert.That(Named<Border>(Status, "PreparingCard").IsEffectivelyVisible, Is.False);
            Assert.That(Named<Border>(Status, "ErrorCard").IsEffectivelyVisible, Is.True);
            Window.UpdateLayout();
            Assert.That(Named<Border>(Status, "ErrorCard").Bounds.Width, Is.EqualTo(960).Within(1), "the card fills the capped column at 1024");
            Assert.That(Named<Border>(Status, "ErrorCard").Bounds.Height, Is.LessThan(Window.ClientSize.Height / 2), "the card stretches to the window height");
            TextBlock Heading = Named<TextBlock>(Status, "ErrorHeading");
            Assert.That(Heading.Text, Is.EqualTo("Campertron couldn't prepare its campground data"));
            Assert.That(AutomationProperties.GetHeadingLevel(Heading), Is.EqualTo(1));

            SelectableTextBlock Message = Named<SelectableTextBlock>(Status, "ErrorMessageText");
            SelectableTextBlock Folder = Named<SelectableTextBlock>(Status, "ConfigPathText");
            Assert.That(Message.Text, Is.EqualTo(InvalidSettings));
            Assert.That(Folder.Text, Is.EqualTo(GuiTestEnvironment.ConfigPath), "the GUI's default settings folder (LocalApplicationData/CampertronConfig)");
            foreach (SelectableTextBlock Selectable in new[] { Message, Folder })
            {
                Assert.That(Selectable.IsTabStop, Is.True, Selectable.Name + " is not reachable with the keyboard");
                Assert.That(Selectable.Padding.Left, Is.GreaterThanOrEqualTo(4), Selectable.Name + ": focus rectangle touches the text");
                Assert.That(Selectable.Margin.Left, Is.EqualTo(-Selectable.Padding.Left), Selectable.Name + ": text not aligned with its label");
                Assert.That(ControlAutomationPeer.CreatePeerForElement(Selectable).GetName(), Is.EqualTo(Selectable.Text), Selectable.Name);
            }
            Assert.That(AutomationProperties.GetHelpText(Message), Is.EqualTo("Error"));
            Assert.That(AutomationProperties.GetHelpText(Folder), Is.EqualTo("Settings folder"));
            Assert.That(Named<TextBlock>(Status, "ErrorAdviceText").Text, Does.Contain("Fix or delete that file, then select Try again"));

            // Try again has the focus, and Tab reaches the message, the folder and Try again.
            Button Retry = Named<Button>(Status, "RetryButton");
            Assert.That(Focused(Window), Is.SameAs(Retry));
            var Visited = new List<string?>();
            for (int Press = 0; Press < 3; Press++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Visited.Add((Focused(Window) as Control)?.Name);
            }
            Assert.That(Visited, Is.EqualTo(new[] { "ErrorMessageText", "ConfigPathText", "RetryButton" }), string.Join(", ", Visited));
            Assert.That(Queries.TakeCount(), Is.EqualTo(0), "a database query ran although the startup failed");
            Window.Close();
        }

        // Try again starts one new attempt; requests while it runs are ignored; repeated failures stay stable; a later
        // success reaches the ready state.
        [AvaloniaTest]
        public void Retry_StartsOneNewAttempt_IgnoresOverlaps_AndCanSucceedAfterRepeatedFailures()
        {
            using var SecondGate = new ManualResetEventSlim();
            var Loader = new TestLoader()
                .Then(new InvalidDataException("first failure"))
                .Then(new InvalidDataException("second failure"), SecondGate)
                .Then(new InvalidDataException("third failure"));
            var (Window, ViewModel) = Show(Loader);
            WaitForState(ViewModel, StartupState.Failed);
            Assert.That(ViewModel.ErrorMessage, Is.EqualTo("first failure"));

            Button Retry = Named<Button>(Named<StartupStatusView>(Window, "StartupStatus"), "RetryButton");
            Retry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.State, Is.EqualTo(StartupState.Preparing));
            Assert.That(ViewModel.ErrorMessage, Is.Null);
            Assert.That(ViewModel.Retry(), Is.False, "a retry while an attempt runs must be ignored");
            Retry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            WaitUntil(() => Loader.Calls == 2, () => $"expected 2 attempts, got {Loader.Calls}");
            Assert.That(Loader.Calls, Is.EqualTo(2), "overlapping retries started another attempt");

            SecondGate.Set();
            WaitForState(ViewModel, StartupState.Failed);
            Assert.That(ViewModel.ErrorMessage, Is.EqualTo("second failure"));

            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            WaitForState(ViewModel, StartupState.Failed);
            WaitUntil(() => ViewModel.ErrorMessage == "third failure", () => "third attempt did not fail: " + ViewModel.ErrorMessage);
            Assert.That(Named<SelectableTextBlock>(Named<StartupStatusView>(Window, "StartupStatus"), "ErrorMessageText").Text, Is.EqualTo("third failure"));
            Assert.That(Window.IsVisible, Is.True);

            Assert.That(ViewModel.Retry(), Is.True);
            WaitForState(ViewModel, StartupState.Ready);
            Assert.That(Loader.Calls, Is.EqualTo(4));
            Assert.That(ViewModel.ErrorMessage, Is.Null);
            Assert.That(Named<TabControl>(Window, "MainTabControl").IsVisible, Is.True);
            Assert.That(Items(Named<ComboBox>(Window, "ParkComboBox")), Has.Count.EqualTo(4));
            Window.Close();
        }

        // Closing the window while Load.GetConfig still runs is clean, and its later completion changes nothing.
        [AvaloniaTest]
        public void ClosingWhilePreparing_IsClean_AndTheLateCompletionChangesNothing()
        {
            using var Log = new AvaloniaLogCapture();
            using var Gate = new ManualResetEventSlim();
            var Loader = new TestLoader().Then(Gate: Gate);
            var (Window, ViewModel) = Show(Loader);
            WaitUntil(() => Loader.Calls == 1, () => "the loader was not called");
            int Closed = 0;
            Window.Closed += (_, _) => Closed++;
            Exception? Unhandled = null;
            Dispatcher.UIThread.UnhandledException += (_, e) => Unhandled = e.Exception;

            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Closed, Is.EqualTo(1));
            Assert.That(ViewModel.PendingPreparation.IsCompleted, Is.False);

            Gate.Set();
            Assert.That(ViewModel.PendingPreparation.Wait(TimeSpan.FromSeconds(20)), Is.True);
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.PendingPreparation.IsFaulted, Is.False);
            Assert.That(ViewModel.State, Is.EqualTo(StartupState.Preparing), "a closed window's startup must not change state");
            Assert.That(ViewModel.ReactiveViewModel, Is.Null);
            Assert.That(ViewModel.Retry(), Is.False);
            Assert.That(Loader.Calls, Is.EqualTo(1));
            Assert.That(Unhandled, Is.Null);
            Assert.That(Log.Entries, Is.Empty);
        }

        // The loader's exception becomes the Failed state; the background task itself never faults, so nothing is left
        // unobserved.
        [AvaloniaTest]
        public void LoaderExceptions_AreObserved_AndTheStartupTaskNeverFaults()
        {
            int Unobserved = 0;
            EventHandler<UnobservedTaskExceptionEventArgs> Handler = (_, _) => Interlocked.Increment(ref Unobserved);
            TaskScheduler.UnobservedTaskException += Handler;
            try
            {
                var Loader = new TestLoader().Then(new UnauthorizedAccessException("Access to the path is denied."));
                var (Window, ViewModel) = Show(Loader);
                WaitForState(ViewModel, StartupState.Failed);
                Assert.That(ViewModel.PendingPreparation.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(ViewModel.ErrorMessage, Is.EqualTo("Access to the path is denied."));
                Window.Close();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Assert.That(Unobserved, Is.EqualTo(0));
            }
            finally
            {
                TaskScheduler.UnobservedTaskException -= Handler;
            }
        }

        [AvaloniaTest]
        public void Start_RunsOneAttemptOnly()
        {
            var Loader = new TestLoader();
            var (Window, ViewModel) = Show(Loader);
            ViewModel.Start();
            ViewModel.Start();
            WaitForState(ViewModel, StartupState.Ready);
            Assert.That(ViewModel.HasStarted, Is.True);
            Assert.That(Loader.Calls, Is.EqualTo(1));
            Window.Close();
        }

        [Test]
        public void ErrorDescription_IsTheMessageOnly_UnwrappedAndShortened()
        {
            Assert.That(MainWindowViewModel.Describe(new AggregateException(new IOException("disk full"))), Is.EqualTo("disk full"));
            Assert.That(MainWindowViewModel.Describe(new InvalidDataException("line one\r\nline two\n")), Is.EqualTo("line one line two"));
            string Long = MainWindowViewModel.Describe(new InvalidDataException(new string('x', 2000)));
            Assert.That(Long.Length, Is.EqualTo(500));
            Assert.That(Long, Does.EndWith("…"));
            Assert.That(MainWindowViewModel.Describe(null), Is.EqualTo("Unknown error."));
            var Yaml = new YamlDotNet.Core.YamlException(new YamlDotNet.Core.Mark(110, 5, 1), new YamlDotNet.Core.Mark(115, 5, 6),
                "Property 'theme' not found on type 'GeneralConfig'.");
            Assert.That(MainWindowViewModel.Describe(Yaml), Is.EqualTo("Property 'theme' not found on type 'GeneralConfig'. (line 5, column 1)"));
        }
    }
}
