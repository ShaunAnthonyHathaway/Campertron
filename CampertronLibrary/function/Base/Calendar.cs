using System.Globalization;

namespace CampertronLibrary.function.Base
{
    static class EnumerableExtensions
    {
        public static List<List<DateTime>> GetConsecutiveDays(this IEnumerable<DateTime> data, int consecutiveDayLimit)
        {
            if (data == null)
            {
                throw new ArgumentNullException("can't compare null data");
            }

            if (consecutiveDayLimit < 2)
            {
                throw new ArgumentException("can't compare against less than 2");
            }
            List<List<DateTime>> ReturnMatches = new List<List<DateTime>>();
            var SortedIncomingDays = data.Select(item => item.Date).OrderBy(item => item).ToList();
            List<DateTime> AlreadyHitDays = new List<DateTime>();
            foreach (var ThisSortedDay in SortedIncomingDays)
            {
                if (AlreadyHitDays.Contains(ThisSortedDay) == false)
                {
                    List<DateTime> HitDates = new List<DateTime>();
                    int ConsecutiveDays = 0;
                    int DayCounter = 0;
                    int SortedIncomingDaysTotalCount = SortedIncomingDays.Count();
                    while (DayCounter < SortedIncomingDaysTotalCount)
                    {
                        var IncrementedDay = ThisSortedDay.AddDays(DayCounter);
                        if (SortedIncomingDays.Contains(IncrementedDay))
                        {
                            ConsecutiveDays++;
                            if (HitDates.Contains(IncrementedDay) == false)
                            {
                                HitDates.Add(IncrementedDay);
                            }
                        }
                        else
                        {
                            break;
                        }
                        DayCounter++;
                    }
                    if (ConsecutiveDays >= consecutiveDayLimit)
                    {
                        foreach (var ThisHitDay in HitDates)
                        {
                            if (AlreadyHitDays.Contains(ThisHitDay) == false)
                            {
                                AlreadyHitDays.Add(ThisHitDay);
                            }
                        }
                        ReturnMatches.Add(HitDates);
                    }
                }
            }
            return ReturnMatches;
        }
    }
    public static class Calendar
    {
        public static void GenerateCalendar(int Month, int Year, List<DateTime> dates, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder)
        {
            List<Int32> HitDays = new List<Int32>();
            foreach (DateTime dt in dates)
            {
                if (dt.Month == Month && dt.Year == Year && HitDays.Contains(dt.Day) == false)
                {
                    HitDays.Add(dt.Day);
                }
            }
            int days = DateTime.DaysInMonth(Year, Month);
            String StartDay = (new DateTime(Year, Month, 1).DayOfWeek).ToString().Substring(0, 2);
            String MonthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(Month);
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t" + GetSpaces(MonthName) + MonthName + " " + Year, ConsoleColor.Yellow));
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\tMo Tu We Th Fr Sa Su", ConsoleColor.Cyan));
            int Counter = 1;
            bool Started = false;
            String ThisDayOfWeek = "Mo";
            days++;
            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t", true));
            while (Counter < days)
            {
                if (!Started)
                {
                    if (ThisDayOfWeek == StartDay)
                    {
                        Started = true;
                        if (HitDays.Contains(Counter))
                        {
                            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" " + Counter, ConsoleColor.Blue, true));
                        }
                        else
                        {
                            ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" " + Counter, true));
                        }
                        Counter++;
                    }
                    else
                    {
                        ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("   ", true));
                    }
                }
                else
                {
                    if (Counter < 10)
                    {
                        if (ThisDayOfWeek == "Mo")
                        {
                            if (HitDays.Contains(Counter))
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t " + Counter, ConsoleColor.Blue, true));
                            }
                            else
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t " + Counter, true));
                            }
                        }
                        else
                        {
                            if (HitDays.Contains(Counter))
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("  " + Counter, ConsoleColor.Blue, true));
                            }
                            else
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("  " + Counter, true));
                            }
                        }
                    }
                    else
                    {
                        if (ThisDayOfWeek == "Mo")
                        {
                            if (HitDays.Contains(Counter))
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t" + Counter.ToString(), ConsoleColor.Blue, true));
                            }
                            else
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\t\t" + Counter.ToString(), true));
                            }
                        }
                        else
                        {
                            if (HitDays.Contains(Counter))
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" " + Counter, ConsoleColor.Blue, true));
                            }
                            else
                            {
                                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(" " + Counter, true));
                            }
                        }
                    }
                    Counter++;
                }
                if (ThisDayOfWeek == "Su")
                {
                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                }
                if (Counter == days)
                {
                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem("\n", true));
                    ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(true));
                }
                ThisDayOfWeek = GetNextShortDay(ThisDayOfWeek);
            }
        }
        static void WriteDate(int Counter, String WriteMsg, List<Int32> HitDays, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder)
        {
            if (HitDays.Contains(Counter))
            {
                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(WriteMsg, ConsoleColor.Blue));
            }
            else
            {
                ResultHolder.Add(CampsiteConfig.AddConsoleConfigItem(WriteMsg));
            }
        }
        static String GetSpaces(String MonthName)
        {
            String ReturnStr = String.Empty;

            if (MonthName.Length <= 4)
            {
                ReturnStr = "     ";
            }
            else if (MonthName.Length <= 7)
            {
                ReturnStr = "    ";
            }
            else
            {
                ReturnStr = "   ";
            }
            return ReturnStr;
        }
        static String GetNextShortDay(String CurrentShortDay)
        {
            String ReturnStr = String.Empty;
            switch (CurrentShortDay)
            {
                case "Mo":
                    ReturnStr = "Tu";
                    break;
                case "Tu":
                    ReturnStr = "We";
                    break;
                case "We":
                    ReturnStr = "Th";
                    break;
                case "Th":
                    ReturnStr = "Fr";
                    break;
                case "Fr":
                    ReturnStr = "Sa";
                    break;
                case "Sa":
                    ReturnStr = "Su";
                    break;
                case "Su":
                    ReturnStr = "Mo";
                    break;
            }
            return ReturnStr;
        }
    }
}