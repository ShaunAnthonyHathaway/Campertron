// One campground of a GUI list (decision D42, TD-38). Its identity is its Recreation.gov FacilityID, the database's
// TEXT primary key, kept exactly as stored (a string: the search files' campgroundID is a string too, and an ID is never
// parsed as a number, so nothing like a leading zero can be lost). Names are not unique: in the shipped database 24
// names belong to more than one campground (one of them the empty name), and a name can also belong to another kind of
// facility. Equality and the hash code use the FacilityID only; the label is display text and is never parsed back.
// City and State come from the campground's Physical or Default address when it has one (null otherwise; nothing is
// invented). Immutable. Public because the GUI assembly binds to it, as it does to Read and FacilitiesData; it is the
// boundary a later campground picker (CM6) can reuse: a FacilityID, a label that tells duplicates apart, and an
// optional location.
public sealed class CampgroundChoice : IEquatable<CampgroundChoice>
{
    public const string UnnamedName = "Unnamed campground";

    public CampgroundChoice(string FacilityID, string? FacilityName, string? City, string? State)
    {
        // never a blank or invented identity; the ID is otherwise kept exactly (not trimmed, not parsed as a number)
        if (string.IsNullOrWhiteSpace(FacilityID))
        {
            throw new ArgumentException("A campground needs its FacilityID.", nameof(FacilityID));
        }
        this.FacilityID = FacilityID;
        Name = string.IsNullOrWhiteSpace(FacilityName) ? null : FacilityName.Trim();
        this.City = string.IsNullOrWhiteSpace(City) ? null : City.Trim();
        this.State = string.IsNullOrWhiteSpace(State) ? null : State.Trim();
    }

    // the identity, exactly as the database holds it
    public string FacilityID { get; }
    // the trimmed name; null for a blank name
    public string? Name { get; }
    public string? City { get; }
    public string? State { get; }

    public bool IsUnnamed => Name == null;
    // the name to show: "Unnamed campground" for a blank name
    public string DisplayName => Name ?? UnnamedName;
    // "City, ST", or "ST" without a city; null without a state (a city alone is not a usable location, and the
    // queries never return one: their addresses all have a state)
    public string? Location => State == null ? null : City == null ? State : City + ", " + State;
    // the name with its location, as a list shows it next to the ID: "WATCHMAN CAMPGROUND — SPRINGDALE, UT"
    public string Title => Location == null ? DisplayName : DisplayName + " \u2014 " + Location;
    public string IdText => "ID " + FacilityID;
    // the whole choice in one line, for accessibility, tooltips and type-ahead: "WATCHMAN CAMPGROUND — SPRINGDALE, UT — ID 232445"
    public string Label => Title + " \u2014 " + IdText;

    public bool Equals(CampgroundChoice? Other) => Other != null && string.Equals(FacilityID, Other.FacilityID, StringComparison.Ordinal);
    public override bool Equals(object? Other) => Equals(Other as CampgroundChoice);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FacilityID);
    public override string ToString() => Label;

    // the order of a list: by name (trimmed, case-insensitive; unnamed campgrounds last), then state, city and FacilityID,
    // so equal names still have one fixed order
    public static readonly IComparer<CampgroundChoice> ListOrder = Comparer<CampgroundChoice>.Create((A, B) =>
    {
        int Result = A.IsUnnamed.CompareTo(B.IsUnnamed);
        if (Result == 0)
        {
            Result = StringComparer.OrdinalIgnoreCase.Compare(A.Name ?? string.Empty, B.Name ?? string.Empty);
        }
        if (Result == 0)
        {
            Result = StringComparer.OrdinalIgnoreCase.Compare(A.State ?? string.Empty, B.State ?? string.Empty);
        }
        if (Result == 0)
        {
            Result = StringComparer.OrdinalIgnoreCase.Compare(A.City ?? string.Empty, B.City ?? string.Empty);
        }
        if (Result == 0)
        {
            Result = A.FacilityID.Length.CompareTo(B.FacilityID.Length);
        }
        return Result != 0 ? Result : string.CompareOrdinal(A.FacilityID, B.FacilityID);
    });
}
