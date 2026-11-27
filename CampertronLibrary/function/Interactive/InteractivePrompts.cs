using System.Globalization;
using CampertronLibrary.function.Base;

namespace CampertronLibrary.function.Interactive
{
    // how a question of the interactive console ended
    public enum PromptEnd
    {
        Answered,
        // Esc: the question was cancelled
        Cancelled,
        // Ctrl+C: below the main menu it cancels the question as Esc does; at the main menu it ends the console
        Interrupted,
        // the end of the input: the console ends
        EndOfInput,
        // the terminal can't ask it (a secret, without a terminal that hides what is typed): nothing was read
        Unavailable
    }

    // the outcome of a question: its end and, when it was answered, the answer. ToString never shows the answer
    public sealed class PromptResult<T>
    {
        public PromptEnd End { get; }
        // the answer (Answered only)
        public T? Value { get; }

        internal PromptResult(PromptEnd End, T? Value = default)
        {
            this.End = End;
            this.Value = Value;
        }

        public bool IsAnswered => End == PromptEnd.Answered;
        // Esc or Ctrl+C: a question below the main menu is cancelled by either
        public bool IsCancelled => End == PromptEnd.Cancelled || End == PromptEnd.Interrupted;

        public override string ToString() => End.ToString();
    }

    // A secret the user typed (a password, a token), kept out of every text by mistake: ToString is "[hidden]", so it can't
    // reach a line, a log or a test's failure message through string formatting. Reveal gives it to the one place that uses
    // it, which never writes, shows or logs it
    public sealed class SecretText
    {
        private readonly string Value;

        internal SecretText(string Value)
        {
            this.Value = Value;
        }

        public int Length => Value.Length;
        public string Reveal() => Value;
        public override string ToString() => "[hidden]";
    }

    // The questions every screen of the interactive console asks (v0.4.0's phase 6, D121; business-rules §11): a numbered
    // choice, yes or no with an explicit default, text that is required or optional, and a secret. Each one asks in a loop,
    // never by calling itself: an answer it can't use is told in a short sentence and the question is asked again (with its
    // choices), up to its number of attempts, after which it ends as cancelled. Esc cancels a question and Ctrl+C
    // interrupts it (PromptEnd), and the end of the input ends it at once, whatever was typed before, so an input that has
    // ended can never be asked again and again. Nothing here writes a file or reads anything but the terminal; the words
    // are InteractiveWording's.
    public sealed class InteractivePrompts
    {
        // how many unusable answers a question below the main menu takes before it ends as cancelled
        public const int DefaultAttempts = 5;

        public InteractiveTerminal Terminal { get; }

        public InteractivePrompts(InteractiveTerminal Terminal)
        {
            this.Terminal = Terminal ?? throw new ArgumentNullException(nameof(Terminal));
        }

        // a plain line, wrapped at the terminal's width; a wrapped part starts with Indent
        public void Say(string Text, string Indent = "  ")
        {
            foreach (string Line in ConsoleUi.Wrap(Text, Terminal.Width, Indent))
            {
                Terminal.WriteLine(Line);
            }
        }

        // an empty line, then the heading
        public void Heading(string Text)
        {
            Terminal.WriteLine(string.Empty);
            foreach (string Line in ConsoleUi.Wrap(Text, Terminal.Width, "  "))
            {
                Terminal.WriteHeading(Line);
            }
        }

        // a numbered choice: the heading, the options numbered from 1, and the question; the answer is the option's index
        // (from 0). Attempts: null asks until there is an answer (the main menu, where every answer needs a key)
        public PromptResult<int> Choose(string Title, IReadOnlyList<string> Options, int? Attempts = DefaultAttempts)
        {
            if (Options == null || Options.Count == 0)
            {
                throw new ArgumentException("A choice needs at least one option.", nameof(Options));
            }
            int Tries = 0;
            while (true)
            {
                Heading(Title);
                for (int i = 0; i < Options.Count; i++)
                {
                    string Number = (i + 1).ToString(CultureInfo.InvariantCulture);
                    Say("  " + Number + ". " + Options[i], new string(' ', 4 + Number.Length + 2));
                }
                Terminal.WritePrompt(InteractiveWording.ChoosePrompt(Options.Count));
                TerminalInput Input = Terminal.ReadLine();
                if (Input.Kind != TerminalInputKind.Line)
                {
                    return Ended<int>(Input);
                }
                if (TryNumber(Input.Text, Options.Count, out int Chosen))
                {
                    return new PromptResult<int>(PromptEnd.Answered, Chosen - 1);
                }
                if (OutOfAttempts(ref Tries, Attempts))
                {
                    return new PromptResult<int>(PromptEnd.Cancelled);
                }
                Say(Input.Text.Trim().Length == 0 ? InteractiveWording.NoChoice(Options.Count) : InteractiveWording.NotAChoice(Options.Count));
            }
        }

