using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Campertron.ViewModels;
using Campertron.Views;

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
    }
}
