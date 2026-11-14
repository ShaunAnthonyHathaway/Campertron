using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    public static class RefreshRidbRecreationData
    {
        //returns false if another refresh is running, if the refreshed database fails validation (RidbDatabase.Validate),
        //or if quick_check fails after the installation. Only a successful installation changes the installed database,
        //the derived cache (CachePath) and LastRidbDataRefresh (RidbStagedRefresh, business-rules.md §8)
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig)
        {
            return RefreshData(DisplayNoDataMsg, InternalConfig, DownloadAndExtract);
        }

        //ObtainExtractDirectory downloads and extracts the RIDB export and returns the extract directory.
        //The derived cache the refresh clears is InternalConfig.CachePath (TD-05); a missing, root or config
        //cache path throws here, before anything is downloaded or changed
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig, Func<string> ObtainExtractDirectory)
        {
            //the screen is cleared by the menu before a manual refresh, never here: an automatic refresh at the start of
            //the unattended loop writes into a log
            if (DisplayNoDataMsg)
            {
                Console.WriteLine("No database found, please wait while it is downloaded");
            }
            string ConfigPath = InternalConfig.ConfigPath ?? throw new InvalidOperationException("ConfigPath is not set");
            string CacheDirectory = Clear.CheckCacheDirectory(InternalConfig.CachePath, ConfigPath);
            GeneralConfig General = InternalConfig.GeneralConfig ?? throw new InvalidOperationException("GeneralConfig is not set");
            RidbStagedRefresh.Outcome Result;
            List<string> Problems = new List<string>();
            using (FileStream? RefreshLock = RidbStagedRefresh.TryAcquireRefreshLock(ConfigPath, out string LockProblem))
            {
                if (RefreshLock == null)
                {
                    ConsoleUi.Error($"Another RIDB refresh is running ({LockProblem}). Nothing was changed.");
                    WaitForKeyIfInteractive(InternalConfig, General);
                    return false;
                }
                RidbStagedRefresh.RemoveStaleStagingDirectories(ConfigPath);
                ConfigFileReplace.RemoveStaleTemporaryFiles(ConfigPath, "General.yaml");
                string ExtractDirectory = ObtainExtractDirectory();
                Result = RidbStagedRefresh.BuildValidateAndInstall(ExtractDirectory, ConfigPath, CacheDirectory, Problems);
                if (Result == RidbStagedRefresh.Outcome.Installed)
                {
                    //written once, through a validated temporary file, so an interrupted write can't leave General.yaml
                    //empty or partial (TD-37). The in-memory value is updated either way: the installed data is new
                    General.LastRidbDataRefresh = DateTime.UtcNow;
                    try
                    {
                        Yaml.GeneralConfigReplaceYaml(General, ConfigPath);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is YamlDotNet.Core.YamlException)
                    {
                        ConsoleUi.Warning($"The RIDB database refresh succeeded, but General.yaml could not be updated ({ex.Message}). The next start may refresh again.");
                    }
                }
            }
            if (Result != RidbStagedRefresh.Outcome.Installed)
            {
                //one error on standard error: the reason, each problem, and what happened to the installed database
                List<string> Report = new List<string>();
                if (Result == RidbStagedRefresh.Outcome.ValidationFailed)
                {
                    Report.Add("The refreshed RIDB database failed validation:");
                }
                foreach (string Problem in Problems)
                {
                    Report.Add(" - " + Problem);
                }
                if (Result == RidbStagedRefresh.Outcome.ValidationFailed)
                {
                    Report.Add("The installed database was not changed.");
                }
                if (Report.Count > 0)
                {
                    ConsoleUi.Error(Report[0], Report.Skip(1).ToArray());
                }
                WaitForKeyIfInteractive(InternalConfig, General);
                return false;
            }
            //kept on the screen until a key is pressed (the menu clears the screen only after that)
            ConsoleUi.Notice("The campground data was refreshed.");
            return true;
        }

        //waits for a key only in an interactive console, so container, email and unit-test runs never block
        private static void WaitForKeyIfInteractive(CampertronInternalConfig InternalConfig, GeneralConfig General)
        {
            if (!InternalConfig.ContainerMode &&
                !Console.IsInputRedirected &&
                (General.OutputTo == OutputType.Console || General.OutputTo == OutputType.HtmlFile))
            {
                ConsoleUi.WaitForKey("Press any key to continue...");
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