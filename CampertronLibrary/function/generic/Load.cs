using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static ConsoleConfig;

namespace CampertronLibrary.function.generic
{
    public static class Load
    {
        public static void Init()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "🤖 CAMPERTRON 🤖";

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "CampertronCache");
            if (Directory.Exists(cachepath) == false)
            {
                Directory.CreateDirectory(cachepath);
            }

            var configpath = System.IO.Path.Join(path, "CampertronConfig");
            if (Directory.Exists(configpath) == false)
            {
                Directory.CreateDirectory(configpath);
            }

            DbExistsCheck();

            List<CampertronConfig> CampertronConfigFiles = CampertronLibrary.function.generic.Yaml.GetConfigs();
            ConcurrentBag<ConsoleConfig.ConsoleConfigItem> AllConsoleConfigItems = new ConcurrentBag<ConsoleConfig.ConsoleConfigItem>();
            Console.Write("\f\u001bc\x1b[3J");
            while (true)
            {                                
                Parallel.ForEach(CampertronConfigFiles, ThisConfig =>
                {
                    ConsoleConfig.ConsoleConfigItem NewConfigItem = new ConsoleConfigItem();
                    NewConfigItem.Name = ThisConfig.DisplayName;
                    DateTime Start = DateTime.UtcNow;
                    CampsiteConfig.WriteToConsole("Retrieving availability for campground ID:" + ThisConfig.CampgroundID + " on thread:" + Task.CurrentId, ConsoleColor.Magenta);
                    NewConfigItem.Values = CampertronLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground(ThisConfig);
                    AllConsoleConfigItems.Add(NewConfigItem);
                    DateTime End = DateTime.UtcNow;
                    Double TotalSeconds = (End - Start).TotalSeconds;
                    CampsiteConfig.WriteToConsole("Finished retrieving campground ID:" + ThisConfig.CampgroundID + " in " + TotalSeconds + " seconds", ConsoleColor.DarkMagenta);
                });
                ConsoleConfig.ConfigType LastConfigType = ConsoleConfig.ConfigType.WriteLine;

                foreach (ConsoleConfig.ConsoleConfigItem ThisConsoleConfig in AllConsoleConfigItems.OrderBy(p => p.Name))
                {
                    CampsiteConfig.ProcessConsoleConfig(ThisConsoleConfig.Values, ref LastConfigType);
                }

                AllConsoleConfigItems.Clear();
                NextStep();
            }
        }
        public static void NextStep()
        {
            CampsiteConfig.WriteToConsole("\nPress enter to search again or type refresh and hit enter to refresh RIDB Recreation Data", ConsoleColor.Magenta);
            String ReceivedKeys = Console.ReadLine();
            if (ReceivedKeys != null)
            {
                if (ReceivedKeys.ToUpper().Trim() == "REFRESH")
                {
                    CampertronLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData(false);
                }
            }
            Console.Write("\f\u001bc\x1b[3J");
        }
        private static void DbExistsCheck()
        {
            if (!DbExists())
            {
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);
                String DbFile = System.IO.Path.Join(path, "RecreationDotOrg.db");
                var embeddedProvider = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
                using (var reader = embeddedProvider.GetFileInfo(@"content\RecreationDotOrg.db").CreateReadStream())
                {
                    using (Stream s = File.Create(DbFile))
                    {
                        reader.CopyTo(s);
                    }
                }
                CampertronLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData(true);
            }
        }
        private static bool DbExists()
        {
            bool DbExists = false;
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String DbFile = System.IO.Path.Join(path, "RecreationDotOrg.db");
            if (File.Exists(DbFile))
            {
                DbExists = true;
            }
            return DbExists;
        }
    }
}