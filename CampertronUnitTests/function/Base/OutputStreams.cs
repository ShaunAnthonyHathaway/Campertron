using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.api;
using CampertronLibrary.function.RecDotOrg.data;

namespace CampertronUnitTests.function.Base
{
    // Builds the tagged output streams of one search with the production producers (Cache.WriteProgress,
    // AvailabilityApi.MonthHeaderValues/NoEntriesValues/AvailabilityValues), without network, database or files.
    // Producers that use the current culture (month names, short dates) must run under the culture the test sets.
    internal static class OutputStreams
    {
        public static AvailabilityData Hit(string Id, string Name, DateTime Date, string Loop = "Loop C", string Type = "STANDARD NONELECTRIC", params string[] Equipment) =>
            new AvailabilityData
            {
                CampsiteID = Id,
                CampsiteName = Name,
                CampsiteLoop = Loop,
                CampsiteType = Type,
                CampsiteAvailableDate = Date,
                PermittedEquipmentList = Equipment.Length > 0 ? Equipment.ToList() : new List<string> { "Tent", "CAR" }
            };

        public static List<ConsoleConfig.ConsoleConfigValue> Header(string SearchName, string Park = "Zion National Park", string Campground = "WATCHMAN CAMPGROUND", string CampgroundId = "232445", int ConsecutiveDays = 1)
        {
            var Values = new List<ConsoleConfig.ConsoleConfigValue>();
            var Config = new CampertronConfig { DisplayName = SearchName, CampgroundID = CampgroundId, ConsecutiveDays = ConsecutiveDays };
            Cache.WriteProgress(new ReturnParkCampground { ParkName = Park, CampsiteName = Campground }, ref Values, Config);
            return Values;
        }

        // consecutiveDays == 1: a month header followed by the month's hits, in the given order
        public static List<ConsoleConfig.ConsoleConfigValue> Month(DateTime Month, params AvailabilityData[] Hits)
        {
            var Values = AvailabilityApi.MonthHeaderValues(Month);
            foreach (AvailabilityData Hit in Hits)
            {
                Values.AddRange(AvailabilityApi.AvailabilityValues(Hit, "12:00 PM", "11:00 AM", false));
            }
            return Values;
        }

        public static List<ConsoleConfig.ConsoleConfigValue> EmptyMonth(DateTime Month)
        {
            var Values = AvailabilityApi.MonthHeaderValues(Month);
            Values.AddRange(AvailabilityApi.NoEntriesValues(Month));
            return Values;
        }

        // consecutiveDays > 1, as FilterConsecutiveDays writes one campsite: the site part (values 4-9) of its first
        // entry, the date part (values 0-3) of every entry in the order given, then an empty line
        public static List<ConsoleConfig.ConsoleConfigValue> ConsecutiveSite(params AvailabilityData[] Entries)
        {
            var PerEntry = Entries.Select(p => AvailabilityApi.AvailabilityValues(p, "12:00 PM", "11:00 AM", true)).ToList();
            var Values = new List<ConsoleConfig.ConsoleConfigValue>();
            Values.AddRange(PerEntry[0].Skip(4));
            foreach (var Entry in PerEntry)
            {
                Values.AddRange(Entry.Take(4));
            }
            Values.Add(CampsiteConfig.AddConsoleConfigItem(true));
            return Values;
        }

        public static ConsoleConfig.ConsoleConfigItem Search(string? Name, params List<ConsoleConfig.ConsoleConfigValue>[] Parts) =>
            new ConsoleConfig.ConsoleConfigItem { Name = Name, Values = Parts.SelectMany(p => p).ToList() };
    }
}
