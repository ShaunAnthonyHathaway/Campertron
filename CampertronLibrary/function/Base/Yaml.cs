using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CampertronLibrary.function.Base
{
    public static class Yaml
    {
        public static void EmailConfigConvertToYaml(EmailConfig IncomingConfig, String FileName)
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
        public static EmailConfig EmailConfigConvertFromYaml(String Filepath)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

            return deserializer.Deserialize<EmailConfig>(File.ReadAllText(Filepath));
        }
        public static void GeneralConfigConvertToYaml(GeneralConfig IncomingConfig, String FileName)
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
        public static GeneralConfig GeneralConfigConvertFromYaml(String Filepath)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

            return deserializer.Deserialize<GeneralConfig>(File.ReadAllText(Filepath));
        }
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
        public static EmailConfig EmailConfigGetConfig(string configpath)
        {
            EmailConfig ReturnConfig = new EmailConfig();;
            String DefaultEmailConfigFile = System.IO.Path.Join(configpath, "Email.yaml");
            if (File.Exists(DefaultEmailConfigFile))
            {
                ReturnConfig = EmailConfigConvertFromYaml(DefaultEmailConfigFile);
            }
            else
            {
                ReturnConfig = EmailConfigGenerateSampleConfig();
            }

            return ReturnConfig;
        }
        public static GeneralConfig GeneralConfigGetConfig(string configpath)
        {
            GeneralConfig ReturnConfig = new GeneralConfig();
            String DefaultGeneralConfigFile = System.IO.Path.Join(configpath, "General.yaml");
            if (File.Exists(DefaultGeneralConfigFile))
            {
                ReturnConfig = GeneralConfigConvertFromYaml(DefaultGeneralConfigFile);
            }
            else
            {
                ReturnConfig = GeneralConfigGenerateSampleConfig();
            }

            return ReturnConfig;
        }
        public static List<CampertronConfig> CampertronConfigGetConfigs(string configpath)
        {
            List<CampertronConfig> ReturnConfigLst = new List<CampertronConfig>();
            bool FoundValidConfig = false;
            foreach (String ConfigFile in Directory.GetFiles(configpath, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                String ThisLowerConfigFile = ConfigFile.ToLower().Trim();
                FileInfo Fi = new FileInfo(ThisLowerConfigFile);
                if (Fi.Name != "general.yaml" && Fi.Name != "email.yaml")
                {
                    CampertronConfig ThisConfigFile = CampertronConfigConvertFromYaml(ConfigFile);
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
            ZionConfig.IncludeAttributes = new List<string>() { "Shade:Yes", "Campfire Allowed:Yes" };
            ZionConfig.ExcludeAttributes = new List<string>();
            ZionConfig.IncludeCampsiteType = new List<string>();
            ZionConfig.ExcludeCampsiteType = new List<string>() { "GROUP" };
            CampertronConfigConvertToYaml(ZionConfig, "ZionConfig");
            return ZionConfig;
        }
        public static GeneralConfig GeneralConfigGenerateSampleConfig()
        {
            GeneralConfig config = new GeneralConfig();

            config.LastRidbDataRefresh = DateTime.UtcNow.AddYears(-50);
            config.RefreshRidbDataDayInterval = 30;
            config.OutputTo = OutputType.Console;
            config.AutoRefresh = false;
            GeneralConfigConvertToYaml(config, "General");
            return config;
        }
        public static EmailConfig EmailConfigGenerateSampleConfig()
        {
            EmailConfig config = new EmailConfig();

            config.SendFromAddress = String.Empty;
            config.SmtpPort = 0;
            config.SmtpUsername = String.Empty;
            config.SmtpPassword = String.Empty;
            config.SendToAddressList = new List<string>();
            config.SmtpServer = String.Empty;
            EmailConfigConvertToYaml(config, "Email");
            return config; 
        }
    }
}
