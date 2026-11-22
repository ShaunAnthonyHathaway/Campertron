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
        //ntfy's settings (D67): never a search file, never created by Campertron
        internal const string NtfyFileName = "Ntfy.yaml";
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
            if (string.Equals(Name, NtfyFileName, StringComparison.OrdinalIgnoreCase))
            {
                return ConfigFileKind.Ntfy;
            }
            return ConfigFileKind.Search;
        }

        // the search files of the settings folder, in the order Directory.GetFiles gives them (as before)
        internal static List<string> SearchFiles(string ConfigPath)
        {
            return Directory.GetFiles(ConfigPath, SearchPattern, SearchOption.TopDirectoryOnly)
                .Where(FilePath => Classify(FilePath) == ConfigFileKind.Search).ToList();
        }

        // The same search files (the files the console runs: no other discovery), by name for a list (CM6, decision D44):
        // ignoring case, then ordinally, so the order is the same on every platform and in every culture
        internal static List<string> SortedSearchFileNames(string ConfigPath)
        {
            return SearchFiles(ConfigPath).Select(FilePath => Path.GetFileName(FilePath))
                .OrderBy(Name => Name, StringComparer.OrdinalIgnoreCase).ThenBy(Name => Name, StringComparer.Ordinal).ToList();
        }
    }

    // The name of a search file the GUI creates (New search, Save As; CM6, decision D44). A name the user types becomes a
    // file directly in the settings folder whose name ends in ".yaml" (added when it is left out; ".yml" is refused, as
    // the console reads only "*.yaml"), with a name part of letters, digits, spaces and - _ . ( ) only: no folder, no
    // path, no parent folder, no hidden file, nothing Windows refuses or treats specially (a reserved device name, a name
    // ending in a dot or a space), and never General.yaml or Email.yaml in any case. Messages are fixed text.
    internal static class SearchFileName
    {
        internal const string Extension = ".yaml";
        internal const int MaxNameLength = 100;
        private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM\u00B9", "COM\u00B2", "COM\u00B3",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
        };

        // Null and the file name if Input is a usable name, else the problem (FileName empty). Nothing on disk is looked at
        internal static string? Check(string? Input, out string FileName)
        {
            FileName = string.Empty;
            if (string.IsNullOrWhiteSpace(Input))
            {
                return "Enter a name for the search file.";
            }
            string Name = Input;
            if (Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            {
                return "Search files end in .yaml (Campertron does not read .yml files).";
            }
            if (Name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                Name = Name.Substring(0, Name.Length - Extension.Length);
            }
            if (Name.IndexOfAny(new[] { '/', '\\' }) >= 0 || Name.Contains("..", StringComparison.Ordinal))
            {
                return "The name can't contain / or \\ or ..: a search file is directly in the settings folder.";
            }
            if (Name.Length == 0)
            {
                return "Enter a name before .yaml.";
            }
            if (Name.Length > MaxNameLength)
            {
                return $"The name is too long (at most {MaxNameLength} characters before .yaml).";
            }
            if (char.IsWhiteSpace(Name[0]) || char.IsWhiteSpace(Name[^1]))
            {
                return "The name can't start or end with a space.";
            }
            if (Name[0] == '.' || Name[^1] == '.')
            {
                return "The name can't start or end with a dot.";
            }
            foreach (char C in Name)
            {
                if (!(char.IsLetterOrDigit(C) || C == ' ' || C == '-' || C == '_' || C == '.' || C == '(' || C == ')'))
                {
                    return "The name can contain only letters, digits, spaces and - _ . ( ).";
                }
            }
            int Dot = Name.IndexOf('.');
            if (Reserved.Contains((Dot < 0 ? Name : Name.Substring(0, Dot)).TrimEnd(' ')))
            {
                return "That name is reserved by Windows; choose another.";
            }
            string Candidate = Name + Extension;
            if (ConfigFiles.Classify(Candidate) != ConfigFileKind.Search)
            {
                return "General.yaml, Email.yaml and Ntfy.yaml are Campertron's settings files, not search files; choose another name.";
            }
            FileName = Candidate;
            return null;
        }

        // Null if no file, folder or link in the settings folder has this name in any case (two names that differ only in
        // case are the same file on Windows and on macOS by default), else the problem
        internal static string? CheckFree(string ConfigPath, string FileName)
        {
            foreach (string Entry in Directory.EnumerateFileSystemEntries(ConfigPath))
            {
                string Existing = Path.GetFileName(Entry);
                if (string.Equals(Existing, FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(Existing, FileName, StringComparison.Ordinal)
                        ? $"{FileName} already exists; choose another name."
                        : $"{Existing} already exists, and {FileName} differs from it only in upper and lower case; choose another name.";
                }
            }
            return null;
        }
    }
}
