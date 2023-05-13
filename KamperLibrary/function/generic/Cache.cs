using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            String XmlFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Equipment.xml");

            ReturnList = KamperLibrary.function.xml.Serial.DeSerializeObject<List<String>>(XmlFile);

            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String XmlFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Attributes.xml");

            ReturnList = KamperLibrary.function.xml.Serial.DeSerializeObject<List<AttributeValuePair>>(XmlFile);

            return ReturnList;
        }
        public static void GenerateCacheForCampground(String CampgroundID)
        {
            List<String> CampsiteIds = KamperLibrary.function.sqlite.Read.GetCampsiteIdsByPark(CampgroundID);
            foreach (String ThisCampsiteId in CampsiteIds)
            {
                CachePermittedEquipmentByCampsite(ThisCampsiteId);
                CacheCampSiteAttributesByCampsite(ThisCampsiteId);
            }
        }
        public static void CachePermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String XmlFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Equipment.xml");

            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.PermittedEquipmentEntries
                              where s.CampsiteID == CampsiteID
                              select s.EquipmentName).ToList();
                KamperLibrary.function.xml.Serial.SerializeObject(ReturnList, XmlFile);
            };
        }
        public static void CacheCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();

            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            String XmlFile = System.IO.Path.Join(cachepath, $"{CampsiteID}-Attributes.xml");

            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.CampsiteAttributesEntries
                              where s.EntityID == CampsiteID
                              select new AttributeValuePair
                              {
                                  AttributeName = s.AttributeName,
                                  AttributeValue = s.AttributeValue
                              }).ToList();
                KamperLibrary.function.xml.Serial.SerializeObject(ReturnList, XmlFile);
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
                String XmlFile = System.IO.Path.Join(cachepath, $"{CampgroundID}-Cached.xml");
                TextWriter TW = new StreamWriter(XmlFile);
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
            String XmlFile = System.IO.Path.Join(cachepath, $"{CampgroundID}-Cached.xml");
            if (File.Exists(XmlFile))
            {
                CacheExists = true;
            }

            return CacheExists;
        }
    }
}
