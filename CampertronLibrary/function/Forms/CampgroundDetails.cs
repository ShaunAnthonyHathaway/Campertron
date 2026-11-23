using System.Text.RegularExpressions;

namespace CampertronLibrary.function.Forms
{
    // What the campground lookup shows of one campground: an immutable snapshot built from the presentation-neutral
    // Read.GetCampgroundDetailsById values (the selected campground's FacilityID, decision D42). The lookup owns the labels
    // ("Campground ID", "Phone", ..., CampgroundsWording) and the text for missing values; the data layer adds none. A
    // campground with a blank name is shown as "Unnamed campground", as in the lists. Context is the view's own filter
    // selection (park, city and state, or state), so no extra query is needed.
    // Since v0.3.0's PR 9 (D99) in the shared form layer, moved whole with its name from the desktop GUI's view models:
    // the desktop GUI's CampgroundDetailsView and the web UI's campgrounds pages show the same snapshot (ER-37).
    public sealed class CampgroundDetails
    {
        public const string NotListed = CampgroundsWording.NotListed;

        public static readonly CampgroundDetails None = new CampgroundDetails(null, null);

        public CampgroundDetails(FacilitiesData? Facility, string? Context)
        {
            HasCampground = Facility != null;
            CampgroundId = Facility?.FacilityID?.Trim() ?? string.Empty;
            Name = Facility == null ? string.Empty
                : string.IsNullOrWhiteSpace(Facility.FacilityName) ? CampgroundChoice.UnnamedName : Facility.FacilityName.Trim();
            this.Context = string.IsNullOrWhiteSpace(Context) ? string.Empty : Context.Trim();
            Phone = Display(Facility?.FacilityPhone);
            Email = Display(Facility?.FacilityEmail);
            Description = Display(Facility?.FacilityDescription);
            Directions = Display(Facility?.FacilityDirections);
            Fees = Display(Facility?.FacilityUseFeeDescription);
        }

        public bool HasCampground { get; }
        public string CampgroundId { get; }
        public string Name { get; }
        public string Context { get; }
        public bool HasContext => Context.Length > 0;
        public string Phone { get; }
        public string Email { get; }
        public string Description { get; }
        public string Directions { get; }
        public string Fees { get; }

        // "City, ST" for the By City tab; only the state when the campground has no city in RIDB.
        public static string CityContext(string? City, string? State)
        {
            if (string.IsNullOrWhiteSpace(City))
            {
                return State ?? string.Empty;
            }
            return string.IsNullOrWhiteSpace(State) ? City : City + ", " + State;
        }

        // Display only: trims the value and collapses runs of blank lines (common in RIDB HTML) to one blank line.
        private static string Display(string? Value)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                return NotListed;
            }
            return BlankLines.Replace(Value.Replace("\r\n", "\n").Trim(), "\n\n");
        }

        private static readonly Regex BlankLines = new Regex("\n[ \t]*(\n[ \t]*){2,}");
    }
}
