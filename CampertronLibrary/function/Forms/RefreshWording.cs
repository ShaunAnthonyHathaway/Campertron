using System.Globalization;
using CampertronLibrary.function.RecDotOrg.api;

namespace CampertronLibrary.function.Forms
{
    // what a finished refresh of the campground data means to the user
    public enum RefreshResultKind { None, Success, Warning, Canceled, Failure }

    // The rules of the campground data's refresh as a user's action (V011-1, decision D61; business-rules-gui.md §25), for
    // every UI: asking shows a confirmation and starts nothing; the refresh starts from the confirmation only; it can be
    // canceled until the cache step, the first change a stop could not undo (the service's progress says so), and from
    // there it finishes safely; a search and a refresh never overlap (DataOperationGate). The host has the state (what
    // runs, what is shown); these say what it allows.
    internal static class RefreshRules
    {
        // Refresh campground data…: the confirmation may be shown (nothing runs or follows a refresh, it is not shown yet,
        // the folders are known and no search uses the campground data)
        internal static bool CanRequest(bool Detached, bool Running, bool FollowingUp, bool Confirming, bool PathsAvailable, bool DataFree) =>
            !Detached && !Running && !FollowingUp && !Confirming && PathsAvailable && DataFree;

        // Start refresh, in the confirmation: the refresh may start
        internal static bool CanBegin(bool Detached, bool Running, bool FollowingUp, bool PathsAvailable, bool DataFree) =>
            !Detached && !Running && !FollowingUp && PathsAvailable && DataFree;

        // Cancel: a refresh runs, was not asked to stop yet and can still stop (until the cache step)
        internal static bool CanCancel(bool Running, bool CancelRequested, bool Cancellable) => Running && !CancelRequested && Cancellable;

        // past the cache step: the refresh installs, checks and records to its end
        internal static bool IsFinishing(bool Running, bool Cancellable) => Running && !Cancellable;

        // why the refresh can't be asked for or started now, or null (also null while one runs or is followed up: the
        // card then shows that instead)
        internal static string? UnavailableReason(bool Running, bool FollowingUp, bool PathsAvailable, bool SearchRunning) =>
            Running || FollowingUp ? null
            : !PathsAvailable ? RefreshWording.NoFoldersReason
            : SearchRunning ? RefreshWording.SearchRunningReason
            : null;
    }

    // The words of the campground data's refresh (V011-1, D61), for every UI: what it is, what the confirmation says before
    // anything starts, its stages and counts, and its result by kind. Fixed text: never a path or an exception's message
    // (an unexpected problem is named by its type only). What follows a successful refresh is each UI's own (the desktop
    // GUI restarts itself, D62, and says so in its own words; the web UI can't, D84, and says what to do, D99).
    // Since v0.3.0's PR 9 (D99) also the card's own words (its heading, labels, buttons and their help), which the desktop
    // GUI's view binds and the web UI's system page shows, as SearchesWording's are (ER-37).
    public static class RefreshWording
    {
        // ---- the card's own words (its view) ----
        public const string Heading = "Campground data";
        public const string LastRefreshedLabel = "Last refreshed";
        public const string RefreshLabel = "Refresh campground data…";
        public const string RefreshName = "Refresh campground data";
        public const string RefreshTip = "Download the latest campground data from Recreation.gov (about 570 MB) and build a new campground database";
        public const string RefreshHelp = "Shows what the refresh does before it starts: it downloads about 570 MB from Recreation.gov and builds a new campground database";
        public const string ConfirmationHeading = "Refresh the campground data?";
        public const string StartLabel = "Start refresh";
        public const string StartHelp = "Start downloading the campground data from Recreation.gov now";
        public const string BackLabel = "Back";
        public const string BackName = "Back, don't refresh";
        public const string ProgressName = "Campground data refresh progress";
        public const string CancelName = "Cancel the refresh";
        public const string CancelTip = "Stop the refresh; the campground data stays as it is (available until the new data is being installed)";
        public const string CancelHelp = "Stop the refresh; the campground data stays as it is. Unavailable once the new data is being installed";

