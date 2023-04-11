//using (var httpClient = new HttpClient())
//{
//    using (var response = await httpClient.GetAsync("https://localhost:44324/api/Reservation"))
//    {
//        string apiResponse = await response.Content.ReadAsStringAsync();
//    }
//}

using KampLibrary.function.sqlite;

class Program
{
    static void Main(string[] args)
    {
        //ClearDb.Clear();
        PopulateDbFromFile.Populate(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\");
        //KampLibrary.function.ReadDb.Read();
        Console.ReadLine();
    }
}