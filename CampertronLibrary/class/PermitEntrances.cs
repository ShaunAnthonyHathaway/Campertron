using System.ComponentModel.DataAnnotations;

public class PermitEntrancesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<PermitEntrancesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public PermitEntrancesMetadata? Meta { get; set; }
}
public class PermitEntrancesRecdata
{
    [Key]
    public String? PermitEntranceID { get; set; }
    public String? PermitEntranceType { get; set; }
    public String? FacilityID { get; set; }
    public String? PermitEntranceName { get; set; }
    public String? PermitEntranceDescription { get; set; }
    public String? District { get; set; }
    public String? Town { get; set; }
    public bool PermitEntranceAccessible { get; set; }
    public Double Longitude { get; set; }
    public Double Latitude { get; set; }
    public String? CreatedDate { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class PermitEntrancesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}