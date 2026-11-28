using System.Net;
using System.Net.Mail;
using System.Reflection;
using System.Text;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Campgrounds;
using CampertronLibrary.function.Interactive.GuidedSetup;
using CampertronLibrary.function.Interactive.Monitoring;
using CampertronLibrary.function.Interactive.Searches;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;
using GW = CampertronLibrary.function.Forms.GuidedSetupWording;
using SW = CampertronLibrary.function.Forms.SearchesWording;
using W = CampertronLibrary.function.Interactive.GuidedSetup.InteractiveGuidedSetupWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's guided setup (v0.4.0's phase 12, D128; business-rules §43): the shared guided setup's steps
    // (GuidedSetupFlow, D117) hosted at a terminal over the console's own forms (the Notifications and Searches menus' form
    // hosts), driven from the main menu by the terminal that answers a choice by its label (MenuTerminal) and judged by the
    // files written, the shared words shown and what was sent. The shared form or flow, driven directly in another folder with
    // the same files, is the oracle where the console must write what the desktop GUI and the web UI write. The campground data
    // is a list in memory and a function (InteractiveSearches' seams): no database is opened. An email and an ntfy request are
    // recorded by their seams (no server); a refresh of the campground data is a scripted service; the monitoring's signals
    // are recorded, never registered. The done step's Start of the monitoring is InteractiveMonitoringTests'.
    [NonParallelizable]
    public class InteractiveGuidedSetupTests : ConfigEditTestBase
    {
        private static readonly DateTime UtcNow = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        private const string Guided = "Guided setup";
        private const string ByEmail = GW.EmailChoice;
        private const string ByNtfy = GW.NtfyChoice;
        private string Secrets = string.Empty;
        private string Cache => Path.Join(Secrets, "cache");
        private string OldPassword = string.Empty;
        private string NewPassword = string.Empty;
        private string TopicCanary = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Func<string, bool> OriginalPrepared = null!;
        private Func<string, List<CampgroundPickerEntry>> OriginalCampgrounds = null!;
        private Func<string, string, CampgroundFilterChoices> OriginalChoices = null!;
        private Func<string, string> OriginalState = null!;
        private InteractiveMonitoringSeams OriginalMonitoring = null!;
        private Func<RidbRefreshRequest, IProgress<RidbRefreshProgress>, CancellationToken, RidbRefreshResult> OriginalRefresh = null!;
        private TimeSpan OriginalRefreshPoll;
        private Func<Action, IDisposable> OriginalRefreshStop = null!;
        private readonly List<InteractiveSession> Sessions = new();
        private readonly List<string> Emails = new();
        private Recording Handler = null!;
        private bool Prepared;
        private int Refreshes;
        private int SignalRegistrations;

        [SetUp]
        public void Seams()
        {
            Secrets = Path.Join(Path.GetTempPath(), "CampertronInteractiveGuided-" + Guid.NewGuid().ToString("N"));
            SetUps = 0;
            Directory.CreateDirectory(Secrets);
            OldPassword = "PwOld-cnry-" + Guid.NewGuid().ToString("N");
            NewPassword = "PwNew-cnry-" + Guid.NewGuid().ToString("N");
            TopicCanary = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            OriginalPrepared = InteractiveSearches.DataPrepared;
            OriginalCampgrounds = InteractiveSearches.ReadCampgrounds;
            OriginalChoices = InteractiveSearches.ReadFilterChoices;
            OriginalState = InteractiveSearches.DatabaseState;
            OriginalMonitoring = InteractiveMonitoring.Seams;
            OriginalRefresh = InteractiveCampgroundRefresh.Service;
            OriginalRefreshPoll = InteractiveCampgroundRefresh.PollInterval;
            OriginalRefreshStop = InteractiveCampgroundRefresh.WhenAskedToStop;
            Sessions.Clear();
            Emails.Clear();
            Prepared = true;
            Refreshes = 0;
            SignalRegistrations = 0;
            SmtpPassword.GetEnvironment = Name => null;
            NtfyToken.GetEnvironment = Name => null;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => Emails.Add(Message.Subject);
            Handler = new Recording();
            NtfyPublisher.CreateHandler = () => Handler;
            InteractiveSearches.DataPrepared = Folder_ => Prepared;
            InteractiveSearches.ReadCampgrounds = Folder_ => Campgrounds();
            InteractiveSearches.ReadFilterChoices = (Folder_, Id) => CampgroundFilterChoices.None(Id);
            InteractiveSearches.DatabaseState = Folder_ => "db";
            InteractiveMonitoring.Seams = new InteractiveMonitoringSeams
            {
                Prepared = Folder_ => Prepared,
                PrepareCampgroundData = (Config, Cache_) => throw new AssertionException("the guided setup prepares no campground data"),
                WhenEnding = Act =>
                {
                    SignalRegistrations++;
                    return new Undo(() => { });
                }
            };
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) => throw new AssertionException("no refresh was scripted");
            InteractiveCampgroundRefresh.PollInterval = TimeSpan.FromMilliseconds(1);
            InteractiveCampgroundRefresh.WhenAskedToStop = Act => new Undo(() => { });
        }

        [TearDown]
        public void RestoreSeams()
        {
            foreach (InteractiveSession Made in Sessions)
            {
                Made.Monitoring?.Controller.Shutdown();
                Made.Monitoring?.Dispose();
            }
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            InteractiveSearches.DataPrepared = OriginalPrepared;
            InteractiveSearches.ReadCampgrounds = OriginalCampgrounds;
            InteractiveSearches.ReadFilterChoices = OriginalChoices;
            InteractiveSearches.DatabaseState = OriginalState;
            InteractiveMonitoring.Seams = OriginalMonitoring;
            InteractiveCampgroundRefresh.Service = OriginalRefresh;
            InteractiveCampgroundRefresh.PollInterval = OriginalRefreshPoll;
            InteractiveCampgroundRefresh.WhenAskedToStop = OriginalRefreshStop;
            Handler.Dispose();
            if (Directory.Exists(Secrets))
            {
                Directory.Delete(Secrets, true);
            }
        }

        private sealed class Undo : IDisposable
        {
            private readonly Action Do;
            internal Undo(Action Do) => this.Do = Do;
            public void Dispose() => Do();
        }

        private sealed class Recording : HttpMessageHandler
        {
            internal readonly List<string> Seen = new();
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Seen.Add(Request.RequestUri?.ToString() ?? string.Empty);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"abc123\",\"event\":\"message\"}", Encoding.UTF8, "application/json") };
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        // ---- the campground data (in memory) ----

        private static readonly CampgroundPickerEntry UpperPines = new CampgroundPickerEntry(new CampgroundChoice("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park");
        private static List<CampgroundPickerEntry> Campgrounds() => new List<CampgroundPickerEntry> { UpperPines };

        // ---- the files ----

        private string In(string Name, string? Of = null) => Path.Join(Of ?? Folder, Name);
        private string? OnDisk(string Name) => File.Exists(In(Name)) ? File.ReadAllText(In(Name)) : null;

        private static string SearchText(string Name, bool AutoRun, string Extra = "") =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 2\n" + Extra;

        private string EmailText(bool Recipient = true) => "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: guided-user\nsmtpPassword: " + OldPassword
            + "\n" + (Recipient ? "sendToAddressList:\n- to@example.invalid\n" : string.Empty) + "sendFromAddress: from@example.invalid\n";

        private const string GeneralConsole = "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n";
        private const string GeneralEmail = "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n";

        private void WriteComplete()
        {
            File.WriteAllText(Email, EmailText());
            File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: true));
            File.WriteAllText(General, GeneralEmail);
        }

        private Dictionary<string, string> Fingerprint(string? Of = null) =>
            Directory.GetFileSystemEntries(Of ?? Folder, "*", SearchOption.AllDirectories).ToDictionary(P_ => P_,
                P_ => File.Exists(P_) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P_))) + File.GetLastWriteTimeUtc(P_).Ticks : "dir");

        private List<string> Names() => Directory.GetFileSystemEntries(Folder).Select(Path.GetFileName).OrderBy(N => N, StringComparer.Ordinal).ToList()!;

        // ---- the script ----

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static readonly TerminalInput No = TerminalInput.Line("n");
        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private static readonly object[] LeaveToExit = { P(W.LeaveLabel), P("Exit") };

        private InteractiveSession Session(InteractiveTerminal Terminal, bool Container = false, bool NoFolder = false, string? Settings = null)
        {
            var Made = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Settings ?? Folder, CachePath = Cache, ContainerMode = Container });
            Sessions.Add(Made);
            return Made;
        }

        private int Run(MenuTerminal Terminal, InteractiveSession? With = null) => InteractiveConsole.Run(With ?? Session(Terminal));

        // a step's heading as the shared words make it
        private static string Heading(GuidedSetupStep Step, GuidedSetupMethod? Method) => Step switch
        {
            GuidedSetupStep.Done => Method is GuidedSetupMethod M ? GW.DoneHeading(M) : GW.NotDoneHeading,
            _ => GW.StepOf(Step) + ": " + GW.StepName(Step, Method)
        };

        private static IEnumerable<string> Headings(MenuTerminal Terminal) => Terminal.Written.Where(W_ => W_.Kind == "heading").Select(W_ => W_.Text);

        // nothing sent, searched or started: no email, no ntfy request, no history, no lock, no database, no cache
        private void AssertNothingSentOrStarted(InteractiveSession? Console_ = null)
        {
            Assert.That(Emails, Is.Empty, "no email");
            Assert.That(Handler.Seen, Is.Empty, "no ntfy request");
            Assert.That(File.Exists(In("Camp.History")), Is.False, "no Camp.History");
            Assert.That(File.Exists(In(MonitoringLock.FileName)), Is.False, "no monitoring lock");
            Assert.That(File.Exists(In("RecreationDotOrg.db")), Is.False, "no campground data prepared or opened");
            Assert.That(Directory.Exists(Cache), Is.False, "no cache: nothing searched");
            Assert.That(Refreshes, Is.Zero, "no refresh");
            if (Console_?.Monitoring is InteractiveMonitoringHost Host)
            {
                Assert.That(Host.Controller.View().Status.IsRunning, Is.False, "no monitoring running");
            }
        }

        private void AssertSafe(MenuTerminal Terminal)
        {
            string All = Terminal.AllText + "\n" + Out + "\n" + Err;
            foreach (string Canary in new[] { OldPassword, NewPassword, TopicCanary })
            {
                Assert.That(All.Contains(Canary, StringComparison.Ordinal), Is.False, "a secret was written (its canary is not repeated here)");
            }
            Assert.That(All, Does.Not.Contain("Exception").And.Not.Contain("   at "), "no exception's name or stack trace");
            Assert.That(Terminal.AllText.Any(C => char.IsControl(C) && C != '\n'), Is.False, "no control character reaches the terminal");
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "the console writes only through its terminal");
        }

        // ---- the main menu, entering the guided setup ----

        [Test]
        public void TheMainMenu_HasGuidedSetupAfterMonitoring_AndBeforeHelp_AndHelpSaysWhatItDoes()
        {
            Assert.That(InteractiveConsole.MainMenu().Select(I => I.Label),
                Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", Guided, "Help", "Exit" }));
            Assert.That(W.GuidedSetup, Is.EqualTo(GW.Heading), "the shared name");
            string Help = string.Join("\n", InteractiveWording.HelpLines);
            Assert.That(Help, Does.Contain("Guided setup: set up the unattended monitoring step by step"));
            Assert.That(Help, Does.Contain("It never starts the monitoring by itself"));
            foreach (string Later in new[] { "Export", "Import", "Beam", "same campsite", "blank" })
            {
                Assert.That(Help, Does.Not.Contain(Later), "no capability of a later phase");
            }
        }

        [Test]
        public void OpeningIt_InAnEmptyFolder_StartsAtStep1_InTheSharedWords_AndChangesNothing()
        {
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided) }, LeaveToExit));
            InteractiveSession Console_ = Session(Terminal);

            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));

            Assert.That(Terminal.Written, Does.Contain(("heading", W.Title)));
            Assert.That(Terminal.Texts, Does.Contain(GW.Intro).And.Contain(W.HowItWorks).And.Contain(W.WhereYouAre + ": " + GW.StartUnconfigured));
            List<string> Step1 = Terminal.Texts.SkipWhile(T => T != Heading(GuidedSetupStep.Method, null)).ToList();
            Assert.That(Step1.Take(6), Is.EqualTo(new[]
            {
                "Step 1 of 4: How you are notified", "  Step 1 of 4: How you are notified (current step)", "  Step 2 of 4: Email or ntfy settings (to do)",
                "  Step 3 of 4: A search to check (to do)", "  Step 4 of 4: Review and turn on (to do)", W.MethodHint
            }));
            Assert.That(Terminal.MenusOf(GW.MethodLegend).Single(), Is.EqualTo(new[] { ByEmail, ByNtfy, W.LeaveLabel }), "step 1 has no Back");
            Assert.That(Terminal.Texts, Does.Contain(W.Left));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "opening and leaving it creates, writes and changes nothing");
            Assert.That(Console_.Monitoring, Is.Null, "no monitoring is made by opening it");
            AssertNothingSentOrStarted();
            AssertSafe(Terminal);
        }

        //since D130 (PT1) a missing folder outside a container is offered to be created (the first setup's own tests below)
        [TestCase(true, false, W.ContainerFolderMissing)]
        [TestCase(false, true, W.NoFolder)]
        public void WithoutTheSettingsFolder_ItSaysWhereItComesFrom_AndOffersNothing(bool Container, bool NoFolder, string Said)
        {
            string Missing = Path.Join(Secrets, "not-created");
            var Terminal = new MenuTerminal(P(Guided), P("Exit"));
            Assert.That(Run(Terminal, Session(Terminal, Container, NoFolder, Missing)), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Said));
            Assert.That(Directory.Exists(Missing), Is.False, "the console never creates the settings folder");
            Assert.That(Headings(Terminal), Has.None.StartWith("Step "));
            AssertSafe(Terminal);
        }

        // ---- a first setup (D130, PT1's console findings): a missing settings folder offered, created only after a y ----

        private string Missing => Path.Join(Secrets, "first-setup", "CampertronConfig");
        private int SetUps;

        // a session whose settings folder does not exist yet; SetUp: the first start's preparation, by default one that
        // records its call and creates the folder with a General.yaml as a first start does
        private InteractiveSession FirstSetupSession(MenuTerminal Terminal, Func<CampertronInternalConfig>? SetUp = null)
        {
            var Made = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow,
                () => new CampertronInternalConfig { ConfigPath = Missing, CachePath = Path.Join(Secrets, "first-setup", "CampertronCache"), ContainerMode = false },
                SetUp ?? (() =>
                {
                    SetUps++;
                    Directory.CreateDirectory(Missing);
                    File.WriteAllText(Path.Join(Missing, "General.yaml"), GeneralConsole);
                    return new CampertronInternalConfig { ConfigPath = Missing };
                }));
            Sessions.Add(Made);
            return Made;
        }

        [TestCase("Enter")]
        [TestCase("n")]
        [TestCase("Esc")]
        [TestCase("Ctrl+C")]
        public void AMissingSettingsFolder_IsOfferedToBeCreated_NoByDefault_AndAnythingButYCreatesNothing(string Answer)
        {
            object Said = Answer switch { "Enter" => L(string.Empty), "n" => No, "Esc" => Esc, _ => CtrlC };
            var Terminal = new MenuTerminal(P(Guided), Said, P("Exit"));

            Assert.That(Run(Terminal, FirstSetupSession(Terminal)), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.SetUpExplanation));
            Assert.That(Terminal.Prompts, Does.Contain(W.SetUpQuestion + " [y/N]: "), "No by default");
            Assert.That(Terminal.Texts, Does.Contain(W.NothingCreated));
            Assert.That(SetUps, Is.Zero);
            Assert.That(Directory.Exists(Path.Join(Secrets, "first-setup")), Is.False, "nothing created");
            Assert.That(Headings(Terminal), Has.None.StartWith("Step "));
            AssertSafe(Terminal);
        }

        [Test]
        public void TheEndOfTheInput_AtTheFirstSetupsQuestion_EndsTheConsole_CreatingNothing()
        {
            var Terminal = new MenuTerminal(P(Guided), End);

            Assert.That(Run(Terminal, FirstSetupSession(Terminal)), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(SetUps, Is.Zero);
            Assert.That(Directory.Exists(Path.Join(Secrets, "first-setup")), Is.False);
        }

        [Test]
        public void AYes_CreatesTheFolderByTheFirstStartsPreparationOnce_ThenTheGuidedSetupGoesOnFromTheSavedFiles()
        {
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), Yes }, LeaveToExit));

            Assert.That(Run(Terminal, FirstSetupSession(Terminal)), Is.EqualTo(0));
            Assert.That(SetUps, Is.EqualTo(1));
            Assert.That(Terminal.Texts, Does.Contain(W.SettingUp).And.Contain(W.SetUpDone));
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Method, null)), "the real guided setup's first step, from the saved files");
            //nothing but the preparation's own files: no search, no notification, no refresh, no monitoring
            Assert.That(Directory.GetFileSystemEntries(Missing).Select(Path.GetFileName), Is.EqualTo(new[] { "General.yaml" }));
            Assert.That(Emails, Is.Empty);
            Assert.That(Handler.Seen, Is.Empty);
            Assert.That(Refreshes, Is.Zero);
            Assert.That(Sessions.Last().Monitoring, Is.Null, "no monitoring made, none started");
            Assert.That(File.ReadAllText(Path.Join(Missing, "General.yaml")), Is.EqualTo(GeneralConsole), "outputTo and autoRun untouched");

            //opened again: the folder exists, so nothing is offered or created, and it resumes from the saved files
            var Again = new MenuTerminal(Script(new object[] { P(Guided) }, LeaveToExit));
            Assert.That(Run(Again, FirstSetupSession(Again)), Is.EqualTo(0));
            Assert.That(SetUps, Is.EqualTo(1), "never prepared twice");
            Assert.That(Again.Prompts, Has.None.StartWith(W.SetUpQuestion));
            Assert.That(Headings(Again), Does.Contain(Heading(GuidedSetupStep.Method, null)));
            AssertSafe(Terminal);
        }

        [Test]
        public void TheFirstSetup_IsTheFirstStartsOwnPreparation_CreatingWhatAFirstStartCreates_AndNothingElse()
        {
            Assume.That(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == null && Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") == null,
                "outside a container, where the settings folder is LocalApplicationData's");
            string Here = Path.Join(Secrets, "local-app-data");
            string Oracle = Path.Join(Secrets, "oracle-app-data");
            Directory.CreateDirectory(Here);
            Directory.CreateDirectory(Oracle);
            Func<string> OriginalLookup = Load.LocalApplicationDataLookup;
            try
            {
                //what a first start creates: the start without arguments' and the desktop app's own preparation
                Load.LocalApplicationDataLookup = () => Oracle;
                Load.GetConfig();
                Load.LocalApplicationDataLookup = () => Here;
                var Terminal = new MenuTerminal(Script(new object[] { P(Guided), Yes }, LeaveToExit));
                var Made = new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow, Load.FolderLocations);
                Sessions.Add(Made);

                Assert.That(InteractiveConsole.Run(Made), Is.EqualTo(0));

                Assert.That(Terminal.Texts, Does.Contain(W.SetUpDone));
                List<string> Entries(string Root) => Directory.GetFileSystemEntries(Root, "*", SearchOption.AllDirectories)
                    .Select(E => Path.GetRelativePath(Root, E).Replace('\\', '/')).OrderBy(E => E, StringComparer.Ordinal).ToList();
                Assert.That(Entries(Here), Is.EqualTo(Entries(Oracle)), "the same folders and files a first start creates, and no other (no search, no Camp.History, no lock)");
                Assert.That(Entries(Here).Where(E => E.EndsWith(".yaml", StringComparison.Ordinal)), Is.EquivalentTo(new[] { "CampertronConfig/General.yaml", "CampertronConfig/Email.yaml" }));
                string Config = Path.Join(Here, "CampertronConfig"), OracleConfig = Path.Join(Oracle, "CampertronConfig");
                Assert.That(File.ReadAllBytes(Path.Join(Config, "Email.yaml")), Is.EqualTo(File.ReadAllBytes(Path.Join(OracleConfig, "Email.yaml"))));
                static string WithoutItsTime(string Text) => string.Join("\n", Text.Split('\n').Where(L => !L.StartsWith("lastRidbDataRefresh:", StringComparison.Ordinal)));
                Assert.That(WithoutItsTime(File.ReadAllText(Path.Join(Config, "General.yaml"))), Is.EqualTo(WithoutItsTime(File.ReadAllText(Path.Join(OracleConfig, "General.yaml")))),
                    "the same starting General.yaml (outputTo: Console, no automatic refresh), but the moment it was made");
                if (!OperatingSystem.IsWindows())
                {
                    foreach (string Entry in Entries(Here))
                    {
                        Assert.That(File.GetUnixFileMode(Path.Join(Here, Entry)), Is.EqualTo(File.GetUnixFileMode(Path.Join(Oracle, Entry))), "the same mode: " + Entry);
                    }
                }
                Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Method, null)));
                Assert.That(Emails, Is.Empty);
                Assert.That(Handler.Seen, Is.Empty);
                Assert.That(Refreshes, Is.Zero);
            }
            finally
            {
                Load.LocalApplicationDataLookup = OriginalLookup;
            }
        }

        [Test]
        public void AFolderAnotherProgramCreatedMeanwhile_IsUsed_AndNothingIsCreatedHere()
        {
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), (Action)(() =>
            {
                Directory.CreateDirectory(Missing);
                File.WriteAllText(Path.Join(Missing, "General.yaml"), GeneralConsole);
            }), Yes }, LeaveToExit));

            Assert.That(Run(Terminal, FirstSetupSession(Terminal)), Is.EqualTo(0));
            Assert.That(SetUps, Is.Zero, "looked at again just before: nothing prepared");
            Assert.That(Terminal.Texts, Does.Contain(W.FolderAppeared));
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Method, null)));
        }

        [Test]
        public void AFailedPreparation_IsSaidInFixedWords_AndChoosingGuidedSetupAgainGoesOn()
        {
            const string Canary = "CANARY-first-setup-problem";
            int Calls = 0;
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), Yes, P(Guided) }, LeaveToExit));

            Assert.That(Run(Terminal, FirstSetupSession(Terminal, () =>
            {
                Calls++;
                Directory.CreateDirectory(Missing);
                throw new IOException(Canary + " " + Missing);
            })), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.SetUpFailed(nameof(IOException))));
            Assert.That(Terminal.AllText, Does.Not.Contain(Canary).And.Not.Contain(Missing), "the problem's kind only: never its text or a path");
            Assert.That(Calls, Is.EqualTo(1));
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Method, null)), "the next visit goes on with what was created");
        }

        [Test]
        public void TheMainMenu_NeverCreatesTheFolder_NorLeadsToTheGuidedSetup_AndSettingsSaysWhereItIsCreated()
        {
            var Terminal = new MenuTerminal(P("Status"), P("Settings"), P("Notifications"), P("Exit"));

            Assert.That(Run(Terminal, FirstSetupSession(Terminal)), Is.EqualTo(0));
            Assert.That(Directory.Exists(Path.Join(Secrets, "first-setup")), Is.False, "the main menu reads and creates nothing");
            Assert.That(SetUps, Is.Zero);
            Assert.That(Headings(Terminal), Has.None.StartWith("Step ").And.None.EqualTo(W.Title), "no lead to the guided setup (#67, D130)");
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWording.SettingsFolderMissing).And.Contain(InteractiveSettingsWording.FolderMissing));
            Assert.That(InteractiveSettingsWording.SettingsFolderMissing, Does.EndWith("Guided setup, in the main menu, offers to create it."));
        }

        [Test]
        public void APartialConfiguration_ResumesAtTheMethodsFirstStepNotDone_FromTheSavedFilesOnly()
        {
            File.WriteAllText(Email, EmailText());
            File.WriteAllText(General, GeneralConsole);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided) }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain(W.WhereYouAre + ": " + GW.StartPartial));
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), "Email.yaml is ready: the search is next");
            Assert.That(Headings(Terminal), Has.None.EqualTo(Heading(GuidedSetupStep.Method, null)));
            Assert.That(Terminal.Texts, Does.Contain("  Step 1 of 4: How you are notified (done)").And.Contain("  Step 2 of 4: Email settings (done)")
                .And.Contain("  Step 3 of 4: A search to check (current step)"));
            Assert.That(Fingerprint(), Is.EqualTo(Before));
            AssertNothingSentOrStarted();
        }

        [Test]
        public void ACompleteConfiguration_OpensAtStep1_WithItsMethodChosen_AndItsDoneStepSaysSo_ChangingNothing()
        {
            WriteComplete();
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(ByEmail) }, LeaveToExit));
            InteractiveSession Console_ = Session(Terminal);
            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain(W.WhereYouAre + ": " + GW.StartComplete(GuidedSetupMethod.Email)));
            Assert.That(Terminal.Texts, Does.Contain(W.ChosenNow(GuidedSetupMethod.Email)), "the saved method is said on step 1");
            Assert.That(Headings(Terminal), Does.Contain(GW.DoneHeading(GuidedSetupMethod.Email)), "the method's steps are all done");
            Assert.That(Terminal.Texts, Does.Contain(W.DoneText(GuidedSetupMethod.Email)).And.Contain("  Status: Not running.").And.Contain(W.MonitoringNotRunning));
            Assert.That(Terminal.MenusOf(W.DoneChoices).Single(), Is.EqualTo(new[] { GW.StartMonitoringLabel, GW.BackLabel, W.LeaveLabel }));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "a complete configuration is never written again");
            Assert.That(Console_.Monitoring, Is.Not.Null, "the done step shows the console's one monitoring");
            AssertNothingSentOrStarted(Console_);
            AssertSafe(Terminal);
        }

        [TestCase("esc")]
        [TestCase("ctrlc")]
        [TestCase("end")]
        public void AtAStep_EscOrCtrlCLeaves_AndTheEndOfTheInputEndsTheConsole_WithNothingSaved(string How)
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = How == "end" ? new MenuTerminal(P(Guided), End) : new MenuTerminal(P(Guided), How == "esc" ? Esc : CtrlC, P("Exit"));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            if (How == "end")
            {
                Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            }
            else
            {
                Assert.That(Terminal.Texts, Does.Contain(W.Left));
            }
            Assert.That(Fingerprint(), Is.EqualTo(Before));
            AssertNothingSentOrStarted();
        }

        // ---- step 2: the notification settings ----

        [Test]
        public void TheEmailStep_IsTheNotificationsForm_ItsSaveAndContinueSavesThroughTheFlow_AfterAy_ThenTheSearchIsNext()
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            //the oracle: the same file, its form driven by the shared flow directly
            string Other = Path.Join(Secrets, "oracle");
            Directory.CreateDirectory(Other);
            File.WriteAllText(In("Email.yaml", Other), EmailText(Recipient: false));
            EmailSettingsViewModel Form = new SettingsViewModel(Other).Email;
            Form.Recipients.AddRow();
            Form.Recipients.Rows[0].Text = "to@example.invalid";
            Assert.That(GuidedSetupFlow.ContinueNotifications(Form, GuidedSetupMethod.Email, Other).Next, Is.EqualTo(GuidedSetupStep.Search));

            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P("Change Recipients (sendToAddressList)"), P("Add recipient"), L("to@example.invalid"), P("Done"),
                P(GW.SaveAndContinueLabel), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(OnDisk("Email.yaml"), Is.EqualTo(File.ReadAllText(In("Email.yaml", Other))), "the bytes the shared flow writes");
            Assert.That(OnDisk("Email.yaml.bak"), Is.EqualTo(EmailText(Recipient: false)), "the form's own backup of the file before");
            Assert.That(Terminal.Texts, Does.Contain(GW.EmailHelp).And.Contain(GW.TestRecommended).And.Contain(W.SaveAndContinueHint));
            Assert.That(Terminal.Texts, Does.Contain(GW.NotReady(GuidedSetupMethod.Email)), "what the start would refuse, before the save");
            Assert.That(Terminal.Prompts, Does.Contain(InteractiveSettingsWording.SaveQuestion("Email.yaml") + " [y/N]: "));
            Assert.That(Terminal.Texts, Does.Contain("Info: Saved Email.yaml. The previous version is in Email.yaml.bak."), "the form's result, said once on the next step");
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)));
            List<string> FormChoices = Terminal.MenusOf("Email.yaml").First();
            Assert.That(FormChoices, Does.Contain(GW.SaveAndContinueLabel).And.Contain(GW.BackLabel).And.Contain(W.LeaveLabel));
            Assert.That(FormChoices, Does.Not.Contain("Save").And.Not.Contain("Back to Notifications"), "Save and continue stands where Save is");
            AssertNothingSentOrStarted();
            AssertSafe(Terminal);
        }

        [Test]
        public void SaveAndContinue_AnsweredNo_OrEsc_SavesNothing_AndKeepsTheChanges()
        {
            File.WriteAllText(Email, EmailText());
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(GW.BackLabel), P("Change SMTP server"), L("other.example.invalid"), P(GW.SaveAndContinueLabel), No, P(GW.SaveAndContinueLabel), Esc,
                P(GW.BackLabel), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Count(T => T == InteractiveSettingsWording.Kept), Is.EqualTo(2));
            Assert.That(Terminal.Prompts, Does.Contain(InteractiveSettingsWording.LeaveQuestion("Email.yaml") + " [y/N]: "), "Back asks before it leaves the changes");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was saved");
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Method, null)), "Back is the flow's step before");
        }

        [Test]
        public void AValueInError_StaysOnTheStep_WithTheFormsProblems_AndARetryAfterTheCorrectionGoesOn()
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            string Saved = EmailText(Recipient: false);
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P("Change SMTP port"), L("x"), P(GW.SaveAndContinueLabel), Yes,
                Do(() => Assert.That(OnDisk("Email.yaml"), Is.EqualTo(Saved), "a value with its own error is never saved")),
                P("Change SMTP port"), L("587"), P("Change Recipients (sendToAddressList)"), P("Add recipient"), L("to@example.invalid"), P("Done"),
                P(GW.SaveAndContinueLabel), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            int At = Terminal.Texts.IndexOf(W.ProblemsHeading + ":");
            Assert.That(At, Is.GreaterThan(0), "the form's problems have the attention");
            Assert.That(Terminal.Texts[At + 1], Does.StartWith("  SMTP port: "));
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), "the retry went on");
            Assert.That(OnDisk("Email.yaml"), Does.Contain("to@example.invalid"));
        }

        private static Action Do(Action Act) => Act;

        [Test]
        public void AFailedWrite_StaysWithTheFormsResult_AndTheRetryWritesOnce()
        {
            File.WriteAllText(Email, EmailText());
            int Writes = 0;
            bool Fail = true;
            ConfigFileReplace.Step = (Name, Path_) =>
            {
                if (Name == "before-replace")
                {
                    Writes++;
                    if (Fail)
                    {
                        throw new IOException("Email.yaml could not be replaced.");
                    }
                }
            };
            try
            {
                var Terminal = new MenuTerminal(Script(new object[]
                {
                    P(Guided), P(GW.BackLabel), P("Change User name"), L("second-user"), P(GW.SaveAndContinueLabel), Yes, Do(() => Fail = false), P(GW.SaveAndContinueLabel), Yes
                }, LeaveToExit));
                Assert.That(Run(Terminal), Is.EqualTo(0));
                Assert.That(Terminal.Texts, Does.Contain("Error: Email.yaml could not be replaced."), "the form's own result");
                Assert.That(Writes, Is.EqualTo(2), "one failed write, one retry, nothing more");
                Assert.That(OnDisk("Email.yaml"), Does.Contain("smtpUsername: second-user"));
                Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)));
            }
            finally
            {
                ConfigFileReplace.Step = (_, _) => { };
            }
        }

        [Test]
        public void ThePlainTextPassword_IsTypedHidden_NeverShown_AndTheBackupKeepsNoReplacedPassword()
        {
            File.WriteAllText(Email, EmailText());
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(GW.BackLabel), P("Change Password (plain text)"), L(NewPassword), P(GW.SaveAndContinueLabel), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.SecretReads, Is.EqualTo(1), "read by the hidden question");
            Assert.That(OnDisk("Email.yaml"), Does.Contain(NewPassword));
            Assert.That(OnDisk("Email.yaml.bak"), Does.Not.Contain(OldPassword).And.Not.Contain(NewPassword));
            AssertSafe(Terminal);
        }

        [Test]
        public void TheTestEmail_IsTheFormsOwn_SentOnceAfterAy_WritingNothing_AndStartingNoMonitoring()
        {
            File.WriteAllText(Email, EmailText());
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(GW.BackLabel), P("Send a test email"), No, P("Send a test email"), Yes }, LeaveToExit));
            InteractiveSession Console_ = Session(Terminal);
            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));
            Assert.That(Emails, Has.Count.EqualTo(1), "sent once, after the y");
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWording.NotSent));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "a test writes nothing");
            Assert.That(File.Exists(In("Camp.History")), Is.False);
            Assert.That(Console_.Monitoring, Is.Null, "a test starts no monitoring");
            AssertSafe(Terminal);
        }

        [Test]
        public void TheNtfyStep_TurnsNtfyOn_WithARandomTopicNeverShown_ItsTestNotification_AndABackupWithoutTheReplacedTopic()
        {
            File.WriteAllText(In("Ntfy.yaml"), "enabled: false\nserver: https://ntfy.example.invalid\ntopic: " + TopicCanary + "\n");
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByNtfy), P(GW.SaveAndContinueLabel),
                P("Change Enabled: send new openings with ntfy"), Yes, P("Generate a random topic"), P(GW.SaveAndContinueLabel), Yes,
                P(GW.BackLabel), P("Send a test notification"), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(GW.NotReady(GuidedSetupMethod.Ntfy)), "turned off: what the start would refuse");
            Assert.That(OnDisk("Ntfy.yaml"), Does.Contain("enabled: true").And.Not.Contain(TopicCanary), "a new random topic, saved through the form");
            Assert.That(OnDisk("Ntfy.yaml.bak"), Does.Not.Contain(TopicCanary), "the replaced topic is not kept in the backup (D112)");
            Assert.That(Handler.Seen, Has.Count.EqualTo(1), "the test notification, once, after the y");
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Ntfy)));
            string Saved = OnDisk("Ntfy.yaml")!;
            string NewTopic = Saved.Split('\n').Single(L_ => L_.StartsWith("topic: ", StringComparison.Ordinal)).Substring("topic: ".Length);
            Assert.That(Terminal.AllText, Does.Not.Contain(NewTopic), "the topic is shown only on request");
            AssertSafe(Terminal);
        }

        [Test]
        public void AMissingEmailYaml_IsCreatedOnlyByItsFormsCreate_AfterAy_AndSaveAndContinueComesOnlyThen()
        {
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P("Create Email.yaml"), No, Do(() => Assert.That(File.Exists(Email), Is.False, "No creates nothing")), P("Create Email.yaml"), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            List<List<string>> Forms = Terminal.MenusOf("Email.yaml");
            Assert.That(Forms.First(), Is.EqualTo(new[] { "Create Email.yaml", GW.BackLabel, W.LeaveLabel }), "a missing file: only Create, and the ways out");
            Assert.That(Forms.Last(), Does.Contain(GW.SaveAndContinueLabel), "created: Save and continue is offered");
            Assert.That(Names(), Is.EqualTo(new[] { "Email.yaml" }), "the form's own sample");
            AssertNothingSentOrStarted();
        }

        [Test]
        public void TheEndOfTheInput_InAFormWithChanges_EndsTheConsole_AndSavesNothing()
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P(Guided), P(ByEmail), P("Change SMTP server"), L("other.example.invalid"), P(GW.SaveAndContinueLabel), End);
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo(InteractiveWording.InputEnded));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "the save's question was never answered: nothing saved");
        }

        [Test]
        public void SaveAndContinue_AgainWithNothingToSave_GoesOnWithoutAQuestionOrASecondWrite()
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            DateTime? Written = null;
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P("Change Recipients (sendToAddressList)"), P("Add recipient"), L("to@example.invalid"), P("Done"), P(GW.SaveAndContinueLabel), Yes,
                Do(() => Written = File.GetLastWriteTimeUtc(Email)), P(GW.BackLabel), P(GW.SaveAndContinueLabel)
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Prompts.Count(T => T == InteractiveSettingsWording.SaveQuestion("Email.yaml") + " [y/N]: "), Is.EqualTo(1), "asked only when it would write");
            Assert.That(File.GetLastWriteTimeUtc(Email), Is.EqualTo(Written), "the second Save and continue wrote nothing");
            Assert.That(Headings(Terminal).Count(H => H == Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), Is.EqualTo(2));
        }

        // ---- step 3: a search ----

        [Test]
        public void NewSearch_IsTheFlowsNewSearch_TheExampleNotEnabledAsToday_AndTheSameNameAgainCreatesNoSecondFile()
        {
            File.WriteAllText(Email, EmailText());
            //the oracle: the Searches menu's form's own New search in another folder (TD-66 not begun: the example, not enabled)
            string Other = Path.Join(Secrets, "oracle");
            Directory.CreateDirectory(Other);
            var Oracle = new SearchesViewModel(Other, () => UtcNow.ToLocalTime(), Campgrounds, () => new FilterChoiceSource(Id => CampgroundFilterChoices.None(Id), () => "db", Job => Job(), Job => { Job(); return Task.CompletedTask; }));
            Oracle.Show();
            Oracle.NewSearchName = "Lake trip";
            Oracle.NewSearchCommand.Execute(null);

            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(SW.Plain(SW.NewSearchLabel)), L("Lake trip"), Yes, P(GW.BackToSearches), P(SW.Plain(SW.NewSearchLabel)), L("Lake trip"), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(OnDisk("Lake trip.yaml"), Is.EqualTo(File.ReadAllText(In("Lake trip.yaml", Other))), "the shared New search's file, byte for byte");
            Assert.That(OnDisk("Lake trip.yaml"), Does.Contain("autoRun: false"));
            Assert.That(Names().Count(N => N.StartsWith("Lake trip", StringComparison.Ordinal)), Is.EqualTo(1), "never a second file for the same name");
            Assert.That(Terminal.Texts, Has.Some.StartWith("Info: Created Lake trip.yaml"));
            Assert.That(Terminal.Texts, Has.Some.StartWith("Error: "), "the form refuses the same name, in its words");
            AssertNothingSentOrStarted();
            AssertSafe(Terminal);
        }

        [Test]
        public void ASearchsPage_ShowsItsMainSettingsOnly_AndSaveAndContinue_GoesOnOnceItIsEnabled_KeepingTheOtherSettings()
        {
            File.WriteAllText(Email, EmailText());
            string Text = SearchText("Trip", AutoRun: false, "includeEquipment:\n- Tent\n");
            File.WriteAllText(In("Trip.yaml"), Text);
            string Label = W.SetUp(InteractiveSearchesWording.File("Trip.yaml", Form().Files.Single().ListDetails, Form().Files.Single().ListStatus));
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(Label), P(GW.SaveAndContinueLabel), P("Change Enabled (autoRun)"), Yes, P(GW.SaveAndContinueLabel), Yes
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            List<string> Page = Terminal.MenusOf("Trip.yaml").First();
            Assert.That(Page, Is.EqualTo(new[]
            {
                "Change Name (displayName)", "Change Enabled (autoRun)", "Change Campground (campgroundID)", "Change People (totalHumans)",
                "Change Dates to search (searchBy)", "Change Days or months ahead (searchValue)", "Change Dates (searchValueDates)",
                "Change Consecutive nights (consecutiveDays)", GW.SaveAndContinueLabel, "Reload from disk", GW.BackToSearches, GW.BackLabel, W.LeaveLabel
            }), "the main settings, no days, filters or Save As");
            Assert.That(Page, Has.None.EqualTo("Change Days of the week").And.None.EqualTo("Change Campsite filters").And.None.EqualTo(SW.Plain(SW.SaveAsLabel)));
            Assert.That(Terminal.Texts, Does.Contain(GW.SearchNotReady), "not enabled yet: the flow's notice");
            Assert.That(OnDisk("Trip.yaml"), Is.EqualTo(Text.Replace("autoRun: false", "autoRun: true", StringComparison.Ordinal)), "only autoRun changed; the days, consecutive nights and filters kept");
            Assert.That(Headings(Terminal), Does.Contain(Heading(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email)));
            AssertNothingSentOrStarted();
        }

        // PT1 (v0.4.0's phase 13): the search step's problems are the shared check's, in this console's words for where the
        // search files are edited (its Searches menu), so no note translates the Searches tab any more
        [Test]
        public void TheSearchStepsProblems_NameThisConsolesSearchesMenu_TheSameProblemOtherwise()
        {
            File.WriteAllText(Email, EmailText());
            File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: false));
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided) }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Problem = GuidedSetupStatus.Read(Folder, UtcNow, SearchPlace.Menu).SearchProblems.Single();
            Assert.That(Problem, Is.EqualTo(GuidedSetupStatus.Read(Folder, UtcNow).SearchProblems.Single().Replace("the Searches tab", "the Searches menu")), "the shared problem, its place's words only");
            Assert.That(Problem, Does.EndWith("No search is Enabled (autoRun): in the Searches menu, check Enabled (autoRun) for a search with a campground and save it."));
            Assert.That(Terminal.Texts, Does.Contain("  " + Problem));
            Assert.That(Terminal.AllText, Does.Not.Contain("Searches tab"), "no word of the desktop GUI's tab, and no note translating it");
            AssertNothingSentOrStarted();
        }

        // the Searches menu's form as the shared layer gives it, for a file's line
        private SearchesViewModel Form()
        {
            var Searches = new SearchesViewModel(Folder, () => UtcNow.ToLocalTime(), Campgrounds, () => new FilterChoiceSource(Id => CampgroundFilterChoices.None(Id), () => "db", Job => Job(), Job => { Job(); return Task.CompletedTask; }));
            GeneralSettingsViewModel G = new SettingsViewModel(Folder).General;
            Searches.SetOutput(G.OutputToValue, G.SavedOutputToValue);
            Searches.Show();
            return Searches;
        }

        [Test]
        public void AProblemOfASettingTheStepDoesNotShow_IsSaid_AndKeepsTheUserOnTheStep_WritingNothing()
        {
            File.WriteAllText(Email, EmailText());
            string Text = SearchText("Trip", AutoRun: true, "includeEquipment:\n- ~\n- Tent\n");
            File.WriteAllText(In("Trip.yaml"), Text);
            string Label = W.SetUp(InteractiveSearchesWording.File("Trip.yaml", Form().Files.Single().ListDetails, Form().Files.Single().ListStatus));
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(Label), P(GW.SaveAndContinueLabel) }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.OtherSettingsProblem));
            Assert.That(Terminal.Texts, Has.Some.StartWith("  includeEquipment: "));
            Assert.That(OnDisk("Trip.yaml"), Is.EqualTo(Text));
            Assert.That(Headings(Terminal), Has.None.EqualTo(Heading(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email)));
        }

        [Test]
        public void WithoutTheCampgroundData_TheSearchStepSaysSo_AndOffersTheRefresh_WhichStartsOnlyAfterAy()
        {
            File.WriteAllText(Email, EmailText());
            Prepared = false;
            Directory.CreateDirectory(Cache);
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                Refreshes++;
                Prepared = true;
                return new RidbRefreshResult(RidbRefreshOutcome.Refreshed, RidbRefreshStage.Completed, true, UtcNow, TimeSpan.FromSeconds(3), RidbRefreshResult.NoProblems, null, null, false);
            };
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(W.RefreshLabel), No, P(W.RefreshLabel), Yes }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.DataMissing));
            Assert.That(Terminal.Texts, Does.Contain("Nothing was started."), "No starts no refresh");
            Assert.That(Refreshes, Is.EqualTo(1), "the Campgrounds menu's own refresh, once, after the y");
            List<List<string>> Lists = Terminal.MenusOf(GW.SearchesHeading);
            Assert.That(Lists.First(), Does.Contain(W.RefreshLabel));
            Assert.That(Lists.Last(), Does.Not.Contain(W.RefreshLabel), "prepared now: no refresh offered");
            Assert.That(Terminal.Texts.Count(T => T == W.DataMissing), Is.EqualTo(2), "said while it is missing, not after the refresh installed it");
        }

        [Test]
        public void AFailedRefresh_IsSaidInTheSharedWords_ChangesNothing_AndTheStepOffersItAgain()
        {
            File.WriteAllText(Email, EmailText());
            Prepared = false;
            Directory.CreateDirectory(Cache);
            InteractiveCampgroundRefresh.Service = (Request, Progress, Token) =>
            {
                Refreshes++;
                return new RidbRefreshResult(RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading, false, null, TimeSpan.FromSeconds(3), RidbRefreshResult.NoProblems, null, null, false);
            };
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(W.RefreshLabel), Yes }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Refreshes, Is.EqualTo(1), "nothing retried");
            Assert.That(Terminal.Texts, Has.Some.StartWith("Error: "), "the refresh's own result");
            Assert.That(Terminal.MenusOf(GW.SearchesHeading), Has.Count.EqualTo(2).And.All.Contain(W.RefreshLabel), "still not prepared: offered again");
            Assert.That(Prepared, Is.False);
            AssertSafe(Terminal);
        }

        [Test]
        public void AControlCharacterInASearchFilesName_OrValue_IsNeverWrittenToTheTerminal()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows file names hold no control characters");
            }
            File.WriteAllText(Email, EmailText());
            string Odd = "Odd\u001b[31m.yaml";
            File.WriteAllText(In(Odd), SearchText("Trip", AutoRun: false).Replace("displayName: Trip", "displayName: \"Bell\\aand\\e[2J\"", StringComparison.Ordinal));
            SearchFileViewModel Listed = Form().Files.Single();
            string Label = W.SetUp(InteractiveSettingsForm.Visible(InteractiveSearchesWording.File(Listed.FileName, Listed.ListDetails, Listed.ListStatus)));
            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(Label), P(GW.BackToSearches) }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.AllText, Does.Contain("Odd\uFFFD[31m.yaml").And.Contain("Bell\uFFFDand\uFFFD[2J"), "shown with the control characters replaced");
            AssertSafe(Terminal);
        }

        // ---- step 4: review and turn on ----

        [Test]
        public void TurningTheMonitoringOn_SavesOutputToThroughTheGeneralForm_AfterAy_AndStartsNothing()
        {
            File.WriteAllText(Email, EmailText());
            File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: true));
            //the oracle: the shared flow in another folder
            string Other = Path.Join(Secrets, "oracle");
            Directory.CreateDirectory(Other);
            File.WriteAllText(In("Email.yaml", Other), EmailText());
            File.WriteAllText(In("Trip.yaml", Other), SearchText("Trip", AutoRun: true));
            Assert.That(GuidedSetupFlow.TurnOn(new SettingsViewModel(Other).General, GuidedSetupMethod.Email, GuidedSetupStatus.Read(Other, DateTime.Now), Other, W.Host).Next, Is.EqualTo(GuidedSetupStep.Done));

            var Terminal = new MenuTerminal(Script(new object[] { P(Guided), P(GW.TurnOnLabel(GuidedSetupMethod.Email)), No, P(GW.TurnOnLabel(GuidedSetupMethod.Email)), Yes }, LeaveToExit));
            InteractiveSession Console_ = Session(Terminal);
            Assert.That(Run(Terminal, Console_), Is.EqualTo(0));

            Assert.That(Terminal.Texts, Does.Contain(W.TurnOnHelp(GuidedSetupMethod.Email)).And.Contain(GW.NoGeneralYet).And.Contain(W.NothingSaved));
            //the bytes the shared flow writes: General.yaml created with its defaults (its sample's refresh time is the moment it is
            //created) and outputTo
            static string WithoutTheSampleTime(string Text) => System.Text.RegularExpressions.Regex.Replace(Text, "lastRidbDataRefresh: [^\n]*", "lastRidbDataRefresh: <the sample's>");
            Assert.That(WithoutTheSampleTime(OnDisk("General.yaml")!), Is.EqualTo(WithoutTheSampleTime(File.ReadAllText(In("General.yaml", Other)))));
            Assert.That(OnDisk("General.yaml"), Does.Contain("outputTo: Email"));
            Assert.That(Terminal.Texts, Does.Contain("Info: General.yaml was saved."));
            Assert.That(Headings(Terminal), Does.Contain(GW.DoneHeading(GuidedSetupMethod.Email)));
            AssertNothingSentOrStarted(Console_);
            AssertSafe(Terminal);
        }

        [Test]
        public void WhileTheFilesAreNotReady_TurnOnIsNotOffered_AndTheReviewLeadsToTheStepsNotDone()
        {
            File.WriteAllText(Email, EmailText());
            File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: true));
            Dictionary<string, string> Before = Fingerprint();
            //the review, Back to the search, then the search turned off on disk before Continue: the review says so and saves nothing
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(GW.BackLabel), Do(() => File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: false))), P(GW.ContinueLabel),
                P(GW.StepName(GuidedSetupStep.Search, GuidedSetupMethod.Email))
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(W.TurnOnUnavailable));
            Assert.That(Terminal.MenusOf(GW.StepName(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email)).First(), Does.Contain(GW.TurnOnLabel(GuidedSetupMethod.Email)), "ready at first");
            Assert.That(Terminal.MenusOf(GW.StepName(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email)).Last(), Does.Not.Contain(GW.TurnOnLabel(GuidedSetupMethod.Email)));
            Assert.That(OnDisk("General.yaml"), Is.Null, "nothing saved, not even a sample");
            List<string> All = Headings(Terminal).ToList();
            Assert.That(All.Skip(All.LastIndexOf(Heading(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email)) + 1).First(H => H.StartsWith("Step ", StringComparison.Ordinal)),
                Is.EqualTo(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), "the review leads to the step not done");
        }

        [Test]
        public void TheDoneStep_NotComplete_ListsTheStepsToDo_AndLeadsToThem_AndBackIsTheReview()
        {
            WriteComplete();
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P(GW.BackLabel), Do(() => File.WriteAllText(In("Trip.yaml"), SearchText("Trip", AutoRun: false))), P(GW.ContinueLabel),
                P(GW.StepName(GuidedSetupStep.Search, GuidedSetupMethod.Email))
            }, LeaveToExit));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Headings(Terminal), Does.Contain(GW.DoneHeading(GuidedSetupMethod.Email)), "complete at first");
            Assert.That(Headings(Terminal), Does.Contain(GW.NotDoneHeading), "the done step reads the saved files again");
            Assert.That(Terminal.Texts, Does.Contain(GW.NotDoneText).And.Contain("  Step 3 of 4: A search to check (to do)"));
            Assert.That(Terminal.MenusOf(W.DoneChoices).Last(), Is.EqualTo(new[]
                { GW.StepName(GuidedSetupStep.Search, GuidedSetupMethod.Email), GW.StepName(GuidedSetupStep.TurnOn, GuidedSetupMethod.Email), GW.BackLabel, W.LeaveLabel }),
                "no Start while it is not complete");
            Assert.That(Headings(Terminal).SkipWhile(H => H != GW.NotDoneHeading).Skip(1).First(H => H.StartsWith("Step ", StringComparison.Ordinal)),
                Is.EqualTo(Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), "the step to do leads there");
        }

        // ---- leaving and coming back ----

        [Test]
        public void LeavingAndOpeningItAgain_ResumesFromTheSavedFiles_AndNoFileOfItsOwnIsWritten()
        {
            File.WriteAllText(Email, EmailText(Recipient: false));
            var Terminal = new MenuTerminal(Script(new object[]
            {
                P(Guided), P(ByEmail), P("Change Recipients (sendToAddressList)"), P("Add recipient"), L("to@example.invalid"), P("Done"),
                P(GW.SaveAndContinueLabel), Yes, P(W.LeaveLabel), P(Guided), P(W.LeaveLabel), P("Exit")
            }));
            Assert.That(Run(Terminal), Is.EqualTo(0));
            Assert.That(Headings(Terminal).Count(H => H == Heading(GuidedSetupStep.Search, GuidedSetupMethod.Email)), Is.EqualTo(2), "after the save, and again when it is opened again");
            Assert.That(Names(), Is.EqualTo(new[] { "Email.yaml", "Email.yaml.bak" }), "nothing but the form's own file and backup: no progress or record");
        }

        // ---- what the code reaches ----

        private static List<string> Reached()
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveGuidedSetup).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveGuidedSetup).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheGuidedSetupCode_ReachesTheSharedFlowAndTheConsolesOwnForms_AndNothingThatDecidesWritesSendsOrMonitorsByItself()
        {
            List<string> Called = Reached();
            string[] Forbidden =
            {
                //no YAML, writer, session, validation, secret, sender or test of its own: only the forms and the flow reach them
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.ConfigValidation", "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.SmtpPassword::Resolve",
                "CampertronLibrary.function.Base.NtfyToken::Resolve", "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.SecretFile",
                "CampertronLibrary.function.Base.NtfyTopic", "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.Base.Notifications",
                //no step's save, create or outputTo of its own: only the flow saves, creates a search and sets outputTo
                "CampertronLibrary.function.Forms.SettingsFileViewModel::Save", "CampertronLibrary.function.Forms.SettingsFileViewModel::get_CreateCommand",
                "CampertronLibrary.function.Forms.SearchesViewModel::get_NewSearchCommand", "CampertronLibrary.function.Forms.SearchesViewModel::get_SaveAsCommand",
                "CampertronLibrary.function.Forms.GeneralSettingsViewModel::get_OutputTo", "CampertronLibrary.function.Forms.GuidedSetupStatus::IsDone",
                //no second monitoring, refresh, search, results run or web host
                "CampertronLibrary.function.Forms.MonitoringController::.ctor", "CampertronLibrary.function.Forms.MonitoringController::Act",
                "CampertronLibrary.function.Forms.MonitoringController::StartWithHost", "CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoringHost::.ctor",
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.RecDotOrg.api.",
                "CampertronLibrary.function.RecDotOrg.data.RidbRefreshService", "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                "CampertronLibrary.function.RecDotOrg.data.Load::", "CampertronLibrary.function.Forms.ResultsStart", "CampertronLibrary.function.Forms.DataOperationGate",
                "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.", "CampertronLibrary.function.Base.Menu",
                //no file, folder, process, network or environment of its own
                "System.IO.File::Write", "System.IO.File::Create", "System.IO.File::Delete", "System.IO.File::Move", "System.IO.File::Copy", "System.IO.File::Open",
                "System.IO.File::Read", "System.IO.File::Append", "System.IO.File::Replace", "System.IO.Directory::Create", "System.IO.Directory::Delete",
                "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.StreamReader", "System.Diagnostics.Process", "System.Net.", "System.Environment::",
                "System.Console::", "System.Threading.Thread::", "System.Threading.Tasks."
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            //and the scan sees the shared flow and the console's own forms, refresh and monitoring it does drive
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.Forms.GuidedSetupFlow::get_Steps", "CampertronLibrary.function.Forms.GuidedSetupFlow::Previous", "CampertronLibrary.function.Forms.GuidedSetupFlow::StateOf",
                "CampertronLibrary.function.Forms.GuidedSetupFlow::ToDo", "CampertronLibrary.function.Forms.GuidedSetupFlow::ResumeMethod", "CampertronLibrary.function.Forms.GuidedSetupFlow::Choose",
                "CampertronLibrary.function.Forms.GuidedSetupFlow::ContinueNotifications", "CampertronLibrary.function.Forms.GuidedSetupFlow::NewSearch",
                "CampertronLibrary.function.Forms.GuidedSetupFlow::ContinueSearch", "CampertronLibrary.function.Forms.GuidedSetupFlow::TurnOn",
                "CampertronLibrary.function.Forms.GuidedSetupStatus::Read", "CampertronLibrary.function.Forms.GuidedSetupStatus::NextStep", "CampertronLibrary.function.Forms.FormProblems::Of",
                "CampertronLibrary.function.Interactive.Settings.InteractiveSettingsForm::Run", "CampertronLibrary.function.Interactive.Searches.InteractiveSearchForm::Run",
                "CampertronLibrary.function.Interactive.Searches.InteractiveSearches::Compose", "CampertronLibrary.function.Interactive.Searches.InteractiveSearches::NewSearchName",
                "CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundRefresh::Run", "CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoring::HostOf",
                "CampertronLibrary.function.Interactive.Monitoring.InteractiveMonitoring::Act", "CampertronLibrary.function.Forms.MonitoringController::View",
                "CampertronLibrary.function.Interactive.Settings.InteractiveSettingsMenu::SettingsFolder"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheGuidedSetupCode_KeepsNoStaticStateThatChanges_AndHasNoWordOfALaterPhase()
        {
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var Literals = new List<string>();
            foreach (Type T in typeof(InteractiveGuidedSetup).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveGuidedSetup).Namespace))
            {
                foreach (FieldInfo Field in T.GetFields(Declared).Where(F => !F.IsLiteral && !F.Name.Contains('<') && !T.Name.Contains('<')))
                {
                    Assert.That(Field.IsInitOnly, Is.True, T.Name + "." + Field.Name + " is static state that can change");
                }
                foreach (MethodBase Method in T.GetMethods(Declared | BindingFlags.Instance).Cast<MethodBase>().Concat(T.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)))
                {
                    Literals.AddRange(ConsoleStartContractTests.StringLiterals(Method));
                }
                Literals.AddRange(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!));
            }
            Assert.That(Literals.Where(S => S.Any(char.IsControl)), Is.Empty, "no escape sequence or control character");
            foreach (string Later in new[] { "Export", "Import", "Beam", "same campsite", "blank search", "free space", "disk space", "resume", "progress file" })
            {
                Assert.That(Literals.Where(S => S.Contains(Later, StringComparison.OrdinalIgnoreCase)), Is.Empty, "no word of a later phase or of a record of progress: " + Later);
            }
        }
    }
}
