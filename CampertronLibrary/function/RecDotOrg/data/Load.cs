using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using static ConsoleConfig;
using System.Text.Json;
using System.IO.Compression;
using System.Collections.Generic;
using System.ComponentModel;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Load
    {
        public static RunningData Preload(CampertronInternalConfig InternalConfig)
        {
            OutputType? OutputTo = InternalConfig?.GeneralConfig?.OutputTo;
            if (OutputTo == OutputType.Console || OutputTo == OutputType.HtmlFile)
            {
                //a search from the menu starts on a clear screen (a terminal only; the unattended loop is never cleared)
                ConsoleUi.ClearScreen();
            }
            List<CampertronConfig> CampertronConfigFiles = Yaml.CampertronConfigGetConfigs(InternalConfig.ConfigPath);
            return Prepare(InternalConfig, CampertronConfigFiles);
        }
        //the run of searches the search files loaded: each campground's cache, the searches that run listed (not for
        //UnitTest) and the run's empty collections. Preload loads them for every Search; email mode loads them itself, once,
        //and checks that they hold a search to check first (D64)
        internal static RunningData Prepare(CampertronInternalConfig InternalConfig, List<CampertronConfig> CampertronConfigFiles)
        {
            ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>();//Stores console data to write
            ConcurrentDictionary<string, AvailabilityEntries> SiteData = new ConcurrentDictionary<string, AvailabilityEntries>();//Contains deserialized site data
            ConcurrentDictionary<string, bool> Urls = new ConcurrentDictionary<string, bool>();//Ensures that multiple campground configs for the same site/date is only downloaded once
            OutputType? OutputTo = InternalConfig.GeneralConfig?.OutputTo;
            //caching sqlite site data
            List<string> UniqueCampgroundIds = new List<string>();
            foreach (CampertronConfig ThisConfig in CampertronConfigFiles)
            {
                if (UniqueCampgroundIds.Contains(ThisConfig.CampgroundID) == false)
                {
                    UniqueCampgroundIds.Add(ThisConfig.CampgroundID);
                }
            }

            Parallel.ForEach(UniqueCampgroundIds, ThisCampgroundId =>
            {
                Cache.PreCheckCache(ThisCampgroundId, InternalConfig.CachePath, InternalConfig.ConfigPath!);
            });

            if (OutputTo != OutputType.UnitTest)
            {
                //what is about to be searched, and what cannot find anything as it is written
                string? CachePath = InternalConfig.CachePath;
                ConsoleResults.Scope Scope = ConsoleResults.DescribeSearches(CampertronConfigFiles, Id => Cache.GetCampInfo(Id, CachePath),
                    System.Globalization.CultureInfo.CurrentCulture, ConsoleUi.Current.Width);
                ConsoleUi.Write(Scope.Lines);
                ConsoleUi.Line();
                foreach (string ScopeWarning in Scope.Warnings)
                {
                    ConsoleUi.Warning(ScopeWarning);
                }
            }

            //data we care about
            ConcurrentBag<AvailableData> FilteredAvailableData = new ConcurrentBag<AvailableData>();

            return new RunningData
            {
                AllConsoleConfigItems = AllConsoleConfigItems,
                SiteData = SiteData,
                Urls = Urls,
                UniqueCampgroundIds = UniqueCampgroundIds,
                CampertronConfigFiles = CampertronConfigFiles,
                FilteredAvailableData = FilteredAvailableData
            };
        }
        //the console's run once its settings are loaded (Program.Main returns the result as its exit code): the menu and the
        //Email loop run until the process ends (Exit, a failure Program.Main reports, a signal); a run that ends by itself
        //ends with 0 (an outputTo: UnitTest start) or with 1, the exit code of a handled failure, when email mode had no
        //search to check and has said why on standard error (D64)
        public static int RunConsole(CampertronInternalConfig InternalConfig)
        {
            return RunConsoleSearch(InternalConfig) ? 0 : 1;
        }
        //true when the run ended by itself (outputTo: UnitTest); false when email mode did not start its checks because it
        //had no search to check (D64). The menu and the Email loop otherwise never return
        public static bool RunConsoleSearch(CampertronInternalConfig InternalConfig)
        {
            if(InternalConfig?.GeneralConfig?.OutputTo == OutputType.UnitTest)
            {
                RunConsoleSearch(InternalConfig, false);
                return true;
            }
            else
            {
                return RunConsoleSearch(InternalConfig, false);
            }            
        }
        public static bool RunConsoleSearch(CampertronInternalConfig InternalConfig, bool ForceSearch)
        {            
            if (InternalConfig?.GeneralConfig?.OutputTo == OutputType.Email || ForceSearch == true)
            {
                //the unattended loop writes a log: one timestamped line per check instead of per-search progress
                bool Unattended = InternalConfig?.GeneralConfig?.OutputTo == OutputType.Email;
                RunningData RunData;
                if (Unattended)
                {
                    //Email.yaml must hold what sending needs before anything is searched (TD-51, D41): the sample's empty
                    //server and addresses used to fail only at the first send, after a whole search
                    CheckEmailOutputSettings(InternalConfig!);
                    ConsoleUi.UseUtf8Output();
                    ConsoleUi.InstallTerminalHandlers();
                    ConsoleUi.LoopLine(ConsoleUi.ProgramIdentity() + " | settings " + InternalConfig?.ConfigPath + " | cache " + InternalConfig?.CachePath + " | output Email");
                    //the search files are read once, here, for every check of this process (a change needs a restart). The
                    //checks start only with a search to check (D64): with none, email mode would check nothing every minute
                    //for as long as it ran, which looks healthy to a supervisor, so it says why once and returns instead
                    CampertronInternalConfig Settings = InternalConfig!;
                    //without a search file nothing is created: no example search is checked in the operator's name (D65)
                    List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(Settings.ConfigPath!, false, false, out int SearchFiles);
                    EmailStartProblem? Problem = CheckSearchesToCheck(SearchFiles, Searches);
                    if (Problem != null)
                    {
                        List<string> Lines = EmailStartProblemText(Problem.Value, SearchFiles, Searches.Count);
                        ConsoleUi.Error(Lines[0], Lines.Skip(1).ToArray());
                        return false;
                    }
                    RunData = Prepare(Settings, Searches);
                }
                else
                {
                    RunData = Preload(InternalConfig);
                }
                bool FirstCheck = true;
                while (true)
                {
                    if (Unattended)
                    {
                        //never a check of nothing: the start refused that, so an empty plan here is a defect, ended visibly
                        RequireSearchesToCheck(RunData.CampertronConfigFiles);
                    }
                    if (!FirstCheck)
                    {
                        RegenerateSearchDates(RunData.CampertronConfigFiles, DateTime.Now);
                    }
                    FirstCheck = false;
                    Parallel.ForEach(RunData.CampertronConfigFiles, ThisCampertronConfig =>
                    {
                        ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                        NewConfigItem.Name = ThisCampertronConfig.DisplayName;
                        DateTime Start = DateTime.UtcNow;
                        NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisCampertronConfig, ref RunData, InternalConfig);
                        RunData.AllConsoleConfigItems.Add(NewConfigItem);
                        DateTime End = DateTime.UtcNow;
                        double TotalSeconds = (End - Start).TotalSeconds;
                        if (!Unattended)
                        {
                            ConsoleUi.Status(CheckedText(ThisCampertronConfig, Cache.GetCampInfo(ThisCampertronConfig.CampgroundID, InternalConfig?.CachePath), TotalSeconds));
                        }
                    });
                    PostProcess(InternalConfig, ref RunData);
                    if (InternalConfig?.GeneralConfig?.OutputTo == OutputType.UnitTest)
                    {
                        return true;
                    }
                }
            }
            else
            {
                Menu.Init(InternalConfig);
            }
            return false;
        }
        //the wait between two checks of the Email loop; replaceable so tests can end the loop after a check
        internal static Action<TimeSpan> CheckWait { get; set; } = Thread.Sleep;
        //the Email output's start check: Email.yaml must have a server, a port from 1 to 65535, a valid sender and at least
        //one valid recipient (ConfigValidation, D41). Throws, naming the settings but never their values, before the Email
        //loop searches anything; no connection is made and nothing is sent. The exception carries Email.yaml for the
        //failure message (ConsoleUi.NoteFile)
        internal static void CheckEmailOutputSettings(CampertronInternalConfig InternalConfig)
        {
            string EmailFile = Path.Join(InternalConfig.ConfigPath, ConfigFiles.EmailFileName);
            try
            {
                ConfigValidation.ThrowIfEmailOutputRefused(InternalConfig.EmailConfig);
            }
            catch (Exception ex) when (ConsoleUi.NoteFile(ex, EmailFile))
            {
                throw;
            }
        }
        //why email mode does not start its checks (D64): what its start loaded holds no search to check
        internal enum EmailStartProblem
        {
            //the settings folder has no search file: email mode creates no example search (D65; the console's Search does)
            NoSearchFile,
            //search files, none of them Enabled (autoRun: true)
            NoneEnabled,
            //Enabled (autoRun) searches, none of them with a campgroundID
            NoCampground
        }
        //the loaded searches email mode checks: those with a campgroundID. The loaded searches are the enabled ones the
        //loader did not refuse (an enabled search with an error of the validation stops the start with its own message,
        //D41); a loaded search without a campgroundID is skipped at every check (GetAvailabilitiesByCampground; the
        //validation's CampgroundIdMissing, "the search is skipped"; CampsiteSearch.LoadEntry's Skipped), so it is nothing
        //to check
        internal static List<CampertronConfig> SearchesToCheck(IEnumerable<CampertronConfig> Loaded)
        {
            return Loaded.Where(Search => !string.IsNullOrWhiteSpace(Search.CampgroundID)).ToList();
        }
        //null when the searches a start loaded hold one to check, else why not; SearchFiles is how many search files the
        //start read (Yaml.CampertronConfigGetConfigs)
        internal static EmailStartProblem? CheckSearchesToCheck(int SearchFiles, IReadOnlyCollection<CampertronConfig> Loaded)
        {
            if (SearchesToCheck(Loaded).Count > 0)
            {
                return null;
            }
            if (Loaded.Count > 0)
            {
                return EmailStartProblem.NoCampground;
            }
            return SearchFiles > 0 ? EmailStartProblem.NoneEnabled : EmailStartProblem.NoSearchFile;
        }
        //email mode's error when its start has no search to check: the problem (written after "Error: "), what to do, and
        //that the files are read when it starts. Counts only: no file name, search name, path or value of a settings file
        internal static List<string> EmailStartProblemText(EmailStartProblem Problem, int SearchFiles, int Enabled)
        {
            System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.CurrentCulture;
            const string Again = "start Campertron again, or restart its container.";
            string Reason;
            string Fix;
            switch (Problem)
            {
                case EmailStartProblem.NoneEnabled:
                    Reason = SearchFiles == 1
                        ? "the search file is not Enabled (autoRun)."
                        : "none of the " + SearchFiles.ToString("N0", Culture) + " search files is Enabled (autoRun).";
                    Fix = "Enable at least one valid search (set autoRun: true in its file, or check Enabled (autoRun) in CampertronGUI's Searches tab and save it), then " + Again;
                    break;
                case EmailStartProblem.NoCampground:
                    Reason = Enabled == 1
                        ? "the Enabled (autoRun) search has no campgroundID, so it would never be checked."
                        : "none of the " + Enabled.ToString("N0", Culture) + " Enabled (autoRun) searches has a campgroundID, so none would ever be checked.";
                    Fix = "Set the campgroundID of at least one enabled search (CampertronGUI's Searches tab can choose the campground), then " + Again;
                    break;
                default:
                    Reason = "the settings folder has no search file.";
                    Fix = "Add a search file and enable it (set autoRun: true in it, or use New search and Enabled (autoRun) in CampertronGUI's Searches tab), then " + Again;
                    break;
            }
            return new List<string> { "email mode has no search to check, so it stopped: " + Reason, Fix, "Email mode reads the search files only when it starts." };
        }
        //the Email loop's invariant: every check has a search to check. Its start refuses a plan without one (D64), so an
        //empty plan here is a defect: the exception ends the process visibly (Program.Main reports it, exit code 1) instead
        //of a check of nothing every minute
        internal static void RequireSearchesToCheck(IEnumerable<CampertronConfig> Plan)
        {
            if (SearchesToCheck(Plan).Count == 0)
            {
                throw new InvalidOperationException("email mode reached a check with no search to check, which its start refuses (D64).");
            }
        }
        //the Email loop runs for days, but its start reads the search files and generates their dates only once: before
        //every later check the dates follow Now again, so a DaysOut or MonthsOut window rolls forward instead of running
        //out (TD-46). The searches loaded at the start are reused (no file is read again) and no warning is repeated
        internal static void RegenerateSearchDates(IEnumerable<CampertronConfig> Searches, DateTime Now)
        {
            foreach (CampertronConfig Search in Searches)
            {
                Search.RegenerateSearchData(Now);
            }
        }
        //one line per finished search: its name, campground, the months it asked for and how long it took
        public static string CheckedText(CampertronConfig Search, ReturnParkCampground? CampInfo, double Seconds)
        {
            System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.CurrentCulture;
            string Name = "\"" + Search.DisplayName + "\"";
            if (string.IsNullOrWhiteSpace(Search.CampgroundID))
            {
                return "Skipped " + Name + ": no campgroundID.";
            }
            string Campground = string.IsNullOrWhiteSpace(CampInfo?.CampsiteName) ? "campground ID " + Search.CampgroundID : CampInfo.CampsiteName.Trim();
            string Months = ConsoleResults.MonthsText(Search, DateTime.Now, Culture);
            return "Checked " + Name + " at " + Campground + ": " + (Months.Length > 0 ? Months : "no month to check") +
                   " (" + Seconds.ToString("0.0", Culture) + "s)";
        }
        private static int CheckNumber;
        //the line of one check of the unattended loop: "Check 3: 2 searches, nothing new. Next check 15:04:05." or, when
        //emails follow (one per search), "Check 3: 2 searches, 8 new openings; sending 2 emails."
        public static string CheckText(int Number, int Searches, int NewOpenings, DateTime Now)
        {
            string Check = "Check " + Number + ": " + ConsoleUi.Count(Searches, "search", "searches") + ", ";
            return NewOpenings > 0
                ? Check + ConsoleUi.Count(NewOpenings, "new opening", "new openings") + "; sending " + ConsoleUi.Count(Searches, "email", "emails") + "."
                : Check + "nothing new. " + NextCheckText(Now);
        }
        //checks are one minute apart (PostProcess)
        public static string NextCheckText(DateTime Now)
        {
            return "Next check " + Now.AddMinutes(1).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + ".";
        }
        //Camp.History is the notification history of the Email loop, the only periodic mode: only Email reads it
        //(AvailabilityApi.FilterData) and writes it (RecordNotificationHistory). Console, HtmlFile and UnitTest are one-pass
        //searches that show every current match and leave Camp.History untouched; a missing config is not Email
        internal static bool UsesNotificationHistory(CampertronInternalConfig? InternalConfig)
        {
            return InternalConfig?.GeneralConfig?.OutputTo == OutputType.Email;
        }
        //true if a hit has no Camp.History marker; for Email it also creates the missing markers. The Email loop itself
        //does not use it: it reads the history at its gate and records an opening only after its email was sent (TD-14)
        public static bool HasNewEntries(List<AvailableData> FilteredAvailableData, CampertronInternalConfig InternalConfig)
        {
            bool HasNewEntries = HasUnreportedEntries(FilteredAvailableData, InternalConfig);
            RecordNotificationHistory(HitsOf(FilteredAvailableData), InternalConfig);
            return HasNewEntries;
        }
        //true if a hit has no Camp.History marker. Only reads: it creates neither a marker nor the Camp.History folder,
        //which appears with the first marker (TD-43)
        internal static bool HasUnreportedEntries(IEnumerable<AvailableData> Hits, CampertronInternalConfig InternalConfig)
        {
            bool Unreported = false;
            foreach (AvailableData Hit in Hits)
            {
                if (!Directory.Exists(MarkerPath(InternalConfig, Hit.HitDate, Hit.CampsiteID)))
                {
                    Unreported = true;
                }
            }
            return Unreported;
        }
        //records reported openings in Camp.History, for Email only: one directory per date and campsite, existing ones
        //left as they are. Called only once the email that shows them has been sent (TD-14), so an email that fails
        //records nothing and its openings are reported again by a later check
        internal static void RecordNotificationHistory(IEnumerable<(DateTime Date, string? CampsiteID)> Openings, CampertronInternalConfig InternalConfig)
        {
            if (!UsesNotificationHistory(InternalConfig))
            {
                return;
            }
            foreach (var Opening in Openings)
            {
                string NotificationPath = MarkerPath(InternalConfig, Opening.Date, Opening.CampsiteID);
                if (!Directory.Exists(NotificationPath))
                {
                    //creates Camp.History too, with its first marker
                    Directory.CreateDirectory(NotificationPath);
                }
            }
        }
        //the marker of a campsite and date, in the layout used since 2024: Camp.History/{yyyy}/{M}/{d}/{CampsiteID}, with
        //'/' and no zero padding (AvailabilityApi.FilterData reads the same layout)
        private static string MarkerPath(CampertronInternalConfig InternalConfig, DateTime Date, string? CampsiteID)
        {
            return Path.Join(InternalConfig.ConfigPath, "Camp.History") + '/' +
                   Date.Year.ToString() + '/' +
                   Date.Month.ToString() + '/' +
                   Date.Day.ToString() + '/' +
                   CampsiteID.ToString();
        }
        private static IEnumerable<(DateTime Date, string? CampsiteID)> HitsOf(IEnumerable<AvailableData> Hits)
        {
            return Hits.Select(p => (p.HitDate, p.CampsiteID));
        }
        //the openings one search's email shows: its date entries (single nights and consecutive nights)
        private static IEnumerable<(DateTime Date, string? CampsiteID)> OpeningsOf(List<ConsoleConfigValue>? Values)
        {
            return (Values ?? new List<ConsoleConfigValue>())
                .Where(p => (p.Kind == OutputKind.Availability || p.Kind == OutputKind.ConsecutiveDate) && p.Metadata?.Date != null)
                .Select(p => (p.Metadata!.Date!.Value, p.Metadata.CampsiteID));
        }
        //LocalApplicationData on the desktop (Linux: $XDG_DATA_HOME or ~/.local/share; Windows: %LOCALAPPDATA%; macOS:
        //~/Library/Application Support), created if it does not exist yet. Looked up without SpecialFolderOption.Create, a
        //missing folder was "", and the settings, the database and the cache went into the working directory (TD-47).
        //Without a full path Campertron stops; it never uses the working directory instead. The lookup is replaceable
        //for tests
        internal static Func<string> LocalApplicationDataLookup { get; set; } = () =>
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        internal static string DesktopDataFolder()
        {
            string Folder = LocalApplicationDataLookup();
            if (string.IsNullOrEmpty(Folder) || !Path.IsPathFullyQualified(Folder))
            {
                throw new InvalidOperationException("The local application data folder (LocalApplicationData) is not available, so Campertron has no folder for its settings and cache, and it does not use the working directory instead. Make sure the home folder is set and writable (on Linux: HOME, or XDG_DATA_HOME).");
            }
            return Folder;
        }
        public static CampertronInternalConfig GetConfig()
        {
            CampertronInternalConfig ReturnConfig = new CampertronInternalConfig();

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null)
            {
                ReturnConfig.ConfigPath = "/config";
                ReturnConfig.CachePath = "/cache";

                if (Directory.Exists(ReturnConfig.ConfigPath) == false)
                {
                    throw new Exception("Config path does not exist");
                }
                if (Directory.Exists(ReturnConfig.CachePath) == false)
                {
                    throw new Exception("Cache path does not exist");
                }

                ReturnConfig.GeneralConfig = Yaml.GeneralConfigGetConfig(ReturnConfig.ConfigPath);
                ReturnConfig.EmailConfig = Yaml.EmailConfigGetConfig(ReturnConfig.ConfigPath);
                ReturnConfig.GeneralConfig.AutoRefresh = false;
                ReturnConfig.ContainerMode = true;
            }
            else
            {
                var path = DesktopDataFolder();
                var cachepath = Path.Join(path, "CampertronCache");

                if (Directory.Exists(cachepath) == false)
                {
                    Directory.CreateDirectory(cachepath);
                }

                var configpath = Path.Join(path, "CampertronConfig");
                if (Directory.Exists(configpath) == false)
                {
                    Directory.CreateDirectory(configpath);
                }

                ReturnConfig.ConfigPath = configpath;
                ReturnConfig.CachePath = cachepath;
                ReturnConfig.ContainerMode = false;

                if (Directory.Exists(ReturnConfig.ConfigPath) == false)
                {
                    throw new Exception("Config path does not exist");
                }
            }

            ReturnConfig.GeneralConfig = Yaml.GeneralConfigGetConfig(ReturnConfig.ConfigPath);
            ReturnConfig.EmailConfig = Yaml.EmailConfigGetConfig(ReturnConfig.ConfigPath);
            DbExistsCheck(ReturnConfig);

            return ReturnConfig;
        }
        public static bool PostProcess(CampertronInternalConfig config, ref RunningData RunData)
        {
            ReportResults(config, ref RunData);

            //clear data for next run
            RunData.Urls.Clear();
            RunData.AllConsoleConfigItems.Clear();
            RunData.SiteData.Clear();
            RunData.FilteredAvailableData.Clear();

            String ConfigPath = config.ConfigPath;
            if (config.GeneralConfig.OutputTo == OutputType.UnitTest)
            {
                return true;
            }
            else
            {
                if (config.GeneralConfig.OutputTo != OutputType.Email)
                {
                    if (config.ContainerMode == true)
                    {
                        ConsoleUi.Line("Sleeping for 1 minute");
                        Thread.Sleep(60000);
                    }
                    else
                    {
                        if (config.GeneralConfig.OutputTo != OutputType.Console && config.GeneralConfig.OutputTo != OutputType.HtmlFile)
                        {
                            RunConsoleSearch(config, false);
                        }
                        else
                        {
                            ConsoleUi.HideCursor();
                            ConsoleUi.Line();
                            ConsoleUi.WaitForKey("Press any key to return to the menu...");
                            Menu.Init(config);
                        }
                    }
                }
                else
                {
                    //the wait between two checks; the check's line has already said when the next one is
                    CheckWait(TimeSpan.FromMinutes(1));
                }
                return false;
            }
        }
        //the output of one run or check (PostProcess then clears the run and waits): the Email gate, the results and, for
        //Email, the notification history. Camp.History is only read before the emails: each search's openings are
        //recorded after its email was sent (OutputResults), and the run's other hits after the last email, so a check
        //whose emails all went out records what it always recorded, and an email that fails records nothing (TD-14)
        internal static void ReportResults(CampertronInternalConfig config, ref RunningData RunData)
        {
            //place tracking for writing lines
            ConfigType LastConfigType = ConfigType.WriteLine;

            //determine if there are new entries
            bool NewEntries = HasUnreportedEntries(new List<AvailableData>(RunData.FilteredAvailableData), config);

            //the unattended loop (outputTo: Email) reports each check on one timestamped line (and one per email, written
            //when it is sent)
            bool Unattended = UsesNotificationHistory(config);
            bool SendsEmail = Unattended && NewEntries && RunData.FilteredAvailableData.Count() > 0;
            if (Unattended)
            {
                CheckNumber++;
                ConsoleUi.LoopLine(CheckText(CheckNumber, RunData.AllConsoleConfigItems.Count, SendsEmail ? RunData.FilteredAvailableData.Count() : 0, ConsoleUi.Now()));
            }

            //if output to email and there are new entries or not output to email process config
            if (SendsEmail || !Unattended)
            {
                OutputResults(config, RunData.AllConsoleConfigItems, ref LastConfigType);
            }
            if (SendsEmail)
            {
                //every email of the check was sent: the run's other hits (those no email shows, such as a search with
                //consecutiveDays below 1) are recorded now, so the history of the check is complete
                RecordNotificationHistory(HitsOf(RunData.FilteredAvailableData), config);
                ConsoleUi.LoopLine(NextCheckText(ConsoleUi.Now()));
            }
        }
        //writes the searches' output ordered by search name: HtmlFile as one page for the whole run, Console and UnitTest
        //as one console rendering of the whole run, Email once per search through ProcessConsoleConfig (one email per search)
        public static void OutputResults(CampertronInternalConfig config, IEnumerable<ConsoleConfigItem> Items, ref ConfigType LastConfigType)
        {
            //the output type decides everything below; PostProcess has already read config.GeneralConfig.OutputTo
            GeneralConfig GenConfig = config.GeneralConfig ?? throw new ArgumentException("GeneralConfig is not loaded", nameof(config));
            List<ConsoleConfigItem> Ordered = Items.OrderBy(p => p.Name).ToList();
            if (GenConfig.OutputTo == OutputType.HtmlFile)
            {
                string Page = CampsiteConfig.WriteAndOpenHtmlResults(Ordered, config.CachePath);
                ConsoleUi.Line();
                ConsoleUi.Status("Results page (opened in your browser): " + Page);
                ConsoleUi.Status(ConsoleResults.SummaryText(Ordered, System.Globalization.CultureInfo.CurrentCulture));
                return;
            }
            if (GenConfig.OutputTo == OutputType.Console || GenConfig.OutputTo == OutputType.UnitTest)
            {
                //the whole run at once: every search's results and one summary line
                ConsoleUi.Line();
                CampsiteConfig.WriteConsoleResults(Ordered, true);
                return;
            }
            foreach (ConsoleConfigItem ThisConsoleConfig in Ordered)
            {
                //sends this search's email, or throws
                CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType, GenConfig, config.EmailConfig, config.CachePath);
                //only a sent email records the openings it shows (TD-14); a later failure does not undo it
                RecordNotificationHistory(OpeningsOf(ThisConsoleConfig.Values), config);
            }
        }
        //called before the bundled database is extracted on a first start. Unset by default, so GetConfig itself writes
        //nothing: the GUI shows its own progress and its smoke test's standard output must hold only its report lines
        //(SmokeTest.cs). CampertronConsole sets it to say what the short wait is.
        public static Action? FirstStartNotice { get; set; }
        public static bool DbExistsCheck(CampertronInternalConfig InternalConfig)
        {
            //if db doesn't exist extract blank db and populate
            if (!DbExists(InternalConfig))
            {
                if (InternalConfig.GeneralConfig?.OutputTo != OutputType.UnitTest)
                {
                    FirstStartNotice?.Invoke();
                }
                ExtractEmbeddedDbTo(Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.db"));
            }
            else
            {
                //one-time repair: replace a db that no current version would produce (2024 swapped state/postal-code
                //columns, or a pre-0.1.0-beta.4 db that still has the omitted RIDB tables) with the bundled db
                sqlite.LegacyAddressLayout.RepairIfNeeded(InternalConfig.ConfigPath, InternalConfig.CachePath);
                //if db does exist see if it needs to be autorefreshed. LastRidbDataRefresh is written by RefreshData,
                //only after a successful installation, so a failed refresh is retried on the next start (TD-36)
                if (InternalConfig.GeneralConfig.AutoRefresh == true &&
                    DateTime.UtcNow.AddDays(-InternalConfig.GeneralConfig.RefreshRidbDataDayInterval) > InternalConfig.GeneralConfig.LastRidbDataRefresh)
                {
                    RefreshRidbRecreationData.RefreshData(false, InternalConfig);
                }
            }
            //the campsite attributes' index by campsite (D51): built once, after the repair and any refresh, so the bundled
            //database just extracted and one installed by an earlier version get it; a database that has it is only read.
            //Best effort: without it the searches only build their campsite cache more slowly, and the next start tries again
            sqlite.RidbIndexes.EnsureQuietly(Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.db"));
            return true;
        }
        //decompresses the embedded Brotli-compressed RecreationDotOrg.db.br to DestinationDbFile (must not exist).
        //The data is written to a temporary file next to the destination and moved into place only when complete,
        //so an interrupted extraction never leaves a partial RecreationDotOrg.db behind.
        public static void ExtractEmbeddedDbTo(string DestinationDbFile)
        {
            string PartialFile = DestinationDbFile + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                var resource = embeddedProvider.GetFileInfo(@"content/RecreationDotOrg.db.br");
                if (!resource.Exists)
                {
                    throw new InvalidDataException("embedded resource content/RecreationDotOrg.db.br not found");
                }
                using (var reader = resource.CreateReadStream())
                using (var brotli = new BrotliStream(reader, CompressionMode.Decompress))
                using (var output = new FileStream(PartialFile, FileMode.CreateNew, FileAccess.Write))
                {
                    brotli.CopyTo(output);
                }
                File.Move(PartialFile, DestinationDbFile, false);
            }
            finally
            {
                if (File.Exists(PartialFile))
                {
                    File.Delete(PartialFile);
                }
            }
        }
        public static bool DbExists(CampertronInternalConfig InternalConfig)
        {
            bool DbExists = false;
            string DbFile = Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.db");
            if (File.Exists(DbFile))
            {
                DbExists = true;
            }
            return DbExists;
        }
    }
}