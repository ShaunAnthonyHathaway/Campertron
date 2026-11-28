namespace CampertronLibrary.function.Base
{
    // A settings folder as a caller holds it in memory, for the unattended monitoring start's own checks to judge without
    // anything written (D134, v0.4.0's phase 16b: the settings transfer's candidate; v0.4.0-settings-transfer.md §outputTo
    // gate, "the candidate-text seam"). For each name it holds: the candidate's text, or null when the candidate has no such
    // file. A search file the folder does not have yet is one of the search files a start would read; a name it does not hold
    // is read from the folder as before. It decides nothing: ConfigValidation.MonitoringStartErrors and MonitoringSearchErrors
    // (Load.LoadMonitoringSearches, Yaml.CampertronConfigGetConfigs) judge it with their own rules and words, and without one
    // they read the folder exactly as they always did. It never opens, writes or lists anything by itself.
    internal sealed class ConfigCandidate
    {
        private readonly Dictionary<string, string?> Files;

        internal ConfigCandidate(IReadOnlyDictionary<string, string?> Files)
        {
            this.Files = new Dictionary<string, string?>(Files, StringComparer.Ordinal);
        }

        // whether the candidate decides this file (by its exact name)
        internal bool Holds(string FileName) => Files.ContainsKey(FileName);

        // the candidate's text of a file it holds; null when it holds the file as absent
        internal string? Text(string FileName) => Files.TryGetValue(FileName, out string? Text) ? Text : null;

        // whether the file exists in the candidate: its own answer for a name it holds, else the folder's
        internal bool Exists(string ConfigPath, string FileName) => Holds(FileName) ? Text(FileName) != null : File.Exists(Path.Join(ConfigPath, FileName));

        // The search files a start would read in the candidate: the folder's (ConfigFiles.SearchFiles, in its order) but for
        // those it holds as absent, then the search files it adds, by name (ignoring case, then ordinally)
        internal List<string> SearchFiles(string ConfigPath)
        {
            List<string> Listed = Directory.Exists(ConfigPath) ? ConfigFiles.SearchFiles(ConfigPath) : new List<string>();
            var Present = new HashSet<string>(Listed.Select(F => Path.GetFileName(F)), StringComparer.Ordinal);
            List<string> Result = Listed.Where(F => !Holds(Path.GetFileName(F)) || Text(Path.GetFileName(F)) != null).ToList();
            Result.AddRange(Files.Where(F => F.Value != null && !Present.Contains(F.Key) && ConfigFiles.Classify(F.Key) == ConfigFileKind.Search
                    && F.Key.EndsWith(ConfigFiles.SearchPattern.TrimStart('*'), StringComparison.Ordinal))
                .Select(F => F.Key)
                .OrderBy(Name => Name, StringComparer.OrdinalIgnoreCase).ThenBy(Name => Name, StringComparer.Ordinal)
                .Select(Name => Path.Join(ConfigPath, Name)));
            return Result;
        }

        // the text a start reads for a search file of SearchFiles: the candidate's for a name it holds, else null (the folder's)
        internal string? SearchText(string FilePath) => Holds(Path.GetFileName(FilePath)) ? Text(Path.GetFileName(FilePath)) : null;

        public override string ToString() => $"ConfigCandidate: {Files.Count} files";
    }
}
