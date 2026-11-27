using System.Globalization;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using CW = CampertronLibrary.function.Forms.CampgroundsWording;
using SW = CampertronLibrary.function.Forms.SearchesWording;
using W = CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundsWording;

namespace CampertronLibrary.function.Interactive.Campgrounds
{
    // The campground data the Campgrounds screens read (tests give their own): whether it is prepared (the database file
    // exists; never opened) and the library's own read-only queries, the ones the desktop GUI's Campgrounds tab, the web UI's
    // campgrounds pages and the search editors' picker and filters run, on the settings folder's RecreationDotOrg.db
    internal sealed class CampgroundLookupSource
    {
        internal Func<string, bool> Prepared { get; init; } = Folder => Load.DbExists(new CampertronInternalConfig { ConfigPath = Folder });
        internal Func<string, List<CampgroundPickerEntry>> Campgrounds { get; init; } = Read.CampgroundPickerEntries;
        internal Func<string, List<string>> Parks { get; init; } = Read.ParksWithCampgrounds;
        internal Func<string, string, List<CampgroundChoice>> CampgroundsByPark { get; init; } = (Folder, Park) => Read.CampgroundChoicesByPark(Park, Folder);
        internal Func<string, List<string>> States { get; init; } = Read.UniqueStates;
        internal Func<string, string, List<string>> Cities { get; init; } = (Folder, State) => Read.UniqueCities(State, Folder);
        internal Func<string, string, string, List<CampgroundChoice>> CampgroundsByStateAndCity { get; init; } = (Folder, State, City) => Read.CampgroundChoicesByStateAndCity(State, City, Folder);
        internal Func<string, string, List<CampgroundChoice>> CampgroundsByState { get; init; } = (Folder, State) => Read.CampgroundChoicesByState(State, Folder);
        internal Func<string, string, FacilitiesData?> Details { get; init; } = (Folder, Id) => Read.GetCampgroundDetailsById(Id, Folder);
        internal Func<string, string, CampgroundFilterChoices> Campsites { get; init; } = (Folder, Id) => Read.FilterChoicesByCampground(Id, Folder);
    }

    // The interactive console's Campgrounds (v0.4.0's phase 9, D125; business-rules §43): the campground lookup of the
    // desktop GUI's Campgrounds tab and the web UI's campgrounds pages, the campground data's status, and the refresh of the
    // campground data (InteractiveCampgroundRefresh) that the desktop GUI's Settings > System and the web UI's system page
    // start. Entering it, the lookup and the status read and change nothing but what they show:
    //  - The lookup finds a campground by words of its name, city, state or park or by its exact ID (the search editors'
    //    picker: CampgroundPickerEntry.Filter over every reservable, enabled campground), or lists them by park, by city or
    //    by state (the Campgrounds tab's three ways and queries); a list is shown a part at a time. A campground is its
    //    FacilityID (ER-08): its details are read by it, never by a name, and only for a campground of the list shown.
    //  - A campground's details are the shared snapshot (CampgroundDetails, CampgroundsWording: its name, the list's
    //    context, its ID, phone, email, description, directions and fees, "Not listed" for what is missing), and its
    //    campsites' values, on request, the search editors' filter values with their counts (Read.FilterChoicesByCampground).
    //    No link and no other detail: the shared lookup has none.
    //  - The campground data is read only when it is prepared (the database file exists): the lookup and the status never
    //    prepare, extract, repair, index or refresh it, and never open a database that is not there (SQLite would create an
    //    empty one). A database that can't be read is said in fixed words (CampgroundsWording.NoData); never an exception's
    //    text, a path or a query. No request reaches Recreation.gov but the refresh's own download.
    //  - Every text of the campground data reaches the terminal with its control characters replaced.
    internal static class InteractiveCampgrounds
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.CampgroundsTitle, W.Campgrounds, W.BackToCampgrounds,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        internal static CampgroundLookupSource Source = new CampgroundLookupSource();

