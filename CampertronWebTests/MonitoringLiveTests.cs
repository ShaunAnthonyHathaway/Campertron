using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using CampertronLibrary.function.Forms;
using static CampertronWebTests.MonitoringPageTests;
using static CampertronWebTests.ResultsLiveTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The monitoring page's automatic progress (D109; business-rules-settings §35 and §39), as the web UI answers it: while
    // the web UI's monitoring goes on, its page names it for the web UI's one script (Assets/live.js) and "?handler=progress"
    // answers its state in the page's words (WebMonitoringLive.Observe). It only looks: asking never starts, stops or
    // restarts the monitoring, starts a check, sends a notification, writes Camp.History or takes the lock. The monitoring
    // is the web UI's (D98): every signed-in session follows it; a browser that is not signed in gets the sign-in. Its page
    // is in parts (status, actions, last check), each named by what it shows, so a finished check changes the status and
    // the last check and leaves Start, Stop and Restart as they are. The browser's side is MonitoringLiveBrowserTests'.
    public class MonitoringLiveTests
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

        internal static Task<(WebPage Page, JsonElement Answer)> AskMonitoring(Browser Who, string? Key) => AskAt(Who, "/monitoring", Key);

        internal static string? PartOf(WebPage Page, string Id) => (string?)Element(Page, Id)?.Attribute("data-part");

        [Test]
        public async Task APageOfAMonitoringThatGoesOn_NamesItForTheWebUIsOneScript_AndAPageOfOneThatDoesNot_HasNoScript()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            WebPage Idle = await Owner.Get("/monitoring");
            LiveMarkup.Check("not started", Idle, false, WebMonitoringWording.ShowLatest);
            Assert.That(Idle.Text(), Does.Contain(WebMonitoringWording.LiveIntro).And.Not.Contain("does not update by itself"));

            await Act(Owner, "start", Idle);
            M.Waits.Await();
            WebPage Running = await Waiting(Owner);
            LiveMarkup.Check("running", Running, true, WebMonitoringWording.ShowLatest);
            XElement Live_ = Live(Running)!;
            Assert.That(KeyOf(Running), Is.EqualTo(M.Monitoring.Instance), "this web UI's random name: the monitoring is the web UI's one");
            Assert.That(M.Monitoring.Instance, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(((string?)Live_.Attribute("data-state"), (string?)Live_.Attribute("data-rank")), Is.EqualTo(((string?)"running", (string?)"0")),
                "the monitoring's states come round again: no rank");
            Assert.That(Live_.Value, Is.EqualTo(WebMonitoringWording.LiveManual));
            Assert.That(new[] { "data-updating", "data-resumed", "data-failing", "data-ended-failing", "data-signed-out", "data-out-of-date" }.Select(A => (string?)Live_.Attribute(A)),
                Is.EqualTo(new[] { WebMonitoringWording.LiveUpdating, WebMonitoringWording.LiveResumed, "Warning: " + WebMonitoringWording.LiveFailing,
                    "Warning: " + WebMonitoringWording.LiveEndedNotShown, "Warning: " + WebMonitoringWording.LiveSignedOut, "Warning: " + WebMonitoringWording.LiveOutOfDate }));
            Assert.That(WebMonitoringWording.LiveFailing, Does.Contain("does not stop the monitoring"));
            Assert.That(Live_.Ancestors().Any(A => (string?)A.Attribute("id") == WebMonitoringLive.StatusPart), Is.True, "#live is in the status part");
            foreach (string Part in new[] { WebMonitoringLive.StatusPart, WebMonitoringLive.ActionsPart, WebMonitoringLive.CheckPart })
            {
                Assert.That(PartOf(Running, Part), Does.Match("^[0-9a-f]{16}$"), Part);
            }
            Assert.That(new[] { "start-button", "stop-button", "restart-button" }.Select(Id => Element(Running, Id) != null), Has.All.True, "the focus finds them again");
            Assert.That(Running.Text(), Does.Not.Contain("Show the latest status to see when it has").And.Not.Contain("Show the latest status, then try again"));

            WebPage Stopped = await Act(Owner, "stop", Running);
            Stopped = await NotRunning(Owner);
            LiveMarkup.Check("stopped", Stopped, false, WebMonitoringWording.ShowLatest);
        }

        [Test]
        public async Task TheAnswer_IsTheMonitoringsStateInThePagesWords_AndAskingStartsChecksSendsOrRecordsNothing()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            //a monitoring that does not run: asking starts nothing
            await Owner.Get("/monitoring");
            for (int i = 0; i < 5; i++)
            {
                (_, JsonElement Idle) = await AskMonitoring(Owner, M.Monitoring.Instance);
                Assert.That((State(Idle), Rank(Idle)), Is.EqualTo(("ended", 0)));
            }
            Assert.That((M.Monitoring.Controller.View().Generation, M.Monitoring.Controller.View().Status.IsRunning, Requested.Count), Is.EqualTo((0L, false, 0)), "asking never starts the monitoring");

            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage First = await Waiting(Owner);
            string Key = KeyOf(First);
            (WebPage Answered, JsonElement A) = await AskMonitoring(Owner, Key);
            Assert.That(Answered.Header("Content-Security-Policy"), Is.EqualTo(Policy));
            Assert.That(Answered.Header("Cache-Control"), Does.Contain("no-store"));
            Assert.That((State(A), Text(A, "key"), A.GetProperty("format").GetInt32(), Rank(A)), Is.EqualTo(("running", Key, 2, 0)));
            Assert.That(Names(A), Is.SubsetOf(AnswerKeys).And.Not.Contain("bar"));
            Assert.That(Text(A, "view"), Is.EqualTo((string?)Live(First)!.Attribute("data-view")), "the page's own view");
            Assert.That(FieldNames(A), Is.EquivalentTo(new[] { "status", "check-summary" }));
            Assert.That(Field(A, "status"), Is.EqualTo(State(First)));
            Assert.That(Field(A, "check-summary"), Is.EqualTo(Said(First, "check-summary")));

            //asking again and again only looks: no check, no request, no email, no marker, nothing written
            int Requests = Requested.Count;
            int Mails = Emails.Count;
            int Markers = M.Markers().Count;
            int Checks = M.Waits.Count;
            //the settings folder's files, but the monitoring lock (held open while the monitoring runs; its name is listed)
            string Files() => string.Join("\n", Directory.GetFileSystemEntries(M.Folder).OrderBy(N => N, StringComparer.Ordinal).Select(N => Path.GetFileName(N) + "\t"
                + (Path.GetFileName(N) == CampertronLibrary.function.RecDotOrg.monitoring.MonitoringLock.FileName || Directory.Exists(N) ? "-" : System.Convert.ToHexString(File.ReadAllBytes(N)) + "\t" + File.GetLastWriteTimeUtc(N).Ticks)));
            string Folder = Files();
            string Search_ = File.ReadAllText(M.In("Alpha.yaml"));
            long Last = Seq(A);
            for (int i = 0; i < 25; i++)
            {
                (_, JsonElement Again) = await AskMonitoring(Owner, Key);
                Assert.That(Seq(Again), Is.GreaterThan(Last));
                Last = Seq(Again);
                Assert.That((State(Again), Text(Again, "view")), Is.EqualTo(("running", Text(A, "view"))));
            }
            Assert.That((Requested.Count, Emails.Count, M.Markers().Count, M.Waits.Count, M.Monitoring.Controller.View().Generation), Is.EqualTo((Requests, Mails, Markers, Checks, 1L)),
                "asking starts no check, asks recreation.gov nothing, sends nothing and records nothing");
            Assert.That((Files(), File.ReadAllText(M.In("Alpha.yaml"))), Is.EqualTo((Folder, Search_)), "no settings or search file changed");

            //the next check: the status and the last check change (another view), Start, Stop and Restart do not
            M.Waits.Next();
            M.Waits.Await();
            WebPage Second = await Until(Owner, P => (Said(P, "check-summary") ?? "").StartsWith("Check 2 at ", StringComparison.Ordinal), "the second check");
            JsonElement B = (await AskMonitoring(Owner, Key)).Answer;
            Assert.That(Text(B, "view"), Is.Not.EqualTo(Text(A, "view")), "a finished check is a new view: the script takes the page again");
            Assert.That(Text(B, "view"), Is.EqualTo((string?)Live(Second)!.Attribute("data-view")));
            Assert.That(PartOf(Second, WebMonitoringLive.ActionsPart), Is.EqualTo(PartOf(First, WebMonitoringLive.ActionsPart)), "the actions part stays (and the focus in it)");
            Assert.That(PartOf(Second, WebMonitoringLive.CheckPart), Is.Not.EqualTo(PartOf(First, WebMonitoringLive.CheckPart)));
            Assert.That(PartOf(Second, WebMonitoringLive.StatusPart), Is.Not.EqualTo(PartOf(First, WebMonitoringLive.StatusPart)));

            //Stop: the answer is the monitoring's end, then nothing starts it again
            await Act(Owner, "stop", Second);
            await NotRunning(Owner);
            for (int i = 0; i < 5; i++)
            {
                (_, JsonElement End) = await AskMonitoring(Owner, Key);
                Assert.That((State(End), Text(End, "key")), Is.EqualTo(("ended", Key)));
                Assert.That(Names(End), Is.EquivalentTo(new[] { "format", "state", "key", "seq", "rank" }));
            }
            Assert.That((M.Monitoring.Controller.View().Status.IsRunning, M.Monitoring.Controller.View().Generation), Is.EqualTo((false, 1L)), "an ended monitoring is never started again by a question");
            Assert.That(M.LockIsHeld(), Is.False);
        }

        [Test]
        public async Task APageOfAnEarlierWebUI_OrAKeyThatIsNone_IsShownNothing()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            string Key = KeyOf(await Waiting(Owner));
            foreach (string? Asked in new[] { null, "", "x", Key.ToUpperInvariant(), Key + "-1", Key + " ", "0000000000000000", new string('a', 5000), "<script>alert(1)</script>" })
            {
                (WebPage Page, JsonElement Answer) = await AskMonitoring(Owner, Asked);
                Assert.That(State(Answer), Is.EqualTo("replaced"), Asked ?? "no key");
                Assert.That(Names(Answer), Is.EquivalentTo(new[] { "format", "state", "seq" }), "nothing of the monitoring");
                Assert.That(Page.Body, Does.Not.Contain(Key).And.Not.Contain("Running").And.Not.Contain("script"));
            }
            Assert.That(State((await AskMonitoring(Owner, Key)).Answer), Is.EqualTo("running"));
            Assert.That(M.Monitoring.Controller.View().Generation, Is.EqualTo(1L));
        }

        [Test]
        public async Task EverySignedInSession_FollowsTheWebUIsMonitoring_AndASessionThatEnded_GetsTheSignInOnly_WhileItGoesOn()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            using Browser Other = await M.Host.SignedIn();
            using Browser Visitor = M.Host.Browser();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            string Key = KeyOf(await Waiting(Owner));
            Assert.That(KeyOf(await Waiting(Other)), Is.EqualTo(Key), "the web UI's one monitoring");
            Assert.That(State((await AskMonitoring(Other, Key)).Answer), Is.EqualTo("running"));

            foreach (string Asked in new[] { "/monitoring?handler=progress&key=" + Key, "/monitoring?handler=view" })
            {
                WebPage Visit = await Visitor.Get(Asked);
                Assert.That((Visit.Status, Visit.Location?.StartsWith("/signin?returnUrl=", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)), Asked);
                Assert.That(Visit.Body, Does.Not.Contain("Running").And.Not.Contain(Key));
            }
            Owner.Headers["Host"] = "evil.example";
            WebPage Host = await Owner.Get("/monitoring?handler=progress&key=" + Key);
            Assert.That(Host.Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(Host.Body, Does.Not.Contain(Key));
            Owner.Headers.Remove("Host");

            Assert.That((await Owner.Post("/signout")).Status, Is.EqualTo(HttpStatusCode.Redirect));
            WebPage After = await Owner.Get("/monitoring?handler=progress&key=" + Key);
            Assert.That((After.Status, After.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)));
            Assert.That(After.Body, Does.Not.Contain(Key));
            Assert.That(M.Monitoring.Controller.View().Status.IsRunning, Is.True, "a session's end stops nothing of the web UI's monitoring");
            Assert.That(State((await AskMonitoring(Other, Key)).Answer), Is.EqualTo("running"));
        }

        [Test]
        public async Task TheProgressQuestion_KeepsEveryRequestCheck_AndCountsInTheClientsLimit()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            string Key = KeyOf(await Waiting(Owner));

            Assert.That((await Owner.Post("/monitoring?handler=progress&key=" + Key, ("__RequestVerificationToken", "forged"))).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Owner.Origin = "http://evil.example";
            Assert.That((await Owner.Post("/monitoring?handler=progress&key=" + Key)).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Owner.Origin = M.Host.Root.GetLeftPart(UriPartial.Authority);
            await Owner.Get("/monitoring");
            Assert.That((await Owner.Post("/monitoring?handler=progress&key=" + Key)).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            Assert.That((await Owner.Post("/monitoring?handler=view")).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            foreach (string Method in new[] { "PUT", "DELETE", "PATCH", "OPTIONS" })
            {
                Assert.That((await Owner.Send(new HttpRequestMessage(new HttpMethod(Method), "/monitoring?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.MethodNotAllowed), Method);
            }
            Owner.Headers["Origin"] = "http://evil.example";
            WebPage Cross = await Owner.Get("/monitoring?handler=progress&key=" + Key);
            Owner.Headers.Remove("Origin");
            Assert.That(Cross.Header("Access-Control-Allow-Origin") + Cross.Header("Access-Control-Allow-Credentials"), Is.Empty);
            Assert.That(M.Monitoring.Controller.View().Generation, Is.EqualTo(1L), "nothing started, stopped or restarted it");
            Assert.That(M.Monitoring.Controller.View().Status.IsRunning, Is.True);

            int Answered = 0;
            WebPage Last;
            do
            {
                Last = await Owner.Get("/monitoring?handler=progress&key=" + Key);
                Answered++;
            }
            while (Last.Status == HttpStatusCode.OK && Answered <= CampertronWebHost.RequestsPerClient + 1);
            Assert.That(Last.Status, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Answered, Is.LessThanOrEqualTo(CampertronWebHost.RequestsPerClient));
            Assert.That(Last.Text(), Does.Contain("Campertron received too many requests from this address."));
            Assert.That(Last.Header("Content-Security-Policy"), Is.EqualTo(Policy));
        }

        [Test]
        public async Task ThePageForItsScript_KeepsTheAddresssChoice_AndLeavesTheNoticeOfAnActionToTheUsersNextPage()
        {
            await using MonitoringHost M = await MonitoringHost.Start();
            WriteSearch(M, "Alpha.yaml", "First trip");
            WriteSearch(M, "Bravo.yaml", "Second trip");
            using Browser Owner = await M.Host.SignedIn();
            await Act(Owner, "start", await Owner.Get("/monitoring"));
            M.Waits.Await();
            WebPage Running = await Waiting(Owner);
            MonitoringControlCheck Check = M.Monitoring.Controller.View().LastCheck!;
            //an out-of-date Stop leaves a notice for the user's next page
            WebPage Posted = await Owner.Post("/monitoring?handler=stop", ("state", "9-running"));
            Assert.That(Posted.Status, Is.EqualTo(HttpStatusCode.Redirect));
            string Address = "?run=" + Check.Generation + "&check=" + Check.Number + "&search=1";
            WebPage View = await Owner.Get("/monitoring" + Address + "&handler=view");
            Assert.That(Element(View, "page-notice"), Is.Null, "the script's page leaves the notice to the user's");
            Assert.That(Said(View, "shown-heading"), Is.EqualTo("Second trip"), "the search the address chose");
            WebPage Next = await Owner.Get("/monitoring");
            Assert.That(Said(Next, "page-notice"), Is.EqualTo("Warning: " + WebMonitoringWording.StaleText));
            Assert.That(M.Monitoring.Controller.View().Status.IsRunning, Is.True);
        }
    }
}
