namespace CampertronLibrary.function.Interactive.Settings
{
    // The interactive console's own words for its settings screens (v0.4.0's phase 7, D122, and phase 7a, D123;
    // business-rules §43): the Notifications and Settings menus and their summaries, and how a shared settings form is shown
    // and asked about at a terminal. Only the console says them. Every word of a form itself (its title, its labels, hints, messages, states, explanations, the
    // test's words, the random topic's words) is the shared form layer's and is shown as the form gives it (ER-37), never
    // written again here. Fixed words: no settings value, no secret, no path and no exception's text.
    public static class InteractiveSettingsWording
    {
        // ---- the Notifications menu ----

        public const string Notifications = "Notifications";
        public const string Email = "Email";
        public const string Ntfy = "ntfy";
        public const string BackToMainMenu = "Back to the main menu";
        public const string NotificationsTitle = "Notifications (nothing is saved or sent until you choose to)";

        // the summary's facts: whether each file exists and whether the monitoring's start would accept it as it is saved
        // (GuidedSetupStatus, the start's own checks: no secret is read), never a value of it
        public const string NotFound = "not found";
        public const string CantBeEdited = "found, but it can't be used as it is: choose it to see why";
        public const string EmailReady = "found; email mode's start would accept it as it is saved";
        public const string EmailNotReady = "found; email mode's start would refuse it as it is saved: choose Email to see why";
        public const string NtfyOff = "found; ntfy is off";
        public const string NtfyReady = "found; ntfy is on, and the monitoring's start would accept it as it is saved";
        public const string NtfyNotReady = "found; ntfy is on, but the monitoring's start would refuse it as it is saved: choose ntfy to see why";
        public const string OutputLabel = "outputTo (General.yaml)";
        public const string OutputUnknown = "not set up (General.yaml is missing or can't be used)";
        public const string OutputNote = "General.yaml's outputTo decides whether the unattended monitoring sends emails, ntfy notifications or nothing; choose it in Settings > General settings.";

        // the settings folder is needed before a file in it can be created or changed (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there are no notification settings to change.";
        public const string FolderMissing = "The settings folder does not exist yet, so there are no notification settings to change. Guided setup, in the main menu, offers to create it.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there are no notification settings to change. In a container, mount the settings folder at /config.";

        // ---- the Settings menu (phase 7a, D123) ----

        public const string Settings = "Settings";
        public const string SettingsTitle = "Settings (nothing is saved until you choose to)";
        // the forms' own titles (GeneralSettingsViewModel.Title, WebSettingsViewModel.Title), so the menu and the form say one name
        public const string General = "General settings";
        public const string Web = "Web UI settings";
        public const string BackToSettings = "Back to Settings";
        // the summary's facts: whether each file exists and what it decides, never a secret (Web.yaml holds none)
        public const string GeneralMissing = "not found: a start without arguments, or the desktop app, creates it (outputTo: Console); choose General settings to create it now";
        public static string GeneralFound(string Output) => "found; outputTo: " + Output;
        public static string MonitoringAccepts(string Output) => GeneralFound(Output) + "; the monitoring's start would accept the saved settings";
        public static string MonitoringRefuses(string Output) => GeneralFound(Output) + "; the monitoring's start would refuse the saved settings: choose General settings to see why";
        public const string WebMissing = "not found; web mode uses the defaults shown in Web UI settings, and Save creates the file";
        public static string WebFound(bool Password, bool Network) => "found; passwordProtection: " + (Password ? On : Off) + "; networkListening: " + (Network ? On : Off);
        public const string SettingsNote = "Saving a file here starts, stops and restarts nothing: Campertron reads General.yaml when it starts, a monitoring when it starts, and web mode reads Web.yaml when it starts. The web UI's password is set and changed in the web UI only.";
        public const string SettingsNoFolder = "This computer gives Campertron no folder for its settings, so there are no settings to change.";
        public const string SettingsFolderMissing = "The settings folder does not exist yet, so there are no settings to change. Guided setup, in the main menu, offers to create it.";
        public const string SettingsContainerFolderMissing = "The settings folder was not found, so there are no settings to change. In a container, mount the settings folder at /config.";

