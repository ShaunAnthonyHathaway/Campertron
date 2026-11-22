using System.Globalization;
using System.Net.Mail;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // Email and ntfy in one check (decision D67, business-rules §28): the searches run once; their streams go to every enabled
    // provider (email: one per search, as before; ntfy: one per search with an opening); a provider that fails is not tried
    // again in that check while the other goes on; a search's openings are recorded in Camp.History once any provider
    // delivered them (the maintainer's decision of 2026-11-22), so the next check, also after a restart, sends nothing again;
    // when every provider fails nothing is recorded. Failures are thrown after the check: one as it is, both together.
    // The fixture is EmailDeliveryHistoryTests': real searches over months held in memory (no network), Load.ReportResults,
    // the email send and the ntfy publish replaced by recorders. Every topic is a canary searched for in the output.
    public class NotificationDeliveryTests
    {
        private Action<EmailConfig, MailMessage> OriginalSend = null!;
        private Action<NtfyConfig, NtfyMessage> OriginalPublish = null!;
        private TextWriter? OriginalOut;
        private CultureInfo? OriginalCulture;
        private StringWriter Captured = null!;
        private readonly List<MailMessage> Sent = new();
        private string Root = string.Empty;
        private string ConfigPath => Path.Join(Root, "config");
        private string HistoryPath => Path.Join(ConfigPath, "Camp.History");
        private string Topic = string.Empty;

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
                FixtureRoot = Path.Join(Path.GetTempPath(), "CampertronNotificationDeliveryFixture-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(FixtureCache);
                DateTime Ahead = DateTime.Now.AddMonths(2);
                MonthStart = new DateTime(Ahead.Year, Ahead.Month, 1);
                foreach (string Campground in new[] { Watchman, Mather })
                {
                    Cache.PreCheckCache(Campground, FixtureCache, Installed.ConfigPath!);
                    var Header = new List<ConsoleConfig.ConsoleConfigValue>();
                    var Probe = new CampertronInternalConfig { GeneralConfig = new GeneralConfig { OutputTo = OutputType.UnitTest }, CachePath = FixtureCache, ConfigPath = FixtureRoot };
                    SiteIds[Campground] = Cache.CheckCache(Search("ids", Campground, 1), ref Header, Probe).Select(p => p.CampsiteID!).Distinct().Take(3).ToList();
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

        [SetUp]
        public void Setup()
        {
            OriginalSend = CampsiteConfig.SendResultsEmail;
            OriginalPublish = Notifications.PublishNtfy;
            OriginalCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            OriginalOut = Console.Out;
            Captured = new StringWriter();
            Console.SetOut(Captured);
            Sent.Clear();
            Root = Path.Join(Path.GetTempPath(), "CampertronNotificationDeliveryTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ConfigPath);
            Topic = "cnryTopic" + Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        [TearDown]
        public void TearDown()
        {
            CampsiteConfig.SendResultsEmail = OriginalSend;
            Notifications.PublishNtfy = OriginalPublish;
            Console.SetOut(OriginalOut!);
            Captured.Dispose();
            CultureInfo.CurrentCulture = OriginalCulture!;
            foreach (MailMessage Message in Sent)
            {
                Message.Dispose();
            }
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

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

        private const string A = "A Watchman single nights", B = "B Mather two nights", C = "C Mather consecutiveDays 0";

        private string Marker(string Campground, int Site, int Day)
        {
            DateTime Date = MonthStart.AddDays(Day - 1);
            return $"Camp.History/{Date.Year}/{Date.Month}/{Date.Day}/{SiteIds[Campground][Site]}";
        }

        private List<string> AMarkers() => new() { Marker(Watchman, 0, 3), Marker(Watchman, 0, 4), Marker(Watchman, 1, 10) };
        private List<string> BMarkers() => new() { Marker(Mather, 0, 10), Marker(Mather, 0, 11) };
        private List<string> RemainderMarkers() => new() { Marker(Mather, 1, 12) };

        private List<string> MarkerDirectories() => !Directory.Exists(HistoryPath) ? new List<string>()
            : Directory.GetFileSystemEntries(HistoryPath, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(ConfigPath, p).Replace('\\', '/')).Where(p => p.Count(c => c == '/') == 4).OrderBy(p => p, StringComparer.Ordinal).ToList();

        private CampertronInternalConfig Config(OutputType Output, bool Ntfy) => new CampertronInternalConfig
        {
            GeneralConfig = new GeneralConfig { OutputTo = Output },
            EmailConfig = new EmailConfig
            {
                SmtpServer = "localhost", SmtpPort = 25, SmtpUsername = "tester", SmtpPassword = "not-a-real-password",
                SendFromAddress = "campertron@example.invalid", SendToAddressList = new List<string> { "someone@example.invalid" }
            },
            NtfyConfig = Ntfy ? Yaml.LoadSettingsText<NtfyConfig>("enabled: true\nserver: https://ntfy.example.invalid\ntopic: " + Topic + "\n") : null,
            CachePath = FixtureCache,
            ConfigPath = ConfigPath
        };

        private sealed class Attempts
        {
            internal readonly List<string> Emails = new();
            internal readonly List<NtfyMessage> Ntfy = new();
            internal int Searches;
        }

        // one check: the three searches with one RunningData (each searched once), then Load.ReportResults. Email and ntfy
        // succeed unless told to fail for a search
        private (Attempts Seen, Exception? Failure) Check(OutputType Output, bool Ntfy, Func<string, bool>? EmailOk = null, Func<string, bool>? NtfyOk = null)
        {
            var Seen = new Attempts();
            CampertronInternalConfig ThisConfig = Config(Output, Ntfy);
            RunningData RunData = new RunningData
            {
                AllConsoleConfigItems = new(), SiteData = new(), Urls = new(), UniqueCampgroundIds = new(), CampertronConfigFiles = new(), FilteredAvailableData = new()
            };
            foreach (var (Campground, Days) in new[] { (Watchman, WatchmanDays), (Mather, MatherDays) })
            {
                string Url = $"https://www.recreation.gov/api/camps/availability/campground/{Campground}/month?start_date={MonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture)}-01T00%3A00%3A00.000Z";
                RunData.SiteData.TryAdd(Url, Month(Campground, Days));
                RunData.Urls.TryAdd(Url, false);
            }
            foreach (CampertronConfig ThisSearch in new[] { Search(C, Mather, 0), Search(A, Watchman, 1), Search(B, Mather, 2) })
            {
                var Item = new ConsoleConfig.ConsoleConfigItem { Name = ThisSearch.DisplayName };
                Item.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisSearch, ref RunData, ThisConfig);
                Seen.Searches++;
                RunData.AllConsoleConfigItems.Add(Item);
            }
            CampsiteConfig.SendResultsEmail = (Email, Message) =>
            {
                Sent.Add(Message);
                string Name = System.Text.RegularExpressions.Regex.Match(Message.Body, "<h2[^>]*>(.*?)</h2>").Groups[1].Value;
                Seen.Emails.Add(Name);
                if (EmailOk != null && !EmailOk(Name))
                {
                    throw new SmtpException("the test refused the email");
                }
            };
            Notifications.PublishNtfy = (Settings, Message) =>
            {
                Seen.Ntfy.Add(Message);
                string Name = Message.Body.Split('\n')[0];
                if (NtfyOk != null && !NtfyOk(Name))
                {
                    throw new NtfyException(NtfyFailure.ServerError, 503);
                }
            };
            try
            {
                Load.ReportResults(ThisConfig, ref RunData);
                return (Seen, null);
            }
            catch (Exception Failure)
            {
                return (Seen, Failure);
            }
        }

        private void AssertNoTopic() => Assert.That(Captured.ToString(), Does.Not.Contain(Topic));

        // ---- each provider alone, and both ----

        [Test]
        public void EmailOnly_IsAsBefore()
        {
            var (Seen, Failure) = Check(OutputType.Email, Ntfy: false);
            Assert.That(Failure, Is.Null);
            Assert.That((Seen.Searches, Seen.Emails, Seen.Ntfy.Count), Is.EqualTo((3, new List<string> { A, B, C }, 0)));
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())));
            Assert.That(Captured.ToString(), Does.Contain("8 new openings; sending 3 emails."));
            Assert.That(Captured.ToString(), Does.Not.Contain("ntfy"));
        }

        [Test]
        public void NtfyOnly_PublishesEachSearchWithOpenings_OnceAndSendsNoEmail()
        {
            var (Seen, Failure) = Check(OutputType.Ntfy, Ntfy: true);
            Assert.That(Failure, Is.Null);
            Assert.That((Seen.Searches, Seen.Emails.Count), Is.EqualTo((3, 0)), "searched once each; no email");
            Assert.That(Seen.Ntfy.Select(M => M.Body.Split('\n')[0]), Is.EqualTo(new[] { A, B }), "C shows no opening, so it has no message");
            Assert.That(Seen.Ntfy.Select(M => M.Openings), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())));
            Assert.That(Captured.ToString(), Does.Contain("8 new openings; sending 2 ntfy notifications.")
                .And.Contain("ntfy notification sent: " + A + " (3 openings)").And.Contain("ntfy notification sent: " + B + " (2 openings)"));
            AssertNoTopic();
        }

        [Test]
        public void Both_SendEachProviderOneDeliveryPerSearch_FromOneSearch()
        {
            var (Seen, Failure) = Check(OutputType.Email, Ntfy: true);
            Assert.That(Failure, Is.Null);
            Assert.That(Seen.Searches, Is.EqualTo(3), "the searches ran once for both providers");
            Assert.That(Seen.Emails, Is.EqualTo(new[] { A, B, C }));
            Assert.That(Seen.Ntfy.Select(M => M.Body.Split('\n')[0]), Is.EqualTo(new[] { A, B }));
            Assert.That(Captured.ToString(), Does.Contain("8 new openings; sending 3 emails and 2 ntfy notifications."));
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers()).Concat(RemainderMarkers())));
            AssertNoTopic();
        }

        // the next check, as after a restart (a new RunningData; only Camp.History carries over), sends nothing again
        [TestCase(OutputType.Email, true)]
        [TestCase(OutputType.Ntfy, true)]
        public void TheNextCheck_SendsNothingAgain(OutputType Output, bool Ntfy)
        {
            Assert.That(Check(Output, Ntfy).Failure, Is.Null);
            var (Again, Failure) = Check(Output, Ntfy);
            Assert.That(Failure, Is.Null);
            Assert.That((Again.Emails.Count, Again.Ntfy.Count), Is.EqualTo((0, 0)), "no duplicate");
            Assert.That(Captured.ToString(), Does.Contain("nothing new"));
        }

        // ---- one provider fails: the other is still tried; any delivery records ----

        [Test]
        public void AFailedEmail_StillPublishesNtfy_RecordsWhatNtfyDelivered_AndThrowsTheEmailsFailure()
        {
            var (Seen, Failure) = Check(OutputType.Email, Ntfy: true, EmailOk: Name => false);
            Assert.That(Failure, Is.InstanceOf<SmtpException>(), "the email's own failure, reported as before");
            Assert.That(Seen.Emails, Is.EqualTo(new[] { A }), "email is not tried again in this check");
            Assert.That(Seen.Ntfy.Select(M => M.Body.Split('\n')[0]), Is.EqualTo(new[] { A, B }), "ntfy goes on");
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers())), "any delivery records; the check's remainder is not recorded after a failure");
            Assert.That(Captured.ToString(), Does.Not.Contain("Email sent").And.Contain("ntfy notification sent: " + A));

            var (Next, NextFailure) = Check(OutputType.Email, Ntfy: true);
            Assert.That(NextFailure, Is.Null);
            Assert.That(Next.Ntfy, Is.Empty, "no ntfy duplicate of what ntfy delivered");
            Assert.That(Next.Emails, Is.EqualTo(new[] { A, B, C }), "the gate opens for the remainder; the email shows only openings not yet recorded");
        }

        [Test]
        public void AFailedNtfy_StillSendsEveryEmail_RecordsThem_AndThrowsNtfysFailure()
        {
            var (Seen, Failure) = Check(OutputType.Email, Ntfy: true, NtfyOk: Name => false);
            Assert.That(Failure, Is.InstanceOf<NtfyException>());
            Assert.That(Seen.Emails, Is.EqualTo(new[] { A, B, C }), "email goes on");
            Assert.That(Seen.Ntfy, Has.Count.EqualTo(1), "ntfy is not tried again in this check: no storm");
            Assert.That(MarkerDirectories(), Is.EquivalentTo(AMarkers().Concat(BMarkers())));
            List<string> Described = ConsoleUi.DescribeFailure(Failure!);
            Assert.That(Described[0], Is.EqualTo("the ntfy notification could not be sent: the ntfy server failed (HTTP 503)."));
            AssertNoTopic();
        }

        [Test]
        public void BothFail_NothingIsRecorded_AndBothFailuresAreReported()
        {
            var (Seen, Failure) = Check(OutputType.Email, Ntfy: true, EmailOk: Name => false, NtfyOk: Name => false);
            NotificationException Both = (NotificationException)Failure!;
            Assert.That(Both.Failures.Select(F => F.Provider), Is.EqualTo(new[] { NotificationProvider.Email, NotificationProvider.Ntfy }));
            Assert.That((Seen.Emails.Count, Seen.Ntfy.Count), Is.EqualTo((1, 1)), "each tried once, then not again in this check");
            Assert.That(MarkerDirectories(), Is.Empty);
            Assert.That(Directory.Exists(HistoryPath), Is.False);
            List<string> Described = ConsoleUi.DescribeFailure(Failure!);
            Assert.That(Described[0], Is.EqualTo("the notifications could not be sent: both the email and the ntfy notification failed."));
            Assert.That(Described, Has.Some.StartsWith("Email: the email could not be sent: ").And.Some.StartsWith("ntfy: the ntfy notification could not be sent: "));

            var (Next, _) = Check(OutputType.Email, Ntfy: true);
            Assert.That(Next.Ntfy.Select(M => M.Openings).Sum(), Is.EqualTo(5), "everything comes again (D36)");
        }

        [Test]
        public void AFailedNtfyOnly_RecordsNothing_AndTheNextCheckTriesAgain()
        {
            var (Seen, Failure) = Check(OutputType.Ntfy, Ntfy: true, NtfyOk: Name => Name != A);
            Assert.That(Failure, Is.InstanceOf<NtfyException>());
            Assert.That(Seen.Ntfy, Has.Count.EqualTo(1));
            Assert.That(MarkerDirectories(), Is.Empty);
            var (Next, NextFailure) = Check(OutputType.Ntfy, Ntfy: true);
            Assert.That(NextFailure, Is.Null);
            Assert.That(Next.Ntfy.Select(M => M.Body.Split('\n')[0]), Is.EqualTo(new[] { A, B }));
        }

        // ---- the one definition of the providers ----

        [Test]
        public void TheProviders_FollowOutputToAndNtfyYaml()
        {
            CampertronInternalConfig Of(OutputType Output, string? Ntfy) => new CampertronInternalConfig
            {
                GeneralConfig = new GeneralConfig { OutputTo = Output },
                NtfyConfig = Ntfy == null ? null : Yaml.LoadSettingsText<NtfyConfig>(Ntfy)
            };
            const string On = "enabled: true\nserver: https://ntfy.sh\ntopic: t\n", Off = "enabled: false\n";
            foreach (OutputType Output in Enum.GetValues<OutputType>())
            {
                foreach (string? Ntfy in new[] { null, Off, On })
                {
                    CampertronInternalConfig C = Of(Output, Ntfy);
                    bool Monitoring = Output == OutputType.Email || Output == OutputType.Ntfy;
                    Assert.That(Notifications.IsMonitoring(Output), Is.EqualTo(Monitoring));
                    Assert.That(Load.UsesNotificationHistory(C), Is.EqualTo(Monitoring));
                    Assert.That(Notifications.EmailEnabled(C), Is.EqualTo(Output == OutputType.Email), Output + " " + Ntfy);
                    Assert.That(Notifications.NtfyEnabled(C), Is.EqualTo(Monitoring && Ntfy == On), Output + " " + Ntfy);
                }
            }
            Assert.That(Notifications.OutputText(Of(OutputType.Email, null)), Is.EqualTo("Email"));
            Assert.That(Notifications.OutputText(Of(OutputType.Email, On)), Is.EqualTo("Email and ntfy"));
            Assert.That(Notifications.OutputText(Of(OutputType.Ntfy, On)), Is.EqualTo("Ntfy"));
            Assert.That(Notifications.ModeName(Of(OutputType.Ntfy, On)), Is.EqualTo("ntfy mode"));
            Assert.That(Notifications.ModeName(Of(OutputType.Email, On)), Is.EqualTo("email mode"));
        }

        [Test]
        public void TheCheckLine_NamesWhatIsSent()
        {
            DateTime Now = new DateTime(2026, 11, 22, 15, 4, 5);
            Assert.That(Load.CheckText(3, 2, 8, Now), Is.EqualTo("Check 3: 2 searches, 8 new openings; sending 2 emails."), "as before");
            Assert.That(Load.CheckText(3, 2, 8, Now, 2, 0), Is.EqualTo("Check 3: 2 searches, 8 new openings; sending 2 emails."));
            Assert.That(Load.CheckText(3, 2, 8, Now, 0, 1), Is.EqualTo("Check 3: 2 searches, 8 new openings; sending 1 ntfy notification."));
            Assert.That(Load.CheckText(3, 2, 8, Now, 2, 1), Is.EqualTo("Check 3: 2 searches, 8 new openings; sending 2 emails and 1 ntfy notification."));
            Assert.That(Load.CheckText(3, 2, 8, Now, 0, 0), Is.EqualTo("Check 3: 2 searches, 8 new openings; nothing to send."));
            Assert.That(Load.CheckText(3, 2, 0, Now, 2, 1), Is.EqualTo("Check 3: 2 searches, nothing new. Next check 15:05:05."));
        }
    }
}
