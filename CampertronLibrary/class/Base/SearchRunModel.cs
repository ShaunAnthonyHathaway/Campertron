using System.Collections.ObjectModel;

// The host-neutral search service's request, plan, progress and results (GS1, decision D45; business-rules.md §19).
// CampsiteSearch.Plan reads and checks the chosen search files; CampsiteSearch.Run searches them and returns a
// SearchRunResult. Everything here is immutable once built: records and read-only lists over arrays of the service's
// own. Nothing holds a RunningData collection or rendered text, and every message is fixed text: never a response body,
// an exception's message or a value of a settings file (Email.yaml is never read at all). A ConsoleConfigValue is held
// only when a run asks for its searches' tagged output streams (SearchHostOptions.KeepOutputValues: the no-argument
// console's Search, D131), in SearchResult.OutputValues, as data its host renders.

// what to search: the settings folder (with the campground data), the cache folder and the search files by name
internal sealed record SearchRunRequest(string ConfigPath, string CachePath, IReadOnlyList<string> SearchFileNames);

// whether a chosen search file can be searched
internal enum SearchPlanState
{
    // loaded, checked as an enabled search would be at a start, its dates generated
    Ready,
    // loaded and valid, but without a campgroundID: nothing to search
    Skipped,
    // not a search file, missing, unreadable, or refused by the checks of a start (its diagnostics say why)
    Invalid
}

// one chosen search file, read once. Search is the operation's own snapshot (null unless Ready): Run reads it, nothing
// writes it back, and no settings file is ever written
internal sealed class SearchPlanEntry
{
    internal SearchPlanEntry(string FileName, SearchPlanState State, string? DisplayName, bool AutoRun, string? CampgroundId, int ConsecutiveNights,
        IReadOnlyList<ConfigDiagnostic> Diagnostics, IReadOnlyList<SearchIssue> Issues, CampertronConfig? Search)
    {
        this.FileName = FileName;
        this.State = State;
        this.DisplayName = DisplayName;
        this.AutoRun = AutoRun;
        this.CampgroundId = CampgroundId;
        this.ConsecutiveNights = ConsecutiveNights;
        this.Diagnostics = Diagnostics;
        this.Issues = Issues;
        this.Search = Search;
    }
    internal string FileName { get; }
    internal SearchPlanState State { get; }
    internal string? DisplayName { get; }
    // as the file has it: a file with autoRun: false can be searched explicitly, and stays as it is
    internal bool AutoRun { get; }
    internal string? CampgroundId { get; }
    internal int ConsecutiveNights { get; }
    // the configuration validation's diagnostics (CM3), with the severities of an enabled search; warnings never block
    internal IReadOnlyList<ConfigDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<SearchIssue> Issues { get; }
    internal CampertronConfig? Search { get; }
}

internal sealed class SearchRunPlan
{
    internal SearchRunPlan(string ConfigPath, string CachePath, DateTime PlannedAt, IReadOnlyList<SearchPlanEntry> Entries, IReadOnlyList<SearchIssue> Issues)
    {
        this.ConfigPath = ConfigPath;
        this.CachePath = CachePath;
        this.PlannedAt = PlannedAt;
        this.Entries = Entries;
        this.Issues = Issues;
    }
    internal string ConfigPath { get; }
    internal string CachePath { get; }
    // the time the search dates were generated for
    internal DateTime PlannedAt { get; }
    // one per distinct file name, in the order asked for
    internal IReadOnlyList<SearchPlanEntry> Entries { get; }
    // problems of the request itself (the folders)
    internal IReadOnlyList<SearchIssue> Issues { get; }
}

internal enum SearchRunStatus
{
    // every chosen search was searched completely (with or without availability)
    Completed,
    // some searches or months were not searched (a file, a request or the campsite details failed)
    CompletedWithProblems,
    // recreation.gov blocked or rate-limited the requests: no further request was made; what was received is kept
    Blocked,
    // the caller canceled the run; searches that had finished keep their results
    Canceled,
    // nothing could be searched
    Failed
}

// a search's state, while the run goes on (progress) and at its end (result)
internal enum SearchState
{
    Waiting,
    Searching,
    // every month it needed was checked; it may have no availability
    Complete,
    // some months were checked, others not (SearchResult.Months says which)
    Incomplete,
    // it was run, but no month could be checked, or it stopped
    Failed,
    Canceled,
    // not run: no campgroundID
    Skipped,
    // not run: the file can't be searched
    Invalid
}

// what the run is doing; the last five are its end, as SearchRunStatus
internal enum SearchRunStage
{
    Validating,
    PreparingCampsiteDetails,
    CheckingAvailability,
    // checking availability, with at least one request waiting to be tried again
    Retrying,
    BuildingResults,
    Completed,
    CompletedWithProblems,
    Blocked,
    Canceled,
    Failed
}

internal enum SearchIssueSeverity
{
    Warning,
    Error
}

