using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Forms;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's responsive layout (decision D106, found in the maintainer's personal test of v0.3.0: maximized, the
    // content stayed in a 960 column at the left of the window and the rest stood empty). Every tab is a ResponsiveColumns:
    // below the wide width one column of at most 960 (976 with the Settings tab's own gutter), centered; from it up a side
    // column of 400 (416) and a main column of up to 960 (976), centered together; the tabs and their content are capped at
    // ShellMaxWidth (1416) and centered as one. The boundary is the panels' width, so every tab switches at the same window
    // width: 1191 is one column and 1192 two (the panels are 1136 wide there, 1152 for Settings). Resizing moves and sizes
    // the same controls; it creates, drops and resets nothing, and maximize stays available.
    public class ResponsiveLayoutTests
    {
        private const double Shell = 1416;
        private const double Group = 400 + 16 + 960;
        private const int Boundary = 1192;

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
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                Window.UpdateLayout();
            }
        }

        private static T Find<T>(Visual Root, string Name) where T : Control =>
            Root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == Name) ?? throw new AssertionException(Name + " not found");

        private static Rect In(Control Control, Visual Root) =>
            new Rect(Control.TranslatePoint(new Point(0, 0), Root)!.Value, Control.Bounds.Size);

        // a part's place in its panel: its bounds and its margin (the Settings tab's card carries the 16 gutter as one)
        private static Rect Slot(Control Part, Visual Root)
        {
            Rect At = In(Part, Root);
            Thickness M = Part.Margin;
            return new Rect(At.X - M.Left, At.Y - M.Top, At.Width + M.Left + M.Right, At.Height + M.Top + M.Bottom);
        }

        // the window with three synthetic search files, after one run of the Results tab (so its results card is shown)
        private (MainWindow Window, MainWindowViewModel ViewModel, TestGov Gov) Show()
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("LOCAL TEST Alpha (synthetic)", true));
            Folders.Write("Bravo.yaml", ResultsFolders.Search("LOCAL TEST Bravo with a long display name that keeps going to test trimming (synthetic)", true, Consecutive: 2));
            Folders.Write("Charlie.yaml", ResultsFolders.Search("LOCAL TEST Charlie (synthetic)", false));
            var Gov = new TestGov();
            var (Window, ViewModel) = Folders.ShowWindow(Seams(Gov, new ManualTimers()));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Run(ViewModel.Results!);
            Settle(Window);
            return (Window, ViewModel, Gov);
        }

        // each tab with its responsive panel (and the gutter-carrying Settings tab's values): Searches, Results, Settings >
        // Campertron, Settings > System, Campgrounds (the filter card's and the details' panels, a campground selected)
        private static IEnumerable<(string Page, ResponsiveColumns Panel, double Side, double Main)> Pages(MainWindow Window, MainWindowViewModel ViewModel)
        {
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            ViewModel.Searches!.SelectedFile ??= ViewModel.Searches.Files.First();
            Settle(Window);
            yield return ("Searches", Find<ResponsiveColumns>(Window, "SearchesLayout"), 400, 960);
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            yield return ("Results", Find<ResponsiveColumns>(Window, "ResultsColumn"), 400, 960);
            Tabs(Window).SelectedIndex = 2;
            SettingsTabs(Window).SelectedIndex = 0;
            Settle(Window);
            yield return ("Settings", Find<ResponsiveColumns>(Window, "SettingsLayout"), 416, 976);
            SettingsTabs(Window).SelectedIndex = 1;
            Settle(Window);
            yield return ("System", Find<ResponsiveColumns>(Window, "SystemLayout"), 400, 960);
            Tabs(Window).SelectedIndex = 3;
            SelectTab(Window, 0);
            if (Named<ComboBox>(Window, "ParkComboBox").SelectedItem == null)
            {
                Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
                SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDeltaID);
            }
            Settle(Window);
            yield return ("Campgrounds filters", (ResponsiveColumns)Named<Border>(Window, "ParkFilters").Parent!, 400, 960);
            yield return ("Campground details", Find<ResponsiveColumns>(Details(Window, 0), "DetailsLayout"), 400, 960);
        }

        // ---- the shell and every tab, below, at and above the boundary ----

        [AvaloniaTest]
        public void EveryTab_IsOneCenteredColumnBelowTheBoundary_AndTwoColumnsFromIt([Values(720, 1024, 1191, 1192, 1280, 1440, 1920, 2048, 2560)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 900);
            Settle(Window);
            double Client = Window.ClientSize.Width;
            TabControl TabsControl = Named<TabControl>(Window, "MainTabControl");
            Rect TabsRect = In(TabsControl, Window);
            Assert.That(TabsRect.Width, Is.EqualTo(Math.Min(Client - 16, Shell)).Within(0.5), "the tabs fill the window up to the shell's cap");
            Assert.That(TabsRect.X - 8, Is.EqualTo(Client - TabsRect.Right - 8).Within(1), "the tabs are not centered");
            bool Wide = Width >= Boundary;
            foreach (var (Page, Panel, Side, Main) in Pages(Window, ViewModel))
            {
                string At = Page + " at " + Width;
                Assert.That(Panel.IsWide, Is.EqualTo(Wide), At + ": every tab switches at the same window width");
                Control[] Parts = Panel.Children.Where(c => c.IsVisible).ToArray();
                Rect PanelRect = In(Panel, Window);
                Rect First = Slot(Parts[0], Window);
                Rect Union = Parts.Select(p => Slot(p, Window)).Aggregate((a, b) => a.Union(b));
                Assert.That(Union.X - PanelRect.X, Is.EqualTo(PanelRect.Right - Union.Right).Within(1), At + ": the content is not centered in its area");
                Assert.That(Union.Right, Is.LessThanOrEqualTo(Client + 0.5), At + ": wider than the window");
                if (Parts.Length == 1)
                {
                    Assert.That(First.Width, Is.EqualTo(Wide ? Math.Min(PanelRect.Width, Side + (Side == 416 ? 0 : 16) + Main) : Math.Min(PanelRect.Width, Main)).Within(1), At + ": a lone card");
                    continue;
                }
                Rect Second = Slot(Parts[1], Window);
                if (Wide)
                {
                    Assert.That(First.Width, Is.EqualTo(Side).Within(0.5), At + ": the side column");
                    Assert.That(Second.Width, Is.EqualTo(Math.Min(Main, PanelRect.Width - Side - (Side == 416 ? 0 : 16))).Within(0.5), At + ": the main column");
                    Assert.That(Second.X, Is.GreaterThanOrEqualTo(First.Right - 0.5), At + ": the columns overlap");
                    Assert.That(Second.Y, Is.EqualTo(First.Y).Within(0.5), At + ": the columns don't start at the top together");
                }
                else
                {
                    Assert.That(First.Width, Is.EqualTo(Math.Min(PanelRect.Width, Main)).Within(0.5), At + ": the one column");
                    Assert.That(Second.X, Is.EqualTo(First.X).Within(0.5), At + ": one column");
                    Assert.That(Second.Width, Is.EqualTo(First.Width).Within(0.5), At + ": one column");
                    Assert.That(Second.Y, Is.GreaterThanOrEqualTo(First.Bottom - 0.5), At + ": the main part is not below the side part");
                }
            }
            foreach (ScrollViewer Scroll in Window.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible && s.Viewport.Width > 0))
            {
                Assert.That(Scroll.Extent.Width, Is.LessThanOrEqualTo(Scroll.Viewport.Width + 0.5), (Scroll.Name ?? "a scroll viewer") + " scrolls sideways at " + Width);
            }
            Assert.That(Log.Entries, Is.Empty, string.Join("\n", Log.Entries));
            Window.Close();
        }

        // The personal test's finding: a maximized or very wide window centers the capped content and uses its width; the
        // old 960 column at the left (with the rest empty) must not return on any tab.
        [AvaloniaTest]
        public void AVeryWideWindow_UsesTheColumns_AndCentersThem_OnEveryTab([Values(1920, 2048, 2560)] int Width, [Values("Light", "Dark")] string Theme)
        {
            Application.Current!.RequestedThemeVariant = Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 1080);
            Settle(Window);
            double Client = Window.ClientSize.Width;
            foreach (var (Page, Panel, Side, _) in Pages(Window, ViewModel))
            {
                // the Settings tab's parts carry the 16 gutter themselves (its editor's scroll area is its main column)
                double Gutter = Side == 416 ? 16 : 0;
                Rect Union = Panel.Children.Where(c => c.IsVisible).Select(p => Slot(p, Window)).Aggregate((a, b) => a.Union(b));
                Assert.That(Union.Width, Is.EqualTo(Group + Gutter).Within(1), Page + " does not use the two columns' width at " + Width);
                Assert.That(Union.X, Is.GreaterThan(Width / 8.0), Page + " is left-aligned at " + Width);
                // centered with the 16 gutter on its right (the scroll bar's place): as much space left as right of it
                double Left = Union.X;
                double Right = Client - Union.Right - (16 - Gutter);
                Assert.That(Math.Abs(Left - Right), Is.LessThanOrEqualTo(1), Page + " leaves the empty space on one side at " + Width);
            }
            Window.Close();
        }

        // ---- the panel itself ----

        [AvaloniaTest]
        public void ThePanel_SwitchesExactlyAtItsBoundary_AndBack_KeepingItsChildren()
        {
            var Side = new Border { Name = "S", Height = 50 };
            var Main = new Border { Name = "M", MinHeight = 80 };
            var Panel = new ResponsiveColumns { Children = { Side, Main } };
            var Host = new Window { Content = Panel, Width = 1135, Height = 600 };
            Host.Show();
            foreach (double Width in new[] { 1135.0, 1136.0, 1135.0, 1136.0, 2400.0, 720.0, 1136.0, 1135.0 })
            {
                Host.Width = Width;
                Settle(Host);
                bool Wide = Width >= 1136;
                Assert.That(Panel.IsWide, Is.EqualTo(Wide), "at " + Width);
                Assert.That(Panel.Classes.Contains(":wide"), Is.EqualTo(Wide), "the :wide pseudo-class at " + Width);
                Assert.That(Panel.Children, Is.EquivalentTo(new Control[] { Side, Main }), "the same two children");
                Assert.That(Panel.Children[0], Is.SameAs(Side), "the order");
                if (Wide)
                {
                    Assert.That(Side.Bounds.Width, Is.EqualTo(400).Within(0.51));
                    Assert.That(Main.Bounds.X, Is.EqualTo(Side.Bounds.Right + 16).Within(0.51));
                    Assert.That(Main.Bounds.Width, Is.EqualTo(Math.Min(960, Width - 416)).Within(0.51));
                    Assert.That(Side.Bounds.X, Is.EqualTo(Width - Main.Bounds.Right).Within(0.51), "centered");
                    Assert.That(Main.Bounds.Height, Is.EqualTo(600).Within(0.51), "the main part fills the height");
                }
                else
                {
                    Assert.That(Side.Bounds.Width, Is.EqualTo(Math.Min(960, Width)).Within(0.51));
                    Assert.That(Main.Bounds.Y, Is.EqualTo(Side.Bounds.Bottom + 12).Within(0.51));
                    Assert.That(Main.Bounds.Height, Is.EqualTo(600 - 50 - 12).Within(0.51), "the main part fills the rest of the height");
                    Assert.That(Side.Bounds.X, Is.EqualTo((Width - Side.Bounds.Width) / 2).Within(0.51), "centered");
                }
            }
            Main.IsVisible = false;
            Host.Width = 1920;
            Settle(Host);
            Assert.That(Side.Bounds.Width, Is.EqualTo(Group).Within(0.51), "a hidden main part leaves both columns to the side part");
            Assert.That(Side.Bounds.Height, Is.EqualTo(50).Within(0.51), "a lone part keeps its own height");
            Host.Close();
        }

        // ---- state across resizing ----

        // Resizing narrow, wide, narrow and wide again moves the same controls: the chosen search file, an unsaved edit and a
        // conflict on the Searches tab, an unsaved General.yaml edit, the chosen campground and a search that runs all stay.
        [AvaloniaTest]
        public void Resizing_KeepsTheSelection_TheUnsavedEdits_TheConflict_TheCampground_AndARun()
        {
            var (Window, ViewModel, Gov) = Show();
            SearchesViewModel Searches = ViewModel.Searches!;
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            Searches.SelectedFile = Searches.Files.Single(f => f.FileName == "Bravo.yaml");
            Settle(Window);
            SearchFileViewModel Chosen = Searches.SelectedFile!;
            Chosen.TotalHumans.Text = "3";
            File.AppendAllText(Folders.In("Bravo.yaml"), "# changed by another program\n");
            Chosen.SaveCommand.Execute(null);
            Settle(Window);
            Assert.That(Chosen.HasConflict, Is.True, "the test needs a conflict");
            SearchFileView Editor = Find<SearchFileView>(Window, "SearchEditor");
            ViewModel.Settings!.General.RefreshInterval.Text = "7";
            Tabs(Window).SelectedIndex = 3;
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDeltaID);
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            Gov.Answer = TestGov.Stall;
            Start(ViewModel.Results!);
            Settle(Window);
            Assert.That(ViewModel.Results!.CanCancel, Is.True, "the test needs a run that goes on");
            foreach (int Width in new[] { 1024, 1920, 1024, 2560, 1191, 1192 })
            {
                Resize(Window, Width, 900);
                foreach (int Tab in new[] { 0, 1, 2, 3 })
                {
                    Tabs(Window).SelectedIndex = Tab;
                    Settle(Window);
                }
                string At = " after resizing to " + Width;
                Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
                Settle(Window);
                Assert.That(Searches.SelectedFile, Is.SameAs(Chosen), "the chosen search file" + At);
                Assert.That(Chosen.TotalHumans.Text, Is.EqualTo("3"), "the unsaved edit" + At);
                Assert.That(Chosen.HasConflict, Is.True, "the conflict" + At);
                Assert.That(Find<SearchFileView>(Window, "SearchEditor"), Is.SameAs(Editor), "the editor is the same control" + At);
                Assert.That(Window.GetVisualDescendants().OfType<SearchFileView>().Count(), Is.EqualTo(1), "one editor" + At);
                Assert.That(ViewModel.Settings!.General.RefreshInterval.Text, Is.EqualTo("7"), "the unsaved General.yaml edit" + At);
                Assert.That(ViewModel.ReactiveViewModel!.SelectedCampgroundByPark?.FacilityID, Is.EqualTo(GuiTestEnvironment.CampgroundDeltaID), "the campground" + At);
                Tabs(Window).SelectedIndex = 3;
                Settle(Window);
                Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.CampgroundDeltaID), "its details" + At);
                Assert.That(ViewModel.Results!.CanCancel, Is.True, "the run" + At);
            }
            ViewModel.Results!.CancelCommand.Execute(null);
            WaitForEnd(ViewModel.Results);
            Assert.That(ViewModel.Results.CanCancel, Is.False, "Cancel still works after the resizing");
            Window.Close();
        }

        // ---- keyboard, messages, bounded lists, maximize ----

        // Both layouts keep the controls in the same order, so the reading order and the Tab order are the same.
        [AvaloniaTest]
        public void TheTabOrder_IsTheSameInBothLayouts()
        {
            var (Window, ViewModel, _) = Show();
            List<string> Order(int Width)
            {
                Resize(Window, Width, 900);
                Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
                ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.First();
                Settle(Window);
                return Named<SearchesView>(Window, "SearchesContent").GetVisualDescendants().OfType<Control>()
                    .Where(c => c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && !string.IsNullOrEmpty(c.Name)).Select(c => c.Name!).ToList();
            }
            List<string> Narrow = Order(1024);
            List<string> Wide = Order(1920);
            Assert.That(Narrow, Is.Not.Empty);
            Assert.That(Wide, Is.EqualTo(Narrow));
            foreach (int Width in new[] { 1024, 1920 })
            {
                Resize(Window, Width, 900);
                Settle(Window);
                TextBox NewName = Find<TextBox>(Window, "NewSearchNameBox");
                NewName.Focus();
                Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                Dispatcher.UIThread.RunJobs();
                Assert.That((Focused(Window) as Control)?.Name, Is.EqualTo("NewSearchButton"), "Tab after the new search's name at " + Width);
            }
            Window.Close();
        }

        // A refused value's message and the file-state warnings stay inside their cards and the window at the boundary.
        [AvaloniaTest]
        public void Messages_StayInsideTheirCards_OnBothSidesOfTheBoundary([Values(1191, 1192, 1920)] int Width)
        {
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 900);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.Single(f => f.FileName == "Bravo.yaml");
            ViewModel.Searches.SelectedFile!.TotalHumans.Text = "abc";
            Settle(Window);
            Assert.That(ViewModel.Searches.SelectedFile.TotalHumans.IsError, Is.True, "the test needs an error");
            Border Card = Find<Border>(Window, "DatesCard");
            Rect CardRect = In(Card, Window);
            TextBlock[] Messages = Card.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && t.Text == ViewModel.Searches.SelectedFile.TotalHumans.MessageText).ToArray();
            Assert.That(Messages, Is.Not.Empty, "the message is shown");
            foreach (TextBlock Message in Messages)
            {
                Rect M = In(Message, Window);
                Assert.That(CardRect.Contains(M), Is.True, "the message leaves its card at " + Width + ": " + M + " in " + CardRect);
                Assert.That(M.Right, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5));
                Assert.That(Message.Bounds.Width, Is.GreaterThan(100), "the message is squeezed at " + Width);
            }
            Tabs(Window).SelectedIndex = 2;
            SettingsTabs(Window).SelectedIndex = 0;
            ViewModel.Settings!.IsEmailSelected = true;
            Settle(Window);
            Border EmailCard = Find<EmailSettingsView>(Window, "EmailEditor").GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("card") && b.IsEffectivelyVisible);
            Rect Email = In(EmailCard, Window);
            foreach (TextBlock Text in EmailCard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)))
            {
                Rect T = In(Text, Window);
                Assert.That(T.Right, Is.LessThanOrEqualTo(Email.Right + 0.5), "text outside its card at " + Width + ": " + Text.Text);
            }
            Window.Close();
        }

        // The campground picker's list keeps its bounded height in the wider main column.
        [AvaloniaTest]
        public void TheCampgroundPicker_StaysBounded([Values(1024, 1920)] int Width)
        {
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 1080);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.First();
            Settle(Window);
            ListBox List = Find<ListBox>(Window, "CampgroundList");
            Assert.That(List.Bounds.Height, Is.LessThanOrEqualTo(260.5));
            Assert.That(In(List, Window).Right, Is.LessThanOrEqualTo(Window.ClientSize.Width + 0.5));
            Window.Close();
        }

        // Maximize and resizing stay available: the fix makes the content follow the window, not the other way round.
        [AvaloniaTest]
        public void MaximizeAndResizing_StayAvailable()
        {
            var (Window, _, _) = Show();
            Assert.That(Window.CanResize, Is.True);
            Assert.That(Window.CanMaximize, Is.True);
            Assert.That(Window.MinWidth, Is.EqualTo(720));
            Assert.That(Window.MinHeight, Is.EqualTo(540));
            Window.WindowState = WindowState.Maximized;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Window.WindowState, Is.EqualTo(WindowState.Maximized));
            Window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.That(Window.WindowState, Is.EqualTo(WindowState.Normal));
            Window.Close();
        }
    }
}