        internal const string Explanation = "Campertron's list of campgrounds and campsites comes from Recreation.gov's RIDB export. A refresh downloads the latest export "
            + "(about 570 MB) and builds a new campground database from it; it can take several minutes. Your settings and search files stay as they are.";
        // the confirmation, up to what follows a successful refresh (each UI adds that)
        internal const string ConfirmationBasics = "Campertron will download Recreation.gov's RIDB export (about 570 MB, from ridb.recreation.gov) and build a new campground "
            + "database. This can take several minutes; you can cancel until the new data is being installed.";
        internal const string SearchRunningReason = "Unavailable while a search runs in the Results tab. Refresh the campground data when the search has finished.";
        internal const string NoFoldersReason = "Campertron couldn't find its cache folder, so it can't refresh the campground data. Restart Campertron.";
        internal const string FinishingText = "Finishing safely: the new campground data is being installed. This can't be canceled now. If you close Campertron, it finishes first.";
        internal const string CancelingText = "Canceling: the refresh stops at its next safe point and removes what it downloaded. The campground data stays as it is.";
        // the refresh pauses the monitoring before it starts (D83 point 6; the web UI's words since v0.3.0's PR 9, shared since
        // v0.4.0's phase 4, when the desktop GUI's refresh pauses its monitoring too)
        internal const string PausingMonitoring = "Pausing the monitoring before the refresh starts: a check that is sending finishes its notifications first.";
        internal const string FinishedAfterCancel = "The refresh had already reached the installation when Cancel was selected, so it finished. ";

        internal static string CancelLabel(bool IsCanceling) => IsCanceling ? "Canceling…" : "Cancel";

        // the status line for a stage the refresh can still be canceled in
        internal static string Refreshing(string? StageText) => "Refreshing the campground data: " + StageText + ".";

        // the status line from the cache step on: the stage, that it finishes safely, and that Cancel came too late
        internal static string Finishing(string? StageText, bool CancelRequested) =>
            (CancelRequested ? "Too late to cancel: " + StageText : Sentence(StageText)) + ". " + FinishingText;

        // the status line after a refresh: its result and how long it took
        internal static string Finished(string Text, string? Elapsed) => Text + " Elapsed " + Elapsed + ".";

        internal static string StageName(RidbRefreshStage Stage) => Stage switch
        {
            RidbRefreshStage.AcquiringLock => "starting",
            RidbRefreshStage.CleaningUp => "removing what an earlier refresh left",
            RidbRefreshStage.Preparing => "preparing the download",
            RidbRefreshStage.Downloading => "downloading the campground data from Recreation.gov",
            RidbRefreshStage.Extracting => "extracting the downloaded files",
            RidbRefreshStage.PreparingDatabase => "preparing a new campground database",
            RidbRefreshStage.Importing => "reading the campground data",
            RidbRefreshStage.Saving => "saving the campground data",
            RidbRefreshStage.Indexing => "indexing the campsite details",
            RidbRefreshStage.Validating => "checking the new campground database",
            RidbRefreshStage.Finalizing => "finishing the new campground database",
            RidbRefreshStage.ClearingCache => "clearing the cache",
            RidbRefreshStage.Installing => "installing the new campground data",
            RidbRefreshStage.CheckingDatabase => "checking the installed campground data",
            RidbRefreshStage.RecordingRefresh => "recording the refresh in General.yaml",
            _ => "finishing",
        };

        // a stage's words at the start of a sentence
        internal static string Sentence(string? Text) => string.IsNullOrEmpty(Text) ? string.Empty : char.ToUpperInvariant(Text[0]) + Text.Substring(1);

        // the counts whose total is known (bytes while downloading, with the total when the server sent it)
        internal static string? Count(RidbRefreshProgress Snapshot)
        {
            if (Snapshot.Done is not long Done)
            {
                return null;
            }
            return Snapshot.Stage switch
            {
                RidbRefreshStage.Downloading => Snapshot.Total is long Total && Total > 0
                    ? Megabytes(Done) + " of " + Megabytes(Total) + " downloaded"
                    : Megabytes(Done) + " downloaded",
                RidbRefreshStage.Extracting => Snapshot.Total is long Entries ? Done + " of " + Entries + " files extracted" : null,
                RidbRefreshStage.Importing => Snapshot.Total is long Files ? Done + " of " + Files + " files read" : null,
                _ => null,
            };
        }

