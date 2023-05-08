using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KampLibrary.function.sqlite.Clear.All();
        //KampLibrary.function.sqlite.PopulateDbFromFile.Populate(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\");

        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232445", 2,  "GROUP", null);
        //KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByCampground("232490", 0, "GROUP", null);        

        Console.WriteLine("Press enter to terminate...");
        Console.ReadLine();
    }
}