using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Campertron.ViewModels
{
    // One setting of General.yaml or Email.yaml in the settings editor (CM5, decision D43): its label, whether it is
    // changed, whether it can be edited (and why not), and the validation's message for it. Messages are the validation's,
    // the editor's or the GUI's own fixed text: never a value of the file.
    public abstract class SettingField : ViewModelBase
    {
        private string? _Message;
        private string _Severity = string.Empty;
        private bool _IsChanged;
        private bool _IsEditable = true;
        private string? _LockReason;

        protected SettingField(string Key, string Label, string Hint)
        {
            this.Key = Key;
            this.Label = Label;
            this.Hint = Hint;
        }

        // the YAML key (camelCase)
        public string Key { get; }
        // the visible label, with its access key marked by "_"
        public string Label { get; }
        // what the setting does (fixed text)
        public string Hint { get; }
        public string AccessibleName => Label.Replace("_", "");

        public string? Message
        {
            get => _Message;
            private set => this.RaiseAndSetIfChanged(ref _Message, value);
        }
        // "Error", "Warning", "Info" or empty
        public string Severity
        {
            get => _Severity;
            private set => this.RaiseAndSetIfChanged(ref _Severity, value);
        }
        public bool HasMessage => Message != null;
        // the message as shown, with its severity in words (not only in color)
        public string MessageText => Message == null ? string.Empty : Severity + ": " + Message;
        public bool IsError => Severity == "Error";
        public bool IsWarning => Severity == "Warning";
        public bool IsInfo => Severity == "Info";
        // the text a screen reader reads with the control: the problem (prefixed by its severity), else why it is locked
        public string HelpText => Message != null ? Severity + ": " + Message : LockReason ?? Hint;

        public bool IsChanged
        {
            get => _IsChanged;
            set => this.RaiseAndSetIfChanged(ref _IsChanged, value);
        }
        public bool IsEditable
        {
            get => _IsEditable;
            set => this.RaiseAndSetIfChanged(ref _IsEditable, value);
        }
        public string? LockReason
        {
            get => _LockReason;
            set
            {
                this.RaiseAndSetIfChanged(ref _LockReason, value);
                this.RaisePropertyChanged(nameof(HasLockReason));
                this.RaisePropertyChanged(nameof(HelpText));
            }
        }
        public bool HasLockReason => LockReason != null;

        internal void SetMessage(string? Text, string Kind)
        {
            Message = Text;
            Severity = Text == null ? string.Empty : Kind;
            this.RaisePropertyChanged(nameof(HasMessage));
            this.RaisePropertyChanged(nameof(MessageText));
            this.RaisePropertyChanged(nameof(IsError));
            this.RaisePropertyChanged(nameof(IsWarning));
            this.RaisePropertyChanged(nameof(IsInfo));
            this.RaisePropertyChanged(nameof(HelpText));
        }

        // set by the file's view model: called when the user changes the value
        internal Action? UserChanged;
        // true while the view model fills in the value, which is no user change
        internal bool Loading;

        protected void OnChanged()
        {
            if (!Loading)
            {
                UserChanged?.Invoke();
            }
        }
    }

    // a setting entered as text: a name, an address, a number, a date or the password
    public sealed class TextSettingField : SettingField
    {
        private string _Text = string.Empty;

        public TextSettingField(string Key, string Label, string Hint, bool IsSecret = false) : base(Key, Label, Hint)
        {
            this.IsSecret = IsSecret;
        }

        // the password: shown masked, never in a message, a preview or accessibility text
        public bool IsSecret { get; }

        public string Text
        {
            get => _Text;
            set
            {
                string New = value ?? string.Empty;
                if (_Text != New)
                {
                    this.RaiseAndSetIfChanged(ref _Text, New);
                    OnChanged();
                }
            }
        }

        // a text that is not a value of the setting's type (not a number, not a date): the GUI's own message
        internal string? InputError { get; set; }

        internal void Fill(string Value)
        {
            Loading = true;
            Text = Value;
            Loading = false;
        }
    }

    public sealed class BoolSettingField : SettingField
    {
        private bool _IsChecked;

        public BoolSettingField(string Key, string Label, string Hint) : base(Key, Label, Hint)
        {
        }

        public bool IsChecked
        {
            get => _IsChecked;
            set
            {
                if (_IsChecked != value)
                {
                    this.RaiseAndSetIfChanged(ref _IsChecked, value);
                    OnChanged();
                }
            }
        }

        internal void Fill(bool Value)
        {
            Loading = true;
            IsChecked = Value;
            Loading = false;
        }
    }

    // A list setting (Email.yaml's recipients, Ntfy.yaml's tags, a search file's dates): its rows, each with its own message,
    // and the commands that add, remove and move them. Every change of the rows goes to the file's view model
    // (RowsChanged), which gives the session the rows with the item each was in the file.
    //
    // D71: a row that is empty or only spaces is never written and never an error (it is left out of what the session
    // gets), and every other row's value is written without the spaces around it. Until the user changes the list, the
    // rows are the file's items one by one, an empty item of the file included (its problem is the file's, shown at it).
    public sealed class ListSettingField : SettingField
    {
        private bool _Filling;
        private bool _Sent;

        public ListSettingField(string Key, string Label, string Hint, string ItemName, string ItemHint, string? RowsHelp = null) : base(Key, Label, Hint)
        {
            this.ItemName = ItemName;
            this.ItemHint = ItemHint;
            this.RowsHelp = RowsHelp ?? "One " + ItemName + " per row. Empty rows are ignored.";
            AddCommand = new RelayCommand(AddRow, () => IsEditable);
        }

        // what one item is called, in lower case ("recipient", "date", ...), and what it is
        public string ItemName { get; }
        public string ItemHint { get; }
        // how the rows work, shown under the label (D71)
        public string RowsHelp { get; }
        public string AddLabel => "Add " + ItemName;
        public string AddHelpText => "Adds an empty row at the end and moves the focus to it. " + RowsHelp;
        public string EmptyText => "No " + ItemName + "s.";

        public System.Collections.ObjectModel.ObservableCollection<ListRow> Rows { get; } = new System.Collections.ObjectModel.ObservableCollection<ListRow>();
        public bool HasNoRows => Rows.Count == 0;
        public RelayCommand AddCommand { get; }

        // set by the file's view model: called when the user adds, removes, moves or changes a row
        internal Action? RowsChanged;

        // the rows of the session's items (after opening, a reload, a revert or a save); no user change
        internal void FillRows(IEnumerable<ConfigListItem> Items)
        {
            _Filling = true;
            _Sent = false;
            Rows.Clear();
            foreach (var Item in Items)
            {
                Rows.Add(NewRow(Item.OriginalIndex, Item.Value));
            }
            Renumber();
            _Filling = false;
        }

        // the rows as the session takes them (D71): the rows that are not empty, each with the item it was in the file (null
        // for a new one) and its value without the spaces around it; an empty row is left out, so it is never written
        internal IEnumerable<(int? OriginalIndex, string? Value)> Values() => Rows.Where(R => !R.IsBlank).Select(R => (R.OriginalIndex, (string?)R.Text.Trim()));

        // a row is empty or only spaces: the file's empty item, or a row the user added or cleared
        internal bool HasBlankRows => Rows.Any(R => R.IsBlank);

        // whether the session has the rows as Values() gives them (after a change of the rows, or when the file's view
        // model gave them because another setting changed), rather than the file's items one by one
        internal bool Sent => _Sent;
        internal void MarkSent()
        {
            _Sent = true;
        }

        public void AddRow()
        {
            var Row = NewRow(null, string.Empty);
            Row.FocusWhenShown = true;
            Rows.Add(Row);
            RowsUpdated();
        }

        private ListRow NewRow(int? OriginalIndex, string? Value)
        {
            var Row = new ListRow(this, OriginalIndex, Value, RowsUpdated);
            Row.RemoveCommand = new RelayCommand(() => Remove(Row), () => IsEditable);
            Row.UpCommand = new RelayCommand(() => Move(Row, -1), () => IsEditable && Rows.IndexOf(Row) > 0);
            Row.DownCommand = new RelayCommand(() => Move(Row, 1), () => IsEditable && Rows.IndexOf(Row) < Rows.Count - 1);
            return Row;
        }

        private void Remove(ListRow Row)
        {
            Rows.Remove(Row);
            RowsUpdated();
        }

        private void Move(ListRow Row, int By)
        {
            int From = Rows.IndexOf(Row);
            int To = From + By;
            if (From < 0 || To < 0 || To >= Rows.Count)
            {
                return;
            }
            Rows.Move(From, To);
            RowsUpdated();
        }

        private void Renumber()
        {
            for (int i = 0; i < Rows.Count; i++)
            {
                Rows[i].Number = i + 1;
            }
            this.RaisePropertyChanged(nameof(HasNoRows));
        }

        private void RowsUpdated()
        {
            if (_Filling)
            {
                return;
            }
            _Sent = true;
            Renumber();
            RowsChanged?.Invoke();
        }

        // each row's own message (the notices about its item), and the commands' states. The notices number the items the
        // session has: the file's items one by one until the rows were sent, then the rows that are not empty (D71), so an
        // empty row the user added never gets a message
        internal void ShowRowMessages(IEnumerable<(int Index, string Severity, string Message)> Messages)
        {
            var ByRow = Messages.GroupBy(M => M.Index).ToDictionary(G => G.Key, G => G.ToList());
            List<ListRow> Items = _Sent ? Rows.Where(R => !R.IsBlank).ToList() : Rows.ToList();
            for (int i = 0; i < Rows.Count; i++)
            {
                int Item = Items.IndexOf(Rows[i]);
                if (Item >= 0 && ByRow.TryGetValue(Item, out var Own))
                {
                    var Ordered = Own.OrderBy(M => M.Severity == "Error" ? 0 : M.Severity == "Warning" ? 1 : 2).ToList();
                    Rows[i].SetMessage(string.Join(" ", Ordered.Select(M => M.Message).Distinct()), Ordered[0].Severity);
                }
                else
                {
                    Rows[i].SetMessage(null, string.Empty);
                }
                Rows[i].RemoveCommand?.Refresh();
                Rows[i].UpCommand?.Refresh();
                Rows[i].DownCommand?.Refresh();
            }
            AddCommand.Refresh();
        }
    }

    // one entry of a choice; its Label is what the list shows and what a screen reader reads
    public sealed class SettingChoice
    {
        public SettingChoice(object Value, string Label)
        {
            this.Value = Value;
            this.Label = Label;
        }
        public object Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    public sealed class ChoiceSettingField : SettingField
    {
        private List<SettingChoice> _Choices = new List<SettingChoice>();
        private SettingChoice? _Selected;

        public ChoiceSettingField(string Key, string Label, string Hint) : base(Key, Label, Hint)
        {
        }

        public List<SettingChoice> Choices
        {
            get => _Choices;
            private set => this.RaiseAndSetIfChanged(ref _Choices, value);
        }

        public SettingChoice? Selected
        {
            get => _Selected;
            set
            {
                if (!ReferenceEquals(_Selected, value) && value != null)
                {
                    this.RaiseAndSetIfChanged(ref _Selected, value);
                    OnChanged();
                }
            }
        }

        internal void Fill(List<SettingChoice> Choices, SettingChoice? Selected)
        {
            Loading = true;
            this.Choices = Choices;
            _Selected = Selected;
            this.RaisePropertyChanged(nameof(this.Selected));
            Loading = false;
        }
    }

    // One row of a list setting: the item it was in the file (null for a new one) and its text. An item that is empty
    // (null) in the file shows an empty box and stays empty in the file until the user types in it.
    public sealed class ListRow : ViewModelBase
    {
        private readonly ListSettingField _Field;
        private readonly Action _Changed;
        private string _Text;
        private bool _EmptyInFile;
        private int _Number;
        private string? _Message;
        private string _Severity = string.Empty;

        internal ListRow(ListSettingField Field, int? OriginalIndex, string? Value, Action Changed)
        {
            _Field = Field;
            this.OriginalIndex = OriginalIndex;
            _Text = Value ?? string.Empty;
            _EmptyInFile = Value == null;
            _Changed = Changed;
        }

        internal int? OriginalIndex { get; }
        // the row is empty or only spaces (D71: never written, never an error)
        internal bool IsBlank => string.IsNullOrWhiteSpace(_Text);
        // a row just added with Add: the view moves the keyboard focus to its box once it is shown (D71)
        internal bool FocusWhenShown { get; set; }

        public string Text
        {
            get => _Text;
            set
            {
                string New = value ?? string.Empty;
                if (_Text != New || _EmptyInFile)
                {
                    _EmptyInFile = false;
                    this.RaiseAndSetIfChanged(ref _Text, New);
                    this.RaisePropertyChanged(nameof(Placeholder));
                    _Changed();
                }
            }
        }

        // shown in the box of an empty item of the file
        public string? Placeholder => _EmptyInFile ? "(empty in the file)" : null;

        // 1-based position, for the labels "Recipient 1", "Remove recipient 1", ...
        public int Number
        {
            get => _Number;
            internal set
            {
                this.RaiseAndSetIfChanged(ref _Number, value);
                this.RaisePropertyChanged(nameof(AccessibleName));
                this.RaisePropertyChanged(nameof(RemoveName));
                this.RaisePropertyChanged(nameof(UpName));
                this.RaisePropertyChanged(nameof(DownName));
            }
        }
        private string Item => _Field.ItemName;
        public string AccessibleName => char.ToUpperInvariant(Item[0]) + Item.Substring(1) + " " + Number;
        public string RemoveName => "Remove " + Item + " " + Number;
        public string UpName => "Move " + Item + " " + Number + " up";
        public string DownName => "Move " + Item + " " + Number + " down";
        public string HelpText => Message != null ? Severity + ": " + Message : _Field.ItemHint;

        public string? Message
        {
            get => _Message;
            private set => this.RaiseAndSetIfChanged(ref _Message, value);
        }
        public string Severity
        {
            get => _Severity;
            private set => this.RaiseAndSetIfChanged(ref _Severity, value);
        }
        public bool HasMessage => Message != null;
        public string MessageText => Message == null ? string.Empty : Severity + ": " + Message;
        public bool IsError => Severity == "Error";
        public bool IsWarning => Severity == "Warning";

        internal void SetMessage(string? Text, string Kind)
        {
            Message = Text;
            Severity = Text == null ? string.Empty : Kind;
            this.RaisePropertyChanged(nameof(MessageText));
            this.RaisePropertyChanged(nameof(HasMessage));
            this.RaisePropertyChanged(nameof(IsError));
            this.RaisePropertyChanged(nameof(IsWarning));
            this.RaisePropertyChanged(nameof(HelpText));
        }

        public RelayCommand? RemoveCommand { get; internal set; }
        public RelayCommand? UpCommand { get; internal set; }
        public RelayCommand? DownCommand { get; internal set; }
    }

    // a message about the whole file (not about one field)
    public sealed class NoticeItem
    {
        public NoticeItem(string Severity, string Message)
        {
            this.Severity = Severity;
            this.Message = Message;
        }
        public string Severity { get; }
        public string Message { get; }
        public bool IsError => Severity == "Error";
        public bool IsWarning => Severity == "Warning";
        public string Text => Severity + ": " + Message;
    }

    // one line of the preview of a save; the password's value is already masked
    public sealed class PreviewLineItem
    {
        public PreviewLineItem(string Kind, string Text)
        {
            this.Kind = Kind;
            this.Text = Text;
        }
        // "Removed", "Added", "Context" or "Gap"
        public string Kind { get; }
        public string Text { get; }
        public bool IsRemoved => Kind == "Removed";
        public bool IsAdded => Kind == "Added";
        public string Marker => Kind switch { "Removed" => "-", "Added" => "+", _ => " " };
        public string Display => Marker + " " + Text;
    }
}
