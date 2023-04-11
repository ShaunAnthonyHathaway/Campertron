using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KampLibrary.function.sqlite
{
    static public class ClearDb
    {
        static public void Clear()
        {
            using var db = new RecreationDotOrgContext();
            
        }
    }
}
