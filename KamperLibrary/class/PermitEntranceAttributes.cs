using System.ComponentModel.DataAnnotations;

public class PermitEntranceAttributesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<PermitEntranceAttributesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public PermitEntranceAttributesMetadata? Meta { get; set; }
}
public class PermitEntranceAttributesRecdata
{
    [Key]
    public Int32? AttributeID { get; set; }
    public String? AttributeName { get; set; }
    public String? AttributeValue { get; set; }
    [Key]
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
}
public class PermitEntranceAttributesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}