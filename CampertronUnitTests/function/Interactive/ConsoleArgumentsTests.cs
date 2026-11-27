using CampertronLibrary.function.Base;
using CampertronLibrary.function.Interactive;

namespace CampertronUnitTests.function.Interactive
{
    // CampertronConsole's own arguments (ConsoleArguments; v0.4.0's phase 6, D115, D121): --interactive and --help, each a
    // whole command line; every other argument still leaves the start as it always was. The decision is made from the
    // arguments alone: no terminal, environment variable, file or folder takes part in it.
    [NonParallelizable]
    public class ConsoleArgumentsTests
    {
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;

        [SetUp]
        public void Setup()
        {
            OriginalCapabilities = ConsoleUi.Current;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Err.Dispose();
            ConsoleUi.Current = OriginalCapabilities;
        }

        [Test]
        public void NoArguments_IsTheStartAsBefore()
        {
            Assert.That(ConsoleArguments.Parse(Array.Empty<string>()).Command, Is.EqualTo(ConsoleCommand.Start));
            Assert.That(ConsoleArguments.Parse(null).Command, Is.EqualTo(ConsoleCommand.Start));
            Assert.That(ConsoleArguments.Parse(Array.Empty<string>()).Error, Is.Null);
        }

        [TestCase("anything")]
        [TestCase("-h")]
        [TestCase("/?")]
        [TestCase("-interactive")]
        [TestCase("--Interactive")]
        [TestCase("--INTERACTIVE")]
        [TestCase("--HELP")]
        [TestCase("interactive")]
        [TestCase("--interactiveX")]
        [TestCase("--version", "extra")]
        [TestCase("one", "two", "three")]
        [TestCase("")]
        public void ArgumentsTheConsoleDoesNotRead_AreStillIgnored(params string[] Arguments)
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(Arguments);

            Assert.That(Parsed.Command, Is.EqualTo(ConsoleCommand.Start), "the start without arguments, as before (D115)");
            Assert.That(Parsed.Error, Is.Null);
        }

        [Test]
        public void Interactive_AloneOpensTheInteractiveConsole()
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(new[] { "--interactive" });

