using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;

namespace CampertronGUITests
{
    // Avalonia 12 migration regression tests for the real App, MainWindow and view models. The window runs on the
    // headless platform with the fixture database from GuiTestEnvironment (no 226 MB extraction).
    public class MainWindowTests
    {
        private static readonly string[] ExpectedTabs = { "By Park", "By City", "By State" };

        // Same construction as App.OnFrameworkInitializationCompleted for the classic desktop lifetime.
        private static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
        {
            var ViewModel = new MainWindowViewModel();
            var Window = new MainWindow { DataContext = ViewModel };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            return (Window, ViewModel);
        }

        private static TabControl Tabs(MainWindow Window)
        {
            return Window.FindControl<TabControl>("MainTabControl") ?? throw new AssertionException("MainTabControl not found");
        }

        // Selects a tab and returns its content panel, which declares DataContext="{Binding ReactiveViewModel}".
        private static StackPanel SelectTab(MainWindow Window, int Index)
        {
            TabControl TabControl = Tabs(Window);
            TabControl.SelectedIndex = Index;
            Dispatcher.UIThread.RunJobs();
            var Item = (TabItem)TabControl.Items[Index]!;
            return (StackPanel)Item.Content!;
        }

        private static List<ComboBox> ComboBoxes(StackPanel TabContent)
        {
            return TabContent.GetLogicalDescendants().OfType<ComboBox>().ToList();
        }

        // The details panel (DataContext="{Binding CampgroundByName}") is the second child of the tab content.
        private static StackPanel DetailsPanel(StackPanel TabContent)
        {
            return (StackPanel)TabContent.Children[1];
        }

        private static string? DetailsFacilityIdText(StackPanel TabContent)
        {
            return DetailsPanel(TabContent).GetLogicalDescendants().OfType<TextBlock>().First().Text;
        }

        private static List<string> Items(ComboBox ComboBox)
        {
            return ComboBox.ItemsSource?.Cast<string>().ToList() ?? new List<string>();
        }

        private static void Select(ComboBox ComboBox, string Value)
        {
            ComboBox.SelectedItem = Value;
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaTest]
        public void Application_InitializesWithFluentLightThemeAndViewLocator()
        {
            Assert.That(Application.Current, Is.InstanceOf<App>());
            App CurrentApp = (App)Application.Current!;
            Assert.That(CurrentApp.Styles.OfType<FluentTheme>().Count(), Is.EqualTo(1));
            Assert.That(CurrentApp.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Light));
            Assert.That(CurrentApp.DataTemplates.OfType<Campertron.ViewLocator>().Count(), Is.EqualTo(1));
        }

        [AvaloniaTest]
        public void MainWindow_ConstructsAndShows_UsingFixtureDatabaseWithoutExtraction()
        {
            using var Log = new AvaloniaLogCapture();

            var (Window, ViewModel) = ShowMainWindow();

            Assert.That(Window.Title, Is.EqualTo("Campertron"));
            Assert.That(Window.IsVisible, Is.True);
            Assert.That(Window.Width, Is.EqualTo(1024));
            Assert.That(Window.Height, Is.EqualTo(768));
            Assert.That(Window.DataContext, Is.SameAs(ViewModel));
            // Load.GetConfig() in the constructor kept the fixture: no extraction, no repair backup.
            Assert.That(new FileInfo(GuiTestEnvironment.DbFile).Length, Is.LessThan(GuiTestEnvironment.MaxFixtureDbBytes));
            Assert.That(Directory.GetFiles(GuiTestEnvironment.ConfigPath, "*.bak"), Is.Empty);
            Assert.That(Log.BindingEntries, Is.Empty);

            Window.Close();
        }

        [AvaloniaTest]
        public void MainWindow_HasTheThreeLookupTabsInOrder()
        {
            var (Window, _) = ShowMainWindow();

            List<string?> Headers = Tabs(Window).Items.Cast<TabItem>().Select(t => t.Header as string).ToList();

            Assert.That(Headers, Is.EqualTo(ExpectedTabs));
            Window.Close();
        }

        // Avalonia 12 changed how TabItem content inherits DataContext (AvaloniaUI/Avalonia #20541, #20856). Every tab's
        // content must still resolve {Binding ReactiveViewModel} against the window's MainWindowViewModel, and its details
        // panel {Binding CampgroundByName} against that ReactiveViewModel.
        [AvaloniaTest]
        public void EachTabContent_HasReactiveViewModelDataContext_AndIsPresentedInTheWindow()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = ShowMainWindow();

            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                StackPanel Content = SelectTab(Window, Index);

