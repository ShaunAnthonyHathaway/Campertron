public class CampsiteAttributesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<CampsiteAttributesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public CampsiteAttributesMetadata? Meta { get; set; }
}
public class CampsiteAttributesRecdata
{
    public Int32? AttributeID { get; set; }
    public String? AttributeName { get; set; }
    public String? AttributeValue { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
}
public class CampsiteAttributesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}