using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronUnitTests.function.RecDotOrg.search
{
    // v0.4.0's phase 14: the no-argument console's Search (the menu's Search, Load.RunConsoleSearch(config, true)) side by
    // side with LegacyConsoleSearch, the Search as it was before the phase, on the same search files, campground cache,
    // recreation.gov answers and Camp.History. A run whose every month is fetched writes the same bytes (but a search's
    // time and the order its parallel searches end in), asks for the same months and changes the same files; the
    // reference's own behavior after a block, heavy traffic and an error status is pinned here as the record of what the
    // phase changed on purpose (D131).
    [NonParallelizable]
    public class ConsoleSearchParityTests : ConsoleSearchTestBase
    {
        // ---- the scenarios: search files, written as a user would ----

        public static readonly string[] Scenarios = { "one search", "several searches", "no availability", "a skipped search", "a shared month", "the example search" };

        private void Write(string Scenario)
        {
            switch (Scenario)
            {
                case "one search":
                    WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA.AddDays(4), MonthB.AddDays(19));
                    break;
                case "several searches":
                    //display names out of the files' order: the output is ordered by display name
                    WriteSearch("A-file.yaml", SearchFixture.Mather, MonthA, MonthB.AddDays(9), DisplayName: "Zeta at Mather");
                    WriteSearch("B-file.yaml", SearchFixture.Watchman, MonthB, MonthC.AddDays(14), DisplayName: "Alpha at Watchman");
                    WriteSearch("C-file.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(20), Consecutive: 2, DisplayName: "Middle two nights");
                    WriteSearch("D-file.yaml", SearchFixture.Mather, MonthA, MonthC, AutoRun: false, DisplayName: "Not enabled");
                    break;
                case "no availability":
                    WriteSearch("Reserved.yaml", SearchFixture.Mather, MonthA, MonthB.AddDays(5));
                    break;
                case "a skipped search":
                    //the console's step that prepares each campground's cache reads the campground data for a search without a
                    //campgroundID too (it finds nothing): a copy of the test run's database
                    File.Copy(Path.Join(SearchFixture.InstalledConfigPath, "RecreationDotOrg.db"), Path.Join(ConfigPath, "RecreationDotOrg.db"));
                    File.WriteAllText(Path.Join(ConfigPath, "NoCampground.yaml"), "displayName: No campground\nautoRun: true\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: 7\n");
                    WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(12));
                    break;
                case "a shared month":
                    WriteSearch("First.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(10), DisplayName: "First");
                    WriteSearch("Second.yaml", SearchFixture.Watchman, MonthA.AddDays(5), MonthA.AddDays(25), DisplayName: "Second");
                    break;
                case "the example search":
                    //no search file: the Search creates ZionConfig.yaml and runs it
                    break;
            }
        }

        // recreation.gov's answers of a scenario (the default months unless scripted here)
        private void Answer(string Scenario)
        {
            if (Scenario == "no availability")
            {
                foreach (DateTime Month in new[] { MonthA, MonthB })
                {
                    string Body = SearchTestBase.MonthJson(Month, SearchFixture.SiteIds(SearchFixture.Mather, 8), (Id, Day) => false);
                    Gov.Script(Url(SearchFixture.Mather, Month), (R, T) => new SearchResponse(200, Body));
                }
            }
        }

        // between the two runs of a scenario: a stand-in with the same answers and no request yet, the campground cache
        // as it was, and the example search the first run created gone again, so the second run creates it too
        private void Reset(string Scenario)
        {
            if (Scenario == "the example search")
            {
                File.Delete(Path.Join(ConfigPath, "ZionConfig.yaml"));
            }
            //the campground cache as the first run found it (a run prepares what is missing in it)
            Directory.Delete(CachePath, true);
            Directory.CreateDirectory(CachePath);
            foreach (string File in Directory.GetFiles(SearchFixture.CachePath))
            {
                System.IO.File.Copy(File, Path.Join(CachePath, Path.GetFileName(File)));
            }
            Gov = new GovStandIn();
            Answer(Scenario);
        }

        // the search files of the settings folder, each with its size, time and content hash
        private List<string> SearchFiles() => Fingerprint(ConfigPath).Where(F => F.Split(' ')[0].EndsWith(".yaml", StringComparison.Ordinal)).ToList();

        // the menu's Search with outputTo: UnitTest, which returns after its run: the whole path from the search files to
        // the results, as the menu runs it
        private (string Out, string Err, List<string> Requests) RunTheMenusSearch()
        {
            Assert.That(Load.RunConsoleSearch(Settings(OutputType.UnitTest), true), Is.True, "a UnitTest run returns after its output");
            (string Written, string Errors) = Take();
            return (Written, Errors, RequestedUrls());
        }

        private (string Out, string Err, List<string> Requests) RunTheReference(OutputType Output)
        {
            LegacyConsoleSearch.Run(Settings(Output));
            (string Written, string Errors) = Take();
            return (Written, Errors, RequestedUrls());
        }

        [TestCaseSource(nameof(Scenarios))]
        public void AFullyFetchedRun_IsTheReferenceByteForByte(string Scenario)
        {
            Write(Scenario);
            Answer(Scenario);
            List<string> Before = SearchFiles();

            var Shared = RunTheMenusSearch();
            List<string> AfterShared = SearchFiles();
            Reset(Scenario);
            var Reference = RunTheReference(OutputType.UnitTest);
            List<string> AfterReference = SearchFiles();

            Assert.That(Normalized(Shared.Out), Is.EqualTo(Normalized(Reference.Out)), "standard output");
            Assert.That(Shared.Err, Is.EqualTo(Reference.Err), "standard error");
            Assert.That(Shared.Requests, Is.EqualTo(Reference.Requests), "the same months asked for");
            Assert.That(Shared.Out, Does.Contain("Done: "), "the run's summary");
            Assert.That(Steps, Is.Empty, "no block: no wait and no exit");
            Assert.That(Directory.Exists(Path.Join(ConfigPath, "Camp.History")), Is.False, "a manual search never creates Camp.History");
            if (Scenario == "the example search")
            {
                Assert.That(AfterShared.Count(F => F.StartsWith("ZionConfig.yaml ", StringComparison.Ordinal)), Is.EqualTo(1), "the example search was created");
            }
            else
            {
                Assert.That(AfterShared, Is.EqualTo(Before), "no search file changed");
                Assert.That(AfterReference, Is.EqualTo(Before));
            }
        }

        // ---- the contract both keep ----

        // an enabled search the start refuses refuses the whole run: the same error, before any request or output, the
        // valid search not run and no file changed
        [Test]
        public void AnInvalidEnabledSearch_RefusesTheWholeRun_AsBefore()
        {
            WriteSearch("Valid.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(10));
            //a negative searchValue: refused at a start (SearchValueNegative)
            File.WriteAllText(Path.Join(ConfigPath, "Invalid.yaml"), "displayName: Invalid\nautoRun: true\ncampgroundID: '232490'\ntotalHumans: 2\nsearchBy: DaysOut\nsearchValue: -1\n");
            List<string> Before = Fingerprint(ConfigPath);

            Exception Shared = Assert.Catch(() => Load.RunConsoleSearch(Settings(OutputType.UnitTest), true))!;
            var SharedRun = Take();
            Exception Reference = Assert.Catch(() => LegacyConsoleSearch.Run(Settings(OutputType.UnitTest)))!;
            var ReferenceRun = Take();

            Assert.That(Shared.GetType(), Is.EqualTo(Reference.GetType()));
            Assert.That(Shared, Is.InstanceOf<ConfigValidationException>());
            Assert.That(Shared.Message, Is.EqualTo(Reference.Message));
            Assert.That(SharedRun, Is.EqualTo(ReferenceRun), "the same output before the refusal");
            Assert.That(SharedRun.Out, Does.Not.Contain("Done: "), "no results");
            Assert.That(Gov.Requests, Is.Empty, "nothing asked of recreation.gov");
            Assert.That(Fingerprint(ConfigPath), Is.EqualTo(Before), "neither search file is changed or disabled");

            //Program.Main's report of it: one error naming the file, exit code 1
            Assert.That(ConsoleUi.ReportFailure(Shared, (EmailConfig?)null), Is.EqualTo(1));
            string SharedReport = Take().Err;
            Assert.That(ConsoleUi.ReportFailure(Reference, (EmailConfig?)null), Is.EqualTo(1));
            Assert.That(SharedReport, Is.EqualTo(Take().Err));
            Assert.That(SharedReport, Does.StartWith("Error: ").And.Contain("Invalid.yaml"));
        }

        // a manual search shows every current match whatever Camp.History holds, and leaves it exactly as it is
        [Test]
        public void CampHistory_ChangesNothing_AndIsLeftAsItIs()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(27));
            var Without = RunTheMenusSearch();
            //a marker for every night of every campsite of the month, as the monitoring would have written them
            foreach (string Site in SearchFixture.SiteIds(SearchFixture.Watchman, 8))
            {
                for (int Day = 1; Day <= 28; Day++)
                {
                    Directory.CreateDirectory(Path.Join(ConfigPath, "Camp.History", MonthA.Year.ToString(), MonthA.Month.ToString(), Day.ToString(), Site));
                }
            }
            List<string> History = Fingerprint(Path.Join(ConfigPath, "Camp.History"));
            Gov = new GovStandIn();

            var With = RunTheMenusSearch();

            Assert.That(Normalized(With.Out), Is.EqualTo(Normalized(Without.Out)), "the markers hide nothing");
            Assert.That(Fingerprint(Path.Join(ConfigPath, "Camp.History")), Is.EqualTo(History), "no marker written or removed");
        }

        // ---- the reference's own behavior, which the phase changed on purpose (D131) ----

        // a blocked request: every blocked request thread writes the error, waits and exits (here the seams return), and
        // the other searches' requests go on
        [Test]
        public void Before_ABlockIsOneErrorPerBlockedRequest_AndTheOtherRequestsGoOn()
        {
            WriteSearch("Mather.yaml", SearchFixture.Mather, MonthA, MonthA.AddDays(5));
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5));
            WriteSearch("Later.yaml", SearchFixture.Watchman, MonthB, MonthB.AddDays(5));
            const string Blocked = "<html><body><h1>Request blocked.</h1></body></html>";
            Gov.Script(Url(SearchFixture.Mather, MonthA), (R, T) => new SearchResponse(200, Blocked));
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(200, Blocked));

            var Reference = RunTheReference(OutputType.UnitTest);

            string[] Errors = Reference.Err.Split('\n').Where(L => L.StartsWith("Error: Blocked by API", StringComparison.Ordinal)).ToArray();
            Assert.That(Errors, Has.Length.EqualTo(2), "one error per blocked request");
            Assert.That(Steps.Count(S => S == "wait 00:05:00"), Is.EqualTo(2));
            Assert.That(Steps.Count(S => S == "exit 1"), Is.EqualTo(2));
            Assert.That(Reference.Requests, Does.Contain(Url(SearchFixture.Watchman, MonthB)), "the other search's request was made");
        }

        // heavy traffic is tried again every second, without the shared bound of three requests
        [Test]
        public void Before_HeavyTrafficIsRetriedBeyondThreeRequests()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(5));
            string Month = GovStandIn.DefaultMonth(SearchFixture.Watchman, MonthA);
            Func<SearchRequest, CancellationToken, SearchResponse> Heavy = (R, T) => new SearchResponse(200, "{\"error\":\"We are currently experiencing heavy traffic on our site. Please try again.\"}");
            Gov.Script(Url(SearchFixture.Watchman, MonthA), Heavy, Heavy, Heavy, Heavy, (R, T) => new SearchResponse(200, Month));

            var Reference = RunTheReference(OutputType.UnitTest);

            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(5), "five requests for the month");
            Assert.That(Reference.Err.Split('\n').Count(L => L.StartsWith("Warning: Heavy traffic at recreation.gov", StringComparison.Ordinal)), Is.EqualTo(4));
        }

        // the HTTP status is never read: an error status with a month's body is that month
        [Test]
        public void Before_TheStatusIsNeverRead()
        {
            WriteSearch("Watchman.yaml", SearchFixture.Watchman, MonthA, MonthA.AddDays(27));
            string Month = GovStandIn.DefaultMonth(SearchFixture.Watchman, MonthA);
            var Ok = RunTheReference(OutputType.UnitTest);
            Gov = new GovStandIn();
            Gov.Script(Url(SearchFixture.Watchman, MonthA), (R, T) => new SearchResponse(503, Month));

            var Error = RunTheReference(OutputType.UnitTest);

            Assert.That(Normalized(Error.Out), Is.EqualTo(Normalized(Ok.Out)), "the month's openings, as with 200");
            Assert.That(Error.Err, Is.Empty);
            Assert.That(Gov.Count(Url(SearchFixture.Watchman, MonthA)), Is.EqualTo(1));
        }
    }
}
