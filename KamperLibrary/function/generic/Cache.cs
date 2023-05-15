using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KamperLibrary.function.generic
{
    public static class Cache
    {
        public static List<String> GetPermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Equipment.json");

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

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Attributes.json");

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
            Parallel.ForEach(CampsiteIds, ThisCampsiteId =>
            {
                CachePermittedEquipmentByCampsite(ThisCampsiteId);
                CacheCampSiteAttributesByCampsite(ThisCampsiteId);
            });
        }
        public static void CachePermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Equipment.json");

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

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Attributes.json");

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
        public static void CheckCache(String CampgroundID, String CampgroundName)
        {
            if (CacheExist(CampgroundID) == false)
            {
                Console.WriteLine($"Generating cache for {CampgroundName}");
                GenerateCacheForCampground(CampgroundID);
                var folder = Environment.SpecialFolder.LocalApplicationData;
                var path = Environment.GetFolderPath(folder);
                var cachepath = System.IO.Path.Join(path, "KamperCache");
                String JsonFile = System.IO.Path.Join(cachepath, $"{CampgroundID}-Cached.json");
                TextWriter TW = new StreamWriter(JsonFile);
                TW.WriteLine(DateTime.UtcNow.ToString());
                TW.Close();
            }
        }
        public static bool CacheExist(String CampgroundID)
        {
            bool CacheExists = false;

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String JsonFile = System.IO.Path.Join(cachepath, $"{CampgroundID}-Cached.json");
            if (File.Exists(JsonFile))
            {
                CacheExists = true;
            }

            return CacheExists;
        }
    }
}
