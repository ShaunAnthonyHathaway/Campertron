using Microsoft.EntityFrameworkCore;

namespace CampertronLibrary.function.RecDotOrg.sqlite
{
    static public class Clear
    {
        static public void All(string ConfigPath, string CachePath)
        {
            ClearDatabase(ConfigPath);
            ClearRefreshCache(CachePath);
        }
        //the cache directory a cache clear empties is the configured CachePath (CampertronInternalConfig.CachePath:
        //LocalApplicationData/CampertronCache on the desktop, /cache in a container; TD-05). Throws
        //InvalidOperationException if it is not set, is a filesystem root, or is ConfigPath (when given);
        //returns it unchanged. There is no fallback to another directory.
        static public string CheckCacheDirectory(string? CacheDirectory, string? ConfigPath = null)
        {
            if (string.IsNullOrWhiteSpace(CacheDirectory))
            {
                throw new InvalidOperationException("CachePath is not set");
            }
            string FullCache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(CacheDirectory));
            string? Root = Path.GetPathRoot(FullCache);
            if (Root != null && string.Equals(Path.TrimEndingDirectorySeparator(Root), FullCache, PathComparison))
            {
                throw new InvalidOperationException($"refusing to use the filesystem root {FullCache} as the cache directory");
            }
            if (!string.IsNullOrWhiteSpace(ConfigPath) &&
                string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(ConfigPath)), FullCache, PathComparison))
            {
                throw new InvalidOperationException($"refusing to use the config directory {FullCache} as the cache directory");
            }
            return CacheDirectory;
        }
        //deletes every file directly in CacheDirectory (no subdirectory; a symbolic link is deleted as a link, never
        //followed); throws if the directory or a file can't be read or deleted. Refuses (InvalidOperationException,
        //nothing deleted) a path CheckCacheDirectory rejects, or a directory holding RecreationDotOrg.db or
        //General.yaml, which is a config directory however it was reached (link, bind mount)
        static public void ClearRefreshCache(string CacheDirectory)
        {
            CheckCacheDirectory(CacheDirectory);
            foreach (string ConfigFile in new[] { RidbStagedRefresh.DbFileName, "General.yaml" })
            {
                if (File.Exists(Path.Join(CacheDirectory, ConfigFile)))
                {
                    throw new InvalidOperationException($"refusing to clear {CacheDirectory}: it contains {ConfigFile}, so it is a config directory, not the cache");
                }
            }
            Console.WriteLine("Clearing cache");
            foreach (string ThisFile in Directory.GetFiles(CacheDirectory))
            {
                File.Delete(ThisFile);
            }
        }
        //paths are compared case-sensitively only on Linux; elsewhere the default file systems ignore case
        private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
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
                    //the table name comes only from the constant RidbDatabase.OmittedTables list, and SQL identifiers cannot be parameterized
                    #pragma warning disable EF1003
                    DbContext.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS \"" + OmittedTable + "\"");
                    #pragma warning restore EF1003
                }
                DbContext.Database.ExecuteSqlRaw("VACUUM");
            }
        }
    }
}