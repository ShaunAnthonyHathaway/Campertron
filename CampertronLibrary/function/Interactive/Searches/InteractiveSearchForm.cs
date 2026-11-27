using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Settings;
using IW = CampertronLibrary.function.Interactive.Settings.InteractiveSettingsWording;
using SW = CampertronLibrary.function.Forms.SearchesWording;
using W = CampertronLibrary.function.Interactive.Searches.InteractiveSearchesWording;

namespace CampertronLibrary.function.Interactive.Searches
{
    // The interactive console's host of one search file's form (v0.4.0's phase 8, D124; business-rules §43): a
    // SearchFileViewModel of the shared SearchesViewModel (ER-37), the form the desktop GUI's Searches tab and the web UI's
    // searches pages show, hosted at a terminal by the console's settings form host (InteractiveSettingsForm: its fields'
    // questions, Show the changes Save would write, Save, Revert, Reload, the conflict's resolutions, the leave question, all
    // as phase 7 asks them), with what only a search's form has:
    //  - the look at the disk is the Searches form's own (every file, the folder, and a campground data that changed): a
    //    clean file deleted on disk leaves the list, so its form says so and the list comes again;
    //  - the days of the week as one group, and the campsite filters (filterMatching and the eight include and exclude
    //    lists) as another, as both other UIs group them (SearchesWording's headings), each a screen of its own;
    //  - the campground picker (CampgroundSettingField): Find a campground, then a match by its number, or a campground ID
    //    exactly as typed; the form looks it up by its FacilityID only (ER-08) and says why one can't be used;
    //  - a filter selector (FilterSelectorField, ER-23): the campground's values checked or unchecked by number (a find
    //    narrows them, Select all shown and Clear shown act on the shown ones), a value added exactly as typed, a value the
    //    campground does not have removed; the list's items stay as the form keeps them;
    //  - Save As (SearchesViewModel's: its words, its name rules, create-only), asked once, No by default; the new file's
    //    form follows at once, as both other UIs show it;
    //  - the warning that a change would leave the monitoring no search to check (D73), which never blocks Save.
    // Every word of the form is shown as the form gives it, a value of the file with its control characters replaced; what is
    // typed goes to the form exactly as typed (the form trims a number, keeps a name as entered and refuses an ID or a file
    // name with spaces around it). Nothing here parses, validates, reads or writes a search file.
    internal sealed class InteractiveSearchForm : InteractiveSettingsForm
    {
        // the most values of a filter its line in the form's summary names (the filter's screen shows them all)
        private const int SummaryValues = 8;

        private readonly SearchesViewModel Searches;
        private readonly SearchFileViewModel Search;
        private readonly Func<bool> DataPrepared;
        // the file Save As wrote, whose form follows this one
        private SearchFileViewModel? Written;

        private InteractiveSearchForm(InteractivePrompts Prompts, SearchesViewModel Searches, SearchFileViewModel Search, Func<bool> DataPrepared)
            : base(Prompts, Search, W.BackToSearches, () => DateTime.Now, () => false, () => false)
        {
            this.Searches = Searches;
            this.Search = Search;
            this.DataPrepared = DataPrepared;
        }

        // The file's form until the user leaves it or the input ends; Written: the file Save As wrote (its form is next).
        // Searches.SelectedFile is the file (its filter selectors read their campground's values)
        internal static (ScreenEnd End, SearchFileViewModel? Written) Run(InteractivePrompts Prompts, SearchesViewModel Searches, SearchFileViewModel Search, Func<bool> DataPrepared)
        {
            var Host = new InteractiveSearchForm(Prompts, Searches, Search, DataPrepared);
            ScreenEnd End = Host.Loop();
            return (End, End == ScreenEnd.Back ? Host.Written : null);
        }

        // a search's name and file name come from the file and the disk: shown with their control characters replaced
        private protected override string Safe(string Text) => Visible(Text);

        private protected override bool TextAsTyped => true;

