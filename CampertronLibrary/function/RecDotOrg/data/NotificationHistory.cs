using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronLibrary.function.RecDotOrg.data
{
    // Camp.History's re-arming (decision D104, the maintainer's decision of 2026-11-24 that replaced the rule of D8's
    // refinement, "never notified again"; business-rules §5, ER-03). A marker, Camp.History/{yyyy}/{M}/{d}/{CampsiteID}, means that a
    // notification showing that campsite and night was delivered while it was available. The monitoring notifies it once
    // while it stays available; once a check that completed has seen it unavailable, its marker is removed, so that it is
    // notified again if it becomes available again (a cancellation).
    //  - Evidence only from a month the check fetched and read (RunningData.SiteData: a month is there only once its
    //    answer was received and read; a failed, blocked or stopped request leaves nothing there), and only in a check whose
    //    searches all completed (MonitoringService.ReportCheck runs only then). A month that was not fetched, another
    //    campground's month, a campsite the month does not list and a night the month gives no status for re-arm nothing.
    //  - "Unavailable" is any status other than exactly "Available", the one status the search counts as available
    //    (CampsitesDataEntry.CalculateAvailableDates), read with the search's own day offsets; an empty status is not an
    //    observation. A campsite listed twice is available when either entry says so.
    //  - The marker folder is removed only when it is empty (Directory.Delete without recursion); a link is never
    //    followed or removed, nor is anything under a linked year, month or day folder. A marker that can't be removed
    //    stays (it keeps suppressing, as before) and is counted, so the check can say so; nothing here stops a check.
    //  - Nothing is created: no Camp.History, year, month or day folder, and no marker. Empty day, month and year folders
    //    are left as they are (the layout makes at most one per notified night's date).
    //  - The format is unchanged, so earlier versions read the same history, and a marker written before this rule is
    //    re-armed the first time a check sees its night unavailable.
    internal static class NotificationHistory
    {
        // the one status the search counts as available (CampsitesDataEntry.CalculateAvailableDates)
        private const string Available = "Available";

        // removes one marker folder, only if it is empty; replaceable so tests can make a removal fail on any platform
        internal static Action<string> RemoveMarker { get; set; } = Folder => Directory.Delete(Folder, false);

        // what a re-arm did: the markers it removed, and those of nights seen unavailable that it could not remove
        internal readonly record struct RearmResult(int Removed, int NotRemoved);

        // the night's status in a month, as the search reads it
        private enum Seen
        {
            NotObserved,
            Available,
            Unavailable
        }

        // Re-arms the markers of the nights the given months show unavailable (see the class comment). Months: a completed
        // check's months by their URL (AvailabilityApi.MonthUrl), as RunningData.SiteData holds them. Only the unattended
        // monitoring has a history (Load.UsesNotificationHistory)
        internal static RearmResult Rearm(CampertronInternalConfig Config, IEnumerable<KeyValuePair<string, AvailabilityEntries>> Months)
        {
            if (!Load.UsesNotificationHistory(Config) || string.IsNullOrEmpty(Config.ConfigPath))
            {
                return default;
            }
            string Root = Path.Join(Config.ConfigPath, "Camp.History");
            if (!IsPlainFolder(Root))
            {
                return default;
            }
            //each fetched month: every campsite entry it lists, by campsite ID (ordinal, as the markers' names are written)
            var ByMonth = new Dictionary<DateTime, Dictionary<string, List<List<availabilities>>>>();
            foreach (KeyValuePair<string, AvailabilityEntries> Month in Months)
            {
                if (Month.Value?.campsites == null || !AvailabilityApi.TryMonthOfUrl(Month.Key, out DateTime First))
                {
                    continue;
                }
                if (!ByMonth.TryGetValue(First, out var Sites))
                {
                    Sites = new Dictionary<string, List<List<availabilities>>>(StringComparer.Ordinal);
                    ByMonth[First] = Sites;
                }
                foreach (CampsitesDataEntry Entry in Month.Value.campsites)
                {
                    if (string.IsNullOrEmpty(Entry?.campsite_id) || Entry.availabilities == null)
                    {
                        continue;
                    }
                    if (!Sites.TryGetValue(Entry.campsite_id, out var Entries))
                    {
                        Entries = new List<List<availabilities>>();
                        Sites[Entry.campsite_id] = Entries;
                    }
                    Entries.Add(Entry.availabilities);
                }
            }
            int Removed = 0;
            int NotRemoved = 0;
            foreach (var (First, Sites) in ByMonth)
            {
                //the folders are named from numbers, as the markers are written (Load.MarkerPath); no name is parsed
                string YearFolder = Path.Join(Root, First.Year.ToString(CultureInfo.InvariantCulture));
                string MonthFolder = Path.Join(YearFolder, First.Month.ToString(CultureInfo.InvariantCulture));
                if (!IsPlainFolder(YearFolder) || !IsPlainFolder(MonthFolder))
                {
                    continue;
                }
                int DaysInMonth = DateTime.DaysInMonth(First.Year, First.Month);
                for (int Day = 1; Day <= DaysInMonth; Day++)
                {
                    string DayFolder = Path.Join(MonthFolder, Day.ToString(CultureInfo.InvariantCulture));
                    if (!IsPlainFolder(DayFolder))
                    {
                        continue;
                    }
                    List<string> Markers;
                    try
                    {
                        Markers = Directory.EnumerateDirectories(DayFolder).ToList();
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        //a day that can't be listed now re-arms nothing; a later check tries again
                        continue;
                    }
                    foreach (string Marker in Markers)
                    {
                        //a marker is named by its campsite ID: only one this month lists can be judged
                        if (!Sites.TryGetValue(Path.GetFileName(Marker), out var Entries) || Observe(Entries, Day, DaysInMonth) != Seen.Unavailable)
                        {
                            continue;
                        }
                        if (TryRemove(Marker))
                        {
                            Removed++;
                        }
                        else
                        {
                            NotRemoved++;
                        }
                    }
                }
            }
            return new RearmResult(Removed, NotRemoved);
        }

        // the check's warning when markers it should have removed are still there: fixed words and a number, never a path
        internal static string NotRemovedText(int NotRemoved)
        {
            return "Camp.History: " + ConsoleUi.Count(NotRemoved, "marker", "markers") +
                " of notified openings that are no longer available could not be removed; those openings are not notified again until a later check removes them.";
        }

        // what the month says about a night: the statuses are the last Count days of the month (counter = DaysInMonth -
        // Count, as CalculateAvailableDates reads them), so a day before them has no status in that entry
        private static Seen Observe(List<List<availabilities>> Entries, int Day, int DaysInMonth)
        {
            bool Unavailable = false;
            foreach (List<availabilities> Statuses in Entries)
            {
                int Index = Day - 1 - (DaysInMonth - Statuses.Count);
                if (Index < 0 || Index >= Statuses.Count)
                {
                    continue;
                }
                string? Status = Statuses[Index]?.availability;
                if (Status == Available)
                {
                    return Seen.Available;
                }
                if (!string.IsNullOrEmpty(Status))
                {
                    Unavailable = true;
                }
            }
            return Unavailable ? Seen.Unavailable : Seen.NotObserved;
        }

        private static bool TryRemove(string Marker)
        {
            try
            {
                //a link is not a marker Campertron wrote: it is left as it is, and so is what it points to
                if (IsLink(Marker))
                {
                    return false;
                }
                RemoveMarker(Marker);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                //not empty (a file or folder inside), removed meanwhile, or not permitted: the marker stays as it is
                return !Directory.Exists(Marker) && !File.Exists(Marker);
            }
        }

        // an existing folder that is not a link
        private static bool IsPlainFolder(string Folder)
        {
            try
            {
                return Directory.Exists(Folder) && !IsLink(Folder);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsLink(string Entry)
        {
            return (File.GetAttributes(Entry) & FileAttributes.ReparsePoint) != 0;
        }
    }
}
