using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
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
    // V011-3 (decision D63): the GUI's words for a search's autoRun setting, "Enabled (autoRun)" and "Not enabled", and the
    // behavior they describe, in the real window on the headless platform: the GUI never searches because it starts, the
    // Results tab selects the enabled searches by default and runs one that is not enabled when it is selected, a new
    // search is not enabled, the setting is told apart from errors and from the Results tab's choice, and no word says
    // that a search runs at startup. Every run answers through a test transport; folders of the test's own.
    public class AutoRunWordingTests
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

        private static T In<T>(Control Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        // the words V011-3 replaced: none of them may describe autoRun again (case ignored)
        internal static readonly string[] Retired = { "startup", "at start", "next start", "when campertron starts", "run this search", "disabled", "does not run until" };

        internal static IEnumerable<string> RetiredIn(IEnumerable<string?> Texts) =>
            Texts.Where(T => T != null && Retired.Any(R => T.Contains(R, StringComparison.OrdinalIgnoreCase))).Select(T => T!);

        // every text a user or a screen reader gets from a view: the texts, tooltips, accessible names and help texts
        private static List<string?> TextsOf(Control Root)
        {
            var Texts = new List<string?>();
            foreach (Control Each in Root.GetVisualDescendants().OfType<Control>().Prepend(Root))
            {
                Texts.Add((Each as TextBlock)?.Text);
                Texts.Add((Each as ContentControl)?.Content as string);
                Texts.Add(ToolTip.GetTip(Each) as string);
                Texts.Add(AutomationProperties.GetName(Each));
                Texts.Add(AutomationProperties.GetHelpText(Each));
            }
            return Texts;
        }

        // ---- the behavior the words describe ----

        // behaviors 6, 7 and 8: the window starts and shows both tabs without searching; the enabled file is selected by
        // default, the one that is not enabled is not, and it runs when it is selected; its file still says autoRun: false
        [AvaloniaTest]
        public void TheGuiStart_RunsNoSearch_TheEnabledFileIsSelected_AndANotEnabledOneRunsWhenSelected()
        {
            Folders.Write("Enabled.yaml", ResultsFolders.Search("Enabled", true));
            Folders.Write("Off.yaml", ResultsFolders.Search("Off", false));
            string OffBefore = File.ReadAllText(Folders.In("Off.yaml"));
            var Runs = new List<IReadOnlyList<string>>();
            var Gov = new TestGov();
            var (Window, ViewModel) = Folders.ShowWindow(Seams(Gov, new ManualTimers(), Run: (Plan, Progress, Token, Options) =>
            {
                lock (Runs)
                {
                    Runs.Add(Plan.Entries.Select(E => E.FileName).ToList());
                }
                return CampsiteSearch.Run(Plan, Progress, Token, Options);
            }));
            Settle(Window);
            Assert.That(ViewModel.Searches!.IsLoaded, Is.True, "the Searches tab, shown at the start, read its files");
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            ResultsViewModel Results = ViewModel.Results!;
            Assert.That(Runs, Is.Empty, "the GUI searched nothing because it started");
            Assert.That(Gov.Requests, Is.Empty);
            Assert.That(Row(Results, "Enabled.yaml").IsSelected, Is.True, "an enabled file is selected by default");
            Assert.That(Row(Results, "Off.yaml").IsSelected, Is.False, "a file that is not enabled is not");
            Assert.That(Results.IsRunning, Is.False);

            Row(Results, "Off.yaml").IsSelected = true;
            Run(Results);
            Assert.That(Runs.Single(), Is.EqualTo(new[] { "Enabled.yaml", "Off.yaml" }));
            Assert.That(Results.LastResult!.Searches.Single(S => S.FileName == "Off.yaml").State, Is.EqualTo(SearchState.Complete), "it ran when selected");
            Assert.That(File.ReadAllText(Folders.In("Off.yaml")), Is.EqualTo(OffBefore), "its autoRun: false is unchanged");
            Results.CheckForExternalChanges();
            Assert.That(Row(Results, "Off.yaml").IsEnabled, Is.False);
            Assert.That(Row(Results, "Off.yaml").IsSelected, Is.True, "the user's choice stays");
            Window.Close();
        }

        // behaviors 9 and 10: with no search file the GUI creates none (the console creates its example when it next
        // searches, and the notice says so); New search creates a search that is not enabled, which the Results tab lists
        // unselected and can run when it is selected
        [AvaloniaTest]
        public void WithNoSearchFile_TheGuiCreatesNone_AndANewSearchIsNotEnabled_ButCanBeSelected()
        {
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Settle(Window);
            SearchesViewModel Searches = ViewModel.Searches!;
            Assert.That(Searches.HasNoFiles, Is.True);
            Assert.That(Searches.FolderNoticeText, Is.EqualTo("Info: There is no search file. Create one with New search. While there is none, the console creates "
                + "and runs its example search the next time you choose Search or start email mode."));
            Assert.That(AutomationProperties.GetLiveSetting(Named<SearchesView>(Window, "SearchesContent").GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Name == "FolderNoticeText")), Is.EqualTo(AutomationLiveSetting.Polite));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Assert.That(Directory.GetFiles(Folders.Config, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml", "Email.yaml" }),
                "the GUI's start and its tabs create no search file");

            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Searches.NewSearchName = "Trip";
            Searches.NewSearchCommand.Execute(null);
            Settle(Window);
            Assert.That(Searches.NewSearchMessage, Is.EqualTo("Created Trip.yaml with the example search's settings. It is not enabled (autoRun: false): the Results tab "
                + "runs it when you select it, and the console's Search and email mode run it once you enable it and save."));
            TextBlock Message = In<TextBlock>(Named<SearchesView>(Window, "SearchesContent"), "NewSearchMessage");
            Assert.That(AutomationProperties.GetLiveSetting(Message), Is.EqualTo(AutomationLiveSetting.Polite), "the result is announced");
            SearchFileViewModel Trip = Searches.SelectedFile!;
            Assert.That(Trip.AutoRun.IsChecked, Is.False);
            Assert.That(Trip.ListDetails, Does.Contain(" · Not enabled · "));
            Assert.That(Trip.ListAccessibleName, Does.EndWith(". The console's Search and email mode skip it; the Results tab runs it only when you select it."));

            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            ResultsSearchRow Row_ = Row(ViewModel.Results!, "Trip.yaml");
            Assert.That((Row_.IsEnabled, Row_.IsSelected, Row_.IsRunnable), Is.EqualTo((false, false, true)), "listed, not selected, can run");
            Assert.That(Row_.CheckBoxName, Is.EqualTo("Zion non-group tent friendly sites (Trip.yaml), Not enabled: runs when selected"));
            Row_.IsSelected = true;
            Assert.That(ViewModel.Results!.CanStart, Is.True, "it can be run when it is selected");
            Window.Close();
        }

        // behavior 11: the setting, the validation and the Results tab's choice are told apart: an enabled file with an
        // error is "Enabled (autoRun)" with errors that keep it from being searched (never "selected by default"); one that
        // is not enabled is never told as unusable
        [AvaloniaTest]
        public void TheSetting_IsToldApartFromErrors_AndFromTheResultsChoice()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", false));
            Folders.Write("Broken.yaml", ResultsFolders.Search("Broken", true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(30) + ", " + ResultsFolders.Date(30) + "]"));
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Settle(Window);
            SearchesViewModel Searches = ViewModel.Searches!;
            SearchFileViewModel File(string Name) => Searches.Files.Single(F => F.FileName == Name);

            Assert.That(File("Alpha.yaml").ListAccessibleName, Does.StartWith("Alpha.yaml, Alpha · Enabled (autoRun) · ").And.Not.Contain("Has errors")
                .And.EndWith(". The console's Search and email mode run it, and the Results tab selects it by default."));
            Assert.That(File("Beta.yaml").ListAccessibleName, Does.StartWith("Beta.yaml, Beta · Not enabled · ")
                .And.EndWith(". The console's Search and email mode skip it; the Results tab runs it only when you select it."));
            Assert.That(File("Beta.yaml").ListAccessibleName, Does.Not.Contain("error").And.Not.Contain("can't"), "not enabled is not unusable");
            Assert.That(File("Broken.yaml").ListAccessibleName, Does.Contain(" · Enabled (autoRun) · ").And.Contain("Has errors")
                .And.EndWith(". It has errors, so it can't be searched until they are fixed."));
            Assert.That(File("Broken.yaml").ListStatusIsError, Is.True, "an error is styled and said in words");
            ListBox List = In<ListBox>(Named<SearchesView>(Window, "SearchesContent"), "SearchFilesList");
            Assert.That(List.GetVisualDescendants().OfType<ListBoxItem>().Select(I => AutomationProperties.GetName(I)),
                Is.EquivalentTo(Searches.Files.Select(F => F.ListAccessibleName)), "the list items are read as their whole entry");

            // the editor: the enabled file's check box explains the setting; a file that is not enabled says what that means
            Searches.SelectedFile = File("Alpha.yaml");
            Settle(Window);
            CheckBox Box = In<CheckBox>(Named<SearchesView>(Window, "SearchesContent"), "AutoRunCheckBox");
            Assert.That(AutomationProperties.GetName(Box), Is.EqualTo("Enabled (autoRun)"));
            Assert.That(AutomationProperties.GetHelpText(Box), Is.EqualTo(AutoRunWording.Hint));
            Assert.That(AutoRunWording.Hint, Does.Contain("each time you choose Search in the console").And.Contain("at every check of email mode")
                .And.Contain("selects enabled searches by default").And.Contain("runs one that is not enabled when you select it")
                .And.Contain("doesn't make the GUI search by itself"));
            Searches.SelectedFile = File("Beta.yaml");
            Settle(Window);
            Assert.That(AutomationProperties.GetHelpText(In<CheckBox>(Named<SearchesView>(Window, "SearchesContent"), "AutoRunCheckBox")),
                Does.StartWith("Info: Not enabled (autoRun: false): the console's Search and email mode skip this search."));

            // the Results tab: the choice for the files that can run, the setting alone for the one that can't
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            ResultsViewModel Results = ViewModel.Results!;
            Assert.That(Row(Results, "Alpha.yaml").ChoiceText, Is.EqualTo("Enabled (autoRun): selected by default"));
            Assert.That(Row(Results, "Beta.yaml").ChoiceText, Is.EqualTo("Not enabled: runs when selected"));
            Assert.That(Row(Results, "Broken.yaml").ChoiceText, Is.EqualTo("Enabled (autoRun)"));
            Assert.That(Row(Results, "Broken.yaml").CheckBoxName, Is.EqualTo("Broken (Broken.yaml), Enabled (autoRun), can't be searched now"));
            Assert.That(Row(Results, "Broken.yaml").HelpText, Does.Contain("Can't be searched:"));
            Window.Close();
        }

        // ---- the words ----

        // no text, tooltip, accessible name or help text of the Searches and Results tabs, and none of their view models'
        // texts, says that a search runs at startup or uses the retired words, in every state the words differ in
        [AvaloniaTest]
        public void NoWordOfTheSearchesAndResultsTabs_SaysASearchRunsAtStartup()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", false));
            Folders.Write("Broken.yaml", ResultsFolders.Search("Broken", true, SearchBy: "StartEndDate", Dates: "[" + ResultsFolders.Date(30) + ", " + ResultsFolders.Date(30) + "]"));
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Settle(Window);
            SearchesViewModel Searches = ViewModel.Searches!;
            var Texts = new List<string?>();
            foreach (SearchFileViewModel Each in Searches.Files.ToList())
            {
                Searches.SelectedFile = Each;
                Settle(Window);
                Texts.AddRange(TextsOf(Named<SearchesView>(Window, "SearchesContent")));
                Texts.AddRange(new[] { Each.ListDetails, Each.ListStatus, Each.ListAccessibleName, Each.AutoRun.HelpText, Each.AutoRun.MessageText, Each.AutoRun.Hint });
            }
            // the folder without a usable enabled search (Alpha saved not enabled), and an unsaved change (the Results tab
            // blocks that file)
            SearchFileViewModel Alpha = Searches.Files.Single(F => F.FileName == "Alpha.yaml");
            Searches.SelectedFile = Alpha;
            Alpha.AutoRun.IsChecked = false;
            Alpha.SaveCommand.Execute(null);
            Settle(Window);
            Assert.That(Searches.FolderNoticeText, Does.StartWith("Warning: No usable search file is enabled (autoRun: true)"));
            Texts.Add(Searches.FolderNoticeText);
            Alpha.DisplayName.Text = "Alpha, unsaved";
            Settle(Window);
            Texts.AddRange(TextsOf(Named<SearchesView>(Window, "SearchesContent")));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Assert.That(Row(ViewModel.Results!, "Alpha.yaml").Reason, Is.EqualTo("Save or revert the changes in the Searches tab first."));
            Texts.AddRange(TextsOf(Named<ResultsView>(Window, "ResultsContent")));
            Texts.AddRange(ViewModel.Results!.Searches.SelectMany(R => new[] { R.Details, R.ChoiceText, R.SettingText, R.CheckBoxName, R.HelpText, R.AccessibleName, R.Reason }));
            Alpha.RevertCommand.Execute(null);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Settle(Window);
            Searches.NewSearchName = "New one";
            Searches.NewSearchCommand.Execute(null);
            Settle(Window);
            Texts.Add(Searches.NewSearchMessage);
            Texts.AddRange(TextsOf(Named<SearchesView>(Window, "SearchesContent")));

            Assert.That(Texts.Count(T => !string.IsNullOrEmpty(T)), Is.GreaterThan(200), "the sweep saw the tabs");
            Assert.That(RetiredIn(Texts).Distinct(), Is.Empty);
            List<string> Said = Texts.Where(T => !string.IsNullOrEmpty(T)).Select(T => T!).ToList();
            Assert.That(Said, Has.Some.Contains("Enabled (autoRun)").And.Some.Contains("Not enabled"), "the canonical words are there");
            Window.Close();
        }

        // ---- layout and accessibility ----

        // the longer words fit: in Light and Dark, from 720x540 to a large window, the check box and its explanation stay
        // in the Searches tab's column (the explanation wraps, nothing is cut off), the Results tab's choice text is trimmed
        // only on screen with the whole text in the tooltip and the accessible name, the selection buttons stay whole, the
        // choice is the check box's own state, and Avalonia reports no warning (the keyboard order is pinned, unchanged, by
        // ResultsViewTests and SearchEditorTests)
        [AvaloniaTest]
        [TestCase("Light")]
        [TestCase("Dark")]
        public void TheNewWords_FitSmallAndLargeWindows_InLightAndDark(string Theme)
        {
            using var Log = new AvaloniaLogCapture();
            Application.Current!.RequestedThemeVariant = Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            Folders.Write("Alpha.yaml", ResultsFolders.Search("Alpha", true));
            Folders.Write("Beta.yaml", ResultsFolders.Search("Beta", false));
            var (Window, ViewModel) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            foreach ((double Width, double Height) in new[] { (720.0, 540.0), (1024.0, 768.0), (1920.0, 1200.0) })
            {
                Resize(Window, Width, Height);
                Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
                ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.Single(F => F.FileName == "Beta.yaml");
                Settle(Window);
                Control Searches = Named<SearchesView>(Window, "SearchesContent");
                double Column = In<StackPanel>(Searches, "SearchesColumn").Bounds.Width;
                Assert.That(Column, Is.LessThanOrEqualTo(Window.ClientSize.Width), Width + "x" + Height);
                CheckBox Box = In<CheckBox>(Searches, "AutoRunCheckBox");
                Assert.That(Box.Bounds.Width, Is.LessThanOrEqualTo(Column), "the check box at " + Width + "x" + Height);
                TextBlock Hint = Box.Parent!.Parent is StackPanel Field ? Field.Children.OfType<TextBlock>().Single(t => t.Text == AutoRunWording.Hint) : throw new AssertionException("no hint");
                Assert.That(Hint.IsEffectivelyVisible, Is.True);
                Assert.That(Hint.TextWrapping, Is.EqualTo(TextWrapping.Wrap), "the explanation wraps");
                Assert.That(Hint.TextTrimming, Is.EqualTo(TextTrimming.None), "and is never cut off");
                Assert.That(Hint.Bounds.Width, Is.LessThanOrEqualTo(Column), "the explanation at " + Width + "x" + Height);
                TextBlock Notice = In<TextBlock>(Searches, "FolderNoticeText");
                Assert.That(Notice.Bounds.Width, Is.LessThanOrEqualTo(Column));

                Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
                Settle(Window);
                Control Results = Named<ResultsView>(Window, "ResultsContent");
                Rect Client = new Rect(Window.ClientSize);
                foreach (string Button in new[] { "SelectEnabledButton", "SelectAllButton", "ClearSelectionButton" })
                {
                    Control Each = In<Control>(Results, Button);
                    Rect Bounds = new Rect(Each.TranslatePoint(new Point(0, 0), Window)!.Value, Each.Bounds.Size);
                    Assert.That(Client.Contains(Bounds), Is.True, Button + " at " + Width + "x" + Height + ": " + Bounds);
                }
                foreach (TextBlock Details in Results.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "SearchDetails"))
                {
                    ResultsSearchRow Row_ = (ResultsSearchRow)Details.DataContext!;
                    Assert.That(Details.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
                    Assert.That(ToolTip.GetTip(Details), Is.EqualTo(Row_.Details).And.EndWith(Row_.ChoiceText), "the whole text in the tooltip");
                }
            }
            CheckBox[] Boxes = Named<ResultsView>(Window, "ResultsContent").GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.That(Boxes.Select(b => AutomationProperties.GetName(b)), Is.EquivalentTo(new[]
            {
                "Alpha (Alpha.yaml), Enabled (autoRun): selected by default", "Beta (Beta.yaml), Not enabled: runs when selected",
            }));
            Assert.That(Boxes.Select(b => b.IsChecked), Is.EquivalentTo(new bool?[] { true, false }), "the choice is the check box's own state, not a color");
            Button SelectEnabled = In<Button>(Named<ResultsView>(Window, "ResultsContent"), "SelectEnabledButton");
            Assert.That(SelectEnabled.Content, Is.EqualTo("Select enabled"));
            Assert.That(AutomationProperties.GetName(SelectEnabled), Is.EqualTo("Select the enabled searches"));
            Assert.That(AutomationProperties.GetHelpText(SelectEnabled), Does.Contain("Nothing runs until Start."));
            Assert.That(AutomationProperties.GetHelpText(In<Button>(Named<ResultsView>(Window, "ResultsContent"), "SelectAllButton")), Does.Contain("enabled or not"));
            Assert.That(Log.Entries, Is.Empty, string.Join("\n", Log.Entries));
            Window.Close();
        }
    }
}
