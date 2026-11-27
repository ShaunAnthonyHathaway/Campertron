using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using W = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronLibrary.function.Interactive.Settings
{
    // The interactive console's Settings (v0.4.0's phase 7a, D123; business-rules §43): General.yaml and Web.yaml, each in
    // the shared form the desktop GUI's Settings > Campertron and the web UI's settings pages show (SettingsViewModel's
    // General and Web forms, ER-37), hosted by InteractiveSettingsForm as Notifications hosts Email.yaml and Ntfy.yaml.
    //
    // General.yaml's form judges outputTo by the monitoring start's own checks of the saved Email.yaml, Ntfy.yaml and search
    // files (D71, D73, D77) and says ntfy's state, so a saved outputTo of Email or Ntfy is one the monitoring would start
    // with; nothing here starts, stops or restarts it, and nothing sends. Web.yaml's form has the web UI's settings (D81,
    // D92: the password's requirement, the network, the host names, the trusted proxies) and its one confirmation; the web
    // UI's password, its record (Web.password), the setup code, the sessions and the keys are the web UI's own (D84) and
    // nothing here reads, shows or writes them. Saving Web.yaml starts no web host and changes no running one: web mode
    // reads it when it starts.
    //
    // Entering it reads and changes nothing but what it shows: a summary in the console's words (whether each file exists;
    // General.yaml's outputTo and, for Email or Ntfy, whether the monitoring's start would accept the saved settings, by the
    // start's own checks, GuidedSetupStatus; Web.yaml's two switches as saved, which hold no secret). The menu's loop is
    // InteractiveSettingsMenu's, the Notifications menu's too.
    internal static class InteractiveSettings
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.SettingsTitle, W.Settings, W.BackToSettings,
            W.SettingsNoFolder, W.SettingsFolderMissing, W.SettingsContainerFolderMissing);

        private static readonly IReadOnlyList<SettingsMenuFile> Files = Array.AsReadOnly(new[]
        {
            new SettingsMenuFile(W.General, Forms => Forms.General),
            new SettingsMenuFile(W.Web, Forms => Forms.Web)
        });

        // the main menu's Settings
        internal static InteractiveStep Run(InteractiveSession Session) => InteractiveSettingsMenu.Run(Session, Words, Files, Summary, W.SettingsNote);

        // The summary: General.yaml (missing, can't be used, or its saved outputTo and, for Email or Ntfy, whether the
        // monitoring's start would accept the saved settings: GuidedSetupStatus.FilesReady, the start's own checks, which read
        // no secret) and Web.yaml (missing, can't be used, or its saved passwordProtection and networkListening)
        internal static List<(string Label, string Value)> Summary(string Settings, DateTime Now)
        {
            var Forms = new SettingsViewModel(Settings);
            var Facts = new List<(string, string)>();
            GeneralSettingsViewModel General = Forms.General;
            string GeneralFact;
            if (General.IsMissing)
            {
                GeneralFact = W.GeneralMissing;
            }
            else if (!General.IsEditable && !General.HasConflict)
            {
                GeneralFact = W.CantBeEdited;
            }
            else
            {
                GuidedSetupStatus Status = GuidedSetupStatus.Read(Settings, Now);
                OutputType? Output = Status.SavedOutput;
                GeneralFact = Output switch
                {
                    null => W.CantBeEdited,
                    OutputType.Email => Status.FilesReady(GuidedSetupMethod.Email) ? W.MonitoringAccepts(Output.Value.ToString()) : W.MonitoringRefuses(Output.Value.ToString()),
                    OutputType.Ntfy => Status.FilesReady(GuidedSetupMethod.Ntfy) ? W.MonitoringAccepts(Output.Value.ToString()) : W.MonitoringRefuses(Output.Value.ToString()),
                    _ => W.GeneralFound(Output.Value.ToString())
                };
            }
            Facts.Add((ConfigFiles.GeneralFileName, GeneralFact));
            WebSettingsViewModel Web = Forms.Web;
            Facts.Add((ConfigFiles.WebFileName, Web.IsNew ? W.WebMissing
                : !Web.IsEditable && !Web.HasConflict ? W.CantBeEdited
                : W.WebFound(Web.PasswordProtection.IsChecked, Web.NetworkListening.IsChecked)));
            return Facts;
        }
    }
}
