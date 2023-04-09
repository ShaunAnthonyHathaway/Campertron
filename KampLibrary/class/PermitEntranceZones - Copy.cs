public class PermitEntranceZonesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<PermitEntranceZonesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public PermitEntranceZonesMetadata? Meta { get; set; }
}
public class PermitEntranceZonesRecdata
{
    public String? PermitEntranceZoneID { get; set; }
    public String? Zone { get; set; }
    public String? PermitEntranceID { get; set; }
}
public class PermitEntranceZonesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}