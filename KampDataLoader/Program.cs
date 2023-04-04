//using (var httpClient = new HttpClient())
//{
//    using (var response = await httpClient.GetAsync("https://localhost:44324/api/Reservation"))
//    {
//        string apiResponse = await response.Content.ReadAsStringAsync();
//    }
//}

using KampWebApi.function;

class Program
{
    static void Main(string[] args)
    {
        RecAreaAddressesEntries? RecAreaAddressesData = LoadJsonFromFile.LoadRecAreaAddressesEntries(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\RecAreaAddresses_API_v1.json");
        Console.WriteLine("123");
        Console.ReadLine();
    }
}