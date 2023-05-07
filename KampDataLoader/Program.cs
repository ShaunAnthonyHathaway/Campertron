//using (var httpClient = new HttpClient())
//{
//    using (var response = await httpClient.GetAsync("https://localhost:44324/api/Reservation"))
//    {
//        string apiResponse = await response.Content.ReadAsStringAsync();
//    }
//}

using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KampLibrary.function.sqlite.Clear.All();
        //KampLibrary.function.sqlite.PopulateDbFromFile.Populate(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\");
        int counter = 0;
        while (counter < 3)
        {
            DateTime Pdt = DateTime.Now.AddMonths(counter);//.AddMonths(1));
            DateTime ChkDt = Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");
            KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByPark("232445", ChkDt);
            counter++;
        }
        Console.WriteLine("Done");
        Console.ReadLine();
    }
}