using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using static ConsoleConfig;
using System.Text.Json;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Load
    {
        public static void Init()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "🤖 CAMPERTRON 🤖";
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = Path.Join(path, "CampertronCache");
            if (Directory.Exists(cachepath) == false)
            {
                Directory.CreateDirectory(cachepath);
            }
            var configpath = Path.Join(path, "CampertronConfig");
            if (Directory.Exists(configpath) == false)
            {
                Directory.CreateDirectory(configpath);
            }
            GeneralConfig GeneralConfig = Yaml.GeneralConfigGetConfig();
            EmailConfig EmailConfig = Yaml.EmailConfigGetConfig();            
            DbExistsCheck(GeneralConfig);
            ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>();//Stores console data to write
            ConcurrentDictionary<string, AvailabilityEntries> SiteData = new ConcurrentDictionary<string, AvailabilityEntries>();//Contains deserialized site data
            ConcurrentDictionary<string, bool> Urls = new ConcurrentDictionary<string, bool>();//Ensures that multiple campground configs for the same site/date is only downloaded once
            Console.Write("\f\u001bc\x1b[3J");
            List<string> UniqueCampgroundIds = new List<string>();

            List<CampertronConfig> CampertronConfigFiles = Yaml.CampertronConfigGetConfigs();
            foreach (CampertronConfig ThisConfig in CampertronConfigFiles)
            {
                if (UniqueCampgroundIds.Contains(ThisConfig.CampgroundID) == false)
                {
                    UniqueCampgroundIds.Add(ThisConfig.CampgroundID);
                }
            }
            Parallel.ForEach(UniqueCampgroundIds, ThisCampgroundId =>
            {
                Cache.PreCheckCache(ThisCampgroundId);
            });

            List<CampsiteHistory> CampHistoryList = new List<CampsiteHistory>();
            var CampHistoryPath = Path.Join(cachepath, "Camp.History");
            if (File.Exists(CampHistoryPath) && GeneralConfig.OutputTo == OutputType.Email)
            {
                CampHistoryList = JsonSerializer.Deserialize<List<CampsiteHistory>>(File.ReadAllText(CampHistoryPath));
            }
            ConcurrentBag<CampsiteHistory> CampHistory = new ConcurrentBag<CampsiteHistory>(CampHistoryList);
            List<CampsiteHistory> OldHistoryList = CampHistoryList;
            while (true)
            {
                Parallel.ForEach(CampertronConfigFiles, ThisConfig =>
                {
                    ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                    NewConfigItem.Name = ThisConfig.DisplayName;
                    DateTime Start = DateTime.UtcNow;
                    CampsiteConfig.WriteToConsole("Retrieving availability for campground ID:" + ThisConfig.CampgroundID + " on thread:" + Task.CurrentId, ConsoleColor.Magenta);
                    NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisConfig, ref SiteData, ref Urls, GeneralConfig, ref CampHistory);
                    AllConsoleConfigItems.Add(NewConfigItem);
                    DateTime End = DateTime.UtcNow;
                    double TotalSeconds = (End - Start).TotalSeconds;
                    CampsiteConfig.WriteToConsole("Finished retrieving campground ID:" + ThisConfig.CampgroundID + " in " + TotalSeconds + " seconds", ConsoleColor.DarkMagenta);
                });
                ConfigType LastConfigType = ConfigType.WriteLine;
                //determine if there are new entries
                CampHistoryList = new List<CampsiteHistory>(CampHistory);
                bool NewEntries = HasNewEntries(NewList: CampHistoryList, OldList: OldHistoryList);
                //if output to email and there are new entries or not output to email process config
                if ((GeneralConfig.OutputTo == OutputType.Email && NewEntries) || GeneralConfig.OutputTo != OutputType.Email)
                {
                    foreach (ConsoleConfigItem ThisConsoleConfig in AllConsoleConfigItems.OrderBy(p => p.Name))
                    {
                        CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType, GeneralConfig, EmailConfig);
                    }
                }
                if (GeneralConfig.OutputTo == OutputType.Email)
                {
                    String CampsiteHistoryJson = JsonSerializer.Serialize(CampHistoryList);
                    File.WriteAllText(CampHistoryPath, CampsiteHistoryJson);
                }
                Urls.Clear();
                AllConsoleConfigItems.Clear();
                SiteData.Clear();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("CampertronConfig:" + configpath);
                OldHistoryList = CampHistoryList;
                NextStep();
            }
        }
        public static void NextStep()
        {
            CampsiteConfig.WriteToConsole("\nPress enter to search again or type refresh and hit enter to refresh RIDB Recreation Data", ConsoleColor.Magenta);
            string ReceivedKeys = Console.ReadLine();
            if (ReceivedKeys != null)
            {
                if (ReceivedKeys.ToUpper().Trim() == "REFRESH")
                {
                    RefreshRidbRecreationData.RefreshData(false);
                }
            }
            Console.Write("\f\u001bc\x1b[3J");
        }
        public static bool DbExistsCheck(GeneralConfig GeneralConfig)
        {
            //if db doesn't exist extract blank db and populate
            if (!DbExists())
            {
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);
                string DbFile = Path.Join(path, "RecreationDotOrg.db");
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                using (var reader = embeddedProvider.GetFileInfo(@"content\RecreationDotOrg.db").CreateReadStream())
                {
                    using (Stream s = File.Create(DbFile))
                    {
                        reader.CopyTo(s);
                    }
                }
                RefreshRidbRecreationData.RefreshData(true);
            }
            else
            {//if db does exist see if it needs to be autorefreshed
                if (DateTime.UtcNow.AddDays(-GeneralConfig.RefreshRidbDataDayInterval) > GeneralConfig.LastRidbDataRefresh)
                {
                    GeneralConfig.LastRidbDataRefresh = DateTime.UtcNow;
                    Yaml.GeneralConfigConvertToYaml(GeneralConfig, "General");
                    RefreshRidbRecreationData.RefreshData(false);
                }
            }
            return true;
        }
        public static bool DbExists()
        {
            bool DbExists = false;
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            string DbFile = Path.Join(path, "RecreationDotOrg.db");
            if (File.Exists(DbFile))
            {
                DbExists = true;
            }
            return DbExists;
        }
        public static bool HasNewEntries(List<CampsiteHistory> NewList, List<CampsiteHistory> OldList)
        {
            bool HasNewEntries = false;
            foreach(CampsiteHistory ThisNewListItem in NewList)
            {
                var NewItemCheck = (from p in OldList
                                    where p.CampsiteID == ThisNewListItem.CampsiteID &&
                                    p.HitDate == ThisNewListItem.HitDate
                                    select p).FirstOrDefault();
                if (NewItemCheck == null)
                {
                    HasNewEntries = true;
                    break;
                }
            }
            return HasNewEntries;
        }
    }
}