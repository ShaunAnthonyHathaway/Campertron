using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Campertron
{
    // What starts the new process: the running process's own executable (Environment.ProcessPath) with an explicit argument
    // list. "apphost": CampertronGUI itself (CampertronGUI.exe on Windows), as every release archive (self-contained) and a
    // local build or `dotnet run` (framework-dependent) start it; no argument. "dotnet": the shared host running the entry
    // assembly (`dotnet CampertronGUI.dll`); its one argument is that assembly's path. Nothing else (no shell, no PATH
    // lookup, no working directory) decides what runs.
    internal sealed record RestartTarget(string Executable, IReadOnlyList<string> Arguments, string Kind);

    internal static class RestartTargets
    {
        internal static RestartTarget? Resolve(string? ProcessPath, string? EntryAssemblyPath, Func<string, bool> FileExists)
        {
            if (string.IsNullOrEmpty(ProcessPath) || !Path.IsPathFullyQualified(ProcessPath) || !FileExists(ProcessPath))
            {
                return null;
            }
            if (string.Equals(Path.GetFileNameWithoutExtension(ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(EntryAssemblyPath) || !Path.IsPathFullyQualified(EntryAssemblyPath)
                    || !string.Equals(Path.GetExtension(EntryAssemblyPath), ".dll", StringComparison.OrdinalIgnoreCase) || !FileExists(EntryAssemblyPath))
                {
                    return null;
                }
                return new RestartTarget(ProcessPath, new[] { EntryAssemblyPath }, "dotnet");
            }
            return new RestartTarget(ProcessPath, Array.Empty<string>(), "apphost");
        }

        // this process's own target (read only: nothing is started)
        internal static RestartTarget? Current() =>
            Resolve(Environment.ProcessPath, Assembly.GetEntryAssembly()?.Location, File.Exists);
    }

    // the started process, as far as the restart needs it
    internal interface IRestartChild : IDisposable
    {
        bool HasExited { get; }
        void Kill();
    }

    // how an attempt to restart ended (tests and diagnostics: the detail is an exception's type name, never a message,
    // path or token)
    internal enum RestartAttemptStatus { Acknowledged, Unavailable, HandoffFailed, LaunchFailed, ChildExited, NoAcknowledgment, Abandoned }

    internal sealed record RestartAttempt(RestartAttemptStatus Status, string? Detail = null)
    {
        internal bool Succeeded => Status == RestartAttemptStatus.Acknowledged;
    }

    internal sealed class RestartLaunchOptions
    {
        internal RestartHandoffStore Store { get; init; } = RestartHandoffStore.ForCurrentUser();
        internal Func<RestartTarget?> Target { get; init; } = RestartTargets.Current;
        internal Func<ProcessStartInfo, IRestartChild?> Start { get; init; } = AutomaticRestart.StartProcess;
        internal TimeProvider Time { get; init; } = TimeProvider.System;
        internal Action<TimeSpan> Sleep { get; init; } = System.Threading.Thread.Sleep;
        internal TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(50);
        internal TimeSpan AcknowledgeTimeout { get; init; } = AutomaticRestart.AcknowledgeTimeout;
    }

    // The old process's side of the automatic restart (decision D62, business-rules-gui.md §26), on a worker thread after
    // a successful refresh: write the handoff, start the new process with the token, and wait, at most AcknowledgeTimeout,
    // for it to claim the handoff, which it does first thing in Program.Main. Claimed: the caller ends this process.
    // Anything else (no target, no handoff, the start throws or returns nothing, the new process exits before claiming,
    // no claim in time) invalidates the handoff, ends a new process that never claimed it, and leaves this process running;
    // there is no second attempt. A claimed handoff shows that the new process started and took it, not that its whole
    // startup will succeed.
    internal static class AutomaticRestart
    {
        internal static readonly TimeSpan AcknowledgeTimeout = TimeSpan.FromSeconds(20);

        // Set by the GUI tests' fixture: a real process start then throws instead (no test may start a process).
        internal static volatile bool StartForbidden;

        internal static RestartAttempt Run(RestartHandoff Handoff, RestartLaunchOptions Options, Func<bool> StillWanted)
        {
            RestartTarget? Target = Options.Target();
            if (Target == null || Options.Store.Folder == null)
            {
                return new RestartAttempt(RestartAttemptStatus.Unavailable);
            }
            Options.Store.RemoveStale(Options.Time.GetUtcNow().UtcDateTime);
            string Token = RestartToken.Create();
            string HandoffPath;
            try
            {
                HandoffPath = Options.Store.Create(Token, Handoff);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                return new RestartAttempt(RestartAttemptStatus.HandoffFailed, ex.GetType().Name);
            }
            // the window was closed meanwhile: an ordinary close, no restart
            if (!StillWanted())
            {
                Options.Store.TryInvalidate(HandoffPath);
                return new RestartAttempt(RestartAttemptStatus.Abandoned);
            }
            var Start = new ProcessStartInfo(Target.Executable) { UseShellExecute = false };
            foreach (string Argument in Target.Arguments)
            {
                Start.ArgumentList.Add(Argument);
            }
            Start.Environment[RestartToken.EnvironmentVariable] = Token;
            IRestartChild? Child;
            try
            {
                Child = Options.Start(Start);
            }
            catch (Exception ex)
            {
                Options.Store.TryInvalidate(HandoffPath);
                return new RestartAttempt(RestartAttemptStatus.LaunchFailed, ex.GetType().Name);
            }
            if (Child == null)
            {
                Options.Store.TryInvalidate(HandoffPath);
                return new RestartAttempt(RestartAttemptStatus.LaunchFailed);
            }
            using (Child)
            {
                long Started = Options.Time.GetTimestamp();
                while (Options.Time.GetElapsedTime(Started) < Options.AcknowledgeTimeout)
                {
                    if (!Options.Store.IsPending(HandoffPath))
                    {
                        return new RestartAttempt(RestartAttemptStatus.Acknowledged);
                    }
                    if (Child.HasExited)
                    {
                        // it may have claimed the handoff just before it ended; otherwise it ended without taking it
                        return Options.Store.TryInvalidate(HandoffPath)
                            ? new RestartAttempt(RestartAttemptStatus.ChildExited)
                            : new RestartAttempt(RestartAttemptStatus.Acknowledged);
                    }
                    Options.Sleep(Options.PollInterval);
                }
                if (!Options.Store.TryInvalidate(HandoffPath))
                {
                    // claimed at the last moment
                    return new RestartAttempt(RestartAttemptStatus.Acknowledged);
                }
                // it never claimed the handoff (so it has done nothing yet): end it rather than leave a second window later
                try
                {
                    Child.Kill();
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception || ex is NotSupportedException)
                {
                    // it ended meanwhile, or can't be ended: nothing more to do
                }
                return new RestartAttempt(RestartAttemptStatus.NoAcknowledgment);
            }
        }

        // The production start: a typed process start of an explicit executable and argument list, without a shell.
        internal static IRestartChild? StartProcess(ProcessStartInfo Start)
        {
            if (StartForbidden)
            {
                throw new InvalidOperationException("Starting a process is forbidden in the GUI tests.");
            }
            Process? Started = Process.Start(Start);
            return Started == null ? null : new ProcessChild(Started);
        }

        private sealed class ProcessChild : IRestartChild
        {
            private readonly Process _Process;

            internal ProcessChild(Process Process)
            {
                _Process = Process;
            }

            public bool HasExited
            {
                get
                {
                    try
                    {
                        return _Process.HasExited;
                    }
                    catch (InvalidOperationException)
                    {
                        return true;
                    }
                }
            }

            public void Kill() => _Process.Kill(entireProcessTree: false);

            public void Dispose() => _Process.Dispose();
        }
    }
}
