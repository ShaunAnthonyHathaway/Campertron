using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CampertronLibrary.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityEntries",
                columns: table => new
                {
                    ActivityID = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityParentID = table.Column<int>(type: "INTEGER", nullable: true),
                    ActivityName = table.Column<string>(type: "TEXT", nullable: true),
                    ActivityLevel = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntries", x => x.ActivityID);
                });

            migrationBuilder.CreateTable(
                name: "CampsiteAttributesEntries",
                columns: table => new
                {
                    AttributeID = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    AttributeName = table.Column<string>(type: "TEXT", nullable: true),
                    AttributeValue = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampsiteAttributesEntries", x => new { x.AttributeID, x.EntityID });
                });

            migrationBuilder.CreateTable(
                name: "CampsitesEntries",
                columns: table => new
                {
                    CampsiteID = table.Column<string>(type: "TEXT", nullable: false),
                    FacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    CampsiteName = table.Column<string>(type: "TEXT", nullable: true),
                    CampsiteType = table.Column<string>(type: "TEXT", nullable: true),
                    TypeOfUse = table.Column<string>(type: "TEXT", nullable: true),
                    Loop = table.Column<string>(type: "TEXT", nullable: true),
                    CampsiteAccessible = table.Column<bool>(type: "INTEGER", nullable: false),
                    CampsiteLongitude = table.Column<double>(type: "REAL", nullable: false),
                    CampsiteLatitude = table.Column<double>(type: "REAL", nullable: false),
                    CreatedDate = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampsitesEntries", x => x.CampsiteID);
                });

            migrationBuilder.CreateTable(
                name: "EntityActivitiesEntries",
                columns: table => new
                {
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityID = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivityDescription = table.Column<string>(type: "TEXT", nullable: true),
                    ActivityFeeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityActivitiesEntries", x => new { x.EntityID, x.ActivityID });
                });

            migrationBuilder.CreateTable(
                name: "EventsEntries",
                columns: table => new
                {
                    EventID = table.Column<string>(type: "TEXT", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true),
                    EventName = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    EventTypeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    EventFeeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    EventFrequencyRateDescription = table.Column<string>(type: "TEXT", nullable: true),
                    EventScopeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    EventAgeGroup = table.Column<string>(type: "TEXT", nullable: true),
                    EventRegistrationRequired = table.Column<bool>(type: "INTEGER", nullable: true),
                    EventADAAccess = table.Column<string>(type: "TEXT", nullable: true),
                    EventComments = table.Column<string>(type: "TEXT", nullable: true),
                    EventEmail = table.Column<string>(type: "TEXT", nullable: true),
                    EventURLAddress = table.Column<string>(type: "TEXT", nullable: true),
                    EventURLText = table.Column<string>(type: "TEXT", nullable: true),
                    EventStartDate = table.Column<string>(type: "TEXT", nullable: true),
                    EventEndDate = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorName = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorClassType = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorPhone = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorEmail = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorURLAddress = table.Column<string>(type: "TEXT", nullable: true),
                    SponsorURLText = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventsEntries", x => x.EventID);
                });

            migrationBuilder.CreateTable(
                name: "FacilitiesEntries",
                columns: table => new
                {
                    FacilityID = table.Column<string>(type: "TEXT", nullable: false),
                    LegacyFacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    OrgFacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    ParentOrgID = table.Column<string>(type: "TEXT", nullable: true),
                    ParentRecAreaID = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityName = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityDescription = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityTypeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityUseFeeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityDirections = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityPhone = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityEmail = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityReservationURL = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityMapURL = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityAdaAccess = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityLongitude = table.Column<double>(type: "REAL", nullable: false),
                    FacilityLatitude = table.Column<double>(type: "REAL", nullable: false),
                    Keywords = table.Column<string>(type: "TEXT", nullable: true),
                    StayLimit = table.Column<string>(type: "TEXT", nullable: true),
                    Reservable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilitiesEntries", x => x.FacilityID);
                });

            migrationBuilder.CreateTable(
                name: "FacilityAddressesEntries",
                columns: table => new
                {
                    FacilityAddressID = table.Column<string>(type: "TEXT", nullable: false),
                    FacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityAddressType = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityStreetAddress1 = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityStreetAddress2 = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityStreetAddress3 = table.Column<string>(type: "TEXT", nullable: true),
                    City = table.Column<string>(type: "TEXT", nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: true),
                    AddressStateCode = table.Column<string>(type: "TEXT", nullable: true),
                    AddressCountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacilityAddressesEntries", x => x.FacilityAddressID);
                });

            migrationBuilder.CreateTable(
                name: "LinksEntries",
                columns: table => new
                {
                    EntityLinkID = table.Column<string>(type: "TEXT", nullable: false),
                    LinkType = table.Column<string>(type: "TEXT", nullable: true),
                    EntityID = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    URL = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinksEntries", x => x.EntityLinkID);
                });

            migrationBuilder.CreateTable(
                name: "MediaEntries",
                columns: table => new
                {
                    EntityMediaID = table.Column<string>(type: "TEXT", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    MediaType = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Subtitle = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    EmbedCode = table.Column<string>(type: "TEXT", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    URL = table.Column<string>(type: "TEXT", nullable: true),
                    Credits = table.Column<string>(type: "TEXT", nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsPreview = table.Column<bool>(type: "INTEGER", nullable: true),
                    IsGallery = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaEntries", x => new { x.EntityID, x.EntityMediaID });
                });

            migrationBuilder.CreateTable(
                name: "MemberToursEntries",
                columns: table => new
                {
                    MemberTourID = table.Column<string>(type: "TEXT", nullable: false),
                    TourName = table.Column<string>(type: "TEXT", nullable: true),
                    TourID = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberToursEntries", x => x.MemberTourID);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationEntries",
                columns: table => new
                {
                    OrgID = table.Column<string>(type: "TEXT", nullable: false),
                    OrgName = table.Column<string>(type: "TEXT", nullable: true),
                    OrgImageURL = table.Column<string>(type: "TEXT", nullable: true),
                    OrgURLText = table.Column<string>(type: "TEXT", nullable: true),
                    OrgURLAddress = table.Column<string>(type: "TEXT", nullable: true),
                    OrgType = table.Column<string>(type: "TEXT", nullable: true),
                    OrgAbbrevName = table.Column<string>(type: "TEXT", nullable: true),
                    OrgJurisdictionType = table.Column<string>(type: "TEXT", nullable: true),
                    OrgParentID = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationEntries", x => x.OrgID);
                });

            migrationBuilder.CreateTable(
                name: "OrgEntitiesEntries",
                columns: table => new
                {
                    OrgID = table.Column<string>(type: "TEXT", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgEntitiesEntries", x => new { x.EntityID, x.OrgID });
                });

            migrationBuilder.CreateTable(
                name: "PermitEntranceAttributesEntries",
                columns: table => new
                {
                    AttributeID = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    AttributeName = table.Column<string>(type: "TEXT", nullable: true),
                    AttributeValue = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitEntranceAttributesEntries", x => new { x.AttributeID, x.EntityID });
                });

            migrationBuilder.CreateTable(
                name: "PermitEntrancesEntries",
                columns: table => new
                {
                    PermitEntranceID = table.Column<string>(type: "TEXT", nullable: false),
                    PermitEntranceType = table.Column<string>(type: "TEXT", nullable: true),
                    FacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    PermitEntranceName = table.Column<string>(type: "TEXT", nullable: true),
                    PermitEntranceDescription = table.Column<string>(type: "TEXT", nullable: true),
                    District = table.Column<string>(type: "TEXT", nullable: true),
                    Town = table.Column<string>(type: "TEXT", nullable: true),
                    PermitEntranceAccessible = table.Column<bool>(type: "INTEGER", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    CreatedDate = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitEntrancesEntries", x => x.PermitEntranceID);
                });

            migrationBuilder.CreateTable(
                name: "PermitEntranceZonesEntries",
                columns: table => new
                {
                    PermitEntranceZoneID = table.Column<string>(type: "TEXT", nullable: false),
                    Zone = table.Column<string>(type: "TEXT", nullable: true),
                    PermitEntranceID = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermitEntranceZonesEntries", x => x.PermitEntranceZoneID);
                });

            migrationBuilder.CreateTable(
                name: "PermittedEquipmentEntries",
                columns: table => new
                {
                    EquipmentName = table.Column<string>(type: "TEXT", nullable: false),
                    CampsiteID = table.Column<string>(type: "TEXT", nullable: false),
                    MaxLength = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermittedEquipmentEntries", x => new { x.CampsiteID, x.EquipmentName });
                });

            migrationBuilder.CreateTable(
                name: "RecAreaAddressesEntries",
                columns: table => new
                {
                    RecAreaAddressID = table.Column<string>(type: "TEXT", nullable: false),
                    RecAreaID = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaAddressType = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaStreetAddress1 = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaStreetAddress2 = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaStreetAddress3 = table.Column<string>(type: "TEXT", nullable: true),
                    City = table.Column<string>(type: "TEXT", nullable: true),
                    PostalCode = table.Column<string>(type: "TEXT", nullable: true),
                    AddressStateCode = table.Column<string>(type: "TEXT", nullable: true),
                    AddressCountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecAreaAddressesEntries", x => x.RecAreaAddressID);
                });

            migrationBuilder.CreateTable(
                name: "RecAreaEntries",
                columns: table => new
                {
                    RecAreaID = table.Column<string>(type: "TEXT", nullable: false),
                    OrgRecAreaID = table.Column<string>(type: "TEXT", nullable: true),
                    ParentOrgID = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaName = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaDescription = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaFeeDescription = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaDirections = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaPhone = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaEmail = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaReservationURL = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaMapURL = table.Column<string>(type: "TEXT", nullable: true),
                    RecAreaLongitude = table.Column<double>(type: "REAL", nullable: false),
                    RecAreaLatitude = table.Column<double>(type: "REAL", nullable: false),
                    StayLimit = table.Column<string>(type: "TEXT", nullable: true),
                    Keywords = table.Column<string>(type: "TEXT", nullable: true),
                    Reservable = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecAreaEntries", x => x.RecAreaID);
                });

            migrationBuilder.CreateTable(
                name: "RecAreaFacilitiesEntries",
                columns: table => new
                {
                    RecAreaID = table.Column<string>(type: "TEXT", nullable: false),
                    FacilityID = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecAreaFacilitiesEntries", x => new { x.RecAreaID, x.FacilityID });
                });

            migrationBuilder.CreateTable(
                name: "TourAttributesEntries",
                columns: table => new
                {
                    AttributeID = table.Column<double>(type: "REAL", nullable: false),
                    EntityID = table.Column<string>(type: "TEXT", nullable: false),
                    AttributeName = table.Column<string>(type: "TEXT", nullable: true),
                    AttributeValue = table.Column<string>(type: "TEXT", nullable: true),
                    EntityType = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourAttributesEntries", x => new { x.AttributeID, x.EntityID });
                });

            migrationBuilder.CreateTable(
                name: "ToursEntries",
                columns: table => new
                {
                    TourID = table.Column<string>(type: "TEXT", nullable: false),
                    FacilityID = table.Column<string>(type: "TEXT", nullable: true),
                    TourName = table.Column<string>(type: "TEXT", nullable: true),
                    TourType = table.Column<string>(type: "TEXT", nullable: true),
                    TourDescription = table.Column<string>(type: "TEXT", nullable: true),
                    TourDuration = table.Column<double>(type: "REAL", nullable: true),
                    TourAccessible = table.Column<bool>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<string>(type: "TEXT", nullable: true),
                    LastUpdatedDate = table.Column<string>(type: "TEXT", nullable: true),
                    ATTRIBUTES = table.Column<string>(type: "TEXT", nullable: true),
                    MEMBERTOURS = table.Column<string>(type: "TEXT", nullable: true),
                    ENTITYMEDIA = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToursEntries", x => x.TourID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEntries");

            migrationBuilder.DropTable(
                name: "CampsiteAttributesEntries");

            migrationBuilder.DropTable(
                name: "CampsitesEntries");

            migrationBuilder.DropTable(
                name: "EntityActivitiesEntries");

            migrationBuilder.DropTable(
                name: "EventsEntries");

            migrationBuilder.DropTable(
                name: "FacilitiesEntries");

            migrationBuilder.DropTable(
                name: "FacilityAddressesEntries");

            migrationBuilder.DropTable(
                name: "LinksEntries");

            migrationBuilder.DropTable(
                name: "MediaEntries");

            migrationBuilder.DropTable(
                name: "MemberToursEntries");

            migrationBuilder.DropTable(
                name: "OrganizationEntries");

            migrationBuilder.DropTable(
                name: "OrgEntitiesEntries");

            migrationBuilder.DropTable(
                name: "PermitEntranceAttributesEntries");

            migrationBuilder.DropTable(
                name: "PermitEntrancesEntries");

            migrationBuilder.DropTable(
                name: "PermitEntranceZonesEntries");

            migrationBuilder.DropTable(
                name: "PermittedEquipmentEntries");

            migrationBuilder.DropTable(
                name: "RecAreaAddressesEntries");

            migrationBuilder.DropTable(
                name: "RecAreaEntries");

            migrationBuilder.DropTable(
                name: "RecAreaFacilitiesEntries");

            migrationBuilder.DropTable(
                name: "TourAttributesEntries");

            migrationBuilder.DropTable(
                name: "ToursEntries");
        }
    }
}
