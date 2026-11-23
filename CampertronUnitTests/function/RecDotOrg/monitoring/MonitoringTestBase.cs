using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronUnitTests.function.Base;
using CampertronUnitTests.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // What the monitoring service's tests share (v0.3.0, decisions D83 and D90). Each test has its own temporary settings
    // and cache folders; the campground's cache is written by hand as a search builds it (the completion marker last), so
    // no database is opened; every month is answered through the console's download seam (no network); an email and an
    // ntfy notification are recorded by their send seams (no SMTP server, no ntfy server); the SMTP password's and the
    // token's environment is the test's own; and the console is captured, because a service writes nothing to it.
    public abstract class MonitoringTestBase
    {
        protected const string Campground = "990701";
        protected const string OtherCampground = "990702";
        protected const string Password = "M0nitoring-S3cret";
        protected const string Topic = "monitoring-test-topic-4f1c";
        protected static readonly TimeSpan Long = TimeSpan.FromSeconds(60);

        protected string Root = string.Empty;
        protected string ConfigPath => Path.Join(Root, "config");
        protected string CachePath => Path.Join(Root, "cache");
        protected string HistoryPath => Path.Join(ConfigPath, "Camp.History");
        protected string LockPath => Path.Join(ConfigPath, MonitoringLock.FileName);
        protected StringWriter Out = null!;
        protected StringWriter Err = null!;
        private TextWriter OriginalOut = null!;
        private TextWriter OriginalError = null!;
        private ConsoleUi.Capabilities OriginalCapabilities = null!;
        private bool OriginalUnacknowledged;
        private CultureInfo OriginalCulture = null!;
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private Func<string, string> OriginalDownload = null!;
        private Func<string, string?> OriginalSmtpEnvironment = null!;
        private Func<string, string?> OriginalNtfyEnvironment = null!;
        private Action<TimeSpan> OriginalBlockedWait = null!;
        private Action<int> OriginalExit = null!;

        // what recreation.gov answers for a month's URL: by default every campsite of the campground is reserved
        protected Func<string, string> Answer = null!;
        protected readonly ConcurrentQueue<string> Requested = new();
        // the names of the searches whose email was sent (or refused by Send), in order
        protected readonly ConcurrentQueue<string> Emails = new();
        protected readonly ConcurrentQueue<string> Published = new();
        // what the send seams do with a search's email or notification after recording it; they throw to refuse it
        protected Action<string> Send = Name => { };
        protected Action<string> Publish = Name => { };
        protected readonly ConcurrentQueue<string> BlockedSteps = new();
        private protected readonly List<MonitoringService> Services = new();

        [SetUp]
        public void BaseSetUp()
        {
            Root = Path.Join(Path.GetTempPath(), "CampertronMonitoringTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Directory.CreateDirectory(CachePath);
            OriginalOut = Console.Out;
            OriginalError = Console.Error;
            Out = new StringWriter();
            Err = new StringWriter();
            Console.SetOut(TextWriter.Synchronized(Out));
            Console.SetError(TextWriter.Synchronized(Err));
            OriginalCapabilities = ConsoleUi.Current;
            OriginalUnacknowledged = ConsoleUi.Unacknowledged;
            ConsoleUi.Current = new ConsoleUi.Capabilities();
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalDownload = AvailabilityApi.ConsoleDownload;
            OriginalSmtpEnvironment = SmtpPassword.GetEnvironment;
            OriginalNtfyEnvironment = NtfyToken.GetEnvironment;
            OriginalBlockedWait = AvailabilityApi.BlockedWait;
            OriginalExit = AvailabilityApi.ExitProcess;
            Requested.Clear();
            Emails.Clear();
            Published.Clear();
            BlockedSteps.Clear();
            Services.Clear();
            Send = Name => { };
            Publish = Name => { };
            Answer = Url => Month(Url, (Site, Day) => false);
            SmtpPassword.GetEnvironment = Name => null;
            NtfyToken.GetEnvironment = Name => null;
            AvailabilityApi.BlockedWait = Wait => BlockedSteps.Enqueue("wait " + Wait);
            AvailabilityApi.ExitProcess = Code => BlockedSteps.Enqueue("exit " + Code);
            CampsiteConfig.SendResultsEmail = (Settings, Message) =>
            {
                string Name = Regex.Match(ResultsEmails.Html(Message), "<h2[^>]*>(.*?)</h2>").Groups[1].Value;
                Message.Dispose();
                Emails.Enqueue(Name);
                Send(Name);
            };
            Notifications.PublishNtfy = (Settings, Message) =>
            {
                string Name = Message.Body.Split('\n')[0];
                Published.Enqueue(Name);
                Publish(Name);
            };
            AvailabilityApi.ConsoleDownload = Url =>
            {
                Requested.Enqueue(Url);
                return Answer(Url);
            };
            CacheCampground(Campground, "FIXTURE CAMPGROUND");
            CacheCampground(OtherCampground, "OTHER CAMPGROUND");
        }

        [TearDown]
        public void BaseTearDown()
        {
            // no test leaves a monitoring behind: its thread would use the next test's seams
            foreach (MonitoringService Service in Services)
            {
                Service.Stop();
            }
            Console.SetOut(OriginalOut);
            Console.SetError(OriginalError);
            Out.Dispose();
            Err.Dispose();
            ConsoleUi.Current = OriginalCapabilities;
            ConsoleUi.Unacknowledged = OriginalUnacknowledged;
            CultureInfo.CurrentCulture = OriginalCulture;
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            AvailabilityApi.ConsoleDownload = OriginalDownload;
            SmtpPassword.GetEnvironment = OriginalSmtpEnvironment;
            NtfyToken.GetEnvironment = OriginalNtfyEnvironment;
            AvailabilityApi.BlockedWait = OriginalBlockedWait;
            AvailabilityApi.ExitProcess = OriginalExit;
            if (Directory.Exists(Root))
            {
                if (!OperatingSystem.IsWindows())
                {
                    foreach (string Folder in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Append(Root))
                    {
                        File.SetUnixFileMode(Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }
                Directory.Delete(Root, true);
            }
        }

        // ---- the fixture ----

        protected static List<string> Sites(string Id) => new() { Id + "1", Id + "2" };

        // a campground's search cache as a search builds it, with two campsites and their (empty) equipment and attributes
        protected void CacheCampground(string Id, string Name)
        {
            File.WriteAllText(Path.Join(CachePath, Id + "-CampInfo.json"), JsonSerializer.Serialize(new ReturnParkCampground { ParkName = "Fixture Park", CampsiteName = Name }));
            File.WriteAllText(Path.Join(CachePath, Id + "-Campsites.json"), JsonSerializer.Serialize(Sites(Id).Select(Site => new CampsitesRecdata
            {
                CampsiteID = Site, FacilityID = Id, CampsiteName = "Site " + Site, CampsiteType = "STANDARD NONELECTRIC", Loop = "A"
            }).ToList()));
            foreach (string Site in Sites(Id))
            {
                File.WriteAllText(Path.Join(CachePath, Site + "-Equipment.json"), "[]");
                File.WriteAllText(Path.Join(CachePath, Site + "-Attributes.json"), "[]");
            }
            File.WriteAllText(Path.Join(CachePath, Id + "-Cached.json"), "\"2026-11-21\"");
        }

        // the month a URL asks for, as recreation.gov writes it, for the URL's campground
        protected static string Month(string Url, Func<string, int, bool> Available)
        {
            Match Asked = Regex.Match(Url, @"/campground/([^/]*)/month\?start_date=(\d{4})-(\d{2})-");
            Assert.That(Asked.Success, Is.True, "a month's URL: " + Url);
            var First = new DateTime(int.Parse(Asked.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(Asked.Groups[3].Value, CultureInfo.InvariantCulture), 1);
            return SearchTestBase.MonthJson(First, Sites(Asked.Groups[1].Value), Available);
        }

        // every day of every month: the campground's first campsite is free
        protected static string Openings(string Url) => Month(Url, (Site, Day) => Site.EndsWith('1'));

        protected string WriteSearch(string FileName, string Name, bool AutoRun = true, string Id = Campground)
        {
            string FilePath = Path.Join(ConfigPath, FileName);
            File.WriteAllText(FilePath, Base.EmailStartTests.SearchText(Name, AutoRun, "campgroundID: '" + Id + "'"));
            return FilePath;
        }

        protected const string NtfyText = "enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n";

        // the settings a host loaded: email (never contacted: the send is a seam) and, with Ntfy, an enabled Ntfy.yaml,
        // which the start requires to exist in the settings folder
        protected CampertronInternalConfig Settings(OutputType Output = OutputType.Email, bool Ntfy = false, string? Folder = null, string? Cache = null)
        {
            if (Ntfy)
            {
                File.WriteAllText(Path.Join(Folder ?? ConfigPath, ConfigFiles.NtfyFileName), NtfyText);
            }
            return new CampertronInternalConfig
            {
                ConfigPath = Folder ?? ConfigPath,
                CachePath = Cache ?? CachePath,
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                EmailConfig = new EmailConfig
                {
                    SmtpServer = "smtp.example.invalid", SmtpPort = 587, SmtpUsername = "tester", SmtpPassword = Password,
                    SendFromAddress = "from@example.invalid", SendToAddressList = new List<string> { "to@example.invalid" }
                },
                NtfyConfig = Ntfy ? Yaml.LoadSettingsText<NtfyConfig>(NtfyText) : null
            };
        }

        // every marker of Camp.History, relative to the settings folder, with '/' (empty when the folder does not exist)
        protected List<string> Markers() => !Directory.Exists(HistoryPath) ? new List<string>()
            : Directory.GetFileSystemEntries(HistoryPath, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(ConfigPath, p).Replace('\\', '/')).Where(p => p.Count(c => c == '/') == 4).OrderBy(p => p, StringComparer.Ordinal).ToList();

        // true while a monitoring holds the settings folder's lock
        protected bool LockIsHeld(string? Folder = null)
        {
            using FileStream? Taken = MonitoringLock.TryAcquire(Folder ?? ConfigPath, out MonitoringLockProblem? Problem);
            return Taken == null && Problem == MonitoringLockProblem.Held;
        }

        // ---- a service with its events and its waits recorded ----

        private protected sealed class Recorder
        {
            internal readonly ConcurrentQueue<MonitoringEvent> Events = new();
            internal readonly ConcurrentQueue<TimeSpan> Delays = new();
            // released once for every wait between two checks that began
            internal readonly SemaphoreSlim Waiting = new(0);
            // how many waits return at once (the next check follows); the following wait holds until the run is stopped
            internal int PassWaits;
            private int WaitCount;
            internal MonitoringService Service = null!;

            internal void Wait(TimeSpan Delay, CancellationToken Token)
            {
                Delays.Enqueue(Delay);
                int Number = Interlocked.Increment(ref WaitCount);
                Waiting.Release();
                if (Number > PassWaits)
                {
                    Token.WaitHandle.WaitOne();
                }
            }
            internal List<MonitoringEventKind> Kinds => Events.Select(E => E.Kind).ToList();
            internal List<MonitoringEvent> Of(MonitoringEventKind Kind) => Events.Where(E => E.Kind == Kind).ToList();
            internal void AwaitWaits(int Count)
            {
                for (int i = 0; i < Count; i++)
                {
                    Assert.That(Waiting.Wait(Long), Is.True, "wait " + (i + 1) + " between two checks began");
                }
            }
        }

        // a service whose waits are the recorder's; DefaultWait leaves the service's own one-minute wait in place
        private protected Recorder NewService(int PassWaits = 0, bool DefaultWait = false, Action<string>? Blocked = null, Action<MonitoringEvent>? OnEvent = null)
        {
            var Recorded = new Recorder { PassWaits = PassWaits };
            Recorded.Service = new MonitoringService(new MonitoringOptions
            {
                Report = Event =>
                {
                    Recorded.Events.Enqueue(Event);
                    OnEvent?.Invoke(Event);
                },
                Wait = DefaultWait ? null : Recorded.Wait,
                Blocked = Blocked
            });
            Services.Add(Recorded.Service);
            return Recorded;
        }

        private protected static MonitoringStatus Ended(MonitoringService Service)
        {
            MonitoringStatus? Status = Service.WaitForEnd(Long);
            Assert.That(Status, Is.Not.Null, "the monitoring ended");
            return Status!;
        }

        protected void AssertNothingOnTheConsole()
        {
            Assert.That(Out.ToString() + Err.ToString(), Is.Empty, "a monitoring service writes nothing to the console");
        }
    }
}
