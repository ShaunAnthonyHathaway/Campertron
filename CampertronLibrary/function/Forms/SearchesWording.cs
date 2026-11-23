namespace CampertronLibrary.function.Forms
{
    // The words of the Searches tab and of the web UI's searches pages that are not a setting's (CM6, decisions D44, D51,
    // D63; since v0.3.0's PR 6, D96, written once here for both UIs, ER-37): headings, the labels and help of the tab's own
    // boxes and buttons, and the notes under them. The desktop GUI's views bind them (an underscore marks a label's access
    // key there); the web UI shows them without the underscore (Plain). A setting's own label, hint and messages are its
    // field's (SearchFileViewModel), and the files' states the forms'.
    public static class SearchesWording
    {
        // ---- the list of search files and New search (the Searches tab's first card) ----
        public const string FilesHeading = "Search files";
        public const string NoFiles = "There are no search files in the settings folder yet.";
        public const string ChooseFile = "Choose a search file to edit it.";
        public const string NewSearchLabel = "_New search file";
        public const string NewSearchPlaceholder = "A name, such as Yosemite weekends";
        public const string NewSearchName = "New search file name";
        public const string NewSearchHelp = "The file is created in the settings folder with .yaml added, from the example search, not enabled (autoRun: false).";
        public const string CreateSearch = "Create search";
        public const string NewSearchNote = "A new search starts from the example search and is not enabled: the Results tab runs it when you select it, and the console's Search and email mode run it once you enable it and save. Search files are never deleted here.";
        public const string LookForChanges = "Look for changes on disk";
        public const string LookForChangesHelp = "Read the settings folder and every search file again";

        // ---- Save As ----
        public const string SaveAsLabel = "Save as a ne_w search file";
        public const string SaveAsName = "New file name for Save as";
        public const string SaveAsHelp = "Writes this search, with your changes, to a new file in the settings folder. The file you edited is not changed.";
        public const string SaveAsButton = "Save as";
        public const string SaveAsNote = "Writes this search, with your changes, to a new file. The file you edited keeps what is on disk. An existing file is never written over.";

        // ---- the campground ----
        public const string FindCampgroundLabel = "_Find a campground";
        public const string FindCampgroundName = "Find a campground";
        public const string FindCampgroundPlaceholder = "Name, city, state, park or exact ID";
        public const string FindCampgroundHelp = "Narrows the list to campgrounds whose name, city, state or park contains each word, or whose ID is exactly what you type.";
        public const string CampgroundsName = "Campgrounds";
        public const string UseSelectedCampground = "Use the selected campground";
        public const string CampgroundIdLabel = "Or enter a campground _ID";
        public const string CampgroundIdName = "Campground ID";
        public const string UseCampgroundId = "Use this ID";

        // ---- the groups of the editor ----
        public const string DatesHeading = "Dates and people";
        public const string WeekdaysHeading = "Days of the week";
        public const string WeekdaysNote = "Openings are shown only on the days checked (showMonday to showSunday).";
        public const string FiltersHeading = "Campsite filters";
        public const string FiltersExplanation = "An include list keeps a campsite that matches any of its values; an exclude list leaves out a campsite that matches any of its values. Empty lists filter nothing. The values to check come from the chosen campground's data; a value it does not have is kept in the list and shown under the list.";
        public const string RetryChoices = "Read the campground's values again";
        public const string EquipmentHeading = "Equipment";
        public const string CampsitesHeading = "Campsites";
        public const string CampsiteTypesHeading = "Campsite types";
        public const string AttributesHeading = "Attributes";

        // ---- a filter selector ----
        public const string FindPlaceholder = "Find…";
        public const string FindHelp = "Shows only the values that contain each word you type (ignoring case).";
        public const string SelectShown = "Select all shown";
        public const string ClearShown = "Clear shown";
        public const string Remove = "Remove";
        public const string CustomPlaceholder = "Add another value, exactly as written";
        public const string Add = "Add";

        // a label without its access key's mark, as a UI without access keys shows it
        public static string Plain(string Label) => Label.Replace("_", string.Empty);
    }
}
