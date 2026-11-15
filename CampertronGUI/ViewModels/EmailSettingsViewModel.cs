using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Campertron.ViewModels
{
    // Email.yaml in the Settings tab (CM5, decision D43): the SMTP server, port, user name and password, the sender and
    // the recipients the Email output sends new results to. The password is masked unless the user reveals it for a
    // moment (hidden again after a save, a revert, a reload, a conflict resolution, leaving the tab or the window); it
    // never appears in a message, the preview or accessibility text. Nothing here connects to the server or sends.
    public sealed class EmailSettingsViewModel : SettingsFileViewModel<EmailConfig>
    {
        private const string RecipientsKey = "sendToAddressList";
        private bool _IsPasswordRevealed;
        private bool _Filling;

        internal EmailSettingsViewModel(ConfigEditSession<EmailConfig> Session) : base(Session)
        {
            SmtpServer = new TextSettingField("smtpServer", "SMTP _server", "The name of your email provider's SMTP server, such as smtp.example.com.");
            SmtpPort = new TextSettingField("smtpPort", "SMTP _port", "The SMTP server's port, usually 587 or 465.");
            SmtpUsername = new TextSettingField("smtpUsername", "_User name", "The user name Campertron signs in with.");
            SmtpPassword = new TextSettingField("smtpPassword", "Pass_word", "The password Campertron signs in with. It is stored as plain text in Email.yaml.", IsSecret: true);
            SendFromAddress = new TextSettingField("sendFromAddress", "_From address", "The address the results are sent from.");
            Recipients = new ListSettingField(RecipientsKey, "_Recipients", "The addresses the results are sent to.");
            Add(SmtpServer);
            Add(SmtpPort);
            Add(SmtpUsername);
            Add(SmtpPassword);
            Add(SendFromAddress);
            Add(Recipients);
            AddRecipientCommand = new RelayCommand(AddRecipient, () => Recipients.IsEditable);
            Load();
        }

        public override string Title => "Email settings";
        public TextSettingField SmtpServer { get; }
        public TextSettingField SmtpPort { get; }
        public TextSettingField SmtpUsername { get; }
        public TextSettingField SmtpPassword { get; }
        public TextSettingField SendFromAddress { get; }
        public ListSettingField Recipients { get; }
        public RelayCommand AddRecipientCommand { get; }

        public bool IsPasswordRevealed
        {
            get => _IsPasswordRevealed;
            set => this.RaiseAndSetIfChanged(ref _IsPasswordRevealed, value);
        }
        public string RevealLabel => "Show password";

        public string OutputExplanation => Session.EmailOutput
            ? "outputTo is Email, so these settings are needed: problems block saving."
            : "These settings are used only when outputTo is Email; until then problems are warnings.";

        // General.yaml's outputTo as the General editor shows it
        internal void SetEmailOutput(bool EmailOutput)
        {
            if (Session.EmailOutput != EmailOutput)
            {
                Session.EmailOutput = EmailOutput;
                this.RaisePropertyChanged(nameof(OutputExplanation));
                Refresh();
            }
        }

        protected override void HideSecrets()
        {
            IsPasswordRevealed = false;
        }

        internal void HidePassword()
        {
            IsPasswordRevealed = false;
        }

        protected override void Fill()
        {
            _Filling = true;
            SmtpServer.Fill(Session.Value("smtpServer") as string ?? string.Empty);
            SmtpPort.Fill(Session.Value("smtpPort") is int Port ? Port.ToString(CultureInfo.InvariantCulture) : string.Empty);
            SmtpUsername.Fill(Session.Value("smtpUsername") as string ?? string.Empty);
            SmtpPassword.Fill(Session.Value("smtpPassword") as string ?? string.Empty);
            SendFromAddress.Fill(Session.Value("sendFromAddress") as string ?? string.Empty);
            Recipients.Rows.Clear();
            foreach (ConfigListItem Item in Session.Items(RecipientsKey))
            {
                Recipients.Rows.Add(NewRow(Item.OriginalIndex, Item.Value!));
            }
            Renumber();
            _Filling = false;
        }

        private RecipientRow NewRow(int? OriginalIndex, string Value)
        {
            var Row = new RecipientRow(OriginalIndex, Value, RowsChanged);
            Row.RemoveCommand = new RelayCommand(() => Remove(Row), () => Recipients.IsEditable);
            Row.UpCommand = new RelayCommand(() => Move(Row, -1), () => Recipients.IsEditable && Recipients.Rows.IndexOf(Row) > 0);
            Row.DownCommand = new RelayCommand(() => Move(Row, 1), () => Recipients.IsEditable && Recipients.Rows.IndexOf(Row) < Recipients.Rows.Count - 1);
            return Row;
        }

        private void Renumber()
        {
            for (int i = 0; i < Recipients.Rows.Count; i++)
            {
                Recipients.Rows[i].Number = i + 1;
            }
            Recipients.RefreshCount();
        }

        public void AddRecipient()
        {
            Recipients.Rows.Add(NewRow(null, string.Empty));
            RowsChanged();
        }

        private void Remove(RecipientRow Row)
        {
            Recipients.Rows.Remove(Row);
            RowsChanged();
        }

        private void Move(RecipientRow Row, int By)
        {
            int From = Recipients.Rows.IndexOf(Row);
            int To = From + By;
            if (From < 0 || To < 0 || To >= Recipients.Rows.Count)
            {
                return;
            }
            Recipients.Rows.Move(From, To);
            RowsChanged();
        }

        private void RowsChanged()
        {
            if (_Filling)
            {
                return;
            }
            Renumber();
            StatusText = null;
            if (Session.State == ConfigEditState.Editable && Session.Conflict == null && Recipients.IsEditable)
            {
                Session.SetItems(RecipientsKey, Recipients.Rows.Select(R => new ConfigListItem(R.OriginalIndex, R.Text)).ToList());
            }
            Refresh();
        }

        protected override void Apply(SettingField Field)
        {
            if (Session.State != ConfigEditState.Editable || Session.Conflict != null || Field is not TextSettingField Text)
            {
                return;
            }
            if (Field.Key == "smtpPort")
            {
                if (int.TryParse(Text.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int Port))
                {
                    Text.InputError = null;
                    Session.Set("smtpPort", Port);
                }
                else
                {
                    Text.InputError = "Enter a port number, such as 587.";
                    Session.Revert("smtpPort");
                }
                return;
            }
            // an empty box is the file's empty or missing value; anything else is the text as entered
            string Shown = Session.OriginalValue(Field.Key) as string ?? string.Empty;
            if (Text.Text == Shown)
            {
                Session.Revert(Field.Key);
            }
            else
            {
                Session.Set(Field.Key, Text.Text);
            }
        }

        private protected override void ShowItemMessages(IReadOnlyList<ConfigEditNotice> Notices)
        {
            for (int i = 0; i < Recipients.Rows.Count; i++)
            {
                List<ConfigEditNotice> Own = Notices.Where(N => N.Key == RecipientsKey && N.Index == i)
                    .OrderBy(N => N.Severity == ConfigSeverity.Error ? 0 : N.Severity == ConfigSeverity.Warning ? 1 : 2).ToList();
                Recipients.Rows[i].SetMessage(Own.Count == 0 ? null : string.Join(" ", Own.Select(N => N.Message).Distinct()),
                    Own.Count == 0 ? string.Empty : Own[0].Severity.ToString());
                Recipients.Rows[i].RemoveCommand?.Refresh();
                Recipients.Rows[i].UpCommand?.Refresh();
                Recipients.Rows[i].DownCommand?.Refresh();
            }
            AddRecipientCommand?.Refresh();
        }
    }
}
