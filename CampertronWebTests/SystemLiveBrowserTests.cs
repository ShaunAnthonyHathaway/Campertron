using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using static CampertronWebTests.SystemPageTests;

namespace CampertronWebTests
{
    // The system page's automatic progress while the campground data is refreshed (D109) in a real browser: headless
    // Chrome runs the web UI's one script under the web UI's Content-Security-Policy against a test's web UI, with the
    // refresh service as the test drives it (SystemPageTests.FakeRefresh). While the refresh runs, the page changes in place
    // (its stage, counts, elapsed time and bar, and when it last got them) and never reloads, moves the focus or the scroll
    // position, or announces an ordinary update; the cache step and the end put the page's changed parts in place, the
    // card's new date and the configuration's refresh line included, each announced once. A failed, stalled or unusable
    // answer is said at once as the page's problem, not the refresh's, and the updates come back by themselves without a
    // second refresh; a page that can't get the end says so and keeps trying. Cancel stays the page's protected POST, and
    // a session that ends stops the updates while the web UI's refresh goes on.
    public class SystemLiveBrowserTests
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(1200);
        private const string LiveText = "document.getElementById('live').textContent";
        private const string LiveClass = "document.getElementById('live').className";
        private const string StatusText = "document.getElementById('status').textContent";
        private const string Details = "document.getElementById('progress-details').textContent";

        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        // a web UI whose refresh the test drives and a signed-in Chrome on its system page, the refresh started from the page
        private sealed class Scene : LiveScene
        {
            internal SystemHost S = null!;
            protected override WebTestHost? Web => S?.Host;
            protected override ValueTask DisposeWeb() => S.DisposeAsync();
            internal FakeRefresh Fake => S.Fake;

            internal static Task<Scene> Start(bool Intercept = false, int Width = 1024, int Height = 768, TimeSpan? Limit = null, string? Theme = null, string? Prefers = null)
            {
                Chrome.Require();
                return Build(new Scene(), async Scene_ =>
                {
                    Scene_.S = await SystemHost.Start(More: Settings =>
                    {
                        Settings.LiveInterval = Interval;
                        Settings.LiveTimeout = Limit ?? Timeout;
                    });
                    File.WriteAllText(Scene_.S.In("Alpha.yaml"), ResultsPageTests.Search("First trip"));
                    await Scene_.Browse(Intercept, Width, Height, Theme, Prefers);
                    await Scene_.SignIn();
                    await Scene_.Begin();
                });
            }

            // the confirmation's Start refresh, and the script running for the refresh
            internal async Task Begin()
            {
                await B.Open(S.Host.Root + "settings/system?confirm=refresh");
                await B.Press("form[action='/settings/system?handler=start'] button");
                Fake.Await(Fake.Downloading, "the refresh started");
                await Running();
                await Watch();
            }

            // the service reports a downloaded amount
            internal void Downloaded(long Sequence, long Bytes) => Fake.Reporter!.Report(new RidbRefreshProgress(Sequence, RidbRefreshStage.Downloading, true, Bytes, 570_000_000));
        }

