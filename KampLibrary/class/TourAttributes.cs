public class TourAttributesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<TourAttributesFacility>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public TourAttributesMetadata? Meta { get; set; }
}
public class TourAttributesFacility
{
    public Double AttributeID { get; set; }
    public String? AttributeName { get; set; }
    public String? AttributeValue { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
}
public class TourAttributesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}
