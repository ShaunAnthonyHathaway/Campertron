using System.Text;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Interactive
{
    // how a read from the interactive console's terminal ended
    public enum TerminalInputKind
    {
        // a line, ended by Enter
        Line,
        // Esc: the question is cancelled
        Escape,
        // Ctrl+C, read as a key while the console waits for an answer
        Interrupt,
        // the end of the input: Ctrl+D (on Windows also Ctrl+Z) on an empty line, or a terminal that is gone
        EndOfInput,
        // nothing was read: the terminal can't do what was asked (a secret, on a terminal that can't hide what is typed)
        Unavailable
    }

    // one read from the terminal. The text is never in ToString: it can be a secret, and a test's failure message or a log
    // line must never show it
    public sealed class TerminalInput
    {
        public TerminalInputKind Kind { get; }
        // the line typed (Kind Line only, else empty)
        public string Text { get; }

        private TerminalInput(TerminalInputKind Kind, string Text)
        {
            this.Kind = Kind;
            this.Text = Text;
        }

        public static TerminalInput Line(string Text) => new TerminalInput(TerminalInputKind.Line, Text ?? string.Empty);
        public static readonly TerminalInput Escape = new TerminalInput(TerminalInputKind.Escape, string.Empty);
        public static readonly TerminalInput Interrupt = new TerminalInput(TerminalInputKind.Interrupt, string.Empty);
        public static readonly TerminalInput EndOfInput = new TerminalInput(TerminalInputKind.EndOfInput, string.Empty);
        public static readonly TerminalInput Unavailable = new TerminalInput(TerminalInputKind.Unavailable, string.Empty);

        public override string ToString() => Kind == TerminalInputKind.Line ? "Line (" + Text.Length + " characters)" : Kind.ToString();
    }

    // The interactive console's terminal (v0.4.0's phase 6, D121; business-rules §11): where its lines go and where its
    // answers come from. ConsoleTerminal is the real one; the tests give their own, with scripted answers and the lines
    // recorded by kind, so they judge words and outcomes, never a screen. Plain lines only: no cursor movement, no
    // full-screen drawing and nothing that depends on the terminal's size but the width the text is wrapped at.
    public abstract class InteractiveTerminal
    {
        // the columns a line is wrapped at; int.MaxValue: never wrapped
        public abstract int Width { get; }
        // whether what is typed can be kept from being shown (a secret)
        public abstract bool CanHideInput { get; }
        // a plain line on standard output
        public abstract void WriteLine(string Text);
        // a heading: a plain line, emphasized only where the terminal can show it
        public abstract void WriteHeading(string Text);
        // a warning or an error, on standard error with its word ("Warning:", "Error:") before it
        public abstract void WriteWarning(string Text);
        public abstract void WriteError(string Text);
        // "Stopped." on standard error, as a Ctrl+C that ends any start of the console writes it (no "Error:" before it)
        public abstract void WriteStopped();
        // the text of a question, without a line break: the answer is typed after it
        public abstract void WritePrompt(string Text);
        // the answer to a question, shown as it is typed
        public abstract TerminalInput ReadLine();
        // a secret, never shown as it is typed; Unavailable, with nothing read, where CanHideInput is false
        public abstract TerminalInput ReadSecret();
    }

    // The terminal the console runs in. Its lines go through ConsoleUi (ER-13), so color and wrapping follow the console's
    // one policy (color only on a terminal that has it, never with NO_COLOR or TERM=dumb). Its answers are read key by key
    // (KeyLineReader), so Esc can cancel a question and a secret is never echoed; while an answer is read Ctrl+C is read as
    // a key (Console.TreatControlCAsInput, restored after the answer), so the first Ctrl+C cancels the question instead of
    // ending the process. Only for a person at a terminal: InteractiveConsole refuses to start without one.
    internal sealed class ConsoleTerminal : InteractiveTerminal
    {
        private readonly ConsoleUi.Capabilities Caps;
        private readonly Func<ConsoleKeyInfo?> NextKey;
        private readonly Func<bool> ControlCAsInput;
        private readonly Action<bool> SetControlCAsInput;

        // NextKey: the next key, or null at the end of the input. ControlCAsInput and SetControlCAsInput: whether Ctrl+C is
        // read as a key. Both the terminal's (Console.ReadKey, Console.TreatControlCAsInput) unless a test gives its own
        internal ConsoleTerminal(ConsoleUi.Capabilities Caps, Func<ConsoleKeyInfo?>? NextKey = null, Func<bool>? ControlCAsInput = null, Action<bool>? SetControlCAsInput = null)
        {
            this.Caps = Caps;
            this.NextKey = NextKey ?? ReadKey;
            this.ControlCAsInput = ControlCAsInput ?? (() => Console.TreatControlCAsInput);
            this.SetControlCAsInput = SetControlCAsInput ?? (Value => Console.TreatControlCAsInput = Value);
        }

        public override int Width => Caps.Width;
        public override bool CanHideInput => Caps.InputIsTerminal;
        public override void WriteLine(string Text) => ConsoleUi.Line(Text);
        public override void WriteHeading(string Text) => ConsoleUi.Line(Text, ConsoleColor.Green);
        public override void WriteWarning(string Text) => ConsoleUi.Warning(Text);
        public override void WriteError(string Text) => ConsoleUi.Error(Text);
        public override void WriteStopped() => ConsoleUi.Stopped();
        public override void WritePrompt(string Text) => ConsoleUi.Prompt(Text);
        public override TerminalInput ReadLine() => Read(false);
        public override TerminalInput ReadSecret() => CanHideInput ? Read(true) : TerminalInput.Unavailable;

        private TerminalInput Read(bool Hidden)
        {
            bool Restore = false;
            bool Before = false;
            try
            {
                Before = ControlCAsInput();
                SetControlCAsInput(true);
                Restore = true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is PlatformNotSupportedException)
            {
                //a console that can't read Ctrl+C as a key: Ctrl+C keeps ending the process (ConsoleUi's handler)
            }
            try
            {
                return KeyLineReader.Read(NextKey, ConsoleUi.Echo, Hidden, OperatingSystem.IsWindows());
            }
            finally
            {
                //however the read ended (an answer, Esc, Ctrl+C, the end of the input, a failure): Ctrl+C as it was before
                if (Restore)
                {
                    try
                    {
                        SetControlCAsInput(Before);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is PlatformNotSupportedException)
                    {
                        //the terminal is gone
                    }
                }
            }
        }

        private static ConsoleKeyInfo? ReadKey()
        {
            try
            {
                return Console.ReadKey(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException)
            {
                //no terminal to read from any more: the end of the input
                return null;
            }
        }
    }

    // The interactive console's line editor: an answer read key by key. Enter ends it; Backspace removes the last character
    // (written back as backspace, space, backspace, the one way to rub a character out that every terminal knows, never an
    // escape sequence; nothing for a secret); Esc and Ctrl+C end the read without an answer; Ctrl+D on an empty line, on
    // Windows also Ctrl+Z, and a key source that has ended are the end of the input. Every other control character and
    // every key without a character (the arrows, the function keys) is ignored: no cursor moves. A secret is never written
    // back, not even as a mask. An answer has at most MaxLength characters, so a held key or a long paste can't grow it
    // without bound; the keys after that are ignored.
    public static class KeyLineReader
    {
        public const int MaxLength = 4096;
        public const string RubOut = "\b \b";

        // NextKey: the next key, or null at the end of the input. Echo: what is written back, as it is typed.
        // CtrlZEndsInput: Ctrl+Z on an empty line is the end of the input (Windows' end-of-file key; elsewhere it is ignored)
        public static TerminalInput Read(Func<ConsoleKeyInfo?> NextKey, Action<string> Echo, bool Hidden, bool CtrlZEndsInput)
        {
            var Text = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo? Next = NextKey();
                if (Next == null)
                {
                    Echo(Environment.NewLine);
                    return TerminalInput.EndOfInput;
                }
                ConsoleKeyInfo Key = Next.Value;
                char C = Key.KeyChar;
                if (C == '\r' || C == '\n' || (C == '\0' && Key.Key == ConsoleKey.Enter))
                {
                    Echo(Environment.NewLine);
                    return TerminalInput.Line(WithoutLoneSurrogate(Text));
                }
                if (C == '\u001b' || (C == '\0' && Key.Key == ConsoleKey.Escape))
                {
                    Echo(Environment.NewLine);
                    return TerminalInput.Escape;
                }
                if (C == '\u0003')
                {
                    Echo(Environment.NewLine);
                    return TerminalInput.Interrupt;
                }
                if (C == '\u0004' || (CtrlZEndsInput && C == '\u001a'))
                {
                    if (Text.Length == 0)
                    {
                        Echo(Environment.NewLine);
                        return TerminalInput.EndOfInput;
                    }
                    continue;
                }
                if (C == '\b' || C == '\u007f' || (C == '\0' && Key.Key == ConsoleKey.Backspace))
                {
                    if (Text.Length > 0)
                    {
                        //a character outside the Basic Multilingual Plane is two UTF-16 units: removed together
                        int Remove = Text.Length >= 2 && char.IsLowSurrogate(Text[Text.Length - 1]) && char.IsHighSurrogate(Text[Text.Length - 2]) ? 2 : 1;
                        Text.Remove(Text.Length - Remove, Remove);
                        if (!Hidden)
                        {
                            Echo(RubOut);
                        }
                    }
                    continue;
                }
                if (C == '\0' || char.IsControl(C) || Text.Length >= MaxLength)
                {
                    continue;
                }
                Text.Append(C);
                if (!Hidden)
                {
                    Echo(C.ToString());
                }
            }
        }

        // a high surrogate whose low half did not fit is not a character: left out
        private static string WithoutLoneSurrogate(StringBuilder Text)
        {
            if (Text.Length > 0 && char.IsHighSurrogate(Text[Text.Length - 1]))
            {
                Text.Remove(Text.Length - 1, 1);
            }
            return Text.ToString();
        }
    }
}
