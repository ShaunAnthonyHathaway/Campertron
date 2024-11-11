using System.Collections.Concurrent;
using static ConsoleConfig;

public class ConsoleConfig
{
    public class ConsoleConfigItem
    {
        public String? Name { get; set; }
        public List<ConsoleConfigValue>? Values { get; set; }
    }
    public class ConsoleConfigValue
    {
        public ConfigType ConfigType { get; set; }
        public String? ConfigValue { get; set; }
        public System.ConsoleColor Color { get; set; }
    }
    public enum ConfigType
    {
        WriteLine,
        Write,
        WriteLineColor,
        WriteColor,
        WriteEmptyLine
    }
}
public class RunningData
{
    public ConcurrentBag<ConsoleConfigItem> AllConsoleConfigItems { get; set; }
    public ConcurrentDictionary<string, AvailabilityEntries> SiteData { get; set; }
    public ConcurrentDictionary<string, bool> Urls { get; set; }
    public List<string> UniqueCampgroundIds { get; set; }
    public List<CampertronConfig> CampertronConfigFiles { get; set; }
    public ConcurrentBag<AvailableData> FilteredAvailableData { get; set; }
}