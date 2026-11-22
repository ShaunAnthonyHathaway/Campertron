using CampertronLibrary.function.Base;
using ReactiveUI.Reactive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Campertron.ViewModels
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

        // ntfy's state for the outputTo the form shows (D67): read-only, from Ntfy.yaml's validation in the same settings
        // folder (the saved file), in its fixed words. Since D69 Ntfy.yaml has its own editor in this tab; this form never
        // writes it, never reads ntfy's token and never shows the server, the topic or a path
        public string NtfyStatus
        {
            get
            {
                bool Required = OutputToValue == OutputType.Ntfy;
                List<ConfigDiagnostic>? Found = ConfigValidation.ValidateNtfyFile(System.IO.Path.GetDirectoryName(this.FilePath) ?? string.Empty, DateTime.Now, Required ? OutputType.Ntfy : null);
                if (Found == null)
                {
                    return Required
                        ? "Ntfy.yaml does not exist, and outputTo: Ntfy needs it: choose Ntfy.yaml above, turn Enabled on, enter the server and the topic, and save."
                        : "Ntfy.yaml does not exist, so ntfy is off. To add ntfy notifications, choose Ntfy.yaml above.";
                }
                List<ConfigDiagnostic> Problems = Found.Where(D => D.Severity == ConfigSeverity.Error || D.Severity == ConfigSeverity.Warning).ToList();
                if (Problems.Count > 0)
                {
                    return "Ntfy.yaml: " + Problems[0].Severity + ": " + Problems[0].Message + (Problems.Count > 1 ? " (" + (Problems.Count - 1).ToString(CultureInfo.InvariantCulture) + " more)" : string.Empty);
                }
                if (Found.Any(D => D.Code == ConfigCode.NtfyDisabled))
                {
                    return "Ntfy.yaml: ntfy is off (enabled: false).";
                }
                return "Ntfy.yaml: ntfy is on. With outputTo Email or Ntfy, Campertron also publishes new results to its topic (not shown here).";
            }
        }

        // Ntfy.yaml was saved, reloaded or changed on disk (D69): the line above reads it again
        internal void RefreshNtfyStatus()
        {
            this.RaisePropertyChanged(nameof(NtfyStatus));
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
            this.RaisePropertyChanged(nameof(NtfyStatus));
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
                    this.RaisePropertyChanged(nameof(NtfyStatus));
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