        // the Searches form looks at every file and the folder (a clean file deleted on disk leaves its list)
        private protected override bool LookAtDisk()
        {
            Searches.CheckForExternalChanges();
            if (Searches.Files.Contains(Search))
            {
                return true;
            }
            if (Search.HasStatus)
            {
                Prompts.Say(Safe(StatusText()));
            }
            return false;
        }

        private protected override void ShowIntroduction()
        {
            Prompts.Heading(Safe(Search.Title + " (" + Search.FileName + ")"));
        }

        // D73: a change that would leave the monitoring no search to check, once saved (a warning only)
        private protected override void ShowAfterNotices()
        {
            if (Search.HasMonitoringWarning)
            {
                Prompts.Say(Safe(Search.MonitoringWarningText));
            }
        }

        // the fields in the form's order, the days of the week as one line and the campsite filters as one group
        private protected override void ShowFields()
        {
            if (Search.IsMissing)
            {
                return;
            }
            foreach (SettingField Field in Search.FieldList)
            {
                switch (Field)
                {
                    case CampgroundSettingField Picker:
                        ShowCampground(Picker);
                        break;
                    case BoolSettingField Day when Search.Weekdays.Contains(Day):
                        if (ReferenceEquals(Day, Search.Weekdays[0]))
                        {
                            ShowDays();
                        }
                        break;
                    case FilterSelectorField:
                        break;
                    case ChoiceSettingField Matching when ReferenceEquals(Matching, Search.Matching):
                        ShowFilters();
                        break;
                    default:
                        ShowField(Field);
                        break;
                }
            }
        }

        private void ShowCampground(CampgroundSettingField Picker)
        {
            Prompts.Say("  " + Picker.AccessibleName + ": " + Safe(Picker.CurrentText) + (Picker.IsChanged ? IW.Changed : string.Empty), "    ");
            ShowMessages(Picker, "    ");
        }

        private void ShowDays()
        {
            List<string> Checked = Search.Weekdays.Where(Day => Day.IsChecked).Select(Day => Day.AccessibleName).ToList();
            Prompts.Say("  " + SW.WeekdaysHeading + ": " + (Checked.Count == 0 ? W.NoDays : string.Join(", ", Checked))
                + (Search.Weekdays.Any(Day => Day.IsChanged) ? IW.Changed : string.Empty), "    ");
            if (Search.HasWeekdaysMessage)
            {
                Prompts.Say("    " + Safe(Search.WeekdaysMessageText), "      ");
            }
            foreach (BoolSettingField Day in Search.Weekdays)
            {
                ShowMessages(Day, "    ");
            }
        }

        private void ShowFilters()
        {
            Prompts.Say("  " + SW.FiltersHeading + ":", "    ");
            ShowMatching("    ");
            if (Search.HasChoicesProblem)
            {
                Prompts.Say("    " + Safe(Search.Selectors[0].StatusText), "      ");
            }
            foreach (FilterSelectorField Selector in Grouped().SelectMany(Group => Group.Lists))
            {
                ShowSelectorLine(Selector, "    ");
            }
        }

        private void ShowMatching(string Indent)
        {
            Prompts.Say(Indent + Search.Matching.AccessibleName + ": " + ValueOf(Search.Matching) + (Search.Matching.IsChanged ? IW.Changed : string.Empty), Indent + "  ");
            ShowMessages(Search.Matching, Indent + "  ");
        }

        private void ShowSelectorLine(FilterSelectorField Selector, string Indent)
        {
            Prompts.Say(Indent + Selector.AccessibleName + ": " + ValuesOf(Selector) + (Selector.IsChanged ? IW.Changed : string.Empty), Indent + "  ");
            if (Selector.HasConflict)
            {
                Prompts.Say(Indent + "  " + Safe(Selector.ConflictText!), Indent + "    ");
            }
            ShowMessages(Selector, Indent + "  ");
        }

        // a field's own message (its severity's word first) and why it is locked
        private void ShowMessages(SettingField Field, string Indent)
        {
            if (Field.HasMessage)
            {
                Prompts.Say(Indent + Safe(Field.MessageText), Indent + "  ");
            }
            if (Field.HasLockReason)
            {
                Prompts.Say(Indent + Field.LockReason, Indent + "  ");
            }
        }

