﻿using Microsoft.EntityFrameworkCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KamperLibrary.function.RecDotOrg
{
    static public class AvailabilityApi
    {
        public static List<AvailabilityData> GetAvailabilitiesByCampground(
            String? CampgroundID,
            Int32? MonthsToCheck,
            Int32? TotalHumans,
            String? FilterOutByCampsiteType,
            String? FilterInByCampsiteType
            )
        {
            List<AvailabilityData> ReturnDates = new List<AvailabilityData>();
            if (CampgroundID != null)
            {
                List<CampsitesRecdata> Sites = KamperLibrary.function.generic.Cache.CheckCache(CampgroundID);

                int totalcounter = 0;
                while (totalcounter <= MonthsToCheck)
                {
                    List<DateTime> HitDates = new List<DateTime>();
                    Int32 HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎");
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
                                string json = KamperLibrary.function.generic.Data.NormalizeApiJsonData(r.ReadToEnd(), CampgroundID, CheckDt);
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
                                        foreach (var entry in source.campsites)
                                        {
                                            entry.GenerateDates(CheckDt);
                                        }

                                        Int32 DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
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
                                                                                  PermittedEquipmentList = KamperLibrary.function.generic.Cache.GetPermittedEquipmentByCampsite(CampsitesTable.campsite_id),
                                                                                  CampsiteAttributes = KamperLibrary.function.generic.Cache.GetCampSiteAttributesByCampsite(CampsitesTable.campsite_id),
                                                                                  Maxppl = CampsitesTable.max_num_people,
                                                                                  Minppl = CampsitesTable.min_num_people
                                                                              };
                                            if (FilterOutByCampsiteType != null && FilterOutByCampsiteType.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(FilterOutByCampsiteType) == false);
                                            }

                                            if (FilterInByCampsiteType != null && FilterInByCampsiteType.Length > 0)
                                            {
                                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(p => p.CampsiteType?.ToUpper()?.Contains(FilterInByCampsiteType) == true);
                                            }

                                            if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                                            {
                                                foreach (var ThisEntry in CampsiteAvailabilityEntries)
                                                {
                                                    String? Checkin = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                    String? Checkout = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                    //String? MinPeople = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Min Num of People").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                    //String? MaxPeople = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Max Num of People").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                                    //Int32 MinPeopleInt = Convert.ToInt32(MinPeople);
                                                    //Int32 MaxPeopleInt = Convert.ToInt32(MaxPeople);
                                                    if (TotalHumans >= ThisEntry.Minppl && TotalHumans <= ThisEntry.Maxppl)
                                                    {
                                                        Console.Write($"    Date:      {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (");
                                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                                        Console.Write($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}");
                                                        Console.ResetColor();
                                                        Console.Write($") 📆 Checkin:{Checkin} CheckOut:{Checkout}");
                                                        Console.WriteLine();
                                                        Console.WriteLine($"    Site:      {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})");
                                                        Console.WriteLine($"    Equipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}");
                                                        Console.Write("    URL:       ");
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
                        Console.WriteLine($"    No entries found on {Pdt.Month}/{Pdt.Year}");
                        Console.WriteLine();
                        Console.ResetColor();
                    }
                    if (HitDates.Count > 0)
                    {
                        KamperLibrary.function.generic.Calendar.GenerateCalendar(HitDates);
                    }
                }
            }
            return ReturnDates;
        }
    }
}