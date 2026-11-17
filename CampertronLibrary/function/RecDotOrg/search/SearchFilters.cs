namespace CampertronLibrary.function.RecDotOrg.search
{
    // what an include or exclude list of a search is matched against
    internal enum FilterField
    {
        //includeEquipment, excludeEquipment: the campsite's permitted equipment names
        Equipment,
        //includeSites, excludeSites: the campsite's name
        Sites,
        //includeAttributes, excludeAttributes: the campsite's attributes, each written "Name:Value"
        Attributes,
        //includeCampsiteType, excludeCampsiteType: the campsite's type
        CampsiteType
    }

    // The eight include and exclude lists of a search (decision D51, business-rules §3). Every list has OR semantics: an
    // include list keeps a campsite when ANY of its values matches ANY of the campsite's values, and an exclude list drops a
    // campsite when ANY of its values matches. An empty or missing list is no filter. Before D51 excludeEquipment and
    // excludeAttributes dropped a campsite only when EVERY value matched, and includeCampsiteType kept one only when it
    // matched EVERY value (an AND); the other five were already OR.
    //
    // A value matches by the search's filterMatching (FilterMatchMode), always ordinally (case, spaces and punctuation
    // count; nothing is trimmed):
    //   - Exact: the value equals the campsite's value.
    //   - Contains, the matching every search had before D51 (and still has without filterMatching): an equipment or
    //     attribute value contains the campsite's equipment name or "Name:Value" (the list value is the longer one, so
    //     "RV/MOTORHOME" matches a campsite that permits "RV"); a campsite type contains the value ("GROUP" matches
    //     "GROUP STANDARD NONELECTRIC"); a campsite name equals the value, as it always has (a name that contained the value
    //     would make "1" match "10" to "19" in a file written for exact names).
    // A null value on either side matches nothing (the validation refuses an empty item of the six lists FilterData used to
    // call Contains on, and the campground data has no null names, types, equipment or attributes).
    internal static class SearchFilters
    {
        internal static bool Matches(FilterField Field, FilterMatchMode Mode, string? ListValue, string? CampsiteValue)
        {
            if (ListValue == null || CampsiteValue == null)
            {
                return false;
            }
            if (Mode == FilterMatchMode.Exact || Field == FilterField.Sites)
            {
                return string.Equals(ListValue, CampsiteValue, StringComparison.Ordinal);
            }
            return Field == FilterField.CampsiteType
                ? CampsiteValue.Contains(ListValue, StringComparison.Ordinal)
                : ListValue.Contains(CampsiteValue, StringComparison.Ordinal);
        }

        // whether any value of the list matches any of the campsite's values
        internal static bool AnyMatches(IReadOnlyList<string>? List, FilterField Field, FilterMatchMode Mode, IEnumerable<string?>? CampsiteValues)
        {
            if (List == null || List.Count == 0 || CampsiteValues == null)
            {
                return false;
            }
            List<string?> Values = CampsiteValues as List<string?> ?? CampsiteValues.ToList();
            foreach (string ListValue in List)
            {
                foreach (string? CampsiteValue in Values)
                {
                    if (Matches(Field, Mode, ListValue, CampsiteValue))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // an include list keeps the campsite: no list, an empty list, or a value that matches
        internal static bool Includes(IReadOnlyList<string>? List, FilterField Field, FilterMatchMode Mode, IEnumerable<string?>? CampsiteValues) =>
            List == null || List.Count == 0 || AnyMatches(List, Field, Mode, CampsiteValues);

        // an exclude list drops the campsite: a value of a non-empty list matches
        internal static bool Excludes(IReadOnlyList<string>? List, FilterField Field, FilterMatchMode Mode, IEnumerable<string?>? CampsiteValues) =>
            List != null && List.Count > 0 && AnyMatches(List, Field, Mode, CampsiteValues);

        // the campsite-type lists (ProcessMonth applies them before the other conditions, as before)
        internal static bool KeepsCampsiteType(CampertronConfig Search, string? CampsiteType)
        {
            FilterMatchMode Mode = Search.EffectiveFilterMatching;
            string?[] Type = { CampsiteType };
            return !Excludes(Search.ExcludeCampsiteType, FilterField.CampsiteType, Mode, Type)
                && Includes(Search.IncludeCampsiteType, FilterField.CampsiteType, Mode, Type);
        }

        // the equipment, campsite-name and attribute lists (FilterData)
        internal static bool KeepsCampsite(CampertronConfig Search, AvailabilityData Entry)
        {
            FilterMatchMode Mode = Search.EffectiveFilterMatching;
            string?[] Name = { Entry.CampsiteName };
            List<string>? Equipment = Entry.PermittedEquipmentList;
            List<string>? Attributes = Entry.CampsiteAttributeLists?.AttValuePairStr;
            return Includes(Search.IncludeEquipment, FilterField.Equipment, Mode, Equipment)
                && !Excludes(Search.ExcludeEquipment, FilterField.Equipment, Mode, Equipment)
                && Includes(Search.IncludeSites, FilterField.Sites, Mode, Name)
                && !Excludes(Search.ExcludeSites, FilterField.Sites, Mode, Name)
                && Includes(Search.IncludeAttributes, FilterField.Attributes, Mode, Attributes)
                && !Excludes(Search.ExcludeAttributes, FilterField.Attributes, Mode, Attributes);
        }
    }
}
