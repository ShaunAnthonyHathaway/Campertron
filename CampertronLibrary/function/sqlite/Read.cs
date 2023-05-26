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
                ReturnList = (from s in db.FacilitiesEntries
                              join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                              where s.FacilityTypeDescription == "Campground" &&
                              a.PostalCode != null &&
                              a.PostalCode.Trim().Length > 0 &&
                              (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                              select a.PostalCode).Distinct().OrderBy(p => p).ToList();
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
                    ReturnList = (from s in db.FacilitiesEntries
                                  join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                                  where a.PostalCode == State &&
                                  s.FacilityTypeDescription == "Campground" &&
                                  a.PostalCode != null &&
                                  (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                                  select a.City).Distinct().OrderBy(p => p).ToList();
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
                    ReturnList = (from s in db.FacilitiesEntries
                                  join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                                  where a.PostalCode == State &&
                                  a.City == City &&
                                  s.FacilityTypeDescription == "Campground" &&
                                  a.PostalCode != null &&
                                  (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                                  select s.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<String> UniqueParks()
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.FacilitiesEntries
                              join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                              where s.FacilityTypeDescription == "Campground" &&
                              a.PostalCode != null &&
                              (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                              select s.FacilityName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<String> UniqueCampgrounds()
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.FacilitiesEntries
                              join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                              join d in db.RecAreaEntries on s.ParentRecAreaID equals d.RecAreaID
                              where s.FacilityTypeDescription == "Campground" &&
                              a.PostalCode != null &&
                              a.PostalCode.Trim().Length > 0 &&
                              (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                              select d.RecAreaName).Distinct().OrderBy(p => p).ToList();
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
                    ReturnList = (from s in db.FacilitiesEntries
                                  join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                                  join d in db.RecAreaEntries on s.ParentRecAreaID equals d.RecAreaID
                                  where d.RecAreaName == Park &&
                                  s.FacilityTypeDescription == "Campground" &&
                                  a.PostalCode != null &&
                                  a.PostalCode.Trim().Length > 0 &&
                                  (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                                  select s.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<String> GetCampsiteIdsByPark(String Facility)
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.CampsitesEntries
                              where s.FacilityID == Facility
                              select s.CampsiteID).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<CampsitesRecdata> GetCampsitesByPark(String ParkID)
        {
            var ReturnList = new List<CampsitesRecdata>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.CampsitesEntries
                              where s.FacilityID == ParkID
                              select s).ToList();
            };
            return ReturnList;
        }
        public static ReturnParkCampground GetParkCampgroundInfo(String CampsiteID)
        {
            ReturnParkCampground ReturnInfo = new ReturnParkCampground();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnInfo = (from s in db.FacilitiesEntries
                        join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                        join d in db.RecAreaEntries on s.ParentRecAreaID equals d.RecAreaID
                        where s.FacilityID == CampsiteID
                        select new ReturnParkCampground
                        {
                            CampsiteName = s.FacilityName ?? "",
                            ParkName = d.RecAreaName ?? ""
                        }).FirstOrDefault() ?? new ReturnParkCampground();
            }
            return ReturnInfo;
        }
        public static List<String> GetPermittedEquipmentByCampsite(String CampsiteID)
        {
            var ReturnList = new List<String>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.PermittedEquipmentEntries
                              where s.CampsiteID == CampsiteID
                              select s.EquipmentName).ToList();
            };
            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(String CampsiteID)
        {
            var ReturnList = new List<AttributeValuePair>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from s in db.CampsiteAttributesEntries
                              where s.EntityID == CampsiteID
                              select new AttributeValuePair
                              {
                                  AttributeName = s.AttributeName,
                                  AttributeValue = s.AttributeValue
                              }).ToList();

            }
            return ReturnList;
        }
    }
}