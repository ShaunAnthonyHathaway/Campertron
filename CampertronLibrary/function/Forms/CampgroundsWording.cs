namespace CampertronLibrary.function.Forms
{
    // The words of the campground lookup (beta.8, D28; campgrounds by FacilityID since D42; the tab's name since D55): the
    // desktop GUI's Campgrounds tab and, since v0.3.0's PR 9 (D99), the web UI's campgrounds pages, written once here for
    // both UIs (ER-37): the three ways to look, the filters' labels, what to do before a campground is chosen and the
    // labels of a campground's details. The desktop GUI's views bind them (an underscore marks a label's access key there;
    // SearchesWording.Plain drops it); the web UI shows them, with the few that only a page needs. A campground's label is
    // CampgroundChoice's, its details CampgroundDetails'. Copying the ID to the clipboard is the desktop GUI's own button
    // (a page's text is selected and copied in the browser itself; the web UI serves no script, D80).
    public static class CampgroundsWording
    {
        public const string Heading = "Campgrounds";
        public const string ByPark = "By Park";
        public const string ByCity = "By City";
        public const string ByState = "By State";

        // ---- the filters ----
        public const string ParkLabel = "_Park";
        public const string StateLabel = "_State";
        public const string CityLabel = "C_ity";
        public const string CampgroundLabel = "_Campground";
        public const string ParkPlaceholder = "Select or type a park";
        public const string StatePlaceholder = "Select or type a state";
        public const string CityPlaceholder = "Select or type a city";
        public const string CampgroundPlaceholder = "Select or type a campground";
        // a city the campground data lists without a name (the empty city is a city of its own: it is chosen and queried as
        // it is stored)
        public const string NoCityListed = "(No city listed)";

        // what each way to look asks for before a campground is chosen
        public const string ByParkHint = "Choose a park, then one of its campgrounds, to see the campground ID and details.";
        public const string ByCityHint = "Choose a state, a city and then a campground to see the campground ID and details.";
        public const string ByStateHint = "Choose a state, then one of its campgrounds, to see the campground ID and details.";

        // ---- a campground's details ----
        public const string NoCampgroundSelected = "No campground selected";
        public const string DetailsName = "Campground details";
        public const string CampgroundIdLabel = "Campground ID";
        public const string CampgroundIdNote = "Use this number as the campgroundID in a search file.";
        public const string ContactHeading = "Contact";
        public const string PhoneLabel = "Phone";
        public const string EmailLabel = "Email";
        public const string DescriptionHeading = "Description";
        public const string DirectionsHeading = "Directions";
        public const string FeesHeading = "Fees";
        public const string NotListed = "Not listed";

        // ---- what only a page needs (the web UI, D99): a list of campgrounds a part at a time, and the filters' button ----
        public const string ShowLabel = "Show";
        public const string ShowParkHelp = "Shows the campgrounds of the chosen park";
        public const string ShowCityHelp = "Shows the cities of the chosen state, or the campgrounds of the chosen city";
        public const string ShowStateHelp = "Shows the campgrounds of the chosen state";
        public const string ChooseOne = "Choose one";
        public const string CampgroundsListName = "Campgrounds to choose from";
        public const string PreviousCampgrounds = "Previous campgrounds";
        public const string NextCampgrounds = "Next campgrounds";
        public const string NoCampgrounds = "This list has no campground.";
        public const string NotInList = "The campground the address names is not in this list, so no campground is shown. Choose one of the list.";
        public const string NoData = "Campertron couldn't read its campground data, so campgrounds can't be listed here.";
        public const string SelectToCopy = "Select the ID to copy it.";

        // a city as the lists show it
        public static string City(string? Value) => string.IsNullOrWhiteSpace(Value) ? NoCityListed : Value;

        // which campgrounds of a list a part shows (never cut without saying so)
        public static string CampgroundsShown(int First, int Last, int Total) =>
            "Showing campgrounds " + First.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " to "
            + Last.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + " of " + Total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) + ".";
    }
}
