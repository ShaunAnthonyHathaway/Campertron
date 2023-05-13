using System.Text.Json;

namespace KamperLibrary.function.json
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
        static public CampsitesEntries? LoadEntriesCampsites(String JsonFileLocation)
        {
            CampsitesEntries? source = new CampsitesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<CampsitesEntries>(json);
            }
            return source;
        }
        static public EntityActivitiesEntries? LoadEntriesEntityActivities(String JsonFileLocation)
        {
            EntityActivitiesEntries? source = new EntityActivitiesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<EntityActivitiesEntries>(json);
            }
            return source;
        }
        static public EventsEntries? LoadEntriesEvents(String JsonFileLocation)
        {
            EventsEntries? source = new EventsEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<EventsEntries>(json);
            }
            return source;
        }
        static public FacilityAddressesEntries? LoadEntriesFacilityAddresses(String JsonFileLocation)
        {
            FacilityAddressesEntries? source = new FacilityAddressesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<FacilityAddressesEntries>(json);
            }
            return source;
        }
        static public LinksEntries? LoadEntriesLinks(String JsonFileLocation)
        {
            LinksEntries? source = new LinksEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<LinksEntries>(json);
            }
            return source;
        }
        static public MediaEntries? LoadEntriesMediaEntries(String JsonFileLocation)
        {
            MediaEntries? source = new MediaEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<MediaEntries>(json);
            }
            return source;
        }
        static public MemberToursEntries? LoadEntriesMemberTours(String JsonFileLocation)
        {
            MemberToursEntries? source = new MemberToursEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<MemberToursEntries>(json);
            }
            return source;
        }
        static public PermitEntranceAttributesEntries? LoadEntriesPermitEntranceAttributes(String JsonFileLocation)
        {
            PermitEntranceAttributesEntries? source = new PermitEntranceAttributesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntranceAttributesEntries>(json);
            }
            return source;
        }
        static public PermitEntrancesEntries? LoadEntriesPermitEntrances(String JsonFileLocation)
        {
            PermitEntrancesEntries? source = new PermitEntrancesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntrancesEntries>(json);
            }
            return source;
        }
        static public PermitEntranceZonesEntries? LoadEntriesPermitEntranceZones(String JsonFileLocation)
        {
            PermitEntranceZonesEntries? source = new PermitEntranceZonesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntranceZonesEntries>(json);
            }
            return source;
        }
        static public PermittedEquipmentEntries? LoadEntriesPermittedEquipment(String JsonFileLocation)
        {
            PermittedEquipmentEntries? source = new PermittedEquipmentEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermittedEquipmentEntries>(json);
            }
            return source;
        }
        static public TourAttributesEntries? LoadEntriesTourAttributes(String JsonFileLocation)
        {
            TourAttributesEntries? source = new TourAttributesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<TourAttributesEntries>(json);
            }
            return source;
        }
        static public ToursEntries? LoadEntriesTours(String JsonFileLocation)
        {
            ToursEntries? source = new ToursEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<ToursEntries>(json);
            }
            return source;
        }        
    }
}