using CampertronLibrary.function.Forms;
using CampertronLibrary.function.RecDotOrg.data;
using CampertronLibrary.function.RecDotOrg.sqlite;

namespace CampertronWeb
{
    // The campground data of the searches pages (v0.3.0's PR 6, D96) and of the results page (PR 7, D97: its list's
    // campground names; a run reads the database prepared here): the campground picker's list and a campground's
    // filter values, read from the settings folder's RecreationDotOrg.db by the library's own queries (Read), the ones the
    // desktop GUI's Searches tab uses. Web mode's start reads no database (business-rules-settings §31), so the first
    // page that needs it prepares it as every other start of Campertron does (Load.PrepareDatabase: the bundled database
    // extracted when there is none, the one-time repair of an old one, the attributes' index), once for this web UI and
    // never with the automatic refresh (a refresh is the campground data's page, D79's PR 9). The database is never opened
    // before it exists: SQLite would create an empty file. Reads only; one preparation at a time; no state but whether the
    // preparation ran.
    internal sealed class WebCampgroundData
    {
        // the console line when the bundled database is extracted (the console's own start says the same in its words); since
        // v0.3.0's PR 7 the results page's first look prepares it too (its list names a search file's campground)
        internal const string FirstUseLine = "Preparing the campground data for the searches and results pages (first use)...";

        private readonly WebHostSettings _Settings;
        private readonly object _Lock = new object();
        private bool _Prepared;

        internal WebCampgroundData(WebHostSettings Settings)
        {
            _Settings = Settings;
        }

        internal string DatabaseFile => Path.Join(_Settings.SettingsFolder, "RecreationDotOrg.db");

        // The database exists and was prepared once; else an IOException, which the picker and the selectors report in
        // their own fixed words (CampgroundCatalog, FilterChoiceSource)
        internal void Ensure()
        {
            lock (_Lock)
            {
                if (_Prepared && File.Exists(DatabaseFile))
                {
                    return;
                }
                if (!File.Exists(DatabaseFile))
                {
                    _Settings.Line(FirstUseLine);
                }
                try
                {
                    (_Settings.PrepareCampgroundData ?? (() => Load.PrepareDatabase(_Settings.SettingsFolder, _Settings.CachePath)))();
                }
                catch (IOException) when (File.Exists(DatabaseFile))
                {
                    //another Campertron extracted it meanwhile (a database is moved into place only when it is complete)
                }
                catch (InvalidDataException)
                {
                    throw new IOException("The bundled campground data can't be extracted.");
                }
                if (!File.Exists(DatabaseFile))
                {
                    throw new IOException("The campground data is missing.");
                }
                _Prepared = true;
            }
        }

        // every reservable, enabled campground, for the picker
        internal List<CampgroundPickerEntry> Campgrounds()
        {
            if (_Settings.Campgrounds is Func<List<CampgroundPickerEntry>> Seam)
            {
                return Seam();
            }
            Ensure();
            return Read.CampgroundPickerEntries(_Settings.SettingsFolder);
        }

        // a campground's equipment, campsite types, campsite names and attributes, for the selectors: never a preparation of
        // its own (the picker's list, read first, prepares the data), and never a missing database
        internal CampgroundFilterChoices FilterChoices(string FacilityID)
        {
            if (_Settings.FilterChoices is Func<string, CampgroundFilterChoices> Seam)
            {
                return Seam(FacilityID);
            }
            if (!File.Exists(DatabaseFile))
            {
                throw new IOException("The campground data is missing.");
            }
            Ensure();
            return Read.FilterChoicesByCampground(FacilityID, _Settings.SettingsFolder);
        }

        // whether the database changed (a refresh by the console or the desktop GUI): the selectors read its values again
        internal string Fingerprint() => _Settings.FilterChoices != null ? "seam" : FilterChoiceSource.DatabaseFingerprint(_Settings.SettingsFolder);

        // The selectors' source for one browser session's Searches form: the request's own thread reads the values (it is
        // no UI thread, D49), and their answer is handed to the form at once, as the form's host would post it
        internal FilterChoiceSource ChoiceSource() => new FilterChoiceSource(FilterChoices, Fingerprint, Job => Job(), Job =>
        {
            Job();
            return Task.CompletedTask;
        });
    }
}
