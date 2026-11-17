using CampertronLibrary.function.RecDotOrg.search;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Campertron.ViewModels
{
    // Where a selector's campground values stand
    public enum FilterChoicesStatus
    {
        // no campground is chosen: nothing to offer from the data
        NoCampground,
        Loading,
        Loaded,
        Failed
    }

    // One include or exclude list of a search file as a database-backed selector (decision D51): the values of the chosen
    // campground's own campsites, checked when the list holds them exactly, case variants grouped, and every other value of
    // the list (custom, from another campground, stale, an empty item) kept as a separate entry until the user removes it.
    //
    // The list itself stays the YAML list: the rows are the file's items with the item each was (as ListSettingField's), so
    // saving goes through the same editing session item by item, with comments, order, quoting and duplicates kept.
    // Checking a value adds one item at the end; unchecking removes every item that is exactly that value; a custom value is
    // added as typed (never trimmed or case-changed); nothing else changes an item. The campground's values never change
    // the list by themselves: a value the campground does not have is shown as such, never dropped.
    public sealed class FilterSelectorField : SettingField
    {
        private sealed class Row
        {
            internal Row(int? OriginalIndex, string? Value)
            {
                this.OriginalIndex = OriginalIndex;
                this.Value = Value;
            }
            internal int? OriginalIndex { get; }
            internal string? Value { get; }
        }

        private readonly List<Row> _Rows = new List<Row>();
        private IReadOnlyList<FilterChoice> _Choices = Array.Empty<FilterChoice>();
        private FilterChoicesStatus _Status = FilterChoicesStatus.NoCampground;
        private string? _Problem;
        private FilterMatchMode _Mode = FilterMatchMode.Contains;
        private string _FindText = string.Empty;
        private string _CustomText = string.Empty;
        private string? _CustomMessage;
        private bool _CustomIsError;
        private string? _ConflictText;
        private readonly Dictionary<int, (string Severity, string Message)> _RowMessages = new Dictionary<int, (string, string)>();

        internal FilterSelectorField(string Key, string Label, string Hint, FilterField Field, bool IsInclude, string ItemName, string ItemsName)
            : base(Key, Label, Hint)
        {
            this.Field = Field;
            this.IsInclude = IsInclude;
            this.ItemName = ItemName;
            this.ItemsName = ItemsName;
            AddCustomCommand = new RelayCommand(AddCustom, () => IsEditable);
            SelectShownCommand = new RelayCommand(SelectShown, () => IsEditable && Options.Any(O => O.IsChoice && O.IsChecked != true));
            ClearShownCommand = new RelayCommand(ClearShown, () => IsEditable && Options.Any(O => O.IsChoice && O.IsChecked == true));
        }

        internal FilterField Field { get; }
        public bool IsInclude { get; }
        // what one value is called ("equipment type") and several ("equipment types")
        public string ItemName { get; }
        public string ItemsName { get; }

        // the campground's values as shown (filtered), with group headers for values that differ only by case
        public ObservableCollection<FilterOptionRow> Options { get; } = new ObservableCollection<FilterOptionRow>();
        // the list's values that are not among the campground's values (and empty items), in the list's order
        public ObservableCollection<FilterCustomRow> OtherValues { get; } = new ObservableCollection<FilterCustomRow>();
        public bool HasOtherValues => OtherValues.Count > 0;

        public FilterChoicesStatus Status => _Status;
        public bool IsLoading => _Status == FilterChoicesStatus.Loading;
        public bool HasChoices => _Status == FilterChoicesStatus.Loaded && _Choices.Count > 0;
        // a find box, Select all shown and Clear shown for lists long enough to need them
        public bool CanFind => HasChoices && _Choices.Count > LongList;
        public bool ShowBulkActions => CanFind;
        // a short list is shown whole; a long one scrolls in a bounded, virtualized list
        public double ListMaxHeight => Options.Count <= ShortListRows ? double.PositiveInfinity : 320;
        private const int LongList = 12;
        private const int ShortListRows = 16;

        // the state of the campground's values in words (a polite live region)
        public string StatusText
        {
            get
            {
                switch (_Status)
                {
                    case FilterChoicesStatus.NoCampground:
                        return "Choose a campground to pick from its " + ItemsName + ". Values typed below are kept either way.";
                    case FilterChoicesStatus.Loading:
                        return "Reading this campground's " + ItemsName + "…";
                    case FilterChoicesStatus.Failed:
                        return _Problem ?? "Campertron couldn't read this campground's values.";
                }
                if (_Choices.Count == 0)
                {
                    return "This campground has no " + ItemsName + " in Campertron's campground data.";
                }
                int Checked = _Choices.Count(C => Contains(C.Value));
                string Shown = _FindText.Trim().Length == 0 ? string.Empty
                    : string.Format(CultureInfo.CurrentCulture, " {0:N0} shown.", Options.Count(O => O.IsChoice));
                return string.Format(CultureInfo.CurrentCulture, "{0:N0} {1} at this campground, {2:N0} checked.", _Choices.Count, _Choices.Count == 1 ? ItemName : ItemsName, Checked) + Shown;
            }
        }

        // narrows the shown values to those that contain each word (ignoring case)
        public string FindText
        {
            get => _FindText;
            set
            {
                string New = value ?? string.Empty;
                if (_FindText != New)
                {
                    this.RaiseAndSetIfChanged(ref _FindText, New);
                    Rebuild();
                }
            }
        }

        public string CustomText
        {
            get => _CustomText;
            set
            {
                string New = value ?? string.Empty;
                if (_CustomText != New)
                {
                    this.RaiseAndSetIfChanged(ref _CustomText, New);
                    SetCustomMessage(null, false);
                }
            }
        }
        public RelayCommand AddCustomCommand { get; }
        public string? CustomMessage => _CustomMessage;
        public bool HasCustomMessage => _CustomMessage != null;
        public bool CustomIsError => _CustomIsError;
        public string CustomHelpText => (_CustomMessage != null ? (_CustomIsError ? "Error: " : string.Empty) + _CustomMessage + " " : string.Empty)
            + "A value exactly as you type it, kept even when the campground does not have it. " + ModeSentence;

        public RelayCommand SelectShownCommand { get; }
        public RelayCommand ClearShownCommand { get; }

        // the same value in the include and the exclude list (exclude wins); set by the file's view model
        public string? ConflictText
        {
            get => _ConflictText;
            internal set
            {
                this.RaiseAndSetIfChanged(ref _ConflictText, value);
                this.RaisePropertyChanged(nameof(HasConflict));
            }
        }
        public bool HasConflict => _ConflictText != null;

        // accessible names of the selector's own controls (one selector per list, so each names its list)
        public string ListName => AccessibleName + ": " + ItemsName + " of the campground";
        public string FindName => "Find " + ItemsName + " (" + Key + ")";
        public string CustomName => "Another value for " + Key;
        public string AddCustomName => "Add the value to " + Key;
        public string OtherValuesHeading => "Other values in " + Key;

        public FilterMatchMode Mode => _Mode;
        private string ModeSentence => _Mode == FilterMatchMode.Exact
            ? "With Exact matching a value must equal the campsite's value."
            : Field == FilterField.Sites
                ? "Campsite names always match exactly."
                : Field == FilterField.CampsiteType
                    ? "With Contains matching a campsite type matches when it contains the value."
                    : "With Contains matching a value matches when it contains the campsite's " + (Field == FilterField.Equipment ? "equipment name." : "Name:Value.");

        // set by the file's view model; called after the user changed the list
        internal Action? RowsChanged;

        // ---- the list ----

        // the session's items (after opening, a reload, a revert or a save); no user change
        internal void FillRows(IEnumerable<ConfigListItem> Items)
        {
            _Rows.Clear();
            foreach (ConfigListItem Item in Items)
            {
                _Rows.Add(new Row(Item.OriginalIndex, Item.Value));
            }
            Rebuild();
        }

        // the rows as the session takes them
        internal IEnumerable<(int? OriginalIndex, string? Value)> Values() => _Rows.Select(R => (R.OriginalIndex, R.Value));

        // the list holds exactly this value
        internal bool Contains(string Value) => _Rows.Any(R => string.Equals(R.Value, Value, StringComparison.Ordinal));

        // the values in both this list and another (exactly), in this list's order
        internal List<string> Shared(FilterSelectorField Other) =>
            _Rows.Where(R => R.Value != null && Other.Contains(R.Value)).Select(R => R.Value!).Distinct(StringComparer.Ordinal).ToList();

        // ---- the campground's values ----

        internal void SetNoCampground() => SetChoices(FilterChoicesStatus.NoCampground, null, null);
        internal void SetLoading() => SetChoices(FilterChoicesStatus.Loading, null, null);
        internal void SetFailed(string Problem) => SetChoices(FilterChoicesStatus.Failed, null, Problem);
        internal void SetLoaded(IReadOnlyList<FilterChoice> Choices) => SetChoices(FilterChoicesStatus.Loaded, Choices, null);

        private void SetChoices(FilterChoicesStatus Status, IReadOnlyList<FilterChoice>? Choices, string? Problem)
        {
            _Status = Status;
            _Choices = Choices ?? Array.Empty<FilterChoice>();
            _Problem = Problem;
            this.RaisePropertyChanged(nameof(Status));
            this.RaisePropertyChanged(nameof(IsLoading));
            this.RaisePropertyChanged(nameof(HasChoices));
            this.RaisePropertyChanged(nameof(CanFind));
            this.RaisePropertyChanged(nameof(ShowBulkActions));
            Rebuild();
        }

        internal void SetMode(FilterMatchMode Mode)
        {
            if (_Mode != Mode)
            {
                _Mode = Mode;
                this.RaisePropertyChanged(nameof(Mode));
                this.RaisePropertyChanged(nameof(CustomHelpText));
                Rebuild();
            }
        }

        // the validation's messages about items of the list (by their position in the list as edited)
        internal void ShowRowMessages(IEnumerable<(int Index, string Severity, string Message)> Messages)
        {
            _RowMessages.Clear();
            foreach (var Group in Messages.GroupBy(M => M.Index))
            {
                var Ordered = Group.OrderBy(M => M.Severity == "Error" ? 0 : M.Severity == "Warning" ? 1 : 2).ToList();
                _RowMessages[Group.Key] = (Ordered[0].Severity, string.Join(" ", Ordered.Select(M => M.Message).Distinct()));
            }
            Rebuild();
            AddCustomCommand.Refresh();
        }

        // ---- the user's changes ----

        // checks or unchecks a value of the campground: checking adds it once at the end, unchecking removes every item that
        // is exactly it; a group checks every spelling it lacks, or unchecks them all
        public void Toggle(FilterOptionRow Option)
        {
            if (!IsEditable)
            {
                return;
            }
            SetChecked(Option, Option.IsChecked != true);
        }

        internal void SetChecked(FilterOptionRow Option, bool Checked)
        {
            if (!IsEditable)
            {
                return;
            }
            bool Changed = false;
            foreach (string Value in Option.Values)
            {
                if (Checked && !Contains(Value))
                {
                    _Rows.Add(new Row(null, Value));
                    Changed = true;
                }
                else if (!Checked && Contains(Value))
                {
                    _Rows.RemoveAll(R => string.Equals(R.Value, Value, StringComparison.Ordinal));
                    Changed = true;
                }
            }
            if (Changed)
            {
                Updated();
            }
            else
            {
                Rebuild();
            }
        }

        // the shown values all checked (or unchecked); the values the find box hides and the other values stay as they are
        public void SelectShown() => SetShown(true);
        public void ClearShown() => SetShown(false);

        private void SetShown(bool Checked)
        {
            if (!IsEditable)
            {
                return;
            }
            bool Changed = false;
            foreach (FilterOptionRow Option in Options.Where(O => O.IsChoice).ToList())
            {
                string Value = Option.Values[0];
                if (Checked && !Contains(Value))
                {
                    _Rows.Add(new Row(null, Value));
                    Changed = true;
                }
                else if (!Checked && Contains(Value))
                {
                    _Rows.RemoveAll(R => string.Equals(R.Value, Value, StringComparison.Ordinal));
                    Changed = true;
                }
            }
            if (Changed)
            {
                Updated();
            }
        }

        // adds the typed value exactly as typed; a value the list holds is not added twice, and a value of the campground is
        // checked in its list
        public void AddCustom()
        {
            if (!IsEditable)
            {
                return;
            }
            string Value = _CustomText;
            if (Value.Length == 0)
            {
                SetCustomMessage("Type a value first.", true);
                return;
            }
            if (Contains(Value))
            {
                SetCustomMessage("The list already holds exactly this value.", true);
                return;
            }
            _Rows.Add(new Row(null, Value));
            bool OfCampground = _Choices.Any(C => string.Equals(C.Value, Value, StringComparison.Ordinal));
            _CustomText = string.Empty;
            this.RaisePropertyChanged(nameof(CustomText));
            SetCustomMessage(OfCampground ? "Added; it is one of this campground's values and is checked above." : "Added as typed.", false);
            Updated();
        }

        // removes one item of the list (a value the campground does not have, or an empty item)
        internal void Remove(FilterCustomRow Custom)
        {
            if (!IsEditable)
            {
                return;
            }
            int At = _Rows.FindIndex(R => ReferenceEquals(R, Custom.Source));
            if (At >= 0)
            {
                _Rows.RemoveAt(At);
                Updated();
            }
        }

        private void Updated()
        {
            Rebuild();
            if (!Loading)
            {
                RowsChanged?.Invoke();
            }
        }

        private void SetCustomMessage(string? Message, bool IsError)
        {
            _CustomMessage = Message;
            _CustomIsError = IsError;
            this.RaisePropertyChanged(nameof(CustomMessage));
            this.RaisePropertyChanged(nameof(HasCustomMessage));
            this.RaisePropertyChanged(nameof(CustomIsError));
            this.RaisePropertyChanged(nameof(CustomHelpText));
        }

        internal void RefreshCommands()
        {
            AddCustomCommand.Refresh();
            SelectShownCommand.Refresh();
            ClearShownCommand.Refresh();
            foreach (FilterOptionRow Option in Options)
            {
                Option.RaiseEnabled();
            }
            foreach (FilterCustomRow Custom in OtherValues)
            {
                Custom.RemoveCommand.Refresh();
            }
        }

        // ---- what is shown ----

        private void Rebuild()
        {
            var Words = _FindText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            bool Shown(FilterChoice C) => Words.All(W => FilterText.Display(C.Value).Contains(W, StringComparison.OrdinalIgnoreCase) || C.Value.Contains(W, StringComparison.OrdinalIgnoreCase));

            var NewOptions = new List<FilterOptionRow>();
            if (_Status == FilterChoicesStatus.Loaded)
            {
                // the choices come ordered with case variants next to each other (FilterChoice.ListOrder)
                foreach (var Group in _Choices.GroupBy(C => C.Value, StringComparer.OrdinalIgnoreCase))
                {
                    List<FilterChoice> Spellings = Group.ToList();
                    if (!Spellings.Any(Shown))
                    {
                        continue;
                    }
                    if (Spellings.Count > 1)
                    {
                        NewOptions.Add(FilterOptionRow.ForGroup(this, Spellings, Spellings.Select(S => Contains(S.Value)).ToList()));
                        foreach (FilterChoice Spelling in Spellings)
                        {
                            NewOptions.Add(FilterOptionRow.ForChoice(this, Spelling, Contains(Spelling.Value), IsVariant: true, AlsoMatches(Spelling)));
                        }
                    }
                    else
                    {
                        NewOptions.Add(FilterOptionRow.ForChoice(this, Spellings[0], Contains(Spellings[0].Value), IsVariant: false, AlsoMatches(Spellings[0])));
                    }
                }
            }
            Replace(Options, NewOptions, (A, B) => A.SameAs(B));
            this.RaisePropertyChanged(nameof(ListMaxHeight));

            var NewOthers = new List<FilterCustomRow>();
            for (int i = 0; i < _Rows.Count; i++)
            {
                Row R = _Rows[i];
                bool OfCampground = R.Value != null && _Status == FilterChoicesStatus.Loaded && _Choices.Any(C => string.Equals(C.Value, R.Value, StringComparison.Ordinal));
                if (OfCampground)
                {
                    continue;
                }
                _RowMessages.TryGetValue(i, out var Message);
                NewOthers.Add(new FilterCustomRow(this, R, R.Value, i + 1, Describe(R.Value), Message.Message, Message.Severity));
            }
            OtherValues.Clear();
            foreach (FilterCustomRow Other in NewOthers)
            {
                OtherValues.Add(Other);
            }
            this.RaisePropertyChanged(nameof(HasOtherValues));
            this.RaisePropertyChanged(nameof(StatusText));
            RefreshCommands();
        }

        private static void Replace(ObservableCollection<FilterOptionRow> Target, List<FilterOptionRow> Source, Func<FilterOptionRow, FilterOptionRow, bool> Same)
        {
            // the same rows in the same order keep their objects (the list keeps its focus and scroll position), so only
            // their checks are updated
            if (Target.Count == Source.Count && Target.Zip(Source).All(P => Same(P.First, P.Second)))
            {
                for (int i = 0; i < Target.Count; i++)
                {
                    Target[i].UpdateFrom(Source[i]);
                }
                return;
            }
            Target.Clear();
            foreach (FilterOptionRow Option in Source)
            {
                Target.Add(Option);
            }
        }

        // the campground's other values a checked value also matches with Contains matching (it is not shown with Exact)
        private List<string> AlsoMatches(FilterChoice Choice)
        {
            if (_Mode != FilterMatchMode.Contains || Field == FilterField.Sites)
            {
                return new List<string>();
            }
            return _Choices.Where(C => !string.Equals(C.Value, Choice.Value, StringComparison.Ordinal)
                                       && SearchFilters.Matches(Field, FilterMatchMode.Contains, Choice.Value, C.Value))
                           .Select(C => C.Value).ToList();
        }

        // what a value the campground does not have does here
        private string Describe(string? Value)
        {
            if (Value == null)
            {
                return "An empty item in the file. It is kept until you remove it.";
            }
            if (_Status != FilterChoicesStatus.Loaded)
            {
                return "Kept as written.";
            }
            List<string> Matched = _Choices.Where(C => SearchFilters.Matches(Field, _Mode, Value, C.Value)).Select(C => C.Value).ToList();
            FilterChoice? CaseOnly = _Choices.FirstOrDefault(C => string.Equals(C.Value, Value, StringComparison.OrdinalIgnoreCase));
            string Text;
            if (Matched.Count == 0)
            {
                Text = "Not among this campground's " + ItemsName + "; it matches none of them" + (_Mode == FilterMatchMode.Exact ? " with Exact matching." : " with Contains matching.");
            }
            else
            {
                Text = string.Format(CultureInfo.CurrentCulture, "Not one of this campground's {0}; with Contains matching it matches {1}: {2}.",
                    ItemsName, Matched.Count == 1 ? "one of them" : Matched.Count.ToString(CultureInfo.CurrentCulture) + " of them",
                    string.Join(", ", Matched.Take(5).Select(FilterText.Quoted)) + (Matched.Count > 5 ? ", …" : string.Empty));
            }
            if (CaseOnly != null)
            {
                Text += " It differs only in upper and lower case from " + FilterText.Quoted(CaseOnly.Value) + ", which is a different value.";
            }
            return Text;
        }
    }

    // a value of the campground in a selector, or a group of its spellings that differ only by case
    public sealed class FilterOptionRow : ViewModelBase
    {
        private readonly FilterSelectorField _Field;
        private bool? _IsChecked;
        private string _AlsoText = string.Empty;

        private FilterOptionRow(FilterSelectorField Field, bool IsChoice, IReadOnlyList<string> Values, string Label, int Campsites, bool? IsChecked, bool IsVariant, string AlsoText)
        {
            _Field = Field;
            this.IsChoice = IsChoice;
            this.Values = Values;
            this.Label = Label;
            this.Campsites = Campsites;
            _IsChecked = IsChecked;
            this.IsVariant = IsVariant;
            _AlsoText = AlsoText;
        }

        internal static FilterOptionRow ForChoice(FilterSelectorField Field, FilterChoice Choice, bool Checked, bool IsVariant, List<string> Also) =>
            new FilterOptionRow(Field, true, new[] { Choice.Value }, FilterText.Display(Choice.Value), Choice.Campsites, Checked, IsVariant,
                Also.Count == 0 ? string.Empty : "With Contains matching it also matches " + string.Join(", ", Also.Take(5).Select(FilterText.Quoted)) + (Also.Count > 5 ? ", …" : string.Empty) + ".");

        internal static FilterOptionRow ForGroup(FilterSelectorField Field, List<FilterChoice> Spellings, List<bool> Checked) =>
            new FilterOptionRow(Field, false, Spellings.Select(S => S.Value).ToList(),
                FilterText.Display(Spellings[0].Value) + " (" + Spellings.Count.ToString(CultureInfo.CurrentCulture) + " spellings that differ only by case)",
                Spellings.Sum(S => S.Campsites), Checked.All(C => C) ? true : Checked.Any(C => C) ? null : false, false,
                "Checking it adds every spelling; each spelling is its own value in the search.");

        // a value of the campground (false: the header of a group of spellings)
        public bool IsChoice { get; }
        public bool IsGroup => !IsChoice;
        // a spelling shown under its group
        public bool IsVariant { get; }
        // the exact values (one for a value, every spelling for a group)
        public IReadOnlyList<string> Values { get; }
        public string Label { get; }
        public int Campsites { get; }
        public string CampsitesText => Campsites == 1 ? "1 campsite" : Campsites.ToString("N0", CultureInfo.CurrentCulture) + " campsites";
        public string AlsoText => _AlsoText;
        public bool HasAlsoText => _AlsoText.Length > 0;
        public bool IsEditable => _Field.IsEditable;

        // a group is partly checked (null) when only some of its spellings are in the list
        public bool? IsChecked
        {
            get => _IsChecked;
            set
            {
                if (_IsChecked != value)
                {
                    _Field.SetChecked(this, value == true);
                }
            }
        }

        // what a screen reader reads for the row: the value, how many campsites, and whether it is selected
        public string AccessibleName => (IsVariant ? "Spelling " : string.Empty) + Label + ", " + CampsitesText + ", "
            + (_IsChecked == true ? "checked" : _IsChecked == null ? "partly checked" : "not checked")
            + (HasAlsoText ? ". " + _AlsoText : string.Empty);

        internal bool SameAs(FilterOptionRow Other) => IsChoice == Other.IsChoice && IsVariant == Other.IsVariant && Values.SequenceEqual(Other.Values, StringComparer.Ordinal);

        internal void UpdateFrom(FilterOptionRow Other)
        {
            this.RaiseAndSetIfChanged(ref _IsChecked, Other._IsChecked, nameof(IsChecked));
            this.RaiseAndSetIfChanged(ref _AlsoText, Other._AlsoText, nameof(AlsoText));
            this.RaisePropertyChanged(nameof(HasAlsoText));
            this.RaisePropertyChanged(nameof(AccessibleName));
        }

        internal void RaiseEnabled() => this.RaisePropertyChanged(nameof(IsEditable));
    }

    // a value of the list that the campground does not have (or an empty item), kept until the user removes it
    public sealed class FilterCustomRow : ViewModelBase
    {
        internal FilterCustomRow(FilterSelectorField Field, object Source, string? Value, int Position, string Description, string? Message, string? Severity)
        {
            this.Source = Source;
            this.Value = Value;
            this.Position = Position;
            this.Description = Description;
            this.Message = Message;
            this.Severity = Severity ?? string.Empty;
            RemoveCommand = new RelayCommand(() => Field.Remove(this), () => Field.IsEditable);
            Label = Value == null ? "(empty item)" : FilterText.Display(Value);
            RemoveName = "Remove " + Label + " from " + Field.AccessibleName;
        }
        internal object Source { get; }
        public string? Value { get; }
        // its position in the list (1-based)
        public int Position { get; }
        public string Label { get; }
        public string Description { get; }
        public string? Message { get; }
        public string Severity { get; }
        public bool HasMessage => Message != null;
        public bool IsError => Severity == "Error";
        public string MessageText => Message == null ? string.Empty : Severity + ": " + Message;
        public string AccessibleName => "Item " + Position.ToString(CultureInfo.CurrentCulture) + ", " + Label + ". " + Description + (Message != null ? " " + MessageText : string.Empty);
        public string RemoveName { get; }
        public RelayCommand RemoveCommand { get; }
    }

    // how a stored value is shown: as it is, with blank and space-padded values made visible (never changed)
    internal static class FilterText
    {
        internal static string Display(string Value)
        {
            if (Value.Length == 0)
            {
                return "(blank)";
            }
            if (Value.Trim().Length == 0)
            {
                return Value.Length == 1 ? "(one space)" : "(" + Value.Length.ToString(CultureInfo.CurrentCulture) + " spaces)";
            }
            if (Value != Value.Trim())
            {
                return "“" + Value + "” (with spaces at its start or end)";
            }
            return Value;
        }

        internal static string Quoted(string Value) => Value.Length == 0 || Value != Value.Trim() ? Display(Value) : "“" + Value + "”";
    }
}