        // a filter's values as its selector shows them (the campground's values it holds, then its other values), the first
        // few of them; never a find's narrowing (a find lasts only while its values are chosen)
        private string ValuesOf(FilterSelectorField Selector)
        {
            List<string> Values = Selector.Options.Where(Option => Option.IsChoice && Option.IsChecked == true).Select(Option => Option.Prefix + Option.Label)
                .Concat(Selector.OtherValues.Select(Other => Other.Label)).ToList();
            if (Values.Count == 0)
            {
                return IW.Empty;
            }
            return Safe(string.Join(", ", Values.Take(SummaryValues)) + (Values.Count > SummaryValues ? ", " + W.AndMore(Values.Count - SummaryValues) : string.Empty));
        }

        // the four groups of filters, as both other UIs show them: equipment, campsites, campsite types, attributes
        private List<(string Heading, FilterSelectorField[] Lists)> Grouped() => new()
        {
            (SW.EquipmentHeading, new[] { Search.IncludeEquipment, Search.ExcludeEquipment }),
            (SW.CampsitesHeading, new[] { Search.IncludeSites, Search.ExcludeSites }),
            (SW.CampsiteTypesHeading, new[] { Search.IncludeCampsiteType, Search.ExcludeCampsiteType }),
            (SW.AttributesHeading, new[] { Search.IncludeAttributes, Search.ExcludeAttributes })
        };

        // ---- the form's choices ----

        private protected override void AddFieldChoices(List<Choice> Choices)
        {
            foreach (SettingField Field in Search.FieldList)
            {
                switch (Field)
                {
                    case CampgroundSettingField Picker:
                        if (Picker.IsEditable)
                        {
                            Choices.Add(new Choice(IW.Change(Picker.AccessibleName), () => EditCampground(Picker)));
                        }
                        break;
                    case BoolSettingField Day when Search.Weekdays.Contains(Day):
                        if (ReferenceEquals(Day, Search.Weekdays[0]) && Search.Weekdays.Any(Each => Each.IsEditable))
                        {
                            Choices.Add(new Choice(IW.Change(SW.WeekdaysHeading), EditDays));
                        }
                        break;
                    case FilterSelectorField:
                        break;
                    case ChoiceSettingField Matching when ReferenceEquals(Matching, Search.Matching):
                        if (Matching.IsEditable || Search.Selectors.Any(Selector => Selector.IsEditable))
                        {
                            Choices.Add(new Choice(IW.Change(SW.FiltersHeading), EditFilters));
                        }
                        break;
                    default:
                        if (Field.IsEditable)
                        {
                            Choices.Add(new Choice(IW.Change(Field.AccessibleName), () => Edit(Field)));
                        }
                        break;
                }
            }
        }

        // Save As, when the form offers it (an editable file)
        private protected override void AddFormChoices(List<Choice> Choices)
        {
            if (Searches.SaveAsCommand.CanExecute(null))
            {
                Choices.Add(new Choice(SW.Plain(SW.SaveAsLabel), SaveAs));
            }
        }

        // a screen's choices asked; the end of the input ends the form, Esc or Ctrl+C goes back to the screen above it
        private Step Screen(string Title, List<Choice> Choices)
        {
            PromptResult<int> Picked = Prompts.Choose(Title, Choices.Select(C => C.Label).ToList());
            if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
            {
                return Step.EndOfInput;
            }
            if (!Picked.IsAnswered)
            {
                return Step.Back;
            }
            return Choices[Picked.Value].Run();
        }

        // a screen's loop: its choices until Done, Esc, Ctrl+C (the form's choices again) or the end of the input
        private Step Until(Func<Step> Once)
        {
            while (true)
            {
                Step Next = Once();
                if (Next == Step.EndOfInput)
                {
                    return Step.EndOfInput;
                }
                if (Next == Step.Back)
                {
                    return Step.Stay;
                }
            }
        }

        // ---- the days of the week ----

