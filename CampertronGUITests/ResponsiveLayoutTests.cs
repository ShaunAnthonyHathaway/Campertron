using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.search;
using System.Globalization;
using System.Text;
using System.Text.Json;
using static CampertronGUITests.GuiTestHelpers;
using static CampertronGUITests.ResultsTestHelpers;

namespace CampertronGUITests
{
    // The desktop GUI's responsive layout (decision D106, found in the maintainer's personal test of v0.3.0: maximized, the
    // content stayed in a 960 column at the left of the window and the rest stood empty). Every tab is a ResponsiveColumns:
    // below the wide width one column, centered; from it up a side column of 400 (416 with the Settings tab's own gutter) and
    // a main column. The boundary is the panels' width, so every tab switches at the same window width: 1191 is one column
    // and 1192 two (the panels are 1136 wide there, 1152 for Settings). Resizing moves and sizes the same controls; it
    // creates, drops and resets nothing, and maximize stays available.
    // How far a page grows is its own (decision D107, the maintainer's retest of D106: D106's caps for every page, 1416 for
    // the tabs and 960 for every main column, left large margins around Results and cut its campsite details short). The
    // tabs span the window; each page has a width category: readable (Settings, System: one column of at most 960, 400 + 16
    // + 960 at most, centered), data (Results: the results take the rest of the width, also in one column; the run card
    // alone at most 400 + 16 + 960 at the left) and mixed (Searches, a campground's details: one column of at most 960; a main
    // column of up to 960 + 16 + 960, whose content splits into two equal columns of at most 960 from 664 + 16 + 664 up;
    // running text at most 960 at the left; the Campgrounds filter card at most 400 + 16 + 960 at the columns' left).
    public class ResponsiveLayoutTests
    {
        private const double Readable = 960;
        private const double ReadableGroup = 400 + 16 + 960;
        private const double TwoColumnsMin = 664 + 16 + 664;
        private const double TwoColumnsMax = 960 + 16 + 960;
        private const int Boundary = 1192;

        private enum Category { Readable, Data, Mixed }

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

        // each tab with its responsive panel, its width category, its side column and its main column's greatest width (the
        // gutter-carrying Settings tab's values, 416 and 976): Searches, Results, Settings > Campertron, Settings > System,
        // Campgrounds (the filter card's and the details' panels, a campground selected)
        private static IEnumerable<(string Page, ResponsiveColumns Panel, Category Category, double Side, double Main)> Pages(MainWindow Window, MainWindowViewModel ViewModel)
        {
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            ViewModel.Searches!.SelectedFile ??= ViewModel.Searches.Files.First();
            Settle(Window);
            yield return ("Searches", Find<ResponsiveColumns>(Window, "SearchesLayout"), Category.Mixed, 400, TwoColumnsMax);
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Settle(Window);
            yield return ("Results", Find<ResponsiveColumns>(Window, "ResultsColumn"), Category.Data, 400, double.PositiveInfinity);
            Tabs(Window).SelectedIndex = 2;
            SettingsTabs(Window).SelectedIndex = 0;
            Settle(Window);
            yield return ("Settings", Find<ResponsiveColumns>(Window, "SettingsLayout"), Category.Readable, 416, 976);
            SettingsTabs(Window).SelectedIndex = 1;
            Settle(Window);
            yield return ("System", Find<ResponsiveColumns>(Window, "SystemLayout"), Category.Readable, 400, Readable);
            Tabs(Window).SelectedIndex = 3;
            SelectTab(Window, 0);
            if (Named<ComboBox>(Window, "ParkComboBox").SelectedItem == null)
            {
                Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
                SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDeltaID);
            }
            Settle(Window);
            yield return ("Campgrounds filters", (ResponsiveColumns)Named<Border>(Window, "ParkFilters").Parent!, Category.Mixed, 400, TwoColumnsMax);
            yield return ("Campground details", Find<ResponsiveColumns>(Details(Window, 0), "DetailsLayout"), Category.Mixed, 400, TwoColumnsMax);
        }

        // the columns' expected place in a panel: the group's left and width, the side and main widths (Settings: no gap, its
        // parts carry the gutter)
        private static (double Left, double Group, double Side, double Main) Expected(ResponsiveColumns Panel, Category Category, double Side, double Main)
        {
            double Width = Panel.Bounds.Width;
            double Gap = Side == 416 ? 0 : 16;
            if (!Panel.IsWide)
            {
                double Column = Category == Category.Data ? Width : Math.Min(Width, Side == 416 ? 976 : Readable);
                return ((Width - Column) / 2, Column, Column, Column);
            }
            double MainWidth = Math.Min(Main, Width - Side - Gap);
            double Group = Side + Gap + MainWidth;
            return ((Width - Group) / 2, Group, Side, MainWidth);
        }

