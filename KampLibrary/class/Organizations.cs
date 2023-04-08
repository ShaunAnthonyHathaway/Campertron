public class OrganizationEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<OrganizationRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public OrganizationMetadata? Meta { get; set; }
}
public class OrganizationRecdata
{
    public String? OrgID { get; set; }
    public String? OrgName { get; set; }
    public String? OrgImageURL { get; set; }
    public String? OrgURLText { get; set; }
    public String? OrgURLAddress { get; set; }
    public String? OrgType { get; set; }
    public String? OrgAbbrevName { get; set; }
    public String? OrgJurisdictionType { get; set; }
    public String? OrgParentID { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class OrganizationMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}