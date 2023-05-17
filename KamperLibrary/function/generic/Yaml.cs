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
            TextWriter TW = new StreamWriter($@"C:\Users\Shaun\Desktop\{FileName}.yaml");
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
            foreach(String ConfigFile in Directory.GetFiles(configpath, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                KamperConfig ThisConfigFile = ConvertFromYaml(ConfigFile);
                if (ThisConfigFile.AutoRun)
                {
                    ThisConfigFile.GenerateSearchData();
                    ReturnConfigLst.Add(ThisConfigFile);
                }
            }
            return ReturnConfigLst; 
        }
    }
}
