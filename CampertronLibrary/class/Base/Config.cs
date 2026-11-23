using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
//the unit tests call the internal seams, such as the search dates for a given time
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CampertronUnitTests")]
//the GUI's settings editor (CM5, decision D43) edits General.yaml and Email.yaml through the internal editing session
//(ConfigEditSession: the YAML document editor, the safe writer and the validation), which stay internal; its tests use the
//same seams
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CampertronGUI")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("CampertronGUITests")]
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
    //the absolute path of a file that holds the SMTP password (decision D66, business-rules §27): written in Email.yaml, it
    //is the password's source, before CAMPERTRON_EMAIL_PASSWORD and smtpPassword (SmtpPassword). Null here is "not in the
    //file" for the serializer, which leaves it out then, so the sample Email.yaml keeps its bytes (D40). The setter records
    //that the key was written even when its value is empty (`smtpPasswordFile:` reads as null): an empty key is a problem
    //of the configured source, never a reason to use another one
    [YamlDotNet.Serialization.YamlMember(DefaultValuesHandling = YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)]
    public string? SmtpPasswordFile
    {
        get => _SmtpPasswordFile;
        set
        {
            _SmtpPasswordFile = value;
            SmtpPasswordFileWritten = true;
        }
    }
    private string? _SmtpPasswordFile;
    //Email.yaml has smtpPasswordFile (any value): the file is the password's source. A copy made by setting every property
    //(the GUI's editing session clones its models so) is marked too; nothing resolves or validates such a copy
    internal bool SmtpPasswordFileWritten { get; private set; }
    //the password email mode's start resolved, kept for every send of the process (SmtpPassword.ResolveOnce); never
    //serialized, edited or shown
    internal CampertronLibrary.function.Base.SmtpPasswordResult? ResolvedPassword { get; set; }
}
public enum OutputType
{
    Console,
    Email,
    HtmlFile,
    UnitTest,
    //unattended monitoring that notifies with ntfy only (D67): the checks of outputTo: Email without email; Ntfy.yaml
    //must enable ntfy. outputTo: Email keeps email and adds ntfy when Ntfy.yaml enables it
    Ntfy
}
//ntfy's priorities (https://docs.ntfy.sh/publish/: 1=min, 3=default, 5=max), written by name or number
public enum NtfyPriority
{
    Min = 1,
    Low = 2,
    Default = 3,
    High = 4,
    Max = 5
}
//Ntfy.yaml (decision D67, business-rules §28): ntfy, the notification provider besides email. Never created by Campertron;
//without the file ntfy is off. The topic acts as a password where the server has no access control, so it is never
//written into a message, a diagnostic or View Config; the access token comes only from tokenFile or
//CAMPERTRON_NTFY_TOKEN (NtfyToken), never from this file
public class NtfyConfig
{
    public bool Enabled { get; set; }
    //the server's base URL, such as https://ntfy.sh; the message is published to its root (JSON), never to a topic URL
    public string? Server { get; set; }
    public string? Topic { get; set; }
    //the absolute path of a file that holds the access token (a Bearer token); written only when set (OmitNull). As
    //smtpPasswordFile (D66), the setter records the key even when its value is empty: a configured source that can't be
    //used is a problem, never a reason to publish without the token
    [YamlDotNet.Serialization.YamlMember(DefaultValuesHandling = YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)]
    public string? TokenFile
    {
        get => _TokenFile;
        set
        {
            _TokenFile = value;
            TokenFileWritten = true;
        }
    }
    private string? _TokenFile;
    public NtfyPriority Priority { get; set; } = NtfyPriority.Default;
    //ntfy's tags; a tag that is an emoji short code shows as the emoji. Without the key: the default tags
    public List<String>? Tags { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    //Ntfy.yaml has tokenFile (any value)
    internal bool TokenFileWritten { get; private set; }
    //the token email mode's start resolved, kept for every publish of the process (NtfyToken.ResolveOnce); never
    //serialized or shown
    internal CampertronLibrary.function.Base.NtfyTokenResult? ResolvedToken { get; set; }
    //the tags a message carries: the file's, or the default (a tent emoji) when the file has none
    internal static readonly IReadOnlyList<string> DefaultTags = new[] { "tent" };
    internal IReadOnlyList<string> EffectiveTags => Tags ?? (IReadOnlyList<string>)DefaultTags;
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
    //Warn false writes none of the search's warnings (a later check of the Email loop, or the GUI's check of what the
    //monitoring's start would load, D73)
    internal void GenerateSearchData(DateTime Now, bool Warn)
    {
        GenerateSearchData(Now, Warn ? CampertronLibrary.function.Base.ConsoleUi.Warning : null);
    }
    //the same with where the search's warnings go: the console's warning, the monitoring service's event (D90), or null
    //for none
    internal void GenerateSearchData(DateTime Now, Action<string>? Warn)
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
    //how the eight include and exclude lists below match a campsite (decision D51, business-rules §3): Exact or Contains.
    //A search file without filterMatching (every file written before D51) uses Contains, its matching before D51; a new
    //search is written with Exact. Null here is "not in the file"; the serializer leaves it out then, so a search written
    //from these settings never gets an empty filterMatching. The YAML text itself is checked by ConfigValidation (only the
    //names Exact and Contains; YamlDotNet would also read a number, padding or "Exact, Contains")
    [YamlDotNet.Serialization.YamlMember(DefaultValuesHandling = YamlDotNet.Serialization.DefaultValuesHandling.OmitNull)]
    public FilterMatchMode? FilterMatching { get; set; }
    //the matching the search uses: the file's filterMatching, or Contains when the file has none
    internal FilterMatchMode EffectiveFilterMatching => FilterMatching ?? FilterMatchMode.Contains;
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
    private List<DateTime> GenerateSearchDates(DateTime Now, Action<string>? Warn)
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
                                if (Warn != null)
                                {
                                    Warn($"search \"{DisplayName}\": \"{ThisDateString}\" in searchValueDates is not a date and is ignored.");
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
    private int GetMonthsOut(DateTime Now, Action<string>? Warn)
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
                    if (Warn != null)
                    {
                        Warn($"search \"{DisplayName}\": results are limited to 12 months out.");
                    }
                    Matches = true;
                }
            }
        }
        return ReturnResult;
    }
}
//how a search's include and exclude values match a campsite's values (decision D51, business-rules §3). Both compare
//ordinally: case, spaces and punctuation count
public enum FilterMatchMode
{
    //the value equals the campsite's value character for character
    Exact,
    //the containment the search has always used: an equipment or attribute value contains the campsite's equipment name or
    //"Name:Value", a campsite type contains the value; campsite names (includeSites, excludeSites) still match exactly
    Contains
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
    //Ntfy.yaml, loaded only for unattended monitoring (outputTo: Email or Ntfy) and only if the file exists (D67)
    public NtfyConfig? NtfyConfig { get; set; }
    public bool ContainerMode { get; set; }
    public string? ConfigPath { get; set; }
    public string? CachePath { get; set; }
}