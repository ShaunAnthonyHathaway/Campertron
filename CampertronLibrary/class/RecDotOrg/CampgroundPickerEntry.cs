// One campground of the search editor's campground picker (CM6, decision D44; TD-53, open question #41): its
// CampgroundChoice (the FacilityID identity, the label that tells duplicate names apart and the optional location of
// CM4, D42) and the name of its park, if it has one. Read.CampgroundPickerEntries lists every reservable, enabled
// campground this way, also the ones without a park or a usable address that no lookup tab lists. Nothing is invented:
// an entry without a park has no park, one without a location has none. Immutable; identity and equality are the
// choice's (the FacilityID). Public because the GUI binds to it.
public sealed class CampgroundPickerEntry
{
    public CampgroundPickerEntry(CampgroundChoice Choice, string? Park)
    {
        this.Choice = Choice ?? throw new ArgumentNullException(nameof(Choice));
        this.Park = string.IsNullOrWhiteSpace(Park) ? null : Park.Trim();
    }

    public CampgroundChoice Choice { get; }
    // the trimmed park (recreation area) name; null without a named park
    public string? Park { get; }

    public string FacilityID => Choice.FacilityID;
    public string Label => Choice.Label;
    public string Title => Choice.Title;
    public string IdText => Choice.IdText;
    public string? ParkText => Park == null ? null : "Park: " + Park;
    public bool HasPark => Park != null;
    // what a screen reader reads for the entry: the label and the park
    public string AccessibleName => Park == null ? Label : Label + " \u2014 park " + Park;

    public override bool Equals(object? Other) => Other is CampgroundPickerEntry Entry && Choice.Equals(Entry.Choice);
    public override int GetHashCode() => Choice.GetHashCode();
    public override string ToString() => Label;

    // Whether the entry matches a filter's terms: each term is part of its name (its shown name, so "unnamed" finds the
    // unnamed campgrounds), city, state or park, ignoring case (ordinal, never the current culture), or is exactly its
    // FacilityID. No term matches part of an ID: an ID is matched exactly or not at all.
    public bool Matches(IReadOnlyList<string> Terms)
    {
        foreach (string Term in Terms)
        {
            if (!(string.Equals(Term, FacilityID, StringComparison.Ordinal)
                || Choice.DisplayName.Contains(Term, StringComparison.OrdinalIgnoreCase)
                || (Choice.City?.Contains(Term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (Choice.State?.Contains(Term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (Park?.Contains(Term, StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                return false;
            }
        }
        return true;
    }

    // The entries a filter text shows, in the order given (CampgroundChoice.ListOrder from the query); an entry whose
    // FacilityID is exactly the whole text comes first. An empty or blank text shows every entry.
    public static List<CampgroundPickerEntry> Filter(IReadOnlyList<CampgroundPickerEntry> Entries, string? Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return Entries.ToList();
        }
        string[] Terms = Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        string Whole = Text.Trim();
        var Exact = new List<CampgroundPickerEntry>();
        var Others = new List<CampgroundPickerEntry>();
        foreach (CampgroundPickerEntry Entry in Entries)
        {
            if (string.Equals(Entry.FacilityID, Whole, StringComparison.Ordinal))
            {
                Exact.Add(Entry);
            }
            else if (Entry.Matches(Terms))
            {
                Others.Add(Entry);
            }
        }
        Exact.AddRange(Others);
        return Exact;
    }
}
