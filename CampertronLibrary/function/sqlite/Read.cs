using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CampertronLibrary.function.sqlite
{
    static public class Read
    {
        public static List<String> UniqueStates()
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              FacilityAddressesEntries.PostalCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true
                              select FacilityAddressesEntries.PostalCode).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<String> UniqueCities(String State)
        {
            var ReturnList = new List<String>();
            if (State != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  where FacilityAddressesEntries.PostalCode == State &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.PostalCode != null &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true
                                  select FacilityAddressesEntries.City).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<String> UniqueParks(String State, String City)
        {
            var ReturnList = new List<String>();
            if (State != null && City != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  where FacilityAddressesEntries.PostalCode == State &&
                                  FacilityAddressesEntries.City == City &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.PostalCode != null &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<String> UniqueParks()
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true
                              select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<String> UniqueCampgrounds()
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              FacilityAddressesEntries.PostalCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true
                              select RecAreaEntries.RecAreaName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<String> UniqueCampgroundsByPark(String Park)
        {
            var ReturnList = new List<String>();
            if (Park != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                                  where RecAreaEntries.RecAreaName == Park &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.PostalCode != null &&
                                  FacilityAddressesEntries.PostalCode.Trim().Length > 0 &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<String> GetCampsiteIdsByPark(String Facility)
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from CampsitesEntries in db.CampsitesEntries
                              where CampsitesEntries.FacilityID == Facility
                              select CampsitesEntries.CampsiteID).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<CampsitesRecdata> GetCampsitesByPark(String ParkID)
        {
            var ReturnList = new List<CampsitesRecdata>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from CampsitesEntries in db.CampsitesEntries
                              where CampsitesEntries.FacilityID == ParkID
                              select CampsitesEntries).ToList();
            };
            return ReturnList;
        }
        public static ReturnParkCampground GetParkCampgroundInfo(String CampsiteID)
        {
            ReturnParkCampground ReturnInfo = new ReturnParkCampground();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnInfo = (from FacilitiesEntries in db.FacilitiesEntries
                              join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                              where FacilitiesEntries.FacilityID == CampsiteID
                              select new ReturnParkCampground
                              {
                                  CampsiteName = FacilitiesEntries.FacilityName ?? "",
                                  ParkName = RecAreaEntries.RecAreaName ?? ""
                              }).FirstOrDefault() ?? new ReturnParkCampground();
            }
            return ReturnInfo;
        }
        public static String GetParkCampgroundIdByName(String CampsiteName)
        {
            String ReturnStr = string.Empty;
            if (CampsiteName != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnStr = (from FacilitiesEntries in db.FacilitiesEntries
                                 where FacilitiesEntries.FacilityName.ToUpper().Trim() == CampsiteName.ToUpper().Trim()
                                 select FacilitiesEntries.FacilityID).FirstOrDefault() ?? String.Empty;
                }
            }
            return ReturnStr;
        }
        public static List<String> GetPermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from PermittedEquipmentEntries in db.PermittedEquipmentEntries
                              where PermittedEquipmentEntries.CampsiteID == CampsiteID
                              select PermittedEquipmentEntries.EquipmentName).ToList();
            };
            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from CampsiteAttributesEntries in db.CampsiteAttributesEntries
                              where CampsiteAttributesEntries.EntityID == CampsiteID
                              select new AttributeValuePair
                              {
                                  AttributeName = CampsiteAttributesEntries.AttributeName,
                                  AttributeValue = CampsiteAttributesEntries.AttributeValue
                              }).ToList();

            }
            return ReturnList;
        }
    }
}