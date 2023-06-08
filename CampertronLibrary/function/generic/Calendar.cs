using System.Diagnostics.Metrics;
using System.Globalization;

namespace CampertronLibrary.function.generic
{
    public static class Calendar
    {
        public static void GenerateCalendar(List<DateTime> dates, ref List<ConsoleConfig.ConsoleConfigItem> ResultHolder)
        {
            List<Int32> HitDays = new List<Int32>();
            foreach (DateTime dt in dates)
            {
                if (HitDays.Contains(dt.Day) == false)
                {
                    HitDays.Add(dt.Day);
                }
            }

            int Year = dates[0].Year;
            int Month = dates[0].Month;

            int days = DateTime.DaysInMonth(Year, Month);
            String StartDay = ConvertToShortDay(new DateTime(Year, Month, 1).DayOfWeek);
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
        static void WriteDate(int Counter, String WriteMsg, List<Int32> HitDays, ref List<ConsoleConfig.ConsoleConfigItem> ResultHolder)
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
        static String ConvertToShortDay(DayOfWeek CompareDay)
        {
            String ReturnStr = String.Empty;

            switch (CompareDay)
            {
                case DayOfWeek.Monday:
                    ReturnStr = "Mo";
                    break;
                case DayOfWeek.Tuesday:
                    ReturnStr = "Tu";
                    break;
                case DayOfWeek.Wednesday:
                    ReturnStr = "We";
                    break;
                case DayOfWeek.Thursday:
                    ReturnStr = "Th";
                    break;
                case DayOfWeek.Friday:
                    ReturnStr = "Fr";
                    break;
                case DayOfWeek.Saturday:
                    ReturnStr = "Sa";
                    break;
                case DayOfWeek.Sunday:
                    ReturnStr = "Su";
                    break;
            }

            return ReturnStr;
        }
    }
}