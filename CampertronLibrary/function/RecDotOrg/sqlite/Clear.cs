using Microsoft.EntityFrameworkCore;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class Clear
    {
        static public void All(string ConfigPath)
        {
            ClearDatabase(ConfigPath);
            ClearRefreshCache(RefreshCachePath());
        }
        //the cache directory a RIDB refresh clears: LocalApplicationData/CampertronCache, not CachePath (TD-05)
        static public string RefreshCachePath()
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            return Path.Join(path, "CampertronCache");
        }
        //deletes every file directly in CacheDirectory; throws if the directory or a file can't be read or deleted
        static public void ClearRefreshCache(string CacheDirectory)
        {
            Console.WriteLine("Clearing cache");
            foreach (string ThisFile in Directory.GetFiles(CacheDirectory))
            {
                File.Delete(ThisFile);
            }
        }
        //empties the imported tables and drops the RIDB tables Campertron does not use (RidbDatabase.OmittedTables)
        static public void ClearDatabase(string ConfigPath)
        {
            Console.WriteLine("Clearing database tables");
            using (var DbContext = new RecreationDotOrgContext(ConfigPath))
            {
                DbContext.Database.ExecuteSqlRaw("DELETE FROM CampsiteAttributesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM CampsitesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM FacilitiesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM FacilityAddressesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM PermittedEquipmentEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM RecAreaEntries");
                foreach (string OmittedTable in RidbDatabase.OmittedTables)
                {
                    DbContext.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS \"" + OmittedTable + "\"");
                }
                DbContext.Database.ExecuteSqlRaw("VACUUM");
            }
        }
    }
}