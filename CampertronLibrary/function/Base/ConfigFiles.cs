namespace CampertronLibrary.function.Base
{
    // Which settings file a file of the settings folder is (decision D41): General.yaml, Email.yaml, or else a search
    // file. One rule for every caller (the search loader, View Config, the failure messages, the validation): the name is
    // compared ordinally and case-insensitively, so it never depends on the current culture (in Turkish, "EMAIL.yaml"
    // lower-cased with the culture is "emaıl.yaml", which used to make it a search file). Discovery is unchanged: the
    // top-level files matching "*.yaml" (no ".yml", no subfolder); CM2's backups and temporary files ("<name>.bak",
    // "<name>.<32 hex>.tmp") never match it.
    internal static class ConfigFiles
    {
        internal const string GeneralFileName = "General.yaml";
        internal const string EmailFileName = "Email.yaml";
        internal const string SearchPattern = "*.yaml";

        internal static ConfigFileKind Classify(string FileName)
        {
            string Name = Path.GetFileName(FileName);
            if (string.Equals(Name, GeneralFileName, StringComparison.OrdinalIgnoreCase))
            {
                return ConfigFileKind.General;
            }
            if (string.Equals(Name, EmailFileName, StringComparison.OrdinalIgnoreCase))
            {
                return ConfigFileKind.Email;
            }
            return ConfigFileKind.Search;
        }

        // the search files of the settings folder, in the order Directory.GetFiles gives them (as before)
        internal static List<string> SearchFiles(string ConfigPath)
        {
            return Directory.GetFiles(ConfigPath, SearchPattern, SearchOption.TopDirectoryOnly)
                .Where(FilePath => Classify(FilePath) == ConfigFileKind.Search).ToList();
        }
    }
}
