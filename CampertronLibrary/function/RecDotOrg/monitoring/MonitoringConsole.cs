using System.Runtime.ExceptionServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronLibrary.function.RecDotOrg.monitoring
{
    // The console's email and ntfy mode (outputTo: Email or Ntfy): an adapter of the host-neutral MonitoringService
    // (v0.3.0, decisions D83 and D90), as RefreshRidbRecreationData is the refresh service's. It writes the service's
    // events through ConsoleUi exactly as the loop of Load.RunConsoleSearch wrote them before the service (the same calls
    // in the same order, so the same bytes), keeps the console's answer to a blocked request (the error, the five-minute
    // wait, exit code 1) and lets a failure end the program as before: the service's exception is rethrown unchanged, so
    // Program.Main still reports it with exit code 1. The console starts one monitoring and never stops or restarts it:
    // its process ends (a signal, a failure) instead.
    internal static class MonitoringConsole
    {
        // False when the monitoring did not start its checks and one error on standard error has said why (the console
        // then exits with code 1): a secret that can't be used, no search to check (D64), or another monitoring of the
        // same settings folder (the monitoring lock). Throws what the start or a check threw. Otherwise it returns only
        // when the monitoring was stopped, which nothing in the console does. CheckWait replaces the wait between two
        // checks (tests)
        internal static bool Run(CampertronInternalConfig Config, Action<TimeSpan, CancellationToken>? CheckWait)
        {
            var Service = new MonitoringService(new MonitoringOptions
            {
                Report = Event => Write(Config, Event),
                Now = () => ConsoleUi.Now(),
                Wait = CheckWait,
                Blocked = AvailabilityApi.ConsoleBlocked
            });
            MonitoringStartResult Started = Service.Start(Config);
            if (!Started.Started)
            {
                if (Started.Error != null)
                {
                    ExceptionDispatchInfo.Throw(Started.Error);
                }
                ConsoleUi.Error(Started.Lines[0], Started.Lines.Skip(1).ToArray());
                return false;
            }
            MonitoringStatus Ended = Service.WaitForEnd();
            if (Ended.LastError != null)
            {
                ExceptionDispatchInfo.Throw(Ended.LastError.Error);
            }
            return false;
        }

        // one event of the service, as the console always wrote it
        internal static void Write(CampertronInternalConfig Config, MonitoringEvent Event)
        {
            switch (Event.Kind)
            {
                case MonitoringEventKind.Starting:
                    ConsoleUi.UseUtf8Output();
                    ConsoleUi.InstallTerminalHandlers();
                    ConsoleUi.LoopLine(ConsoleUi.ProgramIdentity() + " | settings " + Config?.ConfigPath + " | cache " + Config?.CachePath + " | output " + Notifications.OutputText(Config));
                    break;
                case MonitoringEventKind.Status:
                    ConsoleUi.Status(Event.Text ?? string.Empty);
                    break;
                case MonitoringEventKind.Warning:
                    ConsoleUi.Warning(Event.Text ?? string.Empty);
                    break;
                case MonitoringEventKind.SearchesLoaded:
                    //what is about to be checked, and what cannot find anything as it is written
                    Load.WriteScope(Config!, Event.Searches ?? Array.Empty<CampertronConfig>());
                    break;
                case MonitoringEventKind.CheckReported:
                    //one timestamped line per check (and one per notification, written when it is sent)
                    ConsoleUi.LoopLine(Load.CheckText(Event.CheckNumber, Event.SearchCount, Event.NewOpenings, ConsoleUi.Now(), Event.Emails, Event.NtfyMessages));
                    break;
                case MonitoringEventKind.EmailSent:
                    ConsoleUi.LoopLine(CampsiteConfig.EmailSentText(Event.SearchName ?? string.Empty, Event.Openings));
                    break;
                case MonitoringEventKind.NtfySent:
                    ConsoleUi.LoopLine(CampsiteConfig.NtfySentText(Event.SearchName ?? string.Empty, Event.Openings));
                    break;
                case MonitoringEventKind.DeliveriesFinished:
                    ConsoleUi.LoopLine(Load.NextCheckText(ConsoleUi.Now()));
                    break;
            }
        }

        // one check's report on the console, numbered as given: the service's report with the console's lines. The
        // console's monitoring reaches it through its service; Load.ReportResults keeps it reachable for one check alone
        internal static void ReportCheck(CampertronInternalConfig Config, RunningData RunData, int Number)
        {
            MonitoringService.ReportCheck(Config, RunData, Number, Event => Write(Config, Event), () => ConsoleUi.Now());
        }
    }
}
