using CampertronLibrary.function.Base;
using CampertronLibrary.function.Forms;
using CampertronLibrary.function.Interactive.Settings;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;
using SW = CampertronLibrary.function.Forms.SearchesWording;
using W = CampertronLibrary.function.Interactive.Searches.InteractiveSearchesWording;

namespace CampertronLibrary.function.Interactive.Searches
{
    // The interactive console's Searches (v0.4.0's phase 8, D124; business-rules §43): the settings folder's search files in
    // the shared form both other UIs show (SearchesViewModel, the desktop GUI's Searches tab and the web UI's searches pages,
    // ER-37, ER-14): the list with each file's details and state and the folder's notice, New search, and each file's form
    // (InteractiveSearchForm) with every setting, the campground picker, the filters, Save, Save As, Revert, Reload and the
    // conflicts. The form is the authority for every rule and word: it lists the files (the console's own search files,
    // never a backup or another settings file), names and creates a new file from the example search (create-only, not
    // enabled: today's New search, which D120's TD-66 will correct for every host at once), writes only on Save, Save As or
    // New search through its editing sessions (the safe writer, the backup, the external-change checks), and never deletes
    // or renames a file. Nothing here parses, validates, reads or writes a search file, runs a search, starts the
    // monitoring or touches Camp.History.
    //
    // Entering it changes nothing: the list is the form's, read each time it is shown (so leaving a file's form keeps only
    // what was saved, and a change on disk is always seen). General.yaml's outputTo, as saved, is handed to the form as the
    // web UI hands it (D73: the folder's notice and a search's warning when the monitoring would have no search to check).
    //
    // The campground data (the picker's campgrounds and a campground's filter values) is read through the library's own
    // queries, the ones the forms use, only when it is prepared: the console never prepares, repairs or refreshes it, and
    // never opens a database that is not there (SQLite would create an empty one). Without it the forms say so in their own
    // words (CampgroundCatalog, FilterChoiceSource) and the console says where it comes from. A campground's values are
    // read on the console's own thread and handed to the form at once, as the web UI reads them (WebCampgroundData), so the
    // form posts nothing from another thread and needs no FormHost.
    internal static class InteractiveSearches
    {
        private static readonly SettingsMenuWords Words = new SettingsMenuWords(W.SearchesTitle, SW.FilesHeading, W.BackToSearches,
            W.NoFolder, W.FolderMissing, W.ContainerFolderMissing);

        // the campground data the forms read (the tests give their own): whether it is prepared (the database file exists,
        // never opened), every reservable, enabled campground, a campground's filter values, and the database's state
        internal static Func<string, bool> DataPrepared = Folder => Load.DbExists(new CampertronInternalConfig { ConfigPath = Folder });
        internal static Func<string, List<CampgroundPickerEntry>> ReadCampgrounds = Read.CampgroundPickerEntries;
        internal static Func<string, string, CampgroundFilterChoices> ReadFilterChoices = (Folder, Id) => Read.FilterChoicesByCampground(Id, Folder);
        internal static Func<string, string> DatabaseState = FilterChoiceSource.DatabaseFingerprint;

        // the main menu's Searches
        internal static InteractiveStep Run(InteractiveSession Session)
        {
            InteractivePrompts Prompts = Session.Prompts;
            //since D130 (PT1): a screen of its own, started on a cleared screen on arrival and on each return to it
            InteractiveScreen Here = Prompts.Screen();
            Here.Show();
            Prompts.Heading(W.SearchesTitle);
            string? Folder = InteractiveSettingsMenu.SettingsFolder(Session, Words);
            if (Folder == null)
            {
                return InteractiveStep.Done;
            }
            var Data = new CampgroundData(Folder);
            bool First = true;
            while (true)
            {
                if (!First)
                {
                    Here.Show();
                    Prompts.Heading(W.SearchesTitle);
                }
                First = false;
                //a new form each time: it reads the folder and every file as they are now
                SearchesViewModel Searches = Compose(Session, Folder, Data);
                Prompts.Say(W.SearchesNote);
                if (Searches.HasFolderNotice)
                {
                    Prompts.Say(InteractiveSettingsForm.Visible(Searches.FolderNoticeText));
                }
                if (!Data.Prepared)
                {
                    Prompts.Say(W.DataMissing);
                }
                if (Searches.HasNoFiles)
                {
                    Prompts.Say(SW.NoFiles);
                }
                List<SearchFileViewModel> Files = Searches.Files.ToList();
                List<string> Choices = Files.Select(File => InteractiveSettingsForm.Visible(W.File(File.FileName, File.ListDetails, File.ListStatus)))
                    .Append(SW.Plain(SW.NewSearchLabel)).Append(SW.LookForChanges).Append(InteractiveSettingsWording.BackToMainMenu).ToList();
                PromptResult<int> Picked = Prompts.Choose(SW.FilesHeading, Choices);
                if (Picked.End == PromptEnd.EndOfInput || Picked.End == PromptEnd.Unavailable)
                {
                    return InteractiveStep.EndOfInput;
                }
                if (!Picked.IsAnswered || Picked.Value == Files.Count + 2)
                {
                    //Back, Esc or Ctrl+C: the main menu again
                    return InteractiveStep.Done;
                }
                if (Picked.Value == Files.Count + 1)
                {
                    //Look for changes on disk: the list is read again, as it is every time it is shown
                    continue;
                }
                SearchFileViewModel? Open;
                if (Picked.Value < Files.Count)
                {
                    Open = Files[Picked.Value];
                }
                else
                {
                    Open = NewSearch(Prompts, Searches, out bool Ended);
                    if (Ended)
                    {
                        return InteractiveStep.EndOfInput;
                    }
                    if (Open == null)
                    {
                        continue;
                    }
                }
                if (Edit(Prompts, Searches, Open, Data) == ScreenEnd.EndOfInput)
                {
                    return InteractiveStep.EndOfInput;
                }
            }
        }

