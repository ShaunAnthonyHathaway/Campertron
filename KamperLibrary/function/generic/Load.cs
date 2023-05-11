using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KamperLibrary.function.generic
{
    public static class Load
    {
        public static void Init()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "⛺ Kamper ⛺";
        }
        public static void Fin()
        {
            Console.WriteLine("Press enter to terminate...");
            Console.ReadLine();
        }
    }
}
