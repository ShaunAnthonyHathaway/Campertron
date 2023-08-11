using System.Text.Json;

namespace CampertronLibrary.function.RecDotOrg.data
{
    static public class LoadJsonFromFile
    {
        static public RecAreaAddressesEntries? LoadEntriesRecAreaAddresses(string JsonFileLocation)
        {
            RecAreaAddressesEntries? source = new RecAreaAddressesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaAddressesEntries>(json);
            }
            return source;
        }
        static public FacilitiesEntries? LoadEntriesFacilities(string JsonFileLocation)
        {
            FacilitiesEntries? source = new FacilitiesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<FacilitiesEntries>(json);
            }
            return source;
        }
        static public ActivityEntries? LoadEntriesActivity(string JsonFileLocation)
        {
            ActivityEntries? source = new ActivityEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<ActivityEntries>(json);
            }
            return source;
        }
        static public OrganizationEntries? LoadEntriesOrganization(string JsonFileLocation)
        {
            OrganizationEntries? source = new OrganizationEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<OrganizationEntries>(json);
            }
            return source;
        }
        static public RecAreaFacilitiesEntries? LoadEntriesRecAreaFacilities(string JsonFileLocation)
        {
            RecAreaFacilitiesEntries? source = new RecAreaFacilitiesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaFacilitiesEntries>(json);
            }
            return source;
        }
        static public OrgEntitiesEntries? LoadEntriesOrgEntities(string JsonFileLocation)
        {
            OrgEntitiesEntries? source = new OrgEntitiesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<OrgEntitiesEntries>(json);
            }
            return source;
        }
        static public RecAreaEntries? LoadEntriesRecArea(string JsonFileLocation)
        {
            RecAreaEntries? source = new RecAreaEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaEntries>(json);
            }
            return source;
        }
        static public CampsiteAttributesEntries? LoadEntriesCampsiteAttributes(string JsonFileLocation)
        {
            CampsiteAttributesEntries? source = new CampsiteAttributesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<CampsiteAttributesEntries>(json);
            }
            return source;
        }
        static public CampsitesEntries? LoadEntriesCampsites(string JsonFileLocation)
        {
            CampsitesEntries? source = new CampsitesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<CampsitesEntries>(json);
            }
            return source;
        }
        static public EntityActivitiesEntries? LoadEntriesEntityActivities(string JsonFileLocation)
        {
            EntityActivitiesEntries? source = new EntityActivitiesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<EntityActivitiesEntries>(json);
            }
            return source;
        }
        static public EventsEntries? LoadEntriesEvents(string JsonFileLocation)
        {
            EventsEntries? source = new EventsEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<EventsEntries>(json);
            }
            return source;
        }
        static public FacilityAddressesEntries? LoadEntriesFacilityAddresses(string JsonFileLocation)
        {
            FacilityAddressesEntries? source = new FacilityAddressesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<FacilityAddressesEntries>(json);
            }
            return source;
        }
        static public LinksEntries? LoadEntriesLinks(string JsonFileLocation)
        {
            LinksEntries? source = new LinksEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<LinksEntries>(json);
            }
            return source;
        }
        static public MediaEntries? LoadEntriesMediaEntries(string JsonFileLocation)
        {
            MediaEntries? source = new MediaEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<MediaEntries>(json);
            }
            return source;
        }
        static public MemberToursEntries? LoadEntriesMemberTours(string JsonFileLocation)
        {
            MemberToursEntries? source = new MemberToursEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<MemberToursEntries>(json);
            }
            return source;
        }
        static public PermitEntranceAttributesEntries? LoadEntriesPermitEntranceAttributes(string JsonFileLocation)
        {
            PermitEntranceAttributesEntries? source = new PermitEntranceAttributesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntranceAttributesEntries>(json);
            }
            return source;
        }
        static public PermitEntrancesEntries? LoadEntriesPermitEntrances(string JsonFileLocation)
        {
            PermitEntrancesEntries? source = new PermitEntrancesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntrancesEntries>(json);
            }
            return source;
        }
        static public PermitEntranceZonesEntries? LoadEntriesPermitEntranceZones(string JsonFileLocation)
        {
            PermitEntranceZonesEntries? source = new PermitEntranceZonesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermitEntranceZonesEntries>(json);
            }
            return source;
        }
        static public PermittedEquipmentEntries? LoadEntriesPermittedEquipment(string JsonFileLocation)
        {
            PermittedEquipmentEntries? source = new PermittedEquipmentEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<PermittedEquipmentEntries>(json);
            }
            return source;
        }
        static public TourAttributesEntries? LoadEntriesTourAttributes(string JsonFileLocation)
        {
            TourAttributesEntries? source = new TourAttributesEntries();
            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<TourAttributesEntries>(json);
            }
            return source;
        }
        static public ToursEntries? LoadEntriesTours(string JsonFileLocation)
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