            Assert.That(Parsed.Command, Is.EqualTo(ConsoleCommand.Interactive));
            Assert.That(Parsed.Error, Is.Null);
        }

        [Test]
        public void Help_AloneShowsTheHelp()
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(new[] { "--help" });

            Assert.That(Parsed.Command, Is.EqualTo(ConsoleCommand.Help));
            Assert.That(Parsed.Error, Is.Null);
        }

        [TestCase("--interactive", "--interactive")]
        [TestCase("--interactive", "extra")]
        [TestCase("extra", "--interactive")]
        [TestCase("--interactive", "--version")]
        [TestCase("--version", "--interactive")]
        [TestCase("--interactive", "--help")]
        [TestCase("--help", "--interactive")]
        [TestCase("--interactive=yes")]
        [TestCase("--interactive=")]
        public void Interactive_WithAValueOrAnotherArgument_IsRefused(params string[] Arguments)
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(Arguments);

            Assert.That(Parsed.Command, Is.EqualTo(ConsoleCommand.Refused));
            Assert.That(Parsed.Error, Is.EqualTo("--interactive opens the interactive console and takes no value and no other argument."));
        }

        [TestCase("--help", "--help")]
        [TestCase("--help", "extra")]
        [TestCase("-h", "--help")]
        [TestCase("--help", "--version")]
        [TestCase("--help=all")]
        public void Help_WithAValueOrAnotherArgument_IsRefused(params string[] Arguments)
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(Arguments);

            Assert.That(Parsed.Command, Is.EqualTo(ConsoleCommand.Refused));
            Assert.That(Parsed.Error, Is.EqualTo("--help shows how to start CampertronConsole and takes no value and no other argument."));
        }

        [Test]
        public void ARefusal_NeverRepeatsAnArgumentsValue()
        {
            ConsoleArguments Parsed = ConsoleArguments.Parse(new[] { "--interactive=MARKER-SECRET", "MARKER-OTHER" });

            Assert.That(Parsed.Error, Does.Not.Contain("MARKER"));
            Assert.That(ConsoleArguments.Usage, Does.Not.Contain("MARKER"));
        }

        [Test]
        public void TheUsageLine_NamesEveryWayToStart()
        {
            string Usage = ConsoleArguments.Usage;
            foreach (string Start in new[] { "--interactive", "--web", "--healthcheck", "--version", "--help" })
            {
                Assert.That(Usage, Does.Contain(Start));
            }
            Assert.That(Usage, Does.StartWith("Usage: CampertronConsole ["));
            Assert.That(Usage, Does.Contain("without an argument it starts as before"));
        }

        [Test]
        public void TheHelp_DescribesEveryStart_TheNoArgumentStartFirst()
        {
            string Help = string.Join("\n", ConsoleArguments.HelpLines);

            Assert.That(ConsoleArguments.HelpLines[0], Is.EqualTo("Usage:"));
            Assert.That(ConsoleArguments.HelpLines[1], Is.EqualTo("  CampertronConsole"));
            Assert.That(ConsoleArguments.HelpLines[2], Does.Contain("Starts as it always has: General.yaml's outputTo decides"));
            Assert.That(ConsoleArguments.HelpLines, Does.Contain("  CampertronConsole --interactive"));
            Assert.That(ConsoleArguments.HelpLines, Does.Contain("  CampertronConsole --web [--listen <address>] [--port <port>]"));
            Assert.That(ConsoleArguments.HelpLines, Does.Contain("  CampertronConsole --healthcheck [--listen <address>] [--port <port>]"));
            Assert.That(ConsoleArguments.HelpLines, Does.Contain("  CampertronConsole --version"));
            Assert.That(ConsoleArguments.HelpLines, Does.Contain("  CampertronConsole --help"));
            Assert.That(Help, Does.Contain("It needs a terminal, and it starts nothing by itself."));
            //nothing that a later phase has not built yet is offered
            foreach (string Later in new[] { "Beam", "Export", "Import", "guided", "Guided", "search files", "refresh" })
            {
                Assert.That(Help, Does.Not.Contain(Later));
            }
        }

        [Test]
        public void TheHelp_IsWrappedAtATerminalsWidth_KeepingEachDescriptionsIndentation_AndNeverWhenRedirected()
        {
            ConsoleUi.Current = new ConsoleUi.Capabilities { OutputIsTerminal = true, InputIsTerminal = true, ErrorIsTerminal = true, Width = 40 };
            ConsoleArguments.WriteHelp();
            string[] Narrow = Out.ToString().Replace(Environment.NewLine, "\n").TrimEnd('\n').Split('\n');

            Assert.That(Narrow.Where(L => L.Length > 40 && L.Trim().Contains(' ')), Is.Empty, "only a single long word can pass the width");
            Assert.That(Narrow.Where(L => L.StartsWith("      ", StringComparison.Ordinal)).Count(), Is.GreaterThan(ConsoleArguments.HelpLines.Count / 2));
            Assert.That(string.Join(" ", Narrow.Select(L => L.Trim())), Is.EqualTo(string.Join(" ", ConsoleArguments.HelpLines.Select(L => L.Trim()))), "the same words");
            Assert.That(Err.ToString(), Is.Empty);

            Out.GetStringBuilder().Clear();
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            ConsoleArguments.WriteHelp();
            Assert.That(Out.ToString().Replace(Environment.NewLine, "\n"), Is.EqualTo(string.Join("\n", ConsoleArguments.HelpLines) + "\n"), "redirected: the lines as they are");
            Assert.That(Out.ToString().Contains('\u001b'), Is.False, "no escape character (an ordinal check)");
        }
    }
}
