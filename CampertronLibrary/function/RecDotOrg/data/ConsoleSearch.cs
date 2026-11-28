using System.Runtime.ExceptionServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.search;
using static ConsoleConfig;

namespace CampertronLibrary.function.RecDotOrg.data
{
    // The no-argument console's Search, the menu's (v0.4.0's phase 14, decision D131): the console's adapter of the shared
    // search service, as MonitoringConsole is the monitoring service's and RefreshRidbRecreationData the refresh service's.
    // It lives beside Load, which calls it, outside the search service's namespace, which never writes to a console.
    //
    // CampsiteSearch is the Search's only request and result authority: its requests (SearchHost's bounds: at most three
    // per month, a 30-second timeout, HTTP 429 and the block page a block and no request after one) and its results, with
    // the tagged output stream it keeps for this host (SearchHostOptions.KeepOutputValues; no calendar values: the
    // console's renderers draw their own calendar from the openings). The console keeps its presentation: the search files
    // read as every console start reads them (the example search when there is none, the date warnings, an enabled search
    // the start refuses ending the whole run), each campground's cache prepared with its status line, the scope, a
    // "Checked" line for each search whose every month was checked, its own renderers (ConsoleResults, the results page)
    // and, from the menu, the key wait and the menu again.
    //
    // What the shared bounds bring is shown in the console's words: a month that could not be checked is a warning with
    // CampsiteSearch's fixed message (never left out silently, never shown as a month without availability), and a block
    // is the console's own answer, once, after the run (AvailabilityApi.ConsoleBlocked: the error, the five-minute wait and
    // exit code 1). It is a manual search: Camp.History is never read or written, no notification is sent and no settings
    // file is written but the example search
    internal static class ConsoleSearch
    {
        // the run's options: CampsiteSearch's own bounds (SearchHostOptions' defaults: its transport, a 30-second request
        // timeout, 30 seconds before another attempt, 1 second after heavy traffic, at most 32 MB an answer), keeping the
        // stream this host renders. RunOptions is replaceable so tests answer recreation.gov in memory and shorten the waits
        // (they keep the stream too); production is DefaultRunOptions
        internal static SearchHostOptions DefaultRunOptions() => new SearchHostOptions { KeepOutputValues = true };
        internal static Func<SearchHostOptions> RunOptions { get; set; } = DefaultRunOptions;

        // the menu's Search: true when the run ended by itself (outputTo: UnitTest, after its output). Otherwise the key wait
        // and the menu again, which end only with the process; false after a block, whose answer ends the process
        internal static bool Run(CampertronInternalConfig InternalConfig)
        {
            if (!Show(InternalConfig))
            {
                return false;
            }
            if (InternalConfig.GeneralConfig?.OutputTo == OutputType.UnitTest)
            {
                return true;
            }
            ConsoleUi.HideCursor();
            ConsoleUi.Line();
            ConsoleUi.WaitForKey("Press any key to return to the menu...");
            Menu.Init(InternalConfig);
            return false;
        }

        // the Search up to its results: true when they were shown; false after a block (the console's answer has been
        // given: in production the process has ended). Throws what reading the search files or preparing a campground
        // throws, and an unexpected problem the shared run contained, as the console's Search always ended with them
        internal static bool Show(CampertronInternalConfig InternalConfig)
        {
            OutputType? OutputTo = InternalConfig.GeneralConfig?.OutputTo;
            //a search from the menu starts on a clear screen (a terminal only)
            if (OutputTo == OutputType.Console || OutputTo == OutputType.HtmlFile)
            {
                ConsoleUi.ClearScreen();
            }
            //the search files as every console start reads them (Yaml.CampertronConfigGetConfigs(ConfigPath)), with their names
            var FileNames = new List<string>();
            List<CampertronConfig> Searches = Yaml.CampertronConfigGetConfigs(InternalConfig.ConfigPath!, true, true, ConsoleUi.Warning, null, out _, FileNames);
            //each campground's cache, with the status line for one prepared now; then what is about to be searched
            Load.PrepareCampgrounds(InternalConfig, Searches, ConsoleUi.Status);
            if (OutputTo != OutputType.UnitTest)
            {
                Load.WriteScope(InternalConfig, Searches);
            }

            List<(string FileName, CampertronConfig Search)> Loaded = FileNames.Zip(Searches).ToList();
            SearchRunPlan Plan = CampsiteSearch.PlanLoaded(InternalConfig.ConfigPath!, InternalConfig.CachePath!, Loaded, DateTime.Now);
            if (Plan.Issues.Count > 0)
            {
                //folders a search can't use (GetConfig never gives such): the shared search's fixed words
                throw new InvalidOperationException(Plan.Issues[0].Message);
            }
            //a search without a campgroundID: its line ("Skipped …"), as the console's Search wrote it
            foreach ((string FileName, CampertronConfig Search) in Loaded.Where((L, i) => Plan.Entries[i].State == SearchPlanState.Skipped))
            {
                ConsoleUi.Status(Load.CheckedText(Search, null, 0));
            }
            SearchRunResult Result = CampsiteSearch.Run(Plan, new CheckedLines(Plan, InternalConfig.CachePath), CancellationToken.None, RunOptions());
            if (Result.Defects.Count > 0)
            {
                //an unexpected problem, as the console's Search ended with one: Program.Main reports it, exit code 1
                ExceptionDispatchInfo.Throw(Result.Defects[0]);
            }

            foreach (string Warning in UncheckedMonths(Result))
            {
                ConsoleUi.Warning(Warning);
            }
            if (Result.Status == SearchRunStatus.Blocked)
            {
                AvailabilityApi.ConsoleBlocked(BlockedRequest(Result));
                return false;
            }

            //the results, ordered by display name (Load.OutputResults); a search that could not check any month is left to
            //its warnings, so it never reads as one without availability
            List<ConsoleConfigItem> Items = Result.Searches
                .Where(S => S.State == SearchState.Complete || S.State == SearchState.Incomplete || S.State == SearchState.Skipped)
                .Select(S => new ConsoleConfigItem { Name = S.DisplayName, Values = S.OutputValues?.ToList() ?? new List<ConsoleConfigValue>() })
                .ToList();
            ConfigType LastConfigType = ConfigType.WriteLine;
            Load.OutputResults(InternalConfig, Items, ref LastConfigType);
            return true;
        }

