using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    public static class RefreshRidbRecreationData
    {
        public static void RefreshData(bool DisplayNoDataMsg)
        {
            Console.Write("\f\u001bc\x1b[3J");
            if (DisplayNoDataMsg)
            {
                Console.WriteLine("No database found, please wait while it is downloaded");
            }
            Console.WriteLine($"Downloading RIDBFullExport_V1_JSON.zip");
            string DestinationFile = function.Base.Web.Download("https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip", "RIDBFullExport_V1_JSON.zip").Result;
            Console.WriteLine($"Extracting {DestinationFile}");
            string ExtractDirectory = function.Base.Compression.Unzip(DestinationFile);
            Console.WriteLine("Clearing Database");
            Clear.All();
            Console.WriteLine($"Populating Database from {ExtractDirectory}");
            PopulateDbFromRIDBFiles.Populate(ExtractDirectory);
            Console.WriteLine("Refresh complete...press any key to continue");
            Console.ReadKey();
            Console.Write("\f\u001bc\x1b[3J");
        }
    }
}