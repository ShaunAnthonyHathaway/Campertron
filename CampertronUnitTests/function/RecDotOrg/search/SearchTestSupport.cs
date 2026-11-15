using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // The campground cache the search tests share: Watchman's and Mather's campsites, prepared once from the bundled
    // database of the isolated test run (as SearchIsolationTests), so a Run finds its campsite details ready and never opens
    // a database. recreation.gov is never reached: every month comes from FakeGov through the transport seam.
    [SetUpFixture]
    public class SearchFixture
    {
        internal const string Watchman = "232445";
        internal const string Mather = "232490";
        internal static string Root = string.Empty;
        internal static string CachePath => Path.Join(Root, "cache");
        internal static string InstalledConfigPath = string.Empty;
        internal static readonly Dictionary<string, List<CampsitesRecdata>> Sites = new();

        [OneTimeSetUp]
        public void Prepare()
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            TextWriter Out = Console.Out;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Console.SetOut(TextWriter.Null);
                InstalledConfigPath = Load.GetConfig().ConfigPath!;
                Root = Path.Join(Path.GetTempPath(), "CampertronSearchTests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(CachePath);
                foreach (string Campground in new[] { Watchman, Mather })
                {
                    Cache.PreCheckCache(Campground, CachePath, InstalledConfigPath);
                    Sites[Campground] = JsonSerializer.Deserialize<List<CampsitesRecdata>>(File.ReadAllText(Path.Join(CachePath, Campground + "-Campsites.json")))!;
                    Assert.That(Sites[Campground], Has.Count.GreaterThan(50), $"campground {Campground} has its campsites");
                }
            }
            finally
            {
                Console.SetOut(Out);
                CultureInfo.CurrentCulture = Original;
            }
        }

        [OneTimeTearDown]
        public void Remove()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        internal static List<string> SiteIds(string Campground, int Count = int.MaxValue) =>
            Sites[Campground].Select(S => S.CampsiteID!).Distinct().Take(Count).ToList();
    }

    // recreation.gov in memory: an answer per URL (by default a 404), every request recorded in order
    internal sealed class FakeGov
    {
        private readonly ConcurrentDictionary<string, Func<SearchRequest, CancellationToken, SearchResponse>> Routes = new(StringComparer.Ordinal);
        internal readonly ConcurrentQueue<SearchRequest> Requests = new();

        internal void Answer(string Url, Func<SearchRequest, CancellationToken, SearchResponse> Handler) => Routes[Url] = Handler;
        internal void Json(string Url, string Body) => Answer(Url, (Request, Token) => new SearchResponse(200, Body));

        internal SearchResponse Send(SearchRequest Request, CancellationToken Token)
        {
            Requests.Enqueue(Request);
            return Routes.TryGetValue(Request.Url, out var Handler) ? Handler(Request, Token) : new SearchResponse(404, "{\"error\":\"not found\"}");
        }

        internal int Count(string Url) => Requests.Count(R => R.Url == Url);
    }

    // a synchronous progress recorder (Progress<T> would post to the thread pool)
    internal sealed class ProgressLog : IProgress<SearchProgress>
    {
        internal readonly List<SearchProgress> Snapshots = new();
        private readonly Action<SearchProgress>? OnReport;
        internal ProgressLog(Action<SearchProgress>? OnReport = null)
        {
            this.OnReport = OnReport;
        }
        public void Report(SearchProgress Value)
        {
            lock (Snapshots)
            {
                Snapshots.Add(Value);
            }
            OnReport?.Invoke(Value);
        }
    }

    // Each test: its own settings folder, en-US, the console captured (an interactive search writes nothing), the
    // blocked path's wait and exit recorded (an interactive search never takes them), months two and more ahead
    public abstract class SearchTestBase
    {
        protected string ConfigPath = string.Empty;
        protected DateTime MonthA;
        protected DateTime MonthB;
        protected DateTime MonthC;
        internal FakeGov Gov = null!;
        protected StringWriter Out = null!;
        protected StringWriter Err = null!;
        protected readonly List<int> ExitCalls = new();
        private CultureInfo? OriginalCulture;
        private TextWriter? OriginalOut;
        private TextWriter? OriginalErr;
        private Action<int>? OriginalExit;
        private Action<TimeSpan>? OriginalWait;

        [SetUp]
        public void BaseSetUp()
        {
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            OriginalErr = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(Out);
            Console.SetError(Err);
            OriginalExit = AvailabilityApi.ExitProcess;
            OriginalWait = AvailabilityApi.BlockedWait;
            ExitCalls.Clear();
            AvailabilityApi.ExitProcess = Code =>
            {
                lock (ExitCalls)
                {
                    ExitCalls.Add(Code);
                }
            };
            AvailabilityApi.BlockedWait = Wait => { };
            ConfigPath = Path.Join(SearchFixture.Root, "config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            DateTime Ahead = DateTime.Now.AddMonths(2);
            MonthA = new DateTime(Ahead.Year, Ahead.Month, 1);
            MonthB = MonthA.AddMonths(1);
            MonthC = MonthA.AddMonths(2);
            Gov = new FakeGov();
        }

        [TearDown]
        public void BaseTearDown()
        {
            Console.SetOut(OriginalOut!);
            Console.SetError(OriginalErr!);
            CultureInfo.CurrentCulture = OriginalCulture!;
            AvailabilityApi.ExitProcess = OriginalExit!;
            AvailabilityApi.BlockedWait = OriginalWait!;
            try
            {
                Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "nothing was written to the console");
                Assert.That(ExitCalls, Is.Empty, "the process exit was never called");
            }
            finally
            {
                Out.Dispose();
                Err.Dispose();
                if (Directory.Exists(ConfigPath))
                {
                    MakeReadable(ConfigPath);
                    Directory.Delete(ConfigPath, true);
                }
            }
        }

        private static void MakeReadable(string Folder)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            foreach (string File in Directory.GetFiles(Folder, "*", SearchOption.AllDirectories))
            {
                //never through a link (it would change its target)
                if (new FileInfo(File).LinkTarget == null)
                {
                    System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }
        }

        protected static string Url(string Campground, DateTime Month) => AvailabilityApi.MonthUrl(Campground, Month);

        // a search file written as a user would: StartEndDate from First to Last, every weekday unless Extra says
        // otherwise (Extra lines replace nothing; they are added at the end)
        protected string WriteSearch(string FileName, string Campground, DateTime First, DateTime Last, int Consecutive = 1, bool AutoRun = true,
            string? Extra = null, string? DisplayName = null, string Weekdays = "MTWTFSS")
        {
            string Day(int i) => Weekdays[i] == '-' ? "false" : "true";
            var Text = new StringBuilder();
            Text.Append("# a search of the tests\n");
            Text.Append("displayName: ").Append(DisplayName ?? Path.GetFileNameWithoutExtension(FileName)).Append('\n');
            Text.Append("autoRun: ").Append(AutoRun ? "true" : "false").Append('\n');
            Text.Append("campgroundID: '").Append(Campground).Append("'\n");
            Text.Append("totalHumans: 2\nsearchBy: StartEndDate\nsearchValue: 0\nsearchValueDates:\n");
            Text.Append("- ").Append(First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            Text.Append("- ").Append(Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            Text.Append("showMonday: ").Append(Day(0)).Append("\nshowTuesday: ").Append(Day(1)).Append("\nshowWednesday: ").Append(Day(2))
                .Append("\nshowThursday: ").Append(Day(3)).Append("\nshowFriday: ").Append(Day(4)).Append("\nshowSaturday: ").Append(Day(5))
                .Append("\nshowSunday: ").Append(Day(6)).Append('\n');
            Text.Append("consecutiveDays: ").Append(Consecutive).Append('\n');
            Text.Append(Extra ?? string.Empty);
            File.WriteAllText(Path.Join(ConfigPath, FileName), Text.ToString());
            return FileName;
        }

        // a month as recreation.gov writes it: each campsite with a status for every day of the month
        internal static string MonthJson(DateTime Month, IEnumerable<string> Sites, Func<string, int, bool> Available, int Min = 1, int Max = 8, IEnumerable<string>? Repeat = null)
        {
            int Days = DateTime.DaysInMonth(Month.Year, Month.Month);
            var Json = new StringBuilder("{\"campsites\":{");
            int Count = 0;
            void Site(string Key, string Id)
            {
                if (Count++ > 0)
                {
                    Json.Append(',');
                }
                Json.Append('"').Append(Key).Append("\":{\"availabilities\":{");
                for (int Day = 1; Day <= Days; Day++)
                {
                    Json.Append(Day > 1 ? "," : "").Append('"').Append(Month.AddDays(Day - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                        .Append("T00:00:00Z\":\"").Append(Available(Id, Day) ? "Available" : "Reserved").Append('"');
                }
                Json.Append("},\"campsite_id\":\"").Append(Id).Append("\",\"campsite_type\":\"STANDARD NONELECTRIC\",\"loop\":\"A\",\"max_num_people\":")
                    .Append(Max).Append(",\"min_num_people\":").Append(Min).Append(",\"site\":\"").Append(Id).Append("\"}");
            }
            foreach (string Id in Sites)
            {
                Site(Id, Id);
            }
            //the same campsite again under another key (the duplicate-identity case)
            foreach (string Id in Repeat ?? Array.Empty<string>())
            {
                Site(Id + "-again", Id);
            }
            Json.Append("},\"count\":").Append(Count).Append('}');
            return Json.ToString();
        }

        internal SearchRunPlan PlanOf(params string[] Files) =>
            CampsiteSearch.Plan(new SearchRunRequest(ConfigPath, SearchFixture.CachePath, Files), DateTime.Now);

        internal SearchHostOptions Options(TimeSpan? RetryDelay = null, TimeSpan? Timeout = null, int MaxBytes = 32 * 1024 * 1024) => new SearchHostOptions
        {
            Transport = Gov.Send,
            RetryDelay = RetryDelay ?? TimeSpan.Zero,
            HeavyTrafficDelay = TimeSpan.Zero,
            RequestTimeout = Timeout ?? TimeSpan.FromSeconds(30),
            MaxResponseBytes = MaxBytes
        };

        // Run on its own thread, as a host would; a run that does not return in time fails the test instead of hanging it
        internal SearchRunResult RunOf(SearchRunPlan Plan, SearchHostOptions? With = null, CancellationToken Token = default, IProgress<SearchProgress>? Progress = null)
        {
            Task<SearchRunResult> Running = Task.Run(() => CampsiteSearch.Run(Plan, Progress, Token, With ?? Options()));
            Assert.That(Running.Wait(TimeSpan.FromSeconds(120)), Is.True, "the run returned");
            return Running.Result;
        }

        // an opening as one comparable line: every value
        internal static string Key(SearchOpening O) => string.Join("|", O.CampsiteId, O.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), O.CampsiteName,
            O.Loop, O.CampsiteType, string.Join(",", O.Equipment), O.CheckinTime, O.CheckoutTime, O.MinOccupancy, O.MaxOccupancy, O.BookingUrl);

        internal static List<string> Keys(IEnumerable<SearchOpening> Openings) => Openings.Select(Key).ToList();

        // the same search through the console's path (no host): the same file, the same months put into RunData.SiteData
        // with their URLs claimed, the console's GetAvailabilitiesByCampground
        internal List<ConsoleConfig.ConsoleConfigValue> ConsoleStream(string FileName, OutputType Output, IDictionary<string, string> MonthsByUrl)
        {
            CampertronConfig Search = Yaml.LoadSettingsText<CampertronConfig>(File.ReadAllText(Path.Join(ConfigPath, FileName)))!;
            Search.RegenerateSearchData(DateTime.Now);
            RunningData RunData = EmptyRun();
            foreach (var Month in MonthsByUrl)
            {
                bool Heavy = false;
                bool Received = false;
                RunData.SiteData[Month.Key] = Data.DynamicDeserialize(Month.Value, ref Heavy, ref Received);
                RunData.Urls[Month.Key] = true;
            }
            var Config = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = Output }, ConfigPath = ConfigPath, CachePath = SearchFixture.CachePath };
            return AvailabilityApi.GetAvailabilitiesByCampground(Search, ref RunData, Config);
        }

        internal static RunningData EmptyRun() => new RunningData
        {
            AllConsoleConfigItems = new ConcurrentBag<ConsoleConfig.ConsoleConfigItem>(),
            SiteData = new ConcurrentDictionary<string, AvailabilityEntries>(),
            Urls = new ConcurrentDictionary<string, bool>(),
            UniqueCampgroundIds = new List<string>(),
            CampertronConfigFiles = new List<CampertronConfig>(),
            FilteredAvailableData = new ConcurrentBag<AvailableData>()
        };

        // every file and folder under a folder with its size, time and (for files) content hash: a fingerprint
        internal static List<string> Fingerprint(string Folder)
        {
            if (!Directory.Exists(Folder))
            {
                return new List<string> { "(none)" };
            }
            return Directory.GetFileSystemEntries(Folder, "*", SearchOption.AllDirectories).OrderBy(P => P, StringComparer.Ordinal).Select(P =>
            {
                var Info = new FileInfo(P);
                bool IsFile = File.Exists(P);
                string Hash = IsFile ? System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(P))) : "dir";
                return Path.GetRelativePath(Folder, P) + " " + (IsFile ? Info.Length : 0) + " " + File.GetLastWriteTimeUtc(P).Ticks + " " + Hash;
            }).ToList();
        }
    }
}
