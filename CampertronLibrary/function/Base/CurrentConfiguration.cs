using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using YamlDotNet.Core;

namespace CampertronLibrary.function.Base
{
    // Campertron's current configuration (decision D60, V011-2): the eight items of the console's View Config, built here
    // once for both hosts, so neither formats them on its own. The console's View Config (Menu.ViewConfigLines) writes
    // Describe's items as its lines, byte for byte as before D60; the GUI's Settings > System > Current configuration
    // shows Read's items and copies their text. Nothing here writes, creates or deletes a file, opens Email.yaml (the
    // settings folder is only listed, and Email.yaml is told apart by its name), reads an environment variable, opens the
    // database or makes a network request.
    public static class CurrentConfiguration
    {
        // the value the GUI shows for what it could not read: General.yaml missing, unreadable or refused at start, or no
        // cache folder
        public const string Unknown = "unknown";

        // the labels, in order: the console's View Config labels
        public static readonly IReadOnlyList<string> Labels = Array.AsReadOnly(new[] { "Version", "Config", "Cache", "Output", "Searches", "RIDB refresh", "OS", "Framework" });

        // The console's View Config: the settings Campertron loaded at its start (with a refresh's new timestamp), as View
        // Config showed them before D60: a missing path or output is empty, a missing General.yaml date is "never", and the
        // search files are counted now (0 when the folder can't be listed).
        public static ConfigurationSummary Describe(CampertronInternalConfig config, DateTime UtcNow, CultureInfo Culture, Assembly App)
        {
            return Build(App, config.ConfigPath ?? string.Empty, config.CachePath ?? string.Empty, config.ConfigPath,
                config.GeneralConfig?.OutputTo.ToString() ?? string.Empty, RefreshText(config.GeneralConfig?.LastRidbDataRefresh, UtcNow, Culture));
        }

        // The GUI's card (V011-2): the folders the GUI's startup prepared (Load.GetConfig), the search files counted now, and
        // outputTo and the last RIDB refresh read now from General.yaml without writing or creating anything (ReadGeneral),
        // so a save in Settings > Campertron shows the next time the card is read. What can't be read is Unknown.
        internal static ConfigurationSummary Read(string ConfigPath, string? CachePath, DateTime UtcNow, CultureInfo Culture, Assembly App)
        {
            GeneralConfig? General = ReadGeneral(ConfigPath, UtcNow);
            return Build(App, ConfigPath, CachePath ?? Unknown, ConfigPath,
                General == null ? Unknown : General.OutputTo.ToString(),
                General == null ? Unknown : RefreshText(General.LastRidbDataRefresh, UtcNow, Culture));
        }

        // The web UI's system page (v0.3.0's PR 9, D99): Read's items, with the two folders in words, because a page of the
        // web UI never shows a path (business-rules-settings §31); a cache folder that is not known stays Unknown. Every
        // other item is Read's, so both UIs show the same configuration
        public const string FolderNotShown = "not shown in the web UI (CampertronGUI and the console's View Config show it)";
        internal static ConfigurationSummary ReadWithoutPaths(string ConfigPath, string? CachePath, DateTime UtcNow, CultureInfo Culture, Assembly App)
        {
            ConfigurationSummary Full = Read(ConfigPath, CachePath, UtcNow, Culture, App);
            return new ConfigurationSummary(Full.Items.Select(Item =>
                Item.Label == Labels[1] ? new ConfigurationItem(Item.Label, FolderNotShown)
                : Item.Label == Labels[2] ? new ConfigurationItem(Item.Label, CachePath == null ? Unknown : FolderNotShown)
                : Item));
        }

        // The last RIDB refresh in words as the campground data's card shows it in both UIs (V011-1, D61): General.yaml's
        // lastRidbDataRefresh read now, read-only (ReadGeneral), or Unknown when the file can't be read
        internal static string LastRefreshed(string ConfigPath, DateTime UtcNow, CultureInfo Culture)
        {
            GeneralConfig? General = ReadGeneral(ConfigPath, UtcNow);
            return General == null ? Unknown : RefreshText(General.LastRidbDataRefresh, UtcNow, Culture);
        }

        private static ConfigurationSummary Build(Assembly App, string Config, string Cache, string? SettingsFolder, string Output, string Refresh)
        {
            return new ConfigurationSummary(new[]
            {
                //the first line of --version (RuntimeInfo): the program and its version
                new ConfigurationItem(Labels[0], RuntimeInfo.Describe(App).Split(Environment.NewLine)[0]),
                new ConfigurationItem(Labels[1], Config),
                new ConfigurationItem(Labels[2], Cache),
                new ConfigurationItem(Labels[3], Output),
                new ConfigurationItem(Labels[4], ConsoleUi.Count(SearchFileCount(SettingsFolder), "search file", "search files")),
                new ConfigurationItem(Labels[5], Refresh),
                new ConfigurationItem(Labels[6], RuntimeInformation.OSDescription + " (" + RuntimeInformation.OSArchitecture + ")"),
                //the same text as the second line of --version
                new ConfigurationItem(Labels[7], RuntimeInformation.FrameworkDescription + " (" + RuntimeInformation.ProcessArchitecture + ")"),
            });
        }

        // The number of search files: the console's discovery (ConfigFiles, one rule for every caller). Only names are
        // listed; no file is opened. 0 when the folder can't be listed, as View Config showed it before D60.
        internal static int SearchFileCount(string? ConfigPath)
        {
            try
            {
                return ConfigFiles.SearchFiles(ConfigPath ?? string.Empty).Count;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return 0;
            }
        }

        // The last RIDB refresh in words: "N days ago (date)", "today (date)" in the culture's short date, or "never" for
        // none or the sample General.yaml's date 50 years back, which means "not refreshed yet".
        internal static string RefreshText(DateTime? Refreshed, DateTime UtcNow, CultureInfo Culture)
        {
            string Text = "never (the campground list that came with Campertron is in use)";
            if (Refreshed.HasValue && (UtcNow - Refreshed.Value).TotalDays < 3650)
            {
                int Days = Math.Max(0, (int)Math.Floor((UtcNow - Refreshed.Value).TotalDays));
                Text = (Days == 0 ? "today" : ConsoleUi.Count(Days, "day", "days") + " ago") + " (" + Refreshed.Value.ToString("d", Culture) + ")";
            }
            return Text;
        }

        // General.yaml as a start reads it (the bounded loader, D41, and the start's refusals), but read-only: a missing file
        // is never created (Yaml.GeneralConfigGetConfig would create the sample) and nothing is written. Only an existing,
        // non-empty file is opened: a FIFO, socket or device reports length 0, and opening one could block. Null when the
        // file is missing, can't be read, has no settings, can't be loaded or would be refused at a start.
        internal static GeneralConfig? ReadGeneral(string ConfigPath, DateTime UtcNow)
        {
            try
            {
                string FilePath = Path.Join(ConfigPath, ConfigFiles.GeneralFileName);
                var Info = new FileInfo(FilePath);
                if (!Info.Exists || Info.Length == 0)
                {
                    return null;
                }
                GeneralConfig? General = Yaml.LoadSettingsFile<GeneralConfig>(FilePath);
                if (General == null)
                {
                    return null;
                }
                ConfigValidation.ThrowIfRefused(ConfigValidation.CheckGeneral(General, ConfigFiles.GeneralFileName, UtcNow.ToLocalTime()));
                return General;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is YamlException || ex is InvalidDataException
                || ex is ConfigValidationException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                return null;
            }
        }
    }
}
