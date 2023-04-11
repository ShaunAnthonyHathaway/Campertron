using System.ComponentModel.DataAnnotations;

public class FacilityAddressesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<FacilityAddressesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public FacilityAddressesMetadata? Meta { get; set; }
}
public class FacilityAddressesRecdata
{
    [Key]
    public String? FacilityAddressID { get; set; }
    public String? FacilityID { get; set; }
    public String? FacilityAddressType { get; set; }
    public String? FacilityStreetAddress1 { get; set; }
    public String? FacilityStreetAddress2 { get; set; }
    public String? FacilityStreetAddress3 { get; set; }
    public String? City { get; set; }
    public String? PostalCode { get; set; }
    public String? AddressStateCode { get; set; }
    public String? AddressCountryCode { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class FacilityAddressesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}