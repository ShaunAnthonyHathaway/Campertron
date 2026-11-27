using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using W = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronLibrary.function.Interactive.Settings
{
    // The interactive console's Notifications (v0.4.0's phase 7, D122; business-rules §43): Email.yaml and Ntfy.yaml, each
    // in the shared form both other UIs show (SettingsViewModel's Email and Ntfy forms, the layer's composition: their
    // checks follow General.yaml's outputTo as it is saved, D71), hosted by InteractiveSettingsForm, with the test email,
    // the test notification and the random topic of those forms. General.yaml and Web.yaml are the Settings menu's
    // (InteractiveSettings, phase 7a, D123).
    //
    // Entering it reads and changes nothing but what it shows: a short summary of the two files (whether each exists and
    // whether the monitoring's start would accept it as it is saved: GuidedSetupStatus, the start's own checks, which read
    // no secret, open no secret file and read no environment variable) and General.yaml's outputTo. Nothing is created,
    // saved or sent until the user chooses it in a form and confirms it. A form lasts while it is shown: leaving it keeps
    // only what was saved. The settings folder must exist: the console never creates it (a start without arguments, the
    // desktop app or web mode does). The menu's loop is InteractiveSettingsMenu's, the Settings menu's too.
    internal static class InteractiveNotifications
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.NotificationsTitle, W.Notifications, W.BackToNotifications,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        private static readonly IReadOnlyList<SettingsMenuFile> Files = Array.AsReadOnly(new[]
        {
            new SettingsMenuFile(W.Email, Forms => Forms.Email),
            new SettingsMenuFile(W.Ntfy, Forms => Forms.Ntfy)
        });

        // the main menu's Notifications
        internal static InteractiveStep Run(InteractiveSession Session) => InteractiveSettingsMenu.Run(Session, Words, Files, Summary, W.OutputNote);

        // The summary: for Email.yaml and Ntfy.yaml whether each exists and, read as the monitoring's start reads it,
        // whether that start would accept it (GuidedSetupStatus.NotificationsReady, the start's own checks); for Ntfy.yaml
        // whether ntfy is on (its form, as saved); and General.yaml's outputTo as a start would use it
        internal static List<(string Label, string Value)> Summary(string Settings, DateTime Now)
        {
            GuidedSetupStatus Status = GuidedSetupStatus.Read(Settings, Now);
            var Forms = new SettingsViewModel(Settings);
            var Facts = new List<(string, string)>();
            EmailSettingsViewModel Email = Forms.Email;
            Facts.Add((ConfigFiles.EmailFileName, Email.IsMissing ? W.NotFound
                : !Email.IsEditable && !Email.HasConflict ? W.CantBeEdited
                : Status.NotificationsReady(GuidedSetupMethod.Email) ? W.EmailReady : W.EmailNotReady));
            NtfySettingsViewModel Ntfy = Forms.Ntfy;
            Facts.Add((ConfigFiles.NtfyFileName, Ntfy.IsNew ? W.NotFound
                : !Ntfy.IsEditable && !Ntfy.HasConflict ? W.CantBeEdited
                : !Ntfy.Enabled.IsChecked ? W.NtfyOff
                : Status.NotificationsReady(GuidedSetupMethod.Ntfy) ? W.NtfyReady : W.NtfyNotReady));
            Facts.Add((W.OutputLabel, Status.SavedOutput?.ToString() ?? W.OutputUnknown));
            return Facts;
        }
    }
}
