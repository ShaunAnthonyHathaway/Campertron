using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.api;
using static CampertronWebTests.ResultsLiveTests;
using static CampertronWebTests.ResultsPageTests;
using static CampertronWebTests.SystemPageTests;

namespace CampertronWebTests
{
    // The system page's automatic progress while the campground data is refreshed (D109; business-rules-settings §36 and
    // §39), as the web UI answers it: the page of a running refresh names it for the web UI's one script (Assets/live.js),
    // and "?handler=progress" answers the refresh's state in the page's words (WebRefresh.Observe). It only looks: asking
    // never starts, repeats, resumes, cancels or installs a refresh, writes nothing and changes no settings or search file.
    // The refresh is the web UI's, not a session's (D99): every signed-in session may follow it, a browser that is not
    // signed in gets the sign-in. Its states only go forward (running, canceling, finishing, ended). Cancel stays the
    // page's protected POST. The browser's side is SystemLiveBrowserTests'.
    public class SystemLiveTests
    {
        private MonitoringPageTests Seams = null!;

        [SetUp]
        public void SetUp()
        {
            Seams = new MonitoringPageTests();
            Seams.SetUp();
        }

        [TearDown]
        public void TearDown() => Seams.TearDown();

        internal static Task<(WebPage Page, JsonElement Answer)> AskSystem(Browser Who, string? Key) => AskAt(Who, Address, Key);

        // a settings folder with a search file, for the checks that the refresh's observer changes none of them
        private static void WithSearch(SystemHost S) => File.WriteAllText(S.In("Alpha.yaml"), Search("First trip"));

        [Test]
        public async Task APageOfARunningRefresh_NamesItForTheWebUIsOneScript_AndNoOtherSystemPageHasAScript()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Idle = await Owner.Get(Address);
            WebPage Confirm = await Owner.Get(Address + "?confirm=refresh");
            LiveMarkup.Check("at rest", Idle, false, WebSystemWording.ShowLatest);
            LiveMarkup.Check("the confirmation", Confirm, false, WebSystemWording.ShowLatest);

            WebPage Running = await StartRefresh(Owner, Confirm);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            Running = await Owner.Get(Address);
            LiveMarkup.Check("downloading", Running, true, WebSystemWording.ShowLatest);
            XElement Live_ = Live(Running)!;
            WebRefresh Refresh = S.Host.Service<WebRefresh>();
            Assert.That(KeyOf(Running), Is.EqualTo(Refresh.Instance + "-1"), "this web UI's random name and the refresh's number");
            Assert.That(Refresh.Instance, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(((string?)Live_.Attribute("data-state"), (string?)Live_.Attribute("data-rank")), Is.EqualTo(((string?)"running", (string?)"1")));
            Assert.That(((string?)Live_.Attribute("data-interval"), (string?)Live_.Attribute("data-timeout")), Is.EqualTo(("2000", "10000")), "every two seconds; ten seconds for an answer");
            Assert.That(Live_.Value, Is.EqualTo(WebSystemWording.LiveManual), "without the script: the manual way, never \"does not update by itself\"");
            Assert.That(new[] { "data-updating", "data-resumed", "data-failing", "data-ended-failing", "data-signed-out", "data-out-of-date" }.Select(A => (string?)Live_.Attribute(A)),
                Is.EqualTo(new[] { WebSystemWording.LiveUpdating, WebSystemWording.LiveResumed, "Warning: " + WebSystemWording.LiveFailing, "Warning: " + WebSystemWording.LiveEndedNotShown,
                    "Warning: " + WebSystemWording.LiveSignedOut, "Warning: " + WebSystemWording.LiveOutOfDate }));
            Assert.That(WebSystemWording.LiveFailing, Does.Contain("does not stop the refresh"), "the page's problem is never said as the refresh's");
            Assert.That(WebSystemWording.LiveSignedOut, Does.Contain("goes on"), "the refresh is the web UI's: signing out stops nothing");
            Assert.That(Running.Text(), Does.Not.Contain("does not update by itself"));
            Assert.That(Element(Running, "cancel-button"), Is.Not.Null, "Cancel has an id: the focus finds it again");

            //the page that shows the refresh's end has no script
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "the installation");
            S.Fake.LetInstall.Set();
            WebPage Done = await RefreshEnded(Owner);
            LiveMarkup.Check("refreshed", Done, false, WebSystemWording.ShowLatest);
        }

