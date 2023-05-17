using System.Collections.Generic;

public class KamperConfig
{
    public void GenerateSearchData()
    {
        this._searchdates = this.GenerateSearchDates();
        this._monthstocheck = this.GetMonthsOut();
    }
    public bool AutoRun { get; set; }
    public String? CampgroundID { get; set; }
    public int? TotalHumans { get; set; }
    public String? FilterOutByCampsiteType { get; set; }
    public String? FilterInByCampsiteType { get; set; }
    public Double? GetMonthsToCheck()
    {
        return _monthstocheck;
    }
    private Double? _monthstocheck { get; set; }
    public List<DateTime> GetSearchDates()
    {
        return this._searchdates;
    }
    private List<DateTime> _searchdates { get; set; }
    public SearchTypes SearchBy { get; set; }
    public int SearchValue { get; set; }
    public List<String> SearchValueDates { get; set; }
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
            case SearchTypes.SpecificDates:
                int SpecificHits = 0;
                if (SearchValueDates.Count == 0)
                {
                    ReturnDates.Add(ConvertToZeroHour(DateTime.Now));
                }
                else
                {
                    foreach(String ThisDateString in SearchValueDates)
                    {
                        try
                        {
                            ReturnDates.Add(Convert.ToDateTime(ThisDateString));
                            SpecificHits++;
                        }
                        catch {
                            Console.WriteLine($"Error converting {ThisDateString} to DateTime");
                        }
                    }
                    if(SpecificHits == 0)
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
                if(ReturnResult == 12 && Matches == false)
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
    Until
}
