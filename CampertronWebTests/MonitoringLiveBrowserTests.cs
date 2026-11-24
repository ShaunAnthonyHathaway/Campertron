using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using static CampertronWebTests.MonitoringPageTests;

namespace CampertronWebTests
{
    // The monitoring page's automatic progress (D109) in a real browser: headless Chrome runs the web UI's one script under
    // the web UI's Content-Security-Policy against a test's web UI, with the monitoring's recreation.gov, email and waits
    // the test's (MonitoringPageTests). While the monitoring goes on, the page follows it: a start that started, each check
    // that begins and finishes, a stop, a pause for the campground data's refresh and the start after it. Only the parts
    // that changed are put in place, so Start, Stop and Restart stay as they are while the checks go on (the focus on Stop
    // stays on that very button) and the scroll position stays; a routine check is never announced, a change of state once.
    // A failed answer or a page it can't get is the page's problem, said once and recovered from by itself. A stop from
    // another browser ends the updates; a session that ends stops them while the web UI's monitoring goes on.
    public class MonitoringLiveBrowserTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(1200);
        private const string LiveText = "document.getElementById('live').textContent";
        private const string LiveClass = "document.getElementById('live').className";
        private const string StatusText = "document.getElementById('status').textContent";
        private const string Summary = "(document.getElementById('check-summary') ? document.getElementById('check-summary').textContent : '')";

        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        // a web UI whose monitoring the test steers and a signed-in Chrome on its monitoring page, the monitoring started there
        private sealed class Scene : LiveScene
        {
            internal MonitoringHost M = null!;
            internal SystemPageTests.FakeRefresh? Refresh;
            protected override WebTestHost? Web => M?.Host;
            protected override ValueTask DisposeWeb() => M.DisposeAsync();

            internal static Task<Scene> Start(bool Intercept = false, int Height = 768, TimeSpan? Limit = null, bool WithRefresh = false)
            {
                Chrome.Require();
                return Build(new Scene(), async S =>
                {
                    WebRefreshSeams? Seams_ = null;
                    if (WithRefresh)
                    {
                        S.Refresh = new SystemPageTests.FakeRefresh();
                        Seams_ = new WebRefreshSeams { Run = S.Refresh.Run, LastRefreshed = Folder => CurrentConfiguration.Unknown };
                    }
                    S.M = await MonitoringHost.Start(Refresh: Seams_, More: Settings =>
                    {
                        Settings.LiveInterval = Interval;
                        Settings.LiveTimeout = Limit ?? Timeout;
                    });
                    WriteSearch(S.M, "Alpha.yaml", "First trip");
                    await S.Browse(Intercept, Height: Height);
                    await S.SignIn();
                    await S.B.Open(S.M.Host.Root + "monitoring");
                    Assert.That(await S.B.Is("!document.getElementById('live') && !document.querySelector('script')"), Is.True, "a monitoring that does not run: no script");
                    await S.B.Press("#start-button");
                    S.M.Waits.Await();
                    await S.B.Until(StatusText + ".indexOf('Running: the next check starts at ') === 0", "the started monitoring, followed by the page");
                    await S.Running();
                    await S.Watch();
                });
            }

            internal async Task NextCheck(int Number)
            {
                M.Waits.Next();
                M.Waits.Await();
                await B.Until(Summary + ".indexOf('Check " + Number + " at ') === 0 && " + StatusText + ".indexOf('Running: the next check starts at ') === 0", "check " + Number);
            }

            protected override void Releasing()
            {
                Refresh?.LetDownload.Set();
                Refresh?.LetInstall.Set();
            }
        }

