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
        //The calendar of one month as the console shows it: a title, the weekdays (Monday first) and the days, with
        //the days in Dates marked by "*" (and by color where color is used), so the marks do not depend on color.
        public static List<ConsoleUi.StyledLine> Lines(int Year, int Month, IEnumerable<DateTime> Dates, CultureInfo Culture, string Indent, ConsoleColor MarkColor)
        {
            HashSet<int> Marked = new HashSet<int>(Dates.Where(p => p.Year == Year && p.Month == Month).Select(p => p.Day));
            List<ConsoleUi.StyledLine> Result = new List<ConsoleUi.StyledLine>();
            DateTime First = new DateTime(Year, Month, 1);
            Result.Add(new ConsoleUi.StyledLine(Indent + First.ToString("Y", Culture) + " (* = open)"));
            Result.Add(new ConsoleUi.StyledLine(Indent + " Mo  Tu  We  Th  Fr  Sa  Su"));
            int Column = ((int)First.DayOfWeek + 6) % 7;
            ConsoleUi.StyledLine Row = new ConsoleUi.StyledLine(Indent + new string(' ', Column * 4));
            int Days = DateTime.DaysInMonth(Year, Month);
            for (int Day = 1; Day <= Days; Day++)
            {
                string Cell = ((Marked.Contains(Day) ? "*" : string.Empty) + Day.ToString(CultureInfo.InvariantCulture)).PadLeft(3);
                Row.Add(Cell, Marked.Contains(Day) ? MarkColor : null);
                Column++;
                if (Column == 7 || Day == Days)
                {
                    Result.Add(Row);
                    Row = new ConsoleUi.StyledLine(Indent);
                    Column = 0;
                }
                else
                {
                    Row.Add(" ");
                }
            }
            return Result;
        }
        //The calendar as values of a Console output stream. Every value is tagged OutputKind.Calendar: the console
        //renders the results from their metadata and draws the calendar itself (Lines), so it skips these values.
        public static void GenerateCalendar(int Month, int Year, List<DateTime> dates, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder)
        {
            int FirstValue = ResultHolder.Count;
            AddCalendarValues(Month, Year, dates, ref ResultHolder);
            for (int i = FirstValue; i < ResultHolder.Count; i++)
            {
                ResultHolder[i].Kind = ConsoleConfig.OutputKind.Calendar;
            }
        }
        private static void AddCalendarValues(int Month, int Year, List<DateTime> dates, ref List<ConsoleConfig.ConsoleConfigValue> ResultHolder)
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