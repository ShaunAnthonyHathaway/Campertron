using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The monitoring's clock and the wait between two checks (tests only change them)
    internal sealed class WebMonitoringSeams
    {
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        // null: the service's own one-minute wait
        internal Action<TimeSpan, CancellationToken>? Wait { get; init; }
        // called while a page is being written, after it took its view of the monitoring (tests hold a page there)
        internal Action? Rendering { get; init; }
    }

    // The web UI's unattended monitoring (v0.3.0's PR 8, D83 and D98; business-rules-settings §35): the library's monitoring
    // controller (MonitoringController, v0.4.0's phase 2; its MonitoringService is the one the console's email and ntfy mode
    // run), owned by this web UI and by no browser session. One object per web UI; no static state. What is the web UI's
    // own is only here: the controller's settings and cache folders, its campground data's preparation (WebCampgroundData),
    // the five minutes after a block it shares with the results page (WebResultsRuns), its console lines after
    // "Web UI: monitoring: ", the web UI's words in the shared ones (WebMonitoringWording.Host) and this start of the web
    // UI's random name for the live pages (Instance). Every rule of the monitoring's start, actions, last check, pause for
    // the refresh and stop is the controller's: web mode starts it with the host (WebMode), the monitoring page acts on it
    // (Pages/Monitoring), the refresh pauses it (WebRefresh) and the host's stop stops it (CampertronWebHost).
    internal sealed class WebMonitoring
    {
        internal const string LinePrefix = "Web UI: monitoring: ";

        internal MonitoringController Controller { get; }

        // this start of the web UI's random name, the key a monitoring page names for its script (D109): a page from before a
        // restart is told so
        internal string Instance { get; } = WebLive.NewInstance();

        internal WebMonitoring(WebHostSettings Settings, WebCampgroundData Data, WebResultsRuns Runs)
        {
            Controller = new MonitoringController(new MonitoringControllerOptions
            {
                SettingsFolder = Settings.SettingsFolder,
                CachePath = Settings.CachePath,
                PrepareCampgroundData = Data.Ensure,
                CooldownText = Runs.CooldownText,
                HoldAfterBlock = Runs.HoldAfterBlock,
                Line = Text => Settings.Line(LinePrefix + Text),
                Words = WebMonitoringWording.Host,
                Now = Settings.Monitoring.Now,
                Wait = Settings.Monitoring.Wait
            });
        }
    }
}
