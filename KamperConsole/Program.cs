using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KamperLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData();
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 1, "GROUP", null);//Zion
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232489", 1, "GROUP", null);//North Rim

        Console.WriteLine("Press enter to terminate...");
        Console.ReadLine();
    }
}