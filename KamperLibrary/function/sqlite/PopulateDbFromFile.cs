using KamperLibrary.function.json;

namespace KamperLibrary.function.sqlite
{
    static public class PopulateDbFromFile
    {
        static public void Populate(String BaseDirectory)
        {
            using (var db = new RecreationDotOrgContext())
            {
                ActivityEntries? ActivityData = LoadJsonFromFile.LoadEntriesActivity(BaseDirectory + "Activities_API_v1.json"); Console.WriteLine("Processing Activities_API_v1.json");

                if (ActivityData?.RecEntries != null)
                {
                    foreach (var ThisActivityData in ActivityData.RecEntries)
                    {
                        db.ActivityEntries.Add(ThisActivityData);
                    }
                }
                CampsiteAttributesEntries? CampsiteAttributesData = LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "CampsiteAttributes_API_v1.json"); Console.WriteLine("Processing CampsiteAttributes_API_v1.json");
                if (CampsiteAttributesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsiteAttributesData.RecEntries)
                    {
                        db.CampsiteAttributesEntries.Add(ThisData);
                    }
                }
                CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "Campsites_API_v1.json"); Console.WriteLine("Processing Campsites_API_v1.json");
                if (CampsitesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsitesData.RecEntries)
                    {
                        db.CampsitesEntries.Add(ThisData);
                    }
                }
                EntityActivitiesEntries? EntityActivitiesData = LoadJsonFromFile.LoadEntriesEntityActivities(BaseDirectory + "EntityActivities_API_v1.json"); Console.WriteLine("Processing EntityActivities_API_v1.json");
                if (EntityActivitiesData?.RecEntries != null)
                {
                    foreach (var ThisData in EntityActivitiesData.RecEntries)
                    {
                        db.EntityActivitiesEntries.Add(ThisData);
                    }
                }
                EventsEntries? EventsData = LoadJsonFromFile.LoadEntriesEvents(BaseDirectory + "Events_API_v1.json"); Console.WriteLine("Processing Events_API_v1.json");
                if (EventsData?.RecEntries != null)
                {
                    foreach (var ThisData in EventsData.RecEntries)
                    {
                        db.EventsEntries.Add(ThisData);
                    }
                }
                FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "Facilities_API_v1.json"); Console.WriteLine("Processing Facilities_API_v1.json");
                if (FacilityData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityData.RecEntries)
                    {

                        db.FacilitiesEntries.Add(ThisData);
                    }
                }
                FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "FacilityAddresses_API_v1.json"); Console.WriteLine("Processing FacilityAddresses_API_v1.json");
                if (FacilityAddressesData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityAddressesData.RecEntries)
                    {
                        if (ThisData != null)
                        {
                            String? FixedPostCode = ThisData.PostalCode?.Trim()?.ToUpper();
                            if (FixedPostCode != null)
                            {
                                ThisData.PostalCode = KamperLibrary.function.generic.Convert.StateNameToAbbreviation(FixedPostCode);
                            }
                            ThisData.City = ThisData.City?.Trim().Trim(',').ToUpper();
                            db.FacilityAddressesEntries.Add(ThisData);
                        }
                    }
                }
                LinksEntries? LinksData = LoadJsonFromFile.LoadEntriesLinks(BaseDirectory + "Links_API_v1.json"); Console.WriteLine("Processing Links_API_v1.json");
                if (LinksData?.RecEntries != null)
                {
                    foreach (var ThisData in LinksData.RecEntries)
                    {
                        db.LinksEntries.Add(ThisData);
                    }
                }
                MediaEntries? MediaData = LoadJsonFromFile.LoadEntriesMediaEntries(BaseDirectory + "Media_API_v1.json"); Console.WriteLine("Processing Media_API_v1.json");
                if (MediaData?.RecEntries != null)
                {
                    foreach (var ThisData in MediaData.RecEntries)
                    {
                        db.MediaEntries.Add(ThisData);
                    }
                }
                MemberToursEntries? MemberToursData = LoadJsonFromFile.LoadEntriesMemberTours(BaseDirectory + "MemberTours_API_v1.json"); Console.WriteLine("Processing MemberTours_API_v1.json");
                if (MemberToursData?.RecEntries != null)
                {
                    foreach (var ThisData in MemberToursData.RecEntries)
                    {
                        db.MemberToursEntries.Add(ThisData);
                    }
                }
                OrganizationEntries? OrganizationData = LoadJsonFromFile.LoadEntriesOrganization(BaseDirectory + "Organizations_API_v1.json"); Console.WriteLine("Processing Organizations_API_v1.json");
                if (OrganizationData?.RecEntries != null)
                {
                    foreach (var ThisData in OrganizationData.RecEntries)
                    {
                        db.OrganizationEntries.Add(ThisData);
                    }
                }
                OrgEntitiesEntries? OrgEntitiesData = LoadJsonFromFile.LoadEntriesOrgEntities(BaseDirectory + "OrgEntities_API_v1.json"); Console.WriteLine("Processing OrgEntities_API_v1.json");
                if (OrgEntitiesData?.RecEntries != null)
                {
                    foreach (var ThisData in OrgEntitiesData.RecEntries)
                    {
                        db.OrgEntitiesEntries.Add(ThisData);
                    }
                }
                PermitEntranceAttributesEntries? PermitEntranceAttributesData = LoadJsonFromFile.LoadEntriesPermitEntranceAttributes(BaseDirectory + "PermitEntranceAttributes_API_v1.json"); Console.WriteLine("Processing PermitEntranceAttributes_API_v1.json");
                if (PermitEntranceAttributesData?.RecEntries != null)
                {
                    foreach (var ThisData in PermitEntranceAttributesData.RecEntries)
                    {
                        db.PermitEntranceAttributesEntries.Add(ThisData);
                    }
                }
                PermitEntrancesEntries? PermitEntrancesData = LoadJsonFromFile.LoadEntriesPermitEntrances(BaseDirectory + "PermitEntrances_API_v1.json"); Console.WriteLine("Processing PermitEntrances_API_v1.json");
                if (PermitEntrancesData?.RecEntries != null)
                {
                    foreach (var ThisData in PermitEntrancesData.RecEntries)
                    {
                        db.PermitEntrancesEntries.Add(ThisData);
                    }
                }
                PermitEntranceZonesEntries? PermitEntranceZonesData = LoadJsonFromFile.LoadEntriesPermitEntranceZones(BaseDirectory + "PermitEntranceZones_API_v1.json"); Console.WriteLine("Processing PermitEntranceZones_API_v1.json");
                if (PermitEntranceZonesData?.RecEntries != null)
                {
                    foreach (var ThisData in PermitEntranceZonesData.RecEntries)
                    {
                        db.PermitEntranceZonesEntries.Add(ThisData);
                    }
                }
                PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "PermittedEquipment_API_v1.json"); Console.WriteLine("Processing PermittedEquipment_API_v1.json");
                if (PermittedEquipmentData?.RecEntries != null)
                {
                    foreach (var ThisData in PermittedEquipmentData.RecEntries)
                    {
                        db.PermittedEquipmentEntries.Add(ThisData);
                    }
                }
                RecAreaAddressesEntries? RecAreaAddressesData = LoadJsonFromFile.LoadEntriesRecAreaAddresses(BaseDirectory + "RecAreaAddresses_API_v1.json"); Console.WriteLine("Processing RecAreaAddresses_API_v1.json");
                if (RecAreaAddressesData?.RecEntries != null)
                {
                    foreach (var ThisData in RecAreaAddressesData.RecEntries)
                    {
                        db.RecAreaAddressesEntries.Add(ThisData);
                    }
                }
                RecAreaFacilitiesEntries? RecAreaFacilitiesData = LoadJsonFromFile.LoadEntriesRecAreaFacilities(BaseDirectory + "RecAreaFacilities_API_v1.json"); Console.WriteLine("Processing RecAreaFacilities_API_v1.json");
                if (RecAreaFacilitiesData?.RecEntries != null)
                {
                    foreach (var ThisData in RecAreaFacilitiesData.RecEntries)
                    {
                        db.RecAreaFacilitiesEntries.Add(ThisData);
                    }
                }
                RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "RecAreas_API_v1.json"); Console.WriteLine("Processing RecAreas_API_v1.json");
                if (RecAreaData?.RecEntries != null)
                {
                    foreach (var ThisData in RecAreaData.RecEntries)
                    {
                        db.RecAreaEntries.Add(ThisData);
                    }
                }
                TourAttributesEntries? TourAttributesData = LoadJsonFromFile.LoadEntriesTourAttributes(BaseDirectory + "TourAttributes_API_v1.json"); Console.WriteLine("Processing TourAttributes_API_v1.json");
                if (TourAttributesData?.RecEntries != null)
                {
                    foreach (var ThisData in TourAttributesData.RecEntries)
                    {
                        db.TourAttributesEntries.Add(ThisData);
                    }
                }
                ToursEntries? ToursData = LoadJsonFromFile.LoadEntriesTours(BaseDirectory + "Tours_API_v1.json"); Console.WriteLine("Processing Tours_API_v1.json");
                if (ToursData?.RecEntries != null)
                {
                    foreach (var ThisData in ToursData.RecEntries)
                    {
                        db.ToursEntries.Add(ThisData);
                    }
                }
                Console.WriteLine("Saving changes to DB...Please wait");
                db.SaveChanges();
                if (Directory.Exists(BaseDirectory))
                {
                    Directory.Delete(BaseDirectory, true);
                }
            }
        }
    }
}
