using System.Reflection;
using System.Text;

namespace CampertronLibrary.function.Base
{
    // The console's presenter: every message the console shows around the results goes through here, so that
    //  - results, status, notices and prompts go to standard output; warnings and errors go to standard error, each
    //    with a text prefix ("Warning:", "Error:"), so their meaning never depends on the stream or on color;
    //  - color is used only on a terminal (never when standard output is redirected, when NO_COLOR is set or on a dumb
    //    terminal) and is always reset after the text it belongs to;
    //  - the screen is cleared and the cursor hidden only on an interactive terminal, the scrollback is never erased,
    //    and the cursor and color are restored when the program ends or is interrupted;
    //  - a whole line is written at once (one lock), also when parallel searches report at the same time.
    // Text is line-oriented: no cursor movement, no animation. The interactive menu (Menu) is the only cursor user. The
    // interactive console (--interactive, v0.4.0's phase 6) writes its lines here too; its questions (Prompt) and the
    // answers typed after them (Echo) are the only text without a line break.
    public static class ConsoleUi
    {
        // Exception.Data key under which the settings loaders record the file an exception belongs to
        public const string FileKey = "CampertronFile";
        public const int DefaultWidth = 80;
        // cursor home + erase the visible screen. Not ESC c (a full terminal reset) and not ESC[3J (erase the scrollback)
        public const string ClearSequence = "\u001b[H\u001b[2J";

        public static readonly object Sync = new object();

        public sealed class Capabilities
        {
            public bool OutputIsTerminal { get; init; }
            public bool InputIsTerminal { get; init; }
            public bool ErrorIsTerminal { get; init; }
            // the terminal understands control sequences (not a dumb terminal)
            public bool ControlSequences { get; init; }
            public bool Color { get; init; }
            // columns used to wrap long lists and messages; int.MaxValue = never wrap (redirected output)
            public int Width { get; init; } = int.MaxValue;
            // Width is the terminal's own (it reported one from 20 to 500 columns), not the default (since D130: the
            // interactive console's logo is drawn whole only where the width is known)
            public bool WidthReported { get; init; }
            // a person at a terminal: the menu, key presses and screen clearing need both streams
            public bool Interactive => OutputIsTerminal && InputIsTerminal;
        }

        // The decision, from plain values, so that it can be tested. WindowWidth may throw or return nonsense.
        public static Capabilities Detect(bool OutputRedirected, bool InputRedirected, bool ErrorRedirected, string? NoColor, string? Term, bool IsWindows, Func<int> WindowWidth)
        {
            bool OutputIsTerminal = !OutputRedirected;
            bool Dumb = string.Equals(Term, "dumb", StringComparison.OrdinalIgnoreCase);
            // Windows consoles have no TERM; elsewhere a terminal without TERM has no known capabilities
            bool ControlSequences = OutputIsTerminal && !Dumb && (IsWindows || !string.IsNullOrWhiteSpace(Term));
            int Width = int.MaxValue;
            bool WidthReported = false;
            if (OutputIsTerminal)
            {
                Width = DefaultWidth;
                try
                {
                    int Reported = WindowWidth();
                    if (Reported >= 20 && Reported <= 500)
                    {
                        Width = Reported;
                        WidthReported = true;
                    }
                }
                catch (Exception)
                {
                    //no usable width: keep the default
                }
            }
            return new Capabilities
            {
                OutputIsTerminal = OutputIsTerminal,
                InputIsTerminal = !InputRedirected,
                ErrorIsTerminal = !ErrorRedirected,
                ControlSequences = ControlSequences,
                // NO_COLOR (no-color.org): any non-empty value turns color off
                Color = ControlSequences && string.IsNullOrEmpty(NoColor),
                Width = Width,
                WidthReported = WidthReported
            };
        }

        private static Capabilities? _Current;
        // detected once, on first use; settable so tests can describe a terminal
        public static Capabilities Current
        {
            get
            {
                return _Current ??= DetectCurrent();
            }
            set
            {
                _Current = value;
            }
        }
        private static Capabilities DetectCurrent()
        {
            try
            {
                return Detect(Console.IsOutputRedirected, Console.IsInputRedirected, Console.IsErrorRedirected,
                    Environment.GetEnvironmentVariable("NO_COLOR"), Environment.GetEnvironmentVariable("TERM"),
                    OperatingSystem.IsWindows(), () => Console.WindowWidth);
            }
            catch (Exception)
            {
                //capabilities unknown: plain text, nothing interactive
                return new Capabilities();
            }
        }

