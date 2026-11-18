using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class PopulateDbFromRIDBFiles
    {
        //the RIDB files the import reads, in the order it reads them
        internal const int FileCount = 6;

        static public void Populate(string BaseDirectory, string ConfigPath)
        {
            Populate(BaseDirectory, ConfigPath, Console.WriteLine, CancellationToken.None, null, null);
        }

        //the same, with the progress lines going to Log, the token checked before each file and before the save (V011-1:
        //nothing has been inserted then), FileRead told after each file (the files read, FileCount) and Saving before the
        //insert, which is one transaction and is not interrupted
        static internal void Populate(string BaseDirectory, string ConfigPath, Action<string> Log, CancellationToken Token, Action<int, int>? FileRead, Action? Saving)
        {
            int Read = 0;
            void Processed(string Name)
            {
                Log("Processing " + Name);
                FileRead?.Invoke(++Read, FileCount);
            }
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                // only the tables Campertron reads are imported; see RidbDatabase.OmittedTables
                Token.ThrowIfCancellationRequested();
                CampsiteAttributesEntries? CampsiteAttributesData = LoadJsonFromFile.LoadEntriesCampsiteAttributes(BaseDirectory + "/CampsiteAttributes_API_v1.json"); Processed("CampsiteAttributes_API_v1.json");
                Token.ThrowIfCancellationRequested();
                CampsitesEntries? CampsitesData = LoadJsonFromFile.LoadEntriesCampsites(BaseDirectory + "/Campsites_API_v1.json"); Processed("Campsites_API_v1.json");
                Token.ThrowIfCancellationRequested();
                FacilitiesEntries? FacilityData = LoadJsonFromFile.LoadEntriesFacilities(BaseDirectory + "/Facilities_API_v1.json"); Processed("Facilities_API_v1.json");
                Token.ThrowIfCancellationRequested();
                FacilityAddressesEntries? FacilityAddressesData = LoadJsonFromFile.LoadEntriesFacilityAddresses(BaseDirectory + "/FacilityAddresses_API_v1.json"); Processed("FacilityAddresses_API_v1.json");
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
                Token.ThrowIfCancellationRequested();
                PermittedEquipmentEntries? PermittedEquipmentData = LoadJsonFromFile.LoadEntriesPermittedEquipment(BaseDirectory + "/PermittedEquipment_API_v1.json"); Processed("PermittedEquipment_API_v1.json");
                Token.ThrowIfCancellationRequested();
                RecAreaEntries? RecAreaData = LoadJsonFromFile.LoadEntriesRecArea(BaseDirectory + "/RecAreas_API_v1.json"); Processed("RecAreas_API_v1.json");
                Token.ThrowIfCancellationRequested();
                Saving?.Invoke();
                Log("Saving changes to DB...Please wait");
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