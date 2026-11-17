using Avalonia.Threading;
using CampertronLibrary.function.RecDotOrg.sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Campertron.ViewModels
{
    // The Searches tab's source of filter choices (decision D51): a campground's equipment, campsite types, campsite names
    // and attributes from the local campground database (Read.FilterChoicesByCampground), read on a worker thread, never on
    // the UI thread, and kept per campground while the database file is unchanged (its length and write time and those of
    // its WAL file: a refresh installs new data through them). Results are handed back on the UI thread through the
    // dispatcher captured when the source was created there (D49: never ask for Dispatcher.UIThread on a worker thread). A
    // caller that asked again meanwhile ignores the older answer (FilterChoicesState's request number). Nothing here writes,
    // searches availability or makes a web request.
    internal sealed class FilterChoiceSource
    {
        private readonly Func<string, CampgroundFilterChoices> _Load;
        private readonly Func<string> _Fingerprint;
        private readonly Action<Action> _Post;
        private readonly Func<Action, Task> _Start;
        private readonly Dictionary<string, CampgroundFilterChoices> _Cache = new(StringComparer.Ordinal);
        private string? _CacheFingerprint;

        // the production source: the settings folder's RecreationDotOrg.db; created on the UI thread
        internal FilterChoiceSource(string ConfigPath) : this(
            Id => Read.FilterChoicesByCampground(Id, ConfigPath),
            () => DatabaseFingerprint(ConfigPath),
            CapturePost(),
            Job => Task.Run(Job))
        {
        }

        // tests: their own load, fingerprint, UI queue and worker
        internal FilterChoiceSource(Func<string, CampgroundFilterChoices> Load, Func<string> Fingerprint, Action<Action> Post, Func<Action, Task> Start)
        {
            _Load = Load;
            _Fingerprint = Fingerprint;
            _Post = Post;
            _Start = Start;
        }

        // reads on the worker since this source was made (tests count them)
        internal int Loads { get; private set; }

        private static Action<Action> CapturePost()
        {
            Dispatcher Ui = Dispatcher.UIThread;
            return Job => Ui.Post(Job, DispatcherPriority.Background);
        }

        // the database file's state, cheap to read on the UI thread (no query)
        internal static string DatabaseFingerprint(string ConfigPath)
        {
            static string Of(string Path_)
            {
                var Info = new FileInfo(Path_);
                return Info.Exists ? Info.Length + "@" + Info.LastWriteTimeUtc.Ticks : "-";
            }
            string Db = Path.Join(ConfigPath, "RecreationDotOrg.db");
            return Of(Db) + "|" + Of(Db + "-wal");
        }

        // the choices of this campground if they are kept and the database is unchanged
        internal bool TryGet(string FacilityID, out CampgroundFilterChoices Choices)
        {
            Choices = null!;
            if (!string.Equals(_Fingerprint(), _CacheFingerprint, StringComparison.Ordinal))
            {
                _Cache.Clear();
                _CacheFingerprint = null;
                return false;
            }
            if (_Cache.TryGetValue(FacilityID, out CampgroundFilterChoices? Kept))
            {
                Choices = Kept;
                return true;
            }
            return false;
        }

        // Reads the campground's choices on the worker; Done runs on the UI thread with them, or with the reason they can't
        // be read (fixed text). Call on the UI thread
        internal void Request(string FacilityID, Action<CampgroundFilterChoices?, string?> Done)
        {
            string Fingerprint = _Fingerprint();
            Loads++;
            _Start(() =>
            {
                CampgroundFilterChoices? Choices = null;
                string? Problem = null;
                try
                {
                    Choices = _Load(FacilityID);
                }
                catch (Exception ex) when (ex is System.Data.Common.DbException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    Problem = "Campertron couldn't read this campground's values from its campground data.";
                }
                _Post(() =>
                {
                    if (Choices != null && string.Equals(Fingerprint, _Fingerprint(), StringComparison.Ordinal))
                    {
                        if (!string.Equals(Fingerprint, _CacheFingerprint, StringComparison.Ordinal))
                        {
                            _Cache.Clear();
                            _CacheFingerprint = Fingerprint;
                        }
                        _Cache[FacilityID] = Choices;
                    }
                    Done(Choices, Problem);
                });
            });
        }

        // the database changed since the choices were kept (a refresh by the console, for example)
        internal bool DatabaseChanged => _CacheFingerprint != null && !string.Equals(_Fingerprint(), _CacheFingerprint, StringComparison.Ordinal);
    }
}
