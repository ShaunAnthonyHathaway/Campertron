using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;

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

    // a list setting (Email.yaml's recipients): its rows, each with its own message
    public sealed class ListSettingField : SettingField
    {
        public ListSettingField(string Key, string Label, string Hint) : base(Key, Label, Hint)
        {
        }

        public System.Collections.ObjectModel.ObservableCollection<RecipientRow> Rows { get; } = new System.Collections.ObjectModel.ObservableCollection<RecipientRow>();
        public bool HasNoRows => Rows.Count == 0;

        internal void RefreshCount()
        {
            this.RaisePropertyChanged(nameof(HasNoRows));
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

    // one recipient of Email.yaml's sendToAddressList: the item it was in the file (null for a new one) and its address
    public sealed class RecipientRow : ViewModelBase
    {
        private string _Text;
        private int _Number;
        private string? _Message;
        private string _Severity = string.Empty;

        internal RecipientRow(int? OriginalIndex, string Text, Action Changed)
        {
            this.OriginalIndex = OriginalIndex;
            _Text = Text;
            _Changed = Changed;
        }

        private readonly Action _Changed;
        internal int? OriginalIndex { get; }

        public string Text
        {
            get => _Text;
            set
            {
                string New = value ?? string.Empty;
                if (_Text != New)
                {
                    this.RaiseAndSetIfChanged(ref _Text, New);
                    _Changed();
                }
            }
        }

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
        public string AccessibleName => "Recipient " + Number;
        public string RemoveName => "Remove recipient " + Number;
        public string UpName => "Move recipient " + Number + " up";
        public string DownName => "Move recipient " + Number + " down";
        public string HelpText => Message != null ? Severity + ": " + Message : "An email address the results are sent to.";

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
