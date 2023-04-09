public class LinksEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<LinksRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public LinksMetadata? Meta { get; set; }
}
public class LinksRecdata
{
    public String? EntityLinkID { get; set; }
    public String? LinkType { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
    public String? Title { get; set; }
    public String? Description { get; set; }
    public String? URL { get; set; }
}
public class LinksMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}