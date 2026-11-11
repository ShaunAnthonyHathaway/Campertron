using Microsoft.EntityFrameworkCore;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class Clear
    {
        static public void All(string ConfigPath)
        {
            ClearDatabase(ConfigPath);
            Console.WriteLine("Clearing cache");
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = Path.Join(path, "CampertronCache");
            foreach (string ThisFile in Directory.GetFiles(cachepath))
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