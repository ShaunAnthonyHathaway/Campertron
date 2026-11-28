using CampertronLibrary.function.Interactive.Settings;
using W = CampertronLibrary.function.Interactive.Campgrounds.InteractiveCampgroundsWording;

namespace CampertronLibrary.function.Interactive.Campgrounds
{
    // how a list screen ended: an entry was chosen (its index in the whole list), or the user went back, or the input ended
    internal enum ListEnd { Chosen, Back, EndOfInput }

    // One list of the Campgrounds screens at a terminal (v0.4.0's phase 9, D125): a park's, a state's or a city's
    // campgrounds, the parks, the states, the cities, the campgrounds found, or a campground's campsite values. A terminal
    // has no scrolling list, so the list is shown a part at a time (W.PageSize entries at most), with the next and the
    // previous part and a find that narrows it to the entries that contain each typed word (ignoring case, as the shared
    // finds do); the whole list comes back on request. A list of choices numbers its entries; a list to read (the campsite
    // values) only lists them. Its texts are given already safe for a terminal (no control character), and it keeps where
    // it was, so a campground's details lead back to the same part of the list. Nothing here reads data: it only shows.
    internal sealed class InteractiveCampgroundList
    {
        private readonly IReadOnlyList<string> All;
        private List<int> Shown;
        private int Start;

        internal InteractiveCampgroundList(IReadOnlyList<string> Labels)
        {
            All = Labels;
            Shown = Enumerable.Range(0, Labels.Count).ToList();
        }

        internal int Count => All.Count;

        // since D130 (PT1): the screen the list is shown in (its caller's, or its own), shown at each round so the list starts
        // on a cleared screen when the user comes back to it; each other part of it is a new screen. Null: the screen around it
        internal InteractiveScreen? Screen { get; set; }

        // Show the list once more until an entry is chosen (Choosable), the user goes back (Back, Esc, Ctrl+C) or the input
        // ends. Notes: said before each part (what the list is, how to choose)
        internal (ListEnd End, int Index) Show(InteractivePrompts Prompts, string Title, bool Choosable, string BackLabel, params string[] Notes)
        {
            int ShownAt = -1;
            while (true)
            {
                if (Screen != null && ShownAt >= 0 && ShownAt != Start)
                {
                    //another part of the list: a new screen
                    Screen = Prompts.Screen();
                }
                Screen?.Show();
                ShownAt = Start;
                foreach (string Note in Notes)
                {
                    Prompts.Say(Note);
                }
                if (All.Count == 0)
                {
                    Prompts.Say(W.EmptyList);
                }
                List<int> Part = Shown.Skip(Start).Take(W.PageSize).ToList();
                if (Shown.Count > W.PageSize || Shown.Count != All.Count)
                {
                    Prompts.Say(Part.Count == 0 ? W.EmptyList : W.Shown(Start + 1, Start + Part.Count, Shown.Count));
                }
                var Choices = new List<string>();
                if (Choosable)
                {
                    Choices.AddRange(Part.Select(Index => All[Index]));
                }
                else
                {
                    Prompts.Heading(Title);
                    foreach (int Index in Part)
                    {
                        Prompts.Say("  " + All[Index], "    ");
                    }
                }
                int Next = Math.Min(W.PageSize, Shown.Count - Start - Part.Count);
                int NextAt = Next > 0 ? Choices.Count : -1;
                if (Next > 0)
                {
                    Choices.Add(W.NextPart(Next));
                }
                int PreviousAt = Start > 0 ? Choices.Count : -1;
                if (Start > 0)
                {
                    Choices.Add(W.PreviousPart(Math.Min(W.PageSize, Start)));
                }
                int FindAt = All.Count > W.PageSize ? Choices.Count : -1;
                if (FindAt >= 0)
                {
                    Choices.Add(W.FindInList);
                }
                int WholeAt = Shown.Count != All.Count ? Choices.Count : -1;
                if (WholeAt >= 0)
                {
                    Choices.Add(W.WholeList);
                }
                Choices.Add(BackLabel);
                PromptResult<int> Picked = Prompts.Choose(Choosable ? Title : W.ListChoices, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return (ListEnd.EndOfInput, -1);
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    return (ListEnd.Back, -1);
                }
                int At = Picked.Value;
                if (Choosable && At < Part.Count)
                {
                    return (ListEnd.Chosen, Part[At]);
                }
                if (At == NextAt)
                {
                    Start += W.PageSize;
                }
                else if (At == PreviousAt)
                {
                    Start = Math.Max(0, Start - W.PageSize);
                }
                else if (At == WholeAt)
                {
                    Shown = Enumerable.Range(0, All.Count).ToList();
                    Start = 0;
                }
                else if (At == FindAt)
                {
                    if (Find(Prompts) == ListEnd.EndOfInput)
                    {
                        return (ListEnd.EndOfInput, -1);
                    }
                }
            }
        }

        // the entries that contain each typed word, ignoring case (ordinal, never the current culture); Enter alone shows the
        // whole list, Esc keeps it as it is, and words nothing contains keep it too, said so
        private ListEnd Find(InteractivePrompts Prompts)
        {
            Prompts.Say(W.FindInListHow);
            PromptResult<string> Typed = Prompts.Text(W.FindQuestion, false);
            if (!Typed.IsAnswered)
            {
                return Typed.End == PromptEnd.EndOfInput ? ListEnd.EndOfInput : ListEnd.Back;
            }
            string[] Words = Typed.Value!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            List<int> Matches = Enumerable.Range(0, All.Count)
                .Where(Index => Words.All(Word => All[Index].Contains(Word, StringComparison.OrdinalIgnoreCase))).ToList();
            if (Matches.Count == 0)
            {
                Prompts.Say(W.NothingInList);
                return ListEnd.Back;
            }
            Shown = Matches;
            Start = 0;
            if (Words.Length > 0)
            {
                Prompts.Say(W.Found(Matches.Count));
            }
            return ListEnd.Back;
        }

        // a text from the campground data as a terminal may show it: every control character replaced (an escape sequence
        // or a line break of the data never reaches the terminal), as the settings and search forms' values are shown
        internal static string Safe(string? Text) => InteractiveSettingsForm.Visible(Text ?? string.Empty);
    }
}