        private Step EditDays() => Until(() =>
        {
            Prompts.Heading(SW.WeekdaysHeading);
            Prompts.Say(SW.WeekdaysNote);
            if (Search.HasWeekdaysMessage)
            {
                Prompts.Say(Safe(Search.WeekdaysMessageText));
            }
            Prompts.Say(W.DaysHow);
            var Choices = new List<Choice>();
            foreach (BoolSettingField Day in Search.Weekdays.Where(Day => Day.IsEditable))
            {
                Choices.Add(new Choice(Day.AccessibleName + ": " + (Day.IsChecked ? W.Checked : W.NotChecked) + (Day.IsChanged ? IW.Changed : string.Empty), () =>
                {
                    Day.IsChecked = !Day.IsChecked;
                    return Step.Stay;
                }));
            }
            Choices.Add(new Choice(W.Done, () => Step.Back));
            return Screen(SW.WeekdaysHeading, Choices);
        });

        // ---- the campground ----

        private Step EditCampground(CampgroundSettingField Picker) => Until(() =>
        {
            Prompts.Heading(Picker.AccessibleName);
            Prompts.Say(Picker.Hint);
            Prompts.Say(IW.Now(Safe(Picker.CurrentText)));
            ShowMessages(Picker, string.Empty);
            if (!DataPrepared())
            {
                Prompts.Say(W.DataMissing);
            }
            if (Picker.HasTypedIdMessage)
            {
                Prompts.Say(Safe(MessageSeverity.AccessibleText(Picker.TypedIdMessage, Picker.TypedIdIsError ? MessageSeverity.Error : MessageSeverity.Info)));
            }
            var Choices = new List<Choice>();
            if (Picker.IsEditable)
            {
                Choices.Add(new Choice(SW.FindCampgroundName, () => FindCampground(Picker)));
                Choices.Add(new Choice(SW.Plain(SW.CampgroundIdLabel), () => TypeCampgroundId(Picker)));
            }
            Choices.Add(new Choice(W.Done, () => Step.Back));
            return Screen(Picker.AccessibleName, Choices);
        });

        // the form's find (each word in a name, city, state or park, or an exact ID), its summary, its first matches by
        // number; the chosen one used as the form uses it (by its FacilityID)
        private Step FindCampground(CampgroundSettingField Picker)
        {
            Prompts.Say(SW.FindCampgroundHelp);
            Prompts.Say(W.FindCampgroundHow);
            PromptResult<string> Find = Prompts.Text(SW.FindCampgroundName, false, Trim: false);
            if (!Find.IsAnswered)
            {
                return Unanswered(Find);
            }
            Picker.FilterText = Find.Value!;
            string? More = Picker.MoreMatchesText(W.ListedCampgrounds);
            Prompts.Say(Safe(Picker.MatchSummary + (More == null ? string.Empty : " " + More)));
            IReadOnlyList<CampgroundPickerEntry> Listed = Picker.FirstMatches(W.ListedCampgrounds);
            if (Listed.Count == 0)
            {
                return Step.Stay;
            }
            Prompts.Say(W.ChooseCampgroundHow);
            PromptResult<int> Picked = Prompts.Choose(SW.CampgroundsName, Listed.Select(Entry => Safe(Entry.AccessibleName)).ToList());
            if (!Picked.IsAnswered)
            {
                return Unanswered(Picked);
            }
            Picker.SelectMatch(Listed[Picked.Value].FacilityID);
            return Command(Picker.UseSelectedCommand);
        }

        // an ID exactly as typed: the form uses it only when a reservable, enabled campground has exactly this ID
        private Step TypeCampgroundId(CampgroundSettingField Picker)
        {
            Prompts.Say(Picker.TypedIdHint);
            Prompts.Say(W.CampgroundIdHow);
            PromptResult<string> Typed = Prompts.Text(SW.CampgroundIdName, false, Trim: false);
            if (!Typed.IsAnswered)
            {
                return Unanswered(Typed);
            }
            Picker.TypedId = Typed.Value!;
            return Command(Picker.UseTypedIdCommand);
        }

        // ---- the campsite filters ----

