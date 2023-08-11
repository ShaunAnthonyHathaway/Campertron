using System.ComponentModel.DataAnnotations;

public class RecAreaAddressesEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<RecAreaAddressesRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public RecAreaAddressesMetadata? Meta { get; set; }
}
public class RecAreaAddressesRecdata
{
    [Key]
    public String? RecAreaAddressID { get; set; }
    public String? RecAreaID { get; set; }
    public String? RecAreaAddressType { get; set; }
    public String? RecAreaStreetAddress1 { get; set; }
    public String? RecAreaStreetAddress2 { get; set; }
    public String? RecAreaStreetAddress3 { get; set; }
    public String? City { get; set; }
    public String? PostalCode { get; set; }
    public String? AddressStateCode { get; set; }
    public String? AddressCountryCode { get; set; }
    public String? LastUpdatedDate { get; set; }
}
public class RecAreaAddressesMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}