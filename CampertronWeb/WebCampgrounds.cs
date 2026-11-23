using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronWeb
{
    // The campgrounds pages' lookup (tests only change it): the library's queries of the desktop GUI's Campgrounds tab
    internal sealed class WebLookupSeams
    {
        internal Func<List<string>> Parks { get; init; } = () => new List<string>();
        internal Func<string, List<CampgroundChoice>> CampgroundsByPark { get; init; } = Park => new List<CampgroundChoice>();
        internal Func<List<string>> States { get; init; } = () => new List<string>();
        internal Func<string, List<string>> Cities { get; init; } = State => new List<string>();
        internal Func<string, string, List<CampgroundChoice>> CampgroundsByStateAndCity { get; init; } = (State, City) => new List<CampgroundChoice>();
        internal Func<string, List<CampgroundChoice>> CampgroundsByState { get; init; } = State => new List<CampgroundChoice>();
        internal Func<string, FacilitiesData?> Details { get; init; } = FacilityID => null;
        // the database is there (a test without one says false)
        internal bool HasData { get; init; } = true;
    }

    // The three ways to look of the desktop GUI's Campgrounds tab (beta.8, D28, D42; business-rules-gui §16)
    internal enum WebLookupView
    {
        Park,
        City,
        State
    }

    // The campgrounds pages' reads (v0.3.0's PR 9, D99; business-rules-settings §36): the library's own queries, the ones
    // the desktop GUI's Campgrounds tab runs (Read.ParksWithCampgrounds, CampgroundChoicesByPark, UniqueStates, UniqueCities,
    // CampgroundChoicesByStateAndCity, CampgroundChoicesByState, GetCampgroundDetailsById), on the settings folder's
    // RecreationDotOrg.db. Reads only, and only of a database that is there: SQLite would create an empty file for one that
    // is not, and a page that only looks never prepares, extracts, repairs, indexes or refreshes the campground data (the
    // searches and results pages and the monitoring's start prepare it, WebCampgroundData). A campground is its FacilityID
    // (D42): a campground's details are looked up by it, never by a name, and only for a campground of the list the page
    // shows. No state: one object per web UI, safe from any thread; SQLite keeps a reader's snapshot while a refresh installs
    // a new database (D26), and the next read sees the new data.
    internal sealed class WebCampgrounds
    {
        private readonly WebHostSettings _Settings;

        internal WebCampgrounds(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        private string Folder => _Settings.SettingsFolder;
        private WebLookupSeams? Seams => _Settings.Lookup;

        // the database is there to be read
        internal bool HasData => Seams?.HasData ?? File.Exists(Path.Join(Folder, "RecreationDotOrg.db"));

        internal List<string> Parks() => Seams?.Parks() ?? Read.ParksWithCampgrounds(Folder);
        internal List<CampgroundChoice> CampgroundsByPark(string Park) => Seams?.CampgroundsByPark(Park) ?? Read.CampgroundChoicesByPark(Park, Folder);
        internal List<string> States() => Seams?.States() ?? Read.UniqueStates(Folder);
        internal List<string> Cities(string State) => Seams?.Cities(State) ?? Read.UniqueCities(State, Folder);
        internal List<CampgroundChoice> CampgroundsByStateAndCity(string State, string City) => Seams?.CampgroundsByStateAndCity(State, City) ?? Read.CampgroundChoicesByStateAndCity(State, City, Folder);
        internal List<CampgroundChoice> CampgroundsByState(string State) => Seams?.CampgroundsByState(State) ?? Read.CampgroundChoicesByState(State, Folder);

        // the details of exactly this campground, by its FacilityID, with the view's own filters as their context
        internal CampgroundDetails Details(CampgroundChoice Campground, string? Context)
        {
            FacilitiesData? Facility = Seams != null ? Seams.Details(Campground.FacilityID) : Read.GetCampgroundDetailsById(Campground.FacilityID, Folder);
            return Facility == null ? CampgroundDetails.None : new CampgroundDetails(Facility, Context);
        }
    }

    // One page of the campgrounds pages: what the address asked for, checked against the lists as they are now. A filter
    // value is one of its list, exactly (ordinal), or none; a campground one of the list the filters give, by its exact
    // FacilityID, or none; a part of the list a number within it. Anything else is no choice: the page then shows the list
    // without it, and says so for a campground it can't show. Built for one request; immutable afterwards.
    internal sealed class WebLookupPage
    {
        // the most campgrounds a part of a list shows: a state's list can hold hundreds
        internal const int PageSize = 50;
        // what a chosen park or city is written as in an address and a form: "=" and the value, so the empty city (a city
        // of its own, "(No city listed)") is told apart from no choice
        internal const string ValueMark = "=";

        internal WebLookupView View { get; private init; }
        internal bool HasData { get; private init; } = true;
        internal IReadOnlyList<string> Parks { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> States { get; private init; } = Array.Empty<string>();
        internal IReadOnlyList<string> Cities { get; private init; } = Array.Empty<string>();
        internal string? Park { get; private init; }
        internal string? State { get; private init; }
        internal string? City { get; private init; }
        // the campgrounds the filters give (null before they are chosen), and the part shown
        internal IReadOnlyList<CampgroundChoice>? Campgrounds { get; private init; }
        internal int Part { get; private init; } = 1;
        internal int Parts => Campgrounds == null || Campgrounds.Count == 0 ? 1 : (Campgrounds.Count + PageSize - 1) / PageSize;
        internal IEnumerable<CampgroundChoice> Shown => Campgrounds == null ? Enumerable.Empty<CampgroundChoice>() : Campgrounds.Skip((Part - 1) * PageSize).Take(PageSize);
        internal CampgroundChoice? Chosen { get; private init; }
        internal CampgroundDetails Details { get; private init; } = CampgroundDetails.None;
        // the address named a campground, a park, a state or a city its list does not hold
        internal string? Notice { get; private init; }

        internal string? RangeText => Campgrounds == null || Campgrounds.Count <= PageSize ? null
            : CampgroundsWording.CampgroundsShown((Part - 1) * PageSize + 1, Math.Min(Part * PageSize, Campgrounds.Count), Campgrounds.Count);

        // a filter value from an address: "=" and the value, or none
        internal static string? Marked(string? Sent) => Sent != null && Sent.StartsWith(ValueMark, StringComparison.Ordinal) ? Sent.Substring(ValueMark.Length) : null;
        internal static string Mark(string Value) => ValueMark + Value;

        // the campground data could not be read: the page says so in fixed words (CampgroundsWording.NoData)
        internal bool IsUnreadable { get; private init; }
        internal static WebLookupPage Unreadable(WebLookupView View) => new WebLookupPage { View = View, IsUnreadable = true };

        internal static WebLookupPage Read(WebCampgrounds Lookup, WebLookupView View, string? SentPark, string? SentState, string? SentCity, string? SentId, int? SentPart)
        {
            if (!Lookup.HasData)
            {
                return new WebLookupPage { View = View, HasData = false };
            }
            bool Unknown = false;
            string? Park = null, State = null, City = null;
            IReadOnlyList<string> Parks = Array.Empty<string>(), States = Array.Empty<string>(), Cities = Array.Empty<string>();
            List<CampgroundChoice>? Campgrounds = null;
            string? Context = null;
            switch (View)
            {
                case WebLookupView.Park:
                    Parks = Lookup.Parks();
                    Park = Pick(Parks, Marked(SentPark), ref Unknown);
                    if (Park != null)
                    {
                        Campgrounds = Lookup.CampgroundsByPark(Park);
                        Context = Park;
                    }
                    break;
                case WebLookupView.City:
                    States = Lookup.States();
                    State = Pick(States, SentState, ref Unknown);
                    if (State != null)
                    {
                        Cities = Lookup.Cities(State);
                        City = Pick(Cities, Marked(SentCity), ref Unknown);
                        if (City != null)
                        {
                            Campgrounds = Lookup.CampgroundsByStateAndCity(State, City);
                            Context = CampgroundDetails.CityContext(City, State);
                        }
                    }
                    break;
                default:
                    States = Lookup.States();
                    State = Pick(States, SentState, ref Unknown);
                    if (State != null)
                    {
                        Campgrounds = Lookup.CampgroundsByState(State);
                        Context = State;
                    }
                    break;
            }
            CampgroundChoice? Chosen = null;
            if (SentId != null && Campgrounds != null)
            {
                Chosen = Campgrounds.FirstOrDefault(Each => string.Equals(Each.FacilityID, SentId, StringComparison.Ordinal));
                Unknown |= Chosen == null;
            }
            else if (SentId != null)
            {
                Unknown = true;
            }
            int Count = Campgrounds?.Count ?? 0;
            int Parts = Count == 0 ? 1 : (Count + PageSize - 1) / PageSize;
            int Part = SentPart is int Asked && Asked >= 1 && Asked <= Parts ? Asked
                : Chosen != null ? Campgrounds!.IndexOf(Chosen) / PageSize + 1 : 1;
            return new WebLookupPage
            {
                View = View,
                Parks = Parks,
                States = States,
                Cities = Cities,
                Park = Park,
                State = State,
                City = City,
                Campgrounds = Campgrounds,
                Part = Part,
                Chosen = Chosen,
                Details = Chosen == null ? CampgroundDetails.None : Lookup.Details(Chosen, Context),
                Notice = Unknown ? CampgroundsWording.NotInList : null
            };
        }

        // the value of the list that is exactly the one sent, or none (a value the list does not hold is no choice)
        private static string? Pick(IReadOnlyList<string> List, string? Sent, ref bool Unknown)
        {
            if (Sent == null || (Sent.Length == 0 && !List.Contains(Sent, StringComparer.Ordinal)))
            {
                return null;
            }
            string? Found = List.FirstOrDefault(Each => string.Equals(Each, Sent, StringComparison.Ordinal));
            Unknown |= Found == null;
            return Found;
        }

        // the address of this view with its filters, a campground and a part of the list
        internal string Address(string? Id = null, int? ShownPart = null)
        {
            var Query = new List<KeyValuePair<string, string?>>();
            if (View == WebLookupView.Park && Park != null)
            {
                Query.Add(new("park", Mark(Park)));
            }
            if (View != WebLookupView.Park && State != null)
            {
                Query.Add(new("state", State));
            }
            if (View == WebLookupView.City && City != null)
            {
                Query.Add(new("city", Mark(City)));
            }
            if (Id != null)
            {
                Query.Add(new("id", Id));
            }
            if (ShownPart is int P && P > 1)
            {
                Query.Add(new("part", P.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            return WebLookupPage.Path(View) + Microsoft.AspNetCore.Http.QueryString.Create(Query).ToUriComponent();
        }

        internal static string Path(WebLookupView View) => "/campgrounds/" + View.ToString().ToLowerInvariant();
    }
}
