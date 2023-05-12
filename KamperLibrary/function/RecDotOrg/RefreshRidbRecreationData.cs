using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KamperLibrary.function.RecDotOrg
{
    public static class RefreshRidbRecreationData
    {
        public static void RefreshData(bool DisplayNoDataMsg)
        {
            Console.Write("\f\u001bc\x1b[3J");
            if(DisplayNoDataMsg)
            {
                Console.WriteLine("No database found, please wait while it is downloaded");
            }
            Console.WriteLine($"Downloading RIDBFullExport_V1_JSON.zip");
            String DestinationFile = (KamperLibrary.function.generic.Web.Download("https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip", "RIDBFullExport_V1_JSON.zip")).Result;
            Console.WriteLine($"Extracting {DestinationFile}");
            String ExtractDirectory = KamperLibrary.function.generic.Compression.Unzip(DestinationFile);
            Console.WriteLine("Clearing Database");
            KampLibrary.function.sqlite.Clear.All();
            Console.WriteLine($"Populating Database from {ExtractDirectory}");
            KampLibrary.function.sqlite.PopulateDbFromFile.Populate(ExtractDirectory);
            Console.WriteLine("Refresh complete...press any key to continue");
            Console.ReadKey();
            Console.Write("\f\u001bc\x1b[3J");
        }
    }
}
