using CampertronLibrary.function.Forms;

namespace CampertronWeb
{
    // The settings forms of the web UI's browsers (v0.3.0's PR 5; business-rules-settings §32). The web UI fills and reads
    // the shared form layer's SettingsViewModel (D91), the one the desktop GUI shows, so both edit the same files with the
    // same rules and words. A form keeps the user's unsaved changes between requests, as the desktop GUI's window keeps
    // them between tabs, so each browser session has its own (signed in: the session's identifier; without password
    // protection: one for the web UI, as there is one owner, D81):
    //  - one request at a time uses a workspace (its Lock): a form is used by one thread at a time (FormHost, ER-37);
    //  - each file has a revision that changes with every action the web UI applies to it and whenever its values were
    //    replaced (Generation): a page sends the revision it showed, and entries from a page that is out of date are never
    //    applied (another tab, the back button, a file changed on disk), so nothing is overwritten unseen;
    //  - a workspace ends with its session (a sign-out, a password change, the idle time, WebSessions.Ended), so an SMTP
    //    password that was entered and not saved does not outlive the session. Nothing of a workspace is written to disk;
    //  - one test notification at a time in this web UI (TryBeginTest), so a send can't be started twice by two tabs.
    // No secret source is read here: the test notification reads it only in the library's TestNotification, when the user
    // asks for one (D88). An object is one web UI's; it has no static state.
    // Since v0.3.0's PR 6 (D96) a workspace also holds the session's Searches form (the desktop GUI's Searches tab's,
    // SearchesViewModel), made when a searches page is first asked for: its files' revisions come from one counter, so a
    // page of a file that was removed and listed again, or of a list that changed, is out of date too.
    internal sealed class WebSettingsWorkspaces
    {
        internal const string OpenKey = "open";
        internal static readonly string[] FileNames = { "general", "email", "ntfy", "web" };

        internal sealed class Workspace
        {
            private readonly Func<SearchesViewModel> _MakeSearches;
            private SearchesViewModel? _Searches;
            // the searches' revisions: each a new number of one counter, for the list and for each file form (by the form
            // itself, so a file listed again is a new form with a new revision), with the Generation last seen
            private int _NextSearchRevision;
            private readonly Dictionary<SearchFileViewModel, (int Revision, int Generation)> _SearchRevisions = new(ReferenceEqualityComparer.Instance);
            private List<string> _Listed = new List<string>();

            internal Workspace(SettingsViewModel Settings, Func<SearchesViewModel> MakeSearches)
            {
                this.Settings = Settings;
                _MakeSearches = MakeSearches;
                foreach (string Name in FileNames)
                {
                    Revisions[Name] = 1;
                    Generations[Name] = File(Name).Generation;
                }
            }

            internal SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1);
            internal SettingsViewModel Settings { get; }
            internal Dictionary<string, int> Revisions { get; } = new(StringComparer.Ordinal);
            // each file's Generation when the web UI last looked: a change means its values were replaced
            internal Dictionary<string, int> Generations { get; } = new(StringComparer.Ordinal);

            internal SettingsFileViewModel File(string Name) => Name switch
            {
                "general" => Settings.General,
                "email" => Settings.Email,
                "ntfy" => Settings.Ntfy,
                "web" => Settings.Web,
                _ => throw new ArgumentOutOfRangeException(nameof(Name))
            };

            // the files' values were replaced since the web UI last looked (a reload, a change on disk): a new revision,
            // so a page that showed the old values is out of date
            internal void FollowGenerations()
            {
                foreach (string Name in FileNames)
                {
                    int Now = File(Name).Generation;
                    if (Generations[Name] != Now)
                    {
                        Generations[Name] = Now;
                        Revisions[Name]++;
                    }
                }
            }

            // an action of the web UI changed the file's form
            internal void Changed(string Name)
            {
                Revisions[Name]++;
                FollowGenerations();
            }

            // ---- the searches pages (v0.3.0's PR 6) ----

            // the session's Searches form, made the first time a searches page asks for it (the files and the campground
            // data are read when it is shown)
            internal SearchesViewModel Searches
            {
                get
                {
                    if (_Searches == null)
                    {
                        _Searches = _MakeSearches();
                        ListRevision = ++_NextSearchRevision;
                    }
                    return _Searches;
                }
            }

            // the revision of the list of search files (New search, Look for changes on disk)
            internal int ListRevision { get; private set; }

