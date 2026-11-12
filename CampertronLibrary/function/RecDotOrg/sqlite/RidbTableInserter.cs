using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    // Inserts the six retained RIDB tables for PopulateDbFromRIDBFiles.Populate with prepared, parameterized
    // Microsoft.Data.Sqlite commands instead of EF Core Add/SaveChanges (decision D25, build-and-test.md §M).
    // EF Core's SQLite provider executes one command per row inside SaveChanges; for the ~3 million RIDB rows
    // that pipeline, not SQLite, took about 54 of the 60 seconds of the save.
    //
    // The result must stay identical to what SaveChanges wrote, down to the rowid order:
    //  - all six tables are inserted in one transaction on the context's own connection, as SaveChanges did;
    //    nothing is committed unless every table is inserted (a failure rolls back every insert);
    //  - tables are inserted in ordinal table-name order, and the rows of each table are sorted by the
    //    complete primary key, component by component in the key order of the EF model, with
    //    Comparer<T>.Default for each component. That is the order EF Core's SaveChanges uses. For strings
    //    Comparer<string>.Default is culture-sensitive (the current culture), so the physical row order
    //    depends on the culture, exactly as it did with SaveChanges (TD-36);
    //  - columns, SQLite types and nullability are those of the EF model. PopulateTests compares these
    //    hand-typed mappings with the model, so a model change without a matching change here fails a test.
    // Null rows and invalid values are not skipped or coerced: a null row throws, and SQLite's NOT NULL and
    // PRIMARY KEY constraints reject null or duplicate keys.
    public static class RidbTableInserter
    {
        // One inserted column: name, SQLite storage type and whether the column accepts NULL.
        public sealed record Column(string Name, SqliteType Type, bool Nullable);

        // Table name, inserted columns (in INSERT order), primary-key columns (in key order) and the INSERT
        // statement. Public so that PopulateTests can compare every mapping with the EF model.
        public sealed record TableMapping(string Table, IReadOnlyList<Column> Columns, IReadOnlyList<string> KeyColumns, string InsertSql);

        public static readonly TableMapping CampsiteAttributes = new TableMapping(
            "CampsiteAttributesEntries",
            new[]
            {
                new Column("AttributeID", SqliteType.Integer, false),
                new Column("EntityID", SqliteType.Text, false),
                new Column("AttributeName", SqliteType.Text, true),
                new Column("AttributeValue", SqliteType.Text, true),
                new Column("EntityType", SqliteType.Text, true)
            },
            new[] { "AttributeID", "EntityID" },
            "INSERT INTO \"CampsiteAttributesEntries\" (\"AttributeID\", \"EntityID\", \"AttributeName\", \"AttributeValue\", \"EntityType\") " +
            "VALUES ($AttributeID, $EntityID, $AttributeName, $AttributeValue, $EntityType)");

        public static readonly TableMapping Campsites = new TableMapping(
            "CampsitesEntries",
            new[]
            {
                new Column("CampsiteID", SqliteType.Text, false),
                new Column("FacilityID", SqliteType.Text, true),
                new Column("CampsiteName", SqliteType.Text, true),
                new Column("CampsiteType", SqliteType.Text, true),
                new Column("TypeOfUse", SqliteType.Text, true),
                new Column("Loop", SqliteType.Text, true),
                new Column("CampsiteAccessible", SqliteType.Integer, false),
                new Column("CampsiteLongitude", SqliteType.Real, false),
                new Column("CampsiteLatitude", SqliteType.Real, false),
                new Column("CreatedDate", SqliteType.Text, true),
                new Column("LastUpdatedDate", SqliteType.Text, true)
            },
            new[] { "CampsiteID" },
            "INSERT INTO \"CampsitesEntries\" (\"CampsiteID\", \"FacilityID\", \"CampsiteName\", \"CampsiteType\", \"TypeOfUse\", \"Loop\", " +
            "\"CampsiteAccessible\", \"CampsiteLongitude\", \"CampsiteLatitude\", \"CreatedDate\", \"LastUpdatedDate\") " +
            "VALUES ($CampsiteID, $FacilityID, $CampsiteName, $CampsiteType, $TypeOfUse, $Loop, " +
            "$CampsiteAccessible, $CampsiteLongitude, $CampsiteLatitude, $CreatedDate, $LastUpdatedDate)");

        public static readonly TableMapping Facilities = new TableMapping(
            "FacilitiesEntries",
            new[]
            {
                new Column("FacilityID", SqliteType.Text, false),
                new Column("LegacyFacilityID", SqliteType.Text, true),
                new Column("OrgFacilityID", SqliteType.Text, true),
                new Column("ParentOrgID", SqliteType.Text, true),
                new Column("ParentRecAreaID", SqliteType.Text, true),
                new Column("FacilityName", SqliteType.Text, true),
                new Column("FacilityDescription", SqliteType.Text, true),
                new Column("FacilityTypeDescription", SqliteType.Text, true),
                new Column("FacilityUseFeeDescription", SqliteType.Text, true),
                new Column("FacilityDirections", SqliteType.Text, true),
                new Column("FacilityPhone", SqliteType.Text, true),
                new Column("FacilityEmail", SqliteType.Text, true),
                new Column("FacilityReservationURL", SqliteType.Text, true),
                new Column("FacilityMapURL", SqliteType.Text, true),
                new Column("FacilityAdaAccess", SqliteType.Text, true),
                new Column("FacilityLongitude", SqliteType.Real, false),
                new Column("FacilityLatitude", SqliteType.Real, false),
                new Column("Keywords", SqliteType.Text, true),
                new Column("StayLimit", SqliteType.Text, true),
                new Column("Reservable", SqliteType.Integer, false),
                new Column("Enabled", SqliteType.Integer, false),
                new Column("LastUpdatedDate", SqliteType.Text, true)
            },
            new[] { "FacilityID" },
            "INSERT INTO \"FacilitiesEntries\" (\"FacilityID\", \"LegacyFacilityID\", \"OrgFacilityID\", \"ParentOrgID\", \"ParentRecAreaID\", " +
            "\"FacilityName\", \"FacilityDescription\", \"FacilityTypeDescription\", \"FacilityUseFeeDescription\", \"FacilityDirections\", " +
            "\"FacilityPhone\", \"FacilityEmail\", \"FacilityReservationURL\", \"FacilityMapURL\", \"FacilityAdaAccess\", " +
            "\"FacilityLongitude\", \"FacilityLatitude\", \"Keywords\", \"StayLimit\", \"Reservable\", \"Enabled\", \"LastUpdatedDate\") " +
            "VALUES ($FacilityID, $LegacyFacilityID, $OrgFacilityID, $ParentOrgID, $ParentRecAreaID, " +
            "$FacilityName, $FacilityDescription, $FacilityTypeDescription, $FacilityUseFeeDescription, $FacilityDirections, " +
            "$FacilityPhone, $FacilityEmail, $FacilityReservationURL, $FacilityMapURL, $FacilityAdaAccess, " +
            "$FacilityLongitude, $FacilityLatitude, $Keywords, $StayLimit, $Reservable, $Enabled, $LastUpdatedDate)");

        public static readonly TableMapping FacilityAddresses = new TableMapping(
            "FacilityAddressesEntries",
            new[]
            {
                new Column("FacilityAddressID", SqliteType.Text, false),
                new Column("FacilityID", SqliteType.Text, true),
                new Column("FacilityAddressType", SqliteType.Text, true),
                new Column("FacilityStreetAddress1", SqliteType.Text, true),
                new Column("FacilityStreetAddress2", SqliteType.Text, true),
                new Column("FacilityStreetAddress3", SqliteType.Text, true),
                new Column("City", SqliteType.Text, true),
                new Column("PostalCode", SqliteType.Text, true),
                new Column("AddressStateCode", SqliteType.Text, true),
                new Column("AddressCountryCode", SqliteType.Text, true),
                new Column("LastUpdatedDate", SqliteType.Text, true)
            },
            new[] { "FacilityAddressID" },
            "INSERT INTO \"FacilityAddressesEntries\" (\"FacilityAddressID\", \"FacilityID\", \"FacilityAddressType\", " +
            "\"FacilityStreetAddress1\", \"FacilityStreetAddress2\", \"FacilityStreetAddress3\", \"City\", \"PostalCode\", " +
            "\"AddressStateCode\", \"AddressCountryCode\", \"LastUpdatedDate\") " +
            "VALUES ($FacilityAddressID, $FacilityID, $FacilityAddressType, " +
            "$FacilityStreetAddress1, $FacilityStreetAddress2, $FacilityStreetAddress3, $City, $PostalCode, " +
            "$AddressStateCode, $AddressCountryCode, $LastUpdatedDate)");

        public static readonly TableMapping PermittedEquipment = new TableMapping(
            "PermittedEquipmentEntries",
            new[]
            {
                new Column("EquipmentName", SqliteType.Text, false),
                new Column("CampsiteID", SqliteType.Text, false),
                new Column("MaxLength", SqliteType.Real, true)
            },
            new[] { "CampsiteID", "EquipmentName" },
            "INSERT INTO \"PermittedEquipmentEntries\" (\"EquipmentName\", \"CampsiteID\", \"MaxLength\") " +
            "VALUES ($EquipmentName, $CampsiteID, $MaxLength)");

        public static readonly TableMapping RecAreas = new TableMapping(
            "RecAreaEntries",
            new[]
            {
                new Column("RecAreaID", SqliteType.Text, false),
                new Column("OrgRecAreaID", SqliteType.Text, true),
                new Column("ParentOrgID", SqliteType.Text, true),
                new Column("RecAreaName", SqliteType.Text, true),
                new Column("RecAreaDescription", SqliteType.Text, true),
                new Column("RecAreaFeeDescription", SqliteType.Text, true),
                new Column("RecAreaDirections", SqliteType.Text, true),
                new Column("RecAreaPhone", SqliteType.Text, true),
                new Column("RecAreaEmail", SqliteType.Text, true),
                new Column("RecAreaReservationURL", SqliteType.Text, true),
                new Column("RecAreaMapURL", SqliteType.Text, true),
                new Column("RecAreaLongitude", SqliteType.Real, false),
                new Column("RecAreaLatitude", SqliteType.Real, false),
                new Column("StayLimit", SqliteType.Text, true),
                new Column("Keywords", SqliteType.Text, true),
                new Column("Reservable", SqliteType.Integer, false),
                new Column("Enabled", SqliteType.Integer, false),
                new Column("LastUpdatedDate", SqliteType.Text, true)
            },
            new[] { "RecAreaID" },
            "INSERT INTO \"RecAreaEntries\" (\"RecAreaID\", \"OrgRecAreaID\", \"ParentOrgID\", \"RecAreaName\", \"RecAreaDescription\", " +
            "\"RecAreaFeeDescription\", \"RecAreaDirections\", \"RecAreaPhone\", \"RecAreaEmail\", \"RecAreaReservationURL\", \"RecAreaMapURL\", " +
            "\"RecAreaLongitude\", \"RecAreaLatitude\", \"StayLimit\", \"Keywords\", \"Reservable\", \"Enabled\", \"LastUpdatedDate\") " +
            "VALUES ($RecAreaID, $OrgRecAreaID, $ParentOrgID, $RecAreaName, $RecAreaDescription, " +
            "$RecAreaFeeDescription, $RecAreaDirections, $RecAreaPhone, $RecAreaEmail, $RecAreaReservationURL, $RecAreaMapURL, " +
            "$RecAreaLongitude, $RecAreaLatitude, $StayLimit, $Keywords, $Reservable, $Enabled, $LastUpdatedDate)");

        // In ordinal table-name order, the order in which the tables are inserted.
        public static readonly IReadOnlyList<TableMapping> Mappings = new[]
        {
            CampsiteAttributes, Campsites, Facilities, FacilityAddresses, PermittedEquipment, RecAreas
        };

        // Inserts all rows in one transaction on the context's connection and commits only after the last
        // table. Any exception rolls the transaction back and propagates.
        public static void InsertAll(RecreationDotOrgContext Db,
            List<CampsiteAttributesRecdata> CampsiteAttributesRows,
            List<CampsitesRecdata> CampsitesRows,
            List<FacilitiesData> FacilitiesRows,
            List<FacilityAddressesRecdata> FacilityAddressesRows,
            List<PermittedEquipmentRecdata> PermittedEquipmentRows,
            List<RecAreaFacilityRecData> RecAreasRows)
        {
            Db.Database.OpenConnection();
            try
            {
                var Connection = (SqliteConnection)Db.Database.GetDbConnection();
                using (SqliteTransaction Transaction = Connection.BeginTransaction())
                {
                    InsertCampsiteAttributes(Connection, Transaction, CampsiteAttributesRows);
                    InsertCampsites(Connection, Transaction, CampsitesRows);
                    InsertFacilities(Connection, Transaction, FacilitiesRows);
                    InsertFacilityAddresses(Connection, Transaction, FacilityAddressesRows);
                    InsertPermittedEquipment(Connection, Transaction, PermittedEquipmentRows);
                    InsertRecAreas(Connection, Transaction, RecAreasRows);
                    Transaction.Commit();
                }
            }
            finally
            {
                Db.Database.CloseConnection();
            }
        }

        private static void InsertCampsiteAttributes(SqliteConnection Connection, SqliteTransaction Transaction, List<CampsiteAttributesRecdata> Rows)
        {
            CampsiteAttributesRecdata[] Sorted = SortByKey(Rows, CampsiteAttributes, (x, y) =>
            {
                int Result = Comparer<int?>.Default.Compare(x.AttributeID, y.AttributeID);
                return Result != 0 ? Result : Comparer<string?>.Default.Compare(x.EntityID, y.EntityID);
            });
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, CampsiteAttributes))
            {
                SqliteParameter AttributeID = Command.Parameters["$AttributeID"];
                SqliteParameter EntityID = Command.Parameters["$EntityID"];
                SqliteParameter AttributeName = Command.Parameters["$AttributeName"];
                SqliteParameter AttributeValue = Command.Parameters["$AttributeValue"];
                SqliteParameter EntityType = Command.Parameters["$EntityType"];
                foreach (CampsiteAttributesRecdata Row in Sorted)
                {
                    AttributeID.Value = (object?)Row.AttributeID ?? DBNull.Value;
                    EntityID.Value = (object?)Row.EntityID ?? DBNull.Value;
                    AttributeName.Value = (object?)Row.AttributeName ?? DBNull.Value;
                    AttributeValue.Value = (object?)Row.AttributeValue ?? DBNull.Value;
                    EntityType.Value = (object?)Row.EntityType ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertCampsites(SqliteConnection Connection, SqliteTransaction Transaction, List<CampsitesRecdata> Rows)
        {
            CampsitesRecdata[] Sorted = SortByKey(Rows, Campsites, (x, y) => Comparer<string?>.Default.Compare(x.CampsiteID, y.CampsiteID));
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, Campsites))
            {
                SqliteParameter CampsiteID = Command.Parameters["$CampsiteID"];
                SqliteParameter FacilityID = Command.Parameters["$FacilityID"];
                SqliteParameter CampsiteName = Command.Parameters["$CampsiteName"];
                SqliteParameter CampsiteType = Command.Parameters["$CampsiteType"];
                SqliteParameter TypeOfUse = Command.Parameters["$TypeOfUse"];
                SqliteParameter Loop = Command.Parameters["$Loop"];
                SqliteParameter CampsiteAccessible = Command.Parameters["$CampsiteAccessible"];
                SqliteParameter CampsiteLongitude = Command.Parameters["$CampsiteLongitude"];
                SqliteParameter CampsiteLatitude = Command.Parameters["$CampsiteLatitude"];
                SqliteParameter CreatedDate = Command.Parameters["$CreatedDate"];
                SqliteParameter LastUpdatedDate = Command.Parameters["$LastUpdatedDate"];
                foreach (CampsitesRecdata Row in Sorted)
                {
                    CampsiteID.Value = (object?)Row.CampsiteID ?? DBNull.Value;
                    FacilityID.Value = (object?)Row.FacilityID ?? DBNull.Value;
                    CampsiteName.Value = (object?)Row.CampsiteName ?? DBNull.Value;
                    CampsiteType.Value = (object?)Row.CampsiteType ?? DBNull.Value;
                    TypeOfUse.Value = (object?)Row.TypeOfUse ?? DBNull.Value;
                    Loop.Value = (object?)Row.Loop ?? DBNull.Value;
                    CampsiteAccessible.Value = Row.CampsiteAccessible;
                    CampsiteLongitude.Value = Row.CampsiteLongitude;
                    CampsiteLatitude.Value = Row.CampsiteLatitude;
                    CreatedDate.Value = (object?)Row.CreatedDate ?? DBNull.Value;
                    LastUpdatedDate.Value = (object?)Row.LastUpdatedDate ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertFacilities(SqliteConnection Connection, SqliteTransaction Transaction, List<FacilitiesData> Rows)
        {
            FacilitiesData[] Sorted = SortByKey(Rows, Facilities, (x, y) => Comparer<string?>.Default.Compare(x.FacilityID, y.FacilityID));
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, Facilities))
            {
                SqliteParameter FacilityID = Command.Parameters["$FacilityID"];
                SqliteParameter LegacyFacilityID = Command.Parameters["$LegacyFacilityID"];
                SqliteParameter OrgFacilityID = Command.Parameters["$OrgFacilityID"];
                SqliteParameter ParentOrgID = Command.Parameters["$ParentOrgID"];
                SqliteParameter ParentRecAreaID = Command.Parameters["$ParentRecAreaID"];
                SqliteParameter FacilityName = Command.Parameters["$FacilityName"];
                SqliteParameter FacilityDescription = Command.Parameters["$FacilityDescription"];
                SqliteParameter FacilityTypeDescription = Command.Parameters["$FacilityTypeDescription"];
                SqliteParameter FacilityUseFeeDescription = Command.Parameters["$FacilityUseFeeDescription"];
                SqliteParameter FacilityDirections = Command.Parameters["$FacilityDirections"];
                SqliteParameter FacilityPhone = Command.Parameters["$FacilityPhone"];
                SqliteParameter FacilityEmail = Command.Parameters["$FacilityEmail"];
                SqliteParameter FacilityReservationURL = Command.Parameters["$FacilityReservationURL"];
                SqliteParameter FacilityMapURL = Command.Parameters["$FacilityMapURL"];
                SqliteParameter FacilityAdaAccess = Command.Parameters["$FacilityAdaAccess"];
                SqliteParameter FacilityLongitude = Command.Parameters["$FacilityLongitude"];
                SqliteParameter FacilityLatitude = Command.Parameters["$FacilityLatitude"];
                SqliteParameter Keywords = Command.Parameters["$Keywords"];
                SqliteParameter StayLimit = Command.Parameters["$StayLimit"];
                SqliteParameter Reservable = Command.Parameters["$Reservable"];
                SqliteParameter Enabled = Command.Parameters["$Enabled"];
                SqliteParameter LastUpdatedDate = Command.Parameters["$LastUpdatedDate"];
                foreach (FacilitiesData Row in Sorted)
                {
                    FacilityID.Value = (object?)Row.FacilityID ?? DBNull.Value;
                    LegacyFacilityID.Value = (object?)Row.LegacyFacilityID ?? DBNull.Value;
                    OrgFacilityID.Value = (object?)Row.OrgFacilityID ?? DBNull.Value;
                    ParentOrgID.Value = (object?)Row.ParentOrgID ?? DBNull.Value;
                    ParentRecAreaID.Value = (object?)Row.ParentRecAreaID ?? DBNull.Value;
                    FacilityName.Value = (object?)Row.FacilityName ?? DBNull.Value;
                    FacilityDescription.Value = (object?)Row.FacilityDescription ?? DBNull.Value;
                    FacilityTypeDescription.Value = (object?)Row.FacilityTypeDescription ?? DBNull.Value;
                    FacilityUseFeeDescription.Value = (object?)Row.FacilityUseFeeDescription ?? DBNull.Value;
                    FacilityDirections.Value = (object?)Row.FacilityDirections ?? DBNull.Value;
                    FacilityPhone.Value = (object?)Row.FacilityPhone ?? DBNull.Value;
                    FacilityEmail.Value = (object?)Row.FacilityEmail ?? DBNull.Value;
                    FacilityReservationURL.Value = (object?)Row.FacilityReservationURL ?? DBNull.Value;
                    FacilityMapURL.Value = (object?)Row.FacilityMapURL ?? DBNull.Value;
                    FacilityAdaAccess.Value = (object?)Row.FacilityAdaAccess ?? DBNull.Value;
                    FacilityLongitude.Value = Row.FacilityLongitude;
                    FacilityLatitude.Value = Row.FacilityLatitude;
                    Keywords.Value = (object?)Row.Keywords ?? DBNull.Value;
                    StayLimit.Value = (object?)Row.StayLimit ?? DBNull.Value;
                    Reservable.Value = Row.Reservable;
                    Enabled.Value = Row.Enabled;
                    LastUpdatedDate.Value = (object?)Row.LastUpdatedDate ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertFacilityAddresses(SqliteConnection Connection, SqliteTransaction Transaction, List<FacilityAddressesRecdata> Rows)
        {
            FacilityAddressesRecdata[] Sorted = SortByKey(Rows, FacilityAddresses, (x, y) => Comparer<string?>.Default.Compare(x.FacilityAddressID, y.FacilityAddressID));
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, FacilityAddresses))
            {
                SqliteParameter FacilityAddressID = Command.Parameters["$FacilityAddressID"];
                SqliteParameter FacilityID = Command.Parameters["$FacilityID"];
                SqliteParameter FacilityAddressType = Command.Parameters["$FacilityAddressType"];
                SqliteParameter FacilityStreetAddress1 = Command.Parameters["$FacilityStreetAddress1"];
                SqliteParameter FacilityStreetAddress2 = Command.Parameters["$FacilityStreetAddress2"];
                SqliteParameter FacilityStreetAddress3 = Command.Parameters["$FacilityStreetAddress3"];
                SqliteParameter City = Command.Parameters["$City"];
                SqliteParameter PostalCode = Command.Parameters["$PostalCode"];
                SqliteParameter AddressStateCode = Command.Parameters["$AddressStateCode"];
                SqliteParameter AddressCountryCode = Command.Parameters["$AddressCountryCode"];
                SqliteParameter LastUpdatedDate = Command.Parameters["$LastUpdatedDate"];
                foreach (FacilityAddressesRecdata Row in Sorted)
                {
                    FacilityAddressID.Value = (object?)Row.FacilityAddressID ?? DBNull.Value;
                    FacilityID.Value = (object?)Row.FacilityID ?? DBNull.Value;
                    FacilityAddressType.Value = (object?)Row.FacilityAddressType ?? DBNull.Value;
                    FacilityStreetAddress1.Value = (object?)Row.FacilityStreetAddress1 ?? DBNull.Value;
                    FacilityStreetAddress2.Value = (object?)Row.FacilityStreetAddress2 ?? DBNull.Value;
                    FacilityStreetAddress3.Value = (object?)Row.FacilityStreetAddress3 ?? DBNull.Value;
                    City.Value = (object?)Row.City ?? DBNull.Value;
                    PostalCode.Value = (object?)Row.PostalCode ?? DBNull.Value;
                    AddressStateCode.Value = (object?)Row.AddressStateCode ?? DBNull.Value;
                    AddressCountryCode.Value = (object?)Row.AddressCountryCode ?? DBNull.Value;
                    LastUpdatedDate.Value = (object?)Row.LastUpdatedDate ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertPermittedEquipment(SqliteConnection Connection, SqliteTransaction Transaction, List<PermittedEquipmentRecdata> Rows)
        {
            PermittedEquipmentRecdata[] Sorted = SortByKey(Rows, PermittedEquipment, (x, y) =>
            {
                int Result = Comparer<string?>.Default.Compare(x.CampsiteID, y.CampsiteID);
                return Result != 0 ? Result : Comparer<string?>.Default.Compare(x.EquipmentName, y.EquipmentName);
            });
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, PermittedEquipment))
            {
                SqliteParameter EquipmentName = Command.Parameters["$EquipmentName"];
                SqliteParameter CampsiteID = Command.Parameters["$CampsiteID"];
                SqliteParameter MaxLength = Command.Parameters["$MaxLength"];
                foreach (PermittedEquipmentRecdata Row in Sorted)
                {
                    EquipmentName.Value = (object?)Row.EquipmentName ?? DBNull.Value;
                    CampsiteID.Value = (object?)Row.CampsiteID ?? DBNull.Value;
                    MaxLength.Value = (object?)Row.MaxLength ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        private static void InsertRecAreas(SqliteConnection Connection, SqliteTransaction Transaction, List<RecAreaFacilityRecData> Rows)
        {
            RecAreaFacilityRecData[] Sorted = SortByKey(Rows, RecAreas, (x, y) => Comparer<string?>.Default.Compare(x.RecAreaID, y.RecAreaID));
            using (SqliteCommand Command = PrepareCommand(Connection, Transaction, RecAreas))
            {
                SqliteParameter RecAreaID = Command.Parameters["$RecAreaID"];
                SqliteParameter OrgRecAreaID = Command.Parameters["$OrgRecAreaID"];
                SqliteParameter ParentOrgID = Command.Parameters["$ParentOrgID"];
                SqliteParameter RecAreaName = Command.Parameters["$RecAreaName"];
                SqliteParameter RecAreaDescription = Command.Parameters["$RecAreaDescription"];
                SqliteParameter RecAreaFeeDescription = Command.Parameters["$RecAreaFeeDescription"];
                SqliteParameter RecAreaDirections = Command.Parameters["$RecAreaDirections"];
                SqliteParameter RecAreaPhone = Command.Parameters["$RecAreaPhone"];
                SqliteParameter RecAreaEmail = Command.Parameters["$RecAreaEmail"];
                SqliteParameter RecAreaReservationURL = Command.Parameters["$RecAreaReservationURL"];
                SqliteParameter RecAreaMapURL = Command.Parameters["$RecAreaMapURL"];
                SqliteParameter RecAreaLongitude = Command.Parameters["$RecAreaLongitude"];
                SqliteParameter RecAreaLatitude = Command.Parameters["$RecAreaLatitude"];
                SqliteParameter StayLimit = Command.Parameters["$StayLimit"];
                SqliteParameter Keywords = Command.Parameters["$Keywords"];
                SqliteParameter Reservable = Command.Parameters["$Reservable"];
                SqliteParameter Enabled = Command.Parameters["$Enabled"];
                SqliteParameter LastUpdatedDate = Command.Parameters["$LastUpdatedDate"];
                foreach (RecAreaFacilityRecData Row in Sorted)
                {
                    RecAreaID.Value = (object?)Row.RecAreaID ?? DBNull.Value;
                    OrgRecAreaID.Value = (object?)Row.OrgRecAreaID ?? DBNull.Value;
                    ParentOrgID.Value = (object?)Row.ParentOrgID ?? DBNull.Value;
                    RecAreaName.Value = (object?)Row.RecAreaName ?? DBNull.Value;
                    RecAreaDescription.Value = (object?)Row.RecAreaDescription ?? DBNull.Value;
                    RecAreaFeeDescription.Value = (object?)Row.RecAreaFeeDescription ?? DBNull.Value;
                    RecAreaDirections.Value = (object?)Row.RecAreaDirections ?? DBNull.Value;
                    RecAreaPhone.Value = (object?)Row.RecAreaPhone ?? DBNull.Value;
                    RecAreaEmail.Value = (object?)Row.RecAreaEmail ?? DBNull.Value;
                    RecAreaReservationURL.Value = (object?)Row.RecAreaReservationURL ?? DBNull.Value;
                    RecAreaMapURL.Value = (object?)Row.RecAreaMapURL ?? DBNull.Value;
                    RecAreaLongitude.Value = Row.RecAreaLongitude;
                    RecAreaLatitude.Value = Row.RecAreaLatitude;
                    StayLimit.Value = (object?)Row.StayLimit ?? DBNull.Value;
                    Keywords.Value = (object?)Row.Keywords ?? DBNull.Value;
                    Reservable.Value = Row.Reservable;
                    Enabled.Value = Row.Enabled;
                    LastUpdatedDate.Value = (object?)Row.LastUpdatedDate ?? DBNull.Value;
                    Command.ExecuteNonQuery();
                }
            }
        }

        // Rejects null rows, then sorts a copy of the rows by the complete primary key (Array.Sort, as an
        // in-memory sort of the rows in their source order).
        private static T[] SortByKey<T>(List<T> Rows, TableMapping Mapping, Comparison<T> KeyOrder) where T : class
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i] == null)
                {
                    throw new InvalidDataException($"{Mapping.Table}: row {i} of the RIDB data is null");
                }
            }
            T[] Sorted = Rows.ToArray();
            Array.Sort(Sorted, KeyOrder);
            return Sorted;
        }

        // Creates the table's INSERT command on the transaction, with one parameter per column, and prepares it.
        // The parameters are created once and their values are replaced for every row.
        private static SqliteCommand PrepareCommand(SqliteConnection Connection, SqliteTransaction Transaction, TableMapping Mapping)
        {
            SqliteCommand Command = Connection.CreateCommand();
            Command.Transaction = Transaction;
            Command.CommandText = Mapping.InsertSql;
            foreach (Column ThisColumn in Mapping.Columns)
            {
                Command.Parameters.Add("$" + ThisColumn.Name, ThisColumn.Type);
            }
            Command.Prepare();
            return Command;
        }
    }
}
