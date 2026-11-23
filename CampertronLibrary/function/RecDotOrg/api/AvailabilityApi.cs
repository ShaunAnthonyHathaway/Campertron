using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.monitoring;
using CampertronLibrary.function.RecDotOrg.search;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using static CampertronLibrary.function.RecDotOrg.data.Load;

namespace CampertronLibrary.function.RecDotOrg.api
{
    static public class AvailabilityApi
    {
        static public void GetData(String Url, ref AvailabilityEntries source, ref RunningData RunData)
        {
            bool HeavyTraffic = false;
            bool ReceivedData = false;
            GetWebApiData(Url, ref source, ref RunData, ref HeavyTraffic, ref ReceivedData);
        }
        //after a blocked request Campertron waits BlockedWaitTime (5 minutes) and ends with exit code 1. Replaceable so
        //tests can see the wait and the exit code without waiting or ending the test run (production: Thread.Sleep and
        //Environment.Exit, unchanged)
        internal static readonly TimeSpan BlockedWaitTime = TimeSpan.FromMinutes(5);
        internal static Action<TimeSpan> BlockedWait { get; set; } = Thread.Sleep;
        internal static Action<int> ExitProcess { get; set; } = Environment.Exit;
        //the console's answer to a blocked request, given the request's description: one error, the wait and exit code 1.
        //The console's Search takes it here; the console's monitoring takes it as its service's Blocked (MonitoringConsole)
        internal static void ConsoleBlocked(string Request)
        {
            ConsoleUi.Error("Blocked by API for exceeding request limit, try again later (" + Request + "). Campertron waits 5 minutes and then exits.");
            BlockedWait(BlockedWaitTime);
            //exit code 1, a failure: a supervisor that restarts on failure starts Campertron again (TD-44)
            ExitProcess(1);
        }
        //a warning and a wait of a console search or of a monitoring's check (RunningData.Monitoring, D90): the console's
        //warning and sleep, or the monitoring's event and a wait that its stop ends
        private static void Warn(MonitoringRun? Monitor, string Text)
        {
            if (Monitor != null)
            {
                Monitor.Warning(Text);
            }
            else
            {
                ConsoleUi.Warning(Text);
            }
        }
        private static void Pause(MonitoringRun? Monitor, int Milliseconds)
        {
            if (Monitor != null)
            {
                Monitor.Pause(Milliseconds);
            }
            else
            {
                Thread.Sleep(Milliseconds);
            }
        }
        static public void GetWebApiData(
            String Url,
            ref AvailabilityEntries source,
            ref RunningData RunData,
            ref bool HeavyTraffic,
            ref bool ReceivedData)
        {
            bool CompletedTransaction = false;
            int Attempt = 0;
            //a monitoring's check (D90): its warnings, its waits and a blocked request are its service's; a stop ends the
            //check here instead of another attempt. Null for the console's Search, which is unchanged
            MonitoringRun? Monitor = RunData.Monitoring;
            while (!CompletedTransaction)
            {
                try
                {
                    Monitor?.ThrowIfStopped();
                    Attempt++;
                    string json = ConsoleDownload(Url);
                    //too many requests in short time period generate blocked error
                    if (json != null && json.ToUpper().Contains("REQUEST BLOCKED"))
                    {
                        if (Monitor != null)
                        {
                            Monitor.Blocked(DescribeRequest(Url));
                        }
                        else
                        {
                            ConsoleBlocked(DescribeRequest(Url));
                        }
                    }
                    else
                    {
                        while (ReceivedData == false)
                        {
                            source = Data.DynamicDeserialize(json, ref HeavyTraffic, ref ReceivedData);
                            if (HeavyTraffic == false)
                            {
                                RunData.SiteData.TryAdd(Url, source);
                            }
                            else
                            {
                                Warn(Monitor, "Heavy traffic at recreation.gov (" + DescribeRequest(Url) + "); retrying in 1 second.");
                                Pause(Monitor, 1000);
                                GetWebApiData(Url, ref source, ref RunData, ref HeavyTraffic, ref ReceivedData);
                            }
                        }
                    }
                    CompletedTransaction = true;
                }
                catch (Exception ex) when (Monitor == null || !Monitor.EndsTheCheck(ex))
                {
                    //the retry is unchanged (every 30 seconds, without limit); it is no longer silent
                    Warn(Monitor, "the request to recreation.gov for " + DescribeRequest(Url) + " failed (attempt " + Attempt + ": " + ConsoleUi.Reason(ex) + "); retrying in 30 seconds.");
                    Pause(Monitor, 30000);
                }
            }
        }
        //the console's download of a month's availability: a new HttpClient per request and the body read whatever the
        //status, as always. Replaceable so the tests run the console's search without recreation.gov (TD-01); the
        //interactive search (CampsiteSearch, GS1) has its own per-run transport and never calls it
        internal static Func<string, string> ConsoleDownload { get; set; } = DownloadText;
        private static string DownloadText(string Url)
        {
            using (var httpClient = new HttpClient())
            {
                using (var response = httpClient.GetAsync(Url))
                {
                    var apiResponse = response.Result.Content.ReadAsStream();
                    using (StreamReader r = new StreamReader(apiResponse))
                    {
                        return r.ReadToEnd();
                    }
                }
            }
        }
        //"WATCHMAN CAMPGROUND, November 2026" (or "campground 232445, November 2026") for a monthly availability URL
        public static string DescribeRequest(string Url)
        {
            var Match = System.Text.RegularExpressions.Regex.Match(Url ?? string.Empty, @"/campground/([^/]*)/month\?start_date=(\d{4})-(\d{2})-");
            if (!Match.Success)
            {
                return Url ?? string.Empty;
            }
            string Id = Match.Groups[1].Value;
            string Month = new DateTime(int.Parse(Match.Groups[2].Value, CultureInfo.InvariantCulture), Math.Clamp(int.Parse(Match.Groups[3].Value, CultureInfo.InvariantCulture), 1, 12), 1).ToString("Y", CultureInfo.CurrentCulture);
            return (Cache.KnownCampgroundNames.TryGetValue(Id, out string? Name) && !string.IsNullOrWhiteSpace(Name) ? Name : "campground " + Id) + ", " + Month;
        }
        public static List<ConsoleConfig.ConsoleConfigValue> GetAvailabilitiesByCampground(CampertronConfig CampgroundConfig,
            ref RunningData RunData,
            CampertronInternalConfig config)
        {
            //Holds running content we are writing to the console
            List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder = new List<ConsoleConfig.ConsoleConfigValue>();
            //this search's own hits: FilterData collects them and FilterConsecutiveDays reads and replaces only them, so
            //searches running in parallel can neither take nor lend each other hits (TD-04). Only the final hits join the
            //run's FilteredAvailableData. A ConcurrentBag, as the run-wide set was, keeps a search's output order as it was
            ConcurrentBag<AvailableData> SearchHits = new ConcurrentBag<AvailableData>();
            //an interactive search's per-run host (GS1), null for the console: it only adds cancellation checkpoints, a
            //bounded fetch and the months' bookkeeping, so the search itself and its output stay the console's
            SearchHost? Host = RunData.Host;
            if (CampgroundConfig.CampgroundID != null)
            {
                //we cache campsite data in local json files to make faster
                //a monitoring's check (D90) builds a missing cache as the console does, its status line through its service
                MonitoringRun? Monitor = RunData.Monitoring;
                List<CampsitesRecdata> Sites = Host != null
                    ? Cache.CheckCache(CampgroundConfig, ref ConsoleResultHolder, config, null, Host.Token, false)
                    : Monitor != null
                        ? Cache.CheckCache(CampgroundConfig, ref ConsoleResultHolder, config, Monitor.Status, Monitor.Token, true)
                        : Cache.CheckCache(CampgroundConfig, ref ConsoleResultHolder, config);
                int totalcounter = 0;
                //api is by month so get total months of data we need to receive
                while (totalcounter <= CampgroundConfig.GetMonthsToCheck())
                {
                    Host?.ThrowIfCanceled();
                    Monitor?.ThrowIfStopped();
                    //Dates that match criteria
                    List<DateTime> HitDates = new List<DateTime>();
                    int HitCounter = 0;
                    DateTime Pdt = DateTime.Now.AddMonths(totalcounter);
                    DateTime CheckDt = MonthStart(Pdt);
                    //verify we have any dates in this month, else skip
                    if (HasDatesDuringThisMonth(CampgroundConfig, Pdt.Month, Pdt.Year))
                    {
                        ProcessMonth(CampgroundConfig, config, Pdt, CheckDt, HitCounter, ref ConsoleResultHolder, ref RunData, ref Sites, ref HitDates, SearchHits);
                    }
                    totalcounter++;
                }
            }
            //filter consecutive days
            if (CampgroundConfig.ConsecutiveDays > 1)
            {
                SearchHits = FilterConsecutiveDays(CampgroundConfig, config, SearchHits, ref ConsoleResultHolder, Host);
            }
            foreach (AvailableData Hit in SearchHits)
            {
                RunData.FilteredAvailableData.Add(Hit);
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
            ref RunningData RunData,
            ref List<CampsitesRecdata> Sites,
            ref List<DateTime> HitDates,
            ConcurrentBag<AvailableData> SearchHits)
        {
            SearchHost? Host = RunData.Host;
            //we only retrieve unique urls per search session, overlaping config files use the same data that's only retrieved once
            string Url = MonthUrl(CampgroundConfig.CampgroundID, CheckDt);
            //first thread to create entry receives data, remaining threads wait for data
            AvailabilityEntries source = new AvailabilityEntries();
            if (Host != null)
            {
                //an interactive search (GS1): fetched with the run's bounds, or waited for until whoever fetches it is done
                //(with data, a final failure, a block or the end of the run). A month that could not be checked adds
                //nothing to the output, so it never reads as a month without availability
                AvailabilityEntries? Month = Host.GetMonth(Url, CampgroundConfig, CheckDt, RunData);
                if (Month == null)
                {
                    return;
                }
                source = Month;
            }
            else if (RunData.Urls.TryAdd(Url, false))
            {
                GetData(Url, ref source, ref RunData);
            }
            else
            {
                //loop until thread is finished retrieving data
                bool DataReady = false;
                while (!DataReady)
                {
                    source = RunData.SiteData.Where(p => p.Key == Url).FirstOrDefault().Value;
                    if (source != null)
                    {
                        DataReady = true;
                    }
                    else
                    {
                        //a stopped monitoring ends the wait: the search that fetches the month has stopped too
                        Pause(RunData.Monitoring, 500);
                    }
                }
            }
            //the month's header follows the fetch (nothing is added to the output while a month is fetched, so the
            //console's output is the same as when it came first)
            if (CampgroundConfig.ConsecutiveDays == 1)
            {
                ConsoleResultHolder.AddRange(MonthHeaderValues(Pdt));
            }
            if (source?.campsites != null)
            {
                int DaysInCurrentMonth = DateTime.DaysInMonth(CheckDt.Year, CheckDt.Month);
                //source is shared with every other search of this campground and month: read it, never write it (TD-40)
                List<CampsiteAvailableDates> SearchDates = AvailableDatesBySite(source, CheckDt, DaysInCurrentMonth);
                int counter = 0;
                while (counter < DaysInCurrentMonth)
                {
                    Host?.ThrowIfCanceled();
                    //find matches
                    DateTime Checker = CheckDt.AddDays(counter);
                    var CampsiteAvailabilityEntries = from SiteDates in SearchDates
                                                      let CampsitesTable = SiteDates.Entry
                                                      join SitesTable in Sites on CampsitesTable.campsite_id equals SitesTable.CampsiteID
                                                      where SiteDates.AvailableDates.Contains(Checker)
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
                    //the campsite-type lists: excludeCampsiteType drops a campsite whose type matches any of its values,
                    //includeCampsiteType keeps one whose type matches any of its values (OR since D51; before, every value)
                    if ((CampgroundConfig.ExcludeCampsiteType != null && CampgroundConfig.ExcludeCampsiteType.Count > 0) ||
                        (CampgroundConfig.IncludeCampsiteType != null && CampgroundConfig.IncludeCampsiteType.Count > 0))
                    {
                        CampsiteAvailabilityEntries = CampsiteAvailabilityEntries.Where(x => SearchFilters.KeepsCampsiteType(CampgroundConfig, x.CampsiteType));
                    }
                    //if matches
                    if (CampsiteAvailabilityEntries != null && CampsiteAvailabilityEntries.Count() > 0)
                    {
                        foreach (AvailabilityData ThisEntry in CampsiteAvailabilityEntries)
                        {
                            Host?.ThrowIfCanceled();
                            FilterData(CampgroundConfig, config, ThisEntry, SearchHits, ref ConsoleResultHolder, ref HitCounter, ref HitDates);
                        }
                    }
                    counter++;
                }
            }
            if (CampgroundConfig.ConsecutiveDays == 1)
            {
                if (HitCounter == 0)
                {
                    ConsoleResultHolder.AddRange(NoEntriesValues(Pdt));
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
        //the first of Pdt's month, built from its numbers: parsing "M/1/yyyy" text depended on the current culture and
        //turned November into 11 January in en-GB (TD-11)
        internal static DateTime MonthStart(DateTime Pdt)
        {
            return new DateTime(Pdt.Year, Pdt.Month, 1);
        }
        //the month's availability URL, written with the invariant culture: a culture with another calendar (fa-IR)
        //writes another month number (TD-11)
        internal static string MonthUrl(string? CampgroundID, DateTime FirstOfMonth)
        {
            return $"https://www.recreation.gov/api/camps/availability/campground/{CampgroundID}/month?start_date={FirstOfMonth.Year.ToString(CultureInfo.InvariantCulture)}-{FirstOfMonth.ToString("MM", CultureInfo.InvariantCulture)}-01T00%3A00%3A00.000Z";
        }
        //each campsite of the month with its available dates, in source.campsites order (duplicates kept). The entries
        //are the shared cached objects and are only read; the date lists are new and belong to the calling search (TD-40)
        public static List<CampsiteAvailableDates> AvailableDatesBySite(AvailabilityEntries Source, DateTime CheckDt, int DaysInMonth)
        {
            List<CampsiteAvailableDates> ReturnList = new List<CampsiteAvailableDates>();
            foreach (CampsitesDataEntry Entry in Source.campsites ?? new List<CampsitesDataEntry>())
            {
                ReturnList.Add(new CampsiteAvailableDates(Entry, CampsitesDataEntry.CalculateAvailableDates(Entry.availabilities, CheckDt, DaysInMonth)));
            }
            return ReturnList;
        }
        private static void FilterData(CampertronConfig CampgroundConfig,
            CampertronInternalConfig InternalConfig,
            AvailabilityData ThisEntry,
            ConcurrentBag<AvailableData> SearchHits,
            ref List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder,
            ref int HitCounter,
            ref List<DateTime> HitDates)
        {
            //duplicate notification check
            var CampHistoryPath = Path.Join(InternalConfig.ConfigPath, "Camp.History");
            String NotificationPath = CampHistoryPath + '/' +
                          ThisEntry.CampsiteAvailableDate?.Year.ToString() + '/' +
                          ThisEntry.CampsiteAvailableDate?.Month.ToString() + '/' +
                          ThisEntry.CampsiteAvailableDate?.Day.ToString() + '/' +
                          ThisEntry.CampsiteID.ToString();

            //total humans is mandatory
            if (CampgroundConfig.TotalHumans >= ThisEntry.Minppl &&
                CampgroundConfig.TotalHumans <= ThisEntry.Maxppl &&
                ThisEntry.CampsiteAvailableDate >= DateTime.Now &&
                CampgroundConfig.GetSearchDates().Contains((DateTime)ThisEntry.CampsiteAvailableDate) &&
                //search days is mandatory
                CampgroundConfig.ShowThisDay(ThisEntry.CampsiteAvailableDate?.DayOfWeek.ToString()) &&
                //the equipment, campsite-name and attribute lists, each with OR semantics, matched by the search's
                //filterMatching (SearchFilters, D51)
                SearchFilters.KeepsCampsite(CampgroundConfig, ThisEntry) &&
                
                //prevents duplicate notifications: Email only (Load.UsesNotificationHistory)
                (Load.UsesNotificationHistory(InternalConfig) == false || Directory.Exists(NotificationPath) == false))
            {
                //store the entry
                AvailableData ThisAvailableData = new AvailableData();
                ThisAvailableData.CampsiteID = ThisEntry?.CampsiteID;
                ThisAvailableData.HitDate = (DateTime)ThisEntry.CampsiteAvailableDate;
                ThisAvailableData.AvailablityObj = ThisEntry;
                ThisAvailableData.ConsoleData = new List<ConsoleConfig.ConsoleConfigValue>();
                //get property values to show
                string? Checkin = ThisEntry.CampsiteAttributeLists.AttValuePair.Where(p => p.AttributeName == "Checkin Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                string? Checkout = ThisEntry.CampsiteAttributeLists.AttValuePair.Where(p => p.AttributeName == "Checkout Time").Select(p => p.AttributeValue).FirstOrDefault() ?? "";
                if (CampgroundConfig.ConsecutiveDays != 1)
                {
                    ThisAvailableData.ConsoleData.AddRange(AvailabilityValues(ThisEntry, Checkin, Checkout, true));
                }
                else
                {
                    ConsoleResultHolder.AddRange(AvailabilityValues(ThisEntry, Checkin, Checkout, false));
                    //get unique hitdates for calendar generation
                    if (HitDates.Contains((DateTime)ThisEntry.CampsiteAvailableDate) == false)
                    {
                        HitDates.Add((DateTime)ThisEntry.CampsiteAvailableDate);
                    }

                    HitCounter++;
                }
                //get unique hitdates for calendar generation
                if (HitDates.Contains((DateTime)ThisEntry.CampsiteAvailableDate) == false)
                {
                    HitDates.Add((DateTime)ThisEntry.CampsiteAvailableDate);
                }

                HitCounter++;
                SearchHits.Add(ThisAvailableData);
            }
        }
        //the hits of this search that belong to runs of ConsecutiveDays nights on one campsite, written to the output;
        //it reads only SearchHits, this search's own hits, never another search's (TD-04)
        private static ConcurrentBag<AvailableData> FilterConsecutiveDays(
            CampertronConfig CampgroundConfig,
            CampertronInternalConfig config,
            ConcurrentBag<AvailableData> SearchHits,
            ref List<ConsoleConfig.ConsoleConfigValue> ConsoleResultHolder,
            SearchHost? Host)
        {
            //hold results for this campsite
            List<AvailableData> Filtered = new List<AvailableData>();
            //initial filter
            var EntriesWithEnoughValues = SearchHits.GroupBy(p => p.CampsiteID).Where(p => p.Count() >= CampgroundConfig.ConsecutiveDays);
            foreach (var ThisCampsiteData in EntriesWithEnoughValues)
            {
                Host?.ThrowIfCanceled();
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
                var CampsitesFiltered = SearchHits.Where(p => p.CampsiteID == ThisCampsiteData.Key);
                foreach (var ThisDate in UniqueDates)
                {
                    Filtered.AddRange(CampsitesFiltered.Where(p => p.HitDate == ThisDate));
                }
            }
            ConcurrentBag<AvailableData> FinalHits = new ConcurrentBag<AvailableData>(Filtered);
            var GroupedAvailableData = FinalHits.GroupBy(p => p.CampsiteID);
            foreach (var ThisGroupedAvailableData in GroupedAvailableData)
            {
                Host?.ThrowIfCanceled();
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
            return FinalHits;
        }
        //"Searching for entries during {Month}/{Year}" (consecutiveDays == 1), tagged MonthHeader for the HTML results
        public static List<ConsoleConfig.ConsoleConfigValue> MonthHeaderValues(DateTime Pdt)
        {
            var Header = CampsiteConfig.AddConsoleConfigItem($"Searching for entries during {CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Pdt.Month)}/{Pdt.Year} 🔎", ConsoleColor.Yellow);
            Header.Kind = ConsoleConfig.OutputKind.MonthHeader;
            Header.Metadata = new ConsoleConfig.OutputMetadata { Date = new DateTime(Pdt.Year, Pdt.Month, 1) };
            return new List<ConsoleConfig.ConsoleConfigValue>
            {
                CampsiteConfig.AddConsoleConfigItem(true),
                Header,
                CampsiteConfig.AddConsoleConfigItem(true)
            };
        }
        //"No entries found on {M}/{yyyy}" (consecutiveDays == 1), tagged NoEntries for the HTML results
        public static List<ConsoleConfig.ConsoleConfigValue> NoEntriesValues(DateTime Pdt)
        {
            var Message = CampsiteConfig.AddConsoleConfigItem($"\tNo entries found on {Pdt.Month}/{Pdt.Year}", ConsoleColor.DarkRed);
            Message.Kind = ConsoleConfig.OutputKind.NoEntries;
            Message.Metadata = new ConsoleConfig.OutputMetadata { Date = new DateTime(Pdt.Year, Pdt.Month, 1) };
            return new List<ConsoleConfig.ConsoleConfigValue> { Message, CampsiteConfig.AddConsoleConfigItem(true) };
        }
        //the values of one hit. Single nights (Consecutive false): 10 values written in order, the first tagged
        //Availability. Consecutive: the AvailableData.ConsoleData layout FilterConsecutiveDays relies on: values 0-3
        //are the date part (written once per date; value 0 tagged ConsecutiveDate) and 4-9 the site part (written
        //once per campsite; value 5 tagged ConsecutiveSite). Every other value is tagged Detail.
        public static List<ConsoleConfig.ConsoleConfigValue> AvailabilityValues(AvailabilityData ThisEntry, string Checkin, string Checkout, bool Consecutive)
        {
            List<ConsoleConfig.ConsoleConfigValue> Values = new List<ConsoleConfig.ConsoleConfigValue>();
            Values.Add(CampsiteConfig.AddConsoleConfigItem($"\tDate:\t   {ThisEntry.CampsiteAvailableDate?.ToShortDateString()} (", true));
            Values.Add(CampsiteConfig.AddConsoleConfigItem($"{ThisEntry.CampsiteAvailableDate?.DayOfWeek}", ConsoleColor.Cyan, true));
            Values.Add(CampsiteConfig.AddConsoleConfigItem($") 📆 Checkin:{Checkin} CheckOut:{Checkout}", true));
            Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            if (Consecutive)
            {
                Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            }
            Values.Add(CampsiteConfig.AddConsoleConfigItem($"\tSite:\t   {ThisEntry.CampsiteName} ➰ {ThisEntry.CampsiteLoop} ({ThisEntry.CampsiteType})"));
            Values.Add(CampsiteConfig.AddConsoleConfigItem($"\tEquipment: {string.Join(",", ThisEntry.PermittedEquipmentList.ToArray())}"));
            Values.Add(CampsiteConfig.AddConsoleConfigItem("\tURL:\t   ", true));
            Values.Add(CampsiteConfig.AddConsoleConfigItem($"https://www.recreation.gov/camping/campsites/{ThisEntry.CampsiteID}", ConsoleColor.Blue, true));
            Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            if (!Consecutive)
            {
                Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            }

            ConsoleConfig.OutputMetadata Meta = new ConsoleConfig.OutputMetadata
            {
                Date = ThisEntry.CampsiteAvailableDate,
                CampsiteID = ThisEntry.CampsiteID,
                CampsiteName = ThisEntry.CampsiteName,
                CampsiteLoop = ThisEntry.CampsiteLoop,
                CampsiteType = ThisEntry.CampsiteType,
                Equipment = new List<string>(ThisEntry.PermittedEquipmentList ?? new List<string>()),
                Checkin = Checkin,
                Checkout = Checkout,
                MinOccupancy = ThisEntry.Minppl,
                MaxOccupancy = ThisEntry.Maxppl
            };
            foreach (ConsoleConfig.ConsoleConfigValue Value in Values)
            {
                Value.Kind = ConsoleConfig.OutputKind.Detail;
            }
            Values[0].Kind = Consecutive ? ConsoleConfig.OutputKind.ConsecutiveDate : ConsoleConfig.OutputKind.Availability;
            Values[0].Metadata = Meta;
            if (Consecutive)
            {
                Values[5].Kind = ConsoleConfig.OutputKind.ConsecutiveSite;
                Values[5].Metadata = Meta;
            }
            return Values;
        }
        //used to determine if we need to get data for this month (also by CampsiteSearch, to plan the same months)
        internal static bool HasDatesDuringThisMonth(CampertronConfig CampgroundConfig, int Month, int Year)
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