using System.Net.Mail;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // The console's email and ntfy mode as the monitoring service's adapter (decisions D83 and D90): the lines of a run on
    // standard output, in their order; the monitoring lock at the console (another monitoring of the same settings folder
    // refuses the start with one error and exit code 1; a refused start creates no lock file); a failed send and a blocked
    // request, which end the program as they did before the service. What the console says and returns for every other
    // start is pinned, unchanged, by EmailStartTests, NtfyStartTests, SmtpPasswordEmailModeTests,
    // EmailOutputValidationTests and MonitoringSearchCheckTests.
    public class MonitoringConsoleTests : MonitoringTestBase
    {
        private sealed class StopTheLoop : Exception
        {
        }

        private int Waits;
        // the wait between two checks: the first PassWaits return at once, the next one stops the loop
        private int PassWaits;
        private Action<int>? OnWait;
        private void CheckWait(TimeSpan Delay, CancellationToken Token)
        {
            Assert.That(Delay, Is.EqualTo(TimeSpan.FromMinutes(1)), "checks are one minute apart");
            Waits++;
            OnWait?.Invoke(Waits);
            if (Waits > PassWaits)
            {
                throw new StopTheLoop();
            }
        }

        [SetUp]
        public void SetUp()
        {
            Waits = 0;
            PassWaits = 0;
            OnWait = null;
        }

        private static string[] Lines(StringWriter Writer) =>
            Writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Where(L => L.Length > 0).ToArray();

        private const string Stamp = @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  ";

        // ---- a run's lines ----

        [Test]
        public void ARunsLines_AreTheLoopsLines_InTheirOrder_AndEveryStartCountsItsChecksFromOne()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            PassWaits = 1;

            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings(Ntfy: true), CheckWait), "the second wait stopped the loop");

            string[] Written = Lines(Out);
            Assert.That(Written[0], Does.Match(Stamp + @"\S+ \S+ \| \.NET .* \| settings .* \| cache .* \| output Email and ntfy$"), "the start line (the program's name is the test host's here)");
            Assert.That(Written[1], Is.EqualTo("Running 1 search:"));
            int Check = Array.FindIndex(Written, L => L.Contains("  Check "));
            Assert.That(Check, Is.GreaterThan(1));
            Match First = Regex.Match(Written[Check], Stamp + @"Check 1: 1 search, (\d+) new openings; sending 1 email and 1 ntfy notification\.$");
            Assert.That(First.Success, Is.True, Written[Check]);
            string Count = First.Groups[1].Value;
            Assert.That(Written[Check + 1], Does.Match(Stamp + "Email sent: Trip \\(" + Count + " openings\\)$"));
            Assert.That(Written[Check + 2], Does.Match(Stamp + "ntfy notification sent: Trip \\(" + Count + " openings\\)$"));
            Assert.That(Written[Check + 3], Does.Match(Stamp + @"Next check \d{2}:\d{2}:\d{2}\.$"));
            Assert.That(Written[Check + 4], Does.Match(Stamp + @"Check 2: 1 search, nothing new\. Next check \d{2}:\d{2}:\d{2}\.$"));
            Assert.That(Written, Has.Length.EqualTo(Check + 5), "nothing after the second check's line");
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That((Emails.Count, Published.Count, Waits), Is.EqualTo((1, 1, 2)));
            Assert.That(Out.ToString(), Does.Not.Contain(Password).And.Not.Contain(Topic));

            // a new start is a new monitoring: its checks count from 1 again (before the service the number was the process's)
            Out.GetStringBuilder().Clear();
            Waits = 0;
            PassWaits = 0;
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings(Ntfy: true), CheckWait));
            Assert.That(Lines(Out).Last(), Does.Match(Stamp + @"Check 1: 1 search, nothing new\. Next check \d{2}:\d{2}:\d{2}\.$"));
        }

        // ---- the monitoring lock at the console ----

        [Test]
        public void TheConsolesMonitoring_HoldsTheLockWhileItRuns_AndItsFileIsTheOnlyNewFile()
        {
            WriteSearch("Trip.yaml", "Trip");
            List<string> Before = Directory.GetFileSystemEntries(ConfigPath).Select(p => Path.GetFileName(p)!).ToList();
            bool? HeldDuringTheRun = null;
            OnWait = Number => HeldDuringTheRun = LockIsHeld();

            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings(), CheckWait));

            Assert.That(HeldDuringTheRun, Is.True, "held between two checks");
            Assert.That(LockIsHeld(), Is.False, "released when the monitoring ended");
            Assert.That(Directory.GetFileSystemEntries(ConfigPath).Select(p => Path.GetFileName(p)!), Is.EquivalentTo(Before.Append(MonitoringLock.FileName)),
                "nothing new was found, so no Camp.History: only the lock's file");
            Assert.That(new FileInfo(LockPath).Length, Is.EqualTo(0));
        }

        private static readonly string[] HeldWords =
        {
            " can't start: another Campertron is already monitoring this settings folder.",
            "  Only one monitoring runs for a settings folder, so that every opening is notified once. Stop the other one first (another CampertronConsole in email or ntfy mode, or a container that uses the same settings folder), or give this one its own settings folder.",
            "  Nothing was searched or sent."
        };

        [TestCase(OutputType.Email, "email mode", "Email")]
        [TestCase(OutputType.Ntfy, "ntfy mode", "Ntfy")]
        public void ASecondMonitoring_OfTheSameSettingsFolder_IsRefused_WithOneErrorAndExitCode1(OutputType Output, string Mode, string OutputText)
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            CampertronInternalConfig Config = Settings(Output, Ntfy: Output == OutputType.Ntfy);
            using (FileStream? Other = MonitoringLock.TryAcquire(ConfigPath, out _))
            {
                Assert.That(Other, Is.Not.Null, "another monitoring holds the settings folder");

                Assert.That(Load.RunConsoleSearch(Config, CheckWait), Is.False, "the start reports that the monitoring did not run");

                Assert.That(Lines(Err), Is.EqualTo(new[] { "Error: " + Mode + HeldWords[0], HeldWords[1], HeldWords[2] }), "one error on standard error");
                string[] Written = Lines(Out);
                Assert.That(Written, Has.Length.EqualTo(1), "standard output holds only the start line: " + Out);
                Assert.That(Written[0], Does.Match(Stamp + @".* \| output " + OutputText + "$"));
                Assert.That(Out.ToString(), Does.Not.Contain("Running").And.Not.Match(@"Check \d+:"));
                Assert.That((Requested.Count, Emails.Count, Published.Count, Waits, Markers().Count), Is.EqualTo((0, 0, 0, 0, 0)),
                    "nothing asked of recreation.gov, nothing sent, no wait, nothing recorded");
                Assert.That(Err.ToString(), Does.Not.Contain(Root).And.Not.Contain(Password).And.Not.Contain(Topic));

                Err.GetStringBuilder().Clear();
                Assert.That(Load.RunConsole(Config, CheckWait), Is.EqualTo(1), "exit code 1, as for every start that is refused");
                Assert.That(Lines(Err)[0], Is.EqualTo("Error: " + Mode + HeldWords[0]));
            }

            // the other monitoring ended: the same start now runs, and sends what the refused one did not
            Err.GetStringBuilder().Clear();
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Config, CheckWait));
            Assert.That(Err.ToString(), Is.Empty);
            Assert.That(Emails.Count + Published.Count, Is.EqualTo(1));
        }

        [Test]
        [Platform("Linux,MacOsX")]
        public void ASettingsFolderThatCannotHoldTheLock_RefusesTheStart_WithOneErrorAndExitCode1()
        {
            if (OperatingSystem.IsWindows())
            {
                //never reached (the Platform attribute); it tells the platform analyzer so
                return;
            }
            if (Environment.UserName == "root")
            {
                Assert.Ignore("root writes to a read-only folder");
            }
            WriteSearch("Trip.yaml", "Trip");
            CampertronInternalConfig Config = Settings();
            File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.That(Load.RunConsole(Config, CheckWait), Is.EqualTo(1));

            Assert.That(Lines(Err), Is.EqualTo(new[]
            {
                "Error: email mode can't start: the monitoring's lock file can't be created or opened in the settings folder.",
                "  Campertron keeps the file Campertron.monitoring.lock open there while it monitors, so that only one monitoring runs for a settings folder. Let the user that runs Campertron write to the settings folder.",
                "  Nothing was searched or sent."
            }));
            Assert.That((Requested.Count, Emails.Count, Waits), Is.EqualTo((0, 0, 0)));
            Assert.That(File.Exists(LockPath), Is.False);
        }

        // D64's promise holds with the lock: a start that is refused writes and creates nothing, the lock's file included
        [Test]
        public void AStartRefusedForAnotherReason_CreatesNoLockFile()
        {
            Assert.That(Load.RunConsoleSearch(Settings(), CheckWait), Is.False, "no search file");
            Assert.That(Lines(Err)[0], Is.EqualTo("Error: email mode has no search to check, so it stopped: the settings folder has no search file."));
            Assert.That(Directory.GetFileSystemEntries(ConfigPath), Is.Empty, "nothing created: no lock file, no example search");

            Err.GetStringBuilder().Clear();
            WriteSearch("Trip.yaml", "Trip");
            CampertronInternalConfig Incomplete = Settings();
            Incomplete.EmailConfig = new EmailConfig();
            Assert.Throws<ConfigValidationException>(() => Load.RunConsoleSearch(Incomplete, CheckWait));
            Assert.That(File.Exists(LockPath), Is.False);
        }

        // ---- failures end the program as before ----

        [Test]
        public void AFailedSend_IsThrownToTheProgram_NothingIsRecorded_AndTheLockIsReleased()
        {
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException("the test refused the email");
            CampertronInternalConfig Config = Settings();

            SmtpException Failure = Assert.Throws<SmtpException>(() => Load.RunConsoleSearch(Config, CheckWait))!;

            Assert.That(Failure.Message, Is.EqualTo("the test refused the email"), "the send's own exception, unchanged");
            Assert.That(Waits, Is.EqualTo(0), "the monitoring stopped: no wait for a next check");
            Assert.That(Markers(), Is.Empty, "the undelivered openings are not recorded");
            Assert.That(Out.ToString(), Does.Contain("sending 1 email.").And.Not.Contain("Email sent").And.Not.Contain("Next check"));
            Assert.That(Err.ToString(), Is.Empty, "the program reports it (Program.Main)");
            Assert.That(LockIsHeld(), Is.False);
            // what Program.Main then writes, and its exit code
            Assert.That(ConsoleUi.ReportFailure(Failure, Config), Is.EqualTo(1));
            Assert.That(Lines(Err), Is.EqualTo(new[]
            {
                "Error: the email could not be sent: the test refused the email",
                "  Check the server, port, user name and password in Email.yaml."
            }));
        }

        [Test]
        public void ABlockedRequest_KeepsTheConsolesErrorItsFiveMinuteWaitAndExitCode1()
        {
            WriteSearch("Trip.yaml", "Trip");
            int Asked = 0;
            Answer = Url => Interlocked.Increment(ref Asked) == 1 ? "<html><body><h1>Request blocked.</h1></body></html>" : NoLiveNetwork.EmptyMonth;

            // the test's exit returns, so the run goes on to its wait; the program's exit ends the process there
            Assert.Throws<StopTheLoop>(() => Load.RunConsoleSearch(Settings(), CheckWait));

            Assert.That(BlockedSteps, Is.EqualTo(new[] { "wait 00:05:00", "exit 1" }), "the 5-minute wait, then exit code 1");
            Assert.That(Lines(Err), Has.Length.EqualTo(1));
            Assert.That(Lines(Err)[0], Does.Match(@"^Error: Blocked by API for exceeding request limit, try again later \(FIXTURE CAMPGROUND, .*\)\. Campertron waits 5 minutes and then exits\.$"));
        }

        // each event of the service, as the console writes it: status and log lines on standard output, a warning on
        // standard error with its prefix
        [Test]
        public void EveryEvent_IsWrittenAsTheConsoleAlwaysWroteIt()
        {
            CampertronInternalConfig Config = Settings();

            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.Status) { Text = "Preparing campsite details for FIXTURE CAMPGROUND..." });
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.Warning) { Text = "Heavy traffic at recreation.gov (FIXTURE CAMPGROUND, January 2027); retrying in 1 second." });
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.CheckReported) { CheckNumber = 3, SearchCount = 2, NewOpenings = 8, Emails = 2, NtfyMessages = 1 });
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.EmailSent) { SearchName = "Trip", Openings = 1 });
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.NtfySent) { SearchName = "Trip", Openings = 2 });
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.DeliveriesFinished));
            MonitoringConsole.Write(Config, new MonitoringEvent(MonitoringEventKind.CheckReported) { CheckNumber = 4, SearchCount = 1, NewOpenings = 0, Emails = 1 });

            Assert.That(Lines(Err), Is.EqualTo(new[] { "Warning: Heavy traffic at recreation.gov (FIXTURE CAMPGROUND, January 2027); retrying in 1 second." }));
            string[] Written = Lines(Out);
            Assert.That(Written[0], Is.EqualTo("Preparing campsite details for FIXTURE CAMPGROUND..."));
            Assert.That(Written[1], Does.Match(Stamp + @"Check 3: 2 searches, 8 new openings; sending 2 emails and 1 ntfy notification\.$"));
            Assert.That(Written[2], Does.Match(Stamp + @"Email sent: Trip \(1 opening\)$"));
            Assert.That(Written[3], Does.Match(Stamp + @"ntfy notification sent: Trip \(2 openings\)$"));
            Assert.That(Written[4], Does.Match(Stamp + @"Next check \d{2}:\d{2}:\d{2}\.$"));
            Assert.That(Written[5], Does.Match(Stamp + @"Check 4: 1 search, nothing new\. Next check \d{2}:\d{2}:\d{2}\.$"));
            Assert.That(Written, Has.Length.EqualTo(6));
        }
    }
}
