using System.Text.Json;

namespace KampLibrary
{
    static public class LoadJsonFromFile
    {
        static public RecAreaAddressesEntries? LoadEntriesRecAreaAddresses(String JsonFileLocation)
        {
            RecAreaAddressesEntries? source = new RecAreaAddressesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaAddressesEntries>(json);
            }

            return source;
        }
        static public FacilitiesEntries? LoadEntriesFacilities(String JsonFileLocation)
        {
            FacilitiesEntries? source = new FacilitiesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<FacilitiesEntries>(json);
            }

            return source;
        }
        static public ActivityEntries? LoadEntriesActivity(String JsonFileLocation)
        {
            ActivityEntries? source = new ActivityEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<ActivityEntries>(json);
            }

            return source;
        }
        static public OrganizationEntries? LoadEntriesOrganization(String JsonFileLocation)
        {
            OrganizationEntries? source = new OrganizationEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<OrganizationEntries>(json);
            }

            return source;
        }
        static public RecAreaFacilitiesEntries? LoadEntriesRecAreaFacilities(String JsonFileLocation)
        {
            RecAreaFacilitiesEntries? source = new RecAreaFacilitiesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaFacilitiesEntries>(json);
            }

            return source;
        }
        static public OrgEntitiesEntries? LoadEntriesOrgEntities(String JsonFileLocation)
        {
            OrgEntitiesEntries? source = new OrgEntitiesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<OrgEntitiesEntries>(json);
            }

            return source;
        }
        static public RecAreaEntries? LoadEntriesRecArea(String JsonFileLocation)
        {
            RecAreaEntries? source = new RecAreaEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaEntries>(json);
            }

            return source;
        }
        static public CampsiteAttributesEntries? LoadEntriesCampsiteAttributes(String JsonFileLocation)
        {
            CampsiteAttributesEntries? source = new CampsiteAttributesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<CampsiteAttributesEntries>(json);
            }

            return source;
        }
        
    }
}