        // ---- the shell and every tab, below, at and above the boundary ----

        [AvaloniaTest]
        public void EveryTab_IsOneCenteredColumnBelowTheBoundary_AndTwoColumnsFromIt_EachInItsWidthCategory([Values(720, 1024, 1191, 1192, 1280, 1440, 1600, 1920, 2048, 2560)] int Width)
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 900);
            Settle(Window);
            double Client = Window.ClientSize.Width;
            Rect TabsRect = In(Named<TabControl>(Window, "MainTabControl"), Window);
            Assert.That(TabsRect.X, Is.EqualTo(8).Within(0.5), "the tabs start at the window's margin (D107: no cap, no centering)");
            Assert.That(TabsRect.Width, Is.EqualTo(Client - 16).Within(0.5), "the tabs span the window");
            bool Wide = Width >= Boundary;
            foreach (var (Page, Panel, Kind, Side, Main) in Pages(Window, ViewModel))
            {
                string At = Page + " at " + Width;
                Assert.That(Panel.Classes.Contains(Kind.ToString().ToLowerInvariant()), Is.True, At + ": its width category");
                Assert.That(Panel.IsWide, Is.EqualTo(Wide), At + ": every tab switches at the same window width");
                Control[] Parts = Panel.Children.Where(c => c.IsVisible).ToArray();
                Rect PanelRect = In(Panel, Window);
                var (Left, Group, SideWidth, MainWidth) = Expected(Panel, Kind, Side, Main);
                Rect First = Slot(Parts[0], Window);
                Assert.That(First.X - PanelRect.X, Is.EqualTo(Left).Within(1), At + ": the columns' left");
                foreach (Control Part in Parts)
                {
                    Assert.That(Slot(Part, Window).Right, Is.LessThanOrEqualTo(Client + 0.5), At + ": wider than the window");
                }
                if (Parts.Length == 1)
                {
                    // a lone card: the column, or both columns up to the readable 400 + 16 + 960 at their left
                    Assert.That(First.Width, Is.EqualTo(Wide ? Math.Min(Group, ReadableGroup) : Group).Within(1), At + ": a lone card");
                    continue;
                }
                Rect Second = Slot(Parts[1], Window);
                if (Wide)
                {
                    Assert.That(First.Width, Is.EqualTo(SideWidth).Within(0.5), At + ": the side column");
                    Assert.That(Second.Width, Is.EqualTo(MainWidth).Within(0.5), At + ": the main column");
                    Assert.That(Second.X, Is.GreaterThanOrEqualTo(First.Right - 0.5), At + ": the columns overlap");
                    Assert.That(Second.Y, Is.EqualTo(First.Y).Within(0.5), At + ": the columns don't start at the top together");
                    Assert.That(Second.Right - First.X, Is.EqualTo(Group).Within(1), At + ": the columns' width");
                }
                else
                {
                    Assert.That(First.Width, Is.EqualTo(Group).Within(0.5), At + ": the one column");
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

        // The personal test's findings: a maximized or very wide window uses its width on every tab and leaves no large empty
        // area on one side (D106); a data page (Results) takes nearly the whole width with the window's usual margins, a mixed
        // page grows up to its two readable columns and a readable page keeps 400 + 16 + 960, both centered (D107).
        [AvaloniaTest]
        public void AVeryWideWindow_GivesEachPageItsWidth_AndCentersWhatIsCapped([Values(1920, 2048, 2560)] int Width, [Values("Light", "Dark")] string Theme)
        {
            Application.Current!.RequestedThemeVariant = Theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 1080);
            Settle(Window);
            double Client = Window.ClientSize.Width;
            foreach (var (Page, Panel, Kind, Side, _) in Pages(Window, ViewModel))
            {
                // the Settings tab's parts carry the 16 gutter themselves (its editor's scroll area is its main column)
                double Gutter = Side == 416 ? 16 : 0;
                Control[] Parts = Panel.Children.Where(c => c.IsVisible).ToArray();
                Rect Union = Parts.Select(p => Slot(p, Window)).Aggregate((a, b) => a.Union(b));
                double Left = Union.X;
                double Right = Client - Union.Right - (16 - Gutter);
                string At = Page + " at " + Width;
                double Area = Client - 56;
                if (Parts.Length == 1)
                {
                    Assert.That(Union.Width, Is.EqualTo(ReadableGroup).Within(1), At + ": the filter card keeps the readable width");
                    Assert.That(Left, Is.EqualTo(Kind == Category.Mixed ? 20 + (Area - (416 + Math.Min(Area - 416, TwoColumnsMax))) / 2 : 0).Within(1), At + ": the filter card starts with the details");
                    continue;
                }
                double Expected = Kind switch
                {
                    Category.Data => Area,
                    Category.Mixed => 416 + Math.Min(Area - 416, TwoColumnsMax),
                    _ => ReadableGroup + Gutter,
                };
                Assert.That(Union.Width, Is.EqualTo(Expected).Within(1), At + ": the page's width (" + Kind + ")");
                Assert.That(Math.Abs(Left - Right), Is.LessThanOrEqualTo(1), At + ": the empty space is on one side");
                if (Kind == Category.Data)
                {
                    Assert.That(Left, Is.EqualTo(20).Within(0.5), At + ": the data page keeps the window's usual margin, no more");
                }
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
            Assert.That(Side.Bounds.Width, Is.EqualTo(ReadableGroup).Within(0.51), "a hidden main part leaves both columns to the side part");
            Assert.That(Side.Bounds.Height, Is.EqualTo(50).Within(0.51), "a lone part keeps its own height");
            Host.Close();
        }

        // D107's three uses of the panel: no greatest width (data), two equal columns (pair) and a lone part's greatest width.
        [AvaloniaTest]
        public void ThePanel_StretchesAData_SplitsAPairEvenly_AndBoundsALonePartAtTheLeft()
        {
            var Side = new Border { Height = 50 };
            var Main = new Border { MinHeight = 70 };
            var Panel = new ResponsiveColumns { Children = { Side, Main }, MainMaxWidth = double.PositiveInfinity, NarrowMaxWidth = double.PositiveInfinity };
            var Host = new Window { Content = Panel, Width = 1100, Height = 600 };
            Host.Show();
            Settle(Host);
            Assert.That(Panel.IsWide, Is.False);
            Assert.That(Side.Bounds.Width, Is.EqualTo(1100).Within(0.51), "data: the one column takes the width");
            Host.Width = 2400;
            Settle(Host);
            Assert.That(Side.Bounds.Width, Is.EqualTo(400).Within(0.51), "data: the side column keeps its width");
            Assert.That(Main.Bounds.Width, Is.EqualTo(2400 - 416).Within(0.51), "data: the main column takes the rest");
            Assert.That(Side.Bounds.X, Is.EqualTo(0).Within(0.51));

            Panel.MainMaxWidth = 960;
            Panel.NarrowMaxWidth = double.PositiveInfinity;
            Panel.EvenColumns = true;
            Panel.WideMinWidth = TwoColumnsMin;
            foreach (double Width in new[] { 1343.0, 1344.0, 1600.0, 1936.0, 2400.0, 1343.0 })
            {
                Host.Width = Width;
                Settle(Host);
                bool Wide = Width >= TwoColumnsMin;
                Assert.That(Panel.IsWide, Is.EqualTo(Wide), "pair at " + Width);
                if (Wide)
                {
                    double Part = Math.Min((Width - 16) / 2, 960);
                    Assert.That(Side.Bounds.Width, Is.EqualTo(Part).Within(0.51), "pair: equal columns at " + Width);
                    Assert.That(Main.Bounds.Width, Is.EqualTo(Part).Within(0.51), "pair: equal columns at " + Width);
                    Assert.That(Side.Bounds.X, Is.EqualTo(Width - Main.Bounds.Right).Within(0.51), "pair: centered at " + Width);
                    Assert.That(Main.Bounds.Height, Is.EqualTo(70).Within(0.51), "pair: a part keeps its own height (a card is not stretched to the other's) at " + Width);
                }
                else
                {
                    Assert.That(Side.Bounds.Width, Is.EqualTo(Width).Within(0.51), "pair: one column as wide as the panel at " + Width);
                    Assert.That(Main.Bounds.Y, Is.EqualTo(Side.Bounds.Bottom + 12).Within(0.51), "pair: stacked at " + Width);
                }
            }

            Panel.EvenColumns = false;
            Panel.WideMinWidth = 1136;
            Panel.MainMaxWidth = TwoColumnsMax;
            Panel.LoneMaxWidth = ReadableGroup;
            Main.IsVisible = false;
            Host.Width = 2400;
            Settle(Host);
            double Group = 416 + TwoColumnsMax;
            Assert.That(Side.Bounds.Width, Is.EqualTo(ReadableGroup).Within(0.51), "a lone part keeps LoneMaxWidth");
            Assert.That(Side.Bounds.X, Is.EqualTo((2400 - Group) / 2).Within(0.51), "a lone part starts at the columns' left");
            Panel.NarrowMaxWidth = 960;
            Host.Width = 1000;
            Settle(Host);
            Assert.That(Side.Bounds.Width, Is.EqualTo(960).Within(0.51), "in one column a lone part takes the column");
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

        // ---- D107: each page's width ----

        // a synthetic campground whose campsites have realistic long details (a loop, a type, nine kinds of equipment, the
        // check-in and checkout), as recreation.gov's larger campgrounds have them
        private const string LongCampground = "990000300";
        private static readonly string[] LongSites = Enumerable.Range(1, 30).Select(i => (990003000 + i).ToString(CultureInfo.InvariantCulture)).ToArray();

        private static void WriteJson(string Folder, string Name, object Value) =>
            File.WriteAllText(Path.Join(Folder, Name), JsonSerializer.Serialize(Value), new UTF8Encoding(false));

        private void WriteLongCampground()
        {
            string Cache = Folders.Cache;
            WriteJson(Cache, LongCampground + "-CampInfo.json", new ReturnParkCampground { CampsiteName = "LOCAL TEST UPPER LOOP (synthetic)", ParkName = "Campertron Probe Park" });
            WriteJson(Cache, LongCampground + "-Campsites.json", LongSites.Select((Site, Index) => new CampsitesRecdata
            {
                CampsiteID = Site, FacilityID = LongCampground, CampsiteName = (Index + 1).ToString("000", CultureInfo.InvariantCulture),
                CampsiteType = "STANDARD NONELECTRIC", Loop = "UPPER PINES CAMPGROUND LOOP B", TypeOfUse = "Overnight",
            }).ToList());
            WriteJson(Cache, LongCampground + "-CampsitesByPark.json", LongSites.ToList());
            foreach (string Site in LongSites)
            {
                WriteJson(Cache, Site + "-Equipment.json", new List<string> { "Tent", "RV", "Trailer", "Pickup Camper", "Caravan/Camper Van", "Fifth Wheel", "Pop up", "Small Tent", "Large Tent Over 9X12`" });
                WriteJson(Cache, Site + "-Attributes.json", new List<AttributeValuePair>
                {
                    new AttributeValuePair { AttributeName = "Checkin Time", AttributeValue = "12:00 PM" },
                    new AttributeValuePair { AttributeName = "Checkout Time", AttributeValue = "11:00 AM" },
                });
            }
            WriteJson(Cache, LongCampground + "-Cached.json", DateTime.UtcNow.ToString(CultureInfo.InvariantCulture));
        }

        // The maintainer's state: three completed searches, a large result set with long campsite details, a campsite
        // selected. Results is a data page: the run card keeps its 400 and the results take the rest of the window, with the
        // window's usual margins, so a campsite's details show more the wider the window (in full from 2048 here); in one
        // column the run card stays above the results. The selection, the list's place and the actions stay through every
        // resize, also back to one column; nothing scrolls sideways.
        [AvaloniaTest]
        public void Results_TakeTheRestOfTheWidth_ShowMoreOfEachCampsite_AndKeepTheirState()
        {
            using var Log = new AvaloniaLogCapture();
            WriteLongCampground();
            Folders.Write("Alpha.yaml", ResultsFolders.Search("LOCAL TEST Upper loop one night (synthetic)", true, Campground: LongCampground));
            Folders.Write("Bravo.yaml", ResultsFolders.Search("LOCAL TEST Upper loop two nights (synthetic)", true, Campground: LongCampground, Consecutive: 2));
            Folders.Write("Charlie.yaml", ResultsFolders.Search("LOCAL TEST Upper loop next two weeks (synthetic)", true, Campground: LongCampground, Value: 14));
            var Gov = new TestGov { Answer = (Request, _) => new SearchResponse(200, RunProbe.MonthJson(Request.Month, LongSites)) };
            var (Window, ViewModel) = Folders.ShowWindow(Seams(Gov, new ManualTimers()));
            ResultsViewModel Results = ViewModel.Results!;
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Resize(Window, 2048, 1152);
            Settle(Window);
            Run(Results);
            Settle(Window);
            Assert.That(Results.ResultSearches.Count, Is.EqualTo(3), "three completed searches");
            Assert.That(Results.SelectedRows.Count, Is.GreaterThan(200), "a large result set");
            ResultRow Chosen = Results.SelectedRows.Where(r => r is ResultOpeningRow).Skip(40).First();
            string Details = ((ResultOpeningRow)Chosen).Details;
            Results.SelectedRow = Chosen;
            ListBox Rows = Find<ListBox>(Window, "ResultRowsList");
            Rows.ScrollIntoView(Chosen);
            Settle(Window);
            ScrollViewer RowsScroll = Rows.GetVisualDescendants().OfType<ScrollViewer>().First();
            double Offset = RowsScroll.Offset.Y;
            Assert.That(Offset, Is.GreaterThan(0), "the test needs a scrolled list");
            double LastResults = 0, LastDetails = 0;
            var Room = new Dictionary<int, double>();
            foreach (int Width in new[] { 720, 1024, 1191, 1192, 1440, 1920, 2048, 2560, 1024, 2048 })
            {
                string At = " at " + Width;
                Resize(Window, Width, Width <= 1024 ? 768 : 1080);
                Settle(Window);
                Rows.ScrollIntoView(Chosen);
                Settle(Window);
                double Client = Window.ClientSize.Width;
                Rect Run = In(Find<Border>(Window, "RunCard"), Window);
                Rect Card = In(Find<Border>(Window, "ResultsCard"), Window);
                Assert.That(Run.X, Is.EqualTo(20).Within(0.5), "the left margin" + At);
                Assert.That(Client - Card.Right, Is.EqualTo(36).Within(0.5), "the right margin (the 16 gutter for the scroll bar included)" + At);
                if (Width < Boundary)
                {
                    Assert.That(Card.X, Is.EqualTo(Run.X).Within(0.5), "one column" + At);
                    Assert.That(Card.Y, Is.GreaterThan(Run.Bottom), "the run card above the results" + At);
                    Assert.That(Card.Width, Is.EqualTo(Client - 56).Within(0.5), "the results take the width" + At);
                }
                else
                {
                    Assert.That(Run.Width, Is.EqualTo(400).Within(0.5), "the run card keeps its column" + At);
                    Assert.That(Card.X, Is.EqualTo(Run.Right + 16).Within(0.5), "the gap" + At);
                    Assert.That(Card.Width, Is.EqualTo(Client - 56 - 416).Within(0.5), "the results take the rest of the width" + At);
                    Assert.That(Card.Width, Is.GreaterThan(Run.Width), "most of the width is the results'" + At);
                    if (Width >= 1920)
                    {
                        Assert.That(Card.Width, Is.GreaterThan(Run.Width * 3), "a wide window's width is nearly all the results'" + At);
                    }
                }
                var Item = (ListBoxItem)Rows.ContainerFromItem(Chosen)!;
                Assert.That(Item, Is.Not.Null, "the chosen row is shown" + At);
                Assert.That(Item.IsSelected, Is.True, "the chosen row stays selected" + At);
                TextBlock Text = Item.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Details);
                Assert.That(Text.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis), "a cut detail ends in an ellipsis" + At);
                Assert.That(ToolTip.GetTip(Text), Is.EqualTo(Details), "the whole detail is its tooltip" + At);
                if (Width >= 1440 && Width > LastResults)
                {
                    Assert.That(Card.Width, Is.GreaterThan(LastResults), "the results grow with the window" + At);
                    Assert.That(Text.Bounds.Width, Is.GreaterThan(LastDetails), "the details get more room" + At);
                    LastResults = Card.Width;
                    LastDetails = Text.Bounds.Width;
                }
                Room[Width] = Text.Bounds.Width;
                Assert.That(Results.SelectedRow, Is.SameAs(Chosen), "the selection" + At);
                Assert.That(Find<Button>(Window, "OpenBookingButton").IsEffectivelyEnabled, Is.True, "Open booking page" + At);
                Assert.That(Find<Button>(Window, "CopyLinkButton").IsEffectivelyEnabled, Is.True, "Copy booking link" + At);
                Assert.That(Find<TextBlock>(Window, "SelectedCampsiteText").Text, Is.Not.Empty, "the chosen campsite" + At);
                Assert.That(RowsScroll.Extent.Width, Is.LessThanOrEqualTo(RowsScroll.Viewport.Width + 0.5), "the list scrolls sideways" + At);
                Assert.That(Find<ScrollViewer>(Window, "ResultsScroll").Extent.Width, Is.LessThanOrEqualTo(Find<ScrollViewer>(Window, "ResultsScroll").Viewport.Width + 0.5), "the tab scrolls sideways" + At);
            }
            // the room is layout, the same with every font: this details line needs about 1,510 in the GUI's own font (measured
            // in a rendered window, record §CT), and D106 gave it 906 on every wide window
            Assert.That(Room[2048], Is.GreaterThanOrEqualTo(1500), "at 2048 the details have the room the whole line needs");
            Assert.That(Room[2048], Is.GreaterThanOrEqualTo(Room[1440] * 1.5), "at 2048 the details have half as much room again as at 1440");
            Assert.That(Room[2560], Is.GreaterThan(Room[2048]), "and more still at 2560");
            Assert.That(Log.Entries, Is.Empty, string.Join("\n", Log.Entries));
            Window.Close();
        }

        // Before the first results the run card is alone: in one column as wide as it, from the boundary up at most the
        // readable 400 + 16 + 960 (D106's width, kept), at the window's usual margin under the tabs, where it stays when the
        // results come; never a huge empty results column, never centered between large margins.
        [AvaloniaTest]
        public void TheRunCardAlone_KeepsTheReadableWidth_AtTheLeft([Values(1024, 1440, 2048)] int Width)
        {
            Folders.Write("Alpha.yaml", ResultsFolders.Search("LOCAL TEST Alpha (synthetic)", true));
            var (Window, _) = Folders.ShowWindow(Seams(new TestGov(), new ManualTimers()));
            Tabs(Window).SelectedIndex = MainWindow.ResultsTabIndex;
            Resize(Window, Width, 900);
            Settle(Window);
            Assert.That(Find<Border>(Window, "ResultsCard").IsVisible, Is.False, "the test needs no results");
            Rect Run = In(Find<Border>(Window, "RunCard"), Window);
            double Area = Window.ClientSize.Width - 56;
            Assert.That(Run.Width, Is.EqualTo(Math.Min(Area, ReadableGroup)).Within(0.5), "the run card alone");
            Assert.That(Run.X, Is.EqualTo(20).Within(0.5), "at the window's usual margin, under the tabs");
            Window.Close();
        }

        // Searches is a mixed page: the files keep their 400; the editor takes the rest up to two readable columns, and once it
        // is 664 + 16 + 664 wide its filters stand beside its other cards (each at most 960); in one column its running text
        // keeps the readable 960 at the left of its card; long names stay usable (trimmed, the whole name as the tooltip).
        [AvaloniaTest]
        public void TheSearchEditor_GrowsUpToTwoReadableColumns_AndSplits_TheFilesStayBounded([Values(1191, 1440, 1600, 1920, 2048, 2560)] int Width)
        {
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 1080);
            Tabs(Window).SelectedIndex = MainWindow.SearchesTabIndex;
            ViewModel.Searches!.SelectedFile = ViewModel.Searches.Files.Single(f => f.FileName == "Bravo.yaml");
            Settle(Window);
            string At = " at " + Width;
            double Area = Window.ClientSize.Width - 56;
            Rect Files = In(Find<Border>(Window, "SearchFilesCard"), Window);
            Rect Editor = In(Find<SearchFileView>(Window, "SearchEditor"), Window);
            Rect Settings = In(Find<StackPanel>(Window, "SearchSettingsCards"), Window);
            Rect Filters = In(Find<Border>(Window, "FiltersCard"), Window);
            ResponsiveColumns Cards = Find<ResponsiveColumns>(Window, "SearchCards");
            if (Width < Boundary)
            {
                Assert.That(Editor.Width, Is.EqualTo(Readable).Within(0.5), "one readable column" + At);
                Assert.That(Cards.IsWide, Is.False);
            }
            else
            {
                Assert.That(Files.Width, Is.EqualTo(400).Within(0.5), "the files keep their column" + At);
                double EditorWidth = Math.Min(Area - 416, TwoColumnsMax);
                Assert.That(Editor.Width, Is.EqualTo(EditorWidth).Within(0.5), "the editor grows up to two readable columns" + At);
                Assert.That(Cards.IsWide, Is.EqualTo(EditorWidth >= TwoColumnsMin), "the editor splits from 664 + 16 + 664" + At);
                if (Cards.IsWide)
                {
                    double Part = Math.Min((EditorWidth - 16) / 2, Readable);
                    Assert.That(Settings.Width, Is.EqualTo(Part).Within(0.5), "the settings' column" + At);
                    Assert.That(Filters.Width, Is.EqualTo(Part).Within(0.5), "the filters' column" + At);
                    Assert.That(Filters.X, Is.EqualTo(Settings.Right + 16).Within(0.5), "the filters beside the other cards" + At);
                    Assert.That(Filters.Y, Is.EqualTo(Settings.Y).Within(0.5), "both columns start at the top" + At);
                    Assert.That(Part, Is.GreaterThanOrEqualTo(664 - 0.5), "a column never narrower than the window's least column" + At);
                    Border FiltersBorder = Find<Border>(Window, "FiltersCard");
                    Assert.That(FiltersBorder.Bounds.Height, Is.EqualTo(FiltersBorder.DesiredSize.Height).Within(0.5), "the filters card keeps its own height" + At);
                    Assert.That(Settings.Height, Is.EqualTo(Find<StackPanel>(Window, "SearchSettingsCards").DesiredSize.Height).Within(0.5), "the settings' cards keep their own height" + At);
                }
                else
                {
                    Assert.That(Filters.X, Is.EqualTo(Settings.X).Within(0.5), "one column" + At);
                    Assert.That(Filters.Y, Is.GreaterThan(Settings.Bottom), "the filters after the other cards" + At);
                }
            }
            // running text keeps the readable line length at its card's left; fields and lists stay inside their cards
            Border FiltersCard = Find<Border>(Window, "FiltersCard");
            foreach (TextBlock Text in FiltersCard.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Classes.Contains("body")))
            {
                Assert.That(Text.Bounds.Width, Is.LessThanOrEqualTo(Readable + 0.5), "running text wider than the readable 960" + At + ": " + Text.Text);
            }
            ListBox Campgrounds = Find<ListBox>(Window, "CampgroundList");
            Assert.That(Campgrounds.Bounds.Width, Is.GreaterThan(600), "the campground list keeps room for long names" + At);
            Campgrounds.ScrollIntoView(Campgrounds.Items.OfType<CampgroundPickerEntry>().First(e => e.Title.Contains("VERY LONG NAME")));
            Settle(Window);
            TextBlock Title = Campgrounds.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PickerEntryTitle" && t.Text!.Contains("VERY LONG NAME"));
            Assert.That(Title.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis), "a long campground name is trimmed" + At);
            Assert.That(ToolTip.GetTip((Control)Title.Parent!.Parent!) as string, Does.Contain(Title.Text), "and its whole name is the tooltip" + At);
            TextBlock FileName = Find<ListBox>(Window, "SearchFilesList").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text != null && t.Text.Contains("long display name"));
            Assert.That(FileName.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis), "a long search name is trimmed in the list" + At);
            Assert.That(Find<TextBlock>(Window, "SearchTitle").TextWrapping, Is.Not.EqualTo(TextWrapping.NoWrap), "the editor's title wraps" + At);
            Window.Close();
        }

        // Settings is a readable page: from 1920 up the editor stays the readable 976 (with its gutter) and does not grow with
        // the window, centered with the files; no field is wider than its card.
        [AvaloniaTest]
        public void Settings_KeepTheReadableWidth_OnAnyWideWindow([Values(1440, 1920, 2560)] int Width)
        {
            var (Window, ViewModel, _) = Show();
            Resize(Window, Width, 1080);
            Tabs(Window).SelectedIndex = 2;
            SettingsTabs(Window).SelectedIndex = 0;
            ViewModel.Settings!.IsEmailSelected = true;
            Settle(Window);
            Rect Editor = In(Find<ScrollViewer>(Window, "SettingsScrollViewer"), Window);
            Rect Files = In(Find<Border>(Window, "SettingsFilesCard"), Window);
            Assert.That(Editor.Width, Is.EqualTo(Math.Min(976, Window.ClientSize.Width - 40 - 416)).Within(0.5), "the editor's readable width at " + Width);
            Assert.That(Files.Width, Is.EqualTo(400).Within(0.5));
            double Left = Files.X - 20;
            double Right = Window.ClientSize.Width - 20 - Editor.Right;
            Assert.That(Math.Abs(Left - Right), Is.LessThanOrEqualTo(1), "centered at " + Width);
            foreach (TextBox Box in Find<EmailSettingsView>(Window, "EmailEditor").GetVisualDescendants().OfType<TextBox>().Where(b => b.IsEffectivelyVisible))
            {
                Assert.That(Box.Bounds.Width, Is.LessThanOrEqualTo(960), (Box.Name ?? "a field") + " at " + Width);
            }
            Window.Close();
        }

        // A campground's details are a mixed page: the long texts' column grows with the window up to two readable columns
        // and splits once it is 664 + 16 + 664 wide (the description beside the directions and the fees); running text never
        // gets wider than the readable 960; the filter card keeps the readable 400 + 16 + 960 at the details' left.
        [AvaloniaTest]
        public void TheCampgroundDetails_GrowAndSplit_WithReadableText([Values(1440, 1600, 1920, 2560)] int Width)
        {
            var (Window, _, _) = Show();
            Resize(Window, Width, 1080);
            Tabs(Window).SelectedIndex = 3;
            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkThree);
            SelectCampground(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundDeltaID);
            Settle(Window);
            CampgroundDetailsView View = Details(Window, 0);
            string At = " at " + Width;
            double Area = Window.ClientSize.Width - 56;
            double MainWidth = Math.Min(Area - 416, TwoColumnsMax);
            Rect Side = In(Find<StackPanel>(View, "DetailsColumn"), Window);
            Rect Main = In(Find<StackPanel>(View, "DetailsMainColumn"), Window);
            Assert.That(Side.Width, Is.EqualTo(400).Within(0.5), At);
            Assert.That(Main.Width, Is.EqualTo(MainWidth).Within(0.5), "the long texts' column grows" + At);
            ResponsiveColumns Texts = Find<ResponsiveColumns>(View, "DetailsTexts");
            Assert.That(Texts.IsWide, Is.EqualTo(MainWidth >= TwoColumnsMin), "the split" + At);
            Rect Description = In(Find<SelectableTextBlock>(View, "DescriptionText"), Window);
            Rect Directions = In(Find<SelectableTextBlock>(View, "DirectionsText"), Window);
            if (Texts.IsWide)
            {
                Assert.That(Directions.X, Is.GreaterThan(Description.Right), "the directions beside the description" + At);
            }
            else
            {
                Assert.That(Directions.Y, Is.GreaterThan(Description.Bottom), "the directions after the description" + At);
            }
            foreach (string Name in new[] { "DescriptionText", "DirectionsText", "FeesText" })
            {
                Assert.That(Find<SelectableTextBlock>(View, Name).Bounds.Width, Is.LessThanOrEqualTo(Readable + 0.5), Name + " wider than a readable line" + At);
            }
            Rect Filters = In(Named<Border>(Window, "ParkFilters"), Window);
            Assert.That(Filters.Width, Is.EqualTo(Math.Min(ReadableGroup, 416 + MainWidth)).Within(0.5), "the filter card stays bounded" + At);
            Assert.That(Filters.X, Is.EqualTo(Side.X).Within(0.5), "the filter card starts with the details" + At);
            Window.Close();
        }

        // The tabs span the window and stay where they are; switching tabs gives each page its own width again (no page's
        // width stays on the next).
        [AvaloniaTest]
        public void SwitchingTabs_GivesEachPageItsOwnWidth_AndTheTabsStay()
        {
            var (Window, ViewModel, _) = Show();
            Resize(Window, 2048, 1152);
            Settle(Window);
            ViewModel.Searches!.SelectedFile ??= ViewModel.Searches.Files.First();
            double Area = Window.ClientSize.Width - 56;
            Rect? Header = null;
            foreach (int Tab in new[] { MainWindow.ResultsTabIndex, 2, MainWindow.ResultsTabIndex, MainWindow.SearchesTabIndex, 2, 3, MainWindow.ResultsTabIndex, 2 })
            {
                Tabs(Window).SelectedIndex = Tab;
                SettingsTabs(Window).SelectedIndex = 0;
                Settle(Window);
                Rect Now = In(Window.GetVisualDescendants().OfType<TabItem>().First(t => t.Name == "SearchesTab"), Window);
                Header ??= Now;
                Assert.That(Now.X, Is.EqualTo(Header.Value.X).Within(0.5), "the tab headers moved on tab " + Tab);
                Rect TabsRect = In(Named<TabControl>(Window, "MainTabControl"), Window);
                Assert.That(TabsRect.Width, Is.EqualTo(Window.ClientSize.Width - 16).Within(0.5), "the tabs span the window on tab " + Tab);
                (string Name, double Width) Page = Tab switch
                {
                    0 => ("SearchesLayout", 416 + Math.Min(Area - 416, TwoColumnsMax)),
                    1 => ("ResultsColumn", Area),
                    2 => ("SettingsLayout", ReadableGroup + 16),
                    _ => ("DetailsLayout", 0),
                };
                if (Page.Width == 0)
                {
                    continue;
                }
                ResponsiveColumns Panel = Find<ResponsiveColumns>(Window, Page.Name);
                Rect Union = Panel.Children.Where(c => c.IsVisible).Select(p => Slot(p, Window)).Aggregate((a, b) => a.Union(b));
                Assert.That(Union.Width, Is.EqualTo(Page.Width).Within(1), Page.Name + " after switching to tab " + Tab);
            }
            Window.Close();
        }
    }
}
