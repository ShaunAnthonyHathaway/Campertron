using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.FileProviders;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KamperConfig ZionConfig = new KamperConfig();
        //ZionConfig.AutoRun = true;
        //ZionConfig.CampgroundID = "232445";
        //ZionConfig.TotalHumans = 1;
        //ZionConfig.FilterOutByCampsiteType = "GROUP";
        //ZionConfig.FilterInByCampsiteType = null;
        //ZionConfig.SearchBy = SearchTypes.Until;
        //ZionConfig.SearchValue = 0;
        //ZionConfig.SearchValueDates = new List<String>() { "06/30/2023" };
        //ZionConfig.ShowMonday = true;
        //ZionConfig.ShowTuesday = true;
        //ZionConfig.ShowWednesday = true;
        //ZionConfig.ShowThursday = true;
        //ZionConfig.ShowFriday = true;
        //ZionConfig.ShowSaturday = true;
        //ZionConfig.ShowSunday = true;
        //ZionConfig.GenerateSearchData();

        //KamperLibrary.function.generic.Yaml.ConvertToYaml(ZionConfig, "ZionConfig");

        //KamperConfig NorthRimConfig = new KamperConfig();
        //NorthRimConfig.AutoRun = true;
        //NorthRimConfig.CampgroundID = "232489";
        //NorthRimConfig.TotalHumans = 1;
        //NorthRimConfig.FilterOutByCampsiteType = "GROUP";
        //NorthRimConfig.FilterInByCampsiteType = null;
        //NorthRimConfig.SearchBy = SearchTypes.Until;
        //NorthRimConfig.SearchValue = 0;
        //NorthRimConfig.SearchValueDates = new List<String>() { "06/30/2023" };
        //NorthRimConfig.ShowMonday = true;
        //NorthRimConfig.ShowTuesday = true;
        //NorthRimConfig.ShowWednesday = true;
        //NorthRimConfig.ShowThursday = true;
        //NorthRimConfig.ShowFriday = true;
        //NorthRimConfig.ShowSaturday = true;
        //NorthRimConfig.ShowSunday = true;
        //NorthRimConfig.GenerateSearchData();

        //KamperLibrary.function.generic.Yaml.ConvertToYaml(NorthRimConfig, "NorthRimConfig");

        //KamperConfig ZionConfig = KamperLibrary.function.generic.Yaml.ConvertFromYaml(@"C:\Users\Shaun\Desktop\ZionConfig.yaml");
        //ZionConfig.GenerateSearchData();
        //KamperConfig NorthRimConfig = KamperLibrary.function.generic.Yaml.ConvertFromYaml(@"C:\Users\Shaun\Desktop\NorthRimConfig.yaml");
        //NorthRimConfig.GenerateSearchData();
        function.generic.Load.Init();
        List <KamperConfig> KamperConfigFiles = KamperLibrary.function.generic.Yaml.GetConfigs();
        while (true)
        {
            foreach(KamperConfig ThisConfig in KamperConfigFiles)
            {
                KamperLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground(ThisConfig);
            }
            function.generic.Load.NextStep();
        }
    }
}