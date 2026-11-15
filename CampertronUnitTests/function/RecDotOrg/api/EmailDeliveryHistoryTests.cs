using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // The Email output and its notification history (business-rules §5, §6). The SMTP send is replaced through
    // CampsiteConfig.SendResultsEmail (the production default is the SMTP client), so a test can record every message,
    // make a send succeed or fail, and look at Camp.History while a send is in progress. No email leaves the process.
    //
    // TD-14: Camp.History used to be written at the Email gate, before any email was sent, so an email that failed
    // (an unfilled Email.yaml on the first run, a refused connection) hid its openings for good. Now a search's
    // openings are recorded after its email was sent, and the check's other hits after its last email. The checks run
    // three searches through the real search with one shared RunningData (as SearchIsolationTests, months held in
    // memory, URLs claimed, no network) and then Load.ReportResults, the part of PostProcess before it clears and waits.
    public class EmailDeliveryHistoryTests
    {
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<string> OriginalOpen = null!;
        private readonly List<string> OpenedPages = new();
        private TextWriter? OriginalOut;
        private CultureInfo? OriginalCulture;
        private StringWriter Captured = null!;
        private readonly List<MailMessage> Sent = new();
        private string Root = string.Empty;
        private string CacheDirectory => Path.Join(Root, "cache");
        private string ConfigPath => Path.Join(Root, "config");
        private string HistoryPath => Path.Join(ConfigPath, "Camp.History");

        [SetUp]
        public void Setup()
        {
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalOpen = CampsiteConfig.OpenHtmlFile;
            OpenedPages.Clear();
            CampsiteConfig.OpenHtmlFile = FilePath => OpenedPages.Add(FilePath);
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Captured = new StringWriter();
            Console.SetOut(Captured);
            Sent.Clear();
            Root = Path.Join(Path.GetTempPath(), "CampertronEmailDeliveryHistoryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(ConfigPath);
        }

        [TearDown]
        public void TearDown()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            CampsiteConfig.OpenHtmlFile = OriginalOpen;
            Console.SetOut(OriginalOut!);
            Captured.Dispose();
            CultureInfo.CurrentCulture = OriginalCulture!;
            // a message keeps its attachment (the results page) open until it is disposed
            foreach (MailMessage Message in Sent)
            {
                Message.Dispose();
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        // addresses of the reserved .invalid domain and a placeholder password: nothing is sent anywhere
        private static EmailConfig TestEmail() => new EmailConfig
        {
            SmtpServer = "localhost",
            SmtpPort = 25,
            SmtpUsername = "tester",
            SmtpPassword = "not-a-real-password",
            SendFromAddress = "campertron@example.invalid",
            SendToAddressList = new List<string> { "someone@example.invalid" }
        };

        private static ConsoleConfig.ConsoleConfigItem Alpha() => OutputStreams.Search("Alpha", OutputStreams.Header("Alpha"),
            OutputStreams.Month(new DateTime(2027, 1, 1), OutputStreams.Hit("111", "A001", new DateTime(2027, 1, 3))));

        // ---- the send seam: the same message as before, reported as sent only after the send returned ----

        [Test]
        public void EmailOutput_HandsTheResultsMessageToTheSend_AndReportsItOnlyAfterwards()
        {
            string? WrittenWhileSending = null;
            CampsiteConfig.SendResultsEmail = (Email, Message) => { Sent.Add(Message); WrittenWhileSending = Captured.ToString(); };
            ConsoleConfig.ConsoleConfigItem Search = Alpha();
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            CampsiteConfig.ProcessConsoleConfig(Search.Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, TestEmail(), CacheDirectory);

            MailMessage Message = Sent.Single();
            string Page = ResultsHtml.Render(new[] { new ConsoleConfig.ConsoleConfigItem { Values = Search.Values } }, CultureInfo.CurrentCulture);
            Assert.That(Message.Subject, Is.EqualTo("Campertron Auto-Email"));
            Assert.That(Message.From!.Address, Is.EqualTo("campertron@example.invalid"));
            Assert.That(Message.To.Select(p => p.Address), Is.EqualTo(new[] { "someone@example.invalid" }));
            Assert.That(Message.IsBodyHtml, Is.True);
            Assert.That(Message.Body, Is.EqualTo(Page));
            Attachment File_ = Message.Attachments.Single();
            Assert.That(File_.ContentType.MediaType, Is.EqualTo("application/octet-stream"));
            Assert.That(File_.Name, Is.EqualTo(Path.GetFileName(Directory.GetFiles(CacheDirectory, "*.html").Single())));
            using (var Copy = new MemoryStream())
            {
                File_.ContentStream.CopyTo(Copy);
                Assert.That(Copy.ToArray(), Is.EqualTo(new System.Text.UTF8Encoding(false).GetBytes(Page)), "the attachment is the page");
            }
            Assert.That(WrittenWhileSending, Does.Not.Contain("Email sent"), "not reported before the send returned");
            Assert.That(Captured.ToString(), Does.Contain("Email sent: Alpha (1 opening)"));
        }

        [Test]
        public void EmailOutput_AFailedSend_Propagates_AndIsNotReportedAsSent()
        {
            var Refused = new SmtpException("refused by the test");
            CampsiteConfig.SendResultsEmail = (Email, Message) => { Sent.Add(Message); throw Refused; };
            ConsoleConfig.ConfigType Last = ConsoleConfig.ConfigType.WriteLine;

            var Thrown = Assert.Throws<SmtpException>(() =>
                CampsiteConfig.ProcessConsoleConfig(Alpha().Values!, ref Last, new GeneralConfig { OutputTo = OutputType.Email }, TestEmail(), CacheDirectory));

            Assert.That(Thrown, Is.SameAs(Refused), "the send's own exception, for the console's failure message");
            Assert.That(Sent, Has.Count.EqualTo(1));
            Assert.That(Captured.ToString(), Does.Not.Contain("Email sent"));
        }

        // ---- TD-14: the history is written only after delivery ----

        private const string Watchman = "232445";
        private const string Mather = "232490";
        private string FixtureRoot = string.Empty;
        private string FixtureCache => Path.Join(FixtureRoot, "cache");
        private readonly Dictionary<string, List<string>> SiteIds = new();
        private DateTime MonthStart;

        [OneTimeSetUp]
        public void FixtureSetup()
        {
            CultureInfo Original = CultureInfo.CurrentCulture;
            TextWriter OriginalWriter = Console.Out;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("en-US");
                Console.SetOut(TextWriter.Null);
                CampertronInternalConfig Installed = Load.GetConfig();
                FixtureRoot = Path.Join(Path.GetTempPath(), "CampertronEmailDeliveryFixture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(FixtureCache);
                DateTime Ahead = DateTime.Now.AddMonths(2);
                MonthStart = new DateTime(Ahead.Year, Ahead.Month, 1);
                foreach (string Campground in new[] { Watchman, Mather })
                {
                    Cache.PreCheckCache(Campground, FixtureCache, Installed.ConfigPath!);
                    var Header = new List<ConsoleConfig.ConsoleConfigValue>();
                    var Probe = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest }, CachePath = FixtureCache, ConfigPath = FixtureRoot };
                    SiteIds[Campground] = Cache.CheckCache(Search("ids", Campground, 1), ref Header, Probe).Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
                    Assert.That(SiteIds[Campground], Has.Count.EqualTo(3), $"campground {Campground} has at least three campsites");
                }
            }
            finally
            {
                Console.SetOut(OriginalWriter);
                CultureInfo.CurrentCulture = Original;
            }
        }

        [OneTimeTearDown]
        public void FixtureTearDown()
        {
            if (Directory.Exists(FixtureRoot))
            {
                Directory.Delete(FixtureRoot, true);
            }
        }

        private CampertronInternalConfig Config(OutputType Output) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            EmailConfig = TestEmail(),
            CachePath = FixtureCache,
            ConfigPath = ConfigPath
        };

        private CampertronConfig Search(string Name, string Campground, int ConsecutiveDays)
        {
            var Search = new CampertronConfig
            {
                DisplayName = Name,
                CampgroundID = Campground,
                TotalHumans = 2,
                SearchBy = SearchTypes.StartEndDate,
                SearchValueDates = new List<string> { MonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), MonthStart.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                ShowMonday = true, ShowTuesday = true, ShowWednesday = true, ShowThursday = true, ShowFriday = true, ShowSaturday = true, ShowSunday = true,
                IncludeEquipment = new(), ExcludeEquipment = new(), IncludeSites = new(), ExcludeSites = new(),
                IncludeAttributes = new(), ExcludeAttributes = new(), IncludeCampsiteType = new(), ExcludeCampsiteType = new(),
                ConsecutiveDays = ConsecutiveDays
            };
            Search.GenerateSearchData();
            return Search;
        }

        // Watchman: site 0 open on days 3 and 4, site 1 on day 10. Mather: site 0 on days 10 and 11, site 1 on day 12
        private static readonly int[][] WatchmanDays = { new[] { 3, 4 }, new[] { 10 } };
        private static readonly int[][] MatherDays = { new[] { 10, 11 }, new[] { 12 } };

        private AvailabilityEntries Month(string Campground, int[][] Days) => new AvailabilityEntries
        {
            campsites = SiteIds[Campground].Select((Id, i) => new CampsitesDataEntry
            {
                campsite_id = Id,
                min_num_people = 1,
                max_num_people = 6,
                availabilities = Enumerable.Range(1, DateTime.DaysInMonth(MonthStart.Year, MonthStart.Month))
                    .Select(Day => new availabilities { availability = i < Days.Length && Days[i].Contains(Day) ? "Available" : "Reserved" }).ToList()
            }).ToList()
        };

        // emailed in name order: A shows its three single nights, B the two-night run, C nothing (consecutiveDays 0: its
        // hits are only in the run's set, which is recorded after the last email)
        private const string A = "A Watchman single nights", B = "B Mather two nights", C = "C Mather consecutiveDays 0";

        private string Marker(string Campground, int Site, int Day)
        {
            DateTime Date = MonthStart.AddDays(Day - 1);
            return $"Camp.History/{Date.Year}/{Date.Month}/{Date.Day}/{SiteIds[Campground][Site]}";
        }

        private List<string> AMarkers() => new() { Marker(Watchman, 0, 3), Marker(Watchman, 0, 4), Marker(Watchman, 1, 10) };
        private List<string> BMarkers() => new() { Marker(Mather, 0, 10), Marker(Mather, 0, 11) };
        private List<string> RemainderMarkers() => new() { Marker(Mather, 1, 12) };

        // one check: the three searches with one RunningData, then the check's output. Returns the run's hits as markers
        // (what the history held after a check before TD-14) and the searches whose emails were attempted, in order
        private (List<string> RunSetMarkers, Exception? Failure) Check(Func<string, int, bool> Send, List<string>? Attempts = null, OutputType Output = OutputType.Email,
            int[][]? WatchmanOpen = null, int[][]? MatherOpen = null)
        {
            CampertronInternalConfig ThisConfig = Config(Output);
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            foreach (var (Campground, Days) in new[] { (Watchman, WatchmanOpen ?? WatchmanDays), (Mather, MatherOpen ?? MatherDays) })
            {
                string Url = $"https://www.recreation.gov/api/camps/availability/campground/{Campground}/month?start_date={MonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)}-01T00%3A00%3A00.000Z";
                RunData.SiteData.TryAdd(Url, Month(Campground, Days));
                RunData.Urls.TryAdd(Url, false);
            }
            foreach (CampertronConfig ThisSearch in new[] { Search(C, Mather, 0), Search(A, Watchman, 1), Search(B, Mather, 2) })
            {
                var Item = new ConsoleConfig.ConsoleConfigItem { Name = ThisSearch.DisplayName };
                Item.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, ThisConfig);
                RunData.AllConsoleConfigItems.Add(Item);
            }
            var RunSet = RunData.FilteredAvailableData.Select(p => $"Camp.History/{p.HitDate.Year}/{p.HitDate.Month}/{p.HitDate.Day}/{p.CampsiteID}").Distinct().ToList();
            int Number = 0;
            CampsiteConfig.SendResultsEmail = (Email, Message) =>
            {
                Sent.Add(Message);
                Number++;
                string Name = System.Text.RegularExpressions.Regex.Match(Message.Body, "<h2[^>]*>(.*?)</h2>").Groups[1].Value;
                Attempts?.Add(Name);
                if (!Send(Name, Number))
                {
                    throw new SmtpException($"the test refused the email of {Name}");
                }
            };
            try
            {
                if (Output == OutputType.UnitTest)
                {
                    Assert.That(Load.PostProcess(ThisConfig, ref RunData), Is.True);
                }
                else
                {
                    Load.ReportResults(ThisConfig, ref RunData);
                }
                return (RunSet, null);
            }
            catch (Exception Failure)
            {
                return (RunSet, Failure);
            }
        }

        // every directory and file under Camp.History, relative, with '/' (empty if it does not exist)
        private List<string> HistorySnapshot() => !Directory.Exists(HistoryPath) ? new List<string>()
            : Directory.GetFileSystemEntries(HistoryPath, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(ConfigPath, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToList();

        private List<string> MarkerDirectories() => HistorySnapshot().Where(p => p.Count(c => c == '/') == 4).ToList();

        [Test]
        public void AllEmailsSent_RecordsTheSameMarkersAsBefore()
        {
            var Attempts = new List<string>();
            var (RunSet, Failure) = Check((Name, Number) => true, Attempts);

            Assert.That(Failure, Is.Null);
            Assert.That(Attempts, Is.EqualTo(new[] { A, B, C }), "one email per search, in name order");
            Assert.That(RunSet, Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())), "the run's set: every search's final hits");
            // exactly what the history held after a check before TD-14 (every hit of the run's set), in the same layout:
            // empty directories Camp.History/{yyyy}/{M}/{d}/{CampsiteID}, no zero padding, no file
            Assert.That(MarkerDirectories(), Is.EquivalentTo(RunSet));
            // besides the markers only their year, month and day folders
            var Parents = RunSet.SelectMany(Marker => new[] { 2, 3, 4 }.Select(Parts => string.Join('/', Marker.Split('/').Take(Parts)))).Distinct();
            Assert.That(HistorySnapshot().Where(p => p.Count(c => c == '/') < 4), Is.EquivalentTo(Parents));
            Assert.That(Directory.GetFiles(HistoryPath, "*", SearchOption.AllDirectories), Is.Empty);
            foreach (string Marker in RunSet)
            {
                Assert.That(Directory.GetFileSystemEntries(Path.Join(ConfigPath, Marker)), Is.Empty, Marker + " is empty");
            }
            Assert.That(Captured.ToString(), Does.Contain("Check ").And.Contain("8 new openings; sending 3 emails."), "every hit of the run's set is counted, as before");
            Assert.That(Captured.ToString(), Does.Contain("Email sent: " + A + " (3 openings)").And.Contain("Email sent: " + B + " (2 openings)").And.Contain("Email sent: " + C + " (0 openings)"));
            Assert.That(Captured.ToString(), Does.Contain("Next check "));
        }

        [Test]
        public void FirstEmailFails_RecordsNothing_AndTheFailurePropagates()
        {
            var Attempts = new List<string>();
            var (RunSet, Failure) = Check((Name, Number) => false, Attempts);

            Assert.That(Failure, Is.InstanceOf<SmtpException>(), "the send's own exception, for the console's failure message");
            Assert.That(Attempts, Is.EqualTo(new[] { A }), "nothing is sent after a failure");
            Assert.That(MarkerDirectories(), Is.Empty, "no opening is recorded");
            Assert.That(Directory.Exists(HistoryPath), Is.False, "not even the Camp.History folder (TD-43)");
            Assert.That(Captured.ToString(), Does.Not.Contain("Email sent"));
            Assert.That(Captured.ToString(), Does.Not.Contain("Next check "));
        }

        [Test]
        public void LaterEmailFails_KeepsTheMarkersOfTheEmailsSentBeforeIt()
        {
            var (_, FailsAtB) = Check((Name, Number) => Name != B);
            Assert.That(FailsAtB, Is.InstanceOf<SmtpException>());
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers()), "A was sent; B failed; the run's other hits wait for a complete check");

            Directory.Delete(HistoryPath, true);
            var (_, FailsAtC) = Check((Name, Number) => Name != C);
            Assert.That(FailsAtC, Is.InstanceOf<SmtpException>());
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers())), "A and B were sent; C failed, so C's hits are not recorded");
        }

        [Test]
        public void NextCheck_ReportsWhatWasNotSent_AndRecordsItOnceSent()
        {
            Check((Name, Number) => Name != B);
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers()));

            // the next check (a new run, as after a restart): A's openings stay reported, B's and C's come again
            int SentBefore = Sent.Count;
            var Attempts = new List<string>();
            var (RunSet, Failure) = Check((Name, Number) => true, Attempts);
            Assert.That(Failure, Is.Null);
            Assert.That(Attempts, Is.EqualTo(new[] { A, B, C }));
            Assert.That(RunSet, Is.EquivalentTo(BMarkers().Concat(RemainderMarkers())), "A's markers suppress its openings; B's and C's are new again");
            MailMessage ToB = Sent.Skip(SentBefore).Single(p => p.Body.Contains(">" + B + "</h2>"));
            Assert.That(ToB.Body, Does.Contain($"datetime=\"{MonthStart.AddDays(9):yyyy-MM-dd}\"").And.Contain(SiteIds[Mather][0]), "B's email shows its run again");
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())));

            // and once everything was sent, nothing is new any more
            var (Again, _) = Check((Name, Number) => true);
            Assert.That(Again, Is.Empty);
        }

        [Test]
        public void WhileAnEmailIsSent_ItsOpeningsAreNotRecordedYet()
        {
            var Seen = new Dictionary<string, List<string>>();
            Check((Name, Number) =>
            {
                Seen[Name] = MarkerDirectories();
                return true;
            });

            Assert.That(Seen[A], Is.Empty, "nothing is recorded before the first email went out");
            Assert.That(Seen[B], Is.EquivalentTo(AMarkers()), "only what the emails before it showed");
            Assert.That(Seen[C], Is.EquivalentTo(AMarkers().Concat(BMarkers())));
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())), "the rest after the last email");
        }

        [Test]
        public void ExistingHistory_KeepsItsMarkersAndFiles()
        {
            string Old = Path.Join(HistoryPath, "2024", "7", "4", "999999");
            Directory.CreateDirectory(Old);
            File.WriteAllText(Path.Join(Old, "note.txt"), "x");
            Directory.CreateDirectory(Path.Join(ConfigPath, Marker(Watchman, 0, 3)));

            var (RunSet, Failure) = Check((Name, Number) => true);

            Assert.That(Failure, Is.Null);
            Assert.That(RunSet, Does.Not.Contain(Marker(Watchman, 0, 3)), "an existing marker suppresses its opening");
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers()).Append("Camp.History/2024/7/4/999999")));
            Assert.That(File.ReadAllText(Path.Join(Old, "note.txt")), Is.EqualTo("x"));
        }

        // ---- TD-43: Camp.History appears with its first marker, never empty ----

        [TestCase(OutputType.Console)]
        [TestCase(OutputType.HtmlFile)]
        [TestCase(OutputType.UnitTest)]
        public void ManualAndTestRuns_CreateNoHistoryFolder(OutputType Output)
        {
            var (RunSet, Failure) = Check((Name, Number) => true, null, Output);

            Assert.That(Failure, Is.Null);
            Assert.That(RunSet, Is.Not.Empty, "the searches found openings");
            Assert.That(Sent, Is.Empty, "no email");
            Assert.That(Directory.Exists(HistoryPath), Is.False);
            Assert.That(Directory.GetFileSystemEntries(ConfigPath), Is.Empty, "nothing in the settings folder");
            Assert.That(OpenedPages, Has.Count.EqualTo(Output == OutputType.HtmlFile ? 1 : 0));
        }

        [Test]
        public void EmailCheckWithNothingNew_CreatesNoHistoryFolder()
        {
            var (RunSet, Failure) = Check((Name, Number) => true, null, OutputType.Email, Array.Empty<int[]>(), Array.Empty<int[]>());

            Assert.That(Failure, Is.Null);
            Assert.That(RunSet, Is.Empty);
            Assert.That(Sent, Is.Empty, "nothing new, no email");
            Assert.That(Captured.ToString(), Does.Contain("nothing new"));
            Assert.That(Directory.Exists(HistoryPath), Is.False);
        }

        [Test]
        public void FirstSentEmail_CreatesTheHistoryFolder()
        {
            Assert.That(Directory.Exists(HistoryPath), Is.False);
            var Seen = new Dictionary<string, bool>();
            Check((Name, Number) => { Seen[Name] = Directory.Exists(HistoryPath); return true; });

            Assert.That(Seen[A], Is.False, "not before the first email went out");
            Assert.That(Seen[B], Is.True, "created with A's markers");
            Assert.That(Directory.Exists(HistoryPath), Is.True);
        }
    }
}
