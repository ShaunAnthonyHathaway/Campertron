using CampertronLibrary.function.Interactive;

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

        // ---- --help and the refusals (D121) ----

        [Test]
        public void Help_ListsEveryWayToStart_ExitCode0_AndReadsOrCreatesNothing()
        {
            using var Console = new ConsoleProcess();

            Console.Start(new[] { "--help" });
            int Code = Console.Exit();

            Assert.That(Code, Is.Zero);
            Assert.That(Lines(Console.Out), Is.EqualTo(ConsoleArguments.HelpLines), "redirected: the lines as they are");
            Assert.That(Console.Err, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Console.Data), Is.Empty, "no folder was created");
            Assert.That(Directory.GetFileSystemEntries(Console.Home), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Console.Work), Is.Empty);
        }

        [TestCase("--interactive extra", "--interactive opens the interactive console and takes no value and no other argument.")]
        [TestCase("--interactive --interactive", "--interactive opens the interactive console and takes no value and no other argument.")]
        [TestCase("--interactive=yes", "--interactive opens the interactive console and takes no value and no other argument.")]
        [TestCase("--version --interactive", "--interactive opens the interactive console and takes no value and no other argument.")]
        [TestCase("--help --interactive", "--interactive opens the interactive console and takes no value and no other argument.")]
        [TestCase("--help extra", "--help shows how to start CampertronConsole and takes no value and no other argument.")]
        [TestCase("--help=all", "--help shows how to start CampertronConsole and takes no value and no other argument.")]
        public void AnInteractiveOrHelpCommandLineThatIsWrong_IsRefused_ExitCode1_AndNothingIsCreated(string Arguments, string Problem)
        {
            using var Console = new ConsoleProcess();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), BrokenGeneral);

            Console.Start(Split(Arguments));
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Console.Out, Is.Empty);
            Assert.That(Lines(Console.Err), Is.EqualTo(new[] { "Error: " + Problem, "  " + ConsoleArguments.Usage }));
            Assert.That(Directory.GetFileSystemEntries(Console.Data).Select(Path.GetFileName), Is.EqualTo(new[] { "CampertronConfig" }), "General.yaml was not read: nothing else was created");
        }

        [TestCase("--web --interactive")]
        [TestCase("--interactive --web")]
        [TestCase("--port 8790 --help")]
        public void AWebModeCommandLine_WithTheNewArguments_IsStillWebModesToRefuse(string Arguments)
        {
            using var Console = new ConsoleProcess();

            Console.Start(Split(Arguments));
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Console.Out, Is.Empty);
            Assert.That(Lines(Console.Err), Is.EqualTo(new[] { "Error: an argument was given that web mode does not know: it knows --web, --healthcheck, --listen and --port.", "  " + WebArguments.Usage }));
            Assert.That(Directory.GetFileSystemEntries(Console.Data), Is.Empty);
        }

        // ---- --interactive (D115, D121) ----

        [TestCase(true)]
        [TestCase(false)]
        public void Interactive_WithoutATerminal_IsRefused_ExitCode1_AndReadsOrCreatesNothing(bool WithAnswers)
        {
            //every stream is redirected (a script, a pipe, a container without -t); answers on standard input are never read
            using var Console = new ConsoleProcess();

            Console.Start(new[] { "--interactive" });
            Console.Type(WithAnswers ? "1\n3\n" : string.Empty);
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(1));
            Assert.That(Console.Out, Is.Empty, "no menu");
            Assert.That(Lines(Console.Err), Is.EqualTo(new[] { "Error: " + InteractiveWording.NeedsTerminal }));
            Assert.That(Directory.GetFileSystemEntries(Console.Data), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Console.Home), Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(Console.Work), Is.Empty);
        }

        private static ConsoleProcess OnTerminal(Action<ConsoleProcess>? Prepare = null, params (string Name, string? Value)[] Environment_)
        {
            if (!ConsoleProcess.CanUseTerminal)
            {
                Assert.Ignore("no util-linux script(1) on this computer: the console is not run on a pseudo-terminal here");
            }
            var Console = new ConsoleProcess();
            Prepare?.Invoke(Console);
            return Console;
        }

        private const string Choose = "Choose a number from 1 to 5: ";

        [Test]
        public void Interactive_OnATerminal_ShowsTheMenuStatusAndHelp_ExitsWith0_AndCreatesNothing()
        {
            using ConsoleProcess Console = OnTerminal();
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("4\r");
            Assert.That(Console.WaitFor(Choose, 3), Is.True, Console.Out);
            Console.Type("5\r");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.StartWith("\nCampertron interactive console\n"));
            Assert.That(Out, Does.Contain("Main menu\n  1. Status\n  2. Notifications\n  3. Settings\n  4. Help\n  5. Exit\n" + Choose + "1\n"));
            Assert.That(Out, Does.Contain("Status (read only: nothing is changed)\n  Version: CampertronConsole "));
            Assert.That(Out, Does.Contain("  Email.yaml: not found\n"));
            Assert.That(Out, Does.Contain("  Search files: 0\n"));
            Assert.That(Out, Does.Contain("Help\n  This is Campertron's interactive console"));
            Assert.That(Out, Does.EndWith(Choose + "5\n"));
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.Not.Contain("Preparing campground data"), "no start: no settings and no campground data prepared");
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "nothing was created, changed or deleted");
        }

        [Test]
        public void Interactive_OnATerminal_WithSettings_ShowsTheirStatus_StartsNothing_AndChangesNothing()
        {
            const string Sentinel = "S3ntinel-Secret-4f1c";
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            Directory.CreateDirectory(Console.Cache);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Email\n");
            File.WriteAllText(Path.Join(Console.Settings, "Email.yaml"), "smtpServer: smtp.example\nsmtpPort: 587\nsmtpPassword: " + Sentinel + "\n");
            File.WriteAllText(Path.Join(Console.Settings, "Ntfy.yaml"), "enabled: true\nserver: https://ntfy.example\ntopic: " + Sentinel + "\n");
            File.WriteAllText(Path.Join(Console.Settings, "Zion.yaml"), "displayName: " + Sentinel + "\nautoRun: true\n");
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);

            //a failure's message never shows the secret: the output is told with it replaced
            string Told() => Console.Out.Replace(Sentinel, "[sentinel]");
            Console.StartOnTerminal(new[] { "--interactive" }, ("CAMPERTRON_EMAIL_PASSWORD", Sentinel));
            Assert.That(Console.WaitFor(Choose), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Told());
            Console.Type("5\r");
            int Code = Console.Exit();

            Assert.That(Console.Out.Contains(Sentinel) || Console.Err.Contains(Sentinel), Is.False, "no value of a file and no environment variable is shown");
            string Out = Told().Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("  General.yaml: found; outputTo: Email\n  Email.yaml: found\n  Ntfy.yaml: found\n  Web.yaml: not found\n  Search files: 1\n"));
            Assert.That(Out, Does.Not.Contain("Monitoring").And.Not.Contain("monitoring is").And.Not.Contain("Checking"), "outputTo: Email starts no monitoring here");
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "no Camp.History, no lock, no database, nothing written");
        }

        // v0.4.0's phase 7 (D122): Notifications > Email on a real pseudo-terminal: Esc and Ctrl+C at a field's question
        // cancel it (never "Stopped."), the plain-text password is typed without one character of it written back, Save
        // writes it through the shared form, and the backup holds no replaced password
        [Test]
        public void Interactive_OnATerminal_TheEmailForm_HidesThePassword_CancelsAQuestion_AndSavesThroughTheSharedForm()
        {
            string Old = "PwOld-pty-" + Guid.NewGuid().ToString("N");
            string New = "PwNew-pty-" + Guid.NewGuid().ToString("N");
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\n");
            string Email = Path.Join(Console.Settings, "Email.yaml");
            File.WriteAllText(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Old + "\nsendFromAddress: from@example.invalid\nsendToAddressList:\n- to@example.invalid\n");
            string Told() => Console.Out.Replace(Old, "[old]").Replace(New, "[new]");
            const string Clean = "Choose a number from 1 to 11: ";
            const string Edited = "Choose a number from 1 to 12: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Told());
            Console.Type("2\r");
            Assert.That(Console.WaitFor("Choose a number from 1 to 3: "), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Clean), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor("New value (optional, Enter leaves it empty): "), Is.True, Told());
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Clean, 2), Is.True, Told());
            Console.Type("2\r");
            Assert.That(Console.WaitFor("New value (optional, Enter leaves it empty): ", 2), Is.True, Told());
            Console.Type("\u0003");
            Assert.That(Console.WaitFor(Clean, 3), Is.True, Told());
            Console.Type("5\r");
            Assert.That(Console.WaitFor("New plain-text password (not shown as you type): "), Is.True, Told());
            Console.Type(New + "\r");
            Assert.That(Console.WaitFor(Edited), Is.True, Told());
            Console.Type("10\r");
            Assert.That(Console.WaitFor("Save Email.yaml now? [y/N]: "), Is.True, Told());
            Console.Type("y\r");
            Assert.That(Console.WaitFor("Info: Saved Email.yaml."), Is.True, Told());
            Assert.That(Console.WaitFor(Clean, 4), Is.True, Told());
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Told().Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Console.Out.Contains(Old) || Console.Out.Contains(New) || Console.Err.Contains(Old) || Console.Err.Contains(New), Is.False,
                "neither password was ever written to the terminal");
            Assert.That(Out, Does.Contain("New plain-text password (not shown as you type): \n"), "only the line break is written back");
            Assert.That(ConsoleProcess.Occurrences(Out, "Cancelled.\n"), Is.EqualTo(2), "Esc and Ctrl+C each cancelled a question");
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            string Saved = File.ReadAllText(Email);
            Assert.That(Saved.Contains("smtpPassword: '" + New + "'") || Saved.Contains("smtpPassword: " + New + "\n"), Is.True, "the new password was saved");
            Assert.That(File.ReadAllText(Email + ".bak").Contains(Old), Is.False, "D111: the replaced password is not in the backup");
            Assert.That(File.ReadAllText(Email + ".bak"), Does.Contain("smtpUsername: someone"));
        }

        // v0.4.0's phase 7a (D123): Settings > Web UI settings on a real pseudo-terminal: Ctrl+C at a switch's question
        // cancels it, the password's switch turned off, Save after y asks the form's one confirmation (D81), Esc there writes
        // nothing, Don't save writes nothing, its first choice saves through the shared form; the web UI's password record is
        // never read, shown or changed, and no web host is started
        [Test]
        public void Interactive_OnATerminal_TheWebForm_AsksItsOneConfirmation_AndSavesThroughTheSharedForm()
        {
            string Record = "WebRecord-pty-" + Guid.NewGuid().ToString("N");
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\n");
            string Web = Path.Join(Console.Settings, "Web.yaml");
            const string WebText = "# my web settings\npasswordProtection: true\nnetworkListening: true\n";
            File.WriteAllText(Web, WebText);
            string PasswordRecord = Path.Join(Console.Settings, "Web.password");
            File.WriteAllText(PasswordRecord, Record);
            DateTime RecordTime = File.GetLastWriteTimeUtc(PasswordRecord);
            string Told() => Console.Out.Replace(Record, "[record]");
            const string Clean = "Choose a number from 1 to 6: ";
            const string Edited = "Choose a number from 1 to 8: ";
            const string Confirm = "Choose a number from 1 to 2: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Told());
            Console.Type("3\r");
            Assert.That(Console.WaitFor("Choose a number from 1 to 3: "), Is.True, Told());
            Console.Type("2\r");
            Assert.That(Console.WaitFor(Clean), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor("Require a password (passwordProtection)? [Y/n]: "), Is.True, Told());
            Console.Type("\u0003");
            Assert.That(Console.WaitFor(Clean, 2), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor("Require a password (passwordProtection)? [Y/n]: ", 2), Is.True, Told());
            Console.Type("n\r");
            Assert.That(Console.WaitFor(Edited), Is.True, Told());
            Console.Type("6\r");
            Assert.That(Console.WaitFor("Save Web.yaml now? [y/N]: "), Is.True, Told());
            Console.Type("y\r");
            Assert.That(Console.WaitFor(Confirm), Is.True, Told());
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Edited, 2), Is.True, Told());
            Assert.That(File.ReadAllText(Web), Is.EqualTo(WebText), "Esc at the confirmation writes nothing");
            Console.Type("6\r");
            Assert.That(Console.WaitFor("Save Web.yaml now? [y/N]: ", 2), Is.True, Told());
            Console.Type("y\r");
            Assert.That(Console.WaitFor(Confirm, 2), Is.True, Told());
            Console.Type("2\r");
            Assert.That(Console.WaitFor(Edited, 3), Is.True, Told());
            Assert.That(File.ReadAllText(Web), Is.EqualTo(WebText), "Don't save writes nothing");
            Console.Type("6\r");
            Assert.That(Console.WaitFor("Save Web.yaml now? [y/N]: ", 3), Is.True, Told());
            Console.Type("y\r");
            Assert.That(Console.WaitFor(Confirm, 3), Is.True, Told());
            Console.Type("1\r");
            Assert.That(Console.WaitFor("Info: Saved Web.yaml."), Is.True, Told());
            Assert.That(Console.WaitFor(Clean, 3), Is.True, Told());
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Told().Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Console.Out.Contains(Record) || Console.Err.Contains(Record), Is.False, "the web UI's password record is never shown");
            Assert.That(Out, Does.Contain("Main menu\n  1. Status\n  2. Notifications\n  3. Settings\n  4. Help\n  5. Exit\n"));
            Assert.That(Out, Does.Contain("Settings\n  1. General settings\n  2. Web UI settings\n  3. Back to the main menu\n"));
            Assert.That(Out, Does.Contain("Save without a password?\n"));
            Assert.That(Out, Does.Contain("  1. Save without a password, reachable from the network\n  2. Don't save\n"));
            Assert.That(ConsoleProcess.Occurrences(Out, "Cancelled.\n"), Is.EqualTo(2), "Ctrl+C at the switch and Esc at the confirmation");
            Assert.That(ConsoleProcess.Occurrences(Out, "Info: Nothing was saved. Your changes are still here.\n"), Is.EqualTo(2));
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out, Does.Not.Contain("Web UI: listening").And.Not.Contain("Web UI setup code"), "no web host was started, no setup code made");
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            Assert.That(File.ReadAllText(Web), Is.EqualTo(WebText.Replace("passwordProtection: true", "passwordProtection: false")), "the confirmed save, through the shared form");
            Assert.That(File.ReadAllText(Web + ".bak"), Is.EqualTo(WebText));
            Assert.That(File.ReadAllText(PasswordRecord), Is.EqualTo(Record));
            Assert.That(File.GetLastWriteTimeUtc(PasswordRecord), Is.EqualTo(RecordTime), "the password record is the web UI's: never written");
            Assert.That(File.Exists(Path.Join(Console.Settings, "Web.keys")), Is.False, "no key ring: no web host");
        }

        [Test]
        public void Interactive_OnATerminal_CtrlCAtTheMainMenu_StopsWith130()
        {
            using ConsoleProcess Console = OnTerminal();

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("\u0003");
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(130), Console.Out);
            Assert.That(Console.Out.Replace("\r\n", "\n"), Does.EndWith(Choose + "\nStopped.\n"), "Ctrl+C was read as a key: the line ends, then Stopped.");
            Assert.That(Directory.GetFileSystemEntries(Console.Data), Is.Empty);
        }

        [Test]
        public void Interactive_OnATerminal_EscAtTheMainMenu_HasNothingToCancel_ThenCtrlDEndsItWith0()
        {
            using ConsoleProcess Console = OnTerminal();

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor("There is nothing to cancel here. Choose Exit to leave."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("x");
            Assert.That(Console.WaitFor(Choose + "x"), Is.True, Console.Out);
            Console.Type("\u0004");
            Assert.That(Console.WaitFor("The input ended", 1, 2), Is.False, "Ctrl+D on a line with text is ignored");
            Console.Type("\u007f");
            Assert.That(Console.WaitFor(Choose + "x\b \b"), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            Assert.That(Code, Is.Zero, Console.Out);
            Assert.That(Console.Out.Replace("\r\n", "\n"), Does.EndWith(Choose + "x\b \b\nThe input ended, so the interactive console ended.\n"));
        }
    }
}
