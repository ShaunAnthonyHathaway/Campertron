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
            GetAvailabilitiesByPark("232445", ChkDt);
            counter++;
        }
        Console.WriteLine("Done");
        Console.ReadLine();
    }
    async static void GetAvailabilitiesByPark(String ParkID, DateTime CheckDt)
    {
        List<CampsitesRecdata> Sites = KampLibrary.function.sqlite.Read.GetCampsitesByFacility(ParkID);
        String Url = $"https://www.recreation.gov/api/camps/availability/campground/{ParkID}/month?start_date={CheckDt.Year.ToString()}-{CheckDt.ToString("MM")}-01T00%3A00%3A00.000Z";

        using (var httpClient = new HttpClient())
        {
            using (var response = httpClient.GetAsync(Url))
            {
                var apiResponse = await response.Result.Content.ReadAsStreamAsync();
                AvailabilityEntries? source = new AvailabilityEntries();
                using (StreamReader r = new StreamReader(apiResponse))
                {
                    string json = NormalizeApiJsonData(r.ReadToEnd(), ParkID, CheckDt);
                    source = JsonSerializer.Deserialize<AvailabilityEntries>(json);
                    if (source?.campsites != null)
                    {
                        foreach (var entry in source.campsites)
                        {
                            entry.GenerateDates(CheckDt);
                        }

                        Int32 DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
                        int counter = 0;
                        while (counter < DaysInCurrentMonth)
                        {
                            DateTime Checker = CheckDt.AddDays(counter);
                            var entrytest = from p in source.campsites
                                            join p2 in Sites on p.campsite_id equals p2.CampsiteID
                                            where p.AvailabilityDates.Contains(Checker) && p2?.CampsiteType?.ToUpper()?.Contains("GROUP") == false
                                            orderby p.campsite_id
                                            select new
                                            {
                                                p.campsite_id,
                                                p2.CampsiteType,
                                                p2.CampsiteName,
                                                p2.Loop
                                            };
                            if (entrytest != null && entrytest.Count() > 0)
                            {
                                foreach (var ThisEntry in entrytest)
                                {
                                    //if (ThisEntry.campsite_id == "338")
                                    //{
                                    Console.WriteLine("***START***");
                                    Console.WriteLine($"Date:   {Checker}");
                                    Console.WriteLine($"SiteID: { ThisEntry.campsite_id}");
                                    Console.WriteLine($"Site:   {ThisEntry.CampsiteName} Loop:{ThisEntry.Loop}");
                                    Console.WriteLine($"Type:   {ThisEntry.CampsiteType}");
                                    Console.WriteLine("***END***");
                                    Console.WriteLine("");
                                    //}
                                }
                            }
                            counter++;
                        }
                    }
                }
            }
        }

    }
    static String NormalizeApiJsonData(String Incoming, String FacilityID, DateTime CheckDt)
    {
        String ReturnStr = Incoming;

        List<String> CampsiteIds = KampLibrary.function.sqlite.Read.GetCampsiteIdsByFacility(FacilityID);
        foreach (String ThisCampsiteId in CampsiteIds)
        {
            ReturnStr = ReturnStr.Replace("\"" + ThisCampsiteId + "\":{", "{");
        }

        Int32 DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
        int counter = 0;
        while (counter < DaysInCurrentMonth)
        {
            counter++;
            String CounterStr = counter.ToString();
            if (CounterStr.Length == 1)
            {
                CounterStr = "0" + CounterStr;
            }
            String MonthStr = CheckDt.ToString("MM");
            String FormattedDate = $"\"{CheckDt.Year.ToString()}-{MonthStr}-{CounterStr}T00:00:00Z\":";
            ReturnStr = ReturnStr
                .Replace(FormattedDate, "\"availability\":")
                .Replace(",\"availability\"", "},{\"availability\"");
        }

        return ReturnStr
            .Replace("\"campsites\":{", "\"campsites\":[")
            .Replace("}}", "}]")
            .Replace("\"availabilities\":{", "\"availabilities\":[{")
            .Replace("},\"campsite_id\":", "}],\"campsite_id\":")
            .Replace("\"quantities\":{", "\"quantities\":[{")
            .Replace("},\"site\":", "}],\"site\":");
    }
}