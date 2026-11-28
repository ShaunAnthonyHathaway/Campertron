using System.Globalization;

namespace CampertronLibrary.function.Interactive.Campgrounds
{
    // The interactive console's own words for its Campgrounds screens (v0.4.0's phase 9, D125; business-rules §43): the menu,
    // its notes, how the lookup's lists, a campground's details and its campsites' values are shown and asked about at a
    // terminal, the campground data's status, and what the console says around the refresh. Only the console says them.
    // Every word the other UIs show for the same thing is the shared form layer's and is shown as it gives it (ER-37): the
    // lookup's headings, labels and missing values (CampgroundsWording, CampgroundDetails, CampgroundChoice), the campsite
    // values' headings (SearchesWording), and the refresh's explanation, confirmation, stages, counts, cancel and result
    // (RefreshWording). Fixed words: no settings value, no path, no secret and no exception's text.
    public static class InteractiveCampgroundsWording
    {
        // ---- the menu ----

        public const string Campgrounds = "Campgrounds";
        public const string CampgroundsTitle = "Campgrounds (looking up a campground and the status change nothing)";
        public const string CampgroundsNote = "Look up a campground in the campground data and see its details, see the campground data's status, or refresh the campground data from Recreation.gov. "
            + "Only a refresh changes anything, and only after you answer y.";
        // the menu's choices (CampgroundsWording.Heading, "Campgrounds", is a list of campgrounds)
        public const string CampgroundsChoices = "Campgrounds and the campground data";
        public const string LookUp = "Look up a campground";
        public const string DataStatus = "Campground data status";
        public const string RefreshOrInstall = "Refresh or install campground data";
        public const string BackToCampgrounds = "Back to Campgrounds";

        // the settings folder holds the campground data (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there is no campground data here.";
        public const string FolderMissing = "The settings folder does not exist yet, so there is no campground data here. Guided setup, in the main menu, offers to create it.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there is no campground data here. In a container, mount the settings folder at /config.";

        // ---- the lookup ----

        // without the campground data nothing is listed and no database is opened (the console never prepares it)
        public const string DataMissing = "The campground data is not prepared yet, so there are no campgrounds to look up. Refresh or install campground data downloads it and installs it; "
            + "a start without arguments, or the desktop app, prepares the campground list that came with Campertron.";
        public const string FindChoice = "Find a campground by name, city, state, park or exact ID";
        public const string FindHow = "Type words of a campground's name, city, state or park, or its exact ID, and press Enter; Enter alone lists every campground. Esc goes back.";
        public const string FindQuestion = "Find";
        public const string NoMatches = "No campground matches. Try other words.";
        public const string ChooseCampground = "Choose a campground to see its details.";

        // the most entries a terminal lists at once (a state's or a park's list can hold hundreds; every campground thousands)
        public const int PageSize = 25;
        public static string Shown(int First, int Last, int Total) => "Showing " + N(First) + " to " + N(Last) + " of " + N(Total) + ".";
        public static string NextPart(int Count) => "Show the next " + N(Count);
        public static string PreviousPart(int Count) => "Show the previous " + N(Count);
        public const string FindInList = "Find in this list";
        public const string WholeList = "Show the whole list again";
        public const string FindInListHow = "Type words and press Enter to show only the entries that contain each of them; Enter alone shows the whole list. Esc keeps the list as it is.";
        public const string NothingInList = "Nothing in this list contains those words. The list is shown as it was.";
        public const string EmptyList = "This list is empty.";
        // the choices under a list that is only read (a campground's campsite values)
        public const string ListChoices = "Choices";
        public const string Back = "Back";
        public static string Found(int Count) => Count == 1 ? "1 entry contains those words." : N(Count) + " entries contain those words.";

        // ---- a campground's details ----

