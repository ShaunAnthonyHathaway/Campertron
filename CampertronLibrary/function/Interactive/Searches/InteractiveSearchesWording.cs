using System.Globalization;

namespace CampertronLibrary.function.Interactive.Searches
{
    // The interactive console's own words for its Searches screens (v0.4.0's phase 8, D124; business-rules §43): the menu,
    // its notes, and how a search file's form is shown and asked about at a terminal where it differs from a settings form
    // (InteractiveSettingsWording has the rest). Only the console says them. Every word of the search forms themselves (the
    // list's details and states, New search's and Save As's words and results, every setting's label, hint and message, the
    // campground picker's and the filter selectors' words, the days' and the filters' headings and notes) is the shared form
    // layer's (SearchesViewModel, SearchFileViewModel, SearchesWording) and is shown as the form gives it (ER-37). Fixed
    // words: no settings value, no secret and no exception's text.
    public static class InteractiveSearchesWording
    {
        // ---- the menu ----

        public const string Searches = "Searches";
        public const string SearchesTitle = "Searches (nothing is saved until you choose to)";
        public const string BackToSearches = "Back to Searches";
        public const string SearchesNote = "Nothing here runs a search, and no search file is deleted or renamed. The search files' words are those of the desktop app and the web UI: "
            + "the Results tab is the desktop app's Results tab and the web UI's Results page, and the console's Search is the menu of CampertronConsole started without arguments.";
        // the campground data is needed to list, find and check campgrounds and to offer a campground's values; the console reads
        // it only when it is prepared, and never prepares or refreshes it (Status says the same)
        public const string DataMissing = "The campground data is not prepared yet, so campgrounds can't be listed, found or checked here and the filters can't offer a campground's values; "
            + "the values a search already has are kept. A start without arguments, or the desktop app, prepares it; this console does not.";
        // the settings folder is needed before a search file in it can be listed, created or changed (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there are no search files to change.";
        public const string FolderMissing = "The settings folder does not exist yet, so there are no search files to change. Guided setup, in the main menu, offers to create it.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there are no search files to change. In a container, mount the settings folder at /config.";

        // a file of the list: its name, then the form's details and state (SearchFileViewModel.ListDetails, ListStatus)
        public static string File(string FileName, string Details, string Status) => FileName + ", " + Details + ", " + Status;

        // ---- New search and Save As (their words, rules and results are the form's) ----

        public const string CreateQuestion = "Create the search file now?";
        public const string NothingCreated = "Nothing was created.";
        public const string SaveAsQuestion = "Write this search, with your changes, to the new search file now?";
        public const string NothingWritten = "Nothing was written.";
        public const string NameHow = "Type the name and press Enter. Esc cancels.";

        // ---- a search's form ----

        public const string NoDays = "none checked";
        public const string Checked = "checked";
        public const string NotChecked = "not checked";
        public const string DaysHow = "Choose a day to check or uncheck it, then Done.";
        public static string AndMore(int Count) => "and " + Count.ToString("N0", CultureInfo.CurrentCulture) + " more";
        public const string Done = "Done";

        // ---- the campground ----

        public const string FindCampgroundHow = "Type a name, city, state, park or an exact ID and press Enter; Enter alone lists every campground. Esc keeps the campground as it is.";
        public const string ChooseCampgroundHow = "Choose the campground; Esc keeps the campground as it is.";
        public const string CampgroundIdHow = "Type the campground ID exactly and press Enter. Esc keeps the campground as it is.";
        public const string NoMatches = "No campground matches. Try other words.";
        // the most matches a terminal lists at once (the form says when there are more: CampgroundSettingField.MoreMatchesText)
        public const int ListedCampgrounds = 25;

        // ---- a filter ----

        public const string CheckValues = "Check or uncheck values";
        public const string CheckHow = "Choose a value to check or uncheck it (a group of spellings checks or unchecks them all), then Done.";
        public const string FindHow = "Type words and press Enter to show only the values that contain each of them; Enter alone shows every value. Esc keeps it as it is.";
        public const string CustomHow = "Type the value exactly as it is to be written and press Enter. Esc adds nothing.";
        public const string RemoveValue = "Remove a value that is not the campground's";
        public const string WhichValue = "Which value?";
        public const string ValuesHeading = "In the list";
    }
}
