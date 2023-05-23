using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KamperLibrary.function.sqlite
{
    static public class Clear
    {
        static public void All()
        {
            Console.WriteLine("Clearing database tables");
            using (var DbContext = new RecreationDotOrgContext())
            {
                DbContext.Database.ExecuteSqlRaw("DELETE FROM ActivityEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM CampsiteAttributesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM CampsitesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM EntityActivitiesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM EventsEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM FacilitiesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM FacilityAddressesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM LinksEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM MediaEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM MemberToursEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM OrgEntitiesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM OrganizationEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM PermitEntranceAttributesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM PermitEntranceZonesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM PermitEntrancesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM PermittedEquipmentEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM RecAreaAddressesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM RecAreaEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM RecAreaFacilitiesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM TourAttributesEntries");
                DbContext.Database.ExecuteSqlRaw("DELETE FROM ToursEntries");
                DbContext.Database.ExecuteSqlRaw("VACUUM");
            }
            Console.WriteLine("Clearing cache");
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var cachepath = System.IO.Path.Join(path, "KamperCache");
            foreach(String ThisFile in Directory.GetFiles(cachepath))
            {
                File.Delete(ThisFile);
            }
        }
    }
}