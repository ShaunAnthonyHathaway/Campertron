using System.Text.Json;

class Program
{
    static void Main(string[] args)
    {
        //KampLibrary.function.sqlite.Clear.All();
        //KampLibrary.function.sqlite.PopulateDbFromFile.Populate(@"C:\Users\Shaun\Desktop\RIDBFullExport_V1_JSON\");

        KampLibrary.function.RecDotOrg.AvailabilityApi.GetAvailabilitiesByPark("232445");//, 2, new List<string>() { "GROUP", "STANDARD NONELECTRIC" });

        Console.WriteLine("Done");
        Console.ReadLine();
    }
}