using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Cache
    {
        public static List<string> GetPermittedEquipmentByCampsite(string CampsiteID, string _cachepath)
        {
            var ReturnList = new List<string>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<string>>(json);
            }
            return ReturnList ?? new List<string>();
        }
        public static AttributeValueLists GetCampsiteAttributeList(string CampsiteID, string _cachepath)
        {
            AttributeValueLists ReturnList = new AttributeValueLists();

            ReturnList.AttValuePair = GetCampSiteAttributesByCampsite(CampsiteID, _cachepath);
            ReturnList.GenerateStringList();

            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(string CampsiteID, string _cachepath)
        {
            var ReturnList = new List<AttributeValuePair>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<AttributeValuePair>>(json);
            }
            return ReturnList ?? new List<AttributeValuePair>();
        }
        public static List<String> ConvertAttributeValuePairToString(List<AttributeValuePair> attributes)
        {
            var ReturnList = new List<String>();

            foreach (AttributeValuePair attr in attributes)
            {
                ReturnList.Add($"{attr.AttributeName}:{attr.AttributeValue}");
            }

            return ReturnList;
        }
        public static void GenerateCacheForCampground(string CampgroundID, string _cachepath, string _configpath)
        {
            GenerateCacheForCampground(CampgroundID, _cachepath, _configpath, CancellationToken.None);
        }
        //Token (an interactive search, GS1): stops between campsites with OperationCanceledException; the caller then
        //writes no completion marker ({CampgroundID}-Cached.json), so the next search builds the campground again
        internal static void GenerateCacheForCampground(string CampgroundID, string _cachepath, string _configpath, CancellationToken Token)
        {
            List<string> CampsiteIds = Read.GetCampsiteIdsByPark(CampgroundID, _configpath);
            Serialize(_cachepath, $"{CampgroundID}-CampsitesByPark.json", CampsiteIds);
            Parallel.ForEach(CampsiteIds, new ParallelOptions { CancellationToken = Token }, ThisCampsiteId =>
            {
                CachePermittedEquipmentByCampsite(ThisCampsiteId, _cachepath, _configpath);
                CacheCampSiteAttributesByCampsite(ThisCampsiteId, _cachepath, _configpath);
            });
        }
        public static void CachePermittedEquipmentByCampsite(string CampsiteID, string _cachepath, string _configpath)
        {
            var ReturnList = new List<string>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
            using (var db = new RecreationDotOrgContext(_configpath))
            {
                ReturnList = (from s in db.PermittedEquipmentEntries
                              where s.CampsiteID == CampsiteID
                              select s.EquipmentName).ToList();
                TextWriter TW = new StreamWriter(JsonFile);
                TW.Write(JsonSerializer.Serialize(ReturnList));
                TW.Close();
            };
        }
        public static void CacheCampSiteAttributesByCampsite(string CampsiteID, string _cachepath, string _configpath)
        {
            var ReturnList = new List<AttributeValuePair>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
            using (var db = new RecreationDotOrgContext(_configpath))
            {
                ReturnList = (from s in db.CampsiteAttributesEntries
                              where s.EntityID == CampsiteID
                              select new AttributeValuePair
                              {
                                  AttributeName = s.AttributeName,
                                  AttributeValue = s.AttributeValue
                              }).ToList();
                TextWriter TW = new StreamWriter(JsonFile);
                TW.Write(JsonSerializer.Serialize(ReturnList));
                TW.Close();
            }
        }
        public static void WriteProgress(ReturnParkCampground CampInfo, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder, CampertronConfig CampgroundConfig)
        {
            //the search header; the HTML results take the typed SearchHeader metadata, the console the text
            List<ConsoleConfig.ConsoleConfigValue> Header = new List<ConsoleConfig.ConsoleConfigValue>
            {
                CampsiteConfig.AddConsoleConfigItem($"*** {CampInfo.ParkName} 🌲", ConsoleColor.DarkGreen, true),
                CampsiteConfig.AddConsoleConfigItem(" - ", true),
                CampsiteConfig.AddConsoleConfigItem($"{CampInfo.CampsiteName}", ConsoleColor.DarkYellow, true),
                CampsiteConfig.AddConsoleConfigItem(" - ", true),
                CampsiteConfig.AddConsoleConfigItem($"🌳 {CampgroundConfig.DisplayName} ***", ConsoleColor.DarkGreen, true)
            };
            foreach (ConsoleConfig.ConsoleConfigValue Value in Header)
            {
                Value.Kind = ConsoleConfig.OutputKind.Detail;
            }
            Header[0].Kind = ConsoleConfig.OutputKind.SearchHeader;
            Header[0].Metadata = new ConsoleConfig.OutputMetadata
            {
                SearchName = CampgroundConfig.DisplayName,
                ParkName = CampInfo.ParkName,
                CampgroundName = CampInfo.CampsiteName,
                CampgroundID = CampgroundConfig.CampgroundID,
                ConsecutiveDays = CampgroundConfig.ConsecutiveDays
            };
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
            ResultHolder.AddRange(Header);
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
        }
        public static void Serialize(string cachepath, string Filename, object SerialObj)
        {
            string JsonFile = Path.Join(cachepath, Filename);
            TextWriter TW = new StreamWriter(JsonFile);
            TW.Write(JsonSerializer.Serialize(SerialObj));
            TW.Close();
        }
        public static List<CampsitesRecdata> CheckCache(CampertronConfig CampgroundConfig, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder, CampertronInternalConfig config)
        {
            return CheckCache(CampgroundConfig, ref ResultHolder, config, ConsoleUi.Status, CancellationToken.None, true);
        }
        //Status: where "Preparing campsite details for …" goes (the console's status line; null for an interactive
        //search, which writes nothing to the console). MayBuild false (an interactive search, GS1, whose run prepared the
        //campground first): a campground without its cache is not built here but refused (CampsiteDetailsMissingException),
        //so the database is never opened from the search itself
        internal static List<CampsitesRecdata> CheckCache(CampertronConfig CampgroundConfig, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder, CampertronInternalConfig config,
            Action<string>? Status, CancellationToken Token, bool MayBuild)
        {
            string CampgroundID = CampgroundConfig.CampgroundID;
            List<CampsitesRecdata> ReturnSites = new List<CampsitesRecdata>();
            if (CacheExist(CampgroundID, config.CachePath) == false)
            {
                if (!MayBuild)
                {
                    throw new CampsiteDetailsMissingException();
                }
                ReturnParkCampground CampInfo = Read.GetParkCampgroundInfo(CampgroundID, config.ConfigPath);
                WriteProgress(CampInfo, ref ResultHolder, CampgroundConfig);
                Serialize(config.CachePath, $"{CampgroundID}-CampInfo.json", CampInfo);
                ReturnSites = Read.GetCampsitesByPark(CampgroundID, config.ConfigPath);
                Serialize(config.CachePath, $"{CampgroundID}-Campsites.json", ReturnSites);
                Status?.Invoke(PreparingText(CampInfo, CampgroundID));
                GenerateCacheForCampground(CampgroundID, config.CachePath, config.ConfigPath, Token);
                Token.ThrowIfCancellationRequested();
                Serialize(config.CachePath, $"{CampgroundID}-Cached.json", DateTime.UtcNow.ToString());
            }
            else
            {
                string JsonFile = Path.Join(config.CachePath, $"{CampgroundID}-CampInfo.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    var CampInfo = JsonSerializer.Deserialize<ReturnParkCampground>(json);
                    WriteProgress(CampInfo, ref ResultHolder, CampgroundConfig);
                }
                JsonFile = Path.Join(config.CachePath, $"{CampgroundID}-Campsites.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    ReturnSites = JsonSerializer.Deserialize<List<CampsitesRecdata>>(json);
                }
            }
            return ReturnSites?? new List<CampsitesRecdata>();
        }
        public static void PreCheckCache(string CampgroundID, string _cachepath, string _configpath)
        {
            PrepareCampground(CampgroundID, _cachepath, _configpath, ConsoleUi.Status, CancellationToken.None);
        }
        //the campground's cache, built from the database if it is not complete yet. The completion marker
        //({CampgroundID}-Cached.json, the only file CacheExist looks at) is written last and only after every other file
        //was written, so a build that stops (a canceled interactive search, GS1; an exception; a killed process) leaves no
        //cache that looks complete, and the next search builds it again. Status: the console's status line, or null
        internal static void PrepareCampground(string CampgroundID, string _cachepath, string _configpath, Action<string>? Status, CancellationToken Token)
        {
            List<CampsitesRecdata> ReturnSites = new List<CampsitesRecdata>();
            if (CacheExist(CampgroundID, _cachepath) == false)
            {
                Token.ThrowIfCancellationRequested();
                ReturnParkCampground CampInfo = Read.GetParkCampgroundInfo(CampgroundID, _configpath);
                Serialize(_cachepath, $"{CampgroundID}-CampInfo.json", CampInfo);
                ReturnSites = Read.GetCampsitesByPark(CampgroundID, _configpath);
                Serialize(_cachepath, $"{CampgroundID}-Campsites.json", ReturnSites);
                Status?.Invoke(PreparingText(CampInfo, CampgroundID));
                GenerateCacheForCampground(CampgroundID, _cachepath, _configpath, Token);
                Token.ThrowIfCancellationRequested();
                Serialize(_cachepath, $"{CampgroundID}-Cached.json", DateTime.UtcNow.ToString());
            }
        }
        //shown once per campground, while its campsite details are copied from the database into the cache
        private static string PreparingText(ReturnParkCampground? CampInfo, string? CampgroundID)
        {
            if (!string.IsNullOrWhiteSpace(CampInfo?.CampsiteName))
            {
                return "Preparing campsite details for " + CampInfo.CampsiteName.Trim() + "...";
            }
            return string.IsNullOrWhiteSpace(CampgroundID) ? "Preparing campsite details..." : "Preparing campsite details for campground ID " + CampgroundID + "...";
        }
        //campground names by ID, as far as the console has looked them up (GetCampInfo), for its messages
        public static System.Collections.Concurrent.ConcurrentDictionary<string, string> KnownCampgroundNames { get; } = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        //the campground and park of a cached campground ({CampgroundID}-CampInfo.json), or null if it is not cached or
        //cannot be read. For display only: nothing depends on the result.
        public static ReturnParkCampground? GetCampInfo(string? CampgroundID, string? _cachepath)
        {
            if (string.IsNullOrWhiteSpace(CampgroundID) || string.IsNullOrWhiteSpace(_cachepath))
            {
                return null;
            }
            try
            {
                string JsonFile = Path.Join(_cachepath, $"{CampgroundID}-CampInfo.json");
                if (!File.Exists(JsonFile))
                {
                    return null;
                }
                ReturnParkCampground? CampInfo = JsonSerializer.Deserialize<ReturnParkCampground>(File.ReadAllText(JsonFile));
                if (!string.IsNullOrWhiteSpace(CampInfo?.CampsiteName))
                {
                    KnownCampgroundNames[CampgroundID] = CampInfo.CampsiteName.Trim();
                }
                return CampInfo;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }
        //an interactive search found a campground without its prepared cache (CheckCache with MayBuild false)
        internal sealed class CampsiteDetailsMissingException : Exception
        {
            internal CampsiteDetailsMissingException() : base("The campground's campsite details are not prepared.")
            {
            }
        }
        public static bool CacheExist(string CampgroundID,string _cachepath)
        {
            bool CacheExists = false;
            string JsonFile = Path.Join(_cachepath, $"{CampgroundID}-Cached.json");
            if (File.Exists(JsonFile))
            {
                CacheExists = true;
            }
            return CacheExists;
        }
    }
}