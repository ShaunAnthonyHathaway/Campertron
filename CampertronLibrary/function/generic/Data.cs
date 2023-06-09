using Newtonsoft.Json;

namespace CampertronLibrary.function.generic
{
    public static class Data
    {
        public static AvailabilityEntries DynamicDeserialize(String Json)
        {
            AvailabilityEntries ReturnAvailabilityEntries = new AvailabilityEntries();

            dynamic JsonObject = JsonConvert.DeserializeObject<dynamic>(Json);
            foreach (var Root in JsonObject)
            {                
                if (Root.Name != "count")
                {
                    ReturnAvailabilityEntries.campsites = new List<CampsitesDataEntry>();
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
                                        String testing = "";
                                    }
                                    break;
                                case "campsite_id":
                                    ReturnCampsiteDataEntry.campsite_id = CampsiteDataEntryValues.Value;
                                    break;
                                case "campsite_reserve_type":
                                    ReturnCampsiteDataEntry.campsite_reserve_type = CampsiteDataEntryValues.Value;
                                    break;
                                case "campsite_type":
                                    ReturnCampsiteDataEntry.campsite_type = CampsiteDataEntryValues.Value;
                                    break;
                                case "capacity_rating":
                                    ReturnCampsiteDataEntry.capacity_rating = CampsiteDataEntryValues.Value;
                                    break;
                                case "loop":
                                    ReturnCampsiteDataEntry.loop = CampsiteDataEntryValues.Value;
                                    break;
                                case "max_num_people":
                                    ReturnCampsiteDataEntry.max_num_people = CampsiteDataEntryValues.Value;
                                    break;
                                case "min_num_people":
                                    ReturnCampsiteDataEntry.min_num_people = CampsiteDataEntryValues.Value;
                                    break;
                                case "site":
                                    ReturnCampsiteDataEntry.site = CampsiteDataEntryValues.Value;
                                    break;
                                case "supplemental_camping":
                                    ReturnCampsiteDataEntry.supplemental_camping = CampsiteDataEntryValues.Value;
                                    break;
                                case "type_of_use":
                                    ReturnCampsiteDataEntry.type_of_use = CampsiteDataEntryValues.Value;
                                    break;
                            }
                        }
                        ReturnAvailabilityEntries.campsites.Add(ReturnCampsiteDataEntry);
                    }
                }
            }

            return ReturnAvailabilityEntries;
        }
    }
}