        // sets (a color) or resets (null) the terminal's foreground color; replaceable so tests can record it
        public static Action<ConsoleColor?> ApplyColor { get; set; } = ApplyConsoleColor;
        public static void ApplyConsoleColor(ConsoleColor? Color)
        {
            try
            {
                if (Color.HasValue)
                {
                    Console.ForegroundColor = Color.Value;
                }
                else
                {
                    Console.ResetColor();
                }
            }
            catch (IOException)
            {
                //a console that cannot change color: the text is still written
            }
        }
        // the clock of the timestamped lines; replaceable for tests
        public static Func<DateTime> Now { get; set; } = () => DateTime.Now;

        // ---- styled lines ----

        public sealed class Span
        {
            public string Text { get; init; } = string.Empty;
            public ConsoleColor? Color { get; init; }
        }
        public sealed class StyledLine
        {
            public List<Span> Spans { get; } = new List<Span>();
            public StyledLine() { }
            public StyledLine(string Text, ConsoleColor? Color = null)
            {
                Add(Text, Color);
            }
            public StyledLine Add(string Text, ConsoleColor? Color = null)
            {
                Spans.Add(new Span { Text = Text, Color = Color });
                return this;
            }
            public string Text => string.Concat(Spans.Select(p => p.Text));
        }

        private static void WriteStyled(TextWriter Writer, StyledLine Line, bool UseColor)
        {
            foreach (Span ThisSpan in Line.Spans)
            {
                if (UseColor && ThisSpan.Color.HasValue && ThisSpan.Text.Length > 0)
                {
                    ApplyColor(ThisSpan.Color);
                    try
                    {
                        Writer.Write(ThisSpan.Text);
                    }
                    finally
                    {
                        ApplyColor(null);
                    }
                }
                else
                {
                    Writer.Write(ThisSpan.Text);
                }
            }
            Writer.WriteLine();
        }

        // ---- standard output: results, status, notices, prompts ----

        public static void Write(IEnumerable<StyledLine> Lines)
        {
            lock (Sync)
            {
                bool UseColor = Current.Color;
                foreach (StyledLine Line in Lines)
                {
                    WriteStyled(Console.Out, Line, UseColor);
                }
            }
        }
        public static void Line(string Text = "")
        {
            Write(new[] { new StyledLine(Text) });
        }
        public static void Line(string Text, ConsoleColor Color)
        {
            Write(new[] { new StyledLine(Text, Color) });
        }
        // the interactive console's question (v0.4.0's phase 6): its text without a line break, the answer is typed after it
        public static void Prompt(string Text)
        {
            Echo(Text);
        }
        // what the interactive console's line editor writes back for a key as it is typed (KeyLineReader): the character, a
        // backspace-space-backspace for a removed one, the line break of Enter. Plain text, never an escape sequence
        public static void Echo(string Text)
        {
            lock (Sync)
            {
                Console.Out.Write(Text);
                Console.Out.Flush();
            }
        }
        // a status line; on a terminal a long one is wrapped at a space (continuation lines indented), never inside a word
        public static void Status(string Text)
        {
            Write(Wrap(Text, Current.Width, "  ").Select(p => new StyledLine(p)));
        }
        // a line of the unattended (Email) loop, with the local time, for logs
        public static void LoopLine(string Text)
        {
            Line(Now().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + "  " + Text);
        }
        // something the user should have seen before the screen is cleared (a database repair, a refresh result)
        public static void Notice(string Text)
        {
            Unacknowledged = true;
            Status(Text);
        }

        // ---- standard error: warnings and errors ----

