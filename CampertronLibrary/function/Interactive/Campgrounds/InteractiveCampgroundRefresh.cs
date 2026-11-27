using System.Runtime.InteropServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using W = CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundsWording;

namespace CampertronLibrary.function.Interactive.Campgrounds
{
    // The interactive console's refresh of the campground data (v0.4.0's phase 9, D125; business-rules §43): the library's
    // RidbRefreshService (D61), the one the desktop GUI's Settings > System, the web UI's system page and the start without
    // arguments run, with the shared rules and words (RefreshRules, RefreshWording). No second refresh, download,
    // extraction, import, validation, installation, lock or cleanup: they are the service's, in its fixed order (ER-05).
    //  - It starts only from its confirmation, after a y (No by default), never by itself: entering Campgrounds, the
    //    lookup and the status start nothing, and nothing is retried.
    //  - The settings and the cache folders must exist (the console never creates them); without the campground data the
    //    refresh installs what it downloads, as the service does for every UI (the bundled database first, under the repair
    //    lock, so the installed path never holds a partial file).
    //  - It runs on a thread of its own while the console's thread shows its progress, a line for each stage (the shared
    //    words) and its counts at most once every CountInterval (the same count never twice), and reads the keys as they are
    //    typed: Esc and Ctrl+C (read as a key for the refresh's whole time, never ending the process) cancel it until the
    //    service reports the cache step, the first change a stop could not undo; from there it finishes, and a key says so
    //    (RefreshWording.Finishing). A key pressed again while it cancels says so once. The end of the input or a terminal
    //    that is gone, and a request to stop the process (SIGTERM, SIGHUP), cancel it the same way, and the console ends
    //    once the refresh has stopped or finished. The console never leaves a refresh running: it waits for its end before
    //    anything else is shown or asked.
    //  - A canceled or failed refresh leaves the installed database, the cache and General.yaml as they were and removes
    //    its own files (the service's guarantee). Its result is said in the shared words, which never name a path or an
    //    exception's message; a refresh that installed is followed by the console's sentence about what uses the new data.
    //  - One refresh at a time: the console asks nothing else while it runs, and the service's refresh lock keeps another
    //    process's refresh apart (AnotherRefreshRunning). The console has no monitoring or search of its own yet, so there
    //    is nothing of its own to pause; a monitoring of another program is not paused (TD-54, unchanged).
    //  - After a refresh nothing in this process is reloaded or restarted (D61; the automatic restart is the desktop app's,
    //    D62): the console keeps no campground data between its screens, so its next screen reads the new data.
    internal static class InteractiveCampgroundRefresh
    {
        // the service, the clock and the waits (tests only change them)
        internal static Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>, CancellationToken, RidbRefreshResult> Service =
            (Request, Progress, Token) => RidbRefreshService.Run(Request, Progress, Token);
        internal static TimeProvider Time = TimeProvider.System;
        // how often the console looks at the keys and the progress while the refresh runs
        internal static TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
        // the least time between two lines of counts (a download's megabytes)
        internal static TimeSpan CountInterval = TimeSpan.FromSeconds(5);
        // a request to stop the process while the refresh runs (SIGTERM, SIGHUP): the action runs on the signal's thread
        internal static Func<Action, IDisposable> WhenAskedToStop = RegisterStopSignals;

        internal static InteractiveStep Run(InteractiveSession Session, string Folder)
        {
            InteractivePrompts Prompts = Session.Prompts;
            Prompts.Heading(W.RefreshOrInstall);
            CampertronInternalConfig? Folders = null;
            try
            {
                Folders = Session.Folders();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException
                || ex is System.Security.SecurityException || ex is InvalidOperationException)
            {
                //no folders (Load.DesktopDataFolder's refusal): the shared reason below
            }
            string? Cache = Folders?.CachePath;
            bool PathsAvailable = Path.IsPathFullyQualified(Folder) && Cache != null && Path.IsPathFullyQualified(Cache);
            if (!RefreshRules.CanBegin(false, false, false, PathsAvailable, true))
            {
                Prompts.Say(RefreshRules.UnavailableReason(false, false, PathsAvailable, false) ?? RefreshWording.NoFoldersReason);
                return InteractiveStep.Done;
            }
            if (!Directory.Exists(Cache))
            {
                Prompts.Say(Folders!.ContainerMode ? W.ContainerCacheFolderMissing : W.CacheFolderMissing);
                return InteractiveStep.Done;
            }
            Prompts.Say(RefreshWording.Explanation);
            if (!InteractiveCampgrounds.IsPrepared(Folder))
            {
                Prompts.Say(W.InstallNote);
            }
            Prompts.Heading(RefreshWording.ConfirmationHeading);
            Prompts.Say(RefreshWording.ConfirmationBasics);
            Prompts.Say(W.ConfirmationAfter);
            PromptResult<bool> Start = Prompts.Confirm(W.StartQuestion, false);
            if (Start.End == PromptEnd.EndOfInput || Start.End == PromptEnd.Unavailable)
            {
                return InteractiveStep.EndOfInput;
            }
            if (!Start.IsAnswered || !Start.Value)
            {
                Prompts.Say(W.NotStarted);
                return InteractiveStep.Done;
            }
            return Execute(Prompts, new RidbRefreshRequest(Folder, Cache!, Folders!.ContainerMode));
        }

