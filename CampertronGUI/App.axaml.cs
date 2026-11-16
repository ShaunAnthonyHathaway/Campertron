using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Campertron.ViewModels;
using Campertron.Views;
using System;
using System.Threading.Tasks;

namespace Campertron
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // The main window's view model, which owns the startup preparation (R8).
        internal MainWindowViewModel? MainViewModel { get; private set; }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                MainViewModel = new MainWindowViewModel();
                desktop.MainWindow = new MainWindow
                {
                    DataContext = MainViewModel,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

        // After the window has closed: lets a preparation that was still running (Load.GetConfig: extraction, repair or
        // refresh, none of which can be interrupted) finish before the process exits. The task never faults.
        internal void WaitForPendingPreparation()
        {
            MainViewModel?.PendingPreparation.Wait();
        }

        // How long the process waits, after the window has closed, for a search the close canceled (GS2). Cancellation is
        // observed well within a second; the search's thread is a background thread, so the process ends after this
        // wait even if the search has not.
        internal static readonly TimeSpan SearchShutdownWait = TimeSpan.FromSeconds(10);

        // After the window has closed: lets a canceled search stop at its next checkpoint (for example between two
        // campsites of a campground's details, so no cache file is left looking complete) within a bounded time. The UI
        // thread is not involved: the search posts its end to the dispatcher and never waits for it.
        internal bool WaitForPendingSearch(TimeSpan Timeout)
        {
            Task? Search = MainViewModel?.PendingSearch;
            if (Search == null)
            {
                return true;
            }
            try
            {
                return Search.Wait(Timeout);
            }
            catch (AggregateException)
            {
                return true;
            }
        }
    }
}
