using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
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
    // The Results tab in the real window on the headless platform (GS2, decision D46): its accessibility, keyboard use,
    // layout in Light and Dark at small and large sizes, the virtualized result list, the isolation from Email.yaml and
    // Camp.History, and the GUI's boundaries around the search service. The numbers refer to the GS2 test list.
    public class ResultsViewTests
    {
        private ResultsFolders Folders = null!;

        [SetUp]
        public void CreateFolders() => Folders = new ResultsFolders();

        [TearDown]
        public void DeleteFolders()
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            Folders.Dispose();
        }

        private static void Settle(Window Window)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        private static ResultsView View(MainWindow Window) => Named<ResultsView>(Window, "ResultsContent");

        private static T In<T>(MainWindow Window, string Name) where T : Control =>
            View(Window).GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        // the window on the Results tab, after a fixture run of Alpha (and any other files the test wrote)
        private (MainWindow Window, MainWindowViewModel ViewModel, ResultsViewModel Results) ShowAfterRun(ResultsSeams Seams, bool RunFirst = true)
        {
            if (!File.Exists(Folders.In("Alpha.yaml")))
            {
                Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            }
            var (Window, ViewModel) = Folders.ShowWindow(Seams);
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            ResultsViewModel Results = ViewModel.Results!;
            if (RunFirst)
            {
                Run(Results);
                Settle(Window);
            }
            return (Window, ViewModel, Results);
        }

        private static string? Name(Control Control) => ControlAutomationPeer.CreatePeerForElement(Control).GetName();

        // ---- accessibility ----

        // 63, 64, 65, 66, 69: every interactive control has a name; Start and Cancel keep theirs; each check box names its
        // file and whether it runs at startup; the status is a polite live region named by its text; the bar has a name and,
        // when determinate, a value; each result row reads its date and campsite; problems carry words, not only color
        [AvaloniaTest]
        public void EveryControl_HasAnAccessibleName_AndStatesAreSaidInWords()
        {
            Folders.Write("Disabled.yaml", ResultsFolders.Search("Disabled", false));
            Folders.Write("Broken.yaml", ResultsFolders.Search("Broken", true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(30) + ", " + ResultsFolders.Date(30) + "]"));
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers()));
            Control Root = View(Window);
            List<Control> Interactive = Root.GetVisualDescendants().OfType<Control>()
                .Where(c => c is Button || c is CheckBox || c is ComboBox || c is ListBox || c is ProgressBar).ToList();
            Assert.That(Interactive.Count, Is.GreaterThan(8));
            foreach (Control Each in Interactive)
            {
                Assert.That(Name(Each), Is.Not.Null.And.Not.Empty, Each.GetType().Name + " " + Each.Name);
            }
            Assert.That(Name(In<Button>(Window, "StartButton")), Is.EqualTo("Start search"));
            Assert.That(Name(In<Button>(Window, "CancelButton")), Is.EqualTo("Cancel search"));
            List<string?> Boxes = Root.GetVisualDescendants().OfType<CheckBox>().Select(Name).ToList();
            Assert.That(Boxes, Is.EquivalentTo(new[]
            {
                "Alpha (Alpha.yaml), runs at startup", "Broken (Broken.yaml), can't be searched now", "Disabled (Disabled.yaml), not run at startup",
            }));
            CheckBox BrokenBox = Root.GetVisualDescendants().OfType<CheckBox>().Single(c => Name(c)!.StartsWith("Broken"));
            Assert.That(AutomationProperties.GetHelpText(BrokenBox), Does.Contain("Can't be searched:").And.Contain("Error:"), "the reason is read, not only shown in red");

            TextBlock Status = In<TextBlock>(Window, "StatusText");
            Assert.That(AutomationProperties.GetLiveSetting(Status), Is.EqualTo(AutomationLiveSetting.Polite));
            Assert.That(Name(Status), Is.EqualTo(Results.StatusText), "the live region reads the status itself");
            Assert.That(Results.StatusText, Does.StartWith("Finished:"));

            ListBox Rows = In<ListBox>(Window, "ResultRowsList");
            ListBoxItem First = (ListBoxItem)Rows.ContainerFromIndex(1)!;
            ResultOpeningRow Opening = (ResultOpeningRow)First.DataContext!;
            Assert.That(Name(First), Is.EqualTo(Opening.AccessibleName));
            Assert.That(Opening.AccessibleName, Does.StartWith(ResultText.Night(Results.LastResult!.Searches.Single().Openings[0].Night) + ": Site 001, ID 990000101"));
            Window.Close();
        }

        // 65: while availability is checked the bar is named and has GS1's counts as its value
        [AvaloniaTest]
        public void TheProgressBar_HasANameAndAValueWhenItsTotalIsKnown()
        {
            var Gov = new TestGov { Answer = TestGov.Stall };
            var Timers = new ManualTimers();
            var (Window, _, Results) = ShowAfterRun(Seams(Gov, Timers), RunFirst: false);
            Start(Results);
            Assert.That(Gov.Reached.Wait(TimeSpan.FromSeconds(10)), Is.True);
            // the progress timer is the test's: fired while waiting, it publishes the newest snapshot once the interval passed
            WaitUntil(() => { Timers.FireOnce(); return Results.RequestsText != null; }, () => "no request count: " + Results.ProgressDetails);
            Settle(Window);
            ProgressBar Bar = In<ProgressBar>(Window, "SearchProgressBar");
            Assert.That(Bar.IsVisible, Is.True);
            TextBlock Details = In<TextBlock>(Window, "ProgressDetailsText");
            Assert.That(Details.IsVisible, Is.True, "the stage, the counts and the elapsed time are shown while the run goes on");
            Assert.That(Details.Text, Is.EqualTo(Results.ProgressDetails).And.Contain("Availability: 0 of").And.Contain("Elapsed "));
            Assert.That(Name(Bar), Is.EqualTo("Search progress"));
            Assert.That(Bar.IsIndeterminate, Is.False);
            var Range = (IRangeValueProvider)ControlAutomationPeer.CreatePeerForElement(Bar);
            Assert.That(Range.Value, Is.EqualTo(Results.ProgressValue));
            Assert.That(Range.Maximum, Is.EqualTo(Results.ProgressMaximum).And.GreaterThan(0));
            Results.Cancel();
            WaitForEnd(Results);
            Window.Close();
        }

        // 67, 68: everything works from the keyboard: Tab reaches the check boxes, the choice buttons, Start, the result
        // choice, the list and the actions in a logical order and wraps to the tab header (TD-39); Enter toggles a check
        // box and starts a search; arrow keys choose a row; the actions run from the keyboard
        [AvaloniaTest]
        public void TheResultsTab_WorksWithTheKeyboardAlone()
        {
            var Copied = new List<string>();
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers()), RunFirst: false);
            Results.CopyText = Text => { Copied.Add(Text); return Task.FromResult(true); };
            TabItem Header = (TabItem)Tabs(Window).SelectedItem!;
            Header.Focus(NavigationMethod.Tab);
            var Visited = new List<string?>();
            for (int i = 0; i < 12 && (Visited.Count == 0 || Visited[^1] != "Tab:Results"); i++)
            {
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                IInputElement? Focus = Focused(Window);
                Visited.Add(Focus is TabItem T ? "Tab:" + T.Header : (Focus as Control)?.Name);
            }
            Assert.That(Visited, Is.EqualTo(new[] { "StartButton", "SearchCheckBox", "SelectEnabledButton", "SelectAllButton", "ClearSelectionButton", "Tab:Results" }),
                "Cancel is skipped while nothing runs, and Tab wraps to the tab header");

            // Enter on the check box clears the choice, Enter again chooses it; Enter on Start runs
            CheckBox Box = In<CheckBox>(Window, "SearchCheckBox");
            Box.Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Results.Searches.Single().IsSelected, Is.False);
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Results.Searches.Single().IsSelected, Is.True);
            In<Button>(Window, "StartButton").Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            WaitForEnd(Results);
            Settle(Window);

            // after the run: the result choice, the list (arrows choose a row) and the actions
            ListBox Rows = In<ListBox>(Window, "ResultRowsList");
            In<ComboBox>(Window, "ResultSearchChoice").Focus(NavigationMethod.Tab);
            Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Focused(Window) is ListBoxItem || ReferenceEquals(Focused(Window), Rows), Is.True, "Tab reaches the result list");
            Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Results.SelectedRow, Is.InstanceOf<ResultOpeningRow>());
            Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That((Focused(Window) as Control)?.Name, Is.EqualTo("OpenBookingButton"));
            Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That((Focused(Window) as Control)?.Name, Is.EqualTo("CopyIdButton"));
            Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.That(Copied, Is.EqualTo(new[] { Results.SelectedRow!.CampsiteId }));
            Window.Close();
        }

        // 59 through the view: Copy campsite ID puts the ID on the window's clipboard
        [AvaloniaTest]
        public void CopyCampsiteId_UsesTheWindowsClipboard()
        {
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers()));
            Results.SelectedRow = Results.SelectedResult!.Rows.OfType<ResultOpeningRow>().First();
            In<Button>(Window, "CopyIdButton").Command!.Execute(null);
            WaitUntil(() => Results.HasActionMessage, () => "no message");
            Task<string?> Clip = Window.Clipboard!.TryGetTextAsync();
            WaitFor(Clip);
            Assert.That(Clip.Result, Is.EqualTo(Results.SelectedRow!.CampsiteId));
            Assert.That(Results.ActionMessage, Is.EqualTo("Copied the campsite ID " + Results.SelectedRow.CampsiteId + "."));
            Window.Close();
        }

        // ---- layout ----

        // 52, 70, 71, 72, 73: in Light and Dark, at 720x540, 1024x768 and a large window, Start, Cancel and the status stay
        // in the window, the result list keeps a usable height, nothing overflows sideways, long names are trimmed with
        // their whole text in a tooltip, and Avalonia reports no warning
        [AvaloniaTest]
        [TestCase("Light")]
        [TestCase("Dark")]
        public void TheResultsTab_FitsSmallAndLargeWindows(string Theme)
        {
            using var Log = new AvaloniaLogCapture();
            Application.Current!.RequestedThemeVariant = Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            string LongName = "A very long search name that keeps going " + new string('L', 120);
            Folders.Write("Alpha.yaml", ResultsFolders.Search(LongName, true));
            RunProbe.WriteCampground(Folders.Cache, RunProbe.CampgroundId, "THE CAMPGROUND WITH A VERY LONG NAME " + new string('N', 120), "A PARK WITH A LONG NAME TOO", RunProbe.Campsites,
                Site => "Site " + Site + " with a long descriptive name " + new string('S', 100));
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers()));
            foreach ((double Width, double Height) in new[] { (720.0, 540.0), (1024.0, 768.0), (1920.0, 1200.0) })
            {
                Resize(Window, Width, Height);
                Settle(Window);
                Rect Client = new Rect(Window.ClientSize);
                foreach (string Critical in new[] { "StartButton", "CancelButton", "StatusText" })
                {
                    Control Each = In<Control>(Window, Critical);
                    Rect Bounds = new Rect(Each.TranslatePoint(new Point(0, 0), Window)!.Value, Each.Bounds.Size);
                    Assert.That(Client.Contains(Bounds), Is.True, Critical + " at " + Width + "x" + Height + ": " + Bounds);
                }
                ListBox Rows = In<ListBox>(Window, "ResultRowsList");
                // the list keeps a usable height and never runs under the actions; from 1024x768 up the tab does not scroll (at
                // 720x540 a wide font may make it scroll, with Start, Cancel and the status at its top)
                Assert.That(Rows.Bounds.Height, Is.GreaterThanOrEqualTo(72), "the result list at " + Width + "x" + Height);
                double ListBottom = Rows.TranslatePoint(new Point(0, Rows.Bounds.Height), Window)!.Value.Y;
                double ActionsTop = In<Button>(Window, "OpenBookingButton").TranslatePoint(new Point(0, 0), Window)!.Value.Y;
                Assert.That(ListBottom, Is.LessThanOrEqualTo(ActionsTop + 0.5), "the list ends above the actions at " + Width + "x" + Height);
                ScrollViewer Scroll = In<ScrollViewer>(Window, "ResultsScroll");
                if (Height >= 768)
                {
                    Assert.That(Scroll.Extent.Height, Is.LessThanOrEqualTo(Scroll.Viewport.Height + 0.5), "no scrolling at " + Width + "x" + Height);
                }
                Grid Column = In<Grid>(Window, "ResultsColumn");
                Assert.That(Column.Bounds.Width, Is.LessThanOrEqualTo(Math.Min(Window.ClientSize.Width, 976 + 16)), "no sideways overflow");
            }
            TextBlock Title = In<CheckBox>(Window, "SearchCheckBox").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == LongName);
            Assert.That(Title.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(ToolTip.GetTip(In<CheckBox>(Window, "SearchCheckBox")), Does.Contain(Results.Searches.Single().Details));
            ListBoxItem Item = (ListBoxItem)In<ListBox>(Window, "ResultRowsList").ContainerFromIndex(1)!;
            TextBlock SiteTitle = Item.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.Contains("long descriptive name"));
            Assert.That(SiteTitle.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(ToolTip.GetTip(SiteTitle), Is.EqualTo(SiteTitle.Text));
            Assert.That(Log.Entries, Is.Empty, string.Join("\n", Log.Entries));
            Window.Close();
        }

        // 51: the result list is virtualized: twelve thousand openings create only the rows on screen, also after
        // scrolling to the end
        [AvaloniaTest]
        public void TwelveThousandOpenings_CreateOnlyTheVisibleRows()
        {
            List<SearchOpening> Many = Enumerable.Range(1, 30).SelectMany(Day => Enumerable.Range(1, 400)
                .Select(Site => Opening((990000000 + Site).ToString(), new DateTime(2027, 7, Day)))).ToList();
            SearchRunResult Big = RunResult(SearchRunStatus.Completed, Result("Alpha.yaml", SearchState.Complete, Many));
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers(), Run: (_, _, _, _) => Big));
            ListBox Rows = In<ListBox>(Window, "ResultRowsList");
            Assert.That(Rows.ItemsPanelRoot, Is.InstanceOf<VirtualizingStackPanel>());
            Assert.That(Rows.ItemCount, Is.EqualTo(12030));
            Assert.That(Rows.GetRealizedContainers().Count(), Is.InRange(1, 100));
            Rows.ScrollIntoView(12029);
            Settle(Window);
            Assert.That(Rows.GetRealizedContainers().Count(), Is.InRange(1, 100));
            Assert.That(Rows.ContainerFromIndex(12029)?.DataContext, Is.SameAs(Results.SelectedResult!.Rows[12029]));
            Window.Close();
        }

        // ---- isolation ----

        // 76, 77: Email.yaml's values (an Email output and a password) never reach the tab; Camp.History is neither read
        // (a marked opening is still shown, as in any manual search) nor written
        [AvaloniaTest]
        public void EmailSettingsAndCampHistory_AreNeitherUsedNorChanged()
        {
            DateTime Night = DateTime.Today.AddDays(2);
            string Marker = Path.Join(Folders.Config, "Camp.History", Night.Year.ToString(), Night.Month.ToString(), Night.Day.ToString(), "990000101");
            Directory.CreateDirectory(Marker);
            var (Window, _, Results) = ShowAfterRun(Seams(new TestGov(), new ManualTimers()), RunFirst: false);
            // the settings files and the marker (the lookups' database is not a settings file)
            Dictionary<string, string> Settings() => Fingerprint(Folders.Config).Where(F => !F.Key.StartsWith("RecreationDotOrg.db")).ToDictionary(F => F.Key, F => F.Value);
            Dictionary<string, string> Before = Settings();
            string[] DirectoriesBefore = Directory.GetDirectories(Folders.Config, "*", SearchOption.AllDirectories);
            Run(Results);
            Settle(Window);
            SearchResult Search = Results.LastResult!.Searches.Single();
            Assert.That(Search.Openings.Any(O => O.CampsiteId == "990000101" && O.Night == Night), Is.True, "a marked opening is shown");
            Assert.That(Before.Keys, Is.SupersetOf(new[] { "General.yaml", "Email.yaml", "Alpha.yaml" }));
            Assert.That(Settings(), Is.EqualTo(Before));
            Assert.That(Directory.GetDirectories(Folders.Config, "*", SearchOption.AllDirectories), Is.EquivalentTo(DirectoriesBefore), "no marker written");

            var Texts = new List<string?>();
            foreach (Control Each in View(Window).GetVisualDescendants().OfType<Control>())
            {
                Texts.Add((Each as TextBlock)?.Text);
                Texts.Add(ToolTip.GetTip(Each) as string);
                Texts.Add(AutomationProperties.GetName(Each));
                Texts.Add(AutomationProperties.GetHelpText(Each));
            }
            Texts.AddRange(new[] { Results.StatusText, Results.RunSummary, Results.ProgressDetails });
            Assert.That(Texts.Where(t => t != null && (t.Contains(ResultsFolders.Sentinel) || t.Contains("smtp.example.com") || t.Contains("someone@example.com"))), Is.Empty);
            Window.Close();
        }

        // 78, 79, 80, 81, 82: from the IL of every GUI type: no direct HTTP, no AvailabilityApi, no console pipeline, no
        // Camp.History or Email code; only the Results tab's own types use CampsiteSearch; the lookup types use nothing of
        // the search service; and a lookup session in the window starts no run. Since V011-1 (D61) the library's refresh
        // service is the one api member the GUI calls, only from the campground data refresh's seam (RefreshSeams).
        [AvaloniaTest]
        public void OnlyTheResultsTab_UsesTheSearchService_AndNothingInTheGuiSearchesAnotherWay()
        {
            Assembly Gui = typeof(ResultsViewModel).Assembly;
            string[] Forbidden =
            {
                "System.Net.Http.", "System.Net.Sockets.", "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.Base.Web::",
                "CampertronLibrary.function.RecDotOrg.data.Load::RunConsoleSearch", "CampertronLibrary.function.RecDotOrg.data.Load::Preload",
                "CampertronLibrary.function.RecDotOrg.data.Load::PostProcess", "CampertronLibrary.function.RecDotOrg.data.Load::OutputResults",
                "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults", "CampertronLibrary.function.RecDotOrg.data.Load::RecordNotificationHistory",
                "CampertronLibrary.function.RecDotOrg.data.Load::HasUnreportedEntries", "CampertronLibrary.function.RecDotOrg.data.Load::HasNewEntries",
                "CampertronLibrary.function.Base.CampsiteConfig::", "CampertronLibrary.function.Base.ConsoleUi::", "CampertronLibrary.function.Base.ResultsHtml::Render",
                "CampertronLibrary.function.Base.ConsoleResults::", "CampertronLibrary.function.RecDotOrg.search.HttpSearchTransport::", "System.Diagnostics.Process::",
            };
            const string RefreshRun = "CampertronLibrary.function.RecDotOrg.api.RidbRefreshService::Run";
            List<string> Called = ReferencedMembers(Gui.GetTypes()).Select(c => c.Member).Distinct().ToList();
            Assert.That(Called, Has.Count.GreaterThan(100));
            Assert.That(Called.Where(m => m != RefreshRun && Forbidden.Any(f => m.StartsWith(f, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Gui.GetTypes().Where(t => !t.IsNested && ReferencedMembers(t).Any(c => c.Member == RefreshRun)), Is.EqualTo(new[] { typeof(RefreshSeams) }));

            Type[] ResultsFeature =
            {
                typeof(ResultsSeams), typeof(ResultsViewModel), typeof(ResultsSearchRow), typeof(ResultSearchViewModel), typeof(ResultRow), typeof(ResultDateRow),
                typeof(ResultOpeningRow), typeof(ResultSiteRow), typeof(ResultStayRow), typeof(ResultText), typeof(BookingLink), typeof(ProgressGate), typeof(RunProbe),
            };
            var UsesTheService = Gui.GetTypes().Where(t => !t.IsNested && ReferencedMembers(t).Any(c => c.Member.StartsWith("CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::", StringComparison.Ordinal))).ToList();
            Assert.That(UsesTheService, Is.Not.Empty);
            Assert.That(UsesTheService, Is.SubsetOf(ResultsFeature));
            Assert.That(ReferencedMembers(typeof(ResultsSeams)).Select(c => c.Member), Does.Contain("CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Run")
                .And.Contain("CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Plan"));

            Type[] Lookup = { typeof(ReactiveViewModel), typeof(CampgroundDetails), typeof(CampgroundDetailsView), typeof(DisplayConverters) };
            Assert.That(ReferencedMembers(Lookup).Select(c => c.Member).Where(m => m.Contains("RecDotOrg.search.") || m.StartsWith("Search", StringComparison.Ordinal)
                || m.Contains("Campertron.ViewModels.Result") || m.Contains("ProgressGate")), Is.Empty);

            int Runs = 0;
            using var Requests = new WebRequestListener();
            Requests.Prove();
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers(), Run: (Plan, Progress, Token, Options) =>
            {
                Interlocked.Increment(ref Runs);
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            }));
            for (int Tab = 2; Tab >= 0; Tab--)
            {
                SelectTab(Window, Tab);
            }
            Select(Named<ComboBox>(Window, "ParkComboBox"), Items(Named<ComboBox>(Window, "ParkComboBox")).First());
            Settle(Window);
            Assert.That(Runs, Is.Zero, "the lookup tabs start no search");
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            ViewModel.Results!.CheckForExternalChanges();
            Run(ViewModel.Results);
            Assert.That(Runs, Is.EqualTo(1));
            Assert.That(Requests.Count, Is.Zero, "a search answered by the test transport makes no web request");
            Window.Close();
        }
    }
}