        private static bool IsBlock(SearchIssueCode? Code) => Code == SearchIssueCode.RequestBlocked || Code == SearchIssueCode.RateLimited;

        // One warning per month a search needed and the run could not check (a block's months are its error's): the shared
        // search's fixed message, then what it was for, as the console's request warnings name it (the search, the
        // campground and the month) and the facts the shared results add (the HTTP status, the requests made)
        internal static List<string> UncheckedMonths(SearchRunResult Result)
        {
            var Warnings = new List<string>();
            foreach (SearchResult Search in Result.Searches)
            {
                foreach (SearchMonthResult Month in Search.Months.Where(M => !M.Checked && M.Reason != null && !IsBlock(M.Reason) && M.Reason != SearchIssueCode.Canceled))
                {
                    SearchIssue? Issue = Search.Issues.FirstOrDefault(I => I.Code == Month.Reason && I.Month == Month.Month);
                    var Facts = new List<string> { AvailabilityApi.DescribeRequest(AvailabilityApi.MonthUrl(Search.CampgroundId, Month.Month)) };
                    if (Issue?.StatusCode != null)
                    {
                        Facts.Add("HTTP " + Issue.StatusCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    if (Issue != null && Issue.Attempts > 0)
                    {
                        Facts.Add(ConsoleUi.Count(Issue.Attempts, "request", "requests"));
                    }
                    Warnings.Add("\"" + Search.DisplayName + "\": " + CampsiteSearch.Message(Month.Reason!.Value) + " (" + string.Join(", ", Facts) + ")");
                }
            }
            return Warnings;
        }

        // the request recreation.gov blocked, as the console's error names it: the first month whose request was answered with
        // the block (the plan's order); the months after it were never requested
        internal static string BlockedRequest(SearchRunResult Result)
        {
            foreach (bool Answered in new[] { true, false })
            {
                foreach (SearchResult Search in Result.Searches)
                {
                    SearchIssue? Block = Search.Issues.FirstOrDefault(I => IsBlock(I.Code) && I.Month != null && (!Answered || I.Attempts > 0));
                    if (Block != null)
                    {
                        return AvailabilityApi.DescribeRequest(AvailabilityApi.MonthUrl(Search.CampgroundId, Block.Month!.Value));
                    }
                }
            }
            throw new InvalidOperationException("The shared search reported a block without the month it was for.");
        }

        // The console's line for each search whose every month was checked, when it ends: Load.CheckedText with the time
        // from its start, as the console's Search always wrote it. The run reports its progress from the searching threads
        // under its own lock, one snapshot after another
        private sealed class CheckedLines : IProgress<SearchProgress>
        {
            private readonly Dictionary<string, CampertronConfig> Searches = new(StringComparer.Ordinal);
            private readonly Dictionary<string, DateTime> Started = new(StringComparer.Ordinal);
            private readonly HashSet<string> Ended = new(StringComparer.Ordinal);
            private readonly string? CachePath;

            internal CheckedLines(SearchRunPlan Plan, string? CachePath)
            {
                foreach (SearchPlanEntry Entry in Plan.Entries.Where(E => E.State == SearchPlanState.Ready && E.Search != null))
                {
                    Searches[Entry.FileName] = Entry.Search!;
                }
                this.CachePath = CachePath;
            }

            public void Report(SearchProgress Value)
            {
                lock (Started)
                {
                    foreach (SearchProgressItem Item in Value.Searches)
                    {
                        if (!Searches.TryGetValue(Item.FileName, out CampertronConfig? Search) || Ended.Contains(Item.FileName))
                        {
                            continue;
                        }
                        if (Item.State == SearchState.Searching)
                        {
                            Started.TryAdd(Item.FileName, DateTime.UtcNow);
                        }
                        else if (Started.TryGetValue(Item.FileName, out DateTime Start) && Item.State != SearchState.Waiting)
                        {
                            Ended.Add(Item.FileName);
                            if (Item.State == SearchState.Complete)
                            {
                                ConsoleUi.Status(Load.CheckedText(Search, Cache.GetCampInfo(Search.CampgroundID, CachePath), (DateTime.UtcNow - Start).TotalSeconds));
                            }
                        }
                    }
                }
            }
        }
    }
}
