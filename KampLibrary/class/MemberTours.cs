public class MemberToursEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<MemberToursRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public MemberToursMetadata? Meta { get; set; }
}
public class MemberToursRecdata
{
    public String? MemberTourID { get; set; }
    public String? TourName { get; set; }
    public String? TourID { get; set; }
}
public class MemberToursMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}