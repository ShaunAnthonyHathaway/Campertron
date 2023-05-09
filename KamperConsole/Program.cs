using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KamperLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData();
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 1, "GROUP", null);//Zion
        //KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232489", 1, "GROUP", null);//North Rim
        //var folder = Environment.SpecialFolder.LocalApplicationData;
        //var path = Environment.GetFolderPath(folder);
        //Console.WriteLine(System.IO.Path.Join(path, "RecreationDotOrg.db"));

        //Console.WriteLine(path);
        //Console.WriteLine(folder);

        Console.WriteLine("Press enter to terminate...");
        Console.ReadLine();
    }
}