        // yes or no. DefaultAnswer is what Enter alone answers, shown as the capital letter of [Y/n] or [y/N]; y, yes, n and
        // no are understood in any case
        public PromptResult<bool> Confirm(string Question, bool DefaultAnswer, int Attempts = DefaultAttempts)
        {
            int Tries = 0;
            while (true)
            {
                Terminal.WritePrompt(Question + (DefaultAnswer ? " [Y/n]: " : " [y/N]: "));
                TerminalInput Input = Terminal.ReadLine();
                if (Input.Kind != TerminalInputKind.Line)
                {
                    return Ended<bool>(Input);
                }
                string Answer = Input.Text.Trim().ToLowerInvariant();
                if (Answer.Length == 0)
                {
                    return new PromptResult<bool>(PromptEnd.Answered, DefaultAnswer);
                }
                if (Answer == "y" || Answer == "yes")
                {
                    return new PromptResult<bool>(PromptEnd.Answered, true);
                }
                if (Answer == "n" || Answer == "no")
                {
                    return new PromptResult<bool>(PromptEnd.Answered, false);
                }
                if (OutOfAttempts(ref Tries, Attempts))
                {
                    return new PromptResult<bool>(PromptEnd.Cancelled);
                }
                Say(InteractiveWording.YesOrNo);
            }
        }

        // text, without the spaces around it. Required: an empty answer is asked again; else it is the answer (""). Check
        // returns what is wrong with an answer, in fixed words that never repeat it, or null when it can be used. Trim false
        // (since v0.4.0's phase 8, D124): the answer exactly as typed, spaces included, for a form that judges them itself
        // (a search's name, a filter's value, a file name); an answer of only spaces is then empty for Required alone
        public PromptResult<string> Text(string Question, bool Required, Func<string, string?>? Check = null, int Attempts = DefaultAttempts, bool Trim = true)
        {
            int Tries = 0;
            while (true)
            {
                Terminal.WritePrompt(Question + (Required ? ": " : InteractiveWording.OptionalSuffix));
                TerminalInput Input = Terminal.ReadLine();
                if (Input.Kind != TerminalInputKind.Line)
                {
                    return Ended<string>(Input);
                }
                string Answer = Trim ? Input.Text.Trim() : Input.Text;
                string? Problem = Answer.Trim().Length == 0
                    ? (Required ? InteractiveWording.Required : null)
                    : Check?.Invoke(Answer);
                if (Problem == null)
                {
                    return new PromptResult<string>(PromptEnd.Answered, Answer);
                }
                if (OutOfAttempts(ref Tries, Attempts))
                {
                    return new PromptResult<string>(PromptEnd.Cancelled);
                }
                Say(Problem);
            }
        }

        // a secret, typed without being shown and never written back; an empty one is asked again. On a terminal that can't
        // hide what is typed nothing is read: the question says so and ends Unavailable (it never pretends to hide it)
        public PromptResult<SecretText> Secret(string Question, int Attempts = DefaultAttempts)
        {
            int Tries = 0;
            while (true)
            {
                if (!Terminal.CanHideInput)
                {
                    Say(InteractiveWording.SecretNeedsTerminal);
                    return new PromptResult<SecretText>(PromptEnd.Unavailable);
                }
                Terminal.WritePrompt(Question + InteractiveWording.SecretSuffix);
                TerminalInput Input = Terminal.ReadSecret();
                if (Input.Kind == TerminalInputKind.Unavailable)
                {
                    Say(InteractiveWording.SecretNeedsTerminal);
                    return new PromptResult<SecretText>(PromptEnd.Unavailable);
                }
                if (Input.Kind != TerminalInputKind.Line)
                {
                    return Ended<SecretText>(Input);
                }
                if (Input.Text.Length > 0)
                {
                    return new PromptResult<SecretText>(PromptEnd.Answered, new SecretText(Input.Text));
                }
                if (OutOfAttempts(ref Tries, Attempts))
                {
                    return new PromptResult<SecretText>(PromptEnd.Cancelled);
                }
                Say(InteractiveWording.SecretEmpty);
            }
        }

        // one more unusable answer; true (and the reason said) when it was the last one allowed
        private bool OutOfAttempts(ref int Tries, int? Attempts)
        {
            Tries++;
            if (Attempts.HasValue && Tries >= Attempts.Value)
            {
                Say(InteractiveWording.TooManyAttempts);
                return true;
            }
            return false;
        }

        private static PromptResult<T> Ended<T>(TerminalInput Input)
        {
            return new PromptResult<T>(Input.Kind switch
            {
                TerminalInputKind.Escape => PromptEnd.Cancelled,
                TerminalInputKind.Interrupt => PromptEnd.Interrupted,
                TerminalInputKind.Unavailable => PromptEnd.Unavailable,
                _ => PromptEnd.EndOfInput
            });
        }

        // a whole number from 1 to Count, in ASCII digits, with spaces around it allowed (no sign, no other characters)
        internal static bool TryNumber(string Text, int Count, out int Number)
        {
            Number = 0;
            string Digits = (Text ?? string.Empty).Trim();
            if (Digits.Length == 0 || Digits.Length > 9 || !Digits.All(C => C >= '0' && C <= '9'))
            {
                return false;
            }
            Number = int.Parse(Digits, NumberStyles.None, CultureInfo.InvariantCulture);
            return Number >= 1 && Number <= Count;
        }
    }
}