        [Test]
        public async Task TheRunningRefreshsPage_ChangesInPlace_KeepsTheFocusAndTheScroll_AndShowsTheEndWithTheNewCampgroundData()
        {
            await using Scene Sc = await Scene.Start(Height: 300);
            Chrome B = Sc.B;
            string Search_ = File.ReadAllText(Sc.S.In("Alpha.yaml"));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            Assert.That(await B.Text(LiveText), Does.Match("^This page updates by itself while the refresh runs\\. Last update: .+\\.$").And.Not.Contain("{time}"), "when it last got the progress");
            Assert.That(await B.Text("document.body.innerText"), Does.Not.Contain("does not update by itself"));
            string ConfigurationBefore = (await B.Text("document.querySelectorAll('#configuration dd')[5].textContent"))!;
            await Sc.Questions_(2);
            //the user is at Cancel, scrolled down
            await B.Evaluate("document.getElementById('cancel-button').focus(); window.scrollTo(0, document.documentElement.scrollHeight)");
            double Y = await B.Number("window.scrollY");
            Assert.That(Y, Is.GreaterThan(0), "the page scrolls in a low window");

            //the download's progress, the bar and the elapsed time (the web UI's clock), in place
            Sc.Downloaded(3, 300_000_000);
            await B.Until(Details + ".indexOf('300.0 MB of 570.0 MB downloaded') === 0", "the downloaded amount");
            Assert.That(await B.Text("document.getElementById('progress').getAttribute('value') + '/' + document.getElementById('progress').getAttribute('max')"), Is.EqualTo("300000000/570000000"));
            Sc.S.Time.Advance(TimeSpan.FromSeconds(65));
            await B.Until("/Elapsed 1:05$/.test(" + Details + ")", "the elapsed time");
            Sc.Downloaded(4, 450_000_000);
            await B.Until(Details + ".indexOf('450.0 MB of 570.0 MB downloaded') === 0", "the next amount");
            await Sc.Questions_(3);
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "never a reload");
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("cancel-button"), "the focus stays at Cancel");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y), "the scroll position stays");
            Assert.That(await Sc.Said(), Is.Empty, "an ordinary update is never announced");
            Assert.That(await B.Is("document.getElementById('cancel-button').disabled === false"), Is.True, "Cancel stays available");
            Assert.That(await B.Is("document.getElementById('show-latest').textContent === 'Show the latest progress'"), Is.True, "the manual way stays");
            Assert.That(B.Fetches(Url => Url.Contains("handler=view", StringComparison.Ordinal)), Is.Zero, "nothing but the answers while nothing else changed");

            //the cache step: Cancel can't stop it now; the page's Cancel changes in place, said once, the focus to the status
            Sc.Fake.LetDownload.Set();
            Sc.Fake.Await(Sc.Fake.Installing, "the installation");
            await B.Until("document.getElementById('cancel-button').disabled === true", "Cancel unavailable from the cache step");
            Assert.That(await B.Text(StatusText), Does.EndWith(RefreshWording.FinishingText));
            Assert.That(await B.Text("document.getElementById('cancel-unavailable').textContent"), Is.EqualTo(RefreshWording.FinishingText));
            Assert.That(await B.Text("document.activeElement.id"), Is.EqualTo("status"), "the focus, from a Cancel that can't be used, to the status");
            Assert.That(await B.Number("window.scrollY"), Is.EqualTo(Y));
            Assert.That(await Sc.Said(), Is.EqualTo(new[] { await B.Text(StatusText) }), "the change, said once");
            Assert.That(await B.Is("window.__kept === 1"), Is.True);

            //the end: the service records the refresh; the card's date and the configuration's line in place, said once
            Sc.Fake.OnInstall = () => File.WriteAllText(Sc.S.In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-23T11:00:00.0000000Z\nautoRefresh: false\n");
            Sc.Fake.LetInstall.Set();
            await B.Until("!document.getElementById('live') && document.getElementById('status').textContent.trim().indexOf('Info: The campground data was refreshed.') === 0", "the end");
            Assert.That(await B.Text("document.getElementById('last-refreshed').textContent"), Is.EqualTo("today (11/23/2026)"), "the card's new date");
            Assert.That(await B.Text("document.querySelectorAll('#configuration dd')[5].textContent"), Is.Not.EqualTo(ConfigurationBefore), "the configuration's refresh line, read again");
            Assert.That(await B.Text("document.body.innerText"), Does.Contain(WebSystemWording.RestartNote));
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "the end came without a reload");
            List<string> Said = await Sc.Said();
            Assert.That(Said, Has.Count.EqualTo(2), "the cache step and the end, each once");
            Assert.That(Said[1], Does.StartWith("Info: The campground data was refreshed."));
            Assert.That(await B.Number("window.scrollY"), Is.LessThanOrEqualTo(Y), "no jump down");
            Assert.That(await B.Is("document.querySelector('form[action=\"/settings/system\"] button').disabled === false"), Is.True, "a new refresh can be asked for");
            await Sc.AsksNoMore(Interval);
            Assert.That(B.Fetches(Url => Url.Contains("handler=view", StringComparison.Ordinal)), Is.EqualTo(2), "the page asked for twice: the cache step, the end");
            Assert.That(Sc.Fake.Calls, Is.EqualTo(1), "one refresh, one installation");
            Assert.That(File.ReadAllText(Sc.S.In("Alpha.yaml")), Is.EqualTo(Search_), "the search file is the user's");
            await Sc.NoErrorsOrViolations();
        }

        [Test]
        public async Task AFailedStalledOrUnusableAnswer_IsSaidAtOnceAsThePagesProblem_AndTheUpdatesComeBackWithoutAnotherRefresh()
        {
            await using Scene Sc = await Scene.Start(Intercept: true, Limit: ShortTimeout);
            Chrome B = Sc.B;
            Questions Q = Sc.Asked!;
            string Downloading = (await B.Text(StatusText))!;
            foreach (string Mode in new[] { "fail", "malformed", "markup", "error", "hold" })
            {
                Q.Mode = Mode;
                await B.Until(LiveClass + " === 'message warning'", "the warning for " + Mode, Seconds: 20);
                Assert.That(await B.Text(LiveText), Does.Match("^Warning: This page can't get the refresh's progress right now \\(last update: .+\\)\\. That does not stop the refresh")
                    .And.Not.Contain("{time}"), Mode);
                Assert.That(await B.Text(StatusText), Is.EqualTo(Downloading), "the refresh is not said to have failed: " + Mode);
                Q.Mode = "pass";
                await Sc.Release();
                await B.Until(LiveClass + " === 'hint'", "the updates again after " + Mode, Seconds: 20);
                Assert.That((await Sc.Said()).Last(), Is.EqualTo(WebSystemWording.LiveResumed), "said once when they came back: " + Mode);
            }
            List<string> Said = await Sc.Said();
            Assert.That(Said.Count(T => T.StartsWith("Warning: ", StringComparison.Ordinal)), Is.EqualTo(5), "each failure said once, not at every retry");
            Assert.That(Said.Count(T => T == WebSystemWording.LiveResumed), Is.EqualTo(5));

            //the refresh went on all the while: the next report shows, and nothing started it again
            Sc.Downloaded(3, 200_000_000);
            await B.Until(Details + ".indexOf('200.0 MB of 570.0 MB downloaded') === 0", "the progress after the failures");
            Assert.That(Sc.Fake.Calls, Is.EqualTo(1), "no update started or repeated the refresh");

            //the refresh ends while the page can't get the page itself: it says so, keeps trying and shows the end once it can
            Q.ViewMode = "error";
            Sc.Fake.LetDownload.Set();
            Sc.Fake.Await(Sc.Fake.Installing, "the installation");
            Sc.Fake.LetInstall.Set();
            await B.Until(LiveText + ".indexOf('Warning: The refresh has ended, but this page can\\'t show how it ended right now') === 0", "the end not shown yet", Seconds: 20);
            Assert.That(await B.Text(StatusText), Does.Not.Contain("The campground data was refreshed."), "the page never claims an end it can't show");
            Q.ViewMode = "pass";
            await B.Until("!document.getElementById('live') && document.getElementById('status').textContent.trim().indexOf('Info: The campground data was refreshed.') === 0", "the end after all", Seconds: 20);
            Assert.That((await Sc.Said()).Last(), Does.StartWith("Info: The campground data was refreshed."), "the end, said");
            Assert.That(await B.Is("window.__kept === 1"), Is.True);
            Assert.That(Sc.Fake.Calls, Is.EqualTo(1));
            await Sc.AsksNoMore(Interval);
            await Sc.NoErrorsOrViolations();
        }

        [Test]
        public async Task AnOlderAnswer_AnEarlierState_OrAnotherWebUIsAnswer_NeverChangesWhatThePageShows()
        {
            await using Scene Sc = await Scene.Start(Intercept: true);
            Chrome B = Sc.B;
            string Key = (await Sc.LiveAttribute("data-key"))!;
            string View = (await Sc.LiveAttribute("data-view"))!;
            Dictionary<string, string> Say(string Text) => new() { ["status"] = Text, ["progress-details"] = Text + " details" };

            await Sc.Give(LiveScene.Crafted(Key, "running", 900_000, 1, View, Say("Newer state")));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Newer state"));
            await Sc.Give(LiveScene.Crafted(Key, "running", 800_000, 1, View, Say("Older state")));
            await Sc.Give(LiveScene.Crafted(Key, "running", 900_000, 1, View, Say("The same number again")));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Newer state"), "an older or repeated answer never replaces a newer one");
            //canceling wins: a running answer, however new, never makes the refresh look as if it could be canceled again
            await Sc.Give(LiveScene.Crafted(Key, "canceling", 950_000, 2, View, Say("Canceling as crafted")));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Canceling as crafted"));
            await Sc.Give(LiveScene.Crafted(Key, "running", 990_000, 1, View, Say("Running again")));
            Assert.That(await B.Text(StatusText), Is.EqualTo("Canceling as crafted"), "never an earlier state");
            await Sc.Give(LiveScene.Crafted(Key, "canceling", 991_000, 2, View, Say("<img src=x onerror=\"window.__pwned=1\">")));
            Assert.That(await B.Is("!document.querySelector('main img') && window.__pwned === undefined"), Is.True, "markup is text");
            Assert.That(await Sc.Said(), Is.EqualTo(new[] { "Canceling as crafted" }), "only the change of state is said");

            //another start of the web UI: the page says it no longer shows the refresh and stops asking
            Sc.Asked!.Crafted.Enqueue(LiveScene.Crafted("0000000000000000-1", "running", 999_999, 1, View, Say("Another web UI")));
            await B.Until(LiveText + " === 'Warning: " + WebSystemWording.LiveOutOfDate.Replace("'", "\\'") + "'", "the out-of-date page");
            Assert.That(await B.Text(StatusText), Is.Not.EqualTo("Another web UI"));
            await Sc.AsksNoMore(Interval);
            Assert.That(await B.Is("document.getElementById('show-latest')"), Is.True, "the manual way stays");
            Assert.That(Sc.Fake.Calls, Is.EqualTo(1));
            Assert.That(Sc.Fake.Token.IsCancellationRequested, Is.False, "no answer canceled the refresh");
            await Sc.NoErrorsOrViolations();
        }

        [Test]
        public async Task CancelFromThePage_StaysItsProtectedPost_AndASessionThatEnds_StopsTheUpdatesWhileTheRefreshGoesOn()
        {
            await using Scene Sc = await Scene.Start();
            Chrome B = Sc.B;
            await B.Press("#cancel-button");
            await B.Until("!document.getElementById('live') && document.getElementById('status').textContent.trim().indexOf('Info: The refresh was canceled.') === 0", "the canceled refresh's end");
            Assert.That(Sc.Fake.Token.IsCancellationRequested, Is.True, "Cancel, the page's POST, stopped the refresh");
            await Sc.AsksNoMore(Interval);
            Assert.That(B.Events.Count(E => E.Method == "Network.requestWillBeSent" && E.Params.GetProperty("type").GetString() == "Fetch"
                && E.Params.GetProperty("request").GetProperty("method").GetString() != "GET"), Is.Zero, "the script sends GETs only");

            //a new refresh, then every session ends (a password change): the page says it is signed out; the refresh goes on
            Sc.Fake.LetDownload.Reset();
            await Sc.Begin();
            Sc.S.Host.Service<WebSessions>().Clear();
            await B.Until(LiveText + ".indexOf('Warning: This page stopped updating: this browser is no longer signed in.') === 0", "the signed-out page");
            Assert.That((await Sc.Said()).Last(), Is.EqualTo("Warning: " + WebSystemWording.LiveSignedOut));
            await Sc.AsksNoMore(Interval);
            Assert.That(Sc.S.Host.Service<WebRefresh>().View().IsRunning, Is.True, "the web UI's refresh goes on");
            Assert.That(Sc.Fake.Calls, Is.EqualTo(2));
            await Sc.NoErrorsOrViolations();
        }

        // D110: the theme this browser chose (or, for System, the browser's own setting) through the whole refresh: its
        // progress in place, the cache step's parts and the end's put in place keep the page in that theme, never the other
        // one, never a mix, never a reload, and a choice of theme not yet applied stays as the user left it (the theme's
        // form is outside the parts the script puts in place)
        [TestCase(null, "light", "light")]
        [TestCase(null, "dark", "dark")]
        [TestCase("light", "dark", "light")]
        [TestCase("dark", "light", "dark")]
        public async Task TheChosenTheme_StaysThroughTheRefreshsUpdatesAndItsEnd(string? Theme, string Prefers, string Shown)
        {
            await using Scene Sc = await Scene.Start(Theme: Theme, Prefers: Prefers);
            Chrome B = Sc.B;
            string Scheme = Theme ?? "light dark";
            (string?, string?) Asked = await ThemeLook.Asked(B);
            Assert.That(Asked, Is.EqualTo((Theme ?? "system", Scheme)));
            await ThemeLook.Check(B, Shown, Scheme, "the running refresh");
            //a theme chosen but not applied: the other one than the page's
            string Unapplied = Theme == "dark" ? "theme-light" : "theme-dark";
            await B.Evaluate("document.getElementById('" + Unapplied + "').checked = true");
            Sc.Downloaded(3, 300_000_000);
            await B.Until(Details + ".indexOf('300.0 MB of 570.0 MB downloaded') === 0", "the downloaded amount");
            await Sc.Questions_(2);
            await ThemeLook.Check(B, Shown, Scheme, "updated in place");
            Sc.Fake.LetDownload.Set();
            Sc.Fake.Await(Sc.Fake.Installing, "the installation");
            await B.Until("document.getElementById('cancel-button').disabled === true", "the cache step's parts");
            await ThemeLook.Check(B, Shown, Scheme, "the cache step's parts put in place");
            Sc.Fake.LetInstall.Set();
            await B.Until("!document.getElementById('live') && document.getElementById('status').className.indexOf('message') >= 0", "the end");
            await ThemeLook.Check(B, Shown, Scheme, "the end put in place");
            Assert.That(await ThemeLook.Asked(B), Is.EqualTo(Asked), "the page still asks for the same theme");
            Assert.That(await B.Is("document.getElementById('" + Unapplied + "').checked"), Is.True, "the choice not yet applied stays");
            Assert.That(await B.Is("window.__kept === 1"), Is.True, "never a reload");
            Assert.That(Sc.Fake.Calls, Is.EqualTo(1), "nothing started again");
            await Sc.AsksNoMore(Interval);
            await Sc.NoErrorsOrViolations();
        }
    }
}
