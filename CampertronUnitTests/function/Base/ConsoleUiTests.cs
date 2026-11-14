using System.Reflection;
using System.Runtime.InteropServices;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // ConsoleUi is the console's presenter: which stream a message goes to, when color and screen control are used,
    // and how text is wrapped. The tests describe the terminal through ConsoleUi.Current and record color changes
    // through ConsoleUi.ApplyColor; nothing depends on the terminal the tests run in. No files, no network.
    public class ConsoleUiTests
    {
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private Action<ConsoleColor?> OriginalApplyColor = null!;
        private Func<DateTime> OriginalNow = null!;
        private bool OriginalUnacknowledged;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private List<string> Colors = null!;

        private static readonly ConsoleUi.Capabilities Plain = new ConsoleUi.Capabilities();
        private static ConsoleUi.Capabilities Terminal(int Width = 80, bool Color = true, bool Control = true, bool Input = true, bool ErrorTerminal = true) =>
            new ConsoleUi.Capabilities { OutputIsTerminal = true, InputIsTerminal = Input, ErrorIsTerminal = ErrorTerminal, ControlSequences = Control, Color = Color, Width = Width };

        [SetUp]
        public void Setup()
        {
            OriginalCapabilities = ConsoleUi.Current;
            OriginalApplyColor = ConsoleUi.ApplyColor;
            OriginalNow = ConsoleUi.Now;
            OriginalUnacknowledged = ConsoleUi.Unacknowledged;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            Colors = new List<string>();
            // color changes are recorded in the stream they surround, so their position can be asserted
            ConsoleUi.ApplyColor = Color =>
            {
                Colors.Add(Color?.ToString() ?? "reset");
                Out.Write(Color.HasValue ? "<" + Color + ">" : "</>");
            };
            ConsoleUi.Current = Plain;
            ConsoleUi.Unacknowledged = false;
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Err.Dispose();
            ConsoleUi.Current = OriginalCapabilities;
            ConsoleUi.ApplyColor = OriginalApplyColor;
            ConsoleUi.Now = OriginalNow;
            ConsoleUi.Unacknowledged = OriginalUnacknowledged;
        }

        private static string Lf(string Text) => Text.Replace(Environment.NewLine, "\n");

        // ---- capability detection ----

        [Test]
        public void Detect_RedirectedOutput_IsPlain_NeverWrapped_NotInteractive()
        {
            ConsoleUi.Capabilities Caps = ConsoleUi.Detect(true, false, false, null, "xterm-256color", false, () => 120);

            Assert.That(Caps.OutputIsTerminal, Is.False);
            Assert.That(Caps.Color, Is.False);
            Assert.That(Caps.ControlSequences, Is.False);
            Assert.That(Caps.Interactive, Is.False);
            Assert.That(Caps.Width, Is.EqualTo(int.MaxValue), "redirected output is never wrapped");
        }

        [Test]
        public void Detect_Terminal_UsesColorControlAndItsWidth()
        {
            ConsoleUi.Capabilities Caps = ConsoleUi.Detect(false, false, false, null, "xterm-256color", false, () => 132);

            Assert.That(Caps.Color, Is.True);
            Assert.That(Caps.ControlSequences, Is.True);
            Assert.That(Caps.Interactive, Is.True);
            Assert.That(Caps.ErrorIsTerminal, Is.True);
            Assert.That(Caps.Width, Is.EqualTo(132));
        }

        [TestCase("1", false)]
        [TestCase("true", false)]
        [TestCase("0", false, Description = "any non-empty value turns color off")]
        [TestCase("", true, Description = "an empty NO_COLOR is not set")]
        [TestCase(null, true)]
        public void Detect_NoColor(string? NoColor, bool ExpectColor)
        {
            ConsoleUi.Capabilities Caps = ConsoleUi.Detect(false, false, false, NoColor, "xterm", false, () => 80);

            Assert.That(Caps.Color, Is.EqualTo(ExpectColor));
            Assert.That(Caps.ControlSequences, Is.True, "NO_COLOR is about color only");
        }

        [TestCase("dumb", false, false)]
        [TestCase("DUMB", false, false)]
        [TestCase(null, false, false, Description = "a Unix terminal without TERM has no known capabilities")]
        [TestCase("", false, false)]
        [TestCase(null, true, true, Description = "Windows consoles have no TERM")]
        [TestCase("dumb", true, false)]
        [TestCase("vt100", false, true)]
        public void Detect_Term(string? Term, bool IsWindows, bool ExpectControl)
        {
            ConsoleUi.Capabilities Caps = ConsoleUi.Detect(false, false, false, null, Term, IsWindows, () => 80);

            Assert.That(Caps.ControlSequences, Is.EqualTo(ExpectControl));
            Assert.That(Caps.Color, Is.EqualTo(ExpectControl));
            Assert.That(Caps.OutputIsTerminal, Is.True);
        }

        [Test]
        public void Detect_RedirectedInput_IsNotInteractive()
        {
            Assert.That(ConsoleUi.Detect(false, true, false, null, "xterm", false, () => 80).Interactive, Is.False);
            Assert.That(ConsoleUi.Detect(false, false, true, null, "xterm", false, () => 80).ErrorIsTerminal, Is.False);
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(19)]
        [TestCase(501)]
        [TestCase(int.MaxValue)]
        public void Detect_UnusableWidth_FallsBackTo80(int Reported)
        {
            Assert.That(ConsoleUi.Detect(false, false, false, null, "xterm", false, () => Reported).Width, Is.EqualTo(80));
        }

        [Test]
        public void Detect_WidthThatThrows_FallsBackTo80()
        {
            Assert.That(ConsoleUi.Detect(false, false, false, null, "xterm", false, () => throw new IOException("no console")).Width, Is.EqualTo(80));
        }

        [TestCase(20)]
        [TestCase(40)]
        [TestCase(500)]
        public void Detect_UsableWidth_IsUsed(int Reported)
        {
            Assert.That(ConsoleUi.Detect(false, false, false, null, "xterm", false, () => Reported).Width, Is.EqualTo(Reported));
        }

        // ---- streams ----

        [Test]
        public void ResultsStatusNoticesAndLoopLines_GoToStandardOutput()
        {
            ConsoleUi.Now = () => new DateTime(2026, 11, 14, 15, 2, 4);
            ConsoleUi.Line("a line");
            ConsoleUi.Status("a status");
            ConsoleUi.Notice("a notice");
            ConsoleUi.LoopLine("a check");
            ConsoleUi.Write(new[] { new ConsoleUi.StyledLine("styled", ConsoleColor.Green) });

            Assert.That(Lf(Out.ToString()), Is.EqualTo("a line\na status\na notice\n2026-11-14 15:02:04  a check\nstyled\n"));
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void WarningsAndErrors_GoToStandardError_WithATextPrefix()
        {
            ConsoleUi.Warning("something is odd.");
            ConsoleUi.Error("something failed.", "File: /x/General.yaml", "first\nsecond");

            Assert.That(Lf(Err.ToString()), Is.EqualTo(
                "Warning: something is odd.\n" +
                "Error: something failed.\n" +
                "  File: /x/General.yaml\n" +
                "  first\n" +
                "  second\n"));
            Assert.That(Out.ToString(), Is.Empty, "nothing of a warning or error on standard output");
        }

        // ---- color ----

        [Test]
        public void Color_IsUsedOnlyWhenTheTerminalHasIt_AndIsResetAfterItsText()
        {
            ConsoleUi.Line("plain", ConsoleColor.Green);
            Assert.That(Colors, Is.Empty, "no color change without a color terminal");
            Assert.That(Lf(Out.ToString()), Is.EqualTo("plain\n"));

            Out.GetStringBuilder().Clear();
            ConsoleUi.Current = Terminal();
            ConsoleUi.Write(new[] { new ConsoleUi.StyledLine("title", ConsoleColor.Green).Add(" rest") });

            Assert.That(Lf(Out.ToString()), Is.EqualTo("<Green>title</> rest\n"), "set before, reset right after the colored text");
            Assert.That(Colors, Is.EqualTo(new[] { "Green", "reset" }));
        }

        [Test]
        public void Color_IsReset_EvenWhenWritingFails()
        {
            ConsoleUi.Current = Terminal();
            var Failing = new StringWriter();
            Failing.Dispose();
            Console.SetOut(Failing);

            Assert.That(() => ConsoleUi.Line("text", ConsoleColor.Red), Throws.InstanceOf<ObjectDisposedException>());
            Assert.That(Colors, Is.EqualTo(new[] { "Red", "reset" }));
        }

        [Test]
        public void DiagnosticColor_NeedsStandardErrorOnTheTerminalToo()
        {
            ConsoleUi.Current = Terminal(ErrorTerminal: false);
            ConsoleUi.Warning("redirected error stream");
            Assert.That(Colors, Is.Empty);

            ConsoleUi.Current = Terminal();
            ConsoleUi.Warning("both on the terminal");
            Assert.That(Colors, Is.EqualTo(new[] { "DarkYellow", "reset" }));
            ConsoleUi.Error("bad");
            Assert.That(Colors.Skip(2), Is.EqualTo(new[] { "Red", "reset" }));
            // the prefix carries the meaning; it is there with and without color
            Assert.That(Lf(Err.ToString()), Is.EqualTo("Warning: redirected error stream\nWarning: both on the terminal\nError: bad\n"));
        }

        // ---- screen control ----

        [Test]
        public void ClearScreen_OnAnInteractiveTerminal_ClearsTheVisibleScreenOnly()
        {
            ConsoleUi.Current = Terminal();

            Assert.That(ConsoleUi.ClearScreen(), Is.True);
            Assert.That(Out.ToString(), Is.EqualTo("\u001b[H\u001b[2J"));
            // ordinal checks: a culture-sensitive search ignores control characters
            Assert.That(Out.ToString().Contains("\u001bc", StringComparison.Ordinal), Is.False, "no terminal reset");
            Assert.That(Out.ToString().Contains("[3J", StringComparison.Ordinal), Is.False, "the scrollback is not erased");
            Assert.That(Out.ToString().Contains('\f'), Is.False);
        }

        [Test]
        public void ClearScreen_WritesNothing_WhenOutputIsRedirected_NotInteractive_OrDumb()
        {
            foreach (ConsoleUi.Capabilities Caps in new[] { Plain, Terminal(Input: false), Terminal(Control: false, Color: false) })
            {
                ConsoleUi.Current = Caps;
                Assert.That(ConsoleUi.ClearScreen(), Is.False);
            }
            Assert.That(Out.ToString(), Is.Empty);
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void ClearScreen_NeverClearsOverAnUnreadMessage()
        {
            ConsoleUi.Current = Terminal();
            ConsoleUi.Notice("The previous database was kept as backup.");
            Assert.That(ConsoleUi.Unacknowledged, Is.True);
            Out.GetStringBuilder().Clear();

            Assert.That(ConsoleUi.ClearScreen(), Is.False);
            Assert.That(Out.ToString(), Is.Empty);

            ConsoleUi.Unacknowledged = false;
            Assert.That(ConsoleUi.ClearScreen(), Is.True);
        }

        [Test]
        public void WarningsErrorsAndNotices_StayUnacknowledged_UntilAKeyIsWaitedFor()
        {
            ConsoleUi.Warning("w");
            Assert.That(ConsoleUi.Unacknowledged, Is.True);
            ConsoleUi.Unacknowledged = false;
            ConsoleUi.Error("e");
            Assert.That(ConsoleUi.Unacknowledged, Is.True);
            ConsoleUi.Unacknowledged = false;
            ConsoleUi.Line("status is not a notice");
            ConsoleUi.Status("neither");
            Assert.That(ConsoleUi.Unacknowledged, Is.False);
        }

        [Test]
        public void WaitForKey_WithoutAnInteractiveTerminal_NeitherPromptsNorBlocks()
        {
            ConsoleUi.Notice("n");
            Out.GetStringBuilder().Clear();

            ConsoleUi.WaitForKey("Press any key to continue...");
            ConsoleUi.Notice("again");
            ConsoleUi.AcknowledgePending();

            Assert.That(Lf(Out.ToString()), Is.EqualTo("again\n"), "no prompt in a log");
            Assert.That(ConsoleUi.Unacknowledged, Is.False);
        }

        [Test]
        public void HideCursorAndRestore_DoNothing_WithoutAnInteractiveTerminal()
        {
            ConsoleUi.HideCursor();
            ConsoleUi.RestoreTerminal();

            Assert.That(Out.ToString(), Is.Empty);
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That(Colors, Is.Empty);
        }

        // ---- wrapping ----

        [Test]
        public void Wrap_BreaksAtSpaces_IndentsContinuations_AndKeepsTheTextsOwnIndent()
        {
            Assert.That(ConsoleUi.Wrap("  one two three four five", 12, "    "), Is.EqualTo(new[] { "  one two", "    three", "    four", "    five" }));
            Assert.That(ConsoleUi.Wrap("short", 12, "  "), Is.EqualTo(new[] { "short" }));
            Assert.That(ConsoleUi.Wrap("exactly 12 c", 12, "  "), Is.EqualTo(new[] { "exactly 12 c" }));
        }

        [Test]
        public void Wrap_NeverBreaksAWord_SoLinksAndPathsStayWhole()
        {
            const string Url = "https://www.recreation.gov/camping/campsites/10206585";
            List<string> Lines = ConsoleUi.Wrap("Book it at " + Url + " today", 20, "  ");

            Assert.That(Lines, Is.EqualTo(new[] { "Book it at", "  " + Url, "  today" }));
        }

        [Test]
        public void Wrap_WithoutAWidth_ReturnsTheTextUnchanged()
        {
            string Long = string.Join(" ", Enumerable.Repeat("word", 200));
            Assert.That(ConsoleUi.Wrap(Long, int.MaxValue, "  "), Is.EqualTo(new[] { Long }));
        }

        [Test]
        public void StatusAndWarnings_AreWrappedOnATerminal_AndNotInALog()
        {
            const string Text = "the request to recreation.gov for WATCHMAN CAMPGROUND, November 2026 failed (attempt 2: Connection refused); retrying in 30 seconds.";
            ConsoleUi.Warning(Text);
            ConsoleUi.Status("Checked \"Zion non-group tent friendly sites\" at WATCHMAN CAMPGROUND: November 2026, December 2026 (0.2s)");
            Assert.That(Lf(Err.ToString()), Is.EqualTo("Warning: " + Text + "\n"), "one line in a log");
            Assert.That(Lf(Out.ToString()).Count(c => c == '\n'), Is.EqualTo(1));

            Out.GetStringBuilder().Clear();
            Err.GetStringBuilder().Clear();
            ConsoleUi.Current = Terminal(Width: 60, Color: false);
            ConsoleUi.Warning(Text);
            ConsoleUi.Status("Checked \"Zion non-group tent friendly sites\" at WATCHMAN CAMPGROUND: November 2026, December 2026 (0.2s)");

            Assert.That(Lf(Err.ToString()), Is.EqualTo(
                "Warning: the request to recreation.gov for WATCHMAN\n" +
                "  CAMPGROUND, November 2026 failed (attempt 2: Connection\n" +
                "  refused); retrying in 30 seconds.\n"));
            Assert.That(Lf(Out.ToString()), Is.EqualTo(
                "Checked \"Zion non-group tent friendly sites\" at WATCHMAN\n" +
                "  CAMPGROUND: November 2026, December 2026 (0.2s)\n"));
            Assert.That(Lf(Err.ToString() + Out.ToString()).Split('\n').All(p => p.Length <= 60), Is.True);
        }

        // ---- identity ----

        [Test]
        public void ProgramIdentity_ComesFromTheVersionReport()
        {
            Assembly Library = typeof(RuntimeInfo).Assembly;
            string[] Version = RuntimeInfo.Describe(Library).Split(Environment.NewLine);

            Assert.That(ConsoleUi.ProgramIdentity(Library), Is.EqualTo(Version[0] + " | " + RuntimeInformation.FrameworkDescription + " (" + RuntimeInformation.ProcessArchitecture + ")"));
            Assert.That(ConsoleUi.ProgramIdentity(Library), Does.StartWith("CampertronLibrary "));
        }

        [Test]
        public void Count_UsesSingularAndPlural()
        {
            Assert.That(ConsoleUi.Count(1, "search", "searches"), Is.EqualTo("1 search"));
            Assert.That(ConsoleUi.Count(0, "search", "searches"), Is.EqualTo("0 searches"));
            Assert.That(ConsoleUi.Count(2, "search", "searches"), Is.EqualTo("2 searches"));
        }
    }
}
