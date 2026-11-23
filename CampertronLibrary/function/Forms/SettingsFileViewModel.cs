using CampertronLibrary.function.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // One settings file in the Settings tab (CM5, decision D43): what SettingsFileView shows for General.yaml and
    // Email.yaml alike: the file's state (missing, unreadable, read-only), an external change or conflict and how to
    // resolve it, the problems that are not about one field, the preview of the save, and Save, Revert and Reload.
    public abstract class SettingsFileViewModel : FormObject
    {
        private string? _StateText;
        private bool _IsMissing;
        private bool _IsEditable;
        private string? _ConflictText;
        private bool _CanApplyMine;
        private string _ApplyMineLabel = "Apply my changes to the new version";
        private bool _CanRecreate;
        private string? _StatusText;
        private bool _StatusIsError;
        private List<NoticeItem> _FileNotices = new List<NoticeItem>();
        private List<PreviewLineItem> _PreviewLines = new List<PreviewLineItem>();
        private string _PreviewSummary = string.Empty;
        private bool _IsDirty;
        private bool _CanSave;

        protected SettingsFileViewModel(string FileName, string FilePath)
        {
            this.FileName = FileName;
            this.FilePath = FilePath;
        }

        public abstract string Title { get; }
        public string FileName { get; }
        public string FilePath { get; }
        public string CreateLabel => "Create " + FileName;
        public string PreviewHeading => "Changes to " + FileName;
        // the note under the preview
        public virtual string PreviewNote => "Lines starting with - are replaced by the lines starting with +. Nothing else in the file changes. The password is never shown.";

        // why the file can't be edited (missing, unreadable, read-only), or null
        public string? StateText
        {
            get => _StateText;
            protected set
            {
                this.RaiseAndSetIfChanged(ref _StateText, value);
                this.RaisePropertyChanged(nameof(HasStateText));
            }
        }
        public bool HasStateText => StateText != null;
        public bool IsMissing
        {
            get => _IsMissing;
            protected set => this.RaiseAndSetIfChanged(ref _IsMissing, value);
        }
        public bool IsEditable
        {
            get => _IsEditable;
            protected set => this.RaiseAndSetIfChanged(ref _IsEditable, value);
        }

        // the file changed or disappeared on disk while it had unsaved changes
        public string? ConflictText
        {
            get => _ConflictText;
            protected set
            {
                this.RaiseAndSetIfChanged(ref _ConflictText, value);
                this.RaisePropertyChanged(nameof(HasConflict));
            }
        }
        public bool HasConflict => ConflictText != null;
        public bool CanApplyMine
        {
            get => _CanApplyMine;
            protected set => this.RaiseAndSetIfChanged(ref _CanApplyMine, value);
        }
        public string ApplyMineLabel
        {
            get => _ApplyMineLabel;
            protected set => this.RaiseAndSetIfChanged(ref _ApplyMineLabel, value);
        }
        public bool CanRecreate
        {
            get => _CanRecreate;
            protected set => this.RaiseAndSetIfChanged(ref _CanRecreate, value);
        }

        // the result of the last action (saved, reloaded, a failure): fixed text and file names
        public string? StatusText
        {
            get => _StatusText;
            protected set
            {
                this.RaiseAndSetIfChanged(ref _StatusText, value);
                this.RaisePropertyChanged(nameof(HasStatus));
            }
        }
        public bool HasStatus => StatusText != null;
        public bool StatusIsError
        {
            get => _StatusIsError;
            protected set => this.RaiseAndSetIfChanged(ref _StatusIsError, value);
        }

        public List<NoticeItem> FileNotices
        {
            get => _FileNotices;
            protected set
            {
                this.RaiseAndSetIfChanged(ref _FileNotices, value);
                this.RaisePropertyChanged(nameof(HasFileNotices));
            }
        }
        public bool HasFileNotices => FileNotices.Count > 0;

        public List<PreviewLineItem> PreviewLines
        {
            get => _PreviewLines;
            protected set
            {
                this.RaiseAndSetIfChanged(ref _PreviewLines, value);
                this.RaisePropertyChanged(nameof(HasPreview));
                this.RaisePropertyChanged(nameof(PreviewText));
            }
        }
        public bool HasPreview => PreviewLines.Count > 0;
        // the preview as one read-only text (the password masked)
        public string PreviewText => string.Join("\n", PreviewLines.Select(L => L.Display));
        public string PreviewSummary
        {
            get => _PreviewSummary;
            protected set => this.RaiseAndSetIfChanged(ref _PreviewSummary, value);
        }

        public bool IsDirty
        {
            get => _IsDirty;
            protected set => this.RaiseAndSetIfChanged(ref _IsDirty, value);
        }
        public bool CanSave
        {
            get => _CanSave;
            protected set => this.RaiseAndSetIfChanged(ref _CanSave, value);
        }

        public RelayCommand SaveCommand { get; protected init; } = null!;
        public RelayCommand RevertCommand { get; protected init; } = null!;
        public RelayCommand ReloadCommand { get; protected init; } = null!;
        public RelayCommand CreateCommand { get; protected init; } = null!;
        public RelayCommand UseTheirsCommand { get; protected init; } = null!;
        public RelayCommand ApplyMineCommand { get; protected init; } = null!;
        public RelayCommand RecreateCommand { get; protected init; } = null!;

        // looks at the file on disk: a clean editor follows it, an edited one opens a conflict
        public abstract void CheckForExternalChanges();

        // the form's settings in the order the editor shows them (a UI that renders every field the same way, the web UI)
        public abstract IReadOnlyList<SettingField> FieldList { get; }

        // how many times the fields were filled from the session (opening, a save, a revert, a reload, a change on disk, a
        // conflict's resolution): a host that sends the user's entries later (the web UI) applies them only to the values
        // it showed, never to values that were replaced meanwhile
        public int Generation { get; protected set; }

        // A host without live updates (the web UI) sends the user's entries with every action and asks for Reload after
        // they were applied: with unsaved changes nothing is reloaded and the form says why (the desktop GUI does not offer
        // Reload then, so it never shows these words)
        public const string ReloadRefused = "Nothing was reloaded: Reload is offered only when there are no unsaved changes. Save or revert them first.";
        public abstract void ReloadUnlessChanged();

        // saves the file when nothing blocks it, else says what does (whoever calls it: D71)
        public abstract void Save();

        // the file's values again in every field, also a field's own input error that never reached the session (a host
        // without live updates offers Revert whenever the form can be edited; the desktop GUI only with changes)
        public abstract void Revert();
    }

    // The part that drives the editing session; the General and Email editors add their fields.
    public abstract class SettingsFileViewModel<T> : SettingsFileViewModel where T : class, new()
    {
        private static readonly string[] SeverityOrder = { "Error", "Warning", "Info" };
        protected readonly List<SettingField> Fields = new List<SettingField>();

        internal SettingsFileViewModel(ConfigEditSession<T> Session) : base(Session.FileName, Session.FilePath)
        {
            this.Session = Session;
            SaveCommand = new RelayCommand(Save, () => CanSave);
            RevertCommand = new RelayCommand(Revert, () => IsDirty);
            ReloadCommand = new RelayCommand(Reload, () => !IsDirty);
            CreateCommand = new RelayCommand(Create, () => IsMissing);
            UseTheirsCommand = new RelayCommand(UseTheirs, () => HasConflict);
            ApplyMineCommand = new RelayCommand(ApplyMine, () => CanApplyMine);
            RecreateCommand = new RelayCommand(Recreate, () => CanRecreate);
        }

        internal ConfigEditSession<T> Session { get; }

        public override IReadOnlyList<SettingField> FieldList => Fields;

        protected void Add(SettingField Field)
        {
            Field.UserChanged = () => OnFieldChanged(Field);
            if (Field is ListSettingField List)
            {
                List.RowsChanged = () => OnRowsChanged(List);
            }
            if (Field is FilterSelectorField Selector)
            {
                Selector.RowsChanged = () => OnSelectorChanged(Selector);
            }
            Fields.Add(Field);
        }

        // the field's value from the session (after opening, a reload, a revert or a save)
        protected abstract void Fill();
        // Fill, counted (Generation)
        private void Refill()
        {
            Generation++;
            Fill();
        }
        // the user's value of a field into the session (or its InputError)
        protected abstract void Apply(SettingField Field);
        // input errors that are not in Fields (General.yaml's: an outputTo the monitoring's start would refuse, D71)
        protected virtual bool HasOtherInputErrors => false;
        // at the start of every refresh: what the editor checks beyond its own file (General.yaml's outputTo, D71)
        protected virtual void Recheck()
        {
        }
        // after everything shown was refreshed from the session
        protected virtual void OnRefreshed()
        {
        }
        // a notice about the whole file that the editor shows next to its fields instead (a search's weekdays)
        private protected virtual bool ShownWithFields(ConfigEditNotice Notice) => false;
        // after a save, reload or revert: the password is hidden again
        protected virtual void HideSecrets()
        {
        }
        // the save itself, once nothing blocks it: the session's (Web.yaml's goes through WebAccessRule, D81). Null when the
        // save waits for the user's confirmation, which the form then shows; nothing was written
        private protected virtual ConfigEditResult? SaveSession() => Session.Save();
        // a field changed, or the edits were reverted, reloaded or saved (Web.yaml's editor drops a confirmation it asked for)
        private protected virtual void OnEdited()
        {
        }

        private void OnFieldChanged(SettingField Field)
        {
            StatusText = null;
            OnEdited();
            Apply(Field);
            DropEmptyItems();
            Refresh();
        }

        // a list's rows were added, removed, moved or changed: the session gets them, each with the item it was in the file
        // (D71: the rows that are not empty, without the spaces around their values)
        private void OnRowsChanged(ListSettingField List)
        {
            StatusText = null;
            OnEdited();
            if (Session.State == ConfigEditState.Editable && Session.Conflict == null && List.IsEditable)
            {
                Session.SetItems(List.Key, List.Values().Select(R => new ConfigListItem(R.OriginalIndex, R.Value)).ToList());
            }
            DropEmptyItems();
            Refresh();
        }

        // D71: an empty row is never written, also when the file has an empty item and another setting changed: with any
        // change, a list that still shows the file's items one by one and has an empty row goes to the session without it
        // (the preview shows the removed item)
        private void DropEmptyItems()
        {
            if (Session.State != ConfigEditState.Editable || Session.Conflict != null || !Session.IsDirty)
            {
                return;
            }
            foreach (ListSettingField List in Fields.OfType<ListSettingField>())
            {
                if (!List.Sent && List.IsEditable && List.HasBlankRows)
                {
                    Session.SetItems(List.Key, List.Values().Select(R => new ConfigListItem(R.OriginalIndex, R.Value)).ToList());
                    List.MarkSent();
                }
            }
        }

        // a selector's list changed (a value checked or unchecked, a custom value added or removed, D51): the session gets
        // its rows exactly as a list's, each with the item it was in the file
        private void OnSelectorChanged(FilterSelectorField Selector)
        {
            StatusText = null;
            if (Session.State == ConfigEditState.Editable && Session.Conflict == null && Selector.IsEditable)
            {
                Session.SetItems(Selector.Key, Selector.Values().Select(R => new ConfigListItem(R.OriginalIndex, R.Value)).ToList());
            }
            DropEmptyItems();
            Refresh();
        }

        // a list's rows from the session (in Fill)
        protected void FillList(ListSettingField List)
        {
            List.FillRows(Session.Items(List.Key));
        }

        // a selector's rows from the session (in Fill)
        protected void FillSelector(FilterSelectorField Selector)
        {
            Selector.FillRows(Session.Items(Selector.Key));
        }

        protected void Load()
        {
            Refill();
            Refresh();
        }

        // everything shown, from the session's state
        protected void Refresh()
        {
            Recheck();
            ConfigEditState State = Session.State;
            IsMissing = State == ConfigEditState.Missing;
            StateText = State == ConfigEditState.Editable ? null : Session.StateReason;
            ConfigConflict? Conflict = Session.Conflict;
            IsEditable = State == ConfigEditState.Editable && Conflict == null;
            ConflictText = Conflict?.Message;
            CanApplyMine = Conflict != null && Conflict.CanApplyEdits;
            ApplyMineLabel = Conflict != null && Conflict.OverlappingKeys.Count > 0
                ? "Keep my values for " + string.Join(", ", Conflict.OverlappingKeys)
                : "Apply my changes to the new version";
            CanRecreate = Conflict != null && Conflict.Kind == ConfigConflictKind.Deleted && Session.Candidate != null && !Session.HasBlockingErrors;

            IReadOnlyList<ConfigEditNotice> Notices = Session.Notices;
            var Keys = new HashSet<string>(Fields.Select(F => F.Key), StringComparer.Ordinal);
            foreach (SettingField Field in Fields)
            {
                Field.IsChanged = Session.IsChanged(Field.Key);
                Field.LockReason = State == ConfigEditState.Editable ? Session.LockReason(Field.Key) : null;
                Field.IsEditable = IsEditable && Field.LockReason == null;
                if (Field is TextSettingField Text && Text.InputError != null)
                {
                    Field.SetMessage(Text.InputError, "Error");
                    continue;
                }
                List<ConfigEditNotice> Own = Notices.Where(N => N.Key == Field.Key && N.Index == null).OrderBy(N => Rank(N.Severity)).ToList();
                Field.SetMessage(Own.Count == 0 ? null : string.Join(" ", Own.Select(N => N.Message).Distinct()), Own.Count == 0 ? string.Empty : Own[0].Severity.ToString());
            }
            foreach (ListSettingField List in Fields.OfType<ListSettingField>())
            {
                List.ShowRowMessages(Notices.Where(N => N.Key == List.Key && N.Index != null).Select(N => (N.Index!.Value, N.Severity.ToString(), N.Message)));
            }
            foreach (FilterSelectorField Selector in Fields.OfType<FilterSelectorField>())
            {
                Selector.ShowRowMessages(Notices.Where(N => N.Key == Selector.Key && N.Index != null).Select(N => (N.Index!.Value, N.Severity.ToString(), N.Message)));
            }
            FileNotices = Notices.Where(N => (N.Key == null || !Keys.Contains(N.Key)) && !ShownWithFields(N)).OrderBy(N => Rank(N.Severity))
                .Select(N => new NoticeItem(N.Severity.ToString(), N.Message)).ToList();

            PreviewLines = Session.Preview.Select(L => new PreviewLineItem(L.Kind.ToString(), L.Text)).ToList();
            PreviewSummary = Session.IsDirty
                ? "Changes: " + string.Join(", ", Session.ChangedKeys.Select(K => Session.SecretKeys.Contains(K) ? K + " (hidden)" : K))
                : string.Empty;
            IsDirty = Session.IsDirty;
            CanSave = Session.CanSave && !HasInputErrors;
            foreach (RelayCommand Command in new[] { SaveCommand, RevertCommand, ReloadCommand, CreateCommand, UseTheirsCommand, ApplyMineCommand, RecreateCommand })
            {
                Command.Refresh();
            }
            OnRefreshed();
        }

        private static int Rank(ConfigSeverity Severity) => Array.IndexOf(SeverityOrder, Severity.ToString());

        // a field's own error (a value the session never got) or an error beyond the file (D71): Save is not offered
        private bool HasInputErrors => Fields.OfType<TextSettingField>().Any(F => F.InputError != null) || HasOtherInputErrors;

        private void Report(ConfigEditResult Result)
        {
            StatusText = Result.Message;
            StatusIsError = !Result.Succeeded;
        }

        private void ClearInputErrors()
        {
            foreach (TextSettingField Field in Fields.OfType<TextSettingField>())
            {
                Field.InputError = null;
            }
        }

        public override void Save()
        {
            // D71: checked again just before the save (another file may have changed since), and never saved with a field's
            // own error or an error beyond the file, whoever calls Save
            Refresh();
            if (HasInputErrors)
            {
                Report(new ConfigEditResult(false, "Nothing was saved: fix the errors shown first."));
                return;
            }
            ConfigEditResult? Result = SaveSession();
            if (Result == null)
            {
                // the form asks for a confirmation first (Web.yaml, D81): nothing was written
                StatusText = null;
                Refresh();
                return;
            }
            if (Result.Succeeded)
            {
                ClearInputErrors();
                HideSecrets();
                OnEdited();
                Refill();
            }
            Report(Result);
            Refresh();
        }

        public override void Revert()
        {
            OnEdited();
            Session.RevertAll();
            ClearInputErrors();
            HideSecrets();
            Refill();
            Report(new ConfigEditResult(true, "Your changes to " + FileName + " were reverted."));
            Refresh();
        }

        public void Reload()
        {
            OnEdited();
            Session.Reload();
            ClearInputErrors();
            HideSecrets();
            Refill();
            Report(new ConfigEditResult(true, "Reloaded " + FileName + "."));
            Refresh();
        }

        public override void ReloadUnlessChanged()
        {
            if (IsDirty || HasConflict)
            {
                Report(new ConfigEditResult(false, ReloadRefused));
                Refresh();
                return;
            }
            Reload();
        }

        public void Create()
        {
            Report(Session.Create());
            Refill();
            Refresh();
        }

        public void UseTheirs()
        {
            OnEdited();
            Session.UseExternalVersion();
            ClearInputErrors();
            HideSecrets();
            Refill();
            Report(new ConfigEditResult(true, "Your changes were discarded; " + FileName + " shows the version on disk."));
            Refresh();
        }

        public void ApplyMine()
        {
            OnEdited();
            ConfigConflict? Conflict = Session.Conflict;
            Report(Session.ApplyEditsToExternalVersion(KeepMine: Conflict != null && Conflict.OverlappingKeys.Count > 0));
            Refill();
            Refresh();
        }

        public void Recreate()
        {
            OnEdited();
            ConfigEditResult Result = Session.RecreateWithEdits();
            if (Result.Succeeded)
            {
                ClearInputErrors();
                HideSecrets();
            }
            Refill();
            Report(Result);
            Refresh();
        }

        public override void CheckForExternalChanges()
        {
            ConfigExternalChange Change = Session.CheckForExternalChange();
            switch (Change)
            {
                case ConfigExternalChange.Reloaded:
                    ClearInputErrors();
                    Refill();
                    Report(new ConfigEditResult(true, FileName + " was changed on disk and has been reloaded."));
                    break;
                case ConfigExternalChange.Deleted:
                    ClearInputErrors();
                    Refill();
                    Report(new ConfigEditResult(false, FileName + " was deleted on disk."));
                    break;
                case ConfigExternalChange.MetadataChanged:
                    Report(new ConfigEditResult(true, FileName + "'s permissions or time changed on disk; its content is the same."));
                    break;
                case ConfigExternalChange.Conflict:
                    StatusText = null;
                    break;
            }
            Refresh();
        }
    }
}