        private Step EditFilters() => Until(() =>
        {
            Prompts.Heading(SW.FiltersHeading);
            Prompts.Say(SW.FiltersExplanation);
            if (!DataPrepared())
            {
                Prompts.Say(W.DataMissing);
            }
            ShowMatching("  ");
            if (Search.HasChoicesProblem)
            {
                Prompts.Say("  " + Safe(Search.Selectors[0].StatusText), "    ");
            }
            var Choices = new List<Choice>();
            if (Search.Matching.IsEditable)
            {
                Choices.Add(new Choice(IW.Change(Search.Matching.AccessibleName), () => Edit(Search.Matching)));
            }
            foreach ((string Heading, FilterSelectorField[] Lists) in Grouped())
            {
                Prompts.Say("  " + Heading + ":", "    ");
                foreach (FilterSelectorField Selector in Lists)
                {
                    ShowSelectorLine(Selector, "    ");
                    if (Selector.IsEditable)
                    {
                        Choices.Add(new Choice(IW.Change(Selector.AccessibleName), () => EditSelector(Selector)));
                    }
                }
            }
            if (Search.HasChoicesProblem)
            {
                Choices.Add(new Choice(SW.RetryChoices, () => Command(Search.RetryChoicesCommand)));
            }
            Choices.Add(new Choice(W.Done, () => Step.Back));
            return Screen(SW.FiltersHeading, Choices);
        });

        // one include or exclude list: its state, the values it holds, and the selector's own actions
        private Step EditSelector(FilterSelectorField Selector) => Until(() =>
        {
            Prompts.Heading(Selector.AccessibleName);
            Prompts.Say(Selector.Hint);
            Prompts.Say(Safe(Selector.StatusText));
            Prompts.Say(W.ValuesHeading + ":");
            List<FilterOptionRow> Held = Selector.Options.Where(Option => Option.IsChoice && Option.IsChecked == true).ToList();
            foreach (FilterOptionRow Option in Held)
            {
                Prompts.Say("  " + Safe(Option.AccessibleName), "    ");
            }
            if (Selector.HasOtherValues)
            {
                Prompts.Say("  " + Selector.OtherValuesHeading + ":", "    ");
                foreach (FilterCustomRow Other in Selector.OtherValues)
                {
                    Prompts.Say("    " + Safe(Other.AccessibleName), "      ");
                }
            }
            if (Held.Count == 0 && !Selector.HasOtherValues)
            {
                Prompts.Say("  " + IW.Empty);
            }
            if (Selector.HasConflict)
            {
                Prompts.Say(Safe(Selector.ConflictText!));
            }
            ShowMessages(Selector, string.Empty);
            if (Selector.HasCustomMessage)
            {
                Prompts.Say(Safe(MessageSeverity.AccessibleText(Selector.CustomMessage, Selector.CustomIsError ? MessageSeverity.Error : MessageSeverity.Info)));
            }
            var Choices = new List<Choice>();
            if (Selector.IsEditable && Selector.HasChoices)
            {
                Choices.Add(new Choice(W.CheckValues, () => CheckValues(Selector)));
            }
            if (Selector.AddCustomCommand.CanExecute(null))
            {
                Choices.Add(new Choice(Selector.AddCustomName, () => AddValue(Selector)));
            }
            if (Selector.OtherValues.Any(Other => Other.RemoveCommand.CanExecute(null)))
            {
                Choices.Add(new Choice(W.RemoveValue, () => RemoveValue(Selector)));
            }
            if (Search.HasChoicesProblem)
            {
                Choices.Add(new Choice(SW.RetryChoices, () => Command(Search.RetryChoicesCommand)));
            }
            Choices.Add(new Choice(W.Done, () => Step.Back));
            return Screen(Selector.AccessibleName, Choices);
        });

