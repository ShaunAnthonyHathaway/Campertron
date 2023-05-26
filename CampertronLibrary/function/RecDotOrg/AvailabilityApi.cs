using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CampertronLibrary.function.RecDotOrg
{
    static public class AvailabilityApi
    {
        public static List<AvailabilityData> GetAvailabilitiesByCampground(CampertronConfig CampgroundConfig)
        {
            List <AvailabilityData> ReturnDates = new List<AvailabilityData>();
            if (CampgroundConfig.CampgroundID != null)
            {
                List<CampsitesRecdata> Sites = CampertronLibrary.function.generic.Cache.CheckCache(CampgroundConfig.CampgroundID);

                int totalcounter = 0;
                while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                {
                    List<DateTime> HitDates = new List<DateTime>();
                    Int32 HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎");
                    Console.WriteLine();
                    Console.ResetColor();

                    String Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundConfig.CampgroundID}/month?start_date={CheckDt.Year.ToString()}-{CheckDt.ToString("MM")}-01T00%3A00%3A00.000Z";
                    using (var httpClient = new HttpClient())
                    {
                        using (var response = httpClient.GetAsync(Url))
                        {
                            var apiResponse = response.Result.Content.ReadAsStream();
                            AvailabilityEntries? source = new AvailabilityEntries();
                            using (StreamReader r = new StreamReader(apiResponse))
                            {
                                string json = CampertronLibrary.function.generic.Data.NormalizeApiJsonData(r.ReadToEnd(), CampgroundConfig.CampgroundID, CheckDt);
                                source = JsonSerializer.Deserialize<AvailabilityEntries>(json);
                                if (json != null && json.ToUpper().Contains("REQUEST BLOCKED"))
                                {
                                    Console.ForegroundColor = ConsoleColor.Red;
                                    Console.WriteLine("Too many requests, sleeping for 5 minutes");
                                    Thread.Sleep(300000);//5 mintues
                                    Console.ResetColor();
                                    break;
                                }
                                else
                                {
                                    if (source?.campsites != null)
                                    {
                                        Int32 DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
                                        foreach (var entry in source.campsites)
                                        {
                                            entry.GenerateDates(CheckDt, DaysInCurrentMonth);
                                        }                                        
                                        int counter = 0;
                                        while (counter < DaysInCurrentMonth)
                                        {

                                            DateTime Checker = CheckDt.AddDays(counter);
                                            var CampsiteAvailabilityEntries = from CampsitesTable in source.campsites
                                                                              join SitesTable in Sites on CampsitesTable.campsite_id equals SitesTable.CampsiteID
                                                                              where CampsitesTable.AvailabilityDates.Contains(Checker)
                                                                              orderby CampsitesTable.campsite_id
                                                                              select new AvailabilityData
                                                                              {
                                                                                  CampsiteID = CampsitesTable.campsite_id,
                                                                                  CampsiteType = SitesTable.CampsiteType,
                                                                                  CampsiteName = SitesTable.CampsiteName,
                                                                                  CampsiteLoop = SitesTable.Loop,
                                                                                  CampsiteAvailableDate = Checker,
                                                                                  PermittedEquipmentList = CampertronLibrary.function.generic.Cache.GetPermittedEquipmentByCampsite(CampsitesTable.campsite_id),
                                                                                  CampsiteAttributes = CampertronLibrary.function.generic.Cache.GetCampSiteAttributesByCampsite(CampsitesTable.campsite_id),
                                                                                  Maxppl = CampsitesTable.max_num_people,
                                                                                  Minppl = CampsitesTable.min_num_people
                                                                              };
                                            if (CampgroundConfig.FilterOutByCampsiteType != null && CampgroundConfig.FilterOutByCampsiteType.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(CampgroundConfig.FilterOutByCampsiteType) == false);
                                            }

                                            if (CampgroundConfig.FilterInByCampsiteType != null && CampgroundConfig.FilterInByCampsiteType.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(CampgroundConfig.FilterInByCampsiteType) == true);
                                            }

                                            if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                                            {
                                                foreach (var ThisEntry in CampsiteAvailabilityEntries)
                                                {
                                                String? Checkin = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                String? Checkout = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                if (CampgroundConfig.TotalHumans >= ThisEntry.Minppl && CampgroundConfig.TotalHumans <= ThisEntry.Maxppl && ThisEntry.CampsiteAvailableDate >= DateTime.Now &&
                                                    CampgroundConfig.GetSearchDates().Contains(ThisEntry.CampsiteAvailableDate) &&
                                                    CampgroundConfig.ShowThisDay(ThisEntry.CampsiteAvailableDate.DayOfWeek.ToString()))
                                                {
                                                    Console.Write($"\tDate:\t   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (");
                                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                                        Console.Write($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}");
                                                        Console.ResetColor();
                                                        Console.Write($") 📆 Checkin:{Checkin} CheckOut:{Checkout}");
                                                        Console.WriteLine();
                                                        Console.WriteLine($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})");
                                                        Console.WriteLine($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}");
                                                        Console.Write("\tURL:\t   ");
                                                        Console.ForegroundColor = ConsoleColor.Blue;
                                                        Console.Write($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}");
                                                        Console.Write("\n\n");
                                                        Console.ResetColor();
                                                        ReturnDates.Add(ThisEntry);
                                                        HitDates.Add(ThisEntry.CampsiteAvailableDate);
                                                        HitCounter++;
                                                    }
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
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine($"\tNo entries found on {Pdt.Month}/{Pdt.Year}");
                        Console.WriteLine();
                        Console.ResetColor();
                    }
                    if (HitDates.Count > 0)
                    {
                        CampertronLibrary.function.generic.Calendar.GenerateCalendar(HitDates);
                    }
                }
            }
            return ReturnDates;
        }
    }
}