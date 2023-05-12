public class ReturnParkCampground
{
    public String ParkName { get; set; }
    public String CampsiteName { get; set; }
}
public class AvailabilityData
{
    public String CampsiteID { get; set; }
    public String CampsiteType { get; set; }
    public String CampsiteName { get; set; }
    public String CampsiteLoop { get; set; }
    public DateTime CampsiteAvailableDate { get; set; }
    public List<String> PermittedEquipmentList { get; set; }
    public List<AttributeValuePair> CampsiteAttributes { get; set; }
}
public class AttributeValuePair
{
    public String AttributeName { get; set; }
    public String AttributeValue { get; set; }
}