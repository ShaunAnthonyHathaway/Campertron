using System.Text;
using CampertronLibrary.function.Interactive;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's line editor (KeyLineReader): an answer read key by key, as a terminal gives the keys
    // (Console.ReadKey: the character, or '\0' for a key without one). Scripted keys and the text written back are all a
    // test needs: no terminal, no file. The secret's sentinel is never put into an assertion's message.
    public class KeyLineReaderTests
    {
        private const string Sentinel = "S3ntinel-Secret-4f1c";

        private static ConsoleKeyInfo Char(char C) => new ConsoleKeyInfo(C, ConsoleKey.NoName, false, false, false);
        private static ConsoleKeyInfo Key(ConsoleKey K, char C = '\0', bool Control = false) => new ConsoleKeyInfo(C, K, false, false, Control);
        private static readonly ConsoleKeyInfo Enter = Key(ConsoleKey.Enter, '\r');
        private static readonly ConsoleKeyInfo Escape = Key(ConsoleKey.Escape, '\u001b');
        private static readonly ConsoleKeyInfo CtrlC = Key(ConsoleKey.C, '\u0003', true);
        private static readonly ConsoleKeyInfo CtrlD = Key(ConsoleKey.D, '\u0004', true);
        private static readonly ConsoleKeyInfo CtrlZ = Key(ConsoleKey.Z, '\u001a', true);
        // Unix terminals send DEL (127) for Backspace, Windows and some terminals BS (8)
        private static readonly ConsoleKeyInfo Backspace = Key(ConsoleKey.Backspace, '\u007f');
        private static readonly ConsoleKeyInfo BackspaceBs = Key(ConsoleKey.Backspace, '\b', true);

        private static IEnumerable<ConsoleKeyInfo> Typed(string Text) => Text.Select(Char);

        // reads one answer from Keys; Echo holds what was written back; Used: how many keys were read
        private static TerminalInput Read(IEnumerable<ConsoleKeyInfo> Keys, out string Echo, out int Used, bool Hidden = false, bool CtrlZEndsInput = false)
        {
            var Queue = new Queue<ConsoleKeyInfo>(Keys);
            var Written = new StringBuilder();
            int Count = 0;
            TerminalInput Result = KeyLineReader.Read(() =>
            {
                if (Queue.Count == 0)
                {
                    return null;
                }
                Count++;
                return Queue.Dequeue();
            }, T => Written.Append(T), Hidden, CtrlZEndsInput);
            Echo = Written.ToString();
            Used = Count;
            return Result;
        }

        [Test]
        public void Enter_EndsTheLine_WhatWasTypedIsWrittenBack()
        {
            TerminalInput Result = Read(Typed("12").Append(Enter), out string Echo, out _);

            Assert.That(Result.Kind, Is.EqualTo(TerminalInputKind.Line));
            Assert.That(Result.Text, Is.EqualTo("12"));
            Assert.That(Echo, Is.EqualTo("12" + Environment.NewLine));
        }

        [Test]
        public void Enter_Alone_IsAnEmptyLine()
        {
            TerminalInput Result = Read(new[] { Enter }, out string Echo, out _);

            Assert.That(Result.Kind, Is.EqualTo(TerminalInputKind.Line));
            Assert.That(Result.Text, Is.Empty);
            Assert.That(Echo, Is.EqualTo(Environment.NewLine));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Backspace_RemovesTheLastCharacter_AndRubsItOutWithoutAnEscapeSequence(bool Del)
        {
            ConsoleKeyInfo Rub = Del ? Backspace : BackspaceBs;
            TerminalInput Result = Read(Typed("abc").Append(Rub).Append(Rub).Concat(Typed("x")).Append(Enter), out string Echo, out _);

            Assert.That(Result.Text, Is.EqualTo("ax"));
            Assert.That(Echo, Is.EqualTo("abc" + KeyLineReader.RubOut + KeyLineReader.RubOut + "x" + Environment.NewLine));
            Assert.That(Echo.Contains('\u001b'), Is.False, "no escape character (an ordinal check: a culture's comparison ignores it)");
        }

        [Test]
        public void Backspace_OnAnEmptyLine_DoesNothing()
        {
            TerminalInput Result = Read(new[] { Backspace, Backspace }.Concat(Typed("1")).Append(Enter), out string Echo, out _);

            Assert.That(Result.Text, Is.EqualTo("1"));
            Assert.That(Echo, Is.EqualTo("1" + Environment.NewLine));
        }

        [Test]
        public void Backspace_RemovesACharacterOutsideTheBasicPlane_Whole()
        {
            string Tent = char.ConvertFromUtf32(0x26FA) + char.ConvertFromUtf32(0x1F3D5);
            TerminalInput Result = Read(Typed(Tent).Append(Backspace).Append(Enter), out _, out _);

            Assert.That(Result.Text, Is.EqualTo(char.ConvertFromUtf32(0x26FA)));
        }

        [Test]
        public void Escape_EndsTheReadWithoutAnAnswer_AndReadsNoFurtherKey()
        {
            TerminalInput Result = Read(Typed("12").Append(Escape).Concat(Typed("3")).Append(Enter), out string Echo, out int Used);

            Assert.That(Result.Kind, Is.EqualTo(TerminalInputKind.Escape));
            Assert.That(Result.Text, Is.Empty);
            Assert.That(Used, Is.EqualTo(3));
            Assert.That(Echo, Is.EqualTo("12" + Environment.NewLine));
        }

        [Test]
        public void CtrlC_ReadAsAKey_InterruptsTheRead()
        {
            TerminalInput Result = Read(Typed("12").Append(CtrlC).Append(Enter), out string Echo, out int Used);

            Assert.That(Result.Kind, Is.EqualTo(TerminalInputKind.Interrupt));
            Assert.That(Used, Is.EqualTo(3));
            Assert.That(Echo, Is.EqualTo("12" + Environment.NewLine));
        }

        [Test]
        public void CtrlD_OnAnEmptyLine_IsTheEndOfTheInput_ElsewhereItIsIgnored()
        {
            Assert.That(Read(new[] { CtrlD }, out string Echo, out _).Kind, Is.EqualTo(TerminalInputKind.EndOfInput));
            Assert.That(Echo, Is.EqualTo(Environment.NewLine));

            TerminalInput Typing = Read(Typed("ab").Append(CtrlD).Append(Enter), out _, out _);
            Assert.That(Typing.Kind, Is.EqualTo(TerminalInputKind.Line));
            Assert.That(Typing.Text, Is.EqualTo("ab"));
        }

        [Test]
        public void CtrlZ_IsTheEndOfTheInputOnlyWhereItIsTheEndOfFileKey()
        {
            Assert.That(Read(new[] { CtrlZ }, out _, out _, CtrlZEndsInput: true).Kind, Is.EqualTo(TerminalInputKind.EndOfInput), "Windows");
            TerminalInput Unix = Read(new[] { CtrlZ }.Concat(Typed("1")).Append(Enter), out _, out _, CtrlZEndsInput: false);
            Assert.That(Unix.Kind, Is.EqualTo(TerminalInputKind.Line));
            Assert.That(Unix.Text, Is.EqualTo("1"));
        }

        [Test]
        public void AnInputThatEnded_IsTheEndOfTheInput_AlsoInTheMiddleOfALine()
        {
            Assert.That(Read(Array.Empty<ConsoleKeyInfo>(), out _, out _).Kind, Is.EqualTo(TerminalInputKind.EndOfInput));
            Assert.That(Read(Typed("12"), out _, out _).Kind, Is.EqualTo(TerminalInputKind.EndOfInput));
        }

        [Test]
        public void KeysWithoutACharacter_AndOtherControlCharacters_AreIgnored_NoCursorMoves()
        {
            var Keys = new List<ConsoleKeyInfo>
            {
                Key(ConsoleKey.LeftArrow), Key(ConsoleKey.UpArrow), Key(ConsoleKey.Home), Key(ConsoleKey.F1), Key(ConsoleKey.Delete),
                Key(ConsoleKey.Tab, '\t'), Char('\u0007'), Char('\u0001'), Char('\u0015'), Char('\u0017'), Char('\u001a')
            };
            TerminalInput Result = Read(Typed("a").Concat(Keys).Concat(Typed("b")).Append(Enter), out string Echo, out _);

            Assert.That(Result.Text, Is.EqualTo("ab"));
            Assert.That(Echo, Is.EqualTo("ab" + Environment.NewLine));
        }

        [Test]
        public void ALine_HasAtMostMaxLengthCharacters_TheRestIsIgnored()
        {
            TerminalInput Result = Read(Enumerable.Repeat(Char('x'), KeyLineReader.MaxLength + 500).Append(Enter), out string Echo, out int Used);

            Assert.That(Result.Text, Has.Length.EqualTo(KeyLineReader.MaxLength));
            Assert.That(Used, Is.EqualTo(KeyLineReader.MaxLength + 501), "every key is read, so none is left for the next question");
            Assert.That(Echo, Has.Length.EqualTo(KeyLineReader.MaxLength + Environment.NewLine.Length));
        }

        [Test]
        public void HiddenEntry_WritesBackNothingOfTheSecret_NotEvenAMask_OnlyTheLineBreak()
        {
            TerminalInput Result = Read(Typed(Sentinel + "x").Append(Backspace).Append(Enter), out string Echo, out _, Hidden: true);

            //boolean checks only: a failure must not print the secret
            Assert.That(Echo == Environment.NewLine, Is.True, "no character, no mask and no rub-out: only the line break");
            Assert.That(Result.Kind, Is.EqualTo(TerminalInputKind.Line));
            Assert.That(Result.Text == Sentinel, Is.True, "the secret is read as it was typed, without the removed character");
            Assert.That(Result.ToString() == "Line (" + Sentinel.Length + " characters)", Is.True, "a read's text is never in its ToString");
        }

        [Test]
        public void HiddenEntry_CanBeCancelled_OrEndedByTheInput_WithoutAnAnswer()
        {
            TerminalInput Cancelled = Read(Typed(Sentinel).Append(Escape), out string Echo, out _, Hidden: true);
            Assert.That(Echo.Contains(Sentinel), Is.False);
            Assert.That(Cancelled.Kind, Is.EqualTo(TerminalInputKind.Escape));
            Assert.That(Cancelled.Text.Length, Is.Zero);

            TerminalInput Interrupted = Read(Typed(Sentinel).Append(CtrlC), out _, out _, Hidden: true);
            Assert.That(Interrupted.Kind, Is.EqualTo(TerminalInputKind.Interrupt));
            Assert.That(Interrupted.Text.Length, Is.Zero);

            TerminalInput Ended = Read(Typed(Sentinel), out _, out _, Hidden: true);
            Assert.That(Ended.Kind, Is.EqualTo(TerminalInputKind.EndOfInput));
            Assert.That(Ended.Text.Length, Is.Zero);
        }
    }
}