        // the campground's values by number, each as the selector names it (checked, partly checked or not), with the find,
        // Select all shown and Clear shown of a long list; a find lasts only while this screen is shown
        private Step CheckValues(FilterSelectorField Selector)
        {
            try
            {
                return Until(() =>
                {
                    Prompts.Heading(Selector.ListName);
                    Prompts.Say(Safe(Selector.StatusText));
                    Prompts.Say(W.CheckHow);
                    var Choices = new List<Choice>();
                    foreach (FilterOptionRow Option in Selector.Options.Where(Option => Option.IsCheckable).ToList())
                    {
                        Choices.Add(new Choice(Safe(Option.AccessibleName), () =>
                        {
                            Selector.Toggle(Option);
                            return Step.Stay;
                        }));
                    }
                    if (Selector.CanFind)
                    {
                        Choices.Add(new Choice(Selector.FindName, () => FindValues(Selector)));
                    }
                    if (Selector.ShowBulkActions && Selector.SelectShownCommand.CanExecute(null))
                    {
                        Choices.Add(new Choice(SW.SelectShown, () => Command(Selector.SelectShownCommand)));
                    }
                    if (Selector.ShowBulkActions && Selector.ClearShownCommand.CanExecute(null))
                    {
                        Choices.Add(new Choice(SW.ClearShown, () => Command(Selector.ClearShownCommand)));
                    }
                    Choices.Add(new Choice(W.Done, () => Step.Back));
                    return Screen(Selector.ListName, Choices);
                });
            }
            finally
            {
                Selector.FindText = string.Empty;
            }
        }

        private Step FindValues(FilterSelectorField Selector)
        {
            Prompts.Say(SW.FindHelp);
            Prompts.Say(W.FindHow);
            PromptResult<string> Find = Prompts.Text(Selector.FindName, false, Trim: false);
            if (!Find.IsAnswered)
            {
                return Unanswered(Find);
            }
            Selector.FindText = Find.Value!;
            return Step.Stay;
        }

        // a value added exactly as typed (the form refuses an empty one and one the list holds, in its own words)
        private Step AddValue(FilterSelectorField Selector)
        {
            Prompts.Say(Selector.CustomHint);
            Prompts.Say(W.CustomHow);
            PromptResult<string> Value = Prompts.Text(Selector.CustomName, false, Trim: false);
            if (!Value.IsAnswered)
            {
                return Unanswered(Value);
            }
            Selector.CustomText = Value.Value!;
            return Command(Selector.AddCustomCommand);
        }

        // one of the values the campground does not have (or an empty item), by its number among them
        private Step RemoveValue(FilterSelectorField Selector)
        {
            List<FilterCustomRow> Others = Selector.OtherValues.Where(Other => Other.RemoveCommand.CanExecute(null)).ToList();
            PromptResult<int> Picked = Prompts.Choose(W.WhichValue, Others.Select(Other => Safe(Other.AccessibleName)).ToList());
            if (!Picked.IsAnswered)
            {
                return Unanswered(Picked);
            }
            return Command(Others[Picked.Value].RemoveCommand);
        }

        // ---- Save As ----

        // the form's Save As (its words, its name rules, create-only): this search with its changes to a new file, asked once,
        // No by default; the new file's form follows, and this file keeps what is on disk
        private Step SaveAs()
        {
            Prompts.Heading(SW.Plain(SW.SaveAsLabel));
            Prompts.Say(SW.SaveAsHelp);
            Prompts.Say(SW.SaveAsNote);
            Prompts.Say(W.NameHow);
            PromptResult<string> Name = Prompts.Text(SW.SaveAsName, false, Trim: false);
            if (!Name.IsAnswered)
            {
                return Unanswered(Name);
            }
            if (!Confirmed(W.SaveAsQuestion, out Step Ended))
            {
                return Ended == Step.EndOfInput ? Ended : Said(W.NothingWritten);
            }
            Searches.SaveAsName = Name.Value!;
            Searches.SaveAsCommand.Execute(null);
            SearchFileViewModel? Created = Searches.SelectedFile;
            if (Searches.SaveAsIsError || Created == null || ReferenceEquals(Created, Search))
            {
                if (Searches.HasSaveAsMessage)
                {
                    Prompts.Say(Safe(MessageSeverity.AccessibleText(Searches.SaveAsMessage, MessageSeverity.Error)));
                }
                return Step.Stay;
            }
            Prompts.Say(Safe(MessageSeverity.AccessibleText(Searches.SaveAsMessage, MessageSeverity.Info)));
            Written = Created;
            return Step.Back;
        }
    }
}
