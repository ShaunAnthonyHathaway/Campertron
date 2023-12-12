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
    public DbSet<RecAreaFacilityRecData> RecAreaEntries { get; set; }
    public DbSet<TourAttributesFacilityRecData> TourAttributesEntries { get; set; }
    public DbSet<ToursFacilityRecData> ToursEntries { get; set; }
    public string DbPath { get; }
    public RecreationDotOrgContext(string ConfigPath)
    {
        DbPath = System.IO.Path.Join(ConfigPath, "RecreationDotOrg.db");
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CampsiteAttributesRecdata>().HasKey(card => new { card.AttributeID, card.EntityID });
        modelBuilder.Entity<EntityActivitiesRecdata>().HasKey(eard => new { eard.EntityID, eard.ActivityID });
        modelBuilder.Entity<MediaRecdata>().HasKey(mrd => new { mrd.EntityID, mrd.EntityMediaID });
        modelBuilder.Entity<OrgEntitiesRecdata>().HasKey(oerd => new { oerd.EntityID, oerd.OrgID });
        modelBuilder.Entity<PermitEntranceAttributesRecdata>().HasKey(peard => new { peard.AttributeID, peard.EntityID });
        modelBuilder.Entity<PermittedEquipmentRecdata>().HasKey(perd => new { perd.CampsiteID, perd.EquipmentName });
        modelBuilder.Entity<TourAttributesFacilityRecData>().HasKey(taf => new { taf.AttributeID, taf.EntityID });
        modelBuilder.Entity<RecAreaFacilitiesRecdata>().HasKey(rafd => new { rafd.RecAreaID, rafd.FacilityID });
    }
}