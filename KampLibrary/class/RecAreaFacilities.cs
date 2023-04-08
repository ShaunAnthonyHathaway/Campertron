public class RecAreaFacilitiesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<RecAreaFacilitiesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public RecAreaFacilitiesMetadata? Meta { get; set; }
}
public class RecAreaFacilitiesRecdata
{
    public String? RecAreaID { get; set; }
    public String? FacilityID { get; set; }
}
public class RecAreaFacilitiesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}