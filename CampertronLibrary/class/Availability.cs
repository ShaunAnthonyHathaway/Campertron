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
    public List<DateTime> AvailabilityDates { get; set; }
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
    public void GenerateDates(DateTime ChkDt, int TotalDaysInMonth)
    {
        this.AvailabilityDates = new List<DateTime>();
        if (this.availabilities != null)
        {
            int counter = TotalDaysInMonth - this.availabilities.Count;
            foreach (availabilities a in this.availabilities)
            {
                if (a.availability == "Available")
                {
                    this.AvailabilityDates.Add(ChkDt.AddDays(counter));
                }
                counter++;
            }
        }
    }
}
public class availabilities
{
    public String availability { get; set; }
}