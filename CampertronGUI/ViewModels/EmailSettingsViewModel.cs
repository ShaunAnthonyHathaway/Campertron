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

        internal EmailSettingsViewModel(ConfigEditSession<EmailConfig> Session) : base(Session)
        {
            SmtpServer = new TextSettingField("smtpServer", "SMTP _server", "The name of your email provider's SMTP server, such as smtp.example.com.");
            SmtpPort = new TextSettingField("smtpPort", "SMTP _port", "The SMTP server's port, usually 587 or 465.");
            SmtpUsername = new TextSettingField("smtpUsername", "_User name", "The user name Campertron signs in with.");
            SmtpPassword = new TextSettingField("smtpPassword", "Pass_word", "The password Campertron signs in with. It is stored as plain text in Email.yaml.", IsSecret: true);
            SendFromAddress = new TextSettingField("sendFromAddress", "_From address", "The address the results are sent from.");
            Recipients = new ListSettingField(RecipientsKey, "_Recipients", "The addresses the results are sent to.", "recipient", "An email address the results are sent to.");
            Add(SmtpServer);
            Add(SmtpPort);
            Add(SmtpUsername);
            Add(SmtpPassword);
            Add(SendFromAddress);
            Add(Recipients);
            Load();
        }

        public override string Title => "Email settings";
        public TextSettingField SmtpServer { get; }
        public TextSettingField SmtpPort { get; }
        public TextSettingField SmtpUsername { get; }
        public TextSettingField SmtpPassword { get; }
        public TextSettingField SendFromAddress { get; }
        public ListSettingField Recipients { get; }
        public RelayCommand AddRecipientCommand => Recipients.AddCommand;

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
            SmtpServer.Fill(Session.Value("smtpServer") as string ?? string.Empty);
            SmtpPort.Fill(Session.Value("smtpPort") is int Port ? Port.ToString(CultureInfo.InvariantCulture) : string.Empty);
            SmtpUsername.Fill(Session.Value("smtpUsername") as string ?? string.Empty);
            SmtpPassword.Fill(Session.Value("smtpPassword") as string ?? string.Empty);
            SendFromAddress.Fill(Session.Value("sendFromAddress") as string ?? string.Empty);
            FillList(Recipients);
        }

        public void AddRecipient()
        {
            Recipients.AddRow();
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
    }
}