        // the shared form over the settings folder, its outputTo as General.yaml has it saved (the web UI's FollowOutput: the
        // General form's shown and saved values, here both the saved one), shown: the files and the campground data read
        internal static SearchesViewModel Compose(InteractiveSession Session, string Folder, CampgroundData Data)
        {
            var Searches = new SearchesViewModel(Folder, () => Session.UtcNow().ToLocalTime(), Data.Campgrounds, () => Data.Source);
            //the folder's notice and a file's warning name this console's Searches menu and New search file (PT1)
            Searches.UseSearchPlace(SearchPlace.Menu);
            GeneralSettingsViewModel General = new SettingsViewModel(Folder).General;
            Searches.SetOutput(General.OutputToValue, General.SavedOutputToValue);
            Searches.Show();
            return Searches;
        }

        // one file's form, and the file Save As wrote after it (its form follows at once, as both other UIs show it)
        private static ScreenEnd Edit(InteractivePrompts Prompts, SearchesViewModel Searches, SearchFileViewModel Open, CampgroundData Data)
        {
            SearchFileViewModel? Next = Open;
            while (Next != null)
            {
                //the file shown in the editor: its filter selectors read its campground's values
                Searches.SelectedFile = Next;
                (ScreenEnd End, SearchFileViewModel? Written) = InteractiveSearchForm.Run(Prompts, Searches, Next, () => Data.Prepared);
                if (End == ScreenEnd.EndOfInput)
                {
                    return ScreenEnd.EndOfInput;
                }
                Next = Written;
            }
            return ScreenEnd.Back;
        }

        // New search (the form's: its words, its name rules, the example search not enabled, create-only), asked once, No by
        // default; the new file, or null when nothing was created (Ended: the input ended)
        private static SearchFileViewModel? NewSearch(InteractivePrompts Prompts, SearchesViewModel Searches, out bool Ended)
        {
            string? Name = NewSearchName(Prompts, out Ended);
            if (Name == null)
            {
                return null;
            }
            Searches.NewSearchName = Name;
            Searches.NewSearchCommand.Execute(null);
            SayNewSearchResult(Prompts, Searches);
            return Searches.NewSearchIsError ? null : Searches.SelectedFile;
        }

        // New search's questions (the form's help and note, the name exactly as typed, then "Create the search file now?",
        // No by default): the name to hand to the form, or null when nothing is to be created (Ended: the input ended). Since
        // v0.4.0's phase 12 (D128) the console's guided setup asks them too, before the shared guided setup's New search
        internal static string? NewSearchName(InteractivePrompts Prompts, out bool Ended)
        {
            Ended = false;
            Prompts.Heading(SW.Plain(SW.NewSearchLabel));
            Prompts.Say(SW.NewSearchHelp);
            Prompts.Say(SW.NewSearchNote);
            Prompts.Say(W.NameHow);
            PromptResult<string> Name = Prompts.Text(SW.NewSearchName, false, Trim: false);
            if (!Name.IsAnswered)
            {
                Ended = Name.End == PromptEnd.EndOfInput;
                if (Name.IsCancelled)
                {
                    Prompts.Say(InteractiveWording.Cancelled);
                }
                return null;
            }
            PromptResult<bool> Create = Prompts.Confirm(W.CreateQuestion, false);
            if (!Create.IsAnswered || !Create.Value)
            {
                Ended = Create.End == PromptEnd.EndOfInput;
                if (!Ended)
                {
                    Prompts.Say(W.NothingCreated);
                }
                return null;
            }
            return Name.Value!;
        }

        // New search's result in the form's words, with its severity's word
        internal static void SayNewSearchResult(InteractivePrompts Prompts, SearchesViewModel Searches)
        {
            Prompts.Say(InteractiveSettingsForm.Visible(MessageSeverity.AccessibleText(Searches.NewSearchMessage,
                Searches.NewSearchIsError ? MessageSeverity.Error : MessageSeverity.Info)));
        }

        // The campground data of one visit of Searches: the picker's campgrounds, read once when the database exists (the
        // list is the same for every file and every time the list is shown), and one source of a campground's filter
        // values (it keeps each campground's values while the database is unchanged), read on the console's thread and
        // handed to the form at once. A database that is not there is never opened: the forms get an IOException, which
        // they report in their own fixed words
        internal sealed class CampgroundData
        {
            private readonly string Folder;
            private List<CampgroundPickerEntry>? Kept;

            internal CampgroundData(string Folder)
            {
                this.Folder = Folder;
                Source = new FilterChoiceSource(Id => Prepared ? ReadFilterChoices(Folder, Id) : throw new IOException("The campground data is not prepared."),
                    () => DatabaseState(Folder), Job => Job(), Job =>
                    {
                        Job();
                        return Task.CompletedTask;
                    });
            }

            internal FilterChoiceSource Source { get; }
            internal bool Prepared => DataPrepared(Folder);

            internal List<CampgroundPickerEntry> Campgrounds()
            {
                if (Kept != null)
                {
                    return Kept;
                }
                if (!Prepared)
                {
                    throw new IOException("The campground data is not prepared.");
                }
                Kept = ReadCampgrounds(Folder);
                return Kept;
            }
        }
    }
}
