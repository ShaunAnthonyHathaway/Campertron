using System.Collections.Concurrent;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive;
using CampertronLibrary.function.Interactive.Results;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronUnitTests.function.RecDotOrg.monitoring;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Results.InteractiveResultsWording;

namespace CampertronUnitTests.function.Interactive
{
    // The interactive console's Results and the monitoring (v0.4.0's phase 10, D126; ER-03, D115's rule for the console's
    // searches): a search of the console's Results changes nothing the monitoring's next check depends on, so that check
    // notifies, records and re-arms exactly what a twin settings folder's check does without it (the shared contract's own
    // test is ManualSearchHistoryTests). recreation.gov is answered in memory for both (the console's search through the
    // shared run's transport seam, the monitoring through the console's download seam); nothing is sent (the send seams
    // record); no database is opened.
    [NonParallelizable]
    public class InteractiveResultsMonitoringTests : MonitoringTestBase
    {
        private Action<string> OriginalRemove = null!;
        private InteractiveResultsSeams OriginalSeams = null!;
        private readonly ConcurrentQueue<string> Removed = new();

        private string TwinPath => Path.Join(Root, "twin");
        private static DateTime Tomorrow => DateTime.Now.Date.AddDays(1);

        [SetUp]
        public void Seams()
        {
            OriginalRemove = NotificationHistory.RemoveMarker;
            OriginalSeams = InteractiveResults.Seams;
            Removed.Clear();
            NotificationHistory.RemoveMarker = Folder =>
            {
                Removed.Enqueue(Folder);
                OriginalRemove(Folder);
            };
            //every day of every month: the campground's first campsite is free, its second reserved
            Answer = Openings;
            InteractiveResults.Seams = new InteractiveResultsSeams
            {
                Prepared = Folder => false,
                Campgrounds = Folder => new List<CampgroundPickerEntry>(),
                Options = new SearchHostOptions
                {
                    Transport = (Request, Token) =>
                    {
                        Requested.Enqueue(Request.Url);
                        return new SearchResponse(200, Answer(Request.Url));
                    },
                    RetryDelay = TimeSpan.Zero,
                    HeavyTrafficDelay = TimeSpan.Zero
                },
                PollInterval = TimeSpan.FromMilliseconds(1),
                WhenAskedToStop = Act => new Nothing()
            };
        }

        [TearDown]
        public void RestoreSeams()
        {
            NotificationHistory.RemoveMarker = OriginalRemove;
            InteractiveResults.Seams = OriginalSeams;
        }

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }

        private static void MarkBoth(string Folder)
        {
            foreach (string Site in Sites(Campground))
            {
                Directory.CreateDirectory(Path.Join(Folder, "Camp.History", Tomorrow.Year.ToString(), Tomorrow.Month.ToString(), Tomorrow.Day.ToString(), Site));
            }
        }

        private static List<string> MarkersIn(string Folder) => !Directory.Exists(Path.Join(Folder, "Camp.History")) ? new List<string>()
            : Directory.GetFileSystemEntries(Path.Join(Folder, "Camp.History"), "*", SearchOption.AllDirectories)
                .Select(P => Path.GetRelativePath(Folder, P).Replace('\\', '/')).Where(P => P.Count(C => C == '/') == 4).OrderBy(P => P, StringComparer.Ordinal).ToList();

        private static List<(string?, int)> Sent(Recorder Run) => Run.Of(MonitoringEventKind.EmailSent).Select(E => (E.SearchName, E.Openings)).ToList();

        [Test]
        public void AfterASearchOfTheConsolesResults_TheMonitoringsNextCheck_NotifiesRecordsAndRearmsAsWithout()
        {
            Directory.CreateDirectory(TwinPath);
            WriteSearch("Trip.yaml", "Trip");
            File.Copy(Path.Join(ConfigPath, "Trip.yaml"), Path.Join(TwinPath, "Trip.yaml"));
            MarkBoth(ConfigPath);
            MarkBoth(TwinPath);
            Recorder Twin = NewService();
            Assert.That(Twin.Service.Start(Settings(Folder: TwinPath)).Started, Is.True);
            Twin.AwaitWaits(1);
            Twin.Service.Stop();
            List<(string?, int)> TwinSent = Sent(Twin);
            List<string> TwinMarkers = MarkersIn(TwinPath);
            int TwinRemoved = Removed.Count;
            Assert.That(TwinSent, Has.Count.EqualTo(1), "the twin's check sent one email");
            Assert.That(TwinRemoved, Is.EqualTo(1), "and re-armed the reserved campsite's marker");
            Emails.Clear();
            Removed.Clear();
            List<string> Before = MarkersIn(ConfigPath);

            var Terminal = new MenuTerminal(new Pick(W.Results), new Pick(RW.StartName), new Pick(W.BackToResults), new Pick("Back to the main menu"), new Pick("Exit"));
            int Code = InteractiveConsole.Run(new InteractiveSession(Terminal, typeof(InteractiveConsole).Assembly, () => DateTime.UtcNow,
                () => new CampertronInternalConfig { ConfigPath = ConfigPath, CachePath = CachePath }));

            Assert.That(Code, Is.EqualTo(0), Terminal.AllText);
            Assert.That(Terminal.AllText, Does.Contain("Finished: "), "the console's search ran");
            Assert.That(Terminal.AllText, Does.Contain("Complete: "));
            Assert.That(MarkersIn(ConfigPath), Is.EqualTo(Before), "the console's search changed no marker");
            Assert.That((Emails.Count, Published.Count, Removed.Count), Is.EqualTo((0, 0, 0)), "and sent and removed nothing");
            Assert.That(File.Exists(LockPath), Is.False, "nor took the monitoring's lock");

            Recorder After = NewService();
            Assert.That(After.Service.Start(Settings()).Started, Is.True);
            After.AwaitWaits(1);
            After.Service.Stop();

            Assert.That(Sent(After), Is.EqualTo(TwinSent), "the same notification as without the console's search");
            Assert.That(MarkersIn(ConfigPath), Is.EqualTo(TwinMarkers), "the same markers recorded and re-armed");
            Assert.That(Removed.Count, Is.EqualTo(TwinRemoved));
            AssertNothingOnTheConsole();
        }
    }
}
