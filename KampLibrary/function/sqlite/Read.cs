using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KampLibrary.function.sqlite
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
                              where s.FacilityTypeDescription == "Campground" && a.PostalCode != null && a.PostalCode.Trim().Length > 0 && (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
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
                                  where a.PostalCode == State && s.FacilityTypeDescription == "Campground" && a.PostalCode != null && (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                                  select a.City).Distinct().OrderBy(p => p).ToList();
                };
            }

            return ReturnList;
        }
        public static List<String> UniqueFacilities(String State, String City)
        {
            var ReturnList = new List<String>();

            if (State != null && City != null)
            {
                using (var db = new RecreationDotOrgContext())
                {
                    ReturnList = (from s in db.FacilitiesEntries
                                  join a in db.FacilityAddressesEntries on s.FacilityID equals a.FacilityID
                                  where a.PostalCode == State && a.City == City && s.FacilityTypeDescription == "Campground" && a.PostalCode != null && (a.FacilityAddressType == "Physical" || a.FacilityAddressType == "Default")
                                  select s.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }

            return ReturnList;
        }
    }
}
