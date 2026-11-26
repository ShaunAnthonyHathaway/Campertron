namespace CampertronWebTests
{
    // CampertronConsole's start as a process (D115): without arguments, or with arguments it does not read, it starts as it
    // always did, also on a terminal: General.yaml's outputTo decides, and the interactive console is never chosen because
    // a terminal is attached. The interactive console (--interactive) and the help (--help) are v0.4.0's phase 6 (D121).
    // Each test has its own data, home and working folders in the temporary folder; nothing reaches the network.
    [Platform("Linux")]
    public class ConsoleStartProcessTests
    {
        private const string BrokenGeneral = "outputTo: [unclosed\n";

        private static string[] Split(string Arguments) => Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        private static string[] Lines(string Text) => Text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        private static string[] GeneralCantBeRead(ConsoleProcess Console) => new[]
        {
            "Error: General.yaml could not be read: While parsing a flow sequence, did not find expected ',' or ']' (line 2, column 1)",
            "  File: " + Path.Join(Console.Settings, "General.yaml"),
            "  Fix the file, or delete it and Campertron creates a new one with the default settings (outputTo: Console)."
        };

        [TestCase("")]
        [TestCase("anything")]
        [TestCase("-h")]
        [TestCase("/?")]
        [TestCase("--Interactive")]
        [TestCase("--version extra")]
        public void WithoutArguments_OrWithArgumentsItDoesNotRead_TheConsoleStartsAsBefore(string Arguments)
        {
            using var Console = new ConsoleProcess();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), BrokenGeneral);

            Console.Start(Split(Arguments));
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Console.Out, Is.Empty);
            Assert.That(Lines(Console.Err), Is.EqualTo(GeneralCantBeRead(Console)), "the start read General.yaml: the no-argument start");
        }

        [Test]
        public void AFirstStartWithoutArguments_PreparesItsData_AndTheMenuRefusesWithoutATerminal()
        {
            using var Console = new ConsoleProcess();

            Console.Start(Array.Empty<string>());
            int Code = Console.Exit(120);

            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Lines(Console.Out), Is.EqualTo(new[] { "Preparing campground data (first start)..." }));
            Assert.That(Lines(Console.Err), Is.EqualTo(new[] { "Error: the Campertron menu needs an interactive terminal. For unattended use, set outputTo: Email or Ntfy in General.yaml." }));
            Assert.That(Directory.GetFileSystemEntries(Console.Settings).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Email.yaml", "General.yaml", "RecreationDotOrg.db" }), "a first start creates the settings and the campground data, as before");
            Assert.That(File.ReadAllText(Path.Join(Console.Settings, "General.yaml")), Does.Contain("outputTo: Console"));
            Assert.That(Directory.GetFileSystemEntries(Console.Cache), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Console.Work), Is.Empty, "nothing in the working directory");
        }

        [Test]
        public void OnATerminal_WithoutArguments_TheConsoleStartsAsBefore()
        {
            if (!ConsoleProcess.CanUseTerminal)
            {
                Assert.Ignore("no util-linux script(1) on this computer: the console is not run on a pseudo-terminal here");
            }
            using var Console = new ConsoleProcess();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), BrokenGeneral);

            Console.StartOnTerminal(Array.Empty<string>());
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(1));
            //on a terminal the error is wrapped at its width (80 columns: script's terminal reports none), as before
            Assert.That(Unwrapped(Lines(Console.Out)), Is.EqualTo(Unwrapped(GeneralCantBeRead(Console))), "a terminal alone opens nothing new: the start read General.yaml");
            Assert.That(Lines(Console.Out)[0], Does.StartWith("Error: General.yaml could not be read: ").And.Length.LessThanOrEqualTo(80));
        }

        private static string Unwrapped(IEnumerable<string> Lines) => string.Join(" ", Lines.Select(L => L.Trim()));
    }
}
