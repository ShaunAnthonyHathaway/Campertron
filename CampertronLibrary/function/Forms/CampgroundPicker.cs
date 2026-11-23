using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // The campgrounds the search editor's picker offers (CM6, decision D44; TD-53, open question #41): every reservable,
    // enabled campground, read with one query (Read.CampgroundPickerEntries) the first time the Searches tab needs it and
    // kept for the window's life, looked up by exact FacilityID (ordinal, never parsed or trimmed). Reading it never
    // searches availability.
    public sealed class CampgroundCatalog
    {
        private readonly Func<List<CampgroundPickerEntry>> _Load;
        private List<CampgroundPickerEntry>? _Entries;
        private Dictionary<string, CampgroundPickerEntry>? _ById;

        internal CampgroundCatalog(Func<List<CampgroundPickerEntry>> Load)
        {
            _Load = Load;
        }

        // why the campgrounds can't be listed (fixed text), or null
        public string? LoadError { get; private set; }
        public bool IsLoaded => _Entries != null;

        public IReadOnlyList<CampgroundPickerEntry> Entries
        {
            get
            {
                Ensure();
                return _Entries!;
            }
        }

        // the campground with exactly this FacilityID, or null
        internal CampgroundPickerEntry? Find(string? FacilityID)
        {
            Ensure();
            return FacilityID != null && _ById!.TryGetValue(FacilityID, out CampgroundPickerEntry? Entry) ? Entry : null;
        }

        internal void Ensure()
        {
            if (_Entries != null)
            {
                return;
            }
            try
            {
                _Entries = _Load();
                LoadError = null;
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                _Entries = new List<CampgroundPickerEntry>();
                LoadError = "Campertron couldn't read its campground data, so campgrounds can't be listed or checked here.";
            }
            _ById = new Dictionary<string, CampgroundPickerEntry>(StringComparer.Ordinal);
            foreach (CampgroundPickerEntry Entry in _Entries)
            {
                _ById.TryAdd(Entry.FacilityID, Entry);
            }
        }
    }

    // A search file's campgroundID (CM6, decision D44): the campground chosen (its label, or why its ID is not one Campertron
    // knows), a filtered list of every reservable, enabled campground to choose from, and the exact entry of an ID. Its
    // identity is the FacilityID only: a label is never parsed, a name never looked up. Choosing writes nothing until the
    // file is saved, and nothing here searches.
    public sealed class CampgroundSettingField : SettingField
    {
        private readonly CampgroundCatalog _Catalog;
        private string? _Id;
        private string _FilterText = string.Empty;
        private List<CampgroundPickerEntry> _Matches = new List<CampgroundPickerEntry>();
        private CampgroundPickerEntry? _SelectedMatch;
        private string _TypedId = string.Empty;
        private string? _TypedIdMessage;
        private bool _TypedIdIsError;

        public CampgroundSettingField(string Key, string Label, string Hint, CampgroundCatalog Catalog) : base(Key, Label, Hint)
        {
            _Catalog = Catalog;
            UseSelectedCommand = new RelayCommand(UseSelected, () => IsEditable && SelectedMatch != null);
            UseTypedIdCommand = new RelayCommand(UseTypedId, () => IsEditable);
        }

        // the FacilityID as edited or as the file has it (exactly; null when missing)
        public string? Id => _Id;

        // the chosen campground as a sentence
        public string CurrentText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_Id))
                {
                    return "No campground is chosen.";
                }
                CampgroundPickerEntry? Entry = _Catalog.Find(_Id);
                if (Entry != null)
                {
                    return Entry.Label + (Entry.Park == null ? string.Empty : " \u2014 park " + Entry.Park);
                }
                return _Catalog.LoadError != null
                    ? "ID " + _Id + " (the campground data can't be read to name it)."
                    : "ID " + _Id + ": not a reservable, enabled campground in Campertron's campground data.";
            }
        }

        public string FilterText
        {
            get => _FilterText;
            set
            {
                string New = value ?? string.Empty;
                if (_FilterText != New)
                {
                    this.RaiseAndSetIfChanged(ref _FilterText, New);
                    Filter();
                }
            }
        }

        public List<CampgroundPickerEntry> Matches
        {
            get => _Matches;
            private set => this.RaiseAndSetIfChanged(ref _Matches, value);
        }

        public string MatchSummary => _Catalog.LoadError ?? (FilterText.Trim().Length == 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:N0} campgrounds. Type a name, city, state, park or an exact ID to narrow the list.", Matches.Count)
            : string.Format(CultureInfo.CurrentCulture, "{0:N0} of {1:N0} campgrounds match.", Matches.Count, _Catalog.Entries.Count));

        public CampgroundPickerEntry? SelectedMatch
        {
            get => _SelectedMatch;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedMatch, value);
                UseSelectedCommand.Refresh();
            }
        }

        public RelayCommand UseSelectedCommand { get; }

        // ---- a UI without a virtualized list (the web UI, v0.3.0's PR 6, D96) ----

        // the first matches, in the list's order: what such a UI lists (every campground would be thousands of entries)
        public IReadOnlyList<CampgroundPickerEntry> FirstMatches(int Limit) => Matches.Take(Math.Max(0, Limit)).ToList();

        // what it says when it lists only the first matches, or null when it lists them all
        public string? MoreMatchesText(int Limit) => Matches.Count <= Limit ? null
            : string.Format(CultureInfo.CurrentCulture, "The first {0:N0} are listed. Type a name, city, state, park or an exact ID to narrow the list.", Limit);

        // the match chosen in the list it showed, by its exact FacilityID (a UI that sends the user's choice with its next
        // action): an ID the list does not have now chooses nothing, and no ID leaves the choice as it is
        public void SelectMatch(string? FacilityID)
        {
            if (FacilityID != null && Matches.FirstOrDefault(Match => string.Equals(Match.FacilityID, FacilityID, StringComparison.Ordinal)) is CampgroundPickerEntry Found)
            {
                SelectedMatch = Found;
            }
        }

        public string TypedId
        {
            get => _TypedId;
            set
            {
                string New = value ?? string.Empty;
                if (_TypedId != New)
                {
                    this.RaiseAndSetIfChanged(ref _TypedId, New);
                    SetTypedIdMessage(null, false);
                }
            }
        }
        public RelayCommand UseTypedIdCommand { get; }
        public string? TypedIdMessage => _TypedIdMessage;
        public bool HasTypedIdMessage => _TypedIdMessage != null;
        public bool TypedIdIsError => _TypedIdIsError;
        public string TypedIdHelpText => _TypedIdMessage != null ? (_TypedIdIsError ? "Error: " : string.Empty) + _TypedIdMessage : TypedIdHint;
        // what the box takes (a page shows the message about the last ID apart)
        public string TypedIdHint => "A campground ID exactly as Recreation.gov writes it, such as 232445.";

        // the file's value (after opening, a reload, a revert or a save); no user change
        internal void Fill(string? Id)
        {
            _Id = Id;
            this.RaisePropertyChanged(nameof(Id));
            this.RaisePropertyChanged(nameof(CurrentText));
            if (_Catalog.IsLoaded && Matches.Count == 0 && _Catalog.Entries.Count > 0)
            {
                Filter();
            }
        }

        // the list for the filter (the catalog is read the first time)
        internal void Filter()
        {
            Matches = CampgroundPickerEntry.Filter(_Catalog.Entries, FilterText);
            this.RaisePropertyChanged(nameof(MatchSummary));
            if (SelectedMatch != null && !Matches.Contains(SelectedMatch))
            {
                SelectedMatch = null;
            }
        }

        internal void RefreshCommands()
        {
            UseSelectedCommand.Refresh();
            UseTypedIdCommand.Refresh();
        }

        public void UseSelected()
        {
            if (SelectedMatch != null && IsEditable)
            {
                Choose(SelectedMatch.FacilityID);
                SetTypedIdMessage(null, false);
            }
        }

        // the typed ID is used only if a reservable, enabled campground has exactly this ID (no trimming, no number
        // parsing, no partial match); the message never repeats what was typed
        public void UseTypedId()
        {
            if (!IsEditable)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(TypedId))
            {
                SetTypedIdMessage("Enter a campground ID.", true);
                return;
            }
            if (_Catalog.LoadError != null)
            {
                SetTypedIdMessage("The campground data can't be read, so the ID can't be checked.", true);
                return;
            }
            CampgroundPickerEntry? Entry = _Catalog.Find(TypedId);
            if (Entry == null)
            {
                SetTypedIdMessage((TypedId != TypedId.Trim() ? "Remove the spaces before or after the ID. " : string.Empty)
                    + "No reservable, enabled campground in Campertron's campground data has exactly this ID.", true);
                return;
            }
            Choose(Entry.FacilityID);
            SetTypedIdMessage("Chosen: " + Entry.Label + ".", false);
        }

        private void Choose(string FacilityID)
        {
            _Id = FacilityID;
            this.RaisePropertyChanged(nameof(Id));
            this.RaisePropertyChanged(nameof(CurrentText));
            OnChanged();
        }

        private void SetTypedIdMessage(string? Message, bool IsError)
        {
            _TypedIdMessage = Message;
            _TypedIdIsError = IsError;
            this.RaisePropertyChanged(nameof(TypedIdMessage));
            this.RaisePropertyChanged(nameof(HasTypedIdMessage));
            this.RaisePropertyChanged(nameof(TypedIdIsError));
            this.RaisePropertyChanged(nameof(TypedIdHelpText));
        }
    }
}
