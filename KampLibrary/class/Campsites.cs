public class CampsitesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<CampsitesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public CampsitesMetadata? Meta { get; set; }
}
public class CampsitesRecdata
{
    public String? CampsiteID { get; set; }
    public String? FacilityID { get; set; }
    public String? CampsiteName { get; set; }
    public String? CampsiteType { get; set; }
    public String? TypeOfUse { get; set; }
    public String? Loop { get; set; }
    public bool CampsiteAccessible { get; set; }
    public Double CampsiteLongitude { get; set; }
    public Double CampsiteLatitude { get; set; }
    public String? CreatedDate { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class CampsitesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}