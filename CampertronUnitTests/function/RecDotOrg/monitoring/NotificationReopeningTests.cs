using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;

namespace CampertronUnitTests.function.RecDotOrg.monitoring
{
    // A campsite and night notified once while it stays available, and again after a check saw it unavailable (decision
    // D104, the maintainer's decision of 2026-11-24 that replaced the rule of D8's refinement; open question #54), and the
    // stays of a search of consecutive nights (TD-61). Every check runs through the real monitoring service: its searches
    // (FilterData, FilterConsecutiveDays), its report (the re-arm, the gate, Notifications.Deliver, the markers) and its
    // end. recreation.gov's answer is set for each check (Phase); the emails and ntfy notifications go to the base class's
    // recorders (no network); every folder is the test's own. A new service with new settings is a restart as far as
    // Campertron's state goes: the service has no static state, and Camp.History on disk is all that is carried over.
    public class NotificationReopeningTests : MonitoringTestBase
    {
        private const string S1 = Campground + "1";
        private const string S2 = Campground + "2";
        private const string O1 = OtherCampground + "1";
        // the month the searches ask for, two months ahead (no night of it is in the past), and their window in it
        private DateTime First;
        private const int WindowFirstDay = 10;
        private const int WindowLastDay = 20;
        private Action<string> OriginalRemoveMarker = null!;

        public enum Providers
        {
            Email,
            Ntfy,
            Both
        }

        [SetUp]
        public void ReopeningSetUp()
        {
            DateTime Ahead = DateTime.Now.AddMonths(2);
            First = new DateTime(Ahead.Year, Ahead.Month, 1);
            OriginalRemoveMarker = NotificationHistory.RemoveMarker;
        }

        [TearDown]
        public void ReopeningTearDown()
        {
            NotificationHistory.RemoveMarker = OriginalRemoveMarker;
        }

        // ---- recreation.gov, one check at a time ----

        // What recreation.gov answers in one check. For the target month of the fixture campground: each night's status as
        // set, "Reserved" for every other night; Listed (default: both campsites) and FirstStatusDay (the month's statuses
        // start that day, as a shorter list does) change what the month lists. Every other month and campground: every
        // night reserved, unless Other answers it
        private sealed class Phase
        {
            internal readonly Dictionary<(string Site, int Day), string?> Status = new();
            internal List<string>? Listed;
            internal int FirstStatusDay = 1;
            internal readonly List<string> Extra = new();
            internal Func<string, string?>? Other;
            internal Phase Set(string Site, string? Value, params int[] Days)
            {
                foreach (int Day in Days)
                {
                    Status[(Site, Day)] = Value;
                }
                return this;
            }
            internal Phase Open(string Site, params int[] Days) => Set(Site, "Available", Days);
        }

        private static Phase P() => new Phase();