        [Test]
        public async Task TheProgressAnswer_IsTheRefreshsStateInThePagesWords_AndAskingStartsRepeatsResumesOrWritesNothing()
        {
            await using SystemHost S = await SystemHost.Start();
            WithSearch(S);
            using Browser Owner = await S.Host.SignedIn();
            string Before = Snapshot(S.Folder);
            string CacheBefore = Snapshot(S.Cache);

            //no refresh: asking, with any key, starts none
            await Owner.Get(Address);
            for (int i = 0; i < 5; i++)
            {
                Assert.That(State((await AskSystem(Owner, S.Host.Service<WebRefresh>().Instance + "-1")).Answer), Is.EqualTo("replaced"), "no refresh had that number");
                Assert.That(State((await AskSystem(Owner, null)).Answer), Is.EqualTo("replaced"));
            }
            Assert.That(S.Fake.Calls, Is.Zero, "asking never starts a refresh");
            Assert.That(Snapshot(S.Folder), Is.EqualTo(Before));

            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            Running = await Owner.Get(Address);
            string Key = KeyOf(Running);
            long Shown = long.Parse((string)Live(Running)!.Attribute("data-seq")!, System.Globalization.CultureInfo.InvariantCulture);
            (WebPage First, JsonElement A) = await AskSystem(Owner, Key);
            Assert.That(First.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That(First.Header("X-Content-Type-Options"), Is.EqualTo("nosniff"));
            Assert.That(First.Header("Content-Security-Policy"), Is.EqualTo(Policy), "the policy is unchanged");
            Assert.That((State(A), Text(A, "key"), A.GetProperty("format").GetInt32(), Rank(A)), Is.EqualTo(("running", Key, 2, 1)));
            Assert.That(Seq(A), Is.GreaterThan(Shown), "newer than the page");
            Assert.That(Names(A), Is.SubsetOf(AnswerKeys));
            Assert.That(Text(A, "view"), Is.EqualTo((string?)Live(Running)!.Attribute("data-view")), "the page's own view");
            Assert.That(FieldNames(A), Is.EquivalentTo(new[] { "status", "progress-details" }), "the page's own elements, nothing else");
            Assert.That(Field(A, "status"), Is.EqualTo("Refreshing the campground data: downloading the campground data from Recreation.gov."));
            Assert.That(Field(A, "progress-details"), Does.StartWith("120.4 MB of 570.0 MB downloaded · Elapsed "));
            JsonElement Bar = A.GetProperty("bar");
            Assert.That((Bar.GetProperty("indeterminate").GetBoolean(), Bar.GetProperty("value").GetDouble(), Bar.GetProperty("max").GetDouble()), Is.EqualTo((false, 120_400_000.0, 570_000_000.0)));

            //the elapsed time is the web UI's clock
            S.Time.Advance(TimeSpan.FromSeconds(65));
            (_, JsonElement B) = await AskSystem(Owner, Key);
            Assert.That(Field(B, "progress-details"), Does.EndWith("Elapsed 1:05"));
            Assert.That(Text(B, "view"), Is.EqualTo(Text(A, "view")), "the elapsed time changes no control");

            //asking again and again only looks: one refresh, nothing written, nothing said, the token untouched
            long Last = Seq(B);
            for (int i = 0; i < 25; i++)
            {
                (_, JsonElement Again) = await AskSystem(Owner, Key);
                Assert.That(Seq(Again), Is.GreaterThan(Last));
                Last = Seq(Again);
                Assert.That(State(Again), Is.EqualTo("running"));
            }
            Assert.That(S.Fake.Calls, Is.EqualTo(1), "asking never starts or repeats a refresh");
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False, "asking never cancels it");
            Assert.That(Snapshot(S.Folder), Is.EqualTo(Before), "nothing in the settings folder changed: no settings or search file");
            Assert.That(Snapshot(S.Cache), Is.EqualTo(CacheBefore));
            Assert.That(S.Host.Lines, Is.EqualTo(new[] { WebRefresh.LinePrefix + "refresh started." }), "nothing said but the start");

            //the end: the answer says so and shows nothing more of it; asking after the end resumes nothing
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "the installation");
            S.Fake.LetInstall.Set();
            await RefreshEnded(Owner);
            for (int i = 0; i < 5; i++)
            {
                (_, JsonElement End) = await AskSystem(Owner, Key);
                Assert.That((State(End), Text(End, "key"), Rank(End)), Is.EqualTo(("ended", Key, WebRefreshView.EndedRank)));
                Assert.That(Names(End), Is.EquivalentTo(new[] { "format", "state", "key", "seq", "rank" }));
            }
            Assert.That(S.Fake.Calls, Is.EqualTo(1), "an ended refresh is never resumed or repeated");
            Assert.That(S.Host.Service<WebRefresh>().View().IsRunning, Is.False);
        }

        [Test]
        public async Task Cancel_StaysAProtectedPost_AndOnceAsked_TheRefreshIsNeverShownRunningAgain()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            Running = await Owner.Get(Address);
            string Key = KeyOf(Running);
            string Token = StateOf(CancelForm(Running)!);

            //a question is no way to cancel; Cancel without the form's token, from another site, or for another moment cancels nothing
            await Owner.Get(Address + "?handler=progress&key=" + Key + "&handler=cancel");
            await Owner.Get(Address + "?handler=view&handler=cancel&state=" + Token);
            Assert.That((await Owner.Send(new HttpRequestMessage(HttpMethod.Head, Address + "?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await Owner.Post(Address + "?handler=cancel", ("state", Token), ("__RequestVerificationToken", "forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post(Address + "?handler=cancel", ("state", Token))).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = S.Host.Root.GetLeftPart(UriPartial.Authority);
            await Owner.Get(Address);
            WebPage Stale = await CancelRefresh(Owner, Running, State: "0-idle");
            Assert.That(Says(Stale, "page-notice"), Does.StartWith("Warning: This page was out of date"));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False, "nothing canceled the refresh");
            Assert.That(State((await AskSystem(Owner, Key)).Answer), Is.EqualTo("running"));

            //the page's Cancel: canceling (a later state, another view: its Cancel is "Canceling…"), then ended; never running again
            string RunningView = Text((await AskSystem(Owner, Key)).Answer, "view")!;
            S.Fake.HoldAfterCancel = new ManualResetEventSlim(false);
            WebPage Canceled = await CancelRefresh(Owner, await Owner.Get(Address));
            for (int i = 0; i < 10; i++)
            {
                JsonElement Canceling = (await AskSystem(Owner, Key)).Answer;
                Assert.That((State(Canceling), Rank(Canceling), Field(Canceling, "status")), Is.EqualTo(("canceling", WebRefreshView.CancelingRank, (string?)RefreshWording.CancelingText)));
                Assert.That(Text(Canceling, "view"), Is.Not.EqualTo(RunningView), "its Cancel changed: the script takes the page again");
            }
            Assert.That(Enabled(CancelForm(Canceled)!), Is.False, "Cancel asked for: not offered again");
            Assert.That(CancelForm(Canceled)!.Descendants("button").Single().Value, Is.EqualTo(RefreshWording.CancelLabel(true)));
            S.Fake.HoldAfterCancel.Set();
            var Seen = new List<(string State, int Rank)>();
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (Clock.Elapsed < Long)
            {
                JsonElement Now = (await AskSystem(Owner, Key)).Answer;
                Seen.Add((State(Now), Rank(Now)));
                if (State(Now) == "ended")
                {
                    break;
                }
                await Task.Delay(10);
            }
            Assert.That(Seen.Last(), Is.EqualTo(("ended", WebRefreshView.EndedRank)));
            Assert.That(Seen.Select(N => N.State), Has.None.EqualTo("running"), "after Cancel the refresh is canceling, then ended");
            Assert.That(Seen.Select(N => N.Rank), Is.Ordered, "never an earlier state");
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.True);
            WebPage Done = await RefreshEnded(Owner);
            Assert.That(Done.Text(), Does.Contain("The refresh was canceled. The campground data was not changed."));
            for (int i = 0; i < 5; i++)
            {
                Assert.That(State((await AskSystem(Owner, Key)).Answer), Is.EqualTo("ended"));
            }
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
            Assert.That(Canceled.Status, Is.EqualTo(HttpStatusCode.OK));
        }

        [Test]
        public async Task FromTheCacheStepOn_TheAnswerSaysFinishing_CancelIsTooLate_AndTheEndShowsTheNewCampgroundData()
        {
            await using SystemHost S = await SystemHost.Start();
            WithSearch(S);
            using Browser Owner = await S.Host.SignedIn();
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            Running = await Owner.Get(Address);
            string Key = KeyOf(Running);
            string RunningView = (string)Live(Running)!.Attribute("data-view")!;
            string Search_ = File.ReadAllText(S.In("Alpha.yaml"));
            Assert.That(Says(Running, "last-refreshed"), Is.EqualTo("3 days ago (11/20/2026)"));
            string ConfigurationBefore = Running.Xml().Descendants("dl").Single(D => (string?)D.Attribute("id") == "configuration").Elements("dd").ElementAt(5).Value;

            //the service reaches the cache step: finishing, a later state with another view (Cancel no longer offered)
            S.Fake.LetDownload.Set();
            S.Fake.Await(S.Fake.Installing, "the installation");
            JsonElement Finishing = (await AskSystem(Owner, Key)).Answer;
            Assert.That((State(Finishing), Rank(Finishing)), Is.EqualTo(("finishing", WebRefreshView.FinishingRank)));
            Assert.That(Text(Finishing, "view"), Is.Not.EqualTo(RunningView), "the page's Cancel changed: the script takes the page again");
            Assert.That(Field(Finishing, "status"), Does.EndWith(RefreshWording.FinishingText));
            WebPage Page = await Owner.Get(Address);
            Assert.That((string?)Live(Page)!.Attribute("data-view"), Is.EqualTo(Text(Finishing, "view")), "the page and its answer say the same of a moment");
            Assert.That(Enabled(CancelForm(Page)!), Is.False);
            WebPage TooLate = await CancelRefresh(Owner, Running);
            Assert.That(Says(TooLate, "page-notice"), Does.Contain("already reached the installation"));
            Assert.That((State((await AskSystem(Owner, Key)).Answer), S.Fake.Token.IsCancellationRequested), Is.EqualTo(("finishing", false)), "the installation goes on");

            //the service records the refresh (General.yaml's lastRidbDataRefresh, as the service writes it) and ends
            S.Fake.OnInstall = () => File.WriteAllText(S.In("General.yaml"), "outputTo: Console\nrefreshRidbDataDayInterval: 30\nlastRidbDataRefresh: 2026-11-23T11:00:00.0000000Z\nautoRefresh: false\n");
            S.Fake.LetInstall.Set();
            WebPage Done = await RefreshEnded(Owner);
            Assert.That(State((await AskSystem(Owner, Key)).Answer), Is.EqualTo("ended"));
            WebPage View = await Owner.Get(Address + "?handler=view");
            foreach (WebPage Shown in new[] { Done, View })
            {
                Assert.That(Shown.Text(), Does.Contain("Info: The campground data was refreshed.").And.Contain(WebSystemWording.RestartNote));
                Assert.That(Says(Shown, "last-refreshed"), Is.EqualTo("today (11/23/2026)"), "the card's new date");
                Assert.That(Shown.Xml().Descendants("dl").Single(D => (string?)D.Attribute("id") == "configuration").Elements("dd").ElementAt(5).Value, Is.Not.EqualTo(ConfigurationBefore),
                    "the configuration's refresh line, read again");
                LiveMarkup.Check("the end", Shown, false, WebSystemWording.ShowLatest);
            }
            Assert.That(File.ReadAllText(S.In("Alpha.yaml")), Is.EqualTo(Search_), "the search file is the user's, unchanged");
            Assert.That(S.Fake.Calls, Is.EqualTo(1), "one installation");
        }

        [TestCase("refreshed")]
        [TestCase("download failed")]
        [TestCase("canceled")]
        [TestCase("check failed")]
        [TestCase("unexpected")]
        public async Task EveryEndOfARefresh_EndsTheRunningState_AndNoEndIsRepeatedOrResumed(string How)
        {
            await using SystemHost S = await SystemHost.Start();
            WithSearch(S);
            using Browser Owner = await S.Host.SignedIn();
            string Search_ = File.ReadAllText(S.In("Alpha.yaml"));
            string General = File.ReadAllText(S.In("General.yaml"));
            switch (How)
            {
                case "download failed":
                    S.Fake.Before = () => FakeRefresh.Result(RidbRefreshOutcome.DownloadFailed, RidbRefreshStage.Downloading, false, new HttpRequestException(ServerText));
                    break;
                case "check failed":
                    S.Fake.After = RidbRefreshOutcome.InstalledButCheckFailed;
                    break;
                case "unexpected":
                    S.Fake.Before = () => throw new InvalidDataException(ServerText);
                    break;
            }
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            string Key = KeyOf(await Owner.Get(Address));
            Assert.That(State((await AskSystem(Owner, Key)).Answer), Is.EqualTo("running"));
            if (How == "canceled")
            {
                await CancelRefresh(Owner, await Owner.Get(Address));
            }
            else
            {
                S.Fake.LetDownload.Set();
                if (S.Fake.Before == null)
                {
                    S.Fake.Await(S.Fake.Installing, "the installation");
                    S.Fake.LetInstall.Set();
                }
            }
            WebPage Done = await RefreshEnded(Owner);
            (WebPage Page, JsonElement Answer) = await AskSystem(Owner, Key);
            Assert.That((State(Answer), Rank(Answer)), Is.EqualTo(("ended", WebRefreshView.EndedRank)), How);
            Assert.That(Page.Body + Done.Body, Does.Not.Contain(ServerText).And.Not.Contain(S.Folder).And.Not.Contain(S.Cache).And.Not.Contain("s3cret"), "fixed words only (an unexpected problem by its type)");
            Assert.That(Done.Text(), Does.Contain(How switch
            {
                "refreshed" => "The campground data was refreshed.",
                "download failed" => "The download from Recreation.gov failed.",
                "canceled" => "The refresh was canceled.",
                "check failed" => "Error: The new campground data was installed, but Campertron's check of it found a problem.",
                _ => "Error: The refresh stopped because of an unexpected problem (InvalidDataException)."
            }), How);
            LiveMarkup.Check(How, Done, false, WebSystemWording.ShowLatest);
            for (int i = 0; i < 5; i++)
            {
                await AskSystem(Owner, Key);
            }
            Assert.That(S.Fake.Calls, Is.EqualTo(1), "an ended refresh is never repeated or resumed by a question");
            Assert.That((File.ReadAllText(S.In("Alpha.yaml")), File.ReadAllText(S.In("General.yaml"))), Is.EqualTo((Search_, General)), "the settings and search files as they were");
        }

        [Test]
        public async Task AnEarlierRefreshsPage_IsToldItEnded_AndAPageOfAnEarlierWebUIOrOfNoRefresh_IsShownNothing()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage First = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the first refresh");
            string Old = KeyOf(await Owner.Get(Address));
            await CancelRefresh(Owner, await Owner.Get(Address));
            await RefreshEnded(Owner);
            S.Fake.LetDownload.Reset();
            WebPage Second = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the second refresh");
            string Key = KeyOf(await Owner.Get(Address));
            Assert.That(Key, Is.Not.EqualTo(Old));
            Assert.That((State((await AskSystem(Owner, Old)).Answer), State((await AskSystem(Owner, Key)).Answer)), Is.EqualTo(("ended", "running")),
                "the first refresh's page is told it ended; the second's gets its state");
            string Instance = S.Host.Service<WebRefresh>().Instance;
            string[] NotARefresh =
            {
                "", "1", "2", Instance, Instance + "-", Instance + "-0", Instance + "-02", Instance + "-3", Instance + "--2", Instance + "-+2", Instance.ToUpperInvariant() + "-2",
                "0000000000000000-2", Key + " ", Key + "\n", new string('a', 5000), "<script>alert(1)</script>", "../../General.yaml"
            };
            foreach (string Asked in NotARefresh)
            {
                (WebPage Page, JsonElement Answer) = await AskSystem(Owner, Asked);
                Assert.That(State(Answer), Is.EqualTo("replaced"), Asked);
                Assert.That(Names(Answer), Is.EquivalentTo(new[] { "format", "state", "seq" }), "nothing of a refresh: " + Asked);
                Assert.That(Page.Body, Does.Not.Contain(Key).And.Not.Contain("script").And.Not.Contain("General").And.Not.Contain("Refreshing"));
            }
            Assert.That(S.Fake.Calls, Is.EqualTo(2), "two Starts, two refreshes: no question started one");
        }

        [Test]
        public async Task EverySignedInSession_FollowsTheWebUIsRefresh_AndASessionThatEndedGetsTheSignInOnly_WhileTheRefreshGoesOn()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            using Browser Other = await S.Host.SignedIn();
            using Browser Visitor = S.Host.Browser();
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            string Key = KeyOf(await Owner.Get(Address));

            //the refresh is the web UI's (D99): another signed-in session sees it and may follow it
            Assert.That(KeyOf(await Other.Get(Address)), Is.EqualTo(Key));
            Assert.That(State((await AskSystem(Other, Key)).Answer), Is.EqualTo("running"));

            //a browser that is not signed in: the sign-in, nothing of the refresh
            foreach (string Asked in new[] { Address + "?handler=progress&key=" + Key, Address + "?handler=view" })
            {
                WebPage Visit = await Visitor.Get(Asked);
                Assert.That((Visit.Status, Visit.Location?.StartsWith("/signin?returnUrl=", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)), Asked);
                Assert.That(Visit.Body, Does.Not.Contain("Refreshing").And.Not.Contain(Key).And.Not.Contain("state"));
            }
            //the wrong host name is refused before anything else
            Owner.Headers["Host"] = "evil.example";
            WebPage Host = await Owner.Get(Address + "?handler=progress&key=" + Key);
            Assert.That(Host.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(Host.Body, Does.Not.Contain("Refreshing").And.Not.Contain(Key));
            Owner.Headers.Remove("Host");

            //the owner signs out: its page's next question gets the sign-in; the refresh, the web UI's, goes on for the other
            Assert.That((await Owner.Post("/signout")).Status, Is.EqualTo(HttpStatusCode.Redirect));
            WebPage After = await Owner.Get(Address + "?handler=progress&key=" + Key);
            Assert.That((After.Status, After.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)));
            Assert.That(After.Body, Does.Not.Contain("Refreshing").And.Not.Contain(Key));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False, "a session's end stops nothing of the web UI's refresh");
            Assert.That(State((await AskSystem(Other, Key)).Answer), Is.EqualTo("running"));

            //a session past its idle time gets the sign-in for its question too
            S.Host.Now += WebSessions.IdleTimeout;
            WebPage Expired = await Other.Get(Address + "?handler=progress&key=" + Key);
            Assert.That((Expired.Status, Expired.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)));
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False);
        }

        [Test]
        public async Task TheProgressQuestion_KeepsEveryRequestCheck_AndCountsInTheClientsLimit()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            string Key = KeyOf(await Owner.Get(Address));

            //its answer is a GET's only: a POST of that name is refused as every POST without the page's handler
            Assert.That((await Owner.Post(Address + "?handler=progress&key=" + Key, ("__RequestVerificationToken", "forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post(Address + "?handler=progress&key=" + Key)).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = S.Host.Root.GetLeftPart(UriPartial.Authority);
            await Owner.Get(Address);
            Assert.That((await Owner.Post(Address + "?handler=progress&key=" + Key)).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That((await Owner.Post(Address + "?handler=view")).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            foreach (string Method in new[] { "PUT", "DELETE", "PATCH", "OPTIONS" })
            {
                Assert.That((await Owner.Send(new HttpRequestMessage(new HttpMethod(Method), Address + "?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), Method);
            }
            //no CORS, and every header of the web UI
            Owner.Headers["Origin"] = "http://evil.example";
            WebPage Cross = await Owner.Get(Address + "?handler=progress&key=" + Key);
            Owner.Headers.Remove("Origin");
            Assert.That(Cross.Header("Access-Control-Allow-Origin") + Cross.Header("Access-Control-Allow-Credentials"), Is.Empty);
            WebPage Answer = await Owner.Get(Address + "?handler=progress&key=" + Key);
            Assert.That((Answer.Header("X-Frame-Options"), Answer.Header("Referrer-Policy"), Answer.Header("Cross-Origin-Resource-Policy"), Answer.Header("Content-Security-Policy")),
                Is.EqualTo(("DENY", "same-origin", "same-origin", Policy)));
            Assert.That(S.Fake.Calls, Is.EqualTo(1));

            //every question counts in the client's limit (RequestsPerClient in a window), then the fixed words
            int Answered = 0;
            WebPage Last;
            do
            {
                Last = await Owner.Get(Address + "?handler=progress&key=" + Key);
                Answered++;
            }
            while (Last.Status == HttpStatusCode.OK && Answered <= CampertronWebHost.RequestsPerClient + 1);
            Assert.That(Last.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Answered, Is.LessThanOrEqualTo(CampertronWebHost.RequestsPerClient));
            Assert.That(Last.Text(), Does.Contain("Campertron received too many requests from this address."));
            Assert.That(Last.Header("Content-Security-Policy"), Is.EqualTo(Policy));
            Assert.That(S.Fake.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ThePageForItsScript_IsThePageWithoutTheNoticeOfAnAction_AndNeverTheConfirmation()
        {
            await using SystemHost S = await SystemHost.Start();
            using Browser Owner = await S.Host.SignedIn();
            WebPage Running = await StartRefresh(Owner);
            S.Fake.Await(S.Fake.Downloading, "the refresh started");
            //an out-of-date Cancel leaves a notice for the session's next page
            WebPage Posted = await Owner.Post(Address + "?handler=cancel", ("state", "0-idle"));
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect));
            WebPage View = await Owner.Get(Address + "?handler=view&confirm=refresh");
            Assert.That(Element(View, "page-notice"), Is.Null, "the script's page leaves the notice to the user's");
            Assert.That(StartForm(View), Is.Null, "never the confirmation");
            Assert.That(CancelForm(View), Is.Not.Null);
            WebPage Next = await Owner.Get(Address);
            Assert.That(Says(Next, "page-notice"), Does.StartWith("Warning: This page was out of date"), "the user's next page has it");
            Assert.That(S.Fake.Token.IsCancellationRequested, Is.False);
        }
    }
}
