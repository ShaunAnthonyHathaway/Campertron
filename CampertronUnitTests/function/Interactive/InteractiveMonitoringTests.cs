using System.Collections.Concurrent;
using System.Net.Mail;
using System.Reflection;
using System.Security.Cryptography;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.Interactive.Monitoring;
using CampertronLibrary.function.Interactive.Results;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronUnitTests.function.Base;
using CampertronUnitTests.function.RecDotOrg.monitoring;
using CW = CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundsWording;
using MW = CampertronLibrary.function.Forms.MonitoringWording;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Monitoring (v0.4.0's phase 11, D127; business-rules §43): the unattended monitoring through the
    // shared MonitoringController (D116), owned by the console's session and by no menu, driven from the main menu by the
    // terminal that answers a choice by its label (MenuTerminal), and judged by the shared words shown, the files left, what
    // was sent and recorded, and the controller's own state. recreation.gov is answered in memory through the console's
    // download seam (no request leaves the process, NoLiveNetwork); the campground's campsite details are written into the
    // cache by hand (no database is opened); an email and an ntfy notification are recorded by their send seams (no server);
    // the waits between two checks are the test's; the signals that end the process are recorded and raised by the test.
    [NonParallelizable]
    public class InteractiveMonitoringTests : MonitoringTestBase
    {
        private static readonly DateTime Fixed = new DateTime(2026, 11, 26, 9, 30, 0);
        private const string BackToMain = "Back to the main menu";
        private const string Canary = "cnry-monitoring-7d2a";

        private InteractiveMonitoringSeams OriginalSeams = null!;
        private InteractiveResultsSeams OriginalResults = null!;
        private Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>, CancellationToken, RidbRefreshResult> OriginalRefresh = null!;
        private TimeProvider OriginalRefreshTime = null!;
        private TimeSpan OriginalRefreshPoll;
        private Func<Action, IDisposable> OriginalRefreshStop = null!;
        private CampgroundLookupSource OriginalSource = null!;
        private Action<string> OriginalRemove = null!;
        private readonly ConcurrentQueue<string> Removed = new();
        private readonly List<InteractiveSession> Sessions = new();
        private Waits Waited = null!;
        private ManualTime Clock = null!;
        private int Prepared;
        private bool DataPrepared;
        private Action? Ending;
        private int EndingRegistrations;
        private int EndingDisposals;

        [SetUp]
        public void Seams()
        {
            OriginalSeams = InteractiveMonitoring.Seams;
            OriginalResults = InteractiveResults.Seams;
            OriginalRefresh = InteractiveCampgroundRefresh.Service;
            OriginalRefreshTime = InteractiveCampgroundRefresh.Time;
            OriginalRefreshPoll = InteractiveCampgroundRefresh.PollInterval;
            OriginalRefreshStop = InteractiveCampgroundRefresh.WhenAskedToStop;
            OriginalSource = InteractiveCampgrounds.Source;
            OriginalRemove = NotificationHistory.RemoveMarker;
            Removed.Clear();
            NotificationHistory.RemoveMarker = Marker =>
            {
                Removed.Enqueue(Marker);
                OriginalRemove(Marker);
            };
            Sessions.Clear();
            Waited = new Waits();
            Clock = new ManualTime();
            Prepared = 0;
            DataPrepared = true;
            Ending = null;
            EndingRegistrations = 0;
            EndingDisposals = 0;
            InteractiveMonitoring.Seams = With();
            InteractiveResults.Seams = new InteractiveResultsSeams
            {
                Prepared = Folder => false,
                Campgrounds = Folder => new List<CampgroundPickerEntry>(),
                Time = Clock,
                Options = new SearchHostOptions
                {
                    Transport = (Request, Token) =>
                    {
                        Requested.Enqueue(Request.Url);
                        return new SearchResponse(200, Answer(Request.Url));
                    },
                    RetryDelay = TimeSpan.Zero,
                    HeavyTrafficDelay = TimeSpan.Zero
                },
                PollInterval = TimeSpan.FromMilliseconds(1),
                WhenAskedToStop = Act => new Undo(() => { })
            };
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) => throw new AssertionException("no refresh was scripted");
            InteractiveCampgroundRefresh.Time = Clock;
            InteractiveCampgroundRefresh.PollInterval = TimeSpan.FromMilliseconds(1);
            InteractiveCampgroundRefresh.WhenAskedToStop = Act => new Undo(() => { });
            InteractiveCampgrounds.Source = new CampgroundLookupSource { Prepared = Folder => false, Campgrounds = Folder => new List<CampgroundPickerEntry>() };
        }

        // no test leaves a monitoring behind: its thread would use the next test's seams
        [TearDown]
        public void RestoreSeams()
        {
            foreach (InteractiveSession Session in Sessions)
            {
                Session.Monitoring?.Controller.Shutdown();
                Session.Monitoring?.Dispose();
            }
            InteractiveMonitoring.Seams = OriginalSeams;
            InteractiveResults.Seams = OriginalResults;
            InteractiveCampgroundRefresh.Service = OriginalRefresh;
            InteractiveCampgroundRefresh.Time = OriginalRefreshTime;
            InteractiveCampgroundRefresh.PollInterval = OriginalRefreshPoll;
            InteractiveCampgroundRefresh.WhenAskedToStop = OriginalRefreshStop;
            InteractiveCampgrounds.Source = OriginalSource;
            NotificationHistory.RemoveMarker = OriginalRemove;
        }

        private sealed class Undo : IDisposable
        {
            private readonly Action Do;
            internal Undo(Action Do) => this.Do = Do;
            public void Dispose() => Do();
        }

        // a clock the tests move: the hold after a block lasts, and the menu's wait for an action ends, only when a test moves it
        private sealed class ManualTime : TimeProvider
        {
            private long Ticks;
            public override long GetTimestamp() => Interlocked.Read(ref Ticks);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            internal void Advance(TimeSpan By) => Interlocked.Add(ref Ticks, By.Ticks);
        }

        // a clock that moves a second at each look (the menu's wait for an action reaches its bound after ten looks)
        private sealed class Ticking : TimeProvider
        {
            private long Ticks;
            internal int Looks;
            public override long GetTimestamp()
            {
                Interlocked.Increment(ref Looks);
                return Interlocked.Add(ref Ticks, TimeSpan.TicksPerSecond);
            }
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        }

        // the waits between two checks: each holds until the monitoring is stopped or the test lets the next check begin
        private sealed class Waits
        {
            private readonly AutoResetEvent _Next = new AutoResetEvent(false);
            internal readonly SemaphoreSlim Began = new SemaphoreSlim(0);
            internal void Wait(TimeSpan Delay, CancellationToken Token)
            {
                Began.Release();
                WaitHandle.WaitAny(new[] { Token.WaitHandle, _Next });
            }
            internal void Await() => Assert.That(Began.Wait(Long), Is.True, "a wait between two checks began");
            internal void Next() => _Next.Set();
        }

        private InteractiveMonitoringSeams With(TimeSpan? ActionWait = null) => new InteractiveMonitoringSeams
        {
            Now = () => Fixed,
            Wait = Waited.Wait,
            Time = Clock,
            Prepared = Folder => DataPrepared,
            PrepareCampgroundData = (Config, Cache) => Interlocked.Increment(ref Prepared),
            PollInterval = TimeSpan.FromMilliseconds(1),
            ActionWait = ActionWait ?? TimeSpan.FromSeconds(10),
            EndWait = Long,
            WhenEnding = Act =>
            {
                Ending = Act;
                EndingRegistrations++;
                return new Undo(() => EndingDisposals++);
            }
        };

        private void WriteSettings(string OutputTo = "Email", bool WithEmail = true)
        {
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: " + OutputTo + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            if (WithEmail)
            {
                File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: tester\nsmtpPassword: " + Password
                    + "\nsendToAddressList:\n  - to@example.invalid\nsendFromAddress: from@example.invalid\n");
            }
        }

        // ---- the script ----

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static Action Do(Action Act) => Act;
        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private object[] StartIt => new object[] { P(W.Monitoring), P(MW.StartLabel), Yes, Do(Waited.Await) };
        private static readonly object[] BackToExit = { P(BackToMain), P("Exit") };

        private InteractiveSession Session(MenuTerminal Terminal, string? Cache = "", bool NoFolder = false)
        {
            var Made = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => DateTime.UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : ConfigPath, CachePath = Cache == "" ? CachePath : Cache });
            Sessions.Add(Made);
            return Made;
        }

        private int Run(MenuTerminal Terminal, InteractiveSession? With_ = null) => InteractiveConsole.Run(With_ ?? Session(Terminal));

        private static MonitoringController ControllerOf(InteractiveSession Session) => Session.Monitoring!.Controller;

        // every status block the menu showed: its lines between Monitoring's heading and its choices (the introduction left out)
        private static List<List<string>> Blocks(MenuTerminal Terminal)
        {
            var Found = new List<List<string>>();
            List<string>? Current = null;
            foreach ((string Kind, string Text) in Terminal.Written)
            {
                if (Kind == "heading" && Text == W.MonitoringTitle)
                {
                    Current = new List<string>();
                    continue;
                }
                if (Current != null && Kind == "heading")
                {
                    Found.Add(Current);
                    Current = null;
                    continue;
                }
                if (Current != null && Text.Length > 0 && Text != W.Intro)
                {
                    Current.Add(Text);
                }
            }
            return Found;
        }

        private static string When(int Minutes = 0) => MW.When(Fixed.AddMinutes(Minutes));

        private static readonly string[] Idle = { "  Status: Not running.", "Stop and Restart are available while the monitoring runs." };

        private string[] Running(int Check = 1, string Summary = "1 search, nothing new to notify.", string Notifications = "Email") => new[]
        {
            "  Status: Running: the next check starts at " + When(1) + ".",
            "  Started: " + When(),
            "  Notifications: " + Notifications,
            "  Searches checked: 1 (the Enabled (autoRun) searches it read when it started)",
            "  Last finished check: Check " + Check + " at " + When(),
            "  Next check: " + When(1),
            "  The last check: Check " + Check + " at " + When() + ": " + Summary,
            "Start is unavailable while the monitoring runs: stop or restart it instead."
        };

        private static Dictionary<string, string> Fingerprint(string Of) =>
            Directory.GetFileSystemEntries(Of, "*", SearchOption.AllDirectories).Where(P_ => Path.GetFileName(P_) != MonitoringLock.FileName).ToDictionary(P_ => P_,
                P_ => File.Exists(P_) ? System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(P_))) + File.GetLastWriteTimeUtc(P_).Ticks : "dir");

        private void AssertSafe(MenuTerminal Terminal)
        {
            string All = Terminal.AllText;
            Assert.That(All.Contains(Password) || All.Contains(Topic) || All.Contains(Canary), Is.False, "no secret, topic or server's text");
            Assert.That(All, Does.Not.Contain("Exception").And.Not.Contain("   at "), "no exception's name or stack trace");
            Assert.That(All.Any(C => char.IsControl(C) && C != '\n'), Is.False, "no control character reaches the terminal");
            AssertNothingOnTheConsole();
        }

        // ---- the main menu, entering Monitoring ----

        [Test]
        public void TheMainMenu_HasMonitoringAfterResults_AndBeforeHelp_AndHelpSaysWhatItDoes()
        {
            Assert.That(InteractiveConsole.MainMenu().Select(I => I.Label), Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Help", "Exit" }));
            Assert.That(W.Monitoring, Is.EqualTo(MW.Heading), "the shared name");
            string Help = string.Join("\n", InteractiveWording.HelpLines);
            Assert.That(Help, Does.Contain("Monitoring: start, stop and restart the unattended monitoring"));
            Assert.That(Help, Does.Contain("never by itself, and a later start of the console does not start it again"));
            Assert.That(Help, Does.Contain("Exit asks before it does"));
        }

        [Test]
        public void EnteringMonitoring_ShowsItsState_AndStartsReadsAndWritesNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Dictionary<string, string> Before = Fingerprint(ConfigPath);
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(W.ShowLatest) }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.Intro));
            Assert.That(Blocks(Terminal), Is.EqualTo(new[] { Idle, Idle }), "the state twice, in the shared words, and nothing else");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading), Has.All.EqualTo(new[] { MW.StartLabel, W.ShowLatest, BackToMain }), "only what is available now");
            Assert.That(ControllerOf(Console_).View().Generation, Is.Zero, "nothing started");
            Assert.That(File.Exists(LockPath), Is.False, "no lock taken or even created");
            Assert.That((Requested.Count, Prepared, Emails.Count, LockIsHeld()), Is.EqualTo((0, 0, 0, false)));
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(Before), "nothing read into a file, written or created");
            AssertSafe(Terminal);
        }

        [TestCase(true, false, W.NoFolder)]
        [TestCase(false, false, W.FolderMissing)]
        [TestCase(false, true, W.ContainerFolderMissing)]
        public void WithoutTheSettingsFolder_MonitoringSaysSo_AndMakesNoMonitoring(bool NoFolder, bool Container, string Said)
        {
            if (!NoFolder)
            {
                Directory.Delete(ConfigPath, true);
            }
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring) }, new object[] { P("Exit") }));
            InteractiveSession Console_ = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => DateTime.UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : ConfigPath, CachePath = CachePath, ContainerMode = Container });
            Sessions.Add(Console_);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Said));
            Assert.That((Console_.Monitoring, EndingRegistrations), Is.EqualTo(((InteractiveMonitoringHost?)null, 0)), "no monitoring and no signal handling");
            if (!NoFolder)
            {
                Assert.That(Directory.Exists(ConfigPath), Is.False, "never created");
            }
        }

        // ---- the architecture: one monitoring, the console's, through the shared controller ----

        [Test]
        public void TheConsole_HasOneMonitoring_MadeOnce_TheSessionsAndNoMenus_AndItGoesOnAcrossVisits()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveMonitoringHost? First = null;
            InteractiveSession Console_ = null!;
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), Do(() => First = Console_.Monitoring), P(W.Monitoring), P(BackToMain), P(W.Monitoring), P(MW.StopLabel), P(BackToMain), P("Exit") }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(First, Is.Not.Null);
            Assert.That(Console_.Monitoring, Is.SameAs(First), "the same monitoring at every visit: never one of a menu");
            Assert.That(EndingRegistrations, Is.EqualTo(1), "made once");
            List<List<string>> Shown = Blocks(Terminal);
            Assert.That(Shown[1], Is.EqualTo(Running()), "the second visit shows the running monitoring of the first");
            Assert.That(Shown[2], Is.EqualTo(Running()), "and so does the third");
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1), "one start, never made again");
        }

        private static List<string> Reached()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var Types = new Stack<Type>(typeof(InteractiveMonitoring).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveMonitoring).Namespace));
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheMonitoringCode_ReachesTheSharedControllerAndWords_AndNoServiceLockHistoryOrEngineOfItsOwn()
        {
            List<string> Called = Reached();
            string[] Forbidden =
            {
                //no monitoring, lock, history, notification or search of its own: the shared controller's
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.api.", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch",
                "CampertronLibrary.function.RecDotOrg.search.SearchHost", "CampertronLibrary.function.RecDotOrg.sqlite.", "CampertronLibrary.function.RecDotOrg.data.Cache",
                "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole", "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::DbExistsCheck",
                "CampertronLibrary.function.RecDotOrg.data.Load::ExtractEmbeddedDbTo", "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults",
                "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                //the console never starts it with itself; only the console's refresh pauses and resumes it
                "CampertronLibrary.function.Forms.MonitoringController::StartWithHost", "CampertronLibrary.function.Forms.MonitoringController::PauseForRefresh",
                "CampertronLibrary.function.Forms.MonitoringController::ResumeAfterRefresh",
                //no settings writer, YAML, secret, sender, guided setup, results run, refresh or transfer
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.SmtpPassword", "CampertronLibrary.function.Base.NtfyToken",
                "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.Base.Notifications", "CampertronLibrary.function.Base.Menu",
                "CampertronLibrary.function.Forms.GuidedSetup", "CampertronLibrary.function.Forms.DataOperationGate", "CampertronLibrary.function.Forms.ResultsStart", "CampertronLibrary.function.Forms.Settings",
                "CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundRefresh", "CampertronLibrary.function.Interactive.Results.InteractiveSearchRun",
                "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.",
                //no file, process, environment, console or thread of its own
                "System.IO.File::", "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.IO.Directory::Move",
                "System.Diagnostics.Process", "System.Environment::", "System.Console::", "System.Threading.Mutex", "System.Threading.Semaphore", "System.Threading.Monitor::",
                "System.Threading.Thread::.ctor", "System.Threading.Thread::Start", "System.Net."
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.Forms.MonitoringController::.ctor", "CampertronLibrary.function.Forms.MonitoringController::View",
                "CampertronLibrary.function.Forms.MonitoringController::Act", "CampertronLibrary.function.Forms.MonitoringController::Shutdown",
                "CampertronLibrary.function.Forms.MonitoringWording::StateText", "CampertronLibrary.function.Forms.MonitoringWording::Unavailable",
                "CampertronLibrary.function.Forms.MonitoringWording::Outcome", "CampertronLibrary.function.Forms.MonitoringWording::StartedText",
                "CampertronLibrary.function.Forms.MonitoringWording::CheckSummary", "CampertronLibrary.function.Forms.MonitoringWording::SearchLabel",
                "CampertronLibrary.function.Forms.MonitoringWording::SearchesText", "CampertronLibrary.function.Forms.MonitoringWording::LastFinishedText",
                "CampertronLibrary.function.Forms.BlockHold::Text", "CampertronLibrary.function.Forms.BlockHold::Begin",
                "CampertronLibrary.function.RecDotOrg.data.Load::DbExists", "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase",
                "CampertronLibrary.function.Interactive.Results.InteractiveResultsView::ShowSearch", "CampertronLibrary.function.Interactive.InteractiveTerminal::ReadKeysAsTheyAreTyped",
                "CampertronLibrary.function.Interactive.InteractiveTerminal::NextKeyTyped", "System.Runtime.InteropServices.PosixSignalRegistration::Create"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheMonitoringCode_HasNoStateOrLaterPhaseOfItsOwn_AndNoControlCharacter()
        {
            Type[] Own = typeof(InteractiveMonitoring).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveMonitoring).Namespace).ToArray();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.That(Own.Where(T => T.IsEnum), Is.Empty, "no state, phase or outcome of its own: the controller's");
            Assert.That(Own.SelectMany(T => T.GetFields(Declared)).Select(F => F.FieldType).Where(T => T.Namespace == typeof(MonitoringService).Namespace), Is.Empty,
                "no service, lock or history of its own");
            var Literals = new List<string>();
            foreach (Type T in Own)
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Literals.AddRange(ConsoleStartContractTests.StringLiterals(Method));
                }
                Literals.AddRange(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!));
            }
            string AllLiterals = string.Join("\n", Literals);
            foreach (string Later in new[] { "Guided setup", "guided setup", "Export", "Import", "Beam", "same campsite", "free space", "disk space", "Retry", "Try again",
                "starts again by itself after", "service", "at login", "https://", "http://", "resume flag" })
            {
                Assert.That(AllLiterals, Does.Not.Contain(Later), "no later phase, automatic restart, background service, link of its own or retry: " + Later);
            }
            Assert.That(Literals.Where(S => S.Any(char.IsControl)), Is.Empty, "no escape sequence or control character of its own");
        }

        [Test]
        public void TheStartWithoutArguments_KeepsItsOwnMonitoring_AndNeverTheConsolesOrTheController()
        {
            //email and ntfy mode (MonitoringConsole) and the start without arguments (Load) are unchanged: the service directly,
            //never the controller or the interactive console
            var Called = new List<string>();
            const BindingFlags All = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var Types = new Stack<Type>(new[] { typeof(MonitoringConsole), typeof(Load) });
            while (Types.Count > 0)
            {
                Type T = Types.Pop();
                foreach (MethodBase Method in T.GetMethods(All).Cast<MethodBase>().Concat(T.GetConstructors(All)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
                foreach (Type Nested in T.GetNestedTypes(All))
                {
                    Types.Push(Nested);
                }
            }
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringService::Start"));
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Forms.Monitoring", StringComparison.Ordinal) || C.Contains(".Interactive.", StringComparison.Ordinal)), Is.Empty);
        }

        // ---- Start ----

        [Test]
        public void Start_AsksFirst_NoByDefault_AndANoOrACancelStartsNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), L(""), P(MW.StartLabel), L("n"), P(MW.StartLabel), Esc, P(MW.StartLabel), CtrlC }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Prompts.Count(T => T == W.StartQuestion + " [y/N]: "), Is.EqualTo(4), "No by default");
            Assert.That(Terminal.Texts, Does.Contain(MW.StartHelp));
            Assert.That(Terminal.Texts.Count(T => T == W.NotStarted), Is.EqualTo(4));
            Assert.That((ControllerOf(Console_).View().Generation, Requested.Count, Prepared, LockIsHeld()), Is.EqualTo((0L, 0, 0, false)));
        }

        [Test]
        public void Start_StartsThroughTheController_ShowsTheSharedFacts_AndChangesNoFile()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Dictionary<string, string> Before = Fingerprint(ConfigPath);
            bool HeldWhileShown = false;
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(W.ShowLatest), Do(() => HeldWhileShown = LockIsHeld()), P(BackToMain), P("Exit"), P(W.StopAndExit) }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.WaitHow), "the menu waited for the start");
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(Running()));
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Is.EqualTo(new[] { MW.StopLabel, MW.RestartLabel, W.ShowLatest, W.ShowLastCheck, BackToMain }));
            Assert.That(HeldWhileShown, Is.True, "the service holds the settings folder's lock");
            Assert.That(Prepared, Is.EqualTo(1), "the campground data prepared once, as every start prepares it");
            Assert.That(Requested.Count, Is.GreaterThan(0));
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(Before), "no settings or search file written; nothing new in the settings folder");
            MonitoringControlView View = ControllerOf(Console_).View();
            Assert.That((View.Generation, View.ThisStart!.WithHost, View.ShuttingDown), Is.EqualTo((1L, false, true)), "the user's Start, never the console's own");
            AssertSafe(Terminal);
        }

        [Test]
        public void ADuplicateStart_IsNotPossible_AndAStartForAMomentThatChanged_DoesNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            //another start (the same controller, as a quick second action would be) between the menu and its answer
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Do(() =>
            {
                MonitoringController Controller = ControllerOf(Console_);
                Assert.That(Controller.Act(MonitoringControlAction.Start, Controller.View().Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
                Waited.Await();
            }), Yes }, new object[] { P(BackToMain), P("Exit"), P(W.StopAndExit) }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.Host.StaleText), "the menu's Start was for the moment it showed: nothing was done");
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(Running().Append(W.Host.StaleText)), "the state as it is now, then what the Start did");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Does.Not.Contain(MW.StartLabel), "no Start while it runs");
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1), "one start");
        }

        [Test]
        public void TwoQuickActions_ForOneMoment_OnlyTheFirstApplies()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            //the controller's rule, as the menu uses it: an action for a moment that has passed does nothing
            var Host = new InteractiveMonitoringHost(ConfigPath, CachePath, new BlockHold(Clock), With(), Text => { });
            try
            {
                MonitoringControlView Shown = Host.Controller.View();
                Assert.That(Host.Controller.Act(MonitoringControlAction.Start, Shown.Token), Is.EqualTo(MonitoringControlOutcome.Accepted));
                Assert.That(Host.Controller.Act(MonitoringControlAction.Start, Shown.Token), Is.AnyOf(MonitoringControlOutcome.Stale, MonitoringControlOutcome.Busy));
                Waited.Await();
                Assert.That(Host.Controller.View().Generation, Is.EqualTo(1));
            }
            finally
            {
                Host.End(null);
            }
        }

        [Test]
        public void AStartTheControllerRefuses_SaysTheStartsWordsWithTheConsolesFix_AndTheConsoleGoesOn()
        {
            WriteSettings(WithEmail: false);
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(new[]
            {
                "  Status: Not running: the last start did not start it.",
                "Error: email mode can't start: Email.yaml does not exist yet.",
                "  " + W.Host.EmailMissingFix,
                "  Nothing was searched or sent.",
                "Stop and Restart are available while the monitoring runs."
            }));
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Is.EqualTo(new[] { MW.StartLabel, W.ShowLatest, BackToMain }), "Start again, by the user only");
            Assert.That((Requested.Count, LockIsHeld()), Is.EqualTo((0, false)));
            AssertSafe(Terminal);
        }

        [Test]
        public void WithoutAMonitoringTurnedOn_TheStartSaysSo_AsInformation()
        {
            WriteSettings(OutputTo: "Console", WithEmail: false);
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            List<string> Last = Blocks(Terminal).Last();
            Assert.That(Last[0], Is.EqualTo("  Status: Not running: the last start did not start it."));
            Assert.That(Last[1], Does.StartWith("Info: "), "no monitoring is turned on: information, not an error");
            Assert.That((Requested.Count, Prepared), Is.EqualTo((0, 0)));
        }

        [Test]
        public void WithoutAnUsableSearch_TheStartIsRefused_InTheStartsWords()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip", AutoRun: false);
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            MonitoringControlStart Refused = ControllerOf(Console_).View().ThisStart!;
            Assert.That((Refused.Started, Refused.Problem), Is.EqualTo((false, MonitoringStartProblem.NoSearchToCheck)));
            Assert.That(Blocks(Terminal).Last()[1], Is.EqualTo("Error: " + Refused.Lines[0]), "the start's own words (D64)");
            Assert.That(Directory.GetFiles(ConfigPath, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml", "Email.yaml", "Trip.yaml" }), "no example search");
            Assert.That(Requested.Count, Is.Zero);
        }

        [Test]
        public void AnInvalidSettingsFile_IsRefusedByName_InFixedWords_AndTheConsolesFix()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: [Email\nsecret: " + Canary + "\n");
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            List<string> Last = Blocks(Terminal).Last();
            Assert.That(Last[1], Does.StartWith("Error: the monitoring can't start: General.yaml can't be read: it is not valid YAML (line "));
            Assert.That(Last[2], Is.EqualTo("  " + W.Host.InvalidYamlFix));
            AssertSafe(Terminal);
        }

        [Test]
        public void WithoutTheCampgroundData_TheStartIsRefused_AndTheConsoleNeverPreparesIt()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            DataPrepared = false;
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(new[]
            {
                "  Status: Not running: the last start did not start it.",
                "Error: email mode can't start: Campertron couldn't read its campground data, so it can't prepare the searches' campsite details.",
                "  " + W.Host.NoCampgroundDataFix,
                "  Nothing was searched or sent.",
                "Stop and Restart are available while the monitoring runs."
            }));
            Assert.That(W.Host.NoCampgroundDataFix, Does.Contain(CW.RefreshOrInstall), "the console's own way to install it");
            Assert.That((Prepared, Requested.Count, File.Exists(Path.Join(ConfigPath, "RecreationDotOrg.db"))), Is.EqualTo((0, 0, false)), "never extracted or prepared here");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WithoutTheCacheFolder_StartStartsNothing_AndTheConsoleNeverCreatesIt(bool Container)
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            string Missing = Path.Join(Root, "no-cache");
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));
            var Console_ = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => DateTime.UtcNow,
                () => new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = Missing, ContainerMode = Container });
            Sessions.Add(Console_);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Container ? W.ContainerCacheFolderMissing : W.CacheFolderMissing));
            Assert.That((ControllerOf(Console_).View().Generation, Directory.Exists(Missing), Requested.Count), Is.EqualTo((0L, false, 0)));
        }

        [Test]
        public void AnotherProgramsMonitoring_OfTheSameFolder_RefusesTheStart_InTheLocksWords()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            using (FileStream? Other = MonitoringLock.TryAcquire(ConfigPath, out _))
            {
                Assert.That(Other, Is.Not.Null);
                Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            }
            MonitoringControlStart Refused = ControllerOf(Console_).View().ThisStart!;
            Assert.That(Refused.Problem, Is.EqualTo(MonitoringStartProblem.LockHeld));
            Assert.That(Blocks(Terminal).Last()[1], Is.EqualTo("Error: " + Refused.Lines[0]));
            Assert.That(Requested.Count, Is.Zero, "no second monitoring of the folder");
        }

        // ---- Stop, Restart ----

        [Test]
        public void Stop_StopsIt_WithoutAQuestion_AndAStoppedMonitoringOffersStartOnly()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(MW.StopLabel) }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.StopHelp));
            Assert.That(Terminal.Prompts.Where(T => T.Contains("[y/N]")).Count(), Is.EqualTo(1), "only Start asked");
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(new[]
            {
                "  Status: Not running: it was stopped at " + When() + ". It does not start again by itself: select Start to start it again.",
                "  Started: " + When(),
                "  Notifications: Email",
                "  Searches checked: 1 (the Enabled (autoRun) searches it read when it started)",
                "  Last finished check: Check 1 at " + When(),
                "  The last check: Check 1 at " + When() + ": 1 search, nothing new to notify.",
                "Stop and Restart are available while the monitoring runs."
            }));
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Is.EqualTo(new[] { MW.StartLabel, W.ShowLatest, W.ShowLastCheck, BackToMain }));
            Assert.That(LockIsHeld(), Is.False);
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1), "nothing started again");
        }

        [Test]
        public void AStopForAMomentThatChanged_DoesNothing_AndSaysSo()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            //the monitoring stopped between the menu and its answer (as another of its ends would)
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(W.ShowLatest), Do(() =>
            {
                MonitoringController Controller = ControllerOf(Console_);
                Controller.Act(MonitoringControlAction.Stop, Controller.View().Token);
                SpinWait.SpinUntil(() => Controller.View().Phase == MonitoringControlPhase.Idle, 20000);
            }), P(MW.StopLabel) }, BackToExit));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.Host.StaleText));
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1));
        }

        [Test]
        public void Restart_AsksFirst_ThenStopsAndStartsAgain_ReadingTheSearchFilesAgain()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                P(W.ShowLatest), Do(() => WriteSearch("Lake.yaml", "Lake")), P(W.ShowLatest), P(MW.RestartLabel), L(""), P(MW.RestartLabel), Yes, Do(Waited.Await), P(W.ShowLatest)
            }, BackToExit, new object[] { P(W.StopAndExit) }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            List<List<string>> Shown = Blocks(Terminal);
            Assert.That(Shown.Any(B => B.Contains("Warning: " + MW.FilesChanged)), Is.True, "the changed search files, in the shared words");
            Assert.That(Terminal.Texts, Does.Contain(MW.RestartHelp));
            Assert.That(Terminal.Texts, Does.Contain(W.NotRestarted), "No by default");
            Assert.That(Shown.Last(), Is.EqualTo(Running(Summary: "2 searches, nothing new to notify.").Select(L_ => L_.Replace("Searches checked: 1", "Searches checked: 2"))));
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(2), "a new start that read the search files again");
        }

        // ---- a block, a failed send, a failure while it stops ----

        [Test]
        public void ABlockedCheck_StopsIt_ShowsTheFailureAndTheHold_AndResultsWaitsToo()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Url => "<html>Request blocked</html>";
            InteractiveSession Console_ = null!;
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes,
                Do(() => SpinWait.SpinUntil(() => ControllerOf(Console_).View().ThisEnd != null && ControllerOf(Console_).View().Busy == MonitoringControlBusy.None, 20000)),
                P(W.ShowLatest), P(BackToMain), P(InteractiveResultsWording.Results), P(RW.StartName), P(BackToMain), P("Exit") }));
            Console_ = Session(Terminal);
            int RequestsAfterBlock = -1;

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            RequestsAfterBlock = Requested.Count;
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(new[]
            {
                "  Status: Not running: it stopped at " + When() + " because a check failed. It does not start again by itself.",
                "  Started: " + When(),
                "  Notifications: Email",
                "  Searches checked: 1 (the Enabled (autoRun) searches it read when it started)",
                "  Last finished check: None yet",
                "Error: recreation.gov limited Campertron's requests (too many requests), so the monitoring stopped during a check and sent nothing for it.",
                "  " + W.Host.BlockedFix,
                "Warning: recreation.gov limited Campertron's requests. Start is available again in 5:00.",
                "  The last check: " + MW.NoCheckYet,
                "Stop and Restart are available while the monitoring runs."
            }), "the shared failure, the console's fix, the shared hold; no Start");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Is.EqualTo(new[] { W.ShowLatest, BackToMain }), "no Start for five minutes");
            Assert.That(Terminal.Texts, Does.Contain("recreation.gov limited Campertron's requests. Start is available again in 5:00."), "Results waits too");
            Assert.That(Terminal.Texts.Count(T => T.StartsWith("Searching ", StringComparison.Ordinal)), Is.Zero, "Results started no search");
            Assert.That(Requested.Count, Is.EqualTo(RequestsAfterBlock));
            Assert.That(BlockedSteps, Is.Empty, "the console's own five-minute wait and exit are the start without arguments' only");
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1), "nothing started it again");
            AssertSafe(Terminal);
        }

        [Test]
        public void ABlockedSearchOfResults_HoldsTheMonitoringsStart_ForTheSameFiveMinutes()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveResultsSeams Shared = InteractiveResults.Seams;
            InteractiveResults.Seams = new InteractiveResultsSeams
            {
                Prepared = Shared.Prepared, Campgrounds = Shared.Campgrounds, Time = Clock, PollInterval = Shared.PollInterval, WhenAskedToStop = Shared.WhenAskedToStop,
                Options = new SearchHostOptions { Transport = (Request, Token) => new SearchResponse(429, "slow down"), RetryDelay = TimeSpan.Zero, HeavyTrafficDelay = TimeSpan.Zero }
            };
            var Terminal = new MenuTerminal(Script(new object[] { P(InteractiveResultsWording.Results), P(RW.StartName), P(InteractiveResultsWording.BackToResults), P(BackToMain), P(W.Monitoring),
                Do(() => Clock.Advance(BlockHold.Duration)), P(W.ShowLatest) }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            List<List<string>> Shown = Blocks(Terminal);
            Assert.That(Shown[0], Does.Contain("Warning: recreation.gov limited Campertron's requests. Start is available again in 5:00."), "the hold Results began");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading)[0], Is.EqualTo(new[] { W.ShowLatest, BackToMain }), "no Start while it lasts");
            Assert.That(Shown[1], Is.EqualTo(Idle), "five minutes later it is gone");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading)[1], Is.EqualTo(new[] { MW.StartLabel, W.ShowLatest, BackToMain }));
        }

        [Test]
        public void AFailedSend_StopsTheMonitoring_RecordsNothing_SaysSoSafely_AndTheConsoleGoesOn()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            Send = Name => throw new SmtpException(SmtpStatusCode.MailboxUnavailable, "s3rver-answer " + Canary + " for someone@example.invalid");
            InteractiveSession Console_ = null!;
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes,
                Do(() => SpinWait.SpinUntil(() => ControllerOf(Console_).View().ThisEnd != null && ControllerOf(Console_).View().Busy == MonitoringControlBusy.None, 20000)),
                Do(() => Thread.Sleep(300)), P(W.ShowLatest) }, BackToExit));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0), "the console goes on after the failure and ends as usual");
            List<string> Last = Blocks(Terminal).Last();
            Assert.That(Last, Does.Contain("Error: the email could not be sent: the SMTP server refused an address of the email (SMTP 550)."));
            Assert.That(Last, Does.Contain("  The openings that were not delivered are not recorded in Camp.History, so the next monitoring notifies them again."));
            Assert.That(Last, Does.Contain("  " + W.Host.NotificationFix));
            Assert.That(Last[0], Is.EqualTo("  Status: Not running: it stopped at " + When() + " because a check failed. It does not start again by itself."));
            Assert.That(Markers(), Is.Empty, "what was not delivered is not recorded");
            Assert.That((Emails.Count, ControllerOf(Console_).View().Generation, LockIsHeld()), Is.EqualTo((1, 1L, false)), "one send, nothing started again by itself");
            Assert.That(Terminal.AllText, Does.Not.Contain("someone@").And.Not.Contain("s3rver"));
            AssertSafe(Terminal);
        }

        [Test]
        public void ADeliveredNotification_IsRecorded_AndTheLastChecksOpeningsShowACampsitesLinkAsText()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            InteractiveSession Console_ = null!;
            string? Row = null;
            MenuTerminal Terminal = null!;
            Terminal = new MenuTerminal(Script(StartIt, new object[] { P(W.ShowLatest), P(W.ShowLastCheck), Do(() => Row = Terminal.LastChoices[0]), L("1"),
                P(InteractiveResultsWording.ShowLink), P(InteractiveResultsWording.BackToRows), P(W.BackToMonitoring) }, BackToExit, new object[] { P(W.StopAndExit) }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            MonitoringControlCheck Check = ControllerOf(Console_).View().LastCheck ?? throw new AssertionException("a check");
            Assert.That((Check.NewOpenings > 0, Check.EmailsSent, Check.Delivered), Is.EqualTo((true, 1, true)));
            Assert.That(Emails.Count, Is.EqualTo(1));
            Assert.That(Markers(), Is.Not.Empty, "what was delivered is recorded, as the monitoring always records it");
            Assert.That(Blocks(Terminal).Last()[6], Is.EqualTo("  The last check: " + MW.CheckSummary(Check, false)));
            Assert.That(Terminal.Texts, Does.Contain(W.LastCheckNote));
            ResultSearchViewModel Search = Check.Searches.Single();
            Assert.That(Terminal.Texts, Does.Contain("  " + Search.CountsText).And.Contain("  " + Search.CampgroundText));
            string Link = CampsiteSearch.BookingUrl(Campground + "1");
            Assert.That(Terminal.Texts.Count(T => T == Link), Is.EqualTo(3), "the booking link alone on its line, as text: with the campsite, alone on request, and with the campsite again");
            Assert.That(Row, Is.EqualTo(Search.Rows.First(R => R.AccessibleName == Row).AccessibleName), "the rows are the shared rows of the controller's snapshot");
            AssertSafe(Terminal);
        }

        [Test]
        public void ASendThatFailsWhileItStops_EndsTheMonitoringAsFailed_AndRecordsNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            using var Sending = new ManualResetEventSlim(false);
            using var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                Sending.Set();
                Release.Wait(Long);
                throw new SmtpException(SmtpStatusCode.ServiceNotAvailable, "busy " + Canary);
            };
            InteractiveSession Console_ = null!;
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes, Do(() => Sending.Wait(Long)), P(W.ShowLatest), P(MW.StopLabel),
                new Press(TerminalInputKind.Escape), Do(() => Release.Set()),
                Do(() => SpinWait.SpinUntil(() => ControllerOf(Console_).View().Phase == MonitoringControlPhase.Idle, 20000)), P(W.ShowLatest) }, BackToExit));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.StoppedWaiting), "Esc stopped the waiting only");
            Assert.That(Blocks(Terminal).Any(B => B[0].StartsWith("  Status: Stopping: ", StringComparison.Ordinal)), Is.True, "the stop in the shared words while it waited for the send");
            List<string> Last = Blocks(Terminal).Last();
            Assert.That(Last[0], Is.EqualTo("  Status: Not running: it stopped at " + When() + " because a check failed. It does not start again by itself."), "the failure, not a stop");
            Assert.That(Markers(), Is.Empty);
            AssertSafe(Terminal);
        }

        [Test]
        public void TheWaitForAnAction_EndsAfterItsBound_AndTheActionGoesOn()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            using var Sending = new ManualResetEventSlim(false);
            using var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                Sending.Set();
                Release.Wait(Long);
            };
            InteractiveMonitoringSeams Seams_ = With(ActionWait: TimeSpan.FromSeconds(10));
            var Ticking = new Ticking();
            InteractiveMonitoring.Seams = new InteractiveMonitoringSeams
            {
                Now = Seams_.Now, Wait = Seams_.Wait, Time = Ticking, Prepared = Seams_.Prepared, PrepareCampgroundData = Seams_.PrepareCampgroundData,
                PollInterval = Seams_.PollInterval, ActionWait = Seams_.ActionWait, EndWait = Seams_.EndWait, WhenEnding = Seams_.WhenEnding
            };
            InteractiveSession Console_ = null!;
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes, Do(() => Sending.Wait(Long)), P(W.ShowLatest), P(MW.StopLabel),
                P(W.ShowLatest), Do(() => Release.Set()),
                Do(() => SpinWait.SpinUntil(() => ControllerOf(Console_).View().Phase == MonitoringControlPhase.Idle, 20000)), P(W.ShowLatest) }, BackToExit));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.StillBusy(TimeSpan.FromSeconds(10))));
            Assert.That(Ticking.Looks, Is.GreaterThanOrEqualTo(10), "it waited its bound, a look at a time");
            Assert.That(Blocks(Terminal).Last()[0], Does.StartWith("  Status: Not running: it was stopped at "), "the stop went on and ended");
            Assert.That((Emails.Count, Markers().Count > 0), Is.EqualTo((1, true)), "the check that was sending finished and recorded first");
        }

        // ---- the console's other menus while it runs ----

        [Test]
        public void TheMonitoring_GoesOn_WhileEveryOtherMenuIsOpened_AndNoneStopsPausesOrRestartsIt()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            var States = new List<(long, MonitoringControlPhase)>();
            void Look() => States.Add((ControllerOf(Console_).View().Generation, ControllerOf(Console_).View().Phase));
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                P(BackToMain), Do(Look), P("Status"), Do(Look), P("Notifications"), P(BackToMain), Do(Look), P("Settings"), P(BackToMain), Do(Look),
                P("Searches"), P(BackToMain), Do(Look), P("Campgrounds"), P(BackToMain), Do(Look), P(InteractiveResultsWording.Results), P(BackToMain), Do(Look),
                P("Help"), Do(Look), P(W.Monitoring), Do(Look), P(W.ShowLatest), P(BackToMain), P("Exit"), P(W.StopAndExit)
            }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(States, Has.Count.EqualTo(9));
            Assert.That(States, Has.All.EqualTo((1L, MonitoringControlPhase.Running)), "one start, running across every menu");
            Assert.That(Blocks(Terminal).Last(), Is.EqualTo(Running()));
        }

        // the console's own saves while it runs: General.yaml (Settings) and a search file (Searches) are written through their
        // shared forms, and the monitoring is neither started, stopped nor restarted; it checks what it read when it started,
        // and Monitoring says so in the shared words (select Restart to read them again)
        [Test]
        public void SavingSettingsAndASearchFile_WhileItRuns_StartsStopsAndRestartsNothing()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            var States = new List<(long, MonitoringControlPhase)>();
            void Look() => States.Add((ControllerOf(Console_).View().Generation, ControllerOf(Console_).View().Phase));
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                P(BackToMain), P("Settings"), P("General settings"), P("Change Refresh interval (days)"), L("9"), P("Save"), Yes, Do(Look),
                P("Back to Settings"), P(BackToMain), P("Searches"), L("1"), P("Change People (totalHumans)"), L("3"), P("Save"), Yes, Do(Look),
                P("Back to Searches"), P(BackToMain), P(W.Monitoring), P(W.ShowLatest), P(BackToMain), P("Exit"), P(W.StopAndExit)
            }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(File.ReadAllText(Path.Join(ConfigPath, "General.yaml")), Does.Contain("refreshRidbDataDayInterval: 9"), "saved through the shared form");
            Assert.That(File.ReadAllText(Path.Join(ConfigPath, "Trip.yaml")), Does.Contain("totalHumans: 3"));
            Assert.That(States, Has.Count.EqualTo(2).And.All.EqualTo((1L, MonitoringControlPhase.Running)), "each save started, stopped and restarted nothing");
            Assert.That(Blocks(Terminal).Last(), Does.Contain("Warning: " + MW.FilesChanged));
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Does.Contain(MW.RestartLabel));
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1));
        }

        [Test]
        public void ASearchOfResults_WhileItRuns_TouchesNoMarker_AndTheMonitoringGoesOn()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            InteractiveSession Console_ = null!;
            List<string>? Recorded = null;
            int EmailsBefore = -1;
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                Do(() => { Recorded = Markers(); EmailsBefore = Emails.Count; }), P(BackToMain), P(InteractiveResultsWording.Results), P(RW.StartName),
                P(InteractiveResultsWording.BackToResults), P(BackToMain), P("Exit"), P(W.StopAndExit)
            }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Finished: ", StringComparison.Ordinal)), Is.True, "the manual search ran");
            Assert.That(Recorded, Is.Not.Empty, "the monitoring's first check recorded what it delivered");
            Assert.That(Markers(), Is.EqualTo(Recorded), "the manual search found the same openings and recorded, removed and re-armed nothing");
            Assert.That(Emails.Count, Is.EqualTo(EmailsBefore), "and sent nothing");
            Assert.That(ControllerOf(Console_).View().Generation, Is.EqualTo(1));
        }

        [Test]
        public void TheConsolesMonitoring_NotifiesRecordsAndRearms_AsTheServiceDoes()
        {
            //a twin folder's check by the service alone, then the console's monitoring's: the same email, markers and re-arm
            string Twin = Path.Join(Root, "twin");
            Directory.CreateDirectory(Twin);
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            File.Copy(Path.Join(ConfigPath, "Trip.yaml"), Path.Join(Twin, "Trip.yaml"));
            DateTime Tomorrow = DateTime.Now.Date.AddDays(1);
            foreach (string Folder in new[] { ConfigPath, Twin })
            {
                foreach (string Site in Sites(Campground))
                {
                    Directory.CreateDirectory(Path.Join(Folder, "Camp.History", Tomorrow.Year.ToString(), Tomorrow.Month.ToString(), Tomorrow.Day.ToString(), Site));
                }
            }
            Answer = Openings;
            Recorder Alone = NewService();
            Assert.That(Alone.Service.Start(Settings(Folder: Twin)).Started, Is.True);
            Alone.AwaitWaits(1);
            Alone.Service.Stop();
            List<string> TwinSent = Emails.ToList();
            int TwinRemoved = Removed.Count;
            List<string> TwinMarkers = Directory.GetFileSystemEntries(Path.Join(Twin, "Camp.History"), "*", SearchOption.AllDirectories)
                .Select(P_ => Path.GetRelativePath(Twin, P_).Replace('\\', '/')).Where(P_ => P_.Count(C => C == '/') == 4).OrderBy(P_ => P_, StringComparer.Ordinal).ToList();
            Emails.Clear();
            Removed.Clear();

            var Terminal = new MenuTerminal(Script(StartIt, BackToExit, new object[] { P(W.StopAndExit) }));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Emails.ToList(), Is.EqualTo(TwinSent), "the same notification");
            Assert.That(Markers(), Is.EqualTo(TwinMarkers), "the same markers recorded");
            Assert.That(Removed.Count, Is.EqualTo(TwinRemoved), "and the same re-armed");
            Assert.That(TwinRemoved, Is.EqualTo(1), "the reserved campsite's marker");
        }

        // ---- the refresh pauses it ----

        [Test]
        public void ARefresh_PausesTheMonitoring_ForItsTime_AndItStartsAgainAfterwards()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            (bool Paused, bool Held, bool Running)? During = null;
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                MonitoringControlView V = ControllerOf(Console_).View();
                During = (V.Paused, LockIsHeld(), V.Status.IsRunning);
                return new RidbRefreshResult(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, true, DateTime.UtcNow, TimeSpan.FromMinutes(1), RidbRefreshResult.NoProblems, null, null, false);
            };
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                P(BackToMain), P(CW.Campgrounds), P(CW.RefreshOrInstall), Yes, P(BackToMain), P(W.Monitoring), Do(Waited.Await), P(W.ShowLatest)
            }, BackToExit, new object[] { P(W.StopAndExit) }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(During, Is.EqualTo(((bool, bool, bool)?)(true, false, false)), "paused: no check runs while the refresh clears the cache");
            Assert.That(Terminal.Texts, Does.Contain(RefreshWording.PausingMonitoring));
            Assert.That(Terminal.Texts, Does.Contain(W.ResumedAfterRefresh));
            MonitoringControlStart Again = ControllerOf(Console_).View().ThisStart!;
            Assert.That((Again.Generation, Again.AfterRefresh), Is.EqualTo((2L, true)), "a new start, again after the refresh");
            Assert.That(Blocks(Terminal).Last()[1], Is.EqualTo("  Started: " + When() + " (again after the campground data's refresh)"));
        }

        [Test]
        public void ARefreshThatEndsTheConsole_DoesNotStartTheMonitoringAgain_AndTheEndStopsIt()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                SpinWait.SpinUntil(() => Token.IsCancellationRequested, 20000);
                return new RidbRefreshResult(RidbRefreshOutcome.Canceled, RidbRefreshStage.Downloading, false, null, TimeSpan.FromMinutes(1), RidbRefreshResult.NoProblems, null, null, true);
            };
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), P(CW.Campgrounds), P(CW.RefreshOrInstall), Yes, new Press(TerminalInputKind.EndOfInput) }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Not.Contain(W.ResumedAfterRefresh));
            MonitoringControlView End_ = ControllerOf(Console_).View();
            Assert.That((End_.Generation, End_.Phase, End_.ShuttingDown), Is.EqualTo((1L, MonitoringControlPhase.Idle, true)), "not started again; stopped with the console");
            Assert.That(LockIsHeld(), Is.False);
        }

        // ---- the console's ends ----

        [Test]
        public void Exit_WhileItIsNotOn_EndsAtOnce_WithoutAQuestion()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(MW.StopLabel) }, BackToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Not.Contain(W.ExitText).And.Not.Contain(W.StoppingAtEnd));
            Assert.That(EndingDisposals, Is.EqualTo(1), "the signals' handling ends with the console");
        }

        [Test]
        public void Exit_WhileItRuns_Asks_KeepCampertronOpenByDefault_ThenStopTheMonitoringAndExit()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            InteractiveSession Console_ = null!;
            var Kept = new List<MonitoringControlPhase>();
            var Terminal = new MenuTerminal(Script(StartIt, new object[]
            {
                P(BackToMain), P("Exit"), Esc, Do(() => Kept.Add(ControllerOf(Console_).View().Phase)), P("Exit"), CtrlC, P("Exit"), L("x"), L("x"), L("x"), L("x"), L("x"),
                P("Exit"), P(W.KeepOpen), Do(() => Kept.Add(ControllerOf(Console_).View().Phase)), P("Exit"), P(W.StopAndExit)
            }));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.MenusOf(W.ExitQuestion), Has.Count.EqualTo(9).And.All.EqualTo(new[] { W.KeepOpen, W.StopAndExit }), "the first is the default; asked five times, the third with five unusable answers");
            Assert.That(Terminal.Texts.Count(T => T == W.ExitText), Is.EqualTo(5));
            Assert.That(Terminal.Texts.Count(T => T == W.KeptOpen), Is.EqualTo(4), "Esc, Ctrl+C, no usable answer and Keep Campertron open keep it open");
            Assert.That(Kept, Has.All.EqualTo(MonitoringControlPhase.Running));
            int Stopping = Terminal.Texts.IndexOf(W.StoppingAtEnd);
            Assert.That(Stopping, Is.GreaterThan(0));
            Assert.That(Terminal.Texts[Stopping + 1], Is.EqualTo(W.StoppedAtEnd));
            MonitoringControlView End_ = ControllerOf(Console_).View();
            Assert.That((End_.ShuttingDown, End_.Status.IsRunning, LockIsHeld()), Is.EqualTo((true, false, false)));
        }

        [Test]
        public void StopAndExit_WaitsForACheckThatSends_AndItsDeliveryIsRecorded()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            using var Sending = new ManualResetEventSlim(false);
            using var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                Sending.Set();
                Release.Wait(Long);
            };
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes, Do(() => Sending.Wait(Long)), P(BackToMain), P("Exit"),
                Do(() => Task.Run(() => { Thread.Sleep(300); Release.Set(); })), P(W.StopAndExit) }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.StoppedAtEnd), "within the bound");
            Assert.That((Emails.Count, Markers().Count > 0, LockIsHeld()), Is.EqualTo((1, true, false)), "the check that was sending finished and recorded first");
        }

        [Test]
        public void AStopThatOutlastsItsBound_IsSaid_AndTheConsoleEndsAnyway()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            using var Sending = new ManualResetEventSlim(false);
            using var Release = new ManualResetEventSlim(false);
            Send = Name =>
            {
                Sending.Set();
                Release.Wait(Long);
            };
            var Terminal = new MenuTerminal(Script(new object[] { P(W.Monitoring), P(MW.StartLabel), Yes, Do(() => Sending.Wait(Long)), P(BackToMain), P("Exit"), P(W.StopAndExit) }));
            InteractiveSession Console_ = Session(Terminal);
            InteractiveMonitoring.Seams = new InteractiveMonitoringSeams
            {
                Now = () => Fixed, Wait = Waited.Wait, Time = Clock, Prepared = F => true, PrepareCampgroundData = (C, K) => { }, PollInterval = TimeSpan.FromMilliseconds(1),
                EndWait = TimeSpan.FromMilliseconds(200), WhenEnding = Act => new Undo(() => { })
            };
            try
            {
                Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
                Assert.That(Terminal.Texts, Does.Contain(W.NotStoppedInTime(MonitoringController.StopWait)), "never abandoned silently");
            }
            finally
            {
                Release.Set();
            }
        }

        [Test]
        public void TheEndOfTheInput_WhileItRuns_StopsItFirst_ThenEndsTheConsole()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), End }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            List<string> Texts = Terminal.Texts;
            Assert.That(Texts.TakeLast(3), Is.EqualTo(new[] { W.StoppingAtEnd, W.StoppedAtEnd, InteractiveWording.InputEnded }));
            Assert.That((ControllerOf(Console_).View().Status.IsRunning, LockIsHeld(), EndingDisposals), Is.EqualTo((false, false, 1)));
        }

        [TestCase("question")]
        [TestCase("menu")]
        [TestCase("wait")]
        public void TheEndOfTheInput_InMonitoring_EndsTheConsole_AndStopsIt(string Where)
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            using var Release = new ManualResetEventSlim(false);
            using var Sending = new ManualResetEventSlim(false);
            Send = Name =>
            {
                Sending.Set();
                Release.Wait(Long);
            };
            object[] Steps = Where switch
            {
                "question" => new object[] { P(W.Monitoring), P(MW.StartLabel), End },
                "menu" => Script(StartIt.Take(3).ToArray(), new object[] { Do(() => Sending.Wait(Long)), Do(() => Release.Set()), Do(Waited.Await), End }),
                _ => Script(StartIt.Take(3).ToArray(), new object[] { Do(() => Sending.Wait(Long)), P(W.ShowLatest), P(MW.StopLabel), new Press(TerminalInputKind.EndOfInput), Do(() => Release.Set()) })
            };
            var Terminal = new MenuTerminal(Steps);
            InteractiveSession Console_ = Session(Terminal);
            if (Where == "wait")
            {
                Task.Run(() => { Thread.Sleep(300); Release.Set(); });
            }

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            if (Where == "wait")
            {
                Assert.That(Terminal.Texts, Does.Contain(W.InputEndedWhileWaiting));
            }
            Assert.That((ControllerOf(Console_).View().Status.IsRunning, LockIsHeld()), Is.EqualTo((false, false)));
        }

        [Test]
        public void CtrlC_AtTheMainMenu_WhileItRuns_StopsItFirst_ThenStoppedAnd130()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), CtrlC }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(InteractiveConsole.ExitCodeStopped));
            Assert.That(Terminal.Written.TakeLast(3), Is.EqualTo(new[] { ("out", W.StoppingAtEnd), ("out", W.StoppedAtEnd), ("stopped", "Stopped.") }));
            Assert.That((ControllerOf(Console_).View().Status.IsRunning, LockIsHeld()), Is.EqualTo((false, false)));
        }

        [Test]
        public void ATerminalThatIsGone_WhileItRuns_StopsIt_AndTheConsoleEndsWithZero()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), Do(() => throw new IOException("gone " + Canary)) }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That((ControllerOf(Console_).View().Status.IsRunning, LockIsHeld(), EndingDisposals), Is.EqualTo((false, false, 1)));
            Assert.That(Terminal.AllText, Does.Not.Contain(Canary));
        }

        [Test]
        public void AnUnexpectedProblem_WhileItRuns_StopsIt_AndTheConsoleEndsWithOne()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), Do(() => throw new InvalidOperationException(Canary)) }));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(InteractiveConsole.ExitCodeRefused));
            Assert.That(Terminal.Written.Where(W_ => W_.Kind == "error").Select(W_ => W_.Text), Is.EqualTo(new[] { InteractiveWording.ConsoleFailed("InvalidOperationException") }));
            Assert.That(Terminal.Texts.TakeLast(2), Is.EqualTo(new[] { W.StoppingAtEnd, W.StoppedAtEnd }));
            Assert.That((ControllerOf(Console_).View().Status.IsRunning, LockIsHeld()), Is.EqualTo((false, false)));
            Assert.That(Terminal.AllText, Does.Not.Contain(Canary));
        }

        [Test]
        public void ASignalThatEndsTheProcess_StopsTheMonitoringFirst_InWords_AndOnlyOnce()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            Answer = Openings;
            InteractiveSession Console_ = null!;
            //the signal's lines come between the main menu's question and its answer, so the answer is typed as its number
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(BackToMain), Do(() => Ending!()), Do(() => Ending!()), L("7") }, BackToExit));
            Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0), "the test's console goes on: a real process ends by the signal's own handling");
            Assert.That(Terminal.Texts.Count(T => T == W.StoppingForSignal), Is.EqualTo(1), "said once");
            Assert.That(Terminal.Texts[Terminal.Texts.IndexOf(W.StoppingForSignal) + 1], Is.EqualTo(W.StoppedAtEnd));
            Assert.That(Blocks(Terminal).Last(), Does.Contain(W.Host.UnavailableShuttingDown), "nothing can start it any more");
            Assert.That(Terminal.MenusOf(MW.ActionsHeading).Last(), Is.EqualTo(new[] { W.ShowLatest, W.ShowLastCheck, BackToMain }));
            Assert.That(Terminal.Texts, Does.Not.Contain(W.ExitText), "Exit asks nothing: it is not on");
            Assert.That((Markers().Count > 0, LockIsHeld()), Is.EqualTo((true, false)), "what the first check delivered stays recorded");
        }

        [Test]
        public void ALaterStartOfTheConsole_NeverStartsTheMonitoringAgain()
        {
            WriteSettings();
            WriteSearch("Trip.yaml", "Trip");
            var First = new MenuTerminal(Script(StartIt, BackToExit, new object[] { P(W.StopAndExit) }));
            Assert.That(Run(First), Is.EqualTo(0));
            Dictionary<string, string> After = Fingerprint(ConfigPath);
            int Requests = Requested.Count;

            var Later = new MenuTerminal(Script(new object[] { Do(() => Thread.Sleep(300)), P(W.Monitoring) }, BackToExit));
            InteractiveSession Next = Session(Later);
            Assert.That(Run(Later, Next), Is.EqualTo(0));
            Assert.That(Blocks(Later).Single(), Is.EqualTo(Idle), "not running: nothing kept starts it");
            Assert.That((Next.Monitoring!.Controller.View().Generation, Requested.Count), Is.EqualTo((0L, Requests)));
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(After), "no file the first console's end wrote or the second read into");
        }

        // ---- what reaches the terminal ----

        [Test]
        public void ControlCharacters_OfASearchName_NeverReachTheTerminal()
        {
            WriteSettings();
            //a double-quoted YAML value, so the file holds real control characters (an escape sequence and a bell)
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Base.EmailStartTests.SearchText("\"Trip\\e[31m\\ax\"", true, "campgroundID: '" + Campground + "'"));
            Answer = Openings;
            var Terminal = new MenuTerminal(Script(StartIt, new object[] { P(W.ShowLatest), P(W.ShowLastCheck), P(W.BackToMonitoring) }, BackToExit, new object[] { P(W.StopAndExit) }));

            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.AllText.Any(C => char.IsControl(C) && C != '\n'), Is.False);
            Assert.That(Terminal.AllText, Does.Contain("Trip"));
        }
    }

}
