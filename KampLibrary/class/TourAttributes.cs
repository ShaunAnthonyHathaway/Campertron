using System.ComponentModel.DataAnnotations;

public class TourAttributesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<TourAttributesFacilityRecData>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public TourAttributesMetadata? Meta { get; set; }
}
public class TourAttributesFacilityRecData
{
    [Key]
    public Double AttributeID { get; set; }
    public String? AttributeName { get; set; }
    public String? AttributeValue { get; set; }
    [Key]
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
}
public class TourAttributesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}
