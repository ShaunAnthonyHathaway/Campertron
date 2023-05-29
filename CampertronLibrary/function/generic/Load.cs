using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace CampertronLibrary.function.generic
{
    public static class Load
    {
        public static void Init()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "⛺ C 🌵 A ⛺ M 🌵 P ⛺";

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
            while (true)
            {
                foreach (CampertronConfig ThisConfig in CampertronConfigFiles)
                {
                    CampertronLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground(ThisConfig);
                }
                NextStep();
            }
        }
        public static void NextStep()
        {
            Console.WriteLine("Press enter to search again or type refresh and hit enter to refresh RIDB Recreation Data");
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