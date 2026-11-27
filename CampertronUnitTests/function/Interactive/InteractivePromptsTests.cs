using CampertronLibrary.function.Interactive;
using static CampertronUnitTests.function.Interactive.ScriptedTerminal;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's questions (InteractivePrompts): a numbered choice, yes or no, text and a secret, each
    // driven by a scripted terminal and judged by its outcome and its words. Every question asks in a loop and reads once
    // per answer, so a test can count the reads; the end of the input ends a question at once. The secret's sentinel is
    // never put into an assertion's message.
    public class InteractivePromptsTests
    {
        private const string Sentinel = "S3ntinel-Secret-4f1c";
        private static readonly string[] Three = { "Status", "Help", "Exit" };

        [TestCase("1", 0)]
        [TestCase("2", 1)]
        [TestCase(" 3 ", 2)]
        [TestCase("03", 2)]
        public void Choose_TakesANumberFromTheList(string Typed, int Index)
        {
            var Terminal = new ScriptedTerminal(Line(Typed));
            PromptResult<int> Result = new InteractivePrompts(Terminal).Choose("Main menu", Three);

            Assert.That(Result.End, Is.EqualTo(PromptEnd.Answered));
            Assert.That(Result.Value, Is.EqualTo(Index));
            Assert.That(Terminal.OfKind(ScriptedTerminal.HeadingKind), Is.EqualTo(new[] { "Main menu" }));
            Assert.That(Terminal.OfKind(ScriptedTerminal.Out), Is.EqualTo(new[] { string.Empty, "  1. Status", "  2. Help", "  3. Exit" }));
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.EqualTo(new[] { "Choose a number from 1 to 3: " }));
        }

        [TestCase("4")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("+1")]
        [TestCase("1.0")]
        [TestCase("one")]
        [TestCase("1 2")]
        [TestCase("１")]
        [TestCase("99999999999999999999")]
        public void Choose_SaysWhatIsWrong_ShowsTheChoicesAgain_AndAsksAgain(string Typed)
        {
            var Terminal = new ScriptedTerminal(Line(Typed), Line("2"));
            PromptResult<int> Result = new InteractivePrompts(Terminal).Choose("Main menu", Three);

            Assert.That(Result.Value, Is.EqualTo(1));
            Assert.That(Terminal.Reads, Is.EqualTo(2));
            Assert.That(Terminal.OfKind(ScriptedTerminal.HeadingKind), Has.Count.EqualTo(2), "the choices are shown again");
            Assert.That(Terminal.Texts, Does.Contain("That is not one of the choices. Type a number from 1 to 3."), "fixed words: the answer is not repeated back");
            Assert.That(Terminal.Texts, Has.Count.EqualTo(13), "two headings with their empty lines and choices, two questions and the one sentence");
        }

        [Test]
        public void Choose_AnEmptyAnswer_SaysHowToChoose()
        {
            var Terminal = new ScriptedTerminal(Line(""), Line("   "), Line("1"));
            PromptResult<int> Result = new InteractivePrompts(Terminal).Choose("Main menu", Three);

            Assert.That(Result.Value, Is.EqualTo(0));
            Assert.That(Terminal.Texts.Count(T => T == "Type the number of a choice, from 1 to 3, then press Enter."), Is.EqualTo(2));
        }

        [Test]
        public void Choose_EndsAsCancelled_AfterItsAttempts_ButTheMainMenusChoiceAsksUntilAnAnswer()
        {
            var Bounded = new ScriptedTerminal(Enumerable.Repeat(Line("x"), 10).ToArray());
            PromptResult<int> Result = new InteractivePrompts(Bounded).Choose("Which", Three);
            Assert.That(Result.End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(Bounded.Reads, Is.EqualTo(InteractivePrompts.DefaultAttempts));
            Assert.That(Bounded.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));

            var Unbounded = new ScriptedTerminal(Enumerable.Repeat(Line("x"), 50).Append(Line("3")).ToArray());
            Assert.That(new InteractivePrompts(Unbounded).Choose("Main menu", Three, null).Value, Is.EqualTo(2));
            Assert.That(Unbounded.Reads, Is.EqualTo(51));
        }

        [Test]
        public void Choose_ManyWrongAnswers_NeverGoDeeper()
        {
            //a loop, not recursion: a hundred thousand wrong answers on a small stack, then an answer
            const int Wrong = 100000;
            PromptResult<int>? Result = null;
            Exception? Failure = null;
            var Worker = new Thread(() =>
            {
                try
                {
                    var Terminal = new ScriptedTerminal(Enumerable.Repeat(Line("x"), Wrong).Append(Line("1")).ToArray());
                    Result = new InteractivePrompts(Terminal).Choose("Main menu", Three, null);
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }
            }, 256 * 1024);
            Worker.Start();
            Assert.That(Worker.Join(TimeSpan.FromMinutes(2)), Is.True);
            Assert.That(Failure, Is.Null, Failure?.ToString());
            Assert.That(Result!.Value, Is.EqualTo(0));
        }

        [Test]
        public void Choose_EscCtrlCAndTheEndOfTheInput_EndIt_AndNothingIsReadAfterThem()
        {
            var Esc_ = new ScriptedTerminal(Esc, Line("1"));
            Assert.That(new InteractivePrompts(Esc_).Choose("Main menu", Three).End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(Esc_.Left, Is.EqualTo(1));

            var Ctrl = new ScriptedTerminal(CtrlC, Line("1"));
            PromptResult<int> Interrupted = new InteractivePrompts(Ctrl).Choose("Main menu", Three);
            Assert.That(Interrupted.End, Is.EqualTo(PromptEnd.Interrupted));
            Assert.That(Interrupted.IsCancelled, Is.True, "below the main menu Ctrl+C cancels as Esc does");
            Assert.That(Ctrl.Left, Is.EqualTo(1));

            var Ended = new ScriptedTerminal();
            Assert.That(new InteractivePrompts(Ended).Choose("Main menu", Three, null).End, Is.EqualTo(PromptEnd.EndOfInput));
            Assert.That(Ended.Reads, Is.EqualTo(1), "an input that ended is not asked again and again");
        }

        [Test]
        public void Choose_OnANarrowTerminal_WrapsAnOptionUnderItsText()
        {
            var Terminal = new ScriptedTerminal(Line("1")) { TerminalWidth = 24 };
            new InteractivePrompts(Terminal).Choose("A heading that is long enough to wrap", new[] { "An option whose label is long enough to wrap" });

            Assert.That(Terminal.Written.Where(W => W.Kind != ScriptedTerminal.PromptKind).Select(W => W.Text).Where(T => T.Length > 24), Is.Empty);
            List<string> Option = Terminal.OfKind(ScriptedTerminal.Out).Where(T => T.Length > 0).ToList();
            Assert.That(Option[0], Does.StartWith("  1. An option"));
            Assert.That(Option.Skip(1), Has.All.StartWith("     "), "a wrapped part starts under the label");
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.EqualTo(new[] { "Type 1 and press Enter: " }));
        }

        [TestCase("y", true, true)]
        [TestCase("Y", false, true)]
        [TestCase("yes", false, true)]
        [TestCase(" YES ", false, true)]
        [TestCase("n", true, false)]
        [TestCase("No", true, false)]
        public void Confirm_TakesYesOrNo(string Typed, bool Default, bool Expected)
        {
            var Terminal = new ScriptedTerminal(Line(Typed));
            PromptResult<bool> Result = new InteractivePrompts(Terminal).Confirm("Save the changes?", Default);

            Assert.That(Result.End, Is.EqualTo(PromptEnd.Answered));
            Assert.That(Result.Value, Is.EqualTo(Expected));
        }

        [TestCase(false, "Save the changes? [y/N]: ")]
        [TestCase(true, "Save the changes? [Y/n]: ")]
        public void Confirm_EnterAlone_IsItsExplicitDefault_ShownAsTheCapital(bool Default, string Prompt)
        {
            var Terminal = new ScriptedTerminal(Line(""));
            PromptResult<bool> Result = new InteractivePrompts(Terminal).Confirm("Save the changes?", Default);

            Assert.That(Result.Value, Is.EqualTo(Default));
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.EqualTo(new[] { Prompt }));
        }

        [Test]
        public void Confirm_AnotherAnswer_IsAskedAgain_ThenCancelledAfterItsAttempts()
        {
            var Terminal = new ScriptedTerminal(Line("maybe"), Line("y"));
            Assert.That(new InteractivePrompts(Terminal).Confirm("Save?", false).Value, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("Type y for yes or n for no."));

            var Endless = new ScriptedTerminal(Enumerable.Repeat(Line("maybe"), 20).ToArray());
            Assert.That(new InteractivePrompts(Endless).Confirm("Save?", false).End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(Endless.Reads, Is.EqualTo(InteractivePrompts.DefaultAttempts));
        }

        [Test]
        public void Confirm_EscCtrlCAndTheEnd_AreNeverAYesOrANo()
        {
            Assert.That(new InteractivePrompts(new ScriptedTerminal(Esc)).Confirm("Save?", true).End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(new InteractivePrompts(new ScriptedTerminal(CtrlC)).Confirm("Save?", true).End, Is.EqualTo(PromptEnd.Interrupted));
            PromptResult<bool> Ended = new InteractivePrompts(new ScriptedTerminal()).Confirm("Save?", true);
            Assert.That(Ended.End, Is.EqualTo(PromptEnd.EndOfInput));
            Assert.That(Ended.IsAnswered, Is.False);
        }

        [Test]
        public void Text_Required_RefusesABlankAnswer_AndTrimsTheAnswer()
        {
            var Terminal = new ScriptedTerminal(Line(""), Line("   "), Line("  Watchman  "));
            PromptResult<string> Result = new InteractivePrompts(Terminal).Text("Name", true);

            Assert.That(Result.Value, Is.EqualTo("Watchman"));
            Assert.That(Terminal.Texts.Count(T => T == "An answer is needed here. Type it, or press Esc to cancel."), Is.EqualTo(2));
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Has.All.EqualTo("Name: "));
        }

        [Test]
        public void Text_Optional_TakesABlankAnswer()
        {
            var Terminal = new ScriptedTerminal(Line(""));
            PromptResult<string> Result = new InteractivePrompts(Terminal).Text("Note", false);

            Assert.That(Result.End, Is.EqualTo(PromptEnd.Answered));
            Assert.That(Result.Value, Is.Empty);
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.EqualTo(new[] { "Note (optional, Enter leaves it empty): " }));
        }

        // v0.4.0's phase 8 (D124): an answer for a form that judges its spaces itself (a search's name, a filter's value, a
        // file name) is given exactly as typed; only for Required is an answer of spaces alone empty
        [Test]
        public void Text_WithoutTrim_GivesTheAnswerExactlyAsTyped()
        {
            var Optional = new ScriptedTerminal(Line("  Weekend trip  "), Line("   "));
            var Prompts = new InteractivePrompts(Optional);
            Assert.That(Prompts.Text("Name", false, Trim: false).Value, Is.EqualTo("  Weekend trip  "));
            Assert.That(Prompts.Text("Name", false, Trim: false).Value, Is.EqualTo("   "), "spaces alone, as typed: the form says what it makes of them");

            var Required = new ScriptedTerminal(Line("   "), Line(" B 7 "));
            Assert.That(new InteractivePrompts(Required).Text("Value", true, Trim: false).Value, Is.EqualTo(" B 7 "));
            Assert.That(Required.Texts, Does.Contain("An answer is needed here. Type it, or press Esc to cancel."));
            Assert.That(new InteractivePrompts(new ScriptedTerminal(Line("  x  "))).Text("Value", false).Value, Is.EqualTo("x"), "the default still trims");
        }

        [Test]
        public void Text_ItsCheck_SaysWhatIsWrong_AndAsksAgain_UpToItsAttempts()
        {
            Func<string, string?> Digits = T => T.All(char.IsAsciiDigit) ? null : "Type digits only.";
            var Terminal = new ScriptedTerminal(Line("abc"), Line("123"));
            Assert.That(new InteractivePrompts(Terminal).Text("Number", true, Digits).Value, Is.EqualTo("123"));
            Assert.That(Terminal.Texts, Does.Contain("Type digits only."));

            var Endless = new ScriptedTerminal(Enumerable.Repeat(Line("abc"), 9).ToArray());
            Assert.That(new InteractivePrompts(Endless).Text("Number", true, Digits, 3).End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(Endless.Reads, Is.EqualTo(3));
        }

        [Test]
        public void Secret_IsReadHidden_AndNeverWrittenOrShownByItsResult()
        {
            var Terminal = new ScriptedTerminal(Line(Sentinel));
            PromptResult<SecretText> Result = new InteractivePrompts(Terminal).Secret("SMTP password");

            //boolean checks first: a failure must not print the secret
            Assert.That(Terminal.AllText.Contains(Sentinel), Is.False, "nothing written holds the secret");
            Assert.That(Result.Value?.ToString() == "[hidden]", Is.True, "a secret's ToString is [hidden]");
            Assert.That($"{Result.Value}" == "[hidden]", Is.True, "also in an interpolated string");
            Assert.That(Result.ToString() == "Answered", Is.True);
            Assert.That(Result.End, Is.EqualTo(PromptEnd.Answered));
            Assert.That(Terminal.SecretReads, Is.EqualTo(1));
            Assert.That(Terminal.Reads, Is.Zero, "a secret is never read as a line that is shown");
            Assert.That(Result.Value!.Reveal() == Sentinel, Is.True);
            Assert.That(Result.Value.Length, Is.EqualTo(Sentinel.Length));
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.EqualTo(new[] { "SMTP password (not shown as you type): " }));
        }

        [Test]
        public void Secret_AnEmptyAnswer_IsAskedAgain()
        {
            var Terminal = new ScriptedTerminal(Line(""), Line(Sentinel));
            PromptResult<SecretText> Result = new InteractivePrompts(Terminal).Secret("Token");

            Assert.That(Terminal.AllText.Contains(Sentinel), Is.False);
            Assert.That(Result.IsAnswered, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("Nothing was typed. Type it, or press Esc to cancel."));
        }

        [Test]
        public void Secret_CanBeCancelled_OrEndedByTheInput_AndKeepsNothing()
        {
            PromptResult<SecretText> Cancelled = new InteractivePrompts(new ScriptedTerminal(Esc)).Secret("Token");
            Assert.That(Cancelled.End, Is.EqualTo(PromptEnd.Cancelled));
            Assert.That(Cancelled.Value == null, Is.True);
            Assert.That(new InteractivePrompts(new ScriptedTerminal(CtrlC)).Secret("Token").End, Is.EqualTo(PromptEnd.Interrupted));
            PromptResult<SecretText> Ended = new InteractivePrompts(new ScriptedTerminal()).Secret("Token");
            Assert.That(Ended.End, Is.EqualTo(PromptEnd.EndOfInput));
            Assert.That(Ended.Value == null, Is.True);
        }

        [Test]
        public void Secret_OnATerminalThatCantHideIt_ReadsNothing_AndSaysSo_NeverPretendingToHideIt()
        {
            var Terminal = new ScriptedTerminal(Line(Sentinel)) { Hides = false };
            PromptResult<SecretText> Result = new InteractivePrompts(Terminal).Secret("SMTP password");

            Assert.That(Terminal.AllText.Contains(Sentinel), Is.False);
            Assert.That(Result.End, Is.EqualTo(PromptEnd.Unavailable));
            Assert.That(Result.Value == null, Is.True);
            Assert.That(Terminal.Reads + Terminal.SecretReads, Is.Zero, "nothing was read");
            Assert.That(Terminal.Left, Is.EqualTo(1));
            Assert.That(Terminal.OfKind(ScriptedTerminal.PromptKind), Is.Empty, "no question that claims the answer is hidden");
            Assert.That(Terminal.Texts, Does.Contain("This needs a terminal that can hide what you type, and this one can't, so nothing was read."));
        }

        [Test]
        public void Secret_ATerminalThatAnswersUnavailable_IsToldTheSameWay()
        {
            var Terminal = new UnavailableSecrets();
            PromptResult<SecretText> Result = new InteractivePrompts(Terminal).Secret("Token");

            Assert.That(Result.End, Is.EqualTo(PromptEnd.Unavailable));
            Assert.That(Terminal.Texts, Does.Contain("This needs a terminal that can hide what you type, and this one can't, so nothing was read."));
        }

        // a terminal that says it can hide input, but whose secret read answers Unavailable
        private sealed class UnavailableSecrets : InteractiveTerminal
        {
            internal List<string> Texts { get; } = new();
            public override int Width => int.MaxValue;
            public override bool CanHideInput => true;
            public override void WriteLine(string Text) => Texts.Add(Text);
            public override void WriteHeading(string Text) => Texts.Add(Text);
            public override void WriteWarning(string Text) => Texts.Add(Text);
            public override void WriteError(string Text) => Texts.Add(Text);
            public override void WritePrompt(string Text) => Texts.Add(Text);
            public override void WriteStopped() => Texts.Add("Stopped.");
            public override TerminalInput ReadLine() => TerminalInput.EndOfInput;
            public override TerminalInput ReadSecret() => TerminalInput.Unavailable;
        }

        [TestCase("7", 9, true, 7)]
        [TestCase("10", 9, false, 0)]
        [TestCase("", 9, false, 0)]
        [TestCase("٣", 9, false, 0)]
        public void TryNumber_TakesAsciiDigitsInRangeOnly(string Text, int Count, bool Ok, int Value)
        {
            Assert.That(InteractivePrompts.TryNumber(Text, Count, out int Number), Is.EqualTo(Ok));
            if (Ok)
            {
                Assert.That(Number, Is.EqualTo(Value));
            }
        }
    }
}
