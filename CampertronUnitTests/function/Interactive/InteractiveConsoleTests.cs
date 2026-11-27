using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;
using static CampertronUnitTests.function.Interactive.ScriptedTerminal;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console (InteractiveConsole; v0.4.0's phase 6, D115, D121): its main menu (Status, Help, Exit), its
    // ends and exit codes, its read-only status and its help, driven by a scripted terminal and judged by the outcome and
    // the words written, never a screen; then the real console terminal with scripted keys and the console's own streams,
    // for what reaches a terminal (no escape sequence where there is no color, no secret); and what the interactive
    // console's code can reach at all (its IL): no monitoring, search, web host, refresh, writer or sender. Each test has
    // its own settings folder in the temporary folder; nothing reaches the network.
    [NonParallelizable]
    public class InteractiveConsoleTests
    {
        private const string Sentinel = "S3ntinel-Secret-4f1c";
        private string Root = string.Empty;
        private string Settings => Path.Join(Root, "CampertronConfig");
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private Action<ConsoleColor?> OriginalApplyColor = null!;
        private bool OriginalUnacknowledged;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private Func<string> OriginalLookup = null!;
        private string? OriginalContainer;
        private string? OriginalContainers;

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronInteractiveTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalCapabilities = ConsoleUi.Current;
            OriginalApplyColor = ConsoleUi.ApplyColor;
            OriginalUnacknowledged = ConsoleUi.Unacknowledged;
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalLookup = Load.LocalApplicationDataLookup;
            OriginalContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
            OriginalContainers = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS");
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
            ConsoleUi.Unacknowledged = OriginalUnacknowledged;
            Load.LocalApplicationDataLookup = OriginalLookup;
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", OriginalContainer);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", OriginalContainers);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private InteractiveSession Session(InteractiveTerminal Terminal, bool Container = false) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc),
                () => new CampertronInternalConfig { ConfigPath = Settings, CachePath = Path.Join(Root, "CampertronCache"), ContainerMode = Container });

        private int Run(ScriptedTerminal Terminal, IReadOnlyList<InteractiveMenuItem>? Items = null) => InteractiveConsole.Run(Session(Terminal), Items);

        private static readonly string[] Menu = { string.Empty, "  1. Status", "  2. Notifications", "  3. Settings", "  4. Help", "  5. Exit" };

        // ---- the main menu ----

        [Test]
        public void TheMainMenu_IsStatusNotificationsSettingsHelpAndExit_Only()
        {
            Assert.That(InteractiveConsole.MainMenu().Select(Item => Item.Label), Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Help", "Exit" }),
                "phase 7 adds Notifications (D122) and phase 7a Settings (D123) before Help; no capability of a later phase");
        }

        [Test]
        public void Exit_EndsTheConsoleWithZero_AfterTheTitleTheIntroductionAndTheMenu()
        {
            var Terminal = new ScriptedTerminal(Line("5"));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Terminal.Written, Is.EqualTo(new List<(string, string)>
            {
                (ScriptedTerminal.Out, ""), (HeadingKind, "Campertron interactive console"),
                (ScriptedTerminal.Out, "Type the number of a choice and press Enter. Esc or Ctrl+C cancels a question; Help says more."),
                (ScriptedTerminal.Out, ""), (HeadingKind, "Main menu"),
                (ScriptedTerminal.Out, "  1. Status"), (ScriptedTerminal.Out, "  2. Notifications"), (ScriptedTerminal.Out, "  3. Settings"),
                (ScriptedTerminal.Out, "  4. Help"), (ScriptedTerminal.Out, "  5. Exit"), (PromptKind, "Choose a number from 1 to 5: ")
            }));
            Assert.That(Terminal.Reads, Is.EqualTo(1));
        }

        [Test]
        public void TheEndOfTheInput_AtTheMainMenu_EndsTheConsoleWithZero_ReadingOnce()
        {
            var Terminal = new ScriptedTerminal(End, Line("1"));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Terminal.Reads, Is.EqualTo(1), "an empty or closed input is never read as answer after answer");
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Terminal.Left, Is.EqualTo(1));
        }

        [Test]
        public void AnEmptyInput_EndsTheConsoleAtOnce()
        {
            var Terminal = new ScriptedTerminal();

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Terminal.Reads, Is.EqualTo(1));
            Assert.That(Terminal.OfKind(ErrorKind), Is.Empty);
        }

        [Test]
        public void CtrlC_AtTheMainMenu_StopsTheConsole_WithStoppedAnd130()
        {
            var Terminal = new ScriptedTerminal(CtrlC, Line("5"));

            Assert.That(Run(Terminal), Is.EqualTo(InteractiveConsole.ExitCodeStopped));
            Assert.That(InteractiveConsole.ExitCodeStopped, Is.EqualTo(130));
            Assert.That(Terminal.OfKind(StoppedKind), Is.EqualTo(new[] { "Stopped." }));
            Assert.That(Terminal.Left, Is.EqualTo(1), "nothing is read after it");
            Assert.That(Terminal.OfKind(ErrorKind), Is.Empty);
        }

        [Test]
        public void Esc_AtTheMainMenu_HasNothingToCancel_AndTheMenuStays_AlsoAgainAndAgain()
        {
            var Terminal = new ScriptedTerminal(Esc, Esc, Esc, Esc, Esc, Line("5"));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Count(T => T == "There is nothing to cancel here. Choose Exit to leave."), Is.EqualTo(5));
            Assert.That(Terminal.OfKind(HeadingKind).Count(T => T == "Main menu"), Is.EqualTo(6));
            Assert.That(Terminal.Reads, Is.EqualTo(6));
        }

        [Test]
        public void AnAnswerThatIsNotAChoice_OrNone_IsToldAndTheMenuComesAgain()
        {
            var Terminal = new ScriptedTerminal(Line("7"), Line(""), Line("exit"), Line("5"));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Count(T => T == "That is not one of the choices. Type a number from 1 to 5."), Is.EqualTo(2));
            Assert.That(Terminal.Texts.Count(T => T == "Type the number of a choice, from 1 to 5, then press Enter."), Is.EqualTo(1));
            Assert.That(Terminal.OfKind(HeadingKind).Count(T => T == "Main menu"), Is.EqualTo(4));
        }

        [Test]
        public void AChoicesQuestion_CancelledByEscOrCtrlC_ReturnsToTheMainMenu_AndASecondCtrlCThereStops()
        {
            var Ask = new InteractiveMenuItem("Ask", S => S.Prompts.Text("Name", true).IsCancelled ? InteractiveStep.Cancelled : InteractiveStep.Done);
            var Items = new List<InteractiveMenuItem> { Ask, InteractiveConsole.MainMenu().Single(Item => Item.Label == "Exit") };
            var Terminal = new ScriptedTerminal(Line("1"), Esc, Line("1"), CtrlC, CtrlC, Line("2"));

            Assert.That(Run(Terminal, Items), Is.EqualTo(InteractiveConsole.ExitCodeStopped));
            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(2), "Esc and the first Ctrl+C each cancel the question");
            Assert.That(Terminal.OfKind(StoppedKind), Has.Count.EqualTo(1), "the second Ctrl+C, at the main menu, stops the console");
            Assert.That(Terminal.Left, Is.EqualTo(1));
        }

        [Test]
        public void AChoicesQuestion_AtTheEndOfTheInput_EndsTheConsoleWithZero()
        {
            var Ask = new InteractiveMenuItem("Ask", S => S.Prompts.Confirm("Go on?", false).End == PromptEnd.EndOfInput ? InteractiveStep.EndOfInput : InteractiveStep.Done);
            var Terminal = new ScriptedTerminal(Line("1"));

            Assert.That(Run(Terminal, new[] { Ask }), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Terminal.Reads, Is.EqualTo(2));
        }

        [Test]
        public void AChoiceThatFails_IsToldInFixedWords_NeverTheExceptionsTextOrStack_AndTheMenuComesAgain()
        {
            var Fails = new InteractiveMenuItem("Fails", S => throw new InvalidOperationException("MARKER " + Sentinel + " at " + Root));
            var Terminal = new ScriptedTerminal(Line("1"), Line("2"));

            int Code = Run(Terminal, new[] { Fails, InteractiveConsole.MainMenu().Single(Item => Item.Label == "Exit") });
            Assert.That(Terminal.AllText.Contains("MARKER") || Terminal.AllText.Contains(Sentinel) || Terminal.AllText.Contains(" at "), Is.False, "no exception text and no stack");
            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Terminal.OfKind(ErrorKind), Is.EqualTo(new[] { "That could not be done because of an unexpected problem (InvalidOperationException)." }));
        }

        [Test]
        public void TheConsoleItselfFailing_EndsWithOne_InFixedWords()
        {
            var Terminal = new FailingTerminal(new InvalidOperationException("MARKER the console broke " + Sentinel));

            int Code = InteractiveConsole.Run(Session(Terminal));

            //a boolean check first: a failure must not print the exception's text, which holds the sentinel
            Assert.That(Terminal.Errors.Any(E => E.Contains("MARKER") || E.Contains(Sentinel)), Is.False, "never the exception's text");
            Assert.That(Code, Is.EqualTo(InteractiveConsole.ExitCodeRefused));
            Assert.That(Terminal.Errors, Is.EqualTo(new[] { "the interactive console stopped because of an unexpected problem (InvalidOperationException)." }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ATerminalThatIsGone_EndsTheConsoleWithZero_AsTheEndOfTheInput_EvenWhenNothingCanBeWritten(bool WritesFail)
        {
            //a closed terminal window whose hang-up signal was ignored: reading (and writing) it fails with an IOException
            var Terminal = new FailingTerminal(new IOException("MARKER the terminal is gone"), WritesFail);

            int Code = InteractiveConsole.Run(Session(Terminal));

            Assert.That(Code, Is.EqualTo(InteractiveConsole.ExitCodeDone));
            Assert.That(Terminal.Errors, Is.Empty, "nothing to report: the terminal is gone");
        }

        // a terminal whose reads fail with Failure; with WritesFail its writes fail too (with an IOException), as a gone one's do
        private sealed class FailingTerminal : InteractiveTerminal
        {
            private readonly Exception Failure;
            private readonly bool WritesFail;
            internal List<string> Errors { get; } = new();
            internal FailingTerminal(Exception Failure, bool WritesFail = false)
            {
                this.Failure = Failure;
                this.WritesFail = WritesFail;
            }
            private void Write()
            {
                if (WritesFail)
                {
                    throw new IOException("MARKER the terminal is gone");
                }
            }
            public override int Width => 80;
            public override bool CanHideInput => true;
            public override void WriteLine(string Text) => Write();
            public override void WriteHeading(string Text) => Write();
            public override void WriteWarning(string Text) => Write();
            public override void WriteError(string Text) { Write(); Errors.Add(Text); }
            public override void WritePrompt(string Text) => Write();
            public override void WriteStopped() => Write();
            public override TerminalInput ReadLine() => throw Failure;
            public override TerminalInput ReadSecret() => throw Failure;
        }

        [Test]
        public void OnANarrowTerminal_EveryLineFits_ButAWordLongerThanALine()
        {
            Directory.CreateDirectory(Settings);
            var Terminal = new ScriptedTerminal(Line("1"), Line("4"), Line("5")) { TerminalWidth = 20 };

            Assert.That(Run(Terminal), Is.EqualTo(0));
            List<string> TooLong = Terminal.Written.Where(W => W.Kind != PromptKind).Select(W => W.Text).Where(T => T.Length > 20).ToList();
            Assert.That(TooLong.Where(T => T.Trim().Contains(' ')), Is.Empty, "only a single word (a path) can be longer than the line");
            Assert.That(Terminal.OfKind(PromptKind), Has.All.EqualTo("Choose a number from 1 to 5: "));
        }

        // ---- help ----

        [Test]
        public void Help_ExplainsTheConsole_AndOffersNothingItCantDo()
        {
            var Terminal = new ScriptedTerminal(Line("4"), Line("5"));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.OfKind(HeadingKind), Does.Contain("Help"));
            foreach (string Line_ in InteractiveWording.HelpLines)
            {
                Assert.That(Terminal.Texts, Does.Contain("  " + Line_));
            }
            string Help = string.Join(" ", InteractiveWording.HelpLines);
            Assert.That(Help, Does.Contain("CampertronConsole --interactive"));
            Assert.That(Help, Does.Contain("type the number of a choice and press Enter"));
            Assert.That(Help, Does.Contain("Esc or Ctrl+C cancels the question"));
            Assert.That(Help, Does.Contain("Exit: choose Exit."));
            Assert.That(Help, Does.Contain("Without --interactive, CampertronConsole starts as it always has"));
            Assert.That(Help, Does.Contain("Missing or incomplete settings can limit what Campertron can do."));
            Assert.That(Help, Does.Contain("Notifications: Email.yaml and Ntfy.yaml"), "phase 7's Notifications (D122)");
            Assert.That(Help, Does.Contain("Nothing is saved or sent until you choose it and answer y"));
            Assert.That(Help, Does.Contain("Settings: General.yaml (where results go, outputTo, and the campground data's automatic update) and Web.yaml (the web UI's settings)"), "phase 7a's Settings (D123)");
            Assert.That(Help, Does.Contain("saving starts, stops and restarts nothing"));
            Assert.That(Help, Does.Contain("The web UI's password is set and changed in the web UI only."));
            foreach (string Later in new[] { "Beam", "Export", "Import", "guided", "Guided", "Settings >", "search files", "refresh", "Refresh", "Monitoring",
                "search now", "Search now", "campgrounds", "Campgrounds", "Results", "setup code", "Start the monitoring" })
            {
                Assert.That(Help, Does.Not.Contain(Later), "no capability of a later phase is offered (search files, the lookup, the refresh, searches, the monitoring's controls, the guided setup, the transfer)");
            }
        }

        // ---- status ----

        private static string Value(List<(string Label, string Value)> Facts, string Label) => Facts.Single(F => F.Label == Label).Value;

        [Test]
        public void Status_WithoutAnySettings_SaysWhatIsMissing_AndCreatesNothing()
        {
            List<(string Label, string Value)> Facts = InteractiveConsole.Status(Session(new ScriptedTerminal()));

            Assert.That(Facts.Select(F => F.Label), Is.EqualTo(new[] { "Version", "Settings folder", "General.yaml", "Email.yaml", "Ntfy.yaml", "Web.yaml", "Search files", "Campground data" }));
            Assert.That(Value(Facts, "Version"), Does.StartWith("CampertronLibrary "));
            Assert.That(Value(Facts, "Settings folder"), Is.EqualTo(Settings + " (not created yet: a start without arguments, or the desktop app, creates it)"));
            Assert.That(Value(Facts, "General.yaml"), Is.EqualTo("not found: a start without arguments, or the desktop app, creates it (outputTo: Console)"));
            Assert.That(Value(Facts, "Email.yaml"), Is.EqualTo("not found"));
            Assert.That(Value(Facts, "Ntfy.yaml"), Is.EqualTo("not found"));
            Assert.That(Value(Facts, "Web.yaml"), Is.EqualTo("not found"));
            Assert.That(Value(Facts, "Search files"), Is.EqualTo("0"));
            Assert.That(Value(Facts, "Campground data"), Is.EqualTo("not prepared yet: a start without arguments, or the desktop app, prepares it"));
            Assert.That(Directory.GetFileSystemEntries(Root), Is.Empty, "no folder or file was created");
        }

        [Test]
        public void Status_InAContainerWithoutItsFolder_SaysWhereToMountIt()
        {
            List<(string Label, string Value)> Facts = InteractiveConsole.Status(Session(new ScriptedTerminal(), Container: true));

            Assert.That(Value(Facts, "Settings folder"), Is.EqualTo(Settings + " (not found: in a container, mount the settings folder at /config)"));
        }

        [Test]
        public void Status_WithoutAFolderForTheSettings_SaysSo_AndStops()
        {
            var NoFolder = new InteractiveSession(new ScriptedTerminal(), typeof(InteractiveConsole).Assembly, () => DateTime.UtcNow,
                () => throw new InvalidOperationException("The local application data folder (LocalApplicationData) is not available"));

            List<(string Label, string Value)> Facts = InteractiveConsole.Status(NoFolder);

            Assert.That(Facts.Select(F => F.Label), Is.EqualTo(new[] { "Version", "Settings folder" }));
            Assert.That(Value(Facts, "Settings folder"), Is.EqualTo("unknown: this computer gives Campertron no folder for its settings"));
        }

        [Test]
        public void Status_WithSettings_SaysWhatIsThere_ShowsNoValueButOutputTo_AndChangesNothing()
        {
            Directory.CreateDirectory(Settings);
            File.WriteAllText(Path.Join(Settings, "General.yaml"), "# " + Sentinel + "-general\noutputTo: Email\nrefreshRidbDataDayInterval: 4321\n");
            File.WriteAllText(Path.Join(Settings, "Email.yaml"), "smtpServer: smtp." + Sentinel + ".example\nsmtpPassword: " + Sentinel + "-password\n");
            File.WriteAllText(Path.Join(Settings, "Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example\ntopic: " + Sentinel + "-topic\n");
            File.WriteAllText(Path.Join(Settings, "Web.yaml"), "networkListening: false\n");
            File.WriteAllText(Path.Join(Settings, "Web.password"), Sentinel + "-web-password-record");
            File.WriteAllText(Path.Join(Settings, "Watchman " + Sentinel + ".yaml"), "displayName: " + Sentinel + "-search\n");
            File.WriteAllText(Path.Join(Settings, "Zion.yaml"), "displayName: Zion\n");
            File.WriteAllText(Path.Join(Settings, "RecreationDotOrg.db"), "not opened");
            File.WriteAllText(Path.Join(Settings, "Camp.History"), Sentinel + "-history");
            List<string> Before = Snapshot(Settings);

            var Terminal = new ScriptedTerminal(Line("1"), Line("5"));
            int Code = Run(Terminal);
            List<(string Label, string Value)> Facts = InteractiveConsole.Status(Session(new ScriptedTerminal()));

            //boolean checks first: a failure must not print a secret
            Assert.That(Terminal.AllText.Contains(Sentinel) || Facts.Any(F => F.Value.Contains(Sentinel)), Is.False, "no value, name or secret of a file is shown");
            Assert.That(Terminal.AllText.Contains("4321"), Is.False);
            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Value(Facts, "Settings folder"), Is.EqualTo(Settings));
            Assert.That(Value(Facts, "General.yaml"), Is.EqualTo("found; outputTo: Email"));
            Assert.That(Value(Facts, "Email.yaml"), Is.EqualTo("found"));
            Assert.That(Value(Facts, "Ntfy.yaml"), Is.EqualTo("found"));
            Assert.That(Value(Facts, "Web.yaml"), Is.EqualTo("found"));
            Assert.That(Value(Facts, "Search files"), Is.EqualTo("2"));
            Assert.That(Value(Facts, "Campground data"), Is.EqualTo("prepared"));
            Assert.That(Terminal.Texts, Does.Contain("  General.yaml: found; outputTo: Email"));
            Assert.That(Terminal.OfKind(HeadingKind), Does.Contain("Status (read only: nothing is changed)"));
            Assert.That(Snapshot(Settings).SequenceEqual(Before), Is.True, "nothing was created, changed or deleted");
        }

        [TestCase("outputTo: [Console\n")]
        [TestCase("outputTo: Sometimes\n")]
        [TestCase("outputTo: Console\nsearchBy: DaysOut\n")]
        [TestCase("")]
        public void Status_AGeneralYamlAStartCantUse_IsFoundButUnusable_WithoutItsProblemsText(string Text)
        {
            Directory.CreateDirectory(Settings);
            File.WriteAllText(Path.Join(Settings, "General.yaml"), Text);

            List<(string Label, string Value)> Facts = InteractiveConsole.Status(Session(new ScriptedTerminal()));

            Assert.That(Value(Facts, "General.yaml"), Is.EqualTo("found, but it can't be used as it is: a start stops with its problem"));
            Assert.That(File.ReadAllText(Path.Join(Settings, "General.yaml")), Is.EqualTo(Text), "never rewritten");
        }

        private static List<string> Snapshot(string Folder) =>
            Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories)
                .Select(P => Path.GetFileName(P) + " " + new FileInfo(P).Length + " " + File.GetLastWriteTimeUtc(P).Ticks + " " + System.Convert.ToHexString(File.ReadAllBytes(P)))
                .OrderBy(P => P, StringComparer.Ordinal).ToList();

        [Test]
        public void TheFolders_AreFoundWhereEveryStartFindsThem_WithoutCreatingThem()
        {
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", null);
            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS", null);
            string Local = Path.Join(Root, "local");
            Directory.CreateDirectory(Local);
            Load.LocalApplicationDataLookup = () => Local;

            CampertronInternalConfig Found = Load.FolderLocations();
            Assert.That(Directory.GetFileSystemEntries(Local), Is.Empty, "nothing created");
            CampertronInternalConfig Started = Load.GetFolders();

            Assert.That(Found.ConfigPath, Is.EqualTo(Started.ConfigPath));
            Assert.That(Found.CachePath, Is.EqualTo(Started.CachePath));
            Assert.That(Found.ContainerMode, Is.False);
            Assert.That(Found.ConfigPath, Is.EqualTo(Path.Join(Local, "CampertronConfig")));

            Environment.SetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER", "true");
            CampertronInternalConfig Container = Load.FolderLocations();
            Assert.That((Container.ConfigPath, Container.CachePath, Container.ContainerMode), Is.EqualTo(("/config", "/cache", true)));
        }

        // ---- the console's terminal: ConsoleUi's streams, scripted keys ----

        private static ConsoleKeyInfo Key(char C) => new ConsoleKeyInfo(C, C == '\r' ? ConsoleKey.Enter : ConsoleKey.NoName, false, false, false);

        private static Func<ConsoleKeyInfo?> Keys(string Typed)
        {
            var Queue = new Queue<char>(Typed);
            return () => Queue.Count > 0 ? Key(Queue.Dequeue()) : null;
        }

        private static ConsoleUi.Capabilities Terminal_(bool Color) =>
            new ConsoleUi.Capabilities { OutputIsTerminal = true, InputIsTerminal = true, ErrorIsTerminal = true, ControlSequences = Color, Color = Color, Width = 80 };

        [Test]
        public void OnTheConsole_WithoutColor_NothingButPlainText_IsWritten()
        {
            ConsoleUi.Current = Terminal_(false);
            ConsoleUi.ApplyColor = Color => Out.Write("\u001b[COLOR]");
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, Keys("1\r4\r\u001b5\r"), () => false, V => { });

            int Code = InteractiveConsole.Run(Session(Terminal));

            Assert.That(Code, Is.EqualTo(0));
            string Written = Out.ToString();
            Assert.That(Written.Contains('\u001b'), Is.False, "no escape sequence: no color, no cursor movement, no clearing (an ordinal check)");
            Assert.That(Written.Where(C => char.IsControl(C) && C != '\r' && C != '\n'), Is.Empty);
            Assert.That(Written, Does.Contain("Choose a number from 1 to 5: 1" + Environment.NewLine), "the answer is written back after its question");
            Assert.That(Written, Does.Contain("Status (read only: nothing is changed)"));
            Assert.That(Written, Does.Contain("There is nothing to cancel here. Choose Exit to leave."));
            Assert.That(Err.ToString(), Is.Empty);
        }

        [Test]
        public void OnTheConsole_WithColor_OnlyHeadingsAreColored_AndEveryColorIsReset()
        {
            ConsoleUi.Current = Terminal_(true);
            var Colors = new List<string>();
            ConsoleUi.ApplyColor = Color => Colors.Add(Color?.ToString() ?? "reset");
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, Keys("5\r"), () => false, V => { });

            Assert.That(InteractiveConsole.Run(Session(Terminal)), Is.EqualTo(0));
            Assert.That(Colors, Is.EqualTo(new[] { "Green", "reset", "Green", "reset" }), "the title and Main menu");
        }

        [Test]
        public void OnTheConsole_ASecret_IsNeverWrittenToTheTerminal()
        {
            ConsoleUi.Current = Terminal_(false);
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, Keys(Sentinel + "x\b\r"), () => false, V => { });

            PromptResult<SecretText> Result = new InteractivePrompts(Terminal).Secret("Token");

            //boolean checks only: a failure must not print the secret
            Assert.That(Out.ToString().Contains(Sentinel) || Err.ToString().Contains(Sentinel), Is.False, "the secret reached neither stream");
            Assert.That(Out.ToString() == "Token (not shown as you type): " + Environment.NewLine, Is.True, "the question and the line break, nothing else");
            Assert.That(Result.Value!.Reveal() == Sentinel, Is.True);
        }

        [Test]
        public void OnTheConsole_TheInputEnding_EndsTheConsole()
        {
            ConsoleUi.Current = Terminal_(false);
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, Keys(""), () => false, V => { });

            Assert.That(InteractiveConsole.Run(Session(Terminal)), Is.EqualTo(0));
            Assert.That(Out.ToString(), Does.EndWith("The input ended, so the interactive console ended." + Environment.NewLine));
        }

        // the keys by name: a test case's name holds no control character (the test adapter would drop the case)
        [TestCase("Exit", 0)]
        [TestCase("CtrlC", 130)]
        [TestCase("EscThenCtrlD", 0)]
        [TestCase("InputEnded", 0)]
        [TestCase("TerminalGone", 0)]
        [TestCase("FAIL", 1)]
        public void OnTheConsole_CtrlCIsAKey_OnlyWhileAnAnswerIsRead_AndIsRestoredAfterEveryEnd(string Typed, int Expected)
        {
            ConsoleUi.Current = Terminal_(false);
            bool ControlCAsInput = false;
            var Seen = new List<bool>();
            string Keys_ = Typed switch { "Exit" => "4\r", "CtrlC" => "\u0003", "EscThenCtrlD" => "\u001b\u0004", _ => string.Empty };
            var Queue = new Queue<char>(Keys_);
            Func<ConsoleKeyInfo?> Next = () =>
            {
                Seen.Add(ControlCAsInput);
                if (Typed == "TerminalGone")
                {
                    throw new IOException("the terminal is gone");
                }
                if (Typed == "FAIL")
                {
                    throw new InvalidOperationException("the read failed");
                }
                return Queue.Count > 0 ? Key(Queue.Dequeue()) : null;
            };
            var Terminal = new ConsoleTerminal(ConsoleUi.Current, Next, () => ControlCAsInput, V => ControlCAsInput = V);

            Assert.That(InteractiveConsole.Run(Session(Terminal)), Is.EqualTo(Expected));
            Assert.That(Seen, Is.Not.Empty);
            Assert.That(Seen, Has.All.True, "every key is read with Ctrl+C as a key");
            Assert.That(ControlCAsInput, Is.False, "and Ctrl+C is as it was after the read, however it ended");
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void WithoutATerminal_TheInteractiveConsoleIsRefused_WithExitCode1_HavingReadNothing(bool OutputRedirected, bool InputRedirected)
        {
            ConsoleUi.Current = ConsoleUi.Detect(OutputRedirected, InputRedirected, true, null, "xterm", false, () => 80);
            Load.LocalApplicationDataLookup = () => throw new AssertionException("the folders were looked up");

            int Code = InteractiveConsole.RunInTerminal(typeof(InteractiveConsole).Assembly);

            Assert.That(Code, Is.EqualTo(InteractiveConsole.ExitCodeRefused));
            Assert.That(Out.ToString(), Is.Empty);
            Assert.That(Err.ToString().Replace(Environment.NewLine, "\n"), Is.EqualTo("Error: the interactive console needs an interactive terminal. Start CampertronConsole --interactive in a terminal, without redirecting its input or output.\n"));
        }

        // ---- what the interactive console's code can reach ----

        private static List<string> Reached()
        {
            var Called = new List<string>();
            foreach (Type T in typeof(InteractiveConsole).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveConsole).Namespace))
            {
                const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheInteractiveConsole_ReachesNoMonitoringSearchWebHostRefreshWriterOrSender()
        {
            List<string> Called = Reached();
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.RecDotOrg.api.",
                "CampertronLibrary.function.Forms.", "CampertronLibrary.function.WebAccess.", "CampertronWeb.",
                "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession", "CampertronLibrary.function.Base.TestNotification",
                "CampertronLibrary.function.Base.SmtpPassword", "CampertronLibrary.function.Base.NtfyToken", "CampertronLibrary.function.Base.NtfyPublisher",
                "CampertronLibrary.function.Base.SecretFile", "CampertronLibrary.function.Base.Menu", "CampertronLibrary.function.Base.Yaml::",
                "CampertronLibrary.function.Base.NtfyTopic", "CampertronLibrary.function.Base.NtfyEndpoint", "CampertronLibrary.function.Base.Notifications",
                "CampertronLibrary.function.Base.ConsoleResults", "CampertronLibrary.function.Base.ResultsHtml", "CampertronLibrary.function.Base.ResultsEmail",
                "CampertronLibrary.function.Base.CampsiteConfig",
                "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::GetFolders",
                "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole", "CampertronLibrary.function.RecDotOrg.data.Load::DbExistsCheck",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                "CampertronLibrary.function.RecDotOrg.data.RidbRefreshService", "CampertronLibrary.function.RecDotOrg.data.Cache",
                "System.IO.File::Write", "System.IO.File::Create", "System.IO.File::Delete", "System.IO.File::Move", "System.IO.File::Copy", "System.IO.File::Open",
                "System.IO.File::Append", "System.IO.File::Replace", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.IO.Directory::Move",
                "System.IO.FileStream", "System.IO.StreamWriter", "System.Diagnostics.Process", "System.Net.", "System.Environment::Exit", "System.Environment::GetEnvironmentVariable"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            //and the scan sees what it does reach
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.CurrentConfiguration::ReadGeneral"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.data.Load::FolderLocations"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.data.Load::DbExists"));
            Assert.That(Called, Does.Contain("System.Console::ReadKey"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Base.ConsoleUi::Echo"));
        }

        [Test]
        public void TheInteractiveConsole_MovesNoCursorAndClearsNoScreen()
        {
            List<string> Called = Reached();
            string[] Screen =
            {
                "System.Console::SetCursorPosition", "System.Console::GetCursorPosition", "System.Console::get_CursorTop", "System.Console::set_CursorTop",
                "System.Console::get_CursorLeft", "System.Console::set_CursorLeft", "System.Console::Clear", "System.Console::set_CursorVisible",
                "System.Console::get_WindowWidth", "System.Console::get_WindowHeight", "System.Console::set_Title", "System.Console::get_BufferHeight",
                "CampertronLibrary.function.Base.ConsoleUi::ClearScreen", "CampertronLibrary.function.Base.ConsoleUi::HideCursor", "CampertronLibrary.function.Base.ConsoleUi::WaitForKey"
            };
            Assert.That(Called.Where(C => Screen.Contains(C)), Is.Empty);
            Assert.That(StringLiteralsOfTheNamespace().Where(S => S.Contains('\u001b')), Is.Empty, "no escape sequence in its words");
        }

        [Test]
        public void TheCommandLine_IsDecidedFromTheArgumentsAlone_NeverFromATerminal()
        {
            MethodBase Parse = typeof(ConsoleArguments).GetMethod(nameof(ConsoleArguments.Parse))!;
            List<string> Called = YamlDocumentPreservationTests.ReferencedMembers(Parse).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();

            Assert.That(Called.Where(C => C.StartsWith("System.Console", StringComparison.Ordinal) || C.StartsWith("CampertronLibrary.function.Base.ConsoleUi", StringComparison.Ordinal)
                || C.StartsWith("System.Environment", StringComparison.Ordinal) || C.StartsWith("System.IO.", StringComparison.Ordinal)), Is.Empty);
        }

        private static List<string> StringLiteralsOfTheNamespace()
        {
            var Literals = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveConsole).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveConsole).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Literals.AddRange(ConsoleStartContractTests.StringLiterals(Method));
                }
                Literals.AddRange(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!));
            }
            return Literals;
        }
    }
}
