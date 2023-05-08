using System.ComponentModel.DataAnnotations;

public class EventsEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<EventsRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public EventsMetadata? Meta { get; set; }
}
public class EventsRecdata
{
    [Key]
    public String? EventID { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
    public String? EventName { get; set; }
    public String? Description { get; set; }
    public String? EventTypeDescription { get; set; }
    public String? EventFeeDescription { get; set; }
    public String? EventFrequencyRateDescription { get; set; }
    public String? EventScopeDescription { get; set; }
    public String? EventAgeGroup { get; set; }
    public bool? EventRegistrationRequired { get; set; }
    public String? EventADAAccess { get; set; }
    public String? EventComments { get; set; }
    public String? EventEmail { get; set; }
    public String? EventURLAddress { get; set; }
    public String? EventURLText { get; set; }
    public String? EventStartDate { get; set; }
    public String? EventEndDate { get; set; }
    public String? SponsorName { get; set; }
    public String? SponsorClassType { get; set; }
    public String? SponsorPhone { get; set; }
    public String? SponsorEmail { get; set; }
    public String? SponsorURLAddress { get; set; }
    public String? SponsorURLText { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class EventsMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}