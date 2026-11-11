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
using System.ComponentModel;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Load
    {
        public static RunningData Preload(CampertronInternalConfig InternalConfig)
        {
            ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfigItem>();//Stores console data to write
            ConcurrentDictionary<string, AvailabilityEntries> SiteData = new ConcurrentDictionary<string, AvailabilityEntries>();//Contains deserialized site data
            ConcurrentDictionary<string, bool> Urls = new ConcurrentDictionary<string, bool>();//Ensures that multiple campground configs for the same site/date is only downloaded once
            if (InternalConfig?.GeneralConfig?.OutputTo != OutputType.UnitTest)
            {
                //clear console
                Console.Write("\f\u001bc\x1b[3J");
            }
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

            return new RunningData
            {
                AllConsoleConfigItems = AllConsoleConfigItems,
                SiteData = SiteData,
                Urls = Urls,
                UniqueCampgroundIds = UniqueCampgroundIds,
                CampertronConfigFiles = CampertronConfigFiles,
                FilteredAvailableData = FilteredAvailableData
            };
        }
        public static bool RunConsoleSearch(CampertronInternalConfig InternalConfig)
        {
            if(InternalConfig?.GeneralConfig?.OutputTo == OutputType.UnitTest)
            {
                RunConsoleSearch(InternalConfig, false);
                return true;
            }
            else
            {
                return RunConsoleSearch(InternalConfig, false);
            }            
        }
        public static bool RunConsoleSearch(CampertronInternalConfig InternalConfig, bool ForceSearch)
        {            
            if (InternalConfig?.GeneralConfig?.OutputTo == OutputType.Email || ForceSearch == true)
            {
                RunningData RunData = Preload(InternalConfig);
                while (true)
                {
                    Parallel.ForEach(RunData.CampertronConfigFiles, ThisCampertronConfig =>
                    {
                        ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                        NewConfigItem.Name = ThisCampertronConfig.DisplayName;
                        DateTime Start = DateTime.UtcNow;
                        CampsiteConfig.WriteToConsole("Retrieving availability for campground ID:" + ThisCampertronConfig.CampgroundID, ConsoleColor.Magenta);
                        NewConfigItem.Values = AvailabilityApi.GetAvailabilitiesByCampground(ThisCampertronConfig, ref RunData, InternalConfig);
                        RunData.AllConsoleConfigItems.Add(NewConfigItem);
                        DateTime End = DateTime.UtcNow;
                        double TotalSeconds = (End - Start).TotalSeconds;
                        CampsiteConfig.WriteToConsole("Finished retrieving campground ID:" + ThisCampertronConfig.CampgroundID + " in " + TotalSeconds + " seconds", ConsoleColor.DarkMagenta);
                    });
                    PostProcess(InternalConfig, ref RunData);
                    if (InternalConfig?.GeneralConfig?.OutputTo == OutputType.UnitTest)
                    {
                        return true;
                    }
                }
            }
            else
            {
                Menu.Init(InternalConfig);
            }
            return false;
        }
        public static bool HasNewEntries(List<AvailableData> FilteredAvailableData, CampertronInternalConfig InternalConfig)
        {
            bool HasNewEntries = false;

            var CampHistoryPath = Path.Join(InternalConfig.ConfigPath, "Camp.History");
            if (!Directory.Exists(CampHistoryPath))
            {
                Directory.CreateDirectory(CampHistoryPath);
            }

            foreach (AvailableData availableData in FilteredAvailableData)
            {
                String NotificationPath = CampHistoryPath + '/' +
                                          availableData.HitDate.Year.ToString() + '/' +
                                          availableData.HitDate.Month.ToString() + '/' +
                                          availableData.HitDate.Day.ToString() + '/' +
                                          availableData.CampsiteID.ToString();
                if (!Directory.Exists(NotificationPath))
                {
                    HasNewEntries = true;
                    if (InternalConfig.GeneralConfig.OutputTo != OutputType.Console)
                    {
                        Directory.CreateDirectory(NotificationPath);
                    }
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
                ReturnConfig.GeneralConfig.AutoRefresh = false;
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
        public static bool PostProcess(CampertronInternalConfig config, ref RunningData RunData)
        {
            //place tracking for writing lines
            ConfigType LastConfigType = ConfigType.WriteLine;

            //determine if there are new entries
            bool NewEntries = HasNewEntries(new List<AvailableData>(RunData.FilteredAvailableData), config);

            //if output to email and there are new entries or not output to email process config
            if ((config.GeneralConfig.OutputTo == OutputType.Email &&
                NewEntries &&
                RunData.FilteredAvailableData.Count() > 0) || config.GeneralConfig.OutputTo != OutputType.Email)
            {
                foreach (ConsoleConfigItem ThisConsoleConfig in RunData.AllConsoleConfigItems.OrderBy(p => p.Name))
                {
                    CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType, config.GeneralConfig, config.EmailConfig);
                }
            }

            //clear data for next run
            RunData.Urls.Clear();
            RunData.AllConsoleConfigItems.Clear();
            RunData.SiteData.Clear();
            RunData.FilteredAvailableData.Clear();

            String ConfigPath = config.ConfigPath;
            if (config.GeneralConfig.OutputTo == OutputType.UnitTest)
            {
                return true;
            }
            else
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
                            RunConsoleSearch(config, false);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Press any key to continue...");
                            Console.ResetColor();
                            Console.CursorVisible = false;
                            Console.ReadKey();
                            Menu.Init(config);
                        }
                    }
                }
                else
                {
                    CampsiteConfig.WriteToConsole("\nSleeping for 1 minute", ConsoleColor.Magenta);
                    Thread.Sleep(60000);
                }
                Console.Write("\f\u001bc\x1b[3J");
                return false;
            }
        }
        public static bool DbExistsCheck(CampertronInternalConfig InternalConfig)
        {
            //if db doesn't exist extract blank db and populate
            if (!DbExists(InternalConfig))
            {
                ExtractEmbeddedDbTo(Path.Join(InternalConfig.ConfigPath, "RecreationDotOrg.db"));
            }
            else
            {
                //one-time repair: replace a db that no current version would produce (2024 swapped state/postal-code
                //columns, or a pre-0.1.0-beta.4 db that still has the omitted RIDB tables) with the bundled db
                sqlite.LegacyAddressLayout.RepairIfNeeded(InternalConfig.ConfigPath, InternalConfig.CachePath);
                //if db does exist see if it needs to be autorefreshed
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
        //decompresses the embedded Brotli-compressed RecreationDotOrg.db.br to DestinationDbFile (must not exist).
        //The data is written to a temporary file next to the destination and moved into place only when complete,
        //so an interrupted extraction never leaves a partial RecreationDotOrg.db behind.
        public static void ExtractEmbeddedDbTo(string DestinationDbFile)
        {
            string PartialFile = DestinationDbFile + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                var resource = embeddedProvider.GetFileInfo(@"content/RecreationDotOrg.db.br");
                if (!resource.Exists)
                {
                    throw new InvalidDataException("embedded resource content/RecreationDotOrg.db.br not found");
                }
                using (var reader = resource.CreateReadStream())
                using (var brotli = new BrotliStream(reader, CompressionMode.Decompress))
                using (var output = new FileStream(PartialFile, FileMode.CreateNew, FileAccess.Write))
                {
                    brotli.CopyTo(output);
                }
                File.Move(PartialFile, DestinationDbFile, false);
            }
            finally
            {
                if (File.Exists(PartialFile))
                {
                    File.Delete(PartialFile);
                }
            }
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