using CampertronLibrary.function.RecDotOrg.data;

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
    public Int32? Maxppl { get; set; }
    public Int32? Minppl { get; set; }
    public DateTime CampsiteAvailableDate { get; set; }
    public List<String> PermittedEquipmentList { get; set; }
    public AttributeValueLists CampsiteAttributeLists { get; set; }
}
public class AttributeValueLists
{
    public List<AttributeValuePair> AttValuePair { get; set; }
    public List<String> AttValuePairStr { get; set; }
    public void GenerateStringList()
    {
        this.AttValuePairStr = Cache.ConvertAttributeValuePairToString(this.AttValuePair);
    }
}
public class AttributeValuePair
{
    public String AttributeName { get; set; }
    public String AttributeValue { get; set; }
}