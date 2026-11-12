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
                CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "/Campsites_API_v1.json"); Console.WriteLine("Processing Campsites_API_v1.json");
                FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "/Facilities_API_v1.json"); Console.WriteLine("Processing Facilities_API_v1.json");
                FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "/FacilityAddresses_API_v1.json"); Console.WriteLine("Processing FacilityAddresses_API_v1.json");
                List<FacilityAddressesRecdata> FacilityAddressesRows = new List<FacilityAddressesRecdata>();
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
                            FacilityAddressesRows.Add(ThisData);
                        }
                    }
                }
                PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "/PermittedEquipment_API_v1.json"); Console.WriteLine("Processing PermittedEquipment_API_v1.json");
                RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "/RecAreas_API_v1.json"); Console.WriteLine("Processing RecAreas_API_v1.json");
                Console.WriteLine("Saving changes to DB...Please wait");
                //one transaction with prepared commands, in EF Core's SaveChanges row order (RidbTableInserter, decision D25)
                RidbTableInserter.InsertAll(db,
                    CampsiteAttributesData?.RecEntries ?? new List<CampsiteAttributesRecdata>(),
                    CampsitesData?.RecEntries ?? new List<CampsitesRecdata>(),
                    FacilityData?.RecEntries ?? new List<FacilitiesData>(),
                    FacilityAddressesRows,
                    PermittedEquipmentData?.RecEntries ?? new List<PermittedEquipmentRecdata>(),
                    RecAreaData?.RecEntries ?? new List<RecAreaFacilityRecData>());
                if (Directory.Exists(BaseDirectory))
                {
                    Directory.Delete(BaseDirectory, true);
                }
            }
        }
    }
}