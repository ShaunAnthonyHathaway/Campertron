using System.Security.Cryptography;

public class AvailabilityEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("campsites")]
    public List<CampsitesDataEntry>? campsites { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("count")]
    public Int32? AvailabilityCount { get; set; }
}
public class CampsitesDataEntry
{
    public List<availabilities>? availabilities { get; set; }
    //kept for compatibility (public model); the search no longer sets or reads it: a month's CampsitesDataEntry
    //objects are shared by every search of that campground and month (RunData.SiteData), see TD-40
    public List<DateTime>? AvailabilityDates { get; set; }
    public String? campsite_id { get; set; }
    public String? campsite_reserve_type { get; set; }
    public String? campsite_rules { get; set; }
    public String? campsite_type { get; set; }
    public String? capacity_rating { get; set; }
    public String? loop { get; set; }
    public Int32? max_num_people { get; set; }
    public Int32? min_num_people { get; set; }
    public List<object>? quantities { get; set; }
    public String? site { get; set; }
    public String? supplemental_camping { get; set; }
    public String? type_of_use { get; set; }
    //kept for compatibility: sets AvailabilityDates on this object. Not used by the search and not safe on an
    //object other threads read; the search uses CalculateAvailableDates instead (TD-40)
    public void GenerateDates(DateTime ChkDt, int TotalDaysInMonth)
    {
        this.AvailabilityDates = CalculateAvailableDates(this.availabilities, ChkDt, TotalDaysInMonth);
    }
    //the dates whose status is exactly "Available", as a new list owned by the caller. Only reads Availabilities.
    //The statuses are taken to be the last Availabilities.Count days of the month starting at ChkDt (open question 13)
    public static List<DateTime> CalculateAvailableDates(IReadOnlyList<availabilities>? Availabilities, DateTime ChkDt, int TotalDaysInMonth)
    {
        List<DateTime> AvailableDates = new List<DateTime>();
        if (Availabilities != null)
        {
            int counter = TotalDaysInMonth - Availabilities.Count;
            foreach (availabilities a in Availabilities)
            {
                if (a.availability == "Available")
                {
                    AvailableDates.Add(ChkDt.AddDays(counter));
                }
                counter++;
            }
        }
        return AvailableDates;
    }
}
public class availabilities
{
    public String? availability { get; set; }
}
//one campsite entry of a cached month with the available dates one search derived from it (AvailabilityApi.AvailableDatesBySite).
//Entry is shared and read-only; AvailableDates belongs to that search
public sealed record CampsiteAvailableDates(CampsitesDataEntry Entry, List<DateTime> AvailableDates);
