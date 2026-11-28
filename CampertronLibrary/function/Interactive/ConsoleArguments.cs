using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Interactive
{
    // which start CampertronConsole's own arguments ask for
    public enum ConsoleCommand
    {
        // no argument, or only arguments the console does not read: the start as it always was (General.yaml's outputTo
        // decides: the menu for Console or HtmlFile, the monitoring for Email or Ntfy)
        Start,
        // --interactive (D115): the interactive console
        Interactive,
        // --help: how to start CampertronConsole
        Help,
        // a command line with --interactive or --help that can't be used: refused, with nothing read or created
        Refused
    }

    // CampertronConsole's own arguments (v0.4.0's phase 6; D115, D121; business-rules §11):
    //
    //   CampertronConsole --interactive    the interactive console (InteractiveConsole)
    //   CampertronConsole --help           how to start CampertronConsole
    //
    // Program.Main reads --version (exactly one argument, RuntimeInfo), --transfer-selftest (exactly one argument, the release
    // smoke tests' run of the transfer package's self-test, TransferSelfTestCommand) and web mode's arguments (WebArguments: a command line
    // with --web, --healthcheck, --listen or --port is web mode's, its refusals its own) first, both unchanged; only a
    // command line that is neither comes here. Each of the two arguments is a whole command line: given with another
    // argument, twice or with a value ("--interactive=yes"), it is refused with fixed words and the usage line, before
    // anything is read or created. Every other argument is still ignored, as it always was, and the console starts as
    // without arguments; a terminal never chooses the interactive console (D115: scripts and containers never enter it
    // unexpectedly).
    public sealed class ConsoleArguments
    {
        public const string InteractiveArgument = "--interactive";
        public const string HelpArgument = "--help";
        public const string Usage = "Usage: CampertronConsole [--interactive | --web | --healthcheck | --version | --help]   (--help says what each one does; without an argument it starts as before)";

        public ConsoleCommand Command { get; private set; } = ConsoleCommand.Start;
        // the problem of a refused command line, in fixed words (an argument's value is never repeated); null otherwise
        public string? Error { get; private set; }

        public static ConsoleArguments Parse(string[]? Args)
        {
            var Result = new ConsoleArguments();
            List<string> Given = (Args ?? Array.Empty<string>()).Select(A => A ?? string.Empty).ToList();
            bool Interactive = Given.Any(A => Is(A, InteractiveArgument));
            bool Help = Given.Any(A => Is(A, HelpArgument));
            if (!Interactive && !Help)
            {
                return Result;
            }
            if (Given.Count == 1 && Given[0] == InteractiveArgument)
            {
                Result.Command = ConsoleCommand.Interactive;
            }
            else if (Given.Count == 1 && Given[0] == HelpArgument)
            {
                Result.Command = ConsoleCommand.Help;
            }
            else
            {
                Result.Command = ConsoleCommand.Refused;
                Result.Error = Interactive
                    ? "--interactive opens the interactive console and takes no value and no other argument."
                    : "--help shows how to start CampertronConsole and takes no value and no other argument.";
            }
            return Result;
        }

        // the argument itself, or the argument with a value after an equals sign
        private static bool Is(string Arg, string Name) => Arg == Name || Arg.StartsWith(Name + "=", StringComparison.Ordinal);

        // --help's text: each start and what it does. The lines are written as they are; a description (indented by six
        // spaces) is wrapped at the terminal's width with its own indentation
        public static readonly IReadOnlyList<string> HelpLines = Array.AsReadOnly(new[]
        {
            "Usage:",
            "  CampertronConsole",
            "      Starts as it always has: General.yaml's outputTo decides. Console or HtmlFile opens the menu; Email or Ntfy starts the monitoring, which notifies you of new openings.",
            "  CampertronConsole --interactive",
            "      Opens the interactive console in this terminal: numbered choices and questions. It needs a terminal, and it starts nothing by itself.",
            "  CampertronConsole --web [--listen <address>] [--port <port>]",
            "      Starts the web UI, on port 8790 unless --port names another.",
            "  CampertronConsole --healthcheck [--listen <address>] [--port <port>]",
            "      Asks a running web UI whether it answers.",
            "  CampertronConsole --version",
            "      Shows the version and the .NET runtime.",
            "  CampertronConsole --help",
            "      Shows this help."
        });

        // writes the help on standard output (ConsoleUi, ER-13), wrapped at the terminal's width (never when redirected);
        // like --version it reads, creates and starts nothing
        public static void WriteHelp()
        {
            int Width = ConsoleUi.Current.Width;
            foreach (string Line in HelpLines)
            {
                string Indent = new string(' ', Line.Length - Line.TrimStart(' ').Length);
                foreach (string Wrapped in ConsoleUi.Wrap(Line, Width, Indent))
                {
                    ConsoleUi.Line(Wrapped);
                }
            }
        }
    }
}
