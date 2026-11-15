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

        // Same construction as App.OnFrameworkInitializationCompleted for the classic desktop lifetime. Since R8 the
        // window prepares the configuration and database after it opens; this waits until that has succeeded.
        public static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow()
        {
            var (Window, ViewModel) = ShowMainWindow(new MainWindowViewModel());
            WaitForState(ViewModel, StartupState.Ready);
            return (Window, ViewModel);
        }

        // Shows the window with the given view model (for example one with a test loader) without waiting.
        public static (MainWindow Window, MainWindowViewModel ViewModel) ShowMainWindow(MainWindowViewModel ViewModel)
        {
            var Window = new MainWindow { DataContext = ViewModel };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            return (Window, ViewModel);
        }

        // Runs the UI thread's jobs until the startup reaches State (background work completes on its own thread).
        public static void WaitForState(MainWindowViewModel ViewModel, StartupState State)
        {
            WaitUntil(() => ViewModel.State == State, () => $"startup state {ViewModel.State} ({ViewModel.ErrorMessage}), expected {State}");
        }

        public static void WaitUntil(Func<bool> Condition, Func<string> Message)
        {
            var Clock = Stopwatch.StartNew();
            while (!Condition() && Clock.ElapsedMilliseconds < 10000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.That(Condition(), Is.True, Message());
        }

        public static T Named<T>(MainWindow Window, string Name) where T : Control
        {
            return Window.FindControl<T>(Name) ?? throw new AssertionException(Name + " not found");
        }

        public static T Named<T>(CampgroundDetailsView View, string Name) where T : Control
        {
            return View.FindControl<T>(Name) ?? throw new AssertionException(Name + " not found in " + View.Name);
        }

        public static T Named<T>(StartupStatusView View, string Name) where T : Control
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

        // A campground selector lists CampgroundChoice entries (decision D42); a list of names would fail the cast.
        public static List<CampgroundChoice> Choices(ComboBox ComboBox)
        {
            return ComboBox.ItemsSource?.Cast<CampgroundChoice>().ToList() ?? new List<CampgroundChoice>();
        }

        public static List<string> Ids(ComboBox ComboBox)
        {
            return Choices(ComboBox).Select(c => c.FacilityID).ToList();
        }

        // Selects the listed entry with this FacilityID, as a user picks it from the list (it must be listed).
        public static CampgroundChoice SelectCampground(ComboBox ComboBox, string FacilityID)
        {
            CampgroundChoice Choice = Choices(ComboBox).SingleOrDefault(c => c.FacilityID == FacilityID)
                ?? throw new AssertionException(FacilityID + " is not listed in " + ComboBox.Name);
            ComboBox.SelectedItem = Choice;
            Dispatcher.UIThread.RunJobs();
            return Choice;
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

    // Counts EF Core commands (EF Core's DiagnosticListener events): those executed on this thread, or with AllThreads
    // on any thread. It also keeps each command's SQL text and parameter values (TakeCommands).
    internal sealed class QueryCounter : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly int _Thread = Environment.CurrentManagedThreadId;
        private readonly bool _AllThreads;
        private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
        private int _Count;
        private readonly List<(string Sql, Dictionary<string, object?> Parameters)> _Commands = new List<(string, Dictionary<string, object?>)>();

        public QueryCounter(bool AllThreads = false)
        {
            _AllThreads = AllThreads;
            _Subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
        }

        public int TakeCount()
        {
            return Interlocked.Exchange(ref _Count, 0);
        }

        public List<(string Sql, Dictionary<string, object?> Parameters)> TakeCommands()
        {
            lock (_Commands)
            {
                var Taken = _Commands.ToList();
                _Commands.Clear();
                return Taken;
            }
        }

        public void OnNext(DiagnosticListener Listener)
        {
            if (Listener.Name == "Microsoft.EntityFrameworkCore")
            {
                lock (_Subscriptions)
                {
                    _Subscriptions.Add(Listener.Subscribe(this));
                }
            }
        }

        public void OnNext(KeyValuePair<string, object?> Event)
        {
            if (Event.Key == "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted"
                && (_AllThreads || Environment.CurrentManagedThreadId == _Thread))
            {
                Interlocked.Increment(ref _Count);
                if (Event.Value is Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData Data)
                {
                    lock (_Commands)
                    {
                        _Commands.Add((Data.Command.CommandText, Data.Command.Parameters.Cast<System.Data.Common.DbParameter>()
                            .ToDictionary(p => p.ParameterName, p => (object?)p.Value)));
                    }
                }
            }
        }

        public void OnCompleted() { }
        public void OnError(Exception error) { }

        public void Dispose()
        {
            lock (_Subscriptions)
            {
                foreach (IDisposable Subscription in _Subscriptions)
                {
                    Subscription.Dispose();
                }
            }
        }
    }
}
