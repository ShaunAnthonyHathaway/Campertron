using System.Text.RegularExpressions;
using CampertronLibrary.function.Interactive;

namespace CampertronUnitTests.function.Interactive
{
    // The terminal of the interactive console's settings tests (InteractiveNotificationsTests, phase 7, D122, and
    // InteractiveSettingsTests, phase 7a, D123), moved here unchanged from the first so that both use one.

    // a choice of the last menu shown, by its label exactly
    internal sealed record Pick(string Label);

    // The terminal of these tests: its script holds answers (TerminalInput), choices by label (Pick) and actions to run
    // at that moment (another program changing a file). Every line written is kept with its kind
    internal sealed class MenuTerminal : InteractiveTerminal
    {
        private readonly Queue<object> Script;
        internal List<(string Kind, string Text)> Written { get; } = new();
        internal int SecretReads { get; private set; }
        internal bool Hides { get; set; } = true;
        private int ReadsAfterTheEnd;

        internal MenuTerminal(params object[] Script)
        {
            this.Script = new Queue<object>(Script);
        }

        public override int Width => int.MaxValue;
        public override bool CanHideInput => Hides;
        public override void WriteLine(string Text) => Written.Add(("out", Text));
        public override void WriteHeading(string Text) => Written.Add(("heading", Text));
        public override void WriteWarning(string Text) => Written.Add(("warning", Text));
        public override void WriteError(string Text) => Written.Add(("error", Text));
        public override void WritePrompt(string Text) => Written.Add(("prompt", Text));
        public override void WriteStopped() => Written.Add(("stopped", "Stopped."));

        private object Next()
        {
            while (Script.Count > 0)
            {
                object Item = Script.Dequeue();
                if (Item is Action Do)
                {
                    Do();
                    continue;
                }
                return Item;
            }
            if (++ReadsAfterTheEnd > 1000)
            {
                throw new InvalidOperationException("the input ended, and it was read again and again");
            }
            return TerminalInput.EndOfInput;
        }

        public override TerminalInput ReadLine()
        {
            object Item = Next();
            if (Item is Pick Choice)
            {
                string Asked = Written.Last().Text;
                if (!Asked.StartsWith("Choose a number", StringComparison.Ordinal) && !Asked.StartsWith("Type 1 and press Enter", StringComparison.Ordinal))
                {
                    throw new AssertionException("a choice (" + Choice.Label + ") was scripted, but the console asked: " + Asked);
                }
                List<string> Offered = LastChoices;
                int At = Offered.IndexOf(Choice.Label);
                if (At < 0)
                {
                    throw new AssertionException("no choice " + Choice.Label + " was offered; the choices: " + string.Join(" | ", Offered));
                }
                return TerminalInput.Line((At + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return (TerminalInput)Item;
        }

        public override TerminalInput ReadSecret()
        {
            SecretReads++;
            if (!Hides)
            {
                return TerminalInput.Unavailable;
            }
            return (TerminalInput)Next();
        }

        private static readonly Regex Numbered = new Regex(@"^  (\d+)\. (.*)$", RegexOptions.CultureInvariant);

        // the choices of the last menu shown: its numbered lines after the last heading
        internal List<string> LastChoices
        {
            get
            {
                int Heading = Written.FindLastIndex(W => W.Kind == "heading");
                return Written.Skip(Heading + 1).Where(W => W.Kind == "out").Select(W => Numbered.Match(W.Text)).Where(M => M.Success).Select(M => M.Groups[2].Value).ToList();
            }
        }

        // every menu's choices, in order
        internal List<List<string>> Menus { get; } = new();

        internal List<string> Texts => Written.Select(W => W.Text).ToList();
        internal string AllText => string.Join("\n", Texts);
        internal List<string> Prompts => Written.Where(W => W.Kind == "prompt").Select(W => W.Text).ToList();
    }
}
