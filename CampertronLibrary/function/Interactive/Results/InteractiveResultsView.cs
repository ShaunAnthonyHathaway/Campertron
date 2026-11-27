using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Campgrounds;
using RW = CampertronLibrary.function.Forms.ResultsWording;
using W = CampertronLibrary.function.Interactive.Results.InteractiveResultsWording;

namespace CampertronLibrary.function.Interactive.Results
{
    // The last run's results at a terminal (v0.4.0's phase 10, D126; business-rules §43): the session's shared ResultsOutcome,
    // which is read only (nothing here searches, reads a file or the campground data, or asks recreation.gov again):
    //  - its status line (the summary and the elapsed time), when it ended (ResultsWording.EndedAgo, when it is looked at
    //    later), its problems, and a choice of its searches when there are several (each by the shared ChoiceLabel);
    //  - a search's result in the shared words (its status, campground, counts, months checked and not checked, problems
    //    and warnings, why nothing is listed) and its rows a page at a time (the shared ResultsListing, PageSize openings or
    //    stays, in its order: single nights by night and campsite ID, consecutive nights by campsite with its stays), each
    //    row by its shared accessible name, with the shared Next rows and Previous rows and the range shown (never a
    //    silent cut);
    //  - a row's campsite: its shared texts, and its booking link only as the shared BookingLink accepts it (ResultsLink),
    //    written as plain text alone on its line so the terminal can copy it, else the shared words why it is not shown;
    //    its ID alone on request. No browser is opened and nothing is copied by Campertron (a terminal has no clipboard).
    // Every text of the results reaches the terminal with its control characters replaced.
    internal static class InteractiveResultsView
    {
        // JustEnded: the run's end was just said, so its status line is not said again
        internal static ListEnd Show(InteractivePrompts Prompts, InteractiveResultsState State, bool JustEnded)
        {
            if (State.Outcome is not ResultsOutcome Outcome)
            {
                Prompts.Say(W.NoResultsYet);
                return ListEnd.Back;
            }
            while (true)
            {
                Prompts.Heading(RW.LastRunHeading);
                if (!JustEnded)
                {
                    if (State.Status != null)
                    {
                        Prompts.Say(InteractiveResults.Safe(State.Status));
                    }
                    Prompts.Say(RW.EndedAgo(InteractiveResults.Seams.Time.GetElapsedTime(State.OutcomeEndedAt)));
                }
                JustEnded = false;
                foreach (string Line in Outcome.IssueLines)
                {
                    Prompts.Say(InteractiveResults.Safe(Line));
                }
                if (Outcome.Searches.Count == 1)
                {
                    return ShowSearch(Prompts, Outcome.Searches[0], W.BackToResults);
                }
                var Choices = Outcome.Searches.Select(S => InteractiveResults.Safe(S.ChoiceLabel)).Append(W.BackToResults).ToList();
                if (Outcome.Searches.Count > 0)
                {
                    Prompts.Say(W.ChooseSearch);
                }
                PromptResult<int> Picked = Prompts.Choose(RW.ResultsOfName, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    return ListEnd.Back;
                }
                if (ShowSearch(Prompts, Outcome.Searches[Picked.Value], W.BackToSearches) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        // one search's result and its rows, a page at a time, until Back (BackLabel), Esc, Ctrl+C or the end of the input
        private static ListEnd ShowSearch(InteractivePrompts Prompts, ResultSearchViewModel Search, string BackLabel)
        {
            int Page = 1;
            while (true)
            {
                Prompts.Heading(InteractiveResults.Safe(Search.Title));
                foreach (string Line in new[] { Search.StatusText, Search.CampgroundText, Search.CountsText, Search.MonthsText }.Concat(Search.IssueLines))
                {
                    Prompts.Say("  " + InteractiveResults.Safe(Line), "    ");
                }
                if (Search.HasEmptyText)
                {
                    Prompts.Say("  " + Search.EmptyText, "    ");
                }
                ResultsListing Listing = ResultsListing.Of(Search, Page, W.PageSize);
                Page = Listing.Page;
                if (Listing.RangeText != null)
                {
                    Prompts.Say(Listing.RangeText);
                }
                var Rows = new List<(ResultsListingGroup Group, ResultsListingItem Item)>();
                foreach (ResultsListingGroup Group in Listing.Groups)
                {
                    foreach (ResultsListingItem Item in Group.Items)
                    {
                        Rows.Add((Group, Item));
                    }
                }
                var Choices = Rows.Select(R => InteractiveResults.Safe(RowOf(R.Item).AccessibleName)).ToList();
                if (Rows.Count > 0)
                {
                    Prompts.Say(W.ChooseCampsite);
                }
                int NextAt = Listing.HasNext ? Choices.Count : -1;
                if (Listing.HasNext)
                {
                    Choices.Add(RW.NextRows);
                }
                int PreviousAt = Listing.HasPrevious ? Choices.Count : -1;
                if (Listing.HasPrevious)
                {
                    Choices.Add(RW.PreviousRows);
                }
                Choices.Add(BackLabel);
                PromptResult<int> Picked = Prompts.Choose(RW.OpeningsName, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    return ListEnd.Back;
                }
                if (Picked.Value == NextAt)
                {
                    Page++;
                }
                else if (Picked.Value == PreviousAt)
                {
                    Page--;
                }
                else if (Campsite(Prompts, Rows[Picked.Value].Group, Rows[Picked.Value].Item) == ListEnd.EndOfInput)
                {
                    return ListEnd.EndOfInput;
                }
            }
        }

        private static ResultRow RowOf(ResultsListingItem Item) => (ResultRow?)Item.Opening ?? Item.Stay!;

        // a row's campsite: its texts, its booking link (or why there is none) and its ID, until Back
        private static ListEnd Campsite(InteractivePrompts Prompts, ResultsListingGroup Group, ResultsListingItem Item)
        {
            ResultRow Row = RowOf(Item);
            //an opening's link is its own; a stay's is its campsite's (the group's), as the shared listing gives them
            ResultsLink? Link = Item.Link ?? Group.Link;
            while (true)
            {
                Prompts.Heading(InteractiveResults.Safe(Row.CampsiteTitle ?? W.Campsite));
                Prompts.Say("  " + W.CampsiteIdLabel + ": " + InteractiveResults.Safe(Row.CampsiteId), "    ");
                if (Item.Opening is ResultOpeningRow Opening)
                {
                    Prompts.Say("  " + InteractiveResults.Safe(Opening.AccessibleName), "    ");
                }
                if (Item.Stay is ResultStayRow Stay)
                {
                    Prompts.Say("  " + W.StayLabel + ": " + InteractiveResults.Safe(Stay.Text), "    ");
                    if (Group.Site is ResultSiteRow Site)
                    {
                        Prompts.Say("  " + W.StaysLabel + ": " + InteractiveResults.Safe(Site.StaysText), "    ");
                        if (Site.HasDetails)
                        {
                            Prompts.Say("  " + W.DetailsLabel + ": " + InteractiveResults.Safe(Site.Details), "    ");
                        }
                    }
                }
                bool Shown = Link?.Href != null;
                if (Shown)
                {
                    Prompts.Say(InteractiveResults.Safe(Link!.Text) + ":");
                    Alone(Prompts, Link.Href!);
                }
                else
                {
                    Prompts.Say(RW.NoBookingLink);
                }
                var Choices = new List<string>();
                if (Shown)
                {
                    Choices.Add(W.ShowLink);
                }
                Choices.Add(W.ShowId);
                Choices.Add(W.BackToRows);
                PromptResult<int> Picked = Prompts.Choose(W.Choices, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return ListEnd.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Choices.Count - 1)
                {
                    return ListEnd.Back;
                }
                Prompts.Say(W.CopyHow);
                Alone(Prompts, Shown && Picked.Value == 0 ? Link!.Href! : Row.CampsiteId ?? string.Empty);
            }
        }

        // a link or an ID alone on its line, never wrapped or indented, so the terminal copies it whole
        private static void Alone(InteractivePrompts Prompts, string Text) => Prompts.Terminal.WriteLine(InteractiveResults.Safe(Text));
    }
}
