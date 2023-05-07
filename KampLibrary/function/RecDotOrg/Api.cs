﻿using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KampLibrary.function.RecDotOrg
{
    static public class Api
    {
        public async static void GetAvailabilitiesByPark(String ParkID, DateTime CheckDt)
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
                        string json = KampLibrary.function.generic.Data.NormalizeApiJsonData(r.ReadToEnd(), ParkID, CheckDt);
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
                                        Console.WriteLine($"SiteID: {ThisEntry.campsite_id}");
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
    }
}