        public const string CampsitesUnreadable = "Campertron couldn't read this campground's campsites from its campground data.";
        public static string CampsiteCount(int Count) => Count == 0 ? "No campsites of this campground are listed in the campground data."
            : Count == 1 ? "1 campsite of this campground is listed in the campground data."
            : N(Count) + " campsites of this campground are listed in the campground data.";
        public const string ValuesNote = "Its campsites' values are what a search's filters can be set to for this campground (Searches, and the desktop app's and the web UI's search editors).";
        public static string Values(string Heading, int Count) => Heading + " (" + (Count == 1 ? "1 value" : N(Count) + " values") + ")";
        public const string BackToList = "Back to the list";
        public const string CampgroundGone = "This campground is no longer a reservable, enabled campground in the campground data, so its details can't be shown.";
        public const string DetailsUnreadable = "Campertron couldn't read this campground's details from its campground data.";

        // a value of a campground's campsites and how many campsites have it (as the search editors' filters count them)
        public static string Value(string Value, int Campsites, bool IsName) => Value + " \u2014 " + (Campsites == 1 ? "1 campsite"
            : N(Campsites) + (IsName ? " campsites share this name" : " campsites"));

        // ---- the campground data's status ----

        public const string StatusTitle = "Campground data status (read only: nothing is changed)";
        public const string PreparedLabel = "Prepared";
        public const string ReadableLabel = "Readable";
        public const string NeededLabel = "What it needs";
        public const string RestartLabel = "After a refresh";
        public const string Prepared = "yes";
        public const string NotPrepared = "no: not prepared yet (nothing was opened)";
        public static string Readable(int Campgrounds) => "yes: " + N(Campgrounds) + (Campgrounds == 1 ? " reservable, enabled campground" : " reservable, enabled campgrounds");
        public const string NotReadable = "no: Campertron couldn't read it";
        public const string NotChecked = "not checked: the campground data is not prepared";
        public const string NeedsInstall = "Refresh or install campground data downloads the latest campground data from Recreation.gov and installs it; a start without arguments, "
            + "or the desktop app, prepares the campground list that came with Campertron instead.";
        public const string NeedsNothing = "Nothing: Refresh or install campground data downloads the latest campground data from Recreation.gov whenever you want it.";
        public const string NeedsAttention = "Campgrounds can't be looked up or chosen while the campground data can't be read.";
        public const string NoRestartHere = "This console reads the campground data again each time Campgrounds or Searches opens it, so it needs no restart. "
            + "A desktop app or web mode that is running keeps the campground data it has read until it is restarted.";

        // ---- the refresh ----

        public const string InstallNote = "The campground data is not prepared yet, so the refresh installs the campground data it downloads.";
        // what the confirmation says after the shared words: the console's own boundary (D125)
        public const string ConfirmationAfter = "While it runs, this console shows its progress, and Esc or Ctrl+C cancels it until the new data is being installed; from then on it finishes. "
            + "If it fails or is canceled, the campground data stays as it was. Nothing is retried, and nothing else is started, stopped or restarted. "
            + "This console uses the new data at once, since Campgrounds and Searches read it each time they open it. "
            + "A desktop app or web mode that is running keeps the campground data it has read until it is restarted, and a monitoring that runs in another Campertron program is not paused by this refresh.";
        public const string StartQuestion = "Start the refresh now?";
        public const string NotStarted = "Nothing was started.";
        public const string CacheFolderMissing = "The cache folder does not exist yet, so the campground data can't be refreshed here. A start without arguments, or the desktop app, creates it.";
        public const string ContainerCacheFolderMissing = "The cache folder was not found, so the campground data can't be refreshed here. In a container, mount the cache folder at /cache.";
        public const string RunningHow = "Esc or Ctrl+C cancels the refresh until the new data is being installed.";
        public const string StillCanceling = "The refresh is already canceling: it stops at its next safe point and removes what it downloaded.";
        public const string AskedToStop = "Campertron was asked to stop, so the refresh is canceled if it still can be; the console ends when the refresh has stopped or finished.";
        public const string InputEndedDuringRefresh = "The input ended, so the refresh is canceled if it still can be; the console ends when the refresh has stopped or finished.";
        // after a successful refresh: no live reload (D61), no automatic restart of the console (the desktop app's alone, D62)
        public const string AfterSuccess = "This console uses the new campground data from now on. A desktop app or web mode that is running keeps the campground data it has read until it is restarted.";

        private static string N(int Value) => Value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
