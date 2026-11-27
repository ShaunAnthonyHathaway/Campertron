using System.Runtime.InteropServices;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using W = CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringWording;

namespace CampertronLibrary.function.Interactive.Monitoring
{
    // What the console's monitoring uses (tests only change them): the controller's clock and the wait between two checks
    // (null: the service's own one-minute wait), the clock of the five minutes after a block, whether the campground data
    // is prepared (the database file exists; never opened) and its preparation as every start prepares it when it exists,
    // how often the menu looks at the keys and the monitoring while it waits for an action, how long it waits for one, how
    // long the console's end waits for the monitoring's stop, and the signals that end the process
    internal sealed class InteractiveMonitoringSeams
    {
        internal Func<DateTime> Now { get; init; } = () => DateTime.Now;
        internal Action<TimeSpan, CancellationToken>? Wait { get; init; }
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal Func<string, bool> Prepared { get; init; } = Folder => Load.DbExists(new CampertronInternalConfig { ConfigPath = Folder });
        internal Action<string, string?> PrepareCampgroundData { get; init; } = (ConfigPath, CachePath) => Load.PrepareDatabase(ConfigPath, CachePath);
        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(100);
        // the menu waits at most the controller's own stop bound for an action it asked for, then says it goes on
        internal TimeSpan ActionWait { get; init; } = MonitoringController.StopWait;
        // the controller's own stop bound and a margin, as the desktop GUI's end waits (App.MonitoringShutdownWait)
        internal TimeSpan EndWait { get; init; } = MonitoringController.StopWait + TimeSpan.FromSeconds(5);
        // a signal that ends the process (SIGTERM, SIGHUP, SIGINT): the action runs on the signal's thread before the signal's
        // own handling, which is never canceled here
        internal Func<Action, IDisposable> WhenEnding { get; init; } = InteractiveMonitoringHost.RegisterEndSignals;
    }

    // The interactive console's unattended monitoring (v0.4.0's phase 11, D127; business-rules §43): the library's
    // monitoring controller (MonitoringController, D116; its MonitoringService is the one the console's email and ntfy mode,
    // web mode and the desktop GUI run), owned by this console's session (InteractiveSession.Monitoring) and by no menu: made
    // when Monitoring is first opened, the one of this console until it ends. What is the console's own is only here: the
    // settings and cache folders it found, the campground data's readiness (the console never prepares a missing campground
    // data: a start without it is refused in the shared words, D124 and D126's rule; when it exists it is prepared as every
    // start prepares it, never with the automatic refresh), the five minutes after a block it shares with its Results
    // (InteractiveSession.Hold), no console lines from the monitoring's threads (the menu shows its state when asked), the
    // console's words in the shared ones (InteractiveMonitoringWording.Host) and its end.
    // Every rule of the monitoring's start, actions, last check, pause for the refresh and stop is the controller's: the
    // user's Start in Monitoring starts it (never the console's start; nothing is kept that a later start resumes), the menu
    // acts on it (InteractiveMonitoring), the console's refresh pauses it (InteractiveCampgroundRefresh) and the console's
    // every end stops it (End): Exit after its question, Ctrl+C at the main menu, the end of the input, a terminal that is
    // gone, an unexpected problem, and a signal that ends the process (SIGTERM, SIGHUP, a Ctrl+C outside a question), whose
    // own handling follows once the monitoring has stopped. Nothing here is static.
    internal sealed class InteractiveMonitoringHost : IDisposable
    {
        private readonly InteractiveMonitoringSeams _Seams;
        private readonly Action<string> _Say;
        private IDisposable? _Signals;
        private Task? _Stopping;

        internal MonitoringController Controller { get; }
        internal string SettingsFolder { get; }

