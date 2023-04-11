using System.ComponentModel.DataAnnotations;

public class RecAreaEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<RecAreaFacility>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public RecAreaMetadata? Meta { get; set; }
}
public class RecAreaFacility
{
    [Key]
    public String? RecAreaID { get; set; }
    public String? OrgRecAreaID { get; set; }
    public String? ParentOrgID { get; set; }
    public String? RecAreaName { get; set; }
    public String? RecAreaDescription { get; set; }
    public String? RecAreaFeeDescription { get; set; }
    public String? RecAreaDirections { get; set; }
    public String? RecAreaPhone { get; set; }
    public String? RecAreaEmail { get; set; }
    public String? RecAreaReservationURL { get; set; }
    public String? RecAreaMapURL { get; set; }
    public GEOJSON? GEOJSON { get; set; }
    public Double RecAreaLongitude { get; set; }
    public Double RecAreaLatitude { get; set; }
    public String? StayLimit { get; set; }
    public String? Keywords { get; set; }
    public bool Reservable { get; set; }
    public bool Enabled { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class RecAreaMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}