        [Test]
        public async Task ThePageFollowsItsChecks_InPlace_KeepingTheFocusOnStopAndTheScroll_WithoutAnnouncingThem_AndStopEndsIt()
        {
            await using Scene S = await Scene.Start(Height: 300);
            Chrome B = S.B;
            Assert.That(await B.Text(Summary), Does.StartWith("Check 1 at "));
            Assert.That(await B.Text(LiveText), Does.Match("^This page updates by itself while the monitoring runs\\. Last update: .+\\.$"));
            Assert.That(await B.Text("document.body.innerText"), Does.Not.Contain("does not update by itself"));
            //the user is at Stop, scrolled down to it; the very button is marked. (Below it, the first check's long list of
            //openings: a later check with none makes the page shorter, which moves nothing above it)
            await B.Evaluate("var Stop = document.getElementById('stop-button'); Stop.__mark = 1; Stop.focus(); Stop.scrollIntoView({ block: 'center' })");
            double Y = await B.Number("window.scrollY");
            double Top = await B.Number("document.getElementById('stop-button').getBoundingClientRect().top");
            Assert.That(Y, Is.GreaterThan(0));
            int Views = B.Fetches(Url => Url.Contains("handler=view", StringComparison.Ordinal));

            //ordinary updates while the monitoring waits for its next check: nothing moves
            await S.Questions_(4);
            Assert.That(await B.Is("window.__kept === 1 && document.activeElement.__mark === 1"), Is.True, "no reload, the focus kept by ordinary updates");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y));
            Assert.That(B.Fetches(Url => Url.Contains("handler=view", StringComparison.Ordinal)), Is.EqualTo(Views), "nothing but the answers while nothing changed");

            //two more checks: the status and the last check change in place; Start, Stop and Restart stay as they are; and
            //the ordinary updates between them
            await S.NextCheck(2);
            await S.Questions_(4);
            await S.NextCheck(3);
            await S.Questions_(4);
            Assert.That(B.Fetches(Url => Url.Contains("handler=view", StringComparison.Ordinal)), Is.GreaterThan(Views), "the changed parts were taken from the page");
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "never a reload");
            Assert.That(await B.Is("document.activeElement.__mark === 1 && document.activeElement.id === 'stop-button'"), Is.True, "the focus stays on that very Stop button");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y), "the scroll position stays");
            Assert.That(await B.Number("document.getElementById('stop-button').getBoundingClientRect().top"), Is.EqualTo(Top), "Stop stays where the user sees it");
            Assert.That(await S.Said(), Is.Empty, "a routine check is never announced");
            Assert.That(await B.Text("document.querySelector('#facts').textContent"), Does.Contain("Check 3 at "));
            Assert.That(await B.Is("document.getElementById('show-latest').textContent === 'Show the latest status'"), Is.True, "the manual way stays");
            Assert.That((Emails.Count, S.M.Monitoring.View().Generation), Is.EqualTo((1, 1L)), "the page's questions sent nothing and started nothing");

            //Stop, the page's protected POST: then the page follows the stop to its end, and asks nothing more
            await B.Press("#stop-button");
            await B.Until(StatusText + ".indexOf('Not running: it was stopped at ') === 0 && !document.getElementById('live')", "the stopped monitoring");
            await S.AsksNoMore(Interval);
            Assert.That(S.M.Monitoring.View().Status.IsRunning, Is.False);
            Assert.That(B.Events.Count(E => E.Method == "Network.requestWillBeSent" && E.Params.GetProperty("type").GetString() == "Fetch"
                && E.Params.GetProperty("request").GetProperty("method").GetString() != "GET"), Is.Zero, "the script sends GETs only");
            await S.NoErrorsOrViolations();
        }

        [Test]
        public async Task AStopFromAnotherBrowser_EndsTheUpdates_SaidOnce_AndASessionThatEnds_StopsThemWhileTheMonitoringGoesOn()
        {
            await using Scene S = await Scene.Start();
            Chrome B = S.B;
            await B.Evaluate("document.getElementById('stop-button').focus()");
            using Browser Other = await S.M.Host.SignedIn();
            await Act(Other, "stop", await Other.Get("/monitoring"));
            await B.Until(StatusText + ".indexOf('Not running: it was stopped at ') === 0 && !document.getElementById('live')", "the stop, from another browser");
            Assert.That(await B.Is("window.__kept === 1"), Is.True);
            List<string> Said = await S.Said();
            Assert.That(Said.Last(), Does.StartWith("Not running: it was stopped at "), "the end, said");
            Assert.That(Said.Count(T => T.StartsWith("Not running", StringComparison.Ordinal)), Is.EqualTo(1), "once");
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("status"), "the focus, from a Stop that can't be used now, to the status");
            Assert.That(await B.Is("document.getElementById('start-button').disabled === false"), Is.True, "Start is offered again");
            await S.AsksNoMore(Interval);

            //started again from the page; then every session ends: the page says so and stops; the monitoring goes on
            await B.Press("#start-button");
            S.M.Waits.Await();
            await B.Until(StatusText + ".indexOf('Running: the next check starts at ') === 0", "the monitoring again");
            await S.Running();
            await S.Watch();
            S.M.Host.Service<WebSessions>().Clear();
            await B.Until(LiveText + ".indexOf('Warning: This page stopped updating: this browser is no longer signed in.') === 0", "the signed-out page");
            Assert.That((await S.Said()).Last(), Is.EqualTo("Warning: " + WebMonitoringWording.LiveSignedOut));
            await S.AsksNoMore(Interval);
            Assert.That(S.M.Monitoring.View().Status.IsRunning, Is.True, "the web UI's monitoring goes on");
            await S.NoErrorsOrViolations();
        }

        [Test]
        public async Task AFailedAnswerOrAPageItCantGet_IsSaidOnceAsThePagesProblem_AndTheUpdatesComeBackByThemselves()
        {
            await using Scene S = await Scene.Start(Intercept: true, Limit: ShortTimeout);
            Chrome B = S.B;
            Questions Q = S.Asked!;
            string Running = (await B.Text(StatusText))!;
            foreach (string Mode in new[] { "fail", "malformed", "error", "hold" })
            {
                Q.Mode = Mode;
                await B.Until(LiveClass + " === 'message warning'", "the warning for " + Mode, Seconds: 20);
                Assert.That(await B.Text(LiveText), Does.Match("^Warning: This page can't get the monitoring's status right now \\(last update: .+\\)\\. That does not stop the monitoring"), Mode);
                Assert.That(await B.Text(StatusText), Is.EqualTo(Running), "the monitoring is not said to have failed: " + Mode);
                Q.Mode = "pass";
                await S.Release();
                await B.Until(LiveClass + " === 'hint'", "the updates again after " + Mode, Seconds: 20);
                Assert.That((await S.Said()).Last(), Is.EqualTo(WebMonitoringWording.LiveResumed), Mode);
            }
            //a check finishes while the page can't get the page itself: said, then the new check shown once it can
            Q.ViewMode = "fail";
            S.M.Waits.Next();
            S.M.Waits.Await();
            await B.Until(LiveClass + " === 'message warning'", "the warning for the page it can't get", Seconds: 20);
            Assert.That(await B.Text(LiveText), Does.StartWith("Warning: This page can't get the monitoring's status right now"), "not an end: the monitoring runs");
            Assert.That(await B.Text(Summary), Does.StartWith("Check 1 at "), "nothing claimed it can't show");
            Q.ViewMode = "pass";
            await B.Until(Summary + ".indexOf('Check 2 at ') === 0 && " + LiveClass + " === 'hint'", "the second check after all", Seconds: 20);
            Assert.That((await S.Said()).Last(), Is.EqualTo(WebMonitoringWording.LiveResumed), "the updates came back, said once");
            Assert.That(await B.Is("window.__kept === 1"), Is.True);
            Assert.That((Emails.Count, S.M.Monitoring.View().Generation), Is.EqualTo((1, 1L)), "no update started, repeated or sent anything");
            await S.NoErrorsOrViolations();
        }

        [Test]
        public async Task ThePageFollowsThePauseForTheCampgroundDataRefresh_AndTheStartAfterIt()
        {
            await using Scene S = await Scene.Start(WithRefresh: true);
            Chrome B = S.B;
            //another browser starts a refresh of the campground data: the monitoring pauses, and the page shows it
            using Browser Other = await S.M.Host.SignedIn();
            await SystemPageTests.StartRefresh(Other);
            S.Refresh!.Await(S.Refresh.Downloading, "the refresh started");
            await B.Until(StatusText + ".indexOf('Paused while the campground data is refreshed') === 0", "the pause");
            Assert.That(await B.Is("document.getElementById('live') && document.getElementById('start-button').disabled && document.getElementById('stop-button').disabled"), Is.True,
                "it starts again by itself: the page goes on following it, with no action offered meanwhile");
            Assert.That(await B.Text("document.getElementById('actions-unavailable').textContent"), Is.EqualTo(WebMonitoringWording.UnavailablePaused));
            //the refresh ends: the monitoring starts again by itself, a new start, and the page follows it
            S.Refresh.LetDownload.Set();
            S.Refresh.Await(S.Refresh.Installing, "the installation");
            S.Refresh.LetInstall.Set();
            S.M.Waits.Await();
            await B.Until(StatusText + ".indexOf('Running: the next check starts at ') === 0 && document.getElementById('live')", "the start after the refresh");
            Assert.That(await B.Text("document.getElementById('facts').textContent"), Does.Contain("(again after the campground data's refresh)"));
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "never a reload");
            List<string> Said = await S.Said();
            Assert.That(Said.Any(T => T.StartsWith("Paused while the campground data is refreshed", StringComparison.Ordinal)), Is.True, "the pause, said");
            Assert.That(Said.Last(), Does.StartWith("Running: the next check starts at "), "the start after it, said");
            Assert.That(Said, Is.Unique.And.Count.LessThanOrEqualTo(5), "each change of state once (stopping, pausing, paused, starting, running), never a poll");
            Assert.That(S.M.Monitoring.View().Generation, Is.EqualTo(2L));
            await S.NoErrorsOrViolations();
        }
    }
}