        // Say: a line on the console's terminal from a signal's thread (it never throws: a terminal that is gone says nothing)
        internal InteractiveMonitoringHost(string ConfigPath, string? CachePath, BlockHold Hold, InteractiveMonitoringSeams Seams, Action<string> Say)
        {
            _Seams = Seams;
            _Say = Say;
            SettingsFolder = ConfigPath;
            Controller = new MonitoringController(new MonitoringControllerOptions
            {
                SettingsFolder = ConfigPath,
                CachePath = CachePath,
                PrepareCampgroundData = () =>
                {
                    //the console never prepares a missing campground data (the controller refuses the start in the shared
                    //words); one that exists is prepared as every start prepares it
                    if (!Seams.Prepared(ConfigPath))
                    {
                        throw new InvalidDataException("The campground data is not prepared.");
                    }
                    Seams.PrepareCampgroundData(ConfigPath, CachePath);
                },
                CooldownText = Hold.Text,
                HoldAfterBlock = Hold.Begin,
                Line = null,
                Words = W.Host,
                Now = Seams.Now,
                Wait = Seams.Wait
            });
            _Signals = Seams.WhenEnding(OnEnding);
        }

        // the stop the console's end began, or null
        internal Task? Stopping => Volatile.Read(ref _Stopping);

        // The console ends: no action is taken any more, and the monitoring is stopped as the controller's Shutdown stops it
        // (a check that is searching ends at its next step and sends nothing; one that is sending finishes and records
        // first), waited for at most EndWait. Once: a second call waits for the same stop and says nothing. Said (Say, null
        // when the terminal is gone) only when there was a monitoring to stop. Never throws for the monitoring's own end
        internal void End(Action<string>? Say)
        {
            if (Stop(out bool First) && First)
            {
                Say?.Invoke(W.StoppingAtEnd);
                Say?.Invoke(Wait());
            }
            else
            {
                Wait();
            }
            Dispose();
        }

        // a signal that ends the process: the monitoring is stopped first (the same stop), then the signal's own handling
        private void OnEnding()
        {
            try
            {
                if (Stop(out bool First) && First)
                {
                    //on a line of its own: the signal can come while a question waits for its answer
                    _Say(string.Empty);
                    _Say(W.StoppingForSignal);
                    _Say(Wait());
                }
                else
                {
                    Wait();
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                //the process ends anyway
            }
        }

        // begins the controller's stop once (on a thread of its own; the first caller's, whatever thread it is on: the
        // console's or a signal's); true when there was a monitoring to stop, or when another caller began the stop
        private bool Stop(out bool First)
        {
            bool On = Controller.View().Phase != MonitoringControlPhase.Idle;
            var Mine = new Task(() =>
            {
                try
                {
                    Controller.Shutdown();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    //the controller's own end is its own; the console ends anyway
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning);
            First = Interlocked.CompareExchange(ref _Stopping, Mine, null) == null;
            if (!First)
            {
                return true;
            }
            Mine.Start(TaskScheduler.Default);
            return On;
        }

        // the stop's end, at most EndWait, in words: stopped, or not within the controller's bound
        private string Wait()
        {
            Task? Stop = Stopping;
            Stop?.Wait(_Seams.EndWait);
            MonitoringControlView After = Controller.View();
            return After.Phase == MonitoringControlPhase.Idle ? W.StoppedAtEnd : W.NotStoppedInTime(MonitoringController.StopWait);
        }

        // the signals' registration ends with the console
        public void Dispose() => Interlocked.Exchange(ref _Signals, null)?.Dispose();

        // SIGTERM, SIGHUP and SIGINT (a Ctrl+C outside a question, which ends the process): the action runs first, then the
        // signal's own handling (never canceled here, so the process ends as it did before). Where a signal can't be
        // registered, its own handling stays
        internal static IDisposable RegisterEndSignals(Action Ending)
        {
            var Registrations = new List<IDisposable>();
            foreach (PosixSignal Signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGINT })
            {
                try
                {
                    Registrations.Add(PosixSignalRegistration.Create(Signal, Context => Ending()));
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException || ex is InvalidOperationException || ex is ArgumentException)
                {
                    //this platform can't: its default handling stays
                }
            }
            return new Registered(Registrations);
        }

        private sealed class Registered : IDisposable
        {
            private readonly List<IDisposable> Items;
            internal Registered(List<IDisposable> Items) => this.Items = Items;
            public void Dispose()
            {
                foreach (IDisposable Item in Items)
                {
                    Item.Dispose();
                }
            }
        }
    }
}
