using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronLibrary.function.Forms
{
    // What happens when a run of the Results tab starts and when it has ended (GS2), for every UI. The run itself (its
    // thread, how its progress and its end reach the form's thread, its timers) is each host's own (D91): the desktop
    // GUI's ResultsViewModel, the web UI's WebResults (since v0.3.0's PR 7, D97). Both start and end a run through these
    // rules, so they refuse the same starts with the same words and show an ended run the same way.

    // A Start: the search files are read again first (a chosen file the disk unchose means nothing starts, and the list
    // says which), then GS1's plan reads each chosen file once; nothing starts, and the previous results stay, unless at
    // least one chosen file can be searched. Plan is null when nothing starts; Status then says why.
    internal sealed class ResultsStart
    {
        private ResultsStart(SearchRunPlan? Plan, IReadOnlyList<string> Files, string? Status, string? ListNotice)
        {
            this.Plan = Plan;
            this.Files = Files;
            this.Status = Status;
            this.ListNotice = ListNotice;
        }

        internal SearchRunPlan? Plan { get; }
        // the chosen files, in the list's order (the run's request)
        internal IReadOnlyList<string> Files { get; }
        // why nothing started (the status line), or null
        internal string? Status { get; }
        // the list's notice when the disk unchose files, or null
        internal string? ListNotice { get; }

        // RescanAtStart: the host's look at the disk with AtStart (ResultsSearchList.Rescan), returning the chosen files it
        // unchose; MakePlan: GS1's plan of the chosen files (CampsiteSearch.Plan)
        internal static ResultsStart Prepare(IReadOnlyCollection<ResultsSearchRow> Rows, Func<List<string>> RescanAtStart, Func<IReadOnlyList<string>, SearchRunPlan> MakePlan)
        {
            List<string> Unchosen = RescanAtStart();
            if (Unchosen.Count > 0)
            {
                return NotStarted(ResultsWording.NotStartedChangedOnDisk, ResultsWording.ChangedOnDisk(Unchosen));
            }
            List<string> Names = Rows.Where(R => R.IsSelected).Select(R => R.FileName).ToList();
            if (Names.Count == 0)
            {
                return NotStarted(ResultsWording.NotStartedNoChoice);
            }
            SearchRunPlan Plan;
            try
            {
                Plan = MakePlan(Names);
            }
            catch (Exception)
            {
                return NotStarted(ResultsWording.NotStartedUnreadable);
            }
            if (!Plan.Entries.Any(E => E.State == SearchPlanState.Ready))
            {
                foreach (SearchPlanEntry Entry in Plan.Entries)
                {
                    ResultsSearchRow? Row = Rows.FirstOrDefault(R => R.FileName == Entry.FileName);
                    if (Row != null)
                    {
                        Row.RunState = ResultsWording.CantBeSearched(Entry);
                    }
                }
                return NotStarted(ResultsWording.NotStartedNoneReady);
            }
            return new ResultsStart(Plan, Names, null, null);
        }

        private static ResultsStart NotStarted(string Status, string? ListNotice = null) =>
            new ResultsStart(null, Array.Empty<string>(), Status, ListNotice);
    }

    // An ended run as the Results tab shows it, built once from GS1's immutable SearchRunResult: its summary, its issues,
    // each search's result (ResultSearchViewModel), the search shown first (the first with openings) and what each listed
    // file's row says. Nothing in it changes afterwards but each search's rows, built when that search is first shown, on
    // the host's thread.
    public sealed class ResultsOutcome
    {
        private readonly IReadOnlyList<string> _Files;

        internal ResultsOutcome(SearchRunResult Result, IReadOnlyList<string> Files)
        {
            this.Result = Result;
            _Files = Files;
            bool Blocked = Result.Status == SearchRunStatus.Blocked;
            Searches = Result.Searches.Select(S => new ResultSearchViewModel(S, Blocked)).ToList().AsReadOnly();
            Openings = Searches.Sum(S => S.Openings);
            Summary = ResultsWording.Summary(Result.Status, Openings, Searches.Count);
            IssueLines = Result.Issues.Select(ResultText.Issue).Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
            Shown = Searches.FirstOrDefault(S => S.Openings > 0) ?? Searches.FirstOrDefault();
        }

        internal SearchRunResult Result { get; }
        internal SearchRunStatus Status => Result.Status;
        public IReadOnlyList<ResultSearchViewModel> Searches { get; }
        public int Openings { get; }
        // "Finished: …", "Stopped: …", "Canceled. …", "Nothing could be searched."
        public string Summary { get; }
        public IReadOnlyList<string> IssueLines { get; }
        public bool HasIssues => IssueLines.Count > 0;
        // the search shown first: the first with openings, else the first
        public ResultSearchViewModel? Shown { get; }
        public bool HadProblems => Result.Status != SearchRunStatus.Completed;
        // recreation.gov blocked the run: Start waits five minutes (BlockHold)
        public bool WasBlocked => Result.Status == SearchRunStatus.Blocked;

        // what a listed file's row says after the run: its search's status, "Not searched" for a chosen file the run did not
        // return, nothing for a file that was not chosen
        public string? RunStateOf(string FileName) =>
            Searches.FirstOrDefault(S => S.FileName == FileName)?.StatusText ?? (_Files.Contains(FileName, StringComparer.Ordinal) ? ResultsWording.NotSearched : null);

        // a run that ended in an unexpected exception (GS1 never throws for an outcome): a failed run whose problem names only
        // the exception's type, never its message
        internal static SearchRunResult Unexpected(Exception? ex)
        {
            var Problem = new SearchIssue(SearchIssueCode.InternalError, SearchIssueSeverity.Error, CampsiteSearch.Message(SearchIssueCode.InternalError),
                DefectType: ex?.GetType().FullName);
            return new SearchRunResult(SearchRunStatus.Failed, DateTime.UtcNow, TimeSpan.Zero, 0, 0, 0, SearchLists.Empty<SearchResult>(), SearchLists.Of(new[] { Problem }))
            {
                Defects = ex == null ? SearchLists.Empty<Exception>() : SearchLists.Of(new[] { ex }),
            };
        }
    }
}
