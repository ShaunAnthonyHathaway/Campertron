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
        static public void GetWebApiData(
            String Url,
            ref AvailabilityEntries source,
            ref ConcurrentDictionary<string, AvailabilityEntries> SiteData,
            ref bool HeavyTraffic,
            ref bool ReceivedData)
        {
            bool CompletedTransaction = false;
            while (!CompletedTransaction)
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        using (var response = httpClient.GetAsync(Url))
                        {
                            var apiResponse = response.Result.Content.ReadAsStream();
                            using (StreamReader r = new StreamReader(apiResponse))
                            {
                                Console.WriteLine("Get: " + Url);
                                string json = r.ReadToEnd();
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
                    CompletedTransaction = true;
                }
                catch
                {
                    Thread.Sleep(30000);
                }
            }
        }
        public static List<ConsoleConfig.ConsoleConfigValue> GetAvailabilitiesByCampground(CampertronConfig CampgroundConfig,
            ref ConcurrentDictionary<string,
                AvailabilityEntries> SiteData,
            ref ConcurrentDictionary<string, bool> Urls,
            CampertronInternalConfig config,
            ref ConcurrentBag<CampsiteHistory> CampsiteHistory,
            ref ConcurrentBag<AvailableData> FilteredAvailableData)
        {
            //Holds running content we are writing to the console
            List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder = new List<ConsoleConfig.ConsoleConfigValue>();
            //data holder to filter consecutive dates
            if (CampgroundConfig.CampgroundID != null)
            {
                //we cache campsite data in local json files to make faster
                List<CampsitesRecdata> Sites = Cache.CheckCache(CampgroundConfig, ref ConsoleResultHolder, config);
                int totalcounter = 0;
                //api is by month so get total months of data we need to receive
                while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                {
                    //Dates that match criteria
                    List<DateTime> HitDates = new List<DateTime>();
                    int HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = System.Convert.ToDateTime($"{Pdt.Month}/1/{Pdt.Year} 0:00:00 AM");
                    //verify we have any dates in this month, else skip
                    if (HasDatesDuringThisMonth(CampgroundConfig, Pdt.Month, Pdt.Year))
                    {
                        ProcessMonth(CampgroundConfig, config, Pdt, CheckDt, HitCounter, ref ConsoleResultHolder, ref Urls, ref SiteData, ref Sites, ref FilteredAvailableData, ref CampsiteHistory, ref HitDates);
                    }
                    totalcounter++;
                }
            }
            //filter consecutive days
            if (CampgroundConfig.ConsecutiveDays > 1)
            {
                FilterConsecutiveDays(CampgroundConfig, config, ref FilteredAvailableData, ref ConsoleResultHolder, ref CampsiteHistory);
            }
            return ConsoleResultHolder;
        }
        private static void ProcessMonth(
            CampertronConfig CampgroundConfig,
            CampertronInternalConfig config,
            DateTime Pdt,
            DateTime CheckDt,
            int HitCounter,
            ref List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder,
            ref ConcurrentDictionary<string, bool> Urls,
            ref ConcurrentDictionary<string, AvailabilityEntries> SiteData,
            ref List<CampsitesRecdata> Sites,
            ref ConcurrentBag<AvailableData> FilteredAvailableData,
            ref ConcurrentBag<CampsiteHistory> CampsiteHistory,
            ref List<DateTime> HitDates)
        {
            if (CampgroundConfig.ConsecutiveDays == 1)
            {
                ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎", ConsoleColor.Yellow));
                ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
            }
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
                                                          PermittedEquipmentList = Cache.GetPermittedEquipmentByCampsite(CampsitesTable.campsite_id, config.CachePath),
                                                          CampsiteAttributeLists = Cache.GetCampsiteAttributeList(CampsitesTable.campsite_id, config.CachePath),
                                                          Maxppl = CampsitesTable.max_num_people,
                                                          Minppl = CampsitesTable.min_num_people
                                                      };
                    //if filter out set in config remove entries that match
                    if (CampgroundConfig.ExcludeCampsiteType != null && CampgroundConfig.ExcludeCampsiteType.Count > 0)
                    {
                        CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.ExcludeCampsiteType.All(y => x.CampsiteType.Contains(y) == false));
                    }
                    //if filter in set in config remove entries that match
                    if (CampgroundConfig.IncludeCampsiteType != null && CampgroundConfig.IncludeCampsiteType.Count > 0)
                    {
                        CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => CampgroundConfig.IncludeCampsiteType.All(y => x.CampsiteType.Contains(y) == true));
                    }
                    //if matches
                    if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                    {
                        foreach (AvailabilityData ThisEntry in CampsiteAvailabilityEntries)
                        {
                            FilterData(CampgroundConfig, config, ThisEntry, ref FilteredAvailableData, ref ConsoleResultHolder, ref CampsiteHistory, ref HitCounter, ref HitDates);
                        }
                    }
                    counter++;
                }
            }
            if (CampgroundConfig.ConsecutiveDays == 1)
            {
                if (HitCounter == 0)
                {
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tNo entries found on {Pdt.Month}/{Pdt.Year}", ConsoleColor.DarkRed));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                }
                else
                {
                    if (config.GeneralConfig.OutputTo == OutputType.Console)
                    {
                        function.Base.Calendar.GenerateCalendar(Pdt.Month, Pdt.Year, HitDates, ref ConsoleResultHolder);
                    }
                }
            }
        }
        private static void FilterData(CampertronConfig CampgroundConfig,
            CampertronInternalConfig config,
            AvailabilityData ThisEntry,
            ref ConcurrentBag<AvailableData> FilteredAvailableData,
            ref List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder,
            ref ConcurrentBag<CampsiteHistory> CampsiteHistory,
            ref int HitCounter,
            ref List<DateTime> HitDates)
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
                //if filterout out by equipment
                (CampgroundConfig.ExcludeEquipment == null ||
                CampgroundConfig.ExcludeEquipment?.Count == 0 ||
                CampgroundConfig.ExcludeEquipment.Any(s1 => ThisEntry.PermittedEquipmentList.Any(s1.Contains) == false)) &&
                //if filtering by campsite matches
                (CampgroundConfig.IncludeSites == null ||
                CampgroundConfig.IncludeSites.Count == 0 ||
                CampgroundConfig.IncludeSites.Contains(ThisEntry.CampsiteName)) &&
                //if filtering out values by campsite matches
                (CampgroundConfig.ExcludeSites == null ||
                CampgroundConfig.ExcludeSites.Count == 0 ||
                CampgroundConfig.ExcludeSites.Contains(ThisEntry.CampsiteName) == false) &&
                //if filtering by attributes
                (CampgroundConfig.IncludeAttributes == null ||
                CampgroundConfig.IncludeAttributes?.Count == 0 ||
                CampgroundConfig.IncludeAttributes.Any(s1 => ThisEntry.CampsiteAttributeLists.AttValuePairStr.Any(s1.Contains))) &&
                //if filterout out by attributes
                (CampgroundConfig.ExcludeAttributes == null ||
                CampgroundConfig.ExcludeAttributes?.Count == 0 ||
                CampgroundConfig.ExcludeAttributes.Any(s1 => ThisEntry.CampsiteAttributeLists.AttValuePairStr.Any(s1.Contains) == false)) &&
                //prevents duplicate notifications
                (CampsiteHistory == null ||
                CampsiteHistory.Count == 0 ||
                config.GeneralConfig.OutputTo != OutputType.Email ||
                (config.GeneralConfig.OutputTo == OutputType.Email && (from p in CampsiteHistory where p.HitDate == ThisEntry.CampsiteAvailableDate && p.CampsiteID == ThisEntry.CampsiteID select p).FirstOrDefault() == null)))
            {
                //store the entry
                AvailableData ThisAvailableData = new AvailableData();
                ThisAvailableData.CampsiteID = ThisEntry.CampsiteID;
                ThisAvailableData.HitDate = ThisEntry.CampsiteAvailableDate;
                ThisAvailableData.AvailablityObj = ThisEntry;
                ThisAvailableData.ConsoleData = new List<ConsoleConfig.ConsoleConfigValue>();
                //get property values to show
                string? Checkin = ThisEntry.CampsiteAttributeLists.AttValuePair.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                string? Checkout = ThisEntry.CampsiteAttributeLists.AttValuePair.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                if (CampgroundConfig.ConsecutiveDays != 1)
                {
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tDate:\t   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (", true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}", ConsoleColor.Cyan, true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($") 📆 Checkin:{Checkin} CheckOut:{Checkout}", true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})"));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}"));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem("\tURL:\t   ", true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}", ConsoleColor.Blue, true));
                    ThisAvailableData.ConsoleData.Add(CampsiteConfig.AddConsoleConfigItem(true));
                }
                else
                {
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tDate:\t   {ThisEntry.CampsiteAvailableDate.ToShortDateString()} (", true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"{ThisEntry.CampsiteAvailableDate.DayOfWeek}", ConsoleColor.Cyan, true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($") 📆 Checkin:{Checkin} CheckOut:{Checkout}", true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})"));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}"));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\tURL:\t   ", true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}", ConsoleColor.Blue, true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                    //get unique hitdates for calendar generation
                    if (HitDates.Contains(ThisEntry.CampsiteAvailableDate) == false)
                    {
                        HitDates.Add(ThisEntry.CampsiteAvailableDate);
                    }
                    //duplicate campsite history checking
                    var query1 = (from p in CampsiteHistory
                                  where p.CampsiteID == ThisEntry.CampsiteID && p.HitDate == ThisEntry.CampsiteAvailableDate
                                  select p).FirstOrDefault();
                    if (query1 == null)
                    {
                        CampsiteHistory.Add(new CampsiteHistory() { CampsiteID = ThisEntry.CampsiteID, HitDate = ThisEntry.CampsiteAvailableDate });
                    }
                    HitCounter++;
                }
                //get unique hitdates for calendar generation
                if (HitDates.Contains(ThisEntry.CampsiteAvailableDate) == false)
                {
                    HitDates.Add(ThisEntry.CampsiteAvailableDate);
                }
                //duplicate campsite history checking
                var query2 = (from p in CampsiteHistory
                              where p.CampsiteID == ThisEntry.CampsiteID && p.HitDate == ThisEntry.CampsiteAvailableDate
                              select p).FirstOrDefault();
                if (query2 == null)
                {
                    CampsiteHistory.Add(new CampsiteHistory() { CampsiteID = ThisEntry.CampsiteID, HitDate = ThisEntry.CampsiteAvailableDate });
                }
                HitCounter++;
                FilteredAvailableData.Add(ThisAvailableData);
            }
        }
        private static void FilterConsecutiveDays(
            CampertronConfig CampgroundConfig,
            CampertronInternalConfig config,
            ref ConcurrentBag<AvailableData> FilteredAvailableData,
            ref List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder,
            ref ConcurrentBag<CampsiteHistory> CampsiteHistory)
        {
            //hold results for this campsite
            List<AvailableData> Filtered = new List<AvailableData>();
            //initial filter
            var EntriesWithEnoughValues = FilteredAvailableData.GroupBy(p => p.CampsiteID).Where(p => p.Count() >= CampgroundConfig.ConsecutiveDays);
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
                var CampsitesFiltered = FilteredAvailableData.Where(p => p.CampsiteID == ThisCampsiteData.Key);
                foreach (var ThisDate in UniqueDates)
                {
                    Filtered.AddRange(CampsitesFiltered.Where(p => p.HitDate == ThisDate));
                }
            }
            FilteredAvailableData = new ConcurrentBag<AvailableData>(Filtered);
            var GroupedAvailableData = FilteredAvailableData.GroupBy(p => p.CampsiteID);
            foreach (var ThisGroupedAvailableData in GroupedAvailableData)
            {
                var First = ThisGroupedAvailableData.FirstOrDefault();
                if (First != null)
                {
                    int SkipCounter = 0;
                    foreach (var ThisConsoleItem in First.ConsoleData)
                    {
                        if (SkipCounter >= 4)
                        {
                            ConsoleResultHolder.Add(ThisConsoleItem);
                        }
                        SkipCounter++;
                    }
                    List<DateTime> HitDates = new List<DateTime>();
                    foreach (var ThisSubGroupedAvailableData in ThisGroupedAvailableData)
                    {
                        //get unique hitdates for calendar generation
                        if (HitDates.Contains(ThisSubGroupedAvailableData.HitDate) == false)
                        {
                            HitDates.Add(ThisSubGroupedAvailableData.HitDate);
                        }
                        //duplicate campsite history checking
                        var query = (from p in CampsiteHistory
                                     where p.CampsiteID == ThisSubGroupedAvailableData.CampsiteID && p.HitDate == ThisSubGroupedAvailableData.HitDate
                                     select p).FirstOrDefault();
                        if (query == null)
                        {
                            CampsiteHistory.Add(new CampsiteHistory() { CampsiteID = ThisSubGroupedAvailableData.CampsiteID, HitDate = ThisSubGroupedAvailableData.HitDate });
                        }
                        SkipCounter = 0;
                        foreach (var ThisConsoleItem in ThisSubGroupedAvailableData.ConsoleData)
                        {
                            if (SkipCounter < 4)
                            {
                                ConsoleResultHolder.Add(ThisConsoleItem);
                                SkipCounter++;
                            }
                        }
                    }

                    ConsoleResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));

                    int totalcounter = 0;
                    while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                    {
                        DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                        if (HasDatesDuringThisMonth(HitDates, Pdt.Month, Pdt.Year))
                        {
                            if (config.GeneralConfig.OutputTo == OutputType.Console)
                            {
                                function.Base.Calendar.GenerateCalendar(Pdt.Month, Pdt.Year, HitDates, ref ConsoleResultHolder);
                            }
                        }
                        totalcounter++;
                    }
                }
            }
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
        //used to determine if we need to generate a calendar for this month
        private static bool HasDatesDuringThisMonth(List<DateTime> HitDates, int Month, int Year)
        {
            bool ReturnBool = false;
            foreach (DateTime ThisConfigSearchDate in HitDates)
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
public class CampsiteHistory
{
    public String CampsiteID { get; set; }
    public DateTime HitDate { get; set; }
}