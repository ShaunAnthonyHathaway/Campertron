using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
public class GeneralConfig
{
    public OutputType OutputTo { get; set; }
    public Double RefreshRidbDataDayInterval { get;set;}
    public DateTime? LastRidbDataRefresh { get; set; }
    public bool AutoRefresh { get; set; }
}
public class EmailConfig
{
    public string? SmtpServer { get; set; }
    public int SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public List<String>? SendToAddressList { get; set; }
    public string? SendFromAddress { get; set; }
}
public enum OutputType
{
    Console,
    Email,
    HtmlFile
}
public class CampertronConfig
{
    public void GenerateSearchData()
    {
        this._searchdates = this.GenerateSearchDates();
        this._monthstocheck = this.GetMonthsOut();
    }
    public String? DisplayName { get; set; }
    private Double? _monthstocheck { get; set; }
    private List<DateTime>? _searchdates { get; set; }
    public bool AutoRun { get; set; }
    public String? CampgroundID { get; set; }
    public int? TotalHumans { get; set; }
    public SearchTypes SearchBy { get; set; }
    public int SearchValue { get; set; }
    public List<String>? SearchValueDates { get; set; }
    public bool ShowMonday { get; set; }
    public bool ShowTuesday { get; set; }
    public bool ShowWednesday { get; set; }
    public bool ShowThursday { get; set; }
    public bool ShowFriday { get; set; }
    public bool ShowSaturday { get; set; }
    public bool ShowSunday { get; set; }
    public List<String>? IncludeEquipment { get; set; }
    public List<String>? ExcludeEquipment { get; set; }
    public List<String>? IncludeSites { get; set; }
    public List<String>? ExcludeSites { get; set; }
    public List<String>? IncludeAttributes { get; set; }
    public List<String>? ExcludeAttributes { get; set; }
    public List<String>? IncludeCampsiteType { get; set; }
    public List<String>? ExcludeCampsiteType { get; set; }
    public int ConsecutiveDays { get; set; }
    public bool ShowThisDay(String DayToEvaluate)
    {
        bool ReturnBool = false;

        switch (DayToEvaluate)
        {
            case "Monday":
                if (this.ShowMonday)
                {
                    ReturnBool = true;
                }
                break;
            case "Tuesday":
                if (this.ShowTuesday)
                {
                    ReturnBool = true;
                }
                break;
            case "Wednesday":
                if (this.ShowWednesday)
                {
                    ReturnBool = true;
                }
                break;
            case "Thursday":
                if (this.ShowThursday)
                {
                    ReturnBool = true;
                }
                break;
            case "Friday":
                if (this.ShowFriday)
                {
                    ReturnBool = true;
                }
                break;
            case "Saturday":
                if (this.ShowSaturday)
                {
                    ReturnBool = true;
                }
                break;
            case "Sunday":
                if (this.ShowSunday)
                {
                    ReturnBool = true;
                }
                break;
        }

        return ReturnBool;
    }
    public Double? GetMonthsToCheck()
    {
        return _monthstocheck;
    }
    public List<DateTime> GetSearchDates()
    {
        return this._searchdates ?? new List<DateTime>();
    }
    private List<DateTime> GenerateSearchDates()
    {
        List<DateTime> ReturnDates = new List<DateTime>();

        switch (SearchBy)
        {
            case SearchTypes.DaysOut:
                DateTime EndDateDays = DateTime.Now.AddDays(SearchValue);
                bool HitEndDays = false;
                int CounterDays = 0;
                while (!HitEndDays)
                {
                    DateTime CurrentDate = DateTime.Now.AddDays(CounterDays);
                    ReturnDates.Add(ConvertToZeroHour(CurrentDate));
                    CounterDays++;
                    if (CurrentDate.Day == EndDateDays.Day && CurrentDate.Month == EndDateDays.Month && CurrentDate.Year == EndDateDays.Year)
                    {
                        HitEndDays = true;
                    }
                }
                break;
            case SearchTypes.MonthsOut:
                DateTime EndDateMonths = DateTime.Now.AddMonths(SearchValue);
                bool HitEndMonths = false;
                int CounterMonths = 0;
                while (!HitEndMonths)
                {
                    DateTime CurrentDate = DateTime.Now.AddDays(CounterMonths);
                    ReturnDates.Add(ConvertToZeroHour(CurrentDate));
                    CounterMonths++;
                    if (CurrentDate.Day == EndDateMonths.Day && CurrentDate.Month == EndDateMonths.Month && CurrentDate.Year == EndDateMonths.Year)
                    {
                        HitEndMonths = true;
                    }
                }
                break;
            case SearchTypes.StartEndDate:
                DateTime StartDate = Convert.ToDateTime(SearchValueDates[0]);
                DateTime EndDate = Convert.ToDateTime(SearchValueDates[1]);
                if (StartDate != EndDate)
                {
                    while (StartDate < EndDate.AddDays(1))
                    {
                        ReturnDates.Add(ConvertToZeroHour(StartDate));
                        StartDate = StartDate.AddDays(1);
                    }
                }
                else
                {
                    Console.WriteLine("Start date and End date cannot be the same");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
                break;
            case SearchTypes.SpecificDates:
                int SpecificHits = 0;
                if (SearchValueDates.Count == 0)
                {
                    ReturnDates.Add(ConvertToZeroHour(DateTime.Now));
                }
                else
                {
                    foreach (String ThisDateString in SearchValueDates)
                    {
                        try
                        {
                            ReturnDates.Add(Convert.ToDateTime(ThisDateString));
                            SpecificHits++;
                        }
                        catch
                        {
                            Console.WriteLine($"Error converting {ThisDateString} to DateTime");
                        }
                    }
                    if (SpecificHits == 0)
                    {
                        ReturnDates.Add(ConvertToZeroHour(DateTime.Now));
                    }
                }
                break;
            case SearchTypes.Until:
                DateTime EndDateUntil = Convert.ToDateTime(SearchValueDates[0]);
                if (DateTime.Now < EndDateUntil)
                {
                    bool HitEndUntil = false;
                    int CounterUntil = 0;
                    while (!HitEndUntil)
                    {
                        DateTime CurrentDate = DateTime.Now.AddDays(CounterUntil);
                        ReturnDates.Add(ConvertToZeroHour(CurrentDate));
                        CounterUntil++;
                        if (CurrentDate.Day == EndDateUntil.Day && CurrentDate.Month == EndDateUntil.Month && CurrentDate.Year == EndDateUntil.Year)
                        {
                            HitEndUntil = true;
                        }
                    }
                }
                else
                {
                    ReturnDates.Add(ConvertToZeroHour(DateTime.Now));
                }
                break;
            default:
                this.SearchBy = SearchTypes.SpecificDates;
                if (SearchValueDates.Count == 0)
                {
                    ReturnDates.Add(ConvertToZeroHour(DateTime.Now));
                }
                break;
        }
        return ReturnDates;
    }
    private DateTime ConvertToZeroHour(DateTime IncomingDT)
    {
        return Convert.ToDateTime($"{IncomingDT.Month}/{IncomingDT.Day}/{IncomingDT.Year} 0:00:00 AM");
    }
    private int GetMonthsOut()
    {
        int ReturnResult = 0;

        if (_searchdates.Count > 0)
        {
            this._searchdates.Sort();
            DateTime EndDate = this._searchdates[_searchdates.Count - 1];
            DateTime Now = DateTime.Now;
            bool Matches = false;
            while (!Matches)
            {
                DateTime Compare = DateTime.Now.AddMonths(ReturnResult);
                if (EndDate.Month == Compare.Month && EndDate.Year == Compare.Year)
                {
                    Matches = true;
                }
                else
                {
                    ReturnResult++;
                }
                if (ReturnResult == 12 && Matches == false)
                {
                    Console.WriteLine("Results limited to 12 months out");
                    Matches = true;
                }
            }
        }
        return ReturnResult;
    }
}
public enum SearchTypes
{
    DaysOut,
    MonthsOut,
    SpecificDates,
    Until,
    StartEndDate
}
public class CampertronInternalConfig
{
    public GeneralConfig GeneralConfig { get; set; }
    public EmailConfig EmailConfig { get; set; }
    public bool ContainerMode { get; set; }
    public string ConfigPath { get; set; }
    public string CachePath { get; set; }
}