            // Its words follow General.yaml's outputTo as the session's General form shows it and as the file has it (D73),
            // as the desktop GUI's Searches tab follows its General editor
            internal void FollowOutput() => Searches.SetOutput(Settings.General.OutputToValue, Settings.General.SavedOutputToValue);

            // the revision of a file's form as a page shows it
            internal int RevisionOf(SearchFileViewModel File)
            {
                FollowSearches();
                return _SearchRevisions.TryGetValue(File, out var Known) ? Known.Revision : 0;
            }

            // a file listed or gone, or a file's values replaced (Generation): new revisions
            internal void FollowSearches()
            {
                if (_Searches == null)
                {
                    return;
                }
                foreach (SearchFileViewModel File in _Searches.Files)
                {
                    if (!_SearchRevisions.TryGetValue(File, out var Known) || Known.Generation != File.Generation)
                    {
                        _SearchRevisions[File] = (++_NextSearchRevision, File.Generation);
                    }
                }
                foreach (SearchFileViewModel Gone in _SearchRevisions.Keys.Where(File => !_Searches.Files.Contains(File)).ToList())
                {
                    _SearchRevisions.Remove(Gone);
                }
                List<string> Names = _Searches.Files.Select(File => File.FileName).ToList();
                if (!Names.SequenceEqual(_Listed, StringComparer.Ordinal))
                {
                    _Listed = Names;
                    ListRevision = ++_NextSearchRevision;
                }
            }

            // an action of the web UI changed a file's form, or the list
            internal void SearchChanged(SearchFileViewModel File)
            {
                FollowSearches();
                if (_SearchRevisions.ContainsKey(File))
                {
                    _SearchRevisions[File] = (++_NextSearchRevision, File.Generation);
                }
            }

            internal void ListChanged()
            {
                FollowSearches();
                ListRevision = ++_NextSearchRevision;
            }

            // New search's or Save As's result, shown once on the new file's page (its address is the next request)
            private (string File, string Text)? _Created;
            internal void Created(string File, string Text) => _Created = (File, Text);
            internal string? TakeCreated(string File)
            {
                (string File, string Text)? Was = _Created;
                _Created = null;
                return Was is (string Name, string Text) && string.Equals(Name, File, StringComparison.Ordinal) ? Text : null;
            }
        }

        private readonly object _Lock = new object();
        private readonly Dictionary<string, Workspace> _Spaces = new(StringComparer.Ordinal);
        private readonly WebHostSettings _Settings;
        private readonly WebSessions _Sessions;
        private readonly WebCampgroundData _Data;
        private int _Testing;

        internal WebSettingsWorkspaces(WebHostSettings Settings, WebSessions Sessions, WebCampgroundData Data)
        {
            _Settings = Settings;
            _Sessions = Sessions;
            _Data = Data;
            Sessions.Ended += Ended;
        }

        internal int Count
        {
            get
            {
                lock (_Lock)
                {
                    return _Spaces.Count;
                }
            }
        }

        // the workspace of a session (signed in) or of the web UI (no password protection); created at its first use
        internal Workspace For(string Key)
        {
            lock (_Lock)
            {
                if (_Spaces.TryGetValue(Key, out Workspace? Found))
                {
                    return Found;
                }
                //a session that ended without a sign-out removes nothing by itself until a request asks for it
                foreach (string Gone in _Spaces.Keys.Where(One => One != OpenKey && !_Sessions.IsAlive(One)).ToList())
                {
                    _Spaces.Remove(Gone);
                }
                var Created = new Workspace(new SettingsViewModel(_Settings.SettingsFolder, null, null, _Settings.ListensOnNetwork),
                    () => new SearchesViewModel(_Settings.SettingsFolder, null, _Data.Campgrounds, _Data.ChoiceSource));
                _Spaces[Key] = Created;
                return Created;
            }
        }

        private void Ended(string? Id)
        {
            lock (_Lock)
            {
                if (Id == null)
                {
                    foreach (string Key in _Spaces.Keys.Where(Key => Key != OpenKey).ToList())
                    {
                        _Spaces.Remove(Key);
                    }
                }
                else
                {
                    _Spaces.Remove(Id);
                }
            }
        }

        // one test notification at a time in this web UI
        internal bool TryBeginTest() => Interlocked.CompareExchange(ref _Testing, 1, 0) == 0;
        internal void EndTest() => Volatile.Write(ref _Testing, 0);
    }
}
