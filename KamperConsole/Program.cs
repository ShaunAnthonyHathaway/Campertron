using System.Globalization;
using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        KamperLibrary.function.generic.Load.Init();
        //KamperLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData();
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 1, null, null);//Zion
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232489", 1, null, null);//North Rim
        //var folder = Environment.SpecialFolder.LocalApplicationData;
        //var path = Environment.GetFolderPath(folder);
        //Console.WriteLine(System.IO.Path.Join(path, "RecreationDotOrg.db"));

        //Console.WriteLine(path);
        //Console.WriteLine(folder);
        KamperLibrary.function.generic.Load.Fin();
    }
}