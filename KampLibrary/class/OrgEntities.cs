public class OrgEntitiesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<OrgEntitiesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public OrgEntitiesMetadata? Meta { get; set; }
}
public class OrgEntitiesRecdata
{
    public String? OrgID { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
}
public class OrgEntitiesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}