        // ---- a form's screen ----

        public const string Empty = "(empty)";
        public const string Changed = " (changed)";
        public const string On = "on";
        public const string Off = "off";
        public const string TopicHidden = "set, not shown (choose Show the topic to see it)";
        public static string Items(int Count) => Count == 1 ? "1 entry" : Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " entries";
        public const string EmailVariable = "CAMPERTRON_EMAIL_PASSWORD set where this console runs";
        public const string EmailVariableNote = "Only whether it is set is shown, never its value. Email mode uses it when Email.yaml has no smtpPasswordFile, before the plain-text password.";
        public const string NtfyVariable = "CAMPERTRON_NTFY_TOKEN set where this console runs";
        public const string NtfyVariableNote = "Only whether it is set is shown, never its value. The monitoring uses it when Ntfy.yaml has no tokenFile.";
        public const string Yes = "Yes";
        public const string No = "No";
        public static string Conflict(string FileName) => FileName + " was changed by another program";
        // General.yaml's form: when a saved change applies (nothing is started, stopped or restarted by a save)
        public const string GeneralApplies = "Campertron reads General.yaml when it starts, and a monitoring reads it when it starts: a saved change applies from the next start. Saving starts, stops and restarts nothing, and this console never starts the monitoring.";

        // ---- a form's choices ----

        public static string Change(string Field) => "Change " + Field;
        public const string ShowTopic = "Show the topic";
        public const string ShowChanges = "Show the changes Save would write";
        public const string Save = "Save";
        public const string Revert = "Revert changes";
        public const string Reload = "Reload from disk";
        public const string Recreate = "Create the file again with my changes";
        public const string RemovePasswordFile = "Remove smtpPasswordFile";
        public const string RemovePassword = "Remove the plain-text password";
        public const string RemoveTokenFile = "Remove tokenFile";
        public const string UseTheirs = "Discard my changes and use the file on disk";
        public const string BackToNotifications = "Back to Notifications";

        // ---- a form's questions ----

        public const string TextHow = "Type the new value and press Enter; Enter alone leaves it empty. Esc keeps it as it is.";
        public const string NewValue = "New value";
        public static string Now(string Value) => "Now: " + Value;
        public const string SecretHow = "Type the new plain-text password and press Enter. It is not shown as you type, and Esc keeps the password as it is.";
        public const string NewPassword = "New plain-text password";
        public const string ChoiceHow = "Esc keeps the choice as it is.";
        public static string TurnOn(string Field) => Field + "?";
        public static string SaveQuestion(string FileName) => "Save " + FileName + " now?";
        public static string RevertQuestion(string FileName) => "Revert your changes to " + FileName + "?";
        public static string CreateQuestion(string FileName) => "Create " + FileName + " with the default settings?";
        public static string LeaveQuestion(string FileName) => "Leave without saving your changes to " + FileName + "? They are not kept.";
        public static string RecreateQuestion(string FileName) => "Create " + FileName + " again with your changes?";
        public static string UseTheirsQuestion(string FileName) => "Discard your changes and use " + FileName + " as it is on disk?";
        public const string SendQuestion = "Send it now?";
        public const string ShowTopicQuestion = "Show the topic on this screen? Anyone who can see the screen can read it.";
        public const string ShowChangesQuestion = "The lines can show the topic. Show them on this screen? Anyone who can see the screen can read them.";
        public const string NothingDone = "Nothing was changed.";
        public const string NotSent = "Nothing was sent.";
        public const string Kept = "Your changes are still here; nothing was saved.";

        // ---- a list's screen ----

        public static string ChangeItem(string Item) => "Change a " + Item;
        public static string RemoveItem(string Item) => "Remove a " + Item;
        public static string MoveUp(string Item) => "Move a " + Item + " up";
        public static string MoveDown(string Item) => "Move a " + Item + " down";
        public const string Done = "Done";
        public static string WhichItem(string Item) => "Which " + Item + "?";
        public const string RowHow = "Type the value and press Enter; Enter alone leaves the row empty, and an empty row is never written. Esc keeps it as it is.";
    }
}
