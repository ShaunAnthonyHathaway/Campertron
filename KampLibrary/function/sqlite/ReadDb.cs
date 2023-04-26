using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KampLibrary.function.sqlite
{
    static public class ReadDb
    {
        public static void Read()
        {
            using var db = new RecreationDotOrgContext();
            foreach (var v in db.FacilitiesEntries.OrderBy(p => p.FacilityName))
            {
                Console.WriteLine(v.FacilityName);
                Console.WriteLine(v.FacilityTypeDescription);
            }
            Console.ReadLine();
        }
    }
}
