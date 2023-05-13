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
        function.generic.Load.Init();
        while (true)
        {
            KamperLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 1, 1, "GROUP", null);//Zion
            KamperLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232489", 1, 1, "GROUP", null);//North Rim
            function.generic.Load.NextStep();
        }
    }
}