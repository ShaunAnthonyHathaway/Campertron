using System.Text.RegularExpressions;
using CampertronLibrary.function.Interactive;

namespace CampertronUnitTests.function.Interactive
{
    // The terminal of the interactive console's settings tests (InteractiveNotificationsTests, phase 7, D122, and
    // InteractiveSettingsTests, phase 7a, D123), moved here unchanged from the first so that both use one.

    // a choice of the last menu shown, by its label exactly
    internal sealed record Pick(string Label);

    // a key typed while an operation runs (since v0.4.0's phase 9, D125: the campground data's refresh), read once When
    // holds (no condition: at the operation's first look at the keys)
    internal sealed record Press(TerminalInputKind Key, Func<bool>? When = null);

    // the answer to "Press Enter to continue." (since D130): without one in the script, Enter is answered by itself
    internal sealed record Cont(TerminalInput Key);

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

        public override bool CanHideInput => Hides;
        public override void WriteLine(string Text) => Written.Add(("out", Text));
        public override void WriteHeading(string Text) => Written.Add(("heading", Text));
        public override void WriteWarning(string Text) => Written.Add(("warning", Text));
        public override void WriteError(string Text) => Written.Add(("error", Text));
        public override void WritePrompt(string Text) => Written.Add(("prompt", Text));
        public override void WriteStopped() => Written.Add(("stopped", "Stopped."));

        // ---- the screen (since D130): recorded as events among the lines, never as a picture of a screen ----

        // whether this terminal clears (a terminal that can't: the new screen follows after an empty line)
        internal bool CanClear { get; set; } = true;
        // a known width (0: unknown, as on redirected output; the logo is then the plain name)
        internal int Columns { get; set; }
        public override bool WidthKnown => Columns > 0;
        public override int Width => Columns > 0 ? Columns : int.MaxValue;
        public override bool ClearScreen()
        {
            //an operation's progress is never taken away while it runs (since D130: every test that runs one checks it)
            if (KeysAsTyped)
            {
                throw new AssertionException("the screen was cleared while an operation ran: " + Written.Last().Text);
            }
            if (CanClear)
            {
                Written.Add(("clear", string.Empty));
            }
            return CanClear;
        }
        public override void WriteLogo(string Text) => Written.Add(("logo", Text));
        // "Press Enter to continue.": the script's Cont when it is next, else Enter by itself; recorded either way
        public override TerminalInput ReadContinue()
        {
            Written.Add(("continue", string.Empty));
            if (Script.Count > 0 && Script.Peek() is Cont Answer)
            {
                Script.Dequeue();
                return Answer.Key;
            }
            return TerminalInput.Line(string.Empty);
        }
        internal int Clears => Written.Count(W => W.Kind == "clear");
        internal int Continues => Written.Count(W => W.Kind == "continue");

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

        // how often an operation looked at the keys, whether its keys are read as they are typed (Ctrl+C a key) now, and
        // how often that began
        internal int Polls;
        internal int OperationKeys;
        internal bool KeysAsTyped;

        public override IDisposable ReadKeysAsTheyAreTyped()
        {
            OperationKeys++;
            KeysAsTyped = true;
            return new Undo(() => KeysAsTyped = false);
        }

        public override TerminalInputKind? NextKeyTyped()
        {
            Interlocked.Increment(ref Polls);
            while (Script.Count > 0 && Script.Peek() is Action Do)
            {
                Script.Dequeue();
                Do();
            }
            if (Script.Count > 0 && Script.Peek() is Press Key && (Key.When?.Invoke() ?? true))
            {
                Script.Dequeue();
                return Key.Key;
            }
            return null;
        }

        private sealed class Undo : IDisposable
        {
            private readonly Action Do;
            internal Undo(Action Do) => this.Do = Do;
            public void Dispose() => Do();
        }

        public override TerminalInput ReadLine()
        {
            if (KeysAsTyped)
            {
                throw new AssertionException("a question was asked while an operation ran: " + Written.Last().Text);
            }
            //since v0.4.0's phase 10 (D126): every menu answered, its heading and its choices, in order
            string Question = Written.Count > 0 ? Written[^1].Text : string.Empty;
            if (Question.StartsWith("Choose a number", StringComparison.Ordinal) || Question.StartsWith("Type 1 and press Enter", StringComparison.Ordinal))
            {
                int Heading = Written.FindLastIndex(W => W.Kind == "heading");
                Menus.Add((Heading >= 0 ? Written[Heading].Text : string.Empty, LastChoices));
            }
            object Item = Next();
            if (Item is Press Typed)
            {
                throw new AssertionException("a key (" + Typed.Key + ") was scripted for an operation, but the console asked: " + Written.Last().Text);
            }
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

        // every menu answered: its heading and its choices, in order (since v0.4.0's phase 10, D126)
        internal List<(string Heading, List<string> Choices)> Menus { get; } = new();

        // the choices of the menus answered under a heading, in order
        internal List<List<string>> MenusOf(string Heading) => Menus.Where(M => M.Heading == Heading).Select(M => M.Choices).ToList();

        // every text written, in order, one for each entry of Written (a screen's event, clear or continue, has none)
        internal List<string> Texts => Written.Select(W => W.Text).ToList();
        internal string AllText => string.Join("\n", Texts);
        internal List<string> Prompts => Written.Where(W => W.Kind == "prompt").Select(W => W.Text).ToList();
    }
}
