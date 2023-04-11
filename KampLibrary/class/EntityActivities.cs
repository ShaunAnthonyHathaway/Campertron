using System.ComponentModel.DataAnnotations;

public class EntityActivitiesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<EntityActivitiesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public EntityActivitiesMetadata? Meta { get; set; }
}
public class EntityActivitiesRecdata
{
    [Key]
    public String? EntityID { get; set; }
    public Int32? ActivityID { get; set; }
    public String? ActivityDescription { get; set; }
    public String? ActivityFeeDescription { get; set; }
    public String? EntityType { get; set; }
}
public class EntityActivitiesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}