        private static string Megabytes(long Bytes) => (Bytes / 1_000_000.0).ToString("0.0", CultureInfo.CurrentCulture) + " MB";

        // Where the bar stands: a value where the total is known (the download, the extraction, the import), indeterminate
        // where nothing is counted, and null where the bar stays as it is (a count without a total to show)
        internal static (bool Indeterminate, double Value, double Maximum)? Bar(RidbRefreshProgress Snapshot)
        {
            if (Snapshot.Total is long Total && Total > 0 && Snapshot.Done is long Done
                && (Snapshot.Stage == RidbRefreshStage.Downloading || Snapshot.Stage == RidbRefreshStage.Extracting || Snapshot.Stage == RidbRefreshStage.Importing))
            {
                return (false, Done, Total);
            }
            if (Snapshot.Done == null || Snapshot.Total == null)
            {
                return (true, 0, 1);
            }
            return null;
        }

        // the counts and the elapsed time, in one line
        internal static string ProgressDetails(string? Count, string? Elapsed) =>
            string.Join(" · ", new[] { Count, Elapsed == null ? null : "Elapsed " + Elapsed }.Where(T => !string.IsNullOrEmpty(T)));

        // the result in words, by its kind: fixed text, never a path or an exception's message (an unexpected problem is
        // named by its type only)
        internal static (RefreshResultKind Kind, string Text) Describe(RidbRefreshResult Result)
        {
            const string Unchanged = " The campground data was not changed.";
            return Result.Outcome switch
            {
                RidbRefreshOutcome.Refreshed => (RefreshResultKind.Success,
                    (Result.CancelRequested ? FinishedAfterCancel : string.Empty)
                    + "The campground data was refreshed."),
                RidbRefreshOutcome.RefreshedWithoutTimestamp => (RefreshResultKind.Warning,
                    "The campground data was refreshed, but Campertron couldn't record the date in General.yaml, so it may refresh again when it starts."),
                RidbRefreshOutcome.Canceled => (RefreshResultKind.Canceled, "The refresh was canceled." + Unchanged),
                RidbRefreshOutcome.AnotherRefreshRunning => (RefreshResultKind.Failure,
                    "Another refresh of the campground data is running (in Campertron's console or another Campertron window). Try again when it has finished." + Unchanged),
                RidbRefreshOutcome.DownloadFailed => (RefreshResultKind.Failure,
                    "The download from Recreation.gov failed. Check the internet connection and try again." + Unchanged),
                RidbRefreshOutcome.DownloadTimedOut => (RefreshResultKind.Failure,
                    "The download from Recreation.gov stopped responding, so Campertron ended it. Try again later." + Unchanged),
                RidbRefreshOutcome.ExtractionFailed => (RefreshResultKind.Failure,
                    "The downloaded file is not a readable Recreation.gov export. Try again later." + Unchanged),
                RidbRefreshOutcome.ImportFailed => (RefreshResultKind.Failure,
                    "The downloaded campground data could not be imported. Try again later." + Unchanged),
                RidbRefreshOutcome.ValidationFailed => (RefreshResultKind.Failure,
                    "The downloaded campground data failed Campertron's checks, so it was not installed." + Unchanged),
                RidbRefreshOutcome.StorageFailed => (RefreshResultKind.Failure,
                    "Campertron couldn't read or write the files it needs (for example, the disk may be full or a folder may not be writable)." + Unchanged),
                RidbRefreshOutcome.InstallationFailed => (RefreshResultKind.Failure,
                    "Campertron couldn't install the new campground data (another program may have been using the database). Try again." + Unchanged),
                RidbRefreshOutcome.InstalledButCheckFailed => (RefreshResultKind.Failure,
                    "The new campground data was installed, but Campertron's check of it found a problem. Restart Campertron; if the campground list or searches fail, refresh the campground data again."),
                _ => Result.Installed
                    ? (RefreshResultKind.Failure, "The new campground data was installed, but an unexpected problem (" + (Result.ErrorType ?? "unknown") + ") stopped the refresh afterwards. Restart Campertron.")
                    : (RefreshResultKind.Failure, "The refresh stopped because of an unexpected problem (" + (Result.ErrorType ?? "unknown") + ")." + Unchanged),
            };
        }
    }
}
