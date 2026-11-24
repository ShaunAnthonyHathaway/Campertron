using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using CampertronLibrary.function.RecDotOrg.search;
using static CampertronWebTests.ResultsLiveTests;
using static CampertronWebTests.ResultsPageTests;

namespace CampertronWebTests
{
    // The web UI's rule for its long-running operations (D109; business-rules-settings §39): a page that shows an operation
    // the user started and that goes on (a search, the refresh of the campground data, the monitoring) updates by itself,
    // with the web UI's one script, and never asks the user to show the latest state again and again to learn whether it
    // still runs. These tests keep the pattern D108 and D109 replaced from coming back: no words that say a page does not
    // update by itself, no page with a progress bar or a "Show the latest …" link without the live parts, no page model of
    // a live page without its answer and its page for the script, one script in the whole web UI. And a page's script
    // never keeps a browser signed in: its requests check the session without renewing its idle time, on every live page.
    public class LivePagesTests
    {
        private static string RepositoryRoot()
        {
            string Root = TestContext.CurrentContext.TestDirectory;
            while (!File.Exists(Path.Join(Root, "Campertron.sln")))
            {
                Root = Path.GetDirectoryName(Root) ?? throw new AssertionException("the repository was not found above the test folder");
            }
            return Root;
        }

        // the web UI's code and pages and the shared form layer, whose words the pages show
        private static IEnumerable<string> Sources()
        {
            string Root = RepositoryRoot();
            foreach (string Folder in new[] { "CampertronWeb", Path.Join("CampertronLibrary", "function", "Forms") })
            {
                foreach (string File_ in Directory.EnumerateFiles(Path.Join(Root, Folder), "*", SearchOption.AllDirectories))
                {
                    string Relative = Path.GetRelativePath(Root, File_).Replace('\\', '/');
                    if (Relative.Contains("/bin/", StringComparison.Ordinal) || Relative.Contains("/obj/", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (Relative.EndsWith(".cs", StringComparison.Ordinal) || Relative.EndsWith(".cshtml", StringComparison.Ordinal) || Relative.EndsWith(".js", StringComparison.Ordinal))
                    {
                        yield return File_;
                    }
                }
            }
        }

        // the words of the pattern D108 and D109 replaced: a page that says it does not update by itself, or that sends the
        // user to the manual way to learn when something has happened
        private static readonly Regex ManualOnly = new Regex(
            "(does not|doesn't|do not|don't|never|won't|will not|cannot|can't) updates? (by|on) (itself|its own)|update (it|the page) (yourself|by hand|manually)|"
            + "Show the latest (progress|status) to see when|Show the latest (progress|status), then try again|(reload|refresh) (the|this) page to see",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [Test]
        public void NoWordsOfTheWebUI_SayAPageDoesNotUpdateByItself_OrSendTheUserToTheManualWayToLearnWhenSomethingHappened()
        {
            var Found = new List<string>();
            int Files = 0;
            foreach (string File_ in Sources())
            {
                Files++;
                string[] Lines = File.ReadAllLines(File_);
                for (int i = 0; i < Lines.Length; i++)
                {
                    if (ManualOnly.IsMatch(Lines[i]))
                    {
                        Found.Add(Path.GetFileName(File_) + ":" + (i + 1) + ": " + Lines[i].Trim());
                    }
                }
            }
            Assert.That(Files, Is.GreaterThan(50), "the web UI's files were read");
            Assert.That(Found, Is.Empty, "the pattern D109 replaced, back in the web UI's words");
            Assert.That(ManualOnly.IsMatch("This page does not update by itself: select Show the latest progress to see how the refresh is going."), Is.True, "the check knows the words it guards against");
            Assert.That(ManualOnly.IsMatch("Start, Stop and Restart are unavailable while the monitoring is starting or stopping. Show the latest status to see when it has."), Is.True);
            //what the manual way says now is only #live's text before the script runs (and in a browser that runs none)
            foreach (WebLiveWords Words in new[] { WebResultsLiveWording.Words, WebSystemWording.Live, WebMonitoringWording.Live })
            {
                Assert.That(Words.Manual, Does.Match("^Select Show the latest (progress|status) to see how the (search|refresh|monitoring) is going\\.$"));
                Assert.That(Words.Updating, Does.Match("^This page updates by itself while the (search|refresh|monitoring) runs\\. Last update: \\{time\\}\\.$"));
                Assert.That(Words.Failing, Does.Contain("(last update: {time})").And.Contain("That does not stop the").And.Contain("the page keeps trying"));
            }
        }

        [Test]
        public void EveryPageWithAProgressBarOrTheManualWay_IsALivePage_AndEveryLivePageModel_HasItsAnswerAndItsPageForTheScript()
        {
            string Pages = Path.Join(RepositoryRoot(), "CampertronWeb", "Pages");
            var Live = new List<string>();
            foreach (string File_ in Directory.EnumerateFiles(Pages, "*.cshtml", SearchOption.AllDirectories))
            {
                string Text = File.ReadAllText(File_);
                string Name = Path.GetFileName(File_);
                bool Progress = Text.Contains("<progress", StringComparison.Ordinal);
                bool Manual = Text.Contains("id=\"show-latest\"", StringComparison.Ordinal) || Regex.IsMatch(Text, ">\\s*Show the latest", RegexOptions.CultureInvariant)
                    || Text.Contains(".ShowLatest<", StringComparison.Ordinal) || Text.Contains("ShowLatest</a>", StringComparison.Ordinal);
                if (Progress || Manual)
                {
                    Live.Add(Name);
                    Assert.That(Text, Does.Contain("@await Html.PartialAsync(\"_Live\", Live)"), Name + ": its #live for the web UI's script");
                    Assert.That(Text, Does.Contain("<script src=\"@WebAssets.LiveScriptPath\" defer=\"defer\"></script>"), Name + ": the web UI's one script while its operation goes on");
                    Assert.That(Text, Does.Contain("<div id=\"live-view\">"), Name + ": the part the script puts in place");
                    Assert.That(Text, Does.Contain("<p id=\"live-announce\" class=\"visually-hidden\" role=\"status\" aria-live=\"polite\" aria-atomic=\"true\"></p>"), Name + ": its announcements");
                }
                //no other script: only the web UI's own file, by its address
                foreach (Match Script in Regex.Matches(Text, "<script[^>]*>", RegexOptions.IgnoreCase))
                {
                    Assert.That(Script.Value, Is.EqualTo("<script src=\"@WebAssets.LiveScriptPath\" defer=\"defer\">"), Name + ": only the web UI's one script");
                }
            }
            Assert.That(Live.Order(StringComparer.Ordinal), Is.EqualTo(new[] { "Monitoring.cshtml", "Results.cshtml", "System.cshtml" }), "the pages of the operations that go on");

            //a live page's model answers its script and gives it the page (its two handlers); no other page model does
            var Models = typeof(WebLive).Assembly.GetTypes().Where(T => typeof(Microsoft.AspNetCore.Mvc.RazorPages.PageModel).IsAssignableFrom(T)).ToList();
            Assert.That(Models, Has.Count.GreaterThan(5));
            foreach (Type Model in Models)
            {
                bool HasLive = Model.GetProperty("Live", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.PropertyType == typeof(WebLiveView);
                bool HasProgress = Model.GetMethod("OnGetProgressAsync") != null;
                bool HasView = Model.GetMethod("OnGetViewAsync") != null;
                Assert.That((HasProgress, HasView), Is.EqualTo((HasLive, HasLive)), Model.Name + ": a live page has its answer and its page for the script, and only a live page");
            }
            Assert.That(Models.Count(M => M.GetMethod("OnGetProgressAsync") != null), Is.EqualTo(3));

            //one script in the whole web UI: the embedded file the host serves for the owner only
            Assert.That(typeof(WebLive).Assembly.GetManifestResourceNames().Where(N => N.EndsWith(".js", StringComparison.Ordinal)), Is.EqualTo(new[] { "CampertronWeb.Assets.live.js" }));
            Assert.That(Directory.EnumerateFiles(Path.Join(RepositoryRoot(), "CampertronWeb"), "*.js", SearchOption.AllDirectories)
                .Select(F => Path.GetRelativePath(RepositoryRoot(), F).Replace('\\', '/')).Where(F => !F.Contains("/bin/") && !F.Contains("/obj/")), Is.EqualTo(new[] { "CampertronWeb/Assets/live.js" }));
        }

        [Test]
        public void TheScriptsRequests_AreTheGetsOfALivePagesTwoHandlers_AndNothingElse()
        {
            static bool Observes(string Method, string Query)
            {
                var Context = new DefaultHttpContext();
                Context.Request.Method = Method;
                Context.Request.QueryString = new QueryString(Query);
                return WebLive.IsObservation(Context.Request);
            }
            Assert.That(Observes("GET", "?handler=progress&key=x"), Is.True);
            Assert.That(Observes("HEAD", "?handler=progress"), Is.True);
            Assert.That(Observes("GET", "?handler=view"), Is.True);
            Assert.That(Observes("GET", "?handler=PROGRESS"), Is.True, "Razor Pages takes a handler's name whatever its case");
            Assert.That(Observes("GET", "?handler=start&handler=view"), Is.True, "any of its handlers: stricter, never laxer");
            Assert.That(Observes("POST", "?handler=progress"), Is.False, "an action is the user's");
            Assert.That(Observes("POST", "?handler=view"), Is.False);
            Assert.That(Observes("GET", ""), Is.False, "a page the user asks for renews the session");
            Assert.That(Observes("GET", "?handler=start"), Is.False);
            Assert.That(Observes("GET", "?progress=1&view=1"), Is.False);
        }

        // ---- the session: a page's script never keeps it alive ----

        [TestCase("results")]
        [TestCase("system")]
        [TestCase("monitoring")]
        public async Task APagesScript_NeverRenewsTheSession_OnlyWhatTheUserDoesKeepsItAlive(string Page)
        {
            var Seams = new MonitoringPageTests();
            Seams.SetUp();
            try
            {
                await NeverRenews(Page);
            }
            finally
            {
                Seams.TearDown();
            }
        }

        private static async Task NeverRenews(string Page)
        {
            WebTestHost Host;
            IAsyncDisposable Owned;
            string Address;
            Func<Browser, Task<string>> Begin;
            var Steer = new Steered();
            switch (Page)
            {
                case "results":
                {
                    ResultsHost R = await ResultsHost.Start(Run: Steer.Run);
                    File.WriteAllText(R.In("Alpha.yaml"), Search("First trip"));
                    (Host, Owned, Address) = (R.Host, R, "/results");
                    Begin = async Who => KeyOf(await StartRun(Who));
                    break;
                }
                case "system":
                {
                    SystemPageTests.SystemHost S = await SystemPageTests.SystemHost.Start();
                    (Host, Owned, Address) = (S.Host, S, SystemPageTests.Address);
                    Begin = async Who =>
                    {
                        await SystemPageTests.StartRefresh(Who);
                        S.Fake.Await(S.Fake.Downloading, "the refresh started");
                        return KeyOf(await Who.Get(SystemPageTests.Address));
                    };
                    break;
                }
                default:
                {
                    MonitoringPageTests.MonitoringHost M = await MonitoringPageTests.MonitoringHost.Start();
                    MonitoringPageTests.WriteSearch(M, "Alpha.yaml", "First trip");
                    (Host, Owned, Address) = (M.Host, M, "/monitoring");
                    Begin = async Who =>
                    {
                        await MonitoringPageTests.Act(Who, "start", await Who.Get("/monitoring"));
                        M.Waits.Await();
                        return KeyOf(await MonitoringPageTests.Waiting(Who));
                    };
                    break;
                }
            }
            try
            {
                using Browser Watcher = await Host.SignedIn();
                using Browser User = await Host.SignedIn();
                string Key = await Begin(Watcher);
                await Watcher.Get(Address);
                await User.Get(Address);

                //almost the whole idle time later: the watcher's page only asks; the user asks for a page
                Host.Now += WebSessions.IdleTimeout - TimeSpan.FromMinutes(1);
                for (int i = 0; i < 3; i++)
                {
                    (WebPage Asked, _) = await AskAt(Watcher, Address, Key);
                    Assert.That(Asked.Status, Is.EqualTo(HttpStatusCode.OK));
                    WebPage View = await Watcher.Get(Address + "?handler=view");
                    Assert.That(View.Status, Is.EqualTo(HttpStatusCode.OK), "the page for its script, still signed in");
                    Assert.That((await Watcher.Send(new HttpRequestMessage(HttpMethod.Head, Address + "?handler=progress&key=" + Key))).Status, Is.EqualTo(HttpStatusCode.OK));
                }
                Assert.That((await User.Get(Address)).Status, Is.EqualTo(HttpStatusCode.OK));

                //two minutes on: past the idle time since the watcher's last own request, whatever its page asked meanwhile
                Host.Now += TimeSpan.FromMinutes(2);
                WebPage After = await Watcher.Get(Address + "?handler=progress&key=" + Key);
                Assert.That((After.Status, After.Location?.StartsWith("/signin", StringComparison.Ordinal)), Is.EqualTo((HttpStatusCode.Redirect, (bool?)true)),
                    Page + ": the page's questions never kept the session alive");
                Assert.That(After.Body, Does.Not.Contain(Key));
                Assert.That((await Watcher.Get(Address)).Status, Is.EqualTo(HttpStatusCode.Redirect), "the session has ended");
                //the user's own request renewed theirs
                (WebPage Still, JsonElement Answer) = await AskAt(User, Address, Page == "results" ? null : Key);
                Assert.That(Still.Status, Is.EqualTo(HttpStatusCode.OK), Page + ": what the user did kept the session alive");
                Assert.That(State(Answer), Is.EqualTo(Page == "results" ? "replaced" : "running"), "another session's run is never this one's; the web UI's refresh and monitoring are every session's");
            }
            finally
            {
                await Owned.DisposeAsync();
            }
        }
    }
}
