using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    public static class RefreshRidbRecreationData
    {
        //returns false if another refresh is running, if the refreshed database fails validation (RidbDatabase.Validate),
        //or if quick_check fails after the installation. Only a successful installation changes the installed database,
        //the derived cache and LastRidbDataRefresh (RidbStagedRefresh, business-rules.md §8)
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig)
        {
            return RefreshData(DisplayNoDataMsg, InternalConfig, DownloadAndExtract, Clear.RefreshCachePath());
        }

        //ObtainExtractDirectory downloads and extracts the RIDB export and returns the extract directory;
        //CacheDirectory is the cache directory the refresh clears
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig, Func<string> ObtainExtractDirectory, string CacheDirectory)
        {
            Console.Write("\f\u001bc\x1b[3J");
            if (DisplayNoDataMsg)
            {
                Console.WriteLine("No database found, please wait while it is downloaded");
            }
            string ConfigPath = InternalConfig.ConfigPath ?? throw new InvalidOperationException("ConfigPath is not set");
            GeneralConfig General = InternalConfig.GeneralConfig ?? throw new InvalidOperationException("GeneralConfig is not set");
            RidbStagedRefresh.Outcome Result;
            List<string> Problems = new List<string>();
            using (FileStream? RefreshLock = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out string LockProblem))
            {
                if (RefreshLock == null)
                {
                    CampsiteConfig.WriteToConsole($"Another RIDB refresh is running ({LockProblem}). Nothing was changed.", ConsoleColor.Red);
                    WaitForKeyIfInteractive(InternalConfig, General);
                    return false;
                }
                RidbStagedRefresh.RemoveStaleStagingDirectories(ConfigPath);
                string ExtractDirectory = ObtainExtractDirectory();
                Result = RidbStagedRefresh.BuildValidateAndInstall(ExtractDirectory, ConfigPath, CacheDirectory, Problems);
                if (Result == RidbStagedRefresh.Outcome.Installed)
                {
                    General.LastRidbDataRefresh = DateTime.UtcNow;
                    try
                    {
                        Yaml.GeneralConfigConvertToYaml(General, "General", ConfigPath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        CampsiteConfig.WriteToConsole($"The RIDB database refresh succeeded, but General.yaml could not be updated ({ex.Message}). The next start may refresh again.", ConsoleColor.Yellow);
                    }
                }
            }
            if (Result != RidbStagedRefresh.Outcome.Installed)
            {
                if (Result == RidbStagedRefresh.Outcome.ValidationFailed)
                {
                    CampsiteConfig.WriteToConsole("The refreshed RIDB database failed validation:", ConsoleColor.Red);
                }
                foreach (string Problem in Problems)
                {
                    CampsiteConfig.WriteToConsole(" - " + Problem, ConsoleColor.Red);
                }
                if (Result == RidbStagedRefresh.Outcome.ValidationFailed)
                {
                    CampsiteConfig.WriteToConsole("The installed database was not changed.", ConsoleColor.Red);
                }
                WaitForKeyIfInteractive(InternalConfig, General);
                return false;
            }
            Console.Write("\f\u001bc\x1b[3J");
            return true;
        }

        //waits for a key only in an interactive console, so container, email and unit-test runs never block
        private static void WaitForKeyIfInteractive(CampertronInternalConfig InternalConfig, GeneralConfig General)
        {
            if (!InternalConfig.ContainerMode &&
                !Console.IsInputRedirected &&
                (General.OutputTo == OutputType.Console || General.OutputTo == OutputType.HtmlFile))
            {
                Console.WriteLine("Press any key to continue");
                Console.ReadKey(true);
            }
        }

        private static string DownloadAndExtract()
        {
            Console.WriteLine($"Downloading RIDBFullExport_V1_JSON.zip");
            string DestinationFile = function.Base.Web.Download("https://ridb.recreation.gov/downloads/RIDBFullExport_V1_JSON.zip", "RIDBFullExport_V1_JSON.zip").Result;
            Console.WriteLine($"Extracting {DestinationFile}");
            return function.Base.Compression.Unzip(DestinationFile);
        }
    }
}