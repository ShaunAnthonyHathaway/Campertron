using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
//the unit tests call the internal seams, such as the search dates for a given time
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CampertronUnitTests")]
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
    HtmlFile,
    UnitTest
}
public class CampertronConfig
{
    public void GenerateSearchData()
    {
        GenerateSearchData(DateTime.Now);
    }
    //the search dates as of Now: the current time in production, a fixed time in tests
    internal void GenerateSearchData(DateTime Now)
    {
        GenerateSearchData(Now, true);
    }
    //the dates again, for a later check of the Email loop: a DaysOut, MonthsOut or Until window follows Now (TD-46).
    //The search's warnings were written when it was loaded and are not repeated
    internal void RegenerateSearchData(DateTime Now)
    {
        GenerateSearchData(Now, false);
    }
    private void GenerateSearchData(DateTime Now, bool Warn)
    {
        this._searchdates = this.GenerateSearchDates(Now, Warn);
        this._monthstocheck = this.GetMonthsOut(Now, Warn);
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
    private List<DateTime> GenerateSearchDates(DateTime Now, bool Warn)
    {
        List<DateTime> ReturnDates = new List<DateTime>();

        switch (SearchBy)
        {
            case SearchTypes.DaysOut:
                DateTime EndDateDays = Now.AddDays(SearchValue);
                bool HitEndDays = false;
                int CounterDays = 0;
                while (!HitEndDays)
                {
                    DateTime CurrentDate = Now.AddDays(CounterDays);
                    ReturnDates.Add(ConvertToZeroHour(CurrentDate));
                    CounterDays++;
                    if (CurrentDate.Day == EndDateDays.Day && CurrentDate.Month == EndDateDays.Month && CurrentDate.Year == EndDateDays.Year)
                    {
                        HitEndDays = true;
                    }
                }
                break;
            case SearchTypes.MonthsOut:
                DateTime EndDateMonths = Now.AddMonths(SearchValue);
                bool HitEndMonths = false;
                int CounterMonths = 0;
                while (!HitEndMonths)
                {
                    DateTime CurrentDate = Now.AddDays(CounterMonths);
                    ReturnDates.Add(ConvertToZeroHour(CurrentDate));
                    CounterMonths++;
                    if (CurrentDate.Day == EndDateMonths.Day && CurrentDate.Month == EndDateMonths.Month && CurrentDate.Year == EndDateMonths.Year)
                    {
                        HitEndMonths = true;
                    }
                }
                break;
            case SearchTypes.StartEndDate:
                if (SearchValueDates != null && SearchValueDates[0] != null && SearchValueDates[1] != null)
                {
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
                        //an unusable search file like any other: the loader names the file and the console ends with one error
                        //and exit code 1, without waiting for a key (TD-45). Equal dates stay an error (business-rules §2)
                        throw new ArgumentException($"search \"{DisplayName}\": Start date and End date cannot be the same.");
                    }
                }
                break;
            case SearchTypes.SpecificDates:
                if (SearchValueDates != null)
                {
                    int SpecificHits = 0;
                    if (SearchValueDates.Count == 0)
                    {
                        ReturnDates.Add(ConvertToZeroHour(Now));
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
                                if (Warn)
                                {
                                    CampertronLibrary.function.Base.ConsoleUi.Warning($"search \"{DisplayName}\": \"{ThisDateString}\" in searchValueDates is not a date and is ignored.");
                                }
                            }
                        }
                        if (SpecificHits == 0)
                        {
                            ReturnDates.Add(ConvertToZeroHour(Now));
                        }
                    }
                }
                break;
            case SearchTypes.Until:
                if (SearchValueDates != null && SearchValueDates[0] != null)
                {
                    DateTime EndDateUntil = Convert.ToDateTime(SearchValueDates[0]);
                    if (Now < EndDateUntil)
                    {
                        bool HitEndUntil = false;
                        int CounterUntil = 0;
                        while (!HitEndUntil)
                        {
                            DateTime CurrentDate = Now.AddDays(CounterUntil);
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
                        ReturnDates.Add(ConvertToZeroHour(Now));
                    }
                }
                break;
            default:
                if (SearchValueDates != null && SearchValueDates[0] != null)
                {
                    this.SearchBy = SearchTypes.SpecificDates;
                    if (SearchValueDates.Count == 0)
                    {
                        ReturnDates.Add(ConvertToZeroHour(Now));
                    }
                }
                break;
        }
        return ReturnDates;
    }
    //midnight of the given day, built from its numbers: formatting a date as text and parsing it back depends on the
    //current culture and its calendar (TD-11: "11/14/2026" is no date in en-GB, and "11/1/2026" is 11 January)
    private DateTime ConvertToZeroHour(DateTime IncomingDT)
    {
        return new DateTime(IncomingDT.Year, IncomingDT.Month, IncomingDT.Day);
    }
    private int GetMonthsOut(DateTime Now, bool Warn)
    {
        int ReturnResult = 0;

        if (_searchdates != null && _searchdates.Count > 0)
        {
            this._searchdates.Sort();
            DateTime EndDate = this._searchdates[_searchdates.Count - 1];
            bool Matches = false;
            while (!Matches)
            {
                DateTime Compare = Now.AddMonths(ReturnResult);
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
                    if (Warn)
                    {
                        CampertronLibrary.function.Base.ConsoleUi.Warning($"search \"{DisplayName}\": results are limited to 12 months out.");
                    }
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
    public GeneralConfig? GeneralConfig { get; set; }
    public EmailConfig? EmailConfig { get; set; }
    public bool ContainerMode { get; set; }
    public string? ConfigPath { get; set; }
    public string? CachePath { get; set; }
}