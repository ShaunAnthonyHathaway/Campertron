using System.Collections.Concurrent;
using System.Diagnostics;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using static ConsoleConfig;

namespace CampertronLibrary.function.RecDotOrg.search
{
    // The host-neutral search service (GS1, decision D45; business-rules.md §19): what every host uses to search saved
    // search files and get typed results: the GUI (GS2), the web UI (D97), the interactive console (D126) and, since
    // v0.4.0's phase 14 (D131), the no-argument console's Search (ConsoleSearch, whose plan is PlanLoaded and which asks
    // for the tagged output stream it renders, SearchHostOptions.KeepOutputValues).
    //
    // Plan reads each chosen search file once, checks it as an enabled search is checked at a start (whatever its
    // autoRun says: choosing it is the explicit request) and generates its dates for Now, quietly and without the network.
    // Run searches the ready ones with the console's own search (AvailabilityApi.GetAvailabilitiesByCampground, which a
    // monitoring's check runs for each search too): the same months, filters and consecutive nights, with a per-run
    // SearchHost on RunningData for what an interactive search adds:
    // cancellation, a transport seam, at most three requests per month, no further request after a block, progress and
    // typed problems. Run is blocking (the caller owns its thread) and returns a SearchRunResult; it never throws for a
    // file, network, block, timeout, retry, response or cancellation outcome.
    //
    // An interactive search is a manual search: it never reads or writes Camp.History (its configuration is the
    // operation's own, with HtmlFile semantics on the search path: no calendar values and no notification history; nothing
    // is rendered, written or opened), never reads General.yaml or Email.yaml (no SMTP setting enters it), never writes
    // a search file, never writes to the console and never ends the process. Its results are built from the tagged
    // output's typed metadata (Kind/Metadata), never from the console text or the HTML page.
    internal static class CampsiteSearch
    {
        // ---- Plan ----

        internal static SearchRunPlan Plan(SearchRunRequest Request, DateTime Now)
        {
            ArgumentNullException.ThrowIfNull(Request);
            var Names = new List<string>();
            var Seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? Name in Request.SearchFileNames ?? Array.Empty<string>())
            {
                string Each = Name ?? string.Empty;
                if (Seen.Add(Each))
                {
                    Names.Add(Each);
                }
            }
            SearchIssue? Folders = CheckFolders(Request.ConfigPath, Request.CachePath);
            var Entries = new List<SearchPlanEntry>();
            foreach (string Name in Names)
            {
                Entries.Add(Folders != null
                    ? Entry(Name, SearchPlanState.Invalid, null, Folders with { FileName = Name })
                    : LoadEntry(Request.ConfigPath, Name, Now));
            }
            return new SearchRunPlan(Request.ConfigPath ?? string.Empty, Request.CachePath ?? string.Empty, Now, SearchLists.Of(Entries),
                Folders == null ? SearchLists.Empty<SearchIssue>() : SearchLists.Of(new[] { Folders }));
        }

        // A plan of searches a host has already read and checked as a start does: the no-argument console's Search (D131),
        // which reads the search files as every console start reads them (Yaml.CampertronConfigGetConfigs: the enabled
        // searches, an enabled search the start refuses ending the whole run, the dates generated) and searches what it
        // read. Nothing is read again: each search is the snapshot the host loaded, with its dates; one without a
        // campgroundID is Skipped, as Plan's. The folders are checked as Plan checks them
        internal static SearchRunPlan PlanLoaded(string ConfigPath, string CachePath, IReadOnlyList<(string FileName, CampertronConfig Search)> Searches, DateTime Now)
        {
            ArgumentNullException.ThrowIfNull(Searches);
            SearchIssue? Folders = CheckFolders(ConfigPath, CachePath);
            var Entries = new List<SearchPlanEntry>();
            foreach ((string FileName, CampertronConfig Search) in Searches)
            {
                Entries.Add(Folders != null
                    ? Entry(FileName, SearchPlanState.Invalid, null, Folders with { FileName = FileName })
                    : string.IsNullOrWhiteSpace(Search.CampgroundID)
                        ? Entry(FileName, SearchPlanState.Skipped, Search, Issue(SearchIssueCode.CampgroundIdMissing, SearchIssueSeverity.Warning, FileName))
                        : Entry(FileName, SearchPlanState.Ready, Search, null, Snapshot: Search));
            }
            return new SearchRunPlan(ConfigPath ?? string.Empty, CachePath ?? string.Empty, Now, SearchLists.Of(Entries),
                Folders == null ? SearchLists.Empty<SearchIssue>() : SearchLists.Of(new[] { Folders }));
        }

