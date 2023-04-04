using System.Text.Json;

namespace KampLibrary
{
    static public class LoadJsonFromFile
    {
        static public RecAreaAddressesEntries? LoadRecAreaAddressesEntries(String JsonFileLocation)
        {
            RecAreaAddressesEntries? source = new RecAreaAddressesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaAddressesEntries>(json);
            }

            return source;
        }
        static public FacilitiesEntries? LoadFacilitiesEntries(String JsonFileLocation)
        {
            FacilitiesEntries? source = new FacilitiesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<FacilitiesEntries>(json);
            }

            return source;
        }
    }
}
