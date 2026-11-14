using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronUnitTests.function.RecDotOrg.api
{
    // TD-40: every search of the same campground and month gets the same cached AvailabilityEntries from
    // RunData.SiteData. The searches used to call CampsitesDataEntry.GenerateDates on those shared objects, which
    // replaced and filled AvailabilityDates without synchronization (ArgumentException from List<T>.Add, silently
    // missing results). Each search now derives its own dates (CalculateAvailableDates, AvailableDatesBySite) and
    // only reads the cached objects. These tests use built in-memory months and fixed dates: no network, database,
    // current date or timing.
    public class AvailableDatesTests
    {
        private static List<availabilities> Statuses(int Days, params int[] AvailableDays) =>
            Enumerable.Range(1, Days).Select(Day => new availabilities { availability = AvailableDays.Contains(Day) ? "Available" : "Reserved" }).ToList();

        private static CampsitesDataEntry Entry(string CampsiteId, List<availabilities> Availabilities) =>
            new CampsitesDataEntry { campsite_id = CampsiteId, site = "S" + CampsiteId, loop = "Loop A", min_num_people = 1, max_num_people = 6, availabilities = Availabilities };

        private static List<DateTime> Days(int Year, int Month, params int[] DaysOfMonth) =>
            DaysOfMonth.Select(Day => new DateTime(Year, Month, Day)).ToList();

        // everything a search could change on a cached entry, including the identity of the lists and items reachable from it
        private static object[] Snapshot(CampsitesDataEntry E) =>
            new object[] { E.campsite_id ?? "", E.site ?? "", E.loop ?? "", E.min_num_people ?? -1, E.max_num_people ?? -1, E.AvailabilityDates ?? (object)"null", E.availabilities ?? (object)"null" }
                .Concat(E.availabilities ?? new List<availabilities>())
                .Concat((E.availabilities ?? new List<availabilities>()).Select(a => (object)(a.availability ?? "null")))
                .ToArray();

        private static void AssertUnchanged(CampsitesDataEntry E, object[] Before)
        {
            object[] After = Snapshot(E);
            Assert.That(After, Has.Length.EqualTo(Before.Length));
            for (int i = 0; i < Before.Length; i++)
            {
                if (Before[i] is string || Before[i].GetType().IsValueType)
                {
                    Assert.That(After[i], Is.EqualTo(Before[i]), $"value {i} changed");
                }
                else
                {
                    Assert.That(After[i], Is.SameAs(Before[i]), $"object {i} was replaced");
                }
            }
            Assert.That(E.AvailabilityDates, Is.Null, "the search path must not set AvailabilityDates");
        }

        [Test]
        public void CalculateAvailableDates_ThirtyDayMonth_ReturnsTheAvailableDays()
        {
            List<DateTime> Result = CampsitesDataEntry.CalculateAvailableDates(Statuses(30, 1, 2, 15, 30), new DateTime(2026, 6, 1), 30);

            Assert.That(Result, Is.EqualTo(Days(2026, 6, 1, 2, 15, 30)));
        }

        [Test]
        public void CalculateAvailableDates_ThirtyOneDayMonth_ReturnsTheAvailableDays()
        {
            List<DateTime> Result = CampsitesDataEntry.CalculateAvailableDates(Statuses(31, 1, 30, 31), new DateTime(2027, 1, 1), 31);

            Assert.That(Result, Is.EqualTo(Days(2027, 1, 1, 30, 31)));
        }

        [Test]
        public void CalculateAvailableDates_OnlyTheExactStatusAvailableCounts()
        {
            var Mixed = new List<availabilities>
            {
                new() { availability = "Available" }, new() { availability = "available" }, new() { availability = "Available " },
                new() { availability = "Reserved" }, new() { availability = "Not Available" }, new() { availability = null },
                new() { availability = "Open" }, new() { availability = "Available" }
            };

            // 8 statuses for a 30-day month: they are the last 8 days (23..30)
            List<DateTime> Result = CampsitesDataEntry.CalculateAvailableDates(Mixed, new DateTime(2026, 11, 1), 30);

            Assert.That(Result, Is.EqualTo(Days(2026, 11, 23, 30)));
        }

        // existing contract (business-rules §3, open question 13): counter = TotalDaysInMonth - availabilities.Count,
        // so a shorter list is the last days of the month and a longer one starts before it
        [Test]
        public void CalculateAvailableDates_ShorterList_IsTheLastDaysOfTheMonth()
        {
            List<DateTime> Result = CampsitesDataEntry.CalculateAvailableDates(Statuses(5, 1, 2, 3, 4, 5), new DateTime(2027, 1, 1), 31);

            Assert.That(Result, Is.EqualTo(Days(2027, 1, 27, 28, 29, 30, 31)));
        }

        [Test]
        public void CalculateAvailableDates_LongerList_StartsBeforeTheMonth_AsBefore()
        {
            List<DateTime> Result = CampsitesDataEntry.CalculateAvailableDates(Statuses(32, 1, 3, 32), new DateTime(2026, 11, 1), 30);

            Assert.That(Result, Is.EqualTo(new[] { new DateTime(2026, 10, 30), new DateTime(2026, 11, 1), new DateTime(2026, 11, 30) }));
        }

        [Test]
        public void CalculateAvailableDates_NoStatuses_ReturnsAnEmptyList()
        {
            Assert.That(CampsitesDataEntry.CalculateAvailableDates(null, new DateTime(2026, 11, 1), 30), Is.Empty);
            Assert.That(CampsitesDataEntry.CalculateAvailableDates(new List<availabilities>(), new DateTime(2026, 11, 1), 30), Is.Empty);
        }

        [Test]
        public void CalculateAvailableDates_LeavesTheEntryAndEverythingReachableFromItUnchanged()
        {
            CampsitesDataEntry Source = Entry("100", Statuses(30, 4, 5, 6));
            object[] Before = Snapshot(Source);

            CampsitesDataEntry.CalculateAvailableDates(Source.availabilities, new DateTime(2026, 11, 1), 30);

            AssertUnchanged(Source, Before);
        }

        [Test]
        public void CalculateAvailableDates_RepeatedCalls_ReturnEqualButIndependentLists()
        {
            CampsitesDataEntry Source = Entry("100", Statuses(30, 4, 5, 6));
            object[] Before = Snapshot(Source);

            List<DateTime> First = CampsitesDataEntry.CalculateAvailableDates(Source.availabilities, new DateTime(2026, 11, 1), 30);
            List<DateTime> Second = CampsitesDataEntry.CalculateAvailableDates(Source.availabilities, new DateTime(2026, 11, 1), 30);

            Assert.That(Second, Is.EqualTo(First));
            Assert.That(Second, Is.Not.SameAs(First));

            First.Clear();
            First.Add(new DateTime(2000, 1, 1));

            Assert.That(Second, Is.EqualTo(Days(2026, 11, 4, 5, 6)));
            Assert.That(CampsitesDataEntry.CalculateAvailableDates(Source.availabilities, new DateTime(2026, 11, 1), 30), Is.EqualTo(Days(2026, 11, 4, 5, 6)));
            AssertUnchanged(Source, Before);
        }

        // two searches of the same campground and month share one cached month object
        [Test]
        public void AvailableDatesBySite_TwoSearchesOfOneCachedMonth_GetIndependentDates_AndTheMonthIsUnchanged()
        {
            var Month = new AvailabilityEntries
            {
                campsites = new List<CampsitesDataEntry> { Entry("300", Statuses(30, 1, 2)), Entry("100", Statuses(30, 10)), Entry("200", Statuses(30)) }
            };
            List<object[]> Before = Month.campsites.Select(Snapshot).ToList();

            List<CampsiteAvailableDates> SearchA = AvailabilityApi.AvailableDatesBySite(Month, new DateTime(2026, 11, 1), 30);
            List<CampsiteAvailableDates> SearchB = AvailabilityApi.AvailableDatesBySite(Month, new DateTime(2026, 11, 1), 30);

            // same entries, in the cached order, each search with its own date lists
            Assert.That(SearchA.Select(p => p.Entry), Is.EqualTo(Month.campsites).AsCollection);
            Assert.That(SearchB.Select(p => p.Entry), Is.EqualTo(Month.campsites).AsCollection);
            for (int i = 0; i < Month.campsites.Count; i++)
            {
                Assert.That(SearchA[i].Entry, Is.SameAs(Month.campsites[i]));
                Assert.That(SearchB[i].AvailableDates, Is.Not.SameAs(SearchA[i].AvailableDates));
            }
            Assert.That(SearchA.Select(p => p.AvailableDates), Is.EqualTo(new[] { Days(2026, 11, 1, 2), Days(2026, 11, 10), new List<DateTime>() }));

            // one search changing its own data cannot reach the other search or the cached month
            SearchA[0].AvailableDates.Clear();
            SearchA[1].AvailableDates.Add(new DateTime(2026, 11, 11));

            Assert.That(SearchB.Select(p => p.AvailableDates), Is.EqualTo(new[] { Days(2026, 11, 1, 2), Days(2026, 11, 10), new List<DateTime>() }));
            for (int i = 0; i < Month.campsites.Count; i++)
            {
                AssertUnchanged(Month.campsites[i], Before[i]);
            }
        }

        [Test]
        public void AvailableDatesBySite_KeepsEntriesWithTheSameCampsiteId_AsSeparateRows()
        {
            CampsitesDataEntry First = Entry("100", Statuses(30, 3));
            CampsitesDataEntry Second = Entry("100", Statuses(30, 4));
            var Month = new AvailabilityEntries { campsites = new List<CampsitesDataEntry> { First, Second } };

            List<CampsiteAvailableDates> Result = AvailabilityApi.AvailableDatesBySite(Month, new DateTime(2026, 11, 1), 30);

            Assert.That(Result, Has.Count.EqualTo(2));
            Assert.That(Result[0].Entry, Is.SameAs(First));
            Assert.That(Result[0].AvailableDates, Is.EqualTo(Days(2026, 11, 3)));
            Assert.That(Result[1].Entry, Is.SameAs(Second));
            Assert.That(Result[1].AvailableDates, Is.EqualTo(Days(2026, 11, 4)));
        }

        [Test]
        public void AvailableDatesBySite_MonthWithoutCampsites_IsEmpty()
        {
            Assert.That(AvailabilityApi.AvailableDatesBySite(new AvailabilityEntries(), new DateTime(2026, 11, 1), 30), Is.Empty);
        }

        // the public GenerateDates is kept for compatibility; the search no longer calls it
        [Test]
        public void GenerateDates_StillSetsAvailabilityDatesToTheCalculatedDates()
        {
            CampsitesDataEntry Source = Entry("100", Statuses(31, 1, 31));

            Source.GenerateDates(new DateTime(2027, 1, 1), 31);

            Assert.That(Source.AvailabilityDates, Is.EqualTo(Days(2027, 1, 1, 31)));
        }
    }
}
