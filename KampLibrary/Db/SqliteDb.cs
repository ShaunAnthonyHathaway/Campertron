using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata;

public class BloggingContext : DbContext
{
    public DbSet<RecAreaAddressesEntries> RecAreaAddressesDbEntries { get; set; }
    public DbSet<FacilitiesEntries> FacilitiesDbEntries { get; set; }
    public string DbPath { get; }
    public BloggingContext()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        DbPath = System.IO.Path.Join(path, "RecreationDotOrg.db");
    }
    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={DbPath}");
}