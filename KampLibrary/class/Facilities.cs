public class FacilitiesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<FacilitiesData>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public FacilitiesMetadata? Meta { get; set; }
}
public class FacilitiesData
{
    public String? FacilityID { get; set; }
    public String? LegacyFacilityID { get; set; }
    public String? OrgFacilityID { get; set; }
    public String? ParentOrgID { get; set; }
    public String? ParentRecAreaID { get; set; }
    public String? FacilityName { get; set; }
    public String? FacilityDescription { get; set; }
    public String? FacilityTypeDescription { get; set; }
    public String? FacilityUseFeeDescription { get; set; }
    public String? FacilityDirections { get; set; }
    public String? FacilityPhone { get; set; }
    public String? FacilityEmail { get; set; }
    public String? FacilityReservationURL { get; set; }
    public String? FacilityMapURL { get; set; }
    public String? FacilityAdaAccess { get; set; }
    public GEOJSON? GEOJSON { get; set; }
    public Double FacilityLongitude { get; set; }
    public Double FacilityLatitude { get; set; }
    public String? Keywords { get; set; }
    public String? StayLimit { get; set; }
    public bool Reservable { get; set; }
    public bool Enabled { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class FacilitiesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}
