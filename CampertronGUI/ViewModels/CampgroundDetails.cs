using System;
using System.Text.RegularExpressions;

namespace Campertron.ViewModels
{
    // What CampgroundDetailsView shows for one tab: an immutable snapshot built from the presentation-neutral
    // Read.GetCampgroundDetailsByName values. The GUI owns the labels ("Campground ID", "Phone", ...) and the text
    // for missing values; the data layer adds none. Context is the tab's own filter selection (park, city and
    // state, or state), so no extra query is needed.
    public sealed class CampgroundDetails
    {
        public const string NotListed = "Not listed";

        public static readonly CampgroundDetails None = new CampgroundDetails(null, null);

        public CampgroundDetails(FacilitiesData? Facility, string? Context)
        {
            HasCampground = Facility != null;
            CampgroundId = Facility?.FacilityID?.Trim() ?? string.Empty;
            Name = Facility?.FacilityName?.Trim() ?? string.Empty;
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
