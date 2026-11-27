using CampertronLibrary.function.Interactive;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's terminal for the tests: its answers are scripted (lines, Esc, Ctrl+C, the end of the input)
    // and every line it is given is recorded with its kind, so a test judges the words and the outcome, never a screen.
    // When the script has run out, every further read is the end of the input, as a closed input is; Reads counts them, so
    // a test can see that nothing asks again and again.
    internal sealed class ScriptedTerminal : InteractiveTerminal
    {
        internal const string Out = "out";
        internal const string HeadingKind = "heading";
        internal const string WarningKind = "warning";
        internal const string ErrorKind = "error";
        internal const string PromptKind = "prompt";
        internal const string StoppedKind = "stopped";

        private readonly Queue<TerminalInput> Script;
        internal List<(string Kind, string Text)> Written { get; } = new();
        internal int Reads { get; private set; }
        internal int SecretReads { get; private set; }
        // int.MaxValue: lines are never wrapped (as on redirected output), so a test can compare them whole; a test of wrapping
        // gives a width
        internal int TerminalWidth { get; set; } = int.MaxValue;
        internal bool Hides { get; set; } = true;

        internal ScriptedTerminal(params TerminalInput[] Script)
        {
            this.Script = new Queue<TerminalInput>(Script);
        }

        internal static TerminalInput Line(string Text) => TerminalInput.Line(Text);
        internal static TerminalInput Esc => TerminalInput.Escape;
        internal static TerminalInput CtrlC => TerminalInput.Interrupt;
        internal static TerminalInput End => TerminalInput.EndOfInput;

        public override int Width => TerminalWidth;
        public override bool CanHideInput => Hides;
        public override void WriteLine(string Text) => Written.Add((Out, Text));
        public override void WriteHeading(string Text) => Written.Add((HeadingKind, Text));
        public override void WriteWarning(string Text) => Written.Add((WarningKind, Text));
        public override void WriteError(string Text) => Written.Add((ErrorKind, Text));
        public override void WritePrompt(string Text) => Written.Add((PromptKind, Text));
        public override void WriteStopped() => Written.Add((StoppedKind, "Stopped."));

        // an input that has ended and is read again and again is a defect: after a thousand reads past its end the read fails
        private int ReadsAfterTheEnd;
        private TerminalInput Next()
        {
            if (Script.Count > 0)
            {
                return Script.Dequeue();
            }
            if (++ReadsAfterTheEnd > 1000)
            {
                throw new InvalidOperationException("the input ended, and it was read again and again");
            }
            return TerminalInput.EndOfInput;
        }

        public override TerminalInput ReadLine()
        {
            Reads++;
            return Next();
        }

        public override TerminalInput ReadSecret()
        {
            SecretReads++;
            if (!Hides)
            {
                return TerminalInput.Unavailable;
            }
            return Next();
        }

        internal int Left => Script.Count;

        // every text written, of every kind, in order
        internal List<string> Texts => Written.Select(W => W.Text).ToList();
        internal string AllText => string.Join("\n", Texts);
        internal List<string> OfKind(string Kind) => Written.Where(W => W.Kind == Kind).Select(W => W.Text).ToList();
    }
}
