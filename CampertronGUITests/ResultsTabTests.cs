using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // The Results tab (GS2, decision D46): choosing saved search files, running them through GS1's search service and
    // showing its typed results. Every run here answers through a test transport or a test run (never recreation.gov),
    // in folders of the test's own. The numbers refer to the GS2 test list.
    public class ResultsTabTests
    {
        private ResultsFolders Folders = null!;

        [SetUp]
        public void CreateFolders() => Folders = new ResultsFolders();

        [TearDown]
        public void DeleteFolders() => Folders.Dispose();

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static ResultsView View(MainWindow Window) => Named<ResultsView>(Window, "ResultsContent");

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        private static void ShowResultsTab(MainWindow Window)
        {
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
        }

        // ---- the tab and its paths ----

        // 1, 2, 74, 75: Results is the second top-level tab, after Searches (D50; it was the sixth, GS2), with the startup's
        // settings and cache folders; the other tabs keep their contents
        [AvaloniaTest]
        public void TheResultsTab_IsTheSecond_AndHasTheStartupsFolders()
        {
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Assert.That(Tabs(Window).Items.Cast<TabItem>().Select(t => t.Header), Is.EqualTo(new[] { "Searches", "Results", "Settings", "Campgrounds" }));
            Assert.That(MainWindow.ResultsTabIndex, Is.EqualTo(1));
            // a tab's content is created when the tab is shown
            Tabs(Window).SelectedIndex = MainWindow.SettingsTabIndex;
            Settle(Window);
            Assert.That(Named<SettingsView>(Window, "SettingsContent").DataContext, Is.SameAs(ViewModel.Settings));
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Assert.That(Named<SearchesView>(Window, "SearchesContent").DataContext, Is.SameAs(ViewModel.Searches));
            Tabs(Window).SelectedIndex = 0;
            Settle(Window);
            Assert.That(ViewModel.CachePath, Is.EqualTo(Folders.Cache));
            Assert.That(ViewModel.Results!.ConfigPath, Is.EqualTo(Folders.Config));
            Assert.That(ViewModel.Results.CachePath, Is.EqualTo(Folders.Cache));
            Assert.That(ViewModel.Results.PathsAvailable, Is.True);
            Assert.That(ViewModel.Results.IsLoaded, Is.False, "nothing is read before the tab is shown");
            ShowResultsTab(Window);
            Assert.That(View(Window).DataContext, Is.SameAs(ViewModel.Results));
            Assert.That(ViewModel.Results.IsLoaded, Is.True);
            Assert.That(ControlAutomationPeer.CreatePeerForElement((TabItem)Tabs(Window).SelectedItem!).GetName(), Is.EqualTo("Results"));
            Window.Close();
        }

        // 75: without a cache folder (a startup that returned none) nothing can start, and the tab says so
        [AvaloniaTest]
        public void WithoutACacheFolder_NothingStarts()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var ViewModel = new ResultsViewModel(Folders.Config, null, NoCampgrounds, _ => false, Seams(new TestGov(), new ManualTimers()));
            ViewModel.Show();
            Assert.That(ViewModel.PathsAvailable, Is.False);
            Assert.That(ViewModel.CanStart, Is.False);
            Assert.That(ViewModel.StatusText, Does.Contain("cache folder"));
        }

        // ---- the list ----

        // 3, 4, 8, 9: enabled files are chosen ("Enabled (autoRun): selected by default"), files that are not enabled are
        // listed unchosen ("Not enabled: runs when selected", V011-3), a file with an error can't be chosen and says why
        // without a value, a file with only warnings can run and shows them
        [AvaloniaTest]
        public void TheList_ChoosesEnabledFiles_AndExplainsTheOthers()
        {
            Folders.Write("Enabled.yaml", ResultsFolders.Search("Enabled", true));
            Folders.Write("Disabled.yaml", ResultsFolders.Search("Disabled", false));
            Folders.Write("Broken.yaml", ResultsFolders.Search("Broken", true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(30) + ", " + ResultsFolders.Date(30) + "]"));
            Folders.Write("Warned.yaml", ResultsFolders.Search("Warned", true, TotalHumans: ""));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));

            Assert.That(ViewModel.Searches.Select(R => R.FileName), Is.EqualTo(new[] { "Broken.yaml", "Disabled.yaml", "Enabled.yaml", "Warned.yaml" }));
            ResultsSearchRow Enabled = Row(ViewModel, "Enabled.yaml"), Disabled = Row(ViewModel, "Disabled.yaml"), Broken = Row(ViewModel, "Broken.yaml"), Warned = Row(ViewModel, "Warned.yaml");
            Assert.That(Enabled.IsSelected && Enabled.IsRunnable && Enabled.IsEnabled, Is.True);
            Assert.That(Enabled.Details, Is.EqualTo("Enabled.yaml · Campground ID " + RunProbe.CampgroundId + " · Enabled (autoRun): selected by default"));
            Assert.That(Disabled.IsSelected, Is.False);
            Assert.That(Disabled.IsRunnable, Is.True);
            Assert.That(Disabled.IsEnabled, Is.False);
            Assert.That(Disabled.Details, Is.EqualTo("Disabled.yaml · Campground ID " + RunProbe.CampgroundId + " · Not enabled: runs when selected"));
            Assert.That(Broken.Details, Does.EndWith(" · Enabled (autoRun)"), "the setting alone: it can't be searched now");
            Assert.That(Broken.IsRunnable || Broken.IsSelected || Broken.CanSelect, Is.False);
            Assert.That(Broken.Reason, Does.StartWith("Can't be searched: ").And.Contain("Error:"));
            Broken.IsSelected = true;
            Assert.That(Broken.IsSelected, Is.False, "an error can't be chosen");
            Assert.That(Warned.IsRunnable && Warned.IsSelected, Is.True);
            Assert.That(Warned.WarningsText, Does.StartWith("Warning: "));
            Assert.That(Warned.HasReason, Is.False);
            Assert.That(ViewModel.CanStart, Is.True);
        }

        // 18, Select enabled, Select all runnable, Clear: the choice and Start follow them
        [AvaloniaTest]
        public void TheChoiceButtons_ChooseWhatTheySay_AndStartNeedsAChoice()
        {
            Folders.Write("Enabled.yaml", ResultsFolders.Search("Enabled", true));
            Folders.Write("Disabled.yaml", ResultsFolders.Search("Disabled", false));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));
            ViewModel.ClearSelectionCommand.Execute(null);
            Assert.That(ViewModel.Searches.Any(R => R.IsSelected), Is.False);
            Assert.That(ViewModel.CanStart || ViewModel.StartCommand.CanExecute(null), Is.False, "nothing chosen, no Start");
            ViewModel.SelectAllCommand.Execute(null);
            Assert.That(ViewModel.Searches.All(R => R.IsSelected), Is.True);
            ViewModel.SelectEnabledCommand.Execute(null);
            Assert.That(ViewModel.Searches.Where(R => R.IsSelected).Select(R => R.FileName), Is.EqualTo(new[] { "Enabled.yaml" }));
            Assert.That(ViewModel.StartCommand.CanExecute(null), Is.True);
        }

        // TD-56's review, the list's refresh (V011-3's words): every look at the disk keeps the user's choice; a file that
        // is no longer enabled is unchosen unless the user chose it; a file that becomes enabled is not chosen behind the
        // user's back; a file that could not run and now can gets the default choice; nothing is searched
        [AvaloniaTest]
        public void TheListsRefresh_KeepsTheUsersChoice_AndFollowsTheSetting()
        {
            string Broken(string Name) => ResultsFolders.Search(Name, true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(30) + ", " + ResultsFolders.Date(30) + "]");
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true));
            Folders.Write("Delta.yaml", ResultsFolders.Search("Delta", false));
            Folders.Write("Epsilon.yaml", Broken("Epsilon"));
            Folders.Write("Gamma.yaml", ResultsFolders.Search("Gamma", false));
            var Gov = new TestGov();
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers()));
            Assert.That(ViewModel.Searches.Where(R => R.IsSelected).Select(R => R.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Beta.yaml" }), "the default choice");
            Row(ViewModel, "Beta.yaml").IsSelected = false;
            Row(ViewModel, "Beta.yaml").IsSelected = true;
            Row(ViewModel, "Gamma.yaml").IsSelected = true;

            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", false));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", false));
            Folders.Write("Delta.yaml", ResultsFolders.Search("Delta", true));
            Folders.Write("Epsilon.yaml", ResultsFolders.Search("Epsilon", true));
            ViewModel.CheckForExternalChanges();
            ViewModel.Show();

            Assert.That(Row(ViewModel, "Alpha.yaml").IsSelected, Is.False, "no longer enabled, and chosen only by default");
            Assert.That(Row(ViewModel, "Beta.yaml").IsSelected, Is.True, "no longer enabled, but the user chose it");
            Assert.That(Row(ViewModel, "Gamma.yaml").IsSelected, Is.True, "the user's choice of a file that is not enabled stays");
            Assert.That(Row(ViewModel, "Delta.yaml").IsSelected, Is.False, "enabled now, but not chosen behind the user's back");
            Assert.That(Row(ViewModel, "Epsilon.yaml").IsSelected, Is.True, "it can run now: the default choice of an enabled file");
            Assert.That(Row(ViewModel, "Delta.yaml").ChoiceText, Is.EqualTo("Enabled (autoRun): selected by default"), "the words say what the default is");
            Assert.That(Gov.Requests, Is.Empty, "a look at the disk searches nothing");
            Assert.That(ViewModel.IsRunning, Is.False);
        }

        // 11: a new file appears when the tab is shown again or the window is activated; an enabled one is chosen
        [AvaloniaTest]
        public void ANewFile_AppearsOnTheNextLook()
        {
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));
            Assert.That(ViewModel.HasNoSearches, Is.True);
            Folders.Write("Later.yaml", ResultsFolders.Search("Later", true));
            Assert.That(ViewModel.Searches, Is.Empty, "no watching");
            ViewModel.CheckForExternalChanges();
            Assert.That(Row(ViewModel, "Later.yaml").IsSelected, Is.True);
            Folders.Write("Second.yaml", ResultsFolders.Search("Second", false));
            ViewModel.Show();
            Assert.That(Row(ViewModel, "Second.yaml").IsSelected, Is.False);
        }

        // At Start the disk is read again: a chosen file that can no longer run stops the Start (nothing chosen silently,
        // nothing dropped silently); a new file is listed but not chosen
        [AvaloniaTest]
        public void AtStart_AChangedChoice_IsNeverSilent()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true));
            var Gov = new TestGov();
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers()));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(9) + ", " + ResultsFolders.Date(9) + "]"));
            Folders.Write("Gamma.yaml", ResultsFolders.Search("Gamma", true));
            ViewModel.StartCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.IsRunning, Is.False);
            Assert.That(ViewModel.ListNotice, Does.Contain("Beta.yaml"));
            Assert.That(ViewModel.StatusText, Does.StartWith("Not started"));
            Assert.That(Row(ViewModel, "Gamma.yaml").IsSelected, Is.False, "a file that appeared at Start is not chosen by itself");
            Assert.That(Gov.Requests, Is.Empty);
        }

        // ---- running files ----

        // 5, 6, 7, 16, 77: a file that is not enabled, chosen explicitly, is searched; afterwards it is still not enabled,
        // and no file of the settings folder (nor Camp.History) was written
        [AvaloniaTest]
        public void ADisabledFile_ChosenExplicitly_IsSearched_AndNothingIsWritten()
        {
            Folders.Write("Disabled.yaml", ResultsFolders.Search("Disabled", false));
            Dictionary<string, string> Before = Fingerprint(Folders.Config);
            var Gov = new TestGov();
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers()));
            Row(ViewModel, "Disabled.yaml").IsSelected = true;
            SearchRunResult Result = Run(ViewModel);

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            SearchResult Search = Result.Searches.Single();
            Assert.That(Search.AutoRun, Is.False);
            Assert.That(Search.Openings, Is.Not.Empty);
            Assert.That(Fingerprint(Folders.Config), Is.EqualTo(Before), "no file of the settings folder written");
            Assert.That(Directory.Exists(Folders.In("Camp.History")), Is.False);
            ViewModel.CheckForExternalChanges();
            Assert.That(Row(ViewModel, "Disabled.yaml").IsEnabled, Is.False);
            Assert.That(Row(ViewModel, "Disabled.yaml").RunState, Does.StartWith("Complete:"));
        }

        // 10, 17: a chosen file deleted before Start is a typed missing-file outcome while the other is searched
        [AvaloniaTest]
        public void AFileMissingAtStart_IsATypedOutcome_AndTheOthersRun()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));
            File.Delete(Folders.In("Beta.yaml"));
            SearchRunResult Result = Run(ViewModel);

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.CompletedWithProblems));
            SearchResult Missing = Result.Searches.Single(S => S.FileName == "Beta.yaml");
            Assert.That(Missing.State, Is.EqualTo(SearchState.Invalid));
            Assert.That(Missing.Issues.Select(I => I.Code), Does.Contain(SearchIssueCode.FileMissing));
            Assert.That(Result.Searches.Single(S => S.FileName == "Alpha.yaml").State, Is.EqualTo(SearchState.Complete));
            ResultSearchViewModel Shown = ViewModel.ResultSearches.Single(S => S.FileName == "Beta.yaml");
            Assert.That(Shown.Kind, Is.EqualTo(ResultKind.NotSearched));
            Assert.That(Shown.IssueLines, Has.Some.Contains("does not exist"));
            ViewModel.CheckForExternalChanges();
            Assert.That(ViewModel.Searches.Select(R => R.FileName), Is.EqualTo(new[] { "Alpha.yaml" }));
        }

        // 12: the bytes read at Start are searched (a change on disk after the list was read is what runs)
        [AvaloniaTest]
        public void AFileChangedOnDisk_IsSearchedAsItIsAtStart()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            ResultsViewModel ViewModel = Results(Folders, Seams(new TestGov(), new ManualTimers()));
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha changed", true, IncludeSites: "['002']"));
            SearchRunResult Result = Run(ViewModel);
            SearchResult Search = Result.Searches.Single();
            Assert.That(Search.DisplayName, Is.EqualTo("Alpha changed"));
            Assert.That(Search.Sites.Select(S => S.CampsiteName).Distinct(), Is.EqualTo(new[] { "002" }));
        }

        // 13, 14, 15: a file with unsaved changes in the Searches tab can't be chosen ("Save or revert …"); the other
        // files stay runnable; Revert and Save each clear it; nothing is saved or discarded by the Results tab
        [AvaloniaTest]
        public void AFileWithUnsavedChanges_IsBlocked_UntilItIsSavedOrReverted()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true));
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            SearchFileViewModel Alpha = ViewModel.Searches!.Files.Single(F => F.FileName == "Alpha.yaml");
            ViewModel.Searches.SelectedFile = Alpha;
            Alpha.AutoRun.IsChecked = false;
            Settle(Window);
            Assert.That(Alpha.IsDirty, Is.True);
            string AlphaOnDisk = File.ReadAllText(Folders.In("Alpha.yaml"));

            ShowResultsTab(Window);
            ResultsViewModel Results = ViewModel.Results!;
            Assert.That(Row(Results, "Alpha.yaml").IsRunnable || Row(Results, "Alpha.yaml").IsSelected, Is.False);
            Assert.That(Row(Results, "Alpha.yaml").Reason, Is.EqualTo("Save or revert the changes in the Searches tab first."));
            Assert.That(Row(Results, "Beta.yaml").IsRunnable && Row(Results, "Beta.yaml").IsSelected, Is.True, "per file: Beta runs");
            Assert.That(File.ReadAllText(Folders.In("Alpha.yaml")), Is.EqualTo(AlphaOnDisk), "not saved by the Results tab");
            Assert.That(Alpha.IsDirty, Is.True, "not discarded by the Results tab");

            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Alpha.RevertCommand.Execute(null);
            ShowResultsTab(Window);
            Assert.That(Row(Results, "Alpha.yaml").IsRunnable && Row(Results, "Alpha.yaml").IsSelected, Is.True, "reverted: runnable and chosen again");

            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Alpha.DisplayName.Text = "Alpha saved";
            Alpha.SaveCommand.Execute(null);
            Settle(Window);
            Assert.That(Alpha.IsDirty, Is.False);
            ShowResultsTab(Window);
            Assert.That(Row(Results, "Alpha.yaml").IsRunnable, Is.True);
            Assert.That(Row(Results, "Alpha.yaml").Title, Is.EqualTo("Alpha saved"));
            Window.Close();
        }

        // 16, 17: one file and several files run through the same GS1 entry point
        [AvaloniaTest]
        public void OneFileAndSeveralFiles_RunThroughTheSameService()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", true, Consecutive: 2));
            var Plans = new List<IReadOnlyList<string>>();
            var Gov = new TestGov();
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers(), Run: (Plan, Progress, Token, Options) =>
            {
                lock (Plans)
                {
                    Plans.Add(Plan.Entries.Select(E => E.FileName).ToList());
                }
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            }));
            Row(ViewModel, "Beta.yaml").IsSelected = false;
            Assert.That(Run(ViewModel).Searches, Has.Count.EqualTo(1));
            Row(ViewModel, "Beta.yaml").IsSelected = true;
            SearchRunResult Both = Run(ViewModel);
            Assert.That(Both.Searches.Select(S => S.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Beta.yaml" }));
            Assert.That(Plans, Is.EqualTo(new[] { new[] { "Alpha.yaml" }, new[] { "Alpha.yaml", "Beta.yaml" } }));
            Assert.That(ViewModel.ResultSearches.Select(S => S.FileName), Is.EqualTo(new[] { "Alpha.yaml", "Beta.yaml" }));
        }

        // 19, 20, 21, 22, 23: while a run goes on Start is disabled and guarded, the choice is locked, Cancel is enabled;
        // Cancel twice is safe, shows Canceling and starts nothing
        [AvaloniaTest]
        public void WhileARunGoesOn_StartIsGuarded_TheChoiceIsLocked_AndCancelIsSafe()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var Gov = new TestGov { Answer = TestGov.Stall };
            int Runs = 0;
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers(), Run: (Plan, Progress, Token, Options) =>
            {
                Interlocked.Increment(ref Runs);
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            }));
            Start(ViewModel);
            Assert.That(Gov.Reached.Wait(TimeSpan.FromSeconds(10)), Is.True);
            long Id = ViewModel.CurrentRunId;
            Assert.That(ViewModel.IsRunning, Is.True);
            Assert.That(ViewModel.StartCommand.CanExecute(null) || ViewModel.CanStart, Is.False);
            ViewModel.StartCommand.Execute(null);
            ViewModel.Start();
            Assert.That(ViewModel.CurrentRunId, Is.EqualTo(Id), "a second Start is ignored");
            Assert.That(Row(ViewModel, "Alpha.yaml").CanSelect, Is.False);
            Row(ViewModel, "Alpha.yaml").IsSelected = false;
            Assert.That(Row(ViewModel, "Alpha.yaml").IsSelected, Is.True, "the choice is locked");
            Assert.That(ViewModel.SelectAllCommand.CanExecute(null) || ViewModel.SelectEnabledCommand.CanExecute(null) || ViewModel.ClearSelectionCommand.CanExecute(null), Is.False);
            Assert.That(ViewModel.CancelCommand.CanExecute(null), Is.True);
            Assert.That(ViewModel.CancelLabel, Is.EqualTo("Cancel"));

            ViewModel.CancelCommand.Execute(null);
            ViewModel.Cancel();
            Assert.That(ViewModel.IsCanceling, Is.True);
            Assert.That(ViewModel.CancelLabel, Is.EqualTo("Canceling…"));
            Assert.That(ViewModel.CancelCommand.CanExecute(null), Is.False);
            WaitForEnd(ViewModel);
            Assert.That(ViewModel.RunStatus, Is.EqualTo(SearchRunStatus.Canceled));
            Assert.That(Runs, Is.EqualTo(1), "Cancel never started another run");
            Assert.That(ViewModel.CancelLabel, Is.EqualTo("Cancel"));
            Assert.That(Row(ViewModel, "Alpha.yaml").CanSelect, Is.True);
        }

        // 24, 25, 26: Cancel ends a run promptly during a request, during the wait before a retry and while results are
        // filtered (GS1's checkpoints, reached through the tab's token)
        [AvaloniaTest]
        [TestCase("request")]
        [TestCase("retry")]
        [TestCase("filtering")]
        public void Cancel_EndsTheRunPromptly(string Where)
        {
            var Gov = new TestGov();
            var Answered = new ManualResetEventSlim(false);
            TimeSpan? Retry = null;
            if (Where == "request")
            {
                Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
                Gov.Answer = TestGov.Stall;
            }
            else if (Where == "retry")
            {
                Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
                Retry = TimeSpan.FromSeconds(30);
                Gov.Answer = (_, _) => new SearchResponse(503, string.Empty);
            }
            else
            {
                // many campsites and a long attribute filter make filtering one month take seconds
                string[] Sites = Enumerable.Range(0, 500).Select(i => "99100" + i.ToString("0000")).ToArray();
                RunProbe.WriteCampground(Folders.Cache, "9910000", "BIG CAMPGROUND", "Campertron Probe Park", Sites);
                string Filter = "[" + string.Join(", ", Enumerable.Range(0, 3000).Select(i => "'Nothing" + i + ":No'")) + "]";
                Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true, Campground: "9910000", Value: 40, IncludeAttributes: Filter));
                Gov.Answer = (Request, _) =>
                {
                    Answered.Set();
                    return new SearchResponse(200, RunProbe.MonthJson(Request.Month, Sites));
                };
            }
            ResultsViewModel ViewModel = Results(Folders, Seams(Gov, new ManualTimers(), RetryDelay: Retry));
            Start(ViewModel);
            Assert.That(Gov.Reached.Wait(TimeSpan.FromSeconds(10)), Is.True);
            if (Where == "filtering")
            {
                Assert.That(Answered.Wait(TimeSpan.FromSeconds(10)), Is.True);
                Thread.Sleep(100);
            }
            else
            {
                Thread.Sleep(200);
            }
            var Clock = Stopwatch.StartNew();
            ViewModel.Cancel();
            WaitForEnd(ViewModel, 10000);
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1.5)), Where);
            Assert.That(ViewModel.RunStatus, Is.EqualTo(SearchRunStatus.Canceled));
            ResultSearchViewModel Shown = ViewModel.ResultSearches.Single();
            Assert.That(Shown.Kind, Is.EqualTo(ResultKind.Canceled));
            Assert.That(Shown.MonthsText, Does.Not.Contain("no matching availability"));
        }

        // 27, 28: closing the window during a stalled request cancels the run, ignores its late result, and the run ends
        // promptly on its own thread (a background thread) without the UI thread
        [AvaloniaTest]
        public void ClosingTheWindow_DuringAStalledRequest_CancelsTheRunAndIgnoresItsEnd()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            var Gov = new TestGov { Answer = TestGov.Stall };
            var Timers = new ManualTimers();
            var (Window, ViewModel) = Folders.ShowWindow(Seams(Gov, Timers));
            ShowResultsTab(Window);
            ResultsViewModel Results = ViewModel.Results!;
            Start(Results);
            Assert.That(Gov.Reached.Wait(TimeSpan.FromSeconds(10)), Is.True);
            Assert.That(Timers.Live.Count(T => T.Repeat), Is.EqualTo(1), "the elapsed-time ticker runs");

            Window.Close();
            Assert.That(Results.IsDetached, Is.True);
            Assert.That(Timers.Live.Count(), Is.Zero, "the timers stopped");
            var Clock = Stopwatch.StartNew();
            Assert.That(ViewModel.PendingSearch.Wait(TimeSpan.FromSeconds(5)), Is.True, "the run ended without the UI thread");
            Assert.That(Clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
            Assert.That(((Task<SearchRunResult>)ViewModel.PendingSearch).Result.Status, Is.EqualTo(SearchRunStatus.Canceled));
            Dispatcher.UIThread.RunJobs();
            Assert.That(Results.LastResult, Is.Null, "the late result changed nothing");
            Assert.That(Results.IsRunning, Is.True, "nothing after the close was applied");
        }

        // 28: App's bounded wait after the window closed returns when the canceled run has ended
        [AvaloniaTest]
        public void TheShutdownWait_IsBounded()
        {
            Assert.That(App.SearchShutdownWait, Is.GreaterThan(TimeSpan.Zero).And.LessThanOrEqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(((App)Avalonia.Application.Current!).WaitForPendingSearch(TimeSpan.FromMilliseconds(10)), Is.True, "nothing pending");
        }
    }
}
