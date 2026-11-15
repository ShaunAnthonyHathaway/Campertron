using CampertronLibrary.function.Base;
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

        private string? _WeekdaysMessage;
        private string _WeekdaysSeverity = string.Empty;

        internal SearchFileViewModel(ConfigEditSession<CampertronConfig> Session, CampgroundCatalog Catalog) : base(Session)
        {
            DisplayName = new TextSettingField("displayName", "_Name (displayName)", "The search's name in the results.");
            AutoRun = new BoolSettingField("autoRun", "_Run this search (autoRun)", "Only searches with autoRun: true run when Campertron starts. Saving never runs a search.");
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
            IncludeEquipment = Filter("includeEquipment", "Include equipment (includeEquipment)", "Only campsites that allow one of these equipment types, such as Tent.", "equipment type");
            ExcludeEquipment = Filter("excludeEquipment", "Exclude equipment (excludeEquipment)", "Leave out campsites that allow one of these equipment types.", "equipment type");
            IncludeSites = Filter("includeSites", "Include campsites (includeSites)", "Only these campsites, by their exact name, such as A012.", "campsite");
            ExcludeSites = Filter("excludeSites", "Exclude campsites (excludeSites)", "Leave out these campsites, by their exact name.", "campsite");
            IncludeAttributes = Filter("includeAttributes", "Include attributes (includeAttributes)", "Only campsites with one of these attributes, written Name:Value, such as Shade:Yes.", "attribute");
            ExcludeAttributes = Filter("excludeAttributes", "Exclude attributes (excludeAttributes)", "Leave out campsites with one of these attributes, written Name:Value.", "attribute");
            IncludeCampsiteType = Filter("includeCampsiteType", "Include campsite types (includeCampsiteType)", "Only campsites of one of these types, such as STANDARD NONELECTRIC.", "campsite type");
            ExcludeCampsiteType = Filter("excludeCampsiteType", "Exclude campsite types (excludeCampsiteType)", "Leave out campsites of one of these types, such as GROUP.", "campsite type");
            Filters = new List<ListSettingField> { IncludeEquipment, ExcludeEquipment, IncludeSites, ExcludeSites, IncludeAttributes, ExcludeAttributes, IncludeCampsiteType, ExcludeCampsiteType };

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
            foreach (ListSettingField List in Filters)
            {
                Add(List);
            }
            Load();
        }

        private static ListSettingField Filter(string Key, string Label, string Hint, string Item) =>
            new ListSettingField(Key, Label, Hint, Item, "One value; it must match exactly as the campsite data writes it.");

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
        public ListSettingField IncludeEquipment { get; }
        public ListSettingField ExcludeEquipment { get; }
        public ListSettingField IncludeSites { get; }
        public ListSettingField ExcludeSites { get; }
        public ListSettingField IncludeAttributes { get; }
        public ListSettingField ExcludeAttributes { get; }
        public ListSettingField IncludeCampsiteType { get; }
        public ListSettingField ExcludeCampsiteType { get; }
        public List<ListSettingField> Filters { get; }

        // the validation's message about the days of the week (no day enabled), shown with the check boxes
        public string? WeekdaysMessage => _WeekdaysMessage;
        public bool HasWeekdaysMessage => _WeekdaysMessage != null;
        public string WeekdaysMessageText => _WeekdaysMessage == null ? string.Empty : _WeekdaysSeverity + ": " + _WeekdaysMessage;
        public bool WeekdaysMessageIsError => _WeekdaysSeverity == "Error";

        // ---- the file in the list of search files ----

        public bool IsEnabledSearch => Session.Value("autoRun") is true;
        public string ListDetails
        {
            get
            {
                if (Session.State == ConfigEditState.Unreadable || Session.State == ConfigEditState.Missing)
                {
                    return "Can't be read";
                }
                string Name = Session.Value("displayName") is string N && !string.IsNullOrWhiteSpace(N) ? N : "(no name)";
                string Run = Session.State != ConfigEditState.Editable && Session.OriginalValue("autoRun") == null ? "not read" : IsEnabledSearch ? "Enabled" : "Disabled";
                string Where = Campground.Id == null ? "no campground" : Session.Campground?.Label ?? "ID " + Campground.Id;
                return Name + " \u00B7 " + Run + " \u00B7 " + Where;
            }
        }
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
        public string ListAccessibleName => FileName + ", " + ListDetails + ", " + ListStatus;

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
            foreach (ListSettingField List in Filters)
            {
                FillList(List);
            }
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
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(IsEnabledSearch));
            this.RaisePropertyChanged(nameof(ListDetails));
            this.RaisePropertyChanged(nameof(ListStatus));
            this.RaisePropertyChanged(nameof(ListStatusIsError));
            this.RaisePropertyChanged(nameof(ListAccessibleName));
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
}