internal enum SearchIssueCode
{
    // the request and the search files (Plan)
    InvalidRequest,
    NotASearchFile,
    FileMissing,
    FileUnreadable,
    FileInvalid,
    CampgroundIdMissing,
    // the campsite details (Run)
    CampsiteDataMissing,
    CampsiteDetailsUnavailable,
    CampgroundNotFound,
    // a month's request (Run): why a month was not checked
    NetworkFailure,
    Timeout,
    HttpStatus,
    HeavyTraffic,
    MalformedResponse,
    UnexpectedResponse,
    ResponseTooLarge,
    RequestBlocked,
    RateLimited,
    Canceled,
    InternalError
}

// a problem, in fixed text. Month: the month a request was for; StatusCode: recreation.gov's HTTP status; Attempts: the
// requests made for it; DefectType: the type of an unexpected exception (for diagnostics; never its message)
internal sealed record SearchIssue(SearchIssueCode Code, SearchIssueSeverity Severity, string Message, string? FileName = null,
    DateTime? Month = null, int? StatusCode = null, int Attempts = 0, string? DefectType = null)
{
    internal IReadOnlyList<ConfigDiagnostic> Diagnostics { get; init; } = SearchLists.Empty<ConfigDiagnostic>();
}

// a month a search needed (its first day): checked, or not checked and why. A month that was not checked is never a
// month without availability
internal sealed record SearchMonthResult(DateTime Month, bool Checked, SearchIssueCode? Reason);

// one campsite available for one night. CheckinTime and CheckoutTime are the campsite's own times ("2:00 PM") when it
// has them; BookingUrl is recreation.gov's campsite page, built from the fixed prefix and the escaped campsite ID
internal sealed record SearchOpening(string CampsiteId, string? CampsiteName, string? Loop, string? CampsiteType, IReadOnlyList<string> Equipment,
    DateTime Night, string? CheckinTime, string? CheckoutTime, int? MinOccupancy, int? MaxOccupancy, string BookingUrl);

// consecutive nights at one campsite: arrive on FirstNight, leave on CheckoutDate
internal sealed record SearchStay(DateTime FirstNight, int Nights)
{
    internal DateTime CheckoutDate => FirstNight.AddDays(Nights);
}

// a campsite's openings together: its nights in date order and their runs of consecutive nights
internal sealed record SearchSite(string CampsiteId, string? CampsiteName, string? Loop, string? CampsiteType, IReadOnlyList<string> Equipment,
    string? CheckinTime, string? CheckoutTime, int? MinOccupancy, int? MaxOccupancy, string BookingUrl,
    IReadOnlyList<DateTime> Nights, IReadOnlyList<SearchStay> Stays);

// one chosen search file at the end of a run. Openings: one per campsite and night, ordered by night, then campsite ID
// (shorter first, then ordinal); Sites: by campsite ID the same way. A search with consecutiveDays above 1 has only the
// nights of its runs, as the console and the results page show it; one below 1 shows none (as there)
internal sealed record SearchResult(string FileName, string? DisplayName, bool AutoRun, string? CampgroundId, string? CampgroundName, string? ParkName,
    int ConsecutiveNights, SearchState State, int RequestsPlanned, int RequestsCompleted,
    IReadOnlyList<SearchMonthResult> Months, IReadOnlyList<SearchOpening> Openings, IReadOnlyList<SearchSite> Sites,
    IReadOnlyList<SearchIssue> Issues, IReadOnlyList<ConfigDiagnostic> Diagnostics)
{
    // the search's tagged output stream (ConsoleConfigValue: text, write types, colors, Kind and Metadata, as
    // AvailabilityApi.GetAvailabilitiesByCampground wrote it for this manual search): null unless the run asked for it
    // (SearchHostOptions.KeepOutputValues, D131) and the search ran to its end; a month that was not checked added nothing
    internal IReadOnlyList<ConsoleConfig.ConsoleConfigValue>? OutputValues { get; init; }
}

// the end of a run. Requests are the unique campground-month requests: planned, finished (with data or a final
// failure) and failed. Defects: unexpected exceptions the run contained, for tests and diagnostics only (their messages
// may hold anything and are never shown)
internal sealed record SearchRunResult(SearchRunStatus Status, DateTime StartedUtc, TimeSpan Elapsed, int RequestsPlanned, int RequestsCompleted, int RequestsFailed,
    IReadOnlyList<SearchResult> Searches, IReadOnlyList<SearchIssue> Issues)
{
    internal IReadOnlyList<Exception> Defects { get; init; } = SearchLists.Empty<Exception>();
}

// a snapshot of a run, reported when something changed (a stage, a search's state, a prepared campground, a finished
// request, a retry): never per campsite or per day. Counts are only those whose totals are known
internal sealed record SearchProgress(long Sequence, SearchRunStage Stage, int CampgroundsPlanned, int CampgroundsPrepared,
    int RequestsPlanned, int RequestsCompleted, int RequestsFailed, int RequestsRetrying, IReadOnlyList<SearchProgressItem> Searches);

internal sealed record SearchProgressItem(string FileName, SearchState State);

internal static class SearchLists
{
    // a read-only list over a new array: the caller's list can't change it, and it can't be cast back and changed
    internal static IReadOnlyList<T> Of<T>(IEnumerable<T> Items) => Array.AsReadOnly(Items.ToArray());
    internal static IReadOnlyList<T> Empty<T>() => ReadOnlyCollection<T>.Empty;
}
