using Avalonia.Threading;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Campertron
{
    // What the desktop GUI's monitoring uses: the clock, the wait between two checks, the campground data's preparation and
    // the Monitoring tab's timer. Production: the system's clock, the service's own one-minute wait, Load.PrepareDatabase (as
    // every start of Campertron prepares it, never with the automatic refresh) and a timer of the UI thread's dispatcher.
    // Tests replace them.
    internal sealed class DesktopMonitoringSeams
    {
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        // null: the service's own one-minute wait
        internal Action<TimeSpan, CancellationToken>? Wait { get; init; }
        internal Action<string, string?> PrepareCampgroundData { get; init; } = (ConfigPath, CachePath) => Load.PrepareDatabase(ConfigPath, CachePath);
        // a repeating timer on the UI thread (the Monitoring tab reads the monitoring's view while it is shown)
        internal Func<TimeSpan, Action, IDisposable> Ticker { get; init; } = (Interval, Job) => DispatcherTimer.Run(() => { Job(); return true; }, Interval, DispatcherPriority.Background);
    }

    // The desktop GUI's unattended monitoring (v0.4.0's phase 4, D115 and D118; business-rules-gui §41): the library's
    // monitoring controller (MonitoringController, D116; its MonitoringService is the one the console's email and ntfy mode
    // and web mode run), owned by this application and by no tab: MainWindowViewModel holds the one of this process, made
    // when the startup has prepared the settings and cache folders. What is the desktop GUI's own is only here: the folders,
    // the campground data's preparation, the five minutes after a block it shares with the Results tab (the window's
    // BlockHold; the Results tab is told on the UI thread when the monitoring begins one), no console lines (the desktop GUI
    // writes none) and the desktop GUI's words in the shared ones (DesktopMonitoringWording.Host). Every rule of the
    // monitoring's start, actions, last check, pause for the refresh and stop is the controller's: the user's Start on the
    // Monitoring tab starts it (never the application's start, D115), the tab acts on it (MonitoringViewModel), the refresh
    // pauses it (CampgroundDataViewModel), the restart after a refresh hands it on (D62's handoff) and the application's end
    // stops it (Shutdown: App, Program.Main). Nothing here is static.
    internal sealed class DesktopMonitoring
    {
        private readonly object _Lock = new object();
        private Task? _Stopping;

        internal MonitoringController Controller { get; }

        // HoldStarted: called on the monitoring's own thread when a check that recreation.gov blocked begins the shared hold;
        // it must only post to the UI thread
        internal DesktopMonitoring(string ConfigPath, string? CachePath, BlockHold Hold, Action HoldStarted, DesktopMonitoringSeams Seams)
        {
            Controller = new MonitoringController(new MonitoringControllerOptions
            {
                SettingsFolder = ConfigPath,
                CachePath = CachePath,
                PrepareCampgroundData = () => Seams.PrepareCampgroundData(ConfigPath, CachePath),
                CooldownText = Hold.Text,
                HoldAfterBlock = () =>
                {
                    Hold.Begin();
                    HoldStarted();
                },
                Line = null,
                Words = DesktopMonitoringWording.Host,
                Now = Seams.Now,
                Wait = Seams.Wait
            });
        }

        // The application ends: no action is taken any more and the monitoring is stopped as the controller's Shutdown stops
        // it (a check that is searching ends at its next step and sends nothing; one that is sending finishes first), waited
        // for at most the controller's StopWait. On a thread of its own, once: the UI thread never waits for it. The task
        // never faults
        internal Task BeginShutdown()
        {
            lock (_Lock)
            {
                return _Stopping ??= Task.Factory.StartNew(() =>
                {
                    try
                    {
                        Controller.Shutdown();
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        //the controller's own end is its own; the application ends anyway
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        // the stop the application's end began, or null
        internal Task? Stopping
        {
            get
            {
                lock (_Lock)
                {
                    return _Stopping;
                }
            }
        }
    }
}
