using Newtonsoft.Json;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Data
    {
        public static AvailabilityEntries DynamicDeserialize(string Json, ref bool HeavyTraffic, ref bool ReceivedData)
        {
            AvailabilityEntries ReturnAvailabilityEntries = new AvailabilityEntries();

            dynamic JsonObject = JsonConvert.DeserializeObject<dynamic>(Json);
            foreach (var Root in JsonObject)
            {
                if (Root.Name != "count")
                {
                    ReturnAvailabilityEntries.campsites = new List<CampsitesDataEntry>();
                    if (Root.Value.ToString().Contains("We are currently experiencing heavy traffic on our site") == true)
                    {
                        Console.WriteLine("Heavy traffic...retrying in 1 second");
                        HeavyTraffic = true;
                    }
                    else
                    {
                        if (HeavyTraffic == true)
                        {
                            HeavyTraffic = false;
                        }
                        ReceivedData = true;
                        foreach (var CampsiteDataEntries in Root.Value)
                        {
                            CampsitesDataEntry ReturnCampsiteDataEntry = new CampsitesDataEntry();
                            ReturnCampsiteDataEntry.availabilities = new List<availabilities>();
                            ReturnCampsiteDataEntry.quantities = new List<object>();
                            foreach (var CampsiteDataEntryValues in CampsiteDataEntries.Value)
                            {
                                switch (CampsiteDataEntryValues.Name)
                                {
                                    case "availabilities":
                                        foreach (var CampsiteDataPropertyValues in CampsiteDataEntryValues.Value)
                                        {
                                            ReturnCampsiteDataEntry.availabilities.Add(new availabilities() { availability = CampsiteDataPropertyValues.Value });
                                        }
                                        break;
                                    case "campsite_id":
                                        ReturnCampsiteDataEntry.campsite_id = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "campsite_reserve_type":
                                        ReturnCampsiteDataEntry.campsite_reserve_type = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "campsite_type":
                                        ReturnCampsiteDataEntry.campsite_type = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "capacity_rating":
                                        ReturnCampsiteDataEntry.capacity_rating = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "loop":
                                        ReturnCampsiteDataEntry.loop = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "max_num_people":
                                        int MaxPpl = 0;
                                        Int32.TryParse(CampsiteDataEntryValues.Value.Value.ToString(), out MaxPpl);
                                        ReturnCampsiteDataEntry.max_num_people = MaxPpl;
                                        break;
                                    case "min_num_people":
                                        int MinPpl = 0;
                                        Int32.TryParse(CampsiteDataEntryValues.Value.ToString(), out MinPpl);
                                        ReturnCampsiteDataEntry.min_num_people = MinPpl;
                                        break;
                                    case "site":
                                        ReturnCampsiteDataEntry.site = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "supplemental_camping":
                                        ReturnCampsiteDataEntry.supplemental_camping = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                    case "type_of_use":
                                        ReturnCampsiteDataEntry.type_of_use = CampsiteDataEntryValues.Value.ToString();
                                        break;
                                }
                            }
                            ReturnAvailabilityEntries.campsites.Add(ReturnCampsiteDataEntry);
                        }
                    }
                }
            }
            return ReturnAvailabilityEntries;
        }
    }
}