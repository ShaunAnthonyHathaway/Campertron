using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // The no-argument console's Search as it was before v0.4.0's phase 14, kept as the parity tests' reference: the
    // ForceSearch branch of Load.RunConsoleSearch with Load.Preload, Load.Prepare and the report of Load.PostProcess
    // (Load.ReportResults for an output that is not a monitoring), transcribed from master 11a3b82 call for call, without
    // the menu's tail after the output (the key wait and the menu again). It runs the console's own request path
    // (RunningData.Host null: AvailabilityApi.GetWebApiData through AvailabilityApi.ConsoleDownload, its unlimited
    // retries and its answer to a block). Test code only; the product's Search runs on CampsiteSearch since phase 14
    internal static class LegacyConsoleSearch
    {
        internal static void Run(CampertronInternalConfig InternalConfig)
        {
            //Load.Preload
            OutputType? OutputTo = InternalConfig.GeneralConfig?.OutputTo;
            if (OutputTo == OutputType.Console || OutputTo == OutputType.HtmlFile)
            {
                ConsoleUi.ClearScreen();
            }
            List<CampertronConfig> CampertronConfigFiles = Yaml.CampertronConfigGetConfigs(InternalConfig.ConfigPath!);
            //Load.Prepare
            RunningData RunData = Load.PrepareRun(InternalConfig, CampertronConfigFiles, ConsoleUi.Status);
            if (InternalConfig.GeneralConfig?.OutputTo != OutputType.UnitTest)
            {
                Load.WriteScope(InternalConfig, CampertronConfigFiles);
            }
            //the loop's first pass
            Parallel.ForEach(RunData.CampertronConfigFiles, ThisCampertronConfig =>
            {
                ConsoleConfig.ConsoleConfigItem NewConfigItem = new ConsoleConfig.ConsoleConfigItem();
                NewConfigItem.Name = ThisCampertronConfig.DisplayName;
                DateTime Start = DateTime.UtcNow;
                NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisCampertronConfig, ref RunData, InternalConfig);
                RunData.AllConsoleConfigItems.Add(NewConfigItem);
                DateTime End = DateTime.UtcNow;
                double TotalSeconds = (End - Start).TotalSeconds;
                ConsoleUi.Status(Load.CheckedText(ThisCampertronConfig, Cache.GetCampInfo(ThisCampertronConfig.CampgroundID, InternalConfig.CachePath), TotalSeconds));
            });
            //Load.PostProcess → Load.ReportResults: the read of Camp.History, then the run's output
            ConsoleConfig.ConfigType LastConfigType = ConsoleConfig.ConfigType.WriteLine;
            Load.HasUnreportedEntries(new List<AvailableData>(RunData.FilteredAvailableData), InternalConfig);
            Load.OutputResults(InternalConfig, RunData.AllConsoleConfigItems, ref LastConfigType);
        }
    }

    // recreation.gov in memory for both request paths: the shared search's transport (Send: the status and the body) and
    // the console's download seam (Download: the body only, whatever the status, as AvailabilityApi.ConsoleDownload reads
    // it). Every month of a campground the fixture prepared has the same deterministic availability unless a URL is
    // scripted; a script's answers are given in order, its last one again and again. Every request is recorded in order
    internal sealed class GovStandIn
    {
        private readonly ConcurrentDictionary<string, List<Func<SearchRequest, CancellationToken, SearchResponse>>> Scripts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> Attempts = new(StringComparer.Ordinal);
        internal readonly ConcurrentQueue<SearchRequest> Requests = new();

        internal void Script(string Url, params Func<SearchRequest, CancellationToken, SearchResponse>[] Answers) => Scripts[Url] = Answers.ToList();

        internal SearchResponse Send(SearchRequest Request, CancellationToken Token)
        {
            Requests.Enqueue(Request);
            if (Scripts.TryGetValue(Request.Url, out var Answers) && Answers.Count > 0)
            {
                return Answers[Math.Min(Request.Attempt, Answers.Count) - 1](Request, Token);
            }
            return new SearchResponse(200, DefaultMonth(Request.CampgroundId, Request.Month));
        }

        // the console's seam: one request, its attempt counted per URL
        internal string Download(string Url)
        {
            int Attempt = Attempts.AddOrUpdate(Url, 1, (_, Count) => Count + 1);
            return Send(RequestOf(Url, Attempt), CancellationToken.None).Body ?? string.Empty;
        }

        internal int Count(string Url) => Requests.Count(R => R.Url == Url);

        internal static SearchRequest RequestOf(string Url, int Attempt)
        {
            Match Parts = Regex.Match(Url, @"/campground/([^/]*)/month\?start_date=(\d{4})-(\d{2})-");
            return new SearchRequest(Url, Parts.Groups[1].Value,
                new DateTime(int.Parse(Parts.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(Parts.Groups[3].Value, CultureInfo.InvariantCulture), 1), Attempt);
        }

        // the first eight campsites of a prepared campground, each available on the days its position and the day make
        // a multiple of three; any other campground has no campsite
        internal static string DefaultMonth(string Campground, DateTime Month)
        {
            List<string> Sites = SearchFixture.Sites.ContainsKey(Campground) ? SearchFixture.SiteIds(Campground, 8) : new List<string>();
            return SearchTestBase.MonthJson(Month, Sites, (Id, Day) => (Sites.IndexOf(Id) + Day) % 3 == 0);
        }

        internal static SearchResponse Status(int Code, string Body) => new SearchResponse(Code, Body);
    }

    // Each test: its own settings folder and a copy of the fixture's campground cache (an HtmlFile run writes its page
    // there and removes the previous one), en-US, the console captured as a redirected one, recreation.gov in memory
    // through both seams (the console Search's shared transport, ConsoleSearch.RunOptions, with no wait between two
    // attempts unless a test sets one, and the console's download), the blocked answer's wait and exit and the opened
    // results pages recorded, months two and more ahead
    public abstract class ConsoleSearchTestBase
    {
        protected string ConfigPath = string.Empty;
        protected string CachePath = string.Empty;
        protected DateTime MonthA;
        protected DateTime MonthB;
        protected DateTime MonthC;
        internal GovStandIn Gov = null!;
        protected StringWriter Out = null!;
        protected StringWriter Err = null!;
        protected readonly List<string> Steps = new();
        protected readonly List<string> Pages = new();
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;
        private TextWriter? OriginalErr;
        private ConsoleUi.Capabilities? OriginalCapabilities;
        private Action<int>? OriginalExit;
        private Action<TimeSpan>? OriginalWait;
        private Func<string, string>? OriginalDownload;
        private Action<string>? OriginalOpen;
        private Func<SearchHostOptions>? OriginalSearchOptions;
        // the shared run's bounds in a test: the production request timeout, no wait between attempts
        protected TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
        protected TimeSpan RetryDelay = TimeSpan.Zero;

        [SetUp]
        public void ConsoleSearchSetUp()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            OriginalErr = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalCapabilities = ConsoleUi.Current;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            Gov = new GovStandIn();
            Steps.Clear();
            Pages.Clear();
            OriginalExit = AvailabilityApi.ExitProcess;
            OriginalWait = AvailabilityApi.BlockedWait;
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalOpen = CampsiteConfig.OpenHtmlFile;
            AvailabilityApi.ExitProcess = Code => Record("exit " + Code);
            AvailabilityApi.BlockedWait = Wait => Record("wait " + Wait);
            AvailabilityApi.ConsoleDownload = Url => Gov.Download(Url);
            CampsiteConfig.OpenHtmlFile = Page => Pages.Add(File.ReadAllText(Page));
            OriginalSearchOptions = ConsoleSearch.RunOptions;
            RequestTimeout = TimeSpan.FromSeconds(30);
            RetryDelay = TimeSpan.Zero;
            ConsoleSearch.RunOptions = () => new SearchHostOptions
            {
                KeepOutputValues = true,
                Transport = (Request, Token) => Gov.Send(Request, Token),
                RequestTimeout = RequestTimeout,
                RetryDelay = RetryDelay,
                HeavyTrafficDelay = TimeSpan.Zero
            };
            string Id = Guid.NewGuid().ToString("N");
            ConfigPath = Path.Join(SearchFixture.Root, "console-config-" + Id);
            CachePath = Path.Join(SearchFixture.Root, "console-cache-" + Id);
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            foreach (string File in Directory.GetFiles(SearchFixture.CachePath))
            {
                System.IO.File.Copy(File, Path.Join(CachePath, Path.GetFileName(File)));
            }
            DateTime Ahead = DateTime.Now.AddMonths(2);
            MonthA = new DateTime(Ahead.Year, Ahead.Month, 1);
            MonthB = MonthA.AddMonths(1);
            MonthC = MonthA.AddMonths(2);
        }

        [TearDown]
        public void ConsoleSearchTearDown()
        {
            Console.SetOut(OriginalOut!);
            Console.SetError(OriginalErr!);
            CultureInfo.CurrentCulture = OriginalCulture!;
            ConsoleUi.Current = OriginalCapabilities!;
            AvailabilityApi.ExitProcess = OriginalExit!;
            AvailabilityApi.BlockedWait = OriginalWait!;
            AvailabilityApi.ConsoleDownload = OriginalDownload!;
            CampsiteConfig.OpenHtmlFile = OriginalOpen!;
            ConsoleSearch.RunOptions = OriginalSearchOptions!;
            Out.Dispose();
            Err.Dispose();
            foreach (string Folder in new[] { ConfigPath, CachePath })
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, true);
                }
            }
        }

        private void Record(string Step)
        {
            lock (Steps)
            {
                Steps.Add(Step);
            }
        }

        protected CampertronInternalConfig Settings(OutputType Output) => new CampertronInternalConfig
        {
            ConfigPath = ConfigPath,
            CachePath = CachePath,
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            EmailConfig = new EmailConfig(),
            ContainerMode = false
        };

        protected string Url(string Campground, DateTime Month) => AvailabilityApi.MonthUrl(Campground, Month);

        // a search file written as a user would (as SearchTestBase.WriteSearch)
        protected string WriteSearch(string FileName, string Campground, DateTime First, DateTime Last, int Consecutive = 1, bool AutoRun = true,
            string? DisplayName = null, string? Extra = null)
        {
            var Text = new StringBuilder();
            Text.Append("displayName: ").Append(DisplayName ?? Path.GetFileNameWithoutExtension(FileName)).Append('\n');
            Text.Append("autoRun: ").Append(AutoRun ? "true" : "false").Append('\n');
            Text.Append("campgroundID: '").Append(Campground).Append("'\n");
            Text.Append("totalHumans: 2\nsearchBy: StartEndDate\nsearchValue: 0\nsearchValueDates:\n");
            Text.Append("- ").Append(First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            Text.Append("- ").Append(Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            Text.Append("showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n");
            Text.Append("consecutiveDays: ").Append(Consecutive).Append('\n');
            Text.Append(Extra ?? string.Empty);
            File.WriteAllText(Path.Join(ConfigPath, FileName), Text.ToString());
            return FileName;
        }

        // what one run wrote: standard output and standard error, then both emptied for the next run
        protected (string Out, string Err) Take()
        {
            string Written = Out.ToString().Replace("\r\n", "\n");
            string Errors = Err.ToString().Replace("\r\n", "\n");
            Out.GetStringBuilder().Clear();
            Err.GetStringBuilder().Clear();
            return (Written, Errors);
        }

        // what a run's output means, without what differs between two runs of the same search: a search's time in its
        // "Checked" line, the order of the lines its parallel searches write as they end, and the results page's file name
        protected static string Normalized(string Text)
        {
            string Timed = Regex.Replace(Text, @" \(\d+\.\ds\)$", " (…s)", RegexOptions.Multiline);
            string Paged = Regex.Replace(Timed, @"^(Results page \(opened in your browser\): ).*$", "$1…", RegexOptions.Multiline);
            List<string> Lines = Paged.Split('\n').ToList();
            var Sorted = new List<string>();
            for (int i = 0; i < Lines.Count;)
            {
                if (IsEndOfASearch(Lines[i]))
                {
                    int End = i;
                    while (End < Lines.Count && IsEndOfASearch(Lines[End]))
                    {
                        End++;
                    }
                    Sorted.AddRange(Lines.GetRange(i, End - i).OrderBy(L => L, StringComparer.Ordinal));
                    i = End;
                }
                else
                {
                    Sorted.Add(Lines[i]);
                    i++;
                }
            }
            return string.Join("\n", Sorted);
        }

        private static bool IsEndOfASearch(string Line) => Line.StartsWith("Checked \"", StringComparison.Ordinal) || Line.StartsWith("Skipped \"", StringComparison.Ordinal);

        // the requests a run made, as a multiset of URLs in a fixed order
        internal List<string> RequestedUrls() => Gov.Requests.Select(R => R.Url).OrderBy(U => U, StringComparer.Ordinal).ToList();

        // every file and folder under a folder with its content hash: a fingerprint (SearchTestBase's)
        protected static List<string> Fingerprint(string Folder) => SearchTestBase.Fingerprint(Folder);
    }
}
