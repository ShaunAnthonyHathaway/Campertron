using System.Reflection;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;
using CampertronUnitTests.function.Base;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // v0.4.0's phase 14 (D131): the no-argument console's Search on the shared search service. CampsiteSearch's bounded
    // requests are the Search's (at most three per month, the 30-second timeout, HTTP 429 a block and no request after
    // one); what they bring is shown in the console's words (a month not checked is a warning, never a month without
    // availability; a block is the console's error, once, after the run, with its five-minute wait and exit code 1); the
    // Search is a manual search (Camp.History never read, written or re-armed); and nothing else reaches a request engine
    // of the console's own. ConsoleSearchParityTests compares it with the Search before the phase.
    [NonParallelizable]
    public class ConsoleSearchTests : ConsoleSearchTestBase
    {
        private static string MonthName(DateTime Month) => Month.ToString("Y", System.Globalization.CultureInfo.CurrentCulture);

        private (bool Shown, string Out, string Err) Search(OutputType Output = OutputType.UnitTest)
        {
            bool Shown = Output == OutputType.UnitTest ? Load.RunConsoleSearch(Settings(Output), true) : ConsoleSearch.Show(Settings(Output));
            (string Written, string Errors) = Take();
            return (Shown, Written, Errors);
        }

        private static string[] Lines(string Text) => Text.Split('\n').Where(L => L.Length > 0).ToArray();

        private static string Warning(string Search, string Message, string Campground, DateTime Month, params string[] Facts) =>
            "Warning: \"" + Search + "\": " + Message + " (" + string.Join(", ", new[] { Campground + ", " + MonthName(Month) }.Concat(Facts)) + ")";

        // ---- the shared bounds ----

        [Test]
        public void AMonthThatKeepsFailing_IsAskedThreeTimes_ThenItsWarning_AndTheOtherMonthStays()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(20));
            Gov.Script(Url(SearchFixture.Watchman, MonthB), (R, T) => new SearchResponse(503, "<html>unavailable</html>"));

            var Run = Search();

            Assert.That(Run.Shown, Is.True, "the results are shown");
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthB)), Is.EqualTo(3), "three requests, then no more");
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(1));
            Assert.That(Lines(Run.Err), Is.EqualTo(new[]
            {
                Warning("Watchman", "recreation.gov answered with an error, so this month was not checked.", "WATCHMAN CAMPGROUND", MonthB, "HTTP 503", "3 requests")
            }));
            Assert.That(Run.Out, Does.Contain(MonthName(MonthA) + " (* = open)"), "the month that was checked is shown");
            Assert.That(Run.Out, Does.Not.Contain(MonthName(MonthB) + " (* = open)").And.Not.Contain(MonthName(MonthB) + ": no matching availability."),
                "the month that was not checked is no month without availability");
            Assert.That(Run.Out, Does.Not.Contain("Checked \"Watchman\""), "a search is Checked only when every month was");
            Assert.That(Steps, Is.Empty, "no block: no wait and no exit");
        }

        [Test]
        public void TheRequestTimeout_IsThirtySeconds_AndAMonthThatNeverAnswersIsAWarning()
        {
            SearchHostOptions Production = ConsoleSearch.DefaultRunOptions();
            Assert.That((Production.RequestTimeout, Production.RetryDelay, Production.HeavyTrafficDelay, Production.MaxResponseBytes, Production.Transport, Production.KeepOutputValues),
                Is.EqualTo((TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1), 32 * 1024 * 1024, (SearchTransport?)null, true)), "CampsiteSearch's own bounds");
            Assert.That(NoLiveNetwork.ProductionSearchOptions!.Method.Name, Is.EqualTo(nameof(ConsoleSearch.DefaultRunOptions)), "production runs with them");
            Assert.That(SearchHostOptions.MaxAttempts, Is.EqualTo(3));

            RequestTimeout = TimeSpan.FromMilliseconds(200);
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) =>
            {
                T.WaitHandle.WaitOne(TimeSpan.FromSeconds(20));
                T.ThrowIfCancellationRequested();
                return new SearchResponse(200, "{}");
            });

            var Run = Search();

            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(3));
            Assert.That(Lines(Run.Err), Is.EqualTo(new[] { Warning("Watchman", "recreation.gov did not answer in time, so this month was not checked.", "WATCHMAN CAMPGROUND", MonthA, "3 requests") }));
            Assert.That(Run.Out, Does.Contain("Done: 0 searches, no matching availability."), "a search that could check no month is left to its warning");
        }

        [TestCase("{\"count\":0}", "recreation.gov's answer held no availability for the month, so this month was not checked.", "HTTP 200")]
        [TestCase("{\"error\":\"We are currently experiencing heavy traffic on our site. Please try again.\"}", "recreation.gov reported heavy traffic, so this month was not checked.", "HTTP 200")]
        [TestCase("not json at all", "recreation.gov's answer could not be read, so this month was not checked.", "HTTP 200")]
        public void AnAnswerTheConsoleRetriedForever_EndsAfterThreeRequests(string Body, string Message, string Status)
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(200, Body));

            Task<(bool, string, string)> Running = Task.Run(() => Search());
            Assert.That(Running.Wait(TimeSpan.FromSeconds(60)), Is.True, "the Search ends");

            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(3));
            Assert.That(Lines(Running.Result.Item3), Is.EqualTo(new[] { Warning("Watchman", Message, "WATCHMAN CAMPGROUND", MonthA, Status, "3 requests") }));
        }

        // ---- a block ----

        [Test]
        public void A429_IsTheBlock_NoFurtherRequest_TheConsolesErrorOnce_ItsWaitAndExitCode1()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthC.AddDays(10));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(429, "{\"error\":\"too many requests\"}"));

            var Run = Search();

            Assert.That(Run.Shown, Is.False, "after the block nothing more follows");
            Assert.That(Gov.Requests.Select(R => R.Url), Is.EqualTo(new[] { Url(SearchFixture.Watchman, MonthA) }), "no request after the block");
            Assert.That(Lines(Run.Err), Is.EqualTo(new[]
            {
                "Error: Blocked by API for exceeding request limit, try again later (WATCHMAN CAMPGROUND, " + MonthName(MonthA) + "). Campertron waits 5 minutes and then exits."
            }), "the console's error, once; no warning for the months the block left");
            Assert.That(Steps, Is.EqualTo(new[] { "wait 00:05:00", "exit 1" }));
            Assert.That(Run.Out, Does.Not.Contain("Done: "), "no results");
        }

        [Test]
        public void TheBlockPage_InTwoParallelSearches_IsOneErrorAfterTheRun()
        {
            WriteSearch("Mather.yaml", SearchFixture.Mather, MonthA, MonthB.AddDays(10));
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(10));
            const string Blocked = "<html><body><h1>Request blocked.</h1></body></html>";
            Gov.Script(Url(SearchFixture.Mather, MonthA), (R, T) => new SearchResponse(200, Blocked));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(200, Blocked));

            var Run = Search();

            string[] Errors = Lines(Run.Err);
            Assert.That(Errors, Has.Length.EqualTo(1), "one error, whatever the number of blocked requests");
            Assert.That(Errors[0], Does.Match(@"^Error: Blocked by API for exceeding request limit, try again later \([A-Z ]+, " + Regex.Escape(MonthName(MonthA)) + @"\)\. Campertron waits 5 minutes and then exits\.$"));
            Assert.That(Steps, Is.EqualTo(new[] { "wait 00:05:00", "exit 1" }));
            Assert.That(Gov.Requests.Select(R => R.Month), Has.None.EqualTo(MonthB), "the next months were never asked for");
        }

        // ---- a month not checked ----

        [Test]
        public void AnUncheckedMonth_IsNotLeftOutSilentlyNorUnavailable_AndTheCheckedMonthsAreAsAlways()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(20));
            var Fetched = Search();
            Gov = new GovStandIn();
            Gov.Script(Url(SearchFixture.Watchman, MonthB), (R, T) => new SearchResponse(404, "{\"error\":\"not found\"}"));

            var Missing = Search();

            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthB)), Is.EqualTo(1), "an error status that is not worth another attempt");
            Assert.That(Lines(Missing.Err), Is.EqualTo(new[] { Warning("Watchman", "recreation.gov answered with an error, so this month was not checked.", "WATCHMAN CAMPGROUND", MonthB, "HTTP 404", "1 request") }));
            //the first month's results as in the run that fetched both: every line of this run is one of that run's (but the
            //counts), the first month's calendar included
            string[] Counts = { "Done: ", " openings at ", " opening at " };
            Assert.That(Lines(Missing.Out).Where(L => !Counts.Any(L.Contains)), Is.SubsetOf(Lines(Fetched.Out)));
            Assert.That(Missing.Out, Does.Contain(MonthName(MonthA) + " (* = open)"));
            Assert.That(Lines(Missing.Out).Length, Is.LessThan(Lines(Fetched.Out).Length));
            Assert.That(Fetched.Out, Does.Contain(MonthName(MonthB) + " (* = open)"));
            Assert.That(Missing.Out, Does.Not.Contain(MonthName(MonthB) + " (* = open)").And.Not.Contain(MonthName(MonthB) + ": no matching availability."));
        }

        [Test]
        public void ASearchThatCouldCheckNoMonth_IsLeftToItsWarnings_NeverWithoutAvailability()
        {
            WriteSearch("Lost.yaml", SearchFixture.Mather, MonthA, MonthA.AddDays(5), DisplayName: "Lost");
            WriteSearch("Found.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5), DisplayName: "Found");
            Gov.Script(Url(SearchFixture.Mather, MonthA), (R, T) => new SearchResponse(500, "oops"));

            var Run = Search();

            Assert.That(Run.Out, Does.Contain("== Found ==").And.Not.Contain("== Lost =="));
            Assert.That(Run.Out, Does.Contain("Done: 1 search,"));
            Assert.That(Lines(Run.Err), Has.Length.EqualTo(1).And.All.StartWith("Warning: \"Lost\": recreation.gov answered with an error, so this month was not checked. ("));
        }

        // a problem of Campertron itself on the search path ends the Search with it, as it always did (Program.Main reports
        // it, exit code 1), never as a month of a search
        [Test]
        public void AnUnexpectedProblem_EndsTheSearchAsBefore()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => throw new InvalidCastException("the test's defect"));

            Assert.Throws<InvalidCastException>(() => Load.RunConsoleSearch(Settings(OutputType.UnitTest), true));

            (string Written, string Errors) = Take();
            Assert.That(Written, Does.Not.Contain("Done: "));
            Assert.That(Errors, Is.Empty);
        }

        // ---- a manual search ----

        [Test]
        public void CampHistory_IsNeverRead_WrittenOrRearmed_AlsoWithAMonthNotChecked()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(20));
            //markers for every night of both months, also the nights the checked month shows reserved (a monitoring would
            //re-arm those) and those of the month that is not checked
            foreach (DateTime Month in new[] { MonthA, MonthB })
            {
                foreach (string Site in SearchFixture.SiteIds(SearchFixture.Watchman, 8))
                {
                    for (int Day = 1; Day <= 28; Day++)
                    {
                        Directory.CreateDirectory(Path.Join(ConfigPath, "Camp.History", Month.Year.ToString(), Month.Month.ToString(), Day.ToString(), Site));
                    }
                }
            }
            List<string> History = Fingerprint(Path.Join(ConfigPath, "Camp.History"));
            Gov.Script(Url(SearchFixture.Watchman, MonthB), (R, T) => new SearchResponse(503, "busy"));

            var Run = Search();

            Assert.That(Fingerprint(Path.Join(ConfigPath, "Camp.History")), Is.EqualTo(History), "no marker written or removed");
            Assert.That(Run.Out, Does.Contain(MonthName(MonthA) + " (* = open)"), "the markers hide nothing");
        }

        // TD-65 is not corrected by this phase: consecutive nights are still found at one campsite only
        [Test]
        public void ConsecutiveNights_StillOneCampsite_AsBefore()
        {
            WriteSearch("Two.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(20), Consecutive: 2);
            List<string> Sites = SearchFixture.SiteIds(SearchFixture.Watchman, 8);
            string Body = SearchTestBase.MonthJson(MonthA, Sites, (Id, Day) => Id == Sites[0] && Day == 10 || Id == Sites[1] && Day == 11);
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(200, Body));

            var Shared = Search();
            Gov = new GovStandIn();
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(200, Body));
            LegacyConsoleSearch.Run(Settings(OutputType.UnitTest));
            (string Reference, _) = Take();

            Assert.That(Shared.Out, Does.Contain("Done: 1 search, no matching availability."), "the two nights at two campsites are no stay");
            Assert.That(Regex.Replace(Shared.Out, @" \(\d+\.\ds\)", ""), Is.EqualTo(Regex.Replace(Reference, @" \(\d+\.\ds\)", "")));
        }

        // ---- the architecture: the shared search is the Search's only request and result authority ----

        [Test]
        public void TheSearch_RequestsOnlyThroughTheSharedTransport_NeverTheConsolesDownload()
        {
            var Downloaded = new List<string>();
            AvailabilityApi.ConsoleDownload = Url =>
            {
                lock (Downloaded)
                {
                    Downloaded.Add(Url);
                }
                return NoLiveNetwork.EmptyMonth;
            };
            WriteSearch("A.yaml", SearchFixture.Mather, MonthA, MonthB.AddDays(10));
            WriteSearch("B.yaml", SearchFixture.Watchman, MonthA, MonthC.AddDays(10), Consecutive: 2);
            foreach (OutputType Output in new[] { OutputType.UnitTest, OutputType.Console, OutputType.HtmlFile })
            {
                Search(Output);
            }
            Assert.That(Downloaded, Is.Empty, "the console's own request path is never reached from the Search");
            Assert.That(Gov.Requests, Is.Not.Empty);
        }

        private static List<string> Referenced(IEnumerable<Type> Types)
        {
            var Called = new List<string>();
            const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Type T in Types)
            {
                foreach (MethodBase Method in T.GetMethods(Declared).Cast<MethodBase>().Concat(T.GetConstructors(Declared)))
                {
                    Called.AddRange(YamlDocumentPreservationTests.ReferencedMembers(Method).Select(M => ((M as Type)?.FullName ?? M.DeclaringType?.FullName ?? "?") + "::" + M.Name));
                }
            }
            return Called.Distinct().ToList();
        }

        // a type and the types the compiler made for its lambdas and iterators
        private static IEnumerable<Type> WithNested(Type Outer) =>
            Outer.Assembly.GetTypes().Where(T => T == Outer || T.FullName!.StartsWith(Outer.FullName + "+", StringComparison.Ordinal));

        [Test]
        public void TheAdapter_HasNoRequestResultOrHistoryEngineOfItsOwn()
        {
            List<string> Called = Referenced(WithNested(typeof(ConsoleSearch)));
            string[] Forbidden =
            {
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetAvailabilitiesByCampground", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetData",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::GetWebApiData", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_ConsoleDownload",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::ProcessMonth", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::FilterData",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::FilterConsecutiveDays", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_BlockedWait",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_ExitProcess",
                "CampertronLibrary.function.RecDotOrg.data.Load::ReportResults", "CampertronLibrary.function.RecDotOrg.data.Load::HasUnreportedEntries",
                "CampertronLibrary.function.RecDotOrg.data.Load::HasNewEntries", "CampertronLibrary.function.RecDotOrg.data.Load::RecordNotificationHistory",
                "CampertronLibrary.function.RecDotOrg.data.Load::HasNotificationMarker", "CampertronLibrary.function.RecDotOrg.data.Load::UsesNotificationHistory",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareRun", "CampertronLibrary.function.RecDotOrg.data.Load::RegenerateSearchDates",
                "CampertronLibrary.function.RecDotOrg.data.NotificationHistory", "CampertronLibrary.function.RecDotOrg.data.Cache::CheckCache", "CampertronLibrary.function.RecDotOrg.data.Data::",
                "CampertronLibrary.function.RecDotOrg.search.SearchHost::", "CampertronLibrary.function.RecDotOrg.search.HttpSearchTransport", "CampertronLibrary.function.RecDotOrg.search.SearchFilters",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::OpeningsOf", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::SitesOf",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::LoadEntry", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::PlannedMonths",
                "CampertronLibrary.function.RecDotOrg.monitoring.", "CampertronLibrary.function.Forms.", "CampertronLibrary.function.Interactive.",
                "CampertronLibrary.function.Base.Notifications", "CampertronLibrary.function.Base.NtfyPublisher", "CampertronLibrary.function.Base.CampsiteConfig::SendSearchEmail",
                "System.Threading.Thread::", "System.Threading.Tasks.Parallel", "System.Net.", "Newtonsoft.", "System.Text.Json."
            };
            Assert.That(Called.Where(C => Forbidden.Any(F => C.StartsWith(F, StringComparison.Ordinal)) || C == "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Plan"), Is.Empty);
            foreach (string Needed in new[]
            {
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::PlanLoaded", "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Run",
                "CampertronLibrary.function.RecDotOrg.search.CampsiteSearch::Message", "CampertronLibrary.function.Base.Yaml::CampertronConfigGetConfigs",
                "CampertronLibrary.function.RecDotOrg.data.Load::PrepareCampgrounds", "CampertronLibrary.function.RecDotOrg.data.Load::WriteScope",
                "CampertronLibrary.function.RecDotOrg.data.Load::CheckedText", "CampertronLibrary.function.RecDotOrg.data.Load::OutputResults",
                "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::ConsoleBlocked", "CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::DescribeRequest",
                "CampertronLibrary.function.Base.Menu::Init", "CampertronLibrary.function.Base.ConsoleUi::WaitForKey"
            })
            {
                Assert.That(Called, Does.Contain(Needed));
            }
        }

        [Test]
        public void TheMenusSearch_ReachesTheAdapterOnly_AndTheLegacyLoopIsGone()
        {
            MethodInfo Search = typeof(Load).GetMethod(nameof(Load.RunConsoleSearch), new[] { typeof(CampertronInternalConfig), typeof(bool) })!;
            List<string> Called = YamlDocumentPreservationTests.ReferencedMembers(Search).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.data.ConsoleSearch::Run"));
            Assert.That(Called, Does.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringConsole::Run"), "email and ntfy mode as before");
            Assert.That(Called.Where(C => C.StartsWith("CampertronLibrary.function.RecDotOrg.api.", StringComparison.Ordinal) || C.StartsWith("System.Threading", StringComparison.Ordinal)
                || C.Contains("::ReportResults") || C.Contains("::OutputResults")), Is.Empty, "no loop, request or output of its own");
            string[] Gone = { "Preload", "Prepare", "PostProcess" };
            Assert.That(typeof(Load).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Select(M => M.Name).Where(N => Gone.Contains(N)), Is.Empty);

            //the menu's Enter on Search is unchanged: the Search, then the menu again (ER-21's calls)
            MethodInfo Input = typeof(Menu).GetMethod("ProcessInput", BindingFlags.Static | BindingFlags.NonPublic)!;
            List<string> FromMenu = YamlDocumentPreservationTests.ReferencedMembers(Input).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
            Assert.That(FromMenu, Does.Contain("CampertronLibrary.function.RecDotOrg.data.Load::RunConsoleSearch")
                .And.Contain("CampertronLibrary.function.RecDotOrg.api.RefreshRidbRecreationData::RefreshData")
                .And.Contain("CampertronLibrary.function.Base.Menu::ViewConfigLines"));
            Assert.That(FromMenu.Where(C => C.Contains("data.ConsoleSearch::") || C.Contains("search.CampsiteSearch::")), Is.Empty, "the menu calls the Search as before");
        }

        [Test]
        public void TheSharedPolicy_IsOneBoundedPolicy_AndTheMonitoringKeepsItsOwnRequestPath()
        {
            //no second policy in the shared search: no console request path, sleep or console output in its host or service
            List<string> Shared = Referenced(WithNested(typeof(CampsiteSearch)).Concat(WithNested(typeof(SearchHost))));
            Assert.That(Shared.Where(C => C.Contains("AvailabilityApi::GetWebApiData") || C.Contains("AvailabilityApi::GetData") || C.Contains("AvailabilityApi::get_ConsoleDownload")
                || C.Contains("AvailabilityApi::ConsoleBlocked") || C.StartsWith("System.Threading.Thread::", StringComparison.Ordinal) || C.StartsWith("CampertronLibrary.function.Base.ConsoleUi", StringComparison.Ordinal)
                || C.StartsWith("CampertronLibrary.function.Base.ConsoleResults", StringComparison.Ordinal)), Is.Empty);

            //the monitoring's request path is GetWebApiData, as before: its download, its answer to a block, its waits
            MethodInfo Web = typeof(AvailabilityApi).GetMethod(nameof(AvailabilityApi.GetWebApiData))!;
            List<string> FromWeb = YamlDocumentPreservationTests.ReferencedMembers(Web).Select(M => (M.DeclaringType?.FullName ?? "?") + "::" + M.Name).ToList();
            Assert.That(FromWeb, Does.Contain("CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::get_ConsoleDownload")
                .And.Contain("CampertronLibrary.function.RecDotOrg.monitoring.MonitoringRun::get_Blocked")
                .And.Contain("CampertronLibrary.function.RecDotOrg.api.AvailabilityApi::ConsoleBlocked"));
            Assert.That(Int32Constants(Web), Does.Contain(30000).And.Contain(1000), "its 30-second and 1-second waits");
            MethodInfo Month = typeof(AvailabilityApi).GetMethod("ProcessMonth", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.That(YamlDocumentPreservationTests.ReferencedMembers(Month).Select(M => M.Name), Does.Contain(nameof(AvailabilityApi.GetData)), "a check without a search host requests through it");

            //the monitoring never requests through the shared search or the console's Search (it shapes a check's openings
            //with the shared search's OpeningsOf and SitesOf since D98, as before)
            IEnumerable<Type> Monitoring = typeof(Load).Assembly.GetTypes().Where(T => T.Namespace == "CampertronLibrary.function.RecDotOrg.monitoring"
                || T.Namespace == "CampertronLibrary.function.Forms" && T.Name.StartsWith("Monitoring", StringComparison.Ordinal));
            string[] Requesting = { "search.CampsiteSearch::Run", "search.CampsiteSearch::Plan", "search.SearchHost", "search.HttpSearchTransport", "data.ConsoleSearch" };
            Assert.That(Referenced(Monitoring).Where(C => Requesting.Any(C.Contains)), Is.Empty);
        }

        // the other hosts never reach the no-argument console's Search or its presentation
        [Test]
        public void TheOtherHosts_NeverReachTheConsolesSearch()
        {
            IEnumerable<Type> Others = typeof(Load).Assembly.GetTypes().Where(T => T.Namespace != null &&
                (T.Namespace.StartsWith("CampertronLibrary.function.Interactive", StringComparison.Ordinal) || T.Namespace == "CampertronLibrary.function.Forms"
                 || T.Namespace.StartsWith("CampertronLibrary.function.WebAccess", StringComparison.Ordinal)));
            Assert.That(Referenced(Others).Where(C => C.Contains("ConsoleSearch") || C.Contains("CampsiteSearch::PlanLoaded") || C.Contains("::set_KeepOutputValues")), Is.Empty);
        }

        // the int32 constants a method loads (ldc.i4)
        private static List<int> Int32Constants(MethodBase Method)
        {
            byte[] IL = Method.GetMethodBody()!.GetILAsByteArray()!;
            var Found = new List<int>();
            for (int i = 0; i + 4 < IL.Length; i++)
            {
                if (IL[i] == 0x20)
                {
                    Found.Add(BitConverter.ToInt32(IL, i + 1));
                }
            }
            return Found;
        }
    }

    // the shared search's data option for the console (D131) and its plan of searches already loaded, at the service:
    // nothing written to the console (SearchTestBase checks it)
    public class CampsiteSearchConsoleDataTests : SearchTestBase
    {
        private static string Spelled(IEnumerable<ConsoleConfig.ConsoleConfigValue> Values) =>
            string.Join("\n", Values.Select(V => V.ConfigType + "|" + V.Color + "|" + V.Kind + "|" + V.ConfigValue + "|" + V.Metadata?.CampsiteID + "|" + V.Metadata?.Date?.ToString("yyyy-MM-dd")));

        [Test]
        public void ARunKeepsNoStream_ByDefault_AndTheKeptStreamIsTheSearchsOwn_WithTheSameResults()
        {
            WriteSearch("Trip.yaml", SearchFixture.Watchman, MonthA, MonthB.AddDays(12));
            var Months = new Dictionary<string, string>();
            foreach (DateTime Month in new[] { MonthA, MonthB })
            {
                string Body = MonthJson(Month, SearchFixture.SiteIds(SearchFixture.Watchman, 8), (Id, Day) => Day % 4 == 0);
                Gov.Json(Url(SearchFixture.Watchman, Month), Body);
                Months[Url(SearchFixture.Watchman, Month)] = Body;
            }

            SearchRunResult Default = RunOf(PlanOf("Trip.yaml"));
            SearchRunResult Kept = RunOf(PlanOf("Trip.yaml"), new SearchHostOptions { Transport = Gov.Send, RetryDelay = TimeSpan.Zero, HeavyTrafficDelay = TimeSpan.Zero, KeepOutputValues = true });

            Assert.That(new SearchHostOptions().KeepOutputValues, Is.False, "every other host keeps none");
            Assert.That(Default.Searches.Select(S => S.OutputValues), Has.All.Null);
            Assert.That(Keys(Kept.Searches[0].Openings), Is.EqualTo(Keys(Default.Searches[0].Openings)), "the same typed results");
            Assert.That(Kept.Searches[0].Months, Is.EqualTo(Default.Searches[0].Months));
            Assert.That(Kept.Searches[0].State, Is.EqualTo(Default.Searches[0].State));
            Assert.That(Spelled(Kept.Searches[0].OutputValues!), Is.EqualTo(Spelled(ConsoleStream("Trip.yaml", OutputType.HtmlFile, Months))), "the stream the console's search writes");
            Assert.That(Kept.Searches[0].OutputValues!.Any(V => V.Kind == ConsoleConfig.OutputKind.Availability), Is.True);
        }

        [Test]
        public void PlanLoaded_TakesTheSearchesAsLoaded_AndReadsNothing()
        {
            CampertronConfig Ready = Yaml.LoadSettingsText<CampertronConfig>("displayName: Ready\nautoRun: true\ncampgroundID: '" + SearchFixture.Watchman + "'\nsearchBy: DaysOut\nsearchValue: 7\n")!;
            Ready.GenerateSearchData(DateTime.Now);
            List<DateTime> Dates = Ready.GetSearchDates();
            CampertronConfig NoCampground = Yaml.LoadSettingsText<CampertronConfig>("displayName: None\nautoRun: true\nsearchBy: DaysOut\nsearchValue: 7\n")!;

            SearchRunPlan Plan = CampsiteSearch.PlanLoaded(ConfigPath, SearchFixture.CachePath, new[] { ("Ready.yaml", Ready), ("None.yaml", NoCampground) }, DateTime.Now);

            Assert.That(Plan.Issues, Is.Empty);
            Assert.That(Plan.Entries.Select(E => (E.FileName, E.State)), Is.EqualTo(new[] { ("Ready.yaml", SearchPlanState.Ready), ("None.yaml", SearchPlanState.Skipped) }));
            Assert.That(Plan.Entries[0].Search, Is.SameAs(Ready), "the loaded search itself");
            Assert.That(Ready.GetSearchDates(), Is.EqualTo(Dates), "its dates as they were generated");
            Assert.That(Plan.Entries[1].Issues.Single().Code, Is.EqualTo(SearchIssueCode.CampgroundIdMissing));
            Assert.That(Directory.GetFileSystemEntries(ConfigPath), Is.Empty, "no file was needed, read or written");

            SearchRunPlan Unusable = CampsiteSearch.PlanLoaded(ConfigPath, ConfigPath, new[] { ("Ready.yaml", Ready) }, DateTime.Now);
            Assert.That(Unusable.Issues.Single().Code, Is.EqualTo(SearchIssueCode.InvalidRequest));
            Assert.That(Unusable.Entries.Single().State, Is.EqualTo(SearchPlanState.Invalid));
        }
    }
}
