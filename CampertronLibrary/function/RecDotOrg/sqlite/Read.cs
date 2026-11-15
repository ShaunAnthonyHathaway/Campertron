using System.Net;
using System.Text.RegularExpressions;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class Read
    {
        public static List<string> UniqueStates(String ConfigPath)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.AddressStateCode != null &&
                              FacilityAddressesEntries.AddressStateCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select FacilityAddressesEntries.AddressStateCode).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCities(string State, string ConfigPath)
        {
            var ReturnList = new List<string>();
            if (State != null)
            {
                using (var db = new RecreationDotOrgContext(ConfigPath))
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  where FacilityAddressesEntries.AddressStateCode == State &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.AddressStateCode != null &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilityAddressesEntries.City).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParks(string State, string City, String ConfigPath)
        {
            var ReturnList = new List<string>();
            if (State != null && City != null)
            {
                using (var db = new RecreationDotOrgContext(ConfigPath))
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  where FacilityAddressesEntries.AddressStateCode == State &&
                                  FacilityAddressesEntries.City == City &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.AddressStateCode != null &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParksByState(string State, string ConfigPath)
        {
            var ReturnList = new List<string>();
            if (State != null)
            {
                using (var db = new RecreationDotOrgContext(ConfigPath))
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  where FacilityAddressesEntries.AddressStateCode == State &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.AddressStateCode != null &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        public static List<string> UniqueParks(String ConfigPath)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.AddressStateCode != null &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCampgrounds(String ConfigPath)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                              join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                              join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                              where FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                              FacilityAddressesEntries.AddressStateCode != null &&
                              FacilityAddressesEntries.AddressStateCode.Trim().Length > 0 &&
                              (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                              FacilitiesEntries.Reservable == true &&
                              FacilitiesEntries.Enabled == true
                              select RecAreaEntries.RecAreaName).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<string> UniqueCampgroundsByPark(string Park, String ConfigPath)
        {
            var ReturnList = new List<string>();
            if (Park != null)
            {
                using (var db = new RecreationDotOrgContext(ConfigPath))
                {
                    ReturnList = (from FacilitiesEntries in db.FacilitiesEntries
                                  join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                  join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                                  where RecAreaEntries.RecAreaName == Park &&
                                  FacilitiesEntries.FacilityTypeDescription == "Campground" &&
                                  FacilityAddressesEntries.AddressStateCode != null &&
                                  FacilityAddressesEntries.AddressStateCode.Trim().Length > 0 &&
                                  (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                  FacilitiesEntries.Reservable == true &&
                                  FacilitiesEntries.Enabled == true
                                  select FacilitiesEntries.FacilityName).Distinct().OrderBy(p => p).ToList();
                };
            }
            return ReturnList;
        }
        // ---- campgrounds by FacilityID (decision D42, TD-38) ----
        //
        // The GUI identifies a campground by its FacilityID (the database's TEXT primary key), never by its name: names
        // are not unique (24 names of 4,966 reservable, enabled campgrounds in the shipped database, one of them the
        // empty name; a name can also belong to another kind of facility). Each list is one query, with the location
        // filter (or park) as a parameter, and holds one CampgroundChoice per FacilityID in CampgroundChoice.ListOrder;
        // duplicate names stay separate entries. The location of a choice is its Physical address if it has one, else
        // its Default address (by state, then city), or none. The name-based methods above and below are kept for their
        // callers (the database validation, ReleaseCheck, tests) but the GUI no longer uses them.

        private const string CampgroundType = "Campground";

        // the campgrounds with a Physical or Default address in State, as UniqueParksByState lists their names
        public static List<CampgroundChoice> CampgroundChoicesByState(string? State, String ConfigPath)
        {
            if (State == null)
            {
                return new List<CampgroundChoice>();
            }
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                return Choices((from FacilitiesEntries in db.FacilitiesEntries
                                join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                where FacilityAddressesEntries.AddressStateCode == State &&
                                FacilitiesEntries.FacilityTypeDescription == CampgroundType &&
                                FacilityAddressesEntries.AddressStateCode != null &&
                                (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                FacilitiesEntries.Reservable == true &&
                                FacilitiesEntries.Enabled == true
                                select new ChoiceRow
                                {
                                    FacilityID = FacilitiesEntries.FacilityID,
                                    FacilityName = FacilitiesEntries.FacilityName,
                                    City = FacilityAddressesEntries.City,
                                    State = FacilityAddressesEntries.AddressStateCode,
                                    AddressType = FacilityAddressesEntries.FacilityAddressType
                                }).ToList());
            }
        }

        // the campgrounds with a Physical or Default address in State and City (an empty city included), as
        // UniqueParks(State, City) lists their names
        public static List<CampgroundChoice> CampgroundChoicesByStateAndCity(string? State, string? City, String ConfigPath)
        {
            if (State == null || City == null)
            {
                return new List<CampgroundChoice>();
            }
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                return Choices((from FacilitiesEntries in db.FacilitiesEntries
                                join FacilityAddressesEntries in db.FacilityAddressesEntries on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID
                                where FacilityAddressesEntries.AddressStateCode == State &&
                                FacilityAddressesEntries.City == City &&
                                FacilitiesEntries.FacilityTypeDescription == CampgroundType &&
                                FacilityAddressesEntries.AddressStateCode != null &&
                                (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default") &&
                                FacilitiesEntries.Reservable == true &&
                                FacilitiesEntries.Enabled == true
                                select new ChoiceRow
                                {
                                    FacilityID = FacilitiesEntries.FacilityID,
                                    FacilityName = FacilitiesEntries.FacilityName,
                                    City = FacilityAddressesEntries.City,
                                    State = FacilityAddressesEntries.AddressStateCode,
                                    AddressType = FacilityAddressesEntries.FacilityAddressType
                                }).ToList());
            }
        }

        // the parks (recreation areas) with a reservable, enabled campground. Unlike UniqueCampgrounds, a campground
        // does not need an address here: a park is not a location filter
        public static List<string> ParksWithCampgrounds(String ConfigPath)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                return (from FacilitiesEntries in db.FacilitiesEntries
                        join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                        where FacilitiesEntries.FacilityTypeDescription == CampgroundType &&
                        RecAreaEntries.RecAreaName != null &&
                        FacilitiesEntries.Reservable == true &&
                        FacilitiesEntries.Enabled == true
                        select RecAreaEntries.RecAreaName).Distinct().OrderBy(p => p).ToList()!;
            }
        }

        // every reservable, enabled campground of the park, also one without an address (its choice then has no
        // location; outer join). Unlike UniqueCampgroundsByPark, an address is not required
        public static List<CampgroundChoice> CampgroundChoicesByPark(string? Park, String ConfigPath)
        {
            if (Park == null)
            {
                return new List<CampgroundChoice>();
            }
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                var Qualifying = db.FacilityAddressesEntries.Where(FacilityAddressesEntries =>
                    FacilityAddressesEntries.AddressStateCode != null &&
                    FacilityAddressesEntries.AddressStateCode.Trim().Length > 0 &&
                    (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default"));
                return Choices((from FacilitiesEntries in db.FacilitiesEntries
                                join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID
                                join FacilityAddressesEntries in Qualifying on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID into Addresses
                                from FacilityAddressesEntries in Addresses.DefaultIfEmpty()
                                where RecAreaEntries.RecAreaName == Park &&
                                FacilitiesEntries.FacilityTypeDescription == CampgroundType &&
                                FacilitiesEntries.Reservable == true &&
                                FacilitiesEntries.Enabled == true
                                select new ChoiceRow
                                {
                                    FacilityID = FacilitiesEntries.FacilityID,
                                    FacilityName = FacilitiesEntries.FacilityName,
                                    City = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.City,
                                    State = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.AddressStateCode,
                                    AddressType = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.FacilityAddressType
                                }).ToList());
            }
        }

        // The details of one campground, by its FacilityID (exact, ordinal; one row at most, as FacilityID is the
        // primary key): a reservable, enabled campground, with the HTML fields converted to plain text as
        // GetCampgroundDetailsByName does. Null (without a query for a blank ID) for no ID or an ID that is not such a
        // campground; never another record. The ID is matched exactly: no trimming, no numeric parsing
        public static FacilitiesData? GetCampgroundDetailsById(string? FacilityID, String ConfigPath)
        {
            if (string.IsNullOrWhiteSpace(FacilityID))
            {
                return null;
            }
            FacilitiesData? Entry;
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                Entry = (from FacilitiesEntry in db.FacilitiesEntries
                         where FacilitiesEntry.FacilityID == FacilityID &&
                         FacilitiesEntry.FacilityTypeDescription == CampgroundType &&
                         FacilitiesEntry.Reservable == true &&
                         FacilitiesEntry.Enabled == true
                         select FacilitiesEntry).SingleOrDefault();
            }
            if (Entry != null)
            {
                Entry.FacilityDescription = Entry.FacilityDescription == null ? null : HtmlToPlainText(Entry.FacilityDescription);
                Entry.FacilityDirections = Entry.FacilityDirections == null ? null : HtmlToPlainText(Entry.FacilityDirections);
                Entry.FacilityUseFeeDescription = Entry.FacilityUseFeeDescription == null ? null : HtmlToPlainText(Entry.FacilityUseFeeDescription);
            }
            return Entry;
        }

        // Every reservable, enabled campground, for the search editor's campground picker (CM6, decision D44; TD-53, open
        // question #41): one query, one entry per FacilityID in CampgroundChoice.ListOrder, each with its location as the
        // lookup tabs show it (a Physical, else a Default address with a state; none without) and its park's name (none
        // without a named park). Unlike the tabs' lists, neither a location nor a park is required, so the campgrounds
        // with neither (22 in the shipped database, all 8 blank-name ones among them) are listed too. Nothing is invented
        // for a campground without a park or an address.
        public static List<CampgroundPickerEntry> CampgroundPickerEntries(String ConfigPath)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                var Qualifying = db.FacilityAddressesEntries.Where(FacilityAddressesEntries =>
                    FacilityAddressesEntries.AddressStateCode != null &&
                    FacilityAddressesEntries.AddressStateCode.Trim().Length > 0 &&
                    (FacilityAddressesEntries.FacilityAddressType == "Physical" || FacilityAddressesEntries.FacilityAddressType == "Default"));
                List<ChoiceRow> Rows = (from FacilitiesEntries in db.FacilitiesEntries
                                        join RecAreaEntries in db.RecAreaEntries on FacilitiesEntries.ParentRecAreaID equals RecAreaEntries.RecAreaID into Parks
                                        from RecAreaEntries in Parks.DefaultIfEmpty()
                                        join FacilityAddressesEntries in Qualifying on FacilitiesEntries.FacilityID equals FacilityAddressesEntries.FacilityID into Addresses
                                        from FacilityAddressesEntries in Addresses.DefaultIfEmpty()
                                        where FacilitiesEntries.FacilityTypeDescription == CampgroundType &&
                                        FacilitiesEntries.Reservable == true &&
                                        FacilitiesEntries.Enabled == true
                                        select new ChoiceRow
                                        {
                                            FacilityID = FacilitiesEntries.FacilityID,
                                            FacilityName = FacilitiesEntries.FacilityName,
                                            City = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.City,
                                            State = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.AddressStateCode,
                                            AddressType = FacilityAddressesEntries == null ? null : FacilityAddressesEntries.FacilityAddressType,
                                            Park = RecAreaEntries == null ? null : RecAreaEntries.RecAreaName
                                        }).ToList();
                return Rows.Where(Row => !string.IsNullOrWhiteSpace(Row.FacilityID))
                    .GroupBy(Row => Row.FacilityID!, StringComparer.Ordinal)
                    .Select(Group =>
                    {
                        ChoiceRow Row = Located(Group);
                        return new CampgroundPickerEntry(new CampgroundChoice(Row.FacilityID!, Row.FacilityName, Row.City, Row.State), Row.Park);
                    })
                    .OrderBy(Entry => Entry.Choice, CampgroundChoice.ListOrder)
                    .ToList();
            }
        }

        // a row of a choice query: one campground with one of its addresses (or none), and its park in the picker's query
        private sealed class ChoiceRow
        {
            internal string? FacilityID { get; init; }
            internal string? FacilityName { get; init; }
            internal string? City { get; init; }
            internal string? State { get; init; }
            internal string? AddressType { get; init; }
            internal string? Park { get; init; }
        }

        // one choice per FacilityID: a campground with two addresses (162 have a Physical and a Default one) is listed
        // once, located by its Physical address if it has one, else by state and city (ordinal), so the result is fixed
        private static List<CampgroundChoice> Choices(List<ChoiceRow> Rows)
        {
            return Rows.Where(Row => !string.IsNullOrWhiteSpace(Row.FacilityID))
                .GroupBy(Row => Row.FacilityID!, StringComparer.Ordinal)
                .Select(Located)
                .Select(Row => new CampgroundChoice(Row.FacilityID!, Row.FacilityName, Row.City, Row.State))
                .OrderBy(Choice => Choice, CampgroundChoice.ListOrder)
                .ToList();
        }

        // the row of one campground whose address locates it
        private static ChoiceRow Located(IEnumerable<ChoiceRow> Group)
        {
            return Group
                .OrderBy(Row => Row.AddressType == "Physical" ? 0 : Row.AddressType == null ? 2 : 1)
                .ThenBy(Row => Row.State ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(Row => Row.City ?? string.Empty, StringComparer.Ordinal)
                .First();
        }

        public static List<string> GetCampsiteIdsByPark(string Facility, String ConfigPath)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from CampsitesEntries in db.CampsitesEntries
                              where CampsitesEntries.FacilityID == Facility
                              select CampsitesEntries.CampsiteID).Distinct().OrderBy(p => p).ToList();
            };
            return ReturnList;
        }
        public static List<CampsitesRecdata> GetCampsitesByPark(string ParkID, String ConfigPath)
        {
            var ReturnList = new List<CampsitesRecdata>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from CampsitesEntries in db.CampsitesEntries
                              where CampsitesEntries.FacilityID == ParkID
                              select CampsitesEntries).ToList();
            };
            return ReturnList;
        }
        public static ReturnParkCampground GetParkCampgroundInfo(string CampsiteID, String ConfigPath)
        {
            ReturnParkCampground ReturnInfo = new ReturnParkCampground();
            using (var db = new RecreationDotOrgContext(ConfigPath))
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
        public static string GetParkCampgroundIdByName(string CampsiteName, String ConfigPath)
        {
            string ReturnStr = string.Empty;
            if (CampsiteName != null)
            {
                using (var db = new RecreationDotOrgContext(ConfigPath))
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
        public static FacilitiesData? GetParkCampgroundByName(string CampsiteName, String ConfigPath)
        {
            FacilitiesData? ReturnEntry = new FacilitiesData();
            if (CampsiteName != null)
            {
                ReturnEntry = FindReservableFacilityByName(CampsiteName, ConfigPath) ?? new FacilitiesData();
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
        // Presentation-neutral counterpart of GetParkCampgroundByName. Same lookup (first reservable, enabled facility,
        // of any type, whose trimmed, upper-cased name matches), but the values are returned without label prefixes and
        // without the "Overview" -> "Description:" rewrite. Only the HTML fields are converted to plain text, as in
        // GetParkCampgroundByName. Returns null when no name is given or nothing matches. A name does not identify a
        // campground (TD-38): the GUI uses GetCampgroundDetailsById since D42; kept unchanged for compatibility.
        public static FacilitiesData? GetCampgroundDetailsByName(string? CampsiteName, String ConfigPath)
        {
            if (CampsiteName == null)
            {
                return null;
            }
            FacilitiesData? Entry = FindReservableFacilityByName(CampsiteName, ConfigPath);
            if (Entry != null)
            {
                Entry.FacilityDescription = Entry.FacilityDescription == null ? null : HtmlToPlainText(Entry.FacilityDescription);
                Entry.FacilityDirections = Entry.FacilityDirections == null ? null : HtmlToPlainText(Entry.FacilityDirections);
                Entry.FacilityUseFeeDescription = Entry.FacilityUseFeeDescription == null ? null : HtmlToPlainText(Entry.FacilityUseFeeDescription);
            }
            return Entry;
        }
        private static FacilitiesData? FindReservableFacilityByName(string CampsiteName, String ConfigPath)
        {
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                return (from FacilitiesEntry in db.FacilitiesEntries
                        where FacilitiesEntry.FacilityName.ToUpper().Trim() == CampsiteName.ToUpper().Trim() &&
                        FacilitiesEntry.Reservable == true &&
                        FacilitiesEntry.Enabled == true
                        select FacilitiesEntry).FirstOrDefault();
            }
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
        public static List<string> GetPermittedEquipmentByCampsite(string CampsiteID, String ConfigPath)
        {
            var ReturnList = new List<string>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
            {
                ReturnList = (from PermittedEquipmentEntries in db.PermittedEquipmentEntries
                              where PermittedEquipmentEntries.CampsiteID == CampsiteID
                              select PermittedEquipmentEntries.EquipmentName).ToList();
            };
            return ReturnList;
        }
        public static List<AttributeValuePair> GetCampSiteAttributesByCampsite(string CampsiteID, String ConfigPath)
        {
            var ReturnList = new List<AttributeValuePair>();
            using (var db = new RecreationDotOrgContext(ConfigPath))
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