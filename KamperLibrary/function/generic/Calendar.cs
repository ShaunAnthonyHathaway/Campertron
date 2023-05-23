using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace KamperLibrary.function.generic
{
    public static class Calendar
    {
        static int year = new int();
        static int month = new int();
        static int[,] calendar = new int[6, 7];
        private static DateTime date;
        private static List<Int32> HitDays = new List<Int32>();
        public static void GenerateCalendar(List<DateTime> dates)
        {
            HitDays.Clear();
            foreach (DateTime dt in dates)
            {
                if (HitDays.Contains(dt.Day) == false)
                {
                    HitDays.Add(dt.Day);
                }
            }
            year = dates[0].Year;
            month = dates[0].Month;

            date = new DateTime(year, month, 1);
            DrawHeader();
            FillCalendar();
            DrawCalendar();
        }

        static void DrawHeader()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("              " + CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month) + " " + year);
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("        Mo Tu We Th Fr Sa Su");
            Console.ResetColor();
        }
        static void FillCalendar()
        {
            int days = DateTime.DaysInMonth(year, month);
            int currentDay = 1;
            var dayOfWeek = (int)date.DayOfWeek;
            for (int i = 0; i < calendar.GetLength(0); i++)
            {
                for (int j = 0; j < calendar.GetLength(1) && currentDay <= days; j++)
                {
                    if (i == 0 && dayOfWeek - 1 > j)
                    {
                        calendar[i, j] = 0;
                    }
                    else
                    {
                        calendar[i, j] = currentDay; currentDay++;
                    }
                }
            }
        }
        static void DrawCalendar()
        {
            int LastI = 9;
            for (int i = 0; i < calendar.GetLength(0); i++)
            {
                for (int j = 0; j < calendar.GetLength(1); j++)
                {
                    if (calendar[i, j] > 0)
                    {
                        if (calendar[i, j] < 10)
                        {
                            if(LastI != i)
                            {
                                Console.Write("        ");
                                LastI = i;
                            }
                            if (HitDays.Contains(calendar[i, j]))
                            {
                                Console.ForegroundColor = ConsoleColor.Blue;
                            }
                            Console.Write(" " + calendar[i, j] + " ");
                            if (HitDays.Contains(calendar[i, j]))
                            {
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            if (LastI != i)
                            {
                                Console.Write("        ");
                                LastI = i;
                            }
                            if (HitDays.Contains(calendar[i, j]))
                            {
                                Console.ForegroundColor = ConsoleColor.Blue;
                            }
                            Console.Write(calendar[i, j] + " ");
                            if (HitDays.Contains(calendar[i, j]))
                            {
                                Console.ResetColor();
                            }
                        }
                    }
                    else
                    {
                        Console.Write("   ");
                    }
                }
                Console.WriteLine("");
            }
        }
    }
}