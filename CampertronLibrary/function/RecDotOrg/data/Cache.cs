using System.Text.Json;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronLibrary.function.RecDotOrg.data
{
    public static class Cache
    {
        private static string _cachepath = GetCachPath();
        public static List<string> GetPermittedEquipmentByCampsite(string CampsiteID)
        {
            var ReturnList = new List<string>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<string>>(json);
            }
            return ReturnList;
        }
        public static AttributeValueLists GetCampsiteAttributeList(string CampsiteID)
        {
            AttributeValueLists ReturnList = new AttributeValueLists();

            ReturnList.AttValuePair = GetCampSiteAttributesByCampsite(CampsiteID);
            ReturnList.GenerateStringList();

            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(string CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<AttributeValuePair>>(json);
            }
            return ReturnList;
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
        public static void GenerateCacheForCampground(string CampgroundID)
        {
            List<string> CampsiteIds = Read.GetCampsiteIdsByPark(CampgroundID);
            Serialize(_cachepath, $"{CampgroundID}-CampsitesByPark.json", CampsiteIds);
            Parallel.ForEach(CampsiteIds, ThisCampsiteId =>
            {
                CachePermittedEquipmentByCampsite(ThisCampsiteId);
                CacheCampSiteAttributesByCampsite(ThisCampsiteId);
            });
        }
        public static void CachePermittedEquipmentByCampsite(string CampsiteID)
        {
            var ReturnList = new List<string>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.PermittedEquipmentEntries
                              where s.CampsiteID == CampsiteID
                              select s.EquipmentName).ToList();
                TextWriter TW = new StreamWriter(JsonFile);
                TW.Write(JsonSerializer.Serialize(ReturnList));
                TW.Close();
            };
        }
        public static void CacheCampSiteAttributesByCampsite(string CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            string JsonFile = Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
            using (var db = new RecreationDotOrgContext())
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
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"*** {CampInfo.ParkName} 🌲", ConsoleColor.DarkGreen, true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" - ", true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"{CampInfo.CampsiteName}", ConsoleColor.DarkYellow, true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" - ", true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem($"🌳 {CampgroundConfig.DisplayName} ***", ConsoleColor.DarkGreen, true));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
        }
        public static string GetCachPath()
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = Path.Join(path, "CampertronCache");
            return cachepath;
        }
        public static void Serialize(string cachepath, string Filename, object SerialObj)
        {
            string JsonFile = Path.Join(cachepath, Filename);
            TextWriter TW = new StreamWriter(JsonFile);
            TW.Write(JsonSerializer.Serialize(SerialObj));
            TW.Close();
        }
        public static List<CampsitesRecdata> CheckCache(CampertronConfig CampgroundConfig, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder)
        {
            string CampgroundID = CampgroundConfig.CampgroundID;
            List<CampsitesRecdata> ReturnSites = new List<CampsitesRecdata>();
            if (CacheExist(CampgroundID) == false)
            {
                ReturnParkCampground CampInfo = Read.GetParkCampgroundInfo(CampgroundID);
                WriteProgress(CampInfo, ref ResultHolder, CampgroundConfig);
                Serialize(_cachepath, $"{CampgroundID}-CampInfo.json", CampInfo);
                ReturnSites = Read.GetCampsitesByPark(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-Campsites.json", ReturnSites);
                CampsiteConfig.WriteToConsole($"Generating cache for {CampInfo.CampsiteName}", ConsoleColor.Magenta);
                GenerateCacheForCampground(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-Cached.json", DateTime.UtcNow.ToString());
            }
            else
            {
                string JsonFile = Path.Join(_cachepath, $"{CampgroundID}-CampInfo.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    var CampInfo = JsonSerializer.Deserialize<ReturnParkCampground>(json);
                    WriteProgress(CampInfo, ref ResultHolder, CampgroundConfig);
                }
                JsonFile = Path.Join(_cachepath, $"{CampgroundID}-Campsites.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    ReturnSites = JsonSerializer.Deserialize<List<CampsitesRecdata>>(json);
                }
            }
            return ReturnSites;
        }
        public static void PreCheckCache(string CampgroundID)
        {
            List<CampsitesRecdata> ReturnSites = new List<CampsitesRecdata>();
            if (CacheExist(CampgroundID) == false)
            {
                ReturnParkCampground CampInfo = Read.GetParkCampgroundInfo(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-CampInfo.json", CampInfo);
                ReturnSites = Read.GetCampsitesByPark(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-Campsites.json", ReturnSites);
                CampsiteConfig.WriteToConsole($"Generating cache for {CampInfo.CampsiteName}", ConsoleColor.Magenta);
                GenerateCacheForCampground(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-Cached.json", DateTime.UtcNow.ToString());
            }
        }
        public static bool CacheExist(string CampgroundID)
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