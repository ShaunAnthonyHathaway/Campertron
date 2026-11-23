using System.Collections.ObjectModel;
using CampertronLibrary.function.Base;
using CampertronLibrary.function.RecDotOrg.search;

namespace CampertronLibrary.function.Forms
{
    // The Results tab's list of search files and the rules of its choice (GS2; V011-3, D63), for every UI. The settings
    // folder's search files, in the console's discovery and order, each read as a plan would read it (listing only: nothing
    // is searched or written). The default choice is the enabled files (autoRun: true) that can run; a file that can't run
    // any more is unchosen; a file that is no longer enabled is unchosen unless the user chose it. When a run is about to
    // start (AtStart) nothing is chosen by itself, and a chosen file that is gone stays in the list, still chosen, so the
    // run reports it as missing. The host decides when to look (the tab shown, the window activated, Start) and never
    // looks while a run goes on: its selection is fixed. Used on the form's thread only.
    public sealed class ResultsSearchList
    {
        internal const string NoFolderNotice = "The settings folder does not exist.";
        internal const string UnreadableFolderNotice = "Campertron couldn't read the settings folder.";

        private readonly string _ConfigPath;
        private readonly CampgroundCatalog _Catalog;
        private readonly Func<string, bool> _IsUnsaved;
        private readonly Func<string, string, DateTime, SearchPlanEntry> _Inspect;
        private readonly Func<DateTime> _Now;
        private readonly Action _SelectionChanged;

        // IsUnsaved: whether the Searches form holds unsaved changes to the file (it is then not searched until they are
        // saved or reverted). Inspect: reads one search file as a plan would (CampsiteSearch.LoadEntry). SelectionChanged:
        // after a row was chosen or unchosen
        internal ResultsSearchList(string ConfigPath, CampgroundCatalog Catalog, Func<string, bool> IsUnsaved,
            Func<string, string, DateTime, SearchPlanEntry> Inspect, Func<DateTime> Now, Action SelectionChanged)
        {
            _ConfigPath = ConfigPath;
            _Catalog = Catalog;
            _IsUnsaved = IsUnsaved;
            _Inspect = Inspect;
            _Now = Now;
            _SelectionChanged = SelectionChanged;
        }

        public ObservableCollection<ResultsSearchRow> Rows { get; } = new ObservableCollection<ResultsSearchRow>();

        // Select enabled: the default choice again, every enabled file that can run (V011-3: "enabled" is autoRun: true;
        // nothing runs until Start)
        internal void SelectEnabled()
        {
            foreach (ResultsSearchRow Row in Rows)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(Row.IsRunnable && Row.IsEnabled);
            }
        }

        internal void SelectAllRunnable()
        {
            foreach (ResultsSearchRow Row in Rows)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(Row.IsRunnable);
            }
        }

        internal void ClearSelection()
        {
            foreach (ResultsSearchRow Row in Rows)
            {
                Row.SelectedByUser = true;
                Row.SetSelected(false);
            }
        }

        // The settings folder's search files against the list. Notice gets the folder's problem (or null) before the rows
        // change. Returns the names of chosen files that the disk unchose.
        internal List<string> Rescan(bool AtStart, Action<string?> Notice)
        {
            var Unchosen = new List<string>();
            List<string> Names;
            try
            {
                Names = Directory.Exists(_ConfigPath) ? ConfigFiles.SortedSearchFileNames(_ConfigPath) : new List<string>();
                Notice(Directory.Exists(_ConfigPath) ? null : NoFolderNotice);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Names = new List<string>();
                Notice(UnreadableFolderNotice);
            }
            DateTime Now = _Now();
            var Old = Rows.ToDictionary(R => R.FileName, StringComparer.Ordinal);
            var New = new List<ResultsSearchRow>();
            foreach (string Name in Names)
            {
                SearchPlanEntry? Entry;
                try
                {
                    Entry = _Inspect(_ConfigPath, Name, Now);
                }
                catch (Exception)
                {
                    Entry = null;
                }
                bool IsNew = !Old.TryGetValue(Name, out ResultsSearchRow? Row);
                Row ??= new ResultsSearchRow(Name, _SelectionChanged);
                bool WasSelected = Row.IsSelected;
                bool WasRunnable = Row.IsRunnable;
                bool WasEnabled = Row.IsEnabled;
                Row.Update(Entry, _IsUnsaved(Name), CampgroundText(Entry?.CampgroundId));
                if (IsNew || (!WasRunnable && !Row.SelectedByUser))
                {
                    // the default: the enabled files that can run; never at Start, where the user has not seen the file
                    Row.SetSelected(!AtStart && Row.IsRunnable && Row.IsEnabled);
                }
                else if (WasSelected && WasEnabled && !Row.IsEnabled && !Row.SelectedByUser)
                {
                    Row.SetSelected(false);
                }
                if (WasSelected && !Row.IsSelected)
                {
                    Unchosen.Add(Name);
                }
                New.Add(Row);
            }
            if (AtStart)
            {
                // a chosen file deleted since it was listed: asked for anyway, so the run says it is missing
                foreach (ResultsSearchRow Gone in Old.Values.Where(R => R.IsSelected && !Names.Contains(R.FileName, StringComparer.Ordinal)))
                {
                    Gone.MarkMissing();
                    New.Insert(Math.Min(New.Count, Rows.IndexOf(Gone)), Gone);
                }
            }
            Rows.Clear();
            foreach (ResultsSearchRow Row in New)
            {
                Row.SetLocked(false);
                Rows.Add(Row);
            }
            return Unchosen;
        }

        // the campground's name and place from the campground data (one query when the list is first shown), or null
        private string? CampgroundText(string? Id)
        {
            if (string.IsNullOrWhiteSpace(Id) || _Catalog.LoadError != null)
            {
                return null;
            }
            CampgroundPickerEntry? Entry = _Catalog.Find(Id);
            return Entry?.Choice.Label;
        }
    }
}
