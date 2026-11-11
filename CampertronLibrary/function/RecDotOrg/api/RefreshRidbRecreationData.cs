using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    public static class RefreshRidbRecreationData
    {
        //returns false if the refreshed database fails validation (RidbDatabase.Validate)
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig)
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
            Clear.All(InternalConfig.ConfigPath);
            Console.WriteLine($"Populating Database from {ExtractDirectory}");
            PopulateDbFromRIDBFiles.Populate(ExtractDirectory, InternalConfig.ConfigPath);
            InternalConfig.GeneralConfig.LastRidbDataRefresh = DateTime.UtcNow;
            Yaml.GeneralConfigConvertToYaml(InternalConfig.GeneralConfig, "General", InternalConfig.ConfigPath);
            Console.WriteLine("Validating database");
            List<string> Problems = RidbDatabase.Validate(InternalConfig.ConfigPath);
            if (Problems.Count > 0)
            {
                CampsiteConfig.WriteToConsole("The refreshed RIDB database failed validation:", ConsoleColor.Red);
                foreach (string Problem in Problems)
                {
                    CampsiteConfig.WriteToConsole(" - " + Problem, ConsoleColor.Red);
                }
                if (!InternalConfig.ContainerMode &&
                    !Console.IsInputRedirected &&
                    (InternalConfig.GeneralConfig.OutputTo == OutputType.Console || InternalConfig.GeneralConfig.OutputTo == OutputType.HtmlFile))
                {
                    Console.WriteLine("Press any key to continue");
                    Console.ReadKey(true);
                }
                return false;
            }
            Console.Write("\f\u001bc\x1b[3J");
            return true;
        }
    }
}