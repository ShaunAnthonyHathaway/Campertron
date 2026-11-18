using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Campertron.ViewModels
{
    // One search file in the Searches tab (CM6, decision D44): every setting of a search (CampertronConfig), typed, edited
    // through the same session as General.yaml and Email.yaml (the YAML document editor, the validation, the preview, the
    // safe writer and the external-change checks), so comments, formatting and every untouched value stay as they are.
    // Saving writes the file only; nothing here runs the search or shows availability.
    public sealed class SearchFileViewModel : SettingsFileViewModel<CampertronConfig>
    {
        private static readonly SettingChoice[] OfferedSearchTypes =
        {
            new SettingChoice(SearchTypes.DaysOut, "DaysOut: from today, a number of days ahead"),
            new SettingChoice(SearchTypes.MonthsOut, "MonthsOut: from today, a number of months ahead"),
            new SettingChoice(SearchTypes.SpecificDates, "SpecificDates: the dates listed"),
            new SettingChoice(SearchTypes.Until, "Until: from today until the first date listed"),
            new SettingChoice(SearchTypes.StartEndDate, "StartEndDate: from the first date listed to the second"),
        };

        // filterMatching's choice for a file without it: no edit, the search uses Contains (D51)
        private static readonly object MatchingNotSet = new object();
        // filterMatching as the file has it when it is no choice (empty, a number): kept until the user picks one
        private static readonly object MatchingInvalid = new object();

        private string? _WeekdaysMessage;
        private string _WeekdaysSeverity = string.Empty;
        private readonly FilterChoiceSource? _ChoiceSource;
        // the campground whose values the selectors show or are loading, and the number of the newest request: an answer
        // for an older request (another campground chosen meanwhile) is ignored
        private string? _ChoicesFor;
        private int _ChoicesRequest;
        private bool _IsActive;

        internal SearchFileViewModel(ConfigEditSession<CampertronConfig> Session, CampgroundCatalog Catalog) : this(Session, Catalog, null)
        {
        }

        internal SearchFileViewModel(ConfigEditSession<CampertronConfig> Session, CampgroundCatalog Catalog, FilterChoiceSource? ChoiceSource) : base(Session)
        {
            _ChoiceSource = ChoiceSource;
            DisplayName = new TextSettingField("displayName", "_Name (displayName)", "The search's name in the results.");
            AutoRun = new BoolSettingField("autoRun", AutoRunWording.Label, AutoRunWording.Hint);
            Campground = new CampgroundSettingField("campgroundID", "Campground (campgroundID)", "The campground the search looks at, identified by its ID.", Catalog);
            TotalHumans = new TextSettingField("totalHumans", "_People (totalHumans)", "How many people the campsite must allow.");
            SearchBy = new ChoiceSettingField("searchBy", "_Dates to search (searchBy)", "Which dates the search looks at.");
            SearchValue = new TextSettingField("searchValue", "Days or _months ahead (searchValue)", "Used by DaysOut and MonthsOut: how many days or months from today.");
            SearchValueDates = new ListSettingField("searchValueDates", "Dat_es (searchValueDates)", "Used by SpecificDates, Until and StartEndDate. Write dates as yyyy-MM-dd, such as 2026-07-04.", "date", "A date, written as yyyy-MM-dd.");
            ConsecutiveDays = new TextSettingField("consecutiveDays", "_Consecutive nights (consecutiveDays)", "1 shows single nights; 2 or more shows only runs of that many nights in a row at one campsite.");
            Weekdays = new List<BoolSettingField>
            {
                new BoolSettingField("showMonday", "Monday", "Show openings on Mondays."),
                new BoolSettingField("showTuesday", "Tuesday", "Show openings on Tuesdays."),
                new BoolSettingField("showWednesday", "Wednesday", "Show openings on Wednesdays."),
                new BoolSettingField("showThursday", "Thursday", "Show openings on Thursdays."),
                new BoolSettingField("showFriday", "Friday", "Show openings on Fridays."),
                new BoolSettingField("showSaturday", "Saturday", "Show openings on Saturdays."),
                new BoolSettingField("showSunday", "Sunday", "Show openings on Sundays."),
            };
            Matching = new ChoiceSettingField("filterMatching", "_Matching (filterMatching)",
                "How the include and exclude lists below match a campsite: Exact (a value equals the campsite's value) or Contains (the matching of searches written before 0.1.0-preview.2). Case, spaces and punctuation count either way.");
            IncludeEquipment = Selector("includeEquipment", "Include equipment (includeEquipment)", "Only campsites that allow at least one of the checked equipment types.",
                FilterField.Equipment, true, "equipment type", "equipment types");
            ExcludeEquipment = Selector("excludeEquipment", "Exclude equipment (excludeEquipment)", "Leave out campsites that allow any of the checked equipment types.",
                FilterField.Equipment, false, "equipment type", "equipment types");
            IncludeSites = Selector("includeSites", "Include campsites (includeSites)", "Only the checked campsites, by their name (names always match exactly).",
                FilterField.Sites, true, "campsite name", "campsite names");
            ExcludeSites = Selector("excludeSites", "Exclude campsites (excludeSites)", "Leave out the checked campsites, by their name (names always match exactly).",
                FilterField.Sites, false, "campsite name", "campsite names");
            IncludeAttributes = Selector("includeAttributes", "Include attributes (includeAttributes)", "Only campsites with at least one of the checked attributes (each written Name:Value in the file).",
                FilterField.Attributes, true, "attribute value", "attribute values");
            ExcludeAttributes = Selector("excludeAttributes", "Exclude attributes (excludeAttributes)", "Leave out campsites with any of the checked attributes (each written Name:Value in the file).",
                FilterField.Attributes, false, "attribute value", "attribute values");
            IncludeCampsiteType = Selector("includeCampsiteType", "Include campsite types (includeCampsiteType)", "Only campsites of at least one of the checked types.",
                FilterField.CampsiteType, true, "campsite type", "campsite types");
            ExcludeCampsiteType = Selector("excludeCampsiteType", "Exclude campsite types (excludeCampsiteType)", "Leave out campsites of any of the checked types.",
                FilterField.CampsiteType, false, "campsite type", "campsite types");
            Selectors = new List<FilterSelectorField> { IncludeEquipment, ExcludeEquipment, IncludeSites, ExcludeSites, IncludeAttributes, ExcludeAttributes, IncludeCampsiteType, ExcludeCampsiteType };
            RetryChoicesCommand = new RelayCommand(RetryChoices, () => Selectors.Any(S => S.Status == FilterChoicesStatus.Failed));

            Add(DisplayName);
            Add(AutoRun);
            Add(Campground);
            Add(TotalHumans);
            Add(SearchBy);
            Add(SearchValue);
            Add(SearchValueDates);
            Add(ConsecutiveDays);
            foreach (BoolSettingField Day in Weekdays)
            {
                Add(Day);
            }
            Add(Matching);
            foreach (FilterSelectorField Selector in Selectors)
            {
                Add(Selector);
            }
            Load();
        }

        private static FilterSelectorField Selector(string Key, string Label, string Hint, FilterField Field, bool IsInclude, string ItemName, string ItemsName) =>
            new FilterSelectorField(Key, Label, Hint, Field, IsInclude, ItemName, ItemsName);

        public override string Title => string.IsNullOrWhiteSpace(Session.Value("displayName") as string) ? FileName : (string)Session.Value("displayName")!;
        public override string PreviewNote => "Lines starting with - are replaced by the lines starting with +. Nothing else in the file changes.";

        public TextSettingField DisplayName { get; }
        public BoolSettingField AutoRun { get; }
        public CampgroundSettingField Campground { get; }
        public TextSettingField TotalHumans { get; }
        public ChoiceSettingField SearchBy { get; }
        public TextSettingField SearchValue { get; }
        public ListSettingField SearchValueDates { get; }
        public TextSettingField ConsecutiveDays { get; }
        public List<BoolSettingField> Weekdays { get; }
        public ChoiceSettingField Matching { get; }
        public FilterSelectorField IncludeEquipment { get; }
        public FilterSelectorField ExcludeEquipment { get; }
        public FilterSelectorField IncludeSites { get; }
        public FilterSelectorField ExcludeSites { get; }
        public FilterSelectorField IncludeAttributes { get; }
        public FilterSelectorField ExcludeAttributes { get; }
        public FilterSelectorField IncludeCampsiteType { get; }
        public FilterSelectorField ExcludeCampsiteType { get; }
        // the database-backed selectors of the eight include and exclude lists (D51 to D54)
        public List<FilterSelectorField> Selectors { get; }
        public RelayCommand RetryChoicesCommand { get; }
        // the campground's values could not be read (Retry is offered)
        public bool HasChoicesProblem => Selectors.Any(S => S.Status == FilterChoicesStatus.Failed);

        // the matching the search uses: the file's filterMatching, or Contains without it (D51); a value that is no choice
        // shows as Contains here (the validation reports it)
        internal FilterMatchMode EffectiveMatching => Session.Value("filterMatching") is FilterMatchMode Mode && Enum.IsDefined(Mode) ? Mode : FilterMatchMode.Contains;

        // the validation's message about the days of the week (no day enabled), shown with the check boxes
        public string? WeekdaysMessage => _WeekdaysMessage;
        public bool HasWeekdaysMessage => _WeekdaysMessage != null;
        public string WeekdaysMessageText => _WeekdaysMessage == null ? string.Empty : _WeekdaysSeverity + ": " + _WeekdaysMessage;
        public bool WeekdaysMessageIsError => _WeekdaysSeverity == "Error";

        // ---- the file in the list of search files ----

        // the autoRun setting as edited (or as the file has it): Enabled (autoRun) or Not enabled. It is not whether the
        // search can run: a search with errors can't be searched either way, and one that is not enabled still runs when
        // it is selected in the Results tab (V011-3, D63)
        public bool IsEnabledSearch => Session.Value("autoRun") is true;
        // whether the file's autoRun could be read (a file that can't be read or loaded has none)
        private bool AutoRunKnown => Session.State != ConfigEditState.Unreadable && Session.State != ConfigEditState.Missing
            && (Session.State == ConfigEditState.Editable || Session.OriginalValue("autoRun") != null);
        public string ListDetails
        {
            get
            {
                if (Session.State == ConfigEditState.Unreadable || Session.State == ConfigEditState.Missing)
                {
                    return "Can't be read";
                }
                string Name = Session.Value("displayName") is string N && !string.IsNullOrWhiteSpace(N) ? N : "(no name)";
                string Run = !AutoRunKnown ? "not read" : IsEnabledSearch ? AutoRunWording.Enabled : AutoRunWording.NotEnabled;
                string Where = Campground.Id == null ? "no campground" : Session.Campground?.Label ?? "ID " + Campground.Id;
                return Name + " \u00B7 " + Run + " \u00B7 " + Where;
            }
        }
        // what the setting means for this file, for screen readers and the tooltip: whether the console runs it and how
        // the Results tab treats it, or that its errors keep it from being searched (a setting is never told as whether
        // the search can run)
        public string ListRunNote => !AutoRunKnown ? string.Empty
            : Session.Notices.Any(N => N.Severity == ConfigSeverity.Error) ? AutoRunWording.HasErrorsNote
            : IsEnabledSearch ? AutoRunWording.EnabledNote : AutoRunWording.NotEnabledNote;
        public string ListStatus
        {
            get
            {
                var Parts = new List<string>();
                Parts.Add(Session.State switch
                {
                    ConfigEditState.Missing => "Deleted on disk",
                    ConfigEditState.Unreadable => "Can't be read",
                    ConfigEditState.ReadOnly => "Read-only",
                    _ => Session.Notices.Any(N => N.Severity == ConfigSeverity.Error) ? "Has errors"
                        : Session.Notices.Any(N => N.Severity == ConfigSeverity.Warning) ? "Has warnings" : "No problems"
                });
                if (HasConflict)
                {
                    Parts.Add("changed on disk while edited");
                }
                else if (IsDirty)
                {
                    Parts.Add("unsaved changes");
                }
                return string.Join(", ", Parts);
            }
        }
        public bool ListStatusIsError => Session.State != ConfigEditState.Editable || HasConflict || Session.Notices.Any(N => N.Severity == ConfigSeverity.Error);
        public string ListAccessibleName => FileName + ", " + ListDetails + ", " + ListStatus + (ListRunNote.Length > 0 ? ". " + ListRunNote : string.Empty);

        // ---- filling and applying ----

        protected override void Fill()
        {
            DisplayName.Fill(Session.Value("displayName") as string ?? string.Empty);
            AutoRun.Fill(Session.Value("autoRun") is true);
            Campground.Fill(Session.Value("campgroundID") as string);
            TotalHumans.Fill(Session.Value("totalHumans") is int People ? People.ToString(CultureInfo.InvariantCulture) : string.Empty);
            SearchTypes? Current = Session.Value("searchBy") as SearchTypes?;
            List<SettingChoice> Choices = OfferedSearchTypes.ToList();
            SettingChoice? Selected = Choices.FirstOrDefault(C => Current != null && C.Value.Equals(Current.Value));
            if (Current != null && Selected == null)
            {
                // a number that is no search type: kept as the file has it, never offered
                Selected = new SettingChoice(Current.Value, ((int)Current.Value).ToString(CultureInfo.InvariantCulture) + ": not a valid choice (kept as the file has it)");
                Choices.Add(Selected);
            }
            SearchBy.Fill(Choices, Selected);
            SearchValue.Fill(Session.Value("searchValue") is int Value ? Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
            FillList(SearchValueDates);
            ConsecutiveDays.Fill(Session.Value("consecutiveDays") is int Nights ? Nights.ToString(CultureInfo.InvariantCulture) : string.Empty);
            foreach (BoolSettingField Day in Weekdays)
            {
                Day.Fill(Session.Value(Day.Key) is true);
            }
            FillMatching();
            foreach (FilterSelectorField Selector in Selectors)
            {
                FillSelector(Selector);
            }
        }

        // filterMatching (D51): Exact or Contains; a file without it offers "not set" (Contains, no edit), and a value that
        // is no choice is shown as such and kept until the user picks one
        private void FillMatching()
        {
            bool InFile = Session.InFile("filterMatching");
            object? Current = Session.Value("filterMatching");
            var Exact = new SettingChoice(FilterMatchMode.Exact, "Exact: a value must equal the campsite's value");
            var Contains = new SettingChoice(FilterMatchMode.Contains, "Contains: a value matches when it contains (or, for campsite types, is part of) the campsite's value");
            var Choices = new List<SettingChoice>();
            SettingChoice? NotSet = null;
            if (!InFile)
            {
                NotSet = new SettingChoice(MatchingNotSet, "Contains (not set in this file: a search file without filterMatching uses Contains)");
                Choices.Add(NotSet);
            }
            Choices.Add(Exact);
            Choices.Add(Contains);
            SettingChoice? Selected;
            // a value whose text is refused (a number, padding, "Exact, Contains") loads as a choice, but the file does not
            // say Exact or Contains: it is shown as such, and either choice writes over it
            bool Refused = Session.MustRewrite("filterMatching") && !Session.IsChanged("filterMatching");
            if (!Refused && Current is FilterMatchMode Mode && Enum.IsDefined(Mode))
            {
                Selected = Mode == FilterMatchMode.Exact ? Exact : Contains;
            }
            else if (Current == null && !InFile)
            {
                Selected = NotSet;
            }
            else
            {
                Selected = new SettingChoice(MatchingInvalid, "Not Exact or Contains in the file (kept as it is until you choose)");
                Choices.Add(Selected);
            }
            Matching.Fill(Choices, Selected);
        }

        protected override void Apply(SettingField Field)
        {
            if (Session.State != ConfigEditState.Editable || Session.Conflict != null)
            {
                return;
            }
            switch (Field)
            {
                case BoolSettingField Check:
                    Session.Set(Check.Key, Check.IsChecked);
                    break;
                case ChoiceSettingField Choice when Choice.Key == "filterMatching" && Choice.Selected != null:
                    if (Choice.Selected.Value is FilterMatchMode Mode)
                    {
                        Session.Set(Choice.Key, Mode);
                    }
                    else
                    {
                        // "not set" or the file's own value: back to what the file has, no edit
                        Session.Revert(Choice.Key);
                    }
                    break;
                case ChoiceSettingField Choice when Choice.Selected != null:
                    Session.Set(Choice.Key, (SearchTypes)Choice.Selected.Value);
                    break;
                case CampgroundSettingField Picker:
                    if (Picker.Id == null)
                    {
                        Session.Revert(Picker.Key);
                    }
                    else
                    {
                        Session.Set(Picker.Key, Picker.Id);
                    }
                    break;
                case TextSettingField Text when Text.Key == "displayName":
                    // an empty box is the file's empty or missing name; anything else is the text as entered
                    if (Text.Text == (Session.OriginalValue(Text.Key) as string ?? string.Empty))
                    {
                        Session.Revert(Text.Key);
                    }
                    else
                    {
                        Session.Set(Text.Key, Text.Text);
                    }
                    break;
                case TextSettingField Text when Text.Key == "totalHumans":
                    if (Text.Text.Trim().Length == 0)
                    {
                        // an empty box is no number of people: the file's missing or empty value, or null if it had one
                        Text.InputError = null;
                        Session.Set(Text.Key, null);
                    }
                    else
                    {
                        SetWholeNumber(Text, "Enter a number of people, such as 2.");
                    }
                    break;
                case TextSettingField Text:
                    SetWholeNumber(Text, Text.Key == "consecutiveDays" ? "Enter a whole number of nights, such as 1." : "Enter a whole number, such as 7.");
                    break;
            }
        }

        private void SetWholeNumber(TextSettingField Text, string Error)
        {
            if (int.TryParse(Text.Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int Number))
            {
                Text.InputError = null;
                Session.Set(Text.Key, Number);
            }
            else
            {
                Text.InputError = Error;
                Session.Revert(Text.Key);
            }
        }

        private protected override bool ShownWithFields(ConfigEditNotice Notice) => Notice.Code == nameof(ConfigCode.NoWeekdays);

        protected override void OnRefreshed()
        {
            ConfigEditNotice? Weekday = Session.Notices.FirstOrDefault(N => N.Code == nameof(ConfigCode.NoWeekdays));
            _WeekdaysMessage = Weekday?.Message;
            _WeekdaysSeverity = Weekday?.Severity.ToString() ?? string.Empty;
            this.RaisePropertyChanged(nameof(WeekdaysMessage));
            this.RaisePropertyChanged(nameof(HasWeekdaysMessage));
            this.RaisePropertyChanged(nameof(WeekdaysMessageText));
            this.RaisePropertyChanged(nameof(WeekdaysMessageIsError));
            Campground.RefreshCommands();
            FilterMatchMode Mode = EffectiveMatching;
            foreach (FilterSelectorField Selector in Selectors)
            {
                Selector.SetMode(Mode);
                Selector.RefreshCommands();
            }
            ShowConflict(IncludeEquipment, ExcludeEquipment);
            ShowConflict(IncludeSites, ExcludeSites);
            ShowConflict(IncludeAttributes, ExcludeAttributes);
            ShowConflict(IncludeCampsiteType, ExcludeCampsiteType);
            if (_IsActive)
            {
                EnsureChoices();
            }
            RetryChoicesCommand.Refresh();
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(IsEnabledSearch));
            this.RaisePropertyChanged(nameof(ListDetails));
            this.RaisePropertyChanged(nameof(ListStatus));
            this.RaisePropertyChanged(nameof(ListStatusIsError));
            this.RaisePropertyChanged(nameof(ListRunNote));
            this.RaisePropertyChanged(nameof(ListAccessibleName));
        }

        // the same value in an include and its exclude list: allowed (the file may say so), but the campsite is left out
        private static void ShowConflict(FilterSelectorField Include, FilterSelectorField Exclude)
        {
            List<string> Both = Exclude.Shared(Include);
            Exclude.ConflictText = Both.Count == 0 ? null
                : "Warning: also in " + Include.Key + ": " + string.Join(", ", Both.Take(5).Select(FilterText.Quoted)) + (Both.Count > 5 ? ", …" : string.Empty)
                  + ". A campsite that has " + (Both.Count == 1 ? "it" : "one of them") + " is left out, because an exclude list wins over an include list.";
        }

        // ---- the campground's values for the selectors (D51) ----

        // the file is shown in the editor: its selectors get the campground's values (read once per campground and kept
        // while the database is unchanged); a file that is only listed reads nothing
        internal void Activate()
        {
            _IsActive = true;
            EnsureChoices();
        }

        internal void Deactivate() => _IsActive = false;

        // the selectors show the values of the campground the file has (as edited); a change of campground, or of the
        // database (a refresh while the window is open), reads them again on the worker thread
        internal void EnsureChoices()
        {
            if (_ChoiceSource == null)
            {
                return;
            }
            string? Id = Campground.Id;
            if (string.IsNullOrWhiteSpace(Id))
            {
                _ChoicesFor = null;
                _ChoicesRequest++;
                foreach (FilterSelectorField Selector in Selectors)
                {
                    Selector.SetNoCampground();
                }
                return;
            }
            // the same campground (shown, loading or failed until Retry) is not read again unless the database changed
            bool Shown = string.Equals(Id, _ChoicesFor, StringComparison.Ordinal);
            if (Shown && !_ChoiceSource.DatabaseChanged)
            {
                return;
            }
            _ChoicesFor = Id;
            int Request = ++_ChoicesRequest;
            if (_ChoiceSource.TryGet(Id, out CampgroundFilterChoices Kept))
            {
                ApplyChoices(Kept);
                return;
            }
            foreach (FilterSelectorField Selector in Selectors)
            {
                Selector.SetLoading();
            }
            this.RaisePropertyChanged(nameof(HasChoicesProblem));
            _ChoiceSource.Request(Id, (Choices, Problem) =>
            {
                // another campground (or a newer request) since: this answer is not for what is shown
                if (Request != _ChoicesRequest)
                {
                    return;
                }
                if (Choices != null)
                {
                    ApplyChoices(Choices);
                }
                else
                {
                    foreach (FilterSelectorField Selector in Selectors)
                    {
                        Selector.SetFailed(Problem ?? "Campertron couldn't read this campground's values.");
                    }
                }
                RetryChoicesCommand.Refresh();
                this.RaisePropertyChanged(nameof(HasChoicesProblem));
            });
        }

        private void ApplyChoices(CampgroundFilterChoices Choices)
        {
            foreach (FilterSelectorField Selector in Selectors)
            {
                Selector.SetLoaded(Choices.For(Selector.Key) ?? Array.Empty<FilterChoice>());
            }
            RetryChoicesCommand.Refresh();
            this.RaisePropertyChanged(nameof(HasChoicesProblem));
        }

        private void RetryChoices()
        {
            _ChoicesFor = null;
            EnsureChoices();
        }

        // after Save As: this file keeps what is on disk (the edits are in the new file)
        internal void DiscardAfterSaveAs()
        {
            Session.Reload();
            Fill();
            Refresh();
        }

        internal void SetOutput(OutputType? Output)
        {
            if (Session.SearchOutput != Output)
            {
                Session.SearchOutput = Output;
                Refresh();
            }
        }
    }

    // The GUI's words for a search's autoRun setting (V011-3, decision D63; business-rules-gui.md §18 and §20): "Enabled
    // (autoRun)" and "Not enabled", each explained where it is shown. What the setting does, as the console and the GUI
    // behave: the console's Search reads the search files each time and runs the enabled ones; email mode reads them once,
    // when it starts, and checks the enabled ones every minute; the Results tab selects the enabled ones by default and
    // runs one that is not enabled when it is selected; the GUI never searches because it starts. The YAML key stays
    // autoRun, and none of these words says that a search runs when Campertron starts.
    internal static class AutoRunWording
    {
        internal const string Enabled = "Enabled (autoRun)";
        internal const string NotEnabled = "Not enabled";
        // the search editor's check box (its access key is A: E is the dates' list)
        internal const string Label = "En_abled (autoRun)";
        internal const string Hint = "Enabled searches run each time you choose Search in the console and at every check of email mode, which reads "
            + "the search files when it starts (restart it after a change). The Results tab selects enabled searches by default and runs one that is "
            + "not enabled when you select it. Enabling a search doesn't make the GUI search by itself, and saving never runs a search.";
        // the list of search files, after its state (a screen reader's and the tooltip's whole entry)
        internal const string EnabledNote = "The console's Search and email mode run it, and the Results tab selects it by default.";
        internal const string NotEnabledNote = "The console's Search and email mode skip it; the Results tab runs it only when you select it.";
        internal const string HasErrorsNote = "It has errors, so it can't be searched until they are fixed.";
    }
}
