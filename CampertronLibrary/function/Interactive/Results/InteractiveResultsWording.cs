using System.Globalization;

namespace CampertronLibrary.function.Interactive.Results
{
    // The interactive console's own words for its Results (v0.4.0's phase 10, D126; business-rules §43): the menu and its
    // notes, how a search file is chosen at a terminal, what the console says around a running search (how to cancel it, a
    // key pressed again, the end of the input, a request to stop), and how a result's pages and a campsite are asked about.
    // Only the console says them. Every word the other UIs show for the same thing is the shared form layer's and is shown
    // as it gives it (ER-37): the list's rows and their choice (ResultsSearchRow, AutoRunWording), the selection's labels,
    // Start's refusals, the stages, counts, Canceling and the summary (ResultsWording, ResultsStart), the five minutes after a
    // block (BlockHold), a search's result, its rows and their pages (ResultsOutcome, ResultSearchViewModel, ResultRow,
    // ResultsListing) and a campsite's booking link (ResultsLink). Fixed words: no settings value, no path, no secret and no
    // exception's text.
    public static class InteractiveResultsWording
    {
        // ---- the menu ----

        public const string Results = "Results";
        public const string ResultsTitle = "Results (nothing is searched until you choose Start search)";
        public const string ResultsNote = "Search the chosen search files now at recreation.gov and see what they found, with the choices and words of the desktop app's and the web UI's Results. "
            + "A search here sends no email or notification, never reads or changes Camp.History or a search file, and starts no monitoring; "
            + "like both other UIs it writes only the campgrounds' campsite details into the cache folder. Its results stay until the next search or the end of this console.";
        public const string BackToResults = "Back to Results";

        // the settings folder holds the search files (the console never creates it)
        public const string NoFolder = "This computer gives Campertron no folder for its settings, so there are no search files to search.";
        public const string FolderMissing = "The settings folder does not exist yet, so there are no search files to search. Guided setup, in the main menu, offers to create it.";
        public const string ContainerFolderMissing = "The settings folder was not found, so there are no search files to search. In a container, mount the settings folder at /config.";
        // the cache folder holds the campsite details a search prepares (the console never creates it)
        public const string CacheFolderMissing = "The cache folder does not exist yet, so nothing can be searched here. A start without arguments, or the desktop app, creates it.";
        public const string ContainerCacheFolderMissing = "The cache folder was not found, so nothing can be searched here. In a container, mount the cache folder at /cache.";
        // without the campground data a campground's campsite details can't be prepared (the console never prepares it)
        public const string DataMissing = "The campground data is not prepared yet, so a search can't prepare a campground's campsite details, and says so. "
            + "A start without arguments, or the desktop app, prepares it; Campgrounds can install it.";

        // ---- the choice of the search files ----

        public const string NoSearchFiles = "There are no search files in the settings folder. Create one in Searches.";
        public const string ChooseHow ="Choose a search file to choose it for Start, or to leave it out; the enabled ones (autoRun) that can be searched are chosen at first.";
        public const string Chosen = "chosen";
        public const string NotChosen = "not chosen";
        // a row of the list: the file's own title and details, whether it is chosen, and what the last run did with it
        public static string Row(string Title, string Chosen, string Help, string? RunState) =>
            Title + " [" + Chosen + "] \u00b7 " + Help + (RunState != null ? " \u00b7 Last run: " + RunState : string.Empty);
        public static string NowChosen(string Title) => Title + " is chosen.";
        public static string NoLongerChosen(string Title) => Title + " is no longer chosen.";
        public static string CantBeChosen(string Title, string? Reason) => Title + " can't be chosen. " + (Reason ?? string.Empty);
        public const string NoneChosen = "No search file is chosen.";
        public static string ChosenCount(int Chosen, int Of) => N(Chosen) + " of " + N(Of) + (Of == 1 ? " search file chosen." : " search files chosen.");

        // ---- a running search ----

        public const string RunningHow = "Esc or Ctrl+C cancels the search; searches that already finished keep their results.";
        public static string Searching(string Title) => "  " + Title;
        public static string SearchState(string Title, string State) => "  " + Title + ": " + State;
        public const string StillCanceling = "The search is already canceling: it stops at its next safe point.";
        public const string AskedToStop = "Campertron was asked to stop, so the search is canceled; the console ends when it has stopped.";
        public const string InputEndedDuringSearch = "The input ended, so the search is canceled; the console ends when it has stopped.";

        // ---- the results ----

        public const string NoResultsYet = "No search has run in this console yet.";
        public const string ShowResults = "Show the results of the last run";
        public const string ChooseSearch = "Choose a search to see its results.";
        public const string ChooseCampsite = "Choose an opening or a stay to see its campsite and its booking link.";
        public const string BackToSearches = "Back to the searches of the last run";
        public const string BackToRows = "Back to the results";
        public const string Campsite = "Campsite";
        public const string CampsiteIdLabel = "Campsite ID";
        public const string StayLabel = "Stay";
        public const string StaysLabel = "Stays";
        public const string DetailsLabel = "Details";
        public const string Choices = "Choices";
        // the plain text of a link or an ID, alone on its line, to copy from the terminal (a terminal has no clipboard here)
        public const string ShowLink = "Show the booking link alone, to copy it";
        public const string ShowId = "Show the campsite ID alone, to copy it";
        public const string CopyHow = "Select the line below in your terminal to copy it; Campertron opens no browser from here.";

        // the most openings or stays a terminal lists at once (a run can find thousands)
        public const int PageSize = 25;

        private static string N(int Value) => Value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