        private string Json(string Url, Phase Spec)
        {
            if (Spec.Other?.Invoke(Url) is string Answered)
            {
                return Answered;
            }
            Match Asked = Regex.Match(Url, @"/campground/([^/]*)/month\?start_date=(\d{4})-(\d{2})-");
            Assert.That(Asked.Success, Is.True, "a month's URL: " + Url);
            var Month = new DateTime(int.Parse(Asked.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(Asked.Groups[3].Value, CultureInfo.InvariantCulture), 1);
            bool Target = Month == First && Asked.Groups[1].Value == Campground;
            int Days = DateTime.DaysInMonth(Month.Year, Month.Month);
            List<string> Listed = Target && Spec.Listed != null ? Spec.Listed : Sites(Asked.Groups[1].Value);
            if (Target)
            {
                Listed = Listed.Concat(Spec.Extra).ToList();
            }
            var Text = new StringBuilder("{\"campsites\":{");
            int Count = 0;
            foreach (string Id in Listed)
            {
                Text.Append(Count++ > 0 ? "," : "").Append('"').Append("entry").Append(Count).Append("\":{\"availabilities\":{");
                int FromDay = Target ? Spec.FirstStatusDay : 1;
                for (int Day = FromDay; Day <= Days; Day++)
                {
                    string Key = Month.AddDays(Day - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z";
                    string? Status = Target && Spec.Status.TryGetValue((Id, Day), out string? Set) ? Set : "Reserved";
                    Text.Append(Day > FromDay ? "," : "").Append('"').Append(Key).Append("\":").Append(Status == null ? "null" : "\"" + Status + "\"");
                }
                Text.Append("},\"campsite_id\":\"").Append(Id).Append("\",\"campsite_type\":\"STANDARD NONELECTRIC\",\"loop\":\"A\",\"max_num_people\":8,\"min_num_people\":1,\"quantities\":{},\"site\":\"")
                    .Append(Id).Append("\"}");
            }
            return Text.Append("},\"count\":").Append(Count).Append('}').ToString();
        }

        // ---- the searches ----

        private string SearchYaml(string Name, int Consecutive, int Humans, string Id) =>
            "displayName: " + Name + "\nautoRun: true\ncampgroundID: '" + Id + "'\ntotalHumans: " + Humans.ToString(CultureInfo.InvariantCulture) +
            "\nsearchBy: StartEndDate\nsearchValue: 0\nsearchValueDates:\n- " + First.AddDays(WindowFirstDay - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "\n- " + First.AddDays(WindowLastDay - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
            "\nshowMonday: true\nshowTuesday: true\nshowWednesday: true\nshowThursday: true\nshowFriday: true\nshowSaturday: true\nshowSunday: true\nconsecutiveDays: " +
            Consecutive.ToString(CultureInfo.InvariantCulture) + "\n";

        private void Search(string FileName, string Name, int Consecutive = 1, int Humans = 2, string Id = Campground) =>
            File.WriteAllText(Path.Join(ConfigPath, FileName), SearchYaml(Name, Consecutive, Humans, Id));

        private CampertronInternalConfig Config(Providers Provider = Providers.Email) => Provider switch
        {
            Providers.Ntfy => Settings(OutputType.Ntfy, Ntfy: true),
            Providers.Both => Settings(OutputType.Email, Ntfy: true),
            _ => Settings()
        };

        // ---- Camp.History ----

        // a marker's path relative to the settings folder, as Markers() lists it
        private string Marker(string Site, int Day, DateTime? Month = null)
        {
            DateTime Night = (Month ?? First).AddDays(Day - 1);
            return "Camp.History/" + Night.Year + "/" + Night.Month + "/" + Night.Day + "/" + Site;
        }

        private string Full(string Relative) => Path.Join(ConfigPath, Relative);

        private void Mark(string Site, int Day, DateTime? Month = null) => Directory.CreateDirectory(Full(Marker(Site, Day, Month)));

        // ---- one monitoring over the phases: one check each, stopped after the last ----

        private sealed class Check
        {
            internal int Number;
            internal int NewOpenings;
            internal readonly List<(string Search, int Openings)> Emails = new();
            internal readonly List<(string Search, int Openings)> Ntfy = new();
            internal List<string> Warnings = new();
            internal List<string>? Markers;
            internal int Sent => Emails.Count + Ntfy.Count;
            public override string ToString() => $"check {Number}: new {NewOpenings}, emails [{string.Join(", ", Emails)}], ntfy [{string.Join(", ", Ntfy)}], warnings [{string.Join(" | ", Warnings)}], markers [{string.Join(" ", Markers ?? new List<string>())}]";
        }

        private (List<Check> Checks, MonitoringStatus End) Monitor(CampertronInternalConfig Settings, params Phase[] Phases)
        {
            int Index = 0;
            var Checks = new List<Check>();
            var Pending = new List<string>();
            object Gate = new object();
            Check? Current = null;
            var Done = new ManualResetEventSlim();
            Answer = Url => Json(Url, Phases[Math.Min(Volatile.Read(ref Index), Phases.Length - 1)]);
            var Service = new MonitoringService(new MonitoringOptions
            {
                Report = Event =>
                {
                    lock (Gate)
                    {
                        switch (Event.Kind)
                        {
                            case MonitoringEventKind.Warning:
                                Pending.Add(Event.Text ?? string.Empty);
                                break;
                            case MonitoringEventKind.CheckReported:
                                Current = new Check { Number = Event.CheckNumber, NewOpenings = Event.NewOpenings, Warnings = Pending.ToList() };
                                Pending.Clear();
                                Checks.Add(Current);
                                break;
                            case MonitoringEventKind.EmailSent:
                                Current!.Emails.Add((Event.SearchName!, Event.Openings));
                                break;
                            case MonitoringEventKind.NtfySent:
                                Current!.Ntfy.Add((Event.SearchName!, Event.Openings));
                                break;
                        }
                    }
                },
                Wait = (Delay, Token) =>
                {
                    lock (Gate)
                    {
                        Current!.Markers = Markers();
                    }
                    if (Interlocked.Increment(ref Index) >= Phases.Length)
                    {
                        Done.Set();
                        Token.WaitHandle.WaitOne();
                    }
                }
            });
            Services.Add(Service);
            MonitoringStartResult Started = Service.Start(Settings);
            Assert.That(Started.Started, Is.True, "started: " + string.Join(" ", Started.Lines));
            var Clock = System.Diagnostics.Stopwatch.StartNew();
            while (!Done.IsSet && Service.GetStatus().IsRunning && Clock.Elapsed < Long)
            {
                Thread.Sleep(10);
            }
            MonitoringStatus End = Service.Stop();
            lock (Gate)
            {
                if (Current != null && Current.Markers == null)
                {
                    try
                    {
                        Current.Markers = Markers();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        //a test that took the history's permissions away looks at it itself
                    }
                }
                TestContext.Out.WriteLine(string.Join("\n", Checks));
                return (Checks.ToList(), End);
            }
        }

        private static void AssertNotified(Check Check, Providers Provider, int Openings, string Search = "Trip")
        {
            if (Provider != Providers.Ntfy)
            {
                Assert.That(Check.Emails, Is.EqualTo(new[] { (Search, Openings) }), "check " + Check.Number + "'s email");
            }
            if (Provider != Providers.Email)
            {
                Assert.That(Check.Ntfy, Is.EqualTo(new[] { (Search, Openings) }), "check " + Check.Number + "'s ntfy notification");
            }
            Assert.That(Check.NewOpenings, Is.EqualTo(Openings));
        }

        private static void AssertQuiet(Check Check)
        {
            Assert.That((Check.NewOpenings, Check.Sent), Is.EqualTo((0, 0)), "check " + Check.Number + " notifies nothing");
        }

        // ---- continuous availability ----

        [Test]
        public void AContinuouslyAvailableNight_IsNotifiedOnce([Values] Providers Provider)
        {
            Search("Trip.yaml", "Trip");
            var (Checks, End) = Monitor(Config(Provider), P().Open(S1, 12), P().Open(S1, 12), P().Open(S1, 12));

            Assert.That(Checks, Has.Count.EqualTo(3));
            AssertNotified(Checks[0], Provider, 1);
            AssertQuiet(Checks[1]);
            AssertQuiet(Checks[2]);
            Assert.That(Checks.Select(C => C.Markers), Has.All.EqualTo(new[] { Marker(S1, 12) }), "the marker stays while the night stays available");
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
        }

        [Test]
        public void ARestart_KeepsAContinuouslyAvailableNightSuppressed([Values] Providers Provider)
        {
            Search("Trip.yaml", "Trip");
            var (Before, _) = Monitor(Config(Provider), P().Open(S1, 12));
            var (After, _) = Monitor(Config(Provider), P().Open(S1, 12), P().Open(S1, 12));

            AssertNotified(Before.Single(), Provider, 1);
            AssertQuiet(After[0]);
            AssertQuiet(After[1]);
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }));
        }

        // ---- a night that becomes unavailable and available again ----

        [Test]
        public void AReopenedNight_IsNotifiedAgain_AfterACheckSawItUnavailable([Values("Reserved", "Not Reservable", "Not Available")] string Gone, [Values] Providers Provider)
        {
            Search("Trip.yaml", "Trip");
            var (Checks, End) = Monitor(Config(Provider), P().Open(S1, 12), P().Open(S1, 12), P().Set(S1, Gone, 12), P().Open(S1, 12), P().Open(S1, 12));

            AssertNotified(Checks[0], Provider, 1);
            AssertQuiet(Checks[1]);
            AssertQuiet(Checks[2]);
            Assert.That(Checks[2].Markers, Is.Empty, "the check that saw it " + Gone + " re-armed it");
            Assert.That(Checks[2].Warnings, Is.Empty);
            AssertNotified(Checks[3], Provider, 1);
            Assert.That(Checks[3].Markers, Is.EqualTo(new[] { Marker(S1, 12) }), "recorded again once delivered");
            AssertQuiet(Checks[4]);
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
        }

        [Test]
        public void AMarkerFromBeforeTheStart_KeepsSuppressing_UntilACheckSeesItsNightUnavailable()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            var (Checks, _) = Monitor(Config(), P().Open(S1, 12), P().Set(S1, "Reserved", 12), P().Open(S1, 12));

            AssertQuiet(Checks[0]);
            Assert.That(Checks[0].Markers, Is.EqualTo(new[] { Marker(S1, 12) }));
            AssertQuiet(Checks[1]);
            Assert.That(Checks[1].Markers, Is.Empty);
            AssertNotified(Checks[2], Providers.Email, 1);
        }

