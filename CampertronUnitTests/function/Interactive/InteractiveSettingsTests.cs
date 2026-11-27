using System.Net;
using System.Net.Mail;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Settings;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Settings (v0.4.0's phase 7a, D123; business-rules §43): General.yaml and Web.yaml in the
    // shared forms (SettingsViewModel's General and Web forms, ER-37), hosted at a terminal by phase 7's adapter
    // (InteractiveSettingsForm). Driven from the main menu by the terminal that answers a choice by its label (MenuTerminal)
    // and judged by the files written and the words shown; the shared form, driven directly in another folder, is the
    // oracle where the console must write what the desktop GUI and the web UI write. Nothing may be sent (the email's send
    // seam and ntfy's handler seam count every attempt) and nothing of the web UI's own secrets (Web.password, Web.keys)
    // may be read or shown: their canaries are searched for in every line written (boolean checks).
    [NonParallelizable]
    public class InteractiveSettingsTests : ConfigEditTestBase
    {
        private string Secrets = string.Empty;
        private string SmtpCanary = string.Empty;
        private string TopicCanary = string.Empty;
        private string PasswordRecordCanary = string.Empty;
        private string KeysCanary = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Counting Handler = null!;
        private int EmailSends;

        private string Ntfy => Path.Join(Folder, "Ntfy.yaml");
        private string Web => Path.Join(Folder, "Web.yaml");

        [SetUp]
        public void Seams()
        {
            Secrets = Path.Join(Path.GetTempPath(), "CampertronInteractiveSettings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Secrets);
            SmtpCanary = "PwSmtp-cnry-" + Guid.NewGuid().ToString("N");
            TopicCanary = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            PasswordRecordCanary = "WebPasswordRecord-cnry-" + Guid.NewGuid().ToString("N");
            KeysCanary = "WebKeys-cnry-" + Guid.NewGuid().ToString("N");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            EmailSends = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => EmailSends++;
            Handler = new Counting();
            NtfyPublisher.CreateHandler = () => Handler;
            SmtpPassword.GetEnvironment = Name => null;
            NtfyToken.GetEnvironment = Name => null;
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            Handler.Dispose();
            if (Directory.Exists(Secrets))
            {
                Directory.Delete(Secrets, true);
            }
        }

        private sealed class Counting : HttpMessageHandler
        {
            internal int Seen;
            protected override HttpResponseMessage Send(HttpRequestMessage Request, CancellationToken Cancel)
            {
                Seen++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage Request, CancellationToken Cancel) => Task.FromResult(Send(Request, Cancel));
        }

        // ---- the files and the script ----

        private const string GeneralText = "# my general settings\noutputTo: Console   # where results go\nrefreshRidbDataDayInterval: 30\n" +
            "lastRidbDataRefresh: '2020-01-02T03:04:05.0000000Z'\nautoRefresh: false\n";
        private const string SearchText = "displayName: Form search\nautoRun: true\ncampgroundID: '232447'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";
        private const string WebText = "# my web settings\npasswordProtection: true\nnetworkListening: true\nallowedHosts:\n- campertron.example.invalid\n";
        private string EmailText => "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + SmtpCanary +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private string NtfyText => "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + TopicCanary + "\n";

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static readonly TerminalInput No = TerminalInput.Line("n");

        private static readonly object[] IntoGeneral = { P("Settings"), P("General settings") };
        private static readonly object[] IntoWeb = { P("Settings"), P("Web UI settings") };
        private static readonly object[] OutToExit = { P("Back to Settings"), P("Back to the main menu"), P("Exit") };
        private const string EmailOutput = "Email: check every minute and send new results by email (and with ntfy when Ntfy.yaml enables it)";
        private const string NtfyOutput = "Ntfy: check every minute and send new results with ntfy only (Ntfy.yaml)";
        private const string HtmlOutput = "HtmlFile: write and open a results page";

        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        private InteractiveSession Session(InteractiveTerminal Terminal, string? Settings = null, bool Container = false, bool NoFolder = false) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc),
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Settings ?? Folder, CachePath = Path.Join(Secrets, "cache"), ContainerMode = Container });

        private int Run(MenuTerminal Terminal, string? Settings = null) => InteractiveConsole.Run(Session(Terminal, Settings));

        private Dictionary<string, string> Fingerprint(string? Of = null) =>
            Directory.GetFileSystemEntries(Of ?? Folder, "*", SearchOption.AllDirectories).ToDictionary(P => P,
                P => File.Exists(P) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P))) + File.GetLastWriteTimeUtc(P).Ticks : "dir");

        // the web UI's own secret files, never read, shown or changed by the console
        private void WriteWebUIsOwnFiles()
        {
            File.WriteAllText(Path.Join(Folder, "Web.password"), PasswordRecordCanary);
            File.WriteAllText(Path.Join(Folder, "Web.keys"), KeysCanary);
        }

        // no canary in anything written: the terminal's lines, standard output and standard error (boolean checks)
        private void AssertNoSecret(MenuTerminal Terminal)
        {
            string All = Terminal.AllText + "\n" + Out + "\n" + Err;
            foreach (string Canary in new[] { SmtpCanary, TopicCanary, PasswordRecordCanary, KeysCanary })
            {
                Assert.That(All.Contains(Canary, StringComparison.Ordinal), Is.False, "a secret was written (its canary is not repeated here)");
            }
        }

        // nothing sent, nothing of the monitoring: no history, no lock, no database
        private void AssertNothingSentOrStarted()
        {
            Assert.That(EmailSends, Is.Zero, "no email");
            Assert.That(Handler.Seen, Is.Zero, "no ntfy request");
            Assert.That(File.Exists(Path.Join(Folder, "Camp.History")), Is.False, "no Camp.History");
            Assert.That(File.Exists(Path.Join(Folder, "Campertron.monitoring.lock")), Is.False, "no monitoring lock");
            Assert.That(File.Exists(Path.Join(Folder, "RecreationDotOrg.db")), Is.False, "no campground data prepared");
        }

        // the shared form itself, as the desktop GUI and the web UI drive it, in a folder of its own with the same files
        private string Oracle<T>(Func<SettingsViewModel, T> FormOf, Action<T> Edit, params (string Name, string Text)[] Files) where T : SettingsFileViewModel
        {
            string Other = Path.Join(Secrets, "oracle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Other);
            foreach ((string Name, string Text) in Files)
            {
                Write(Path.Join(Other, Name), Text);
            }
            T Form = FormOf(new SettingsViewModel(Other, () => Now));
            Edit(Form);
            Form.Save();
            if (Form is WebSettingsViewModel { HasConfirmation: true } Access)
            {
                Access.ConfirmSave();
            }
            Assert.That(Form.StatusIsError, Is.False, "the oracle's save failed: " + Form.StatusText);
            return Other;
        }

        // the form's state as it was shown last before a prompt: the lines from the form's title (its state's heading) to
        // the prompt's first appearance after the given point
        private static List<string> StateBefore(MenuTerminal Terminal, string Title, string Prompt, int From = 0)
        {
            int At = Terminal.Written.FindIndex(From, W => W.Kind == "prompt" && W.Text == Prompt);
            Assert.That(At, Is.GreaterThanOrEqualTo(0), "the prompt was asked: " + Prompt);
            int Heading = Terminal.Written.FindLastIndex(At, W => W.Kind == "heading" && W.Text == Title);
            return Terminal.Written.Skip(Heading).Take(At - Heading).Select(W => W.Text).ToList();
        }

        // ---- entering Settings ----

        [Test]
        public void EnteringSettings_ShowsTheSummary_AndChangesNothing()
        {
            Write(General, GeneralText);
            WriteWebUIsOwnFiles();
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P("Settings"), P("Back to the main menu"), P("Exit"));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Written, Does.Contain(("heading", "Settings (nothing is saved until you choose to)")));
            Assert.That(Terminal.Texts, Does.Contain("  General.yaml: found; outputTo: Console"));
            Assert.That(Terminal.Texts, Does.Contain("  Web.yaml: not found; web mode uses the defaults shown in Web UI settings, and Save creates the file"));
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWording.SettingsNote));
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Help", "Exit" }));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was created, changed or deleted");
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void TheSettingsMenu_IsGeneralWebAndBack_Only_TheFormsOwnTitles()
        {
            Write(General, GeneralText);
            var Terminal = new MenuTerminal(P("Settings"), End);

            Run(Terminal);

            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "General settings", "Web UI settings", "Back to the main menu" }), "no search, campground, results, refresh, monitoring, guided setup or transfer");
            var Forms = new SettingsViewModel(Folder, () => Now);
            Assert.That(Forms.General.Title, Is.EqualTo(InteractiveSettingsWording.General), "one name for one form");
            Assert.That(Forms.Web.Title, Is.EqualTo(InteractiveSettingsWording.Web));
        }

        [Test]
        public void OpeningBothForms_AndLeavingAtOnce_CreatesNothing_SendsNothing_AndReadsNoneOfTheWebUIsSecrets()
        {
            WriteWebUIsOwnFiles();
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Back to Settings"), P("Web UI settings") }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before), "no General.yaml or Web.yaml was created, and the web UI's own files are as they were");
            Assert.That(Terminal.Written, Does.Contain(("heading", "General settings (General.yaml)")));
            Assert.That(Terminal.Written, Does.Contain(("heading", "Web UI settings (Web.yaml)")));
            Assert.That(Terminal.Texts, Does.Contain("Warning: General.yaml does not exist."), "the form's own state");
            Assert.That(Terminal.Texts, Does.Contain("  Require a password (passwordProtection): on"), "a new Web.yaml shows the defaults");
            Assert.That(Terminal.Texts, Does.Contain("  Listen on the network (networkListening): on"));
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [TestCase(false, false, "The settings folder does not exist yet, so there are no settings to change. A start without arguments, or the desktop app, creates it.")]
        [TestCase(true, false, "The settings folder was not found, so there are no settings to change. In a container, mount the settings folder at /config.")]
        [TestCase(false, true, "This computer gives Campertron no folder for its settings, so there are no settings to change.")]
        public void WithoutTheSettingsFolder_SettingsSaysSo_AndCreatesNothing(bool Container, bool NoFolder, string Words)
        {
            string Missing = Path.Join(Folder, "not-created");
            var Terminal = new MenuTerminal(P("Settings"), P("Exit"));

            int Code = InteractiveConsole.Run(Session(Terminal, Missing, Container, NoFolder));

            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Words));
            Assert.That(Directory.Exists(Missing), Is.False, "the console never creates the settings folder");
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("  General.yaml", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void TheSummary_SaysOutputTo_WhetherTheMonitoringsStartWouldAcceptTheSavedFiles_AndWebYamlsSwitches_NeverAValue()
        {
            var Said = new List<string>();
            List<(string Label, string Value)> Facts(int Expected)
            {
                List<(string Label, string Value)> Got = InteractiveSettings.Summary(Folder, Now);
                Assert.That(Got.Select(F => F.Label), Is.EqualTo(new[] { "General.yaml", "Web.yaml" }));
                Said.AddRange(Got.Select(F => F.Value));
                return Got;
            }
            Assert.That(Facts(2)[0].Value, Is.EqualTo("not found: a start without arguments, or the desktop app, creates it (outputTo: Console); choose General settings to create it now"));
            Assert.That(Facts(2)[1].Value, Is.EqualTo("not found; web mode uses the defaults shown in Web UI settings, and Save creates the file"));

            Write(General, GeneralText);
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found; outputTo: Console"));

            Write(General, "outputTo: Email\n");
            Write(Email, EmailText);
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found; outputTo: Email; the monitoring's start would accept the saved settings"));
            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 0\n");
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found; outputTo: Email; the monitoring's start would refuse the saved settings: choose General settings to see why"));

            Write(General, "outputTo: Ntfy\n");
            Write(Ntfy, NtfyText);
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found; outputTo: Ntfy; the monitoring's start would accept the saved settings"));
            File.Delete(Path.Join(Folder, "Trip.yaml"));
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found; outputTo: Ntfy; the monitoring's start would refuse the saved settings: choose General settings to see why"), "D73: no search to check");

            File.WriteAllBytes(General, new byte[] { 0x6F, 0x3A, 0x20, 0xFF, 0xFE, 0x0A });
            Assert.That(Facts(2)[0].Value, Is.EqualTo("found, but it can't be used as it is: choose it to see why"));

            Write(Web, "passwordProtection: false\nnetworkListening: false\n");
            Assert.That(Facts(2)[1].Value, Is.EqualTo("found; passwordProtection: off; networkListening: off"));
            Write(Web, WebText);
            Assert.That(Facts(2)[1].Value, Is.EqualTo("found; passwordProtection: on; networkListening: on"));
            Write(Web, "passwordProtection: maybe\n");
            Assert.That(Facts(2)[1].Value, Is.EqualTo("found, but it can't be used as it is: choose it to see why"));

            string All = string.Join("\n", Said);
            Assert.That(All.Contains(SmtpCanary) || All.Contains(TopicCanary) || All.Contains("example.invalid"), Is.False, "no value of a file");
        }

        [Test]
        public void EscCtrlCOrFiveUnusableAnswers_AtTheSettingsMenu_ReturnToTheMainMenu_AndTheEndOfTheInputEndsTheConsole()
        {
            Write(General, GeneralText);
            var Terminal = new MenuTerminal(P("Settings"), Esc, P("Settings"), CtrlC, P("Settings"),
                L("x"), L("9"), L(""), L("0"), L("-1"), P("Settings"), End);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Prompts.Count(T => T == "Choose a number from 1 to 5: "), Is.EqualTo(4), "the main menu after each");
            Assert.That(Terminal.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False, "Ctrl+C below the main menu cancels, it does not stop");
        }

        // ---- General.yaml ----

        [Test]
        public void AMissingGeneralYaml_IsCreatedOnlyWhenConfirmed_WithTheSharedFormsSample()
        {
            Dictionary<string, string> Before = Fingerprint();
            var Refused = new MenuTerminal(Script(IntoGeneral, new object[] { P("Create General.yaml"), No, P("Create General.yaml"), Esc }, OutToExit));
            Run(Refused);
            Assert.That(Fingerprint(), Is.EqualTo(Before), "No and Esc create nothing");
            Assert.That(Refused.Prompts, Does.Contain("Create General.yaml with the default settings? [y/N]: "));
            Assert.That(Refused.LastChoices, Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Help", "Exit" }));

            var Created = new MenuTerminal(Script(IntoGeneral, new object[] { P("Create General.yaml"), Yes }, OutToExit));
            Run(Created);

            string Other = Path.Join(Secrets, "oracle-create");
            Directory.CreateDirectory(Other);
            new SettingsViewModel(Other, () => Now).General.Create();
            //the sample's lastRidbDataRefresh is fifty years before its creation: every other line is the shared Create's
            static string[] Lines(string Path_) => File.ReadAllLines(Path_).Where(L => !L.StartsWith("lastRidbDataRefresh:", StringComparison.Ordinal)).ToArray();
            Assert.That(Lines(General), Is.EqualTo(Lines(Path.Join(Other, "General.yaml"))), "the bytes the desktop GUI's and the web UI's Create write");
            GeneralConfig Saved = Yaml.LoadSettingsFile<GeneralConfig>(General);
            Assert.That((Saved.OutputTo, Saved.AutoRefresh, Saved.RefreshRidbDataDayInterval), Is.EqualTo((OutputType.Console, false, 30d)));
            Assert.That(Created.Texts, Does.Contain("Info: Created General.yaml with the default settings."));
            Assert.That(Created.Texts, Does.Contain("  Output: Console: show results in the console"), "the form shows the new file at once");
            AssertNothingSentOrStarted();
        }

        [Test]
        public void EditingEveryGeneralSetting_WritesTheBytesTheSharedFormWrites_AndItsBackup()
        {
            Write(General, GeneralText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                P("Change Output"), P(HtmlOutput),
                P("Change Refresh the campground data automatically"), Yes,
                P("Change Refresh interval (days)"), L("7.5"),
                P("Change Last refresh (UTC)"), L("2026-01-31T00:00:00Z"),
                P("Show the changes Save would write"),
                P("Save"), Yes
            }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Other = Oracle(S => S.General, Form =>
            {
                Form.OutputTo.Selected = Form.OutputTo.Choices.Single(C => C.Label == HtmlOutput);
                Form.AutoRefresh.IsChecked = true;
                Form.RefreshInterval.Text = "7.5";
                Form.LastRefresh.Text = "2026-01-31T00:00:00Z";
            }, ("General.yaml", GeneralText));
            Assert.That(File.ReadAllBytes(General).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "General.yaml"))), Is.True, "the same YAML as the shared form writes");
            Assert.That(File.ReadAllBytes(General + ".bak").SequenceEqual(File.ReadAllBytes(Path.Join(Other, "General.yaml.bak"))), Is.True, "and the same backup");
            Assert.That(Text(General), Does.StartWith("# my general settings\n").And.Contain("outputTo: HtmlFile   # where results go"), "the comments are kept");
            Assert.That(Text(General + ".bak"), Is.EqualTo(GeneralText), "the backup is the previous version");
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Info: Saved General.yaml.", StringComparison.Ordinal)), Is.True);
            Assert.That(Terminal.Written, Does.Contain(("heading", "Changes to General.yaml")), "the shared preview");
            Assert.That(Terminal.Texts, Does.Contain("    At each start, Campertron refreshes the campground data when the interval has passed since the last refresh."), "the form's explanation follows the switch");
            AssertNothingSentOrStarted();
        }

        [Test]
        public void OutputTosChoices_AreTheSharedFormsChoices_AndAUnitTestOutputIsKeptButNotOffered()
        {
            Write(General, GeneralText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Change Output"), Esc }, OutToExit));
            Run(Terminal);
            var Form = new SettingsViewModel(Folder, () => Now).General;
            int Asked = Terminal.Written.FindLastIndex(W => W.Kind == "heading" && W.Text == "Output");
            List<string> Offered = Terminal.Written.Skip(Asked + 1).TakeWhile(W => W.Kind == "out").Select(W => W.Text).Where(T => T.StartsWith("  ", StringComparison.Ordinal) && char.IsDigit(T[2])).ToList();
            Assert.That(Offered, Is.EqualTo(Form.OutputTo.Choices.Select((C, i) => "  " + (i + 1) + ". " + C.Label).ToList()), "the shared form's four outputs, in its words");
            Assert.That(Terminal.Texts, Does.Contain("Cancelled."));

            Write(General, "outputTo: UnitTest\n");
            var Kept = new MenuTerminal(Script(IntoGeneral, OutToExit));
            Run(Kept);
            Assert.That(Kept.Texts, Does.Contain("  Output: UnitTest: for Campertron's tests only (kept as the file has it)"), "kept as the file has it");
        }

        [Test]
        public void D71_OutputToEmail_IsNotSaved_WhileTheMonitoringsStartWouldRefuseEmailYaml_InTheFormsWords()
        {
            Write(General, GeneralText);
            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\n");
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Change Output"), P(EmailOutput), P("Save"), Yes, P("Back to Settings"), Yes, P("Back to the main menu"), P("Exit") }));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            var Shared = new SettingsViewModel(Folder, () => Now).General;
            Shared.OutputTo.Selected = Shared.OutputTo.Choices.Single(C => C.Label == EmailOutput);
            Assert.That(Shared.OutputTo.IsError, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("    " + Shared.OutputTo.MessageText), "the form's own words: which file, which setting");
            Assert.That(Terminal.Texts, Does.Contain("Error: Nothing was saved: fix the errors shown first."));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was saved");
            Assert.That(Terminal.Prompts, Does.Contain("Leave without saving your changes to General.yaml? They are not kept. [y/N]: "), "the user stayed at the form with the change");
            AssertNothingSentOrStarted();
        }

        [Test]
        public void D73_OutputToEmail_IsNotSaved_WhileTheMonitoringHasNoSearchToCheck()
        {
            Write(General, GeneralText);
            Write(Email, EmailText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Change Output"), P(EmailOutput), P("Save"), Yes, P("Revert changes"), Yes }, OutToExit));

            Run(Terminal);

            var Shared = new SettingsViewModel(Folder, () => Now).General;
            Shared.OutputTo.Selected = Shared.OutputTo.Choices.Single(C => C.Label == EmailOutput);
            Assert.That(Shared.OutputTo.MessageText, Does.Contain("Campertron would refuse to start"));
            Assert.That(Terminal.Texts, Does.Contain("    " + Shared.OutputTo.MessageText));
            Assert.That(Fingerprint(), Is.EqualTo(Before));
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes to General.yaml were reverted."));
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void OutputToEmail_IsSaved_WhenTheStartWouldAcceptTheSavedFiles_AndStartsNothing_SendsNothing()
        {
            Write(General, GeneralText);
            Write(Email, EmailText);
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Change Output"), P(EmailOutput), P("Save"), Yes }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0), "the console goes on to Exit: nothing started");

            Assert.That(Text(General), Does.Contain("outputTo: Email   # where results go"));
            Dictionary<string, string> After = Fingerprint();
            Assert.That(After.Keys.Except(Before.Keys), Is.EqualTo(new[] { General + ".bak" }), "only the backup is new");
            Assert.That(Before.Where(F => After[F.Key] != F.Value).Select(F => F.Key), Is.EqualTo(new[] { General }), "only General.yaml changed");
            Assert.That(Terminal.Texts, Does.Contain("  General.yaml: found; outputTo: Email; the monitoring's start would accept the saved settings"), "the menu's summary after the save");
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void OutputToNtfy_WithoutNtfyYaml_IsRefused_AndTheFormsNtfyLineSaysWhy()
        {
            Write(General, GeneralText);
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Change Output"), P(NtfyOutput), P("Save"), Yes, P("Revert changes"), Yes }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Info: Ntfy.yaml does not exist, so ntfy is off. To add ntfy notifications, choose Ntfy.yaml above."), "outputTo: Console");
            Assert.That(Terminal.Texts, Does.Contain("    Error: Ntfy.yaml does not exist, and outputTo: Ntfy needs it: choose Ntfy.yaml above, turn Enabled on, enter the server and the topic, and save."));
            Assert.That(Terminal.Texts, Does.Contain("Error: Nothing was saved: fix the errors shown first."));
            Assert.That(Text(General), Is.EqualTo(GeneralText));
            Assert.That(File.Exists(Ntfy), Is.False, "the General form never writes another file");
            AssertNothingSentOrStarted();
        }

        [Test]
        public void AChangeOfEmailYamlOnDisk_IsSeenByOutputTosCheck_WhenTheChoicesComeAgain()
        {
            Write(General, GeneralText);
            Write(Email, "smtpServer: smtp.example.invalid\nsmtpPort: 587\n");
            Write(Path.Join(Folder, "Trip.yaml"), SearchText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                P("Change Output"), P(EmailOutput),
                (Action)(() => Write(Email, EmailText)),
                P("Change Refresh interval (days)"), Esc,
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            string Error = "With outputTo: Email, Campertron would refuse to start because of Email.yaml.";
            Assert.That(StateBefore(Terminal, "General settings", "Choose a number from 1 to 8: ").Any(T => T.Contains(Error, StringComparison.Ordinal)), Is.True, "before the change on disk");
            Assert.That(StateBefore(Terminal, "General settings", "Save General.yaml now? [y/N]: ").Any(T => T.Contains(Error, StringComparison.Ordinal)), Is.False,
                "the saved Email.yaml was read again when the choices came again (D71)");
            Assert.That(Text(General), Does.Contain("outputTo: Email"));
            AssertNothingSentOrStarted();
        }

        [Test]
        public void ANumberOrDateThatIsNoValue_IsTheFormsError_NothingIsSaved_AndRevertTakesItBack()
        {
            Write(General, GeneralText);
            Write(General + ".bak", "outputTo: HtmlFile\n");
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                P("Change Refresh interval (days)"), L("a month"),
                P("Change Last refresh (UTC)"), L("yesterday"),
                P("Revert changes"), No, P("Revert changes"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a number of days, such as 30 or 7.5."));
            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a UTC date and time such as 2026-01-31T00:00:00Z."));
            Assert.That(Terminal.Texts, Does.Contain("Your changes are still here; nothing was saved."), "No keeps them");
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes to General.yaml were reverted."));
            Assert.That(Text(General), Is.EqualTo(GeneralText));
            Assert.That(Text(General + ".bak"), Is.EqualTo("outputTo: HtmlFile\n"), "Revert never restores or reads the backup");
        }

        [Test]
        public void AChangeOnDisk_ReloadsACleanGeneralForm_AndIsAConflictForAnEditedOne_ResolvedByTheSessionNeverOverwritten()
        {
            Write(General, GeneralText);
            string Theirs = GeneralText.Replace("autoRefresh: false", "autoRefresh: true") + "# another program\n";
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                (Action)(() => Write(General, GeneralText.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 14"))),
                P("Change Refresh interval (days)"), Esc,
                P("Change Refresh interval (days)"), L("7"),
                (Action)(() => Write(General, Theirs.Replace("refreshRidbDataDayInterval: 30", "refreshRidbDataDayInterval: 14"))),
                P("Change Output"), Esc,
                P("Apply my changes to the new version"),
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Info: General.yaml was changed on disk and has been reloaded."));
            Assert.That(Terminal.Texts, Does.Contain("  Refresh interval (days): 14"));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("General.yaml was changed by another program: Warning: ", StringComparison.Ordinal)), Is.True);
            Assert.That(Text(General), Does.Contain("refreshRidbDataDayInterval: 7").And.Contain("autoRefresh: true").And.Contain("# another program"),
                "my change applied to their version: theirs kept, nothing written over");
        }

        [Test]
        public void AGeneralYamlThatCantBeRead_OffersOnlyReloadAndBack_AndWritesNothing()
        {
            File.WriteAllBytes(General, new byte[] { 0x6F, 0x3A, 0x20, 0xFF, 0xFE, 0x0A });
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[] { P("Reload from disk"), End }));

            Run(Terminal);

            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Reload from disk", "Back to Settings" }));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Warning: ", StringComparison.Ordinal)), Is.True, "the form says why");
            Assert.That(Fingerprint(), Is.EqualTo(Before));
        }

        [Test]
        public void TheGeneralForm_SaysWhenAChangeApplies_TheRefreshExplanation_AndTheNtfyLine_AsBothUIsShowThem()
        {
            Write(General, GeneralText);
            Write(Ntfy, NtfyText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, OutToExit));

            Run(Terminal);

            var Shared = new SettingsViewModel(Folder, () => Now).General;
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWording.GeneralApplies));
            Assert.That(Terminal.Texts, Does.Contain("    " + Shared.RefreshExplanation));
            Assert.That(Terminal.Texts, Does.Contain("    " + MessageSeverity.AccessibleText(Shared.NtfyStatus, Shared.NtfyStatusSeverity)));
            Assert.That(Shared.NtfyStatus, Does.StartWith("Ntfy.yaml: ntfy is on."));
            AssertNoSecret(Terminal);
        }

        [TestCase("AtTheChoices")]
        [TestCase("AtAFieldsQuestion")]
        [TestCase("AtSavesQuestion")]
        [TestCase("AtTheLeaveQuestion")]
        [TestCase("AtTheConfirmation")]
        public void TheEndOfTheInput_InTheGeneralOrWebForm_SavesNothing_AndEndsTheConsoleWithZero(string Where)
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            Dictionary<string, string> Before = Fingerprint();
            object[] Steps = Where switch
            {
                "AtTheChoices" => Script(IntoGeneral, new object[] { P("Change Refresh interval (days)"), L("9"), End }),
                "AtAFieldsQuestion" => Script(IntoGeneral, new object[] { P("Change Refresh interval (days)"), L("9"), P("Change Output"), End }),
                "AtSavesQuestion" => Script(IntoGeneral, new object[] { P("Change Refresh interval (days)"), L("9"), P("Save"), End }),
                "AtTheLeaveQuestion" => Script(IntoWeb, new object[] { P("Change Listen on the network (networkListening)"), No, P("Back to Settings"), End }),
                _ => Script(IntoWeb, new object[] { P("Change Require a password (passwordProtection)"), No, P("Save"), Yes, End }),
            };
            var Terminal = new MenuTerminal(Steps);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
        }

        [Test]
        public void ACancelledQuestion_WithUnsavedChanges_SavesNothing_AndKeepsTheChanges()
        {
            Write(General, GeneralText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                P("Change Refresh interval (days)"), L("9"),
                P("Change Output"), Esc,
                P("Change Refresh the campground data automatically"), CtrlC,
                P("Save"), Esc,
                P("Save"), No,
                P("Revert changes"), CtrlC,
                (Action)(() => Assert.That(Fingerprint(), Is.EqualTo(Before), "no cancelled question saved")),
                P("Back to Settings"), Yes, P("Back to the main menu"), P("Exit")
            }));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before));
            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(4));
            Assert.That(Terminal.Texts.Count(T => T == "  Refresh interval (days): 9 (changed)"), Is.GreaterThanOrEqualTo(5), "the change stayed through every cancel");
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False);
        }

        // ---- Web.yaml ----

        [Test]
        public void AMissingWebYaml_IsANewFile_NothingIsWrittenUntilSave_ThenTheSharedFormsBytes_OwnerOnly()
        {
            Write(General, GeneralText);
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Allowed host names (allowedHosts)"), P("Add host name"), L("campertron.example.invalid"), P("Done"),
                (Action)(() => Assert.That(File.Exists(Web), Is.False, "nothing is written while editing")),
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            string Other = Oracle(S => S.Web, Form =>
            {
                Form.AllowedHosts.AddRow();
                Form.AllowedHosts.Rows[0].Text = "campertron.example.invalid";
            }, ("General.yaml", GeneralText));
            Assert.That(File.ReadAllBytes(Web).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Web.yaml"))), Is.True, "the same YAML as the shared form writes");
            Assert.That(Terminal.Texts, Does.Contain("Info: Created Web.yaml with your settings."));
            Assert.That(File.Exists(Web + ".bak"), Is.False, "a new file has no backup");
            if (!OperatingSystem.IsWindows())
            {
                Assert.That(File.GetUnixFileMode(Web), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
            }
            AssertNothingSentOrStarted();
        }

        [Test]
        public void EveryWebSetting_IsEditedThroughTheForm_AndWritesTheSharedFormsBytes_AndItsBackup()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            WriteWebUIsOwnFiles();
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Listen on the network (networkListening)"), No,
                P("Change Allowed host names (allowedHosts)"),
                    P("Add host name"), L("home.example.invalid"),
                    P("Change a host name"), P("Host name 1: campertron.example.invalid"), L("campertron.home.invalid"),
                    P("Move a host name up"), P("Host name 2: home.example.invalid"),
                    P("Done"),
                P("Change Trusted reverse proxies (trustedProxies)"), P("Add proxy"), L("172.18.0.0/16"), P("Add proxy"), L("172.18.0.2"), P("Remove a proxy"), P("Proxy 1: 172.18.0.0/16"), P("Done"),
                P("Change Require a password (passwordProtection)"), Yes,
                P("Show the changes Save would write"),
                P("Save"), Yes
            }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Other = Oracle(S => S.Web, Form =>
            {
                Form.NetworkListening.IsChecked = false;
                Form.AllowedHosts.AddRow();
                Form.AllowedHosts.Rows[1].Text = "home.example.invalid";
                Form.AllowedHosts.Rows[0].Text = "campertron.home.invalid";
                Form.AllowedHosts.Rows[1].UpCommand!.Execute(null);
                Form.TrustedProxies.AddRow();
                Form.TrustedProxies.Rows[0].Text = "172.18.0.0/16";
                Form.TrustedProxies.AddRow();
                Form.TrustedProxies.Rows[1].Text = "172.18.0.2";
                Form.TrustedProxies.Rows[0].RemoveCommand!.Execute(null);
                Form.PasswordProtection.IsChecked = true;
            }, ("General.yaml", GeneralText), ("Web.yaml", WebText));
            Assert.That(File.ReadAllBytes(Web).SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Web.yaml"))), Is.True, "the same YAML as the shared form writes");
            Assert.That(File.ReadAllBytes(Web + ".bak").SequenceEqual(File.ReadAllBytes(Path.Join(Other, "Web.yaml.bak"))), Is.True, "and the same backup");
            Assert.That(Text(Web), Does.StartWith("# my web settings\n").And.Contain("networkListening: false"));
            Assert.That(Text(Web + ".bak"), Is.EqualTo(WebText));
            Assert.That(Terminal.Written.Any(W => W.Text == "Save without a password?"), Is.False, "the password stays required: no confirmation");
            Assert.That(Text(Path.Join(Folder, "Web.password")), Is.EqualTo(PasswordRecordCanary), "the web UI's password record is never touched");
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void AHostOrProxyTheValidationRefuses_BlocksSave_InTheFormsWords_AndNothingIsWritten()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Allowed host names (allowedHosts)"), P("Add host name"), L("http://bad.example.invalid:8790"), P("Done"),
                P("Change Trusted reverse proxies (trustedProxies)"), P("Add proxy"), L("999.18.0.2"), P("Done"),
                P("Save"), Yes, P("Revert changes"), Yes
            }, OutToExit));

            Run(Terminal);

            var Shared = new SettingsViewModel(Folder, () => Now).Web;
            Shared.AllowedHosts.AddRow();
            Shared.AllowedHosts.Rows[1].Text = "http://bad.example.invalid:8790";
            Shared.TrustedProxies.AddRow();
            Shared.TrustedProxies.Rows[0].Text = "999.18.0.2";
            Assert.That(Shared.AllowedHosts.Rows[1].HasMessage && Shared.TrustedProxies.Rows[0].HasMessage, Is.True, "the shared validation refuses both");
            Assert.That(Terminal.Texts, Does.Contain("       " + Shared.AllowedHosts.Rows[1].MessageText));
            Assert.That(Terminal.Texts, Does.Contain("       " + Shared.TrustedProxies.Rows[0].MessageText));
            Shared.Save();
            Assert.That(Terminal.Texts, Does.Contain(MessageSeverity.AccessibleText(Shared.StatusText, MessageSeverity.Error)), "the form's own refusal");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was saved");
        }

        [Test]
        public void TurningThePasswordOff_OnTheNetwork_AsksTheFormsOneConfirmation_OnlyItsFirstChoiceSaves()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            WriteWebUIsOwnFiles();
            var Shared = new SettingsViewModel(Folder, () => Now).Web;
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Require a password (passwordProtection)"), No,
                P("Save"), Yes, P(Shared.CancelConfirmationLabel),
                (Action)(() => Assert.That(Text(Web), Is.EqualTo(WebText), "Don't save writes nothing")),
                P("Save"), Yes, Esc,
                (Action)(() => Assert.That(Text(Web), Is.EqualTo(WebText), "Esc writes nothing")),
                P("Save"), Yes, CtrlC,
                P("Save"), Yes, L("x"), L("3"), L(""), L("0"), L("-1"),
                (Action)(() => Assert.That(Text(Web), Is.EqualTo(WebText), "Ctrl+C and five unusable answers write nothing")),
                P("Save"), Yes, P(Shared.ConfirmLabel),
                P("Change Allowed host names (allowedHosts)"), P("Add host name"), L("home.example.invalid"), P("Done"),
                P("Save"), Yes
            }, OutToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Written.Count(W => W.Kind == "heading" && W.Text == Shared.ConfirmationHeading), Is.EqualTo(5), "asked at each of the five saves without a password, never for the save after it");
            Assert.That(Terminal.Texts, Does.Contain("Warning: Without a password, everyone who can reach this computer on the network can open Campertron's web UI and change its settings, searches and notifications. Turn the password off only on a network you trust, or let the web UI listen on this computer only."));
            Assert.That(Terminal.Texts, Does.Contain("  1. Save without a password, reachable from the network"));
            Assert.That(Terminal.Texts, Does.Contain("  2. Don't save"));
            Assert.That(Terminal.Texts.Count(T => T == "Info: Nothing was saved. Your changes are still here."), Is.EqualTo(4), "the form's words after Don't save, Esc, Ctrl+C and the five answers");
            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(3), "Esc, Ctrl+C and the five unusable answers");
            Assert.That(Terminal.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));
            Assert.That(Text(Web), Does.Contain("passwordProtection: false").And.Contain("- home.example.invalid"), "the confirmed save, then the later one");
            Assert.That(Text(Path.Join(Folder, "Web.password")), Is.EqualTo(PasswordRecordCanary), "the password record is the web UI's: never touched");
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void ThePasswordOff_OnThisComputerOnly_IsSavedWithoutTheConfirmation()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Listen on the network (networkListening)"), No,
                P("Change Require a password (passwordProtection)"), No,
                P("Save"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Written.Any(W => W.Text == "Save without a password?"), Is.False, "D81: only a save that opens the web UI to the network asks");
            Assert.That(Text(Web), Does.Contain("passwordProtection: false").And.Contain("networkListening: false"));
        }

        [Test]
        public void TheWebForm_SaysWhatTheFileIs_ThePrecedenceAndWhereThePasswordIsSet_AndNeverReadsTheWebUIsOwnFiles()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            WriteWebUIsOwnFiles();
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[] { P("Change Require a password (passwordProtection)"), Esc }, OutToExit));

            Run(Terminal);

            var Shared = new SettingsViewModel(Folder, () => Now).Web;
            Assert.That(Terminal.Texts, Does.Contain(Shared.Explanation), "web mode reads it when it starts: a restart applies a save");
            Assert.That(Terminal.Texts, Does.Contain(Shared.Precedence));
            Assert.That(Terminal.Texts, Does.Contain(Shared.PasswordProtection.Hint), "the password itself is set and changed in the web UI");
            Assert.That(Fingerprint(), Is.EqualTo(Before), "Web.password and Web.keys are as they were");
            AssertNoSecret(Terminal);
        }

        [Test]
        public void AChangeOnDiskOfWebYaml_IsAConflictForAnEditedForm_NeverOverwritten()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            string Theirs = WebText + "trustedProxies:\n- 172.18.0.2\n";
            var Terminal = new MenuTerminal(Script(IntoWeb, new object[]
            {
                P("Change Listen on the network (networkListening)"), No,
                (Action)(() => Write(Web, Theirs)),
                P("Change Require a password (passwordProtection)"), Esc,
                P("Discard my changes and use the file on disk"), No,
                P("Discard my changes and use the file on disk"), Yes
            }, OutToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Web.yaml was changed by another program: Warning: ", StringComparison.Ordinal)), Is.True);
            Assert.That(Terminal.Prompts, Does.Contain("Discard your changes and use Web.yaml as it is on disk? [y/N]: "));
            Assert.That(Text(Web), Is.EqualTo(Theirs), "the other program's version was never written over");
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes were discarded; Web.yaml shows the version on disk."));
        }

        // ---- the choices: the forms' own, nothing more ----

        [Test]
        public void TheFormsChoices_AreTheirFields_TheirPreviewSaveRevertReload_AndBack_Only()
        {
            Write(General, GeneralText);
            Write(Web, WebText);
            var Terminal = new MenuTerminal(Script(IntoGeneral, new object[]
            {
                (Action)(() => { }),
                P("Change Refresh interval (days)"), L("9"),
                P("Revert changes"), Yes, P("Back to Settings"),
                P("Web UI settings"), P("Change Listen on the network (networkListening)"), No,
                P("Revert changes"), Yes
            }, OutToExit));

            Run(Terminal);

            string[] GeneralFields = { "Change Output", "Change Refresh the campground data automatically", "Change Refresh interval (days)", "Change Last refresh (UTC)" };
            string[] WebFields = { "Change Require a password (passwordProtection)", "Change Listen on the network (networkListening)",
                "Change Allowed host names (allowedHosts)", "Change Trusted reverse proxies (trustedProxies)" };
            string[] Edited = { "Show the changes Save would write", "Save", "Revert changes", "Back to Settings" };
            string[] Clean = { "Reload from disk", "Back to Settings" };
            List<List<string>> Shown = Terminal.Written.Select((W, i) => (W, i)).Where(X => X.W.Kind == "heading" && (X.W.Text == "General.yaml" || X.W.Text == "Web.yaml"))
                .Select(X => Terminal.Written.Skip(X.i + 1).TakeWhile(W => W.Kind == "out").Select(W => W.Text).Where(T => T.StartsWith("  ", StringComparison.Ordinal) && T.Length > 2 && char.IsDigit(T[2]))
                    .Select(T => T.Substring(T.IndexOf(". ", StringComparison.Ordinal) + 2)).ToList()).ToList();
            Assert.That(Shown[0], Is.EqualTo(GeneralFields.Concat(Clean)), "General.yaml as saved: no test, no monitoring, no other file");
            Assert.That(Shown[1], Is.EqualTo(GeneralFields.Concat(Edited)), "with a change");
            Assert.That(Shown.Last(L => L.Contains(WebFields[0])), Is.EqualTo(WebFields.Concat(Clean)), "Web.yaml after Revert: no password entry, no user, no HTTPS");
            Assert.That(Shown.Single(L => L.Contains(WebFields[0]) && L.Contains("Save")), Is.EqualTo(WebFields.Concat(Edited)));
        }

        // ---- what the settings' code can reach ----

        private static List<string> Reached(Func<Type, bool> Which)
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveSettings).Assembly.GetTypes().Where(Which))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        [Test]
        public void TheSettingsCode_ReachesTheSharedGeneralAndWebForms_AndNothingOfTheWebUIsPasswordKeysSessionsOrHost()
        {
            List<string> Called = Reached(T => T.Namespace == typeof(InteractiveSettings).Namespace);
            string[] Forbidden =
            {
                //the web UI's own: its password and record, the setup code, sessions, keys, the access rule, the host
                "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.", "System.Security.Cryptography.",
                //no YAML, writer, session or validation of its own: only the forms reach them
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.ConfigValidation", "CampertronLibrary.function.Base.SecretFile", "CampertronLibrary.function.Base.CampsiteConfig",
                "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.NtfyTopic", "CampertronLibrary.function.Base.TestNotification::",
                //no monitoring, search, campground, results, refresh, guided setup, transfer or start
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.RecDotOrg.api.",
                "CampertronLibrary.function.Forms.Monitoring", "CampertronLibrary.function.Forms.Searches", "CampertronLibrary.function.Forms.SearchFile",
                "CampertronLibrary.function.Forms.Campground", "CampertronLibrary.function.Forms.Results", "CampertronLibrary.function.Forms.Refresh",
                "CampertronLibrary.function.Forms.GuidedSetupFlow", "CampertronLibrary.function.Forms.FilterSelector", "CampertronLibrary.function.Base.Menu",
                "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.RidbRefreshService",
                "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                //no file, folder, process, thread, network or environment of its own
                "System.IO.File::", "System.IO.Directory::Create", "System.IO.Directory::Delete", "System.IO.FileStream", "System.IO.StreamWriter",
                "System.IO.StreamReader", "System.Diagnostics.Process", "System.Threading.Thread", "System.Threading.Tasks.Task::Run", "System.Net.",
                "System.Environment::", "System.Console::"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called.Where(C => C.EndsWith("::get_Session", StringComparison.Ordinal)), Is.Empty, "never a form's editing session");
            //and the scan sees the shared forms it does drive
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.Forms.SettingsViewModel::get_General", "CampertronLibrary.function.Forms.SettingsViewModel::get_Web",
                "CampertronLibrary.function.Forms.GeneralSettingsViewModel::get_NtfyStatus",
                "CampertronLibrary.function.Forms.GeneralSettingsViewModel::get_RefreshExplanation", "CampertronLibrary.function.Forms.WebSettingsViewModel::ConfirmSave",
                "CampertronLibrary.function.Forms.WebSettingsViewModel::CancelConfirmation", "CampertronLibrary.function.Forms.WebSettingsViewModel::get_Explanation",
                "CampertronLibrary.function.Forms.WebSettingsViewModel::get_Precedence", "CampertronLibrary.function.Forms.WebSettingsViewModel::get_ConfirmationText",
                "CampertronLibrary.function.Forms.SettingsFileViewModel::Save", "CampertronLibrary.function.Forms.SettingsFileViewModel::CheckForExternalChanges",
                "CampertronLibrary.function.Forms.GuidedSetupStatus::FilesReady"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheSettingsMenusOwnCode_OpensOnlyTheGeneralAndWebForms_AndNamesNoSecretFileOfTheWebUI()
        {
            List<string> Called = Reached(T => T == typeof(InteractiveSettings) || T.DeclaringType == typeof(InteractiveSettings));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.Forms.SettingsViewModel::get_General").And.Contain("CampertronLibrary.function.Forms.SettingsViewModel::get_Web"));
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.Forms.SettingsViewModel::get_Email", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Forms.SettingsViewModel::get_Ntfy", StringComparison.Ordinal)), Is.Empty, "Email.yaml and Ntfy.yaml are Notifications'");
            foreach (Type T in typeof(InteractiveSettings).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveSettings).Namespace))
            {
                const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Assert.That(ConsoleStartContractTests.StringLiterals(Method).Where(S => S.Contains("Web.password", StringComparison.Ordinal) || S.Contains("Web.keys", StringComparison.Ordinal)), Is.Empty);
                }
                Assert.That(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!)
                    .Where(S => S.Contains("Web.password", StringComparison.Ordinal) || S.Contains("Web.keys", StringComparison.Ordinal) || S.Any(char.IsControl)), Is.Empty);
            }
        }
    }
}
