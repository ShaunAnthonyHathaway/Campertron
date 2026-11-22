using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.sqlite;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // Settings > System > Campground data (V011-1, decision D61): the last refresh and Refresh campground data…, which asks
    // first, then runs the library's refresh service on a worker thread with progress, Cancel while it can still stop, the
    // result in words and, since D62, the automatic restart after a success (its lifecycle here; the handoff, the launch
    // and the window placement in AutomaticRestartTests); the Results tab's search and the refresh exclude each other;
    // closing the window cancels a refresh that can stop and lets one that is installing finish. Every test has its own folders (the
    // fixture database), its own theme store, and a refresh the test drives (TestRefresh) or the real service against a
    // held lock (no download): nothing reaches the network, the clipboard or a file manager.
    public class CampgroundDataTests
    {
        private ResultsFolders Folders = null!;
        private string Preferences = string.Empty;

        [SetUp]
        public void CreateFolders()
        {
            Folders = new ResultsFolders();
            Preferences = Path.Join(GuiTestEnvironment.Root, "campground-data-preferences-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteFolders()
        {
            Folders.Dispose();
            if (Directory.Exists(Preferences))
            {
                Directory.Delete(Preferences, true);
            }
        }

        // ---- the card ----

        [AvaloniaTest]
        public void TheCard_IsBelowTheCurrentConfiguration_WithTheLastRefreshAndItsAction()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataView View = Card(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;

            Assert.That(View.IsEffectivelyVisible, Is.True);
            Assert.That(View.DataContext, Is.SameAs(Data));
            CurrentConfigurationView Configuration = Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<CurrentConfigurationView>("CurrentConfigurationContent")!;
            Assert.That(View.TranslatePoint(new Point(0, 0), Window)!.Value.Y, Is.GreaterThan(Configuration.TranslatePoint(new Point(0, 0), Window)!.Value.Y));
            Assert.That(In<TextBlock>(View, "CampgroundDataHeading").Text, Is.EqualTo("Campground data"));
            Assert.That(In<TextBlock>(View, "CampgroundDataExplanation").Text, Does.Contain("about 570 MB").And.Contain("several minutes"));
            Assert.That(In<TextBlock>(View, "LastRefreshedLabel").Text, Is.EqualTo("Last refreshed"));
            string Expected = ResultsFoldersLastRefresh();
            Assert.That(In<SelectableTextBlock>(View, "LastRefreshedValue").Text, Is.EqualTo(Expected));
            Assert.That(In<Button>(View, "RefreshDatabaseButton").IsEffectivelyEnabled, Is.True);
            Assert.That(In<Border>(View, "RefreshConfirmation").IsVisible, Is.False);
            Assert.That(In<StackPanel>(View, "RefreshProgressPanel").IsVisible, Is.False);
            Assert.That(In<TextBlock>(View, "RefreshStatusText").IsVisible, Is.False);
            Assert.That(In<TextBlock>(View, "RefreshUnavailableText").IsVisible, Is.False);
            Assert.That(Refresh.Calls, Is.Zero);
            Window.Close();
        }

        [AvaloniaTest]
        public void TheLastRefresh_IsReadReadOnly_AndAgainWhenSystemIsShownOrTheWindowActivated()
        {
            var (Window, ViewModel) = Show(new TestRefresh());
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            ShowSystem(Window);
            Dictionary<string, string> Before = Fingerprint(Folders.Config);

            // the sample's date, 50 years back: "never"
            Folders.Write("General.yaml", "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 1976-11-18T00:00:00.0000000Z\nautoRefresh: false\n");
            int Reads = Data.Reads;
            Window.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Data.Reads, Is.GreaterThan(Reads));
            Assert.That(Data.LastRefreshedText, Does.StartWith("never"));
            Assert.That(In<SelectableTextBlock>(Card(Window), "LastRefreshedValue").Text, Does.StartWith("never"));

            // a file a start would refuse: unknown, and nothing created or changed
            File.Delete(Folders.In("General.yaml"));
            SettingsTabs(Window).SelectedIndex = MainWindow.CampertronSettingsTabIndex;
            ShowSystem(Window);
            Assert.That(Data.LastRefreshedText, Is.EqualTo("unknown"));
            Assert.That(File.Exists(Folders.In("General.yaml")), Is.False, "never created");
            Before.Remove("General.yaml");
            Assert.That(Fingerprint(Folders.Config), Is.EquivalentTo(Before));
            Window.Close();
        }

        // ---- asking first ----

        [AvaloniaTest]
        public void TheConfirmation_StartsNothing_AndBackOrEscapeClosesIt()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataView View = Card(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;

            Click(In<Button>(View, "RefreshDatabaseButton"));
            Settle(Window);
            Assert.That(Data.IsConfirming, Is.True);
            Assert.That(In<Border>(View, "RefreshConfirmation").IsEffectivelyVisible, Is.True);
            Assert.That(In<StackPanel>(View, "RefreshActions").IsVisible, Is.False);
            Assert.That(In<TextBlock>(View, "RefreshConfirmationHeading").Text, Is.EqualTo("Refresh the campground data?"));
            Assert.That(In<TextBlock>(View, "RefreshConfirmationText").Text, Does.Contain("about 570 MB").And.Contain("Recreation.gov").And.Contain("RIDB export")
                .And.Contain("several minutes").And.Contain("you can cancel until the new data is being installed")
                // D62: the automatic restart, where it reopens, no promise of the exact place, and the manual fallback
                .And.Contain("restarts automatically to load the new data").And.Contain("reopens at Settings > System")
                .And.Contain("when the system allows it").And.Contain("the new data stays installed and you can restart Campertron yourself"));
            Assert.That(FocusName(Window), Is.EqualTo("StartRefreshButton"), "the confirmation's next step has the focus");
            Assert.That(Refresh.Calls, Is.Zero, "asking starts nothing");
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));

            Click(In<Button>(View, "RefreshBackButton"));
            Settle(Window);
            Assert.That(Data.IsConfirming, Is.False);
            Assert.That(In<Border>(View, "RefreshConfirmation").IsVisible, Is.False);
            Assert.That(FocusName(Window), Is.EqualTo("RefreshDatabaseButton"));

            Click(In<Button>(View, "RefreshDatabaseButton"));
            Settle(Window);
            Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Settle(Window);
            Assert.That(Data.IsConfirming, Is.False, "Escape is Back");
            Assert.That(FocusName(Window), Is.EqualTo("RefreshDatabaseButton"));
            Assert.That(Refresh.Calls, Is.Zero);
            Assert.That(Data.HasStatus, Is.False);
            Window.Close();
        }

        // ---- running ----

        [AvaloniaTest]
        public void StartRefresh_RunsTheServiceOnAWorkerThread_WithTheStartupsFolders_AndHoldsTheDataGate()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            int UiThread = Environment.CurrentManagedThreadId;

            Begin(Window, Refresh);

            Assert.That(Refresh.Calls, Is.EqualTo(1));
            Assert.That(Refresh.Request!.ConfigPath, Is.EqualTo(Folders.Config));
            Assert.That(Refresh.Request.CachePath, Is.EqualTo(Folders.Cache));
            Assert.That(Refresh.Thread, Is.Not.EqualTo(UiThread), "a worker thread");
            Assert.That(Data.IsRunning, Is.True);
            Assert.That(Data.CanCancel, Is.True);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.Refresh));
            Assert.That(In<StackPanel>(Card(Window), "RefreshActions").IsVisible, Is.False);
            Assert.That(FocusName(Window), Is.EqualTo("CancelRefreshButton"));
            Assert.That(Data.StatusText, Is.EqualTo("Refreshing the campground data: starting."));

            // one refresh at a time: another Start does nothing
            Data.RequestRefresh();
            Data.Start();
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(Refresh.Calls, Is.EqualTo(1));

            // the automatic restart fails here: the gate is released and Refresh is offered again
            Refresh.NextRestart = new RestartAttempt(RestartAttemptStatus.LaunchFailed, "Win32Exception");
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(Data);
            WaitForRestart(Data);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.True);
            Window.Close();
        }

        [AvaloniaTest]
        public void Progress_ShowsTheStageTheCountsAndTheElapsedTime_ABarOnlyWhereTheTotalIsKnown()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            CampgroundDataView View = Card(Window);
            Begin(Window, Refresh);
            ProgressBar Bar = In<ProgressBar>(View, "RefreshProgressBar");

            Report(Window, Refresh, RidbRefreshStage.Downloading, 0, null);
            Assert.That(Data.StatusText, Is.EqualTo("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            Assert.That(Bar.IsIndeterminate, Is.True, "no length from the server: no bar");
            Report(Window, Refresh, RidbRefreshStage.Downloading, 12_345_678, null);
            Assert.That(Data.ProgressDetails, Does.StartWith("12.3 MB downloaded · Elapsed "));
            Report(Window, Refresh, RidbRefreshStage.Downloading, 114_000_000, 570_000_000);
            Assert.That(Bar.IsIndeterminate, Is.False);
            Assert.That(Bar.Value / Bar.Maximum, Is.EqualTo(0.2).Within(0.0001));
            Assert.That(Data.ProgressDetails, Does.StartWith("114.0 MB of 570.0 MB downloaded"));
            Assert.That(In<TextBlock>(View, "RefreshProgressDetails").IsEffectivelyVisible, Is.True);

            Report(Window, Refresh, RidbRefreshStage.Extracting, 2, 6);
            Assert.That(Bar.IsIndeterminate, Is.False);
            Assert.That(Data.ProgressDetails, Does.StartWith("2 of 6 files extracted"));
            Report(Window, Refresh, RidbRefreshStage.Importing, 3, 6);
            Assert.That(Data.ProgressDetails, Does.StartWith("3 of 6 files read"));
            Assert.That(Data.StatusText, Is.EqualTo("Refreshing the campground data: reading the campground data."));
            Report(Window, Refresh, RidbRefreshStage.Validating, null, null);
            Assert.That(Bar.IsIndeterminate, Is.True);
            Assert.That(Data.ProgressDetails, Does.StartWith("Elapsed "));

            // the elapsed time, every second
            Refresh.Time.Advance(TimeSpan.FromSeconds(83));
            Refresh.Timers.Tick();
            Assert.That(Data.ElapsedText, Does.StartWith("1:2"));
            Refresh.Finish(Result(RidbRefreshOutcome.Canceled, Error: new OperationCanceledException()));
            WaitForEnd(Data);
            Assert.That(In<StackPanel>(View, "RefreshProgressPanel").IsVisible, Is.False, "the progress gives way to the result");
            Assert.That(Bar.IsEffectivelyVisible, Is.False);
            Window.Close();
        }

        [AvaloniaTest]
        public void TheStatusLine_AnnouncesEachStageOnce_NeverEachByte()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            var Announced = new List<string?>();
            Data.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CampgroundDataViewModel.StatusText))
                {
                    Announced.Add(Data.StatusText);
                }
            };

            for (int i = 0; i < 5; i++)
            {
                Report(Window, Refresh, RidbRefreshStage.Downloading, i * 1_000_000, 10_000_000);
            }
            Report(Window, Refresh, RidbRefreshStage.Extracting, 1, 6);
            Report(Window, Refresh, RidbRefreshStage.Extracting, 2, 6);

            Assert.That(Announced, Is.EqualTo(new[]
            {
                "Refreshing the campground data: downloading the campground data from Recreation.gov.",
                "Refreshing the campground data: extracting the downloaded files.",
            }));
            Assert.That(AutomationProperties.GetLiveSetting(In<TextBlock>(Card(Window), "RefreshStatusText")), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(AutomationProperties.GetLiveSetting(In<TextBlock>(Card(Window), "RefreshProgressDetails")), Is.Not.EqualTo(AutomationLiveSetting.Polite));
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(Data);
            WaitForRestart(Data);
            Assert.That(Announced[^1], Is.EqualTo(CampgroundDataViewModel.RestartingText), "the restart is announced, once");
            Assert.That(Announced.Count(a => a == CampgroundDataViewModel.RestartingText), Is.EqualTo(1));
            Window.Close();
        }

        // ---- cancel ----

        [AvaloniaTest]
        public void Cancel_StopsTheRefresh_AndSaysTheDataStaysAsItIs()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Downloading, 1_000_000, null);

            Click(In<Button>(Card(Window), "CancelRefreshButton"));
            Assert.That(Refresh.Token.IsCancellationRequested, Is.True);
            Assert.That(Data.IsCanceling, Is.True);
            Assert.That(Data.CancelLabel, Is.EqualTo("Canceling…"));
            Assert.That(Data.CanCancel, Is.False, "once");
            Assert.That(Data.StatusText, Does.StartWith("Canceling: the refresh stops at its next safe point"));

            Refresh.Finish(Result(RidbRefreshOutcome.Canceled, Error: new OperationCanceledException(), CancelRequested: true));
            WaitForEnd(Data);
            Assert.That(Data.StatusText, Does.StartWith("The refresh was canceled. The campground data was not changed."));
            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Canceled));
            Assert.That(Data.ResultIsError, Is.False);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            Assert.That(FocusName(Window), Is.EqualTo("RefreshDatabaseButton"), "the focus goes back to the card's action");
            Window.Close();
        }

        [AvaloniaTest]
        public void Cancel_IsUnavailableFromTheCacheStepOn_AndTheCardSaysItIsFinishingSafely()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Finalizing, null, null);
            Assert.That(Data.CanCancel, Is.True);

            Report(Window, Refresh, RidbRefreshStage.ClearingCache, null, null, CanCancel: false);
            Assert.That(Data.CanCancel, Is.False);
            Assert.That(Data.IsFinishing, Is.True);
            Assert.That(In<Button>(Card(Window), "CancelRefreshButton").IsEffectivelyEnabled, Is.False);
            Assert.That(Data.StatusText, Does.StartWith("Clearing the cache. Finishing safely").And.Contain("This can't be canceled now"));
            Data.Cancel();
            Assert.That(Refresh.Token.IsCancellationRequested, Is.False, "Cancel does nothing now");
            Report(Window, Refresh, RidbRefreshStage.Installing, null, null, CanCancel: false);
            Assert.That(Data.StatusText, Does.StartWith("Installing the new campground data. Finishing safely"));
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(Data);
            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Success));
            WaitForRestart(Data);
            Window.Close();
        }

        [AvaloniaTest]
        public void ACancelThatCameTooLate_SaysTheRefreshFinished()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            Data.Cancel();
            // the service had already passed its last checkpoint
            Report(Window, Refresh, RidbRefreshStage.Installing, null, null, CanCancel: false);
            Assert.That(Data.StatusText, Does.StartWith("Too late to cancel: installing the new campground data."));
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true, CancelRequested: true));
            WaitForEnd(Data);
            Assert.That(Data.StatusText, Is.EqualTo("The refresh had already reached the installation when Cancel was selected, so it finished. " + CampgroundDataViewModel.RestartingText));
            WaitForRestart(Data);
            Assert.That(Refresh.Exits, Is.EqualTo(1), "and Campertron restarts: the refresh succeeded");
            Window.Close();
        }

        // ---- the result ----

        [AvaloniaTest]
        public void Success_RestartsCampertron_WithTheWindowsPlace_ShowsTheNewDate_AndReloadsNothing()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            ReactiveViewModel Lookup = ViewModel.ReactiveViewModel!;
            SearchesViewModel Searches = ViewModel.Searches!;
            ResultsViewModel Results = ViewModel.Results!;
            CurrentConfigurationViewModel Configuration = ViewModel.SystemSettings.Configuration!;
            Begin(Window, Refresh);

            // what the service does at its end: the new date in General.yaml
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true), Before: () =>
                File.WriteAllText(Folders.In("General.yaml"), File.ReadAllText(Folders.In("General.yaml")).Replace("2026-01-01T00:00:00.0000000Z", DateTime.UtcNow.ToString("o"))));
            WaitForEnd(Data);

            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Success));
            Assert.That(Data.StatusText, Is.EqualTo(CampgroundDataViewModel.RestartingText));
            WaitForRestart(Data);
            // D62: one handoff with the window's normal bounds and Settings > System, then this process ends (the test's exit)
            Assert.That(Refresh.Handoffs.Count, Is.EqualTo(1));
            Assert.That(Refresh.Handoffs.TryPeek(out RestartHandoff? Handoff), Is.True);
            Assert.That(Handoff!.Outcome, Is.EqualTo(RestartOutcome.Refreshed));
            Assert.That(Handoff.CreatedUtc, Is.EqualTo(TestRefresh.Now));
            Assert.That(Handoff.Tab, Is.EqualTo(RestartTopTab.Settings));
            Assert.That(Handoff.SettingsTab, Is.EqualTo(RestartSettingsTab.System));
            Assert.That(Handoff.Window, Is.EqualTo(new RestartWindow(Window.Position.X, Window.Position.Y, Window.ClientSize.Width, Window.ClientSize.Height, false)));
            Assert.That(Data.LastRestart!.Status, Is.EqualTo(RestartAttemptStatus.Acknowledged));
            Assert.That(Refresh.Exits, Is.EqualTo(1), "acknowledged: this process ends");
            Assert.That(Data.IsRestarting, Is.True, "until it has ended: no Refresh, no Cancel");
            Assert.That(Data.NeedsManualRestart, Is.False);
            Assert.That(Data.ShowsRefreshButton, Is.False);
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.Refresh), "no search starts meanwhile");
            Assert.That(Data.LastRefreshedText, Does.StartWith("today"));
            Assert.That(In<SelectableTextBlock>(Card(Window), "LastRefreshedValue").Text, Does.StartWith("today"));
            Assert.That(Configuration.Items[5].Value, Does.StartWith("today"), "the current configuration shows it too");
            Assert.That(ViewModel.ReactiveViewModel, Is.SameAs(Lookup), "no live reload");
            Assert.That(ViewModel.Searches, Is.SameAs(Searches));
            Assert.That(ViewModel.Results, Is.SameAs(Results));
            Assert.That(ViewModel.SystemSettings.Configuration, Is.SameAs(Configuration));
            Window.Close();
        }

        [AvaloniaTest]
        public void AfterTheRefresh_ACleanGeneralEditorReloads_AndOneWithChangesShowsAConflict([Values(false, true)] bool Edited)
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSettingsTab(Window, MainWindow.CampertronSettingsTabIndex);
            Settle(Window);
            GeneralSettingsViewModel General = ViewModel.Settings!.General;
            if (Edited)
            {
                General.RefreshInterval.Text = "45";
                Dispatcher.UIThread.RunJobs();
                Assert.That(General.IsDirty, Is.True);
            }
            ShowSystem(Window);
            Begin(Window, Refresh);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true), Before: () =>
                File.WriteAllText(Folders.In("General.yaml"), File.ReadAllText(Folders.In("General.yaml")).Replace("2026-01-01T00:00:00.0000000Z", "2026-11-18T12:00:00.0000000Z")));
            WaitForEnd(ViewModel.SystemSettings.CampgroundData!);

            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            if (Edited)
            {
                Assert.That(General.HasConflict, Is.True, "unsaved changes meet the refresh's new date as a conflict");
                Assert.That(General.RefreshInterval.Text, Is.EqualTo("45"), "the edit is kept");
                // D62: an automatic restart would lose the edit, so there is none: the window stays and says why
                Assert.That(Refresh.Handoffs, Is.Empty);
                Assert.That(Refresh.Exits, Is.Zero);
                Assert.That(Data.StatusText, Does.StartWith(CampgroundDataViewModel.UnsavedChangesText));
                Assert.That(Data.NeedsManualRestart, Is.True);
                Assert.That(Data.LastRestart!.Detail, Is.EqualTo("UnsavedChanges"));
                Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            }
            else
            {
                Assert.That(General.HasConflict, Is.False);
                Assert.That(General.IsDirty, Is.False);
                Assert.That(General.IsEditable, Is.True, "a clean editor reloaded the file");
                Assert.That(General.LastRefresh.Text, Does.Contain("2026-11-18"), "it shows the refresh's new date");
                WaitForRestart(Data);
                Assert.That(Refresh.Handoffs.Count, Is.EqualTo(1));
                Assert.That(Refresh.Exits, Is.EqualTo(1));
            }
            Window.Close();
        }

        // Every outcome in its own words, by kind: no path, file content, exception message or stack trace.
        [Test]
        public void EveryOutcome_HasItsOwnWords_WithoutPathsOrExceptionMessages()
        {
            var Texts = new Dictionary<RidbRefreshOutcome, (RefreshResultKind Kind, string Text)>();
            var Secret = new InvalidOperationException("/home/someone/.local/share/CampertronConfig " + ResultsFolders.Sentinel);
            foreach (RidbRefreshOutcome Outcome in Enum.GetValues<RidbRefreshOutcome>())
            {
                bool Installed = Outcome is RidbRefreshOutcome.Refreshed or RidbRefreshOutcome.RefreshedWithoutTimestamp or RidbRefreshOutcome.InstalledButCheckFailed;
                var Ended = new RidbRefreshResult(Outcome, RidbRefreshStage.Downloading, Installed, Installed ? DateTime.UtcNow : null, TimeSpan.FromSeconds(3),
                    new[] { "/home/someone/problem " + ResultsFolders.Sentinel }, "/home/someone/detail " + ResultsFolders.Sentinel, Secret, false);
                Texts[Outcome] = CampgroundDataViewModel.Describe(Ended);
            }
            Assert.That(Texts.Values.Select(t => t.Text).Distinct().Count(), Is.EqualTo(Texts.Count), "each outcome its own words");
            Assert.That(Texts.Values.Where(t => t.Text.Contains('/') || t.Text.Contains(ResultsFolders.Sentinel) || t.Text.Contains("   at ") || t.Text.Contains("Exception:")), Is.Empty);
            Assert.That(Texts[RidbRefreshOutcome.Refreshed].Kind, Is.EqualTo(RefreshResultKind.Success));
            Assert.That(Texts[RidbRefreshOutcome.RefreshedWithoutTimestamp].Kind, Is.EqualTo(RefreshResultKind.Warning));
            Assert.That(Texts[RidbRefreshOutcome.Canceled].Kind, Is.EqualTo(RefreshResultKind.Canceled));
            foreach (RidbRefreshOutcome Failed in Texts.Keys.Except(new[] { RidbRefreshOutcome.Refreshed, RidbRefreshOutcome.RefreshedWithoutTimestamp, RidbRefreshOutcome.Canceled }))
            {
                Assert.That(Texts[Failed].Kind, Is.EqualTo(RefreshResultKind.Failure), Failed.ToString());
            }
            // D62: a success restarts Campertron, whose own words follow (RestartingText and the others): the result says no more
            Assert.That(Texts[RidbRefreshOutcome.Refreshed].Text, Is.EqualTo("The campground data was refreshed."));
            Assert.That(Texts[RidbRefreshOutcome.RefreshedWithoutTimestamp].Text, Does.StartWith("The campground data was refreshed, but").And.Not.Contain("Restart"));
            foreach (RidbRefreshOutcome Unchanged in new[]
            {
                RidbRefreshOutcome.Canceled, RidbRefreshOutcome.AnotherRefreshRunning, RidbRefreshOutcome.DownloadFailed, RidbRefreshOutcome.DownloadTimedOut,
                RidbRefreshOutcome.ExtractionFailed, RidbRefreshOutcome.ImportFailed, RidbRefreshOutcome.ValidationFailed, RidbRefreshOutcome.StorageFailed,
                RidbRefreshOutcome.InstallationFailed,
            })
            {
                Assert.That(Texts[Unchanged].Text, Does.EndWith("The campground data was not changed."), Unchanged.ToString());
            }
            Assert.That(Texts[RidbRefreshOutcome.DownloadTimedOut].Text, Does.Contain("stopped responding"));
            Assert.That(Texts[RidbRefreshOutcome.InstalledButCheckFailed].Text, Does.Contain("was installed").And.Contain("found a problem"));
            Assert.That(Texts[RidbRefreshOutcome.Unexpected].Text, Does.Contain("(InvalidOperationException)"), "an unexpected problem by its type only");
            var InstalledThenUnexpected = new RidbRefreshResult(RidbRefreshOutcome.Unexpected, RidbRefreshStage.RecordingRefresh, true, DateTime.UtcNow, TimeSpan.Zero,
                RidbRefreshResult.NoProblems, null, new NullReferenceException(), false);
            Assert.That(CampgroundDataViewModel.Describe(InstalledThenUnexpected).Text, Does.StartWith("The new campground data was installed").And.Contain("(NullReferenceException)"));
        }

        [AvaloniaTest]
        public void AFailure_IsShownAsAnErrorInWords_AndTheCardCanTryAgain()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            string Before = Data.LastRefreshedText;
            Begin(Window, Refresh);
            Refresh.Finish(Result(RidbRefreshOutcome.DownloadFailed, Error: new HttpRequestException("Connection refused " + ResultsFolders.Sentinel)));
            WaitForEnd(Data);

            TextBlock Status = In<TextBlock>(Card(Window), "RefreshStatusText");
            Assert.That(Status.Text, Does.StartWith("The download from Recreation.gov failed. Check the internet connection and try again. The campground data was not changed."));
            Assert.That(Status.Text, Does.Not.Contain(ResultsFolders.Sentinel));
            Assert.That(SeverityOf(Status), Is.EqualTo("Error"));
            Assert.That(Data.LastRefreshedText, Is.EqualTo(Before));
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.True, "Refresh campground data… again");
            Window.Close();
        }

        [AvaloniaTest]
        public void AWarning_SaysTheDataIsNewButTheDateWasNotRecorded()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            // the data is new, so Campertron restarts and the new process words the warning (its handoff carries it); here the
            // restart fails, so this window says it, with the manual restart
            Refresh.NextRestart = new RestartAttempt(RestartAttemptStatus.LaunchFailed);
            Refresh.Finish(Result(RidbRefreshOutcome.RefreshedWithoutTimestamp, Installed: true, Error: new IOException("General.yaml " + ResultsFolders.Sentinel)));
            WaitForEnd(Data);
            WaitForRestart(Data);
            Assert.That(Refresh.Handoffs.TryPeek(out RestartHandoff? Handoff), Is.True);
            Assert.That(Handoff!.Outcome, Is.EqualTo(RestartOutcome.RefreshedWithoutTimestamp));
            Assert.That(Data.ResultKind, Is.EqualTo(RefreshResultKind.Warning));
            Assert.That(Data.ResultIsError, Is.False);
            Assert.That(Data.StatusText, Does.StartWith(CampgroundDataViewModel.RestartFailedText + CampgroundDataViewModel.TimestampNote));
            Assert.That(Data.StatusText, Does.Not.Contain(ResultsFolders.Sentinel));
            Window.Close();
        }

        // The real service, against a refresh lock another process holds: the typed result, before anything is downloaded.
        [AvaloniaTest]
        public void AnotherRefresh_InAnotherProcess_IsReported_AndNothingChanges()
        {
            var (Window, ViewModel) = Show(Refresh: null);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            using var Requests = new WebRequestListener();
            Requests.Prove();
            using (FileStream? Held = RidbStagedRefresh.TryAcquireRefreshLock(Folders.Config, out _))
            {
                Assert.That(Held, Is.Not.Null);
                Click(In<Button>(Card(Window), "RefreshDatabaseButton"));
                Click(In<Button>(Card(Window), "StartRefreshButton"));
                WaitForEnd(Data);
            }

            Assert.That(Data.LastResult!.Outcome, Is.EqualTo(RidbRefreshOutcome.AnotherRefreshRunning));
            Assert.That(Data.StatusText, Does.StartWith("Another refresh of the campground data is running"));
            Dictionary<string, string> After = Fingerprint(Folders.Config);
            Assert.That(After.Remove(RidbStagedRefresh.RefreshLockFileName), Is.True, "the other process's lock file");
            Assert.That(After, Is.EquivalentTo(Before));
            Assert.That(Requests.Count, Is.Zero, "no download");
            Window.Close();
        }

        // ---- the Results tab and the refresh ----

        [AvaloniaTest]
        public void TheResultsSearch_AndTheRefresh_ExcludeEachOther_EachWithItsReason()
        {
            var Gov = new TestGov { Answer = TestGov.Stall };
            var Timers = new ManualTimers();
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh, Seams(Gov, Timers));
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            ResultsViewModel Results = ViewModel.Results!;
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);

            // a search runs: the refresh is unavailable, with its reason
            Start(Results);
            Assert.That(Gov.Reached.Wait(10000), Is.True);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.Search));
            ShowSystem(Window);
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(In<TextBlock>(Card(Window), "RefreshUnavailableText").IsEffectivelyVisible, Is.True);
            Assert.That(In<TextBlock>(Card(Window), "RefreshUnavailableText").Text, Is.EqualTo(CampgroundDataViewModel.SearchRunningReason));
            Results.Cancel();
            ResultsTestHelpers.WaitForEnd(Results);
            Assert.That(ViewModel.DataOperations.Current, Is.EqualTo(DataOperation.None));
            Assert.That(Data.RefreshCommand.CanExecute(null), Is.True);
            Assert.That(Data.HasUnavailableReason, Is.False);

            // a refresh runs: Start is unavailable, with its reason
            Begin(Window, Refresh);
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Assert.That(Results.CanStart, Is.False);
            Assert.That(Results.StartCommand.CanExecute(null), Is.False);
            TextBlock Notice = Named<ResultsView>(Window, "ResultsContent").FindControl<TextBlock>("DataOperationNoticeText")!;
            Assert.That(Notice.IsEffectivelyVisible, Is.True);
            Assert.That(Notice.Text, Does.Contain("campground data is refreshed"));
            Results.Start();
            Assert.That(Results.IsRunning, Is.False, "nothing starts");
            // the restart fails: the window stays, and the gate is free again
            Refresh.NextRestart = new RestartAttempt(RestartAttemptStatus.ChildExited);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(Data);
            WaitForRestart(Data);
            Settle(Window);
            Assert.That(Results.StartCommand.CanExecute(null), Is.True);
            Assert.That(Notice.IsVisible, Is.False);
            Window.Close();
        }

        [AvaloniaTest]
        public void Navigation_AndTheSettingsEditors_StayAvailableWhileARefreshRuns()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Importing, 2, 6);

            foreach (int Tab in new[] { MainWindow.SearchesTabIndex, MainWindow.ResultsTabIndex, MainWindow.CampsitesTabIndex })
            {
                Tabs(Window).SelectedIndex = Tab;
                Settle(Window);
                Assert.That(Tabs(Window).SelectedIndex, Is.EqualTo(Tab));
                Assert.That(((TabItem)Tabs(Window).SelectedItem!).IsEffectivelyEnabled, Is.True);
            }
            ShowSettingsTab(Window, MainWindow.CampertronSettingsTabIndex);
            Settle(Window);
            Assert.That(ViewModel.Settings!.General.IsEditable, Is.True, "the settings stay editable");
            Assert.That(ViewModel.SystemSettings.CampgroundData!.IsRunning, Is.True, "and the refresh goes on");
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(ViewModel.SystemSettings.CampgroundData!);
            WaitForRestart(ViewModel.SystemSettings.CampgroundData!);
            Window.Close();
        }

        // ---- closing the window ----

        [AvaloniaTest]
        public void ClosingTheWindow_CancelsARefreshThatCanStop_AndNothingChangesTheClosedWindow()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Downloading, 1_000_000, null);
            string? Status = Data.StatusText;

            Window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.That(Data.IsDetached, Is.True);
            Assert.That(Refresh.Token.IsCancellationRequested, Is.True, "the close cancels a refresh that can stop");
            Assert.That(ViewModel.PendingRefresh.IsCompleted, Is.False, "the process waits for it");

            Refresh.FinishWhenCanceled();
            Assert.That(ViewModel.PendingRefresh.Wait(10000), Is.True);
            Dispatcher.UIThread.RunJobs();
            RidbRefreshResult Ended = ViewModel.PendingRefresh.Result!;
            Assert.That(Ended.Outcome, Is.EqualTo(RidbRefreshOutcome.Canceled));
            Assert.That(Data.StatusText, Is.EqualTo(Status), "the closed window is not changed");
            Assert.That(Data.WasShown(Ended), Is.False);
            var Error = new StringWriter();
            App.ReportRefreshAfterClose(Ended, Error);
            Assert.That(Error.ToString(), Is.Empty, "a cancel by the close is not reported");
        }

        [AvaloniaTest]
        public void ClosingTheWindowDuringTheInstallation_LetsItFinish_BeforeTheProcessEnds()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Installing, null, null, CanCancel: false);

            Window.Close();
            Dispatcher.UIThread.RunJobs();
            // the service no longer reads the token: the wait lasts until the installation has finished
            Assert.That(ViewModel.PendingRefresh.Wait(300), Is.False, "still installing: Program.Main keeps waiting");
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true, CancelRequested: true));
            Assert.That(ViewModel.PendingRefresh.Wait(10000), Is.True);
            Assert.That(ViewModel.PendingRefresh.Result!.Outcome, Is.EqualTo(RidbRefreshOutcome.Refreshed));
            var Error = new StringWriter();
            App.ReportRefreshAfterClose(ViewModel.PendingRefresh.Result, Error);
            Assert.That(Error.ToString(), Is.Empty, "a success needs no report");
            // D62: the window was closed: an ordinary close, no automatic restart
            Dispatcher.UIThread.RunJobs();
            Assert.That(Refresh.Handoffs, Is.Empty);
            Assert.That(Refresh.Exits, Is.Zero);
        }

        [Test]
        public void AFailureAfterTheWindowClosed_IsOneLineOnStandardError_InTheCardsWords()
        {
            var Error = new StringWriter();
            var Failed = new RidbRefreshResult(RidbRefreshOutcome.InstallationFailed, RidbRefreshStage.Installing, false, null, TimeSpan.Zero,
                new[] { "/home/someone/x" }, "/home/someone/y", new IOException("/home/someone/z"), true);
            App.ReportRefreshAfterClose(Failed, Error);
            App.ReportRefreshAfterClose(null, Error);
            string[] Lines = Error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(Lines, Has.Length.EqualTo(1));
            Assert.That(Lines[0], Is.EqualTo("Campertron: the campground data refresh did not finish after the window closed: " + CampgroundDataViewModel.Describe(Failed).Text));
            Assert.That(Lines[0], Does.Not.Contain("/home"));
        }

        // ---- threads and identity ----

        [AvaloniaTest]
        public void TheWorker_ReachesTheWindowOnlyThroughItsPosts_AndAnEarlierRefreshChangesNothing()
        {
            var Refresh = new TestRefresh();
            int UiThread = Environment.CurrentManagedThreadId;
            var PostedFrom = new ConcurrentBag<int>();
            var Ran = new ConcurrentBag<int>();
            Refresh.Post = Job =>
            {
                PostedFrom.Add(Environment.CurrentManagedThreadId);
                Dispatcher.UIThread.Post(() => { Ran.Add(Environment.CurrentManagedThreadId); Job(); });
            };
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataViewModel Data = ViewModel.SystemSettings.CampgroundData!;
            Begin(Window, Refresh);
            Report(Window, Refresh, RidbRefreshStage.Downloading, 1, null);
            IProgress<RidbRefreshProgress> First = Refresh.Progress!;
            long FirstId = Data.CurrentRefreshId;
            Refresh.Finish(Result(RidbRefreshOutcome.DownloadFailed, Error: new HttpRequestException("x")));
            WaitForEnd(Data);

            Begin(Window, Refresh);
            Assert.That(Data.CurrentRefreshId, Is.GreaterThan(FirstId));
            string? Status = Data.StatusText;
            // a late report of the first refresh: its gate is closed, nothing is shown
            First.Report(new RidbRefreshProgress(999, RidbRefreshStage.Installing, false, null, null));
            Refresh.Time.Advance(TimeSpan.FromSeconds(1));
            Settle(Window);
            Assert.That(Data.StatusText, Is.EqualTo(Status));
            Assert.That(Data.IsFinishing, Is.False);
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(Data);
            WaitForRestart(Data);

            Assert.That(PostedFrom.Any(t => t != UiThread), Is.True, "the worker posts");
            Assert.That(Ran.All(t => t == UiThread), Is.True, "every job runs on the UI thread");
            Window.Close();
        }

        // ---- accessibility, layout and themes ----

        [AvaloniaTest]
        public void TheCard_IsAccessible()
        {
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            ShowSystem(Window);
            CampgroundDataView View = Card(Window);
            Assert.That(AutomationProperties.GetHeadingLevel(In<TextBlock>(View, "CampgroundDataHeading")), Is.EqualTo(2));
            Assert.That(AutomationProperties.GetHelpText(In<SelectableTextBlock>(View, "LastRefreshedValue")), Is.EqualTo("Last refreshed"));
            Assert.That(In<SelectableTextBlock>(View, "LastRefreshedValue").IsTabStop, Is.False);
            AssertButton(View, "RefreshDatabaseButton", "Refresh campground data", "Shows what the refresh does before it starts");
            Click(In<Button>(View, "RefreshDatabaseButton"));
            Settle(Window);
            Assert.That(AutomationProperties.GetHeadingLevel(In<TextBlock>(View, "RefreshConfirmationHeading")), Is.EqualTo(3));
            AssertButton(View, "StartRefreshButton", "Start refresh", "Start downloading the campground data from Recreation.gov now");
            AssertButton(View, "RefreshBackButton", "Back, don't refresh", "Close this without refreshing (Escape)");
            Assert.That(PressTab(Window, 2), Is.EqualTo(new[] { "RefreshBackButton", "Tab:Settings" }), "the confirmation's buttons, then the wrap");
            Click(In<Button>(View, "StartRefreshButton"));
            Settle(Window);
            AssertButton(View, "CancelRefreshButton", "Cancel the refresh", "Stop the refresh; the campground data stays as it is");
            Assert.That(ControlAutomationPeer.CreatePeerForElement(In<ProgressBar>(View, "RefreshProgressBar")).GetName(), Is.EqualTo("Campground data refresh progress"));
            Assert.That(AutomationProperties.GetLiveSetting(In<TextBlock>(View, "RefreshStatusText")), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(FocusName(Window), Is.EqualTo("CancelRefreshButton"));
            Assert.That(PressTab(Window, 1), Is.EqualTo(new[] { "Tab:Settings" }), "while it runs: Cancel, then the wrap");
            Refresh.Finish(Result(RidbRefreshOutcome.Refreshed, Installed: true));
            WaitForEnd(ViewModel.SystemSettings.CampgroundData!);
            WaitForRestart(ViewModel.SystemSettings.CampgroundData!);
            Window.Close();
        }

        // The card fits the window in Light and Dark at the smallest, the default and a wide size, idle, confirming and
        // running: no horizontal overflow, every text and button inside the window, colors only from the theme, no binding
        // error or other log entry.
        [AvaloniaTest]
        public void TheCard_FitsInLightAndDark([Values("Light", "Dark")] string Variant, [Values(720, 1024, 2400)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            var Refresh = new TestRefresh();
            var (Window, ViewModel) = Show(Refresh);
            Application.Current!.RequestedThemeVariant = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Resize(Window, Width, Width == 720 ? 540 : Width == 1024 ? 768 : 1350);
            ShowSystem(Window);
            CampgroundDataView View = Card(Window);
            AssertFits(Window, View);
            Click(In<Button>(View, "RefreshDatabaseButton"));
            Settle(Window);
            AssertFits(Window, View);
            Click(In<Button>(View, "StartRefreshButton"));
            Report(Window, Refresh, RidbRefreshStage.Downloading, 123_456_789, 570_000_000);
            AssertFits(Window, View);
            Refresh.Finish(Result(RidbRefreshOutcome.StorageFailed, Error: new IOException("x")));
            WaitForEnd(ViewModel.SystemSettings.CampgroundData!);
            AssertFits(Window, View);
            Assert.That(Log.BindingEntries, Is.Empty);
            Assert.That(Log.Entries, Is.Empty);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            Window.Close();
        }

        // From the IL: the card reaches the library's refresh service only through RefreshSeams; it never writes a settings
        // file, starts a process, writes to the console, uses the clipboard or the launcher, or makes a web request itself.
        [Test]
        public void TheCardsCode_RefreshesOnlyThroughItsSeam()
        {
            List<string> Called = ReferencedMembers(typeof(CampgroundDataViewModel), typeof(CampgroundDataView), typeof(DataOperationGate)).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.CurrentConfiguration::ReadGeneral"));
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.api.", "EmailConfig", "CampertronLibrary.function.Base.Yaml::", "CampertronLibrary.function.Base.ConfigFileReplace::",
                "ConfigEditSession", "System.IO.File::", "System.IO.Directory::", "System.Diagnostics.Process", "System.Console", "Clipboard", "Launcher",
                "CampsiteSearch", "System.Net.", "RidbStagedRefresh", "Clear::", "Web::",
            };
            Assert.That(Called.Where(m => Forbidden.Any(f => m.Contains(f, StringComparison.Ordinal))), Is.Empty);
            Assert.That(ReferencedMembers(typeof(RefreshSeams)).Select(c => c.Member), Does.Contain("CampertronLibrary.function.RecDotOrg.api.RidbRefreshService::Run"));
            // D62: the card restarts Campertron only through RefreshSeams.Restart; it never writes a handoff or starts a process
            Assert.That(Called.Where(m => m.Contains("AutomaticRestart", StringComparison.Ordinal) || m.Contains("RestartHandoffStore", StringComparison.Ordinal)
                || m.Contains("ProcessStartInfo", StringComparison.Ordinal)), Is.Empty);
            Assert.That(ReferencedMembers(typeof(RefreshSeams)).Select(c => c.Member), Does.Contain("Campertron.AutomaticRestart::Run"));
        }

        // ---- helpers ----

        private (MainWindow Window, MainWindowViewModel ViewModel) Show(TestRefresh? Refresh, ResultsSeams? Results = null)
        {
            File.Copy(GuiTestEnvironment.DbFile, Folders.In("RecreationDotOrg.db"), overwrite: true);
            var Store = new ThemePreferenceStore(Preferences);
            var System = new SystemSettingsViewModel(Store, Store.Load(), _ => { }, () => Application.Current?.ActualThemeVariant);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Folders.Config, CachePath = Folders.Cache },
                Results, System, Refresh?.Seams());
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            Settle(Window);
            return (Window, ViewModel);
        }

        // the fixture's General.yaml date in the card's words
        private static string ResultsFoldersLastRefresh() => CampertronLibrary.function.Base.CurrentConfiguration.RefreshText(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow, System.Globalization.CultureInfo.CurrentCulture);

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static void ShowSystem(MainWindow Window)
        {
            ShowSettingsTab(Window, MainWindow.SystemSettingsTabIndex);
            Settle(Window);
        }

        private static CampgroundDataView Card(MainWindow Window) =>
            Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<CampgroundDataView>("CampgroundDataContent")
                ?? throw new AssertionException("CampgroundDataContent not found");

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().SingleOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found in " + Root.Name);

        private static void Click(Button Button)
        {
            Assert.That(Button.IsEffectivelyEnabled, Is.True, Button.Name + " is disabled");
            Button.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        private static string? FocusName(MainWindow Window)
        {
            IInputElement? Element = Focused(Window);
            return Element is TabItem Tab ? "Tab:" + Tab.Header : (Element as Control)?.Name ?? Element?.GetType().Name;
        }

        private static List<string?> PressTab(MainWindow Window, int Count)
        {
            var Visited = new List<string?>();
            for (int i = 0; i < Count; i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Visited.Add(FocusName(Window));
            }
            return Visited;
        }

        private static void AssertButton(Control View, string Name, string Label, string HelpStart)
        {
            Button Button = In<Button>(View, Name);
            AutomationPeer Peer = ControlAutomationPeer.CreatePeerForElement(Button);
            Assert.That(Peer.GetName(), Is.EqualTo(Label), Name);
            Assert.That(Peer.GetHelpText(), Does.StartWith(HelpStart), Name);
            Assert.That(Button.IsTabStop && Button.Focusable, Is.True, Name);
        }

        private static void AssertFits(MainWindow Window, CampgroundDataView View)
        {
            Settle(Window);
            ScrollViewer Scroll = Named<SystemSettingsView>(Window, "SystemSettingsContent").FindControl<ScrollViewer>("SystemScrollViewer")!;
            Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), "no horizontal overflow");
            foreach (Control C in View.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && (c is TextBlock || c is Button || c is ProgressBar)))
            {
                Point? Right = C.TranslatePoint(new Point(C.Bounds.Width, 0), Window);
                Assert.That(Right!.Value.X, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5), C.Name ?? C.GetType().Name);
                foreach (AvaloniaProperty Color in new AvaloniaProperty[] { TextBlock.ForegroundProperty, TemplatedControl.ForegroundProperty, TemplatedControl.BackgroundProperty, Border.BackgroundProperty })
                {
                    Assert.That(Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(C, Color).Priority, Is.Not.EqualTo(Avalonia.Data.BindingPriority.LocalValue),
                        (C.Name ?? C.GetType().Name) + " has a color of its own: " + Color.Name);
                }
            }
        }

        // Refresh campground data…, then Start refresh: the worker has entered the service
        private static void Begin(MainWindow Window, TestRefresh Refresh)
        {
            CampgroundDataView View = Card(Window);
            Click(In<Button>(View, "RefreshDatabaseButton"));
            Settle(Window);
            Click(In<Button>(View, "StartRefreshButton"));
            Settle(Window);
            Assert.That(Refresh.WaitForCall(), Is.True, "the worker did not reach the service");
            Settle(Window);
        }

        // a progress report from the worker, published at once (the gate's interval has passed on the test clock)
        private static void Report(MainWindow Window, TestRefresh Refresh, RidbRefreshStage Stage, long? Done, long? Total, bool CanCancel = true)
        {
            Refresh.Send(Stage, Done, Total, CanCancel);
            Refresh.Time.Advance(TimeSpan.FromSeconds(1));
            Settle(Window);
            Refresh.Timers.FireOnce();
            Settle(Window);
        }

        private static void WaitForEnd(CampgroundDataViewModel Data, int Milliseconds = 15000)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Data.IsRunning && Clock.ElapsedMilliseconds < Milliseconds)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.That(Data.IsRunning, Is.False, "the refresh did not end");
        }

        // D62: the automatic restart after a success has ended (its worker, then its end on the UI thread)
        internal static void WaitForRestart(CampgroundDataViewModel Data)
        {
            Assert.That(Data.PendingRestart.Wait(10000), Is.True, "the restart did not end");
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Data.LastRestart == null && Clock.ElapsedMilliseconds < 10000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.That(Data.LastRestart, Is.Not.Null, "the restart's end was not shown");
        }

        private static RidbRefreshResult Result(RidbRefreshOutcome Outcome, bool Installed = false, Exception? Error = null, bool CancelRequested = false) =>
            new RidbRefreshResult(Outcome, Outcome == RidbRefreshOutcome.Refreshed ? RidbRefreshStage.Completed : RidbRefreshStage.Downloading, Installed,
                Installed ? DateTime.UtcNow : null, TimeSpan.FromSeconds(1), RidbRefreshResult.NoProblems, null, Error, CancelRequested);
    }

    // A refresh service the test drives: the worker enters Run and does what the test sends (a progress report, an end),
    // in order; it never touches a file or the network. Its seams use the test's clock and timers.
    internal sealed class TestRefresh
    {
        private readonly BlockingCollection<Func<RidbRefreshResult?>> _Steps = new BlockingCollection<Func<RidbRefreshResult?>>();
        private readonly ManualResetEventSlim _Called = new ManualResetEventSlim(false);
        private int _Calls;

        internal readonly ManualTime Time = new ManualTime();
        internal readonly ManualTimers Timers = new ManualTimers();
        internal Action<Action>? Post { get; set; }
        internal int Calls => Volatile.Read(ref _Calls);
        internal RidbRefreshRequest? Request { get; private set; }
        internal IProgress<RidbRefreshProgress>? Progress { get; private set; }
        internal CancellationToken Token { get; private set; }
        internal int Thread { get; private set; }
        private long _Sequence;

        internal RefreshSeams Seams() => new RefreshSeams
        {
            Run = Run, Time = Time, Delay = Timers.Once, Ticker = Timers.Every, Post = Post,
            Restart = RestartAfterRefresh, Exit = () => Interlocked.Increment(ref _Exits), UtcNow = () => Now,
        };

        // D62: the automatic restart after a successful refresh, faked: it records each handoff and ends as the test says
        // (by default acknowledged), optionally only once the test releases it; it never touches a file or starts a process
        internal static readonly DateTime Now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
        internal readonly ConcurrentQueue<RestartHandoff> Handoffs = new ConcurrentQueue<RestartHandoff>();
        internal RestartAttempt NextRestart { get; set; } = new RestartAttempt(RestartAttemptStatus.Acknowledged);
        internal ManualResetEventSlim? HoldRestart { get; set; }
        // the new process had claimed the handoff (or the start had failed) before the window closed: NextRestart stands
        internal bool EndedBeforeTheClose { get; set; }
        internal bool? StillWantedAtEnd { get; private set; }
        private int _Exits;
        internal int Exits => Volatile.Read(ref _Exits);

        private RestartAttempt RestartAfterRefresh(RestartHandoff Handoff, Func<bool> StillWanted)
        {
            Handoffs.Enqueue(Handoff);
            HoldRestart?.Wait(10000);
            StillWantedAtEnd = StillWanted();
            return StillWantedAtEnd == true || EndedBeforeTheClose ? NextRestart : new RestartAttempt(RestartAttemptStatus.Abandoned);
        }

        private RidbRefreshResult Run(RidbRefreshRequest Request, IProgress<RidbRefreshProgress>? Progress, CancellationToken Token)
        {
            this.Request = Request;
            this.Progress = Progress;
            this.Token = Token;
            Thread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref _Calls);
            _Called.Set();
            while (true)
            {
                if (!_Steps.TryTake(out Func<RidbRefreshResult?>? Step, 15000))
                {
                    return new RidbRefreshResult(RidbRefreshOutcome.Unexpected, RidbRefreshStage.AcquiringLock, false, null, TimeSpan.Zero,
                        RidbRefreshResult.NoProblems, null, new TimeoutException("the test sent nothing"), false);
                }
                RidbRefreshResult? Ended = Step();
                if (Ended != null)
                {
                    _Called.Reset();
                    return Ended;
                }
            }
        }

        internal bool WaitForCall() => _Called.Wait(10000);

        internal void Send(RidbRefreshStage Stage, long? Done, long? Total, bool CanCancel)
        {
            var Sent = new ManualResetEventSlim(false);
            _Steps.Add(() =>
            {
                Progress?.Report(new RidbRefreshProgress(Interlocked.Increment(ref _Sequence), Stage, CanCancel, Done, Total));
                Sent.Set();
                return null;
            });
            Assert.That(Sent.Wait(10000), Is.True, "the worker did not report");
        }

        internal void Finish(RidbRefreshResult Result, Action? Before = null)
        {
            _Steps.Add(() =>
            {
                Before?.Invoke();
                return Result;
            });
        }

        // as the service does when the token is canceled at a checkpoint
        internal void FinishWhenCanceled()
        {
            _Steps.Add(() =>
            {
                Token.WaitHandle.WaitOne(10000);
                return new RidbRefreshResult(RidbRefreshOutcome.Canceled, RidbRefreshStage.Downloading, false, null, TimeSpan.Zero, RidbRefreshResult.NoProblems,
                    null, new OperationCanceledException(Token), Token.IsCancellationRequested);
            });
        }
    }
}
