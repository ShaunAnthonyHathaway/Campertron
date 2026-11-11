using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class PopulateDbFromRIDBFiles
    {
        static public void Populate(string BaseDirectory, string ConfigPath)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                // only the tables Campertron reads are imported; see RidbDatabase.OmittedTables
                CampsiteAttributesEntries? CampsiteAttributesData = LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "/CampsiteAttributes_API_v1.json"); Console.WriteLine("Processing CampsiteAttributes_API_v1.json");
                if (CampsiteAttributesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsiteAttributesData.RecEntries)
                    {
                        db.CampsiteAttributesEntries.Add(ThisData);
                    }
                }
                CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "/Campsites_API_v1.json"); Console.WriteLine("Processing Campsites_API_v1.json");
                if (CampsitesData?.RecEntries != null)
                {
                    foreach (var ThisData in CampsitesData.RecEntries)
                    {
                        db.CampsitesEntries.Add(ThisData);
                    }
                }
                FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "/Facilities_API_v1.json"); Console.WriteLine("Processing Facilities_API_v1.json");
                if (FacilityData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityData.RecEntries)
                    {

                        db.FacilitiesEntries.Add(ThisData);
                    }
                }
                FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "/FacilityAddresses_API_v1.json"); Console.WriteLine("Processing FacilityAddresses_API_v1.json");
                if (FacilityAddressesData?.RecEntries != null)
                {
                    foreach (var ThisData in FacilityAddressesData.RecEntries)
                    {
                        if (ThisData != null)
                        {
                            //the state is in AddressStateCode; PostalCode is the ZIP/postal code and is stored as received
                            string? FixedStateCode = ThisData.AddressStateCode?.Trim()?.ToUpper();
                            if (FixedStateCode != null)
                            {
                                ThisData.AddressStateCode = CampertronLibrary.function.Base.Convert.StateNameToAbbreviation(FixedStateCode);
                            }
                            ThisData.City = ThisData.City?.Trim().Trim(',').ToUpper();
                            db.FacilityAddressesEntries.Add(ThisData);
                        }
                    }
                }
                PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "/PermittedEquipment_API_v1.json"); Console.WriteLine("Processing PermittedEquipment_API_v1.json");
                if (PermittedEquipmentData?.RecEntries != null)
                {
                    foreach (var ThisData in PermittedEquipmentData.RecEntries)
                    {
                        db.PermittedEquipmentEntries.Add(ThisData);
                    }
                }
                RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "/RecAreas_API_v1.json"); Console.WriteLine("Processing RecAreas_API_v1.json");
                if (RecAreaData?.RecEntries != null)
                {
                    foreach (var ThisData in RecAreaData.RecEntries)
                    {
                        db.RecAreaEntries.Add(ThisData);
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