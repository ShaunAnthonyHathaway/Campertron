using System.Runtime.ExceptionServices;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.api
{
    // The console's RIDB refresh (the menu's "Refresh Ridb Data" and the start's automatic refresh): an adapter of the
    // host-neutral RidbRefreshService (V011-1, decision D61). It writes the service's lines to standard output and its
    // messages through ConsoleUi exactly as the refresh did before the service, waits for a key only in an interactive
    // console, and lets a failure end the program as before: the service's exception is rethrown unchanged, so
    // Program.Main still reports it ("a download failed: …", exit code 1).
    public static class RefreshRidbRecreationData
    {
        //returns false if another refresh is running, if the refreshed database fails validation (RidbDatabase.Validate),
        //or if quick_check fails after the installation. Only a successful installation changes the installed database,
        //the derived cache (CachePath) and LastRidbDataRefresh (RidbStagedRefresh, business-rules.md §8)
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig)
        {
            return RefreshData(DisplayNoDataMsg, InternalConfig, new RidbRefreshOptions());
        }

        //ObtainExtractDirectory downloads and extracts the RIDB export and returns the extract directory.
        //The derived cache the refresh clears is InternalConfig.CachePath (TD-05); a missing, root or config
        //cache path throws here, before anything is downloaded or changed
        public static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig, Func<string> ObtainExtractDirectory)
        {
            return RefreshData(DisplayNoDataMsg, InternalConfig, new RidbRefreshOptions { ObtainExtractDirectory = _ => ObtainExtractDirectory() });
        }

        internal static bool RefreshData(bool DisplayNoDataMsg, CampertronInternalConfig InternalConfig, RidbRefreshOptions Options)
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
            var ConsoleOptions = new RidbRefreshOptions
            {
                ExportUri = Options.ExportUri,
                Handler = Options.Handler,
                HeaderTimeout = Options.HeaderTimeout,
                StallTimeout = Options.StallTimeout,
                WorkRoot = Options.WorkRoot,
                ObtainExtractDirectory = Options.ObtainExtractDirectory,
                UtcNow = Options.UtcNow,
                Time = Options.Time,
                //the progress lines, on standard output as they happen
                Log = Line => Console.WriteLine(Line),
            };
            RidbRefreshResult Result = RidbRefreshService.Run(new RidbRefreshRequest(ConfigPath, CacheDirectory, InternalConfig.ContainerMode), null, CancellationToken.None, ConsoleOptions);
            //the in-memory value follows the installed data, whether or not General.yaml could be updated
            if (Result.Installed && Result.RefreshedAt.HasValue)
            {
                General.LastRidbDataRefresh = Result.RefreshedAt.Value;
            }
            switch (Result.Outcome)
            {
                case RidbRefreshOutcome.Refreshed:
                    //kept on the screen until a key is pressed (the menu clears the screen only after that)
                    ConsoleUi.Notice("The campground data was refreshed.");
                    return true;
                case RidbRefreshOutcome.RefreshedWithoutTimestamp:
                    ConsoleUi.Warning($"The RIDB database refresh succeeded, but General.yaml could not be updated ({Result.Detail}). The next start may refresh again.");
                    ConsoleUi.Notice("The campground data was refreshed.");
                    return true;
                case RidbRefreshOutcome.AnotherRefreshRunning:
                    ConsoleUi.Error($"Another RIDB refresh is running ({Result.Detail}). Nothing was changed.");
                    WaitForKeyIfInteractive(InternalConfig, General);
                    return false;
                case RidbRefreshOutcome.ValidationFailed:
                case RidbRefreshOutcome.InstalledButCheckFailed:
                    //one error on standard error: the reason, each problem, and what happened to the installed database
                    List<string> Report = new List<string>();
                    if (Result.Outcome == RidbRefreshOutcome.ValidationFailed)
                    {
                        Report.Add("The refreshed RIDB database failed validation:");
                    }
                    foreach (string Problem in Result.Problems)
                    {
                        Report.Add(" - " + Problem);
                    }
                    if (Result.Outcome == RidbRefreshOutcome.ValidationFailed)
                    {
                        Report.Add("The installed database was not changed.");
                    }
                    if (Report.Count > 0)
                    {
                        ConsoleUi.Error(Report[0], Report.Skip(1).ToArray());
                    }
                    WaitForKeyIfInteractive(InternalConfig, General);
                    return false;
                default:
                    //a failure the refresh used to throw: the same exception, so Program.Main reports it as before
                    ExceptionDispatchInfo.Throw(Result.Error ?? new InvalidOperationException("The RIDB refresh ended with " + Result.Outcome + "."));
                    return false;
            }
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
    }
}
