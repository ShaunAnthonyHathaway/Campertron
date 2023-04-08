//using (var httpClient = new HttpClient())
//{
//    using (var response = await httpClient.GetAsync("https://localhost:44324/api/Reservation"))
//    {
//        string apiResponse = await response.Content.ReadAsStringAsync();
//    }
//}

class Program
{
    static void Main(string[] args)
    {
        String? BaseDirectory = @"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\";
        RecAreaAddressesEntries? RecAreaAddressesData = KampLibrary.LoadJsonFromFile.LoadEntriesRecAreaAddresses(BaseDirectory + "RecAreaAddresses_API_v1.json");
        FacilitiesEntries? FacilityData = KampLibrary.LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "Facilities_API_v1.json");
        ActivityEntries? ActivityData = KampLibrary.LoadJsonFromFile.LoadEntriesActivity(BaseDirectory + "Activities_API_v1.json");
        OrganizationEntries? OrganizationData = KampLibrary.LoadJsonFromFile.LoadEntriesOrganization(BaseDirectory + "Organizations_API_v1.json");
        RecAreaFacilitiesEntries? RecAreaFacilitiesData = KampLibrary.LoadJsonFromFile.LoadEntriesRecAreaFacilities(BaseDirectory + "RecAreaFacilities_API_v1.json");
        OrgEntitiesEntries? OrgEntitiesData = KampLibrary.LoadJsonFromFile.LoadEntriesOrgEntities(BaseDirectory + "OrgEntities_API_v1.json");
        RecAreaEntries? RecAreaData = KampLibrary.LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "RecAreas_API_v1.json");
        CampsiteAttributesEntries? CampsiteAttributesData = KampLibrary.LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "CampsiteAttributes_API_v1.json");
        Console.WriteLine("123");
        Console.ReadLine();
    }
}