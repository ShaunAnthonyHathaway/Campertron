using System.Collections.Concurrent;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronUnitTests.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // The manual search's history contract (ER-03, ER-19, ER-20; D115's rule for the console's searches, v0.4.0's phase 10),
    // pinned at the shared service, before any host: a manual search (CampsiteSearch, the desktop GUI's Results tab, the web
    // UI's results page and since phase 10 the interactive console's Results) never reads, creates, changes, removes or
    // re-arms a marker of Camp.History, never takes the monitoring's lock and never sends, so the monitoring's next check
    // notifies exactly what it would have notified without it. recreation.gov is answered in memory for both (the manual
    // search through its transport seam, the monitoring through the console's download seam); nothing is sent (the send
    // seams record) and no database is opened (the campground's cache is written by hand).
    [NonParallelizable]
    public class ManualSearchHistoryTests : MonitoringTestBase
    {
        private Action<string> OriginalRemove = null!;
        private readonly ConcurrentQueue<string> Removed = new();

        private string TwinPath => Path.Join(Root, "twin");

        [SetUp]
        public void History()
        {
            OriginalRemove = NotificationHistory.RemoveMarker;
            Removed.Clear();
            NotificationHistory.RemoveMarker = Folder =>
            {
                Removed.Enqueue(Folder);
                OriginalRemove(Folder);
            };
            //every day of every month: the campground's first campsite is free, its second reserved
            Answer = Openings;
        }

        [TearDown]
        public void RestoreHistory()
        {
            NotificationHistory.RemoveMarker = OriginalRemove;
        }

        // the manual search of the shared service, as every host runs it: its plan, then its run with recreation.gov in memory
        private SearchRunResult ManualSearch(string Folder, params string[] Files)
        {
            SearchRunPlan Plan = CampsiteSearch.Plan(new SearchRunRequest(Folder, CachePath, Files), DateTime.Now);
            return CampsiteSearch.Run(Plan, null, CancellationToken.None, new SearchHostOptions
            {
                Transport = (Request, Token) =>
                {
                    Requested.Enqueue(Request.Url);
                    return new SearchResponse(200, Answer(Request.Url));
                },
                RetryDelay = TimeSpan.Zero,
                HeavyTrafficDelay = TimeSpan.Zero
            });
        }

        // tomorrow's night (inside every search's next seven days): a marker for a campsite, as the monitoring writes it
        private static DateTime Tomorrow => DateTime.Now.Date.AddDays(1);
        private static string Marker(DateTime Night, string Site) => Path.Join("Camp.History", Night.Year.ToString(), Night.Month.ToString(), Night.Day.ToString(), Site);

        // a marker for the free campsite (the monitoring suppresses its night) and one for the reserved campsite (the
        // monitoring re-arms it, D104), in a settings folder
        private static void MarkBoth(string Folder)
        {
            Directory.CreateDirectory(Path.Join(Folder, Marker(Tomorrow, Campground + "1")));
            Directory.CreateDirectory(Path.Join(Folder, Marker(Tomorrow, Campground + "2")));
        }

        private static List<string> MarkersIn(string Folder) => !Directory.Exists(Path.Join(Folder, "Camp.History")) ? new List<string>()
            : Directory.GetFileSystemEntries(Path.Join(Folder, "Camp.History"), "*", SearchOption.AllDirectories)
                .Select(P => Path.GetRelativePath(Folder, P).Replace('\\', '/')).Where(P => P.Count(C => C == '/') == 4).OrderBy(P => P, StringComparer.Ordinal).ToList();

        [Test]
        public void AManualSearch_FindsWhatTheMarkersWouldSuppress_AndReadsCreatesChangesRemovesAndRearmsNoMarker()
        {
            WriteSearch("Trip.yaml", "Trip");
            MarkBoth(ConfigPath);
            List<string> Before = SearchTestBase.Fingerprint(ConfigPath);

            SearchRunResult Result = ManualSearch(ConfigPath, "Trip.yaml");

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            SearchResult Trip = Result.Searches.Single();
            Assert.That(Trip.Openings.Any(O => O.CampsiteId == Campground + "1" && O.Night == Tomorrow), Is.True, "a marked night is still found: no marker suppresses a manual search");
            Assert.That(Trip.Openings.Select(O => O.CampsiteId).Distinct(), Is.EqualTo(new[] { Campground + "1" }), "only the free campsite");
            Assert.That(SearchTestBase.Fingerprint(ConfigPath), Is.EqualTo(Before), "Camp.History (and every file of the settings folder) byte for byte as before");
            Assert.That(Removed, Is.Empty, "no marker was removed or re-armed, also not the reserved campsite's");
            Assert.That((Emails.Count, Published.Count), Is.EqualTo((0, 0)), "nothing was sent");
            Assert.That(File.Exists(LockPath), Is.False, "the monitoring's lock was never taken");
            AssertNothingOnTheConsole();
        }

        [Test]
        public void AManualSearch_LeavesAMissingCampHistoryMissing()
        {
            WriteSearch("Trip.yaml", "Trip");

            SearchRunResult Result = ManualSearch(ConfigPath, "Trip.yaml");

            Assert.That(Result.Status, Is.EqualTo(SearchRunStatus.Completed));
            Assert.That(Result.Searches.Single().Openings, Is.Not.Empty);
            Assert.That(Directory.Exists(HistoryPath), Is.False, "Camp.History is never created by a manual search");
            Assert.That((Removed.Count, Emails.Count, Published.Count), Is.EqualTo((0, 0, 0)));
            Assert.That(File.Exists(LockPath), Is.False);
            AssertNothingOnTheConsole();
        }

        [Test]
        public void TheNextMonitoringCheck_NotifiesAndRecordsExactlyWhatItWouldHaveWithoutTheManualSearch()
        {
            //the twin: the same search and markers, no manual search; its monitoring's check is what the other must equal
            Directory.CreateDirectory(TwinPath);
            WriteSearch("Trip.yaml", "Trip");
            File.Copy(Path.Join(ConfigPath, "Trip.yaml"), Path.Join(TwinPath, "Trip.yaml"));
            MarkBoth(ConfigPath);
            MarkBoth(TwinPath);
            Recorder Twin = NewService();
            Assert.That(Twin.Service.Start(Settings(Folder: TwinPath)).Started, Is.True);
            Twin.AwaitWaits(1);
            Twin.Service.Stop();
            List<(string?, int)> TwinSent = Twin.Of(MonitoringEventKind.EmailSent).Select(E => (E.SearchName, E.Openings)).ToList();
            List<string> TwinMarkers = MarkersIn(TwinPath);
            int TwinRemoved = Removed.Count;
            Assert.That(TwinSent, Is.EqualTo(new[] { ((string?)"Trip", TwinSent.Single().Item2) }), "the twin's check sent one email");
            Assert.That(TwinSent.Single().Item2, Is.GreaterThan(0));
            Assert.That(TwinRemoved, Is.EqualTo(1), "the twin's check re-armed the reserved campsite's marker");
            Emails.Clear();
            Removed.Clear();

            SearchRunResult Manual = ManualSearch(ConfigPath, "Trip.yaml");
            Assert.That(Manual.Searches.Single().Openings, Is.Not.Empty);
            Assert.That((Emails.Count, Removed.Count), Is.EqualTo((0, 0)), "the manual search sent and removed nothing");
            Recorder After = NewService();
            Assert.That(After.Service.Start(Settings()).Started, Is.True);
            After.AwaitWaits(1);
            After.Service.Stop();

            Assert.That(After.Of(MonitoringEventKind.EmailSent).Select(E => (E.SearchName, E.Openings)), Is.EqualTo(TwinSent),
                "the same notification as without the manual search");
            Assert.That(MarkersIn(ConfigPath), Is.EqualTo(TwinMarkers), "the same markers recorded and re-armed");
            Assert.That(Removed.Count, Is.EqualTo(TwinRemoved));
            AssertNothingOnTheConsole();
        }
    }
}
