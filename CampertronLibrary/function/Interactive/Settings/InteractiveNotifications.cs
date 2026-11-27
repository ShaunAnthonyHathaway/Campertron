using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using W = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronLibrary.function.Interactive.Settings
{
    // The interactive console's Notifications (v0.4.0's phase 7, D122; business-rules §43): Email.yaml and Ntfy.yaml, each
    // in the shared form both other UIs show (SettingsViewModel's Email and Ntfy forms, the layer's composition: their
    // checks follow General.yaml's outputTo as it is saved, D71), hosted by InteractiveSettingsForm, with the test email,
    // the test notification and the random topic of those forms. General.yaml and Web.yaml are not offered here.
    //
    // Entering it reads and changes nothing but what it shows: a short summary of the two files (whether each exists and
    // whether the monitoring's start would accept it as it is saved: GuidedSetupStatus, the start's own checks, which read
    // no secret, open no secret file and read no environment variable) and General.yaml's outputTo. Nothing is created,
    // saved or sent until the user chooses it in a form and confirms it. A form lasts while it is shown: leaving it keeps
    // only what was saved. The settings folder must exist: the console never creates it (a start without arguments, the
    // desktop app or web mode does).
    internal static class InteractiveNotifications
    {
        // the main menu's Notifications
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            Prompts.Heading(W.NotificationsTitle);
            string? Settings = SettingsFolder(Session);
            if (Settings == null)
            {
                return InteractiveStep.Done;
            }
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Prompts.Heading(W.NotificationsTitle);
                }
                First = false;
                ShowSummary(Session, Settings);
                PromptResult<int> Picked = Prompts.Choose(W.Notifications, new[] { W.Email, W.Ntfy, W.BackToMainMenu });
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == 2)
                {
                    //Back, Esc or Ctrl+C: the main menu again
                    return InteractiveStep.Done;
                }
                //a new form each time: it reads the files as they are now, and leaving it keeps only what was saved
                var Forms = new SettingsViewModel(Settings);
                SettingsFileViewModel Form = Picked.Value == 0 ? Forms.Email : Forms.Ntfy;
                ScreenEnd End = InteractiveSettingsForm.Run(Prompts, Form, () => DateTime.Now, SmtpPassword.VariableIsSet, NtfyToken.VariableIsSet);
                if (End == ScreenEnd.EndOfInput)
                {
                    return InteractiveStep.EndOfInput;
                }
            }
        }

        // the settings folder, found as Status finds it (Load.FolderLocations, which creates nothing), or null when there is
        // none yet, which is said
        private static string? SettingsFolder(InteractiveSession Session)
        {
            CampertronInternalConfig? Folders = null;
            try
            {
                Folders = Session.Folders();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException
                || ex is System.Security.SecurityException || ex is InvalidOperationException)
            {
                //no folder for the settings (Load.DesktopDataFolder's refusal)
            }
            if (Folders?.ConfigPath == null)
            {
                Session.Prompts.Say(W.NoFolder);
                return null;
            }
            if (!Directory.Exists(Folders.ConfigPath))
            {
                Session.Prompts.Say(Folders.ContainerMode ? W.ContainerFolderMissing : W.FolderMissing);
                return null;
            }
            return Folders.ConfigPath;
        }

        // what the two files are now, in words: never a value of them
        private static void ShowSummary(InteractiveSession Session, string Settings)
        {
            foreach ((string Label, string Value) in Summary(Settings, Session.UtcNow().ToLocalTime()))
            {
                Session.Prompts.Say("  " + Label + ": " + Value, "    ");
            }
            Session.Prompts.Say(W.OutputNote);
        }

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