        [Test]
        public void TheReArm_IsKeptAcrossRestarts_AsCampHistoryIs()
        {
            Search("Trip.yaml", "Trip");
            var (One, _) = Monitor(Config(), P().Open(S1, 12));
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "the first monitoring notified and recorded it");
            var (Two, _) = Monitor(Config(), P().Set(S1, "Reserved", 12));
            Assert.That(Markers(), Is.Empty, "the second saw it reserved and re-armed it");
            var (Three, _) = Monitor(Config(), P().Open(S1, 12));

            AssertNotified(One.Single(), Providers.Email, 1);
            AssertQuiet(Two.Single());
            AssertNotified(Three.Single(), Providers.Email, 1);
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }));
        }

        // ---- only a month the check fetched and read ----

        [Test]
        public void ANightOutsideTheSearchWindow_IsReArmedFromItsFetchedMonth_ButAMonthNoCheckFetched_ReArmsNothing()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 3);
            Mark(S1, 12, First.AddMonths(3));
            Mark(S1, 12, First.AddMonths(-1));
            var (Checks, _) = Monitor(Config(), P(), P());

            Assert.That(Requested.Select(Url => Url[(Url.IndexOf("start_date=", StringComparison.Ordinal) + 11)..(Url.IndexOf("start_date=", StringComparison.Ordinal) + 18)]).Distinct(),
                Is.EqualTo(new[] { First.ToString("yyyy-MM", CultureInfo.InvariantCulture) }), "the searches fetch one month");
            Assert.That(Markers(), Is.EquivalentTo(new[] { Marker(S1, 12, First.AddMonths(3)), Marker(S1, 12, First.AddMonths(-1)) }),
                "day 3 of the fetched month was seen reserved (the month is the evidence, not the window); the other months were not fetched");
            Assert.That(Checks.SelectMany(C => C.Warnings), Is.Empty);
        }

        [Test]
        public void AnotherCampgroundsMarker_IsReArmedOnlyByItsOwnMonth()
        {
            Search("Trip.yaml", "Trip");
            Mark(O1, 12);
            Monitor(Config(), P());
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(O1, 12) }), "this campground's month does not list the other's campsite");

            Search("Other.yaml", "Other", Id: OtherCampground);
            Monitor(Config(), P());
            Assert.That(Markers(), Is.Empty, "the other campground's own month, fetched by its search, saw it reserved");
        }

        [Test]
        public void ACampsiteTheMonthDoesNotList_OrANightWithoutAStatus_IsNotReArmed()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            Mark(S2, 5);
            Mark(S2, 13);
            Mark(S2, 14);
            var Unlisted = P();
            Unlisted.Listed = new List<string> { S2 };
            Unlisted.FirstStatusDay = 10;
            Unlisted.Set(S2, null, 13).Set(S2, string.Empty, 14);
            var (Checks, End) = Monitor(Config(), Unlisted);

            Assert.That(Markers(), Is.EquivalentTo(new[] { Marker(S1, 12), Marker(S2, 5), Marker(S2, 13), Marker(S2, 14) }),
                "a campsite the month does not list, a night before its statuses and an empty status are no observation");
            Assert.That((Checks.Single().Warnings.Count, End.State), Is.EqualTo((0, MonitoringState.Stopped)));

            Monitor(Config(), P());
            Assert.That(Markers(), Is.Empty, "a month that lists them with a status re-arms them");
        }

        [Test]
        public void AFailedRequest_AndThenAStop_ReArmNothing_NotEvenFromAMonthItReadBefore()
        {
            Search("Trip.yaml", "Trip");
            Search("Other.yaml", "Other", Id: OtherCampground);
            Mark(S1, 12);
            var Spec = P();
            Spec.Other = Url => Url.Contains("/campground/" + OtherCampground + "/", StringComparison.Ordinal)
                ? throw new HttpRequestException("the test's recreation.gov is down") : null;
            Answer = Url => Json(Url, Spec);
            var Warned = new ManualResetEventSlim();
            Recorder Run = NewService(OnEvent: Event =>
            {
                if (Event.Kind == MonitoringEventKind.Warning && (Event.Text ?? string.Empty).Contains("failed (attempt 1", StringComparison.Ordinal))
                {
                    Warned.Set();
                }
            });
            Assert.That(Run.Service.Start(Config()).Started, Is.True);
            Assert.That(Warned.Wait(Long), Is.True, "the failed request is tried again after a wait");
            Assert.That(SpinWait.SpinUntil(() => Requested.Any(Url => Url.Contains("/campground/" + Campground + "/", StringComparison.Ordinal)), Long), Is.True,
                "the fixture campground's month, with night 12 reserved, was read");

            MonitoringStatus End = Run.Service.Stop();

            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty, "the check never completed");
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "a check with a failed request is no evidence, not even its other months");
        }

        [Test]
        public void ABlockedRequest_ReArmsNothing_NotEvenFromAMonthItReadBefore()
        {
            Search("Trip.yaml", "Trip");
            Search("Other.yaml", "Other", Id: OtherCampground);
            Mark(S1, 12);
            var Spec = P();
            Spec.Other = Url => Url.Contains("/campground/" + OtherCampground + "/", StringComparison.Ordinal) ? "<html>Request blocked</html>" : null;
            Answer = Url => Json(Url, Spec);
            Recorder Run = NewService();
            Assert.That(Run.Service.Start(Config()).Started, Is.True);

            MonitoringStatus End = Ended(Run.Service);

            Assert.That((End.State, End.LastError?.Blocked), Is.EqualTo((MonitoringState.Failed, (bool?)true)));
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty);
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "a check that did not complete re-arms nothing");
        }

        [Test]
        public void AStoppedCheck_ReArmsNothing_NotEvenFromAMonthItReadBefore()
        {
            Search("Trip.yaml", "Trip");
            Search("Other.yaml", "Other", Id: OtherCampground);
            Mark(S1, 12);
            var Hold = new ManualResetEventSlim();
            var Holding = new ManualResetEventSlim();
            var Spec = P();
            Spec.Other = Url =>
            {
                if (!Url.Contains("/campground/" + OtherCampground + "/", StringComparison.Ordinal))
                {
                    return null;
                }
                Holding.Set();
                Hold.Wait(Long);
                return null;
            };
            Answer = Url => Json(Url, Spec);
            Recorder Run = NewService();
            Assert.That(Run.Service.Start(Config()).Started, Is.True);
            Assert.That(Holding.Wait(Long), Is.True, "the other campground's month is being fetched");
            Assert.That(SpinWait.SpinUntil(() => Requested.Any(Url => Url.Contains("/campground/" + Campground + "/", StringComparison.Ordinal)), Long), Is.True);

            Task<MonitoringStatus> Stopping = Task.Run(() => Run.Service.Stop());
            Assert.That(SpinWait.SpinUntil(() => Run.Service.GetStatus().StopRequested || Stopping.IsCompleted, Long), Is.True);
            Hold.Set();
            MonitoringStatus End = Stopping.Wait(Long) ? Stopping.Result : throw new AssertionException("the stop ended");

            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(Run.Of(MonitoringEventKind.CheckReported), Is.Empty);
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "a stopped check re-arms nothing");
        }

        // ---- removing a marker: only an empty folder, never a link, never a path from recreation.gov ----

        [Test]
        public void AMarkerWithAFileOrAFolderInside_IsKept_AndTheCheckWarnsInFixedWords_AndGoesOn()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            Mark(S2, 12);
            File.WriteAllText(Path.Join(Full(Marker(S1, 12)), "note.txt"), "kept");
            Directory.CreateDirectory(Path.Join(Full(Marker(S2, 12)), "child"));
            var (Checks, End) = Monitor(Config(), P(), P());

            Assert.That(Checks, Has.Count.EqualTo(2), "the monitoring goes on");
            Assert.That(Checks[0].Warnings, Is.EqualTo(new[] { NotificationHistory.NotRemovedText(2) }));
            Assert.That(Checks[0].Warnings.Single(), Is.EqualTo("Camp.History: 2 markers of notified openings that are no longer available could not be removed; those openings are not notified again until a later check removes them."));
            Assert.That(Checks[0].Warnings.Single(), Does.Not.Contain(Root).And.Not.Contain(S1).And.Not.Contain("/"));
            Assert.That(File.ReadAllText(Path.Join(Full(Marker(S1, 12)), "note.txt")), Is.EqualTo("kept"));
            Assert.That(Directory.Exists(Path.Join(Full(Marker(S2, 12)), "child")), Is.True);
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
            Assert.That(NotificationHistory.NotRemovedText(1), Is.EqualTo("Camp.History: 1 marker of notified openings that are no longer available could not be removed; those openings are not notified again until a later check removes them."));
        }

        [Test]
        public void ARemovalThatFails_OnlyWarns_AndALaterCheckReArmsTheNight()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            int Calls = 0;
            NotificationHistory.RemoveMarker = Folder =>
            {
                if (Interlocked.Increment(ref Calls) == 1)
                {
                    throw new IOException("the test's file system refused " + Folder);
                }
                OriginalRemoveMarker(Folder);
            };
            var (Checks, End) = Monitor(Config(), P(), P(), P().Open(S1, 12));

            Assert.That(Checks[0].Warnings, Is.EqualTo(new[] { NotificationHistory.NotRemovedText(1) }), "the failure's own words and path are not shown");
            Assert.That(Checks[0].Markers, Is.EqualTo(new[] { Marker(S1, 12) }));
            Assert.That((Checks[1].Warnings.Count, Checks[1].Markers!.Count), Is.EqualTo((0, 0)), "the next check removed it");
            AssertNotified(Checks[2], Providers.Email, 1);
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
        }

        [Test]
        public void AFolderTheMonitoringMayNotChange_OnlyWarns()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            string Day = Path.GetDirectoryName(Full(Marker(S1, 12)))!;
            File.SetUnixFileMode(Day, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assume.That(Probe(Day), Is.False, "a folder without write permission refuses a removal (not as root)");
            var (Checks, End) = Monitor(Config(), P(), P());

            Assert.That(Checks.Select(C => C.Warnings.Count), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped), "never a failure of the monitoring");
            Assert.That(Directory.Exists(Full(Marker(S1, 12))), Is.True);

            static bool Probe(string Folder)
            {
                string Test = Path.Join(Folder, "probe");
                try
                {
                    Directory.CreateDirectory(Test);
                    Directory.Delete(Test);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

        [Test]
        public void ALinkedMarkerOrDay_IsNeverFollowedOrRemoved()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("symbolic links need a privilege on Windows");
                return;
            }
            Search("Trip.yaml", "Trip");
            string Outside = Path.Join(Root, "outside");
            Directory.CreateDirectory(Path.Join(Outside, "marker-target"));
            Directory.CreateDirectory(Path.Join(Outside, "day-target", S1));
            string LinkedMarker = Full(Marker(S1, 12));
            Directory.CreateDirectory(Path.GetDirectoryName(LinkedMarker)!);
            Directory.CreateSymbolicLink(LinkedMarker, Path.Join(Outside, "marker-target"));
            string LinkedDay = Path.GetDirectoryName(Full(Marker(S1, 13)))!;
            Directory.CreateSymbolicLink(LinkedDay, Path.Join(Outside, "day-target"));
            var (Checks, End) = Monitor(Config(), P());

            Assert.That(new DirectoryInfo(LinkedMarker).LinkTarget, Is.Not.Null, "the linked marker stays");
            Assert.That(Directory.Exists(Path.Join(Outside, "marker-target")), Is.True);
            Assert.That(new DirectoryInfo(LinkedDay).LinkTarget, Is.Not.Null);
            Assert.That(Directory.Exists(Path.Join(Outside, "day-target", S1)), Is.True, "nothing under a linked day");
            Assert.That(Checks.Single().Warnings, Is.EqualTo(new[] { NotificationHistory.NotRemovedText(1) }), "the linked marker is one that could not be removed");
            Assert.That(End.State, Is.EqualTo(MonitoringState.Stopped));
        }

        [Test]
        public void ACampsiteIdFromRecreationGov_IsNeverAPath()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            string MonthFolder = Path.GetDirectoryName(Path.GetDirectoryName(Full(Marker(S1, 12))))!;
            Directory.CreateDirectory(Path.Join(MonthFolder, "13"));
            Directory.CreateDirectory(Path.Join(MonthFolder, "x"));
            var Spec = P();
            Spec.Extra.AddRange(new[] { ".", "..", "../x", "../13", "/" });
            var (Checks, _) = Monitor(Config(), Spec);

            Assert.That(Directory.Exists(Path.Join(MonthFolder, "13")), Is.True, "an empty day folder is not a marker");
            Assert.That(Directory.Exists(Path.Join(MonthFolder, "x")), Is.True);
            Assert.That(Markers(), Is.Empty, "the real marker was re-armed");
            Assert.That(Checks.Single().Warnings, Is.Empty);
        }

        [Test]
        public void TheReArm_CreatesNothing_AndLeavesTheEmptyFoldersAboveAMarker()
        {
            Search("Trip.yaml", "Trip");
            Monitor(Config(), P(), P());
            Assert.That(Directory.Exists(HistoryPath), Is.False, "no Camp.History before a notification was delivered");

            Mark(S1, 12);
            Monitor(Config(), P());
            Assert.That(Markers(), Is.Empty);
            Assert.That(Directory.Exists(Path.GetDirectoryName(Full(Marker(S1, 12)))), Is.True, "the day folder stays");
            Assert.That(Directory.GetFiles(HistoryPath, "*", SearchOption.AllDirectories), Is.Empty);
        }

        // ---- a failed send records nothing, and undoes no re-arm ----

        [Test]
        public void AFailedSend_RecordsNothing_AndTheNextStartNotifiesTheNightAgain()
        {
            Search("Trip.yaml", "Trip");
            Send = Name => throw new SmtpException("the test's SMTP server refused it");
            var (Failed, End) = Monitor(Config(), P().Open(S1, 12), P().Open(S1, 12));

            Assert.That(Failed, Has.Count.EqualTo(1), "the failed check ended the monitoring");
            Assert.That((End.State, End.LastError != null), Is.EqualTo((MonitoringState.Failed, true)));
            Assert.That(Markers(), Is.Empty, "nothing undelivered is recorded");
            Thread.Sleep(100);
            Assert.That(Services.Last().GetStatus().State, Is.EqualTo(MonitoringState.Failed), "nothing starts it again by itself");

            Send = Name => { };
            var (Next, _) = Monitor(Config(), P().Open(S1, 12));
            AssertNotified(Next.Single(), Providers.Email, 1);
        }

        [Test]
        public void AReArmedNight_WhoseNotificationFails_StaysReArmed_AndTheNextStartNotifiesIt()
        {
            Search("Trip.yaml", "Trip");
            int Sends = 0;
            Send = Name =>
            {
                if (Interlocked.Increment(ref Sends) == 2)
                {
                    throw new SmtpException("the test's SMTP server refused it");
                }
            };
            var (Checks, End) = Monitor(Config(), P().Open(S1, 12), P(), P().Open(S1, 12));

            AssertNotified(Checks[0], Providers.Email, 1);
            Assert.That(Checks[1].Markers, Is.Empty, "re-armed");
            Assert.That((Checks.Count, Checks[2].NewOpenings, End.State), Is.EqualTo((3, 1, MonitoringState.Failed)), "its new notification failed");
            Assert.That(Markers(), Is.Empty, "the re-arm stays, and nothing undelivered is recorded");

            var (Next, _) = Monitor(Config(), P().Open(S1, 12));
            AssertNotified(Next.Single(), Providers.Email, 1);
        }

        [Test]
        public void ACheckWhoseNotificationFails_KeepsTheReArmItMade()
        {
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            Send = Name => throw new SmtpException("the test's SMTP server refused it");
            var (Checks, End) = Monitor(Config(), P().Set(S1, "Reserved", 12).Open(S2, 13));

            Assert.That((Checks.Single().NewOpenings, End.State), Is.EqualTo((1, MonitoringState.Failed)), "night 13 was new, and its email failed");
            Assert.That(Markers(), Is.Empty, "night 12's re-arm was made before the sends and stays; night 13 is not recorded");
        }

        [Test]
        public void OneProviderDelivers_TheOtherFails_TheNightIsRecorded_AndItsReopeningIsNotifiedByBoth()
        {
            Search("Trip.yaml", "Trip");
            Send = Name => throw new SmtpException("the test's SMTP server refused it");
            var (Failed, End) = Monitor(Config(Providers.Both), P().Open(S1, 12));

            Assert.That((Failed.Single().Emails.Count, Failed.Single().Ntfy.Count, End.State), Is.EqualTo((0, 1, MonitoringState.Failed)));
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "ntfy delivered it (D67)");

            Send = Name => { };
            var (Checks, _) = Monitor(Config(Providers.Both), P().Open(S1, 12), P(), P().Open(S1, 12));
            AssertQuiet(Checks[0]);
            AssertQuiet(Checks[1]);
            AssertNotified(Checks[2], Providers.Both, 1);
        }

        [Test]
        public void ACampHistoryThatCantBeWritten_AfterASend_FailsTheCheck_AndTheNightIsNotifiedAgainLater()
        {
            Search("Trip.yaml", "Trip");
            File.WriteAllText(HistoryPath, "not a folder");
            var (Failed, End) = Monitor(Config(), P().Open(S1, 12));

            Assert.That((Failed.Single().Emails.Count, End.State), Is.EqualTo((1, MonitoringState.Failed)), "sent, then its marker could not be written");
            File.Delete(HistoryPath);
            var (Next, _) = Monitor(Config(), P().Open(S1, 12));
            AssertNotified(Next.Single(), Providers.Email, 1);
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }));
        }

        [Test]
        public void AnUnreadableHistory_LooksNew_AndItsFailedWriteStillEndsTheCheck_NotTheReArm()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix permissions");
                return;
            }
            Search("Trip.yaml", "Trip");
            Mark(S1, 12);
            string MonthFolder = Path.GetDirectoryName(Path.GetDirectoryName(Full(Marker(S1, 12))))!;
            File.SetUnixFileMode(MonthFolder, UnixFileMode.None);
            List<Check> Checks;
            MonitoringStatus End;
            try
            {
                Assume.That(Directory.Exists(Full(Marker(S1, 12))), Is.False, "a folder without permissions hides its markers (not as root)");
                (Checks, End) = Monitor(Config(), P().Open(S1, 12), P().Open(S1, 12));
            }
            finally
            {
                File.SetUnixFileMode(MonthFolder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Assert.That((Checks.Count, Checks[0].Emails.Count, Checks[0].Warnings.Count), Is.EqualTo((1, 1, 0)), "the night looked new and was sent; the re-arm said nothing");
            Assert.That(End.State, Is.EqualTo(MonitoringState.Failed));
            Assert.That(End.LastError!.Error, Is.InstanceOf<UnauthorizedAccessException>(), "its marker could not be written: the check fails visibly, as before");
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }), "the marker it could not see is unchanged");
        }

        // ---- consecutive nights (TD-61) ----

        [Test]
        public void ALongerStay_WithANightNeverNotified_IsNotifiedWhole()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            var (Checks, _) = Monitor(Config(), P().Open(S1, 12, 13), P().Open(S1, 12, 13, 14), P().Open(S1, 12, 13, 14));

            AssertNotified(Checks[0], Providers.Email, 2);
            AssertNotified(Checks[1], Providers.Email, 3);
            Assert.That(Checks[1].Markers, Is.EquivalentTo(new[] { Marker(S1, 12), Marker(S1, 13), Marker(S1, 14) }));
            AssertQuiet(Checks[2]);
        }

        [Test]
        public void AStayWhoseMiddleNightReopens_IsNotifiedWholeAgain()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            var (Checks, _) = Monitor(Config(), P().Open(S1, 12, 13, 14), P().Open(S1, 12, 14), P().Open(S1, 12, 13, 14));

            AssertNotified(Checks[0], Providers.Email, 3);
            AssertQuiet(Checks[1]);
            Assert.That(Checks[1].Markers, Is.EquivalentTo(new[] { Marker(S1, 12), Marker(S1, 14) }), "night 13 was seen reserved");
            AssertNotified(Checks[2], Providers.Email, 3);
        }

        [Test]
        public void AStayThatDisappearsAndReturns_IsNotifiedAgain()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 12, 13), P(), P().Open(S1, 12, 13));

            AssertNotified(Checks[0], Providers.Ntfy, 2);
            AssertQuiet(Checks[1]);
            Assert.That(Checks[1].Markers, Is.Empty);
            AssertNotified(Checks[2], Providers.Ntfy, 2);
        }

        [Test]
        public void ANotifiedStay_StaysSuppressedWhileItStaysAvailable_AcrossRestarts()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            var (Checks, _) = Monitor(Config(), P().Open(S1, 12, 13), P().Open(S1, 12, 13));
            var (Restarted, _) = Monitor(Config(), P().Open(S1, 12, 13));

            AssertNotified(Checks[0], Providers.Email, 2);
            AssertQuiet(Checks[1]);
            AssertQuiet(Restarted.Single());
        }

        [Test]
        public void ANotifiedStay_IsLeftOutOfALaterNotification_WhileItStaysAvailable()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 12, 13), P().Open(S1, 12, 13).Open(S2, 15, 16));

            AssertNotified(Checks[0], Providers.Ntfy, 2);
            AssertNotified(Checks[1], Providers.Ntfy, 2);
            Assert.That(Checks[1].Markers, Is.EquivalentTo(new[] { Marker(S1, 12), Marker(S1, 13), Marker(S2, 15), Marker(S2, 16) }),
                "only campsite 2's new stay was notified: campsite 1's, notified before and still available, is not sent with it");
        }

        [Test]
        public void AOneNightSearchsMarker_DoesNotHideATwoNightSearchsStay()
        {
            Search("Alpha.yaml", "Alpha");
            Search("Beta.yaml", "Beta", Consecutive: 2);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 12), P().Open(S1, 12, 13), P().Open(S1, 12, 13));

            Assert.That(Checks[0].Ntfy, Is.EqualTo(new[] { ("Alpha", 1) }), "a single night; no stay yet");
            Assert.That(Checks[1].Ntfy, Is.EqualTo(new[] { ("Alpha", 1), ("Beta", 2) }), "Alpha's new night 13; Beta's stay 12-13, whose night 12 Alpha notified");
            Assert.That(Checks[1].NewOpenings, Is.EqualTo(3));
            AssertQuiet(Checks[2]);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void ASearchWithConsecutiveDaysBelowOne_KeepsDroppingItsNotifiedNights(int Consecutive)
        {
            Search("Trip.yaml", "Trip", Consecutive: Consecutive);
            Mark(S1, 12);
            var (Checks, _) = Monitor(Config(), P().Open(S1, 12, 13), P().Open(S1, 12, 13));

            Assert.That(Checks[0].NewOpenings, Is.EqualTo(1), "only night 13: night 12 was notified");
            Assert.That(Checks[0].Markers, Is.EquivalentTo(new[] { Marker(S1, 12), Marker(S1, 13) }));
            AssertQuiet(Checks[1]);
        }

        [Test]
        public void AStay_IsBuiltOnlyFromTheNightsTheSearchKeeps()
        {
            Search("Trip.yaml", "Trip", Consecutive: 2);
            Search("Crowd.yaml", "Crowd", Consecutive: 2, Humans: 9);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 19, 20, 21, 22), P().Open(S1, 19, 20, 21, 22), P().Open(S1, 18, 19, 20, 21, 22));

            Assert.That(Checks[0].Ntfy, Is.EqualTo(new[] { ("Trip", 2) }), "nights 19 and 20: 21 and 22 are after the window; Crowd's nine people fit no campsite");
            Assert.That(Checks[0].Markers, Is.EquivalentTo(new[] { Marker(S1, 19), Marker(S1, 20) }));
            AssertQuiet(Checks[1]);
            Assert.That(Checks[2].Ntfy, Is.EqualTo(new[] { ("Trip", 3) }), "night 18 is new, so the stay 18-20 is notified whole");
        }

        // ---- several searches: one notification each, one history for all ----

        [Test]
        public void TwoSearchesMatchingOneNight_EachNotifyIt_ThenBothWaitForItToReopen()
        {
            Search("Alpha.yaml", "Alpha");
            Search("Beta.yaml", "Beta", Humans: 4);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 12), P().Open(S1, 12), P(), P().Open(S1, 12));

            Assert.That(Checks[0].Ntfy, Is.EqualTo(new[] { ("Alpha", 1), ("Beta", 1) }));
            Assert.That(Checks[0].Markers, Is.EqualTo(new[] { Marker(S1, 12) }), "one marker for both");
            AssertQuiet(Checks[1]);
            AssertQuiet(Checks[2]);
            Assert.That(Checks[3].Ntfy, Is.EqualTo(new[] { ("Alpha", 1), ("Beta", 1) }));
        }

        [Test]
        public void ASearchAddedOrRenamedLater_IsSuppressedWhileTheNightStaysAvailable_AndNotifiedAfterItReopens()
        {
            Search("Alpha.yaml", "Alpha");
            Monitor(Config(Providers.Ntfy), P().Open(S1, 12));
            Assert.That(Markers(), Is.EqualTo(new[] { Marker(S1, 12) }));

            File.Delete(Path.Join(ConfigPath, "Alpha.yaml"));
            Search("Renamed.yaml", "Renamed");
            Search("Beta.yaml", "Beta", Humans: 4);
            var (Checks, _) = Monitor(Config(Providers.Ntfy), P().Open(S1, 12), P(), P().Open(S1, 12));

            AssertQuiet(Checks[0]);
            AssertQuiet(Checks[1]);
            Assert.That(Checks[2].Ntfy, Is.EqualTo(new[] { ("Beta", 1), ("Renamed", 1) }));
        }
    }
}
