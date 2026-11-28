using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using W = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;

namespace CampertronLibrary.function.Interactive.Settings
{
    // one file of a settings menu: its label at the menu and which form of the shared composition it opens
    internal sealed record SettingsMenuFile(string Label, Func<SettingsViewModel, SettingsFileViewModel> FormOf);

    // a settings menu's own words: its heading, its choices' heading, the choice that leaves a form for it, and what it says
    // without the settings folder (not given by this computer, not created yet, not mounted in a container)
    internal sealed record SettingsMenuWords(string Title, string Name, string BackToMenu, string NoFolder, string FolderMissing, string ContainerFolderMissing);

    // The interactive console's settings menus (v0.4.0's phase 7, D122: Notifications; its phase 7a, D123: Settings): the
    // one loop both use, so that every file of them is found, summarized, opened and left alike. Entering a menu reads and
    // changes nothing but what it shows (its summary, never a secret); a choice opens a new shared form of SettingsViewModel
    // (the composition both other UIs use: Email.yaml's and Ntfy.yaml's checks follow General.yaml's outputTo, D71) in
    // InteractiveSettingsForm, and leaving the form keeps only what was saved. Back, Esc and Ctrl+C go back to the main menu;
    // the end of the input ends the console. The settings folder must exist: the console never creates it.
    internal static class InteractiveSettingsMenu
    {
        internal static InteractiveStep Run(InteractiveSession Session, SettingsMenuWords Words, IReadOnlyList<SettingsMenuFile> Files,
            Func<string, DateTime, List<(string Label, string Value)>> Summary, string Note)
        {
            InteractivePrompts Prompts = Session.Prompts;
            //since D130 (PT1): a screen of its own, started on a cleared screen on arrival and on each return to it
            InteractiveScreen Here = Prompts.Screen();
            Here.Show();
            Prompts.Heading(Words.Title);
            string? Settings = SettingsFolder(Session, Words);
            if (Settings == null)
            {
                return InteractiveStep.Done;
            }
            List<string> Choices = Files.Select(File => File.Label).Append(W.BackToMainMenu).ToList();
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Here.Show();
                    Prompts.Heading(Words.Title);
                }
                First = false;
                foreach ((string Label, string Value) in Summary(Settings, Session.UtcNow().ToLocalTime()))
                {
                    Prompts.Say("  " + Label + ": " + Value, "    ");
                }
                Prompts.Say(Note);
                PromptResult<int> Picked = Prompts.Choose(Words.Name, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Files.Count)
                {
                    //Back, Esc or Ctrl+C: the main menu again
                    return InteractiveStep.Done;
                }
                //a new form each time: it reads the files as they are now, and leaving it keeps only what was saved
                var Forms = new SettingsViewModel(Settings);
                //the search files are edited in this console's Searches menu, with New search file (PT1): General.yaml's
                //message at outputTo names them so
                Forms.General.UseSearchPlace(SearchPlace.Menu);
                SettingsFileViewModel Form = Files[Picked.Value].FormOf(Forms);
                ScreenEnd End = InteractiveSettingsForm.Run(Prompts, Form, Words.BackToMenu, () => DateTime.Now, SmtpPassword.VariableIsSet, NtfyToken.VariableIsSet);
                if (End == ScreenEnd.EndOfInput)
                {
                    return InteractiveStep.EndOfInput;
                }
            }
        }

        // where the settings folder is and whether it exists, as Status finds it (Load.FolderLocations, which creates nothing):
        // null Path when this computer gives none; Exists false when it is not there yet; Container in a container (since
        // D130, the guided setup offers to create a missing one, never in a container, where /config is mounted)
        internal static (string? Path, bool Exists, bool Container) FindFolder(InteractiveSession Session)
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
                return (null, false, false);
            }
            return (Folders.ConfigPath, Directory.Exists(Folders.ConfigPath), Folders.ContainerMode);
        }

        // the settings folder, found as Status finds it (Load.FolderLocations, which creates nothing), or null when there is
        // none yet, which is said in the menu's words (since phase 8, D124, also the Searches menu's)
        internal static string? SettingsFolder(InteractiveSession Session, SettingsMenuWords Words)
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
                Session.Prompts.Say(Words.NoFolder);
                return null;
            }
            if (!Directory.Exists(Folders.ConfigPath))
            {
                Session.Prompts.Say(Folders.ContainerMode ? Words.ContainerFolderMissing : Words.FolderMissing);
                return null;
            }
            return Folders.ConfigPath;
        }
    }
}
