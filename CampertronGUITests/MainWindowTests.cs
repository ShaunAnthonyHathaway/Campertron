using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // Regression tests for the real App, MainWindow and view models (Avalonia 12 migration, then the beta.8 layout).
    // The window runs on the headless platform with the fixture database from GuiTestEnvironment (no 226 MB extraction).
    public class MainWindowTests
    {
        [AvaloniaTest]
        public void Application_InitializesWithFluentThemeFollowingTheSystemAndViewLocator()
        {
            Assert.That(Application.Current, Is.InstanceOf<App>());
            App CurrentApp = (App)Application.Current!;
            Assert.That(CurrentApp.Styles.OfType<FluentTheme>().Count(), Is.EqualTo(1));
            // beta.8 (decision D28): follow the operating system's Light or Dark theme instead of forcing Light.
            Assert.That(CurrentApp.RequestedThemeVariant, Is.EqualTo(ThemeVariant.Default));
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
        // content must still resolve {Binding ReactiveViewModel} against the window's MainWindowViewModel, and its
        // details view its own CampgroundDetails property against that ReactiveViewModel.
        [AvaloniaTest]
        public void EachTabContent_HasReactiveViewModelDataContext_AndIsPresentedInTheWindow()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = ShowMainWindow();

            for (int Index = 0; Index < ExpectedTabs.Length; Index++)
            {
                Grid Content = SelectTab(Window, Index);

                Assert.That(Content.DataContext, Is.SameAs(ViewModel.ReactiveViewModel), ExpectedTabs[Index]);
                Assert.That(TopLevel.GetTopLevel(Content), Is.SameAs(Window), ExpectedTabs[Index] + " content is not presented");
                Assert.That(Details(Window, Index).DataContext, Is.InstanceOf<CampgroundDetails>(), ExpectedTabs[Index]);
            }
            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        [AvaloniaTest]
        public void Tabs_ListTheFixtureDataThroughCompiledBindings()
        {
            var (Window, _) = ShowMainWindow();

            SelectTab(Window, 0);
            Assert.That(Items(Named<ComboBox>(Window, "ParkComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.ParkOne, GuiTestEnvironment.ParkThree, GuiTestEnvironment.ParkTwo }));

            SelectTab(Window, 1);
            Assert.That(Items(Named<ComboBox>(Window, "CityStateComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.StateColorado, GuiTestEnvironment.StateUtah }));
            Assert.That(Items(Named<ComboBox>(Window, "CityComboBox")), Is.Empty);

            SelectTab(Window, 2);
            Assert.That(Items(Named<ComboBox>(Window, "StateComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.StateColorado, GuiTestEnvironment.StateUtah }));
            Window.Close();
        }

        // ComboBox.SelectedItem (two-way binding) -> RaiseAndSetIfChanged -> WhenAnyValue subscription ->
        // RaisePropertyChanged of the dependent list -> ComboBox.ItemsSource, ending in the details view.
        [AvaloniaTest]
        public void ByCitySelections_FlowThroughReactiveUiToTheDetailsView()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, ViewModel) = ShowMainWindow();
            SelectTab(Window, 1);

            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateUtah);
            Assert.That(ViewModel.ReactiveViewModel.SelectedState, Is.EqualTo(GuiTestEnvironment.StateUtah));
            Assert.That(Items(Named<ComboBox>(Window, "CityComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.EmptyCity, GuiTestEnvironment.CityAlpha }));

            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityAlpha);
            Assert.That(Items(Named<ComboBox>(Window, "CityCampgroundComboBox")), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundAlpha }));

            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            Assert.That(ViewModel.ReactiveViewModel.SelectedCampground, Is.EqualTo(GuiTestEnvironment.CampgroundAlpha));
            Assert.That(DisplayedId(Details(Window, 1)), Is.EqualTo(GuiTestEnvironment.CampgroundAlphaID));

            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        [AvaloniaTest]
        public void ByParkAndByStateSelections_UpdateTheirCampgroundLists()
        {
            using var Log = new AvaloniaLogCapture();
            var (Window, _) = ShowMainWindow();

            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkTwo);
            Assert.That(Items(Named<ComboBox>(Window, "ParkCampgroundComboBox")), Is.EqualTo(new[] { GuiTestEnvironment.CampgroundBravo }));
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundBravo);
            Assert.That(DisplayedId(Details(Window, 0)), Is.EqualTo(GuiTestEnvironment.CampgroundBravoID));

            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            Assert.That(Items(Named<ComboBox>(Window, "StateCampgroundComboBox")),
                Is.EqualTo(new[] { GuiTestEnvironment.CampgroundBravo, GuiTestEnvironment.CampgroundDelta }));
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundBravo);
            Assert.That(DisplayedId(Details(Window, 2)), Is.EqualTo(GuiTestEnvironment.CampgroundBravoID));

            Assert.That(Log.BindingEntries, Is.Empty);
            Window.Close();
        }

        // Exercises every tab and selection path with log capture: Avalonia must log no warning or error at all
        // (binding, property, layout or other areas) for the whole session, in the Light and the Dark theme.
        [AvaloniaTest]
        public void FullLookupSession_LogsNoAvaloniaWarningsOrErrors([Values("Light", "Dark")] string Variant)
        {
            using var Log = new AvaloniaLogCapture();
            Application.Current!.RequestedThemeVariant = Variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            var (Window, _) = ShowMainWindow();

            SelectTab(Window, 0);
            Select(Named<ComboBox>(Window, "ParkComboBox"), GuiTestEnvironment.ParkOne);
            Select(Named<ComboBox>(Window, "ParkCampgroundComboBox"), GuiTestEnvironment.CampgroundAlpha);
            SelectTab(Window, 1);
            Select(Named<ComboBox>(Window, "CityStateComboBox"), GuiTestEnvironment.StateColorado);
            Select(Named<ComboBox>(Window, "CityComboBox"), GuiTestEnvironment.CityBravo);
            Select(Named<ComboBox>(Window, "CityCampgroundComboBox"), GuiTestEnvironment.CampgroundBravo);
            SelectTab(Window, 2);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateUtah);
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundCharlie);
            Select(Named<ComboBox>(Window, "StateComboBox"), GuiTestEnvironment.StateColorado);
            Select(Named<ComboBox>(Window, "StateCampgroundComboBox"), GuiTestEnvironment.CampgroundDelta);
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
