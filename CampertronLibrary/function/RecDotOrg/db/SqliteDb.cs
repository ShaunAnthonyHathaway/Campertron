using Microsoft.EntityFrameworkCore;

public class RecreationDotOrgContext : DbContext
{
    // Only the RIDB tables Campertron reads are mapped. The 15 omitted tables are listed in
    // CampertronLibrary.function.RecDotOrg.sqlite.RidbDatabase.OmittedTables, with how to restore one.
    public DbSet<CampsiteAttributesRecdata> CampsiteAttributesEntries { get; set; }
    public DbSet<CampsitesRecdata> CampsitesEntries { get; set; }
    public DbSet<FacilitiesData> FacilitiesEntries { get; set; }
    public DbSet<FacilityAddressesRecdata> FacilityAddressesEntries { get; set; }
    public DbSet<PermittedEquipmentRecdata> PermittedEquipmentEntries { get; set; }
    public DbSet<RecAreaFacilityRecData> RecAreaEntries { get; set; }
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
        modelBuilder.Entity<PermittedEquipmentRecdata>().HasKey(perd => new { perd.CampsiteID, perd.EquipmentName });
    }
}