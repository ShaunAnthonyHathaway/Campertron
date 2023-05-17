using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KamperLibrary.function.generic
{
    public static class Cache
    {
        private static String _cachepath = GetCachPath();
        public static List<String> GetPermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();
            String JsonFile = System.IO.Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<String>>(json);
            }
            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            String JsonFile = System.IO.Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
            using (StreamReader r = new StreamReader(JsonFile))
            {
                string json = r.ReadToEnd();
                ReturnList = JsonSerializer.Deserialize<List<AttributeValuePair>>(json);
            }
            return ReturnList;
        }
        public static void GenerateCacheForCampground(String CampgroundID)
        {
            List<String> CampsiteIds = KamperLibrary.function.sqlite.Read.GetCampsiteIdsByPark(CampgroundID);
            Serialize(_cachepath, $"{CampgroundID}-CampsitesByPark.json", CampsiteIds);
            Parallel.ForEach(CampsiteIds, ThisCampsiteId =>
            {
                CachePermittedEquipmentByCampsite(ThisCampsiteId);
                CacheCampSiteAttributesByCampsite(ThisCampsiteId);
            });
        }
        public static void CachePermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();
            String JsonFile = System.IO.Path.Join(_cachepath, $"{CampsiteID}-Equipment.json");
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
        public static void CacheCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            String JsonFile = System.IO.Path.Join(_cachepath, $"{CampsiteID}-Attributes.json");
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
        public static void WriteProgress(ReturnParkCampground CampInfo)
        {
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write($"*** {CampInfo.ParkName} 🌲");
            Console.ResetColor();
            Console.Write(" - ");
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write($"{CampInfo.CampsiteName} 🌳");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write(" ***");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine();
        }
        public static String GetCachPath()
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            return cachepath;
        }
        public static void Serialize(String cachepath, String Filename, object SerialObj)
        {
            String JsonFile = System.IO.Path.Join(cachepath, Filename);
            TextWriter TW = new StreamWriter(JsonFile);
            TW.Write(JsonSerializer.Serialize(SerialObj));
            TW.Close();
        }        
        public static List<CampsitesRecdata> CheckCache(String CampgroundID)
        {
            List<CampsitesRecdata> ReturnSites = new List<CampsitesRecdata>();
            if (CacheExist(CampgroundID) == false)
            {
                ReturnParkCampground CampInfo = KamperLibrary.function.sqlite.Read.GetParkCampgroundInfo(CampgroundID);
                WriteProgress(CampInfo);
                Serialize(_cachepath, $"{CampgroundID}-CampInfo.json", CampInfo);
                ReturnSites = KamperLibrary.function.sqlite.Read.GetCampsitesByPark(CampgroundID);
                Serialize(_cachepath, $"{CampgroundID}-Campsites.json", ReturnSites);                
                Console.WriteLine($"Generating cache for {CampInfo.CampsiteName}");
                GenerateCacheForCampground(CampgroundID);                
                Serialize(_cachepath, $"{CampgroundID}-Cached.json", DateTime.UtcNow.ToString());
            }
            else
            {
                String JsonFile = System.IO.Path.Join(_cachepath, $"{CampgroundID}-CampInfo.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    var CampInfo = JsonSerializer.Deserialize<ReturnParkCampground>(json);
                    WriteProgress(CampInfo);
                }
                JsonFile = System.IO.Path.Join(_cachepath, $"{CampgroundID}-Campsites.json");
                using (StreamReader r = new StreamReader(JsonFile))
                {
                    string json = r.ReadToEnd();
                    ReturnSites = JsonSerializer.Deserialize<List<CampsitesRecdata>>(json);                   
                }
            }
            return ReturnSites;
        }
        public static bool CacheExist(String CampgroundID)
        {
            bool CacheExists = false;
            String JsonFile = System.IO.Path.Join(_cachepath, $"{CampgroundID}-Cached.json");
            if (File.Exists(JsonFile))
            {
                CacheExists = true;
            }
            return CacheExists;
        }
    }
}