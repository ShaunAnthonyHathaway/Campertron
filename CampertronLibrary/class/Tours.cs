using System.ComponentModel.DataAnnotations;

public class ToursEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<ToursFacilityRecData>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public ToursMetadata? Meta { get; set; }
}
public class ToursFacilityRecData
{
    [Key]
    public String? TourID { get; set; }
    public String? FacilityID { get; set; }
    public String? TourName { get; set; }
    public String? TourType { get; set; }
    public String? TourDescription { get; set; }
    public Double? TourDuration { get; set; }
    public bool? TourAccessible { get; set; }
    public String? CreatedDate { get; set; }
    public String? LastUpdatedDate { get; set; }
    public String? ATTRIBUTES { get; set; }
    public String? MEMBERTOURS { get; set; }
    public String? ENTITYMEDIA { get; set; }
}
public class ToursMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}
