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
        KamperConfig ZionConfig = new KamperConfig();
        ZionConfig.AutoRun = true;
        ZionConfig.CampgroundID = "232445";
        ZionConfig.TotalHumans = 1;
        ZionConfig.FilterOutByCampsiteType = "GROUP";
        ZionConfig.FilterInByCampsiteType = null;
        ZionConfig.SearchBy = SearchTypes.Until;
        ZionConfig.SearchValue = 0;
        ZionConfig.SearchValueDates = new List<String>() { "06/30/2023" };
        ZionConfig.GenerateSearchData();

        KamperConfig NorthRimConfig = new KamperConfig();
        NorthRimConfig.AutoRun = true;
        NorthRimConfig.CampgroundID = "232489";
        NorthRimConfig.TotalHumans = 1;
        NorthRimConfig.FilterOutByCampsiteType = "GROUP";
        NorthRimConfig.FilterInByCampsiteType = null;
        NorthRimConfig.SearchBy = SearchTypes.Until;
        NorthRimConfig.SearchValue = 0;
        NorthRimConfig.SearchValueDates = new List<String>() { "06/30/2023" };
        NorthRimConfig.GenerateSearchData();

        function.generic.Load.Init();
        while (true)
        {
            KamperLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground(ZionConfig);//Zion
            KamperLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground(NorthRimConfig);//North Rim
            function.generic.Load.NextStep();
        }
    }
}