        // The refresh, its progress and its keys, until it has ended; then its result
        private static InteractiveStep Execute(InteractivePrompts Prompts, RidbRefreshRequest Request)
        {
            var Op = new RefreshOperation(Time.GetTimestamp());
            var Screen = new RefreshScreen(Prompts);
            using (Prompts.Terminal.ReadKeysAsTheyAreTyped())
            using (WhenAskedToStop(Op.AskToStop))
            {
                //a background thread of its own (it can't keep the process alive by itself), which the console waits for
                Op.Work = Task.Factory.StartNew(() => Work(Op, Request), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Screen.Say(W.RunningHow);
                bool SaidStop = false;
                while (!Op.Work.Wait(PollInterval))
                {
                    Screen.Progress(Op);
                    if (Op.StopAsked && !SaidStop)
                    {
                        SaidStop = true;
                        Screen.Say(W.AskedToStop);
                        Cancel(Op, Screen, false);
                    }
                    while (Prompts.Terminal.NextKeyTyped() is TerminalInputKind Key)
                    {
                        if (Key == TerminalInputKind.Escape || Key == TerminalInputKind.Interrupt)
                        {
                            Cancel(Op, Screen, true);
                        }
                        else if (Key == TerminalInputKind.EndOfInput)
                        {
                            Screen.InputEnd();
                            break;
                        }
                    }
                    if ((Screen.InputEnded || Screen.TerminalGone) && !Op.CancelRequested)
                    {
                        Cancel(Op, Screen, false);
                    }
                }
            }
            RidbRefreshResult Result = Op.Work.Result;
            Op.Ended();
            Screen.Result(Result, ResultText.Elapsed(Time.GetElapsedTime(Op.Started)));
            return Screen.InputEnded || Screen.TerminalGone ? InteractiveStep.EndOfInput : Op.StopAsked ? InteractiveStep.Stopped : InteractiveStep.Done;
        }

        // Esc, Ctrl+C, the end of the input or a request to stop: the refresh stops at its next checkpoint until the cache
        // step; from there it finishes. Said once for each (Asked: a key the user pressed)
        private static void Cancel(RefreshOperation Op, RefreshScreen Screen, bool Asked)
        {
            if (!Op.Cancellable)
            {
                if (Asked)
                {
                    Screen.Say(RefreshWording.Finishing(RefreshWording.StageName(Op.Progress?.Stage ?? RidbRefreshStage.ClearingCache), true));
                }
                return;
            }
            if (Op.CancelRequested)
            {
                if (Asked)
                {
                    Screen.SayOnce(W.StillCanceling);
                }
                return;
            }
            Op.Cancel();
            Screen.Say(RefreshWording.CancelingText);
        }

        // the service on its own thread: its result, or for a request it can't start (an unusable cache folder) the result
        // the web UI gives it, named by its type only
        private static RidbRefreshResult Work(RefreshOperation Op, RidbRefreshRequest Request)
        {
            try
            {
                return Service(Request, Op, Op.Token);
            }
            catch (Exception ex)
            {
                return new RidbRefreshResult(ex is InvalidOperationException ? RidbRefreshOutcome.StorageFailed : RidbRefreshOutcome.Unexpected,
                    Op.Progress?.Stage ?? RidbRefreshStage.AcquiringLock, false, null, Time.GetElapsedTime(Op.Started), RidbRefreshResult.NoProblems, null, ex,
                    Op.CancelRequested);
            }
        }

        // SIGTERM and SIGHUP while the refresh runs: the process is not ended at once (the refresh is canceled and waited
        // for, as web mode's stop waits for its refresh); after the refresh the default handling is back. Where a signal
        // can't be registered, its default handling stays (the refresh is safe from a kill, ER-05)
        private static IDisposable RegisterStopSignals(Action Asked)
        {
            var Registrations = new List<IDisposable>();
            foreach (PosixSignal Signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP })
            {
                try
                {
                    Registrations.Add(PosixSignalRegistration.Create(Signal, Context =>
                    {
                        Context.Cancel = true;
                        Asked();
                    }));
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

    // One refresh of the console: its cancellation, the latest progress the service reported (from the service's thread)
    // and whether the process was asked to stop (from a signal's thread); the console's thread reads them
    internal sealed class RefreshOperation : IProgress<RidbRefreshProgress>
    {
        private readonly CancellationTokenSource _Cancel = new CancellationTokenSource();
        private RidbRefreshProgress? _Progress;
        private int _StopAsked;
        private int _CancelRequested;

        internal RefreshOperation(long Started)
        {
            this.Started = Started;
        }

        internal long Started { get; }
        internal Task<RidbRefreshResult> Work { get; set; } = null!;
        internal CancellationToken Token => _Cancel.Token;
        internal RidbRefreshProgress? Progress => Volatile.Read(ref _Progress);
        // asked for by the console's thread; read there and by the refresh's thread when the service refused the request
        internal bool CancelRequested => Volatile.Read(ref _CancelRequested) != 0;
        internal bool StopAsked => Volatile.Read(ref _StopAsked) != 0;
        // the refresh can still stop: until the service reports the cache step (RidbRefreshProgress.CanCancel)
        internal bool Cancellable => RefreshRules.CanCancel(true, false, Progress?.CanCancel ?? true);

        // the service's progress, from its thread: the latest snapshot (by its sequence), never a list of them
        public void Report(RidbRefreshProgress Value)
        {
            RidbRefreshProgress? Seen = Volatile.Read(ref _Progress);
            while (Seen == null || Value.Sequence > Seen.Sequence)
            {
                RidbRefreshProgress? Was = Interlocked.CompareExchange(ref _Progress, Value, Seen);
                if (ReferenceEquals(Was, Seen))
                {
                    return;
                }
                Seen = Was;
            }
        }

        internal void AskToStop() => Interlocked.Exchange(ref _StopAsked, 1);

        // the console's thread only, while the refresh runs
        internal void Cancel()
        {
            Volatile.Write(ref _CancelRequested, 1);
            _Cancel.Cancel();
        }

        // the refresh has ended (its thread is done): the token is no longer needed
        internal void Ended() => _Cancel.Dispose();
    }

    // What the console shows while a refresh runs: a line for each stage the console sees (the shared words; after Cancel
    // only the finishing stages, as the desktop GUI announces them; none for Completed, which its result says), its counts
    // at most once every CountInterval and never the same twice (none after Cancel), and its result. A terminal that is gone (a write's IOException) is remembered, and nothing more is
    // written to it
    internal sealed class RefreshScreen
    {
        private readonly InteractivePrompts Prompts;
        private RidbRefreshStage? Stage;
        private RidbRefreshStage? CountStage;
        private string? Count;
        private long CountAt;
        private readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal);

        internal RefreshScreen(InteractivePrompts Prompts)
        {
            this.Prompts = Prompts;
        }

        internal bool TerminalGone { get; private set; }
        internal bool InputEnded { get; private set; }

        internal void Say(string Text)
        {
            if (TerminalGone)
            {
                return;
            }
            try
            {
                Prompts.Say(Text);
            }
            catch (IOException)
            {
                TerminalGone = true;
            }
        }

        internal void SayOnce(string Text)
        {
            if (Said.Add(Text))
            {
                Say(Text);
            }
        }

        // the end of the input while the refresh runs (Ctrl+D, a terminal that is gone): said once; the refresh is canceled
        // and the console ends after it
        internal void InputEnd()
        {
            InputEnded = true;
            SayOnce(W.InputEndedDuringRefresh);
        }

        internal void Progress(RefreshOperation Op)
        {
            if (Op.Progress is not RidbRefreshProgress Snapshot)
            {
                return;
            }
            //the last step's own snapshot is followed at once by the result, which says how it ended
            if (Snapshot.Stage != Stage && Snapshot.Stage != RidbRefreshStage.Completed)
            {
                Stage = Snapshot.Stage;
                string StageText = RefreshWording.StageName(Snapshot.Stage);
                if (!Snapshot.CanCancel)
                {
                    Say(RefreshWording.Finishing(StageText, Op.CancelRequested));
                }
                else if (!Op.CancelRequested)
                {
                    Say(RefreshWording.Refreshing(StageText));
                }
            }
            string? Counted = RefreshWording.Count(Snapshot);
            long Now = InteractiveCampgroundRefresh.Time.GetTimestamp();
            if (Counted != null && Counted != Count && !Op.CancelRequested
                && (CountStage != Snapshot.Stage || InteractiveCampgroundRefresh.Time.GetElapsedTime(CountAt, Now) >= InteractiveCampgroundRefresh.CountInterval))
            {
                Count = Counted;
                CountStage = Snapshot.Stage;
                CountAt = Now;
                Say("  " + RefreshWording.ProgressDetails(Counted, ResultText.Elapsed(InteractiveCampgroundRefresh.Time.GetElapsedTime(Op.Started, Now))));
            }
        }

        // the refresh's result in the shared words, with its severity first, and after a refresh that installed what uses
        // the new data
        internal void Result(RidbRefreshResult Result, string Elapsed)
        {
            (RefreshResultKind Kind, string Text) = RefreshWording.Describe(Result);
            string Severity = Kind == RefreshResultKind.Failure ? MessageSeverity.Error : Kind == RefreshResultKind.Warning ? MessageSeverity.Warning : MessageSeverity.Info;
            Say(MessageSeverity.AccessibleText(RefreshWording.Finished(Text, Elapsed), Severity));
            if (Result.Succeeded)
            {
                Say(W.AfterSuccess);
            }
        }
    }
}
