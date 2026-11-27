using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // D73 (open question #48): the GUI's check of the unattended monitoring's search files (ConfigValidation.
    // MonitoringSearchErrors) is the start's own check (Load.LoadMonitoringSearches: the search files read as the start reads
    // them, and D64's rule). For every folder state and every monitoring output (Email, Ntfy, both) it finds a problem
    // exactly when the real start stops: MonitoringNoSearch when the start says it has no search to check, MonitoringSearchRefused
    // (with the file's name) when the start stops on a search file, nothing when the start runs its first check. The start
    // runs with its seams: the send and the publish are counters, recreation.gov answers an empty month from memory, and
    // the wait after the first check ends the loop. Nothing reaches a network.
    public class MonitoringSearchCheckTests
    {
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string CachePath => Path.Join(Root, "cache");
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private StringWriter Out = null!;
        private StringWriter Err = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private CultureInfo OriginalCulture = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private Action<TimeSpan, CancellationToken> CheckWait = null!;
        private Func<string, string> OriginalDownload = null!;

        private sealed class StopTheLoop : Exception
        {
        }

        [SetUp]
        public void Setup()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronMonitoringSearchCheckTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            CampsiteConfig.SendResultsEmail = (Settings, Message) => Message.Dispose();
            Notifications.PublishNtfy = (Settings, Message) => { };
            CheckWait = (Wait, Token) => throw new StopTheLoop();
            AvailabilityApi.ConsoleDownload = Url => NoLiveNetwork.EmptyMonth;
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Err.Dispose();
            ConsoleUi.Current = OriginalCapabilities;
            CultureInfo.CurrentCulture = OriginalCulture;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // ---- the settings and the search files ----

        private const string Password = "Monitor-Check-S3cret";
        private const string Topic = "monitorCheckTopic0123456789";
        private const string ValidEmail = "smtpServer: smtp.example.invalid\nsmtpPort: 587\nsmtpUsername: check-user\nsmtpPassword: " + Password +
            "\nsendToAddressList:\n- to@example.invalid\nsendFromAddress: from@example.invalid\n";
        private const string ValidNtfy = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";

        // a search: a week from today, single nights, every day; its name and campground are values no message may show
        private static string Search(string Name, bool AutoRun, string CampgroundLine = "campgroundID: '232445'", string Extra = "") =>
            "displayName: " + Name + "\nautoRun: " + (AutoRun ? "true" : "false") + "\n" + CampgroundLine + "\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n" +
            "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: 1\n" + Extra;

        private static string EqualDates(string Name)
        {
            string Day = DateTime.Today.AddDays(40).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Search(Name, true).Replace("searchBy: DaysOut\nsearchValue: 7\n", "searchBy: StartEndDate\nsearchValueDates:\n- " + Day + "\n- " + Day + "\n");
        }

        public enum Verdict
        {
            Starts,
            NoSearch,
            Refused
        }

        // folder states: the search files, and what the start does with them
        public static IEnumerable<TestCaseData> Folders()
        {
            yield return Case("no search file", Verdict.NoSearch);
            yield return Case("one search, not enabled", Verdict.NoSearch, ("Trip.yaml", Search("Canary Trip", false)));
            yield return Case("every search not enabled", Verdict.NoSearch, ("A.yaml", Search("Canary A", false)), ("B.yaml", Search("Canary B", false)));
            yield return Case("enabled without a campgroundID", Verdict.NoSearch, ("Trip.yaml", Search("Canary Trip", true, "# no campground")));
            yield return Case("enabled with an empty campgroundID", Verdict.NoSearch, ("Trip.yaml", Search("Canary Trip", true, "campgroundID: ''")));
            yield return Case("one enabled with a campground", Verdict.Starts, ("Trip.yaml", Search("Canary Trip", true)));
            yield return Case("one of two enabled", Verdict.Starts, ("A.yaml", Search("Canary A", false)), ("B.yaml", Search("Canary B", true)));
            yield return Case("an enabled search beyond twelve months (a warning)", Verdict.Starts, ("Trip.yaml", Search("Canary Trip", true).Replace("searchBy: DaysOut\nsearchValue: 7", "searchBy: MonthsOut\nsearchValue: 13")));
            yield return Case("a not-numeric campgroundID (a validation error the start doesn't refuse)", Verdict.Starts, ("Trip.yaml", Search("Canary Trip", true, "campgroundID: 'abc'")));
            yield return Case("the only enabled search with equal dates", Verdict.Refused, ("Trip.yaml", EqualDates("Canary Trip")));
            yield return Case("an enabled search with an empty filter item", Verdict.Refused, ("Good.yaml", Search("Canary Good", true)), ("Trip.yaml", Search("Canary Trip", true, Extra: "includeEquipment:\n- \n")));
            yield return Case("a broken file that is not enabled, next to a good search", Verdict.Refused, ("Good.yaml", Search("Canary Good", true)), ("Trip.yaml", "displayName: [Canary\n"));
            yield return Case("an empty search file, next to a good search", Verdict.Refused, ("Good.yaml", Search("Canary Good", true)), ("Trip.yaml", "# nothing\n"));
            yield return Case("a negative searchValue in an enabled search", Verdict.Refused, ("Trip.yaml", Search("Canary Trip", true).Replace("searchValue: 7", "searchValue: -3")));
        }

        private static TestCaseData Case(string Name, Verdict Expected, params (string File, string Text)[] Files) =>
            new TestCaseData(Name, Expected, Files).SetArgDisplayNames(Name);

        private void Write(IEnumerable<(string File, string Text)> Files)
        {
            foreach (var (Name, Text) in Files)
            {
                File.WriteAllText(Path.Join(ConfigPath, Name), Text);
            }
            // the campgrounds' search caches, so the start needs no database
            foreach (string Id in new[] { "232445", "abc" })
            {
                File.WriteAllText(Path.Join(CachePath, Id + "-CampInfo.json"), JsonSerializer.Serialize(new ReturnParkCampground { ParkName = "Fixture Park", CampsiteName = "Fixture" }));
                File.WriteAllText(Path.Join(CachePath, Id + "-Campsites.json"), "[]");
                File.WriteAllText(Path.Join(CachePath, Id + "-Cached.json"), "\"2026-11-21\"");
            }
        }

        // the monitoring's settings: Email (email only), Ntfy (ntfy only) or EmailAndNtfy (outputTo: Email with ntfy on)
        private CampertronInternalConfig Settings(string Mode)
        {
            OutputType Output = Mode == "Ntfy" ? OutputType.Ntfy : OutputType.Email;
            File.WriteAllText(Path.Join(ConfigPath, "General.yaml"), "outputTo: " + Output + "\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-20T00:00:00Z\nautoRefresh: false\n");
            File.WriteAllText(Path.Join(ConfigPath, "Email.yaml"), ValidEmail);
            if (Mode != "Email")
            {
                File.WriteAllText(Path.Join(ConfigPath, "Ntfy.yaml"), ValidNtfy);
            }
            return new CampertronInternalConfig
            {
                ConfigPath = ConfigPath,
                CachePath = CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = Yaml.LoadSettingsText<EmailConfig>(ValidEmail),
                NtfyConfig = Yaml.NtfyConfigGetConfig(ConfigPath)
            };
        }

        // what the real start does: runs its first check (the wait after it ends the loop), stops with D64's error, or stops
        // on a search file with that file's error (the exception Program.Main reports), with the file it names
        private (Verdict Verdict, string? File) RealStart(CampertronInternalConfig Settings)
        {
            try
            {
                return Load.RunConsoleSearch(Settings, CheckWait) ? throw new AssertionException("the start returned true without a check") : (Verdict.NoSearch, null);
            }
            catch (StopTheLoop)
            {
                return (Verdict.Starts, null);
            }
            catch (Exception ex)
            {
                string? File_ = ex.Data.Contains(ConsoleUi.FileKey) ? ex.Data[ConsoleUi.FileKey] as string : null;
                return (Verdict.Refused, File_ == null ? null : Path.GetFileName(File_));
            }
        }

        [TestCaseSource(nameof(Folders))]
        public void TheCheck_FindsAProblem_ExactlyWhenTheStartStops(string Name, Verdict Expected, (string File, string Text)[] Files)
        {
            foreach (string Mode in new[] { "Email", "Ntfy", "EmailAndNtfy" })
            {
                foreach (string Existing in Directory.GetFiles(ConfigPath))
                {
                    File.Delete(Existing);
                }
                Write(Files);
                CampertronInternalConfig Settings_ = Settings(Mode);
                OutputType Output = Settings_.GeneralConfig!.OutputTo;

                List<ConfigDiagnostic> Found = ConfigValidation.MonitoringSearchErrors(ConfigPath, Output);
                (Verdict Start, string? StoppedOn) = RealStart(Settings_);

                Assert.That(Start, Is.EqualTo(Expected), Mode + ": the start, as the case expects");
                Verdict Check = Found.Count == 0 ? Verdict.Starts : Found.Single().Code == ConfigCode.MonitoringNoSearch ? Verdict.NoSearch : Verdict.Refused;
                Assert.That(Check, Is.EqualTo(Start), Mode + ": the GUI's check agrees with the start");
                Assert.That(Found.All(D => D.Severity == ConfigSeverity.Error && D.FileKind == ConfigFileKind.Search), Is.True);
                if (Start == Verdict.Refused)
                {
                    Assert.That(StoppedOn, Is.Not.Null);
                    Assert.That(Found.Single().FileName, Is.EqualTo(StoppedOn), Mode + ": the file the start stops on, by its name");
                    Assert.That(Found.Single().Message, Does.Contain("The search file " + StoppedOn + " can't be used"));
                }
                if (Start == Verdict.NoSearch)
                {
                    Assert.That(Found.Single().Message, Does.StartWith(ConfigValidation.NeedsEnabledSearch));
                }
            }
        }

        // an enabled search without a campground next to one with a campground: D64's rule has a search to check, so the
        // check finds nothing, as the start's own search check doesn't (what the start then does with the search without a
        // campground needs the campground data, TD-59; this harness has none, so the start is not run here)
        [TestCase(OutputType.Email)]
        [TestCase(OutputType.Ntfy)]
        public void AnEnabledSearchWithoutACampground_NextToOneWithIt_IsASearchToCheck(OutputType Output)
        {
            Write(new[] { ("A.yaml", Search("Canary A", true, "# none")), ("B.yaml", Search("Canary B", true)) });
            List<CampertronConfig> Loaded = Load.LoadMonitoringSearches(ConfigPath, null, null, out int Files, out Load.EmailStartProblem? Problem);
            Assert.That((Files, Loaded.Count, Problem), Is.EqualTo((2, 2, (Load.EmailStartProblem?)null)));
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output), Is.Empty);
            File.Delete(Path.Join(ConfigPath, "B.yaml"));
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output).Single().Message, Does.Contain("No Enabled (autoRun) search has a campground"));
        }

        [Test]
        public void TheNoSearchMessages_SayWhyAndWhatToDo()
        {
            const string Needs = "Campertron needs at least one enabled search before it can monitor. Enable or create a search first.";
            Assert.That(ConfigValidation.NeedsEnabledSearch, Is.EqualTo(Needs));
            Assert.That(Message(), Is.EqualTo(Needs + " There is no search file: in the Searches tab, create one with New search, choose its campground, check Enabled (autoRun) and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Search("Canary Trip", false));
            Assert.That(Message(), Is.EqualTo(Needs + " No search is Enabled (autoRun): in the Searches tab, check Enabled (autoRun) for a search with a campground and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Search("Canary Trip", true, "# none"));
            Assert.That(Message(), Is.EqualTo(Needs + " No Enabled (autoRun) search has a campground, so none would be checked: in the Searches tab, choose a campground for an enabled search and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), EqualDates("Canary Trip"));
            Assert.That(Message(), Is.EqualTo("The search file Trip.yaml can't be used, and the monitoring stops at its start with that file's error. Open it in the Searches tab, fix it and save it first."));

            string Message() => ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Email).Single().Message;
        }

        // PT1 (v0.4.0's phase 13): the interactive console names where its user edits the search files, its Searches menu and
        // New search file, instead of the desktop GUI's and the web UI's Searches tab and New search. Only the words differ:
        // for every folder state the check finds the same problems, of the same code, severity and file, in both words
        [TestCaseSource(nameof(Folders))]
        public void TheConsolesWords_NameItsSearchesMenu_AndTheCheckDecidesTheSame(string Name, Verdict Expected, (string File, string Text)[] Files)
        {
            Write(Files);
            foreach (OutputType Output in new[] { OutputType.Email, OutputType.Ntfy })
            {
                List<ConfigDiagnostic> Default = ConfigValidation.MonitoringSearchErrors(ConfigPath, Output);
                List<ConfigDiagnostic> Tab = ConfigValidation.MonitoringSearchErrors(ConfigPath, Output, null, SearchPlace.Tab);
                List<ConfigDiagnostic> Menu = ConfigValidation.MonitoringSearchErrors(ConfigPath, Output, null, SearchPlace.Menu);
                Assert.That(Tab.Select(D => (D.Code, D.Severity, D.FileName, D.Message)), Is.EqualTo(Default.Select(D => (D.Code, D.Severity, D.FileName, D.Message))),
                    "no place given is the Searches tab: the desktop GUI's and the web UI's words, unchanged");
                Assert.That(Menu.Select(D => (D.Code, D.Severity, D.FileName)), Is.EqualTo(Tab.Select(D => (D.Code, D.Severity, D.FileName))), "the same decision");
                Assert.That(Menu.Select(D => D.Message), Is.EqualTo(Tab.Select(D => D.Message.Replace("the Searches tab", "the Searches menu").Replace("with New search,", "with New search file,"))),
                    "only the place's words differ");
                Assert.That(Menu.Select(D => D.Message), Has.None.Contain("Searches tab"));
                Assert.That(Menu.Count == 0, Is.EqualTo(Expected == Verdict.Starts));
            }
        }

        [Test]
        public void TheConsolesNoSearchMessages_SayTheSearchesMenuAndNewSearchFile()
        {
            const string Needs = "Campertron needs at least one enabled search before it can monitor. Enable or create a search first.";
            Assert.That(Message(), Is.EqualTo(Needs + " There is no search file: in the Searches menu, create one with New search file, choose its campground, check Enabled (autoRun) and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Search("Canary Trip", false));
            Assert.That(Message(), Is.EqualTo(Needs + " No search is Enabled (autoRun): in the Searches menu, check Enabled (autoRun) for a search with a campground and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Search("Canary Trip", true, "# none"));
            Assert.That(Message(), Is.EqualTo(Needs + " No Enabled (autoRun) search has a campground, so none would be checked: in the Searches menu, choose a campground for an enabled search and save it."));
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), EqualDates("Canary Trip"));
            Assert.That(Message(), Is.EqualTo("The search file Trip.yaml can't be used, and the monitoring stops at its start with that file's error. Open it in the Searches menu, fix it and save it first."));

            string Message() => ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Ntfy, null, SearchPlace.Menu).Single().Message;
        }

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.UnitTest)]
        [TestCase(null)]
        public void OtherOutputs_AreNotChecked(OutputType? Output)
        {
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output), Is.Empty, "no search file, but no monitoring either");
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), "displayName: [broken\n");
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output), Is.Empty);
        }

        // the GUI's check writes nothing to the console (the start warns about a search beyond twelve months; the check
        // doesn't) and creates, changes or removes nothing in the folder (no example search)
        [TestCase(OutputType.Email)]
        [TestCase(OutputType.Ntfy)]
        public void TheCheck_WritesNothing_AndChangesNoFile(OutputType Output)
        {
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output).Single().Code, Is.EqualTo(ConfigCode.MonitoringNoSearch));
            Assert.That(Directory.GetFileSystemEntries(ConfigPath), Is.Empty, "no example search created");
            File.WriteAllText(Path.Join(ConfigPath, "Trip.yaml"), Search("Canary Trip", true).Replace("searchBy: DaysOut\nsearchValue: 7", "searchBy: MonthsOut\nsearchValue: 13"));
            File.WriteAllText(Path.Join(ConfigPath, "Broken.yaml"), "displayName: [broken\n");
            File.SetLastWriteTimeUtc(Path.Join(ConfigPath, "Trip.yaml"), new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            var Before = Directory.GetFiles(ConfigPath).ToDictionary(p => p, p => (File.ReadAllBytes(p), File.GetLastWriteTimeUtc(p)));
            ConfigValidation.MonitoringSearchErrors(ConfigPath, Output);
            File.Delete(Path.Join(ConfigPath, "Broken.yaml"));
            Before.Remove(Path.Join(ConfigPath, "Broken.yaml"));
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, Output), Is.Empty);
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "no warning, no line");
            foreach (var (Path_, (Bytes, Written)) in Before)
            {
                Assert.That(File.ReadAllBytes(Path_), Is.EqualTo(Bytes));
                Assert.That(File.GetLastWriteTimeUtc(Path_), Is.EqualTo(Written));
            }
            Assert.That(Directory.GetFiles(ConfigPath).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Trip.yaml" }));
            Write(Array.Empty<(string, string)>());
            Assert.That(RealStart(Settings("Email")).Verdict, Is.EqualTo(Verdict.Starts));
            Assert.That(Err.ToString(), Does.Contain("results are limited to 12 months out"), "the start itself still warns, as before");
        }

        // a search file's unsaved text counts instead of the file (the Searches tab asks what a save would leave); the
        // other files and the disk are as they are
        [Test]
        public void AnUnsavedText_CountsInsteadOfTheFile()
        {
            string Trip = Path.Join(ConfigPath, "Trip.yaml");
            File.WriteAllText(Trip, Search("Canary Trip", true));
            File.WriteAllText(Path.Join(ConfigPath, "Other.yaml"), Search("Canary Other", false));
            byte[] OnDisk = File.ReadAllBytes(Trip);
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Email), Is.Empty);

            string? Disabled(string P) => Path.GetFileName(P) == "Trip.yaml" ? Search("Canary Trip", false) : null;
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Email, Disabled).Single().Message, Does.Contain("No search is Enabled (autoRun)"));
            string? Refused(string P) => Path.GetFileName(P) == "Trip.yaml" ? EqualDates("Canary Trip") : null;
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Ntfy, Refused).Single().FileName, Is.EqualTo("Trip.yaml"));
            string? OtherOn(string P) => Path.GetFileName(P) == "Other.yaml" ? Search("Canary Other", true) : Disabled(P);
            Assert.That(ConfigValidation.MonitoringSearchErrors(ConfigPath, OutputType.Email, OtherOn), Is.Empty, "the other search, enabled, is one to check");
            Assert.That(File.ReadAllBytes(Trip), Is.EqualTo(OnDisk));
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty);
        }

        // the messages are fixed text with a file name at most: never the folder, a search's name, its campground or another
        // value of a settings file
        [TestCaseSource(nameof(Folders))]
        public void TheMessages_NameNoValueOrPath(string Name, Verdict Expected, (string File, string Text)[] Files)
        {
            Write(Files);
            Settings("EmailAndNtfy");
            foreach (OutputType Output in new[] { OutputType.Email, OutputType.Ntfy })
            {
                foreach (ConfigDiagnostic D in ConfigValidation.MonitoringSearchErrors(ConfigPath, Output))
                {
                    foreach (string Never in new[] { Root, ConfigPath, "Canary", "232445", "abc", Password, Topic, "example.invalid", "-3" })
                    {
                        Assert.That(D.Message, Does.Not.Contain(Never));
                        Assert.That(D.FileName, Does.Not.Contain(Never));
                    }
                    Assert.That(D.FileName, Does.Not.Contain("/").And.Not.Contain("\\"));
                }
            }
        }
    }
}
