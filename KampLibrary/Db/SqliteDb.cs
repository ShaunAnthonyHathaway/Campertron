using Microsoft.EntityFrameworkCore;

public class RecreationDotOrgContext : DbContext
{
    public DbSet<ActivityRecdata> ActivityEntries { get; set; }
    public DbSet<CampsiteAttributesRecdata> CampsiteAttributesEntries { get; set; }
    public DbSet<CampsitesRecdata> CampsitesEntries { get; set; }
    public DbSet<EntityActivitiesRecdata> EntityActivitiesEntries { get; set; }
    public DbSet<EventsRecdata> EventsEntries { get; set; }
    public DbSet<FacilitiesData> FacilitiesEntries { get; set; }
    public DbSet<FacilityAddressesRecdata> FacilityAddressesEntries { get; set; }
    public DbSet<LinksRecdata> LinksEntries { get; set; }
    public DbSet<MediaRecdata> MediaEntries { get; set; }
    public DbSet<MemberToursRecdata> MemberToursEntries { get; set; }
    public DbSet<OrganizationRecdata> OrganizationEntries { get; set; }
    public DbSet<OrgEntitiesRecdata> OrgEntitiesEntries { get; set; }
    public DbSet<PermitEntranceAttributesRecdata> PermitEntranceAttributesEntries { get; set; }
    public DbSet<PermitEntrancesRecdata> PermitEntrancesEntries { get; set; }
    public DbSet<PermitEntranceZonesRecdata> PermitEntranceZonesEntries { get; set; }
    public DbSet<PermittedEquipmentRecdata> PermittedEquipmentEntries { get; set; }
    public DbSet<RecAreaAddressesRecdata> RecAreaAddressesEntries { get; set; }
    public DbSet<RecAreaFacilitiesRecdata> RecAreaFacilitiesEntries { get; set; }
    public DbSet<RecAreaFacility> RecAreaEntries { get; set; }
    public DbSet<TourAttributesFacility> TourAttributesEntries { get; set; }
    public DbSet<ToursFacility> ToursEntries { get; set; }
    public string DbPath { get; }
    public RecreationDotOrgContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "RecreationDotOrg.db");
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}