                Assert.That(Content.DataContext, Is.SameAs(ViewModel.ReactiveViewModel), ExpectedTabs[Index]);
                Assert.That(TopLevel.GetTopLevel(Content), Is.SameAs(Window), ExpectedTabs[Index] + " content is not presented");
                Assert.That(DetailsPanel(Content).DataContext, Is.InstanceOf<FacilitiesData>(), ExpectedTabs[Index]);
            }
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        [AvaloniaTest]
        public void Tabs_ListTheFixtureDataThroughCompiledBindings()
        {
            var (Window, _) = ShowMainWindow();

            List<ComboBox> ByPark = ComboBoxes(SelectTab(Window, 0));
            Assert.That(Items(ByPark[0]), Is.EqualTo(new[] { GuiTestEnvironment.ParkOne, GuiTestEnvironment.ParkTwo }));

            List<ComboBox> ByCity = ComboBoxes(SelectTab(Window, 1));
            Assert.That(Items(ByCity[0]), Is.EqualTo(new[] { GuiTestEnvironment.StateColorado, GuiTestEnvironment.StateUtah }));
            Assert.That(Items(ByCity[1]), Is.Empty);

            List<ComboBox> ByState = ComboBoxes(SelectTab(Window, 2));
            Assert.That(Items(ByState[0]), Is.EqualTo(new[] { GuiTestEnvironment.StateColorado, GuiTestEnvironment.StateUtah }));
            Window.Close();
        }

        // ComboBox.SelectedItem (two-way binding) -> RaiseAndSetIfChanged -> WhenAnyValue subscription ->
        // RaisePropertyChanged of the dependent list -> ComboBox.ItemsSource, ending in the details panel.
        [AvaloniaTest]
        public void ByCitySelections_FlowThroughReactiveUiToTheDetailsPanel()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = ShowMainWindow();
            StackPanel Content = SelectTab(Window, 1);
            List<ComboBox> Combo = ComboBoxes(Content);

            Select(Combo[0], GuiTestEnvironment.StateUtah);
            Assert.That(ViewModel.ReactiveViewModel.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(Items(Combo[1]), Is.EqualTo(new[] { GuiTestEnvironment.CityAlpha }));

            Select(Combo[1], GuiTestEnvironment.CityAlpha);
            Assert.That(Items(Combo[2]), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundAlpha }));

            Select(Combo[2], GuiTestEnvironment.CampgroundAlpha);
            Assert.That(ViewModel.ReactiveViewModel.SelectedCampground, Is.EqualTo(GuiTestEnvironment.CampgroundAlpha));
            Assert.That(DetailsFacilityIdText(Content), Does.EndWith(GuiTestEnvironment.CampgroundAlphaID));

            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        [AvaloniaTest]
        public void ByParkAndByStateSelections_UpdateTheirCampgroundLists()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();

            StackPanel ByPark = SelectTab(Window, 0);
            List<ComboBox> ParkCombo = ComboBoxes(ByPark);
            Select(ParkCombo[0], GuiTestEnvironment.ParkTwo);
            Assert.That(Items(ParkCombo[1]), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundBravo }));
            Select(ParkCombo[1], GuiTestEnvironment.CampgroundBravo);
            Assert.That(DetailsFacilityIdText(ByPark), Does.EndWith(GuiTestEnvironment.CampgroundBravoID));

            StackPanel ByState = SelectTab(Window, 2);
            List<ComboBox> StateCombo = ComboBoxes(ByState);
            Select(StateCombo[0], GuiTestEnvironment.StateColorado);
            Assert.That(Items(StateCombo[1]), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundBravo }));

            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // Exercises every tab and selection path with log capture: Avalonia must log no warning or error at all
        // (binding, property, layout or other areas) for the whole session.
        [AvaloniaTest]
        public void FullLookupSession_LogsNoAvaloniaWarningsOrErrors()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();

            List<ComboBox> ByPark = ComboBoxes(SelectTab(Window, 0));
            Select(ByPark[0], GuiTestEnvironment.ParkOne);
            Select(ByPark[1], GuiTestEnvironment.CampgroundAlpha);
            List<ComboBox> ByCity = ComboBoxes(SelectTab(Window, 1));
            Select(ByCity[0], GuiTestEnvironment.StateColorado);
            Select(ByCity[1], GuiTestEnvironment.CityBravo);
            Select(ByCity[2], GuiTestEnvironment.CampgroundBravo);
            List<ComboBox> ByState = ComboBoxes(SelectTab(Window, 2));
            Select(ByState[0], GuiTestEnvironment.StateUtah);
            Select(ByState[1], GuiTestEnvironment.CampgroundAlpha);
            Window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.That(Log.Entries, Is.Empty);
        }

        [AvaloniaTest]
        public void Close_ClosesTheWindowCleanly()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();
            int ClosedCount = 0;
            Window.Closed += (_, _) => ClosedCount++;
            Exception? Unhandled = null;
            Dispatcher.UIThread.UnhandledException += (_, e) => Unhandled = e.Exception;

            Window.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.That(ClosedCount, Is.EqualTo(1));
            Assert.That(Window.IsVisible, Is.False);
            Assert.That(Unhandled, Is.Null);
            Assert.That(Log.Entries, Is.Empty);
        }
    }
}
