﻿//using (var httpClient = new HttpClient())
//{
//    using (var response = await httpClient.GetAsync("https://localhost:44324/api/Reservation"))
//    {
//        string apiResponse = await response.Content.ReadAsStringAsync();
//    }
//}

class Program
{
    static void Main(string[] args)
    {
        KampLibrary.function.sqlite.Clear.All();
        KampLibrary.function.sqlite.PopulateDbFromFile.Populate(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\");
    }
}