using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KamperLibrary.function.RecDotOrg.RefreshRidbRecreationData.RefreshData();
        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 2, "GROUP", null);
        //KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232490", 0, "GROUP", null);

        Console.WriteLine("Press enter to terminate...");
        Console.ReadLine();
    }
}