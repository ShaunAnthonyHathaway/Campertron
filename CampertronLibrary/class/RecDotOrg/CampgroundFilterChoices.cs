using System.Collections.ObjectModel;

// One value a search's include or exclude list can hold, taken from a campground's data (decision D51, business-rules
// §22): the value exactly as the search compares it (an equipment name, a campsite type, a campsite name, or an attribute
// written "Name:Value" as Cache.ConvertAttributeValuePairToString writes it), never trimmed or case-changed, and how many
// of the campground's campsites have it (for a campsite name: how many campsite records carry that name).
public sealed class FilterChoice
{
    public FilterChoice(string Value, int Campsites, string? AttributeName = null, string? AttributeValue = null)
    {
        this.Value = Value ?? throw new ArgumentNullException(nameof(Value));
        this.Campsites = Campsites;
        this.AttributeName = AttributeName;
        this.AttributeValue = AttributeValue;
    }
    public string Value { get; }
    public int Campsites { get; }
    // an attribute's name and value as stored (attributes only); Value is Name + ":" + Value
    public string? AttributeName { get; }
    public string? AttributeValue { get; }
    public override string ToString() => Value;

    // case variants next to each other, then ordinal, so the order is the same in every culture
    public static readonly IComparer<FilterChoice> ListOrder = Comparer<FilterChoice>.Create((A, B) =>
    {
        int Order = string.Compare(A.AttributeName ?? A.Value, B.AttributeName ?? B.Value, StringComparison.OrdinalIgnoreCase);
        if (Order == 0)
        {
            Order = string.Compare(A.AttributeName ?? A.Value, B.AttributeName ?? B.Value, StringComparison.Ordinal);
        }
        if (Order == 0)
        {
            Order = string.Compare(A.Value, B.Value, StringComparison.OrdinalIgnoreCase);
        }
        return Order != 0 ? Order : string.Compare(A.Value, B.Value, StringComparison.Ordinal);
    });
}

// The values a search's eight include and exclude lists can be set to for one campground (decision D51): what the
// campground's own campsites have in the campground data (Read.FilterChoicesByCampground). Rows of the equipment and attribute
// tables whose campsite is not a campsite of the campground are not part of it (the search never sees them).
public sealed class CampgroundFilterChoices
{
    public CampgroundFilterChoices(string FacilityID, int Campsites, IEnumerable<FilterChoice> Equipment, IEnumerable<FilterChoice> CampsiteTypes,
        IEnumerable<FilterChoice> CampsiteNames, IEnumerable<FilterChoice> Attributes)
    {
        this.FacilityID = FacilityID;
        this.Campsites = Campsites;
        this.Equipment = Sorted(Equipment);
        this.CampsiteTypes = Sorted(CampsiteTypes);
        this.CampsiteNames = Sorted(CampsiteNames);
        this.Attributes = Sorted(Attributes);
    }
    public string FacilityID { get; }
    // the campground's campsite records
    public int Campsites { get; }
    public IReadOnlyList<FilterChoice> Equipment { get; }
    public IReadOnlyList<FilterChoice> CampsiteTypes { get; }
    public IReadOnlyList<FilterChoice> CampsiteNames { get; }
    public IReadOnlyList<FilterChoice> Attributes { get; }

    // the choices of a search file's list key (includeEquipment, excludeSites, ...), or null for another key
    public IReadOnlyList<FilterChoice>? For(string Key) => Key switch
    {
        "includeEquipment" or "excludeEquipment" => Equipment,
        "includeSites" or "excludeSites" => CampsiteNames,
        "includeAttributes" or "excludeAttributes" => Attributes,
        "includeCampsiteType" or "excludeCampsiteType" => CampsiteTypes,
        _ => null
    };

    public static CampgroundFilterChoices None(string FacilityID) =>
        new CampgroundFilterChoices(FacilityID, 0, Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>(), Array.Empty<FilterChoice>());

    private static IReadOnlyList<FilterChoice> Sorted(IEnumerable<FilterChoice> Choices) =>
        new ReadOnlyCollection<FilterChoice>(Choices.OrderBy(C => C, FilterChoice.ListOrder).ToList());
}
