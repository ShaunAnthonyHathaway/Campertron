using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Campertron.ViewModels
{
    // Ntfy.yaml in Settings > Campertron (decision D69, business-rules §17 and §28): whether ntfy is on, the server, the
    // topic, the access token's file, the priority, the tags and the timeout of the unattended monitoring's ntfy
    // notifications. A missing Ntfy.yaml is a new file: the form shows the defaults (ntfy off) and nothing is written until
    // the user changes a setting and saves, which creates it. The topic is shown only in its own box here and in the
    // preview of a save; no message, status or notice holds it. The access token is never read: tokenFile is only a path,
    // the token file is never opened and CAMPERTRON_NTFY_TOKEN is never read. Nothing here connects to a server or publishes.
    public sealed class NtfySettingsViewModel : SettingsFileViewModel<NtfyConfig>
    {
        private const string TagsKey = "tags";
        private const string TokenFileKey = "tokenFile";
        private OutputType? _Output;

        private static readonly SettingChoice[] OfferedPriorities =
        {
            new SettingChoice(NtfyPriority.Min, "Min: no sound or vibration"),
            new SettingChoice(NtfyPriority.Low, "Low: no sound, shown in the list"),
            new SettingChoice(NtfyPriority.Default, "Default: the phone's usual notification"),
            new SettingChoice(NtfyPriority.High, "High: more noticeable"),
            new SettingChoice(NtfyPriority.Max, "Max: as noticeable as possible"),
        };

        internal NtfySettingsViewModel(ConfigEditSession<NtfyConfig> Session) : base(Session)
        {
            Enabled = new BoolSettingField("enabled", "_Enabled: send new openings with ntfy",
                "On: the unattended monitoring (outputTo: Ntfy, or outputTo: Email) also sends each search's new openings to your ntfy topic. Off: it sends nothing to ntfy; the other settings below are kept.");
            Server = new TextSettingField("server", "Ser_ver",
                "The ntfy server's address only: https://ntfy.sh or your own server, such as https://ntfy.example.net:8443, without a path, topic, user name or password. Use https: with http the topic, the token and every message cross the network unencrypted.");
            Topic = new TextSettingField("topic", "_Topic",
                "The topic Campertron publishes to and you subscribe to in the ntfy app: letters, digits, - and _, up to 64 characters. On a server without access control the topic works like a password: anyone who knows it can read your notifications, so choose a long random one. It is shown only here, never in a message, the current configuration or a log.");
            TokenFile = new TextSettingField(TokenFileKey, "Access token f_ile (optional)",
                "The full path of a file that holds only an ntfy access token, such as /run/secrets/ntfy_token, for a server or topic that needs one. The monitoring reads the file when it starts; this window never opens it or shows the token, and Ntfy.yaml never holds the token. Without tokenFile the monitoring uses the environment variable CAMPERTRON_NTFY_TOKEN if it is set where it runs (set it there; this window never reads it), else no token.");
            Priority = new ChoiceSettingField("priority", "_Priority", "How noticeable the notification is on your phone or desktop.");
            Tags = new ListSettingField(TagsKey, "T_ags (optional)",
                "ntfy tags, each 1 to 64 letters, digits, _, + or -. A tag that is an emoji short code, such as tent, shows as that emoji. Without the tags setting Campertron sends tent.",
                "tag", "An ntfy tag, such as tent.");
            TimeoutSeconds = new TextSettingField("timeoutSeconds", "Ti_meout (seconds)", "How long one notification may take, from 1 to 120 seconds; 30 when it is not set.");
            Add(Enabled);
            Add(Server);
            Add(Topic);
            Add(TokenFile);
            Add(Priority);
            Add(Tags);
            Add(TimeoutSeconds);
            RemoveTokenFileCommand = new RelayCommand(RemoveTokenFile, () => CanRemoveTokenFile);
            ShowGeneralCommand = new RelayCommand(() => ShowGeneral?.Invoke());
            Load();
        }

        public override string Title => "ntfy settings";
        public override string PreviewNote => "Lines starting with - are replaced by the lines starting with +. Nothing else in the file changes, and no other file is written.";
        public BoolSettingField Enabled { get; }
        public TextSettingField Server { get; }
        public TextSettingField Topic { get; }
        public TextSettingField TokenFile { get; }
        public ChoiceSettingField Priority { get; }
        public ListSettingField Tags { get; }
        public TextSettingField TimeoutSeconds { get; }
        public RelayCommand AddTagCommand => Tags.AddCommand;

        // a new Ntfy.yaml: nothing is on disk yet (the session's IsNew)
        public bool IsNew => Session.IsNew;

        // tokenFile is written in the file (also empty): it can be removed, so the monitoring uses CAMPERTRON_NTFY_TOKEN or
        // no token instead
        public bool CanRemoveTokenFile => IsEditable && TokenFile.IsEditable && Session.InFile(TokenFileKey) && !Session.IsRemoved(TokenFileKey);
        public RelayCommand RemoveTokenFileCommand { get; }

        // Settings > Campertron sets it to show General.yaml, where outputTo is chosen; this editor never changes outputTo
        internal Action? ShowGeneral { get; set; }
        public RelayCommand ShowGeneralCommand { get; }

        // what ntfy does in the unattended monitoring for the outputTo the General editor shows and the form's enabled
        public string ModeText
        {
            get
            {
                bool On = Enabled.IsChecked;
                return _Output switch
                {
                    OutputType.Ntfy => On
                        ? "outputTo is Ntfy: the unattended monitoring sends new openings with ntfy only. ntfy is required, so its problems block saving."
                        : "outputTo is Ntfy, which needs ntfy: turn Enabled on, or choose another outputTo in General.yaml. ntfy is required, so its problems block saving.",
                    OutputType.Email => On
                        ? "outputTo is Email: the unattended monitoring sends emails and, while Enabled is on, ntfy notifications too."
                        : "outputTo is Email: the unattended monitoring sends emails only, because Enabled is off.",
                    _ => "outputTo is " + (_Output?.ToString() ?? "not known") + ": nothing is sent. ntfy is used only by the unattended monitoring, with outputTo: Ntfy (ntfy only) or outputTo: Email (email, and ntfy while Enabled is on).",
                };
            }
        }

        // fixed text: where ntfy applies and when a change takes effect
        public string ModeHelp => "The console's Search, HtmlFile and this window's Results tab need no notification provider. The unattended monitoring reads Ntfy.yaml once, when it starts: after you save, restart it or its container. This editor never changes outputTo; choose it in General.yaml.";

        // General.yaml's outputTo as the General editor shows it (with Ntfy, ntfy is required)
        internal void SetOutput(OutputType? Output)
        {
            _Output = Output;
            if (Session.NtfyRequired != (Output == OutputType.Ntfy))
            {
                Session.NtfyRequired = Output == OutputType.Ntfy;
                Refresh();
            }
            this.RaisePropertyChanged(nameof(ModeText));
        }

        protected override void Fill()
        {
            Enabled.Fill(Session.Value("enabled") is true);
            Server.Fill(Session.Value("server") as string ?? string.Empty);
            Topic.Fill(Session.Value("topic") as string ?? string.Empty);
            TokenFile.Fill(Session.Value(TokenFileKey) as string ?? string.Empty);
            NtfyPriority? Current = Session.Value("priority") as NtfyPriority?;
            List<SettingChoice> Choices = OfferedPriorities.ToList();
            SettingChoice? Selected = Choices.FirstOrDefault(C => Current != null && C.Value.Equals(Current.Value));
            if (Current != null && Selected == null)
            {
                // a number that is no ntfy priority: kept as the file has it until the user chooses one
                Selected = new SettingChoice(Current.Value, "Not a valid choice (kept as the file has it)");
                Choices.Add(Selected);
            }
            Priority.Fill(Choices, Selected);
            FillList(Tags);
            TimeoutSeconds.Fill(Session.Value("timeoutSeconds") is int Seconds ? Seconds.ToString(CultureInfo.InvariantCulture) : string.Empty);
            this.RaisePropertyChanged(nameof(ModeText));
            this.RaisePropertyChanged(nameof(IsNew));
        }

        public void AddTag()
        {
            Tags.AddRow();
        }

        // removes tokenFile's line from Ntfy.yaml (an edit, saved with Save)
        public void RemoveTokenFile()
        {
            if (!CanRemoveTokenFile)
            {
                return;
            }
            StatusText = null;
            TokenFile.InputError = null;
            Session.Remove(TokenFileKey);
            TokenFile.Fill(string.Empty);
            Refresh();
        }

        protected override void OnRefreshed()
        {
            this.RaisePropertyChanged(nameof(CanRemoveTokenFile));
            this.RaisePropertyChanged(nameof(IsNew));
            RemoveTokenFileCommand.Refresh();
        }

        protected override void Apply(SettingField Field)
        {
            if (Session.State != ConfigEditState.Editable || Session.Conflict != null)
            {
                return;
            }
            switch (Field.Key)
            {
                case "enabled":
                    Session.Set("enabled", Enabled.IsChecked);
                    this.RaisePropertyChanged(nameof(ModeText));
                    return;
                case "priority":
                    if (Priority.Selected != null)
                    {
                        Session.Set("priority", (NtfyPriority)Priority.Selected.Value);
                    }
                    return;
                case "timeoutSeconds":
                    // the supported range is enforced here too (D69): the validation alone reports it only as a warning
                    // while ntfy is off, and the GUI never writes a timeout the monitoring would refuse once it is on
                    if (int.TryParse(TimeoutSeconds.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int Seconds)
                        && Seconds >= NtfyEndpoint.MinTimeoutSeconds && Seconds <= NtfyEndpoint.MaxTimeoutSeconds)
                    {
                        TimeoutSeconds.InputError = null;
                        Session.Set("timeoutSeconds", Seconds);
                    }
                    else
                    {
                        TimeoutSeconds.InputError = $"Enter a whole number of seconds from {NtfyEndpoint.MinTimeoutSeconds} to {NtfyEndpoint.MaxTimeoutSeconds}, such as 30.";
                        Session.Revert("timeoutSeconds");
                    }
                    return;
            }
            if (Field is not TextSettingField Text)
            {
                return;
            }
            // an empty box is the file's empty or missing value; anything else is the text as entered. An emptied
            // tokenFile removes its line (never an empty token source)
            string Shown = Session.OriginalValue(Field.Key) as string ?? string.Empty;
            if (Text.Text == Shown)
            {
                Session.Revert(Field.Key);
            }
            else if (Field.Key == TokenFileKey && Text.Text.Length == 0)
            {
                Session.Set(TokenFileKey, null);
            }
            else
            {
                Session.Set(Field.Key, Text.Text);
            }
        }
    }
}