        // the settings folder must exist; the cache folder must be a usable one of its own (the RIDB refresh's rule:
        // not unset, not a root, not the settings folder)
        private static SearchIssue? CheckFolders(string? ConfigPath, string? CachePath)
        {
            bool Usable = !string.IsNullOrWhiteSpace(ConfigPath) && !string.IsNullOrWhiteSpace(CachePath) &&
                          Path.IsPathFullyQualified(ConfigPath) && Path.IsPathFullyQualified(CachePath) && Directory.Exists(ConfigPath);
            if (Usable)
            {
                try
                {
                    Clear.CheckCacheDirectory(CachePath, ConfigPath);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
                {
                    Usable = false;
                }
            }
            return Usable ? null : Issue(SearchIssueCode.InvalidRequest, SearchIssueSeverity.Error);
        }

        // One chosen search file, read exactly once (Yaml.ReadSettingsText, the loaders' bounded reader) and never
        // written. Its diagnostics are the configuration validation's (CM3) for that text, with the severities of an
        // enabled search, because choosing a file is an explicit request to search it: what a start refuses for an enabled
        // search (an Error) makes it Invalid; warnings stay warnings. Its dates are generated as a start generates them,
        // without the console's warnings (the validation already has them). Discovery at a start is not involved
        internal static SearchPlanEntry LoadEntry(string ConfigPath, string FileName, DateTime Now)
        {
            if (!IsSearchFileName(FileName))
            {
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.NotASearchFile, SearchIssueSeverity.Error, FileName));
            }
            string FilePath = Path.Join(ConfigPath, FileName);
            if (!File.Exists(FilePath))
            {
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.FileMissing, SearchIssueSeverity.Error, FileName));
            }
            string Text;
            try
            {
                Text = Yaml.ReadSettingsText(FilePath).Text;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.FileMissing, SearchIssueSeverity.Error, FileName));
            }
            catch (Yaml.SettingsLimitException ex)
            {
                //its message is the limit's fixed text
                var Limit = new ConfigDiagnostic(ex.Code, ConfigSeverity.Error, ConfigFileKind.Search, FileName, null, null, ex.Line, ex.Column, ex.Message);
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, FileName) with { Diagnostics = SearchLists.Of(new[] { Limit }) },
                    Diagnostics: new[] { Limit });
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.FileUnreadable, SearchIssueSeverity.Error, FileName));
            }

            List<ConfigDiagnostic> Diagnostics = ConfigValidation.ValidateSearch(Text, FileName, Now);
            CampertronConfig? Search = TryLoad(Text);
            if (Search == null)
            {
                return Entry(FileName, SearchPlanState.Invalid, null, Issue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, FileName) with { Diagnostics = SearchLists.Of(Diagnostics) },
                    Diagnostics: Diagnostics);
            }
            if (!Search.AutoRun)
            {
                CampertronConfig AsEnabled = TryLoad(Text)!;
                AsEnabled.AutoRun = true;
                Diagnostics = AsRun(Diagnostics, ConfigValidation.CheckSearch(AsEnabled, FileName, Now));
            }
            //SearchDisabled ("Not enabled …") is about the console's runs; the explicit request runs it (AutoRun says what the file has)
            Diagnostics = Diagnostics.Where(D => D.Code != ConfigCode.SearchDisabled).ToList();
            if (Diagnostics.Any(D => D.Severity == ConfigSeverity.Error))
            {
                return Entry(FileName, SearchPlanState.Invalid, Search, Issue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, FileName) with { Diagnostics = SearchLists.Of(Diagnostics) },
                    Diagnostics: Diagnostics);
            }
            if (string.IsNullOrWhiteSpace(Search.CampgroundID))
            {
                return Entry(FileName, SearchPlanState.Skipped, Search, Issue(SearchIssueCode.CampgroundIdMissing, SearchIssueSeverity.Warning, FileName), Diagnostics: Diagnostics);
            }
            try
            {
                //the dates a start generates, without its console warnings
                Search.RegenerateSearchData(Now);
            }
            catch (Exception)
            {
                return Entry(FileName, SearchPlanState.Invalid, Search, Issue(SearchIssueCode.FileInvalid, SearchIssueSeverity.Error, FileName) with { Diagnostics = SearchLists.Of(Diagnostics) },
                    Diagnostics: Diagnostics);
            }
            return Entry(FileName, SearchPlanState.Ready, Search, null, Diagnostics: Diagnostics, Snapshot: Search);
        }

        private static CampertronConfig? TryLoad(string Text)
        {
            try
            {
                return Yaml.LoadSettingsText<CampertronConfig>(Text);
            }
            catch (Exception)
            {
                //the validation's diagnostics already say what is wrong with the text, by position and never by value
                return null;
            }
        }

        // the file's own diagnostics (with their positions), each with the severity it has in an enabled search
        private static List<ConfigDiagnostic> AsRun(List<ConfigDiagnostic> FromFile, List<ConfigDiagnostic> AsEnabled)
        {
            return FromFile.Select(D =>
            {
                ConfigDiagnostic? Enabled = AsEnabled.FirstOrDefault(E => E.Code == D.Code && E.Key == D.Key && E.Index == D.Index);
                return Enabled == null || Enabled.Severity == D.Severity
                    ? D
                    : new ConfigDiagnostic(D.Code, Enabled.Severity, D.FileKind, D.FileName, D.Key, D.Index, D.Line, D.Column, D.Message);
            }).ToList();
        }

        // a file directly in the settings folder that the console would read as a search file
        private static bool IsSearchFileName(string Name) =>
            !string.IsNullOrWhiteSpace(Name) && Name == Path.GetFileName(Name) && Name.IndexOfAny(new[] { '/', '\\' }) < 0 &&
            Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Name.Length > ".yaml".Length &&
            Name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && ConfigFiles.Classify(Name) == ConfigFileKind.Search;

        private static SearchPlanEntry Entry(string FileName, SearchPlanState State, CampertronConfig? Loaded, SearchIssue? Problem,
            IEnumerable<ConfigDiagnostic>? Diagnostics = null, CampertronConfig? Snapshot = null) =>
            new SearchPlanEntry(FileName, State, Loaded?.DisplayName, Loaded?.AutoRun ?? false, Loaded?.CampgroundID, Loaded?.ConsecutiveDays ?? 0,
                SearchLists.Of(Diagnostics ?? Array.Empty<ConfigDiagnostic>()),
                Problem == null ? SearchLists.Empty<SearchIssue>() : SearchLists.Of(new[] { Problem }), Snapshot);

        // ---- Run ----

        internal static SearchRunResult Run(SearchRunPlan Plan, IProgress<SearchProgress>? Progress, CancellationToken Token, SearchHostOptions? Options = null)
        {
            ArgumentNullException.ThrowIfNull(Plan);
            DateTime Started = DateTime.UtcNow;
            Stopwatch Clock = Stopwatch.StartNew();
            SearchHostOptions Settings = Options ?? new SearchHostOptions();
            //one HttpClient for the run unless a transport was given; disposed when the run ends
            HttpSearchTransport? Owned = Settings.Transport == null ? new HttpSearchTransport(Settings.MaxResponseBytes) : null;
            try
            {
                SearchHost Host = new SearchHost(Settings, Settings.Transport ?? Owned!.Send, Token, Progress,
                    Plan.Entries.Select(E => (E.FileName, E.State == SearchPlanState.Ready ? SearchState.Waiting : E.State == SearchPlanState.Skipped ? SearchState.Skipped : SearchState.Invalid)));
                return Execute(Plan, Host, Started, Clock, Settings.KeepOutputValues);
            }
            finally
            {
                Owned?.Dispose();
            }
        }

        // one search's work in a run: the months it needs (its first days, the search's own rule), their URLs, and how it
        // ended. Each is written by its own search only
        private sealed class Work
        {
            internal Work(SearchPlanEntry Entry, List<DateTime> Months)
            {
                this.Entry = Entry;
                this.Months = Months;
                Urls = Months.Select(M => AvailabilityApi.MonthUrl(Entry.Search!.CampgroundID, M)).ToList();
            }
            internal SearchPlanEntry Entry { get; }
            internal List<DateTime> Months { get; }
            internal List<string> Urls { get; }
            internal List<ConsoleConfigValue>? Values { get; set; }
            internal bool Canceled { get; set; }
            internal SearchIssue? Failure { get; set; }
        }

        private static SearchRunResult Execute(SearchRunPlan Plan, SearchHost Host, DateTime Started, Stopwatch Clock, bool KeepOutputValues)
        {
            CancellationToken Token = Host.Token;
            List<SearchPlanEntry> Runnable = Plan.Entries.Where(E => E.State == SearchPlanState.Ready && E.Search != null).ToList();
            var Works = new Dictionary<SearchPlanEntry, Work>(ReferenceEqualityComparer.Instance);
            var Prepared = new ConcurrentDictionary<string, SearchIssue?>(StringComparer.Ordinal);
            var Infos = new ConcurrentDictionary<string, ReturnParkCampground?>(StringComparer.Ordinal);
            var RunIssues = new List<SearchIssue>(Plan.Issues);
            bool Canceled = false;
            Host.ReportNow();
            try
            {
                //Validating: the months each search needs and the cache folder (created as a start creates it)
                DateTime Now = DateTime.Now;
                foreach (SearchPlanEntry Entry in Runnable)
                {
                    Work Each = new Work(Entry, PlannedMonths(Entry.Search!, Now));
                    Works[Entry] = Each;
                    Host.PlanRequests(Each.Urls);
                }
                if (Runnable.Count > 0)
                {
                    try
                    {
                        Directory.CreateDirectory(Plan.CachePath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        foreach (Work Each in Works.Values)
                        {
                            Each.Failure = Issue(SearchIssueCode.CampsiteDetailsUnavailable, SearchIssueSeverity.Error, Each.Entry.FileName);
                        }
                    }
                }
                Token.ThrowIfCancellationRequested();

                //PreparingCampsiteDetails: each campground's cache, once (as Load.Preload), silently and cancellably
                List<string> Campgrounds = Works.Values.Where(W => W.Failure == null).Select(W => W.Entry.Search!.CampgroundID!).Distinct(StringComparer.Ordinal).ToList();
                Host.PlanCampgrounds(Campgrounds.Count);
                Host.SetStage(SearchRunStage.PreparingCampsiteDetails);
                Parallel.ForEach(Campgrounds, new ParallelOptions { CancellationToken = Token }, Campground =>
                {
                    try
                    {
                        Prepared[Campground] = Prepare(Plan, Campground, Token);
                        Infos[Campground] = Cache.GetCampInfo(Campground, Plan.CachePath);
                    }
                    catch (OperationCanceledException) when (Token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Host.RecordDefect(ex);
                        Prepared[Campground] = Issue(SearchIssueCode.CampsiteDetailsUnavailable, SearchIssueSeverity.Error) with { DefectType = ex.GetType().FullName };
                    }
                    Host.CampgroundPrepared();
                });
                foreach (Work Each in Works.Values.Where(W => W.Failure == null))
                {
                    if (Prepared.TryGetValue(Each.Entry.Search!.CampgroundID!, out SearchIssue? Problem) && Problem != null)
                    {
                        Each.Failure = Problem with { FileName = Each.Entry.FileName };
                    }
                }

                //CheckingAvailability: the console's own search for each, in parallel as the console runs them
                Host.SetStage(SearchRunStage.CheckingAvailability);
                RunningData RunData = NewRun(Host);
                CampertronInternalConfig Config = InteractiveConfig(Plan);
                List<Work> ToSearch = Works.Values.Where(W => W.Failure == null).ToList();
                foreach (Work Each in Works.Values.Where(W => W.Failure != null))
                {
                    Host.SetSearchState(Each.Entry.FileName, SearchState.Failed);
                }
                Parallel.ForEach(ToSearch, new ParallelOptions { CancellationToken = Token }, Each =>
                {
                    Host.SetSearchState(Each.Entry.FileName, SearchState.Searching);
                    try
                    {
                        //the operation a monitoring's check runs for each search (and the console's loop did before phase 14), unchanged
                        Each.Values = AvailabilityApi.GetAvailabilitiesByCampground(Each.Entry.Search!, ref RunData, Config);
                    }
                    catch (OperationCanceledException) when (Token.IsCancellationRequested)
                    {
                        Each.Canceled = true;
                    }
                    catch (Cache.CampsiteDetailsMissingException)
                    {
                        Each.Failure = Issue(SearchIssueCode.CampsiteDetailsUnavailable, SearchIssueSeverity.Error, Each.Entry.FileName);
                    }
                    catch (Exception ex)
                    {
                        //a defect on the search path: contained to this search and kept for diagnostics
                        Host.RecordDefect(ex);
                        Each.Failure = Issue(SearchIssueCode.InternalError, SearchIssueSeverity.Error, Each.Entry.FileName) with { DefectType = ex.GetType().FullName };
                    }
                    Host.SetSearchState(Each.Entry.FileName, Each.Canceled ? SearchState.Canceled : Each.Failure != null ? SearchState.Failed : StateOf(Each, Host));
                });
            }
            catch (OperationCanceledException) when (Token.IsCancellationRequested)
            {
                Canceled = true;
            }
            catch (Exception ex)
            {
                //a defect of the run itself: the run ends with what it has, never with an exception
                Host.RecordDefect(ex);
                RunIssues.Add(Issue(SearchIssueCode.InternalError, SearchIssueSeverity.Error) with { DefectType = ex.GetType().FullName });
            }

            //BuildingResults: in the plan's order; once canceled, the searches not built yet are reported canceled
            Host.SetStage(SearchRunStage.BuildingResults);
            var Results = new List<SearchResult>();
            foreach (SearchPlanEntry Entry in Plan.Entries)
            {
                Works.TryGetValue(Entry, out Work? Each);
                ReturnParkCampground? Info = null;
                if (Entry.Search?.CampgroundID != null)
                {
                    Infos.TryGetValue(Entry.Search.CampgroundID, out Info);
                }
                SearchResult Result = Build(Entry, Each, Info, Host, Canceled || Token.IsCancellationRequested);
                if (KeepOutputValues && Each?.Values != null && Each.Failure == null && Result.State != SearchState.Canceled)
                {
                    //the stream as the search wrote it to its end (data; a month that was not checked added nothing to it)
                    Result = Result with { OutputValues = SearchLists.Of(Each.Values) };
                }
                Results.Add(Result);
                Host.SetSearchState(Entry.FileName, Result.State);
            }
            Canceled = Canceled || Token.IsCancellationRequested;

            SearchIssue? Block = Host.Blocked;
            if (Block != null)
            {
                RunIssues.Add(Block);
            }
            SearchRunStatus Status = Canceled ? SearchRunStatus.Canceled
                : Block != null ? SearchRunStatus.Blocked
                : Results.Count > 0 && Results.All(R => R.State == SearchState.Complete) && RunIssues.All(I => I.Severity != SearchIssueSeverity.Error) ? SearchRunStatus.Completed
                : Results.Any(R => R.State == SearchState.Complete || R.State == SearchState.Incomplete) ? SearchRunStatus.CompletedWithProblems
                : SearchRunStatus.Failed;
            if (Canceled)
            {
                RunIssues.Add(Issue(SearchIssueCode.Canceled, SearchIssueSeverity.Warning));
            }
            Host.SetStage(Status switch
            {
                SearchRunStatus.Completed => SearchRunStage.Completed,
                SearchRunStatus.CompletedWithProblems => SearchRunStage.CompletedWithProblems,
                SearchRunStatus.Blocked => SearchRunStage.Blocked,
                SearchRunStatus.Canceled => SearchRunStage.Canceled,
                _ => SearchRunStage.Failed
            });
            (int Planned, int Completed, int Failed) = Host.RequestCounts();
            return new SearchRunResult(Status, Started, Clock.Elapsed, Planned, Completed, Failed, SearchLists.Of(Results), SearchLists.Of(RunIssues))
            {
                Defects = Host.Defects
            };
        }

        // the campground's cache, built from the campground data when it is not complete (never without the database:
        // opening a missing database file would create an empty one)
        private static SearchIssue? Prepare(SearchRunPlan Plan, string Campground, CancellationToken Token)
        {
            if (!Cache.CacheExist(Campground, Plan.CachePath))
            {
                if (!Load.DbExists(new CampertronInternalConfig { ConfigPath = Plan.ConfigPath }))
                {
                    return Issue(SearchIssueCode.CampsiteDataMissing, SearchIssueSeverity.Error);
                }
                Cache.PrepareCampground(Campground, Plan.CachePath, Plan.ConfigPath, null, Token);
            }
            return null;
        }

        // the months GetAvailabilitiesByCampground checks for this search (months 0 to GetMonthsToCheck from now that
        // hold a search date), as their first days
        internal static List<DateTime> PlannedMonths(CampertronConfig Search, DateTime Now)
        {
            var Months = new List<DateTime>();
            int Counter = 0;
            while (Counter <= Search.GetMonthsToCheck())
            {
                DateTime Pdt = Now.AddMonths(Counter);
                if (AvailabilityApi.HasDatesDuringThisMonth(Search, Pdt.Month, Pdt.Year))
                {
                    Months.Add(AvailabilityApi.MonthStart(Pdt));
                }
                Counter++;
            }
            return Months;
        }

        // A manual search: on the search path HtmlFile means no calendar values in the output and no notification history
        // (Load.UsesNotificationHistory is false, so FilterData never reads Camp.History). Run never renders, writes or
        // opens anything with it; no Email settings are part of it. (The console's renderers draw a calendar of their own
        // from the openings and skip a stream's calendar values, so the no-argument console's Search, which renders the
        // kept stream, needs none either, D131)
        private static CampertronInternalConfig InteractiveConfig(SearchRunPlan Plan) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = OutputType.HtmlFile },
            EmailConfig = null,
            ContainerMode = false,
            ConfigPath = Plan.ConfigPath,
            CachePath = Plan.CachePath
        };

        private static RunningData NewRun(SearchHost Host) => new RunningData
        {
            AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>(),
            SiteData = new ConcurrentDictionary<string, AvailabilityEntries>(),
            Urls = new ConcurrentDictionary<string, bool>(),
            UniqueCampgroundIds = new List<string>(),
            CampertronConfigFiles = new List<CampertronConfig>(),
            FilteredAvailableData = new ConcurrentBag<AvailableData>(),
            Host = Host
        };

        // a finished search's state from its months: all checked, some, or none
        private static SearchState StateOf(Work Each, SearchHost Host)
        {
            IReadOnlyDictionary<DateTime, MonthMark> Marks = Host.MonthsOf(Each.Entry.Search!);
            int Checked = Each.Months.Count(M => Marks.TryGetValue(M, out MonthMark? Mark) && Mark.Checked);
            return Checked == Each.Months.Count ? SearchState.Complete : Checked > 0 ? SearchState.Incomplete : SearchState.Failed;
        }

        // ---- results ----

        private static SearchResult Build(SearchPlanEntry Entry, Work? Each, ReturnParkCampground? Info, SearchHost Host, bool Canceled)
        {
            var Issues = new List<SearchIssue>(Entry.Issues);
            if (Entry.State != SearchPlanState.Ready || Each == null)
            {
                SearchState NotRun = Entry.State == SearchPlanState.Skipped ? SearchState.Skipped : Entry.State == SearchPlanState.Ready ? SearchState.Canceled : SearchState.Invalid;
                return new SearchResult(Entry.FileName, Entry.DisplayName, Entry.AutoRun, Entry.CampgroundId, null, null, Entry.ConsecutiveNights, NotRun, 0, 0,
                    SearchLists.Empty<SearchMonthResult>(), SearchLists.Empty<SearchOpening>(), SearchLists.Empty<SearchSite>(), SearchLists.Of(Issues), Entry.Diagnostics);
            }
            if (Info != null && string.IsNullOrWhiteSpace(Info.CampsiteName))
            {
                Issues.Add(Issue(SearchIssueCode.CampgroundNotFound, SearchIssueSeverity.Warning, Entry.FileName));
            }

            //why a month it needed has no mark: it was never reached
            SearchIssueCode Unreached = Each.Canceled || Canceled && Each.Values == null ? SearchIssueCode.Canceled
                : Each.Failure?.Code ?? Host.Blocked?.Code ?? SearchIssueCode.InternalError;
            IReadOnlyDictionary<DateTime, MonthMark> Marks = Host.MonthsOf(Entry.Search!);
            var Months = new List<SearchMonthResult>();
            foreach (DateTime Month in Each.Months.Concat(Marks.Keys.Where(K => !Each.Months.Contains(K)).OrderBy(K => K)))
            {
                if (Marks.TryGetValue(Month, out MonthMark? Mark))
                {
                    Months.Add(new SearchMonthResult(Month, Mark.Checked, Mark.Reason));
                    if (!Mark.Checked && Mark.Reason != null && Mark.Reason != SearchIssueCode.Canceled)
                    {
                        Issues.Add(Issue(Mark.Reason.Value, SearchIssueSeverity.Error, Entry.FileName) with { Month = Month, StatusCode = Mark.StatusCode, Attempts = Mark.Attempts });
                    }
                }
                else
                {
                    Months.Add(new SearchMonthResult(Month, false, Unreached));
                }
            }

            SearchState State;
            IReadOnlyList<SearchOpening> Openings = SearchLists.Empty<SearchOpening>();
            OutputMetadata? Header = null;
            if (Each.Failure != null)
            {
                State = SearchState.Failed;
                Issues.Add(Each.Failure);
            }
            else if (Each.Canceled || Each.Values == null || Canceled)
            {
                //a search that did not finish, or whose results were not built before the run was canceled
                State = Each.Values == null && !Each.Canceled && !Canceled ? SearchState.Failed : SearchState.Canceled;
            }
            else
            {
                try
                {
                    Openings = OpeningsOf(Each.Values, Host.Token, out Header);
                    int Checked = Months.Count(M => M.Checked);
                    State = Checked == Months.Count ? SearchState.Complete : Checked > 0 ? SearchState.Incomplete : SearchState.Failed;
                }
                catch (OperationCanceledException) when (Host.Token.IsCancellationRequested)
                {
                    Openings = SearchLists.Empty<SearchOpening>();
                    State = SearchState.Canceled;
                }
            }
            return new SearchResult(Entry.FileName, Entry.DisplayName, Entry.AutoRun, Entry.CampgroundId,
                Header?.CampgroundName ?? Info?.CampsiteName, Header?.ParkName ?? Info?.ParkName, Entry.ConsecutiveNights, State,
                Each.Urls.Count, Each.Urls.Count(Host.RequestDone), SearchLists.Of(Months), Openings, SitesOf(Openings), SearchLists.Of(Issues), Entry.Diagnostics);
        }

        // The openings of one search's output stream, from its typed metadata only (Kind and Metadata, never the text):
        // the Availability values of single nights and the ConsecutiveDate values of runs, which carry the campsite and
        // its night; one opening per campsite and night (the first in the stream). Header: the stream's SearchHeader
        internal static IReadOnlyList<SearchOpening> OpeningsOf(IEnumerable<ConsoleConfigValue> Values, CancellationToken Token, out OutputMetadata? Header)
        {
            Header = null;
            var Seen = new HashSet<(string, DateTime)>();
            var Openings = new List<SearchOpening>();
            int Count = 0;
            foreach (ConsoleConfigValue Value in Values)
            {
                if ((++Count & 1023) == 0)
                {
                    Token.ThrowIfCancellationRequested();
                }
                OutputMetadata? Meta = Value.Metadata;
                switch (Value.Kind)
                {
                    case OutputKind.SearchHeader:
                        Header ??= Meta;
                        break;
                    case OutputKind.Availability:
                    case OutputKind.ConsecutiveDate:
                        if (Meta?.Date == null)
                        {
                            break;
                        }
                        string Id = Meta.CampsiteID ?? string.Empty;
                        DateTime Night = Meta.Date.Value.Date;
                        if (Seen.Add((Id, Night)))
                        {
                            Openings.Add(new SearchOpening(Id, Meta.CampsiteName, Meta.CampsiteLoop, Meta.CampsiteType,
                                SearchLists.Of(Meta.Equipment ?? new List<string>()), Night, Meta.Checkin, Meta.Checkout, Meta.MinOccupancy, Meta.MaxOccupancy, BookingUrl(Id)));
                        }
                        break;
                }
            }
            Token.ThrowIfCancellationRequested();
            Openings.Sort((A, B) =>
            {
                int ByNight = A.Night.CompareTo(B.Night);
                return ByNight != 0 ? ByNight : CompareIds(A.CampsiteId, B.CampsiteId);
            });
            return SearchLists.Of(Openings);
        }

        // the openings by campsite (in campsite ID order), each with its nights and their runs of consecutive nights;
        // the campsite's details are those of its first night
        internal static IReadOnlyList<SearchSite> SitesOf(IReadOnlyList<SearchOpening> Openings)
        {
            var Sites = new List<SearchSite>();
            foreach (IGrouping<string, SearchOpening> Site in Openings.GroupBy(O => O.CampsiteId, StringComparer.Ordinal))
            {
                SearchOpening First = Site.First();
                List<DateTime> Nights = Site.Select(O => O.Night).Distinct().OrderBy(N => N).ToList();
                var Stays = new List<SearchStay>();
                foreach (DateTime Night in Nights)
                {
                    if (Stays.Count > 0 && Stays[^1].CheckoutDate == Night)
                    {
                        Stays[^1] = Stays[^1] with { Nights = Stays[^1].Nights + 1 };
                    }
                    else
                    {
                        Stays.Add(new SearchStay(Night, 1));
                    }
                }
                Sites.Add(new SearchSite(First.CampsiteId, First.CampsiteName, First.Loop, First.CampsiteType, First.Equipment, First.CheckinTime, First.CheckoutTime,
                    First.MinOccupancy, First.MaxOccupancy, First.BookingUrl, SearchLists.Of(Nights), SearchLists.Of(Stays)));
            }
            Sites.Sort((A, B) => CompareIds(A.CampsiteId, B.CampsiteId));
            return SearchLists.Of(Sites);
        }

        // campsite IDs are numbers written as text: shorter first, then ordinal, so they sort as numbers in every culture
        private static int CompareIds(string A, string B)
        {
            int ByLength = A.Length.CompareTo(B.Length);
            return ByLength != 0 ? ByLength : string.CompareOrdinal(A, B);
        }

        // recreation.gov's campsite page: the fixed prefix of the results page and the escaped campsite ID, nothing else
        internal static string BookingUrl(string CampsiteId) => ResultsHtml.BookingUrlPrefix + Uri.EscapeDataString(CampsiteId);

        // ---- issues ----

        private static SearchIssue Issue(SearchIssueCode Code, SearchIssueSeverity Severity, string? FileName = null) =>
            new SearchIssue(Code, Severity, Message(Code), FileName);

        // fixed text only: never a response body, an exception's message, a path or a value of a settings file
        internal static string Message(SearchIssueCode Code) => Code switch
        {
            SearchIssueCode.InvalidRequest => "The settings folder or the cache folder can't be used for a search.",
            SearchIssueCode.NotASearchFile => "This is not a search file: a search file is a .yaml file in the settings folder other than General.yaml and Email.yaml.",
            SearchIssueCode.FileMissing => "The search file does not exist.",
            SearchIssueCode.FileUnreadable => "The search file could not be read.",
            SearchIssueCode.FileInvalid => "The search file can't be searched until the errors the validation shows are fixed.",
            SearchIssueCode.CampgroundIdMissing => "campgroundID is empty, so this search was skipped.",
            SearchIssueCode.CampsiteDataMissing => "The campground data is missing, so the campsite details could not be prepared.",
            SearchIssueCode.CampsiteDetailsUnavailable => "The campsite details of this campground could not be prepared.",
            SearchIssueCode.CampgroundNotFound => "The campground ID is not a reservable campground in the campground data, so the search finds nothing.",
            SearchIssueCode.NetworkFailure => "recreation.gov could not be reached, so this month was not checked.",
            SearchIssueCode.Timeout => "recreation.gov did not answer in time, so this month was not checked.",
            SearchIssueCode.HttpStatus => "recreation.gov answered with an error, so this month was not checked.",
            SearchIssueCode.HeavyTraffic => "recreation.gov reported heavy traffic, so this month was not checked.",
            SearchIssueCode.MalformedResponse => "recreation.gov's answer could not be read, so this month was not checked.",
            SearchIssueCode.UnexpectedResponse => "recreation.gov's answer held no availability for the month, so this month was not checked.",
            SearchIssueCode.ResponseTooLarge => "recreation.gov's answer was larger than Campertron reads, so this month was not checked.",
            SearchIssueCode.RequestBlocked => "recreation.gov blocked the requests for exceeding its request limit, so no further request was made. Try again later.",
            SearchIssueCode.RateLimited => "recreation.gov asked for fewer requests (HTTP 429), so no further request was made. Try again later.",
            SearchIssueCode.Canceled => "The search was canceled.",
            _ => "The search stopped because of an unexpected problem in Campertron."
        };
    }
}
