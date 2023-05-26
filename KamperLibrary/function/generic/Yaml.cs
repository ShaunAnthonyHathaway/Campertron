using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KamperLibrary.function.generic
{
    public static class Yaml
    {
        public static void ConvertToYaml(KamperConfig IncomingConfig, String FileName)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var stringResult = serializer.Serialize(IncomingConfig);
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var configpath = System.IO.Path.Join(path, "KamperConfig");
            var configfilepath = System.IO.Path.Join(configpath, $"{FileName}.yaml");
            TextWriter TW = new StreamWriter(configfilepath);
            TW.Write(stringResult);
            TW.Close();
        }
        public static KamperConfig ConvertFromYaml(String Filepath)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

            return deserializer.Deserialize<KamperConfig>(File.ReadAllText(Filepath));
        }
        public static List<KamperConfig> GetConfigs()
        {
            List<KamperConfig> ReturnConfigLst = new List<KamperConfig>();
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var configpath = System.IO.Path.Join(path, "KamperConfig");
            bool FoundValidConfig = false;
            foreach (String ConfigFile in Directory.GetFiles(configpath, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                KamperConfig ThisConfigFile = ConvertFromYaml(ConfigFile);
                if (ThisConfigFile.AutoRun)
                {
                    FoundValidConfig = true;
                    ThisConfigFile.GenerateSearchData();
                    ReturnConfigLst.Add(ThisConfigFile);
                }
            }
            if (!FoundValidConfig)
            {
                foreach (String ConfigFile in Directory.GetFiles(Environment.CurrentDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
                {
                    KamperConfig ThisConfigFile = ConvertFromYaml(ConfigFile);
                    if (ThisConfigFile.AutoRun)
                    {
                        FoundValidConfig = true;
                        ThisConfigFile.GenerateSearchData();
                        ReturnConfigLst.Add(ThisConfigFile);
                    }
                }
            }
            if (!FoundValidConfig)
            {
                String ConfigFilePath = System.IO.Path.Join(configpath, "ZionConfig.yaml");
                Console.WriteLine($"No config files found, generating sample at {ConfigFilePath}");
                KamperConfig SampleConfig = GenerateSampleConfig();
                SampleConfig.GenerateSearchData();
                ReturnConfigLst.Add(SampleConfig);
            }
            return ReturnConfigLst;
        }
        public static KamperConfig GenerateSampleConfig()
        {
            KamperConfig ZionConfig = new KamperConfig();
            ZionConfig.AutoRun = true;
            ZionConfig.CampgroundID = "232445";
            ZionConfig.TotalHumans = 2;
            ZionConfig.FilterOutByCampsiteType = "GROUP";
            ZionConfig.FilterInByCampsiteType = null;
            ZionConfig.SearchBy = SearchTypes.DaysOut;
            ZionConfig.SearchValue = 7;
            ZionConfig.SearchValueDates = new List<String>() { };
            ZionConfig.ShowMonday = true;
            ZionConfig.ShowTuesday = true;
            ZionConfig.ShowWednesday = true;
            ZionConfig.ShowThursday = true;
            ZionConfig.ShowFriday = true;
            ZionConfig.ShowSaturday = true;
            ZionConfig.ShowSunday = true;
            KamperLibrary.function.generic.Yaml.ConvertToYaml(ZionConfig, "ZionConfig");
            return ZionConfig;
        }
    }
}
