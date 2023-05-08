using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KampLibrary.function.RecDotOrg
{
    static public class AvailabilityApi
    {
        public static void GetAvailabilitiesByCampground(String? CampgroundID)
        {
            GetAvailabilitiesByCampground(CampgroundID, 0, null, null);
        }
        public static void GetAvailabilitiesByCampground(String? CampgroundID, Int32? MonthsToCheck)
        {
            GetAvailabilitiesByCampground(CampgroundID, MonthsToCheck, null, null);
        }
        public static void GetAvailabilitiesByCampground(String? CampgroundID, Int32? MonthsToCheck, String? FilterOut)
        {
            GetAvailabilitiesByCampground(CampgroundID, MonthsToCheck, FilterOut, null);
        }
        public static List<AvailabilityData> GetAvailabilitiesByCampground(
            String? CampgroundID,
            Int32? MonthsToCheck,
            String? FilterOut,
            String? FilterIn
            )
        {
            List<AvailabilityData> ReturnDates = new List<AvailabilityData>();
            if (CampgroundID != null)
            {                
                var CampInfo = KampLibrary.function.sqlite.Read.GetParkCampgroundInfo(CampgroundID);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"{CampInfo.ParkName}");
                Console.ResetColor();
                Console.Write(" - ");
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.Write($"{CampInfo.CampsiteName}");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine();

                List<CampsitesRecdata> Sites = KampLibrary.function.sqlite.Read.GetCampsitesByPark(CampgroundID);
                int totalcounter = 0;
                while (totalcounter <= MonthsToCheck)
                {
                    Int32 HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Searching for entries on {Pdt.Month}/{Pdt.Year}");
                    Console.WriteLine();
                    Console.ResetColor();

                    String Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundID}/month?start_date={CheckDt.Year.ToString()}-{CheckDt.ToString("MM")}-01T00%3A00%3A00.000Z";
                    using (var httpClient = new HttpClient())
                    {
                        using (var response = httpClient.GetAsync(Url))
                        {
                            var apiResponse = response.Result.Content.ReadAsStream();
                            AvailabilityEntries? source = new AvailabilityEntries();
                            using (StreamReader r = new StreamReader(apiResponse))
                            {
                                string json = KampLibrary.function.generic.Data.NormalizeApiJsonData(r.ReadToEnd(), CampgroundID, CheckDt);
                                source = JsonSerializer.Deserialize<AvailabilityEntries>(json);
                                if (json != null && json.ToUpper().Contains("REQUEST BLOCKED"))
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Error getting data from web api");
                                    Console.ResetColor();
                                    break;
                                }
                                else
                                {
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
                                            var CampsiteAvailabilityEntries = from p in source.campsites
                                                            join p2 in Sites on p.campsite_id equals p2.CampsiteID
                                                            where p.AvailabilityDates.Contains(Checker)
                                                            orderby p.campsite_id
                                                            select new AvailabilityData
                                                            {
                                                                CampsiteID = p.campsite_id,
                                                                CampsiteType = p2.CampsiteType,
                                                                CampsiteName = p2.CampsiteName,
                                                                CampsiteLoop = p2.Loop,
                                                                CampsiteAvailableDate = Checker
                                                            };
                                            if (FilterOut != null && FilterOut.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(FilterOut) == false);
                                            }

                                            if (FilterIn != null && FilterIn.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(FilterIn) == true);
                                            }

                                            if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                                            {
                                                foreach (var ThisEntry in CampsiteAvailabilityEntries)
                                                {
                                                    Console.WriteLine($"Date:   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} ({ThisEntry.CampsiteAvailableDate.DayOfWeek})");
                                                    Console.Write("URL:    ");
                                                    Console.ForegroundColor = ConsoleColor.Blue;
                                                    Console.Write($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}");
                                                    Console.WriteLine();
                                                    Console.ResetColor();
                                                    Console.WriteLine($"Site:   {ThisEntry.CampsiteName}    Loop:{ThisEntry.CampsiteLoop}");
                                                    Console.WriteLine($"Type:   {ThisEntry.CampsiteType}");
                                                    Console.WriteLine("");
                                                    HitCounter++;
                                                    ReturnDates.Add(ThisEntry);
                                                }
                                            }
                                            counter++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    totalcounter++;
                    if (HitCounter == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"No entries found on {Pdt.Month}/{Pdt.Year}");
                        Console.WriteLine();
                        Console.ResetColor();
                    }
                }
            }
            return ReturnDates;
        }
    }
}
