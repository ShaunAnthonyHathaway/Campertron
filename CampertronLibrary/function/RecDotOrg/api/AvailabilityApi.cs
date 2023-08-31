using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CampertronLibrary.function.RecDotOrg.api
{
    static public class AvailabilityApi
    {
        static public void GetData(String Url, ref AvailabilityEntries source, ref ConcurrentDictionary<string, AvailabilityEntries> SiteData)
        {
            bool HeavyTraffic = false;
            bool ReceivedData = false;
            GetWebApiData(Url, ref source, ref SiteData, ref HeavyTraffic, ref ReceivedData);
        }
        static public void GetWebApiData(String Url, ref AvailabilityEntries source, ref ConcurrentDictionary<string, AvailabilityEntries> SiteData, ref bool HeavyTraffic, ref bool ReceivedData)
        {
            using (var httpClient = new HttpClient())
            {
                using (var response = httpClient.GetAsync(Url))
                {
                    var apiResponse = response.Result.Content.ReadAsStream();
                    using (StreamReader r = new StreamReader(apiResponse))
                    {
                        string json = r.ReadToEnd();
                        Console.WriteLine(Url);
                        //too many requests in short time period generate blocked error
                        if (json != null && json.ToUpper().Contains("REQUEST BLOCKED"))
                        {
                            Console.WriteLine("Blocked by API for exceeding request limit, try again later");
                            Thread.Sleep(300000);//5 mintues
                            Environment.Exit(0);
                        }
                        else
                        {
                            while (ReceivedData == false)
                            {
                                source = Data.DynamicDeserialize(json, ref HeavyTraffic, ref ReceivedData);
                                if (HeavyTraffic == false)
                                {
                                    SiteData.TryAdd(Url, source);
                                }
                                else
                                {
                                    Thread.Sleep(1000);
                                    GetWebApiData(Url, ref source, ref SiteData, ref HeavyTraffic, ref ReceivedData);
                                }
                            }
                        }
                    }
                }
            }
        }
        public static List<ConsoleConfig.ConsoleConfigValue> GetAvailabilitiesByCampground(CampertronConfig CampgroundConfig, ref ConcurrentDictionary<string, AvailabilityEntries> SiteData, ref ConcurrentDictionary<string, bool> Urls)
        {
            //data holder to filter consecutive dates
            List<AvailableData> TotalAvailableData = new List<AvailableData>();

            //Holds running content we are writing to the console
            List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder = new List<ConsoleConfig.ConsoleConfigValue>();

            if (CampgroundConfig.CampgroundID != null)
            {
                //we cache campsite data in local json files to make faster
                List<CampsitesRecdata> Sites = Cache.CheckCache(CampgroundConfig, ref ConsoleResultHolder);
                int totalcounter = 0;
                //api is by month so get total months of data we need to receive
                while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                {
                    //Dates that match criteria
                    ///////////////////////////////////////////List<DateTime> HitDates = new List<DateTime>();
                    ///////////////////////////////////////////int HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = System.Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");
                    //verify we have any dates in this month, else skip
                    if (HasDatesDuringThisMonth(CampgroundConfig, Pdt.Month, Pdt.Year))
                    {
                        //ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                        //ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎", ConsoleColor.Yellow));
                        //ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                        //we only retrieve unique urls per search session, overlaping config files use the same data that's only retrieved once
                        string Url = $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundConfig.CampgroundID}/month?start_date={CheckDt.Year.ToString()}-{CheckDt.ToString("MM")}-01T00%3A00%3A00.000Z";
                        //first thread to create entry receives data, remaining threads wait for data
                        AvailabilityEntries source = new AvailabilityEntries();
                        if (Urls.TryAdd(Url, false))
                        {
                            GetData(Url, ref source, ref SiteData);
                        }
                        else
                        {
                            //loop until thread is finished retrieving data
                            bool DataReady = false;
                            while (!DataReady)
                            {
                                source = SiteData.Where(p => p.Key == Url).FirstOrDefault().Value;
                                if (source != null)
                                {
                                    DataReady = true;
                                }
                                else
                                {
                                    Thread.Sleep(500);
                                }
                            }
                        }
                        if (source?.campsites != null)
                        {
                            int DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
                            foreach (var entry in source.campsites)
                            {
                                entry.GenerateDates(CheckDt, DaysInCurrentMonth);
                            }
                            int counter = 0;
                            while (counter < DaysInCurrentMonth)
                            {
                                //find matches
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
                                                                      PermittedEquipmentList = Cache.GetPermittedEquipmentByCampsite(CampsitesTable.campsite_id),
                                                                      CampsiteAttributes = Cache.GetCampSiteAttributesByCampsite(CampsitesTable.campsite_id),
                                                                      Maxppl = CampsitesTable.max_num_people,
                                                                      Minppl = CampsitesTable.min_num_people
                                                                  };
                                //if filter out set in config remove entries that match
                                if (CampgroundConfig.FilterOutByCampsiteType != null && CampgroundConfig.FilterOutByCampsiteType.Count > 0)
                                {
                                    CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.FilterOutByCampsiteType.All(y => x.CampsiteType.Contains(y) == false));
                                }
                                //if filter in set in config remove entries that match
                                if (CampgroundConfig.FilterInByCampsiteType != null && CampgroundConfig.FilterInByCampsiteType.Count > 0)
                                {
                                    CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.FilterInByCampsiteType.All(y => x.CampsiteType.Contains(y) == true));
                                }
                                //consecutive days

                                //if matches
                                if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                                {
                                    foreach (AvailabilityData ThisEntry in CampsiteAvailabilityEntries)
                                    {
                                        //total humans is mandatory
                                        if (CampgroundConfig.TotalHumans >= ThisEntry.Minppl &&
                                            CampgroundConfig.TotalHumans <= ThisEntry.Maxppl &&
                                            ThisEntry.CampsiteAvailableDate >= DateTime.Now &&
                                            CampgroundConfig.GetSearchDates().Contains(ThisEntry.CampsiteAvailableDate) &&
                                            //search days is mandatory
                                            CampgroundConfig.ShowThisDay(ThisEntry.CampsiteAvailableDate.DayOfWeek.ToString()) &&
                                            //if filtering by equipment
                                            (CampgroundConfig.IncludeEquipment == null ||
                                            CampgroundConfig.IncludeEquipment?.Count == 0 ||
                                            CampgroundConfig.IncludeEquipment.Any(s1 => ThisEntry.PermittedEquipmentList.Any(s1.Contains))) &&
                                            //if filtering by campsite matches
                                            (CampgroundConfig.IncludeSites == null ||
                                            CampgroundConfig.IncludeSites.Count == 0 ||
                                            CampgroundConfig.IncludeSites.Contains(ThisEntry.CampsiteName)) &&
                                            //if filtering out values by campsite matches
                                            (CampgroundConfig.ExcludeSites == null ||
                                            CampgroundConfig.ExcludeSites.Count == 0 ||
                                            CampgroundConfig.ExcludeSites.Contains(ThisEntry.CampsiteName) == false))
                                        {
                                            //store the entry
                                            AvailableData ThisAvailableData = new AvailableData();
                                            ThisAvailableData.CampsiteID = ThisEntry.CampsiteID;
                                            ThisAvailableData.HitDate = ThisEntry.CampsiteAvailableDate;
                                            ThisAvailableData.AvailablityObj = ThisEntry;
                                            ThisAvailableData.ConsoleData = new List<ConsoleConfig.ConsoleConfigValue>();
                                            //get property values to show
                                            string? Checkin = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                            string? Checkout = ThisEntry.CampsiteAttributes.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tDate:\t   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (", true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}", ConsoleColor.Cyan, true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($") 📆 Checkin:{Checkin} CheckOut:{Checkout}", true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})"));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}"));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem("\tURL:\t   ", true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}", ConsoleColor.Blue, true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                            ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                                            ////////////////////////////////////////////HitDates.Add(ThisEntry.CampsiteAvailableDate);
                                            ////////////////////////////////////////////HitCounter++;
                                            TotalAvailableData.Add(ThisAvailableData);
                                        }
                                    }
                                }
                                counter++;
                            }
                        }
                        //if (HitCounter == 0)
                        //{
                        //    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tNo entries found on {Pdt.Month}/{Pdt.Year}", ConsoleColor.DarkRed));
                        //    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                        //}
                        //else
                        //{
                        //    function.Base.Calendar.GenerateCalendar(Pdt.Month, Pdt.Year, HitDates, ref ResultHolder);
                        //}
                    }
                    totalcounter++;
                }
            }
            //filter consecutive days
            if (CampgroundConfig.ConsecutiveDays > 1)
            {
                if (CampgroundConfig.ConsecutiveFilter == ConsecutiveFilter.ByCampsite)
                {
                    //hold results for this campsite
                    List<AvailableData> FilteredAvailableData = new List<AvailableData>();
                    //initial filter
                    var EntriesWithEnoughValues = TotalAvailableData.GroupBy(p => p.CampsiteID).Where(p => p.Count() >= CampgroundConfig.ConsecutiveDays);
                    foreach (var ThisCampsiteData in EntriesWithEnoughValues)
                    {
                        List<DateTime> UniqueDates = new List<DateTime>();
                        var ThisCampsiteDates = ThisCampsiteData.Select(p => p.HitDate).Distinct().ToList().GetConsecutiveDays(CampgroundConfig.ConsecutiveDays);
                        foreach (var ThisDateGroup in ThisCampsiteDates)
                        {
                            foreach (var ThisDate in ThisDateGroup)
                            {
                                if (UniqueDates.Contains(ThisDate) == false)
                                {
                                    UniqueDates.Add(ThisDate);
                                }
                            }
                        }
                        var CampsitesFiltered = TotalAvailableData.Where(p => p.CampsiteID == ThisCampsiteData.Key);
                        foreach (var ThisDate in UniqueDates)
                        {
                            FilteredAvailableData.AddRange(CampsitesFiltered.Where(p => p.HitDate == ThisDate));
                        }
                    }
                    TotalAvailableData = FilteredAvailableData;
                    var testing = TotalAvailableData.GroupBy(p => p.CampsiteID);
                    String testestes = "";
                }
                else
                {
                    var DistinctDates = TotalAvailableData.Select(p => p.HitDate).Distinct();
                    String test2 = "";
                }
            }
            return ConsoleResultHolder;
        }
        //used to determine if we need to get data for this month
        private static bool HasDatesDuringThisMonth(CampertronConfig CampgroundConfig, int Month, int Year)
        {
            bool ReturnBool = false;
            foreach (DateTime ThisConfigSearchDate in CampgroundConfig.GetSearchDates())
            {
                if (ThisConfigSearchDate.Year == Year && ThisConfigSearchDate.Month == Month)
                {
                    ReturnBool = true;
                    break;
                }
            }
            return ReturnBool;
        }
    }
}