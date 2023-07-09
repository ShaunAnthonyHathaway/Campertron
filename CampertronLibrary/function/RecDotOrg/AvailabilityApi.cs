using CampertronLibrary.function.generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;

namespace CampertronLibrary.function.RecDotOrg
{
    static public class AvailabilityApi
    {
        public static List<ConsoleConfig.ConsoleConfigValue> GetAvailabilitiesByCampground(CampertronConfig CampgroundConfig, ref ConcurrentDictionary<String, AvailabilityEntries> SiteData, ref ConcurrentDictionary<String, bool> Urls)
        {
            List<ConsoleConfig.ConsoleConfigValue> ResultHolder = new List<ConsoleConfig.ConsoleConfigValue>();

            if (CampgroundConfig.CampgroundID != null)
            {
                List<CampsitesRecdata> Sites = CampertronLibrary.function.generic.Cache.CheckCache(CampgroundConfig, ref ResultHolder);
                int totalcounter = 0;
                while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                {
                    List<DateTime> HitDates = new List<DateTime>();
                    Int32 HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = System.Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");

                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎", ConsoleColor.Yellow));
                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));

                    String Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundConfig.CampgroundID}/month?start_date={CheckDt.Year.ToString()}-{CheckDt.ToString("MM")}-01T00%3A00%3A00.000Z";
                    
                    AvailabilityEntries source = new AvailabilityEntries();
                    if (Urls.TryAdd(Url, false))
                    {                        
                        using (var httpClient = new HttpClient())
                        {
                            using (var response = httpClient.GetAsync(Url))
                            {
                                var apiResponse = response.Result.Content.ReadAsStream();
                                using (StreamReader r = new StreamReader(apiResponse))
                                {
                                    string json = r.ReadToEnd();
                                    if (json != null && json.ToUpper().Contains("REQUEST BLOCKED"))
                                    {
                                        Console.WriteLine("Blocked by API for exceeding request limit, try again later");
                                        Thread.Sleep(300000);//5 mintues
                                        Environment.Exit(0);
                                    }
                                    else
                                    {
                                        source = CampertronLibrary.function.generic.Data.DynamicDeserialize(json);
                                        SiteData.TryAdd(Url, source);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        bool DataReady = false;
                        while (!DataReady)
                        {
                            source = SiteData.Where(p => p.Key == Url).FirstOrDefault().Value;
                            if (source != null)
                            {
                                DataReady= true;
                            }
                            else
                            {
                                Thread.Sleep(500);
                            }
                        }
                    }

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
                            if (CampgroundConfig.FilterOutByCampsiteType != null && CampgroundConfig.FilterOutByCampsiteType.Count > 0)
                            {
                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.FilterOutByCampsiteType.All(y => x.CampsiteType.Contains(y) == false));
                            }

                            if (CampgroundConfig.FilterInByCampsiteType != null && CampgroundConfig.FilterInByCampsiteType.Count > 0)
                            {
                                CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.FilterInByCampsiteType.All(y => x.CampsiteType.Contains(y) == true));
                            }

                            if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                            {
                                foreach (var ThisEntry in CampsiteAvailabilityEntries)
                                {
                                    if (CampgroundConfig.TotalHumans >= ThisEntry.Minppl &&
                                        CampgroundConfig.TotalHumans <= ThisEntry.Maxppl &&
                                        ThisEntry.CampsiteAvailableDate >= DateTime.Now &&
                                        CampgroundConfig.GetSearchDates().Contains(ThisEntry.CampsiteAvailableDate) &&
                                        CampgroundConfig.ShowThisDay(ThisEntry.CampsiteAvailableDate.DayOfWeek.ToString()) &&
                                        (CampgroundConfig.IncludeEquipment == null ||
                                        CampgroundConfig.IncludeEquipment?.Count == 0 ||
                                        CampgroundConfig.IncludeEquipment.Any(s1 => ThisEntry.PermittedEquipmentList.Any(s1.Contains))) &&
                                        (CampgroundConfig.IncludeSites == null ||
                                        CampgroundConfig.IncludeSites.Count == 0 ||
                                        CampgroundConfig.IncludeSites.Contains(ThisEntry.CampsiteName)) &&
                                        (CampgroundConfig.ExcludeSites == null ||
                                        CampgroundConfig.ExcludeSites.Count == 0 ||
                                        CampgroundConfig.ExcludeSites.Contains(ThisEntry.CampsiteName) == false))
                                    {
                                        String? Checkin = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                        String? Checkout = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tDate:\t   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (", true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}", ConsoleColor.Cyan, true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($") 📆 Checkin:{Checkin} CheckOut:{Checkout}", true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})"));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}"));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\tURL:\t   ", true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}", ConsoleColor.Blue, true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                        HitDates.Add(ThisEntry.CampsiteAvailableDate);
                                        HitCounter++;
                                    }
                                }
                            }
                            counter++;
                        }
                    }


                    totalcounter++;
                    if (HitCounter == 0)
                    {
                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tNo entries found on {Pdt.Month}/{Pdt.Year}", ConsoleColor.DarkRed));
                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    }
                    if (HitDates.Count > 0)
                    {
                        CampertronLibrary.function.generic.Calendar.GenerateCalendar(HitDates, ref ResultHolder);
                    }
                }
            }
            return ResultHolder;
        }
    }
}