        public static void Warning(string Text)
        {
            Unacknowledged = true;
            WriteDiagnostic("Warning: ", ConsoleColor.DarkYellow, Text, Array.Empty<string>());
        }
        public static void Error(string Text, params string[] Details)
        {
            Unacknowledged = true;
            WriteDiagnostic("Error: ", ConsoleColor.Red, Text, Details);
        }
        private static void WriteDiagnostic(string Prefix, ConsoleColor Color, string Text, IEnumerable<string> Details)
        {
            lock (Sync)
            {
                Capabilities Caps = Current;
                // the color is a property of the terminal on standard output; use it only when both are that terminal
                bool UseColor = Caps.Color && Caps.ErrorIsTerminal;
                int Width = Caps.ErrorIsTerminal ? Caps.Width : int.MaxValue;
                bool First = true;
                foreach (string Wrapped in Wrap(Prefix + Text, Width, "  "))
                {
                    StyledLine Line = new StyledLine();
                    if (First)
                    {
                        Line.Add(Prefix, Color).Add(Wrapped.Substring(Prefix.Length));
                        First = false;
                    }
                    else
                    {
                        Line.Add(Wrapped);
                    }
                    WriteStyled(Console.Error, Line, UseColor);
                }
                foreach (string Detail in Details)
                {
                    foreach (string DetailLine in Detail.Replace("\r\n", "\n").Split('\n'))
                    {
                        Console.Error.WriteLine("  " + DetailLine);
                    }
                }
            }
        }

        // what a Ctrl+C that ends the process writes on standard error (InstallTerminalHandlers' handler); the interactive
        // console writes the same word when Ctrl+C at its main menu ends it (v0.4.0's phase 6)
        public const string StoppedText = "Stopped.";
        public static void Stopped()
        {
            lock (Sync)
            {
                try
                {
                    Console.Error.WriteLine(StoppedText);
                }
                catch (IOException)
                {
                    //nothing to report to
                }
            }
        }

        // ---- screen and keys (interactive terminals only) ----

        // true while a notice, warning or error has been written that no key press has acknowledged yet
        public static bool Unacknowledged { get; set; }

        // Clears the visible screen, only for a person at a terminal, and never over a message that was not
        // acknowledged. Redirected output, logs and dumb terminals never get a control sequence. Returns whether it cleared.
        public static bool ClearScreen()
        {
            Capabilities Caps = Current;
            if (Unacknowledged)
            {
                return false;
            }
            return ClearVisibleScreen(Caps);
        }
        // The one way the visible screen is cleared (the scrollback is kept): only for a person at a terminal that understands
        // control sequences, so redirected output, a log and a dumb terminal (or one without TERM) never get one. Returns
        // whether it cleared. The no-argument menu's ClearScreen adds its rule about unread messages; the interactive console
        // (since D130) asks for its continuation first instead
        public static bool ClearVisibleScreen(Capabilities Caps)
        {
            if (!Caps.Interactive || !Caps.ControlSequences)
            {
                return false;
            }
            lock (Sync)
            {
                Console.Out.Write(ClearSequence);
                Console.Out.Flush();
            }
            return true;
        }
        // waits for a key (the prompt is written first); a no-op without an interactive terminal, so nothing blocks
        public static void WaitForKey(string Prompt)
        {
            if (!Current.Interactive)
            {
                Unacknowledged = false;
                return;
            }
            Line(Prompt);
            Console.ReadKey(true);
            Unacknowledged = false;
        }
        // lets the user read pending notices, warnings and errors before the screen is cleared
        public static void AcknowledgePending()
        {
            if (Unacknowledged)
            {
                WaitForKey("Press any key to continue...");
            }
        }

        private static bool HandlersInstalled;
        private static bool CursorHidden;
        // restores the cursor and the color when the program ends, also on Ctrl+C; installed on first use only, so a
        // start that shows nothing (--version, outputTo: UnitTest) touches no terminal state at all
        public static void InstallTerminalHandlers()
        {
            lock (Sync)
            {
                if (HandlersInstalled)
                {
                    return;
                }
                HandlersInstalled = true;
            }
            try
            {
                Console.CancelKeyPress += (Sender, Args) =>
                {
                    RestoreTerminal();
                    try
                    {
                        Console.Error.WriteLine("Stopped.");
                    }
                    catch (IOException)
                    {
                        //nothing to report to
                    }
                    //Args.Cancel stays false: the program ends as before, with the interrupt's exit code
                };
                AppDomain.CurrentDomain.ProcessExit += (Sender, Args) => RestoreTerminal();
            }
            catch (Exception)
            {
                //no console to attach to
            }
        }
        public static void HideCursor()
        {
            if (!Current.Interactive)
            {
                return;
            }
            InstallTerminalHandlers();
            try
            {
                Console.CursorVisible = false;
                CursorHidden = true;
            }
            catch (Exception)
            {
                //a console without a cursor
            }
        }
        public static void RestoreTerminal()
        {
            try
            {
                if (CursorHidden)
                {
                    CursorHidden = false;
                    Console.CursorVisible = true;
                }
                if (Current.Color)
                {
                    ApplyColor(null);
                }
            }
            catch (Exception)
            {
                //the terminal is gone
            }
        }
        // campground and search names can contain any character; the menu already did this for interactive starts
        public static void UseUtf8Output()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch (Exception)
            {
                //keep the console's encoding
            }
        }

