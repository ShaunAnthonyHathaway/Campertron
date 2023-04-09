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
        CampsitesEntries? CampsitesData = KampLibrary.LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "Campsites_API_v1.json");
        EntityActivitiesEntries? EntityActivitiesData = KampLibrary.LoadJsonFromFile.LoadEntriesEntityActivities(BaseDirectory + "EntityActivities_API_v1.json");
        EventsEntries? EventsData = KampLibrary.LoadJsonFromFile.LoadEntriesEvents(BaseDirectory + "Events_API_v1.json");
        FacilityAddressesEntries? FacilityAddressesData = KampLibrary.LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "FacilityAddresses_API_v1.json");
        LinksEntries? LinksData = KampLibrary.LoadJsonFromFile.LoadEntriesLinks(BaseDirectory + "Links_API_v1.json");
        MediaEntries? MediaData = KampLibrary.LoadJsonFromFile.LoadEntriesMediaEntries(BaseDirectory + "Media_API_v1.json");
        MemberToursEntries? MemberToursData = KampLibrary.LoadJsonFromFile.LoadEntriesMemberTours(BaseDirectory + "MemberTours_API_v1.json");
        PermitEntranceAttributesEntries? PermitEntranceAttributesData = KampLibrary.LoadJsonFromFile.LoadEntriesPermitEntranceAttributes(BaseDirectory + "PermitEntranceAttributes_API_v1.json");
        PermitEntrancesEntries? PermitEntrancesData = KampLibrary.LoadJsonFromFile.LoadEntriesPermitEntrances(BaseDirectory + "PermitEntrances_API_v1.json");
        PermitEntranceZonesEntries? PermitEntranceZonesData = KampLibrary.LoadJsonFromFile.LoadEntriesPermitEntranceZones(BaseDirectory + "PermitEntranceZones_API_v1.json");
        PermittedEquipmentEntries? PermittedEquipmentData = KampLibrary.LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "PermittedEquipment_API_v1.json");
        TourAttributesEntries? TourAttributesData = KampLibrary.LoadJsonFromFile.LoadEntriesTourAttributes(BaseDirectory + "TourAttributes_API_v1.json");
        ToursEntries? ToursData = KampLibrary.LoadJsonFromFile.LoadEntriesTours(BaseDirectory + "Tours_API_v1.json");
        Console.ReadLine();
    }
}