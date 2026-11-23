using CampertronLibrary.function.RecDotOrg.search;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The Results tab's presentation of GS1's typed results (GS2). Everything shown here comes from the immutable
    // SearchRunResult and the plan entries: never from console output or the results page, and never a response body, an
    // exception's message, a folder or a value of Email.yaml.

    // One search file in the Results tab's list: whether it can run, whether it is enabled (its autoRun as the file has it),
    // and the check box that chooses it for the next run. An enabled file that can run is chosen by default; one that is
    // not enabled runs when it is chosen (V011-3, D63: never "at startup": the GUI searches only when Start is selected).
    public sealed class ResultsSearchRow : FormObject
    {
        private readonly Action _Changed;
        private bool _IsSelected;
        private bool _Locked;
        private string? _RunState;
        // the campground as the list shows it (its name and place, or its ID)
        private string _Place = string.Empty;

        internal ResultsSearchRow(string FileName, Action Changed)
        {
            this.FileName = FileName;
            _Changed = Changed;
        }

        public string FileName { get; }
        public string Title { get; private set; } = string.Empty;
        public string Details { get; private set; } = string.Empty;
        // why the file can't be searched now, or null
        public string? Reason { get; private set; }
        public bool HasReason => Reason != null;
        public string WarningsText { get; private set; } = string.Empty;
        public bool HasWarnings => WarningsText.Length > 0;
        public bool IsRunnable { get; private set; }
        // autoRun as the file has it (Enabled (autoRun) or Not enabled); whether the file can run is IsRunnable
        public bool IsEnabled { get; private set; }
        // chosen, then deleted on disk before Start: still asked for, so the run reports it as missing
        public bool IsMissing { get; private set; }
        internal bool SelectedByUser { get; set; }

        // the check box: only a runnable file can be chosen, and not while a search runs
        public bool IsSelected
        {
            get => _IsSelected;
            set
            {
                if (_Locked || (value && !IsRunnable) || _IsSelected == value)
                {
                    this.RaisePropertyChanged(nameof(IsSelected));
                    return;
                }
                SelectedByUser = true;
                SetSelected(value);
            }
        }
        public bool CanSelect => IsRunnable && !_Locked;

        // what the last or current run did with it ("Searching", "Complete: 12 openings", ...), or null
        public string? RunState
        {
            get => _RunState;
            internal set
            {
                this.RaiseAndSetIfChanged(ref _RunState, value);
                this.RaisePropertyChanged(nameof(HasRunState));
                this.RaisePropertyChanged(nameof(AccessibleName));
            }
        }
        public bool HasRunState => RunState != null;

        // the setting alone, and what it means here for a file that can run: the default choice, or a run only when chosen
        public string SettingText => IsEnabled ? AutoRunWording.Enabled : AutoRunWording.NotEnabled;
        public string ChoiceText => !IsRunnable ? SettingText : IsEnabled ? AutoRunWording.Enabled + ": selected by default" : AutoRunWording.NotEnabled + ": runs when selected";
        public string CheckBoxName => Title + " (" + FileName + "), " + (IsRunnable ? ChoiceText : SettingText + ", can't be searched now");
        public string HelpText => string.Join(" ", new[] { Details, Reason, WarningsText }.Where(T => !string.IsNullOrEmpty(T)));
        public string AccessibleName => CheckBoxName + (RunState != null ? ". " + RunState : string.Empty);

        internal void SetSelected(bool Value)
        {
            if (_IsSelected != Value)
            {
                _IsSelected = Value;
                this.RaisePropertyChanged(nameof(IsSelected));
                _Changed();
            }
        }

        internal void SetLocked(bool Locked)
        {
            if (_Locked != Locked)
            {
                _Locked = Locked;
                this.RaisePropertyChanged(nameof(CanSelect));
            }
        }

        // the file as the listing read it (the plan's rules, without searching) and whether the Searches tab holds unsaved
        // changes to it
        internal void Update(SearchPlanEntry? Entry, bool Unsaved, string? CampgroundText)
        {
            IsMissing = false;
            IsEnabled = Entry?.AutoRun ?? false;
            Title = !string.IsNullOrWhiteSpace(Entry?.DisplayName) ? Entry!.DisplayName!.Trim() : FileName;
            _Place = CampgroundText ?? (string.IsNullOrWhiteSpace(Entry?.CampgroundId) ? "No campground" : "Campground ID " + Entry!.CampgroundId);
            if (Unsaved)
            {
                IsRunnable = false;
                Reason = "Save or revert the changes in the Searches tab first.";
            }
            else if (Entry == null)
            {
                IsRunnable = false;
                Reason = "Can't be searched: " + CampsiteSearch.Message(SearchIssueCode.FileUnreadable);
            }
            else if (Entry.State == SearchPlanState.Ready)
            {
                IsRunnable = true;
                Reason = null;
            }
            else
            {
                IsRunnable = false;
                ConfigDiagnostic? Error = Entry.Diagnostics.FirstOrDefault(D => D.Severity == ConfigSeverity.Error);
                string Issue = Entry.Issues.Count > 0 ? Entry.Issues[0].Message : CampsiteSearch.Message(SearchIssueCode.FileInvalid);
                Reason = "Can't be searched: " + Issue + (Error != null ? " " + ResultText.Diagnostic(Error) : string.Empty);
            }
            WarningsText = IsRunnable
                ? string.Join(" ", Entry!.Diagnostics.Where(D => D.Severity == ConfigSeverity.Warning).Select(ResultText.Diagnostic))
                : string.Empty;
            // what the choice means once it is known whether the file can run
            Details = DetailsText();
            if (!IsRunnable && _IsSelected)
            {
                SetSelected(false);
            }
            RaiseAll();
        }

        internal void MarkMissing()
        {
            IsMissing = true;
            IsRunnable = false;
            Reason = "Not found in the settings folder any more; the search reports it as missing.";
            WarningsText = string.Empty;
            // the file and its campground as listed, with the setting alone (it can't be searched now)
            Details = DetailsText();
            RaiseAll();
        }

        private string DetailsText() => string.Join(" · ", new[] { FileName, _Place, ChoiceText }.Where(T => T.Length > 0));

        private void RaiseAll()
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Details));
            this.RaisePropertyChanged(nameof(Reason));
            this.RaisePropertyChanged(nameof(HasReason));
            this.RaisePropertyChanged(nameof(WarningsText));
            this.RaisePropertyChanged(nameof(HasWarnings));
            this.RaisePropertyChanged(nameof(IsRunnable));
            this.RaisePropertyChanged(nameof(IsEnabled));
            this.RaisePropertyChanged(nameof(IsMissing));
            this.RaisePropertyChanged(nameof(CanSelect));
            this.RaisePropertyChanged(nameof(SettingText));
            this.RaisePropertyChanged(nameof(ChoiceText));
            this.RaisePropertyChanged(nameof(CheckBoxName));
            this.RaisePropertyChanged(nameof(HelpText));
            this.RaisePropertyChanged(nameof(AccessibleName));
        }
    }

    // How a search ended, as the Results tab tells it apart (by text, never by color alone).
    public enum ResultKind { Openings, NoAvailability, Incomplete, Failed, Blocked, Canceled, NotSearched }

    // One search of a finished run: its summary and, built when it is first shown, its rows.
    public sealed class ResultSearchViewModel
    {
        private readonly SearchResult _Result;
        private IReadOnlyList<ResultRow>? _Rows;

        internal ResultSearchViewModel(SearchResult Result, bool RunBlocked)
        {
            _Result = Result;
            FileName = Result.FileName;
            Title = !string.IsNullOrWhiteSpace(Result.DisplayName) ? Result.DisplayName!.Trim() : Result.FileName;
            Openings = Result.Openings.Count;
            Campsites = Result.Sites.Count;
            Nights = Result.Openings.Select(O => O.Night.Date).Distinct().Count();
            bool StoppedByBlock = Result.Months.Any(M => !M.Checked && (M.Reason == SearchIssueCode.RequestBlocked || M.Reason == SearchIssueCode.RateLimited));
            Kind = Result.State switch
            {
                SearchState.Complete => Openings > 0 ? ResultKind.Openings : ResultKind.NoAvailability,
                SearchState.Incomplete or SearchState.Failed when RunBlocked && StoppedByBlock => ResultKind.Blocked,
                SearchState.Incomplete => ResultKind.Incomplete,
                SearchState.Canceled => ResultKind.Canceled,
                SearchState.Skipped or SearchState.Invalid or SearchState.Waiting => ResultKind.NotSearched,
                _ => ResultKind.Failed,
            };
            StatusText = Kind switch
            {
                ResultKind.Openings => "Complete: " + Count(Openings, "opening", "openings") + " found",
                ResultKind.NoAvailability => "Complete: no matching availability in the months checked",
                ResultKind.Incomplete => "Incomplete: some months were not checked" + (Openings > 0 ? "; " + Count(Openings, "opening", "openings") + " found in the others" : string.Empty),
                ResultKind.Blocked => "Stopped: recreation.gov limited the requests before every month was checked" + (Openings > 0 ? "; " + Count(Openings, "opening", "openings") + " found before that" : string.Empty),
                ResultKind.Canceled => "Canceled before it finished" + (Openings > 0 ? "; " + Count(Openings, "opening", "openings") + " found before that" : string.Empty),
                ResultKind.NotSearched => Result.State == SearchState.Skipped ? "Not searched: no campground is set (campgroundID)" : "Not searched: the file can't be searched",
                _ => "Failed: no month could be checked",
            };
            var Place = new List<string>();
            if (!string.IsNullOrWhiteSpace(Result.CampgroundName))
            {
                Place.Add(Result.CampgroundName!.Trim());
            }
            if (!string.IsNullOrWhiteSpace(Result.ParkName))
            {
                Place.Add(Result.ParkName!.Trim());
            }
            if (!string.IsNullOrWhiteSpace(Result.CampgroundId))
            {
                Place.Add("ID " + Result.CampgroundId);
            }
            CampgroundText = Place.Count > 0 ? string.Join(" · ", Place) : "No campground";
            CountsText = Result.ConsecutiveNights > 1
                ? Count(Campsites, "campsite", "campsites") + " with at least " + Result.ConsecutiveNights + " nights in a row (" + Count(Openings, "night", "nights") + " in all)"
                : Count(Openings, "opening", "openings") + " at " + Count(Campsites, "campsite", "campsites") + " on " + Count(Nights, "night", "nights");
            List<SearchMonthResult> Checked = Result.Months.Where(M => M.Checked).ToList();
            List<SearchMonthResult> NotChecked = Result.Months.Where(M => !M.Checked).ToList();
            MonthsText = Result.Months.Count == 0
                ? "No month was searched."
                : (Checked.Count > 0 ? "Months checked: " + string.Join(", ", Checked.Select(M => ResultText.Month(M.Month))) + "." : "No month was checked.")
                  + (NotChecked.Count > 0 ? " Not checked: " + string.Join("; ", NotChecked.Select(M => ResultText.Month(M.Month) + " (" + ResultText.Reason(M.Reason) + ")")) + "." : string.Empty);
            IssueLines = Result.Issues.Select(ResultText.Issue)
                .Concat(Result.Diagnostics.Where(D => D.Severity != ConfigSeverity.Info).Select(ResultText.Diagnostic))
                .Distinct(StringComparer.Ordinal).ToList();
            EmptyText = Kind switch
            {
                ResultKind.NoAvailability => "No campsite matched this search in the months checked.",
                ResultKind.NotSearched => "This search was not run.",
                ResultKind.Canceled when Openings == 0 => "The search was canceled before it found anything. Months not checked are not shown as without availability.",
                ResultKind.Openings => string.Empty,
                _ => Openings == 0 ? "Nothing was found in the months checked. The months that were not checked are listed above; they are not without availability." : string.Empty,
            };
        }

        public string FileName { get; }
        public string Title { get; }
        public ResultKind Kind { get; }
        public string StatusText { get; }
        public string CampgroundText { get; }
        public string CountsText { get; }
        public string MonthsText { get; }
        public IReadOnlyList<string> IssueLines { get; }
        public bool HasIssues => IssueLines.Count > 0;
        public int Openings { get; }
        public int Campsites { get; }
        public int Nights { get; }
        public string EmptyText { get; }
        public bool HasEmptyText => EmptyText.Length > 0;
        public bool IsComplete => Kind == ResultKind.Openings || Kind == ResultKind.NoAvailability;
        public string ChoiceLabel => Title + " (" + FileName + ") · " + StatusText;
        public override string ToString() => ChoiceLabel;

        // the rows of the result list, built once when the search is first shown: single nights by date (each date's
        // campsites in GS1's order, by campsite ID), consecutive nights by campsite with each stay's dates
        public IReadOnlyList<ResultRow> Rows => _Rows ??= BuildRows();

        private IReadOnlyList<ResultRow> BuildRows()
        {
            var Rows = new List<ResultRow>(_Result.Openings.Count + 64);
            if (_Result.ConsecutiveNights > 1)
            {
                foreach (SearchSite Site in _Result.Sites)
                {
                    var SiteRow = new ResultSiteRow(Site);
                    Rows.Add(SiteRow);
                    foreach (SearchStay Stay in Site.Stays.OrderBy(S => S.FirstNight))
                    {
                        Rows.Add(new ResultStayRow(SiteRow, Stay));
                    }
                }
                return Rows;
            }
            foreach (IGrouping<DateTime, SearchOpening> Night in _Result.Openings.GroupBy(O => O.Night.Date).OrderBy(G => G.Key))
            {
                List<SearchOpening> Sites = Night.ToList();
                Rows.Add(new ResultDateRow(Night.Key, Sites.Count));
                foreach (SearchOpening Opening in Sites)
                {
                    Rows.Add(new ResultOpeningRow(Opening));
                }
            }
            return Rows;
        }

        internal static string Count(int Number, string One, string Many) => Number.ToString("N0", CultureInfo.CurrentCulture) + " " + (Number == 1 ? One : Many);
    }

    // A row of the result list. Campsite rows carry the campsite's ID and booking link for the actions.
    public abstract class ResultRow
    {
        public abstract string AccessibleName { get; }
        public virtual string? CampsiteId => null;
        public virtual string? BookingUrl => null;
        public virtual string? CampsiteTitle => null;
        public bool HasCampsite => CampsiteId != null;
    }

    // the heading of one night (single nights)
    public sealed class ResultDateRow : ResultRow
    {
        internal ResultDateRow(DateTime Night, int Campsites)
        {
            Text = ResultText.Night(Night) + " · " + ResultSearchViewModel.Count(Campsites, "campsite", "campsites");
        }
        public string Text { get; }
        public override string AccessibleName => Text;
    }

    // one campsite available on one night
    public sealed class ResultOpeningRow : ResultRow
    {
        private readonly SearchOpening _Opening;

        internal ResultOpeningRow(SearchOpening Opening)
        {
            _Opening = Opening;
            Title = ResultText.Campsite(Opening.CampsiteName, Opening.CampsiteId);
            IdText = "ID " + Opening.CampsiteId;
            Details = ResultText.Details(Opening.Loop, Opening.CampsiteType, Opening.Equipment, Opening.CheckinTime, Opening.CheckoutTime, Opening.MinOccupancy, Opening.MaxOccupancy);
        }
        public string Title { get; }
        public string IdText { get; }
        public string Details { get; }
        public bool HasDetails => Details.Length > 0;
        public override string? CampsiteId => _Opening.CampsiteId;
        public override string? BookingUrl => _Opening.BookingUrl;
        public override string? CampsiteTitle => Title;
        public override string AccessibleName => ResultText.Night(_Opening.Night) + ": " + Title + ", " + IdText + (Details.Length > 0 ? ", " + Details : string.Empty);
    }

    // one campsite with consecutive nights (its stays follow it)
    public sealed class ResultSiteRow : ResultRow
    {
        private readonly SearchSite _Site;

        internal ResultSiteRow(SearchSite Site)
        {
            _Site = Site;
            Title = ResultText.Campsite(Site.CampsiteName, Site.CampsiteId);
            IdText = "ID " + Site.CampsiteId;
            Details = ResultText.Details(Site.Loop, Site.CampsiteType, Site.Equipment, Site.CheckinTime, Site.CheckoutTime, Site.MinOccupancy, Site.MaxOccupancy);
            StaysText = ResultSearchViewModel.Count(Site.Stays.Count, "stay", "stays") + " · " + ResultSearchViewModel.Count(Site.Nights.Count, "night", "nights");
        }
        public string Title { get; }
        public string IdText { get; }
        public string Details { get; }
        public bool HasDetails => Details.Length > 0;
        public string StaysText { get; }
        public override string? CampsiteId => _Site.CampsiteId;
        public override string? BookingUrl => _Site.BookingUrl;
        public override string? CampsiteTitle => Title;
        public override string AccessibleName => Title + ", " + IdText + ", " + StaysText + (Details.Length > 0 ? ", " + Details : string.Empty);
    }

    // one stay of consecutive nights at the campsite above it
    public sealed class ResultStayRow : ResultRow
    {
        private readonly ResultSiteRow _Site;

        internal ResultStayRow(ResultSiteRow Site, SearchStay Stay)
        {
            _Site = Site;
            Text = "Arrive " + ResultText.Night(Stay.FirstNight) + ", leave " + ResultText.Night(Stay.CheckoutDate) + " · "
                + ResultSearchViewModel.Count(Stay.Nights, "night", "nights");
        }
        public string Text { get; }
        public override string? CampsiteId => _Site.CampsiteId;
        public override string? BookingUrl => _Site.BookingUrl;
        public override string? CampsiteTitle => _Site.Title;
        public override string AccessibleName => _Site.Title + ", " + _Site.IdText + ": " + Text;
    }

    // Texts of the Results tab, in fixed words around the data.
    internal static class ResultText
    {
        internal static string Night(DateTime Night) => Night.ToString("D", CultureInfo.CurrentCulture);
        internal static string Month(DateTime Month) => Month.ToString("Y", CultureInfo.CurrentCulture);

        // as the results page names a campsite: "Site 086" for a bare name or number, a name that starts with "Site" as it is
        internal static string Campsite(string? Name, string Id)
        {
            string Shown = !string.IsNullOrWhiteSpace(Name) ? Name!.Trim() : Id;
            return Shown.StartsWith("site", StringComparison.OrdinalIgnoreCase) ? Shown : "Site " + Shown;
        }

        // only the parts the campsite has: no empty labels; the loop and type as stored, as the results page and the console
        // show them
        internal static string Details(string? Loop, string? Type, IReadOnlyList<string> Equipment, string? Checkin, string? Checkout, int? Min, int? Max)
        {
            var Parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Loop))
            {
                Parts.Add(Loop!.Trim());
            }
            if (!string.IsNullOrWhiteSpace(Type))
            {
                Parts.Add(Type!.Trim());
            }
            List<string> Allowed = Equipment.Where(E => !string.IsNullOrWhiteSpace(E)).Select(E => E.Trim()).ToList();
            if (Allowed.Count > 0)
            {
                Parts.Add(string.Join(", ", Allowed));
            }
            if (!string.IsNullOrWhiteSpace(Checkin))
            {
                Parts.Add("check-in " + Checkin!.Trim());
            }
            if (!string.IsNullOrWhiteSpace(Checkout))
            {
                Parts.Add("checkout " + Checkout!.Trim());
            }
            if (Min.HasValue && Max.HasValue && Max.Value > 0)
            {
                Parts.Add(Min.Value == Max.Value ? Max.Value + " people" : Min.Value + " to " + Max.Value + " people");
            }
            else if (Max.HasValue && Max.Value > 0)
            {
                Parts.Add("up to " + Max.Value + " people");
            }
            return string.Join(" · ", Parts);
        }

        // why a month was not checked, in a few words
        internal static string Reason(SearchIssueCode? Code) => Code switch
        {
            SearchIssueCode.NetworkFailure => "recreation.gov could not be reached",
            SearchIssueCode.Timeout => "recreation.gov did not answer in time",
            SearchIssueCode.HttpStatus => "recreation.gov answered with an error",
            SearchIssueCode.HeavyTraffic => "recreation.gov reported heavy traffic",
            SearchIssueCode.MalformedResponse or SearchIssueCode.UnexpectedResponse => "the answer could not be read",
            SearchIssueCode.ResponseTooLarge => "the answer was too large",
            SearchIssueCode.RequestBlocked => "recreation.gov blocked the requests",
            SearchIssueCode.RateLimited => "recreation.gov asked for fewer requests",
            SearchIssueCode.CampsiteDataMissing or SearchIssueCode.CampsiteDetailsUnavailable => "the campsite details were not available",
            SearchIssueCode.Canceled => "canceled",
            _ => "an unexpected problem",
        };

        // a problem of a search or the run: its severity written out, its fixed text, the month, the HTTP status and the
        // requests made; never a body, an exception's message or a value of a file
        internal static string Issue(SearchIssue Issue)
        {
            string Text = (Issue.Severity == SearchIssueSeverity.Error ? "Error: " : "Warning: ") + Issue.Message;
            var Facts = new List<string>();
            if (Issue.Month.HasValue)
            {
                Facts.Add(Month(Issue.Month.Value));
            }
            string Status = Issue.StatusCode.HasValue ? "HTTP " + Issue.StatusCode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            if (Status.Length > 0 && !Issue.Message.Contains(Status, StringComparison.Ordinal))
            {
                Facts.Add(Status);
            }
            if (Issue.Attempts > 0)
            {
                Facts.Add(ResultSearchViewModel.Count(Issue.Attempts, "request", "requests"));
            }
            return Facts.Count > 0 ? Text + " (" + string.Join(", ", Facts) + ")" : Text;
        }

        // a configuration diagnostic: fixed text, key names and numbers only (CM3)
        internal static string Diagnostic(ConfigDiagnostic Diagnostic)
        {
            string Severity = Diagnostic.Severity == ConfigSeverity.Error ? "Error: " : Diagnostic.Severity == ConfigSeverity.Warning ? "Warning: " : "Info: ";
            return Severity + Diagnostic.Message + (Diagnostic.Line > 0 ? " (line " + Diagnostic.Line.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty);
        }

        internal static string Elapsed(TimeSpan Time)
        {
            TimeSpan Whole = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, Time.TotalSeconds)));
            return Whole.TotalHours >= 1
                ? ((int)Whole.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + Whole.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + Whole.Seconds.ToString("00", CultureInfo.InvariantCulture)
                : Whole.Minutes.ToString(CultureInfo.InvariantCulture) + ":" + Whole.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    // A booking link from a result, checked before it reaches the platform's launcher or the clipboard: exactly the link
    // GS1 builds for that campsite (https, www.recreation.gov, /camping/campsites/<the escaped campsite ID>), with nothing
    // else in it. No shell, no command line, no process.
    internal static class BookingLink
    {
        internal const string Host = "www.recreation.gov";

        internal static bool TryCreate(string? Url, string? CampsiteId, [NotNullWhen(true)] out Uri? Link)
        {
            Link = null;
            if (string.IsNullOrEmpty(Url) || string.IsNullOrEmpty(CampsiteId)
                || !string.Equals(Url, CampsiteSearch.BookingUrl(CampsiteId), StringComparison.Ordinal)
                || !Uri.TryCreate(Url, UriKind.Absolute, out Uri? Parsed))
            {
                return false;
            }
            if (Parsed.Scheme != Uri.UriSchemeHttps || !string.Equals(Parsed.Host, Host, StringComparison.Ordinal) || !Parsed.IsDefaultPort
                || Parsed.UserInfo.Length != 0 || Parsed.Query.Length != 0 || Parsed.Fragment.Length != 0
                || Parsed.Segments.Length != 4 || Parsed.Segments[1] != "camping/" || Parsed.Segments[2] != "campsites/"
                || !string.Equals(Parsed.Segments[3], Uri.EscapeDataString(CampsiteId), StringComparison.Ordinal))
            {
                return false;
            }
            Link = Parsed;
            return true;
        }
    }
}
