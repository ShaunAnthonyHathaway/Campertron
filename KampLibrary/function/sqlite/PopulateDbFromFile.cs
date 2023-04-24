using KampLibrary.function.json;

namespace KampLibrary.function.sqlite
{
    static public class PopulateDbFromFile
    {
        static public void Populate(String BaseDirectory)
        {
            using var db = new RecreationDotOrgContext();

            ActivityEntries? ActivityData = LoadJsonFromFile.LoadEntriesActivity(BaseDirectory + "Activities_API_v1.json");
            CampsiteAttributesEntries? CampsiteAttributesData = LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "CampsiteAttributes_API_v1.json");
            CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "Campsites_API_v1.json");
            EntityActivitiesEntries? EntityActivitiesData = LoadJsonFromFile.LoadEntriesEntityActivities(BaseDirectory + "EntityActivities_API_v1.json");
            EventsEntries? EventsData = LoadJsonFromFile.LoadEntriesEvents(BaseDirectory + "Events_API_v1.json");
            FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "Facilities_API_v1.json");
            FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "FacilityAddresses_API_v1.json");
            LinksEntries? LinksData = LoadJsonFromFile.LoadEntriesLinks(BaseDirectory + "Links_API_v1.json");
            MediaEntries? MediaData = LoadJsonFromFile.LoadEntriesMediaEntries(BaseDirectory + "Media_API_v1.json");
            MemberToursEntries? MemberToursData = LoadJsonFromFile.LoadEntriesMemberTours(BaseDirectory + "MemberTours_API_v1.json");
            OrganizationEntries? OrganizationData = LoadJsonFromFile.LoadEntriesOrganization(BaseDirectory + "Organizations_API_v1.json");
            OrgEntitiesEntries? OrgEntitiesData = LoadJsonFromFile.LoadEntriesOrgEntities(BaseDirectory + "OrgEntities_API_v1.json");
            PermitEntranceAttributesEntries? PermitEntranceAttributesData = LoadJsonFromFile.LoadEntriesPermitEntranceAttributes(BaseDirectory + "PermitEntranceAttributes_API_v1.json");
            PermitEntrancesEntries? PermitEntrancesData = LoadJsonFromFile.LoadEntriesPermitEntrances(BaseDirectory + "PermitEntrances_API_v1.json");
            PermitEntranceZonesEntries? PermitEntranceZonesData = LoadJsonFromFile.LoadEntriesPermitEntranceZones(BaseDirectory + "PermitEntranceZones_API_v1.json");
            PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "PermittedEquipment_API_v1.json");
            RecAreaAddressesEntries? RecAreaAddressesData = LoadJsonFromFile.LoadEntriesRecAreaAddresses(BaseDirectory + "RecAreaAddresses_API_v1.json");
            RecAreaFacilitiesEntries? RecAreaFacilitiesData = LoadJsonFromFile.LoadEntriesRecAreaFacilities(BaseDirectory + "RecAreaFacilities_API_v1.json");
            RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "RecAreas_API_v1.json");
            TourAttributesEntries? TourAttributesData = LoadJsonFromFile.LoadEntriesTourAttributes(BaseDirectory + "TourAttributes_API_v1.json");
            ToursEntries? ToursData = LoadJsonFromFile.LoadEntriesTours(BaseDirectory + "Tours_API_v1.json");
            if (ActivityData?.RecEntries != null)
            {
                foreach (var ThisActivityData in ActivityData.RecEntries)
                {
                    db.ActivityEntries.Add(ThisActivityData);
                }
            }            
            if (CampsiteAttributesData?.RecEntries != null)
            {
                foreach (var ThisData in CampsiteAttributesData.RecEntries)
                {
                    db.CampsiteAttributesEntries.Add(ThisData);
                }
            }
            if (CampsitesData?.RecEntries != null)
            {
                foreach (var ThisData in CampsitesData.RecEntries)
                {
                    db.CampsitesEntries.Add(ThisData);
                }
            }
            if (EntityActivitiesData?.RecEntries != null)
            {
                foreach (var ThisData in EntityActivitiesData.RecEntries)
                {
                    db.EntityActivitiesEntries.Add(ThisData);
                }
            }
            if (EventsData?.RecEntries != null)
            {
                foreach (var ThisData in EventsData.RecEntries)
                {
                    db.EventsEntries.Add(ThisData);
                }
            }
            if (FacilityData?.RecEntries != null)
            {
                foreach (var ThisData in FacilityData.RecEntries)
                {
                    db.FacilitiesEntries.Add(ThisData);
                }
            }
            if (FacilityAddressesData?.RecEntries != null)
            {
                foreach (var ThisData in FacilityAddressesData.RecEntries)
                {
                    db.FacilityAddressesEntries.Add(ThisData);
                }
            }
            if (LinksData?.RecEntries != null)
            {
                foreach (var ThisData in LinksData.RecEntries)
                {
                    db.LinksEntries.Add(ThisData);
                }
            }
            if (MediaData?.RecEntries != null)
            {
                foreach (var ThisData in MediaData.RecEntries)
                {
                    db.MediaEntries.Add(ThisData);
                }
            }
            if (MemberToursData?.RecEntries != null)
            {
                foreach (var ThisData in MemberToursData.RecEntries)
                {
                    db.MemberToursEntries.Add(ThisData);
                }
            }
            if (OrganizationData?.RecEntries != null)
            {
                foreach (var ThisData in OrganizationData.RecEntries)
                {
                    db.OrganizationEntries.Add(ThisData);
                }
            }
            if (OrgEntitiesData?.RecEntries != null)
            {
                foreach (var ThisData in OrgEntitiesData.RecEntries)
                {
                    db.OrgEntitiesEntries.Add(ThisData);
                }
            }
            if (PermitEntranceAttributesData?.RecEntries != null)
            {
                foreach (var ThisData in PermitEntranceAttributesData.RecEntries)
                {
                    db.PermitEntranceAttributesEntries.Add(ThisData);
                }
            }
            if (PermitEntrancesData?.RecEntries != null)
            {
                foreach (var ThisData in PermitEntrancesData.RecEntries)
                {
                    db.PermitEntrancesEntries.Add(ThisData);
                }
            }
            if (PermitEntranceZonesData?.RecEntries != null)
            {
                foreach (var ThisData in PermitEntranceZonesData.RecEntries)
                {
                    db.PermitEntranceZonesEntries.Add(ThisData);
                }
            }
            if (PermittedEquipmentData?.RecEntries != null)
            {
                foreach (var ThisData in PermittedEquipmentData.RecEntries)
                {
                    db.PermittedEquipmentEntries.Add(ThisData);
                }
            }
            if (RecAreaAddressesData?.RecEntries != null)
            {
                foreach (var ThisData in RecAreaAddressesData.RecEntries)
                {
                    db.RecAreaAddressesEntries.Add(ThisData);
                }
            }
            if (RecAreaData?.RecEntries != null)
            {
                foreach (var ThisData in RecAreaData.RecEntries)
                {
                    db.RecAreaEntries.Add(ThisData);
                }
            }
            if (TourAttributesData?.RecEntries != null)
            {
                foreach (var ThisData in TourAttributesData.RecEntries)
                {
                    db.TourAttributesEntries.Add(ThisData);
                }
            }
            if (ToursData?.RecEntries != null)
            {
                foreach (var ThisData in ToursData.RecEntries)
                {
                    db.ToursEntries.Add(ThisData);
                }
            }
            db.SaveChanges();
        }
    }
}
