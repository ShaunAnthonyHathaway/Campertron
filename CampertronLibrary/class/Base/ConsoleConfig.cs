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
        // Presentation metadata for the HTML results page (ResultsHtml). Additive: the console ignores both
        // properties, so console output is unchanged. Text (the default) is an untyped value.
        public OutputKind Kind { get; set; }
        public OutputMetadata? Metadata { get; set; }
    }
    // What a tagged value is to ResultsHtml. Detail marks the remaining values of a tagged record
    // (separators, labels, the URL text); the record's first tagged value carries the Metadata.
    public enum OutputKind
    {
        Text,
        Detail,
        SearchHeader,
        MonthHeader,
        NoEntries,
        Availability,
        ConsecutiveSite,
        ConsecutiveDate
    }
    public class OutputMetadata
    {
        // SearchHeader
        public String? SearchName { get; set; }
        public String? ParkName { get; set; }
        public String? CampgroundName { get; set; }
        public String? CampgroundID { get; set; }
        public int ConsecutiveDays { get; set; }
        // MonthHeader / NoEntries: the first day of the month. Availability / ConsecutiveDate: the available date
        public DateTime? Date { get; set; }
        // Availability / ConsecutiveSite / ConsecutiveDate
        public String? CampsiteID { get; set; }
        public String? CampsiteName { get; set; }
        public String? CampsiteLoop { get; set; }
        public String? CampsiteType { get; set; }
        public List<String>? Equipment { get; set; }
        public String? Checkin { get; set; }
        public String? Checkout { get; set; }
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