        // ---- text helpers ----

        // Breaks Text at spaces so that no line is longer than Width; continuation lines start with Indent. A word
        // longer than a line (a URL, a path) is never broken. Width int.MaxValue returns the text unchanged.
        public static List<string> Wrap(string Text, int Width, string Indent)
        {
            List<string> Lines = new List<string>();
            if (Width == int.MaxValue || Text.Length <= Width)
            {
                Lines.Add(Text);
                return Lines;
            }
            //the text's own leading spaces stay on its first line
            int Lead = Text.Length - Text.TrimStart(' ').Length;
            string CurrentLine = Text.Substring(0, Lead);
            bool HasWord = false;
            foreach (string Word in Text.Substring(Lead).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (HasWord && CurrentLine.Length + 1 + Word.Length > Width)
                {
                    Lines.Add(CurrentLine);
                    CurrentLine = Indent + Word;
                }
                else
                {
                    CurrentLine += (HasWord ? " " : string.Empty) + Word;
                }
                HasWord = true;
            }
            Lines.Add(CurrentLine);
            return Lines;
        }
        public static string Count(int Value, string Singular, string Plural)
        {
            return Value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " " + (Value == 1 ? Singular : Plural);
        }
        // the most specific message of a failure (its innermost exception), on one line
        public static string Reason(Exception Error)
        {
            Exception Innermost = Error;
            while (Innermost.InnerException != null)
            {
                Innermost = Innermost.InnerException;
            }
            string Text = OneLine(Innermost.Message);
            return Text.Length > 0 ? Text : Innermost.GetType().Name;
        }
        private static string OneLine(string? Text)
        {
            return string.Join(" ", (Text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        // ---- identity and failures ----

        // "CampertronConsole <version>" and the runtime, from the same source as --version (RuntimeInfo)
        public static string ProgramIdentity(Assembly? App = null)
        {
            string[] Lines = RuntimeInfo.Describe(App ?? Assembly.GetEntryAssembly() ?? typeof(ConsoleUi).Assembly).Split(Environment.NewLine);
            string Framework = Lines.Length > 1 ? Lines[1].Replace("Framework => ", string.Empty) : string.Empty;
            return Framework.Length > 0 ? Lines[0] + " | " + Framework : Lines[0];
        }

        // Records the settings file an exception belongs to and returns false, so it can be used as an exception filter
        // ("catch (Exception ex) when (ConsoleUi.NoteFile(ex, path))"): the exception passes through unchanged.
        public static bool NoteFile(Exception Error, string FilePath)
        {
            try
            {
                if (!Error.Data.Contains(FileKey))
                {
                    Error.Data[FileKey] = FilePath;
                }
            }
            catch (Exception)
            {
                //a read-only Data dictionary: the failure is then reported without the file
            }
            return false;
        }

        // What to tell the user about a failure that ends the program: the first line is the problem (written after
        // "Error: "), the following lines the file and what to do. Messages come from the exceptions only; no settings
        // value is read. For Email.yaml the inner messages are left out, because they can quote a value of that file, and
        // Secret (the SMTP password, if it is known) is removed from everything.
        public static List<string> DescribeFailure(Exception Error, string? Secret = null)
        {
            return DescribeFailure(Error, string.IsNullOrEmpty(Secret) ? Array.Empty<string>() : new[] { Secret }, null);
        }
        // The same for email mode's settings (D66): every password the process knows for them (the one its start resolved
        // from smtpPasswordFile, CAMPERTRON_EMAIL_PASSWORD or smtpPassword, and smtpPassword itself) is removed, and a failed
        // send says where its password came from. Reads nothing: no file and no environment variable
        internal static List<string> DescribeFailure(Exception Error, EmailConfig? Email)
        {
            return DescribeFailure(Error, SmtpPassword.KnownSecrets(Email), Email);
        }
        private static List<string> DescribeFailure(Exception Error, IReadOnlyList<string> Secrets, EmailConfig? Email)
        {
            List<Exception> Chain = new List<Exception>();
            for (Exception? Next = Error; Next != null; Next = Next.InnerException)
            {
                Chain.Add(Next);
            }
            Exception Shown = Error;
            while ((Shown is AggregateException || Shown is TargetInvocationException) && Shown.InnerException != null)
            {
                Shown = Shown.InnerException;
            }
            Exception Innermost = Chain[Chain.Count - 1];
            string? FilePath = Chain.Select(p => p.Data.Contains(FileKey) ? p.Data[FileKey] as string : null).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            YamlDotNet.Core.YamlException? Yaml = Chain.OfType<YamlDotNet.Core.YamlException>().FirstOrDefault();

            List<string> Lines = new List<string>();
            if (FilePath != null)
            {
                string Name = Path.GetFileName(FilePath);
                //Email.yaml and Ntfy.yaml (D67) are told without inner messages: they can quote a value (a password, a topic)
                bool IsEmail = ConfigFiles.Classify(Name) == ConfigFileKind.Email || ConfigFiles.Classify(Name) == ConfigFileKind.Ntfy;
                bool IsNtfy = ConfigFiles.Classify(Name) == ConfigFileKind.Ntfy;
                bool IsGeneral = ConfigFiles.Classify(Name) == ConfigFileKind.General;
                string Reason = OneLine(Yaml != null ? Yaml.Message : Shown.Message);
                if (!IsEmail && !ReferenceEquals(Innermost, Yaml ?? Shown))
                {
                    string Inner = OneLine(Innermost.Message);
                    if (Inner.Length > 0 && !Reason.Contains(Inner))
                    {
                        Reason = Reason.TrimEnd('.') + ": " + Inner;
                    }
                }
                if (Reason.Length == 0)
                {
                    Reason = Shown.GetType().Name;
                }
                //YamlDotNet names a type with its assembly ("OutputType, CampertronLibrary, Version=..."): the type is enough
                Reason = System.Text.RegularExpressions.Regex.Replace(Reason, @", [\w.]+, Version=[\d.]+, Culture=\w+, PublicKeyToken=\w+", string.Empty);
                if (Yaml != null && Yaml.Start.Line > 0)
                {
                    Reason = Reason.TrimEnd('.') + " (line " + Yaml.Start.Line + ", column " + Yaml.Start.Column + ")";
                }
                Lines.Add(Name + " could not be " + (Yaml != null ? "read" : "used") + ": " + Reason);
                Lines.Add("File: " + FilePath);
                bool Missing = Shown is ConfigValidationException Refused && Refused.Diagnostics.Any(D => D.Code == ConfigCode.FileMissing);
                //D71: deleting Email.yaml or Ntfy.yaml would lose its settings and secret-file paths (a new Email.yaml is empty),
                //so only General.yaml, which holds no secret, is offered to be created again with the default settings
                Lines.Add(IsNtfy && Missing ? "Create it in the settings folder with enabled: true, the server and the topic (see the README), or choose another outputTo."
                    : IsNtfy ? "Fix the file in a text editor or in the GUI (Settings > Campertron > Ntfy.yaml). To turn ntfy off, set enabled: false (with outputTo: Ntfy, choose another outputTo too); don't delete the file, which holds the server, the topic and tokenFile."
                    : IsEmail ? "Fix the file in a text editor or in the GUI (Settings > Campertron > Email.yaml). Don't delete it to start over: Campertron would create an empty one, without your email settings or smtpPasswordFile."
                    : IsGeneral ? "Fix the file, or delete it and Campertron creates a new one with the default settings (outputTo: Console)."
                    //Web.yaml (D92): without it the web UI uses its defaults, which are the safe ones for its password
                    : ConfigFiles.Classify(Name) == ConfigFileKind.Web ? "Fix the file in a text editor, or move it out of the settings folder: the web UI then uses its defaults (a password required, reachable from the network)."
                    : "Fix the file, or move it out of the settings folder.");
            }
            else if (Shown.GetType() == typeof(Exception) && (Shown.Message == "Config path does not exist" || Shown.Message == "Cache path does not exist"))
            {
                Lines.Add(Shown.Message);
                Lines.Add("In a container, mount the settings folder at /config and the cache folder at /cache.");
            }
            else if (Shown is NotificationException Both)
            {
                //both providers failed in one check (D67): each one's own words, one after the other
                Lines.Add("the notifications could not be sent: both the email and the ntfy notification failed.");
                foreach ((NotificationProvider Provider, Exception Failure) in Both.Failures)
                {
                    List<string> Own = DescribeFailure(Failure, Array.Empty<string>(), Email);
                    Lines.Add((Provider == NotificationProvider.Email ? "Email: " : "ntfy: ") + Own[0]);
                    Lines.AddRange(Own.Skip(1).Where(p => !p.StartsWith("Details, for a problem report", StringComparison.Ordinal)).Take(2));
                }
            }
            else if (Chain.OfType<NtfyException>().FirstOrDefault() is NtfyException NtfyProblem)
            {
                //a failed publish (D67): its kind and what to check, never the server, the topic, the token or an answer
                Lines.AddRange(NtfyPublisher.Describe(NtfyProblem.Failure, NtfyProblem.Status, NtfyProblem.TimeoutSeconds, NtfyProblem.ExceptionType));
            }
            else if (Chain.OfType<NtfyTokenException>().FirstOrDefault() is NtfyTokenException TokenProblem)
            {
                //a publish whose configured token source can't be used (D67): the same words as the start's
                Lines.AddRange(NtfyToken.Describe(TokenProblem.Result));
            }
            else if (Chain.OfType<SmtpPasswordException>().FirstOrDefault() is SmtpPasswordException PasswordProblem)
            {
                //a send whose configured password source can't be used (D66): the same words as email mode's start
                Lines.AddRange(SmtpPassword.Describe(PasswordProblem.Result));
            }
            else if (Chain.Any(p => p is System.Net.Mail.SmtpException))
            {
                string Reason = OneLine(Chain.First(p => p is System.Net.Mail.SmtpException).Message);
                string Inner = OneLine(Innermost.Message);
                Lines.Add("the email could not be sent: " + Reason + (Inner.Length > 0 && !Reason.Contains(Inner) ? " (" + Inner + ")" : string.Empty));
                Lines.Add(SmtpPassword.SendFailureHint(Email));
            }
            else if (Chain.Any(p => p is HttpRequestException))
            {
                //the campground-data download (searches retry by themselves and never end here)
                Lines.Add("a download failed: " + Reason(Error));
                Lines.Add("Check the internet connection and try again.");
            }
            else
            {
                Lines.Add("Campertron stopped because of an unexpected problem: " + Shown.GetType().Name + ": " + OneLine(Shown.Message));
                Lines.Add("Details, for a problem report:");
                Lines.AddRange(Error.ToString().Replace("\r\n", "\n").Split('\n'));
            }
            //the longest first, so a password that contains another is removed whole
            foreach (string Secret in Secrets.Where(p => !string.IsNullOrEmpty(p)).OrderByDescending(p => p.Length))
            {
                Lines = Lines.Select(p => p.Replace(Secret, "[hidden]")).ToList();
            }
            return Lines;
        }
        // reports the failure on standard error and returns the exit code of a handled failure (1)
        public static int ReportFailure(Exception Failure, string? Secret = null)
        {
            RestoreTerminal();
            List<string> Lines = DescribeFailure(Failure, Secret);
            Error(Lines[0], Lines.Skip(1).ToArray());
            return 1;
        }
        // the same for a failure of the console's run with its email settings: every password the process knows for them is
        // removed from the report (D66)
        public static int ReportFailure(Exception Failure, EmailConfig? Email)
        {
            RestoreTerminal();
            List<string> Lines = DescribeFailure(Failure, Email);
            Error(Lines[0], Lines.Skip(1).ToArray());
            return 1;
        }
        // the same for a failure of the console's run with all its settings (CampertronConsole's Program.Main since D67): the
        // SMTP passwords the process knows, and ntfy's token and topic (the topic acts as a password where the server has no
        // access control), are removed from the report
        public static int ReportFailure(Exception Failure, CampertronInternalConfig? Config)
        {
            RestoreTerminal();
            List<string> Lines = DescribeFailure(Failure, Config);
            Error(Lines[0], Lines.Skip(1).ToArray());
            return 1;
        }
        internal static List<string> DescribeFailure(Exception Error, CampertronInternalConfig? Config)
        {
            return DescribeFailure(Error, SmtpPassword.KnownSecrets(Config?.EmailConfig).Concat(NtfyToken.KnownSecrets(Config?.NtfyConfig)).ToList(), Config?.EmailConfig);
        }
    }
}
