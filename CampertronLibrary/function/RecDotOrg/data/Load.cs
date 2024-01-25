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
using static Org.BouncyCastle.Math.EC.ECCurve;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Load
    {
        public static void InitConsole()
        {
            CampertronInternalConfig config = GetConfig();
            if (config.GeneralConfig.OutputTo == OutputType.Email)
            {
                StartConsoleSearch(config);
            }
            else
            {
                Menu.Init(config, true);
            }
        }
        public static void StartConsoleSearch(CampertronInternalConfig InternalConfig)
        {
            ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>();//Stores console data to write
            ConcurrentDictionary<string, AvailabilityEntries> SiteData = new ConcurrentDictionary<string, AvailabilityEntries>();//Contains deserialized site data
            ConcurrentDictionary<string, bool> Urls = new ConcurrentDictionary<string, bool>();//Ensures that multiple campground configs for the same site/date is only downloaded once
            //clear console
            Console.Write("\f\u001bc\x1b[3J");
            //caching sqlite site data
            List<string> UniqueCampgroundIds = new List<string>();
            List<CampertronConfig> CampertronConfigFiles = Yaml.CampertronConfigGetConfigs(InternalConfig.ConfigPath);            
            foreach (CampertronConfig ThisConfig in CampertronConfigFiles)
            {
                if (UniqueCampgroundIds.Contains(ThisConfig.CampgroundID) == false)
                {
                    UniqueCampgroundIds.Add(ThisConfig.CampgroundID);
                }
            }            
            Parallel.ForEach(UniqueCampgroundIds, ThisCampgroundId =>
            {
                Cache.PreCheckCache(ThisCampgroundId, InternalConfig.CachePath, InternalConfig.ConfigPath);
            });
            //data we care about
            ConcurrentBag<AvailableData> FilteredAvailableData = new ConcurrentBag<AvailableData>();            
            //run forever
            while (true)
            {
                Parallel.ForEach(CampertronConfigFiles, ThisCampertronConfig =>
                {
                    ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                    NewConfigItem.Name = ThisCampertronConfig.DisplayName;
                    DateTime Start = DateTime.UtcNow;
                    CampsiteConfig.WriteToConsole("Retrieving availability for campground ID:" + ThisCampertronConfig.CampgroundID, ConsoleColor.Magenta);
                    NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisCampertronConfig, ref SiteData, ref Urls, InternalConfig, ref FilteredAvailableData);
                    AllConsoleConfigItems.Add(NewConfigItem);
                    DateTime End = DateTime.UtcNow;
                    double TotalSeconds = (End - Start).TotalSeconds;
                    CampsiteConfig.WriteToConsole("Finished retrieving campground ID:" + ThisCampertronConfig.CampgroundID + " in " + TotalSeconds + " seconds", ConsoleColor.DarkMagenta);
                });                
                //place tracking for writing lines
                ConfigType LastConfigType = ConfigType.WriteLine;

                //determine if there are new entries
                bool NewEntries = HasNewEntries(new List<AvailableData>(FilteredAvailableData), InternalConfig);
                
                //if output to email and there are new entries or not output to email process config
                if ((InternalConfig.GeneralConfig.OutputTo == OutputType.Email && 
                    NewEntries && 
                    FilteredAvailableData.Count() > 0) || InternalConfig.GeneralConfig.OutputTo != OutputType.Email)
                {
                    foreach (ConsoleConfigItem ThisConsoleConfig in AllConsoleConfigItems.OrderBy(p => p.Name))
                    {
                        CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType, InternalConfig.GeneralConfig, InternalConfig.EmailConfig);
                    }
                }

                //clear data for next run
                Urls.Clear();
                AllConsoleConfigItems.Clear();
                SiteData.Clear();
                FilteredAvailableData.Clear();
                
                PostProcess(InternalConfig.ConfigPath, InternalConfig);
            }
        }
        public static bool HasNewEntries(List<AvailableData> FilteredAvailableData, CampertronInternalConfig InternalConfig)
        {
            bool HasNewEntries = false;

            var CampHistoryPath = Path.Join(InternalConfig.ConfigPath, "Camp.History");
            if(!Directory.Exists(CampHistoryPath))
            {
                Directory.CreateDirectory(CampHistoryPath);
            }

            foreach(AvailableData availableData in FilteredAvailableData)
            {
                String NotificationPath = CampHistoryPath + '/' +
                                          availableData.HitDate.Year.ToString() + '/' +
                                          availableData.HitDate.Month.ToString() + '/' +
                                          availableData.HitDate.Day.ToString() + '/' +
                                          availableData.CampsiteID.ToString();
                if(!Directory.Exists(NotificationPath))
                {
                    HasNewEntries = true;
                    Directory.CreateDirectory(NotificationPath);
                }
            }

            return HasNewEntries;
        }
        public static CampertronInternalConfig GetConfig()
        {
            CampertronInternalConfig ReturnConfig = new CampertronInternalConfig();

            if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != null || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS") != null)
            {
                ReturnConfig.ConfigPath = "/config";
                ReturnConfig.CachePath = "/cache";

                if (Directory.Exists(ReturnConfig.ConfigPath) == false)
                {
                    throw new Exception("Config path does not exist");
                }
                if (Directory.Exists(ReturnConfig.CachePath) == false)
                {
                    throw new Exception("Cache path does not exist");
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
        public static void PostProcess(String ConfigPath, CampertronInternalConfig config)
        {
            if (config.GeneralConfig.OutputTo != OutputType.Email)
            {
                if (config.ContainerMode == true)
                {
                    CampsiteConfig.WriteToConsole("\nSleeping for 1 minute", ConsoleColor.Magenta);
                    Thread.Sleep(60000);
                }
                else
                {
                    if (config.GeneralConfig.OutputTo != OutputType.Console && config.GeneralConfig.OutputTo != OutputType.HtmlFile)
                    {
                        StartConsoleSearch(config);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Press any key to continue...");
                        Console.ResetColor();
                        Console.ReadKey();
                        Menu.Init(config, false);
                    }
                }
            }
            else
            {
                CampsiteConfig.WriteToConsole("\nSleeping for 1 minute", ConsoleColor.Magenta);
                Thread.Sleep(60000);
            }
            Console.Write("\f\u001bc\x1b[3J");
        }
        public static bool DbExistsCheck(CampertronInternalConfig InternalConfig)
        {
            //if db doesn't exist extract blank db and populate
            if (!DbExists(InternalConfig))
            {
                string DbZipFile = Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.zip");
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                using (var reader = embeddedProvider.GetFileInfo(@"content/RecreationDotOrg.zip").CreateReadStream())
                {
                    using (Stream s = File.Create(DbZipFile))
                    {
                        reader.CopyTo(s);
                    }
                }
                ZipFile.ExtractToDirectory(DbZipFile, InternalConfig.ConfigPath);
                File.Delete(DbZipFile);
            }
            else
            {//if db does exist see if it needs to be autorefreshed
                if (InternalConfig.GeneralConfig.AutoRefresh == true && 
                    DateTime.UtcNow.AddDays(-InternalConfig.GeneralConfig.RefreshRidbDataDayInterval) > InternalConfig.GeneralConfig.LastRidbDataRefresh)
                {
                    InternalConfig.GeneralConfig.LastRidbDataRefresh = DateTime.UtcNow;
                    Yaml.GeneralConfigConvertToYaml(InternalConfig.GeneralConfig, "General", InternalConfig.ConfigPath);
                    RefreshRidbRecreationData.RefreshData(false, InternalConfig);
                }
            }
            return true;
        }
        public static bool DbExists(CampertronInternalConfig InternalConfig)
        {
            bool DbExists = false;
            string DbFile = Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.db");
            if (File.Exists(DbFile))
            {
                DbExists = true;
            }
            return DbExists;
        }
    }
}