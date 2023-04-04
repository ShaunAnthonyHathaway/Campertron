using System.Text.Json;

namespace KampWebApi.function
{
    static class LoadJsonFromFile
    {
        static internal RecAreaAddressesEntries? LoadRecAreaAddressesEntries(String JsonFileLocation)
        {
            RecAreaAddressesEntries? source = new RecAreaAddressesEntries();

            using (StreamReader r = new StreamReader(JsonFileLocation))
            {
                string json = r.ReadToEnd();
                source = JsonSerializer.Deserialize<RecAreaAddressesEntries>(json);
            }

            return source;
        }
    }
}
