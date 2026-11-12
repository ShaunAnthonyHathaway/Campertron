using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Campertron.ViewModels;
using Campertron.Views;

namespace CampertronGUITests
{
    // Shared helpers for the headless GUI tests. Controls are found by their x:Name, not by tree position.
    internal static class GuiTestHelpers
    {
        public static readonly string[] ExpectedTabs = { "By Park", "By City", "By State" };

        public static readonly string[] ContentNames = { "ByParkContent", "ByCityContent", "ByStateContent" };
        public static readonly string[] DetailsNames = { "ByParkDetails", "ByCityDetails", "ByStateDetails" };

        // Same construction as App.OnFrameworkInitializationCompleted for the classic desktop lifetime.
        public static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
        {
            var ViewModel = new MainWindowViewModel();
            var Window = new MainWindow { DataContext = ViewModel };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            return (Window, ViewModel);
        }

        public static T Named<T>(MainWindow Window, string Name) where T : Control
        {
            return Window.FindControl<T>(Name) ?? throw new AssertionException(Name + " not found");
        }

        public static T Named<T>(CampgroundDetailsView View, string Name) where T : Control
        {
            return View.FindControl<T>(Name) ?? throw new AssertionException(Name + " not found in " + View.Name);
        }

        public static TabControl Tabs(MainWindow Window)
        {
            return Named<TabControl>(Window, "MainTabControl");
        }

        // Selects a tab and returns its content grid, which declares DataContext="{Binding ReactiveViewModel}".
        public static Grid SelectTab(MainWindow Window, int Index)
        {
            Tabs(Window).SelectedIndex = Index;
            Dispatcher.UIThread.RunJobs();
            return Named<Grid>(Window, ContentNames[Index]);
        }

        public static CampgroundDetailsView Details(MainWindow Window, int Index)
        {
            return Named<CampgroundDetailsView>(Window, DetailsNames[Index]);
        }

        public static List<string> Items(ComboBox ComboBox)
        {
            return ComboBox.ItemsSource?.Cast<string>().ToList() ?? new List<string>();
        }

        public static void Select(ComboBox ComboBox, string Value)
        {
            ComboBox.SelectedItem = Value;
            Dispatcher.UIThread.RunJobs();
        }

        public static string? DisplayedId(CampgroundDetailsView View)
        {
            return Named<SelectableTextBlock>(View, "CampgroundIdText").Text;
        }

        public static bool ShowsEmptyState(CampgroundDetailsView View)
        {
            return Named<Border>(View, "EmptyState").IsVisible && !Named<ScrollViewer>(View, "DetailsScrollViewer").IsVisible;
        }

        public static void Resize(MainWindow Window, double Width, double Height)
        {
            Window.Width = Width;
            Window.Height = Height;
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }

        public static void WaitFor(Task Task)
        {
            var Clock = Stopwatch.StartNew();
            while (!Task.IsCompleted && Clock.ElapsedMilliseconds < 5000)
            {
                Dispatcher.UIThread.RunJobs();
            }
            Assert.That(Task.IsCompleted, Is.True, "task did not complete");
        }

        public static IInputElement? Focused(MainWindow Window)
        {
            return Window.FocusManager?.GetFocusedElement();
        }
    }
}
