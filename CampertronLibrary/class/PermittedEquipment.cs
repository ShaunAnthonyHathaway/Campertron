using System.ComponentModel.DataAnnotations;

public class PermittedEquipmentEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<PermittedEquipmentRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public PermittedEquipmentMetadata? Meta { get; set; }
}
public class PermittedEquipmentRecdata
{
    [Key]
    public String? EquipmentName { get; set; }
    public Double? MaxLength { get; set; }
    [Key]
    public String? CampsiteID { get; set; }
}
public class PermittedEquipmentMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}