using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CampertronLibrary.function.Base
{
    public static class Yaml
    {
        public static void CampertronConfigConvertToYaml(CampertronConfig IncomingConfig, String FileName)
        {
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var stringResult = serializer.Serialize(IncomingConfig);
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var configpath = System.IO.Path.Join(path, "CampertronConfig");
            var configfilepath = System.IO.Path.Join(configpath, $"{FileName}.yaml");
            TextWriter TW = new StreamWriter(configfilepath);
            TW.Write(stringResult);
            TW.Close();
        }
        public static CampertronConfig CampertronConfigConvertFromYaml(String Filepath)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

            return deserializer.Deserialize<CampertronConfig>(File.ReadAllText(Filepath));
        }
        public static List<CampertronConfig> CampertronConfigGetConfigs()
        {
            List<CampertronConfig> ReturnConfigLst = new List<CampertronConfig>();
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var configpath = System.IO.Path.Join(path, "CampertronConfig");
            bool FoundValidConfig = false;
            foreach (String ConfigFile in Directory.GetFiles(configpath, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                CampertronConfig ThisConfigFile = CampertronConfigConvertFromYaml(ConfigFile);
                if (ThisConfigFile.AutoRun)
                {
                    FoundValidConfig = true;
                    ThisConfigFile.GenerateSearchData();
                    ReturnConfigLst.Add(ThisConfigFile);
                }
            }
            foreach (String ConfigFile in Directory.GetFiles(Environment.CurrentDirectory, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                CampertronConfig ThisConfigFile = CampertronConfigConvertFromYaml(ConfigFile);
                if (ThisConfigFile.AutoRun)
                {
                    FoundValidConfig = true;
                    ThisConfigFile.GenerateSearchData();
                    ReturnConfigLst.Add(ThisConfigFile);
                }
            }
            if (!FoundValidConfig)
            {
                String ConfigFilePath = System.IO.Path.Join(configpath, "ZionConfig.yaml");
                Console.WriteLine($"No config files found, generating sample at {ConfigFilePath}");
                CampertronConfig SampleConfig = CampertronConfigGenerateSampleConfig();
                SampleConfig.GenerateSearchData();
                ReturnConfigLst.Add(SampleConfig);
            }
            return ReturnConfigLst;
        }
        public static CampertronConfig CampertronConfigGenerateSampleConfig()
        {
            CampertronConfig ZionConfig = new CampertronConfig();
            ZionConfig.DisplayName = "Zion non-group tent friendly sites";
            ZionConfig.AutoRun = true;
            ZionConfig.CampgroundID = "232445";
            ZionConfig.TotalHumans = 2;
            ZionConfig.FilterOutByCampsiteType = new List<string>() { "GROUP" };
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
            ZionConfig.IncludeEquipment = new List<string>() { "Tent", "SMALL TENT", "LARGE TENT OVER 9X12`" };
            ZionConfig.ExcludeEquipment = new List<string>();
            ZionConfig.IncludeSites = new List<string>();
            ZionConfig.ExcludeSites = new List<string>();
            ZionConfig.ConsecutiveDays = 1;
            ZionConfig.ConsecutiveFilter = ConsecutiveFilter.ByCampsite;
            CampertronLibrary.function.Base.Yaml.CampertronConfigConvertToYaml(ZionConfig, "ZionConfig");
            return ZionConfig;
        }
    }
}
