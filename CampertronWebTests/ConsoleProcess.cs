using System.Diagnostics;
using System.Text;

namespace CampertronWebTests
{
    // CampertronConsole as a process for the console's start tests (ConsoleStartProcessTests): the program this project
    // builds (dotnet CampertronConsole.dll), with a data folder, a home folder and a working directory of its own in the
    // temporary folder, so it never sees the user's settings. Either with every stream redirected (a script, a pipe, a
    // container without a terminal) or on a pseudo-terminal that util-linux's script(1) gives it, whose keys the test types
    // after the output it waits for. Nothing here reaches the network.
    internal sealed class ConsoleProcess : IDisposable
    {
        internal string Root { get; } = WebTestHost.NewFolder();
        internal string Data => Path.Join(Root, "data");
        internal string Home => Path.Join(Root, "home");
        internal string Work => Path.Join(Root, "work");
        internal string Settings => Path.Join(Data, "CampertronConfig");
        internal string Cache => Path.Join(Data, "CampertronCache");
        internal Process Process { get; private set; } = null!;
        private readonly StringBuilder Output = new();
        private readonly StringBuilder Errors = new();
        private Task? Reading;
        private Task? ReadingErrors;

        internal ConsoleProcess()
        {
            Directory.CreateDirectory(Data);
            Directory.CreateDirectory(Home);
            Directory.CreateDirectory(Work);
        }

        internal string Out
        {
            get
            {
                lock (Output)
                {
                    return Output.ToString();
                }
            }
        }

        internal string Err
        {
            get
            {
                lock (Errors)
                {
                    return Errors.ToString();
                }
            }
        }

        internal static string Dll => Path.Join(AppContext.BaseDirectory, "CampertronConsole.dll");

        // whether this computer can give the console a pseudo-terminal (util-linux's script, on Linux)
        internal static bool CanUseTerminal => OperatingSystem.IsLinux() && File.Exists("/usr/bin/script");

        private ProcessStartInfo Info(string FileName, IEnumerable<string> Arguments, params (string Name, string? Value)[] Environment_)
        {
            var Start = new ProcessStartInfo(FileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                WorkingDirectory = Work
            };
            foreach (string Argument in Arguments)
            {
                Start.ArgumentList.Add(Argument);
            }
            Start.Environment["XDG_DATA_HOME"] = Data;
            Start.Environment["HOME"] = Home;
            Start.Environment.Remove("DOTNET_RUNNING_IN_CONTAINER");
            Start.Environment.Remove("DOTNET_RUNNING_IN_CONTAINERS");
            Start.Environment.Remove("NO_COLOR");
            foreach ((string Name, string? Value) in Environment_)
            {
                if (Value == null)
                {
                    Start.Environment.Remove(Name);
                }
                else
                {
                    Start.Environment[Name] = Value;
                }
            }
            return Start;
        }

        // the console with standard input, output and error redirected (standard input stays empty until Type writes to it)
        internal ConsoleProcess Start(string[] Arguments, params (string Name, string? Value)[] Environment_)
        {
            return Begin(Info("dotnet", new[] { Dll }.Concat(Arguments), Environment_));
        }

        // the console on a pseudo-terminal: script(1) runs it with the terminal as its standard input, output and error,
        // echoes nothing of what the test types itself (--echo never) and ends with the console's exit code (-e). Its output
        // is the terminal's, standard error included; TERM is dumb unless the test names another, so the terminal knows
        // no control sequence
        internal ConsoleProcess StartOnTerminal(string[] Arguments, params (string Name, string? Value)[] Environment_)
        {
            string Command = "exec dotnet " + Quote(Dll) + string.Concat(Arguments.Select(A => " " + Quote(A)));
            var Environment = new List<(string, string?)> { ("SHELL", "/bin/sh"), ("TERM", "dumb") };
            Environment.AddRange(Environment_);
            return Begin(Info("/usr/bin/script", new[] { "-q", "-e", "-E", "never", "-c", Command, "/dev/null" }, Environment.ToArray()));
        }

        private static string Quote(string Text) => "'" + Text.Replace("'", "'\\''") + "'";

        private ConsoleProcess Begin(ProcessStartInfo Start)
        {
            Process = new Process { StartInfo = Start };
            Process.Start();
            Reading = Copy(Process.StandardOutput, Output);
            ReadingErrors = Copy(Process.StandardError, Errors);
            return this;
        }

        // reads a stream as it comes, not line by line: a question has no line break after it
        private static Task Copy(StreamReader From, StringBuilder To)
        {
            return Task.Run(() =>
            {
                char[] Buffer = new char[4096];
                int Count;
                while ((Count = From.Read(Buffer, 0, Buffer.Length)) > 0)
                {
                    lock (To)
                    {
                        To.Append(Buffer, 0, Count);
                    }
                }
            });
        }

        // waits until the output holds Text Times times (or more); false when the process ended first or the time is over
        internal bool WaitFor(string Text, int Times = 1, int Seconds = 30)
        {
            Stopwatch Watch = Stopwatch.StartNew();
            while (Watch.Elapsed < TimeSpan.FromSeconds(Seconds))
            {
                if (Occurrences(Out, Text) >= Times)
                {
                    return true;
                }
                if (Process.HasExited)
                {
                    Process.WaitForExit();
                    return Occurrences(Out, Text) >= Times;
                }
                Thread.Sleep(25);
            }
            return false;
        }

        internal static int Occurrences(string Text, string Part)
        {
            int Count = 0;
            for (int At = Text.IndexOf(Part, StringComparison.Ordinal); At >= 0; At = Text.IndexOf(Part, At + Part.Length, StringComparison.Ordinal))
            {
                Count++;
            }
            return Count;
        }

        // types Keys (on the terminal: as its keys; else onto standard input)
        internal void Type(string Keys)
        {
            Process.StandardInput.Write(Keys);
            Process.StandardInput.Flush();
        }

        // the exit code once the process ended (fails after Seconds)
        internal int Exit(int Seconds = 30)
        {
            if (!Process.WaitForExit(Seconds * 1000))
            {
                Process.Kill(true);
                Process.WaitForExit();
                Assert.Fail("the console did not end within " + Seconds + " seconds; its output: " + Out + " | " + Err);
            }
            Process.WaitForExit();
            Task.WaitAll(new[] { Reading!, ReadingErrors! }, 10000);
            return Process.ExitCode;
        }

        // every file and folder under Folder, as paths relative to it, with each file's length and last write time
        internal static List<string> Snapshot(string Folder)
        {
            if (!Directory.Exists(Folder))
            {
                return new List<string>();
            }
            return Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories)
                .Select(P => Path.GetRelativePath(Folder, P) + (File.Exists(P) ? " " + new FileInfo(P).Length + " " + File.GetLastWriteTimeUtc(P).Ticks : "/"))
                .OrderBy(P => P, StringComparer.Ordinal).ToList();
        }

        public void Dispose()
        {
            if (Process != null)
            {
                if (!Process.HasExited)
                {
                    Process.Kill(true);
                    Process.WaitForExit();
                }
                Process.Dispose();
            }
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
                //a file still open: the temporary folder is cleaned later
            }
        }
    }
}
