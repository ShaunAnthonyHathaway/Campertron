using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class Read
    {
        public static List<string> UniqueStates()
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              FacilityAddressesEntries.PostalCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select FacilityAddressesEntries.PostalCode).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCities(string State)
        {
            var ReturnList = new List<string>();
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
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilityAddressesEntries.City).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParks(string State, string City)
        {
            var ReturnList = new List<string>();
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
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParksByState(string State)
        {
            var ReturnList = new List<string>();
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
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParks()
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCampgrounds()
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.PostalCode != null &&
                              FacilityAddressesEntries.PostalCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select RecAreaEntries.RecAreaName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCampgroundsByPark(string Park)
        {
            var ReturnList = new List<string>();
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
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> GetCampsiteIdsByPark(string Facility)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from CampsitesEntries in db.CampsitesEntries
                              where CampsitesEntries.FacilityID == Facility
                              select CampsitesEntries.CampsiteID).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<CampsitesRecdata> GetCampsitesByPark(string ParkID)
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
        public static ReturnParkCampground GetParkCampgroundInfo(string CampsiteID)
        {
            ReturnParkCampground ReturnInfo = new ReturnParkCampground();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnInfo = (from FacilitiesEntries in db.FacilitiesEntries
                              join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                              where FacilitiesEntries.FacilityID == CampsiteID &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select new ReturnParkCampground
                              {
                                  CampsiteName = FacilitiesEntries.FacilityName ?? "",
                                  ParkName = RecAreaEntries.RecAreaName ?? ""
                              }).FirstOrDefault() ?? new ReturnParkCampground();
            }
            return ReturnInfo;
        }
        public static string GetParkCampgroundIdByName(string CampsiteName)
        {
            string ReturnStr = string.Empty;
            if (CampsiteName != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnStr = (from FacilitiesEntries in db.FacilitiesEntries
                                 where FacilitiesEntries.FacilityName.ToUpper().Trim() == CampsiteName.ToUpper().Trim() &&
                                 FacilitiesEntries.Reservable == true &&
                                 FacilitiesEntries.Enabled == true
                                 select FacilitiesEntries.FacilityID).FirstOrDefault() ?? string.Empty;
                }
            }
            return ReturnStr;
        }
        public static FacilitiesData? GetParkCampgroundByName(string CampsiteName)
        {
            FacilitiesData? ReturnEntry = new FacilitiesData();
            if (CampsiteName != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnEntry = (from FacilitiesEntry in db.FacilitiesEntries
                                   where FacilitiesEntry.FacilityName.ToUpper().Trim() == CampsiteName.ToUpper().Trim() &&
                                   FacilitiesEntry.Reservable == true &&
                                   FacilitiesEntry.Enabled == true
                                   select FacilitiesEntry).FirstOrDefault() ?? new FacilitiesData();
                }
            }
            if (ReturnEntry?.FacilityDescription != null)
            {
                ReturnEntry.FacilityDescription = HtmlToPlainText(ReturnEntry.FacilityDescription).Replace("Overview", "Description:\r\n");

                ReturnEntry.FacilityEmail = NullCheck("Email:    ", ReturnEntry.FacilityEmail);
                ReturnEntry.FacilityPhone = NullCheck("Phone:  ", ReturnEntry.FacilityPhone);
                ReturnEntry.FacilityName = NullCheck("Name:   ", ReturnEntry.FacilityName);
                ReturnEntry.FacilityID = NullCheck("ID:         ", ReturnEntry.FacilityID);
                ReturnEntry.FacilityDirections = NullCheck("Directions: ", HtmlToPlainText(ReturnEntry.FacilityDirections ?? ""));
                ReturnEntry.FacilityUseFeeDescription = NullCheck("UseFee: ", HtmlToPlainText(ReturnEntry.FacilityUseFeeDescription ?? ""));
            }
            return ReturnEntry;
        }
        private static string? NullCheck(string Header, string? Payload)
        {
            if (Payload == null || Payload.Length == 0)
            {
                return null;
            }
            else
            {
                return Header + Payload;
            }
        }
        public static List<string> GetPermittedEquipmentByCampsite(string CampsiteID)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext())
            {
                ReturnList = (from PermittedEquipmentEntries in db.PermittedEquipmentEntries
                              where PermittedEquipmentEntries.CampsiteID == CampsiteID
                              select PermittedEquipmentEntries.EquipmentName).ToList();
            };
            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(string CampsiteID)
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
        private static string HtmlToPlainText(string html)
        {
            string buf;
            string block = "address|article|aside|blockquote|canvas|dd|div|dl|dt|" +
              "fieldset|figcaption|figure|footer|form|h\\d|header|hr|li|main|nav|" +
              "noscript|ol|output|p|pre|section|table|tfoot|ul|video";

            string patNestedBlock = $"(\\s*?</?({block})[^>]*?>)+\\s*";
            buf = Regex.Replace(html, patNestedBlock, "\n", RegexOptions.IgnoreCase);

            // Replace br tag to newline.
            buf = Regex.Replace(buf, @"<(br)[^>]*>", "\n", RegexOptions.IgnoreCase);

            // (Optional) remove styles and scripts.
            buf = Regex.Replace(buf, @"<(script|style)[^>]*?>.*?</\1>", "", RegexOptions.Singleline);

            // Remove all tags.
            buf = Regex.Replace(buf, @"<[^>]*(>|$)", "", RegexOptions.Multiline);

            // Replace HTML entities.
            buf = WebUtility.HtmlDecode(buf);
            return buf;
        }
    }
}