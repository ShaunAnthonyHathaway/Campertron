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
        [TestCase("--transfer-selftest extra")]
        [TestCase("--Transfer-Selftest")]
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

        // --transfer-selftest (TD-69), the release smoke tests' run of the transfer package's self-test: in memory, its fixed facts
        // on standard output only, exit code 0, and nothing created in the data, home or working folders
        [Test]
        public void TheTransferSelfTest_RunsInMemory_PrintsOnlyItsFacts_AndCreatesNothing()
        {
            using var Console = new ConsoleProcess();

            Console.Start(new[] { "--transfer-selftest" });
            int Code = Console.Exit(120);

            Assert.That(Code, Is.EqualTo(0), Console.Out);
            Assert.That(Console.Err, Is.Empty);
            string[] Out = Lines(Console.Out);
            Assert.That(Out, Has.Length.EqualTo(15));
            Assert.That(Out[0], Is.EqualTo("transfer-selftest runtime=" + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier + " "
                + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture));
            Assert.That(Out[1], Is.EqualTo("transfer-selftest os-architecture=" + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture));
            Assert.That(Out[2], Is.EqualTo("transfer-selftest format=1"));
            Assert.That(Out.Skip(3), Has.All.Match(@"^transfer-selftest [a-z0-9-]+=ok$"));
            Assert.That(Out.Last(), Is.EqualTo("transfer-selftest result=ok"));
            foreach (string Folder in new[] { Console.Data, Console.Home, Console.Work })
            {
                Assert.That(Directory.GetFileSystemEntries(Folder), Is.Empty, Folder);
            }
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

        private const string Choose = "Choose a number from 1 to 10: ";
        //since D130 (PT1): before a new screen takes away what is shown
        private const string PressEnter = "Press Enter to continue.";

        [Test]
        public void Interactive_OnATerminal_ShowsTheMenuStatusAndHelp_ExitsWith0_AndCreatesNothing()
        {
            using ConsoleProcess Console = OnTerminal();
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("1\r");
            //since D130 (PT1): Status and Help are screens of their own, so the main menu after each asks for Enter first
            Assert.That(Console.WaitFor(PressEnter), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("9\r");
            Assert.That(Console.WaitFor(PressEnter, 2), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 3), Is.True, Console.Out);
            Console.Type("10\r");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            //the plain branding (TERM=dumb, the width not known), then the title; on a terminal that can't clear, each new screen
            //follows after an empty line
            Assert.That(Out, Does.StartWith("Campertron\n\nCampertron interactive console\n"));
            Assert.That(Out.Split(PressEnter).Length - 1, Is.EqualTo(2), "after Status and after Help");
            Assert.That(Out, Does.Contain("Main menu\n  1. Status\n  2. Notifications\n  3. Settings\n  4. Searches\n  5. Campgrounds\n  6. Results\n  7. Monitoring\n  8. Guided setup\n  9. Help\n  10. Exit\n" + Choose + "1\n"));
            Assert.That(Out, Does.Contain("Status (read only: nothing is changed)\n  Version: CampertronConsole "));
            Assert.That(Out, Does.Contain("  Email.yaml: not found\n"));
            Assert.That(Out, Does.Contain("  Search files: 0\n"));
            Assert.That(Out, Does.Contain("Help\n  This is Campertron's interactive console"));
            Assert.That(Out, Does.EndWith(Choose + "10\n"));
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
            Assert.That(Console.WaitFor(PressEnter), Is.True, Told());
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Told());
            Console.Type("10\r");
            int Code = Console.Exit();

            Assert.That(Console.Out.Contains(Sentinel) || Console.Err.Contains(Sentinel), Is.False, "no value of a file and no environment variable is shown");
            string Out = Told().Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("  General.yaml: found; outputTo: Email\n  Email.yaml: found\n  Ntfy.yaml: found\n  Web.yaml: not found\n  Search files: 1\n"));
            Assert.That(Out, Does.Not.Contain("Status: Running").And.Not.Contain("Status: Starting").And.Not.Contain("monitoring is").And.Not.Contain("Checking"),
                "outputTo: Email starts no monitoring here (since phase 11, D127, only Monitoring's Start does)");
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
            //the form's clean choices (six; since phase 9, D125, no longer the main menu's prompt); its edited choices (eight; the
            //main menu's prompt too from phase 10 to phase 11, D127, which gave the main menu nine)
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
            Assert.That(Out, Does.Contain("Main menu\n  1. Status\n  2. Notifications\n  3. Settings\n  4. Searches\n  5. Campgrounds\n  6. Results\n  7. Monitoring\n  8. Guided setup\n  9. Help\n  10. Exit\n"));
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

        // v0.4.0's phase 8 (D124): Searches on a real pseudo-terminal: the list (the shared form's line, the campground data
        // not prepared, so said and never opened), Ctrl+C at New search's name cancels it and creates nothing, Esc at a
        // field's question cancels it, a name typed with spaces around it goes to the form as typed, and Save after y writes
        // it through the shared form with the backup; nothing is searched, prepared or started
        [Test]
        public void Interactive_OnATerminal_Searches_CancelsAQuestion_AndSavesASearchThroughTheSharedForm()
        {
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\n");
            string Alpha = Path.Join(Console.Settings, "Alpha.yaml");
            const string AlphaText = "# my search\ndisplayName: First trip\nautoRun: true\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";
            File.WriteAllText(Alpha, AlphaText);
            const string List = "Choose a number from 1 to 4: ";
            const string Clean = "Choose a number from 1 to 13: ";
            const string Edited = "Choose a number from 1 to 15: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("4\r");
            Assert.That(Console.WaitFor(List), Is.True, Console.Out);
            Console.Type("2\r");
            Assert.That(Console.WaitFor("New search file name (optional, Enter leaves it empty): "), Is.True, Console.Out);
            Console.Type("\u0003");
            Assert.That(Console.WaitFor(List, 2), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Clean), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor("New value (optional, Enter leaves it empty): "), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Clean, 2), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor("New value (optional, Enter leaves it empty): ", 2), Is.True, Console.Out);
            Console.Type("  Weekend trip  \r");
            Assert.That(Console.WaitFor(Edited), Is.True, Console.Out);
            Console.Type("12\r");
            Assert.That(Console.WaitFor("Save Alpha.yaml now? [y/N]: "), Is.True, Console.Out);
            Console.Type("y\r");
            Assert.That(Console.WaitFor("Info: Saved Alpha.yaml."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Clean, 3), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("Main menu\n  1. Status\n  2. Notifications\n  3. Settings\n  4. Searches\n  5. Campgrounds\n  6. Results\n  7. Monitoring\n  8. Guided setup\n  9. Help\n  10. Exit\n"));
            Assert.That(Out, Does.Contain("Search files\n  1. Alpha.yaml, First trip \u00B7 Enabled (autoRun) \u00B7 ID 232447, No problems\n  2. New search file\n  3. Look for changes on disk\n  4. Back to the main menu\n"));
            Assert.That(Out, Does.Contain("The campground data is not prepared yet"), "said, never prepared");
            Assert.That(ConsoleProcess.Occurrences(Out, "Cancelled.\n"), Is.EqualTo(2), "Ctrl+C at the name and Esc at the field each cancelled a question");
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            Assert.That(File.ReadAllText(Alpha), Is.EqualTo(AlphaText.Replace("displayName: First trip", "displayName: '  Weekend trip  '")), "the name as typed, through the shared form");
            Assert.That(File.ReadAllText(Alpha + ".bak"), Is.EqualTo(AlphaText));
            Assert.That(Directory.GetFiles(Console.Settings).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Alpha.yaml", "Alpha.yaml.bak", "General.yaml" }), "no new search, no database, no Camp.History, no lock");
            Assert.That(Directory.Exists(Console.Cache) && Directory.GetFileSystemEntries(Console.Cache).Length > 0, Is.False, "nothing searched");
        }

        // v0.4.0's phase 9 (D125): Campgrounds on a real pseudo-terminal without the campground data: the lookup says so and
        // opens nothing, the status reads only, and the refresh's confirmation is No by default (Enter) and cancelled by Esc
        // and Ctrl+C, so nothing is downloaded, prepared, created or started
        [Test]
        public void Interactive_OnATerminal_Campgrounds_LooksUpNothingWithoutData_AndItsRefreshIsNoByDefault()
        {
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            Directory.CreateDirectory(Console.Cache);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\nautoRefresh: false\n");
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);
            const string Menu = "Choose a number from 1 to 4: ";
            const string Start = "Start the refresh now? [y/N]: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("5\r");
            Assert.That(Console.WaitFor(Menu), Is.True, Console.Out);
            Console.Type("1\r");
            //the look-up is a screen of its own: what it said is read before Campgrounds is shown again
            Assert.That(Console.WaitFor(PressEnter), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Menu, 2), Is.True, Console.Out);
            Console.Type("2\r");
            Assert.That(Console.WaitFor(Menu, 3), Is.True, Console.Out);
            Console.Type("3\r");
            Assert.That(Console.WaitFor(Start), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Menu, 4), Is.True, Console.Out);
            Console.Type("3\r");
            Assert.That(Console.WaitFor(Start, 2), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Menu, 5), Is.True, Console.Out);
            Console.Type("3\r");
            Assert.That(Console.WaitFor(Start, 3), Is.True, Console.Out);
            Console.Type("\u0003");
            Assert.That(Console.WaitFor(Menu, 6), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("Campgrounds and the campground data\n  1. Look up a campground\n  2. Campground data status\n  3. Refresh or install campground data\n  4. Back to the main menu\n"));
            //a sentence longer than the terminal's line is wrapped, its next lines indented: compared without the wrapping
            string Unwrapped = System.Text.RegularExpressions.Regex.Replace(Out, "\n +", " ");
            Assert.That(Unwrapped, Does.Contain("The campground data is not prepared yet, so there are no campgrounds to look up."));
            Assert.That(Out, Does.Contain("Campground data status (read only: nothing is changed)\n  Prepared: no: not prepared yet (nothing was opened)\n"));
            Assert.That(Unwrapped, Does.Contain("The campground data is not prepared yet, so the refresh installs the campground data it downloads."));
            Assert.That(ConsoleProcess.Occurrences(Out, "Nothing was started.\n"), Is.EqualTo(3), "Enter, Esc and Ctrl+C");
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out, Does.Not.Contain("Refreshing the campground data"), "nothing was started");
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "no database, lock, download, cache or settings file created or changed");
        }

        // v0.4.0's phase 10 (D126): Results on a real pseudo-terminal, where no request may leave the process: the search
        // files listed with the shared row's words and their choice, Start without the cache folder starts nothing (the
        // console never creates it), Esc and Ctrl+C go back, Ctrl+D ends it; nothing is searched, created, changed or
        // started, and the search file is never written
        [Test]
        public void Interactive_OnATerminal_Results_ListsAndChoosesTheSearches_AndStartsNothingItCantStart()
        {
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\nautoRefresh: false\n");
            string Later = Path.Join(Console.Settings, "Later.yaml");
            const string LaterText = "displayName: Later trip\nautoRun: false\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";
            File.WriteAllText(Later, LaterText);
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);
            const string Menu = "Choose a number from 1 to 6: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("6\r");
            Assert.That(Console.WaitFor(Menu), Is.True, Console.Out);
            Console.Type("2\r");
            Assert.That(Console.WaitFor("The cache folder does not exist yet, so nothing can be searched here."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Menu, 2), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor("Later trip (Later.yaml) is chosen."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Menu, 3), Is.True, Console.Out);
            Console.Type("2\r");
            Assert.That(Console.WaitFor("The cache folder does not exist yet, so nothing can be searched here.", 2), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Menu, 4), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("6\r");
            Assert.That(Console.WaitFor(Menu, 5), Is.True, Console.Out);
            Console.Type("\u0003");
            Assert.That(Console.WaitFor(Choose, 3), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            //a sentence longer than the terminal's line is wrapped, its next lines indented: compared without the wrapping
            string Unwrapped = System.Text.RegularExpressions.Regex.Replace(Out, "\n +", " ");
            Assert.That(Out, Does.Contain("Results (nothing is searched until you choose Start search)\n"));
            Assert.That(Unwrapped, Does.Contain("1. Later trip [not chosen] · Later.yaml · Campground ID 232447 · Not enabled: runs when selected"),
                "the shared row's words; without the campground data the campground by its ID");
            Assert.That(Unwrapped, Does.Contain("1. Later trip [chosen] · Later.yaml"), "chosen as the shared row's rule allows");
            Assert.That(Unwrapped, Does.Contain("The campground data is not prepared yet, so a search can't prepare a campground's campsite details"), "said, never prepared");
            Assert.That(Out, Does.Contain("  2. Start search\n  3. Select enabled\n  4. Select all that can run\n  5. Clear selection\n  6. Back to the main menu\n"));
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out, Does.Not.Contain("Searching 1 search"), "nothing was searched");
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            Assert.That(File.ReadAllText(Later), Is.EqualTo(LaterText), "choosing it never changes its autoRun");
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "no cache folder, database, Camp.History, lock or settings file created or changed");
        }

        // v0.4.0's phase 11 (D127): Monitoring on a real pseudo-terminal, where no request may leave the process: the state in
        // the shared words, Start asked first (No by default: Enter), Start without the cache folder starts nothing (the
        // console never creates it), and with it the controller's refusal when General.yaml turns no monitoring on (said as
        // information), Esc back to the main menu, Exit without a question (nothing is on), exit code 0; nothing searched,
        // locked, created or changed
        [Test]
        public void Interactive_OnATerminal_Monitoring_AsksBeforeStart_AndAStartWithoutAMonitoringStartsNothing()
        {
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\nautoRefresh: false\n");
            const string Menu = "Choose a number from 1 to 3: ";
            const string Start = "Start the monitoring now? [y/N]: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("7\r");
            Assert.That(Console.WaitFor(Menu), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Start), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Menu, 2), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Start, 2), Is.True, Console.Out);
            Console.Type("y\r");
            Assert.That(Console.WaitFor("The cache folder does not exist yet, so the monitoring can't start here."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Menu, 3), Is.True, Console.Out);
            Directory.CreateDirectory(Console.Cache);
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(Start, 3), Is.True, Console.Out);
            Console.Type("y\r");
            Assert.That(Console.WaitFor("Status: Not running: the last start did not start it."), Is.True, Console.Out);
            Assert.That(Console.WaitFor(Menu, 4), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("10\r");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            string Unwrapped = System.Text.RegularExpressions.Regex.Replace(Out, "\n +", " ");
            Assert.That(Out, Does.Contain("Monitoring (nothing starts or stops until you choose it)\n"));
            Assert.That(Out, Does.Contain("  Status: Not running.\n"));
            Assert.That(Out, Does.Contain("Start and stop\n  1. Start\n  2. Show the latest status\n  3. Back to the main menu\n"));
            Assert.That(Unwrapped, Does.Contain("Info: "), "no monitoring turned on: information, not an error");
            Assert.That(ConsoleProcess.Occurrences(Out, "Nothing was started.\n"), Is.EqualTo(1), "Enter: No");
            Assert.That(Out, Does.Not.Contain("The monitoring is running").And.Not.Contain("Stopping the monitoring"), "Exit asks nothing: nothing is on");
            Assert.That(Out, Does.Not.Contain("Stopped."));
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "no lock, database, Camp.History, cache or settings file created or changed");
        }

        // D130 (PT1's console findings): on a fresh computer the guided setup offers to create the settings folder, creates
        // it only after a y, by the first start's own preparation (the folders, General.yaml, Email.yaml and the bundled
        // campground data: nothing else, no search), and goes on into its first step; the main menu itself created nothing
        [Test]
        public void Interactive_OnATerminal_GuidedSetup_CreatesTheSettingsFolderAfterAYes_AndGoesOnIntoItsFirstStep()
        {
            using ConsoleProcess Console = OnTerminal();

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Assert.That(Directory.Exists(Console.Settings), Is.False, "the main menu creates nothing");
            Console.Type("8\r");
            Assert.That(Console.WaitFor("Create the settings folder now? [y/N]: "), Is.True, Console.Out);
            Console.Type("y\r");
            Assert.That(Console.WaitFor("Step 1 of 4: How you are notified"), Is.True, Console.Out);
            Assert.That(Console.WaitFor("Choose a number from 1 to ", 2), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor(PressEnter), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("The settings folder was created. Guided setup goes on with the saved files."));
            Assert.That(Directory.GetFiles(Console.Settings).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal),
                Is.EqualTo(new[] { "Email.yaml", "General.yaml", "RecreationDotOrg.db" }), "what a first start creates: no search, no Camp.History, no lock");
            Assert.That(Directory.Exists(Console.Cache), Is.True);
            Assert.That(File.ReadAllText(Path.Join(Console.Settings, "General.yaml")), Does.Contain("outputTo: Console").And.Contain("autoRefresh: false"));
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: no control sequence");
        }

        // D130: on a terminal that understands control sequences each new screen starts on a cleared screen (the visible
        // screen only, by the no-argument menu's own sequence) and the main menu shows the branding before its title
        [Test]
        public void Interactive_OnAnXtermTerminal_StartsEachNewScreenCleared_WithTheBrandingAtTheMainMenusTop()
        {
            using ConsoleProcess Console = OnTerminal();

            Console.StartOnTerminal(new[] { "--interactive" }, ("TERM", "xterm-256color"), ("NO_COLOR", "1"));
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(PressEnter), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("10\r");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            const string Clear = "\u001b[H\u001b[2J";
            //.NET's own keypad sequences on an xterm (application keypad on, and off at the end) are the runtime's, not Campertron's
            Out = Out.Replace("\u001b[?1h\u001b=", string.Empty).Replace("\u001b[?1l\u001b>", string.Empty);
            Assert.That(Out, Does.StartWith(Clear), "cleared before the first screen, nothing shown before it");
            Assert.That(Out.Split(Clear).Length - 1, Is.EqualTo(3), "the main menu, Status, the main menu again");
            string First = Out.Split(Clear)[1];
            Assert.That(First.IndexOf("Campertron interactive console", StringComparison.Ordinal), Is.GreaterThan(0));
            Assert.That(First.Substring(0, First.IndexOf("Campertron interactive console", StringComparison.Ordinal)),
                Does.Contain("  ____").Or.Contain("== CAMPERTRON ==").Or.StartWith("Campertron\n"), "the branding first");
            Assert.That(Out.Replace(Clear, string.Empty).Contains('\u001b'), Is.False, "NO_COLOR: no other control sequence");
            Assert.That(Directory.Exists(Console.Settings), Is.False, "nothing created");
        }

        // v0.4.0's phase 12 (D128): Guided setup on a real pseudo-terminal: it opens where the saved files say (step 1: nothing
        // is ready), the shared words, a method chosen leads to its form (Email.yaml's, with Save and continue where Save is),
        // Esc at the form leaves the guided setup (nothing changed: no question), the end of the input ends the console with 0;
        // nothing created, saved, sent or started
        [Test]
        public void Interactive_OnATerminal_GuidedSetup_OpensWhereTheFilesSay_AndEscLeavesIt_ChangingNothing()
        {
            using ConsoleProcess Console = OnTerminal();
            Directory.CreateDirectory(Console.Settings);
            File.WriteAllText(Path.Join(Console.Settings, "General.yaml"), "outputTo: Console\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(Console.Settings, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsendFromAddress: from@example.invalid\n");
            List<string> Before = ConsoleProcess.Snapshot(Console.Root);
            const string Methods = "Choose a number from 1 to 3: ";

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            Console.Type("8\r");
            Assert.That(Console.WaitFor(Methods), Is.True, Console.Out);
            Console.Type("1\r");
            Assert.That(Console.WaitFor(". Leave guided setup", 2), Is.True, Console.Out);
            Assert.That(Console.WaitFor("Choose a number from 1 to ", 3), Is.True, Console.Out);
            Console.Type("\u001b");
            Assert.That(Console.WaitFor("You left guided setup"), Is.True, Console.Out);
            Assert.That(Console.WaitFor(PressEnter), Is.True, Console.Out);
            Console.Type("\r");
            Assert.That(Console.WaitFor(Choose, 2), Is.True, Console.Out);
            Console.Type("\u0004");
            int Code = Console.Exit();

            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Code, Is.Zero, Out);
            Assert.That(Out, Does.Contain("Guided setup (nothing is saved, sent or started until you choose to)\n"));
            Assert.That(Out, Does.Contain("Step 1 of 4: How you are notified\n  Step 1 of 4: How you are notified (current step)\n"));
            Assert.That(Out, Does.Contain("How do you want to be told about new openings?\n  1. By email: "));
            Assert.That(Out, Does.Contain("Step 2 of 4: Email settings\n"));
            Assert.That(Out, Does.Contain(". Save and continue\n").And.Contain(". Back\n").And.Not.Contain(". Save\n"), "Save and continue stands where Save is");
            Assert.That(Out, Does.Not.Contain("Leave without saving"), "nothing was changed: no question");
            Assert.That(Out.Contains('\u001b'), Is.False, "TERM=dumb: not one escape sequence (an ordinal check)");
            Assert.That(Out, Does.EndWith("The input ended, so the interactive console ended.\n"));
            Assert.That(ConsoleProcess.Snapshot(Console.Root), Is.EqualTo(Before), "no settings, search, history, lock, database or cache file created or changed");
        }

        [Test]
        public void Interactive_OnATerminal_CtrlCAtTheMainMenu_StopsWith130()
        {
            using ConsoleProcess Console = OnTerminal();

            Console.StartOnTerminal(new[] { "--interactive" });
            Assert.That(Console.WaitFor(Choose), Is.True, Console.Out);
            //TD-68 (§EA): the prompt is written a moment before the console starts reading keys (and only then reads Ctrl+C as a
            //key), so a Ctrl+C typed at once can reach the terminal as the process's signal (CI #385: "...: Stopped." without
            //the line break). The terminal echoes nothing itself (script's --echo never): a harmless visible key echoed back is
            //the console's own echo, so it is reading keys, Ctrl+C a key. "x" is no choice and has no Enter after it
            Console.Type("x");
            Assert.That(Console.WaitFor(Choose + "x"), Is.True, "the console read and echoed the harmless key: " + Console.Out);
            Console.Type("\u0003");
            int Code = Console.Exit();

            Assert.That(Code, Is.EqualTo(130), Console.Out);
            string Out = Console.Out.Replace("\r\n", "\n");
            Assert.That(Out, Does.EndWith(Choose + "x\nStopped.\n"), "Ctrl+C was read as a key: the line ends, then Stopped.");
            Assert.That(Out.Split("Main menu").Length - 1, Is.EqualTo(1), "the harmless key chose nothing: one main menu and no other screen");
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
