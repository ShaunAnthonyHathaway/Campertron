using CampertronLibrary.function.Base;
using CampertronLibrary.function.WebAccess;
using System;

namespace CampertronLibrary.function.Forms
{
    // Web.yaml in Settings > Campertron and in the web UI's settings (decisions D81, D92 and v0.3.0's PR 5;
    // business-rules-settings §31, §32; business-rules-gui §17): the settings of Campertron's web UI (CampertronConsole --web):
    // whether a password is required, whether it listens on the network, the host names it answers to and the reverse
    // proxies it believes. Every rule of the settings editors applies, as for Ntfy.yaml: a missing Web.yaml is a new file
    // (the defaults: a password required, reachable from the network) that only Save creates, owner-only.
    //
    // The one confirmation (D81 point 4, WebAccessRule): a save that leaves the password off while the web UI listens on the
    // network, from a saved file that is not so, writes nothing and asks once; the confirmed save goes through. The saved
    // file is then the confirmed state: no later save asks, nothing reminds. Any change of a setting, a revert, a reload or
    // a conflict's resolution drops a confirmation that was asked for. The web UI's password itself is never in Web.yaml:
    // it is set and changed in the web UI only (the sign-in and the password are the web UI's own, D84).
    public sealed class WebSettingsViewModel : SettingsFileViewModel<WebConfig>
    {
        private string? _ConfirmationText;
        private bool _ConfirmNext;

        internal WebSettingsViewModel(ConfigEditSession<WebConfig> Session) : base(Session)
        {
            PasswordProtection = new BoolSettingField(WebAccessRule.PasswordKey, "_Require a password (passwordProtection)",
                "On (the default): the web UI asks for its password before anything else. Off: everyone who can reach the web UI can use it and change Campertron's settings, searches and notifications. The password itself is never in Web.yaml: it is set and changed in the web UI.");
            NetworkListening = new BoolSettingField(WebAccessRule.NetworkKey, "_Listen on the network (networkListening)",
                "On (the default): the web UI listens on every network interface of this computer, so other devices can reach it. Off: on this computer only (localhost). In a container leave it on and publish the port as -p 127.0.0.1:8790:8790 to keep the web UI to that computer: off would leave the published port unanswered.");
            AllowedHosts = new ListSettingField("allowedHosts", "_Allowed host names (allowedHosts)",
                "Names the web UI answers to besides every IP address, localhost and this computer's own name: a name of your home network, or the name a reverse proxy serves it under. A host name only: no port, no http://, no wildcard.",
                "host name", "A host name, such as campertron.example.com.", "One host name per row. Empty rows are ignored.");
            TrustedProxies = new ListSettingField("trustedProxies", "_Trusted reverse proxies (trustedProxies)",
                "The reverse proxies whose forwarded headers (X-Forwarded-For, X-Forwarded-Proto, X-Forwarded-Host) the web UI believes: an IP address, or a network such as 172.18.0.0/16. Without one no forwarded header is read. A proxy that serves HTTPS must be listed, and its name must be in allowedHosts.",
                "proxy", "An IP address, such as 172.18.0.2, or a network, such as 172.18.0.0/16.", "One proxy per row. Empty rows are ignored.");
            Add(PasswordProtection);
            Add(NetworkListening);
            Add(AllowedHosts);
            Add(TrustedProxies);
            ConfirmSaveCommand = new RelayCommand(ConfirmSave, () => HasConfirmation);
            CancelConfirmationCommand = new RelayCommand(CancelConfirmation, () => HasConfirmation);
            Load();
        }

        public override string Title => "Web UI settings";
        public override string PreviewNote => "Lines starting with - are replaced by the lines starting with +. Nothing else in the file changes, and no other file is written.";
        public BoolSettingField PasswordProtection { get; }
        public BoolSettingField NetworkListening { get; }
        public ListSettingField AllowedHosts { get; }
        public ListSettingField TrustedProxies { get; }
        public RelayCommand AddAllowedHostCommand => AllowedHosts.AddCommand;
        public RelayCommand AddTrustedProxyCommand => TrustedProxies.AddCommand;

        // a new Web.yaml: nothing is on disk yet (the session's IsNew)
        public bool IsNew => Session.IsNew;

        // what the file is and when a change applies (fixed text)
        public string Explanation => "Web.yaml holds the settings of Campertron's web UI (CampertronConsole --web). Web mode reads it when it starts: after you save, restart web mode or its container. Without the file the web UI uses the defaults shown here.";
        // which source decides (the command line first; business-rules-settings §31)
        public string Precedence => "The command line decides first: --listen sets where web mode listens, whatever networkListening says, and --port sets its port (8790 without it). passwordProtection, allowedHosts and trustedProxies are set only here.";

        // Whether the web UI that shows this form listens beyond this computer because of its command line (--listen):
        // then networkListening does not decide for it (WebAccessRule). Null in the desktop GUI and without --listen
        internal bool? ListensOnNetwork { get; set; }

        // the warning of a save that waits for the user's confirmation (WebAccessRule.Warning), or null
        public string? ConfirmationText
        {
            get => _ConfirmationText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ConfirmationText, value);
                this.RaisePropertyChanged(nameof(HasConfirmation));
                ConfirmSaveCommand?.Refresh();
                CancelConfirmationCommand?.Refresh();
            }
        }
        public bool HasConfirmation => ConfirmationText != null;
        public string ConfirmationHeading => "Save without a password?";
        public string ConfirmLabel => WebAccessRule.Confirmation;
        public string CancelConfirmationLabel => "Don't save";
        public RelayCommand ConfirmSaveCommand { get; }
        public RelayCommand CancelConfirmationCommand { get; }

        // the save the warning asked about, confirmed
        public void ConfirmSave()
        {
            if (!HasConfirmation)
            {
                return;
            }
            _ConfirmNext = true;
            try
            {
                Save();
            }
            finally
            {
                _ConfirmNext = false;
            }
        }

        // the user does not save: the edits stay, nothing was written
        public void CancelConfirmation()
        {
            if (!HasConfirmation)
            {
                return;
            }
            ConfirmationText = null;
            StatusText = "Nothing was saved. Your changes are still here.";
            StatusIsError = false;
        }

        private protected override ConfigEditResult? SaveSession()
        {
            WebAccessSaveResult Result = WebAccessRule.Save(Session, _ConfirmNext, ListensOnNetwork);
            if (Result.Outcome == WebAccessSaveOutcome.NeedsConfirmation)
            {
                ConfirmationText = Result.Message;
                return null;
            }
            ConfirmationText = null;
            return new ConfigEditResult(Result.Outcome == WebAccessSaveOutcome.Saved, Result.Message);
        }

        private protected override void OnEdited()
        {
            ConfirmationText = null;
        }

        protected override void Fill()
        {
            PasswordProtection.Fill(Session.Value(WebAccessRule.PasswordKey) as bool? ?? true);
            NetworkListening.Fill(Session.Value(WebAccessRule.NetworkKey) as bool? ?? true);
            FillList(AllowedHosts);
            FillList(TrustedProxies);
            this.RaisePropertyChanged(nameof(IsNew));
        }

        protected override void OnRefreshed()
        {
            this.RaisePropertyChanged(nameof(IsNew));
        }

        protected override void Apply(SettingField Field)
        {
            if (Session.State != ConfigEditState.Editable || Session.Conflict != null)
            {
                return;
            }
            if (Field is BoolSettingField Switch)
            {
                Session.Set(Field.Key, Switch.IsChecked);
            }
        }
    }
}
