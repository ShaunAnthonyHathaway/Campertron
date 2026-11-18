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

        // Settings > System (decision D50): the theme this application started with and the store it is saved in.
        internal SystemSettingsViewModel? SystemSettings { get; private set; }

        public override void OnFrameworkInitializationCompleted()
        {
            // The saved theme applies before the window is created, so the first frame already has it (System when there
            // is none or it can't be used). It is the GUI's own preference: no settings file is read for it.
            PrepareTheme(ThemePreferenceStore.ForCurrentUser());
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

        // Reads the theme preference from the store, applies it to this application (System: Avalonia's Default variant,
        // which follows the operating system) and keeps the System tab's view model. Tests pass a store of their own.
        internal SystemSettingsViewModel PrepareTheme(ThemePreferenceStore Store)
        {
            ThemeLoad Loaded = Store.Load();
            AppTheme.Apply(this, Loaded.Choice);
            SystemSettings = SystemSettingsViewModel.ForApplication(this, Store, Loaded);
            return SystemSettings;
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
        // After the window has closed (V011-1, decision D61): waits for a refresh of the campground data until it has stopped
        // at a checkpoint (the close canceled it) or, past the cache step, finished its installation, check and timestamp;
        // a refresh is never ended in the middle of its installation. No time limit is imposed: from a checkpoint the wait is
        // the current step's own (a ZIP entry, a RIDB file, the import's transaction), and after it the installation's (its
        // busy timeout is 30 seconds). The UI thread is not involved. Returns the result when the window did not show it
        // (the refresh ended after the close), otherwise null. The task never faults.
        internal RidbRefreshResult? WaitForPendingRefresh()
        {
            MainWindowViewModel? Main = MainViewModel;
            if (Main == null)
            {
                return null;
            }
            RidbRefreshResult? Result = Main.PendingRefresh.GetAwaiter().GetResult();
            CampgroundDataViewModel? Card = Main.SystemSettings.CampgroundData;
            return Result == null || (Card != null && Card.WasShown(Result)) ? null : Result;
        }

        // A refresh that ended after the window closed and did not succeed, in one line on standard error (the window can't
        // show it any more): the card's own words, no path or exception message. A cancel by the close is not reported.
        internal static void ReportRefreshAfterClose(RidbRefreshResult? Result, System.IO.TextWriter Error)
        {
            if (Result == null || Result.Succeeded || Result.Outcome == RidbRefreshOutcome.Canceled)
            {
                return;
            }
            Error.WriteLine("Campertron: the campground data refresh did not finish after the window closed: " + CampgroundDataViewModel.Describe(Result).Text);
        }

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
