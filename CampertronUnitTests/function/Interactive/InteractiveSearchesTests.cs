using System.Net;
using System.Net.Mail;
using System.Reflection;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Searches;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Searches (v0.4.0's phase 8, D124; business-rules §43): the search files in the shared form
    // both other UIs show (SearchesViewModel and its SearchFileViewModels, ER-37), hosted at a terminal by
    // InteractiveSearches and InteractiveSearchForm over phase 7's form host. Driven from the main menu by the terminal that
    // answers a choice by its label (MenuTerminal) and judged by the files written and the words shown; the shared form,
    // driven directly in another folder with the same files, is the oracle where the console must write and say what the
    // desktop GUI and the web UI write and say. The campground data is a list in memory and a function (InteractiveSearches'
    // seams): no database is opened, and with the data "not prepared" neither is called. Nothing may be sent (the email's
    // send seam and ntfy's handler seam count every attempt), searched or started, and no other settings file's secret may be
    // shown (canaries, boolean checks).
    [NonParallelizable]
    public class InteractiveSearchesTests : ConfigEditTestBase
    {
        private static readonly DateTime UtcNow = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
        private string Secrets = string.Empty;
        private string SmtpCanary = string.Empty;
        private string TopicCanary = string.Empty;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Func<HttpMessageHandler> OriginalHandler = null!;
        private Func<string, bool> OriginalPrepared = null!;
        private Func<string, List<CampgroundPickerEntry>> OriginalCampgrounds = null!;
        private Func<string, string, CampgroundFilterChoices> OriginalChoices = null!;
        private Func<string, string> OriginalState = null!;
        private Counting Handler = null!;
        private int EmailSends;
        private bool Prepared;
        private bool ChoicesFail;
        private int CampgroundReads;
        private int ChoiceReads;

        [SetUp]
        public void Seams()
        {
            Secrets = Path.Join(Path.GetTempPath(), "CampertronInteractiveSearches-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Secrets);
            SmtpCanary = "PwSmtp-cnry-" + Guid.NewGuid().ToString("N");
            TopicCanary = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalHandler = NtfyPublisher.CreateHandler;
            OriginalPrepared = InteractiveSearches.DataPrepared;
            OriginalCampgrounds = InteractiveSearches.ReadCampgrounds;
            OriginalChoices = InteractiveSearches.ReadFilterChoices;
            OriginalState = InteractiveSearches.DatabaseState;
            EmailSends = 0;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => EmailSends++;
            Handler = new Counting();
            NtfyPublisher.CreateHandler = () => Handler;
            Prepared = true;
            ChoicesFail = false;
            CampgroundReads = 0;
            ChoiceReads = 0;
            InteractiveSearches.DataPrepared = Folder_ => Prepared;
            InteractiveSearches.ReadCampgrounds = Folder_ =>
            {
                CampgroundReads++;
                return Campgrounds();
            };
            InteractiveSearches.ReadFilterChoices = (Folder_, Id) =>
            {
                ChoiceReads++;
                return ChoicesFail ? throw new IOException("cnry-a-database-problem") : Choices(Id);
            };
            InteractiveSearches.DatabaseState = Folder_ => "db";
        }

        [TearDown]
        public void RestoreSeams()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            NtfyPublisher.CreateHandler = OriginalHandler;
            InteractiveSearches.DataPrepared = OriginalPrepared;
            InteractiveSearches.ReadCampgrounds = OriginalCampgrounds;
            InteractiveSearches.ReadFilterChoices = OriginalChoices;
            InteractiveSearches.DatabaseState = OriginalState;
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

        // ---- the campground data (in memory) ----

        private static readonly CampgroundPickerEntry UpperPines = new CampgroundPickerEntry(new CampgroundChoice("232447", "UPPER PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park");
        private static readonly CampgroundPickerEntry NorthPines = new CampgroundPickerEntry(new CampgroundChoice("232449", "NORTH PINES", "YOSEMITE VALLEY", "CA"), "Yosemite National Park");
        private static readonly CampgroundPickerEntry Watchman = new CampgroundPickerEntry(new CampgroundChoice("232445", "WATCHMAN CAMPGROUND", "SPRINGDALE", "UT"), "Zion National Park");

        private static List<CampgroundPickerEntry> Campgrounds() => new List<CampgroundPickerEntry> { NorthPines, UpperPines, Watchman };

        // Upper Pines has fourteen campsite names (a long list: its selector has a find, Select all shown and Clear shown)
        private static CampgroundFilterChoices Choices(string Id) => Id == "232445"
            ? new CampgroundFilterChoices(Id, 2, new[] { new FilterChoice("Tent", 2), new FilterChoice("SMALL TENT", 1) },
                new[] { new FilterChoice("STANDARD NONELECTRIC", 2) }, new[] { new FilterChoice("A001", 1), new FilterChoice("A002", 1) },
                new[] { new FilterChoice("Shade:Yes", 1, "Shade", "Yes") })
            : new CampgroundFilterChoices(Id, 14, new[] { new FilterChoice("Tent", 14), new FilterChoice("RV", 3), new FilterChoice("Trailer", 2) },
                new[] { new FilterChoice("STANDARD NONELECTRIC", 13), new FilterChoice("TENT ONLY NONELECTRIC", 1) },
                Enumerable.Range(1, 14).Select(N => new FilterChoice(N.ToString("000", System.Globalization.CultureInfo.InvariantCulture), 1)),
                new[] { new FilterChoice("Shade:Yes", 9, "Shade", "Yes"), new FilterChoice("Shade:No", 5, "Shade", "No") });

        // ---- the files and the script ----

        private const string AlphaText = "# my first search\ndisplayName: First trip\nautoRun: true\ncampgroundID: '232447'   # Upper Pines\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n";
        private const string BravoText = "displayName: Second trip\nautoRun: false\ncampgroundID: '232445'\ntotalHumans: 4\nsearchBy: MonthsOut\nsearchValue: 2\n" +
            "showMonday: false\nshowTuesday: false\nshowWednesday: false\nshowThursday: false\nshowFriday: true\nshowSaturday: true\nshowSunday: false\nconsecutiveDays: 2\n";
        private string EmailText => "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + SmtpCanary +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private string NtfyText => "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + TopicCanary + "\n";

        private string Alpha => In("Alpha.yaml");
        private string Bravo => In("Bravo.yaml");
        private string In(string Name, string? Of = null) => Path.Join(Of ?? Folder, Name);

        private static Pick P(string Label) => new Pick(Label);
        private static TerminalInput L(string Text) => TerminalInput.Line(Text);
        private static TerminalInput Esc => TerminalInput.Escape;
        private static TerminalInput CtrlC => TerminalInput.Interrupt;
        private static TerminalInput End => TerminalInput.EndOfInput;
        private static readonly TerminalInput Yes = TerminalInput.Line("y");
        private static readonly TerminalInput No = TerminalInput.Line("n");

        private static object[] Script(params object[][] Parts) => Parts.SelectMany(Part => Part).ToArray();

        // the main menu's Searches, then a file of the list by its line
        private object[] Into(string FileName) => new object[] { P("Searches"), P(Label(FileName)) };
        private static readonly object[] BackToExit = { P("Back to Searches"), P("Back to the main menu"), P("Exit") };

        private InteractiveSession Session(InteractiveTerminal Terminal, string? Settings = null, bool Container = false, bool NoFolder = false) =>
            new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => UtcNow,
                () => new CampertronInternalConfig { ConfigPath = NoFolder ? null : Settings ?? Folder, CachePath = Path.Join(Secrets, "cache"), ContainerMode = Container });

        private int Run(MenuTerminal Terminal, string? Settings = null) => InteractiveConsole.Run(Session(Terminal, Settings));

        private Dictionary<string, string> Fingerprint(string? Of = null) =>
            Directory.GetFileSystemEntries(Of ?? Folder, "*", SearchOption.AllDirectories).ToDictionary(P => P,
                P => File.Exists(P) ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P))) + File.GetLastWriteTimeUtc(P).Ticks : "dir");

        // the other settings files, with their secrets, which the search screens never show
        private void WriteSettings(string Output = "Console")
        {
            Write(General, "outputTo: " + Output + "\nautoRefresh: false\n");
            Write(Email, EmailText);
            Write(In("Ntfy.yaml"), NtfyText);
        }

        private void AssertNoSecret(MenuTerminal Terminal)
        {
            string All = Terminal.AllText + "\n" + Out + "\n" + Err;
            foreach (string Canary in new[] { SmtpCanary, TopicCanary, "cnry-a-database-problem" })
            {
                Assert.That(All.Contains(Canary, StringComparison.Ordinal), Is.False, "a secret or an exception's text was written (its canary is not repeated here)");
            }
        }

        // nothing sent, searched or started: no email, no ntfy request, no history, no lock, no database, no cache
        private void AssertNothingSentOrStarted()
        {
            Assert.That(EmailSends, Is.Zero, "no email");
            Assert.That(Handler.Seen, Is.Zero, "no ntfy request");
            Assert.That(File.Exists(In("Camp.History")), Is.False, "no Camp.History");
            Assert.That(File.Exists(In("Campertron.monitoring.lock")), Is.False, "no monitoring lock");
            Assert.That(File.Exists(In("RecreationDotOrg.db")), Is.False, "no campground data prepared or opened");
            Assert.That(Directory.Exists(Path.Join(Secrets, "cache")), Is.False, "no cache: nothing searched");
        }

        // the shared form itself, as the desktop GUI and the web UI drive it, over a folder: the same campground data (or
        // none, as the console has it) and the same clock as the console's
        private SearchesViewModel Form(string Of)
        {
            var Searches = new SearchesViewModel(Of, () => UtcNow.ToLocalTime(), () => Prepared ? Campgrounds() : throw new IOException("not prepared"),
                () => new FilterChoiceSource(Id => Prepared ? Choices(Id) : throw new IOException("not prepared"), () => "db", Job => Job(), Job =>
                {
                    Job();
                    return Task.CompletedTask;
                }));
            GeneralSettingsViewModel General_ = new SettingsViewModel(Of).General;
            Searches.SetOutput(General_.OutputToValue, General_.SavedOutputToValue);
            Searches.Show();
            return Searches;
        }

        // a folder of its own with the same files, its form driven directly (Edit), then saved: the oracle of the bytes
        private string Oracle(string FileName, Action<SearchFileViewModel> Edit, params (string Name, string Text)[] Files)
        {
            string Other = Path.Join(Secrets, "oracle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Other);
            foreach ((string Name, string Text) in Files)
            {
                Write(In(Name, Other), Text);
            }
            SearchesViewModel Searches = Form(Other);
            SearchFileViewModel File_ = Searches.Files.Single(F => F.FileName == FileName);
            Searches.SelectedFile = File_;
            Edit(File_);
            File_.Save();
            Assert.That(File_.StatusIsError, Is.False, "the oracle's save failed: " + File_.StatusText);
            return Other;
        }

        // a file's line in the list: its name and the form's details and state, as the form gives them
        private string Label(string FileName, string? Of = null)
        {
            SearchFileViewModel File_ = Form(Of ?? Folder).Files.Single(F => F.FileName == FileName);
            return InteractiveSearchesWording.File(File_.FileName, File_.ListDetails, File_.ListStatus);
        }

        // the screen shown last before a prompt: the lines from its heading to the prompt's first appearance after From
        private static List<string> StateBefore(MenuTerminal Terminal, string Title, string Prompt, int From = 0)
        {
            int At = Terminal.Written.FindIndex(From, W => W.Kind == "prompt" && W.Text == Prompt);
            Assert.That(At, Is.GreaterThanOrEqualTo(0), "the prompt was asked: " + Prompt);
            int Heading = Terminal.Written.FindLastIndex(At, W => W.Kind == "heading" && W.Text == Title);
            return Terminal.Written.Skip(Heading).Take(At - Heading).Select(W => W.Text).ToList();
        }

        private static readonly string[] FormChoices =
        {
            "Change Name (displayName)", "Change Enabled (autoRun)", "Change Campground (campgroundID)", "Change People (totalHumans)",
            "Change Dates to search (searchBy)", "Change Days or months ahead (searchValue)", "Change Dates (searchValueDates)",
            "Change Consecutive nights (consecutiveDays)", "Change Days of the week", "Change Campsite filters"
        };

        // ---- entering Searches and the list ----

        [Test]
        public void EnteringSearches_ListsTheFormsFiles_WithItsDetailsAndStates_AndChangesNothing()
        {
            WriteSettings();
            Write(Alpha, AlphaText);
            Write(Bravo, BravoText);
            Write(In("Charlie.yaml"), "displayName: [not, closed\n# cnry-a-database-problem\n");
            Write(In("Alpha.yaml.bak"), BravoText);
            Write(In("Notes.yml"), BravoText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P("Searches"), P("Back to the main menu"), P("Exit"));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Written, Does.Contain(("heading", "Searches (nothing is saved until you choose to)")));
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSearchesWording.SearchesNote));
            int ListAt = Terminal.Written.FindIndex(W => W.Kind == "heading" && W.Text == "Search files");
            List<string> Listed = Terminal.Written.Skip(ListAt + 1).TakeWhile(W => W.Kind == "out").Select(W => W.Text).ToList();
            Assert.That(Listed, Is.EqualTo(new[]
            {
                "  1. Alpha.yaml, First trip · Enabled (autoRun) · " + UpperPines.Label + ", No problems",
                "  2. Bravo.yaml, Second trip · Not enabled · " + Watchman.Label + ", No problems",
                "  3. Charlie.yaml, (no name) · not read · no campground, Read-only",
                "  4. New search file", "  5. Look for changes on disk", "  6. Back to the main menu"
            }), "the form's files (never a backup, a .yml or a settings file), its details and states, then New search");
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Guided setup", "Help", "Exit" }));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was created, changed or deleted");
            Assert.That(Terminal.AllText, Does.Not.Contain("[not, closed"), "never a file's raw text");
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
            Assert.That(CampgroundReads, Is.EqualTo(1), "the campgrounds are read once for the list's names");
        }

        [Test]
        public void AnEmptyFolder_SaysSoInTheFormsWords_AndOffersNewSearch()
        {
            WriteSettings();
            var Terminal = new MenuTerminal(P("Searches"), End);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            SearchesViewModel Oracle_ = Form(Folder);
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.NoFiles));
            Assert.That(Terminal.Texts, Does.Contain(Oracle_.FolderNoticeText), "the folder's notice, the form's");
            Assert.That(Oracle_.FolderNoticeText, Does.Contain("There is no search file. Create one with New search."));
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "New search file", "Look for changes on disk", "Back to the main menu" }),
                "no delete, rename, search, results, monitoring, lookup or refresh");
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Directory.GetFiles(Folder, "*.yaml").Select(Path.GetFileName), Is.EquivalentTo(new[] { "General.yaml", "Email.yaml", "Ntfy.yaml" }), "no example search created");
        }

        [TestCase(false, false, "The settings folder does not exist yet, so there are no search files to change. A start without arguments, or the desktop app, creates it.")]
        [TestCase(true, false, "The settings folder was not found, so there are no search files to change. In a container, mount the settings folder at /config.")]
        [TestCase(false, true, "This computer gives Campertron no folder for its settings, so there are no search files to change.")]
        public void WithoutTheSettingsFolder_SearchesSaysSo_AndCreatesNothing(bool Container, bool NoFolder, string Words)
        {
            string Missing = Path.Join(Folder, "not-created");
            var Terminal = new MenuTerminal(P("Searches"), P("Exit"));

            int Code = InteractiveConsole.Run(Session(Terminal, Missing, Container, NoFolder));

            Assert.That(Code, Is.EqualTo(0));
            Assert.That(Terminal.Texts, Does.Contain(Words));
            Assert.That(Directory.Exists(Missing), Is.False, "the console never creates the settings folder");
            Assert.That(CampgroundReads, Is.Zero);
        }

        [Test]
        public void EscCtrlCOrFiveUnusableAnswers_AtTheList_ReturnToTheMainMenu_AndTheEndOfTheInputEndsTheConsole()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(P("Searches"), Esc, P("Searches"), CtrlC, P("Searches"),
                L("x"), L("9"), L(""), L("0"), L("-1"), P("Searches"), End);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Terminal.Prompts.Count(T => T == "Choose a number from 1 to 10: "), Is.EqualTo(4), "the main menu after each");
            Assert.That(Terminal.Texts, Does.Contain("No usable answer after several tries, so the question was cancelled."));
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False, "Ctrl+C below the main menu cancels, it does not stop");
        }

        [Test]
        public void LookForChangesOnDisk_AndEveryTimeTheListIsShown_ReadTheFolderAgain()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(P("Searches"), new Action(() => Write(Bravo, BravoText)), P("Look for changes on disk"),
                new Action(() => File.Delete(Alpha)), P("Look for changes on disk"), P("Back to the main menu"), P("Exit"));

            Run(Terminal);

            List<List<string>> Lists = Terminal.Written.Select((W, I) => (W, I)).Where(X => X.W.Kind == "heading" && X.W.Text == "Search files")
                .Select(X => Terminal.Written.Skip(X.I + 1).TakeWhile(W => W.Kind == "out").Select(W => W.Text).ToList()).ToList();
            Assert.That(Lists.Count, Is.EqualTo(3));
            Assert.That(Lists[0].Count(T => T.Contains("Bravo.yaml")), Is.Zero);
            Assert.That(Lists[1].Count(T => T.Contains("Bravo.yaml")), Is.EqualTo(1), "a file created by another program is listed");
            Assert.That(Lists[2].Count(T => T.Contains("Alpha.yaml")), Is.Zero, "a file deleted by another program is gone");
        }

        [Test]
        public void TheFolderNotice_FollowsGeneralYamlsSavedOutputTo_AsBothOtherUIsShowIt()
        {
            WriteSettings("Email");
            Write(Bravo, BravoText);
            var Terminal = new MenuTerminal(P("Searches"), End);

            Run(Terminal);

            //the web UI's composition: the General form's outputTo handed to the Searches form (D73)
            SearchesViewModel Oracle_ = Form(Folder);
            Assert.That(Oracle_.HasFolderNotice && Oracle_.FolderNoticeIsWarning, Is.True);
            Assert.That(Oracle_.FolderNoticeText, Does.Contain("email mode (outputTo: Email) refuses to start"));
            Assert.That(Terminal.Texts, Does.Contain(Oracle_.FolderNoticeText));
            AssertNoSecret(Terminal);
        }

        // ---- the campground data ----

        [Test]
        public void WithoutTheCampgroundData_NoDatabaseIsOpened_AndTheFormsSayWhy_InTheirOwnWords()
        {
            Prepared = false;
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Campground (campgroundID)"), P("Find a campground"), L("pines"), P("Or enter a campground ID"), L("232445"), P("Done"),
                P("Change Campsite filters"), P("Change Include equipment (includeEquipment)"), P("Done"), P("Done")
            }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That((CampgroundReads, ChoiceReads), Is.EqualTo((0, 0)), "nothing reads a database that is not there");
            Assert.That(Terminal.Texts.Count(T => T == InteractiveSearchesWording.DataMissing), Is.GreaterThanOrEqualTo(3), "the list, the campground and the filters say where it comes from");
            Assert.That(Terminal.Texts, Does.Contain("  Campground (campgroundID): ID 232447 (the campground data can't be read to name it)."));
            Assert.That(Terminal.Texts, Does.Contain("Campertron couldn't read its campground data, so campgrounds can't be listed or checked here."), "the picker's own words");
            Assert.That(Terminal.Texts, Does.Contain("Error: The campground data can't be read, so the ID can't be checked."));
            Assert.That(Terminal.Texts, Does.Contain("Campertron couldn't read this campground's values from its campground data."), "the selectors' own words");
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText), "nothing was changed");
            Assert.That(Terminal.LastChoices.Contains("Refresh") || Terminal.AllText.Contains("Refresh campground data"), Is.False, "no refresh is offered");
            AssertNothingSentOrStarted();
        }

        // ---- New search ----

        [Test]
        public void NewSearch_CreatesTodaysExampleSearch_OnlyAfterAYes_ThroughTheSharedForm_AndOpensItsForm()
        {
            WriteSettings();
            var Terminal = new MenuTerminal(P("Searches"), P("New search file"), L("Yosemite weekends"), Yes, P("Back to Searches"), P("Back to the main menu"), P("Exit"));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            //the oracle: the shared form's New search in a folder of its own
            string Other = Path.Join(Secrets, "oracle-new");
            Directory.CreateDirectory(Other);
            SearchesViewModel Shared = Form(Other);
            Shared.NewSearchName = "Yosemite weekends";
            Shared.NewSearch();
            string Created = In("Yosemite weekends.yaml");
            Assert.That(Text(Created), Is.EqualTo(Text(In("Yosemite weekends.yaml", Other))), "the bytes the desktop GUI's and the web UI's New search write");
            Assert.That(Terminal.Texts, Does.Contain("Info: " + Shared.NewSearchMessage), "the form's own result");
            Assert.That(Terminal.Written, Does.Contain(("heading", "Zion non-group tent friendly sites (Yosemite weekends.yaml)")), "its form opens at once");
            Assert.That(File.Exists(Created + ".bak"), Is.False, "a creation has no backup");
            //today's New search, until D120's TD-66 corrects it for every host at once: the example search's values, not enabled
            CampertronConfig Saved = Yaml.LoadSettingsFile<CampertronConfig>(Created);
            Assert.That((Saved.DisplayName, Saved.AutoRun, Saved.CampgroundID, Saved.TotalHumans, Saved.SearchBy, Saved.SearchValue, Saved.ConsecutiveDays),
                Is.EqualTo(("Zion non-group tent friendly sites", false, "232445", 2, SearchTypes.DaysOut, 7, 1)));
            Assert.That(new[] { Saved.ShowMonday, Saved.ShowTuesday, Saved.ShowWednesday, Saved.ShowThursday, Saved.ShowFriday, Saved.ShowSaturday, Saved.ShowSunday }, Has.All.True);
            Assert.That(Saved.IncludeEquipment, Is.EqualTo(new[] { "Tent", "SMALL TENT", "LARGE TENT OVER 9X12`" }));
            Assert.That(Saved.ExcludeCampsiteType, Is.EqualTo(new[] { "GROUP TENT ONLY AREA NONELECTRIC" }));
            Assert.That(Terminal.Prompts, Does.Contain("New search file name (optional, Enter leaves it empty): "));
            Assert.That(Terminal.Prompts, Does.Contain("Create the search file now? [y/N]: "));
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.NewSearchHelp).And.Contain(SearchesWording.NewSearchNote));
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [TestCase("")]
        [TestCase("General")]
        [TestCase("../outside")]
        [TestCase("bad/name")]
        [TestCase(" leading space")]
        [TestCase("trip.yml")]
        [TestCase("CON")]
        [TestCase("alpha")]
        public void NewSearch_ANameTheFormRefuses_IsSaidInItsWords_AndNothingIsWrittenOrOverwritten(string Name)
        {
            WriteSettings();
            Write(Alpha, AlphaText);
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P("Searches"), P("New search file"), L(Name), Yes, P("Back to the main menu"), P("Exit"));

            Run(Terminal);

            SearchesViewModel Shared = Form(Folder);
            Shared.NewSearchName = Name;
            Shared.NewSearch();
            Assert.That(Shared.NewSearchIsError, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("Error: " + Shared.NewSearchMessage), "the form's own words for the name");
            Assert.That(Shared.NewSearchMessage, Does.EndWith("Nothing was written."));
            Assert.That(Fingerprint(), Is.EqualTo(Before), "nothing was written, and an existing file was not written over");
            Assert.That(File.Exists(Path.Join(Path.GetDirectoryName(Folder)!, "outside.yaml")), Is.False);
            Assert.That(Terminal.Written.Any(W => W.Kind == "heading" && W.Text.Contains("Zion")), Is.False, "no form opens");
        }

        [Test]
        public void NewSearch_No_Esc_CtrlC_AndTheEndOfTheInput_CreateNothing()
        {
            WriteSettings();
            Dictionary<string, string> Before = Fingerprint();
            var Terminal = new MenuTerminal(P("Searches"), P("New search file"), L("One"), No, P("New search file"), L("Two"), Esc,
                P("New search file"), Esc, P("New search file"), CtrlC, P("New search file"), L("Three"), L(""), P("New search file"), End);

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Before));
            Assert.That(Terminal.Texts.Count(T => T == InteractiveSearchesWording.NothingCreated), Is.EqualTo(3), "No, Esc at the question, Enter alone (No)");
            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(2), "Esc and Ctrl+C at the name");
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
        }

        [Test]
        public void NewSearch_AFileAnotherProgramCreatesBeforeTheYes_IsNeverWrittenOver()
        {
            WriteSettings();
            var Terminal = new MenuTerminal(P("Searches"), P("New search file"), L("Race"), new Action(() => Write(In("race.yaml"), BravoText)), Yes,
                P("Back to the main menu"), P("Exit"));

            Run(Terminal);

            Assert.That(Text(In("race.yaml")), Is.EqualTo(BravoText), "the other program's file is as it wrote it");
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Does.Not.Contain("Race.yaml"), "nothing created beside it");
            Assert.That(Terminal.Texts, Does.Contain("Error: race.yaml already exists, and Race.yaml differs from it only in upper and lower case; choose another name. Nothing was written."));
        }

        // ---- a file's form ----

        [Test]
        public void AFilesForm_ShowsEveryFieldTheFormHas_GroupedAsBothUIsGroupThem_AndOffersNothingElse()
        {
            WriteSettings();
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { End }));

            Run(Terminal);

            List<string> State = StateBefore(Terminal, "First trip", "Choose a number from 1 to 13: ").TakeWhile(L => L.Length > 0).ToList();
            Assert.That(State, Is.EqualTo(new[]
            {
                "First trip",
                "  Name (displayName): First trip",
                "  Enabled (autoRun): on",
                "  Campground (campgroundID): " + UpperPines.Label + " — park Yosemite National Park",
                "  People (totalHumans): 2",
                "  Dates to search (searchBy): DaysOut: from today, a number of days ahead",
                "  Days or months ahead (searchValue): 7",
                "  Dates (searchValueDates): No dates.",
                "  Consecutive nights (consecutiveDays): 1",
                "  Days of the week: Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday",
                "  Campsite filters:",
                "    Matching (filterMatching): Contains (not set in this file: a search file without filterMatching uses Contains)",
                "    Include equipment (includeEquipment): (empty)",
                "    Exclude equipment (excludeEquipment): (empty)",
                "    Include campsites (includeSites): (empty)",
                "    Exclude campsites (excludeSites): (empty)",
                "    Include campsite types (includeCampsiteType): (empty)",
                "    Exclude campsite types (excludeCampsiteType): (empty)",
                "    Include attributes (includeAttributes): (empty)",
                "    Exclude attributes (excludeAttributes): (empty)"
            }), string.Join("\n", State));
            Assert.That(Terminal.LastChoices, Is.EqualTo(FormChoices.Concat(new[] { "Reload from disk", "Save as a new search file", "Back to Searches" })),
                "every field of the form; no delete, rename, run, results, monitoring or same-campsite option");
            Assert.That(Terminal.Written, Does.Contain(("heading", "First trip (Alpha.yaml)")));
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText));
            Assert.That(ChoiceReads, Is.EqualTo(1), "the file shown reads its campground's values once");
        }

        [Test]
        public void EverySetting_IsEditedThroughItsQuestion_AndSave_WritesTheFormsOwnBytesAndBackup()
        {
            WriteSettings();
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Name (displayName)"), L("  Weekend trip  "),
                P("Change Enabled (autoRun)"), No,
                P("Change People (totalHumans)"), L(" 4 "),
                P("Change Dates to search (searchBy)"), P("SpecificDates: the dates listed"),
                P("Change Dates (searchValueDates)"), P("Add date"), L("2099-07-04"), P("Add date"), L("2099-07-05"), P("Done"),
                P("Change Days or months ahead (searchValue)"), L("14"),
                P("Change Consecutive nights (consecutiveDays)"), L("2"),
                P("Change Days of the week"), P("Sunday: checked"), P("Monday: checked"), P("Done"),
                P("Change Campground (campgroundID)"), P("Find a campground"), L("zion"), P(Watchman.AccessibleName), P("Done"),
                P("Change Campsite filters"), P("Change Matching (filterMatching)"), P("Exact: a value must equal the campsite's value"),
                P("Change Include equipment (includeEquipment)"), P("Check or uncheck values"), P("SMALL TENT, 1 campsite, not checked"), P("Done"), P("Done"),
                P("Change Exclude campsites (excludeSites)"), P("Add the value to excludeSites"), L(" B 7 "), P("Done"),
                P("Change Include attributes (includeAttributes)"), P("Check or uncheck values"), P("Shade: Yes, 1 campsite, not checked"), P("Done"), P("Done"),
                P("Done"),
                P("Save"), Yes
            }, BackToExit));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            string Other = Oracle("Alpha.yaml", F =>
            {
                F.DisplayName.Text = "  Weekend trip  ";
                F.AutoRun.IsChecked = false;
                F.TotalHumans.Text = " 4 ";
                F.SearchBy.Selected = F.SearchBy.Choices[2];
                F.SearchValueDates.AddRow();
                F.SearchValueDates.Rows[0].Text = "2099-07-04";
                F.SearchValueDates.AddRow();
                F.SearchValueDates.Rows[1].Text = "2099-07-05";
                F.SearchValue.Text = "14";
                F.ConsecutiveDays.Text = "2";
                F.Weekdays[6].IsChecked = false;
                F.Weekdays[0].IsChecked = false;
                F.Campground.FilterText = "zion";
                F.Campground.SelectMatch("232445");
                F.Campground.UseSelected();
                F.Matching.Selected = F.Matching.Choices.Single(C => C.Label.StartsWith("Exact", StringComparison.Ordinal));
                F.IncludeEquipment.Toggle(F.IncludeEquipment.Options.Single(O => O.Label == "SMALL TENT"));
                F.ExcludeSites.CustomText = " B 7 ";
                F.ExcludeSites.AddCustom();
                F.IncludeAttributes.Toggle(F.IncludeAttributes.Options.Single(O => O.IsChoice && O.Label == "Yes"));
            }, ("Alpha.yaml", AlphaText));
            Assert.That(Text(Alpha), Is.EqualTo(Text(In("Alpha.yaml", Other))), "the bytes the desktop GUI's and the web UI's Save write");
            Assert.That(Text(Alpha + ".bak"), Is.EqualTo(Text(In("Alpha.yaml.bak", Other))).And.EqualTo(AlphaText), "the backup is the previous file");
            CampertronConfig Saved = Yaml.LoadSettingsFile<CampertronConfig>(Alpha);
            Assert.That(Saved.DisplayName, Is.EqualTo("  Weekend trip  "), "a name as typed, spaces kept, as the form keeps it");
            Assert.That(Saved.ExcludeSites, Is.EqualTo(new[] { " B 7 " }), "a value exactly as typed (ER-23)");
            Assert.That((Saved.TotalHumans, Saved.CampgroundID, Saved.ConsecutiveDays, Saved.FilterMatching), Is.EqualTo((4, "232445", 2, FilterMatchMode.Exact)));
            Assert.That(Text(Alpha), Does.Contain("# my first search\n"), "the comment is kept");
            Assert.That(Terminal.Prompts, Does.Contain("Save Alpha.yaml now? [y/N]: "));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Info: ", StringComparison.Ordinal) && T.Contains("Alpha.yaml")), Is.True, "the form's result");
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Status", "Notifications", "Settings", "Searches", "Campgrounds", "Results", "Monitoring", "Guided setup", "Help", "Exit" }));
            AssertNoSecret(Terminal);
            AssertNothingSentOrStarted();
        }

        [Test]
        public void ANumberThatIsNoWholeNumber_IsTheFormsError_SaveRefused_AndRevertGivesTheFilesValuesBack_NeverTheBackup()
        {
            Write(Alpha, AlphaText);
            Write(Alpha + ".bak", BravoText);
            List<string> WithErrors = new List<string>();
            MenuTerminal? Seen = null;
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change People (totalHumans)"), L("two"), P("Change Consecutive nights (consecutiveDays)"), L("1.5"),
                new Action(() => WithErrors = Seen!.LastChoices), P("Revert changes"), No, P("Revert changes"), Yes
            }, BackToExit));
            Seen = Terminal;

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a number of people, such as 2."));
            Assert.That(Terminal.Texts, Does.Contain("    Error: Enter a whole number of nights, such as 1."));
            Assert.That(WithErrors, Does.Not.Contain("Save").And.Contain("Revert changes"), "a field's own error: Revert, never Save");
            Assert.That(Terminal.Texts, Does.Contain("Info: Your changes to Alpha.yaml were reverted."));
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWordingKept));
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText));
            Assert.That(Text(Alpha + ".bak"), Is.EqualTo(BravoText), "the backup is never restored");
        }

        private const string InteractiveSettingsWordingKept = "Your changes are still here; nothing was saved.";

        [Test]
        public void AValueTheValidationRefuses_IsShownInItsWords_AndSaveWritesNothing()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Days or months ahead (searchValue)"), L("999999999"), P("Save"), Yes
            }, new object[] { P("Back to Searches"), Yes, P("Back to the main menu"), P("Exit") }));

            Run(Terminal);

            SearchesViewModel Shared = Form(Folder);
            SearchFileViewModel File_ = Shared.Files[0];
            Shared.SelectedFile = File_;
            File_.SearchValue.Text = "999999999";
            Assert.That(File_.SearchValue.IsError, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("    " + File_.SearchValue.MessageText), "the validation's own words at its field");
            File_.Save();
            Assert.That(File_.StatusIsError, Is.True, "the form refuses it");
            Assert.That(Terminal.Texts, Does.Contain(MessageSeverity.AccessibleText(File_.StatusText, MessageSeverity.Error)), "Save's own refusal");
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText));
            Assert.That(File.Exists(Alpha + ".bak"), Is.False);
            Assert.That(Terminal.Prompts, Does.Contain("Leave without saving your changes to Alpha.yaml? They are not kept. [y/N]: "));
        }

        [Test]
        public void NoDayChecked_IsTheFormsWeekdaysMessage()
        {
            Write(Alpha, AlphaText);
            var Days = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
            List<string> DaysShown = new List<string>();
            MenuTerminal? Seen = null;
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { P("Change Days of the week") },
                Days.Select(Day => (object)P(Day + ": checked")).ToArray(), new object[] { new Action(() => DaysShown = Seen!.LastChoices), P("Done"), End }));
            Seen = Terminal;

            Run(Terminal);

            SearchFileViewModel File_ = Form(Folder).Files[0];
            foreach (BoolSettingField Day in File_.Weekdays)
            {
                Day.IsChecked = false;
            }
            Assert.That(File_.HasWeekdaysMessage, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("    " + File_.WeekdaysMessageText), "at the days' line");
            Assert.That(Terminal.Texts, Does.Contain("  Days of the week: none checked (changed)"));
            Assert.That(DaysShown, Is.EqualTo(Days.Select(Day => Day + ": not checked (changed)").Append("Done")));
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.WeekdaysNote));
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText), "the end of the input saves nothing");
        }

        [Test]
        public void Reload_IsOfferedWithoutChanges_AndLeavingWithChanges_AsksAndKeepsNothing()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Reload from disk"), P("Change Name (displayName)"), L("Changed"), P("Back to Searches"), No, Esc, Yes, P(Label("Alpha.yaml")), End
            }));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Info: Reloaded Alpha.yaml."));
            Assert.That(Terminal.Prompts.Count(T => T == "Leave without saving your changes to Alpha.yaml? They are not kept. [y/N]: "), Is.EqualTo(2), "Back and Esc ask");
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSettingsWordingKept));
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText));
            Assert.That(Terminal.Written.Count(W => W.Kind == "heading" && W.Text == "First trip (Alpha.yaml)"), Is.EqualTo(2), "opened again: its name as saved");
            Assert.That(StateBefore(Terminal, "First trip", "Choose a number from 1 to 13: ", Terminal.Written.FindLastIndex(W => W.Text == "First trip (Alpha.yaml)")),
                Does.Contain("  Name (displayName): First trip"), "leaving kept nothing that was not saved");
        }

        // ---- changes on disk ----

        [Test]
        public void AChangeOnDisk_ReloadsACleanForm_AndOpensAConflictForAnEditedOne_NeverOverwritingIt()
        {
            Write(Alpha, AlphaText);
            string Theirs = AlphaText.Replace("totalHumans: 2", "totalHumans: 6");
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                new Action(() => Write(Alpha, Theirs)), P("Change Name (displayName)"), Esc,
                P("Change Name (displayName)"), L("Mine"), new Action(() => Write(Alpha, Theirs.Replace("searchValue: 7", "searchValue: 9"))),
                P("Change Enabled (autoRun)"), Esc,
                P("Apply my changes to the new version"), P("Save"), Yes
            }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Info: Alpha.yaml was changed on disk and has been reloaded."));
            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Alpha.yaml was changed by another program: Warning: ", StringComparison.Ordinal)), Is.True, "the session's conflict");
            CampertronConfig Saved = Yaml.LoadSettingsFile<CampertronConfig>(Alpha);
            Assert.That((Saved.DisplayName, Saved.TotalHumans, Saved.SearchValue), Is.EqualTo(("Mine", 6, 9)), "my change applied to their version, never over it");
        }

        [Test]
        public void AFileDeletedOnDisk_LeavesTheList_WhenClean_AndCanBeCreatedAgainWithTheChanges_WhenEdited()
        {
            Write(Alpha, AlphaText);
            Write(Bravo, BravoText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { new Action(() => File.Delete(Alpha)), P("Change Name (displayName)"), Esc },
                new object[] { P(Label("Bravo.yaml")), P("Change Name (displayName)"), L("Kept"), new Action(() => File.Delete(Bravo)), P("Change Enabled (autoRun)"), Esc,
                    P("Create the file again with my changes"), Yes, P("Back to Searches"), P("Back to the main menu"), P("Exit") }));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Error: Alpha.yaml was deleted on disk."), "the form's words, then the list");
            Assert.That(File.Exists(Alpha), Is.False, "never created again by itself");
            Assert.That(Yaml.LoadSettingsFile<CampertronConfig>(Bravo).DisplayName, Is.EqualTo("Kept"), "created again only after a yes");
            Assert.That(Terminal.Prompts, Does.Contain("Create Bravo.yaml again with your changes? [y/N]: "));
        }

        [Test]
        public void CommentsQuotingAndFormatting_AreKept_ByASave_AndOnlyTheValueChanged()
        {
            string Text_ = "# keep me\ndisplayName: First trip   # a comment\nautoRun: true\ncampgroundID: \"232447\"\n\n" +
                "totalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\n" +
                "showFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\nincludeSites:\n  - '001'   # first\n  - 'not a campsite here'\n";
            Write(Alpha, Text_);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { P("Change People (totalHumans)"), L("3"), P("Save"), Yes }, BackToExit));

            Run(Terminal);

            string Other = Oracle("Alpha.yaml", F => F.TotalHumans.Text = "3", ("Alpha.yaml", Text_));
            Assert.That(Text(Alpha), Is.EqualTo(Text(In("Alpha.yaml", Other))));
            Assert.That(Text(Alpha), Is.EqualTo(Text_.Replace("totalHumans: 2", "totalHumans: 3")), "only that value changed");
        }

        // a setting the search files do not have is the validation's error in every UI: the form shows the file read-only, with
        // its reason, and offers only Reload; the file, with the setting, stays exactly as it is (never dropped by a save)
        [Test]
        public void ASettingSearchFilesDoNotHave_KeepsTheFileReadOnly_InTheFormsWords_AndNothingIsWritten()
        {
            string Text_ = AlphaText + "myNote: a setting Campertron does not have\n";
            Write(Alpha, Text_);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { P("Reload from disk") }, BackToExit));

            Run(Terminal);

            SearchesViewModel Shared = Form(Folder);
            SearchFileViewModel File_ = Shared.Files[0];
            Assert.That(File_.IsEditable, Is.False, "the shared form does not edit it either");
            Assert.That(Terminal.Texts, Does.Contain(MessageSeverity.AccessibleText(File_.StateText, MessageSeverity.Warning)));
            foreach (NoticeItem Notice in File_.FileNotices)
            {
                Assert.That(Terminal.Texts, Does.Contain(Notice.Text), "the form's own words for it");
            }
            Assert.That(Text(Alpha), Is.EqualTo(Text_), "nothing written: the setting is still there");
            Assert.That(File.Exists(Alpha + ".bak"), Is.False);
            Assert.That(Terminal.AllText, Does.Not.Contain("a setting Campertron does not have"), "never a file's value");
        }

        // ---- Save As ----

        [Test]
        public void SaveAs_WritesTheSearchWithItsChangesToANewFile_TheEditedFileUnchanged_AndOpensTheNewForm()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Name (displayName)"), L("Copied trip"), P("Save as a new search file"), L("alpha"), Yes,
                P("Save as a new search file"), L("Copy of alpha"), No,
                P("Save as a new search file"), Esc,
                P("Save as a new search file"), L("Copy of alpha"), Yes
            }, BackToExit));

            Run(Terminal);

            string Other = Path.Join(Secrets, "oracle-saveas");
            Directory.CreateDirectory(Other);
            Write(In("Alpha.yaml", Other), AlphaText);
            SearchesViewModel Shared = Form(Other);
            Shared.SelectedFile = Shared.Files[0];
            Shared.Files[0].DisplayName.Text = "Copied trip";
            Shared.SaveAsName = "Copy of alpha";
            Shared.SaveAs();
            Assert.That(Text(In("Copy of alpha.yaml")), Is.EqualTo(Text(In("Copy of alpha.yaml", Other))), "the bytes of the shared Save As");
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText), "the file edited is not changed");
            Assert.That(File.Exists(Alpha + ".bak"), Is.False);
            Assert.That(Terminal.Texts, Does.Contain("Error: Alpha.yaml already exists, and alpha.yaml differs from it only in upper and lower case; choose another name. Nothing was written."));
            Assert.That(Terminal.Texts, Does.Contain(InteractiveSearchesWording.NothingWritten));
            Assert.That(Terminal.Texts, Does.Contain("Info: " + Shared.SaveAsMessage));
            Assert.That(Terminal.Written, Does.Contain(("heading", "Copied trip (Copy of alpha.yaml)")), "the new file's form follows");
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.SaveAsHelp).And.Contain(SearchesWording.SaveAsNote));
            Assert.That(Directory.GetFiles(Folder).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Alpha.yaml", "Copy of alpha.yaml" }));
        }

        // ---- cancels and the end of the input ----

        [TestCase("name")]
        [TestCase("campground")]
        [TestCase("filter")]
        [TestCase("days")]
        [TestCase("save")]
        [TestCase("saveas")]
        [TestCase("list")]
        public void TheEndOfTheInput_AnywhereInAForm_EndsTheConsole_SavingNothing(string Where)
        {
            Write(Alpha, AlphaText);
            object[] Before = { P("Change Name (displayName)"), L("Changed") };
            object[] At = Where switch
            {
                "name" => new object[] { P("Change Name (displayName)"), End },
                "campground" => new object[] { P("Change Campground (campgroundID)"), P("Find a campground"), End },
                "filter" => new object[] { P("Change Campsite filters"), P("Change Include equipment (includeEquipment)"), P("Check or uncheck values"), End },
                "days" => new object[] { P("Change Days of the week"), End },
                "save" => new object[] { P("Save"), End },
                "saveas" => new object[] { P("Save as a new search file"), L("Other"), End },
                _ => new object[] { P("Change Dates (searchValueDates)"), P("Add date"), End }
            };
            Dictionary<string, string> Fingerprint_ = Fingerprint();
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), Before, At));

            Assert.That(Run(Terminal), Is.EqualTo(0));

            Assert.That(Fingerprint(), Is.EqualTo(Fingerprint_), "nothing written");
            Assert.That(Terminal.Texts.Last(), Is.EqualTo("The input ended, so the interactive console ended."));
        }

        [Test]
        public void EscAndCtrlC_CancelAQuestion_OrGoBackOneScreen_KeepingTheChangesUnsaved()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Name (displayName)"), Esc, P("Change Name (displayName)"), CtrlC,
                P("Change Campground (campgroundID)"), P("Find a campground"), Esc, CtrlC,
                P("Change Days of the week"), P("Monday: checked"), Esc,
                P("Change Campsite filters"), P("Change Include equipment (includeEquipment)"), CtrlC, Esc,
                P("Save"), Esc, P("Save"), CtrlC, End
            }));

            Run(Terminal);

            Assert.That(Terminal.Texts.Count(T => T == "Cancelled."), Is.EqualTo(5), "the name twice, the find, and Save's question twice");
            Assert.That(Terminal.Texts.Count(T => T == InteractiveSettingsWordingKept), Is.EqualTo(2), "Save's question twice");
            Assert.That(Terminal.Texts, Does.Contain("  Days of the week: Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday (changed)"), "Esc leaves the days with Monday unchecked, unsaved");
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText));
            Assert.That(Terminal.Written.Any(W => W.Kind == "stopped"), Is.False);
        }

        // ---- the campground ----

        [Test]
        public void TheCampgroundPicker_FindsByWordsOrAnExactId_AndUsesTheFacilityIDOnly_InTheFormsWords()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Campground (campgroundID)"),
                P("Find a campground"), L("pines yosemite"), Esc,
                P("Find a campground"), L("nothing like it"),
                P("Or enter a campground ID"), L(" 232445"),
                P("Or enter a campground ID"), L("23244"),
                P("Or enter a campground ID"), L("232449"),
                P("Done"), End
            }));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("2 of 3 campgrounds match."));
            int Matches = Terminal.Written.FindIndex(W => W.Kind == "heading" && W.Text == "Campgrounds");
            Assert.That(Terminal.Written.Skip(Matches + 1).Take(2).Select(W => W.Text), Is.EqualTo(new[] { "  1. " + NorthPines.AccessibleName, "  2. " + UpperPines.AccessibleName }));
            Assert.That(Terminal.Texts, Does.Contain("0 of 3 campgrounds match."));
            Assert.That(Terminal.Texts, Does.Contain("Error: Remove the spaces before or after the ID. No reservable, enabled campground in Campertron's campground data has exactly this ID."));
            Assert.That(Terminal.Texts, Does.Contain("Error: No reservable, enabled campground in Campertron's campground data has exactly this ID."), "no partial match");
            Assert.That(Terminal.Texts, Does.Contain("Info: Chosen: " + NorthPines.Label + "."));
            Assert.That(Terminal.Texts, Does.Contain("  Campground (campgroundID): " + NorthPines.Label + " — park Yosemite National Park (changed)"));
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.FindCampgroundHelp));
            Assert.That(Text(Alpha), Is.EqualTo(AlphaText), "nothing saved");
        }

        // ---- the filters ----

        [Test]
        public void AFilter_ChecksTheCampgroundsValues_AddsAValueAsTyped_AndRemovesOneTheCampgroundDoesNotHave()
        {
            Write(Alpha, AlphaText.Replace("consecutiveDays: 1\n", "consecutiveDays: 1\nincludeEquipment:\n- Tent\n- Kayak\n"));
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Campsite filters"), P("Change Include equipment (includeEquipment)"),
                P("Add the value to includeEquipment"), L("Tent"),
                P("Add the value to includeEquipment"), L("   "),
                P("Add the value to includeEquipment"), L(" Tent "),
                P("Remove a value that is not the campground's"), P("Item 2, Kayak. Not among this campground's equipment types; it matches none of them with Contains matching."),
                P("Check or uncheck values"), P("Tent, 14 campsites, checked"), P("RV, 3 campsites, not checked"), P("Done"),
                P("Done"), P("Done"), End
            }));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("Error: The list already holds exactly this value."));
            Assert.That(Terminal.Texts, Does.Contain("Error: Type a value first."));
            Assert.That(Terminal.Texts, Does.Contain("Info: Added as typed."));
            Assert.That(Terminal.Texts, Does.Contain("    Include equipment (includeEquipment): RV, “ Tent ” (with spaces at its start or end) (changed)"));
            Assert.That(Terminal.Texts, Does.Contain("3 equipment types at this campground, 1 checked."));
            Assert.That(Terminal.Texts, Does.Contain(SearchesWording.FiltersExplanation));
            Assert.That(Terminal.Texts, Does.Contain("  Equipment:").And.Contain("  Campsites:").And.Contain("  Campsite types:").And.Contain("  Attributes:"));
            Assert.That(Text(Alpha), Does.Contain("- Kayak"), "nothing saved");
        }

        [Test]
        public void ALongFilter_FindsItsValues_SelectsAndClearsTheShownOnes_AndTheFindLastsOnlyWhileTheyAreChosen()
        {
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Campsite filters"), P("Change Include campsites (includeSites)"), P("Check or uncheck values"),
                P("Find campsite names (includeSites)"), L("0"), P("Select all shown"),
                P("Find campsite names (includeSites)"), L("014"), P("Clear shown"), P("Done"),
                P("Done"), P("Done"), P("Save"), Yes
            }, BackToExit));

            Run(Terminal);

            string Other = Oracle("Alpha.yaml", F =>
            {
                F.IncludeSites.FindText = "0";
                F.IncludeSites.SelectShown();
                F.IncludeSites.FindText = "014";
                F.IncludeSites.ClearShown();
            }, ("Alpha.yaml", AlphaText));
            Assert.That(Text(Alpha), Is.EqualTo(Text(In("Alpha.yaml", Other))));
            Assert.That(Yaml.LoadSettingsFile<CampertronConfig>(Alpha).IncludeSites, Is.EqualTo(Enumerable.Range(1, 13).Select(N => N.ToString("000", System.Globalization.CultureInfo.InvariantCulture)).ToArray()));
            Assert.That(Terminal.Texts, Does.Contain("14 campsite names at this campground, 0 checked. 14 shown."));
            Assert.That(Terminal.Texts, Does.Contain("14 campsite names at this campground, 14 checked. 1 shown."));
            Assert.That(Terminal.Texts, Does.Contain("    Include campsites (includeSites): 001, 002, 003, 004, 005, 006, 007, 008, and 5 more (changed)"),
                "the summary names the first values and is never narrowed by a find");
        }

        [Test]
        public void TheSameValueInAnIncludeAndItsExcludeList_IsTheFormsWarning()
        {
            Write(Alpha, AlphaText.Replace("consecutiveDays: 1\n", "consecutiveDays: 1\nincludeEquipment:\n- Tent\nexcludeEquipment:\n- Tent\n"));
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { End }));

            Run(Terminal);

            SearchFileViewModel File_ = Form(Folder).Files[0];
            Assert.That(File_.ExcludeEquipment.HasConflict, Is.True);
            Assert.That(Terminal.Texts, Does.Contain("      " + File_.ExcludeEquipment.ConflictText));
        }

        [Test]
        public void ACampgroundsValuesThatCantBeRead_AreTheFormsWords_NeverTheProblemsText_AndRetryReadsThemAgain()
        {
            ChoicesFail = true;
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[]
            {
                P("Change Campsite filters"), new Action(() => ChoicesFail = false), P("Read the campground's values again"), P("Done"), End
            }));

            Run(Terminal);

            Assert.That(Terminal.Texts, Does.Contain("    Campertron couldn't read this campground's values from its campground data."));
            Assert.That(ChoiceReads, Is.EqualTo(2), "read once, then once again on Retry");
            Assert.That(Terminal.Texts, Does.Contain("  Equipment:"));
            Assert.That(Terminal.Texts.Count(T => T.Contains("3 equipment types") || T.Contains("Include equipment (includeEquipment): (empty)")), Is.GreaterThan(0));
            AssertNoSecret(Terminal);
        }

        // ---- the monitoring's search check (D73) ----

        [Test]
        public void DisablingTheLastEnabledSearch_WithTheMonitoringsOutputTo_IsTheFormsWarning_AndNeverBlocksSave()
        {
            WriteSettings("Email");
            Write(Alpha, AlphaText);
            var Terminal = new MenuTerminal(Script(Into("Alpha.yaml"), new object[] { P("Change Enabled (autoRun)"), No, P("Save"), Yes }, BackToExit));

            Run(Terminal);

            Assert.That(Terminal.Texts.Any(T => T.StartsWith("Warning: With this change saved, email mode (outputTo: Email) would refuse to start.", StringComparison.Ordinal)), Is.True);
            Assert.That(Yaml.LoadSettingsFile<CampertronConfig>(Alpha).AutoRun, Is.False, "saved: a warning never blocks Save");
            AssertNothingSentOrStarted();
            AssertNoSecret(Terminal);
        }

        // ---- a terminal is never written a file's control character ----

        [Test]
        public void AControlCharacterInAFilesNameOrValue_IsNeverWrittenToTheTerminal()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Windows file names hold no control characters");
            }
            string Odd = "Odd\u001b[31m.yaml";
            Write(In(Odd), AlphaText.Replace("displayName: First trip", "displayName: \"Bell\\aand\\e[2J\""));
            var Terminal = new MenuTerminal(P("Searches"), P(InteractiveSettingsFormVisible(Label(Odd))), P("Change Name (displayName)"), Esc, End);

            Run(Terminal);

            Assert.That(Terminal.Written.Any(W => W.Text.Any(char.IsControl)), Is.False, "no control character reaches the terminal");
            Assert.That(Terminal.AllText, Does.Contain("Bell�and�[2J"));
        }

        private static string InteractiveSettingsFormVisible(string Text) => new string(Text.Select(C => char.IsControl(C) ? '�' : C).ToArray());

        // ---- an invalid file ----

        [Test]
        public void AFileThatCantBeLoaded_IsShownReadOnly_InTheFormsWords_OfferingOnlyReloadAndBack_NeverItsText()
        {
            Write(In("Charlie.yaml"), "displayName: [not, closed\n# cnry-a-database-problem\n");
            var Terminal = new MenuTerminal(Script(new object[] { P("Searches"), P(Label("Charlie.yaml")) }, new object[] { End }));

            Run(Terminal);

            SearchFileViewModel File_ = Form(Folder).Files[0];
            Assert.That(File_.IsEditable, Is.False);
            Assert.That(Terminal.Texts, Does.Contain(MessageSeverity.AccessibleText(File_.StateText, MessageSeverity.Warning)));
            Assert.That(Terminal.LastChoices, Is.EqualTo(new[] { "Reload from disk", "Back to Searches" }));
            Assert.That(Terminal.AllText, Does.Not.Contain("[not, closed"));
            AssertNoSecret(Terminal);
        }

        // ---- what the Searches code can reach ----

        private static List<string> Reached(Func<Type, bool> Which)
        {
            var Called = new List<string>();
            foreach (Type T in typeof(InteractiveSearches).Assembly.GetTypes().Where(Which))
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
        public void TheSearchesCode_ReachesTheSharedSearchForms_AndNothingThatParsesValidatesWritesSearchesOrMonitorsByItself()
        {
            List<string> Called = Reached(T => T.Namespace == typeof(InteractiveSearches).Namespace);
            string[] Forbidden =
            {
                //no YAML, writer, session, validation, name rule or creation of its own: only the forms reach them
                "CampertronLibrary.function.Base.Yaml", "YamlDotNet.", "CampertronLibrary.function.Base.ConfigFileReplace", "CampertronLibrary.function.Base.ConfigEditSession",
                "CampertronLibrary.function.Base.ConfigValidation", "CampertronLibrary.function.Base.SearchFiles", "CampertronLibrary.function.Base.SearchFileName",
                "CampertronLibrary.function.Base.ConfigFiles", "CampertronLibrary.function.Base.CampsiteConfig", "CampertronLibrary.function.Base.SecretFile",
                "CampertronLibrary.function.Base.TestNotification", "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.Menu",
                //no search, results, monitoring, guided setup, refresh, preparation or start
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.RecDotOrg.search.", "CampertronLibrary.function.RecDotOrg.api.",
                "CampertronLibrary.function.Forms.Monitoring", "CampertronLibrary.function.Forms.Results", "CampertronLibrary.function.Forms.Refresh",
                "CampertronLibrary.function.Forms.GuidedSetup", "CampertronLibrary.function.Forms.CampgroundDetails", "CampertronLibrary.function.Forms.CampgroundsWording",
                "CampertronLibrary.function.RecDotOrg.data.Load::GetConfig", "CampertronLibrary.function.RecDotOrg.data.Load::RunConsole",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareDatabase", "CampertronLibrary.function.RecDotOrg.data.Load::DbExistsCheck",
                "CampertronLibrary.function.RecDotOrg.data.RidbRefreshService", "CampertronLibrary.function.RecDotOrg.data.RefreshRidbRecreationData",
                "CampertronLibrary.function.RecDotOrg.data.Cache", "CampertronLibrary.function.RecDotOrg.sqlite.RidbStagedRefresh", "CampertronLibrary.function.RecDotOrg.sqlite.RidbIndexes",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::GetCampgroundDetails", "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundChoicesBy",
                "CampertronLibrary.function.WebAccess.", "CampertronWeb.", "Microsoft.AspNetCore.", "Microsoft.Data.Sqlite.", "Microsoft.EntityFrameworkCore.",
                //no file, folder, process, thread, network or environment of its own
                "System.IO.File::", "System.IO.Directory::", "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.StreamReader",
                "System.Diagnostics.Process", "System.Threading.Thread", "System.Threading.Tasks.Task::Run", "System.Net.", "System.Environment::", "System.Console::"
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal))), Is.Empty);
            Assert.That(Called.Where(C => C.EndsWith("::get_Session", StringComparison.Ordinal)), Is.Empty, "never a form's editing session");
            Assert.That(Called.Where(C => C.Contains("::set_Text", StringComparison.Ordinal) && C.Contains("ListRow", StringComparison.Ordinal)), Is.Empty, "a list's rows only through the host's own questions");
            //and the scan sees the shared forms it does drive
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.Forms.SearchesViewModel::Show", "CampertronLibrary.function.Forms.SearchesViewModel::get_NewSearchCommand",
                "CampertronLibrary.function.Forms.SearchesViewModel::set_NewSearchName", "CampertronLibrary.function.Forms.SearchesViewModel::get_SaveAsCommand",
                "CampertronLibrary.function.Forms.SearchesViewModel::set_SaveAsName", "CampertronLibrary.function.Forms.SearchesViewModel::CheckForExternalChanges",
                "CampertronLibrary.function.Forms.SearchesViewModel::SetOutput", "CampertronLibrary.function.Forms.SearchFileViewModel::get_ListDetails",
                "CampertronLibrary.function.Forms.SearchFileViewModel::get_ListStatus", "CampertronLibrary.function.Forms.CampgroundSettingField::get_UseTypedIdCommand",
                "CampertronLibrary.function.Forms.CampgroundSettingField::SelectMatch", "CampertronLibrary.function.Forms.FilterSelectorField::Toggle",
                "CampertronLibrary.function.Forms.FilterSelectorField::get_AddCustomCommand", "CampertronLibrary.function.Forms.FilterSelectorField::get_SelectShownCommand",
                "CampertronLibrary.function.Forms.FilterSelectorField::get_ClearShownCommand", "CampertronLibrary.function.Forms.SettingsViewModel::get_General",
                "CampertronLibrary.function.RecDotOrg.sqlite.Read::CampgroundPickerEntries", "CampertronLibrary.function.RecDotOrg.sqlite.Read::FilterChoicesByCampground",
                "CampertronLibrary.function.RecDotOrg.data.Load::DbExists"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheSearchesCode_HasNoExampleSearchOrWordOfItsOwnForASetting_AndNoLaterPhasesChoice()
        {
            var Literals = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in typeof(InteractiveSearches).Assembly.GetTypes().Where(T => T.Namespace == typeof(InteractiveSearches).Namespace))
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Literals.AddRange(ConsoleStartContractTests.StringLiterals(Method));
                }
                Literals.AddRange(T.GetFields(Declared).Where(F => F.IsLiteral && F.FieldType == typeof(string)).Select(F => (string)F.GetRawConstantValue()!));
            }
            string All = string.Join("\n", Literals);
            foreach (string Example in new[] { "Zion", "232445", "Watchman", "WATCHMAN", "SMALL TENT", "LARGE TENT", "GROUP TENT ONLY", "Shade:Yes", "Campfire Allowed", "showMonday:", "displayName:", "consecutiveDays:", "autoRun: true" })
            {
                Assert.That(All, Does.Not.Contain(Example), "no template or YAML of its own: New search is the form's");
            }
            foreach (string Later in new[] { "Delete", "Rename", "Run the search", "Search now", "Results page", "Start the monitoring", "same campsite", "Refresh campground data", "Export", "Beam", "Guided setup" })
            {
                Assert.That(All.Replace("the Results tab is the desktop app's Results tab and the web UI's Results page", string.Empty), Does.Not.Contain(Later),
                    "no choice of a later phase, no TD-65 option");
            }
            Assert.That(Literals.Where(S => S.Any(char.IsControl)), Is.Empty, "no escape sequence or control character of its own");
        }
    }
}
