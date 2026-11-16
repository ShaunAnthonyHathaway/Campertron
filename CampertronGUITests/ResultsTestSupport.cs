using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Threading;
using Campertron;
using Campertron.ViewModels;
using Campertron.Views;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronGUITests.GuiTestHelpers;

namespace CampertronGUITests
{
    // A clock the test moves (a monotonic timestamp in ticks).
    internal sealed class ManualTime : TimeProvider
    {
        private long _Now = 1_000_000_000;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _Now);
        public void Advance(TimeSpan By) => Interlocked.Add(ref _Now, By.Ticks);
    }

    // Timers the test fires: the Results tab's one-shot progress timer and its ticker.
    internal sealed class ManualTimers
    {
        internal sealed class Timer : IDisposable
        {
            public required TimeSpan Due { get; init; }
            public required Action Job { get; init; }
            public required bool Repeat { get; init; }
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        public readonly List<Timer> All = new List<Timer>();
        public IEnumerable<Timer> Live => All.Where(T => !T.Disposed);
        public IEnumerable<Timer> Tickers => All.Where(T => T.Repeat);

        public IDisposable Once(TimeSpan Due, Action Job)
        {
            var T = new Timer { Due = Due, Job = Job, Repeat = false };
            All.Add(T);
            return T;
        }

        public IDisposable Every(TimeSpan Interval, Action Job)
        {
            var T = new Timer { Due = Interval, Job = Job, Repeat = true };
            All.Add(T);
            return T;
        }

        // fires the live one-shot timers (each once) and returns how many
        public int FireOnce()
        {
            List<Timer> Due = Live.Where(T => !T.Repeat).ToList();
            foreach (Timer T in Due)
            {
                T.Dispose();
                T.Job();
            }
            return Due.Count;
        }

        // one tick of every live ticker
        public void Tick()
        {
            foreach (Timer T in Live.Where(T => T.Repeat).ToList())
            {
                T.Job();
            }
        }
    }

    // A recreation.gov the test controls: answers in memory (never the network) and records every request.
    internal sealed class TestGov
    {
        public Func<SearchRequest, CancellationToken, SearchResponse> Answer = (Request, _) => new SearchResponse(200, RunProbe.MonthJson(Request.Month, RunProbe.Campsites));
        public readonly ConcurrentQueue<SearchRequest> Requests = new ConcurrentQueue<SearchRequest>();
        public readonly ManualResetEventSlim Reached = new ManualResetEventSlim(false);

        public SearchResponse Send(SearchRequest Request, CancellationToken Token)
        {
            Requests.Enqueue(Request);
            Reached.Set();
            return Answer(Request, Token);
        }

        // a request that waits until it is canceled (the run's token or the request timeout)
        public static SearchResponse Stall(SearchRequest Request, CancellationToken Token)
        {
            Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(60));
            Token.ThrowIfCancellationRequested();
            return new SearchResponse(503, string.Empty);
        }
    }

    // A settings folder and a cache folder of the test's own: General.yaml, an Email.yaml with a sentinel password, the
    // fixture campground's campsite details and search files the test writes. A window's startup returns these folders.
    internal sealed class ResultsFolders : IDisposable
    {
        public const string Sentinel = "S3ntinel-password-GS2";
        public readonly string Root;
        public readonly string Config;
        public readonly string Cache;

        public ResultsFolders()
        {
            Root = Path.Join(GuiTestEnvironment.Root, "results-" + Guid.NewGuid().ToString("N"));
            Config = Path.Join(Root, "CampertronConfig");
            Cache = Path.Join(Root, "CampertronCache");
            Directory.CreateDirectory(Config);
            Directory.CreateDirectory(Cache);
            File.WriteAllText(In("General.yaml"), "outputTo: Email\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-01-01T00:00:00.0000000Z\nautoRefresh: false\n");
            File.WriteAllText(In("Email.yaml"), "smtpServer: smtp.example.com\nsmtpPort: 587\nsmtpUsername: someone\nsmtpPassword: " + Sentinel
                + "\nsendToAddressList:\n  - someone@example.com\nsendFromAddress: someone@example.com\n");
            RunProbe.WriteCampground(Cache, RunProbe.CampgroundId, "PROBE CAMPGROUND", "Campertron Probe Park", RunProbe.Campsites);
        }

        public string In(string Name) => Path.Join(Config, Name);

        public void Write(string Name, string Text) => File.WriteAllText(In(Name), Text);

        // a search file with every setting; dates as written (yyyy-MM-dd)
        public static string Search(string Name, bool AutoRun, string Campground = RunProbe.CampgroundId, string SearchBy = "DaysOut", int Value = 10,
            string Dates = "[]", int Consecutive = 1, string TotalHumans = "2", string IncludeSites = "[]", string IncludeAttributes = "[]")
        {
            return "# " + Name + "\n" +
                "displayName: " + Name + "\n" +
                "autoRun: " + (AutoRun ? "true" : "false") + "\n" +
                "campgroundID: '" + Campground + "'\n" +
                "totalHumans: " + TotalHumans + "\n" +
                "searchBy: " + SearchBy + "\n" +
                "searchValue: " + Value.ToString(CultureInfo.InvariantCulture) + "\n" +
                "searchValueDates: " + Dates + "\n" +
                "showMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\n" +
                "includeEquipment: []\nexcludeEquipment: []\nincludeSites: " + IncludeSites + "\nexcludeSites: []\nincludeAttributes: " + IncludeAttributes + "\nexcludeAttributes: []\n" +
                "includeCampsiteType: []\nexcludeCampsiteType: []\n" +
                "consecutiveDays: " + Consecutive.ToString(CultureInfo.InvariantCulture) + "\n";
        }

        public static string Date(int DaysAhead) => DateTime.Today.AddDays(DaysAhead).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // the real window, started with these folders and the given Results seams; the fixture database for the lookups
        public (MainWindow Window, MainWindowViewModel ViewModel) ShowWindow(ResultsSeams Seams)
        {
            File.Copy(GuiTestEnvironment.DbFile, In("RecreationDotOrg.db"), overwrite: true);
            var ViewModel = new MainWindowViewModel(() => new CampertronInternalConfig { ConfigPath = Config, CachePath = Cache }, Seams);
            var (Window, _) = ShowMainWindow(ViewModel);
            WaitForState(ViewModel, StartupState.Ready);
            return (Window, ViewModel);
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }

    internal static class ResultsTestHelpers
    {
        public static readonly CampgroundCatalog NoCampgrounds = new CampgroundCatalog(() => new List<CampgroundPickerEntry>());

        // seams that answer through Gov, with timers the test fires; the clock is the system's unless given
        public static ResultsSeams Seams(TestGov Gov, ManualTimers Timers, TimeProvider? Time = null, TimeSpan? RetryDelay = null,
            Func<SearchRunPlan, IProgress<SearchProgress>?, CancellationToken, SearchHostOptions?, SearchRunResult>? Run = null)
        {
            var Options = new SearchHostOptions { Transport = Gov.Send, RetryDelay = RetryDelay ?? TimeSpan.FromMilliseconds(20), HeavyTrafficDelay = TimeSpan.FromMilliseconds(5) };
            return Run == null
                ? new ResultsSeams { Options = Options, Delay = Timers.Once, Ticker = Timers.Every, Time = Time ?? TimeProvider.System }
                : new ResultsSeams { Options = Options, Delay = Timers.Once, Ticker = Timers.Every, Time = Time ?? TimeProvider.System, Run = Run };
        }

        public static ResultsViewModel Results(ResultsFolders Folders, ResultsSeams Seams, Func<string, bool>? Unsaved = null)
        {
            var ViewModel = new ResultsViewModel(Folders.Config, Folders.Cache, NoCampgrounds, Unsaved ?? (_ => false), Seams);
            ViewModel.Show();
            return ViewModel;
        }

        public static ResultsSearchRow Row(ResultsViewModel ViewModel, string FileName) =>
            ViewModel.Searches.SingleOrDefault(R => R.FileName == FileName) ?? throw new AssertionException(FileName + " is not listed");

        // the UI thread runs until the run has ended (its result shown)
        public static void WaitForEnd(ResultsViewModel ViewModel, int Milliseconds = 15000)
        {
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (ViewModel.IsRunning && Clock.ElapsedMilliseconds < Milliseconds)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.That(ViewModel.IsRunning, Is.False, "the run did not end");
        }

        public static void Start(ResultsViewModel ViewModel)
        {
            Assert.That(ViewModel.StartCommand.CanExecute(null), Is.True, "Start is disabled: " + ViewModel.StatusText);
            ViewModel.StartCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
        }

        public static SearchRunResult Run(ResultsViewModel ViewModel)
        {
            Start(ViewModel);
            WaitForEnd(ViewModel);
            return ViewModel.LastResult ?? throw new AssertionException("no result: " + ViewModel.StatusText);
        }

        // a SearchResult made by the test (the presentation of GS1's model, without a run)
        public static SearchResult Result(string FileName, SearchState State, IEnumerable<SearchOpening> Openings, int Consecutive = 1,
            IEnumerable<SearchMonthResult>? Months = null, IEnumerable<SearchIssue>? Issues = null, string? Campground = "PROBE CAMPGROUND", string? Park = "Campertron Probe Park")
        {
            List<SearchOpening> All = Openings.ToList();
            return new SearchResult(FileName, Path.GetFileNameWithoutExtension(FileName), true, RunProbe.CampgroundId, Campground, Park, Consecutive, State, 1, 1,
                SearchLists.Of(Months ?? new[] { new SearchMonthResult(new DateTime(2027, 7, 1), true, null) }), SearchLists.Of(All), CampsiteSearch.SitesOf(All),
                SearchLists.Of(Issues ?? Array.Empty<SearchIssue>()), SearchLists.Empty<ConfigDiagnostic>());
        }

        public static SearchOpening Opening(string Id, DateTime Night, string? Name = "001", string? Loop = "A", string? Type = "STANDARD NONELECTRIC",
            string[]? Equipment = null, string? Checkin = "2:00 PM", string? Checkout = "12:00 PM", int? Min = 1, int? Max = 6, string? Url = null) =>
            new SearchOpening(Id, Name, Loop, Type, SearchLists.Of(Equipment ?? new[] { "Tent" }), Night, Checkin, Checkout, Min, Max, Url ?? CampsiteSearch.BookingUrl(Id));

        public static SearchRunResult RunResult(SearchRunStatus Status, params SearchResult[] Searches) =>
            new SearchRunResult(Status, DateTime.UtcNow, TimeSpan.FromSeconds(1), 1, 1, 0, SearchLists.Of(Searches), SearchLists.Empty<SearchIssue>());
    }
}
