using CampertronLibrary.function.Base;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CampertronLibrary.function.Forms
{
    // General.yaml in the Settings tab (CM5, decision D43): where results go (outputTo), the automatic refresh of the
    // campground data (autoRefresh, refreshRidbDataDayInterval) and when it last ran (lastRidbDataRefresh). Editing writes
    // values only: nothing here refreshes, downloads or searches. outputTo: UnitTest (Campertron's tests) is kept when the
    // file has it, but not offered.
    public sealed class GeneralSettingsViewModel : SettingsFileViewModel<GeneralConfig>
    {
        // the date and time forms accepted for lastRidbDataRefresh (UTC unless an offset is given)
        private static readonly string[] DateFormats =
        {
            "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ssK", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ssK",
        };

        private static readonly SettingChoice[] OfferedOutputs =
        {
            new SettingChoice(OutputType.Console, "Console: show results in the console"),
            new SettingChoice(OutputType.Email, "Email: check every minute and send new results by email (and with ntfy when Ntfy.yaml enables it)"),
            new SettingChoice(OutputType.HtmlFile, "HtmlFile: write and open a results page"),
            new SettingChoice(OutputType.Ntfy, "Ntfy: check every minute and send new results with ntfy only (Ntfy.yaml)"),
        };

        internal GeneralSettingsViewModel(ConfigEditSession<GeneralConfig> Session) : base(Session)
        {
            OutputTo = new ChoiceSettingField("outputTo", "_Output", "Where the console's search results go.");
            AutoRefresh = new BoolSettingField("autoRefresh", "_Refresh the campground data automatically", "Downloads recreation.gov's campground data again at a start when the interval has passed.");
            RefreshInterval = new TextSettingField("refreshRidbDataDayInterval", "Refresh _interval (days)", "Days between automatic refreshes; used only when the automatic refresh is on.");
            LastRefresh = new TextSettingField("lastRidbDataRefresh", "_Last refresh (UTC)", "When the campground data was last refreshed, as a UTC date and time such as 2026-01-31T00:00:00Z. Campertron sets it after a refresh.");
            Add(OutputTo);
            Add(AutoRefresh);
            Add(RefreshInterval);
            Add(LastRefresh);
            Load();
        }

        public override string Title => "General settings";
        public ChoiceSettingField OutputTo { get; }
        public BoolSettingField AutoRefresh { get; }
        public TextSettingField RefreshInterval { get; }
        public TextSettingField LastRefresh { get; }

        // the outputTo the form shows (edited or as the file has it); Email.yaml's checks follow it
        public bool IsEmailOutput => Session.Value("outputTo") is OutputType.Email;
        // the outputTo the form shows, as a value (null without a readable General.yaml)
        internal OutputType? OutputToValue => Session.Value("outputTo") as OutputType?;
        // the outputTo General.yaml has on disk (null without a readable General.yaml): Email.yaml's and Ntfy.yaml's checks
        // follow it too, until another outputTo is saved (D71)
        internal OutputType? SavedOutputToValue => Session.OriginalValue("outputTo") as OutputType?;

        // D71: what the unattended monitoring's start would refuse in Email.yaml and Ntfy.yaml, as they are saved, for the
        // outputTo the form shows (ConfigValidation.MonitoringStartErrors, the start's own checks). A changed outputTo is not
        // saved while there is one: the message at outputTo says which file and setting to fix first. An outputTo the file
        // already has gets the same message as a warning (saving another setting doesn't make it worse)
        private List<ConfigDiagnostic> _StartErrors = new List<ConfigDiagnostic>();
        // D73: what the same start would refuse in the search files (ConfigValidation.MonitoringSearchErrors, the start's own
        // check of D64: no search to check, or a search file it stops on), judged the same way: a changed outputTo is not
        // saved while there is one, and an outputTo the file already has gets it as a warning (also when the GUI opens)
        private List<ConfigDiagnostic> _SearchErrors = new List<ConfigDiagnostic>();
        // the fix after v0.2.0 step 3's local test (§BU): the secret files the providers of the outputTo the form shows would
        // read (Email.yaml's smtpPasswordFile, Ntfy.yaml's tokenFile while ntfy is on), judged from their metadata as at their
        // own fields (D75): a problem is an error, a file others can read a warning, for a changed outputTo and a saved one
        // alike. Never blocks Save (the maintainer's decision, D75); the files are never opened
        private List<(string Text, bool IsProblem)> _SecretFiles = new List<(string Text, bool IsProblem)>();

        protected override void Recheck()
        {
            OutputType? Output = OutputToValue;
            bool Monitoring = Session.State == ConfigEditState.Editable && (Output == OutputType.Email || Output == OutputType.Ntfy);
            string Folder = System.IO.Path.GetDirectoryName(this.FilePath) ?? string.Empty;
            _StartErrors = Monitoring ? ConfigValidation.MonitoringStartErrors(Folder, Output, DateTime.Now) : new List<ConfigDiagnostic>();
            _SearchErrors = Monitoring ? ConfigValidation.MonitoringSearchErrors(Folder, Output, null, SearchPlace) : new List<ConfigDiagnostic>();
            _SecretFiles = Monitoring ? SecretFileMessages.ForMonitoring(Folder, Output) : new List<(string Text, bool IsProblem)>();
        }

        // where the host's user edits the search files, in its words (the Searches tab; the interactive console's Searches menu,
        // PT1): only the words of the message at outputTo change, never what it decides
        internal SearchPlace SearchPlace { get; private set; } = SearchPlace.Tab;
        internal void UseSearchPlace(SearchPlace Place)
        {
            SearchPlace = Place;
            Refresh();
        }

        protected override bool HasOtherInputErrors => (_StartErrors.Count > 0 || _SearchErrors.Count > 0) && Session.IsChanged("outputTo");

        // whether the message at outputTo says the monitoring has no search to check, or a search file it stops on (D73)
        internal bool HasMonitoringSearchProblem => _SearchErrors.Count > 0;

        protected override void OnRefreshed()
        {
            if (_StartErrors.Count == 0 && _SearchErrors.Count == 0 && _SecretFiles.Count == 0)
            {
                return;
            }
            bool Changed = Session.IsChanged("outputTo");
            var Parts = new List<string>();
            if (_StartErrors.Count > 0)
            {
                string Files = string.Join(" and ", _StartErrors.Select(D => D.FileName).Distinct());
                string Problems = string.Join(" ", _StartErrors.Select(D => D.FileName + ": " + D.Message).Distinct());
                Parts.Add(Changed
                    ? "With outputTo: " + OutputToValue + ", Campertron would refuse to start because of " + Files + ". " + Problems + " Fix and save " + Files + " first, then save General.yaml."
                    : "With outputTo: " + OutputToValue + ", Campertron refuses to start because of " + Files + " as saved. " + Problems + " Fix and save " + Files + ".");
            }
            if (_SearchErrors.Count > 0)
            {
                string Problems = string.Join(" ", _SearchErrors.Select(D => D.Message).Distinct());
                string Lead = _StartErrors.Count > 0 ? "It also needs a search to check: "
                    : Changed ? "With outputTo: " + OutputToValue + ", Campertron would refuse to start: "
                    : "With outputTo: " + OutputToValue + ", Campertron refuses to start: ";
                Parts.Add(Lead + Problems + (Changed ? " Then save General.yaml." : string.Empty));
            }
            Parts.AddRange(_SecretFiles.Select(S => S.Text));
            bool StartProblem = _StartErrors.Count > 0 || _SearchErrors.Count > 0;
            bool Error = OutputTo.IsError || (StartProblem && Changed) || _SecretFiles.Any(S => S.IsProblem);
            string Own = OutputTo.Message is string Message ? Message + " " : string.Empty;
            OutputTo.SetMessage(Own + string.Join(" ", Parts), Error ? "Error" : "Warning");
        }


        // Email.yaml or Ntfy.yaml was saved, reloaded or changed on disk: the check of outputTo and the ntfy line read them again
        internal void RefreshMonitoringCheck()
        {
            Refresh();
            RaiseNtfyStatus();
        }

        private void RaiseNtfyStatus()
        {
            this.RaisePropertyChanged(nameof(NtfyStatus));
            this.RaisePropertyChanged(nameof(NtfyStatusSeverity));
        }

        // ntfy's state for the outputTo the form shows (D67): read-only, from Ntfy.yaml's validation in the same settings
        // folder (the saved file), in its fixed words. Since D69 Ntfy.yaml has its own editor in this tab; this form never
        // writes it, never reads ntfy's token and never shows the server, the topic or a path. Since the fix after v0.2.0 step
        // 3's local test (§BU), while ntfy is on it also says what is wrong with the token file (D75's words: the setting and
        // the file's name), an error while ntfy is in use (outputTo Email or Ntfy, shown or saved), else a warning; and the line
        // is styled by its severity (NtfyStatusSeverity, D74)
        public string NtfyStatus => NtfyState().Text;
        public string NtfyStatusSeverity => NtfyState().Severity;

        private (string Text, string Severity) NtfyState()
        {
            bool Required = OutputToValue == OutputType.Ntfy;
            string Folder = System.IO.Path.GetDirectoryName(this.FilePath) ?? string.Empty;
            List<ConfigDiagnostic>? Found = ConfigValidation.ValidateNtfyFile(Folder, DateTime.Now, Required ? OutputType.Ntfy : null);
            if (Found == null)
            {
                return Required
                    ? ("Ntfy.yaml does not exist, and outputTo: Ntfy needs it: choose Ntfy.yaml above, turn Enabled on, enter the server and the topic, and save.", "Error")
                    : ("Ntfy.yaml does not exist, so ntfy is off. To add ntfy notifications, choose Ntfy.yaml above.", "Info");
            }
            List<ConfigDiagnostic> Problems = Found.Where(D => D.Severity == ConfigSeverity.Error || D.Severity == ConfigSeverity.Warning).ToList();
            if (Problems.Count > 0)
            {
                return ("Ntfy.yaml: " + Problems[0].Severity + ": " + Problems[0].Message + (Problems.Count > 1 ? " (" + (Problems.Count - 1).ToString(CultureInfo.InvariantCulture) + " more)" : string.Empty),
                    Problems[0].Severity.ToString());
            }
            if (Found.Any(D => D.Code == ConfigCode.NtfyDisabled))
            {
                return ("Ntfy.yaml: ntfy is off (enabled: false).", "Info");
            }
            bool InUse = OutputToValue is OutputType.Email or OutputType.Ntfy || SavedOutputToValue is OutputType.Email or OutputType.Ntfy;
            if (SecretFileMessages.ForNtfyLine(Folder, InUse) is (string Token, string Severity))
            {
                return ("Ntfy.yaml: " + Severity + ": " + Token, Severity);
            }
            return ("Ntfy.yaml: ntfy is on. With outputTo Email or Ntfy, Campertron also publishes new results to its topic (not shown here).", "Info");
        }

        // how the automatic refresh and its interval work together, in words
        public string RefreshExplanation => AutoRefresh.IsChecked
            ? "At each start, Campertron refreshes the campground data when the interval has passed since the last refresh."
            : "The campground data is not refreshed automatically; the interval is not used. Refresh it from the console's menu.";

        protected override void Fill()
        {
            OutputType? Current = Session.Value("outputTo") as OutputType?;
            List<SettingChoice> Choices = OfferedOutputs.ToList();
            SettingChoice? Selected = Choices.FirstOrDefault(C => Current != null && C.Value.Equals(Current.Value));
            if (Current != null && Selected == null)
            {
                // outputTo: UnitTest (or a number that is no choice): kept as the file has it, never offered as a choice
                Selected = new SettingChoice(Current.Value, Enum.IsDefined(Current.Value)
                    ? Current.Value + ": for Campertron's tests only (kept as the file has it)"
                    : "Not a valid choice (kept as the file has it)");
                Choices.Add(Selected);
            }
            OutputTo.Fill(Choices, Selected);
            AutoRefresh.Fill(Session.Value("autoRefresh") is true);
            RefreshInterval.Fill(Session.Value("refreshRidbDataDayInterval") is double Days ? Days.ToString("R", CultureInfo.InvariantCulture) : string.Empty);
            LastRefresh.Fill(Session.Value("lastRidbDataRefresh") is DateTime When ? When.ToString("o", CultureInfo.InvariantCulture) : string.Empty);
            this.RaisePropertyChanged(nameof(IsEmailOutput));
            this.RaisePropertyChanged(nameof(RefreshExplanation));
            RaiseNtfyStatus();
        }

        protected override void Apply(SettingField Field)
        {
            if (!Session.Keys.Contains(Field.Key) || Session.State != ConfigEditState.Editable)
            {
                return;
            }
            switch (Field.Key)
            {
                case "outputTo":
                    if (OutputTo.Selected != null)
                    {
                        Session.Set("outputTo", (OutputType)OutputTo.Selected.Value);
                    }
                    this.RaisePropertyChanged(nameof(IsEmailOutput));
                    RaiseNtfyStatus();
                    break;
                case "autoRefresh":
                    Session.Set("autoRefresh", AutoRefresh.IsChecked);
                    this.RaisePropertyChanged(nameof(RefreshExplanation));
                    break;
                case "refreshRidbDataDayInterval":
                    if (double.TryParse(RefreshInterval.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double Days) && double.IsFinite(Days))
                    {
                        RefreshInterval.InputError = null;
                        Session.Set("refreshRidbDataDayInterval", Days);
                    }
                    else
                    {
                        RefreshInterval.InputError = "Enter a number of days, such as 30 or 7.5.";
                        Session.Revert("refreshRidbDataDayInterval");
                    }
                    break;
                case "lastRidbDataRefresh":
                    string Text = LastRefresh.Text.Trim();
                    if (Text.Length == 0 && Session.OriginalValue("lastRidbDataRefresh") == null)
                    {
                        LastRefresh.InputError = null;
                        Session.Revert("lastRidbDataRefresh");
                    }
                    else if (DateTime.TryParseExact(Text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime When))
                    {
                        LastRefresh.InputError = null;
                        Session.Set("lastRidbDataRefresh", When);
                    }
                    else
                    {
                        LastRefresh.InputError = "Enter a UTC date and time such as 2026-01-31T00:00:00Z.";
                        Session.Revert("lastRidbDataRefresh");
                    }
                    break;
            }
        }
    }
}
