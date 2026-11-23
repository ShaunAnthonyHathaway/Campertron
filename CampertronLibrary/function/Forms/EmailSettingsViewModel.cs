using CampertronLibrary.function.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // Email.yaml in the Settings tab (CM5, decision D43): the SMTP server, port, user name and password, the sender and
    // the recipients the Email output sends new results to. The password is masked unless the user reveals it for a
    // moment (hidden again after a save, a revert, a reload, a conflict resolution, leaving the tab or the window); it
    // never appears in a message, the preview or accessibility text. Nothing here connects to the server or sends, except
    // the test email (D86, D88: TestNotificationForm, on the user's action only, with the saved file).
    //
    // Since D70 the SMTP password file (smtpPasswordFile, D66) has its own field, recommended over the plaintext password:
    // only its path is edited; the file is never opened, CAMPERTRON_EMAIL_PASSWORD never read and no password copied into
    // Email.yaml (but by the test email, D88: at the moment the user asks for it, for that one email, never kept). A path the validation would refuse (empty, not absolute) is the field's own error in every output, so it
    // is never saved; clearing the field removes the key's line. The plaintext password is never changed by it.
    public sealed class EmailSettingsViewModel : SettingsFileViewModel<EmailConfig>
    {
        private const string RecipientsKey = "sendToAddressList";
        private const string PasswordFileKey = "smtpPasswordFile";
        private bool _IsPasswordRevealed;
        // General.yaml's outputTo is Email as the General editor shows it, and as General.yaml has it on disk (D71)
        private bool _OutputShown;
        private bool _OutputSaved;

        internal EmailSettingsViewModel(ConfigEditSession<EmailConfig> Session) : base(Session)
        {
            SmtpServer = new TextSettingField("smtpServer", "SMTP _server", "The name of your email provider's SMTP server, such as smtp.example.com.");
            SmtpPort = new TextSettingField("smtpPort", "SMTP _port", "The SMTP server's port, usually 587 or 465.");
            SmtpUsername = new TextSettingField("smtpUsername", "_User name", "The user name Campertron signs in with.");
            //D70: the password file's path, the recommended source; the file itself is never opened here
            SmtpPasswordFile = new TextSettingField(PasswordFileKey, "SMTP password fi_le (recommended)",
                "The full path of a file that holds only the SMTP password, such as /run/secrets/smtp_password (a Docker secret) or a file only you can read. Email mode reads it when it starts; this window opens it only to send a test email when you ask for one, and never shows the password. Clear the box to remove smtpPasswordFile from Email.yaml.");
            //the password field shows and edits smtpPassword only: a password from smtpPasswordFile or CAMPERTRON_EMAIL_PASSWORD
            //(D66) is never read here, never shown and never written into Email.yaml; the validation's notices say which applies
            SmtpPassword = new TextSettingField("smtpPassword", "Pass_word (plain text)",
                "The password stored as plain text in Email.yaml. Email mode uses it only when Email.yaml has no smtpPasswordFile and CAMPERTRON_EMAIL_PASSWORD is not set. Setting the password file does not change or clear it: clear it yourself once the password file works.",
                IsSecret: true);
            SendFromAddress = new TextSettingField("sendFromAddress", "_From address", "The address the results are sent from.");
            RemovePasswordFileCommand = new RelayCommand(RemovePasswordFile, () => CanRemovePasswordFile);
            //D71: an empty row is ignored (never written), the others are written without the spaces around them; email mode
            //needs at least one recipient, and an address that is not valid blocks Save in every output
            Recipients = new ListSettingField(RecipientsKey, "_Recipients (sendToAddressList)", "Email mode sends the new results to each of these addresses.",
                "recipient", "An email address the results are sent to.", "One email address per row. Empty rows are ignored.");
            Add(SmtpServer);
            Add(SmtpPort);
            Add(SmtpUsername);
            Add(SmtpPasswordFile);
            Add(SmtpPassword);
            Add(SendFromAddress);
            Add(Recipients);
            RemovePasswordCommand = new RelayCommand(RemovePassword, () => CanRemovePassword);
            Test = new TestNotificationForm(this, true, System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty);
            Load();
        }

        // the test email (D86, D88): its words, whether it can be sent now, its result
        public TestNotificationForm Test { get; }

        public override string Title => "Email settings";
        public TextSettingField SmtpServer { get; }
        public TextSettingField SmtpPort { get; }
        public TextSettingField SmtpUsername { get; }
        public TextSettingField SmtpPasswordFile { get; }
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

        // D70: where email mode takes the SMTP password from (SmtpPassword's order, business-rules-settings §27)
        public string PasswordSources =>
            "Email mode takes the SMTP password from the first of these that is set up, and never falls back to the next one when it can't be used:\n" +
            "1. The SMTP password file (smtpPasswordFile), recommended: the password stays out of Email.yaml.\n" +
            "2. The environment variable CAMPERTRON_EMAIL_PASSWORD, set where email mode runs, never in this window. Whoever can inspect that process, its service manager or its container (for example with docker inspect) may see it.\n" +
            "3. The plain-text password (smtpPassword), stored in Email.yaml.\n" +
            "This window reads the password from its source only to send a test email when you ask for one, and never shows it or copies it into Email.yaml.";

        // The plain-text password's state in words, for a UI that never shows it back (the web UI, D82: write-only, Replace
        // and Remove): whether Email.yaml has one, or what a save will do with it. Never the password or its length
        public string PasswordState
        {
            get
            {
                bool Saved = Session.OriginalValue("smtpPassword") is string Old && Old.Length > 0;
                if (!Session.IsChanged("smtpPassword"))
                {
                    return Saved ? "A plain-text password is set in Email.yaml. It is never shown here." : "No plain-text password is set in Email.yaml.";
                }
                return Session.Value("smtpPassword") is string New && New.Length > 0
                    ? "A new plain-text password was entered: Save writes it into Email.yaml. It is never shown here."
                    : "The plain-text password will be removed from Email.yaml when you save.";
            }
        }

        // the plain-text password can be removed (it is set, as saved or as entered): the same as clearing the box
        public bool CanRemovePassword => IsEditable && SmtpPassword.IsEditable && Session.Value("smtpPassword") is string Current && Current.Length > 0;
        public RelayCommand RemovePasswordCommand { get; }

        // removes the plain-text password (an edit, saved with Save): what clearing the password box does
        public void RemovePassword()
        {
            if (!CanRemovePassword)
            {
                return;
            }
            SmtpPassword.Text = string.Empty;
        }

        // smtpPasswordFile is in Email.yaml (also written without a value): Remove smtpPasswordFile takes its line out
        public bool CanRemovePasswordFile => IsEditable && SmtpPasswordFile.IsEditable && Session.InFile(PasswordFileKey) && !Session.IsRemoved(PasswordFileKey);
        public RelayCommand RemovePasswordFileCommand { get; }

        public string OutputExplanation => _OutputShown
            ? "outputTo is Email, so these settings are needed: problems block saving."
            : _OutputSaved
            ? "outputTo is Email in the saved General.yaml, so these settings are needed until you save another outputTo there: problems block saving."
            : "These settings are used only when outputTo is Email; until then a missing setting is a warning, but a value that is not valid still blocks saving.";

        // General.yaml's outputTo: Email as the General editor shows it (Shown) and as General.yaml has it on disk (Saved).
        // D71: either one makes what sending needs an error, so a save here never leaves an Email.yaml that the saved or the
        // shown outputTo: Email would refuse
        internal void SetEmailOutput(bool Shown, bool Saved)
        {
            _OutputShown = Shown;
            _OutputSaved = Saved;
            this.RaisePropertyChanged(nameof(OutputExplanation));
            if (Session.EmailOutput != (Shown || Saved))
            {
                Session.EmailOutput = Shown || Saved;
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
            SmtpPasswordFile.Fill(Session.Value(PasswordFileKey) as string ?? string.Empty);
            SmtpPassword.Fill(Session.Value("smtpPassword") as string ?? string.Empty);
            SendFromAddress.Fill(Session.Value("sendFromAddress") as string ?? string.Empty);
            FillList(Recipients);
        }

        public void AddRecipient()
        {
            Recipients.AddRow();
        }

        // removes smtpPasswordFile's line from Email.yaml (an edit, saved with Save); smtpPassword is not touched
        public void RemovePasswordFile()
        {
            if (!CanRemovePasswordFile)
            {
                return;
            }
            StatusText = null;
            SmtpPasswordFile.InputError = null;
            Session.Remove(PasswordFileKey);
            SmtpPasswordFile.Fill(string.Empty);
            Refresh();
        }

        protected override void OnRefreshed()
        {
            // D75: the password file's metadata (never its contents), an error while outputTo Email is shown or saved
            if (!Session.IsRemoved(PasswordFileKey))
            {
                SecretFileMessages.Show(SmtpPasswordFile, Session.Value(PasswordFileKey) as string, "password", _OutputShown || _OutputSaved);
            }
            this.RaisePropertyChanged(nameof(CanRemovePasswordFile));
            RemovePasswordFileCommand.Refresh();
            this.RaisePropertyChanged(nameof(PasswordState));
            this.RaisePropertyChanged(nameof(CanRemovePassword));
            RemovePasswordCommand?.Refresh();
            Test?.Refresh();
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
            if (Field.Key == PasswordFileKey)
            {
                ApplyPasswordFile(Text, Shown);
                return;
            }
            if (Text.Text == Shown)
            {
                Session.Revert(Field.Key);
            }
            else
            {
                Session.Set(Field.Key, Text.Text);
            }
        }

        // D70: the path as the file has it is no edit; an empty box removes smtpPasswordFile's line (never an empty source);
        // a path the validation would refuse is the field's own error and never reaches the session, whatever the output
        // (ConfigValidation.PasswordFilePathProblem: only the text is judged, the file is never opened)
        private void ApplyPasswordFile(TextSettingField Text, string Shown)
        {
            if (Text.Text == Shown)
            {
                Text.InputError = null;
                Session.Revert(PasswordFileKey);
            }
            else if (Text.Text.Length == 0)
            {
                Text.InputError = null;
                Session.Set(PasswordFileKey, null);
            }
            else if (ConfigValidation.PasswordFilePathProblem(Text.Text) is (ConfigCode _, string Problem))
            {
                Text.InputError = Problem;
                Session.Revert(PasswordFileKey);
            }
            else
            {
                Text.InputError = null;
                Session.Set(PasswordFileKey, Text.Text);
            }
        }
    }
}
