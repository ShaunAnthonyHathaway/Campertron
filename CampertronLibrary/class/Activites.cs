using System.ComponentModel.DataAnnotations;

public class ActivityEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<ActivityRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public ActivityMetadata? Meta { get; set; }
}
public class ActivityRecdata
{
    [Key]
    public Int32? ActivityID { get; set; }
    public Int32? ActivityParentID { get; set; }
    public String? ActivityName { get; set; }
    public Int32? ActivityLevel { get; set; }
}
public class ActivityMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}