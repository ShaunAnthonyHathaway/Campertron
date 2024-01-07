using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using static ConsoleConfig;
using System.Text.Json;
using System.IO.Compression;
using System.Collections.Generic;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Load
    {
        public static void Init()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "🤖 CAMPERTRON 🤖";

            CtConfig config = GetConfig();

            ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>();//Stores console data to write
            ConcurrentDictionary<string, AvailabilityEntries> SiteData = new ConcurrentDictionary<string, AvailabilityEntries>();//Contains deserialized site data
            ConcurrentDictionary<string, bool> Urls = new ConcurrentDictionary<string, bool>();//Ensures that multiple campground configs for the same site/date is only downloaded once
            Console.Write("\f\u001bc\x1b[3J");
            List<string> UniqueCampgroundIds = new List<string>();

            List<CampertronConfig> CampertronConfigFiles = Yaml.CampertronConfigGetConfigs(config.ConfigPath);
            foreach (CampertronConfig ThisConfig in CampertronConfigFiles)
            {
                if (UniqueCampgroundIds.Contains(ThisConfig.CampgroundID) == false)
                {
                    UniqueCampgroundIds.Add(ThisConfig.CampgroundID);
                }
            }
            Parallel.ForEach(UniqueCampgroundIds, ThisCampgroundId =>
            {
                Cache.PreCheckCache(ThisCampgroundId, config.CachePath, config.ConfigPath);
            });

            List<CampsiteHistory> CampHistoryList = new List<CampsiteHistory>();

            var CampHistoryPath = Path.Join(config.CachePath, "Camp.History");
            if (File.Exists(CampHistoryPath) && config.GeneralConfig.OutputTo == OutputType.Email)
            {
                CampHistoryList = JsonSerializer.Deserialize<List<CampsiteHistory>>(File.ReadAllText(CampHistoryPath));
            }
            ConcurrentBag<CampsiteHistory> CampHistory = new ConcurrentBag<CampsiteHistory>(CampHistoryList);
            ConcurrentBag<AvailableData> FilteredAvailableData = new ConcurrentBag<AvailableData>();
            List<CampsiteHistory> OldHistoryList = CampHistoryList;
            while (true)
            {
                Parallel.ForEach(CampertronConfigFiles, ThisConfig =>
                {
                    ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                    NewConfigItem.Name = ThisConfig.DisplayName;
                    DateTime Start = DateTime.UtcNow;
                    CampsiteConfig.WriteToConsole("Retrieving availability for campground ID:" + ThisConfig.CampgroundID + " on thread:" + Task.CurrentId, ConsoleColor.Magenta);
                    NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisConfig, ref SiteData, ref Urls, config, ref CampHistory, ref FilteredAvailableData);
                    AllConsoleConfigItems.Add(NewConfigItem);
                    DateTime End = DateTime.UtcNow;
                    double TotalSeconds = (End - Start).TotalSeconds;
                    CampsiteConfig.WriteToConsole("Finished retrieving campground ID:" + ThisConfig.CampgroundID + " in " + TotalSeconds + " seconds", ConsoleColor.DarkMagenta);
                });
                ConfigType LastConfigType = ConfigType.WriteLine;
                //determine if there are new entries
                CampHistoryList = new List<CampsiteHistory>(CampHistory);
                bool NewEntries = HasNewEntries(NewList: ref CampHistoryList, OldList: OldHistoryList, new List<AvailableData>(FilteredAvailableData), CampHistoryPath);
                //if output to email and there are new entries or not output to email process config
                if ((config.GeneralConfig.OutputTo == OutputType.Email && NewEntries && FilteredAvailableData.Count() > 0) || config.GeneralConfig.OutputTo != OutputType.Email)
                {
                    foreach (ConsoleConfigItem ThisConsoleConfig in AllConsoleConfigItems.OrderBy(p => p.Name))
                    {
                        CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType, config.GeneralConfig, config.EmailConfig);
                    }
                }
                Urls.Clear();
                AllConsoleConfigItems.Clear();
                SiteData.Clear();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("CampertronConfig:" + config.ConfigPath);
                OldHistoryList = CampHistoryList;
                NextStep(config.ConfigPath, config);
            }
        }
        public static bool HasNewEntries(ref List<CampsiteHistory> NewList, List<CampsiteHistory> OldList, List<AvailableData> FilteredAvailableData, String CampHistoryPath)
        {
            bool HasNewEntries = false;
            List<CampsiteHistory> NewFilteredList = new List<CampsiteHistory>();
            foreach (CampsiteHistory ThisNewListItem in NewList)
            {
                var NewFilteredItemCheck = (from p in FilteredAvailableData
                                            where p.CampsiteID == ThisNewListItem.CampsiteID &&
                                            p.HitDate == ThisNewListItem.HitDate
                                            select p).FirstOrDefault();
                if (NewFilteredItemCheck != null)
                {
                    NewFilteredList.Add(ThisNewListItem);
                    var NewItemCheck = (from p in OldList
                                        where p.CampsiteID == ThisNewListItem.CampsiteID &&
                                        p.HitDate == ThisNewListItem.HitDate
                                        select p).FirstOrDefault();
                    if (NewItemCheck == null)
                    {
                        HasNewEntries = true;
                        String CampsiteHistoryJson = JsonSerializer.Serialize(NewFilteredList);
                        File.WriteAllText(CampHistoryPath, CampsiteHistoryJson);
                    }                    
                }
            }
            NewList = NewFilteredList;
            return HasNewEntries;
        }
        public static CtConfig GetConfig()
        {
            CtConfig ReturnConfig = new CtConfig();

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null)
            {
                ReturnConfig.ConfigPath = "/config";
                ReturnConfig.CachePath = "/cache";
                if (Directory.Exists(ReturnConfig.ConfigPath) == false)
                {
                    throw new Exception("Config path does not exist");
                }
                ReturnConfig.GeneralConfig = Yaml.GeneralConfigGetConfig(ReturnConfig.ConfigPath);
                ReturnConfig.EmailConfig = Yaml.EmailConfigGetConfig(ReturnConfig.ConfigPath);
                ReturnConfig.GeneralConfig.AutoRefresh = false;//refresh uses too much memory
                ReturnConfig.ContainerMode = true;
            }
            else
            {
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
                ReturnConfig.ConfigPath = configpath;
                ReturnConfig.CachePath = cachepath;
                ReturnConfig.ContainerMode = false;
                if (Directory.Exists(ReturnConfig.ConfigPath) == false)
                {
                    throw new Exception("Config path does not exist");
                }
            }

            ReturnConfig.GeneralConfig = Yaml.GeneralConfigGetConfig(ReturnConfig.ConfigPath);
            ReturnConfig.EmailConfig = Yaml.EmailConfigGetConfig(ReturnConfig.ConfigPath);
            DbExistsCheck(ReturnConfig);

            return ReturnConfig;
        }
        public static void NextStep(String ConfigPath, CtConfig config)
        {
            if (config.GeneralConfig.OutputTo != OutputType.Email)
            {
                CampsiteConfig.WriteToConsole("\nPress enter to search again or type refresh and hit enter to refresh RIDB Recreation Data", ConsoleColor.Magenta);
                string ReceivedKeys = Console.ReadLine();
                if (ReceivedKeys != null)
                {
                    if (ReceivedKeys.ToUpper().Trim() == "REFRESH")
                    {
                        RefreshRidbRecreationData.RefreshData(false, ConfigPath);
                    }
                }
                if (config.ContainerMode == true)
                {
                    CampsiteConfig.WriteToConsole("\nSleeping for 1 minute", ConsoleColor.Magenta);
                    Thread.Sleep(60000);
                }
            }
            else
            {
                CampsiteConfig.WriteToConsole("\nSleeping for 1 minute", ConsoleColor.Magenta);
                Thread.Sleep(60000);
            }
            Console.Write("\f\u001bc\x1b[3J");
        }
        public static bool DbExistsCheck(CtConfig Ctc)
        {
            //if db doesn't exist extract blank db and populate
            if (!DbExists(Ctc))
            {
                string DbZipFile = Path.Join(Ctc.ConfigPath, "RecreationDotOrg.zip");
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                using (var reader = embeddedProvider.GetFileInfo(@"content/RecreationDotOrg.zip").CreateReadStream())
                {
                    using (Stream s = File.Create(DbZipFile))
                    {
                        reader.CopyTo(s);
                    }
                }
                ZipFile.ExtractToDirectory(DbZipFile, Ctc.ConfigPath);
                File.Delete(DbZipFile);
            }
            else
            {//if db does exist see if it needs to be autorefreshed
                if (Ctc.GeneralConfig.AutoRefresh == true && DateTime.UtcNow.AddDays(-Ctc.GeneralConfig.RefreshRidbDataDayInterval) > Ctc.GeneralConfig.LastRidbDataRefresh)
                {
                    Ctc.GeneralConfig.LastRidbDataRefresh = DateTime.UtcNow;
                    Yaml.GeneralConfigConvertToYaml(Ctc.GeneralConfig, "General", Ctc.ConfigPath);
                    RefreshRidbRecreationData.RefreshData(false, Ctc.ConfigPath);
                }
            }
            return true;
        }
        public static bool DbExists(CtConfig Ctc)
        {
            bool DbExists = false;
            string DbFile = Path.Join(Ctc.ConfigPath, "RecreationDotOrg.db");
            if (File.Exists(DbFile))
            {
                DbExists = true;
            }
            return DbExists;
        }
    }
}