        // the main menu's Campgrounds
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            Prompts.Heading(W.CampgroundsTitle);
            string? Folder = InteractiveSettingsMenu.SettingsFolder(Session, Words);
            if (Folder == null)
            {
                return InteractiveStep.Done;
            }
            var Choices = new List<string> { W.LookUp, W.DataStatus, W.RefreshOrInstall, InteractiveSettingsWording.BackToMainMenu };
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Prompts.Heading(W.CampgroundsTitle);
                }
                First = false;
                Prompts.Say(W.CampgroundsNote);
                PromptResult<int> Picked = Prompts.Choose(W.CampgroundsChoices, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == 3)
                {
                    //Back, Esc or Ctrl+C: the main menu again
                    return InteractiveStep.Done;
                }
                if (Picked.Value == 0)
                {
                    if (LookUp(Prompts, Folder) == ListEnd.EndOfInput)
                    {
                        return InteractiveStep.EndOfInput;
                    }
                }
                else if (Picked.Value == 1)
                {
                    ShowStatus(Session, Folder);
                }
                else
                {
                    InteractiveStep Step = InteractiveCampgroundRefresh.Run(Session, Folder);
                    if (Step != InteractiveStep.Done)
                    {
                        return Step;
                    }
                }
            }
        }

        // ---- the campground data's status ----

        // Read only: whether the campground data is prepared (the file exists; never opened when it is not), whether it can
        // be read (the picker's one query: how many reservable, enabled campgrounds it lists), when it was last refreshed
        // (General.yaml read now, read-only, as both other UIs' card shows it), what it needs and what follows a refresh.
        // Nothing is created, prepared, repaired, refreshed or written
        internal static List<(string Label, string Value)> Status(InteractiveSession Session, string Folder)
        {
            bool Prepared = IsPrepared(Folder);
            int? Campgrounds = null;
            if (Prepared)
            {
                try
                {
                    Campgrounds = Source.Campgrounds(Folder).Count;
                }
                catch (Exception ex) when (IsReadProblem(ex))
                {
                    //the campground data can't be read: said in fixed words below
                }
            }
            return new List<(string, string)>
            {
                (W.PreparedLabel, Prepared ? W.Prepared : W.NotPrepared),
                (W.ReadableLabel, !Prepared ? W.NotChecked : Campgrounds is int Count ? W.Readable(Count) : W.NotReadable),
                (RefreshWording.LastRefreshedLabel, CurrentConfiguration.LastRefreshed(Folder, Session.UtcNow(), CultureInfo.CurrentCulture)),
                (W.NeededLabel, !Prepared ? W.NeedsInstall : Campgrounds == null ? W.NeedsAttention : W.NeedsNothing),
                (W.RestartLabel, W.NoRestartHere)
            };
        }

        private static void ShowStatus(InteractiveSession Session, string Folder)
        {
            Session.Prompts.Heading(W.StatusTitle);
            foreach ((string Label, string Value) in Status(Session, Folder))
            {
                Session.Prompts.Say("  " + Label + ": " + Value, "    ");
            }
        }

        // ---- the lookup ----

        private static ListEnd LookUp(InteractivePrompts Prompts, string Folder)
        {
            if (!IsPrepared(Folder))
            {
                Prompts.Heading(W.LookUp);
                Prompts.Say(W.DataMissing);
                return ListEnd.Back;
            }
            var Ways = new List<string> { W.FindChoice, CW.ByPark, CW.ByCity, CW.ByState, W.BackToCampgrounds };
            //the picker's campgrounds, read once for this visit of the lookup (the next visit reads them again)
            List<CampgroundPickerEntry>? Catalog = null;
            while (true)
            {
                PromptResult<int> Way = Prompts.Choose(W.LookUp, Ways);
                if (Way.End == PromptEnd.EndOfInput || Way.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Way.IsAnswered || Way.Value == Ways.Count - 1)
                {
                    return ListEnd.Back;
                }
                ListEnd End;
                try
                {
                    End = Way.Value switch
                    {
                        0 => Find(Prompts, Folder, ref Catalog),
                        1 => ByPark(Prompts, Folder),
                        2 => ByCity(Prompts, Folder),
                        _ => ByState(Prompts, Folder)
                    };
                }
                catch (CampgroundDataUnreadable)
                {
                    //the campground data can't be read (a damaged file, one another program is replacing): fixed words
                    Prompts.Say(CW.NoData);
                    End = ListEnd.Back;
                }
                if (End == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // Find: words of a name, city, state or park, or an exact ID (the search editors' picker's rule,
        // CampgroundPickerEntry.Filter); an ID that is exactly one campground's opens its details at once. Back from the
        // details or the list asks again; Esc at the question goes back to the ways to look
        private static ListEnd Find(InteractivePrompts Prompts, string Folder, ref List<CampgroundPickerEntry>? Catalog)
        {
            Catalog ??= Data(() => Source.Campgrounds(Folder));
            while (true)
            {
                Prompts.Say(W.FindHow);
                PromptResult<string> Typed = Prompts.Text(W.FindQuestion, false);
                if (!Typed.IsAnswered)
                {
                    return Typed.End == PromptEnd.EndOfInput ? ListEnd.EndOfInput : ListEnd.Back;
                }
                string Text = Typed.Value!;
                List<CampgroundPickerEntry> Matches = CampgroundPickerEntry.Filter(Catalog, Text);
                if (Matches.Count == 0)
                {
                    Prompts.Say(W.NoMatches);
                    continue;
                }
                if (Text.Length > 0 && string.Equals(Matches[0].FacilityID, Text, StringComparison.Ordinal))
                {
                    if (Details(Prompts, Folder, Matches[0].Choice, Context(Matches[0])) == ListEnd.EndOfInput)
                    {
                        return ListEnd.EndOfInput;
                    }
                    continue;
                }
                var List = new InteractiveCampgroundList(Matches.Select(Entry => InteractiveCampgroundList.Safe(Entry.AccessibleName)).ToList());
                while (true)
                {
                    (ListEnd End, int Index) = List.Show(Prompts, CW.Heading, true, W.Back, W.ChooseCampground);
                    if (End == ListEnd.EndOfInput)
                    {
                        return ListEnd.EndOfInput;
                    }
                    if (End == ListEnd.Back)
                    {
                        break;
                    }
                    if (Details(Prompts, Folder, Matches[Index].Choice, Context(Matches[Index])) == ListEnd.EndOfInput)
                    {
                        return ListEnd.EndOfInput;
                    }
                }
            }
        }

        // a found campground's context: its park, else its city and state (the Campgrounds tab's contexts)
        private static string? Context(CampgroundPickerEntry Entry) => Entry.Park ?? Entry.Choice.Location;

        // By Park: the parks with a reservable, enabled campground, then a park's campgrounds
        private static ListEnd ByPark(InteractivePrompts Prompts, string Folder)
        {
            List<string> Parks = Data(() => Source.Parks(Folder));
            var List = new InteractiveCampgroundList(Parks.Select(InteractiveCampgroundList.Safe).ToList());
            while (true)
            {
                (ListEnd End, int Index) = List.Show(Prompts, CW.ByPark, true, W.Back, CW.ByParkHint);
                if (End != ListEnd.Chosen)
                {
                    return End;
                }
                string Park = Parks[Index];
                if (Campgrounds(Prompts, Folder, Data(() => Source.CampgroundsByPark(Folder, Park)), Park) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // By City: a state, one of its cities (the empty city is a city of its own, "(No city listed)"), then its campgrounds
        private static ListEnd ByCity(InteractivePrompts Prompts, string Folder)
        {
            List<string> States = Data(() => Source.States(Folder));
            var StateList = new InteractiveCampgroundList(States.Select(InteractiveCampgroundList.Safe).ToList());
            while (true)
            {
                (ListEnd End, int Index) = StateList.Show(Prompts, CW.ByCity, true, W.Back, CW.ByCityHint);
                if (End != ListEnd.Chosen)
                {
                    return End;
                }
                string State = States[Index];
                List<string> Cities = Data(() => Source.Cities(Folder, State));
                var CityList = new InteractiveCampgroundList(Cities.Select(City => InteractiveCampgroundList.Safe(CW.City(City))).ToList());
                while (true)
                {
                    (ListEnd CityEnd, int CityIndex) = CityList.Show(Prompts, CW.ByCity + ": " + InteractiveCampgroundList.Safe(State), true, W.Back);
                    if (CityEnd == ListEnd.EndOfInput)
                    {
                        return ListEnd.EndOfInput;
                    }
                    if (CityEnd == ListEnd.Back)
                    {
                        break;
                    }
                    string City = Cities[CityIndex];
                    if (Campgrounds(Prompts, Folder, Data(() => Source.CampgroundsByStateAndCity(Folder, State, City)), CampgroundDetails.CityContext(City, State)) == ListEnd.EndOfInput)
                    {
                        return ListEnd.EndOfInput;
                    }
                }
            }
        }

        // By State: a state, then its campgrounds
        private static ListEnd ByState(InteractivePrompts Prompts, string Folder)
        {
            List<string> States = Data(() => Source.States(Folder));
            var List = new InteractiveCampgroundList(States.Select(InteractiveCampgroundList.Safe).ToList());
            while (true)
            {
                (ListEnd End, int Index) = List.Show(Prompts, CW.ByState, true, W.Back, CW.ByStateHint);
                if (End != ListEnd.Chosen)
                {
                    return End;
                }
                string State = States[Index];
                if (Campgrounds(Prompts, Folder, Data(() => Source.CampgroundsByState(Folder, State)), State) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // a list of campgrounds (a park's, a city's, a state's) with their context; a campground's details by its FacilityID
        private static ListEnd Campgrounds(InteractivePrompts Prompts, string Folder, List<CampgroundChoice> Choices, string? Context)
        {
            var List = new InteractiveCampgroundList(Choices.Select(Choice => InteractiveCampgroundList.Safe(Choice.Label)).ToList());
            string Title = CW.Heading + (string.IsNullOrWhiteSpace(Context) ? string.Empty : ": " + InteractiveCampgroundList.Safe(Context));
            while (true)
            {
                (ListEnd End, int Index) = List.Show(Prompts, Title, true, W.Back, W.ChooseCampground);
                if (End != ListEnd.Chosen)
                {
                    return End;
                }
                if (Details(Prompts, Folder, Choices[Index], Context) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // ---- a campground's details ----

        // the shared snapshot of exactly this campground (its FacilityID), then, on request, its campsites' values
        private static ListEnd Details(InteractivePrompts Prompts, string Folder, CampgroundChoice Choice, string? Context)
        {
            FacilitiesData? Facility;
            try
            {
                Facility = Source.Details(Folder, Choice.FacilityID);
            }
            catch (Exception ex) when (IsReadProblem(ex))
            {
                Prompts.Say(W.DetailsUnreadable);
                return ListEnd.Back;
            }
            if (Facility == null || !string.Equals(Facility.FacilityID, Choice.FacilityID, StringComparison.Ordinal))
            {
                //no longer such a campground (another program refreshed the data meanwhile): never another record
                Prompts.Say(W.CampgroundGone);
                return ListEnd.Back;
            }
            var D = new CampgroundDetails(Facility, Context);
            Prompts.Heading(InteractiveCampgroundList.Safe(D.Name));
            if (D.HasContext)
            {
                Prompts.Say("  " + InteractiveCampgroundList.Safe(D.Context), "    ");
            }
            Prompts.Say("  " + CW.CampgroundIdLabel + ": " + InteractiveCampgroundList.Safe(D.CampgroundId), "    ");
            Prompts.Say("  " + CW.CampgroundIdNote, "    ");
            Prompts.Say("  " + CW.ContactHeading);
            Prompts.Say("    " + CW.PhoneLabel + ": " + OneLine(D.Phone), "      ");
            Prompts.Say("    " + CW.EmailLabel + ": " + OneLine(D.Email), "      ");
            Section(Prompts, CW.DescriptionHeading, D.Description);
            Section(Prompts, CW.DirectionsHeading, D.Directions);
            Section(Prompts, CW.FeesHeading, D.Fees);
            CampgroundFilterChoices? Values = null;
            try
            {
                Values = Source.Campsites(Folder, Choice.FacilityID);
            }
            catch (Exception ex) when (IsReadProblem(ex))
            {
                //said below in fixed words
            }
            Prompts.Say("  " + SW.CampsitesHeading);
            Prompts.Say("    " + (Values == null ? W.CampsitesUnreadable : W.CampsiteCount(Values.Campsites)), "      ");
            return CampsiteValues(Prompts, D, Values);
        }

        // a heading of the details and its text, line by line as the data has them (blank lines kept, wrapped); a tab or a
        // carriage return is a space, any other control character is replaced (InteractiveCampgroundList.Safe)
        private static void Section(InteractivePrompts Prompts, string Heading, string Text)
        {
            Prompts.Say("  " + Heading);
            foreach (string Line in Text.Split('\n'))
            {
                string Plain = Spaces(Line).Trim();
                Prompts.Say(Plain.Length == 0 ? string.Empty : "    " + InteractiveCampgroundList.Safe(Plain), "    ");
            }
        }

        // a value the data may hold on several lines (a phone, an email), on one line
        private static string OneLine(string Text) => string.Join(" ", Text.Split('\n').Select(Line => InteractiveCampgroundList.Safe(Spaces(Line).Trim())).Where(Line => Line.Length > 0));

        private static string Spaces(string Line) => Line.Replace('\t', ' ').Replace('\r', ' ');

        // The campground's campsite values by kind (the search editors' filter values: equipment, campsite types, campsite
        // names, attributes), each kind a list to read, a part at a time, with the campsites that have each value
        private static ListEnd CampsiteValues(InteractivePrompts Prompts, CampgroundDetails D, CampgroundFilterChoices? Values)
        {
            var Kinds = new List<(string Heading, IReadOnlyList<FilterChoice> Choices, bool IsName)>();
            if (Values != null)
            {
                Kinds.Add((SW.EquipmentHeading, Values.Equipment, false));
                Kinds.Add((SW.CampsiteTypesHeading, Values.CampsiteTypes, false));
                Kinds.Add((SW.CampsitesHeading, Values.CampsiteNames.OrderBy(C => C.Value, NaturalOrder.Instance).ToList(), true));
                Kinds.Add((SW.AttributesHeading, Values.Attributes, false));
                Kinds.RemoveAll(Kind => Kind.Choices.Count == 0);
            }
            if (Kinds.Count > 0)
            {
                Prompts.Say("    " + W.ValuesNote, "      ");
            }
            List<string> Choices = Kinds.Select(Kind => W.Values(Kind.Heading, Kind.Choices.Count)).Append(W.BackToList).ToList();
            string Title = InteractiveCampgroundList.Safe(D.Name);
            while (true)
            {
                PromptResult<int> Picked = Prompts.Choose(Title, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Kinds.Count)
                {
                    return ListEnd.Back;
                }
                (string Heading, IReadOnlyList<FilterChoice> Kind, bool IsName) = Kinds[Picked.Value];
                var List = new InteractiveCampgroundList(Kind.Select(Choice => InteractiveCampgroundList.Safe(W.Value(FilterText.Display(Choice.Value), Choice.Campsites, IsName))).ToList());
                if (List.Show(Prompts, Title + ": " + Heading, false, W.Back).End == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // ---- the campground data ----

        // prepared: the database file exists (never opened)
        internal static bool IsPrepared(string Folder)
        {
            try
            {
                return Source.Prepared(Folder);
            }
            catch (Exception ex) when (IsReadProblem(ex))
            {
                return false;
            }
        }

        // a list of the lookup read from the campground data; one that can't be read ends the way it was asked for, which the
        // lookup says in fixed words (CampgroundsWording.NoData). Only the data's reads are caught here, never the terminal's
        private sealed class CampgroundDataUnreadable : Exception
        {
        }

        private static T Data<T>(Func<T> Read)
        {
            try
            {
                return Read();
            }
            catch (Exception ex) when (IsReadProblem(ex))
            {
                throw new CampgroundDataUnreadable();
            }
        }

        // what a read of the campground data throws when it can't be read (the web UI's campgrounds pages catch the same)
        internal static bool IsReadProblem(Exception ex) =>
            ex is System.Data.Common.DbException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException;
    }
}
